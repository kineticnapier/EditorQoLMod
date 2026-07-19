using System;
using System.Linq;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal struct FloorRange
    {
        public int Start;
        public int End;
        public int Count { get { return End - Start + 1; } }
    }

    internal static class EditorSelection
    {
        public static FloorRange GetRange(scnEditor editor, bool allowFloorZero)
        {
            if (editor == null || editor.floors == null || editor.floors.Count == 0)
                throw new InvalidOperationException("譜面が読み込まれていません。");
            if (editor.selectedFloors == null || editor.selectedFloors.Count == 0)
                throw new InvalidOperationException("タイルが選択されていません。");

            int start = editor.selectedFloors.Min(x => x.seqID);
            int end = editor.selectedFloors.Max(x => x.seqID);
            if (!allowFloorZero && start == 0)
                throw new InvalidOperationException("Floor 0 cannot be used for this operation.");
            return new FloorRange { Start = start, End = end };
        }

        public static void SelectRange(scnEditor editor, int start, int end)
        {
            start = Math.Max(0, Math.Min(start, editor.floors.Count - 1));
            end = Math.Max(0, Math.Min(end, editor.floors.Count - 1));
            if (start > end) { int t = start; start = end; end = t; }
            if (start == end) editor.SelectFloor(editor.floors[start], true);
            else editor.MultiSelectFloors(editor.floors[start], editor.floors[end], false);
        }
    }
}
