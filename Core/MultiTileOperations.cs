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
            FloorRange range = EditorSelection.GetRange(editor, true);
            if (range.Count < 2)
                throw new InvalidOperationException("マルチタイル生成には、連続した実タイルを2個以上選択してください。");

            generatedGroup = ResolveNewGroup(editor, requestedGroup);
            int groupNumber = ParseGroupNumber(generatedGroup);
            string planetGroup = "P" + groupNumber.ToString(CultureInfo.InvariantCulture);
            float tileSize = ResolveTileSize();
            List<LevelEvent> created = new List<LevelEvent>();

            for (int floorNumber = range.Start; floorNumber <= range.End; floorNumber++)
            {
                scrFloor floor = editor.floors[floorNumber];
                int index = floorNumber - range.Start;
                LevelEvent decoration = new LevelEvent(0, LevelEventType.AddObject);
                SetEnabled(decoration, "objectType", ObjectDecorationType.Floor);
                SetEnumText(decoration, "relativeTo", "Global");
                SetEnabled(decoration, "position", ToLevelPosition(floor, tileSize));
                SetEnabled(decoration, "rotation", GetPathRotation(editor, floorNumber));
                SetEnabled(decoration, "trackAngle", GetRelativeAngle(floor));
                SetEnabled(decoration, "depth", index);
                SetEnabled(decoration, "tag", generatedGroup + " " + generatedGroup + "_" +
                    index.ToString(CultureInfo.InvariantCulture));
                created.Add(decoration);
            }

            if (includePlanets)
            {
                created.Add(CreatePlanet(editor.floors[range.Start], tileSize, planetGroup, 1,
                    "DefaultBlue", new Vector2(1f, 0f), GetPathRotation(editor, range.Start)));
                created.Add(CreatePlanet(editor.floors[range.Start + 1], tileSize, planetGroup, 2,
                    "DefaultRed", new Vector2(-1f, 0f), GetPathRotation(editor, range.Start + 1)));
            }

            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent decoration in created) editor.decorations.Add(decoration);
                editor.UpdateDecorationObjects();
                if (editor.propertyControlDecorationsList != null)
                    editor.propertyControlDecorationsList.RefreshItemsList(true);
            }

            return generatedGroup + "として床デコレーションを" + range.Count + "個" +
                   (includePlanets ? "、惑星を2個" : string.Empty) + "生成しました。";
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
                return group + "の" + tokens.Count + "角度を実タイルへ焼き込みました。" +
                       " タイル数が同じため、既存イベントと装飾は維持しています。 " + result;
            }

            int oldCount = range.Count;
            string replaceResult = TileTransformOperations.ReplaceSelectionWithPattern(editor, tokens);
            return group + "の" + tokens.Count + "角度を実タイルへ焼き込みました。" +
                   " 選択範囲が" + oldCount + "タイルだったため、範囲をリズム全体で置換しました。 " + replaceResult;
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
                float angle = GetFloat(decoration, "trackAngle");
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

        private static LevelEvent CreatePlanet(scrFloor floor, float tileSize, string group, int index,
            string colorType, Vector2 pivot, float rotation)
        {
            LevelEvent planet = new LevelEvent(0, LevelEventType.AddObject);
            SetEnumText(planet, "objectType", "Planet");
            SetEnumText(planet, "relativeTo", "Global");
            SetEnumText(planet, "planetColorType", colorType);
            SetEnabled(planet, "position", ToLevelPosition(floor, tileSize));
            SetEnabled(planet, "pivotOffset", pivot);
            SetEnabled(planet, "rotation", rotation);
            SetEnabled(planet, "depth", 0);
            SetEnabled(planet, "tag", group + " " + group + "_" +
                index.ToString(CultureInfo.InvariantCulture));
            return planet;
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
            Vector3 position = floor.transform.position;
            return new Vector2(position.x / tileSize, position.y / tileSize);
        }

        private static float GetPathRotation(scnEditor editor, int floorNumber)
        {
            Vector3 current = editor.floors[floorNumber].transform.position;
            Vector3 other;
            if (floorNumber > 0) other = editor.floors[floorNumber - 1].transform.position;
            else if (floorNumber + 1 < editor.floors.Count)
            {
                other = current;
                current = editor.floors[floorNumber + 1].transform.position;
            }
            else return 0f;

            Vector2 delta = new Vector2(current.x - other.x, current.y - other.y);
            if (delta.sqrMagnitude < 0.000001f) return 0f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            return angle < 0f ? angle + 360f : angle;
        }

        private static float GetRelativeAngle(scrFloor floor)
        {
            float angle = (float)(scrMisc.GetAngleMoved((double)floor.entryangle, (double)floor.exitangle,
                !floor.isCCW) * 57.29577951308232d);
            if (angle <= 0.00001f) return 360f;
            if (angle > 360f && angle < 360.001f) return 360f;
            return angle;
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
            if (!decoration.data.TryGetValue("objectType", out value) || value == null) return false;
            if (value is ObjectDecorationType) return (ObjectDecorationType)value == expected;
            ObjectDecorationType parsed;
            return Enum.TryParse(Convert.ToString(value), true, out parsed) && parsed == expected;
        }

        private static string[] Tags(LevelEvent decoration)
        {
            object value;
            if (!decoration.data.TryGetValue("tag", out value) || value == null) return new string[0];
            return Convert.ToString(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static float GetFloat(LevelEvent decoration, string key)
        {
            object value;
            if (!decoration.data.TryGetValue(key, out value) || value == null)
                throw new InvalidOperationException("床デコレーションに" + key + "がありません。");
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { throw new InvalidOperationException("床デコレーションの" + key + "が数値ではありません。"); }
        }

        private static void SetEnumText(LevelEvent decoration, string key, string value)
        {
            object current;
            if (decoration.data.TryGetValue(key, out current) && current != null && current.GetType().IsEnum)
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
            decoration.data[key] = value;
            if (decoration.disabled.ContainsKey(key)) decoration.disabled[key] = false;
        }
    }
}
