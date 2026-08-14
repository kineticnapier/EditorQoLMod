using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class DiagnosticsOperations
    {
        public static string Analyze(scnEditor editor)
        {
            if (editor == null || editor.floors == null) throw new InvalidOperationException("譜面が読み込まれていません。");
            List<string> issues = new List<string>();
            List<LevelEvent> all = editor.events.Concat(editor.decorations).ToList();
            int maxFloor = editor.floors.Count - 1;

            foreach (LevelEvent evnt in all)
            {
                if (evnt.floor < 0 || evnt.floor > maxFloor)
                    issues.Add("範囲外floor: " + Label(evnt) + " floor=" + evnt.floor);
                else if (evnt.floor == 0 && evnt.info != null && !evnt.info.allowFirstFloorCheck)
                    issues.Add("先頭床に配置不可: " + Label(evnt));

                foreach (KeyValuePair<string, object> pair in evnt.data)
                {
                    if (!(pair.Value is Tuple<int, TileRelativeTo>)) continue;
                    Tuple<int, TileRelativeTo> tile = (Tuple<int, TileRelativeTo>)pair.Value;
                    int raw = RawTileId(tile, evnt.floor, editor.floors.Count);
                    if (raw < 0 || raw > maxFloor)
                        issues.Add("範囲外タイル参照: " + Label(evnt) + " / " + pair.Key + "=" + raw);
                }
            }

            foreach (var group in editor.events.Where(x => GameVersionCompat.IsSoloType(x.eventType))
                .GroupBy(x => new { x.floor, x.eventType }).Where(x => x.Count() > 1))
                issues.Add("soloイベント重複: floor " + group.Key.floor + " / " +
                    JapaneseLocalization.EventLabel(group.Key.eventType.ToString()) + " ×" + group.Count());

            foreach (int floor in editor.events.Select(x => x.floor).Distinct())
            {
                bool hold = editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Hold);
                bool pause = editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Pause);
                if (hold && pause) issues.Add("競合: floor " + floor + " にHoldとPause");
                bool twirl = editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Twirl);
                bool freeRoam = editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.FreeRoam);
                if (twirl && freeRoam) issues.Add("競合: floor " + floor + " にTwirlとFreeRoam");
            }

            if (editor.events.Any(x => x.floor == maxFloor && x.eventType == LevelEventType.FreeRoam))
                issues.Add("最終床のFreeRoam: floor " + maxFloor);

            int disabled = all.Count(x => !x.active);
            if (disabled > 0) issues.Add("無効イベント: " + disabled + "件");

            if (issues.Count == 0) return "診断結果: 問題は見つかりませんでした。";
            const int limit = 80;
            string body = string.Join("\n", issues.Take(limit).Select((x, i) => (i + 1) + ". " + x).ToArray());
            if (issues.Count > limit) body += "\n…ほか" + (issues.Count - limit) + "件";
            return "診断結果: " + issues.Count + "件\n" + body;
        }

        public static string SafeRepair(scnEditor editor)
        {
            if (editor == null || editor.floors == null) throw new InvalidOperationException("譜面が読み込まれていません。");
            int maxFloor = editor.floors.Count - 1;
            List<LevelEvent> remove = new List<LevelEvent>();
            List<LevelEvent> all = editor.events.Concat(editor.decorations).ToList();

            remove.AddRange(all.Where(x => x.floor < 0 || x.floor > maxFloor));
            remove.AddRange(all.Where(x => x.floor == 0 && x.info != null && !x.info.allowFirstFloorCheck));
            remove.AddRange(editor.events.Where(x => x.floor == maxFloor && x.eventType == LevelEventType.FreeRoam));

            foreach (var group in editor.events.Where(x => GameVersionCompat.IsSoloType(x.eventType))
                .GroupBy(x => new { x.floor, x.eventType }).Where(x => x.Count() > 1))
                remove.AddRange(group.Skip(1));

            remove = remove.Distinct().ToList();
            if (remove.Count == 0) return "安全に自動修復できる問題はありませんでした。競合イベントは手動で確認してください。";

            using (new EditorUndoScope(editor))
            {
                editor.RemoveEvents(remove);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (remove.Any(x => EventOperations.AffectsPath(x.eventType))) editor.RemakePath(true, true);
            }
            return remove.Count + "件を安全修復しました。Hold/Pauseなど選択が必要な競合は削除していません。";
        }

        private static int RawTileId(Tuple<int, TileRelativeTo> tile, int eventFloor, int floorCount)
        {
            if (tile.Item2 == TileRelativeTo.ThisTile) return eventFloor + tile.Item1;
            if (tile.Item2 == TileRelativeTo.Start) return tile.Item1;
            if (tile.Item2 == TileRelativeTo.End) return floorCount - 1 + tile.Item1;
            return tile.Item1;
        }

        private static string Label(LevelEvent evnt)
        {
            return JapaneseLocalization.EventLabel(evnt.eventType.ToString()) + "@" + evnt.floor;
        }
    }
}
