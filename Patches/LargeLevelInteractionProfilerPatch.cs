using System;
using System.Collections.Generic;
using System.Diagnostics;
using ADOFAI;
using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    internal enum LargeLevelInteractionStage
    {
        CustomLevelPlay,
        FinishCustomLevelLoading,
        ApplyEventsToFloors,
        PrepVfx
    }

    internal sealed class LargeLevelInteractionSnapshot
    {
        internal string ActionName;
        internal double TotalMs;
        internal double SaveStateMs;
        internal double LevelDataCopyMs;
        internal double RemakePathMs;
        internal double ReloadAssetsMs;
        internal double CustomLevelPlayMs;
        internal double FinishCustomLevelLoadingMs;
        internal double ApplyEventsToFloorsMs;
        internal double PrepVfxMs;
        internal int SaveStateCalls;
        internal int LevelDataCopyCalls;
        internal int RemakePathCalls;
        internal int ReloadAssetsCalls;
        internal int CustomLevelPlayCalls;
        internal int FinishCustomLevelLoadingCalls;
        internal int ApplyEventsToFloorsCalls;
        internal int PrepVfxCalls;
    }

    internal sealed class LargeLevelLatestInteractionTimings
    {
        internal double SaveStateMs;
        internal double LevelDataCopyMs;
        internal int LevelDataCopyCallsInSaveState;
        internal double RemakePathMs;
        internal bool RemakeApplyEvents;
        internal bool RemakeLevel;
        internal double ReloadAssetsMs;
        internal bool ReloadAssetsForce;
        internal bool ReloadAssetsDecorations;
    }

    internal static class LargeLevelInteractionProfiler
    {
        private sealed class ActionSession
        {
            internal string Name;
            internal long Start;
            internal long SaveStateTicks;
            internal long LevelDataCopyTicks;
            internal long RemakePathTicks;
            internal long ReloadAssetsTicks;
            internal long CustomLevelPlayTicks;
            internal long FinishCustomLevelLoadingTicks;
            internal long ApplyEventsToFloorsTicks;
            internal long PrepVfxTicks;
            internal int SaveStateCalls;
            internal int LevelDataCopyCalls;
            internal int RemakePathCalls;
            internal int ReloadAssetsCalls;
            internal int CustomLevelPlayCalls;
            internal int FinishCustomLevelLoadingCalls;
            internal int ApplyEventsToFloorsCalls;
            internal int PrepVfxCalls;
        }

        [ThreadStatic] private static ActionSession currentAction;
        [ThreadStatic] private static int actionDepth;
        [ThreadStatic] private static int saveStateDepth;
        [ThreadStatic] private static long saveStateCopyTicks;
        [ThreadStatic] private static int saveStateCopyCalls;

        private static readonly LargeLevelLatestInteractionTimings latest = new LargeLevelLatestInteractionTimings();
        private static LargeLevelInteractionSnapshot latestUndo;
        private static LargeLevelInteractionSnapshot latestRedo;
        private static LargeLevelInteractionSnapshot latestPlay;

        internal static LargeLevelLatestInteractionTimings Latest { get { return latest; } }
        internal static LargeLevelInteractionSnapshot LatestUndo { get { return latestUndo; } }
        internal static LargeLevelInteractionSnapshot LatestRedo { get { return latestRedo; } }
        internal static LargeLevelInteractionSnapshot LatestPlay { get { return latestPlay; } }

        internal static bool IsLargeLevel()
        {
            if (!Main.Enabled) return false;
            try
            {
                return ADOBase.lm != null && ADOBase.lm.listFloors != null &&
                       ADOBase.lm.listFloors.Count >= LargeLevelRemakeDedupState.MinFloorCount;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldMeasureNested()
        {
            // During Undo/Redo the level reference can temporarily change before RemakePath runs.
            // Once a large-level action has begun, keep profiling its nested calls even if the
            // global ADOBase.lm lookup is briefly unavailable.
            return currentAction != null || IsLargeLevel();
        }

        internal static long BeginAction(string name)
        {
            if (!IsLargeLevel()) return 0L;

            actionDepth++;
            if (actionDepth == 1)
            {
                currentAction = new ActionSession
                {
                    Name = name,
                    Start = Stopwatch.GetTimestamp()
                };
            }
            return currentAction == null ? 0L : currentAction.Start;
        }

        internal static void EndAction(string name, long start)
        {
            if (start == 0L || actionDepth <= 0) return;

            bool publish = false;
            if (actionDepth == 1)
            {
                ActionSession session = currentAction;
                currentAction = null;
                if (session != null)
                {
                    LargeLevelInteractionSnapshot snapshot = new LargeLevelInteractionSnapshot
                    {
                        ActionName = session.Name,
                        TotalMs = ToMilliseconds(Stopwatch.GetTimestamp() - session.Start),
                        SaveStateMs = ToMilliseconds(session.SaveStateTicks),
                        LevelDataCopyMs = ToMilliseconds(session.LevelDataCopyTicks),
                        RemakePathMs = ToMilliseconds(session.RemakePathTicks),
                        ReloadAssetsMs = ToMilliseconds(session.ReloadAssetsTicks),
                        CustomLevelPlayMs = ToMilliseconds(session.CustomLevelPlayTicks),
                        FinishCustomLevelLoadingMs = ToMilliseconds(session.FinishCustomLevelLoadingTicks),
                        ApplyEventsToFloorsMs = ToMilliseconds(session.ApplyEventsToFloorsTicks),
                        PrepVfxMs = ToMilliseconds(session.PrepVfxTicks),
                        SaveStateCalls = session.SaveStateCalls,
                        LevelDataCopyCalls = session.LevelDataCopyCalls,
                        RemakePathCalls = session.RemakePathCalls,
                        ReloadAssetsCalls = session.ReloadAssetsCalls,
                        CustomLevelPlayCalls = session.CustomLevelPlayCalls,
                        FinishCustomLevelLoadingCalls = session.FinishCustomLevelLoadingCalls,
                        ApplyEventsToFloorsCalls = session.ApplyEventsToFloorsCalls,
                        PrepVfxCalls = session.PrepVfxCalls
                    };

                    if (string.Equals(name, "Undo", StringComparison.Ordinal)) latestUndo = snapshot;
                    else if (string.Equals(name, "Redo", StringComparison.Ordinal)) latestRedo = snapshot;
                    else if (string.Equals(name, "Play", StringComparison.Ordinal)) latestPlay = snapshot;
                    publish = true;
                }
            }

            actionDepth--;
            if (publish) Publish();
        }

        internal static long BeginSaveState()
        {
            if (!ShouldMeasureNested()) return 0L;

            saveStateDepth++;
            if (saveStateDepth == 1)
            {
                saveStateCopyTicks = 0L;
                saveStateCopyCalls = 0;
            }
            return Stopwatch.GetTimestamp();
        }

        internal static void EndSaveState(long start)
        {
            if (start == 0L) return;

            long ticks = Stopwatch.GetTimestamp() - start;
            if (saveStateDepth == 1)
            {
                latest.SaveStateMs = ToMilliseconds(ticks);
                latest.LevelDataCopyMs = ToMilliseconds(saveStateCopyTicks);
                latest.LevelDataCopyCallsInSaveState = saveStateCopyCalls;
            }

            if (currentAction != null)
            {
                currentAction.SaveStateTicks += ticks;
                currentAction.SaveStateCalls++;
            }

            if (saveStateDepth > 0) saveStateDepth--;
            if (currentAction == null) Publish();
        }

        internal static long BeginLevelDataCopy()
        {
            return ShouldMeasureNested() ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void EndLevelDataCopy(long start)
        {
            if (start == 0L) return;

            long ticks = Stopwatch.GetTimestamp() - start;
            if (saveStateDepth > 0)
            {
                saveStateCopyTicks += ticks;
                saveStateCopyCalls++;
            }

            if (currentAction != null)
            {
                currentAction.LevelDataCopyTicks += ticks;
                currentAction.LevelDataCopyCalls++;
            }
        }

        internal static long BeginRemakePath()
        {
            return ShouldMeasureNested() ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void EndRemakePath(long start, bool applyEventsToFloors, bool remakeLevel)
        {
            if (start == 0L) return;

            long ticks = Stopwatch.GetTimestamp() - start;
            latest.RemakePathMs = ToMilliseconds(ticks);
            latest.RemakeApplyEvents = applyEventsToFloors;
            latest.RemakeLevel = remakeLevel;

            if (currentAction != null)
            {
                currentAction.RemakePathTicks += ticks;
                currentAction.RemakePathCalls++;
            }
            else
            {
                Publish();
            }
        }

        internal static long BeginReloadAssets()
        {
            return ShouldMeasureNested() ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void EndReloadAssets(long start, bool force, bool reloadDecorations)
        {
            if (start == 0L) return;

            long ticks = Stopwatch.GetTimestamp() - start;
            latest.ReloadAssetsMs = ToMilliseconds(ticks);
            latest.ReloadAssetsForce = force;
            latest.ReloadAssetsDecorations = reloadDecorations;

            if (currentAction != null)
            {
                currentAction.ReloadAssetsTicks += ticks;
                currentAction.ReloadAssetsCalls++;
            }
            else
            {
                Publish();
            }
        }

        internal static long BeginActionStage()
        {
            return currentAction == null ? 0L : Stopwatch.GetTimestamp();
        }

        internal static void EndActionStage(LargeLevelInteractionStage stage, long start)
        {
            ActionSession session = currentAction;
            if (session == null || start == 0L) return;

            long ticks = Stopwatch.GetTimestamp() - start;
            switch (stage)
            {
                case LargeLevelInteractionStage.CustomLevelPlay:
                    session.CustomLevelPlayTicks += ticks;
                    session.CustomLevelPlayCalls++;
                    break;
                case LargeLevelInteractionStage.FinishCustomLevelLoading:
                    session.FinishCustomLevelLoadingTicks += ticks;
                    session.FinishCustomLevelLoadingCalls++;
                    break;
                case LargeLevelInteractionStage.ApplyEventsToFloors:
                    session.ApplyEventsToFloorsTicks += ticks;
                    session.ApplyEventsToFloorsCalls++;
                    break;
                case LargeLevelInteractionStage.PrepVfx:
                    session.PrepVfxTicks += ticks;
                    session.PrepVfxCalls++;
                    break;
            }
        }

        internal static void Clear()
        {
            latest.SaveStateMs = 0.0;
            latest.LevelDataCopyMs = 0.0;
            latest.LevelDataCopyCallsInSaveState = 0;
            latest.RemakePathMs = 0.0;
            latest.RemakeApplyEvents = false;
            latest.RemakeLevel = false;
            latest.ReloadAssetsMs = 0.0;
            latest.ReloadAssetsForce = false;
            latest.ReloadAssetsDecorations = false;
            latestUndo = null;
            latestRedo = null;
            latestPlay = null;
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void Publish()
        {
            UI.EditorQoLWorkbenchIntegration.NotifyPerformanceProfileUpdated();
        }
    }

    [HarmonyPatch(typeof(scnEditor), "SaveState", new[] { typeof(bool), typeof(bool) })]
    internal static class LargeLevelProfileSaveStatePatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginSaveState(); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndSaveState(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LevelData), "Copy")]
    internal static class LargeLevelProfileLevelDataCopyPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginLevelDataCopy(); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndLevelDataCopy(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "RemakePath", new[] { typeof(bool), typeof(bool) })]
    internal static class LargeLevelProfileInteractionRemakePatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginRemakePath(); }
        private static Exception Finalizer(long __state, bool applyEventsToFloors, bool remakeLevel, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndRemakePath(__state, applyEventsToFloors, remakeLevel);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "ReloadAssets", new[] { typeof(bool), typeof(bool) })]
    internal static class LargeLevelProfileReloadAssetsPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginReloadAssets(); }
        private static Exception Finalizer(long __state, bool force, bool reloadDecorations, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndReloadAssets(__state, force, reloadDecorations);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "UndoOrRedo", new[] { typeof(bool) })]
    internal static class LargeLevelProfileUndoRedoPatch
    {
        private static void Prefix(bool redo, out long __state)
        {
            __state = LargeLevelInteractionProfiler.BeginAction(redo ? "Redo" : "Undo");
        }
        private static Exception Finalizer(bool redo, long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndAction(redo ? "Redo" : "Undo", __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "Play")]
    internal static class LargeLevelProfilePlayPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginAction("Play"); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndAction("Play", __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "Play", new[] { typeof(int), typeof(bool) })]
    internal static class LargeLevelProfileCustomLevelPlayPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginActionStage(); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndActionStage(LargeLevelInteractionStage.CustomLevelPlay, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "FinishCustomLevelLoading", new[] { typeof(int), typeof(bool) })]
    internal static class LargeLevelProfileFinishCustomLevelLoadingPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginActionStage(); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndActionStage(LargeLevelInteractionStage.FinishCustomLevelLoading, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "ApplyEventsToFloors", new[] { typeof(List<scrFloor>) })]
    internal static class LargeLevelProfileInteractionApplyEventsPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginActionStage(); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndActionStage(LargeLevelInteractionStage.ApplyEventsToFloors, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "PrepVfx", new[] { typeof(int), typeof(bool) })]
    internal static class LargeLevelProfileInteractionPrepVfxPatch
    {
        private static void Prefix(out long __state) { __state = LargeLevelInteractionProfiler.BeginActionStage(); }
        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelInteractionProfiler.EndActionStage(LargeLevelInteractionStage.PrepVfx, __state);
            return __exception;
        }
    }
}
