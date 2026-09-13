using System;
using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnEditor), "Start")]
    internal static class EditorStartPatch
    {
        private static void Postfix(scnEditor __instance)
        {
            Core.Pacl2UndoBridge.Cleanup();
            Core.TileHighlightOperations.Clear();
            if (Main.Enabled)
            {
                UI.EditorQoLPanel.Attach(__instance);
                Runtime.MultiTileDecorationRuntime.AttachAllInCurrentScenes();
            }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode", new[] { typeof(bool) })]
    internal static class EditorReturnPatch
    {
        private static void Prefix()
        {
            Kiner.ADOFAIEditorQoL.Runtime.RuntimeEffects.CleanupPlaybackEffects();
        }
    }

    [HarmonyPatch(typeof(scnEditor), "HandleKeyboardActions")]
    internal static class EditorKeyboardPatch
    {
        private static bool Prefix()
        {
            return !UI.EditorQoLPanel.CapturesKeyboard;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "HandleMouseActions")]
    internal static class EditorMousePatch
    {
        private static bool Prefix()
        {
            return !UI.EditorQoLPanel.CapturesMouse;
        }
    }

    [HarmonyPatch(typeof(ADOFAI.InspectorPanel), "ShowPanel")]
    internal static class InspectorShowPanelPatch
    {
        private static void Prefix(ADOFAI.InspectorPanel __instance)
        {
            UI.EditorQoLPanel.HandleRegularInspectorPanel(__instance);
        }
    }
}

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnEditor), "UndoOrRedo")]
    internal static class EditorUndoRedoPatch
    {
        private static void Prefix(out bool __state)
        {
            if (!Main.Enabled)
            {
                __state = false;
                return;
            }
            __state = UI.EditorQoLPanel.IsQoLShowing;
        }

        private static void Postfix(scnEditor __instance, bool __state)
        {
            if (!Main.Enabled) return;
            UI.EditorQoLPanel.RestoreAfterUndo(__instance, __state);
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
        private static void Prefix()
        {
            LargeLevelRemakeDedupState.Enter();
        }

        private static Exception Finalizer(Exception __exception)
        {
            LargeLevelRemakeDedupState.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "DrawHolds", new[] { typeof(bool) })]
    internal static class LargeLevelDuplicateDrawHoldsPatch
    {
        private static bool Prefix(scrLevelMaker __instance)
        {
            if (!LargeLevelRemakeDedupState.ShouldOptimize(__instance)) return true;

            LargeLevelRemakeDedupState.DrawHoldsCalls++;
            return LargeLevelRemakeDedupState.DrawHoldsCalls != 2;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "DrawMultiPlanet", new[] { typeof(bool) })]
    internal static class LargeLevelDuplicateDrawMultiPlanetPatch
    {
        private static bool Prefix(scrLevelMaker __instance, bool forcePlaying, ref int __result)
        {
            if (!LargeLevelRemakeDedupState.ShouldOptimize(__instance)) return true;

            LargeLevelRemakeDedupState.DrawMultiPlanetCalls++;
            if (LargeLevelRemakeDedupState.DrawMultiPlanetCalls == 1)
            {
                LargeLevelRemakeDedupState.FirstMultiPlanetForcePlaying = forcePlaying;
                LargeLevelRemakeDedupState.HasFirstMultiPlanetForcePlaying = true;
                return true;
            }

            if (LargeLevelRemakeDedupState.DrawMultiPlanetCalls != 2 ||
                !LargeLevelRemakeDedupState.HasCachedMaxPlanets ||
                !LargeLevelRemakeDedupState.HasFirstMultiPlanetForcePlaying ||
                LargeLevelRemakeDedupState.FirstMultiPlanetForcePlaying != forcePlaying)
            {
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
    }
}
