using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    [Serializable]
    internal sealed class EditorQoLPreferenceData
    {
        public List<string> favoriteCommands = new List<string>();
        public List<string> recentCommands = new List<string>();
        public List<string> collapsedSections = new List<string>();
    }

    internal static class EditorQoLPreferences
    {
        private const int MaximumFavorites = 5;
        private const int MaximumRecents = 5;
        private static EditorQoLPreferenceData data = new EditorQoLPreferenceData();
        private static string filePath;

        internal static void Initialize(string modPath)
        {
            filePath = Path.Combine(string.IsNullOrEmpty(modPath) ? "." : modPath,
                "EditorQoLPreferences.json");
            Load();
        }

        internal static bool IsFavorite(string commandId)
        {
            return !string.IsNullOrEmpty(commandId) && data.favoriteCommands.Contains(commandId);
        }

        internal static bool ToggleFavorite(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return false;
            int index = data.favoriteCommands.IndexOf(commandId);
            bool added = index < 0;
            if (added)
            {
                data.favoriteCommands.Insert(0, commandId);
                Trim(data.favoriteCommands, MaximumFavorites);
            }
            else
            {
                data.favoriteCommands.RemoveAt(index);
            }
            Save();
            return added;
        }

        internal static void RecordRecent(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return;
            data.recentCommands.Remove(commandId);
            data.recentCommands.Insert(0, commandId);
            Trim(data.recentCommands, MaximumRecents);
            Save();
        }

        internal static IList<string> FavoriteCommandIds()
        {
            return data.favoriteCommands.ToArray();
        }

        internal static IList<string> RecentCommandIds()
        {
            return data.recentCommands.ToArray();
        }

        internal static bool IsSectionCollapsed(string sectionId)
        {
            return !string.IsNullOrEmpty(sectionId) && data.collapsedSections.Contains(sectionId);
        }

        internal static void SetSectionCollapsed(string sectionId, bool collapsed, bool save)
        {
            if (string.IsNullOrEmpty(sectionId)) return;
            if (collapsed)
            {
                if (!data.collapsedSections.Contains(sectionId)) data.collapsedSections.Add(sectionId);
            }
            else
            {
                data.collapsedSections.Remove(sectionId);
            }
            if (save) Save();
        }

        private static void Trim(List<string> values, int maximum)
        {
            while (values.Count > maximum) values.RemoveAt(values.Count - 1);
        }

        private static void Load()
        {
            data = new EditorQoLPreferenceData();
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    EditorQoLPreferenceData loaded =
                        JsonUtility.FromJson<EditorQoLPreferenceData>(File.ReadAllText(filePath));
                    if (loaded != null) data = loaded;
                }
                if (data.favoriteCommands == null) data.favoriteCommands = new List<string>();
                if (data.recentCommands == null) data.recentCommands = new List<string>();
                if (data.collapsedSections == null) data.collapsedSections = new List<string>();
                data.favoriteCommands = data.favoriteCommands.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                data.recentCommands = data.recentCommands.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                data.collapsedSections = data.collapsedSections.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                Trim(data.favoriteCommands, MaximumFavorites);
                Trim(data.recentCommands, MaximumRecents);
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("QoL UI設定を読み込めませんでした: " + ex.Message);
                data = new EditorQoLPreferenceData();
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
                Main.Logger.Warning("QoL UI設定を保存できませんでした: " + ex.Message);
            }
        }
    }
}
