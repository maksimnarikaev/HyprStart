using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HyprStart.Core.Chat;

/// <summary>
/// Лёгкий веб-поиск для мини-чата: чистый текст r.jina.ai поверх DuckDuckGo.
/// Прямой запрос к html.duckduckgo.com с VPN-узлов отдаёт антибот-заглушку, поэтому
/// идём через прокси-читалку. Без сторонних парсеров — только регулярки.
/// Помимо заголовков и сниппетов выдачи, для первых <see cref="MaxPagesToFetch"/>
/// страниц догружается их содержательный текст (см. QuickChatClient) — именно он даёт
/// модели даты, цифры и формулировки вместо отписки списком сайтов.
/// </summary>
public static class WebSearch
{
    /// <summary>Явная команда: <c>/web запрос</c>.</summary>
    public const string Command = "/web";

    public const string Endpoint = "https://r.jina.ai/http://lite.duckduckgo.com/lite/?q=";

    /// <summary>
    /// Резервная точка входа в выдачу. В ноябре 2026 r.jina.ai начал отдавать
    /// <c>403 AbuseAlleviationError</c> на <c>lite.duckduckgo.com</c>, зато
    /// <c>duckduckgo.com/html</c> через тот же ридер работает и отдаёт markdown-выдачу
    /// с заголовками-ссылками. Проверено живым запросом.
    /// </summary>
    public const string FallbackEndpoint = "https://r.jina.ai/https://duckduckgo.com/html/?q=";

    /// <summary>
    /// Префикс ридера для произвольной страницы: «https://r.jina.ai/https://site/page».
    /// Тот же сервис, что и для выдачи, — отдаёт markdown и снимает антибот-заглушки.
    /// </summary>
    public const string PageReaderPrefix = "https://r.jina.ai/";

    /// <summary>
    /// Бюджет текста в системном промпте. Было 1200 — этого хватало на три заголовка,
    /// но не на факты. Теперь влезают сниппеты плюс тексты страниц.
    /// </summary>
    public const int MaxContextChars = 4000;

    /// <summary>Сколько символов берём с одной страницы: хватает на первый абзац с фактами.</summary>
    public const int MaxPageChars = 1500;

    /// <summary>Сколько страниц выкачиваем параллельно: больше — лишние секунды ожидания.</summary>
    public const int MaxPagesToFetch = 3;

    /// <summary>
    /// Жёсткий таймаут на всю веб-цепочку (выдача + страницы). Страницы тянем параллельно,
    /// поэтому 12 с — с запасом; дольше пользователь ждать ответа не должен.
    /// </summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Погодные города по умолчанию (Владикавказ) для Open-Meteo: API ждёт координаты.
    /// </summary>
    public const double WeatherLatitude = 43.02;

    public const double WeatherLongitude = 44.68;

    /// <summary>Бесплатный JSON-API погоды: без ключа, без капчи, работает поверх VPN.</summary>
    public static string WeatherUrl =>
        $"https://api.open-meteo.com/v1/forecast?latitude={WeatherLatitude.ToString(CultureInfo.InvariantCulture)}" +
        $"&longitude={WeatherLongitude.ToString(CultureInfo.InvariantCulture)}" +
        "&current=temperature_2m,apparent_temperature,wind_speed_10m";

    /// <summary>Сколько результатов забираем из выдачи: часть из них затем выкачивается целиком.</summary>
    public const int MaxResults = 5;

    /// <summary>
    /// Маркеры «нужна свежая информация». Если такой маркер есть в вопросе,
    /// поиск запускается сам, без команды <c>/web</c>.
    /// </summary>
    private static readonly string[] FreshMarkers =
    {
        "погода", "погоду", "погоде", "погоды", "температура", "температуру",
        "курс", "котировк", "цена", "цену", "стоимость", "сколько стоит",
        "доллар", "евро", "рубл", "биткоин", "биткойн", "валют", "акци",
        "новости", "новость", "новостей", "свежие данные", "последние данные",
        "который час", "работает ли", "актуальн", "текущ", "что сейчас",
    };

    /// <summary>Маркеры погодного вопроса — под них тянем цифры из Open-Meteo.</summary>
    private static readonly string[] WeatherMarkers =
    {
        "погода", "погоду", "погоде", "погоды", "температура", "температуру",
        "градус", "на улице", "ветер",
    };

    /// <summary>
    /// Маркеры «мир изменился»: релизы, патчи, события, сроки. По ним поиск тоже нужен —
    /// без них свежий релиз или патч модель уверенно называет неверной датой.
    /// </summary>
    private static readonly string[] EventMarkers =
    {
        "вышл", "вышел", "вышла", "вышло", "выпустил", "выпущен", "релиз", "патч", "обновлени",
        "версия", "дата выхода", "когда выйдет", "когда вышел", "анонс", "трейлер", "сезон",
        "турнир", "матч", "чемпионат", "выборы", "закон", "скидк", "распродаж", "умер", "погиб",
        "событи", "произош", "случил", "сколько человек", "население", "рекорд",
    };

    /// <summary>
    /// Признаки «мы обсуждаем код»: такие вопросы поиск не запускают даже при наличии
    /// маркеров, иначе «обнови версию пакета в коде» лезет в интернет и тормозит ответ.
    /// </summary>
    private static readonly string[] CodeMarkers =
    {
        "функци", "класс", "метод", "компилятор", "exception", "ошибк", "регулярк",
        "запрос на", "sql", "c#", "c++", "python", "javascript", "typescript", "csproj",
        "linq", "async", "await", "алгоритм", "код ",
    };

    /// <summary>
    /// «1.Заголовок» или «1. Заголовок» — нумерованный результат в выдаче DDG Lite,
    /// которую r.jina.ai отдаёт текстом. Классов и тегов там уже нет.
    /// </summary>
    private static readonly Regex NumberedTitleRegex = new(
        @"^(?<n>\d{1,2})\.\s*(?<t>\S.*)$",
        RegexOptions.Compiled);

    /// <summary>Заголовок, завёрнутый в markdown-ссылку: «[1. Текст](url)».</summary>
    private static readonly Regex LinkedTitleRegex = new(
        @"^\[(?<t>[^\]]+)\]\([^)]*\)$",
        RegexOptions.Compiled);

    /// <summary>Адрес внутри markdown-ссылки: «[текст](https://…)».</summary>
    private static readonly Regex MarkdownHrefRegex = new(
        @"\]\((?<u>[^)\s]+)\)",
        RegexOptions.Compiled);

    /// <summary>Голый http(s)-адрес в строке.</summary>
    private static readonly Regex GoloUrlRegex = new(
        @"https?://[^\s)\]>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Строка-ссылка в markdown: «[текст](url)» либо голый http(s)-URL.</summary>
    private static readonly Regex UrlLineRegex = new(
        @"^(?:\[.*?\]\(.*?\)|<https?://\S+>|https?://\S+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Заголовок-ссылка в выдаче DuckDuckGo HTML: «## [Текст](https://duckduckgo.com/l/?uddg=…)».
    /// Уровень заголовка от 1 до 6, чтобы не зависеть от конкретной вёрстки.
    /// </summary>
    private static readonly Regex HeadingLinkRegex = new(
        @"^#{1,6}\s+\[(?<t>[^\]]{8,240})\]\((?<u>[^)\s]+)\)$",
        RegexOptions.Compiled);

    /// <summary>Параметр <c>uddg</c> — настоящий адрес сайта, завёрнутый в редирект DDG.</summary>
    private static readonly Regex DdgRedirectRegex = new(
        @"[?&]uddg=(?<u>[^&]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Строка-«подпись» под заголовком: иконка сайта и его домен, продублированные ссылкой.
    /// Такие строки не содержат фактов и не должны становиться сниппетом.
    /// </summary>
    private static readonly Regex DomainCaptionRegex = new(
        @"^(?:!\[[^\]]*\]\([^)]*\))*\s*\[?(?<d>[a-z0-9][a-z0-9.-]{2,80}\.[a-z]{2,10}(?:/[^\s)\]]*)?)\]?(?:\([^)]*\))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Минимальная длина «сниппета», чтобы не тащить огрызки вроде «→».</summary>
    private const int MinSnippetChars = 20;

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>Markdown-обёртки (**жирный**, `код`, [текст](url)) — в сниппете не нужны.</summary>
    private static readonly Regex MarkdownRegex = new(
        @"\*\*|__|`|\[(?<t>[^\]]*)\]\([^)]*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Жирный текст в выдаче DDG приходит «слипшимся»: <c>**курс****доллара**</c> —
    /// после снятия пар звёздочек подряд идут четыре, и в тексте остаются `**`.
    /// Снимаем любые серии звёздочек/подчёркиваний, а не только парные.
    /// </summary>
    private static readonly Regex BoldMarkersRegex = new(
        @"\*{2,}|_{2,}",
        RegexOptions.Compiled);

    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Разбирает пользовательский ввод: снимает префикс <c>/web</c> и решает, нужен ли поиск.
    /// </summary>
    public static (string Query, bool NeedsSearch) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (string.Empty, false);

        string trimmed = text.Trim();

        if (trimmed.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
        {
            // «/web» должно быть отдельным словом: «/вебмастер» — не команда.
            if (trimmed.Length == Command.Length) return (string.Empty, true);

            char next = trimmed[Command.Length];
            if (char.IsWhiteSpace(next))
            {
                string rest = trimmed[(Command.Length)..].Trim();
                return (rest, rest.Length > 0);
            }
        }

        return (trimmed, HasFreshMarker(trimmed));
    }

    /// <summary>Есть ли в тексте маркер актуальной информации (регистр и словоформы — грубо).</summary>
    public static bool HasFreshMarker(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string lower = text.ToLowerInvariant();

        // Вопрос про код гасим первым: «обнови версию» и «ошибка в методе» — это не новости.
        foreach (string marker in CodeMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal)) return false;
        }

        foreach (string marker in FreshMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal)) return true;
        }

        foreach (string marker in EventMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal)) return true;
        }

        // Год в вопросе — почти всегда про «что происходит в мире сейчас».
        return YearLikeRegex.IsMatch(lower);
    }

    /// <summary>«2025»/«2026» отдельным числом: 12345 и «2.5» годами не считаем.</summary>
    private static readonly Regex YearLikeRegex = new(@"\b20[1-4]\d\b", RegexOptions.Compiled);

    /// <summary>URL HTML-выдачи DuckDuckGo для готового запроса.</summary>
    public static string BuildUrl(string query) => Endpoint + Uri.EscapeDataString(query ?? string.Empty);

    /// <summary>Резервный адрес выдачи — если основная точка входа заблокирована.</summary>
    public static string BuildFallbackUrl(string query)
        => FallbackEndpoint + Uri.EscapeDataString(query ?? string.Empty);

    /// <summary>
    /// Парсит markdown-выдачу DuckDuckGo HTML (резервный источник): заголовок-ссылка
    /// <c>## [Текст](https://duckduckgo.com/l/?uddg=…)</c>, затем строка с доменом
    /// и следом сниппет. Пустой список — формат не распознан.
    /// </summary>
    public static IReadOnlyList<WebSearchResult> ParseHeadingResults(string? text)
    {
        var results = new List<WebSearchResult>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string? title = null;
        string url = string.Empty;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            Match heading = HeadingLinkRegex.Match(line);
            if (heading.Success)
            {
                // Новый заголовок закрывает предыдущий блок: сниппет не нашёлся.
                title = Clean(heading.Groups["t"].Value);
                url = UnwrapRedirect(heading.Groups["u"].Value);
                if (title.Length == 0 || url.Length == 0) title = null;
                continue;
            }

            if (title is null) continue;

            // Домен/иконка под заголовком — не факт, ждём настоящий сниппет.
            if (DomainCaptionRegex.IsMatch(Clean(line))) continue;

            string snippet = CleanSnippet(line);
            if (snippet.Length < MinSnippetChars) continue;

            results.Add(new WebSearchResult(title, snippet) { Url = url });
            title = null;
            url = string.Empty;
            if (results.Count >= MaxResults) break;
        }

        return results;
    }

    /// <summary>Разворачивает редирект DDG (<c>?uddg=<адрес></c>) в исходный адрес сайта.</summary>
    public static string UnwrapRedirect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        Match redirect = DdgRedirectRegex.Match(url);
        if (redirect.Success)
        {
            string decoded = Uri.UnescapeDataString(redirect.Groups["u"].Value);
            if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return decoded;
        }

        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : string.Empty;
    }

    /// <summary>
    /// Парсит «чистый» текст от r.jina.ai (markdown-подобная выдача DuckDuckGo Lite).
    /// Формат строк: «1.Заголовок», затем «ссылка», затем описание. Блоки разделены пустыми
    /// строками. Возвращает до <see cref="MaxResults"/> пар «заголовок — сниппет».
    /// Пустой список — выдача не распознана (капча, смена формата).
    /// </summary>
    public static IReadOnlyList<WebSearchResult> ParseResults(string? text)
    {
        var results = new List<WebSearchResult>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string? pendingTitle = null;
        string pendingUrl = string.Empty;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                // Пустая строка закрывает блок: неподтверждённый заголовок сбрасываем.
                if (pendingTitle is not null) { pendingTitle = null; pendingUrl = string.Empty; }
                continue;
            }

            Match numbered = NumberedTitleRegex.Match(line);
            if (numbered.Success)
            {
                // Голый заголовок «1. Текст» (без markdown-ссылки) — URL придёт следующей строкой.
                pendingTitle = Clean(numbered.Groups["t"].Value);
                pendingUrl = string.Empty;
                continue;
            }

            // Основной случай выдачи r.jina.ai: заголовок завёрнут в markdown-ссылку
            // «[1. Курс доллара](https://…)» — ссылка здесь же служит и подтверждением.
            Match linked = LinkedTitleRegex.Match(line);
            if (linked.Success)
            {
                string title = Clean(linked.Groups["t"].Value);
                Match inner = NumberedTitleRegex.Match(title);
                if (inner.Success) title = Clean(inner.Groups["t"].Value);

                pendingTitle = title.Length > 0 ? title : null;
                pendingUrl = ExtractHref(line);
                continue;
            }

            if (pendingTitle is null) continue;

            if (UrlLineRegex.IsMatch(line))
            {
                if (pendingUrl.Length == 0) pendingUrl = ExtractHref(line);
                continue;
            }

            // Первая содержательная строка после ссылки — сниппет.
            if (pendingUrl.Length == 0) continue;

            string snippet = CleanSnippet(line);
            if (snippet.Length >= MinSnippetChars)
            {
                results.Add(new WebSearchResult(pendingTitle, snippet) { Url = pendingUrl });
                if (results.Count >= MaxResults) break;
            }

            pendingTitle = null;
            pendingUrl = string.Empty;
        }

        return results;
    }

    /// <summary>Достаёт адрес из «[текст](url)» или из голого http(s)-URL.</summary>
    internal static string ExtractHref(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;

        Match markdown = MarkdownHrefRegex.Match(line.Trim());
        if (markdown.Success) return markdown.Groups["u"].Value.Trim();

        return GoloUrlRegex.Match(line.Trim()).Value;
    }

    /// <summary>Нужен ли отдельный запрос погоды: спрашивают про температуру/погоду.</summary>
    public static bool IsWeatherQuery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string lower = text.ToLowerInvariant();
        foreach (string marker in WeatherMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>Чистит markdown-ответ от служебных заголовков jina и склеивает в один абзац.</summary>
    internal static string BuildRawBody(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        var kept = new List<string>();
        foreach (string rawLine in rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("URL Source:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Markdown Content:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)) continue;
            if (UrlLineRegex.IsMatch(line)) continue;

            kept.Add(CleanSnippet(line));
        }

        return string.Join(' ', kept.Where(s => s.Length > 0));
    }

    /// <summary>
    /// Собирает блок контекста для системного промпта. Пустая строка — результатов нет,
    /// тогда промпт не меняется и модель отвечает как обычно. Для каждого источника берём
    /// заголовок, сниппет выдачи и (если страница выкачана) её содержательный текст —
    /// именно текст даёт модели даты, цифры и формулировки вместо отписки списком сайтов.
    /// </summary>
    public static string BuildContextPrompt(string query, IReadOnlyList<WebSearchResult> results)
    {
        if (results is null || results.Count == 0) return string.Empty;

        var builder = new StringBuilder();
        builder.Append(" Данные из интернета, полученные сейчас, по запросу «")
               .Append((query ?? string.Empty).Trim()).Append("»:");

        int budget = MaxContextChars;
        for (int i = 0; i < results.Count && i < MaxResults && budget > 0; i++)
        {
            WebSearchResult item = results[i];
            var part = new StringBuilder();
            part.Append(' ').Append(i + 1).Append(") ").Append(item.Title);
            if (item.Snippet.Length > 0) part.Append(" — ").Append(item.Snippet);

            // Текст страницы — главный источник фактов. Режем по остатку бюджета,
            // чтобы промпт не разросся до десятков тысяч токенов.
            if (item.PageText.Length > 0)
            {
                int room = Math.Min(MaxPageChars, budget - part.Length - 20);
                if (room > 200)
                {
                    string page = item.PageText.Length > room ? item.PageText[..room] : item.PageText;
                    part.Append(" | Текст страницы: ").Append(page.TrimEnd()).Append('…');
                }
            }

            if (part.Length > budget) break;
            builder.Append(part);
            budget -= part.Length;
        }

        builder.Append(" Опирайся на эти данные и называй конкретные даты, цифры, названия и события из них.")
               .Append(" Если в вопросе спрашивают про свежий релиз, патч или новость — назови, что именно вышло и когда, опираясь на текст страниц выше.")
               .Append(" Если данные противоречат друг другу — скажи об этом.")
               .Append(" Не перечисляй ссылки вместо ответа: ссылки уже есть у пользователя, ему нужен сам факт.")
               .Append(" Если данных действительно недостаточно, честно скажи, чего не хватает.");
        return builder.ToString();
    }

    /// <summary>Чистит строку сниппета: снимает markdown-разметку и лишние пробелы.</summary>
    internal static string CleanSnippet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string text = MarkdownRegex.Replace(raw, "${t}");
        text = BoldMarkersRegex.Replace(text, " ");
        return SpaceRegex.Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    /// <summary>Снимает markdown-разметку, теги, HTML-сущности и лишние пробелы.</summary>
    internal static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Порядок важен: сначала ссылки/жирный (**сегодня**, [текст](url)),
        // иначе звёздочки останутся в заголовке.
        string plain = MarkdownRegex.Replace(raw, "${t}");
        string withoutTags = TagRegex.Replace(plain, " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return SpaceRegex.Replace(decoded, " ").Trim();
    }
}

/// <summary>
/// Один результат поиска: заголовок, краткое описание из выдачи, ссылка на источник и
/// содержательный текст страницы (пустой, если выкачать не удалось — тогда работает сниппет).
/// </summary>
public sealed record WebSearchResult(string Title, string Snippet)
{
    /// <summary>Ссылка на источник; пустая строка, если выдача её не отдала.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Текст страницы, очищенный от навигации. Пустая строка — страницу не читали.</summary>
    public string PageText { get; init; } = string.Empty;

    /// <summary>Есть ли реальный контент помимо заголовка и сниппета.</summary>
    public bool HasPageText => PageText.Length > 0;
}
