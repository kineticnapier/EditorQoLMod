using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class EventInterpolationOperations
    {
        public static List<string> GetPropertyNames(LevelEventType eventType)
        {
            LevelEvent sample = new LevelEvent(0, eventType);
            return sample.data.Keys.Where(key => key != "floor" && IsSupported(sample, key))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Apply(scnEditor editor, LevelEventType eventType, string property,
            string startText, string endText, bool includeDecorations)
        {
            if (string.IsNullOrWhiteSpace(property) || property == "__none__")
                throw new ArgumentException("補間する項目が選択されていません。");
            FloorRange range = EditorSelection.GetRange(editor, true);
            List<LevelEvent> matches = editor.events.Where(x => x.eventType == eventType &&
                x.floor >= range.Start && x.floor <= range.End).ToList();
            if (includeDecorations)
                matches.AddRange(editor.decorations.Where(x => x.eventType == eventType &&
                    x.floor >= range.Start && x.floor <= range.End));
            matches = matches.OrderBy(x => x.floor).ThenBy(x => SourceIndex(editor, x)).ToList();
            if (matches.Count == 0) return "対象イベントがありません。";
            if (!IsSupported(matches[0], property)) throw new InvalidOperationException("この項目は補間に対応していません。");

            PropertyInfo info = matches[0].info.propertiesInfo[property];
            object start = ParseValue(info, matches[0].data[property], startText);
            object end = ParseValue(info, matches[0].data[property], endText);

            using (new EditorUndoScope(editor))
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    float t = matches.Count <= 1 ? 0f : (float)i / (matches.Count - 1);
                    matches[i].data[property] = ValidateValue(info, LerpValue(info, start, end, t));
                    if (matches[i].disabled.ContainsKey(property)) matches[i].disabled[property] = false;
                }
                if (info.affectsFloors) editor.ApplyEventsToFloors();
                if (matches.Any(x => x.IsDecoration)) editor.UpdateDecorationObjects();
                if (info.affectsPath || EventOperations.AffectsPath(eventType)) editor.RemakePath(true, true);
            }

            return JapaneseLocalization.EventLabel(eventType.ToString()) + "の" +
                   JapaneseLocalization.PropertyLabel(property) + "を" + matches.Count + "件補間しました。";
        }

        private static bool IsSupported(LevelEvent evnt, string property)
        {
            PropertyInfo info;
            object value;
            if (!evnt.info.propertiesInfo.TryGetValue(property, out info) || !evnt.data.TryGetValue(property, out value)) return false;
            if (value == null) return false;
            if (info.type == PropertyType.Vector2 && value is Vector2) return true;
            if (info.type == PropertyType.Color && value is string) return true;
            return IsNumber(value);
        }

        private static bool IsNumber(object value)
        {
            if (value == null || value.GetType().IsEnum) return false;
            Type type = value.GetType();
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }

        private static object ParseValue(PropertyInfo info, object current, string text)
        {
            if (info.type == PropertyType.Vector2)
            {
                string[] parts = (text ?? string.Empty).Replace("(", string.Empty).Replace(")", string.Empty)
                    .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) throw new FormatException("Vector2は x,y の形式で入力してください。");
                return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
            }
            if (info.type == PropertyType.Color)
            {
                Color color;
                string value = (text ?? string.Empty).Trim().TrimStart('#');
                if (!ColorUtility.TryParseHtmlString("#" + value, out color))
                    throw new FormatException("色はRRGGBBまたはRRGGBBAAで入力してください。");
                return color;
            }

            double number;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                throw new FormatException("数値を入力してください。");
            return ConvertNumber(number, current.GetType());
        }

        private static object LerpValue(PropertyInfo info, object start, object end, float t)
        {
            if (info.type == PropertyType.Vector2) return Vector2.Lerp((Vector2)start, (Vector2)end, t);
            if (info.type == PropertyType.Color)
            {
                Color color = Color.Lerp((Color)start, (Color)end, t);
                return info.color_usesAlpha ? ColorUtility.ToHtmlStringRGBA(color) : ColorUtility.ToHtmlStringRGB(color);
            }
            double a = Convert.ToDouble(start, CultureInfo.InvariantCulture);
            double b = Convert.ToDouble(end, CultureInfo.InvariantCulture);
            return ConvertNumber(a + (b - a) * t, start.GetType());
        }

        private static object ValidateValue(PropertyInfo info, object value)
        {
            if (info.type == PropertyType.Vector2 && value is Vector2) return info.Validate((Vector2)value);
            if (info.type == PropertyType.Int || info.type == PropertyType.Rating)
                return info.Validate(Convert.ToInt32(value, CultureInfo.InvariantCulture));
            if (info.type == PropertyType.Float)
                return info.Validate(Convert.ToSingle(value, CultureInfo.InvariantCulture));
            return value;
        }

        private static object ConvertNumber(double value, Type type)
        {
            if (type == typeof(float)) return (float)value;
            if (type == typeof(double)) return value;
            if (type == typeof(decimal)) return (decimal)value;
            if (type == typeof(int)) return (int)Math.Round(value);
            if (type == typeof(long)) return (long)Math.Round(value);
            if (type == typeof(short)) return (short)Math.Round(value);
            if (type == typeof(byte)) return (byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, Math.Round(value)));
            if (type == typeof(uint)) return (uint)Math.Max(0d, Math.Round(value));
            if (type == typeof(ulong)) return (ulong)Math.Max(0d, Math.Round(value));
            if (type == typeof(ushort)) return (ushort)Math.Max(0d, Math.Min(ushort.MaxValue, Math.Round(value)));
            if (type == typeof(sbyte)) return (sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, Math.Round(value)));
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        private static float ParseFloat(string text)
        {
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Vector2の成分が数値ではありません。");
            return value;
        }

        private static int SourceIndex(scnEditor editor, LevelEvent evnt)
        {
            int index = editor.events.IndexOf(evnt);
            return index >= 0 ? index : editor.events.Count + editor.decorations.IndexOf(evnt);
        }
    }
}
