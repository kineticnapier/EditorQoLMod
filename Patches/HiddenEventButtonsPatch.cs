using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ADOFAI;
using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(scnEditor), "LoadEditorProperties")]
    internal static class HiddenEventButtonsPatch
    {
        internal static readonly HashSet<LevelEventType> AddedTypes = new HashSet<LevelEventType>();
        // C# enums cannot gain named members at runtime, but an enum variable may
        // still hold an undefined numeric value. Use one as a private virtual
        // category key so the stock editor dictionaries and SetCategory path can
        // be reused without merging these events into a built-in category.
        internal static readonly LevelEventCategory LegacyCategory =
            (LevelEventCategory)(-1000);
        internal const string LegacyCategoryLabel = "Jank（旧式・非推奨イベント）";

        private static readonly FieldInfo CategoryTabsField = AccessTools.Field(typeof(scnEditor), "categoryTabs");
        private static readonly HashSet<int> ScheduledEditors = new HashSet<int>();

        private static void Prefix(scnEditor __instance)
        {
            // LoadEditorProperties finishes by calling SetCategory(currentCategory)
            // before our virtual dictionary entry is restored. Avoid a missing-key
            // exception if the level is reloaded while the virtual tab is selected.
            if (Main.Enabled && __instance != null && IsLegacyCategory(__instance.currentCategory))
                __instance.currentCategory = LevelEventCategory.Favorites;
        }

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
            if (!EnsureLegacyCategoryIcon())
            {
                Main.Logger.Warning("Legacyカテゴリ用のアイコンを用意できないため、旧式イベントを追加できませんでした。");
                return;
            }

            List<LevelEventButton> target;
            if (!editor.eventButtons.TryGetValue(LegacyCategory, out target) || target == null)
            {
                target = new List<LevelEventButton>();
                editor.eventButtons[LegacyCategory] = target;
            }

            HashSet<LevelEventType> visible = new HashSet<LevelEventType>(
                editor.eventButtons.Values
                    .Where(x => x != null)
                    .SelectMany(x => x)
                    .Where(x => x != null)
                    .Select(x => x.type));

            HashSet<LevelEventType> builtInJankTypes = new HashSet<LevelEventType>();
            LevelEventCategory builtInJankCategory;
            List<LevelEventButton> builtInJankButtons;
            if (TryResolveCategory("Jank", out builtInJankCategory) &&
                editor.eventButtons.TryGetValue(builtInJankCategory, out builtInJankButtons) &&
                builtInJankButtons != null)
            {
                builtInJankTypes.UnionWith(builtInJankButtons
                    .Where(x => x != null)
                    .Select(x => x.type));
            }

            List<LevelEventType> legacy = new List<LevelEventType>();
            foreach (LevelEventInfo info in GCS.levelEventsInfo.Values)
            {
                if (info == null || info.isDecoration || info.pro || info.taroDLC) continue;

                LevelEventType type = RDUtils.ParseEnum<LevelEventType>(info.name, LevelEventType.None);
                if (type == LevelEventType.None) continue;
                if (GameVersionCompat.IsSettingsType(type)) continue;

                bool explicitlyOmitted = string.Equals(info.name, "ChangeTrack", StringComparison.Ordinal) ||
                                         string.Equals(info.name, "FreeRoamWarning", StringComparison.Ordinal);
                if (explicitlyOmitted || builtInJankTypes.Contains(type) || !visible.Contains(type))
                    legacy.Add(type);
            }

            legacy = legacy.Distinct().OrderBy(x => (int)x).ToList();
            if (legacy.Count == 0) return;

            AddedTypes.Clear();

            foreach (LevelEventType type in legacy)
            {
                LevelEventButton reusable = RemoveAndTakeExisting(editor, target, LegacyCategory, type);

                if (reusable == null)
                {
                    EnsureFallbackIcon(type);
                    GameObject buttonObject = UnityEngine.Object.Instantiate(
                        editor.prefab_levelEventButton, editor.levelEventsBarButtons);
                    reusable = buttonObject.GetComponent<LevelEventButton>();
                    reusable.Init(type, 0, 0);
                }

                // Do not switch the currently visible category here. The virtual
                // tab still uses the stock SetCategory and ShowEventsPage paths.
                reusable.gameObject.SetActive(false);
                target.Add(reusable);
                AddedTypes.Add(type);
            }

            ReindexButtons(target);
            EnsureCategoryTab(editor, LegacyCategory);
            // Moving the stock Jank buttons empties its original category. Let
            // the editor hide that now-empty tab and keep only the virtual one.
            editor.UpdateCategoryVisibility();

            Main.Logger.Log("Legacy event buttons registered in the virtual category: " +
                string.Join(", ", legacy.Select(x => x.ToString()).ToArray()));
        }

        private static LevelEventButton RemoveAndTakeExisting(scnEditor editor,
            IList<LevelEventButton> target, LevelEventCategory targetCategory, LevelEventType type)
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
                if (pair.Key.Equals(targetCategory)) continue;
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

        private static bool EnsureLegacyCategoryIcon()
        {
            if (GCS.eventCategoryIcons == null) return false;
            if (GCS.eventCategoryIcons.ContainsKey(LegacyCategory)) return true;

            Sprite icon = null;
            LevelEventCategory jankCategory;
            if (TryResolveCategory("Jank", out jankCategory))
                GCS.eventCategoryIcons.TryGetValue(jankCategory, out icon);
            if (icon == null)
                icon = GCS.eventCategoryIcons.Values.FirstOrDefault(x => x != null);
            if (icon == null) return false;

            GCS.eventCategoryIcons[LegacyCategory] = icon;
            return true;
        }

        internal static bool IsLegacyCategory(LevelEventCategory category)
        {
            return category.Equals(LegacyCategory);
        }

        private static void EnsureCategoryTab(scnEditor editor, LevelEventCategory category)
        {
            List<CategoryTab> tabs = CategoryTabsField == null
                ? null
                : CategoryTabsField.GetValue(editor) as List<CategoryTab>;
            if (tabs == null) return;

            CategoryTab tab = tabs.FirstOrDefault(x => x != null && x.levelEventCategory.Equals(category));
            if (tab == null)
            {
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

                tab = tabObject.GetComponent<CategoryTab>();
                if (tab == null)
                {
                    UnityEngine.Object.Destroy(tabObject);
                    return;
                }

                tab.Init(category);
                if (tab.button != null) tab.button.name = "EditorQoL.LegacyCategory";
                tabs.Add(tab);
            }

            tab.gameObject.SetActive(true);
            PlaceVirtualCategoryAtEnd(tab, tabs, editor.levelEventsBarCategories);
        }

        private static void PlaceVirtualCategoryAtEnd(CategoryTab target,
            List<CategoryTab> tabs, RectTransform container)
        {
            if (target == null || tabs == null || container == null) return;

            tabs.Remove(target);
            CategoryTab previous = tabs.LastOrDefault(x => x != null);
            tabs.Add(target);

            if (previous != null)
                target.transform.SetSiblingIndex(previous.transform.GetSiblingIndex() + 1);
            else
                target.transform.SetAsLastSibling();

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            Main.Logger.Log("仮想Legacyカテゴリタブをカテゴリバー末尾へ追加しました。");
        }

        private static void EnsureFallbackIcon(LevelEventType type)
        {
            if (GCS.levelEventIcons == null || GCS.levelEventIcons.ContainsKey(type)) return;
            Sprite fallback = GCS.levelEventIcons.Values.FirstOrDefault(x => x != null);
            if (fallback != null) GCS.levelEventIcons[type] = fallback;
        }
    }

    [HarmonyPatch(typeof(scnEditor), "CycleEventsPage")]
    internal static class VirtualLegacyCategoryCyclePatch
    {
        private static bool Prefix(scnEditor __instance, bool forward, bool moveAllTheWay)
        {
            if (!Main.Enabled || __instance == null || __instance.eventButtons == null)
                return true;

            List<LevelEventButton> legacyButtons;
            if (!__instance.eventButtons.TryGetValue(HiddenEventButtonsPatch.LegacyCategory,
                    out legacyButtons) || legacyButtons == null || legacyButtons.Count == 0)
                return true;

            List<LevelEventCategory> categories = Enum.GetValues(typeof(LevelEventCategory))
                .Cast<LevelEventCategory>()
                .ToList();
            categories.Add(HiddenEventButtonsPatch.LegacyCategory);

            int index = categories.IndexOf(__instance.currentCategory);
            if (index < 0) return true;

            if (moveAllTheWay)
            {
                index = forward ? categories.Count - 1 : 0;
            }
            else
            {
                int direction = forward ? 1 : -1;
                LevelEventCategory candidate;
                do
                {
                    index += direction;
                    if (Persistence.disableEventsPageRepeat)
                        index = Mathf.Clamp(index, 0, categories.Count - 1);
                    else
                        index = (index % categories.Count + categories.Count) % categories.Count;
                    candidate = categories[index];
                }
                while (!HasAvailableButton(__instance, candidate) &&
                       candidate != LevelEventCategory.Favorites);
            }

            __instance.SetCategory(categories[index], false);
            return false;
        }

        private static bool HasAvailableButton(scnEditor editor, LevelEventCategory category)
        {
            List<LevelEventButton> buttons;
            return editor.eventButtons.TryGetValue(category, out buttons) &&
                   buttons != null && buttons.Any(x => x != null &&
                       (!editor.selectedFirstFloor || x.info.allowFirstFloorCheck));
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
    internal static class LegacyCategoryTooltipPatch
    {
        private static bool Prefix(CategoryTab __instance)
        {
            if (!Main.Enabled || __instance == null || ADOBase.editor == null ||
                ADOBase.editor.categoryText == null ||
                !HiddenEventButtonsPatch.IsLegacyCategory(__instance.levelEventCategory))
                return true;

            ADOBase.editor.categoryText.text = HiddenEventButtonsPatch.LegacyCategoryLabel;
            return false;
        }
    }

    [HarmonyPatch(typeof(CategoryTab), "SetSelected")]
    internal static class LegacyCategorySelectedLabelPatch
    {
        private static void Postfix(CategoryTab __instance, bool selected)
        {
            if (!Main.Enabled || !selected || __instance == null || ADOBase.editor == null ||
                ADOBase.editor.categoryText == null) return;
            if (!HiddenEventButtonsPatch.IsLegacyCategory(__instance.levelEventCategory)) return;

            ADOBase.editor.categoryText.text = HiddenEventButtonsPatch.LegacyCategoryLabel;
        }
    }
}
