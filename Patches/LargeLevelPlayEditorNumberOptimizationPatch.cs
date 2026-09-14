using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    // Large-level lazy editor numbers point all not-yet-materialized floors at one hidden dummy
    // editorNum object. scnEditor.Play still loops every floor and calls SetActive(false), which
    // means the same already-hidden dummy can receive hundreds of thousands of redundant native
    // SetActive calls. Replace SetActive calls inside Play with a tiny wrapper that preserves every
    // normal call, but skips that one known no-op case.
    [HarmonyPatch(typeof(scnEditor), "Play")]
    internal static class LargeLevelPlayEditorNumberOptimizationPatch
    {
        private static readonly FieldInfo DummyEditorNumField =
            AccessTools.Field(typeof(LargeLevelLazyEditorNumberState), "dummyEditorNum");

        [System.ThreadStatic] private static int currentSkippedCalls;

        internal static int LastSkippedCalls { get; private set; }

        private static void Prefix()
        {
            currentSkippedCalls = 0;
        }

        private static void Postfix()
        {
            LastSkippedCalls = currentSkippedCalls;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setActive = AccessTools.Method(typeof(GameObject), "SetActive", new[] { typeof(bool) });
            MethodInfo replacement = AccessTools.Method(typeof(LargeLevelPlayEditorNumberOptimizationPatch),
                "SetActiveOptimized");

            foreach (CodeInstruction instruction in instructions)
            {
                if (setActive != null && replacement != null && instruction.Calls(setActive))
                {
                    // Instance SetActive(bool) has [GameObject, bool] on the evaluation stack.
                    // The static replacement takes the same pair, so no stack rewrite is needed.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }

                yield return instruction;
            }
        }

        private static void SetActiveOptimized(GameObject gameObject, bool active)
        {
            if (!active && LargeLevelLazyEditorNumberState.Active && DummyEditorNumField != null)
            {
                Component dummy = DummyEditorNumField.GetValue(null) as Component;
                if (dummy != null && gameObject == dummy.gameObject)
                {
                    currentSkippedCalls++;
                    return;
                }
            }

            // Preserve stock behavior for every other SetActive call in scnEditor.Play.
            gameObject.SetActive(active);
        }
    }
}
