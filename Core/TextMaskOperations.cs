using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class TextMaskOperations
    {
        public static string Apply(scnEditor editor, bool visibleOutside)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            List<LevelEvent> selected = editor.selectedDecorations == null
                ? new List<LevelEvent>()
                : editor.selectedDecorations.Where(x => x != null).ToList();

            List<LevelEvent> texts = selected.Where(x => x.eventType == LevelEventType.AddText).ToList();
            List<LevelEvent> images = selected.Where(x => x.eventType == LevelEventType.AddDecoration).ToList();
            if (texts.Count != 1)
                throw new InvalidOperationException("マスクにするテキスト装飾を1個だけ選択してください。");
            if (images.Count == 0)
                throw new InvalidOperationException("テキストの内側または外側へ表示する画像装飾を1個以上選択してください。");

            LevelEvent text = texts[0];
            string marker = DecorationMarkerTags.FindTag(text, DecorationMarkerTags.TextMaskPrefix);
            if (string.IsNullOrEmpty(marker))
                marker = DecorationMarkerTags.TextMaskPrefix + DateTime.UtcNow.Ticks.ToString("x");

            using (new EditorUndoScope(editor))
            {
                DecorationMarkerTags.ReplacePrefix(text, DecorationMarkerTags.TextMaskPrefix, marker);
                foreach (LevelEvent image in images)
                {
                    DecorationMarkerTags.AddTag(image, marker);
                    SetEnabled(image, "maskingType", visibleOutside
                        ? MaskingType.VisibleOutsideMask
                        : MaskingType.VisibleInsideMask);
                }
                Refresh(editor);
            }

            return "テキストマスクを設定しました。画像" + images.Count + "個を文字の" +
                   (visibleOutside ? "外側" : "内側") + "に表示します。";
        }

        public static string Remove(scnEditor editor)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            List<LevelEvent> selected = editor.selectedDecorations == null
                ? new List<LevelEvent>()
                : editor.selectedDecorations.Where(x => x != null).ToList();
            HashSet<string> markers = new HashSet<string>(selected
                .SelectMany(DecorationMarkerTags.GetTags)
                .Where(x => x.StartsWith(DecorationMarkerTags.TextMaskPrefix, StringComparison.Ordinal)));
            if (markers.Count == 0)
                throw new InvalidOperationException("選択中の装飾にはテキストマスクが設定されていません。");

            int changed = 0;
            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent decoration in editor.decorations)
                {
                    bool hadMarker = DecorationMarkerTags.GetTags(decoration).Any(markers.Contains);
                    if (!hadMarker) continue;
                    foreach (string marker in markers) DecorationMarkerTags.RemoveTag(decoration, marker);
                    if (decoration.eventType == LevelEventType.AddDecoration &&
                        !DecorationMarkerTags.GetTags(decoration)
                            .Any(x => x.StartsWith(DecorationMarkerTags.TextMaskPrefix, StringComparison.Ordinal)))
                    {
                        SetEnabled(decoration, "maskingType", MaskingType.None);
                    }
                    changed++;
                }
                Refresh(editor);
            }
            return "テキストマスクを解除しました（" + changed + "個の装飾を更新）。";
        }

        private static void Refresh(scnEditor editor)
        {
            editor.UpdateDecorationObjects();
            if (editor.propertyControlDecorationsList != null)
                editor.propertyControlDecorationsList.RefreshItemsList(true);
            editor.ApplyEventsToFloors();
        }

        private static void SetEnabled(LevelEvent evnt, string key, object value)
        {
            evnt.data[key] = value;
            if (evnt.disabled.ContainsKey(key)) evnt.disabled[key] = false;
        }
    }
}
