using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class FormulaOperations
    {
        public static List<string> GetNumericPropertyNames(LevelEventType eventType)
        {
            LevelEvent sample = new LevelEvent(0, eventType);
            return sample.data
                .Where(x => !string.Equals(x.Key, "floor", StringComparison.OrdinalIgnoreCase) &&
                            IsNumericValue(x.Value))
                .Select(x => x.Key)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsNumericValue(object value)
        {
            if (value == null || value.GetType().IsEnum) return false;
            Type type = value.GetType();
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
                   type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
                   type == typeof(double) || type == typeof(decimal);
        }

        public static string ApplyNumericProperty(scnEditor editor, LevelEventType eventType, string property,
            string expression, string variableDefinitions, bool includeDecorations)
        {
            if (string.IsNullOrWhiteSpace(property)) throw new ArgumentException("数値項目が選択されていません。");
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("数式が空です。");
            if (!GetNumericPropertyNames(eventType).Contains(property))
                throw new ArgumentException("変更可能な数値項目を選択してください。");
            FloorRange range = EditorSelection.GetRange(editor, true);

            List<LevelEvent> matches = editor.events.Where(x => x.eventType == eventType &&
                x.floor >= range.Start && x.floor <= range.End).ToList();
            if (includeDecorations)
                matches.AddRange(editor.decorations.Where(x => x.eventType == eventType &&
                    x.floor >= range.Start && x.floor <= range.End));
            matches = matches.OrderBy(x => x.floor).ThenBy(x => GetSourceIndex(editor, x)).ToList();
            if (matches.Count == 0) return "No matching events found.";

            List<Tuple<LevelEvent, object>> changes = new List<Tuple<LevelEvent, object>>();
            for (int i = 0; i < matches.Count; i++)
            {
                LevelEvent evnt = matches[i];
                object current;
                if (!evnt.data.TryGetValue(property, out current))
                    throw new KeyNotFoundException(JapaneseLocalization.EventLabel(eventType.ToString()) + "に項目「" + property + "」はありません。");
                double currentNumber;
                if (!TryToDouble(current, out currentNumber))
                    throw new InvalidOperationException("項目「" + property + "」は数値ではありません。");

                Dictionary<string, double> vars = BuildVariables(editor, range, matches.Count, i, evnt.floor, currentNumber);
                ApplyDefinitions(variableDefinitions, vars);
                double result = ExpressionEvaluator.Evaluate(expression, vars);
                changes.Add(Tuple.Create(evnt, ConvertBack(result, current.GetType())));
            }

            using (new EditorUndoScope(editor))
            {
                foreach (Tuple<LevelEvent, object> change in changes)
                {
                    change.Item1.data[property] = change.Item2;
                    if (change.Item1.disabled.ContainsKey(property)) change.Item1.disabled[property] = false;
                }
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                if (EventOperations.AffectsPath(eventType)) editor.RemakePath(true, true);
            }

            return JapaneseLocalization.EventLabel(eventType.ToString()) + "の" + JapaneseLocalization.PropertyLabel(property) + "へ数式を" + changes.Count + "件適用しました。";
        }

        private static Dictionary<string, double> BuildVariables(scnEditor editor, FloorRange range,
            int matchCount, int index, int floor, double value)
        {
            double t = matchCount <= 1 ? 0d : (double)index / (matchCount - 1);
            float bpm = editor.levelData.bpm;
            if (floor >= 0 && floor < editor.floors.Count) bpm *= editor.floors[floor].speed;
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "value", value }, { "i", index }, { "t", t }, { "floor", floor },
                { "start", range.Start }, { "end", range.End }, { "count", matchCount },
                { "tilecount", range.Count }, { "bpm", bpm }, { "pi", Math.PI }, { "e", Math.E }
            };
        }

        private static void ApplyDefinitions(string definitions, IDictionary<string, double> vars)
        {
            if (string.IsNullOrWhiteSpace(definitions)) return;
            string[] lines = definitions.Replace(";", "\n").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) throw new FormatException("Variable definition must be name=expression: " + line);
                string name = line.Substring(0, equals).Trim().TrimStart('$');
                if (name.Length == 0) throw new FormatException("変数名が空です。");
                vars[name] = ExpressionEvaluator.Evaluate(line.Substring(equals + 1), vars);
            }
        }

        private static bool TryToDouble(object value, out double result)
        {
            if (value == null || value.GetType().IsEnum) { result = 0d; return false; }
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0d;
                return false;
            }
        }

        private static object ConvertBack(double value, Type type)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArithmeticException("The expression produced a non-finite value.");
            if (type == typeof(float))
            {
                if (value < -float.MaxValue || value > float.MaxValue)
                    throw new OverflowException("計算結果がSingleの範囲を超えています。");
                float result = (float)value;
                if (float.IsNaN(result) || float.IsInfinity(result))
                    throw new OverflowException("計算結果がSingleの範囲を超えています。");
                return result;
            }
            if (type == typeof(double)) return value;
            if (type == typeof(int)) return CheckedInteger(value, int.MinValue, int.MaxValue, type);
            if (type == typeof(long)) return CheckedInteger(value, long.MinValue, long.MaxValue, type);
            if (type == typeof(short)) return CheckedInteger(value, short.MinValue, short.MaxValue, type);
            if (type == typeof(byte)) return CheckedInteger(value, byte.MinValue, byte.MaxValue, type);
            if (type == typeof(uint)) return CheckedInteger(value, uint.MinValue, uint.MaxValue, type);
            if (type == typeof(ushort)) return CheckedInteger(value, ushort.MinValue, ushort.MaxValue, type);
            if (type == typeof(sbyte)) return CheckedInteger(value, sbyte.MinValue, sbyte.MaxValue, type);
            if (type == typeof(ulong))
            {
                double rounded = Math.Round(value);
                if (rounded < ulong.MinValue || rounded > ulong.MaxValue)
                    throw new OverflowException("計算結果がUInt64の範囲を超えています。");
                return (ulong)rounded;
            }
            if (type == typeof(decimal))
            {
                if (value < (double)decimal.MinValue || value > (double)decimal.MaxValue)
                    throw new OverflowException("計算結果がDecimalの範囲を超えています。");
                return (decimal)value;
            }
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        private static object CheckedInteger(double value, double min, double max, Type type)
        {
            double rounded = Math.Round(value);
            if (rounded < min || rounded > max)
                throw new OverflowException("計算結果が" + type.Name + "の範囲を超えています。");
            return Convert.ChangeType(rounded, type, CultureInfo.InvariantCulture);
        }

        private static int GetSourceIndex(scnEditor editor, LevelEvent evnt)
        {
            int index = editor.events.IndexOf(evnt);
            return index >= 0 ? index : editor.events.Count + editor.decorations.IndexOf(evnt);
        }
    }
}
