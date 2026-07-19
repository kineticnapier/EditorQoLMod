using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Runtime;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnGame), "Awake")]
    internal static class TutorialBackgroundGameRuntimePatch
    {
        private static void Postfix(scnGame __instance)
        {
            if (!Main.Enabled || __instance == null) return;
            TutorialBackgroundRuntime runtime = __instance.GetComponent<TutorialBackgroundRuntime>();
            if (runtime == null) runtime = __instance.gameObject.AddComponent<TutorialBackgroundRuntime>();
            runtime.Configure(__instance);
        }
    }

    [HarmonyPatch(typeof(scnGame), "SetBackground")]
    internal static class TutorialBackgroundSetBackgroundPatch
    {
        private static void Postfix(scnGame __instance)
        {
            if (!Main.Enabled || __instance == null) return;
            TutorialBackgroundRuntime runtime = __instance.GetComponent<TutorialBackgroundRuntime>();
            if (runtime == null)
            {
                runtime = __instance.gameObject.AddComponent<TutorialBackgroundRuntime>();
                runtime.Configure(__instance);
            }
            runtime.RefreshAfterBackgroundSetup();
        }
    }
}
