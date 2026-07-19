using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class DecorationColorWaveOperations
    {
        internal const string PhaseTagPrefix = "qolWavePhaseMs_";

        public static string CreateWave(scnEditor editor, bool backward, float intervalBeats, float duration,
            string primaryColor, string secondaryColor, FloorDecorationColorType colorType,
            float colorAnimationDuration, string easeName)
        {
            if (intervalBeats < 0f) throw new ArgumentOutOfRangeException("intervalBeats");
            if (duration < 0f || colorAnimationDuration < 0f) throw new ArgumentOutOfRangeException("duration");
            if (colorType != FloorDecorationColorType.Single && colorAnimationDuration <= 0f)
                throw new ArgumentOutOfRangeException("colorAnimationDuration", "点滅・発光などでは色アニメーション時間を0より大きくしてください。");

            string primary = NormalizeColor(primaryColor);
            string secondary = NormalizeColor(secondaryColor);
            object ease = ParseEase(easeName);

            List<LevelEvent> selected = GetSelectedFloorDecorations(editor);
            if (selected.Count < 2)
                throw new InvalidOperationException("床型のオブジェクト装飾を2個以上選択してください。");
            if (backward) selected.Reverse();

            int eventFloor = ResolveEventFloor(editor, selected);
            float effectiveBpm = GetEffectiveBpm(editor, eventFloor);
            string groupId = "qolWave_" + DateTime.UtcNow.Ticks.ToString("x");

            using (new EditorUndoScope(editor))
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    LevelEvent decoration = selected[i];
                    string uniqueTag = groupId + "_" + i;
                    float phaseSeconds = i * intervalBeats * 60f / effectiveBpm;
                    AppendTag(decoration, uniqueTag);
                    SetPhaseTag(decoration, phaseSeconds);

                    LevelEvent setObject = new LevelEvent(eventFloor, LevelEventType.SetObject);
                    SetEnabled(setObject, "tag", uniqueTag);
                    // Periodic color modes use a per-decoration runtime phase offset. Single is
                    // still delayed conventionally so it also works when the mod is not loaded.
                    SetEnabled(setObject, "angleOffset",
                        colorType == FloorDecorationColorType.Single ? i * intervalBeats * 180f : 0f);
                    SetEnabled(setObject, "duration", duration);
                    SetEnabled(setObject, "ease", ease);
                    SetEnabled(setObject, "trackColorType", colorType);
                    SetEnabled(setObject, "trackColor", primary);
                    SetEnabled(setObject, "secondaryTrackColor", secondary);
                    SetEnabled(setObject, "trackColorAnimDuration", colorAnimationDuration);
                    editor.events.Add(setObject);
                }

                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
            }

            return eventFloor + "番タイルに、" + selected.Count + "個の床デコレーションへ" +
                   (backward ? "後方" : "前方") + "色ウェーブを作成しました。";
        }

        internal static void ApplyRuntimePhase(scrObjectDecoration decoration)
        {
            if (decoration == null || decoration.floor == null || decoration.sourceLevelEvent == null) return;
            object raw;
            if (!decoration.sourceLevelEvent.data.TryGetValue("tag", out raw) || raw == null) return;

            string[] tags = Convert.ToString(raw).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tag in tags)
            {
                if (!tag.StartsWith(PhaseTagPrefix, StringComparison.Ordinal)) continue;
                int milliseconds;
                if (!int.TryParse(tag.Substring(PhaseTagPrefix.Length), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out milliseconds)) continue;
                decoration.floor.specialTimeOffset = milliseconds / 1000f;
                return;
            }
        }

        private static int ResolveEventFloor(scnEditor editor, List<LevelEvent> selected)
        {
            int lastSelectedFloor = editor.selectedFloorCached;
            if (editor.floors == null || editor.floors.Count == 0)
                throw new InvalidOperationException("譜面が読み込まれていません。");

            lastSelectedFloor = Math.Max(0, Math.Min(lastSelectedFloor, editor.floors.Count - 1));
            int latestDecorationFloor = selected.Where(x => x.floor >= 0).Select(x => x.floor).DefaultIfEmpty(0).Max();
            return Math.Max(lastSelectedFloor, Math.Min(latestDecorationFloor, editor.floors.Count - 1));
        }

        internal static string[] GetEaseNames()
        {
            Type easeType = FindEaseType();
            if (easeType == null || !easeType.IsEnum)
            {
                return new[]
                {
                    "Linear", "InSine", "OutSine", "InOutSine",
                    "InQuad", "OutQuad", "InOutQuad",
                    "InCubic", "OutCubic", "InOutCubic"
                };
            }

            return Enum.GetNames(easeType)
                .Where(name => name != "Unset" && name != "INTERNAL_Zero" && name != "INTERNAL_Custom")
                .ToArray();
        }

        private static object ParseEase(string name)
        {
            Type easeType = FindEaseType();
            if (easeType == null || !easeType.IsEnum)
                throw new InvalidOperationException("DOTween Ease enum was not found at runtime.");

            try
            {
                return Enum.Parse(easeType, name, true);
            }
            catch (Exception)
            {
                throw new FormatException("Unknown ease: " + name);
            }
        }

        private static Type FindEaseType()
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("DG.Tweening.Ease", false);
                if (type != null) return type;
            }
            return null;
        }

        private static List<LevelEvent> GetSelectedFloorDecorations(scnEditor editor)
        {
            if (editor.selectedDecorations == null) return new List<LevelEvent>();
            return editor.selectedDecorations
                .Where(x => x != null && x.eventType == LevelEventType.AddObject && IsFloorObject(x))
                .OrderBy(x => x.floor)
                .ThenBy(x => editor.decorations.IndexOf(x))
                .ToList();
        }

        private static bool IsFloorObject(LevelEvent evnt)
        {
            object value;
            if (!evnt.data.TryGetValue("objectType", out value) || value == null) return false;
            if (value is ObjectDecorationType) return (ObjectDecorationType)value == ObjectDecorationType.Floor;
            ObjectDecorationType parsed;
            return Enum.TryParse(Convert.ToString(value), true, out parsed) && parsed == ObjectDecorationType.Floor;
        }

        private static void AppendTag(LevelEvent decoration, string tag)
        {
            string old = decoration.data.ContainsKey("tag") ? Convert.ToString(decoration.data["tag"]) : string.Empty;
            string[] tags = (old ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!tags.Contains(tag)) decoration.data["tag"] = string.Join(" ", tags.Concat(new[] { tag }).ToArray());
            if (decoration.disabled.ContainsKey("tag")) decoration.disabled["tag"] = false;
        }

        private static void SetPhaseTag(LevelEvent decoration, float seconds)
        {
            string old = decoration.data.ContainsKey("tag") ? Convert.ToString(decoration.data["tag"]) : string.Empty;
            List<string> tags = (old ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !x.StartsWith(PhaseTagPrefix, StringComparison.Ordinal)).ToList();
            int milliseconds = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f));
            tags.Add(PhaseTagPrefix + milliseconds.ToString(CultureInfo.InvariantCulture));
            decoration.data["tag"] = string.Join(" ", tags.ToArray());
            if (decoration.disabled.ContainsKey("tag")) decoration.disabled["tag"] = false;
        }

        private static float GetEffectiveBpm(scnEditor editor, int floor)
        {
            float baseBpm = editor.levelData == null ? 100f : editor.levelData.bpm;
            float speed = 1f;
            if (editor.floors != null && floor >= 0 && floor < editor.floors.Count)
                speed = editor.floors[floor].speed;
            float result = baseBpm * speed;
            return result <= 0f ? baseBpm : result;
        }

        private static string NormalizeColor(string input)
        {
            string text = (input ?? string.Empty).Trim();
            if (!text.StartsWith("#")) text = "#" + text;
            Color color;
            if (!ColorUtility.TryParseHtmlString(text, out color))
                throw new FormatException("色コードが正しくありません: " + input);
            return ColorUtility.ToHtmlStringRGBA(color);
        }

        private static void SetEnabled(LevelEvent evnt, string key, object value)
        {
            evnt.data[key] = value;
            if (evnt.disabled.ContainsKey(key)) evnt.disabled[key] = false;
        }
    }
}
