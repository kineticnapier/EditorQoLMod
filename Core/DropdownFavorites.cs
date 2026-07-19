using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    [Serializable]
    internal sealed class DropdownFavoriteData
    {
        public List<DropdownFavoriteGroup> groups = new List<DropdownFavoriteGroup>();
    }

    [Serializable]
    internal sealed class DropdownFavoriteGroup
    {
        public string scope;
        public List<string> values = new List<string>();
    }

    internal static class DropdownFavorites
    {
        private static DropdownFavoriteData data = new DropdownFavoriteData();
        private static string filePath;
        private static string lastScope;
        private static string lastValue;
        private static string lastLabel;

        internal static void Initialize(string modPath)
        {
            filePath = Path.Combine(string.IsNullOrEmpty(modPath) ? "." : modPath, "DropdownFavorites.json");
            Load();
        }

        internal static void TrackSelection(TweakableDropdown dropdown, TweakableDropdownItem item)
        {
            if (!CanManage(dropdown) || item == null || string.IsNullOrEmpty(item.value)) return;
            lastScope = GetScope(dropdown);
            lastValue = item.value;
            lastLabel = string.IsNullOrEmpty(item.localizedValue) ? item.value : item.localizedValue;
        }

        internal static void Apply(TweakableDropdown dropdown)
        {
            if (!CanManage(dropdown) || dropdown.itemValues == null || dropdown.itemValues.Count < 2) return;
            string scope = GetScope(dropdown);
            DropdownFavoriteGroup group = GetGroup(scope, false);
            if (group == null || group.values == null || group.values.Count == 0) return;

            List<string> originalValues = new List<string>(dropdown.itemValues);
            HashSet<string> favoriteSet = new HashSet<string>(group.values, StringComparer.Ordinal);
            List<string> ordered = group.values.Where(originalValues.Contains)
                .Concat(originalValues.Where(x => !favoriteSet.Contains(x))).ToList();
            if (ordered.SequenceEqual(originalValues)) return;

            Dictionary<string, string> labels = null;
            if (dropdown.useCustomLabels && dropdown.customLabels != null &&
                dropdown.customLabels.Count == originalValues.Count)
            {
                labels = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < originalValues.Count; i++) labels[originalValues[i]] = dropdown.customLabels[i];
            }

            dropdown.itemValues.Clear();
            dropdown.itemValues.AddRange(ordered);
            if (labels != null)
            {
                dropdown.customLabels.Clear();
                foreach (string value in ordered) dropdown.customLabels.Add(labels[value]);
            }
        }

        internal static string ToggleLast()
        {
            EnsureLastSelection();
            DropdownFavoriteGroup group = GetGroup(lastScope, true);
            int index = group.values.IndexOf(lastValue);
            bool added;
            if (index >= 0)
            {
                group.values.RemoveAt(index);
                added = false;
            }
            else
            {
                group.values.Insert(0, lastValue);
                added = true;
            }
            Save();
            ReloadScope(lastScope);
            return "「" + lastLabel + "」をお気に入り" + (added ? "に追加" : "から削除") + "しました。";
        }

        internal static string ClearLastScope()
        {
            EnsureLastSelection();
            DropdownFavoriteGroup group = GetGroup(lastScope, false);
            if (group == null || group.values.Count == 0) return "この一覧にはお気に入りがありません。";
            group.values.Clear();
            Save();
            ReloadScope(lastScope);
            return "現在の一覧のお気に入りを解除しました。";
        }

        internal static string CurrentSelectionText()
        {
            if (string.IsNullOrEmpty(lastScope) || string.IsNullOrEmpty(lastValue))
                return "本家エディタのドロップダウンで項目を1つ選択してください。";
            bool favorite = IsFavorite(lastScope, lastValue);
            return "一覧: " + FriendlyScope(lastScope) + "\n項目: " + lastLabel +
                   (favorite ? "  ★お気に入り" : "");
        }

        internal static string CurrentFavoritesText()
        {
            if (string.IsNullOrEmpty(lastScope)) return string.Empty;
            DropdownFavoriteGroup group = GetGroup(lastScope, false);
            if (group == null || group.values.Count == 0) return "お気に入り: なし";
            return "お気に入り: " + string.Join(", ", group.values.ToArray());
        }

        internal static bool IsOwnedByQoL(TweakableDropdown dropdown)
        {
            return dropdown != null && dropdown.gameObject != null &&
                   dropdown.gameObject.name.StartsWith("Editor QoL", StringComparison.Ordinal);
        }

        private static bool CanManage(TweakableDropdown dropdown)
        {
            return dropdown != null && dropdown.gameObject != null && dropdown.gameObject.scene.IsValid() &&
                   !IsOwnedByQoL(dropdown) && dropdown.itemValues != null;
        }

        private static string GetScope(TweakableDropdown dropdown)
        {
            if (!string.IsNullOrWhiteSpace(dropdown.enumTypeString)) return "enum:" + dropdown.enumTypeString;
            Transform parent = dropdown.transform.parent;
            string parentName = parent == null ? "Root" : parent.name;
            return "ui:" + parentName + "/" + dropdown.gameObject.name;
        }

        private static string FriendlyScope(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return "不明";
            int separator = scope.IndexOf(':');
            return separator >= 0 && separator + 1 < scope.Length ? scope.Substring(separator + 1) : scope;
        }

        private static DropdownFavoriteGroup GetGroup(string scope, bool create)
        {
            DropdownFavoriteGroup group = data.groups.FirstOrDefault(x => x != null && x.scope == scope);
            if (group == null && create)
            {
                group = new DropdownFavoriteGroup { scope = scope };
                data.groups.Add(group);
            }
            return group;
        }

        private static bool IsFavorite(string scope, string value)
        {
            DropdownFavoriteGroup group = GetGroup(scope, false);
            return group != null && group.values != null && group.values.Contains(value);
        }

        private static void EnsureLastSelection()
        {
            if (string.IsNullOrEmpty(lastScope) || string.IsNullOrEmpty(lastValue))
                throw new InvalidOperationException("先に本家エディタのドロップダウンで項目を選択してください。");
        }

        private static void ReloadScope(string scope)
        {
            foreach (TweakableDropdown dropdown in Resources.FindObjectsOfTypeAll<TweakableDropdown>())
            {
                if (!CanManage(dropdown) || GetScope(dropdown) != scope) continue;
                dropdown.ReloadList();
                dropdown.Setup();
            }
        }

        private static void Load()
        {
            data = new DropdownFavoriteData();
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    DropdownFavoriteData loaded = JsonUtility.FromJson<DropdownFavoriteData>(File.ReadAllText(filePath));
                    if (loaded != null && loaded.groups != null) data = loaded;
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("お気に入り設定を読み込めませんでした: " + ex.Message);
            }
        }

        internal static void Save()
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                File.WriteAllText(filePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("お気に入り設定を保存できませんでした: " + ex.Message);
            }
        }
    }
}
