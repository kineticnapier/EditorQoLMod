using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class NavigationOperations
    {
        public static string GoToFloor(scnEditor editor, int floor)
        {
            if (floor < 0 || floor >= editor.floors.Count) throw new ArgumentOutOfRangeException("floor");
            editor.SelectFloor(editor.floors[floor], true);
            return floor + "番タイルへ移動しました。";
        }

        public static string GoRelative(scnEditor editor, int offset)
        {
            if (offset == 0) throw new ArgumentException("移動量に0は指定できません。");
            FloorRange range = EditorSelection.GetRange(editor, true);
            int current = offset > 0 ? range.End : range.Start;
            int target = current + offset;
            if (target < 0 || target >= editor.floors.Count)
                throw new ArgumentOutOfRangeException("offset", "移動先が譜面外です。");
            return GoToFloor(editor, target);
        }

        public static string SelectCount(scnEditor editor, int count)
        {
            if (count == 0) throw new ArgumentException("選択数に0は指定できません。");
            FloorRange range = EditorSelection.GetRange(editor, true);
            int anchor = count > 0 ? range.Start : range.End;
            int other = anchor + count - Math.Sign(count);
            if (other < 0 || other >= editor.floors.Count) throw new ArgumentOutOfRangeException("count", "選択範囲が譜面外へ出ます。");
            EditorSelection.SelectRange(editor, anchor, other);
            FloorRange result = EditorSelection.GetRange(editor, true);
            return result.Start + "～" + result.End + "番タイルを選択しました。";
        }

        public static string FindEvent(scnEditor editor, LevelEventType eventType, bool forward)
        {
            int current = editor.selectedFloors != null && editor.selectedFloors.Count > 0
                ? (forward ? editor.selectedFloors.Max(x => x.seqID) : editor.selectedFloors.Min(x => x.seqID))
                : (forward ? -1 : editor.floors.Count);

            var candidates = editor.events.Concat(editor.decorations).Where(x => x.eventType == eventType);
            int target;
            if (forward)
            {
                var next = candidates.Where(x => x.floor > current).OrderBy(x => x.floor).FirstOrDefault();
                if (next == null) throw new InvalidOperationException("これより後に" + JapaneseLocalization.EventLabel(eventType.ToString()) + "はありません。");
                target = next.floor;
            }
            else
            {
                var previous = candidates.Where(x => x.floor < current).OrderByDescending(x => x.floor).FirstOrDefault();
                if (previous == null) throw new InvalidOperationException("これより前に" + JapaneseLocalization.EventLabel(eventType.ToString()) + "はありません。");
                target = previous.floor;
            }
            return GoToFloor(editor, target);
        }

        public static IList<KeyValuePair<string, string>> BookmarkOptions(scnEditor editor)
        {
            List<LevelEvent> bookmarks = editor.events.Where(x => x.eventType == LevelEventType.Bookmark)
                .OrderBy(x => x.floor).ToList();
            if (bookmarks.Count == 0)
                return new[] { new KeyValuePair<string, string>("__none__", "Bookmarkなし") };

            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < bookmarks.Count; i++)
            {
                LevelEvent bookmark = bookmarks[i];
                string comment = NearbyComment(editor, bookmark.floor);
                string label = bookmark.floor + "番" + (string.IsNullOrWhiteSpace(comment) ? string.Empty : " — " + OneLine(comment));
                result.Add(new KeyValuePair<string, string>(bookmark.floor + ":" + i, label));
            }
            return result;
        }

        public static string GoToBookmark(scnEditor editor, string value)
        {
            return GoToFloor(editor, ParseBookmarkFloor(value));
        }

        public static string AddBookmark(scnEditor editor)
        {
            FloorRange range = EditorSelection.GetRange(editor, true);
            int floor = range.Start;
            if (editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Bookmark))
                return floor + "番タイルには既にBookmarkがあります。";
            using (new EditorUndoScope(editor))
            {
                editor.events.Add(new LevelEvent(floor, LevelEventType.Bookmark));
                editor.ApplyEventsToFloors();
            }
            return floor + "番タイルへBookmarkを追加しました。";
        }

        public static string DeleteBookmark(scnEditor editor, string value)
        {
            int floor = ParseBookmarkFloor(value);
            LevelEvent bookmark = editor.events.FirstOrDefault(x => x.floor == floor && x.eventType == LevelEventType.Bookmark);
            if (bookmark == null) throw new InvalidOperationException("Bookmarkが見つかりません。");
            using (new EditorUndoScope(editor))
            {
                editor.RemoveEvent(bookmark, false);
                editor.ApplyEventsToFloors();
            }
            return floor + "番タイルのBookmarkを削除しました。";
        }

        private static int ParseBookmarkFloor(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "__none__")
                throw new InvalidOperationException("Bookmarkが選択されていません。");
            int colon = value.IndexOf(':');
            int floor;
            if (!int.TryParse(colon < 0 ? value : value.Substring(0, colon), out floor))
                throw new FormatException("Bookmarkの床番号を読み取れませんでした。");
            return floor;
        }

        private static string NearbyComment(scnEditor editor, int floor)
        {
            LevelEvent comment = editor.events.Where(x => x.eventType == LevelEventType.EditorComment &&
                x.GetEventData().ContainsKey("comment") && x.GetEventData()["comment"] is string)
                .OrderBy(x => Math.Abs(x.floor - floor)).ThenBy(x => x.floor).FirstOrDefault();
            if (comment == null || Math.Abs(comment.floor - floor) > 5) return string.Empty;
            return (string)comment.GetEventData()["comment"];
        }

        private static string OneLine(string text)
        {
            string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= 36 ? value : value.Substring(0, 36) + "…";
        }
    }
}
