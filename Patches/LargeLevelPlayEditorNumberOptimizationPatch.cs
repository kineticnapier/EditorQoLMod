namespace Kiner.ADOFAIEditorQoL.Patches
{
    // Intentionally left without Harmony patches.
    // The PR11-07 SetActive(false) experiment did not improve measured Play time. The action-scope
    // profiling repair now lives directly in LargeLevelInteractionProfilerPatch instead.
    internal static class LargeLevelPlayEditorNumberOptimizationPatch
    {
    }
}
