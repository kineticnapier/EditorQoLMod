using System;
using ADOFAI;
using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Core;
using Kiner.ADOFAIEditorQoL.Runtime;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    /// <summary>
    /// scrDecoration.Setup always calls scrTextDecoration.SetFont for AddText.
    /// Patch SetFont itself so every stock font reset is immediately followed by
    /// the selected custom font. This is safer than relying only on Setup timing.
    /// </summary>
    [HarmonyPatch(typeof(scrTextDecoration), "SetFont")]
    internal static class CustomTextFontSetFontPatch
    {
        private static void Postfix(scrTextDecoration __instance)
        {
            if (!Main.Enabled || __instance == null || __instance.sourceLevelEvent == null ||
                __instance.sourceLevelEvent.eventType != LevelEventType.AddText)
                return;

            LevelEvent ev = __instance.sourceLevelEvent;
            string fontMarker = DecorationMarkerTags.FindTag(ev, DecorationMarkerTags.CustomFontPrefix);
            CustomTextFontRuntime fontRuntime = __instance.GetComponent<CustomTextFontRuntime>();

            if (!string.IsNullOrEmpty(fontMarker))
            {
                string family = DecorationMarkerTags.DecodeFontFamily(fontMarker);
                if (!string.IsNullOrEmpty(family))
                {
                    if (fontRuntime == null)
                        fontRuntime = __instance.gameObject.AddComponent<CustomTextFontRuntime>();
                    fontRuntime.Configure(__instance, family);
                    return;
                }
            }

            if (fontRuntime != null)
                UnityEngine.Object.Destroy(fontRuntime);
        }
    }

    [HarmonyPatch(typeof(scrDecoration), "Setup")]
    internal static class TextDecorationRuntimePatch
    {
        private static void Postfix(scrDecoration __instance, LevelEvent ev)
        {
            if (!Main.Enabled) return;
            scrTextDecoration textDecoration = __instance as scrTextDecoration;
            if (textDecoration == null || ev == null || ev.eventType != LevelEventType.AddText) return;

            string maskMarker = DecorationMarkerTags.FindTag(ev, DecorationMarkerTags.TextMaskPrefix);
            TextMaskRuntime maskRuntime = textDecoration.GetComponent<TextMaskRuntime>();
            if (!string.IsNullOrEmpty(maskMarker))
            {
                if (maskRuntime == null) maskRuntime = textDecoration.gameObject.AddComponent<TextMaskRuntime>();
                maskRuntime.Configure(textDecoration, maskMarker);
            }
            else if (maskRuntime != null)
            {
                UnityEngine.Object.Destroy(maskRuntime);
            }
        }
    }
}
