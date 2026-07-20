using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal enum FloorDuplicateMode
    {
        TilesOnly,
        TilesAndEvents,
        TilesEventsDecorations
    }

    internal static class TileTransformOperations
    {
        private static readonly LevelEventType[] ReverseUnsafeTypes =
        {
            LevelEventType.Twirl,
            LevelEventType.MultiPlanet,
            LevelEventType.Hold,
            LevelEventType.Pause,
            LevelEventType.FreeRoam,
            LevelEventType.FreeRoamTwirl,
            LevelEventType.FreeRoamRemove,
            LevelEventType.FreeRoamWarning
        };

        public static string RepeatSelection(scnEditor editor, int copies, FloorDuplicateMode mode)
        {
            if (copies < 1 || copies > 1000)
                throw new ArgumentOutOfRangeException("copies", "コピー回数は1～1000で指定してください。");

            FloorRange range = EditorSelection.GetRange(editor, false);
            using (new EditorUndoScope(editor))
            {
                editor.MultiCopyFloors(false);
                SanitizeClipboard(editor, mode);
                editor.SelectFloor(editor.floors[range.End], false);
                for (int i = 0; i < copies; i++)
                    editor.PasteFloors(mode == FloorDuplicateMode.TilesEventsDecorations);
            }

            return range.Count + "タイルの範囲を" + copies + "回複製しました（" + ModeLabel(mode) + "）。";
        }

        public static string DeleteSelection(scnEditor editor)
        {
            FloorRange range = EditorSelection.GetRange(editor, false);
            using (new EditorUndoScope(editor))
            {
                editor.DeleteMultiSelection(true);
            }
            return range.Start + "～" + range.End + "番の" + range.Count + "タイルを削除しました。";
        }

        public static string ReplaceSelectionWithPattern(scnEditor editor, IList<PatternToken> tokens)
        {
            FloorRange range = EditorSelection.GetRange(editor, false);
            if (tokens == null || tokens.Count == 0) throw new ArgumentException("角度パターンが空です。");
            int inserted = tokens.Count(x => x != null && x.Kind == PatternTokenKind.Angle);
            if (inserted == 0) throw new ArgumentException("置換パターンには角度を1個以上含めてください。");
            using (new EditorUndoScope(editor))
            {
                editor.DeleteMultiSelection(true);
                PatternOperations.InsertRelativePatternAfterSelectionWithoutUndo(editor, tokens);
            }
            return range.Start + "～" + range.End + "番の" + range.Count + "タイルを、" + inserted +
                "タイルのパターンへ置換しました。";
        }

        public static string MoveSelectionAfter(scnEditor editor, int destinationAfterFloor)
        {
            FloorRange range = EditorSelection.GetRange(editor, false);
            if (destinationAfterFloor < 0 || destinationAfterFloor >= editor.floors.Count)
                throw new ArgumentOutOfRangeException("destinationAfterFloor", "移動先の床番号が範囲外です。");
            if (destinationAfterFloor >= range.Start - 1 && destinationAfterFloor <= range.End)
                throw new InvalidOperationException("移動先を選択範囲の内部または元の位置には指定できません。");

            int count = range.Count;
            int adjustedDestination = destinationAfterFloor;
            using (new EditorUndoScope(editor))
            {
                editor.MultiCopyFloors(false);
                editor.DeleteMultiSelection(true);
                if (destinationAfterFloor > range.End) adjustedDestination -= count;
                adjustedDestination = Math.Max(0, Math.Min(adjustedDestination, editor.floors.Count - 1));
                editor.SelectFloor(editor.floors[adjustedDestination], true);
                editor.PasteFloors(true);
            }

            return range.Start + "～" + range.End + "番タイルを、" + destinationAfterFloor + "番タイルの後へ移動しました。";
        }

        public static string RotateSelection(scnEditor editor, float delta)
        {
            EnsureFloatLevel(editor);
            FloorRange range = EditorSelection.GetRange(editor, false);
            int changed = 0;
            using (new EditorUndoScope(editor))
            {
                for (int floor = range.Start; floor <= range.End; floor++)
                {
                    int index = floor - 1;
                    float value = editor.levelData.angleData[index];
                    if (IsMidspin(value)) continue;
                    editor.levelData.angleData[index] = Normalize(value + delta);
                    changed++;
                }
                editor.RemakePath(true, true);
                EditorSelection.SelectRange(editor, range.Start, range.End);
            }
            return changed + "タイルを" + delta.ToString("0.######") + "°回転しました。";
        }

        public static string ReplaceAngles(scnEditor editor, float from, float to, float tolerance)
        {
            EnsureFloatLevel(editor);
            FloorRange range = EditorSelection.GetRange(editor, false);
            tolerance = Math.Max(0f, tolerance);
            int changed = 0;
            float normalizedFrom = Normalize(from);
            float normalizedTo = Normalize(to);

            using (new EditorUndoScope(editor))
            {
                for (int floor = range.Start; floor <= range.End; floor++)
                {
                    int index = floor - 1;
                    float value = editor.levelData.angleData[index];
                    if (IsMidspin(value)) continue;
                    if (AngularDistance(Normalize(value), normalizedFrom) <= tolerance)
                    {
                        editor.levelData.angleData[index] = normalizedTo;
                        changed++;
                    }
                }
                if (changed > 0)
                {
                    editor.RemakePath(true, true);
                    EditorSelection.SelectRange(editor, range.Start, range.End);
                }
            }
            return from.ToString("0.######") + "°を" + to.ToString("0.######") + "°へ" + changed + "件置換しました。";
        }

        public static string MultiplyRelativeAngles(scnEditor editor, float multiplier)
        {
            EnsureFloatLevel(editor);
            if (multiplier <= 0f || multiplier > 100000f || float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                throw new ArgumentOutOfRangeException("multiplier", "倍率は0より大きく100000以下にしてください。");
            FloorRange range = EditorSelection.GetRange(editor, false);
            List<float> scaledRelatives = new List<float>();
            List<bool> directions = new List<bool>();

            for (int floor = range.Start; floor <= range.End; floor++)
            {
                float absolute = editor.levelData.angleData[floor - 1];
                scrFloor floorObject = editor.floors[floor];
                directions.Add(floorObject.isCCW);
                if (IsMidspin(absolute))
                {
                    scaledRelatives.Add(999f);
                    continue;
                }

                float relative = (float)(scrMisc.GetAngleMoved((double)floorObject.entryangle,
                    (double)floorObject.exitangle, !floorObject.isCCW) * 57.29577951308232d);
                if (relative <= 0.00001f) relative = 360f;
                float scaled = relative * multiplier;
                if (scaled <= 0.000001f || scaled > 360.00001f)
                    throw new InvalidOperationException(floor + "番タイルの" + relative.ToString("0.######") +
                        "°に倍率を掛けると" + scaled.ToString("0.######") +
                        "°になります。相対角度は0より大きく360以下にしてください。");
                scaledRelatives.Add(Math.Min(360f, scaled));
            }

            int changed = 0;
            using (new EditorUndoScope(editor))
            {
                float previous = PreviousAbsolute(editor, range.Start);
                for (int i = 0; i < scaledRelatives.Count; i++)
                {
                    int index = range.Start - 1 + i;
                    float relative = scaledRelatives[i];
                    if (IsMidspin(relative))
                    {
                        editor.levelData.angleData[index] = 999f;
                        continue;
                    }

                    float absolute = RelativeToAbsolute(previous, relative, directions[i]);
                    if (AngularDistance(editor.levelData.angleData[index], absolute) > 0.000001f) changed++;
                    editor.levelData.angleData[index] = absolute;
                    previous = absolute;
                }
                if (changed > 0)
                {
                    editor.RemakePath(true, true);
                    EditorSelection.SelectRange(editor, range.Start, range.End);
                }
            }
            return changed + "タイルの相対角度を" + multiplier.ToString("0.######") + "倍しました。";
        }

        public static string SnapAngles(scnEditor editor, float increment)
        {
            EnsureFloatLevel(editor);
            if (increment <= 0f || increment > 360f)
                throw new ArgumentOutOfRangeException("increment", "刻み角度は0より大きく360以下にしてください。");
            FloorRange range = EditorSelection.GetRange(editor, false);
            int changed = 0;

            using (new EditorUndoScope(editor))
            {
                for (int floor = range.Start; floor <= range.End; floor++)
                {
                    int index = floor - 1;
                    float value = editor.levelData.angleData[index];
                    if (IsMidspin(value)) continue;
                    float snapped = Normalize((float)Math.Round(value / increment) * increment);
                    if (AngularDistance(value, snapped) > 0.000001f)
                    {
                        editor.levelData.angleData[index] = snapped;
                        changed++;
                    }
                }
                if (changed > 0)
                {
                    editor.RemakePath(true, true);
                    EditorSelection.SelectRange(editor, range.Start, range.End);
                }
            }
            return changed + "タイルを" + increment.ToString("0.######") + "°刻みに丸めました。";
        }

        public static string ReverseRelativeAngles(scnEditor editor)
        {
            EnsureFloatLevel(editor);
            FloorRange range = EditorSelection.GetRange(editor, false);
            LevelEvent unsafeEvent = editor.events.FirstOrDefault(x => x.floor >= range.Start && x.floor <= range.End &&
                ReverseUnsafeTypes.Contains(x.eventType));
            if (unsafeEvent != null)
                throw new InvalidOperationException("範囲内に" + JapaneseLocalization.EventLabel(unsafeEvent.eventType.ToString()) +
                    "があるため、相対角度の逆順化を中止しました。");

            bool isCcw = range.Start > 0 && editor.floors[range.Start - 1].isCCW;
            float previous = PreviousAbsolute(editor, range.Start);
            List<float> relatives = new List<float>();
            for (int floor = range.Start; floor <= range.End; floor++)
            {
                float absolute = editor.levelData.angleData[floor - 1];
                if (IsMidspin(absolute))
                {
                    relatives.Add(999f);
                    continue;
                }
                scrFloor floorObject = editor.floors[floor];
                float relative = (float)(scrMisc.GetAngleMoved((double)floorObject.entryangle,
                    (double)floorObject.exitangle, !floorObject.isCCW) * 57.29577951308232d);
                if (relative <= 0.00001f) relative = 360f;
                relatives.Add(relative);
            }
            relatives.Reverse();

            using (new EditorUndoScope(editor))
            {
                previous = PreviousAbsolute(editor, range.Start);
                for (int i = 0; i < relatives.Count; i++)
                {
                    float relative = relatives[i];
                    int index = range.Start - 1 + i;
                    if (IsMidspin(relative))
                    {
                        editor.levelData.angleData[index] = 999f;
                        continue;
                    }
                    float absolute = RelativeToAbsolute(previous, relative, isCcw);
                    editor.levelData.angleData[index] = absolute;
                    previous = absolute;
                }
                editor.RemakePath(true, true);
                EditorSelection.SelectRange(editor, range.Start, range.End);
            }
            return range.Count + "タイルの相対角度列を逆順にしました。";
        }

        private static void SanitizeClipboard(scnEditor editor, FloorDuplicateMode mode)
        {
            if (mode == FloorDuplicateMode.TilesEventsDecorations) return;
            for (int i = 0; i < editor.clipboard.Count; i++)
            {
                if (!(editor.clipboard[i] is scnEditor.FloorData)) continue;
                scnEditor.FloorData data = (scnEditor.FloorData)editor.clipboard[i];
                List<LevelEvent> events = mode == FloorDuplicateMode.TilesOnly
                    ? new List<LevelEvent>()
                    : new List<LevelEvent>(data.levelEventData);
                editor.clipboard[i] = new scnEditor.FloorData(data.stringDirection, data.floatDirection,
                    events, new List<LevelEvent>());
            }
        }

        private static string ModeLabel(FloorDuplicateMode mode)
        {
            if (mode == FloorDuplicateMode.TilesOnly) return "タイルのみ";
            if (mode == FloorDuplicateMode.TilesAndEvents) return "タイル＋イベント";
            return "タイル＋イベント＋装飾";
        }

        private static void EnsureFloatLevel(scnEditor editor)
        {
            if (editor == null || editor.levelData == null) throw new InvalidOperationException("譜面が読み込まれていません。");
            if (editor.levelData.angleData == null) throw new InvalidOperationException("この譜面にはangleDataがないため、角度操作を使用できません。");
        }

        private static float PreviousAbsolute(scnEditor editor, int startFloor)
        {
            int index = startFloor - 2;
            while (index >= 0 && IsMidspin(editor.levelData.angleData[index])) index--;
            return index < 0 ? 0f : editor.levelData.angleData[index];
        }

        private static float RelativeToAbsolute(float previous, float relative, bool isCcw)
        {
            float delta = 180f - relative;
            return Normalize(previous + (isCcw ? -delta : delta));
        }

        private static float Normalize(float value)
        {
            value %= 360f;
            if (value < 0f) value += 360f;
            if (Math.Abs(value - 360f) < 0.000001f) value = 0f;
            return value;
        }

        private static float AngularDistance(float a, float b)
        {
            float distance = Math.Abs(Normalize(a) - Normalize(b));
            return Math.Min(distance, 360f - distance);
        }

        private static bool IsMidspin(float value)
        {
            return Math.Abs(value - 999f) < 0.0001f;
        }
    }
}
