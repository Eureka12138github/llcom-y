using System;
using System.Globalization;

namespace llcom.Services;

internal enum ExpressionError
{
    None,
    Syntax,
    DivideByZero,
}

/// <summary>
/// 四则运算表达式求值（支持 + - * / % ^、括号、小数和一元正负号）。
/// 使用手写递归下降解析，不依赖脚本引擎或 DataTable。
/// </summary>
internal static class ExpressionEvaluator
{
    public static bool TryEvaluate(
        string expression,
        out double value,
        out ExpressionError error
    )
    {
        value = 0;
        error = ExpressionError.None;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var normalized = expression
            .Replace('×', '*')
            .Replace('÷', '/')
            .Replace('−', '-')
            .Replace('（', '(')
            .Replace('）', ')');

        try
        {
            value = new Parser(normalized).Parse();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = ExpressionError.Syntax;
                value = 0;
                return false;
            }

            return true;
        }
        catch (DivideByZeroException)
        {
            error = ExpressionError.DivideByZero;
            value = 0;
            return false;
        }
        catch (FormatException)
        {
            error = ExpressionError.Syntax;
            value = 0;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text)
        {
            _text = text;
        }

        public double Parse()
        {
            var value = ParseExpression();
            SkipWhitespace();
            if (_pos != _text.Length)
                throw new FormatException();
            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                    value += ParseTerm();
                else if (Match('-'))
                    value -= ParseTerm();
                else
                    return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                    value *= ParseUnary();
                else if (Match('/'))
                {
                    value /= RequireNonZeroDivisor();
                }
                else if (Match('%'))
                {
                    value %= RequireNonZeroDivisor();
                }
                else
                    return value;
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Match('+'))
                return ParseUnary();
            if (Match('-'))
                return -ParseUnary();
            return ParsePower();
        }

        private double ParsePower()
        {
            var value = ParsePrimary();
            SkipWhitespace();
            if (Match('^'))
                return Math.Pow(value, ParseUnary());
            return value;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Match('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!Match(')'))
                    throw new FormatException();
                return value;
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            var start = _pos;
            var seenDot = false;
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (c >= '0' && c <= '9')
                {
                    _pos++;
                    continue;
                }
                if (c == '.' && !seenDot)
                {
                    seenDot = true;
                    _pos++;
                    continue;
                }
                break;
            }

            if (_pos == start)
                throw new FormatException();

            var text = _text.Substring(start, _pos - start);
            if (
                !double.TryParse(
                    text,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
                throw new FormatException();

            return value;
        }

        private double RequireNonZeroDivisor()
        {
            var divisor = ParseUnary();
            if (divisor == 0)
                throw new DivideByZeroException();
            return divisor;
        }

        private bool Match(char c)
        {
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }
    }
}