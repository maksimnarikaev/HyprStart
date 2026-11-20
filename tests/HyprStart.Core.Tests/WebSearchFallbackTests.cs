using HyprStart.Core.Chat;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Резервная выдача DuckDuckGo HTML через r.jina.ai: заголовок-ссылка «## [Текст](url)»,
/// затем строка с доменом сайта и следом сниппет. Основной lite-домен периодически
/// закрывается 403, поэтому этот формат должен разбираться отдельно.
/// </summary>
public class WebSearchFallbackTests
{
    // Реальный фрагмент ответа r.jina.ai на duckduckgo.com/html (запрос «курс доллара»).
    private const string SampleHtmlSearch = """
        Title: курс доллара at DuckDuckGo

        URL Source: https://duckduckgo.com/html/?q=курс+доллара

        Markdown Content:
        [](https://html.duckduckgo.com/html/?q=курс+доллара)

        ## [Официальные курсы валют на заданную дату](https://duckduckgo.com/l/?uddg=https%3A%2F%2Fcbr.ru%2Fcurrency_base%2Fdaily%2F&rut=abc)

        [![Image 1](https://external-content.duckduckgo.com/ip3/cbr.ru.ico)](https://duckduckgo.com/l/?uddg=x)[cbr.ru/currency_base/daily/](https://duckduckgo.com/l/?uddg=x)

        [28.10.2026 **курс****доллара** США 80,5307 к рублю. Данные ЦБ РФ.](https://duckduckgo.com/l/?uddg=x)

        ## [Курс доллара на 19.11.2026 в Украине - Минфин](https://duckduckgo.com/l/?uddg=https%3A%2F%2Fminfin.com.ua%2Fcurrency%2Fusd%2F)

        [![Image 2](https://external-content.duckduckgo.com/ip3/minfin.com.ua.ico)](https://duckduckgo.com/l/?uddg=y)[minfin.com.ua/currency/usd/](https://duckduckgo.com/l/?uddg=y)

        [НБУ: покупка 45,28841, продажа 45,96582. Межбанк: покупка 45,3850.](https://duckduckgo.com/l/?uddg=y)
        """;

    [Fact]
    public void ParseHeadingResults_TakesTitleSnippetAndRealUrl()
    {
        var results = WebSearch.ParseHeadingResults(SampleHtmlSearch);

        Assert.Equal(2, results.Count);
        Assert.Contains("Официальные курсы валют", results[0].Title);
        Assert.Contains("80,5307", results[0].Snippet);

        // Редирект DDG разворачивается в адрес самого сайта — иначе страницу не выкачать.
        Assert.Equal("https://cbr.ru/currency_base/daily/", results[0].Url);
        Assert.Equal("https://minfin.com.ua/currency/usd/", results[1].Url);
        Assert.Contains("45,28841", results[1].Snippet);
    }

    [Fact]
    public void ParseHeadingResults_SkipsDomainCaptionAsSnippet()
    {
        var results = WebSearch.ParseHeadingResults(SampleHtmlSearch);

        // Строка «cbr.ru/currency_base/daily/» — подпись, а не факт.
        Assert.DoesNotContain(results, r => r.Snippet.StartsWith("cbr.ru", StringComparison.Ordinal));
        Assert.All(results, r => Assert.False(r.Snippet.Contains("external-content", StringComparison.Ordinal)));
    }

    [Fact]
    public void ParseHeadingResults_StripsBoldMarkersWithoutGlueingWords()
    {
        var results = WebSearch.ParseHeadingResults(SampleHtmlSearch);

        // «**курс****доллара**» не должно оставить звёздочек и не должно склеить слова.
        Assert.DoesNotContain("**", results[0].Snippet);
        Assert.Contains("курс доллара", results[0].Snippet);
    }

    [Fact]
    public void ParseHeadingResults_EmptyForGarbageOrNull()
    {
        Assert.Empty(WebSearch.ParseHeadingResults(null));
        Assert.Empty(WebSearch.ParseHeadingResults("   "));
        Assert.Empty(WebSearch.ParseHeadingResults("просто текст без ссылок и заголовков"));
    }

    [Fact]
    public void BuildFallbackUrl_UsesHtmlDuckDuckGoAndEscapesQuery()
    {
        string url = WebSearch.BuildFallbackUrl("курс доллара");

        Assert.StartsWith(WebSearch.FallbackEndpoint, url);
        Assert.Contains("duckduckgo.com/html/?q=", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void UnwrapRedirect_ReturnsOriginalUrlOrEmpty()
    {
        Assert.Equal(
            "https://cbr.ru/",
            WebSearch.UnwrapRedirect("https://duckduckgo.com/l/?uddg=https%3A%2F%2Fcbr.ru%2F&rut=1"));

        // Голый адрес сайта возвращается как есть.
        Assert.Equal("https://example.com/a", WebSearch.UnwrapRedirect("https://example.com/a"));

        // Не-http мусор отбрасываем: иначе ридер дёрнет неизвестную схему.
        Assert.Equal(string.Empty, WebSearch.UnwrapRedirect("javascript:alert(1)"));
        Assert.Equal(string.Empty, WebSearch.UnwrapRedirect(null));
    }
}
