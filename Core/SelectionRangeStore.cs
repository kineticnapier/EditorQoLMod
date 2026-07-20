using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    [Serializable]
    internal sealed class SelectionRangeFile
    {
        public List<SelectionRangeRecord> items = new List<SelectionRangeRecord>();
    }

    [Serializable]
    internal sealed class SelectionRangeRecord
    {
        public string levelPath;
        public string name;
        public int start;
        public int end;
    }

    internal static class SelectionRangeStore
    {
        private static string filePath;
        private static SelectionRangeFile file = new SelectionRangeFile();

        internal static void Initialize(string modPath)
        {
            filePath = Path.Combine(string.IsNullOrEmpty(modPath) ? "." : modPath, "SelectionRanges.json");
            Load();
        }

        internal static IList<KeyValuePair<string, string>> Options()
        {
            string key = CurrentLevelKey();
            List<SelectionRangeRecord> records = file.items
                .Where(x => x != null && x.levelPath == key)
                .OrderBy(x => x.start).ThenBy(x => x.name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (records.Count == 0)
                return new[] { new KeyValuePair<string, string>("__none__", "この譜面の保存範囲なし") };
            return records.Select(x => new KeyValuePair<string, string>(x.name,
                x.name + "（" + x.start + "～" + x.end + "）")).ToList();
        }

        internal static string SaveSelection(scnEditor editor, string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) throw new ArgumentException("範囲名を入力してください。");
            FloorRange range = EditorSelection.GetRange(editor, true);
            string key = CurrentLevelKey();
            SelectionRangeRecord record = file.items.FirstOrDefault(x => x != null && x.levelPath == key &&
                string.Equals(x.name, name, StringComparison.CurrentCultureIgnoreCase));
            if (record == null)
            {
                record = new SelectionRangeRecord { levelPath = key };
                file.items.Add(record);
            }
            record.name = name;
            record.start = range.Start;
            record.end = range.End;
            Save();
            return "選択範囲「" + name + "」を" + range.Start + "～" + range.End + "番として保存しました。";
        }

        internal static string Select(scnEditor editor, string name)
        {
            SelectionRangeRecord record = Find(name);
            if (record.start < 0 || record.end >= editor.floors.Count)
                throw new InvalidOperationException("保存範囲が現在の譜面サイズを超えています。");
            EditorSelection.SelectRange(editor, record.start, record.end);
            return "選択範囲「" + record.name + "」へ移動しました。";
        }

        internal static string Delete(string name)
        {
            SelectionRangeRecord record = Find(name);
            file.items.Remove(record);
            Save();
            return "選択範囲「" + record.name + "」を削除しました。";
        }

        private static SelectionRangeRecord Find(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "__none__")
                throw new InvalidOperationException("選択範囲が選ばれていません。");
            string key = CurrentLevelKey();
            SelectionRangeRecord record = file.items.FirstOrDefault(x => x != null && x.levelPath == key &&
                string.Equals(x.name, name, StringComparison.CurrentCultureIgnoreCase));
            if (record == null) throw new InvalidOperationException("保存した選択範囲が見つかりません。");
            return record;
        }

        private static string CurrentLevelKey()
        {
            FieldInfo field = typeof(ADOBase).GetField("levelPath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            string path = field == null ? null : field.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(path)) return "__unsaved__";
            try { return Path.GetFullPath(path).ToUpperInvariant(); }
            catch { return path.ToUpperInvariant(); }
        }

        private static void Load()
        {
            file = new SelectionRangeFile();
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    SelectionRangeFile loaded = JsonUtility.FromJson<SelectionRangeFile>(File.ReadAllText(filePath));
                    if (loaded != null && loaded.items != null) file = loaded;
                }
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Warning("選択範囲プリセットを読み込めませんでした: " + ex.Message);
            }
        }

        internal static void Save()
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try { File.WriteAllText(filePath, JsonUtility.ToJson(file, true)); }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Warning("選択範囲プリセットを保存できませんでした: " + ex.Message);
            }
        }
    }
}
