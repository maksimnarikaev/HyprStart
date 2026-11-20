using System.Globalization;

namespace HyprStart.Core.Settings;

/// <summary>
/// Настройки мини-чата в виде, не зависящем от конкретного провайдера.
/// <para>
/// Почему так: раньше адрес запроса был зашит константой DeepSeek, поэтому
/// OpenRouter/Groq/Ollama требовали правки исходников. Теперь пользователь
/// задаёт Base URL, ключ и модель в окне настроек, а полный адрес собирается
/// по правилам OpenAI-совместимого API.
/// </para>
/// </summary>
public static class ChatEndpoint
{
    /// <summary>Адрес DeepSeek - дефолт для чистой установки (совместим с OpenAI API).</summary>
    public const string DefaultBaseUrl = "https://api.deepseek.com";

    /// <summary>Модель по умолчанию для <see cref="DefaultBaseUrl"/>.</summary>
    public const string DefaultModel = "deepseek-chat";

    /// <summary>Переменная окружения с ключом: удобно для тех, кто не хочет хранить ключ в JSON.</summary>
    public const string EnvKeyName = "DEEPSEEK_API_KEY";

    /// <summary>Путь Chat Completions в OpenAI-совместимом API.</summary>
    public const string CompletionsPath = "/chat/completions";

    /// <summary>
    /// Приводит Base URL к виду <c>https://host/путь</c> без хвостового слэша:
    /// <c>https://api.deepseek.com/</c> -&gt; <c>https://api.deepseek.com</c>.
    /// Пустая строка остаётся пустой (значит «взять дефолт»).
    /// </summary>
    public static string NormalizeBaseUrl(string? baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;

        return value.TrimEnd('/');
    }

    /// <summary>
    /// Собирает полный адрес запроса. Если пользователь уже вписал
    /// <c>/chat/completions</c> (частый случай при копировании из документации),
    /// второй раз путь не добавляется. Пустой Base URL даёт дефолт DeepSeek.
    /// </summary>
    public static string BuildUrl(string? baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (normalized.Length == 0) normalized = DefaultBaseUrl;

        // Уже полный адрес - не дублируем путь.
        if (normalized.EndsWith(CompletionsPath, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        // Голый хост без схемы (api.deepseek.com) - дописываем https, иначе HttpClient упадёт.
        if (!normalized.Contains("://", StringComparison.Ordinal))
        {
            normalized = "https://" + normalized;
        }

        return normalized + CompletionsPath;
    }

    /// <summary>
    /// Разрешает ключ: значение из настроек важнее переменной окружения.
    /// Пробелы по краям срезаются - иначе заголовок Authorization ломается.
    /// </summary>
    public static string ResolveApiKey(string? fromSettings, Func<string, string?>? readEnv = null)
    {
        if (!string.IsNullOrWhiteSpace(fromSettings)) return fromSettings.Trim();

        var env = readEnv ?? Environment.GetEnvironmentVariable;
        var value = env(EnvKeyName);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// true, если ключ не задан ни в настройках, ни в окружении: панель мини-чата
    /// в этом случае показывает подсказку, а не ошибку запроса.
    /// </summary>
    public static bool IsConfigured(string? apiKey, Func<string, string?>? readEnv = null)
        => ResolveApiKey(apiKey, readEnv).Length > 0;

    /// <summary>Проверка координат перед запросом погоды: Open-Meteo ждёт диапазоны WGS84.</summary>
    public static bool IsValidCoordinate(double latitude, double longitude)
        => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
           && !(Math.Abs(latitude) < 0.000001 && Math.Abs(longitude) < 0.000001);
}

/// <summary>
/// Погодная точка для Open-Meteo. Хранится в настройках, чтобы не ходить
/// за геолокацией при каждом запуске и чтобы пользователь мог задать город сам.
/// </summary>
public sealed class WeatherLocationSettings
{
    /// <summary>Город, определённый по IP или введённый вручную. Пусто - ещё не определён.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Широта. 0/0 - координаты ещё не известны.</summary>
    public double Latitude { get; set; }

    /// <summary>Долгота.</summary>
    public double Longitude { get; set; }

    /// <summary>true, если координаты заданы вручную: автогеолокация их больше не перетирает.</summary>
    public bool Manual { get; set; }

    /// <summary>true, если координаты пригодны для запроса погоды.</summary>
    public bool HasCoordinates => ChatEndpoint.IsValidCoordinate(Latitude, Longitude);

    /// <summary>Подпись для polybar: город, иначе координаты, иначе «погода».</summary>
    public string Describe()
    {
        if (!string.IsNullOrWhiteSpace(City)) return City.Trim();
        return HasCoordinates
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}", Latitude, Longitude)
            : "погода";
    }
}
