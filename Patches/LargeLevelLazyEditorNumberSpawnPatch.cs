using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    // Keep the proven large-level InstantiateFloatFloors replacement untouched. Intercept only
    // its spawn helper and replace the source prefab with the stripped editorNum-free template.
    [HarmonyPatch(typeof(LargeLevelFloorCreationDiagnostics), nameof(LargeLevelFloorCreationDiagnostics.Spawn))]
    internal static class LargeLevelLazyEditorNumberSpawnPatch
    {
        private static scrFloor lastSource;
        private static scrFloor lastTemplate;
        private static bool logged;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref scrFloor prefab)
        {
            if (!Main.Enabled || !LargeLevelFloorCreationDiagnostics.LastFastPathUsed ||
                LargeLevelRemakeDedupState.ScopeDepth <= 0 || prefab == null)
            {
                return;
            }

            if (lastSource == prefab && lastTemplate != null)
            {
                prefab = lastTemplate;
                return;
            }

            int targetCount = LargeLevelFloorCreationDiagnostics.LastObservedTargetCount;
            if (targetCount < LargeLevelRemakeDedupState.MinFloorCount)
            {
                return;
            }

            scrFloor source = prefab;
            scrFloor template;
            if (!LargeLevelLazyEditorNumberState.TryPrepare(source, targetCount, out template) ||
                template == null || template == source)
            {
                return;
            }

            lastSource = source;
            lastTemplate = template;
            prefab = template;

            if (!logged && Main.Logger != null)
            {
                logged = true;
                Main.Logger.Log("Large-level floor-number UI lazy cloning enabled: " +
                                LargeLevelLazyEditorNumberState.GetSummary());
            }
        }
    }
}
