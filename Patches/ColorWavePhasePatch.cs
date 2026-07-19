using System.Reflection;
using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Core;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch]
    internal static class ColorWavePhasePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(scrObjectDecoration), "SetFloorColor");
        }

        private static void Postfix(scrObjectDecoration __instance)
        {
            if (!Main.Enabled) return;
            DecorationColorWaveOperations.ApplyRuntimePhase(__instance);
        }
    }
}
