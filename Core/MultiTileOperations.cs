using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class MultiTileOperations
    {
        private static readonly Regex GroupTagPattern = new Regex(@"^T(\d+)$", RegexOptions.Compiled);
        private static readonly Regex PlanetGroupTagPattern = new Regex(@"^P(\d+)$", RegexOptions.Compiled);

        private sealed class RhythmEntry
        {
            internal int Index;
            internal float Angle;
            internal int DecorationOrder;
        }

        public static string GenerateFromSelectedTiles(scnEditor editor, string requestedGroup,
            bool includePlanets, out string generatedGroup)
        {
            // Floor 0 includes the countdown in its entry-time interval. Starting a
            // native planet conversion there would make the first orbit wait through
            // the countdown.
            FloorRange range = EditorSelection.GetRange(editor, false);
            if (range.Count < 2)
                throw new InvalidOperationException("マルチタイル生成には、連続した実タイルを2個以上選択してください。");

            generatedGroup = ResolveNewGroup(editor, requestedGroup);
            int groupNumber = ParseGroupNumber(generatedGroup);
            string planetGroup = "P" + groupNumber.ToString(CultureInfo.InvariantCulture);
            float tileSize = ResolveTileSize();
            List<LevelEvent> created = new List<LevelEvent>();
            List<LevelEvent> createdEvents = new List<LevelEvent>();

            for (int floorNumber = range.Start; floorNumber <= range.End; floorNumber++)
            {
                scrFloor floor = editor.floors[floorNumber];
                int index = floorNumber - range.Start;
                // Keep the static floor-object snapshot on one anchor floor. Timed
                // MoveDecorations are distributed separately to avoid tween conflicts.
                LevelEvent decoration = new LevelEvent(range.Start, LevelEventType.AddObject);
                SetEnabled(decoration, "objectType", ObjectDecorationType.Floor);
                SetEnumText(decoration, "relativeTo", "Global");
                SetEnabled(decoration, "position", ToLevelPosition(floor, tileSize));
                float visualAngle = GetVisualTrackAngle(floor);
                float rhythmAngle = GetRhythmAngle(floor);
                float rotation = GetTrackRotation(floor, visualAngle);
                SetEnabled(decoration, "rotation", rotation);
                SetEnumText(decoration, "trackType", floor.midSpin ? "Midspin" : "Normal");
                SetEnabled(decoration, "trackAngle", visualAngle);
                Vector3 localScale = floor.transform.localScale;
                SetEnabled(decoration, "scale", new Vector2(localScale.x * 100f, localScale.y * 100f));
                SetEnabled(decoration, "depth", index);
                SetEnabled(decoration, "tag", generatedGroup + " " + generatedGroup + "_" +
                    index.ToString(CultureInfo.InvariantCulture) + " " +
                    DecorationMarkerTags.ManagedMultiTilePrefix + generatedGroup + " " +
                    EncodeRhythmAngle(rhythmAngle));
                ApplyTrackIcon(editor, floorNumber, floor, decoration, rotation);
                created.Add(decoration);
            }

            if (includePlanets)
            {
                CreatePlanetAnimation(editor, range, tileSize, planetGroup,
                    created, createdEvents);
            }

            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent decoration in created) editor.decorations.Add(decoration);
                foreach (LevelEvent levelEvent in createdEvents) editor.events.Add(levelEvent);
                // Rebuild each floor's ffxPlusBase list immediately. Without this, the
                // editor can keep executing the MoveDecorations set from before generation
                // until the chart is reloaded.
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (editor.propertyControlDecorationsList != null)
                    editor.propertyControlDecorationsList.RefreshItemsList(true);
            }

            return generatedGroup + "として床デコレーションを" + range.Count + "個" +
                   (includePlanets ? "、惑星を2個、ネイティブ公転イベントを" +
                    createdEvents.Count.ToString(CultureInfo.InvariantCulture) + "件" : string.Empty) +
                   "生成しました。" +
                   (includePlanets
                       ? " 床デコレーションと惑星は先頭タイル、公転イベントは元タイルへ配置しました。"
                       : " 床デコレーションは先頭タイルにまとめました。");
        }

        public static string BakeToSelectedTiles(scnEditor editor, string requestedGroup)
        {
            FloorRange range = EditorSelection.GetRange(editor, false);
            string group = ResolveExistingGroup(editor, requestedGroup);
            List<RhythmEntry> rhythm = ReadRhythm(editor, group);
            if (rhythm.Count == 0)
                throw new InvalidOperationException(group + "に焼き込める床デコレーションがありません。");
            if (rhythm.Count > 100000)
                throw new InvalidOperationException("マルチタイルのリズムが大きすぎます。");

            List<PatternToken> tokens = rhythm
                .Select(x => PatternToken.FromAngle(NormalizeBakeAngle(x.Angle)))
                .ToList();

            if (range.Count == tokens.Count)
            {
                string result = TileTransformOperations.OverwriteRelativeAngles(editor, tokens);
                string cleanup = RemovePlanetPreview(editor, group);
                return group + "の" + tokens.Count + "角度を実タイルへ焼き込みました。" +
                       " 床デコレーションは維持し、公転イベントとプレビュー惑星を削除しました。 " +
                       result + " " + cleanup;
            }

            int oldCount = range.Count;
            string replaceResult = TileTransformOperations.ReplaceSelectionWithPattern(editor, tokens);
            string replaceCleanup = RemovePlanetPreview(editor, group);
            return group + "の" + tokens.Count + "角度を実タイルへ焼き込みました。" +
                   " 選択範囲が" + oldCount + "タイルだったため、範囲をリズム全体で置換しました。 " +
                   replaceResult + " " + replaceCleanup;
        }

        private static List<RhythmEntry> ReadRhythm(scnEditor editor, string group)
        {
            Regex itemPattern = new Regex("^" + Regex.Escape(group) + @"_(\d+)$");
            List<RhythmEntry> entries = new List<RhythmEntry>();
            List<RhythmEntry> unnumbered = new List<RhythmEntry>();

            for (int order = 0; order < editor.decorations.Count; order++)
            {
                LevelEvent decoration = editor.decorations[order];
                if (decoration == null || decoration.eventType != LevelEventType.AddObject ||
                    !IsObjectType(decoration, ObjectDecorationType.Floor)) continue;

                string[] tags = Tags(decoration);
                if (!tags.Contains(group)) continue;
                string itemTag = tags.FirstOrDefault(x => itemPattern.IsMatch(x));
                float angle = ReadManagedRhythmAngle(decoration,
                    GetFloat(decoration, "trackAngle"));
                if (float.IsNaN(angle) || float.IsInfinity(angle) || angle < 0f || angle > 360f)
                    throw new InvalidOperationException((itemTag ?? group) + "のtrackAngleが0～360の範囲外です。");

                // Older charts sometimes give every tile only the group tag (Tn), without
                // Tn_i. Use decoration order only when no numbered tiles exist at all.
                if (itemTag == null)
                {
                    unnumbered.Add(new RhythmEntry
                    {
                        Index = unnumbered.Count,
                        Angle = angle,
                        DecorationOrder = order
                    });
                    continue;
                }

                Match match = itemPattern.Match(itemTag);
                entries.Add(new RhythmEntry
                {
                    Index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    Angle = angle,
                    DecorationOrder = order
                });
            }

            if (entries.Count == 0) return unnumbered;

            // Some charts layer several visuals under the same Tn_i tag. Equal angles are
            // one rhythm tile; differing angles are kept in decoration order so intentional
            // closing tiles such as Fallenera's final loop are not lost.
            List<IGrouping<int, RhythmEntry>> groupedEntries = entries
                .GroupBy(x => x.Index)
                .OrderBy(x => x.Key)
                .ToList();
            for (int i = 1; i < groupedEntries.Count; i++)
            {
                if (groupedEntries[i].Key != groupedEntries[i - 1].Key + 1)
                    throw new InvalidOperationException(group + "の連番に欠けがあります（" +
                        groupedEntries[i - 1].Key + "の次が" + groupedEntries[i].Key + "）。");
            }

            return groupedEntries
                .SelectMany(grouped => grouped
                    .OrderBy(x => x.DecorationOrder)
                    .GroupBy(x => AngleKey(x.Angle))
                    .Select(x => x.First()))
                .ToList();
        }

        private static void CreatePlanetAnimation(scnEditor editor, FloorRange range, float tileSize,
            string planetGroup, ICollection<LevelEvent> decorations,
            ICollection<LevelEvent> events)
        {
            string blueTag = planetGroup + "_BluePlanet";
            string redTag = planetGroup + "_RedPlanet";

            // Start from ADOFAI's own planets, then immediately hand control to ordinary
            // MoveDecorations events. This is the same approach used by the original
            // "adofai to decoration" project and deliberately avoids a custom per-frame
            // planet mirror.
            decorations.Add(CreatePlanet(range.Start, planetGroup, 1, blueTag,
                "DefaultBlue", "BluePlanet"));
            decorations.Add(CreatePlanet(range.Start, planetGroup, 2, redTag,
                "DefaultRed", "RedPlanet"));

            for (int floorNumber = range.Start; floorNumber < range.End; floorNumber++)
            {
                scrFloor floor = editor.floors[floorNumber];
                Vector2 center = ToLevelPosition(floor, tileSize);
                float baseRotation = ResolvePlanetBaseRotation(floor);
                double motionSeconds = ResolveMotionSeconds(editor, floorNumber);
                float orbitDegrees = ResolveOrbitDegrees(editor, floorNumber, motionSeconds);
                float targetRotation = baseRotation +
                    (floor.isCCW ? orbitDegrees : -orbitDegrees);
                float localBpm = Math.Max(0.0001f, EffectiveBpm(editor, floorNumber));
                float duration = (float)Math.Max(0d, motionSeconds * localBpm / 60d);
                bool evenStep = (floorNumber - range.Start + 1) % 2 == 0;

                // Keep each native tween on the tile where it actually starts. Several
                // delayed MoveDecorations attached to one floor all target the same
                // decoration tween slots and can complete/kill each other while the
                // editor scrubs or restarts playback.
                events.Add(CreatePlanetMove(floorNumber, redTag, 0f, 0f, center,
                    new Vector2(evenStep ? 0f : 1f, float.NaN), baseRotation));
                events.Add(CreatePlanetMove(floorNumber, blueTag, 0f, 0f, center,
                    new Vector2(evenStep ? 1f : 0f, float.NaN), baseRotation));
                events.Add(CreatePlanetMove(floorNumber, redTag, duration, 0f,
                    center, new Vector2(float.NaN, float.NaN), targetRotation));
                events.Add(CreatePlanetMove(floorNumber, blueTag, duration, 0f,
                    center, new Vector2(float.NaN, float.NaN), targetRotation));
            }
        }

        private static LevelEvent CreatePlanet(int floor, string group, int index,
            string colorTag, string colorType, string relativeTo)
        {
            LevelEvent planet = new LevelEvent(floor, LevelEventType.AddObject);
            SetEnumText(planet, "objectType", "Planet");
            SetEnumText(planet, "relativeTo", relativeTo);
            SetEnumText(planet, "planetColorType", colorType);
            SetEnabled(planet, "position", Vector2.zero);
            SetEnabled(planet, "pivotOffset", Vector2.zero);
            SetEnabled(planet, "rotation", 0f);
            SetEnabled(planet, "depth", -1);
            SetEnabled(planet, "tag", group + " " + group + "_" +
                index.ToString(CultureInfo.InvariantCulture) + " " + colorTag);
            return planet;
        }

        private static LevelEvent CreatePlanetMove(int floor, string tag, float duration,
            float angleOffset, Vector2 position, Vector2 pivot, float rotation)
        {
            LevelEvent move = new LevelEvent(floor, LevelEventType.MoveDecorations);
            SetEnabled(move, "duration", Mathf.Max(0f, duration));
            SetEnabled(move, "tag", tag);
            SetEnumText(move, "relativeTo", "Global");
            SetEnabled(move, "positionOffset", position);
            SetEnabled(move, "pivotOffset", pivot);
            SetEnabled(move, "rotationOffset", rotation);
            SetEnabled(move, "parallaxOffset", new Vector2(float.NaN, float.NaN));
            SetEnabled(move, "angleOffset", angleOffset);
            SetEnumText(move, "ease", "Linear");
            return move;
        }

        private static float ResolvePlanetBaseRotation(scrFloor floor)
        {
            if (!floor.midSpin && floor.floatDirection > -900f && floor.floatDirection < 900f)
                return floor.floatDirection - 180f;

            // Midspins have floatDirection=999. They consume no orbit time, but using 999
            // as a decoration rotation would still leave a visible jump while scrubbing.
            return NormalizeSignedDegrees(
                90f - (float)(floor.entryangle * Mathf.Rad2Deg) - 180f);
        }

        private static double ResolveMotionSeconds(scnEditor editor, int floorNumber)
        {
            if (floorNumber >= 0 && floorNumber + 1 < editor.floors.Count)
            {
                scrFloor floor = editor.floors[floorNumber];
                scrFloor next = editor.floors[floorNumber + 1];
                double seconds = next.entryTime - floor.entryTime;
                if (seconds >= 0d && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                    return seconds;
            }

            scrFloor fallback = editor.floors[floorNumber];
            float beats = (float)(Math.Abs(fallback.angleLength) / Math.PI) +
                          Mathf.Max(0f, fallback.extraBeats);
            return Mathf.Max(0f, beats) * 60d /
                   Math.Max(0.0001f, EffectiveBpm(editor, floorNumber));
        }

        private static float ResolveOrbitDegrees(scnEditor editor, int floorNumber)
        {
            return ResolveOrbitDegrees(editor, floorNumber,
                ResolveMotionSeconds(editor, floorNumber));
        }

        private static float ResolveOrbitDegrees(scnEditor editor, int floorNumber,
            double motionSeconds)
        {
            float degrees = (float)(Math.Max(0d, motionSeconds) *
                Math.Max(0.0001f, EffectiveBpm(editor, floorNumber)) * 3d);
            return float.IsNaN(degrees) || float.IsInfinity(degrees)
                ? 0f
                : Mathf.Max(0f, degrees);
        }

        private static string RemovePlanetPreview(scnEditor editor, string tileGroup)
        {
            int groupNumber = ParseGroupNumber(tileGroup);
            string planetGroup = "P" + groupNumber.ToString(CultureInfo.InvariantCulture);
            List<LevelEvent> removeDecorations = editor.decorations.Where(x =>
                x != null && x.eventType == LevelEventType.AddObject &&
                IsObjectTypeName(x, "Planet") && Tags(x).Contains(planetGroup)).ToList();
            List<LevelEvent> removeEvents = new List<LevelEvent>();
            foreach (LevelEvent evnt in editor.events.Where(x => x != null))
            {
                // Both current native previews and v0.14.x previews use ordinary
                // MoveDecorations targeted at Pn_BluePlanet/Pn_RedPlanet.
                if (evnt.eventType == LevelEventType.MoveDecorations &&
                    Tags(evnt).Any(x => x == planetGroup ||
                        x.StartsWith(planetGroup + "_", StringComparison.Ordinal)))
                    removeEvents.Add(evnt);
            }

            if (removeDecorations.Count == 0 && removeEvents.Count == 0)
                return "削除対象の惑星プレビューはありませんでした。";

            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent decoration in removeDecorations)
                    editor.decorations.Remove(decoration);
                foreach (LevelEvent evnt in removeEvents)
                    editor.events.Remove(evnt);
                // Removing LevelEvent objects does not remove the ffx components already
                // attached to floors. Rebuild them before refreshing the decorations.
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (editor.propertyControlDecorationsList != null)
                    editor.propertyControlDecorationsList.RefreshItemsList(true);
            }
            return "惑星" + removeDecorations.Count + "個と公転イベント" +
                   removeEvents.Count + "件を削除しました。";
        }

        private static string ResolveNewGroup(scnEditor editor, string requested)
        {
            HashSet<int> used = UsedGroupNumbers(editor);
            string text = (requested ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                int number = 0;
                while (used.Contains(number)) number++;
                return "T" + number.ToString(CultureInfo.InvariantCulture);
            }

            string normalized = NormalizeGroup(text);
            int requestedNumber = ParseGroupNumber(normalized);
            if (used.Contains(requestedNumber))
                throw new InvalidOperationException(normalized + "または対応するP" + requestedNumber +
                    "は既に使われています。空欄にすると未使用番号を自動選択します。");
            return normalized;
        }

        private static string ResolveExistingGroup(scnEditor editor, string requested)
        {
            string text = (requested ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(text)) return NormalizeGroup(text);

            if (editor.selectedDecorations != null)
            {
                foreach (LevelEvent decoration in editor.selectedDecorations.Where(x => x != null))
                {
                    string found = Tags(decoration).FirstOrDefault(x => GroupTagPattern.IsMatch(x));
                    if (found != null) return found;
                }
            }
            throw new InvalidOperationException("焼き込むグループ名（例: T0）を入力してください。");
        }

        private static HashSet<int> UsedGroupNumbers(scnEditor editor)
        {
            HashSet<int> result = new HashSet<int>();
            if (editor == null || editor.decorations == null) return result;
            foreach (LevelEvent decoration in editor.decorations.Where(x => x != null))
            {
                foreach (string tag in Tags(decoration))
                {
                    Match match = GroupTagPattern.Match(tag);
                    if (!match.Success) match = PlanetGroupTagPattern.Match(tag);
                    int number;
                    if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None,
                            CultureInfo.InvariantCulture, out number)) result.Add(number);
                }
            }
            return result;
        }

        private static string NormalizeGroup(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (Regex.IsMatch(text, @"^\d+$")) text = "T" + text;
            Match match = GroupTagPattern.Match(text);
            if (!match.Success)
                throw new FormatException("グループ名はTに0以上の整数を続けて指定してください（例: T0）。");
            int number;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
                throw new FormatException("グループ番号が大きすぎます。");
            return "T" + number.ToString(CultureInfo.InvariantCulture);
        }

        private static int ParseGroupNumber(string group)
        {
            Match match = GroupTagPattern.Match(group);
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static float ResolveTileSize()
        {
            float result = ADOBase.controller == null ? 1f : ADOBase.controller.tileSize;
            return Mathf.Abs(result) < 0.000001f ? 1f : result;
        }

        private static Vector2 ToLevelPosition(scrFloor floor, float tileSize)
        {
            // Snapshot the editor-visible position once. This intentionally includes
            // justThisTile PositionTrack, but MultiTileOperations must not call
            // ApplyEventsToFloors again after the snapshot or those offsets can be applied
            // to the already-positioned editor floors a second time.
            Vector3 position = floor.transform.position;
            return new Vector2(position.x / tileSize, position.y / tileSize);
        }

        private static float GetTrackRotation(scrFloor floor, float visualAngle)
        {
            // AddObject's Floor rotation is based on the outer edge of the smaller arc,
            // not directly on isCCW. This is the same endpoint selection used by existing
            // multiple-track generators.
            float entryDirection = NormalizeDegrees(
                90f - (float)(floor.entryangle * Mathf.Rad2Deg));
            float exitDirection = NormalizeDegrees(
                90f - (float)(floor.exitangle * Mathf.Rad2Deg));
            float rotation;
            if (entryDirection > exitDirection)
            {
                rotation = entryDirection - exitDirection > 180f
                    ? entryDirection
                    : exitDirection;
            }
            else
            {
                rotation = exitDirection - entryDirection > 180f
                    ? exitDirection
                    : entryDirection;
            }
            return NormalizeSignedDegrees(rotation + visualAngle + 180f);
        }

        private static float GetVisualTrackAngle(scrFloor floor)
        {
            float entryDirection = NormalizeDegrees(
                90f - (float)(floor.entryangle * Mathf.Rad2Deg));
            float exitDirection = NormalizeDegrees(
                90f - (float)(floor.exitangle * Mathf.Rad2Deg));
            float difference = Mathf.Abs(exitDirection - entryDirection);
            return difference > 180f ? 360f - difference : difference;
        }

        private static float GetRhythmAngle(scrFloor floor)
        {
            double moved = scrMisc.GetAngleMoved(floor.entryangle, floor.exitangle,
                !floor.isCCW);
            if (Math.Abs(moved) <= 0.000001d) return 360f;
            return Mathf.Rad2Deg * (float)moved;
        }

        private static float NormalizeDegrees(float value)
        {
            float normalized = Mathf.Repeat(value, 360f);
            return Mathf.Abs(normalized) < 0.000001f ? 0f : normalized;
        }

        private static float NormalizeSignedDegrees(float value)
        {
            float normalized = Mathf.Repeat(value + 180f, 360f) - 180f;
            return Mathf.Abs(normalized) < 0.000001f ? 0f : normalized;
        }

        private static string EncodeRhythmAngle(float angle)
        {
            int encoded = Mathf.RoundToInt(Mathf.Clamp(angle, 0f, 360f) * 10000f);
            return DecorationMarkerTags.ManagedMultiTileRhythmPrefix +
                   encoded.ToString(CultureInfo.InvariantCulture);
        }

        internal static float ReadManagedRhythmAngle(LevelEvent decoration, float fallback)
        {
            string marker = DecorationMarkerTags.FindTag(decoration,
                DecorationMarkerTags.ManagedMultiTileRhythmPrefix);
            if (marker == null) return fallback;
            string encodedText = marker.Substring(
                DecorationMarkerTags.ManagedMultiTileRhythmPrefix.Length);
            int encoded;
            if (!int.TryParse(encodedText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out encoded) ||
                encoded < 0 || encoded > 3600000) return fallback;
            return encoded / 10000f;
        }

        private static void ApplyTrackIcon(scnEditor editor, int floorNumber, scrFloor floor,
            LevelEvent decoration, float rotation)
        {
            string icon = "None";
            float iconAngle = rotation;
            bool iconFlipped = false;
            LevelEvent speedEvent = editor.events.LastOrDefault(x => x != null &&
                x.floor == floorNumber && x.eventType == LevelEventType.SetSpeed);
            LevelEvent twirlEvent = editor.events.LastOrDefault(x => x != null &&
                x.floor == floorNumber && x.eventType == LevelEventType.Twirl);
            LevelEvent checkpointEvent = editor.events.LastOrDefault(x => x != null &&
                x.floor == floorNumber && x.eventType == LevelEventType.Checkpoint);
            LevelEvent multiPlanetEvent = editor.events.LastOrDefault(x => x != null &&
                x.floor == floorNumber && x.eventType == LevelEventType.MultiPlanet);

            // Native floors give a meaningful speed change priority over Twirl. Event-list
            // order must not decide which icon survives when a tile owns both events.
            float speedRatio = speedEvent == null
                ? 1f
                : GetSpeedRatio(editor, floorNumber, speedEvent);
            if (speedEvent != null && speedRatio >= 1.9999f)
            {
                icon = "DoubleRabbit";
            }
            else if (speedEvent != null && speedRatio <= 0.2501f)
            {
                icon = "DoubleSnail";
            }
            else if (speedEvent != null && speedRatio >= 1.0499f)
            {
                icon = "Rabbit";
            }
            else if (speedEvent != null && speedRatio <= 0.9501f)
            {
                icon = "Snail";
            }
            else if (twirlEvent != null)
            {
                icon = "Swirl";
                float localOrbit = ResolveOrbitDegrees(editor, floorNumber);
                iconFlipped = floor.isCCW;
                iconAngle = iconFlipped
                    ? 180f - (180f - localOrbit) / 2f
                    : (180f - localOrbit) / 2f;
                SetEnabled(decoration, "trackRedSwirl", localOrbit < 180f);
            }
            else if (checkpointEvent != null)
            {
                icon = "Checkpoint";
            }
            else if (multiPlanetEvent != null)
            {
                object planets;
                string value = multiPlanetEvent.GetEventData().TryGetValue("planets", out planets)
                    ? Convert.ToString(planets)
                    : string.Empty;
                icon = string.Equals(value, "TwoPlanets", StringComparison.OrdinalIgnoreCase)
                    ? "MultiPlanetTwo"
                    : "MultiPlanetThreeMore";
            }
            else
            {
                return;
            }

            SetEnumText(decoration, "trackIcon", icon);
            SetEnabled(decoration, "trackIconAngle", iconAngle);
            SetEnabled(decoration, "trackIconFlipped", iconFlipped);
            if (icon == "DoubleRabbit" || icon == "DoubleSnail" ||
                icon == "Rabbit" || icon == "Snail")
            {
                SetEnabled(decoration, "trackGraySetSpeedIcon", false);
                SetEnabled(decoration, "trackSetSpeedIconBpm",
                    EffectiveBpm(editor, floorNumber));
            }
        }

        private static float GetSpeedRatio(scnEditor editor, int floorNumber, LevelEvent speedEvent)
        {
            object speedType;
            string type = speedEvent.GetEventData().TryGetValue("speedType", out speedType)
                ? Convert.ToString(speedType)
                : string.Empty;
            if (string.Equals(type, "Multiplier", StringComparison.OrdinalIgnoreCase))
            {
                object multiplier;
                if (speedEvent.GetEventData().TryGetValue("bpmMultiplier", out multiplier) && multiplier != null)
                {
                    try { return Convert.ToSingle(multiplier, CultureInfo.InvariantCulture); }
                    catch { return 1f; }
                }
            }

            float previous = floorNumber <= 0
                ? Math.Max(0.0001f, editor.levelData.bpm)
                : Math.Max(0.0001f, EffectiveBpm(editor, floorNumber - 1));
            return EffectiveBpm(editor, floorNumber) / previous;
        }

        private static float EffectiveBpm(scnEditor editor, int floorNumber)
        {
            float baseBpm = editor.levelData == null ? 100f : editor.levelData.bpm;
            if (editor.floors == null || editor.floors.Count == 0) return baseBpm;
            int index = Mathf.Clamp(floorNumber, 0, editor.floors.Count - 1);
            return baseBpm * editor.floors[index].speed;
        }

        private static double NormalizeBakeAngle(float value)
        {
            // AddObject allows 0 for a retracing tile. angleData represents the same geometry
            // as 360, while the normal pattern input intentionally rejects 0.
            return value <= 0.00001f ? 360d : value;
        }

        private static int AngleKey(float value)
        {
            float normalized = value <= 0.00001f ? 360f : value;
            return Mathf.RoundToInt(normalized * 10000f);
        }

        private static bool IsObjectType(LevelEvent decoration, ObjectDecorationType expected)
        {
            object value;
            if (!decoration.GetEventData().TryGetValue("objectType", out value) || value == null) return false;
            if (value is ObjectDecorationType) return (ObjectDecorationType)value == expected;
            ObjectDecorationType parsed;
            return Enum.TryParse(Convert.ToString(value), true, out parsed) && parsed == expected;
        }

        private static bool IsObjectTypeName(LevelEvent decoration, string expected)
        {
            object value;
            if (!decoration.GetEventData().TryGetValue("objectType", out value) || value == null) return false;
            return string.Equals(Convert.ToString(value), expected,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string[] Tags(LevelEvent decoration)
        {
            object value;
            if (!decoration.GetEventData().TryGetValue("tag", out value) || value == null) return new string[0];
            return Convert.ToString(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static float GetFloat(LevelEvent decoration, string key)
        {
            object value;
            if (!decoration.GetEventData().TryGetValue(key, out value) || value == null)
                throw new InvalidOperationException("床デコレーションに" + key + "がありません。");
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { throw new InvalidOperationException("床デコレーションの" + key + "が数値ではありません。"); }
        }

        private static void SetEnumText(LevelEvent decoration, string key, string value)
        {
            object current;
            if (decoration.GetEventData().TryGetValue(key, out current) && current != null && current.GetType().IsEnum)
            {
                object parsed;
                try { parsed = Enum.Parse(current.GetType(), value, true); }
                catch { throw new InvalidOperationException(key + "の値「" + value + "」を利用できません。"); }
                SetEnabled(decoration, key, parsed);
                return;
            }
            SetEnabled(decoration, key, value);
        }

        private static void SetEnabled(LevelEvent decoration, string key, object value)
        {
            decoration.GetEventData()[key] = value;
            if (decoration.disabled.ContainsKey(key)) decoration.disabled[key] = false;
        }

    }
}
