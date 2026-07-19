using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ADOFAI;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnEditor), "LoadEditorProperties")]
    internal static class HiddenEventButtonsPatch
    {
        internal static readonly HashSet<LevelEventType> AddedTypes = new HashSet<LevelEventType>();

        private static readonly FieldInfo CategoryTabsField = AccessTools.Field(typeof(scnEditor), "categoryTabs");
        private static readonly HashSet<int> ScheduledEditors = new HashSet<int>();

        private static void Postfix(scnEditor __instance)
        {
            if (!Main.Enabled || __instance == null) return;

            int instanceId = __instance.GetInstanceID();
            if (!ScheduledEditors.Add(instanceId)) return;
            __instance.StartCoroutine(InitializeDeferred(__instance, instanceId));
        }

        private static IEnumerator InitializeDeferred(scnEditor editor, int instanceId)
        {
            yield return null;

            if (editor == null || editor.eventButtons == null ||
                editor.prefab_levelEventButton == null || editor.levelEventsBarButtons == null ||
                GCS.levelEventsInfo == null)
            {
                ScheduledEditors.Remove(instanceId);
                yield break;
            }

            try
            {
                Apply(editor);
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("隠しイベントボタンの追加に失敗しました: " + ex);
            }
            finally
            {
                ScheduledEditors.Remove(instanceId);
            }
        }

        private static void Apply(scnEditor editor)
        {
            LevelEventCategory jankCategory;
            if (!TryResolveCategory("Jank", out jankCategory))
            {
                Main.Logger.Warning("Jankカテゴリが見つからないため、旧式イベントを追加できませんでした。");
                return;
            }

            List<LevelEventButton> target;
            if (!editor.eventButtons.TryGetValue(jankCategory, out target) || target == null)
            {
                target = new List<LevelEventButton>();
                editor.eventButtons[jankCategory] = target;
            }

            HashSet<LevelEventType> visible = new HashSet<LevelEventType>(
                editor.eventButtons.Values
                    .Where(x => x != null)
                    .SelectMany(x => x)
                    .Where(x => x != null)
                    .Select(x => x.type));

            List<LevelEventType> legacy = new List<LevelEventType>();
            foreach (LevelEventInfo info in GCS.levelEventsInfo.Values)
            {
                if (info == null || info.isDecoration || info.pro || info.taroDLC) continue;

                LevelEventType type = RDUtils.ParseEnum<LevelEventType>(info.name, LevelEventType.None);
                if (type == LevelEventType.None) continue;
                if (Array.IndexOf(EditorConstants.settingsTypes, type) >= 0) continue;

                bool explicitlyOmitted = string.Equals(info.name, "ChangeTrack", StringComparison.Ordinal) ||
                                         string.Equals(info.name, "FreeRoamWarning", StringComparison.Ordinal);
                if (explicitlyOmitted || !visible.Contains(type)) legacy.Add(type);
            }

            legacy = legacy.Distinct().OrderBy(x => (int)x).ToList();
            if (legacy.Count == 0) return;

            AddedTypes.Clear();

            foreach (LevelEventType type in legacy)
            {
                LevelEventButton reusable = RemoveAndTakeExisting(editor, target, jankCategory, type);

                if (reusable == null)
                {
                    EnsureFallbackIcon(type);
                    GameObject buttonObject = UnityEngine.Object.Instantiate(
                        editor.prefab_levelEventButton, editor.levelEventsBarButtons);
                    reusable = buttonObject.GetComponent<LevelEventButton>();
                    reusable.Init(type, 0, 0);
                }

                // Do not rebuild or switch the currently visible category here.
                // The stock CategoryTab click will display the Jank list normally.
                reusable.gameObject.SetActive(false);
                target.Add(reusable);
                AddedTypes.Add(type);
            }

            ReindexButtons(target);
            EnsureCategoryTab(editor, jankCategory);

            Main.Logger.Log("Legacy event buttons registered in Jank without category refresh: " +
                string.Join(", ", legacy.Select(x => x.ToString()).ToArray()));
        }

        private static LevelEventButton RemoveAndTakeExisting(scnEditor editor,
            IList<LevelEventButton> target, LevelEventCategory jankCategory, LevelEventType type)
        {
            LevelEventButton reusable = null;

            for (int i = target.Count - 1; i >= 0; i--)
            {
                LevelEventButton button = target[i];
                if (button == null || button.type != type) continue;
                target.RemoveAt(i);
                if (reusable == null) reusable = button;
                else if (reusable != button) UnityEngine.Object.Destroy(button.gameObject);
            }

            foreach (KeyValuePair<LevelEventCategory, List<LevelEventButton>> pair in editor.eventButtons)
            {
                if (pair.Key.Equals(jankCategory)) continue;
                List<LevelEventButton> list = pair.Value;
                if (list == null) continue;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    LevelEventButton button = list[i];
                    if (button == null || button.type != type) continue;
                    list.RemoveAt(i);
                    button.gameObject.SetActive(false);
                    if (reusable == null) reusable = button;
                    else if (reusable != button) UnityEngine.Object.Destroy(button.gameObject);
                }
            }

            return reusable;
        }

        private static void ReindexButtons(IList<LevelEventButton> buttons)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                LevelEventButton button = buttons[i];
                if (button == null) continue;

                button.page = i / 11;
                button.keyCode = i % 11 + 1;

                RectTransform rect = button.GetComponent<RectTransform>();
                if (rect != null)
                {
                    Vector2 position = rect.anchoredPosition;
                    position.x = rect.sizeDelta.x * (i % 11);
                    rect.anchoredPosition = position;
                }
            }
        }

        private static bool TryResolveCategory(string name, out LevelEventCategory category)
        {
            foreach (object value in Enum.GetValues(typeof(LevelEventCategory)))
            {
                LevelEventCategory current = (LevelEventCategory)value;
                if (string.Equals(current.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    category = current;
                    return true;
                }
            }

            category = default(LevelEventCategory);
            return false;
        }

        private static void EnsureCategoryTab(scnEditor editor, LevelEventCategory category)
        {
            List<CategoryTab> tabs = CategoryTabsField == null
                ? null
                : CategoryTabsField.GetValue(editor) as List<CategoryTab>;
            if (tabs == null) return;

            CategoryTab existing = tabs.FirstOrDefault(x => x != null && x.levelEventCategory.Equals(category));
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return;
            }

            if (editor.prefab_eventCategoryTab == null || editor.levelEventsBarCategories == null) return;

            GameObject tabObject = UnityEngine.Object.Instantiate(
                editor.prefab_eventCategoryTab, editor.levelEventsBarCategories);
            RectTransform rect = tabObject.GetComponent<RectTransform>();
            if (rect != null)
            {
                Vector2 position = rect.anchoredPosition;
                position.y = 55f;
                rect.anchoredPosition = position;
            }

            CategoryTab tab = tabObject.GetComponent<CategoryTab>();
            tab.Init(category);
            tabs.Add(tab);
            tabObject.SetActive(true);
        }

        private static void EnsureFallbackIcon(LevelEventType type)
        {
            if (GCS.levelEventIcons == null || GCS.levelEventIcons.ContainsKey(type)) return;
            Sprite fallback = GCS.levelEventIcons.Values.FirstOrDefault(x => x != null);
            if (fallback != null) GCS.levelEventIcons[type] = fallback;
        }
    }

    [HarmonyPatch(typeof(LevelEventButton), "ShowAsSelected")]
    internal static class HiddenEventButtonTooltipPatch
    {
        private static void Postfix(LevelEventButton __instance, bool selected)
        {
            if (!Main.Enabled || !selected || __instance == null ||
                !HiddenEventButtonsPatch.AddedTypes.Contains(__instance.type) ||
                ADOBase.editor == null || ADOBase.editor.eventPickerText == null)
                return;

            ADOBase.editor.eventPickerText.text = "[旧式・非推奨] " +
                JapaneseLocalization.EventLabel(__instance.type.ToString()) +
                "　古い譜面との互換用イベントです。現在の機能と競合する可能性があります。";
        }
    }

    [HarmonyPatch(typeof(CategoryTab), "OnPointerEnter")]
    internal static class JankCategoryTooltipPatch
    {
        private static void Postfix(CategoryTab __instance)
        {
            if (!Main.Enabled || __instance == null || ADOBase.editor == null ||
                ADOBase.editor.categoryText == null) return;
            if (!string.Equals(__instance.levelEventCategory.ToString(), "Jank",
                StringComparison.OrdinalIgnoreCase)) return;

            ADOBase.editor.categoryText.text = "Jank（旧式・非推奨イベント）";
        }
    }

    [HarmonyPatch(typeof(CategoryTab), "SetSelected")]
    internal static class JankCategorySelectedLabelPatch
    {
        private static void Postfix(CategoryTab __instance, bool selected)
        {
            if (!Main.Enabled || !selected || __instance == null || ADOBase.editor == null ||
                ADOBase.editor.categoryText == null) return;
            if (!string.Equals(__instance.levelEventCategory.ToString(), "Jank",
                StringComparison.OrdinalIgnoreCase)) return;

            ADOBase.editor.categoryText.text = "Jank（旧式・非推奨イベント）";
        }
    }
}
