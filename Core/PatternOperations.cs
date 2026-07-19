using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class PatternOperations
    {
        public static string RepeatSelection(scnEditor editor, int copies, bool includeDecorations)
        {
            if (copies < 1 || copies > 1000)
                throw new ArgumentOutOfRangeException("copies", "コピー回数は1～1000で指定してください。");
            FloorRange range = EditorSelection.GetRange(editor, false);
            using (new EditorUndoScope(editor))
            {
                editor.MultiCopyFloors(false);
                editor.SelectFloor(editor.floors[range.End], false);
                for (int i = 0; i < copies; i++) editor.PasteFloors(includeDecorations);
            }
            return range.Count + "タイルの範囲を" + copies + "回複製しました。";
        }

        public static string InsertRelativePatternAfterSelection(scnEditor editor, IList<PatternToken> tokens)
        {
            ValidateTokens(tokens);
            using (new EditorUndoScope(editor))
            {
                return InsertRelativePatternAfterSelectionWithoutUndo(editor, tokens);
            }
        }

        internal static string InsertRelativePatternAfterSelectionWithoutUndo(scnEditor editor, IList<PatternToken> tokens)
        {
            ValidateTokens(tokens);
            FloorRange range = EditorSelection.GetRange(editor, true);
            int insertedTileCount = tokens.Count(x => x != null && x.Kind == PatternTokenKind.Angle);
            int afterFloor = range.End;
            int currentFloor = afterFloor;
            int angleInsertIndex = afterFloor;
            int twirlOperations = 0;

            if (insertedTileCount > 0)
                MethodInfoCache.OffsetFloorIds.Invoke(editor, new object[] { afterFloor, insertedTileCount });

            float previous = GetPreviousAbsolute(editor, afterFloor + 1);
            bool isCcw = GetCcwBefore(editor, afterFloor + 1);

            foreach (PatternToken token in tokens)
            {
                if (token == null) continue;
                if (token.Kind == PatternTokenKind.Twirl)
                {
                    ToggleTwirl(editor, currentFloor);
                    isCcw = !isCcw;
                    twirlOperations++;
                    continue;
                }

                float absolute = RelativeToAbsolute(previous, token.Angle, isCcw);
                editor.levelData.angleData.Insert(angleInsertIndex, absolute);
                angleInsertIndex++;
                currentFloor++;
                if (absolute != 999f) previous = absolute;
            }

            editor.ApplyEventsToFloors();
            editor.RemakePath(true, true);
            if (insertedTileCount > 0)
                EditorSelection.SelectRange(editor, afterFloor + 1, afterFloor + insertedTileCount);

            string message = afterFloor + "番タイルの後へ" + insertedTileCount + "タイル挿入しました。";
            if (twirlOperations > 0) message += " twirlを" + twirlOperations + "回適用しました。";
            return message;
        }

        private static void ValidateTokens(IList<PatternToken> tokens)
        {
            if (tokens == null || tokens.Count == 0) throw new ArgumentException("角度パターンが空です。");
            if (tokens.Count > 100000) throw new ArgumentException("角度パターンが大きすぎます。");
        }

        private static void ToggleTwirl(scnEditor editor, int floor)
        {
            LevelEvent existing = editor.events.LastOrDefault(x => x.floor == floor && x.eventType == LevelEventType.Twirl);
            if (existing != null) editor.events.Remove(existing);
            else editor.events.Add(new LevelEvent(floor, LevelEventType.Twirl));
        }

        private static float GetPreviousAbsolute(scnEditor editor, int floor)
        {
            int index = floor - 2;
            while (index >= 0 && editor.levelData.angleData[index] == 999f) index--;
            return index < 0 ? 0f : editor.levelData.angleData[index];
        }

        private static bool GetCcwBefore(scnEditor editor, int floor)
        {
            int previousFloor = Math.Max(0, floor - 1);
            return previousFloor < editor.floors.Count && editor.floors[previousFloor].isCCW;
        }

        private static float RelativeToAbsolute(float previousAbsolute, double relativeAngle, bool isCcw)
        {
            if (Math.Abs(relativeAngle - 999d) < 0.000001d) return 999f;
            double delta = 180d - relativeAngle;
            double absolute = previousAbsolute + (isCcw ? -delta : delta);
            absolute %= 360d;
            if (absolute < 0d) absolute += 360d;
            return (float)absolute;
        }

        private static class MethodInfoCache
        {
            internal static readonly System.Reflection.MethodInfo OffsetFloorIds =
                AccessTools.Method(typeof(scnEditor), "OffsetFloorIDsInEvents") ??
                throw new MissingMethodException("タイル番号補正メソッドが見つかりませんでした。");
        }
    }
}
