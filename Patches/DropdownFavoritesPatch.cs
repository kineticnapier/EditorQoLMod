using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Core;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(TweakableDropdown), "ReloadList")]
    internal static class DropdownFavoriteReloadPatch
    {
        private static void Prefix(TweakableDropdown __instance, out string __state)
        {
            __state = __instance.selectedItem == null ? null : __instance.selectedItem.value;
            DropdownFavorites.Apply(__instance);
        }

        private static void Postfix(TweakableDropdown __instance, string __state)
        {
            if (string.IsNullOrEmpty(__state) || __instance.items == null) return;
            TweakableDropdownItem match = __instance.items.Find(x => x != null && x.value == __state);
            if (match == null) return;
            __instance.selectedItem = match;
            __instance.Setup();
        }
    }

    [HarmonyPatch(typeof(TweakableDropdown), "SelectItem")]
    internal static class DropdownFavoriteSelectionPatch
    {
        private static void Postfix(TweakableDropdown __instance, TweakableDropdownItem targetItem)
        {
            DropdownFavorites.TrackSelection(__instance, targetItem);
        }
    }
}
