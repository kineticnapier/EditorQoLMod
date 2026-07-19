using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal sealed class FilterOrderItem
    {
        public LevelEvent Event;
        public string Label;
    }

    internal static class FilterOrderOperations
    {
        public static List<FilterOrderItem> GetAtSelectedFloor(scnEditor editor)
        {
            FloorRange range = EditorSelection.GetRange(editor, true);
            int floor = range.Start;
            return editor.events.Where(x => x.floor == floor && IsFilter(x.eventType))
                .Select((x, i) => new FilterOrderItem { Event = x, Label = (i + 1) + ". " + Describe(x) })
                .ToList();
        }

        public static string Move(scnEditor editor, int visibleIndex, int direction)
        {
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException("direction");
            List<FilterOrderItem> items = GetAtSelectedFloor(editor);
            if (visibleIndex < 0 || visibleIndex >= items.Count) throw new ArgumentOutOfRangeException("visibleIndex");
            int targetVisible = visibleIndex + direction;
            if (targetVisible < 0 || targetVisible >= items.Count) return "このフィルターはすでに端にあります。";

            LevelEvent first = items[visibleIndex].Event;
            LevelEvent second = items[targetVisible].Event;
            int firstIndex = editor.events.IndexOf(first);
            int secondIndex = editor.events.IndexOf(second);
            if (firstIndex < 0 || secondIndex < 0) throw new InvalidOperationException("Filter event was not found.");

            using (new EditorUndoScope(editor))
            {
                editor.events[firstIndex] = second;
                editor.events[secondIndex] = first;
                editor.ApplyEventsToFloors();
            }
            return Describe(first) + "を" + (direction < 0 ? "上" : "下") + "へ移動しました。";
        }

        private static bool IsFilter(LevelEventType type)
        {
            return type == LevelEventType.SetFilter || type == LevelEventType.SetFilterAdvanced;
        }

        private static string Describe(LevelEvent evnt)
        {
            object filter;
            string name = evnt.data.TryGetValue("filter", out filter) ? Convert.ToString(filter) : "不明";
            return evnt.eventType + " - " + name;
        }
    }
}
