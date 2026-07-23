using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Runtime;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnGame), "Awake")]
    internal static class MultiTilePlanetGameRuntimePatch
    {
        private static void Postfix(scnGame __instance)
        {
            if (!Main.Enabled || __instance == null) return;
            MultiTilePlanetRuntime runtime = __instance.GetComponent<MultiTilePlanetRuntime>();
            if (runtime == null)
                runtime = __instance.gameObject.AddComponent<MultiTilePlanetRuntime>();
            runtime.Configure(__instance);
        }
    }
}
