using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    internal static class LargeLevelHarmonyDiagnostics
    {
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
                MethodInfo floorTarget = AccessTools.Method(typeof(scrLevelMaker), "InstantiateFloatFloors");
                MethodInfo makeLevelTarget = AccessTools.Method(typeof(scrLevelMaker), "MakeLevel");
                if (floorTarget == null) return "InstantiateFloatFloors が見つからない";
                if (makeLevelTarget == null) return "MakeLevel が見つからない";

                var floorInfo = Harmony.GetPatchInfo(floorTarget);
                var makeLevelInfo = Harmony.GetPatchInfo(makeLevelTarget);

                bool fastPrefixFound = false;
                int floorPrefixCount = 0;
                HashSet<string> owners = new HashSet<string>();
                if (floorInfo != null)
                {
                    floorPrefixCount = floorInfo.Prefixes.Count;
                    for (int i = 0; i < floorInfo.Prefixes.Count; i++)
                    {
                        var patch = floorInfo.Prefixes[i];
                        if (!string.IsNullOrEmpty(patch.owner)) owners.Add(patch.owner);
                        MethodInfo patchMethod = patch.PatchMethod;
                        if (patchMethod != null && patchMethod.DeclaringType == typeof(LargeLevelFastFloorCreationPatch))
                        {
                            fastPrefixFound = true;
                        }
                    }
                }

                bool bridgeFound = false;
                if (makeLevelInfo != null)
                {
                    for (int i = 0; i < makeLevelInfo.Transpilers.Count; i++)
                    {
                        var patch = makeLevelInfo.Transpilers[i];
                        if (!string.IsNullOrEmpty(patch.owner)) owners.Add(patch.owner);
                        MethodInfo patchMethod = patch.PatchMethod;
                        if (patchMethod != null && patchMethod.DeclaringType == typeof(LargeLevelMakeLevelBridgePatch))
                        {
                            bridgeFound = true;
                        }
                    }
                }

                string ownerText = owners.Count == 0 ? "なし" : string.Join(", ", new List<string>(owners).ToArray());
                return (bridgeFound ? "MakeLevel bridge登録済み" : "MakeLevel bridge未登録") +
                       " / " + (fastPrefixFound ? "高速Prefix登録済み" : "高速Prefix未登録") +
                       " / Floor Prefix " + floorPrefixCount + " / owners: " + ownerText;
            }
            catch (Exception ex)
            {
                return "PatchInfo取得失敗: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }

    // The runtime currently reports the separate InstantiateFloatFloors fast Prefix as registered,
    // yet the Prefix is not entered while the profiling patch proves MakeLevel still calls floor
    // creation. Route that exact MakeLevel call through one bridge instead of depending on another
    // Prefix on InstantiateFloatFloors. The bridge keeps the game's original method as fallback.
    [HarmonyPatch(typeof(scrLevelMaker), "MakeLevel")]
    internal static class LargeLevelMakeLevelBridgePatch
    {
        private static readonly MethodInfo OriginalInstantiateFloatFloors =
            AccessTools.Method(typeof(scrLevelMaker), "InstantiateFloatFloors");

        private static readonly MethodInfo BridgeMethod =
            AccessTools.Method(typeof(LargeLevelMakeLevelBridgePatch), nameof(InstantiateFloatFloorsBridge));

        private static readonly MethodInfo FastImplementation =
            AccessTools.Method(typeof(LargeLevelFastFloorCreationPatch), "FastInstantiateFloatFloors");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (OriginalInstantiateFloatFloors != null && BridgeMethod != null &&
                    instruction.Calls(OriginalInstantiateFloatFloors))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = BridgeMethod;
                }
                yield return instruction;
            }
        }

        private static void InstantiateFloatFloorsBridge(scrLevelMaker levelMaker)
        {
            LargeLevelFloorCreationDiagnostics.RecordFastPrefix(levelMaker);

            if (levelMaker == null || levelMaker.floorAngles == null || levelMaker.listFloors == null)
            {
                RunOriginal(levelMaker);
                return;
            }

            int targetCount = levelMaker.floorAngles.Length + 1;
            if (targetCount < LargeLevelRemakeDedupState.MinFloorCount)
            {
                RunOriginal(levelMaker);
                return;
            }

            LargeLevelFloorCreationDiagnostics.Prepare(levelMaker.listFloors.Count);

            if (!Main.Enabled)
            {
                LargeLevelFloorCreationDiagnostics.Skip("Editor QoL が無効");
                RunOriginal(levelMaker);
                return;
            }

            if (LargeLevelRemakeDedupState.ScopeDepth <= 0)
            {
                LargeLevelFloorCreationDiagnostics.Skip("scnEditor.RemakePath スコープ外");
                RunOriginal(levelMaker);
                return;
            }

            if (!Application.isPlaying)
            {
                LargeLevelFloorCreationDiagnostics.Skip("Application.isPlaying = false");
                RunOriginal(levelMaker);
                return;
            }

            if (levelMaker.meshFloor == null)
            {
                LargeLevelFloorCreationDiagnostics.Skip("meshFloor が null");
                RunOriginal(levelMaker);
                return;
            }

            scrFloor floorPrefab = levelMaker.meshFloor.GetComponent<scrFloor>();
            if (floorPrefab == null)
            {
                LargeLevelFloorCreationDiagnostics.Skip("meshFloor に scrFloor がない");
                RunOriginal(levelMaker);
                return;
            }

            if (FastImplementation == null)
            {
                LargeLevelFloorCreationDiagnostics.Skip("fast implementation が見つからない");
                RunOriginal(levelMaker);
                return;
            }

            long stageStart = LargeLevelLoadProfiler.BeginStage();
            LargeLevelFloorCreationDiagnostics.BeginFastPath();
            try
            {
                FastImplementation.Invoke(null, new object[] { levelMaker, floorPrefab, targetCount });
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                LargeLevelFloorCreationDiagnostics.Skip("fast implementation 例外: " + inner.GetType().Name);
                throw inner;
            }
            finally
            {
                LargeLevelFloorCreationDiagnostics.EndFastPath();
                LargeLevelLoadProfiler.EndStage(LargeLevelProfileStage.InstantiateFloatFloors, stageStart);
            }
        }

        private static void RunOriginal(scrLevelMaker levelMaker)
        {
            if (levelMaker != null)
            {
                levelMaker.InstantiateFloatFloors();
            }
        }
    }
}
