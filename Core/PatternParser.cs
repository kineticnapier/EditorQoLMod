using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal enum PatternTokenKind
    {
        Angle,
        Twirl
    }

    internal sealed class PatternToken
    {
        public PatternTokenKind Kind { get; private set; }
        public double Angle { get; private set; }

        private PatternToken(PatternTokenKind kind, double angle)
        {
            Kind = kind;
            Angle = angle;
        }

        public static PatternToken FromAngle(double angle)
        {
            return new PatternToken(PatternTokenKind.Angle, angle);
        }

        public static PatternToken Twirl()
        {
            return new PatternToken(PatternTokenKind.Twirl, 0d);
        }
    }

    internal static class PatternParser
    {
        public static List<PatternToken> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("角度パターンが空です。");
            Parser parser = new Parser(text);
            List<PatternToken> result = parser.ParseSequence('\0');
            parser.SkipWhite();
            if (!parser.End) throw new FormatException(parser.Position + "文字目に解釈できない文字があります。");
            if (result.Count == 0) throw new FormatException("角度パターンが空です。");
            if (result.Count > 100000) throw new FormatException("角度パターンが大きすぎます。");
            return result;
        }

        private sealed class Parser
        {
            private readonly string source;
            private int position;

            public int Position { get { return position; } }
            public bool End { get { return position >= source.Length; } }

            public Parser(string source)
            {
                this.source = source;
            }

            public List<PatternToken> ParseSequence(char terminator)
            {
                List<PatternToken> values = new List<PatternToken>();
                while (true)
                {
                    SkipSeparators();
                    if (End)
                    {
                        if (terminator != '\0') throw new FormatException("閉じ記号「" + terminator + "」がありません。");
                        break;
                    }
                    if (terminator != '\0' && source[position] == terminator)
                    {
                        position++;
                        break;
                    }

                    List<PatternToken> item;
                    if (source[position] == '(')
                    {
                        position++;
                        item = ParseSequence(')');
                    }
                    else
                    {
                        PatternToken keyword;
                        if (TryParseKeyword(out keyword)) item = new List<PatternToken> { keyword };
                        else item = new List<PatternToken> { PatternToken.FromAngle(ParseNumber()) };
                    }

                    SkipWhite();
                    int repeat = 1;
                    if (!End && source[position] == '*')
                    {
                        position++;
                        SkipWhite();
                        repeat = ParsePositiveInteger();
                    }

                    for (int i = 0; i < repeat; i++) values.AddRange(item);
                    if (values.Count > 100000) throw new FormatException("角度パターンが大きすぎます。");
                }
                return values;
            }

            public void SkipWhite()
            {
                while (!End && char.IsWhiteSpace(source[position])) position++;
            }

            private void SkipSeparators()
            {
                while (!End && (char.IsWhiteSpace(source[position]) || source[position] == ',' || source[position] == ';'))
                    position++;
            }

            private bool TryParseKeyword(out PatternToken token)
            {
                token = null;
                int start = position;
                if (MatchesWord("twirl"))
                {
                    position += 5;
                    token = PatternToken.Twirl();
                    return true;
                }
                if (MatchesWord("t"))
                {
                    position += 1;
                    token = PatternToken.Twirl();
                    return true;
                }
                if (MatchesWord("旋回"))
                {
                    position += 2;
                    token = PatternToken.Twirl();
                    return true;
                }
                position = start;
                return false;
            }

            private bool MatchesWord(string word)
            {
                if (position + word.Length > source.Length) return false;
                if (!string.Equals(source.Substring(position, word.Length), word, StringComparison.OrdinalIgnoreCase))
                    return false;
                int next = position + word.Length;
                return next >= source.Length || IsBoundary(source[next]);
            }

            private static bool IsBoundary(char c)
            {
                return char.IsWhiteSpace(c) || c == ',' || c == ';' || c == '*' || c == '(' || c == ')';
            }

            private double ParseNumber()
            {
                SkipWhite();
                int start = position;
                if (!End && (source[position] == '+' || source[position] == '-')) position++;
                bool digit = false;
                while (!End && char.IsDigit(source[position]))
                {
                    digit = true;
                    position++;
                }
                if (!End && source[position] == '.')
                {
                    position++;
                    while (!End && char.IsDigit(source[position]))
                    {
                        digit = true;
                        position++;
                    }
                }
                if (!digit) throw new FormatException(start + "文字目に角度またはtwirlを入力してください。");
                double value;
                if (!double.TryParse(source.Substring(start, position - start), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                    throw new FormatException(start + "文字目の角度が正しくありません。");
                if (value != 999d && (value <= 0d || value > 360d))
                    throw new FormatException("角度は0より大きく360以下、ミッドスピンは999で指定してください。");
                return value;
            }

            private int ParsePositiveInteger()
            {
                int start = position;
                while (!End && char.IsDigit(source[position])) position++;
                int value;
                if (start == position || !int.TryParse(source.Substring(start, position - start), out value) || value <= 0)
                    throw new FormatException(start + "文字目に1以上の繰り返し回数を入力してください。");
                return value;
            }
        }
    }
}
