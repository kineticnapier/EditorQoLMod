using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    // scrFloor.Awake creates prefab_topGlow with the floor Transform already supplied as the
    // Instantiate parent, then immediately calls SetParent with that same Transform again.
    // During the large-level fast path, avoid that redundant hierarchy mutation while preserving
    // the original call whenever the parent is not already correct.
    [HarmonyPatch(typeof(scrFloor), "Awake")]
    internal static class LargeLevelFloorAwakeParentPatch
    {
        private static readonly MethodInfo SetParentMethod = AccessTools.Method(
            typeof(Transform), "SetParent", new[] { typeof(Transform) });

        private static readonly MethodInfo ConditionalSetParentMethod = AccessTools.Method(
            typeof(LargeLevelFloorAwakeParentPatch), nameof(SetParentIfNeeded));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (SetParentMethod != null && instruction.Calls(SetParentMethod))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = ConditionalSetParentMethod;
                }
                yield return instruction;
            }
        }

        private static void SetParentIfNeeded(Transform child, Transform parent)
        {
            if (child == null) return;

            bool largeFastPath = Main.Enabled &&
                                 LargeLevelRemakeDedupState.ScopeDepth > 0 &&
                                 LargeLevelFloorCreationDiagnostics.LastFastPathUsed;

            if (!largeFastPath || child.parent != parent)
            {
                child.SetParent(parent);
            }
        }
    }
}
