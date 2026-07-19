using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal enum ShiftOutOfRangeMode
    {
        Abort,
        Clamp,
        Delete
    }

    internal static class EventOperations
    {
        public static LevelEventType ParseEventType(string text)
        {
            LevelEventType result;
            if (!Enum.TryParse(text == null ? string.Empty : text.Trim(), true, out result) || result == LevelEventType.None)
                throw new ArgumentException("Unknown event type: " + text);
            return result;
        }

        public static string DeleteInSelection(scnEditor editor, LevelEventType eventType, bool includeDecorations)
        {
            FloorRange range = EditorSelection.GetRange(editor, true);
            List<LevelEvent> matches = editor.events
                .Where(x => x.floor >= range.Start && x.floor <= range.End && x.eventType == eventType).ToList();
            if (includeDecorations)
                matches.AddRange(editor.decorations.Where(x => x.floor >= range.Start && x.floor <= range.End && x.eventType == eventType));
            if (matches.Count == 0) return "No matching events found.";

            using (new EditorUndoScope(editor))
            {
                editor.RemoveEvents(matches);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (AffectsPath(eventType)) editor.RemakePath(true, true);
            }
            return JapaneseLocalization.EventLabel(eventType.ToString()) + "を" + matches.Count + "個削除しました。";
        }

        public static string ShiftInSelection(scnEditor editor, LevelEventType eventType, int offset, bool includeDecorations)
        {
            return ShiftInSelection(editor, eventType, offset, includeDecorations, ShiftOutOfRangeMode.Abort);
        }

        public static string ShiftInSelection(scnEditor editor, LevelEventType eventType, int offset,
            bool includeDecorations, ShiftOutOfRangeMode outOfRangeMode)
        {
            if (offset == 0) return "移動量が0のため変更しませんでした。";
            FloorRange range = EditorSelection.GetRange(editor, true);
            List<LevelEvent> matches = editor.events
                .Where(x => x.floor >= range.Start && x.floor <= range.End && x.eventType == eventType).ToList();
            if (includeDecorations)
                matches.AddRange(editor.decorations.Where(x => x.floor >= range.Start && x.floor <= range.End && x.eventType == eventType));
            if (matches.Count == 0) return "対象イベントがありません。";

            int maxFloor = editor.floors.Count - 1;
            List<LevelEvent> outside = matches.Where(x => x.floor + offset < 0 || x.floor + offset > maxFloor).ToList();
            if (outside.Count > 0 && outOfRangeMode == ShiftOutOfRangeMode.Abort)
                throw new InvalidOperationException(outside[0].floor + "番タイルのイベントを" + offset +
                    "タイル移動すると譜面外へ出ます。");

            int moved = 0;
            int deleted = 0;
            using (new EditorUndoScope(editor))
            {
                if (outOfRangeMode == ShiftOutOfRangeMode.Delete && outside.Count > 0)
                {
                    editor.RemoveEvents(outside);
                    matches = matches.Except(outside).ToList();
                    deleted = outside.Count;
                }

                foreach (LevelEvent e in matches)
                {
                    int target = e.floor + offset;
                    if (outOfRangeMode == ShiftOutOfRangeMode.Clamp)
                        target = Math.Max(0, Math.Min(maxFloor, target));
                    e.floor = target;
                    moved++;
                }

                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (AffectsPath(eventType)) editor.RemakePath(true, true);
            }

            string result = JapaneseLocalization.EventLabel(eventType.ToString()) + "を" + moved + "個、" +
                offset + "タイル移動しました。";
            if (deleted > 0) result += " 譜面外へ出る" + deleted + "個を削除しました。";
            if (outside.Count > 0 && outOfRangeMode == ShiftOutOfRangeMode.Clamp)
                result += " 譜面外へ出る" + outside.Count + "個は端へ固定しました。";
            return result;
        }

        public static string Statistics(scnEditor editor, bool selectionOnly)
        {
            int start = 0;
            int end = editor.floors.Count - 1;
            if (selectionOnly)
            {
                FloorRange range = EditorSelection.GetRange(editor, true);
                start = range.Start;
                end = range.End;
            }

            List<LevelEvent> target = editor.events.Concat(editor.decorations)
                .Where(x => x.floor >= start && x.floor <= end).ToList();
            if (target.Count == 0) return "対象範囲にイベントはありません。";

            var groups = target.GroupBy(x => x.eventType)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key.ToString())
                .Take(30)
                .ToList();
            var busiest = target.GroupBy(x => x.floor)
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First();
            int floorEvents = target.Count(x => !x.IsDecoration);
            int decorationEvents = target.Count - floorEvents;
            List<string> lines = new List<string>
            {
                "対象床: " + start + "～" + end,
                "合計: " + target.Count + "件（床イベント " + floorEvents + " / 装飾 " + decorationEvents + "）",
                "最多: " + busiest.Key + "番床に" + busiest.Count() + "件",
                "種類数: " + groups.Count
            };
            lines.AddRange(groups.Select(x => JapaneseLocalization.EventLabel(x.Key.ToString()) + ": " + x.Count() + "個"));
            return string.Join("\n", lines.ToArray());
        }

        internal static bool AffectsPath(LevelEventType type)
        {
            return type == LevelEventType.SetSpeed || type == LevelEventType.Twirl ||
                   type == LevelEventType.Pause || type == LevelEventType.Hold ||
                   type == LevelEventType.MultiPlanet || type == LevelEventType.FreeRoam ||
                   type == LevelEventType.FreeRoamTwirl || type == LevelEventType.FreeRoamRemove;
        }
    }
}
