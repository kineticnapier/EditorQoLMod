using System;
using System.Collections.Generic;
using System.Diagnostics;
using ADOFAI;
using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    internal enum LargeLevelProfileStage
    {
        MakeLevel,
        InstantiateFloatFloors,
        ApplyEventsToFloors,
        ApplyCoreEventsToFloors,
        CalculateFloorEntryTimes,
        DrawHolds,
        DrawMultiPlanet
    }

    internal sealed class LargeLevelPerformanceSnapshot
    {
        internal int FloorCount;
        internal double RemakePathMs;
        internal double MakeLevelMs;
        internal double InstantiateFloatFloorsMs;
        internal double ApplyEventsToFloorsMs;
        internal double ApplyCoreEventsToFloorsMs;
        internal double CalculateFloorEntryTimesMs;
        internal double DrawHoldsMs;
        internal double DrawMultiPlanetMs;
        internal int MakeLevelCalls;
        internal int InstantiateFloatFloorsCalls;
        internal int ApplyEventsToFloorsCalls;
        internal int ApplyCoreEventsToFloorsCalls;
        internal int CalculateFloorEntryTimesCalls;
        internal int DrawHoldsCalls;
        internal int DrawMultiPlanetCalls;
    }

    internal static class LargeLevelLoadProfiler
    {
        private sealed class Session
        {
            internal long RemakeStart;
            internal long MakeLevelTicks;
            internal long InstantiateFloatFloorsTicks;
            internal long ApplyEventsToFloorsTicks;
            internal long ApplyCoreEventsToFloorsTicks;
            internal long CalculateFloorEntryTimesTicks;
            internal long DrawHoldsTicks;
            internal long DrawMultiPlanetTicks;
            internal int MakeLevelCalls;
            internal int InstantiateFloatFloorsCalls;
            internal int ApplyEventsToFloorsCalls;
            internal int ApplyCoreEventsToFloorsCalls;
            internal int CalculateFloorEntryTimesCalls;
            internal int DrawHoldsCalls;
            internal int DrawMultiPlanetCalls;
        }

        [ThreadStatic] private static int remakeDepth;
        [ThreadStatic] private static Session current;
        private static LargeLevelPerformanceSnapshot latest;

        internal static LargeLevelPerformanceSnapshot Latest
        {
            get { return latest; }
        }

        internal static void EnterRemake(bool remakeLevel)
        {
            remakeDepth++;
            if (remakeDepth != 1 || !Main.Enabled || !remakeLevel) return;

            current = new Session();
            current.RemakeStart = Stopwatch.GetTimestamp();
        }

        internal static void ExitRemake(scnEditor editor)
        {
            if (remakeDepth <= 0) return;

            bool publish = false;
            if (remakeDepth == 1)
            {
                Session session = current;
                current = null;
                if (session != null)
                {
                    long remakeTicks = Stopwatch.GetTimestamp() - session.RemakeStart;
                    int floorCount = 0;
                    try
                    {
                        if (editor != null && editor.customLevel != null && editor.customLevel.levelMaker != null &&
                            editor.customLevel.levelMaker.listFloors != null)
                        {
                            floorCount = editor.customLevel.levelMaker.listFloors.Count;
                        }
                    }
                    catch
                    {
                        floorCount = 0;
                    }

                    if (floorCount >= LargeLevelRemakeDedupState.MinFloorCount)
                    {
                        latest = new LargeLevelPerformanceSnapshot
                        {
                            FloorCount = floorCount,
                            RemakePathMs = ToMilliseconds(remakeTicks),
                            MakeLevelMs = ToMilliseconds(session.MakeLevelTicks),
                            InstantiateFloatFloorsMs = ToMilliseconds(session.InstantiateFloatFloorsTicks),
                            ApplyEventsToFloorsMs = ToMilliseconds(session.ApplyEventsToFloorsTicks),
                            ApplyCoreEventsToFloorsMs = ToMilliseconds(session.ApplyCoreEventsToFloorsTicks),
                            CalculateFloorEntryTimesMs = ToMilliseconds(session.CalculateFloorEntryTimesTicks),
                            DrawHoldsMs = ToMilliseconds(session.DrawHoldsTicks),
                            DrawMultiPlanetMs = ToMilliseconds(session.DrawMultiPlanetTicks),
                            MakeLevelCalls = session.MakeLevelCalls,
                            InstantiateFloatFloorsCalls = session.InstantiateFloatFloorsCalls,
                            ApplyEventsToFloorsCalls = session.ApplyEventsToFloorsCalls,
                            ApplyCoreEventsToFloorsCalls = session.ApplyCoreEventsToFloorsCalls,
                            CalculateFloorEntryTimesCalls = session.CalculateFloorEntryTimesCalls,
                            DrawHoldsCalls = session.DrawHoldsCalls,
                            DrawMultiPlanetCalls = session.DrawMultiPlanetCalls
                        };
                        publish = true;
                    }
                }
            }

            remakeDepth--;
            if (publish)
            {
                UI.EditorQoLWorkbenchIntegration.NotifyPerformanceProfileUpdated();
            }
        }

        internal static long BeginStage()
        {
            return current == null ? 0L : Stopwatch.GetTimestamp();
        }

        internal static void EndStage(LargeLevelProfileStage stage, long start)
        {
            Session session = current;
            if (session == null || start == 0L) return;

            long ticks = Stopwatch.GetTimestamp() - start;
            switch (stage)
            {
                case LargeLevelProfileStage.MakeLevel:
                    session.MakeLevelTicks += ticks;
                    session.MakeLevelCalls++;
                    break;
                case LargeLevelProfileStage.InstantiateFloatFloors:
                    session.InstantiateFloatFloorsTicks += ticks;
                    session.InstantiateFloatFloorsCalls++;
                    break;
                case LargeLevelProfileStage.ApplyEventsToFloors:
                    session.ApplyEventsToFloorsTicks += ticks;
                    session.ApplyEventsToFloorsCalls++;
                    break;
                case LargeLevelProfileStage.ApplyCoreEventsToFloors:
                    session.ApplyCoreEventsToFloorsTicks += ticks;
                    session.ApplyCoreEventsToFloorsCalls++;
                    break;
                case LargeLevelProfileStage.CalculateFloorEntryTimes:
                    session.CalculateFloorEntryTimesTicks += ticks;
                    session.CalculateFloorEntryTimesCalls++;
                    break;
                case LargeLevelProfileStage.DrawHolds:
                    session.DrawHoldsTicks += ticks;
                    session.DrawHoldsCalls++;
                    break;
                case LargeLevelProfileStage.DrawMultiPlanet:
                    session.DrawMultiPlanetTicks += ticks;
                    session.DrawMultiPlanetCalls++;
                    break;
            }
        }

        internal static void ClearLatest()
        {
            latest = null;
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }

    // scnGame.RemakePath already redraws holds and multi-planet helpers. scnEditor.RemakePath
    // immediately asks for both again. On very large charts those duplicate full-floor passes are
    // noticeable, while the second multi-planet call can also destroy/recreate the same helpers.
    // Keep the first call and only suppress the known duplicate second call for large levels.
    internal static class LargeLevelRemakeDedupState
    {
        internal const int MinFloorCount = 10000;

        [ThreadStatic] internal static int ScopeDepth;
        [ThreadStatic] internal static bool Optimize;
        [ThreadStatic] internal static int DrawHoldsCalls;
        [ThreadStatic] internal static int DrawMultiPlanetCalls;
        [ThreadStatic] internal static int CachedMaxPlanets;
        [ThreadStatic] internal static bool HasCachedMaxPlanets;
        [ThreadStatic] internal static bool FirstMultiPlanetForcePlaying;
        [ThreadStatic] internal static bool HasFirstMultiPlanetForcePlaying;

        internal static void Enter()
        {
            ScopeDepth++;
            if (ScopeDepth != 1) return;

            Optimize = false;
            DrawHoldsCalls = 0;
            DrawMultiPlanetCalls = 0;
            CachedMaxPlanets = 0;
            HasCachedMaxPlanets = false;
            FirstMultiPlanetForcePlaying = false;
            HasFirstMultiPlanetForcePlaying = false;
        }

        internal static void Exit()
        {
            if (ScopeDepth <= 0) return;
            ScopeDepth--;
            if (ScopeDepth != 0) return;

            Optimize = false;
            DrawHoldsCalls = 0;
            DrawMultiPlanetCalls = 0;
            CachedMaxPlanets = 0;
            HasCachedMaxPlanets = false;
            FirstMultiPlanetForcePlaying = false;
            HasFirstMultiPlanetForcePlaying = false;
        }

        internal static bool ShouldOptimize(scrLevelMaker levelMaker)
        {
            if (!Main.Enabled || ScopeDepth != 1 || levelMaker == null) return false;

            if (!Optimize && levelMaker.listFloors != null && levelMaker.listFloors.Count >= MinFloorCount)
            {
                Optimize = true;
            }

            return Optimize;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "RemakePath", new[] { typeof(bool), typeof(bool) })]
    internal static class LargeLevelEditorRemakeScopePatch
    {
        private static void Prefix(bool remakeLevel)
        {
            LargeLevelRemakeDedupState.Enter();
            LargeLevelLoadProfiler.EnterRemake(remakeLevel);
        }

        private static Exception Finalizer(scnEditor __instance, Exception __exception)
        {
            LargeLevelLoadProfiler.ExitRemake(__instance);
            LargeLevelRemakeDedupState.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "MakeLevel")]
    internal static class LargeLevelProfileMakeLevelPatch
    {
        private static void Prefix(out long __state)
        {
            __state = LargeLevelLoadProfiler.BeginStage();
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.MakeLevel, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "InstantiateFloatFloors")]
    internal static class LargeLevelProfileInstantiateFloorsPatch
    {
        private static void Prefix(out long __state)
        {
            __state = LargeLevelLoadProfiler.BeginStage();
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.InstantiateFloatFloors, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "ApplyEventsToFloors", new[] { typeof(List<scrFloor>) })]
    internal static class LargeLevelProfileApplyEventsPatch
    {
        private static void Prefix(out long __state)
        {
            __state = LargeLevelLoadProfiler.BeginStage();
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.ApplyEventsToFloors, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scnGame), "ApplyCoreEventsToFloors", new[]
    {
        typeof(List<scrFloor>), typeof(LevelData), typeof(scrLevelMaker), typeof(List<LevelEvent>), typeof(List<LevelEvent>[])
    })]
    internal static class LargeLevelProfileApplyCoreEventsPatch
    {
        private static void Prefix(out long __state)
        {
            __state = LargeLevelLoadProfiler.BeginStage();
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.ApplyCoreEventsToFloors, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "CalculateFloorEntryTimes")]
    internal static class LargeLevelProfileFloorEntryTimesPatch
    {
        private static void Prefix(out long __state)
        {
            __state = LargeLevelLoadProfiler.BeginStage();
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.CalculateFloorEntryTimes, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "DrawHolds", new[] { typeof(bool) })]
    internal static class LargeLevelDuplicateDrawHoldsPatch
    {
        private static bool Prefix(scrLevelMaker __instance, out long __state)
        {
            __state = 0L;
            if (!LargeLevelRemakeDedupState.ShouldOptimize(__instance))
            {
                __state = LargeLevelLoadProfiler.BeginStage();
                return true;
            }

            LargeLevelRemakeDedupState.DrawHoldsCalls++;
            if (LargeLevelRemakeDedupState.DrawHoldsCalls == 2) return false;

            __state = LargeLevelLoadProfiler.BeginStage();
            return true;
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.DrawHolds, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "DrawMultiPlanet", new[] { typeof(bool) })]
    internal static class LargeLevelDuplicateDrawMultiPlanetPatch
    {
        private static bool Prefix(scrLevelMaker __instance, bool forcePlaying, ref int __result, out long __state)
        {
            __state = 0L;
            if (!LargeLevelRemakeDedupState.ShouldOptimize(__instance))
            {
                __state = LargeLevelLoadProfiler.BeginStage();
                return true;
            }

            LargeLevelRemakeDedupState.DrawMultiPlanetCalls++;
            if (LargeLevelRemakeDedupState.DrawMultiPlanetCalls == 1)
            {
                LargeLevelRemakeDedupState.FirstMultiPlanetForcePlaying = forcePlaying;
                LargeLevelRemakeDedupState.HasFirstMultiPlanetForcePlaying = true;
                __state = LargeLevelLoadProfiler.BeginStage();
                return true;
            }

            if (LargeLevelRemakeDedupState.DrawMultiPlanetCalls != 2 ||
                !LargeLevelRemakeDedupState.HasCachedMaxPlanets ||
                !LargeLevelRemakeDedupState.HasFirstMultiPlanetForcePlaying ||
                LargeLevelRemakeDedupState.FirstMultiPlanetForcePlaying != forcePlaying)
            {
                __state = LargeLevelLoadProfiler.BeginStage();
                return true;
            }

            // scnEditor.DrawMultiPlanet uses the return value for its warning popup, so preserve it.
            __result = LargeLevelRemakeDedupState.CachedMaxPlanets;
            return false;
        }

        private static void Postfix(scrLevelMaker __instance, int __result)
        {
            if (!LargeLevelRemakeDedupState.ShouldOptimize(__instance)) return;
            if (LargeLevelRemakeDedupState.DrawMultiPlanetCalls != 1) return;

            LargeLevelRemakeDedupState.CachedMaxPlanets = __result;
            LargeLevelRemakeDedupState.HasCachedMaxPlanets = true;
        }

        private static Exception Finalizer(long __state, Exception __exception)
        {
            LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.DrawMultiPlanet, __state);
            return __exception;
        }
    }
}
