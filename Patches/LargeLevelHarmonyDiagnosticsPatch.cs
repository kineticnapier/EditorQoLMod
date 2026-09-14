using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    internal static class LargeLevelHarmonyDiagnostics
    {
        [ThreadStatic] private static int remakeDepth;
        [ThreadStatic] private static bool fullRemakeActive;

        internal static int ProbePrefixCalls { get; private set; }
        internal static int LastTargetCount { get; private set; }
        internal static int LastScopeDepth { get; private set; }
        internal static bool LastMainEnabled { get; private set; }
        internal static bool LastApplicationPlaying { get; private set; }

        internal static void EnterRemake(bool remakeLevel)
        {
            remakeDepth++;
            if (remakeDepth != 1 || !remakeLevel) return;

            fullRemakeActive = true;
            ProbePrefixCalls = 0;
            LastTargetCount = 0;
            LastScopeDepth = 0;
            LastMainEnabled = Main.Enabled;
            LastApplicationPlaying = Application.isPlaying;
        }

        internal static void ExitRemake()
        {
            if (remakeDepth <= 0) return;
            if (remakeDepth == 1) fullRemakeActive = false;
            remakeDepth--;
        }

        internal static void RecordInstantiateProbe(scrLevelMaker levelMaker)
        {
            if (!fullRemakeActive) return;

            ProbePrefixCalls++;
            LastScopeDepth = LargeLevelRemakeDedupState.ScopeDepth;
            LastMainEnabled = Main.Enabled;
            LastApplicationPlaying = Application.isPlaying;

            if (levelMaker != null && levelMaker.floorAngles != null)
            {
                LastTargetCount = levelMaker.floorAngles.Length + 1;
            }
        }

        internal static string GetPatchSummary()
        {
            try
            {
                var target = AccessTools.Method(typeof(scrLevelMaker), "InstantiateFloatFloors");
                if (target == null) return "target method が見つからない";

                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) return "PatchInfo なし";

                bool fastPrefixFound = false;
                HashSet<string> owners = new HashSet<string>();
                for (int i = 0; i < info.Prefixes.Count; i++)
                {
                    Patch patch = info.Prefixes[i];
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

    // Runs before normal RemakePath prefixes so each full-load diagnostic starts from a clean slate.
    [HarmonyPatch(typeof(scnEditor), "RemakePath", new[] { typeof(bool), typeof(bool) })]
    internal static class LargeLevelHarmonyDiagnosticRemakePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(bool remakeLevel)
        {
            LargeLevelHarmonyDiagnostics.EnterRemake(remakeLevel);
        }

        private static Exception Finalizer(Exception __exception)
        {
            LargeLevelHarmonyDiagnostics.ExitRemake();
            return __exception;
        }
    }

    // A void, read-only probe at Priority.First. This is deliberately separate from the fast-path
    // bool Prefix so we can tell whether the target method was reached even if another Harmony
    // prefix suppresses the original method before our fast-path prefix gets a chance to run.
    [HarmonyPatch(typeof(scrLevelMaker), "InstantiateFloatFloors")]
    internal static class LargeLevelHarmonyDiagnosticInstantiatePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(scrLevelMaker __instance)
        {
            LargeLevelHarmonyDiagnostics.RecordInstantiateProbe(__instance);
        }
    }
}
