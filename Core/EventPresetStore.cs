using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADOFAI;
using Newtonsoft.Json;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    [Serializable]
    internal sealed class EventPresetFile
    {
        public List<EventPresetRecord> items = new List<EventPresetRecord>();
    }

    [Serializable]
    internal sealed class EventPresetRecord
    {
        public string name;
        public string eventType;
        public string eventJson;
    }

    internal static class EventPresetStore
    {
        private static string filePath;
        private static EventPresetFile file = new EventPresetFile();

        public static void Initialize(string modPath)
        {
            filePath = Path.Combine(modPath ?? string.Empty, "EventPresets.json");
            Load();
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                File.WriteAllText(filePath, JsonUtility.ToJson(file, true));
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Warning("イベントプリセットを保存できませんでした: " + ex.Message);
            }
        }

        public static IList<KeyValuePair<string, string>> Options()
        {
            if (file.items == null || file.items.Count == 0)
                return new[] { new KeyValuePair<string, string>("__none__", "プリセットなし") };
            return file.items.OrderBy(x => x.name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new KeyValuePair<string, string>(x.name, x.name + "（" +
                    JapaneseLocalization.EventLabel(x.eventType) + "）")).ToList();
        }

        public static string SaveFromSelection(scnEditor editor, LevelEventType eventType, string name, bool includeDecorations)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) throw new ArgumentException("プリセット名を入力してください。");
            FloorRange range = EditorSelection.GetRange(editor, true);
            LevelEvent source = editor.events.FirstOrDefault(x => x.eventType == eventType &&
                x.floor >= range.Start && x.floor <= range.End);
            if (source == null && includeDecorations)
                source = editor.decorations.FirstOrDefault(x => x.eventType == eventType &&
                    x.floor >= range.Start && x.floor <= range.End);
            if (source == null) throw new InvalidOperationException("選択範囲に保存元のイベントがありません。");

            EventPresetRecord record = file.items.FirstOrDefault(x => string.Equals(x.name, name, StringComparison.CurrentCultureIgnoreCase));
            if (record == null)
            {
                record = new EventPresetRecord();
                file.items.Add(record);
            }
            record.name = name;
            record.eventType = source.eventType.ToString();
            record.eventJson = GameVersionCompat.EncodeEventJson(source);
            Save();
            return "イベントプリセット「" + name + "」を保存しました。";
        }

        public static string ApplyToSelection(scnEditor editor, string name, int interval, bool overwriteSolo)
        {
            if (interval < 1) throw new ArgumentOutOfRangeException("interval", "間隔は1以上にしてください。");
            EventPresetRecord record = Find(name);
            Dictionary<string, object> sourceDict = DecodeEventDictionary(record.eventJson);
            LevelEvent prototype = new LevelEvent(sourceDict);
            FloorRange range = EditorSelection.GetRange(editor, true);
            int added = 0;
            int skipped = 0;

            using (new EditorUndoScope(editor))
            {
                for (int floor = range.Start; floor <= range.End; floor += interval)
                {
                    if (floor == 0 && !prototype.info.allowFirstFloorCheck)
                    {
                        skipped++;
                        continue;
                    }
                    if (!EventAdvancedOperations.CanPlace(editor, prototype.eventType, floor, overwriteSolo))
                    {
                        skipped++;
                        continue;
                    }
                    if (overwriteSolo && GameVersionCompat.IsSoloType(prototype.eventType))
                        editor.events.RemoveAll(x => x.floor == floor && x.eventType == prototype.eventType);

                    Dictionary<string, object> dict = DecodeEventDictionary(record.eventJson);
                    LevelEvent copy = new LevelEvent(dict);
                    copy.floor = floor;
                    if (copy.IsDecoration)
                    {
                        if (copy.data.ContainsKey("relativeTo")) copy.data["relativeTo"] = DecPlacementType.Tile;
                        editor.decorations.Add(copy);
                    }
                    else editor.events.Add(copy);
                    added++;
                }

                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (EventOperations.AffectsPath(prototype.eventType)) editor.RemakePath(true, true);
            }

            return "プリセット「" + record.name + "」を" + added + "件適用しました。" +
                   (skipped > 0 ? " " + skipped + "件スキップしました。" : string.Empty);
        }

        public static string Delete(string name)
        {
            EventPresetRecord record = Find(name);
            file.items.Remove(record);
            Save();
            return "プリセット「" + record.name + "」を削除しました。";
        }

        private static Dictionary<string, object> DecodeEventDictionary(string json)
        {
            string text = (json ?? string.Empty).Trim();
            if (!text.StartsWith("{", StringComparison.Ordinal)) text = "{ " + text + " }";
            Dictionary<string, object> result = JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
            if (result == null) throw new InvalidDataException("プリセットのイベントデータを読み取れませんでした。");
            return result;
        }

        private static EventPresetRecord Find(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "__none__")
                throw new InvalidOperationException("プリセットが選択されていません。");
            EventPresetRecord record = file.items.FirstOrDefault(x => string.Equals(x.name, name, StringComparison.CurrentCultureIgnoreCase));
            if (record == null) throw new KeyNotFoundException("プリセットが見つかりません: " + name);
            return record;
        }

        private static void Load()
        {
            file = new EventPresetFile();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            try
            {
                EventPresetFile loaded = JsonUtility.FromJson<EventPresetFile>(File.ReadAllText(filePath));
                if (loaded != null && loaded.items != null) file = loaded;
            }
            catch (Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Warning("イベントプリセットを読み込めませんでした: " + ex.Message);
            }
        }
    }
}
