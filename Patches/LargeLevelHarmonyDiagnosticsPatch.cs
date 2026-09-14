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
        private static string explicitBridgeStatus = "未試行";

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

        internal static string ExplicitBridgeStatus
        {
            get { return explicitBridgeStatus; }
        }

        internal static void EnsureExplicitBridge(Harmony harmony)
        {
            try
            {
                if (harmony == null)
                {
                    explicitBridgeStatus = "Harmony instance が null";
                    return;
                }

                MethodInfo target = AccessTools.Method(typeof(scrLevelMaker), "MakeLevel");
                MethodInfo transpiler = AccessTools.Method(typeof(LargeLevelMakeLevelBridgePatch), "Transpiler");
                if (target == null)
                {
                    explicitBridgeStatus = "MakeLevel target が見つからない";
                    return;
                }
                if (transpiler == null)
                {
                    explicitBridgeStatus = "bridge transpiler が見つからない";
                    return;
                }

                if (IsBridgeInstalled(target))
                {
                    explicitBridgeStatus = "既に登録済み";
                    return;
                }

                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                explicitBridgeStatus = IsBridgeInstalled(target) ? "明示登録成功" : "Patch後も未登録";

                if (Main.Logger != null)
                {
                    Main.Logger.Log("Large-level MakeLevel bridge: " + explicitBridgeStatus);
                }
            }
            catch (Exception ex)
            {
                explicitBridgeStatus = "明示登録失敗: " + ex.GetType().Name + ": " + ex.Message;
                if (Main.Logger != null)
                {
                    Main.Logger.Error("Large-level MakeLevel bridge install failed: " + ex);
                }
            }
        }

        private static bool IsBridgeInstalled(MethodInfo target)
        {
            var info = Harmony.GetPatchInfo(target);
            if (info == null) return false;

            for (int i = 0; i < info.Transpilers.Count; i++)
            {
                var patch = info.Transpilers[i];
                MethodInfo patchMethod = patch.PatchMethod;
                if (patchMethod != null && patchMethod.DeclaringType == typeof(LargeLevelMakeLevelBridgePatch))
                {
                    return true;
                }
            }
            return false;
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

                bool bridgeFound = IsBridgeInstalled(makeLevelTarget);
                if (makeLevelInfo != null)
                {
                    for (int i = 0; i < makeLevelInfo.Transpilers.Count; i++)
                    {
                        var patch = makeLevelInfo.Transpilers[i];
                        if (!string.IsNullOrEmpty(patch.owner)) owners.Add(patch.owner);
                    }
                }

                string ownerText = owners.Count == 0 ? "なし" : string.Join(", ", new List<string>(owners).ToArray());
                return (bridgeFound ? "MakeLevel bridge登録済み" : "MakeLevel bridge未登録") +
                       " / " + (fastPrefixFound ? "高速Prefix登録済み" : "高速Prefix未登録") +
                       " / Floor Prefix " + floorPrefixCount + " / owners: " + ownerText +
                       " / explicit: " + explicitBridgeStatus;
            }
            catch (Exception ex)
            {
                return "PatchInfo取得失敗: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }

    // This experimental bridge is installed explicitly after PatchAll. Keeping the class free of
    // HarmonyPatch attributes prevents PatchAll failures from taking down the entire mod; any
    // bridge-install failure is caught and reported through ExplicitBridgeStatus instead.
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
