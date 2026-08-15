using System;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class TrackAnimationRangeOperations
    {
        private sealed class AnimationState
        {
            public TrackAnimationType Appear;
            public TrackAnimationType2 Disappear;
            public float BeatsAhead;
            public float BeatsBehind;
        }

        public static string ApplyToSelection(scnEditor editor, TrackAnimationType appear,
            TrackAnimationType2 disappear, float beatsAhead, float beatsBehind)
        {
            if (beatsAhead < 0f || beatsBehind < 0f)
                throw new ArgumentOutOfRangeException("Track animation beat distances cannot be negative.");

            FloorRange range = EditorSelection.GetRange(editor, true);
            AnimationState previous = GetStateBefore(editor, range.Start);

            using (new EditorUndoScope(editor))
            {
                LevelEvent start = CreateAnimateEvent(range.Start, appear, disappear, beatsAhead, beatsBehind);
                editor.events.Add(start);

                int restoreFloor = range.End + 1;
                if (restoreFloor < editor.floors.Count)
                {
                    LevelEvent restore = CreateAnimateEvent(restoreFloor, previous.Appear, previous.Disappear,
                        previous.BeatsAhead, previous.BeatsBehind);
                    int existingIndex = editor.events.FindIndex(x => x.floor == restoreFloor &&
                        (x.eventType == LevelEventType.ChangeTrack || x.eventType == LevelEventType.AnimateTrack));
                    if (existingIndex < 0) editor.events.Add(restore);
                    else editor.events.Insert(existingIndex, restore);
                }

                editor.ApplyEventsToFloors();
                editor.RemakePath(true, true);
            }

            return range.Start + "～" + range.End + "番タイルへトラックアニメーションを適用しました" +
                   (range.End + 1 < editor.floors.Count ? "。" + (range.End + 1) + "番タイルで元の状態へ戻します。" : "。");
        }

        private static AnimationState GetStateBefore(scnEditor editor, int floor)
        {
            AnimationState state = new AnimationState
            {
                Appear = editor.levelData.trackAnimation,
                Disappear = editor.levelData.trackDisappearAnimation,
                BeatsAhead = editor.levelData.trackBeatsAhead,
                BeatsBehind = editor.levelData.trackBeatsBehind
            };

            foreach (LevelEvent evnt in editor.events.Where(x => x.floor < floor &&
                         (x.eventType == LevelEventType.ChangeTrack || x.eventType == LevelEventType.AnimateTrack))
                     .OrderBy(x => x.floor).ThenBy(x => editor.events.IndexOf(x)))
            {
                if (evnt.eventType == LevelEventType.ChangeTrack)
                {
                    ReadIfPresent(evnt, "trackAnimation", delegate(object value) { state.Appear = (TrackAnimationType)value; });
                    ReadIfPresent(evnt, "trackDisappearAnimation", delegate(object value) { state.Disappear = (TrackAnimationType2)value; });
                    ReadIfPresent(evnt, "beatsAhead", delegate(object value) { state.BeatsAhead = Convert.ToSingle(value); });
                    ReadIfPresent(evnt, "beatsBehind", delegate(object value) { state.BeatsBehind = Convert.ToSingle(value); });
                }
                else
                {
                    ReadIfEnabled(evnt, "trackAnimation", delegate(object value) { state.Appear = (TrackAnimationType)value; });
                    ReadIfEnabled(evnt, "trackDisappearAnimation", delegate(object value) { state.Disappear = (TrackAnimationType2)value; });
                    ReadIfEnabled(evnt, "beatsAhead", delegate(object value) { state.BeatsAhead = Convert.ToSingle(value); });
                    ReadIfEnabled(evnt, "beatsBehind", delegate(object value) { state.BeatsBehind = Convert.ToSingle(value); });
                }
            }
            return state;
        }

        private static LevelEvent CreateAnimateEvent(int floor, TrackAnimationType appear,
            TrackAnimationType2 disappear, float beatsAhead, float beatsBehind)
        {
            LevelEvent evnt = new LevelEvent(floor, LevelEventType.AnimateTrack);
            SetEnabled(evnt, "trackAnimation", appear);
            SetEnabled(evnt, "trackDisappearAnimation", disappear);
            SetEnabled(evnt, "beatsAhead", beatsAhead);
            SetEnabled(evnt, "beatsBehind", beatsBehind);
            return evnt;
        }

        private static void ReadIfPresent(LevelEvent evnt, string key, Action<object> assign)
        {
            object value;
            if (evnt.GetEventData().TryGetValue(key, out value)) assign(value);
        }

        private static void ReadIfEnabled(LevelEvent evnt, string key, Action<object> assign)
        {
            object value;
            bool disabled;
            if (evnt.GetEventData().TryGetValue(key, out value) &&
                (!evnt.disabled.TryGetValue(key, out disabled) || !disabled)) assign(value);
        }

        private static void SetEnabled(LevelEvent evnt, string key, object value)
        {
            evnt.GetEventData()[key] = value;
            if (evnt.disabled.ContainsKey(key)) evnt.disabled[key] = false;
        }
    }
}
