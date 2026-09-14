using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    internal static class LargeLevelHarmonyDiagnostics
    {
        // Expose diagnostics recorded by the fast Prefix itself. A separate probe Prefix on the
        // same original method proved unreliable in the current Harmony/runtime combination.
        internal static int ProbePrefixCalls
        {
            get { return LargeLevelFloorCreationDiagnostics.LargeFastPrefixCalls; }
        }

        internal static int LastTargetCount
        {
            get { return LargeLevelFloorCreationDiagnostics.LastObservedTargetCount; }
        }

        internal static int LastScopeDepth
        {
            get { return LargeLevelFloorCreationDiagnostics.LastObservedScopeDepth; }
        }

        internal static bool LastMainEnabled
        {
            get { return LargeLevelFloorCreationDiagnostics.LastObservedMainEnabled; }
        }

        internal static bool LastApplicationPlaying
        {
            get { return LargeLevelFloorCreationDiagnostics.LastObservedApplicationPlaying; }
        }

        internal static int TotalFastPrefixCalls
        {
            get { return LargeLevelFloorCreationDiagnostics.FastPrefixCalls; }
        }

        internal static string GetPatchSummary()
        {
            try
            {
                var target = AccessTools.Method(typeof(scrLevelMaker), "InstantiateFloatFloors");
                if (target == null) return "target method が見つからない";

                var info = Harmony.GetPatchInfo(target);
                if (info == null) return "PatchInfo なし";

                bool fastPrefixFound = false;
                HashSet<string> owners = new HashSet<string>();
                for (int i = 0; i < info.Prefixes.Count; i++)
                {
                    var patch = info.Prefixes[i];
                    if (!string.IsNullOrEmpty(patch.owner)) owners.Add(patch.owner);

                    var patchMethod = patch.PatchMethod;
                    if (patchMethod != null && patchMethod.DeclaringType == typeof(LargeLevelFastFloorCreationPatch))
                    {
                        fastPrefixFound = true;
                    }
                }

                string ownerText = owners.Count == 0 ? "なし" : string.Join(", ", new List<string>(owners).ToArray());
                return (fastPrefixFound ? "高速Prefix登録済み" : "高速Prefix未登録") +
                       " / Prefix " + info.Prefixes.Count + " / owners: " + ownerText;
            }
            catch (Exception ex)
            {
                return "PatchInfo取得失敗: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
