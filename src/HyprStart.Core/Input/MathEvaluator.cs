using System.Globalization;

namespace HyprStart.Core.Input;

/// <summary>
/// Вычисление простых выражений прямо в строке поиска: + - * / ( ), унарный минус, дроби.
/// Рекурсивный спуск, без внешних зависимостей — покрывается тестами.
/// </summary>
public static class MathEvaluator
{
    public static bool TryEvaluate(string? input, out string result)
    {
        result = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Replace(',', '.').Trim();
        if (text.Length == 0) return false;

        // без оператора или скобок это не выражение, а обычный запрос
        bool hasOperator = false;
        foreach (char ch in text)
        {
            if (ch is '+' or '-' or '*' or '/' or '(' or ')') hasOperator = true;
        }

        if (!hasOperator) return false;

        int position = 0;
        try
        {
            double value = ParseExpression(text, ref position);
            SkipSpaces(text, ref position);

            if (position != text.Length) return false;
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;

            result = value.ToString("0.##########", CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DivideByZeroException)
        {
            return false;
        }
    }

    private static double ParseExpression(string text, ref int position)
    {
        double value = ParseTerm(text, ref position);

        while (true)
        {
            SkipSpaces(text, ref position);
            if (position >= text.Length) return value;

            char op = text[position];
            if (op is not ('+' or '-')) return value;

            position++;
            double right = ParseTerm(text, ref position);
            value = op == '+' ? value + right : value - right;
        }
    }

    private static double ParseTerm(string text, ref int position)
    {
        double value = ParseFactor(text, ref position);

        while (true)
        {
            SkipSpaces(text, ref position);
            if (position >= text.Length) return value;

            char op = text[position];
            if (op is not ('*' or '/')) return value;

            position++;
            double right = ParseFactor(text, ref position);

            if (op == '/' && right == 0) throw new DivideByZeroException();
            value = op == '*' ? value * right : value / right;
        }
    }

    private static double ParseFactor(string text, ref int position)
    {
        SkipSpaces(text, ref position);
        if (position >= text.Length) throw new FormatException();

        char ch = text[position];

        if (ch == '-')
        {
            position++;
            return -ParseFactor(text, ref position);
        }

        if (ch == '+')
        {
            position++;
            return ParseFactor(text, ref position);
        }

        if (ch == '(')
        {
            position++;
            double inner = ParseExpression(text, ref position);
            SkipSpaces(text, ref position);

            if (position >= text.Length || text[position] != ')') throw new FormatException();
            position++;
            return inner;
        }

        int start = position;
        while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.')) position++;

        if (start == position) throw new FormatException();

        var span = text.AsSpan(start, position - start);
        if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            throw new FormatException();
        }

        return number;
    }

    private static void SkipSpaces(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }
}
