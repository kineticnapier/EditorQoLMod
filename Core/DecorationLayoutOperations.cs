using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class DecorationLayoutOperations
    {
        public static string AlignPosition(scnEditor editor, bool xAxis)
        {
            List<LevelEvent> items = Selected(editor, 2);
            Vector2 reference = GetVector(items[0], "position");
            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent item in items)
                {
                    Vector2 value = GetVector(item, "position");
                    if (xAxis) value.x = reference.x;
                    else value.y = reference.y;
                    item.data["position"] = value;
                }
                editor.UpdateDecorationObjects();
            }
            return items.Count + "個の装飾の" + (xAxis ? "X" : "Y") + "座標を揃えました。";
        }

        public static string DistributePosition(scnEditor editor, bool xAxis)
        {
            List<LevelEvent> items = Selected(editor, 3)
                .OrderBy(x => xAxis ? GetVector(x, "position").x : GetVector(x, "position").y)
                .ToList();
            Vector2 first = GetVector(items[0], "position");
            Vector2 last = GetVector(items[items.Count - 1], "position");
            float start = xAxis ? first.x : first.y;
            float end = xAxis ? last.x : last.y;

            using (new EditorUndoScope(editor))
            {
                for (int i = 1; i < items.Count - 1; i++)
                {
                    float t = (float)i / (items.Count - 1);
                    Vector2 value = GetVector(items[i], "position");
                    if (xAxis) value.x = Mathf.Lerp(start, end, t);
                    else value.y = Mathf.Lerp(start, end, t);
                    items[i].data["position"] = value;
                }
                editor.UpdateDecorationObjects();
            }
            return items.Count + "個の装飾を" + (xAxis ? "X" : "Y") + "方向へ等間隔配置しました。";
        }

        public static string Nudge(scnEditor editor, float x, float y)
        {
            List<LevelEvent> items = Selected(editor, 1);
            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent item in items)
                    item.data["position"] = GetVector(item, "position") + new Vector2(x, y);
                editor.UpdateDecorationObjects();
            }
            return items.Count + "個の装飾を(" + x.ToString("0.######") + ", " + y.ToString("0.######") + ")移動しました。";
        }

        public static string MatchRotation(scnEditor editor)
        {
            List<LevelEvent> items = Selected(editor, 2);
            float value = GetFloat(items[0], "rotation");
            using (new EditorUndoScope(editor))
            {
                for (int i = 1; i < items.Count; i++) items[i].data["rotation"] = value;
                editor.UpdateDecorationObjects();
            }
            return items.Count + "個の装飾の回転を揃えました。";
        }

        public static string MatchScale(scnEditor editor)
        {
            List<LevelEvent> items = Selected(editor, 2);
            Vector2 value = GetVector(items[0], "scale");
            using (new EditorUndoScope(editor))
            {
                for (int i = 1; i < items.Count; i++) items[i].data["scale"] = value;
                editor.UpdateDecorationObjects();
            }
            return items.Count + "個の装飾の拡大率を揃えました。";
        }

        public static string Interpolate(scnEditor editor, string property)
        {
            List<LevelEvent> items = Selected(editor, 2)
                .OrderBy(x => editor.decorations.IndexOf(x)).ToList();
            if (property == "position" || property == "scale")
            {
                Vector2 start = GetVector(items[0], property);
                Vector2 end = GetVector(items[items.Count - 1], property);
                using (new EditorUndoScope(editor))
                {
                    for (int i = 0; i < items.Count; i++)
                        items[i].data[property] = Vector2.Lerp(start, end, Fraction(i, items.Count));
                    editor.UpdateDecorationObjects();
                }
            }
            else if (property == "rotation")
            {
                float start = GetFloat(items[0], property);
                float end = GetFloat(items[items.Count - 1], property);
                using (new EditorUndoScope(editor))
                {
                    for (int i = 0; i < items.Count; i++)
                        items[i].data[property] = Mathf.Lerp(start, end, Fraction(i, items.Count));
                    editor.UpdateDecorationObjects();
                }
            }
            else
            {
                throw new ArgumentException("補間できない装飾項目です: " + property);
            }
            return items.Count + "個の装飾の" + JapaneseLocalization.PropertyLabel(property) + "を補間しました。";
        }

        private static float Fraction(int index, int count)
        {
            return count <= 1 ? 0f : (float)index / (count - 1);
        }

        private static List<LevelEvent> Selected(scnEditor editor, int minimum)
        {
            if (editor == null || editor.selectedDecorations == null || editor.selectedDecorations.Count < minimum)
                throw new InvalidOperationException("装飾を" + minimum + "個以上選択してください。");
            List<LevelEvent> result = editor.selectedDecorations.Where(x => x != null).ToList();
            if (result.Count < minimum) throw new InvalidOperationException("装飾を" + minimum + "個以上選択してください。");
            return result;
        }

        private static Vector2 GetVector(LevelEvent item, string key)
        {
            object value;
            if (!item.data.TryGetValue(key, out value) || !(value is Vector2))
                throw new InvalidOperationException(JapaneseLocalization.EventLabel(item.eventType.ToString()) +
                    "に" + JapaneseLocalization.PropertyLabel(key) + "がありません。");
            return (Vector2)value;
        }

        private static float GetFloat(LevelEvent item, string key)
        {
            object value;
            if (!item.data.TryGetValue(key, out value))
                throw new InvalidOperationException(JapaneseLocalization.EventLabel(item.eventType.ToString()) +
                    "に" + JapaneseLocalization.PropertyLabel(key) + "がありません。");
            try { return Convert.ToSingle(value); }
            catch { throw new InvalidOperationException(JapaneseLocalization.PropertyLabel(key) + "が数値ではありません。"); }
        }
    }
}
