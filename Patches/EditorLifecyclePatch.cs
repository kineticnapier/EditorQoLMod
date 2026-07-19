using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnEditor), "Start")]
    internal static class EditorStartPatch
    {
        private static void Postfix(scnEditor __instance)
        {
            Core.Pacl2UndoBridge.Cleanup();
            if (Main.Enabled) UI.EditorQoLPanel.Attach(__instance);
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
}
