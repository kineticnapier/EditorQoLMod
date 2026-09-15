using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ADOFAI;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    // scnGame.PrepVfx(List<scrFloor>, ...) builds a List<LevelEvent> for every floor, then
    // allocates a Dictionary, string[9], and two FindAll result lists for every floor even though
    // only RepeatEvents, SetConditionalEvents, and events carrying a non-empty bgImage are
    // consulted by that part of the method. On very large charts dominated by timing events
    // (for example SetSpeed), almost all of those allocations and scans are dead work.
    //
    // Hook the public editor wrapper directly. Bind its arguments by position because the bool
    // parameter name differs between game builds. In the current runtime this bool is remakeFloors.
    [HarmonyPatch(typeof(scnGame), "PrepVfx", new[] { typeof(int), typeof(bool) })]
    internal static class LargeLevelPrepVfxOptimizationPatch
    {
        internal static bool LastUsed { get; private set; }
        internal static double LastMilliseconds { get; private set; }
        internal static int LastRelevantEvents { get; private set; }
        internal static int LastIgnoredEvents { get; private set; }
        internal static int LastAllocatedEventBuckets { get; private set; }

        private static bool Prefix(scnGame __instance, int __0, bool __1)
        {
            int seqID = __0;
            bool remakeFloors = __1;

            LastUsed = false;
            LastMilliseconds = 0.0;
            LastRelevantEvents = 0;
            LastIgnoredEvents = 0;
            LastAllocatedEventBuckets = 0;

            List<scrFloor> floors = scrLevelMaker.instance != null ? scrLevelMaker.instance.listFloors : null;
            if (!Main.Enabled || !ADOBase.isLevelEditor || __instance == null || floors == null ||
                floors.Count < LargeLevelRemakeDedupState.MinFloorCount)
            {
                return true;
            }

            long start = Stopwatch.GetTimestamp();
            FastPrepVfx(floors, seqID, __instance.events, remakeFloors);
            LastMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            LastUsed = true;
            return false;
        }

        private static void FastPrepVfx(List<scrFloor> floors, int seqID, List<LevelEvent> events, bool remakeFloors)
        {
            List<LevelEvent>[] relevantEventsByFloor = null;

            if (remakeFloors && events != null)
            {
                relevantEventsByFloor = new List<LevelEvent>[floors.Count];

                for (int i = 0; i < events.Count; i++)
                {
                    LevelEvent levelEvent = events[i];
                    if (!IsPrepEventRelevant(levelEvent))
                    {
                        LastIgnoredEvents++;
                        continue;
                    }

                    LastRelevantEvents++;
                    int floor = levelEvent.floor;
                    List<LevelEvent> bucket = relevantEventsByFloor[floor];
                    if (bucket == null)
                    {
                        bucket = new List<LevelEvent>();
                        relevantEventsByFloor[floor] = bucket;
                        LastAllocatedEventBuckets++;
                    }
                    bucket.Add(levelEvent);
                }
            }

            scrVfxPlus vfx = scrVfxPlus.instance;
            vfx.Reset();
            scrConductor conductor = scrConductor.instance;

            for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
            {
                scrFloor floor = floors[floorIndex];

                ffxChangeTrack changeTrack = floor.GetComponent<ffxChangeTrack>();
                if (changeTrack != null)
                {
                    changeTrack.PrepFloor(remakeFloors);
                }

                floor.startPos = floor.transform.position;
                if (floor.opacityVal != 1f)
                {
                    floor.SetOpacity(floor.opacityVal);
                }

                if (remakeFloors && relevantEventsByFloor != null)
                {
                    List<LevelEvent> relevant = relevantEventsByFloor[floor.seqID];
                    if (relevant != null)
                    {
                        PrepareCustomBackgroundEffects(floor, relevant, conductor);
                    }
                }

                ClearConditionalEffects(floor);

                foreach (ffxPlusBase effect in floor.plusEffects)
                {
                    bool hasConditional = false;
                    bool[] conditionalInfo = effect.conditionalInfo;
                    for (int n = 0; n < conditionalInfo.Length; n++)
                    {
                        if (!conditionalInfo[n]) continue;
                        GetConditionalSet(floor, n).Add(effect);
                        hasConditional = true;
                    }

                    if (hasConditional)
                    {
                        effect.triggered = true;
                        floor.hasConditionalChange = true;
                    }
                    else if (effect.runManually)
                    {
                        effect.triggered = true;
                    }
                    else if (!effect.runOnHit)
                    {
                        vfx.effects.Add(effect);
                    }

                    effect.PrepVfx();
                }
            }

            if (seqID == 0)
            {
                vfx.ScrubToTime(0f);
            }
            else
            {
                scrFloor checkpointFloor = null;
                int last = Math.Min(seqID, floors.Count - 1);
                for (int i = last; i >= 0; i--)
                {
                    scrFloor floor = floors[i];
                    if (floor.onCheckpointEffects.Count > 0)
                    {
                        checkpointFloor = floor;
                        break;
                    }
                }

                if (checkpointFloor)
                {
                    foreach (ffxPlusBase effect in checkpointFloor.onCheckpointEffects)
                    {
                        effect.triggered = false;
                        vfx.effects.Add(effect);
                    }
                }
            }

            vfx.effects = vfx.effects
                .OrderBy(fx => fx.startTime - fx.startEffectOffset)
                .ThenBy(fx => fx.floor.seqID)
                .ToList();
        }

        private static bool IsPrepEventRelevant(LevelEvent levelEvent)
        {
            if (levelEvent.eventType == LevelEventType.RepeatEvents ||
                levelEvent.eventType == LevelEventType.SetConditionalEvents)
            {
                return true;
            }

            if (!levelEvent.ContainsKey("bgImage")) return false;
            return !string.IsNullOrEmpty(Convert.ToString(levelEvent["bgImage"]));
        }

        private static void PrepareCustomBackgroundEffects(
            scrFloor floor,
            List<LevelEvent> relevant,
            scrConductor conductor)
        {
            Dictionary<string, Tuple<int, float>> repeats = null;
            string[] conditionalTags = null;
            bool hasConditionalEvent = false;

            for (int i = 0; i < relevant.Count; i++)
            {
                LevelEvent levelEvent = relevant[i];
                if (levelEvent.eventType != LevelEventType.RepeatEvents) continue;

                if (repeats == null)
                {
                    repeats = new Dictionary<string, Tuple<int, float>>();
                }

                int repetitions = Convert.ToInt32(levelEvent["repetitions"]);
                float interval = levelEvent.GetFloat("interval");
                foreach (string tag in (levelEvent.GetString("tag") ?? "").Split(new[] { ' ' }, StringSplitOptions.None))
                {
                    repeats[tag] = new Tuple<int, float>(repetitions, interval);
                }
            }

            for (int i = 0; i < relevant.Count; i++)
            {
                LevelEvent levelEvent = relevant[i];
                if (levelEvent.eventType != LevelEventType.SetConditionalEvents) continue;

                if (conditionalTags == null)
                {
                    conditionalTags = new string[9];
                }

                for (int k = 0; k < conditionalTags.Length; k++)
                {
                    conditionalTags[k] = levelEvent.GetString(GetConditionalEventTagName(k));
                }
                hasConditionalEvent = true;
            }

            for (int i = 0; i < relevant.Count; i++)
            {
                LevelEvent levelEvent = relevant[i];
                if (!levelEvent.ContainsKey("bgImage") ||
                    string.IsNullOrEmpty(Convert.ToString(levelEvent["bgImage"])))
                {
                    continue;
                }

                int repetitions = 0;
                float repeatInterval = 0f;
                if (repeats != null)
                {
                    foreach (string tag in (levelEvent.GetString("eventTag") ?? "").Split(new[] { ' ' }, StringSplitOptions.None))
                    {
                        Tuple<int, float> repeat;
                        if (repeats.TryGetValue(tag, out repeat))
                        {
                            repetitions = repeat.Item1;
                            repeatInterval = repeat.Item2;
                            break;
                        }
                    }
                }

                for (int repetition = 0; repetition <= repetitions; repetition++)
                {
                    ffxCustomBackgroundPlus effect = floor.gameObject.AddComponent<ffxCustomBackgroundPlus>();
                    effect.SetStartTime(conductor.bpm,
                        levelEvent.GetFloat("angleOffset") + repeatInterval * repetition * 180f);
                    effect.color = Convert.ToString(levelEvent["color"]).HexToColor();
                    effect.filePath = Convert.ToString(levelEvent["bgImage"]);
                    effect.imageColor = Convert.ToString(levelEvent["imageColor"]).HexToColor();
                    Vector2 parallax = (Vector2)levelEvent["parallax"];
                    effect.parallax = levelEvent.info.propertiesInfo["parallax"].CheckIfEnabled(levelEvent, null)
                        ? new Vector2(parallax.x, parallax.y) / 100f
                        : Vector2.one;
                    BgDisplayMode displayMode = (BgDisplayMode)levelEvent["bgDisplayMode"];
                    effect.tiled = displayMode == BgDisplayMode.Tiled;
                    effect.fitScreen = displayMode != BgDisplayMode.Unscaled;
                    effect.scalingRatio = (float)levelEvent.GetInt("scalingRatio") / 100f;
                    effect.looping = levelEvent.GetBool("loopBG");
                    effect.lockRot = levelEvent.GetBool("lockRot");
                    effect.imageSmoothing = levelEvent.GetBool("imageSmoothing");
                    floor.plusEffects.Add(effect);

                    if (hasConditionalEvent)
                    {
                        bool[] conditionalInfo = new bool[9];
                        for (int k = 0; k < conditionalInfo.Length; k++)
                        {
                            string tag = conditionalTags[k];
                            conditionalInfo[k] = !tag.IsNoneConditionalTag() &&
                                                 levelEvent.GetString("eventTag") == tag;
                        }
                        effect.conditionalInfo = conditionalInfo;
                        break;
                    }
                }
            }
        }

        private static string GetConditionalEventTagName(int index)
        {
            switch (index)
            {
                case 0: return "perfectTag";
                case 1: return "earlyPerfectTag";
                case 2: return "latePerfectTag";
                case 3: return "veryEarlyTag";
                case 4: return "veryLateTag";
                case 5: return "tooEarlyTag";
                case 6: return "tooLateTag";
                case 7: return "lossTag";
                case 8: return "onCheckpointTag";
                default: throw new IndexOutOfRangeException();
            }
        }

        private static void ClearConditionalEffects(scrFloor floor)
        {
            floor.perfectEffects.Clear();
            floor.earlyPerfectEffects.Clear();
            floor.latePerfectEffects.Clear();
            floor.veryEarlyEffects.Clear();
            floor.veryLateEffects.Clear();
            floor.tooEarlyEffects.Clear();
            floor.tooLateEffects.Clear();
            floor.lossEffects.Clear();
            floor.onCheckpointEffects.Clear();
        }

        private static HashSet<ffxPlusBase> GetConditionalSet(scrFloor floor, int index)
        {
            switch (index)
            {
                case 0: return floor.perfectEffects;
                case 1: return floor.earlyPerfectEffects;
                case 2: return floor.latePerfectEffects;
                case 3: return floor.veryEarlyEffects;
                case 4: return floor.veryLateEffects;
                case 5: return floor.tooEarlyEffects;
                case 6: return floor.tooLateEffects;
                case 7: return floor.lossEffects;
                case 8: return floor.onCheckpointEffects;
                default: throw new IndexOutOfRangeException();
            }
        }
    }
}
