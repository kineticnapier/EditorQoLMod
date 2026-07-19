using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal sealed class ExpressionEvaluator
    {
        private readonly string text;
        private readonly IDictionary<string, double> variables;
        private int index;

        private ExpressionEvaluator(string text, IDictionary<string, double> variables)
        {
            this.text = text ?? string.Empty;
            this.variables = variables ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        public static double Evaluate(string expression, IDictionary<string, double> variables)
        {
            ExpressionEvaluator parser = new ExpressionEvaluator(expression, variables);
            double value = parser.ParseExpression();
            parser.SkipWhite();
            if (parser.index != parser.text.Length)
                throw new FormatException(parser.index + "文字目に解釈できない記号があります。");
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArithmeticException("The expression produced a non-finite value.");
            return value;
        }

        private double ParseExpression()
        {
            double value = ParseTerm();
            while (true)
            {
                SkipWhite();
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            double value = ParseUnary();
            while (true)
            {
                SkipWhite();
                if (Match('*')) value *= ParseUnary();
                else if (Match('/'))
                {
                    double divisor = ParseUnary();
                    if (Math.Abs(divisor) < 1e-15) throw new DivideByZeroException();
                    value /= divisor;
                }
                else if (Match('%'))
                {
                    double divisor = ParseUnary();
                    if (Math.Abs(divisor) < 1e-15) throw new DivideByZeroException();
                    value %= divisor;
                }
                else return value;
            }
        }

        private double ParsePower()
        {
            double value = ParsePrimary();
            SkipWhite();
            if (Match('^')) value = Math.Pow(value, ParseUnary());
            return value;
        }

        private double ParseUnary()
        {
            SkipWhite();
            if (Match('+')) return ParseUnary();
            if (Match('-')) return -ParseUnary();
            return ParsePower();
        }

        private double ParsePrimary()
        {
            SkipWhite();
            if (Match('('))
            {
                double value = ParseExpression();
                SkipWhite();
                Require(')');
                return value;
            }

            if (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
                return ParseNumber();

            string name = ParseIdentifier();
            if (name.Length == 0) throw new FormatException(index + "文字目に数値・変数・関数を入力してください。");

            SkipWhite();
            if (Match('('))
            {
                List<double> args = new List<double>();
                SkipWhite();
                if (!Peek(')'))
                {
                    while (true)
                    {
                        args.Add(ParseExpression());
                        SkipWhite();
                        if (Match(',')) continue;
                        break;
                    }
                }
                Require(')');
                return CallFunction(name, args);
            }

            if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase)) return Math.PI;
            if (string.Equals(name, "e", StringComparison.OrdinalIgnoreCase)) return Math.E;
            double variable;
            if (variables.TryGetValue(name.TrimStart('$'), out variable)) return variable;
            throw new KeyNotFoundException("Unknown variable: " + name);
        }

        private double ParseNumber()
        {
            int start = index;
            bool exponent = false;
            while (index < text.Length)
            {
                char c = text[index];
                if (char.IsDigit(c) || c == '.') { index++; continue; }
                if ((c == 'e' || c == 'E') && !exponent)
                {
                    exponent = true;
                    index++;
                    if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++;
                    continue;
                }
                break;
            }
            double value;
            if (!double.TryParse(text.Substring(start, index - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
                throw new FormatException(start + "文字目の数値が正しくありません。");
            return value;
        }

        private string ParseIdentifier()
        {
            SkipWhite();
            int start = index;
            if (index < text.Length && text[index] == '$') index++;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) index++;
            return text.Substring(start, index - start);
        }

        private static double CallFunction(string name, IList<double> args)
        {
            string n = name.ToLowerInvariant();
            if (n == "sin") return One(name, args, Math.Sin);
            if (n == "cos") return One(name, args, Math.Cos);
            if (n == "tan") return One(name, args, Math.Tan);
            if (n == "asin") return One(name, args, Math.Asin);
            if (n == "acos") return One(name, args, Math.Acos);
            if (n == "atan") return One(name, args, Math.Atan);
            if (n == "abs") return One(name, args, Math.Abs);
            if (n == "sqrt") return One(name, args, Math.Sqrt);
            if (n == "floor") return One(name, args, Math.Floor);
            if (n == "ceil" || n == "ceiling") return One(name, args, Math.Ceiling);
            if (n == "round") return One(name, args, Math.Round);
            if (n == "min") { RequireCount(name, args, 2); return Math.Min(args[0], args[1]); }
            if (n == "max") { RequireCount(name, args, 2); return Math.Max(args[0], args[1]); }
            if (n == "pow") { RequireCount(name, args, 2); return Math.Pow(args[0], args[1]); }
            if (n == "lerp") { RequireCount(name, args, 3); return args[0] + (args[1] - args[0]) * args[2]; }
            if (n == "clamp") { RequireCount(name, args, 3); return Math.Max(args[1], Math.Min(args[2], args[0])); }
            throw new ArgumentException("Unknown function: " + name);
        }

        private static double One(string name, IList<double> args, Func<double, double> function)
        {
            RequireCount(name, args, 1);
            return function(args[0]);
        }

        private static void RequireCount(string name, IList<double> args, int count)
        {
            if (args.Count != count) throw new ArgumentException(name + "には" + count + "個の引数が必要です。");
        }

        private void SkipWhite()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }

        private bool Match(char c)
        {
            SkipWhite();
            if (index >= text.Length || text[index] != c) return false;
            index++;
            return true;
        }

        private bool Peek(char c)
        {
            SkipWhite();
            return index < text.Length && text[index] == c;
        }

        private void Require(char c)
        {
            if (!Match(c)) throw new FormatException(index + "文字目に「" + c + "」が必要です。");
        }
    }
}
