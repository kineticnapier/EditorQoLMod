using System.Globalization;
using HarmonyLib;
using Kiner.ADOFAIEditorQoL.UI;
using KineticNapier.ADOFAIWorkbench;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(EditorQoLWorkbenchPane), "BuildView")]
    internal static class LargeLevelInteractionProfilerWorkbenchPatch
    {
        private static void Postfix(ref WorkbenchPaneView __result)
        {
            if (__result == null) return;

            LargeLevelLatestInteractionTimings latest = LargeLevelInteractionProfiler.Latest;

            __result.Spacer(8)
                .Text("大規模譜面 操作プロファイラ", 12f, true)
                .Text("編集 / Undo / Redo / Play の直近処理を分解します。", 9f, false)
                .Text(FormatLine("SaveState", latest.SaveStateMs, 1), 10f, false)
                .Text(FormatLine("└ LevelData.Copy", latest.LevelDataCopyMs,
                    latest.LevelDataCopyCallsInSaveState), 9f, false)
                .Text(FormatLine("RemakePath [events=" + latest.RemakeApplyEvents.ToString() +
                                 ", remake=" + latest.RemakeLevel.ToString() + "]",
                    latest.RemakePathMs, 1), 10f, false)
                .Text(FormatLine("ReloadAssets [force=" + latest.ReloadAssetsForce.ToString() +
                                 ", decs=" + latest.ReloadAssetsDecorations.ToString() + "]",
                    latest.ReloadAssetsMs, 1), 10f, false);

            AppendAction(__result, "Undo", LargeLevelInteractionProfiler.LatestUndo);
            AppendAction(__result, "Redo", LargeLevelInteractionProfiler.LatestRedo);
            AppendAction(__result, "Play", LargeLevelInteractionProfiler.LatestPlay);
            AppendPrepVfxOptimization(__result);

            __result.Text("※ 内訳は包含関係があるため、単純加算しても合計にはなりません。", 9f, false)
                .Text("※ 0.0 ms はまだ未計測の可能性があります。", 9f, false);
        }

        private static void AppendAction(WorkbenchPaneView view, string label, LargeLevelInteractionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                view.Text(label + ": 未計測", 10f, false);
                return;
            }

            view.Text(FormatLine(label + " 合計", snapshot.TotalMs, 1), 10f, true)
                .Text(FormatLine("├ SaveState", snapshot.SaveStateMs, snapshot.SaveStateCalls), 9f, false)
                .Text(FormatLine("├ LevelData.Copy", snapshot.LevelDataCopyMs, snapshot.LevelDataCopyCalls), 9f, false)
                .Text(FormatLine("├ RemakePath", snapshot.RemakePathMs, snapshot.RemakePathCalls), 9f, false)
                .Text(FormatLine("├ ReloadAssets", snapshot.ReloadAssetsMs, snapshot.ReloadAssetsCalls), 9f, false)
                .Text(FormatLine("├ customLevel.Play", snapshot.CustomLevelPlayMs, snapshot.CustomLevelPlayCalls), 9f, false)
                .Text(FormatLine("│ ├ FinishCustomLevelLoading", snapshot.FinishCustomLevelLoadingMs,
                    snapshot.FinishCustomLevelLoadingCalls), 9f, false)
                .Text(FormatLine("│ ├ ApplyEventsToFloors", snapshot.ApplyEventsToFloorsMs,
                    snapshot.ApplyEventsToFloorsCalls), 9f, false)
                .Text(FormatLine("│ └ PrepVfx", snapshot.PrepVfxMs, snapshot.PrepVfxCalls), 9f, false);
        }

        private static void AppendPrepVfxOptimization(WorkbenchPaneView view)
        {
            if (!LargeLevelPrepVfxOptimizationPatch.LastUsed)
            {
                view.Text("PrepVfx高速化: 未使用", 9f, false);
                return;
            }

            string time = LargeLevelPrepVfxOptimizationPatch.LastMilliseconds >= 1000.0
                ? (LargeLevelPrepVfxOptimizationPatch.LastMilliseconds / 1000.0)
                    .ToString("0.000", CultureInfo.InvariantCulture) + " s"
                : LargeLevelPrepVfxOptimizationPatch.LastMilliseconds
                    .ToString("0.0", CultureInfo.InvariantCulture) + " ms";

            view.Text("PrepVfx高速化: 使用 / " + time, 9f, true)
                .Text("└ relevant=" + LargeLevelPrepVfxOptimizationPatch.LastRelevantEvents.ToString("N0", CultureInfo.InvariantCulture) +
                      " / ignored=" + LargeLevelPrepVfxOptimizationPatch.LastIgnoredEvents.ToString("N0", CultureInfo.InvariantCulture) +
                      " / buckets=" + LargeLevelPrepVfxOptimizationPatch.LastAllocatedEventBuckets.ToString("N0", CultureInfo.InvariantCulture),
                    9f, false);
        }

        private static string FormatLine(string label, double milliseconds, int calls)
        {
            string time = milliseconds >= 1000.0
                ? (milliseconds / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s"
                : milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

            if (calls > 1)
            {
                time += " / " + calls.ToString(CultureInfo.InvariantCulture) + " 回";
            }
            else if (calls == 0 && milliseconds == 0.0)
            {
                time += " / 0 回";
            }

            return label + ": " + time;
        }
    }

    [HarmonyPatch(typeof(EditorQoLWorkbenchPane), "HandleAction")]
    internal static class LargeLevelInteractionProfilerClearPatch
    {
        private static void Postfix(string actionId)
        {
            if (actionId == "clear-performance")
            {
                LargeLevelInteractionProfiler.Clear();
            }
        }
    }
}
