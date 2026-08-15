using System;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class ScreenScrollOperations
    {
        public static string AddTimedScroll(scnEditor editor, double screensX, double screensY, double beats)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            if (beats <= 0d || double.IsNaN(beats) || double.IsInfinity(beats))
                throw new ArgumentOutOfRangeException("beats", "拍数は0より大きい値を指定してください。");
            if ((Math.Abs(screensX) < 0.0000001d && Math.Abs(screensY) < 0.0000001d) ||
                double.IsNaN(screensX) || double.IsNaN(screensY) ||
                double.IsInfinity(screensX) || double.IsInfinity(screensY))
                throw new ArgumentException("Enter a finite, non-zero screen displacement.");

            FloorRange range = EditorSelection.GetRange(editor, true);
            int floor = range.Start;
            float effectiveBpm = GetEffectiveBpm(editor, floor);
            float scrollX = (float)(screensX * effectiveBpm * 100d / (beats * 60d));
            float scrollY = (float)(screensY * effectiveBpm * 100d / (beats * 60d));

            using (new EditorUndoScope(editor))
            {
                LevelEvent start = new LevelEvent(floor, LevelEventType.ScreenScroll);
                SetEnabled(start, "scroll", new Vector2(scrollX, scrollY));
                SetEnabled(start, "angleOffset", 0f);
                editor.events.Add(start);

                LevelEvent stop = new LevelEvent(floor, LevelEventType.ScreenScroll);
                SetEnabled(stop, "scroll", Vector2.zero);
                SetEnabled(stop, "angleOffset", (float)(beats * 180d));
                editor.events.Add(stop);

                editor.ApplyEventsToFloors();
            }

            return floor + "番タイルに、X=" + screensX.ToString("0.###") + "画面、Y=" + screensY.ToString("0.###") + "画面を" + beats.ToString("0.###") + "拍で移動するスクロールを追加しました。";
        }

        private static float GetEffectiveBpm(scnEditor editor, int floor)
        {
            float baseBpm = editor.levelData == null ? 100f : editor.levelData.bpm;
            float speed = 1f;
            if (editor.floors != null && floor >= 0 && floor < editor.floors.Count)
                speed = editor.floors[floor].speed;
            float result = baseBpm * speed;
            return result <= 0f ? baseBpm : result;
        }

        private static void SetEnabled(LevelEvent evnt, string key, object value)
        {
            evnt.GetEventData()[key] = value;
            if (evnt.disabled.ContainsKey(key)) evnt.disabled[key] = false;
        }
    }
}
