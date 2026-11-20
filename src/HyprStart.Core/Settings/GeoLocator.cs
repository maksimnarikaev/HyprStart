using System.Globalization;
using System.Text.RegularExpressions;

namespace HyprStart.Core.Settings;

/// <summary>Результат определения местоположения по IP.</summary>
public sealed record GeoPoint(string City, double Latitude, double Longitude)
{
    public bool IsUsable => !string.IsNullOrWhiteSpace(City) && ChatEndpoint.IsValidCoordinate(Latitude, Longitude);
}

/// <summary>
/// Разбор ответов бесплатных геолокаторов (без ключей). Сервисы отвечают
/// разными форматами, поэтому каждый разбирается отдельным методом, а не общим
/// JSON-маппингом: так проще безопасно деградировать, если формат поменялся.
/// </summary>
public static class GeoLocator
{
    /// <summary>Сколько ждём ответ геолокатора: погода не стоит долгого старта.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Адреса в порядке попыток. Все - без ключа и без регистрации.
    /// ipapi.co идёт первым: отдаёт city и координаты одним JSON-объектом.
    /// </summary>
    public static IReadOnlyList<string> Endpoints { get; } = new[]
    {
        "https://ipapi.co/json/",
        "https://ipwho.is/",
        "https://get.geojs.io/v1/ip/geo.json",
    };

    private static readonly Regex NumberRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    /// <summary>
    /// Разбор ответа ipapi.co: <c>{"city":"Berlin","latitude":52.52,"longitude":13.40,...}</c>
    /// </summary>
    public static GeoPoint? ParseIpApi(string? json)
        => ParseFlat(json, "\"city\"", "\"latitude\"", "\"longitude\"");

    /// <summary>
    /// Разбор ответа ipwho.is: <c>{"success":true,"city":"...","latitude":43.03,"longitude":44.68}</c>.
    /// Координаты могут быть строками - <see cref="ExtractNumber"/> понимает оба вида.
    /// </summary>
    public static GeoPoint? ParseIpWhoIs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // {"success":false,"message":"..."} - запрос отклонён, координат нет.
        if (json.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase)) return null;

        return ParseFlat(json, "\"city\"", "\"latitude\"", "\"longitude\"");
    }

    /// <summary>
    /// Разбор ответа geojs: значения приходят строками -&gt; читаем числа из кавычек.
    /// </summary>
    public static GeoPoint? ParseGeoJs(string? json)
        => ParseFlat(json, "\"city\"", "\"latitude\"", "\"longitude\"");

    /// <summary>
    /// Общий разбор плоского JSON: имя города и два числа по ключам.
    /// Значение может быть строкой или числом - берём первое число из фрагмента.
    /// </summary>
    public static GeoPoint? ParseFlat(string? json, string cityKey, string latKey, string lonKey)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        // Заглушки вида {"error":true} или пустой объект - сразу отказ.
        if (json.Contains("\"bogon\"", StringComparison.OrdinalIgnoreCase)) return null;

        var city = ExtractStringValue(json, cityKey);
        if (string.IsNullOrWhiteSpace(city)) return null;

        var latitude = ExtractNumber(json, latKey);
        var longitude = ExtractNumber(json, lonKey);
        if (latitude is null || longitude is null) return null;

        var point = new GeoPoint(city.Trim(), latitude.Value, longitude.Value);
        return point.IsUsable ? point : null;
    }

    /// <summary>Читает строковое значение по ключу: {"city":"X"} -&gt; X.</summary>
    public static string ExtractStringValue(string json, string key)
    {
        var index = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;

        var colon = json.IndexOf(':', index + key.Length);
        if (colon < 0) return string.Empty;

        var start = colon + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        if (start >= json.Length || json[start] != '"') return string.Empty;

        var end = json.IndexOf('"', start + 1);
        if (end < 0) return string.Empty;

        // Экранирование в именах городов почти не встречается, но кавычку снимаем честно.
        return json.Substring(start + 1, end - start - 1).Replace("\\\"", "\"").Trim();
    }

    /// <summary>Читает число по ключу: {"latitude":43.03} или {"latitude":"43.03"}.</summary>
    public static double? ExtractNumber(string json, string key)
    {
        var index = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var colon = json.IndexOf(':', index + key.Length);
        if (colon < 0) return null;

        // Берём небольшой срез после двоеточия: число не должно «утекать» в соседнее поле.
        var tail = json.Substring(colon + 1, Math.Min(32, json.Length - colon - 1));
        var match = NumberRegex.Match(tail);
        if (!match.Success) return null;

        return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Синхронное определение местоположения: опрос эндпоинтов по очереди и,
    /// если сети нет, фолбэк по часовому поясу системы. Возвращает null, если
    /// не помог ни один способ - тогда остаются координаты из настроек.
    /// </summary>
    /// <param name="fetch">Загрузка тела ответа по адресу. Подменяется в тестах.</param>
    /// <param name="timeZoneId">Часовой пояс; по умолчанию берётся системный.</param>
    public static GeoPoint? Detect(Func<string, string?>? fetch = null, string? timeZoneId = null)
    {
        var parsers = new Func<string?, GeoPoint?>[] { ParseIpApi, ParseIpWhoIs, ParseGeoJs };

        if (fetch is not null)
        {
            for (var i = 0; i < Endpoints.Count; i++)
            {
                try
                {
                    var point = parsers[i](fetch(Endpoints[i]));
                    if (point is not null) return point;
                }
                catch
                {
                    // Эндпоинт недоступен или отдал мусор - пробуем следующий.
                }
            }
        }

        return FromTimeZone(timeZoneId ?? TimeZoneInfo.Local.Id);
    }

    /// <summary>
    /// Запасной путь без сети: часовой пояс ОС -&gt; город и координаты.
    /// Нужен, когда геолокатор недоступен (нет сети, VPN, корпоративный прокси).
    /// </summary>
    public static GeoPoint? FromTimeZone(string? timeZoneId)
    {
        var id = (timeZoneId ?? string.Empty).Trim();
        if (id.Length == 0) return null;

        foreach (var zone in ByTimeZone)
        {
            if (string.Equals(zone.Key, id, StringComparison.OrdinalIgnoreCase))
            {
                return zone.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Минимальная таблица часовых поясов бывшего СССР: покрывает большинство
    /// российских установок Windows и не требует внешних библиотек.
    /// </summary>
    public static IReadOnlyDictionary<string, GeoPoint> ByTimeZone { get; } =
        new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Russian Standard Time"] = new("Москва", 55.7558, 37.6173),
            ["Russia Time Zone 3"] = new("Самара", 53.2001, 50.15),
            ["Russia Time Zone 4"] = new("Екатеринбург", 56.8389, 60.6057),
            ["Russia Time Zone 5"] = new("Омск", 54.9885, 73.3242),
            ["Russia Time Zone 6"] = new("Красноярск", 56.0153, 92.8932),
            ["Russia Time Zone 7"] = new("Новосибирск", 55.0084, 82.9357),
            ["Russia Time Zone 8"] = new("Иркутск", 52.2871, 104.3050),
            ["Russia Time Zone 9"] = new("Якутск", 62.0355, 129.6755),
            ["Russia Time Zone 10"] = new("Владивосток", 43.1155, 131.8855),
            ["Russia Time Zone 11"] = new("Магадан", 59.5638, 150.8035),
            ["Kamchatka Standard Time"] = new("Петропавловск-Камчатский", 53.0444, 158.6483),
        };
}
