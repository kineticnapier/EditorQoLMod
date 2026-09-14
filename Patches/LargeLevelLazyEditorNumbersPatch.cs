using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    // The stock mesh-floor prefab carries an inactive editorNum Canvas/Text hierarchy on every
    // floor. Large charts clone that hierarchy hundreds of thousands of times even when floor
    // numbers are hidden. Build one stripped floor template instead and point all not-yet-
    // materialized floors at one complete hidden dummy editorNum object. The real hierarchy is
    // cloned into a floor only when DrawFloorNums actually needs to show it.
    internal static class LargeLevelLazyEditorNumberState
    {
        private static readonly FieldInfo EditorNumTextField = AccessTools.Field(typeof(scrFloor), "editorNumText");
        private static readonly FieldInfo TopGlowField = AccessTools.Field(typeof(scrFloor), "topGlow");
        private static readonly FieldInfo ShowFloorNumsField = AccessTools.Field(typeof(scnEditor), "showFloorNums");
        private static readonly FieldInfo PlayModeField = AccessTools.Field(typeof(scnEditor), "playMode");
        private static readonly FieldInfo IsFakeField = AccessTools.Field(typeof(scrFloor), "isFake");

        private static GameObject templateHost;
        private static scrFloor sourceFloorPrefab;
        private static scrFloor lightweightFloorTemplate;
        private static GameObject editorNumSource;
        private static Component dummyEditorNum;
        private static FieldInfo letterTextField;
        private static PropertyInfo textProperty;
        private static readonly List<Component> createdEditorNums = new List<Component>();

        internal static bool Active { get; private set; }
        internal static int DeferredFloorCount { get; private set; }
        internal static int CreatedEditorNumCount { get; private set; }
        internal static string LastStatus { get; private set; }

        internal static bool TryPrepare(scrFloor source, int targetCount, out scrFloor spawnTemplate)
        {
            spawnTemplate = source;
            LastStatus = null;

            if (source == null || EditorNumTextField == null)
            {
                Active = false;
                LastStatus = "editorNumText フィールドを取得できない";
                return false;
            }

            if (lightweightFloorTemplate != null && sourceFloorPrefab == source &&
                dummyEditorNum != null && editorNumSource != null)
            {
                Active = true;
                DeferredFloorCount = targetCount;
                CreatedEditorNumCount = CountCreatedEditorNums();
                spawnTemplate = lightweightFloorTemplate;
                LastStatus = "既存の軽量床テンプレートを再利用";
                return true;
            }

            CleanupTemplate();

            try
            {
                Component sourceEditorNum = EditorNumTextField.GetValue(source) as Component;
                if (sourceEditorNum == null)
                {
                    LastStatus = "meshFloor の editorNumText が null";
                    return false;
                }

                editorNumSource = sourceEditorNum.gameObject;
                letterTextField = AccessTools.Field(EditorNumTextField.FieldType, "letterText");
                if (letterTextField != null)
                {
                    textProperty = AccessTools.Property(letterTextField.FieldType, "text");
                }

                templateHost = new GameObject("EditorQoL LargeLevel Floor Templates");
                templateHost.hideFlags = HideFlags.HideAndDontSave;
                templateHost.SetActive(false);

                GameObject dummyObject = UnityEngine.Object.Instantiate(editorNumSource, templateHost.transform, false);
                dummyObject.name = "EditorQoL editorNum dummy";
                dummyObject.SetActive(false);
                dummyEditorNum = dummyObject.GetComponent(EditorNumTextField.FieldType);
                if (dummyEditorNum == null)
                {
                    LastStatus = "editorNum ダミーの Component 取得に失敗";
                    CleanupTemplate();
                    return false;
                }

                // The host is inactive, so this clone can be stripped before it enters the live
                // Floors hierarchy. If a Unity lifecycle callback nevertheless created a topGlow,
                // remove it from the template so each live floor still creates only its own glow.
                lightweightFloorTemplate = UnityEngine.Object.Instantiate(source, templateHost.transform);
                lightweightFloorTemplate.gameObject.name = "EditorQoL Lightweight Floor Template";

                Component clonedEditorNum = EditorNumTextField.GetValue(lightweightFloorTemplate) as Component;
                if (clonedEditorNum != null && clonedEditorNum != dummyEditorNum)
                {
                    UnityEngine.Object.DestroyImmediate(clonedEditorNum.gameObject);
                }
                EditorNumTextField.SetValue(lightweightFloorTemplate, dummyEditorNum);

                StripGeneratedTopGlow(lightweightFloorTemplate);

                sourceFloorPrefab = source;
                Active = true;
                DeferredFloorCount = targetCount;
                CreatedEditorNumCount = 0;
                createdEditorNums.Clear();
                spawnTemplate = lightweightFloorTemplate;
                LastStatus = "editorNum を除外した軽量床テンプレートを使用";
                return true;
            }
            catch (Exception ex)
            {
                LastStatus = "軽量床テンプレート作成失敗: " + ex.GetType().Name + ": " + ex.Message;
                if (Main.Logger != null) Main.Logger.Error(LastStatus + "\n" + ex);
                CleanupTemplate();
                Active = false;
                spawnTemplate = source;
                return false;
            }
        }

        internal static bool HandleDrawFloorNums(scnEditor editor)
        {
            if (!Active || editor == null) return true;

            bool showFloorNums = ReadBool(ShowFloorNumsField, editor, false);
            bool playMode = ReadBool(PlayModeField, editor, false);
            bool shouldShow = showFloorNums && !playMode;

            if (!shouldShow)
            {
                HideCreatedEditorNums();
                return false;
            }

            if (ADOBase.lm == null || ADOBase.lm.listFloors == null)
            {
                return false;
            }

            List<scrFloor> floors = ADOBase.lm.listFloors;
            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                if (floor == null || !floor.enabled) continue;

                if (ReadBool(IsFakeField, floor, false))
                {
                    Component existing = GetEditorNum(floor);
                    if (existing != null && existing != dummyEditorNum)
                    {
                        existing.gameObject.SetActive(false);
                    }
                    continue;
                }

                Component editorNum = EnsureEditorNum(floor);
                if (editorNum == null) continue;
                UpdateEditorNumText(editorNum, floor.seqID);
                editorNum.gameObject.SetActive(true);
            }

            CreatedEditorNumCount = CountCreatedEditorNums();
            return false;
        }

        internal static string GetSummary()
        {
            if (!Active)
            {
                return string.IsNullOrEmpty(LastStatus) ? "未使用" : "未使用（" + LastStatus + "）";
            }

            return "遅延生成 / 実体 " + CreatedEditorNumCount.ToString("N0", CultureInfo.InvariantCulture) +
                   " / 対象 " + DeferredFloorCount.ToString("N0", CultureInfo.InvariantCulture) +
                   "（" + LastStatus + "）";
        }

        internal static void Cleanup()
        {
            Active = false;
            DeferredFloorCount = 0;
            CreatedEditorNumCount = 0;
            createdEditorNums.Clear();
            CleanupTemplate();
            LastStatus = null;
        }

        private static Component EnsureEditorNum(scrFloor floor)
        {
            Component current = GetEditorNum(floor);
            if (current != null && current != dummyEditorNum)
            {
                return current;
            }

            if (editorNumSource == null || EditorNumTextField == null) return null;

            GameObject clone = UnityEngine.Object.Instantiate(editorNumSource, floor.transform, false);
            clone.name = editorNumSource.name;
            clone.SetActive(false);

            Component editorNum = clone.GetComponent(EditorNumTextField.FieldType);
            if (editorNum == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                return null;
            }

            EditorNumTextField.SetValue(floor, editorNum);
            UpdateEditorNumText(editorNum, floor.seqID);
            createdEditorNums.Add(editorNum);
            CreatedEditorNumCount++;
            return editorNum;
        }

        private static Component GetEditorNum(scrFloor floor)
        {
            if (floor == null || EditorNumTextField == null) return null;
            try
            {
                return EditorNumTextField.GetValue(floor) as Component;
            }
            catch
            {
                return null;
            }
        }

        private static void UpdateEditorNumText(Component editorNum, int seqId)
        {
            if (editorNum == null || letterTextField == null || textProperty == null) return;
            try
            {
                object textComponent = letterTextField.GetValue(editorNum);
                if (textComponent != null)
                {
                    textProperty.SetValue(textComponent, seqId.ToString(CultureInfo.InvariantCulture), null);
                }
            }
            catch
            {
                // Displaying the number is optional; keep the floor usable even if a future build
                // changes scrLetterPress internals.
            }
        }

        private static void HideCreatedEditorNums()
        {
            for (int i = createdEditorNums.Count - 1; i >= 0; i--)
            {
                Component editorNum = createdEditorNums[i];
                if (editorNum == null)
                {
                    createdEditorNums.RemoveAt(i);
                    continue;
                }
                editorNum.gameObject.SetActive(false);
            }
            CreatedEditorNumCount = createdEditorNums.Count;
        }

        private static int CountCreatedEditorNums()
        {
            int count = 0;
            for (int i = createdEditorNums.Count - 1; i >= 0; i--)
            {
                if (createdEditorNums[i] == null)
                {
                    createdEditorNums.RemoveAt(i);
                }
                else
                {
                    count++;
                }
            }
            return count;
        }

        private static void StripGeneratedTopGlow(scrFloor template)
        {
            if (template == null) return;

            Transform root = template.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name.StartsWith("topGlow", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            if (TopGlowField != null)
            {
                try { TopGlowField.SetValue(template, null); }
                catch { }
            }
        }

        private static bool ReadBool(FieldInfo field, object target, bool fallback)
        {
            if (field == null || target == null) return fallback;
            try
            {
                object value = field.GetValue(target);
                return value is bool && (bool)value;
            }
            catch
            {
                return fallback;
            }
        }

        private static void CleanupTemplate()
        {
            sourceFloorPrefab = null;
            lightweightFloorTemplate = null;
            editorNumSource = null;
            dummyEditorNum = null;
            letterTextField = null;
            textProperty = null;

            if (templateHost != null)
            {
                try { UnityEngine.Object.DestroyImmediate(templateHost); }
                catch { }
                templateHost = null;
            }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DrawFloorNums")]
    internal static class LargeLevelLazyEditorNumberDrawPatch
    {
        private static bool Prefix(scnEditor __instance)
        {
            return LargeLevelLazyEditorNumberState.HandleDrawFloorNums(__instance);
        }
    }
}
