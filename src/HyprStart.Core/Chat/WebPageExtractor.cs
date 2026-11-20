using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HyprStart.Core.Chat;

/// <summary>
/// Достаёт содержательный текст из выкачанной страницы: r.jina.ai отдаёт markdown,
/// но иногда приходит и сырой HTML (если сайт отдал нечитаемый для ридера формат).
/// Задача — выкинуть навигацию, скрипты, куки-баннеры и меню, оставив абзацы с фактами:
/// датами, цифрами, названиями. Без сторонних библиотек — только регулярки.
/// </summary>
public static class WebPageExtractor
{
    /// <summary>Минимальная длина осмысленного фрагмента: «Читать далее» и «Войти» отбрасываем.</summary>
    private const int MinFragmentChars = 40;

    /// <summary>
    /// Порог для заголовков: «Патч 2.0 вышел 19 ноября 2026» короче 40 символов, но это
    /// самый ценный факт на странице — для них порог ниже.
    /// </summary>
    private const int MinHeadingChars = 12;

    /// <summary>Нижняя граница для коротких фактических блоков: «Курс 92,4 ₽» — факт,
    /// хотя до обычного порога абзаца не дотягивает.</summary>
    private const int MinFactualChars = 18;

    /// <summary>Сколько фрагментов максимум склеиваем: больше всё равно не влезет в промпт.</summary>
    private const int MaxFragments = 40;

    // Служебные блоки HTML целиком вырезаем до любой обработки текста.
    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style|noscript|svg|iframe|head)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CommentRegex = new(
        @"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Навигация, подвал, боковые панели, куки-баннеры.
    private static readonly Regex BoilerplateRegex = new(
        @"<(nav|header|footer|aside|form|button)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex SpaceRegex = new(@"[ \t\u00A0]+", RegexOptions.Compiled);

    private static readonly Regex BlankLinesRegex = new(@"\n{2,}", RegexOptions.Compiled);

    // Строки-мусор: меню, кнопки, копирайт, языки, подписки.
    private static readonly string[] NoiseMarkers =
    {
        "cookie", "куки", "политика конфиденциальности", "все права защищены",
        "подписаться", "войти", "регистрация", "читать далее", "читать больше",
        "поделиться", "комментировать", "реклама", "перейти к содержимому",
        "skip to content", "sign in", "log in", "subscribe", "advertisement",
        // Служебные строки самого ридера r.jina.ai: заголовок и адрес источника.
        // Они дублируют данные выдачи и только занимают бюджет промпта.
        "title:", "url source:", "markdown content:", "published time:",
    };

    /// <summary>Признаки строки-заголовка в markdown-выдаче r.jina.ai.</summary>
    private static readonly Regex MarkdownHeadingRegex = new(
        @"^\s{0,3}#{1,6}\s+(?<t>.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Строка-разделитель markdown: «---», «***», «___».</summary>
    private static readonly Regex HorizontalRuleRegex = new(
        @"^\s*(?:[-*_]\s*){3,}$",
        RegexOptions.Compiled);

    /// <summary>Markdown-обёртки: **жирный**, `код`, [текст](url), ![alt](src).</summary>
    private static readonly Regex MarkdownNoiseRegex = new(
        @"!\[[^\]]*\]\([^)]*\)|\*\*|__|`|\[(?<t>[^\]]*)\]\([^)]*\)",
        RegexOptions.Compiled);

    /// <summary>Невидимый маркер заголовка: ставится до снятия разметки и снимается в NormalizeBlock.</summary>
    private const string HeadingMark = "\u0001";

    /// <summary>Открывающие теги заголовков: следующий за ними текст считается заголовком.</summary>
    private static readonly Regex HeadingOpenRegex = new(
        @"<h[1-6][^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Извлекает содержательный текст страницы. Результат — один «плотный» абзац
    /// с фрагментами через пробел, не длиннее <see cref="maxChars"/> символов.
    /// Пустая строка — из страницы нечего взять (заглушка, капча, пустой ответ).
    /// </summary>
    public static string Extract(string? raw, int maxChars = WebSearch.MaxPageChars)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Нормализуем переводы строк до любой обработки: разбор идёт по разделителю
        // "\n\n", и на CRLF-входе (Windows, часть сайтов) блоки не разделялись бы
        // вовсе — дедупликация и пороги длины молча переставали работать.
        raw = raw.Replace("\r\n", "\n").Replace('\r', '\n');

        string markdown = StripHtmlMarkup(raw);
        var fragments = new List<string>();
        int total = 0;

        // Ридер отдаёт страницу целиком: сверху идут шапка и мега-меню (десятки ссылок),
        // а факты начинаются ниже — иногда за 10 000+ символов. Без обрезки шапки бюджет
        // целиком уходит на «Вклады и счета / Кредиты / Займы» и модель не видит сути.
        markdown = DropNavigationPrefix(markdown);

        // Два прохода. Первый — только отсев мусора (меню, сетки, склейки), порядок
        // страницы сохраняем: он осмысленный, надёжный заголовок и абзац идут первыми.
        var candidates = new List<(string Text, bool Heading, int Score, int Threshold)>();
        foreach (string block in SplitBlocks(markdown))
        {
            // Служебные строки выкидываем до подсчёта кандидата: в выдаче ридера меню
            // и ссылки часто склеены с заголовком в один абзац, и фильтр «по блоку»
            // уносил бы вместе с ними сам факт.
            string candidate = DropNoiseLines(block);
            if (candidate.Length == 0) continue;

            // Строка, у которой ссылки занимают больше половины текста, — это пункт меню,
            // а не факт: такие блоки не должны съедать бюджет.
            if (IsLinkHeavy(block)) continue;

            // Календарная сетка («1 | 2 | 3 | … 30») уже вырезана построчно
            // в DropNoiseLines: тут дополнительно отсеиваем блоки-таблицы целиком.
            if (IsCalendarGrid(candidate)) continue;

            // Заголовок — часто самый информативный фрагмент («Патч 2.0 вышел 19.11.2026»),
            // поэтому для него порог длины ниже, чем для обычного абзаца.
            bool looksLikeHeading = IsHeading(block);
            int threshold = looksLikeHeading ? MinHeadingChars : MinFragmentChars;

            // Короткий, но содержательный блок («Данные ЦБ РФ на 19 ноября 2026 года» —
            // 34 символа) не должен отсекаться порогом длины: порог существует, чтобы
            // не пускать в промпт обрывки меню, а пункты меню уже отсеяны IsLinkHeavy.
            // Блок с признаками факта (дата/значение/валюта) берём, если он не короче
            // абсолютного минимума осмысленности.
            int score = FactScore(candidate);
            int effectiveThreshold = score > 0 ? Math.Min(threshold, MinFactualChars) : threshold;
            if (candidate.Length < effectiveThreshold) continue;

            candidates.Add((candidate, looksLikeHeading, score, effectiveThreshold));
        }

        // Второй проход — укладка в бюджет. Сначала идут блоки с признаками факта
        // (дата, значение, «сегодня») в исходном порядке, затем остальные — тоже в исходном
        // порядке. Так реальная цифра не проигрывает перечню названий, а страница без
        // дат и значений не остаётся пустой.
        var ordered = candidates
            .Where(item => item.Score > 0)
            .Concat(candidates.Where(item => item.Score <= 0))
            .ToList();

        foreach ((string text, bool _, int __, int threshold) in ordered)
        {
            // Дедуп: повторяющиеся фрагменты (меню, языковая панель) только жгут бюджет.
            if (fragments.Any(f => string.Equals(f, text, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (total + text.Length > maxChars)
            {
                int room = maxChars - total;
                if (room < threshold) continue;
                fragments.Add(text[..room].TrimEnd());
                total = maxChars;
                continue;
            }

            fragments.Add(text);
            total += text.Length + 1;

            if (fragments.Count >= MaxFragments) break;
        }

        return string.Join(' ', fragments).Trim();
    }

    /// <summary>
    /// Убирает разметку HTML и markdown-мусор. HTML может быть смешан с markdown —
    /// обрабатываем в два прохода, порядок важен: служебные блоки режем до тегов.
    /// </summary>
    private static string StripHtmlMarkup(string raw)
    {
        string text = CommentRegex.Replace(raw, string.Empty);
        text = ScriptStyleRegex.Replace(text, string.Empty);
        text = BoilerplateRegex.Replace(text, string.Empty);

        // Блочные теги превращаем в переводы строк: так абзацы не слипаются.
        // Заголовки помечаем маркером — после снятия разметки их не отличить от абзацев.
        text = HeadingOpenRegex.Replace(text, "\n" + HeadingMark + " ");
        text = BlockBreakRegex.Replace(text, "\n");
        text = TagRegex.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);

        // Markdown: заголовки и маркеры списков снимаем, текст оставляем.
        text = MarkdownHeadingRegex.Replace(text, HeadingMark + " ${t}");

        // Картинки ридера («![Image 10: монета](https://...)») фактов не несут — убираем целиком.
        text = MarkdownImageRegex.Replace(text, string.Empty);
        text = MarkdownNoiseRegex.Replace(text, "${t}");
        text = HorizontalRuleRegex.Replace(text, string.Empty);
        text = ListMarkerRegex.Replace(text, string.Empty);

        return BlankLinesRegex.Replace(text, "\n\n");
    }

    private static readonly Regex BlockBreakRegex = new(
        @"</?(?:p|div|br|li|tr|h[1-6]|article|section|main)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ListMarkerRegex = new(
        @"^\s*(?:[-*+]|\d{1,2}[.)])\s+",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Режет текст на блоки по пустым строкам; одиночный перевод строки — внутри блока.</summary>
    private static IEnumerable<string> SplitBlocks(string text)
        => text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Отрезает шапку и мега-меню перед основным содержанием.
    /// Признак конца шапки — первый заголовок (<c>\x01</c>) в начале строки: у ридера
    /// заголовок статьи идёт уже после навигации. Вызывать после <see cref="StripHtmlMarkup"/>:
    /// markdown-заголовки к этому моменту уже помечены маркером.
    /// </summary>
    private static string DropNavigationPrefix(string markdown)
    {
        Match match = MainHeadingRegex.Match(markdown);
        if (!match.Success) return markdown;

        // Меню бывает коротким (сайт без мега-навигации) — тогда ничего не режем:
        // первый заголовок в самом начале текста обычно и есть суть страницы.
        if (match.Index < NavigationPrefixChars) return markdown;

        return markdown[match.Index..];
    }

    /// <summary>
    /// Блок считается навигацией, если markdown-ссылки занимают в нём больше половины
    /// символов: «[Вклады](...)[Кредиты](...)[Займы](...)» — это меню, а не факт.
    /// </summary>
    private static bool IsLinkHeavy(string block)
    {
        int linkChars = 0;
        foreach (Match match in LinkRegex.Matches(block)) linkChars += match.Length;

        int letters = 0;
        foreach (char ch in block)
        {
            if (char.IsLetterOrDigit(ch)) letters++;
        }

        // Совсем короткие строки не трогаем: там доля ссылок статистически случайна.
        if (letters < MinFragmentChars) return false;

        return linkChars > block.Length / 2;
    }

    /// <summary>Markdown-картинка целиком: <c>![alt](url)</c> — в тексте фактов от неё только мусор.</summary>
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[[^\]]*\]\([^)]*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Отличает содержательную страницу от заглушки ридера: кэш-снимок, отказ источника
    /// (403/«доступ заблокирован»), сообщение об антиботе. Для заглушки текст бесполезен.
    /// </summary>
    public static bool LooksLikeStub(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;

        // Поиск только по началу ответа: дальше идёт сама страница, там эти слова
        // могут встречаться в статьях и новостях, и ложное срабатывание съело бы факт.
        int probeLength = Math.Min(raw.Length, StubProbeChars);
        string head = raw[..probeLength].ToLowerInvariant();

        foreach (string marker in StubMarkers)
        {
            if (head.Contains(marker, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>Сколько символов от начала ответа считать служебной зоной ридера.</summary>
    private const int StubProbeChars = 400;

    private static readonly string[] StubMarkers =
    {
        "access blocked", "access denied", "abusealleviationerror", "anomaly-modal",
        "captcha", "checking your browser", "enable javascript and cookies",
        "403.", "403 forbidden", "робот", "не робот", "проверка браузера",
    };

    /// <summary>Первый заголовок в начале строки (маркер ставится при снятии разметки).</summary>
    private static readonly Regex MainHeadingRegex = new(
        "^" + Regex.Escape(HeadingMark.ToString()) + @"[ \t]+\S.*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Markdown-ссылка целиком: <c>[текст](url)</c>.</summary>
    private static readonly Regex LinkRegex = new(
        @"\[[^\]]*\]\([^)]*\)",
        RegexOptions.Compiled);

    /// <summary>Шапка сайта короче этого порога не считается навигационным шумом.</summary>
    private const int NavigationPrefixChars = 600;

    /// <summary>Схлопывает пробелы внутри блока, снимая маркер заголовка.</summary>
    private static string NormalizeBlock(string block)
    {
        string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var parts = new StringBuilder();
        foreach (string line in lines)
        {
            string cleaned = SpaceRegex.Replace(line.Replace(HeadingMark, " ", StringComparison.Ordinal), " ").Trim();
            if (cleaned.Length == 0) continue;
            if (parts.Length > 0) parts.Append(' ');
            parts.Append(cleaned);
        }

        return SpaceRegex.Replace(parts.ToString(), " ").Trim();
    }

    /// <summary>
    /// Снимает служебные строки внутри блока, сохраняя полезный текст.
    /// В markdown-выдаче навигация часто приклеена к заголовку одной строкой
    /// («[Skip to content](#main) Заголовок»), поэтому фильтруем построчно,
    /// а не блоком целиком — иначе вместе с меню теряется факт.
    /// </summary>
    private static string DropNoiseLines(string block)
    {
        string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var parts = new StringBuilder();
        foreach (string line in lines)
        {
            string cleaned = SpaceRegex.Replace(line.Replace(HeadingMark, " ", StringComparison.Ordinal), " ").Trim();
            if (cleaned.Length == 0) continue;
            if (IsNoiseLine(cleaned)) continue;

            // Строка календарной сетки («| 8 | 9 | 10 | …») — мусор без фактов. Режем
            // построчно: в тексте ридера строки таблицы и настоящий абзац часто лежат
            // в одном блоке, и отсев по блоку унёс бы вместе с сеткой сам факт.
            if (IsCalendarGrid(cleaned)) continue;
            if (parts.Length > 0) parts.Append(' ');
            parts.Append(cleaned);
        }

        return SpaceRegex.Replace(parts.ToString(), " ").Trim();
    }

    /// <summary>Блок начинался с заголовка (HTML h1-h6 или markdown #).</summary>
    private static bool IsHeading(string block)
        => block.Contains(HeadingMark, StringComparison.Ordinal);

    /// <summary>
    /// Насколько блок похож на факт: даты, числа, проценты, курсы. Значение — сумма попаданий
    /// (дата весит больше числа). Используется как приоритет укладки в бюджет промпта.
    /// </summary>
    private static int FactScore(string text)
    {
        int score = 0;

        if (FactDateRegex.IsMatch(text)) score += 10;

        // Числа считаем фактом, только если это не «(2026)» в названии игры и не код
        // товара: дробные величины и суммы с валютой/процентом — да, одиночный год — нет.
        if (FactValueRegex.IsMatch(text)) score += 6;
        if (text.Contains('%') || text.Contains('₽') || text.Contains('$') || text.Contains('€')) score += 3;

        // «сегодня/вчера/вышел/представлен» — прямое указание на событие.
        if (text.Contains("сегодня", StringComparison.OrdinalIgnoreCase)
            || text.Contains("вчера", StringComparison.OrdinalIgnoreCase)) score += 3;

        // Каталог названий без единой даты и без значений — бесполезен для ответа:
        // уводим его в конец очереди, чтобы он не занял бюджет раньше реальных фактов.
        if (score == 0) score -= 5;

        return score;
    }

    /// <summary>Дата в тексте: «19.11.2026», «19 ноября 2026», «2026-11-19».</summary>
    private static readonly Regex FactDateRegex = new(
        @"\b\d{1,2}[./]\d{1,2}[./]\d{2,4}\b|\b\d{4}-\d{2}-\d{2}\b|" +
        @"\b\d{1,2}\s+(?:январ|феврал|март|апрел|ма[йя]|июн|июл|август|сентябр|октябр|ноябр|декабр)\w*\s+\d{4}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Значение-факт: денежная сумма, процент, измерение, дробное число.
    /// Одиночный год в скобках «(2026)» и «100 Hidden Cats» сюда не попадают — это названия.
    /// </summary>
    private static readonly Regex FactValueRegex = new(
        @"\d+[.,]\d+|\b\d{2,}\s*(?:%|₽|руб|долл|евро|млн|млрд|км|кг|чел|шт)|\b\d{1,3}(?:[ \u00A0]\d{3})+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Календарная сетка ридера: почти нет букв, но много одиночных чисел через «|».
    /// Такой блок фактов не несёт, зато занимает сотни символов бюджета.
    /// </summary>
    private static bool IsCalendarGrid(string text)
    {
        int letters = 0;
        int digits = 0;
        int pipes = 0;
        foreach (char ch in text)
        {
            if (char.IsLetter(ch)) letters++;
            else if (char.IsDigit(ch)) digits++;
            else if (ch == '|') pipes++;
        }

        // Сетка таблицы: разделителей не меньше, чем букв, а чисел не меньше, чем букв.
        // Строка курса («| 840 | USD | 1 | Доллар США | 82,2281 |») под это не попадает:
        // в ней букв больше, чем разделителей, и она несёт сам факт.
        if (pipes >= 6 && pipes >= letters && digits >= letters) return true;

        if (letters >= MinFragmentChars) return false;

        // Мало букв и числовая плотность высокая — это сетка-календарь или таблица кодов.
        return digits >= 8 && digits > letters * 2;
    }

    /// <summary>
    /// Служебная строка: меню, кнопка, копирайт, дубль заголовка сайта.
    /// Проверяется построчно: короткая служебная фраза не должна утянуть за собой соседний факт.
    /// </summary>
    private static bool IsNoiseLine(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (string marker in NoiseMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal)) return true;
        }

        // Строка без букв и цифр («| — •») полезной информации не несёт.
        if (!text.Any(char.IsLetterOrDigit)) return true;

        // «GTA 6GTA 6 Патч 7.42Патч 7.42 МатчиМатчи» — склейка соседних пунктов меню:
        // ридер потерял переводы строк, и одно и то же название повторено дважды подряд.
        return DuplicateWordRegex.IsMatch(text);
    }

    /// <summary>Повтор слова подряд («Патч Патч», «МатчиМатчи») без пробела или через пробел.</summary>
    private static readonly Regex DuplicateWordRegex = new(
        @"(?<w>[A-Za-zА-Яа-я]{3,})\1",
        RegexOptions.Compiled);
}
