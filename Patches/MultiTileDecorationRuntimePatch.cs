using ADOFAI;
using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Runtime;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scrDecoration), "Setup")]
    internal static class MultiTileDecorationRuntimePatch
    {
        private static void Postfix(scrDecoration __instance, LevelEvent ev)
        {
            if (__instance == null) return;
            MultiTileDecorationRuntime runtime =
                __instance.GetComponent<MultiTileDecorationRuntime>();

            if (Main.Enabled && MultiTileDecorationRuntime.IsManagedFloor(ev))
            {
                if (runtime == null)
                    runtime = __instance.gameObject.AddComponent<MultiTileDecorationRuntime>();
                runtime.Configure(__instance);
            }
            else if (runtime != null)
            {
                Object.Destroy(runtime);
            }
        }
    }
}
