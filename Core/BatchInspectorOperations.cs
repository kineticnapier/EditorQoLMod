using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class BatchInspectorOperations
    {
        public static string Open(scnEditor editor, LevelEventType eventType)
        {
            FloorRange range = EditorSelection.GetRange(editor, true);
            List<LevelEvent> matches = editor.events.Where(x => x.eventType == eventType &&
                x.floor >= range.Start && x.floor <= range.End)
                .OrderBy(x => x.floor).ThenBy(x => editor.events.IndexOf(x)).ToList();
            if (matches.Count < 2)
                throw new InvalidOperationException("選択範囲に同種類の床イベントを2個以上配置してください。");

            LevelEvent fake = BuildFake(matches);
            int floor = Math.Max(0, Math.Min(matches[0].floor, editor.floors.Count - 1));
            editor.SelectFloor(editor.floors[floor], true);
            InspectorPanel inspector = editor.levelEventsPanel;
            inspector.ShowPanel(eventType, 0);
            PropertiesPanel panel = inspector.panelsList.FirstOrDefault(x => x.levelEventType == eventType);
            if (panel == null) throw new InvalidOperationException("本家Inspectorの対象パネルが見つかりませんでした。");

            inspector.selectedEvent = fake;
            inspector.selectedEventType = eventType;
            inspector.titleCanvas.SetActive(true);
            inspector.title.text = JapaneseLocalization.EventLabel(eventType.ToString()) + "（" + matches.Count + "件を一括編集）";
            panel.gameObject.SetActive(true);
            panel.SetProperties(fake, true);
            inspector.ShowInspector(true, true);
            return matches.Count + "件を本家Inspectorで一括編集します。異なる値の項目はチェックを入れてから編集してください。";
        }

        private static LevelEvent BuildFake(IList<LevelEvent> matches)
        {
            LevelEvent fake = new LevelEvent(-1, matches[0].eventType) { isFake = true };
            foreach (LevelEvent item in matches) fake.realEvents.Add(item);
            fake.data.Remove("floor");
            fake.disabled.Remove("floor");

            foreach (string key in fake.data.Keys.ToList())
            {
                object first;
                if (!matches[0].data.TryGetValue(key, out first) || matches.Any(x => !x.data.ContainsKey(key)))
                {
                    fake.disabled[key] = true;
                    continue;
                }
                bool allEnabled = matches.All(x => !x.disabled.ContainsKey(key) || !x.disabled[key]);
                if (fake.info.propertiesInfo[key].type == PropertyType.Vector2 && first is Vector2)
                {
                    Vector2 firstVector = (Vector2)first;
                    bool sameX = matches.All(x => x.data[key] is Vector2 &&
                        Mathf.Abs(((Vector2)x.data[key]).x - firstVector.x) < 0.0001f);
                    bool sameY = matches.All(x => x.data[key] is Vector2 &&
                        Mathf.Abs(((Vector2)x.data[key]).y - firstVector.y) < 0.0001f);
                    fake.data[key] = new Vector2(sameX ? firstVector.x : float.NaN,
                        sameY ? firstVector.y : float.NaN);
                    fake.disabled[key] = !allEnabled || (!sameX && !sameY);
                }
                else
                {
                    bool same = matches.All(x => ValuesEqual(first, x.data[key]));
                    fake.data[key] = first;
                    fake.disabled[key] = !allEnabled || !same;
                }
            }

            return fake;
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a is float && b is float) return Math.Abs((float)a - (float)b) < 0.0001f;
            if (a is double && b is double) return Math.Abs((double)a - (double)b) < 0.0000001d;
            return a.Equals(b);
        }
    }
}
