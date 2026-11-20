using System.Text;

namespace HyprStart.Core.Chat;

/// <summary>
/// Контекст мини-чата: сегодняшняя дата и город пользователя дописываются к системному
/// промпту, иначе модель отвечает «не знаю текущую дату» и путает местоположение.
/// </summary>
public static class ChatContext
{
    /// <summary>Город пользователя по умолчанию (Владикавказ).</summary>
    public const string DefaultCity = "Владикавказ";

    /// <summary>Команда погоды: <c>/weather</c> или <c>/погода</c> в начале сообщения.</summary>
    public static bool IsWeatherRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string trimmed = text.TrimStart();

        // Команда должна занимать целиком первое слово: «/weathers» — это не погода.
        int end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end])) end++;
        string command = trimmed[..end];

        return command.Equals("/weather", StringComparison.OrdinalIgnoreCase)
               || command.Equals("/погода", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Системный промпт с контекстом: дата (yyyy-MM-dd) и город.
    /// </summary>
    public static string BuildSystemPrompt(string basePrompt, DateTime now, string city)
    {
        string safeCity = string.IsNullOrWhiteSpace(city) ? DefaultCity : city.Trim();

        var builder = new StringBuilder(basePrompt.TrimEnd());
        builder.Append(" Сегодня ").Append(now.ToString("yyyy-MM-dd"));
        builder.Append(", пользователь в городе ").Append(safeCity).Append('.');
        return builder.ToString();
    }

    /// <summary>
    /// Промпт для <c>/weather</c>: модель пересказывает уже полученную сводку,
    /// поэтому просим только сжать её до одной строки.
    /// </summary>
    public static string BuildWeatherPrompt(string basePrompt, DateTime now, string city, string summary)
    {
        string safeCity = string.IsNullOrWhiteSpace(city) ? DefaultCity : city.Trim();
        string safeSummary = summary.Trim();

        var builder = new StringBuilder(basePrompt.TrimEnd());
        builder.Append(" Сегодня ").Append(now.ToString("yyyy-MM-dd"));
        builder.Append(", пользователь в городе ").Append(safeCity).Append('.');
        builder.Append(" Погода (свежая сводка, перескажи своими словами одной короткой строкой): ");
        builder.Append(safeSummary);
        return builder.ToString();
    }

    /// <summary>URL сводки погоды в текстовом формате wttr.in (одна строка, по-русски).</summary>
    public static string WeatherUrl(string city)
    {
        string safeCity = string.IsNullOrWhiteSpace(city) ? DefaultCity : city.Trim();
        return "https://wttr.in/" + Uri.EscapeDataString(safeCity) + "?format=3&lang=ru";
    }

    /// <summary>
    /// Чистит ответ wttr.in: убирает ANSI-последовательности и переводы строк,
    /// оставляя одну короткую строку.
    /// </summary>
    public static string NormalizeWeather(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var cleaned = new StringBuilder(raw.Length);
        bool inEscape = false;

        foreach (char ch in raw)
        {
            if (inEscape)
            {
                // ANSI-последовательность заканчивается буквой (@, m, K и т.п.).
                if (char.IsLetter(ch)) inEscape = false;
                continue;
            }

            if (ch == '\u001b')
            {
                inEscape = true;
                continue;
            }

            cleaned.Append(ch == '\r' || ch == '\n' ? ' ' : ch);
        }

        // Схлопываем повторные пробелы: ответ должен занимать одну строку.
        string[] words = cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).Trim();
    }
}
