using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal sealed class TileHighlightMarkerRuntime : MonoBehaviour
    {
        private UnityEngine.LineRenderer line;

        private void Awake()
        {
            line = GetComponent<UnityEngine.LineRenderer>();
        }

        private void LateUpdate()
        {
            if (line == null) line = GetComponent<UnityEngine.LineRenderer>();
            if (line != null) line.enabled = scnGame.instance == null;
        }
    }

    internal enum TileHighlightMode
    {
        Event,
        EffectiveBpm,
        SpeedMultiplier
    }

    internal enum TileHighlightComparison
    {
        Exists,
        Equals,
        NotEquals,
        Contains,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual
    }

    internal static class TileHighlightOperations
    {
        internal const string EventExistsProperty = "__event_exists__";
        internal const string ManualValue = "__manual_value__";
        private const string MarkerName = "Editor QoL Tile Highlight";
        private static readonly List<UnityEngine.GameObject> Markers =
            new List<UnityEngine.GameObject>();
        private static UnityEngine.Material markerMaterial;

        internal static IEnumerable<string> EventTypeNames()
        {
            return Enum.GetValues(typeof(LevelEventType)).Cast<LevelEventType>()
                .Where(x => x != LevelEventType.None)
                .Select(x => x.ToString());
        }

        internal static IEnumerable<KeyValuePair<string, string>> PropertyOptions(LevelEventType eventType)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(EventExistsProperty, "イベントが存在する")
            };

            try
            {
                LevelEvent sample = new LevelEvent(0, eventType);
                if (sample.data != null)
                {
                    result.AddRange(sample.data.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new KeyValuePair<string, string>(x, JapaneseLocalization.PropertyLabel(x))));
                }
            }
            catch
            {
                // Hidden or version-specific event types do not always expose metadata.
            }
            return result;
        }

        internal static IEnumerable<KeyValuePair<string, string>> ValueOptions(scnEditor editor,
            LevelEventType eventType, string property, bool includeDecorations)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(ManualValue, "値を手入力")
            };
            if (string.IsNullOrEmpty(property) || property == EventExistsProperty) return result;

            Type enumType = null;
            try
            {
                LevelEvent sample = new LevelEvent(0, eventType);
                object defaultValue;
                if (sample.data != null && sample.data.TryGetValue(property, out defaultValue) &&
                    defaultValue != null)
                {
                    Type type = defaultValue.GetType();
                    if (type.IsEnum)
                    {
                        enumType = type;
                        foreach (string name in Enum.GetNames(type))
                            AddValueOption(result, name, JapaneseLocalization.EnumLabel(type, name));
                    }
                    else if (defaultValue is bool)
                    {
                        AddValueOption(result, "true", "オン（true）");
                        AddValueOption(result, "false", "オフ（false）");
                    }
                }
            }
            catch
            {
                // Existing chart values below are still enough to provide useful candidates.
            }

            if (editor == null) return result;
            IEnumerable<LevelEvent> events = editor.events == null
                ? Enumerable.Empty<LevelEvent>()
                : editor.events.Where(x => x != null && x.eventType == eventType);
            if (includeDecorations && editor.decorations != null)
                events = events.Concat(editor.decorations.Where(x => x != null && x.eventType == eventType));

            foreach (string value in events.Select(x => PropertyText(x, property))
                         .Where(x => x != null)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                         .Take(100))
            {
                string label = enumType == null
                    ? JapaneseLocalization.SplitIdentifier(value)
                    : JapaneseLocalization.EnumLabel(enumType, value);
                AddValueOption(result, value, label);
            }
            return result;
        }

        internal static string Apply(scnEditor editor, TileHighlightMode mode, LevelEventType eventType,
            string property, TileHighlightComparison comparison, string expectedValue,
            bool includeDecorations)
        {
            if (editor == null || editor.floors == null || editor.floors.Count == 0)
                throw new InvalidOperationException("譜面が読み込まれていません。");

            if (mode != TileHighlightMode.Event && comparison == TileHighlightComparison.Exists)
                throw new InvalidOperationException("速度条件では「存在する」以外の比較方法を選んでください。");
            if (RequiresNumber(comparison))
            {
                double ignored;
                if (!TryNumber(expectedValue, out ignored))
                    throw new FormatException("以上・以下などの比較には数値を入力してください。");
            }

            HashSet<int> matchingFloors = mode == TileHighlightMode.Event
                ? FindEventFloors(editor, eventType, property, comparison, expectedValue, includeDecorations)
                : FindSpeedFloors(editor, mode, comparison, expectedValue);

            Clear();
            foreach (int floorNumber in matchingFloors.OrderBy(x => x))
            {
                if (floorNumber < 0 || floorNumber >= editor.floors.Count) continue;
                scrFloor floor = editor.floors[floorNumber];
                if (floor != null) CreateMarker(floor);
            }

            return matchingFloors.Count + "タイルを強調表示しました。譜面データ自体は変更していません。";
        }

        internal static string ClearWithMessage()
        {
            int count = Markers.Count(x => x != null);
            Clear();
            return count == 0 ? "強調表示はありません。" : count + "タイルの強調表示を解除しました。";
        }

        internal static void Clear()
        {
            for (int i = Markers.Count - 1; i >= 0; i--)
                if (Markers[i] != null) UnityEngine.Object.Destroy(Markers[i]);
            Markers.Clear();

            if (markerMaterial != null)
            {
                UnityEngine.Object.Destroy(markerMaterial);
                markerMaterial = null;
            }
        }

        private static HashSet<int> FindEventFloors(scnEditor editor, LevelEventType eventType,
            string property, TileHighlightComparison comparison, string expectedValue,
            bool includeDecorations)
        {
            IEnumerable<LevelEvent> events = editor.events == null
                ? Enumerable.Empty<LevelEvent>()
                : editor.events.Where(x => x != null);
            if (includeDecorations && editor.decorations != null)
                events = events.Concat(editor.decorations.Where(x => x != null));

            return new HashSet<int>(events
                .Where(x => x.eventType == eventType &&
                            MatchesEvent(x, property, comparison, expectedValue))
                .Select(x => x.floor)
                .Where(x => x >= 0 && x < editor.floors.Count));
        }

        private static HashSet<int> FindSpeedFloors(scnEditor editor, TileHighlightMode mode,
            TileHighlightComparison comparison, string expectedValue)
        {
            HashSet<int> result = new HashSet<int>();
            float baseBpm = editor.levelData == null ? 100f : editor.levelData.bpm;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null) continue;
                double value = mode == TileHighlightMode.EffectiveBpm
                    ? baseBpm * floor.speed
                    : floor.speed;
                if (Compare(value, comparison, expectedValue)) result.Add(i);
            }
            return result;
        }

        private static bool MatchesEvent(LevelEvent evnt, string property,
            TileHighlightComparison comparison, string expectedValue)
        {
            if (property == EventExistsProperty || string.IsNullOrEmpty(property)) return true;
            if (evnt.data == null) return false;
            object actual;
            if (!evnt.data.TryGetValue(property, out actual)) return false;
            if (comparison == TileHighlightComparison.Exists) return true;
            return Compare(actual, comparison, expectedValue);
        }

        private static bool Compare(object actual, TileHighlightComparison comparison, string expectedValue)
        {
            if (comparison == TileHighlightComparison.Exists) return true;
            if (actual == null) return false;

            double actualNumber;
            double expectedNumber;
            bool bothNumbers = TryNumber(actual, out actualNumber) &&
                               TryNumber(expectedValue, out expectedNumber);
            if (RequiresNumber(comparison))
            {
                if (!bothNumbers) return false;
                switch (comparison)
                {
                    case TileHighlightComparison.Greater: return actualNumber > expectedNumber;
                    case TileHighlightComparison.GreaterOrEqual: return actualNumber >= expectedNumber;
                    case TileHighlightComparison.Less: return actualNumber < expectedNumber;
                    case TileHighlightComparison.LessOrEqual: return actualNumber <= expectedNumber;
                }
            }

            string actualText = ValueText(actual);
            string expectedText = (expectedValue ?? string.Empty).Trim();
            if (comparison == TileHighlightComparison.Contains)
                return actualText.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0;

            bool equal = bothNumbers
                ? Math.Abs(actualNumber - expectedNumber) <= 0.0000001d
                : string.Equals(actualText.Trim(), expectedText, StringComparison.OrdinalIgnoreCase);
            return comparison == TileHighlightComparison.NotEquals ? !equal : equal;
        }

        private static bool RequiresNumber(TileHighlightComparison comparison)
        {
            return comparison == TileHighlightComparison.Greater ||
                   comparison == TileHighlightComparison.GreaterOrEqual ||
                   comparison == TileHighlightComparison.Less ||
                   comparison == TileHighlightComparison.LessOrEqual;
        }

        private static bool TryNumber(object value, out double number)
        {
            if (value == null)
            {
                number = 0d;
                return false;
            }
            if (value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(number);
            }
            return double.TryParse(ValueText(value), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number);
        }

        private static string PropertyText(LevelEvent evnt, string property)
        {
            if (evnt == null || evnt.data == null) return null;
            object value;
            return evnt.data.TryGetValue(property, out value) && value != null ? ValueText(value) : null;
        }

        private static string ValueText(object value)
        {
            if (value == null) return string.Empty;
            IFormattable formattable = value as IFormattable;
            return formattable == null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        private static void AddValueOption(ICollection<KeyValuePair<string, string>> options,
            string value, string label)
        {
            if (string.IsNullOrEmpty(value) || options.Any(x =>
                    string.Equals(x.Key, value, StringComparison.OrdinalIgnoreCase)))
                return;
            options.Add(new KeyValuePair<string, string>(value,
                string.IsNullOrWhiteSpace(label) ? value : label));
        }

        private static void CreateMarker(scrFloor floor)
        {
            if (markerMaterial == null)
            {
                UnityEngine.Shader shader = UnityEngine.Shader.Find("Sprites/Default");
                if (shader == null) shader = UnityEngine.Shader.Find("Unlit/Color");
                if (shader == null)
                    throw new InvalidOperationException("強調表示用のシェーダーが見つかりません。");
                markerMaterial = new UnityEngine.Material(shader)
                {
                    name = "Editor QoL Tile Highlight Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            UnityEngine.GameObject marker = new UnityEngine.GameObject(MarkerName);
            marker.hideFlags = HideFlags.DontSave;
            marker.layer = floor.gameObject.layer;
            marker.transform.SetParent(floor.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0f, -0.2f);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;

            UnityEngine.LineRenderer line = marker.AddComponent<UnityEngine.LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 40;
            line.startWidth = 0.075f;
            line.endWidth = 0.075f;
            line.startColor = new Color(1f, 0.82f, 0.1f, 0.98f);
            line.endColor = line.startColor;
            line.sharedMaterial = markerMaterial;

            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.66f,
                    Mathf.Sin(angle) * 0.66f, 0f));
            }

            UnityEngine.Renderer floorRenderer = floor.GetComponentInChildren<UnityEngine.Renderer>();
            if (floorRenderer != null)
            {
                line.sortingLayerID = floorRenderer.sortingLayerID;
                line.sortingOrder = floorRenderer.sortingOrder + 100;
            }
            marker.AddComponent<TileHighlightMarkerRuntime>();
            Markers.Add(marker);
        }
    }
}
