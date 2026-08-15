using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal sealed class EventOccurrencePage
    {
        internal IList<KeyValuePair<string, string>> Options;
        internal int Page;
        internal int PageCount;
        internal int Total;
    }

    internal static class EventSearchOperations
    {
        private const int PageSize = 100;

        internal static EventOccurrencePage GetPage(scnEditor editor, LevelEventType type,
            bool includeDecorations, int requestedPage)
        {
            List<LevelEvent> matches = editor.events.Where(x => x.eventType == type).ToList();
            if (includeDecorations)
                matches.AddRange(editor.decorations.Where(x => x.eventType == type));
            matches = matches.OrderBy(x => x.floor).ThenBy(x => x.IsDecoration ? 1 : 0).ToList();
            int pageCount = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
            int page = Math.Max(1, Math.Min(requestedPage, pageCount));
            List<LevelEvent> visible = matches.Skip((page - 1) * PageSize).Take(PageSize).ToList();
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < visible.Count; i++)
            {
                LevelEvent item = visible[i];
                int absoluteIndex = (page - 1) * PageSize + i;
                options.Add(new KeyValuePair<string, string>(item.floor + ":" + absoluteIndex,
                    item.floor + "番" + (item.IsDecoration ? "［装飾］" : string.Empty) + Describe(item)));
            }
            if (options.Count == 0)
                options.Add(new KeyValuePair<string, string>("__none__", "該当イベントなし"));
            return new EventOccurrencePage
            {
                Options = options,
                Page = page,
                PageCount = pageCount,
                Total = matches.Count
            };
        }

        internal static string GoTo(scnEditor editor, string value)
        {
            if (string.IsNullOrEmpty(value) || value == "__none__")
                throw new InvalidOperationException("移動するイベントが選ばれていません。");
            int separator = value.IndexOf(':');
            int floor;
            if (!int.TryParse(separator < 0 ? value : value.Substring(0, separator), out floor))
                throw new FormatException("イベントの床番号を読み取れませんでした。");
            return NavigationOperations.GoToFloor(editor, floor);
        }

        private static string Describe(LevelEvent item)
        {
            if (item.GetEventData() == null) return string.Empty;
            string[] keys = { "comment", "tag", "beatsPerMinute", "bpmMultiplier", "duration", "angleOffset" };
            foreach (string key in keys)
            {
                object value;
                if (!item.GetEventData().TryGetValue(key, out value) || value == null) continue;
                string text = value.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (text.Length > 28) text = text.Substring(0, 28) + "…";
                if (text.Length > 0) return " — " + text;
            }
            return string.Empty;
        }
    }
}
