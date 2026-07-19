using System.Reflection;
using ADOFAI;
using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch]
    internal static class FakeFloorEventSetterPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(LevelEvent), "Item");
        }

        private static void Postfix(LevelEvent __instance, string key)
        {
            if (!Main.Enabled || __instance == null || !__instance.isFake || __instance.IsDecoration) return;
            __instance.ApplyPropertiesToRealEvents();
            if (ADOBase.editor == null) return;

            ADOFAI.PropertyInfo info;
            if (__instance.info != null && __instance.info.propertiesInfo.TryGetValue(key, out info))
            {
                if (info.affectsFloors) ADOBase.editor.ApplyEventsToFloors();
                if (info.affectsPath) ADOBase.editor.RemakePath(true, true);
            }
        }
    }
}
