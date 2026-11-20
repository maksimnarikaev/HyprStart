using System.Globalization;
using HyprStart.Core.Chat;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Разбор чистого текста r.jina.ai (Markdown-выдача DuckDuckGo Lite) и решение «нужен ли поиск».
/// </summary>
public class WebSearchTests
{
    // Так выглядит ответ r.jina.ai на lite.duckduckgo.com: нумерованный список ссылок,
    // под каждой — абзац со сниппетом. Разметки и классов нет, только markdown.
    private const string SampleText = """
        Title: курс доллара сегодня - DuckDuckGo Lite

        [1. Курс доллара **сегодня**](https://duckduckgo.com/l/?uddg=x)
        Официальный курс USD к рублю на 19.11.2026 & прогноз ЦБ.

        [2. Погода во Владикавказе](https://duckduckgo.com/l/?uddg=y)
        Сейчас +7°C, облачно, ветер 3 м/с.

        [3. Новости технологий](https://duckduckgo.com/l/?uddg=z)
        Главное за день: релиз .NET и другие события.

        [4. Четвёртый результат](https://duckduckgo.com/l/?uddg=w)
        Попадает в выдачу: лимит теперь пять результатов.
        """;

    [Fact]
    public void ParseResults_TakesTitlesSnippetsAndUrls()
    {
        var results = WebSearch.ParseResults(SampleText);

        Assert.Equal(4, results.Count);
        Assert.Contains("Курс доллара", results[0].Title);
        Assert.Contains("сегодня", results[0].Title);
        Assert.Contains("прогноз", results[0].Snippet);
        Assert.Contains("Погода во Владикавказе", results[1].Title);
        Assert.Contains("Новости технологий", results[2].Title);

        // Ссылка нужна, чтобы затем выкачать саму страницу: без неё модель видит только заголовок.
        Assert.Contains("uddg=x", results[0].Url);
        Assert.False(string.IsNullOrEmpty(results[3].Url));
    }

    [Fact]
    public void ParseResults_StripsMarkdownAndKeepsText()
    {
        var results = WebSearch.ParseResults(SampleText);

        // **жирный** и [текст](url) не должны остаться в выводе, а сам текст — остаться.
        Assert.DoesNotContain("**", results[0].Title);
        Assert.DoesNotContain("](", results[0].Title);
        Assert.Contains("сегодня", results[0].Title);
        Assert.Contains("прогноз", results[0].Snippet);
        Assert.Contains("+7°C", results[1].Snippet);
        Assert.Contains("релиз", results[2].Snippet);
    }

    [Fact]
    public void ParseResults_CollapsesWhitespace()
    {
        var results = WebSearch.ParseResults(SampleText);

        Assert.DoesNotContain("  ", results[0].Title);
        Assert.Equal(results[0].Title.Trim(), results[0].Title);
    }

    [Fact]
    public void ParseResults_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(WebSearch.ParseResults(null));
        Assert.Empty(WebSearch.ParseResults(string.Empty));
        Assert.Empty(WebSearch.ParseResults("Просто текст без ссылок и заголовков."));
    }

    [Fact]
    public void ParseResults_SkipsResultWithoutSnippet()
    {
        const string text = """
            [1. Только заголовок без описания](https://duckduckgo.com/l/?uddg=1)

            [2. Второй заголовок](https://duckduckgo.com/l/?uddg=2)
            Описание второго результата.
            """;

        var results = WebSearch.ParseResults(text);

        Assert.Single(results);
        Assert.Equal("Второй заголовок", results[0].Title);
    }

    [Fact]
    public void BuildUrl_UsesJinaReaderOverLiteDuckDuckGo()
    {
        string url = WebSearch.BuildUrl("курс доллара");

        Assert.StartsWith("https://r.jina.ai/http://lite.duckduckgo.com/lite/?q=", url);
    }

    [Fact]
    public void WeatherUrl_PointsToOpenMeteoWithCurrentFields()
    {
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?", WebSearch.WeatherUrl);
        Assert.Contains("current=temperature_2m,apparent_temperature,wind_speed_10m", WebSearch.WeatherUrl);
    }

    [Fact]
    public void WeatherUrl_DefaultIsNeutral_NotTiedToAuthorsCity()
    {
        // Портативность: дефолт не должен быть координатами Владикавказа (43.02, 44.68).
        Assert.DoesNotContain("latitude=43.02", WebSearch.WeatherUrl);
        Assert.DoesNotContain("longitude=44.68", WebSearch.WeatherUrl);
        Assert.Contains("latitude=" + WebSearch.DefaultWeatherLatitude.ToString(CultureInfo.InvariantCulture), WebSearch.WeatherUrl);
        Assert.Contains("longitude=" + WebSearch.DefaultWeatherLongitude.ToString(CultureInfo.InvariantCulture), WebSearch.WeatherUrl);
    }

    [Theory]
    [InlineData(55.7887, 49.1221)]
    [InlineData(-33.8688, 151.2093)]
    [InlineData(59.9343, 30.3351)]
    public void WeatherUrlFor_UsesConfiguredCoordinates(double latitude, double longitude)
    {
        string url = WebSearch.WeatherUrlFor(latitude, longitude);

        Assert.Contains("latitude=" + latitude.ToString(CultureInfo.InvariantCulture), url);
        Assert.Contains("longitude=" + longitude.ToString(CultureInfo.InvariantCulture), url);
    }

    [Fact]
    public void WeatherUrlFor_UsesInvariantCultureDecimalPoint()
    {
        // Иначе в русской локали в URL уехала бы запятая вместо точки.
        string url = WebSearch.WeatherUrlFor(55.5, 37.25);

        Assert.Contains("latitude=55.5", url);
        Assert.Contains("longitude=37.25", url);
    }

    [Fact]
    public void IsWeatherQuery_DetectsWeatherQuestions()
    {
        Assert.True(WebSearch.IsWeatherQuery("какая сегодня погода"));
        Assert.True(WebSearch.IsWeatherQuery("температура воздуха"));
        Assert.False(WebSearch.IsWeatherQuery("курс доллара"));
        Assert.False(WebSearch.IsWeatherQuery(null));
    }

    [Theory]
    [InlineData("/web курс доллара", "курс доллара", true)]
    [InlineData("/WEB   курс доллара  ", "курс доллара", true)]
    [InlineData("/web", "", true)]
    public void Parse_CommandPrefix_IsRemoved(string input, string expectedQuery, bool expectedSearch)
    {
        var (query, needsSearch) = WebSearch.Parse(input);

        Assert.Equal(expectedQuery, query);
        Assert.Equal(expectedSearch, needsSearch);
    }

    [Theory]
    [InlineData("какая сегодня погода", true)]
    [InlineData("курс доллара", true)]
    [InlineData("что сейчас с рублём", true)]
    [InlineData("новости за неделю", true)]
    [InlineData("сколько стоит подписка, цена", true)]
    [InlineData("что нового в 2026", true)]
    [InlineData("объясни разницу между TCP и UDP", false)]
    [InlineData("напиши функцию на C# для сортировки", false)]
    public void Parse_FreshMarkers_TriggerSearch(string input, bool expected)
    {
        var (_, needsSearch) = WebSearch.Parse(input);

        Assert.Equal(expected, needsSearch);
    }

    [Fact]
    public void Parse_NoCommandNoMarker_KeepsQueryWithoutSearch()
    {
        var (query, needsSearch) = WebSearch.Parse("привет, как дела");

        Assert.Equal("привет, как дела", query);
        Assert.False(needsSearch);
    }

    [Fact]
    public void Parse_SimilarWordIsNotCommand()
    {
        // «/вебмастер» не должен считаться командой /web.
        var (query, needsSearch) = WebSearch.Parse("/webмастер помоги");

        Assert.Equal("/webмастер помоги", query);
        Assert.False(needsSearch);
    }

    [Fact]
    public void BuildUrl_EscapesQuery()
    {
        string url = WebSearch.BuildUrl("курс доллара & евро");

        Assert.StartsWith(WebSearch.Endpoint, url);
        Assert.Contains("%20", url);
        Assert.DoesNotContain(" ", url);
        Assert.DoesNotContain("&", url[WebSearch.Endpoint.Length..]);
    }

    [Fact]
    public void BuildContextPrompt_ListsAllResultsAndKeepsFillers()
    {
        var results = new[]
        {
            new WebSearchResult("Заголовок 1", "Сниппет 1"),
            new WebSearchResult("Заголовок 2", "Сниппет 2"),
        };

        string prompt = WebSearch.BuildContextPrompt("погода", results);

        Assert.Contains("погода", prompt);
        Assert.Contains("1) Заголовок 1 — Сниппет 1", prompt);
        Assert.Contains("2) Заголовок 2 — Сниппет 2", prompt);
    }

    [Fact]
    public void BuildContextPrompt_EmptyResults_AddsNothing()
    {
        Assert.Equal(string.Empty, WebSearch.BuildContextPrompt("погода", Array.Empty<WebSearchResult>()));
    }
}
