using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal enum EventStateOperation
    {
        Enable,
        Disable,
        Show,
        Hide,
        Lock,
        Unlock
    }

    internal enum EventPasteMode
    {
        Add,
        OverwriteSameType,
        OverwriteAllFloorEvents
    }

    internal static class EventAdvancedOperations
    {
        public static string AddAtInterval(scnEditor editor, LevelEventType eventType, int interval, int phase, bool overwriteSolo)
        {
            if (interval < 1) throw new ArgumentOutOfRangeException("interval", "間隔は1以上にしてください。");
            if (phase < 0 || phase >= interval) throw new ArgumentOutOfRangeException("phase", "開始ずれは0以上、間隔未満にしてください。");
            FloorRange range = EditorSelection.GetRange(editor, true);
            LevelEvent sample = new LevelEvent(0, eventType);
            int added = 0;
            int skipped = 0;

            using (new EditorUndoScope(editor))
            {
                for (int floor = range.Start + phase; floor <= range.End; floor += interval)
                {
                    if (floor == 0 && !sample.info.allowFirstFloorCheck)
                    {
                        skipped++;
                        continue;
                    }
                    if (!CanPlace(editor, eventType, floor, overwriteSolo))
                    {
                        skipped++;
                        continue;
                    }

                    if (overwriteSolo && EditorConstants.soloTypes.Contains(eventType))
                        editor.events.RemoveAll(x => x.floor == floor && x.eventType == eventType);

                    LevelEvent created = new LevelEvent(floor, eventType);
                    if (created.IsDecoration)
                    {
                        if (created.data.ContainsKey("relativeTo")) created.data["relativeTo"] = DecPlacementType.Tile;
                        editor.decorations.Add(created);
                    }
                    else editor.events.Add(created);
                    added++;
                }

                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (EventOperations.AffectsPath(eventType)) editor.RemakePath(true, true);
            }

            return JapaneseLocalization.EventLabel(eventType.ToString()) + "を" + added + "個追加しました。" +
                   (skipped > 0 ? " 競合などで" + skipped + "個スキップしました。" : string.Empty);
        }

        public static string PasteClipboardEvents(scnEditor editor, int interval, int phase,
            bool includeDecorations, EventPasteMode mode)
        {
            if (editor == null || editor.clipboard == null || editor.clipboard.Count == 0 ||
                editor.clipboardContent != scnEditor.ClipboardContent.Floors ||
                !(editor.clipboard[0] is scnEditor.FloorData))
                throw new InvalidOperationException("先に床イベントをコピーしてください（Ctrl+Shift+C）。");
            if (interval < 1) throw new ArgumentOutOfRangeException("interval", "間隔は1以上にしてください。");
            if (phase < 0 || phase >= interval)
                throw new ArgumentOutOfRangeException("phase", "開始ずれは0以上、間隔未満にしてください。");

            scnEditor.FloorData source = (scnEditor.FloorData)editor.clipboard[0];
            List<LevelEvent> sourceEvents = source.levelEventData ?? new List<LevelEvent>();
            List<LevelEvent> sourceDecorations = includeDecorations
                ? (source.attachedDecorations ?? new List<LevelEvent>())
                : new List<LevelEvent>();
            if (sourceEvents.Count == 0 && sourceDecorations.Count == 0)
                throw new InvalidOperationException("クリップボードに貼り付け可能なイベントがありません。");

            FloorRange range = EditorSelection.GetRange(editor, true);
            List<int> targets = new List<int>();
            for (int floor = range.Start + phase; floor <= range.End; floor += interval) targets.Add(floor);
            int added = 0;
            int skipped = 0;
            int removed = 0;
            HashSet<LevelEventType> sourceTypes = new HashSet<LevelEventType>(sourceEvents.Select(x => x.eventType));
            HashSet<LevelEventType> decorationTypes = new HashSet<LevelEventType>(sourceDecorations.Select(x => x.eventType));

            using (new EditorUndoScope(editor))
            {
                if (mode == EventPasteMode.OverwriteAllFloorEvents)
                {
                    removed += editor.events.RemoveAll(x => targets.Contains(x.floor));
                }
                else if (mode == EventPasteMode.OverwriteSameType)
                {
                    removed += editor.events.RemoveAll(x => targets.Contains(x.floor) && sourceTypes.Contains(x.eventType));
                    if (includeDecorations && decorationTypes.Count > 0)
                    {
                        List<LevelEvent> oldDecorations = editor.decorations
                            .Where(x => targets.Contains(x.floor) && decorationTypes.Contains(x.eventType)).ToList();
                        editor.RemoveEvents(oldDecorations);
                        removed += oldDecorations.Count;
                    }
                }

                foreach (int floor in targets)
                {
                    foreach (LevelEvent sourceEvent in sourceEvents)
                    {
                        if (floor == 0 && !sourceEvent.info.allowFirstFloorCheck)
                        {
                            skipped++;
                            continue;
                        }
                        if (!CanPlace(editor, sourceEvent.eventType, floor, false))
                        {
                            skipped++;
                            continue;
                        }
                        LevelEvent copy = sourceEvent.Copy();
                        copy.floor = floor;
                        editor.events.Add(copy);
                        added++;
                    }

                    foreach (LevelEvent sourceDecoration in sourceDecorations)
                    {
                        LevelEvent copy = sourceDecoration.Copy();
                        copy.floor = floor;
                        if (copy.data.ContainsKey("relativeTo")) copy.data["relativeTo"] = DecPlacementType.Tile;
                        editor.decorations.Add(copy);
                        added++;
                    }
                }

                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (sourceEvents.Any(x => EventOperations.AffectsPath(x.eventType))) editor.RemakePath(true, true);
            }

            string result = targets.Count + "床へイベントを" + added + "件貼り付けました。";
            if (removed > 0) result += " 既存" + removed + "件を上書きしました。";
            if (skipped > 0) result += " 競合などで" + skipped + "件スキップしました。";
            return result;
        }

        public static string SetStateInSelection(scnEditor editor, LevelEventType eventType,
            EventStateOperation operation, bool includeDecorations)
        {
            FloorRange range = EditorSelection.GetRange(editor, true);
            List<LevelEvent> matches = editor.events.Where(x => x.eventType == eventType &&
                x.floor >= range.Start && x.floor <= range.End).ToList();
            if (includeDecorations)
                matches.AddRange(editor.decorations.Where(x => x.eventType == eventType &&
                    x.floor >= range.Start && x.floor <= range.End));
            if (operation == EventStateOperation.Show || operation == EventStateOperation.Hide ||
                operation == EventStateOperation.Lock || operation == EventStateOperation.Unlock)
                matches = matches.Where(x => x.IsDecoration).ToList();
            if (matches.Count == 0) return "対象イベントがありません。表示・ロック操作は装飾イベントのみ対象です。";

            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent item in matches)
                {
                    switch (operation)
                    {
                        case EventStateOperation.Enable:
                            editor.EnableEvent(item, true);
                            break;
                        case EventStateOperation.Disable:
                            editor.EnableEvent(item, false);
                            break;
                        case EventStateOperation.Show:
                            editor.ShowEvent(item, true);
                            break;
                        case EventStateOperation.Hide:
                            editor.ShowEvent(item, false);
                            break;
                        case EventStateOperation.Lock:
                            editor.LockEvent(item, true);
                            break;
                        case EventStateOperation.Unlock:
                            editor.LockEvent(item, false);
                            break;
                    }
                }
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (EventOperations.AffectsPath(eventType)) editor.RemakePath(true, true);
            }

            return JapaneseLocalization.EventLabel(eventType.ToString()) + "を" + matches.Count + "件変更しました（" + StateLabel(operation) + "）。";
        }

        internal static bool CanPlace(scnEditor editor, LevelEventType type, int floor, bool overwriteSolo)
        {
            if (floor < 0 || floor >= editor.floors.Count) return false;
            if (EditorConstants.soloTypes.Contains(type) && !overwriteSolo &&
                editor.events.Any(x => x.floor == floor && x.eventType == type)) return false;
            if (type == LevelEventType.Hold && editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Pause)) return false;
            if (type == LevelEventType.Pause && editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Hold)) return false;
            if (type == LevelEventType.FreeRoam && editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.Twirl)) return false;
            if (type == LevelEventType.Twirl && editor.events.Any(x => x.floor == floor && x.eventType == LevelEventType.FreeRoam)) return false;
            if (type == LevelEventType.FreeRoam && floor == editor.floors.Count - 1) return false;
            return true;
        }

        private static string StateLabel(EventStateOperation operation)
        {
            switch (operation)
            {
                case EventStateOperation.Enable: return "有効";
                case EventStateOperation.Disable: return "無効";
                case EventStateOperation.Show: return "表示";
                case EventStateOperation.Hide: return "非表示";
                case EventStateOperation.Lock: return "ロック";
                case EventStateOperation.Unlock: return "ロック解除";
                default: return operation.ToString();
            }
        }
    }
}
