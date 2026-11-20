using HyprStart.Core.Chat;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Реальные страницы начинаются с шапки и мега-меню: у ридера до фактов бывает
/// 10 000+ символов ссылок. Без отрезания шапки бюджет уходит на меню, и модель
/// отвечает «вот сайты» вместо конкретных цифр.
/// </summary>
public class WebPageExtractorNavigationTests
{
    /// <summary>Шапка: длинное меню ссылками, затем заголовок и факты.</summary>
    private static string BuildPageWithHeavyMenu()
    {
        // Явный \n вместо AppendLine: Environment.NewLine на Windows даёт CRLF, и тест
        // зависел бы от платформы — именно так он и расходился локально и в CI.
        var menu = new System.Text.StringBuilder();
        menu.Append("Title: Курс доллара\n");
        menu.Append("\n");
        for (int i = 0; i < 40; i++)
        {
            menu.Append($"*   [Пункт меню номер {i} с длинным текстом](https://site.ru/menu/{i}) " +
                        $"[Ещё один пункт](https://site.ru/extra/{i})\n");
            menu.Append("\n");
        }

        menu.Append("# Курс доллара США к рублю\n");
        menu.Append("\n");
        menu.Append("Сегодня 1 USD = 82,01 RUB, динамика за месяц -1,45 RUB.\n");
        menu.Append("\n");
        menu.Append("Данные ЦБ РФ на 19 ноября 2026 года.\n");
        return menu.ToString();
    }

    [Fact]
    public void Extract_SkipsLongMenuAndReachesFacts()
    {
        string page = BuildPageWithHeavyMenu();
        Assert.True(page.Length > 4000, "шапка должна быть длинной — иначе тест ничего не проверяет");

        string text = WebPageExtractor.Extract(page);

        // Факты ниже меню доходят до контекста.
        Assert.Contains("82,01", text);
        Assert.Contains("19 ноября 2026", text);
        Assert.Contains("ЦБ РФ", text);
    }

    [Fact]
    public void Extract_DropsLinkHeavyMenuLines()
    {
        string text = WebPageExtractor.Extract(BuildPageWithHeavyMenu());

        // Ни один пункт меню не должен просочиться в контекст.
        Assert.DoesNotContain("Пункт меню номер", text);
        Assert.DoesNotContain("site.ru/menu", text);
    }

    [Fact]
    public void Extract_ShortPrefixIsKept()
    {
        // Короткая шапка (меньше порога) не считается навигацией: заголовок и факт на месте.
        const string page = """
            # Патч 1.4.2 вышел 18 ноября 2026 года

            Разработчик добавил 12 новых карт и режим кооператива на четверых.
            """;

        string text = WebPageExtractor.Extract(page);

        Assert.Contains("18 ноября 2026", text);
        Assert.Contains("12 новых карт", text);
    }

    [Fact]
    public void Extract_RemovesMarkdownImages()
    {
        const string page = """
            # Курс доллара

            ![Image 10: синяя монета](https://site.ru/coin.png)

            Сегодня 1 USD = 82,01 RUB по данным ЦБ РФ.
            """;

        string text = WebPageExtractor.Extract(page);

        Assert.Contains("82,01", text);
        Assert.DoesNotContain("Image 10", text);
        Assert.DoesNotContain("coin.png", text);
    }

    [Fact]
    public void Extract_WithoutAnyHeadingStillReturnsFacts()
    {
        // Нет заголовка — шапку не режем, иначе можно потерять данные.
        const string page = """
            Обычный текст без заголовков.

            Курс доллара на сегодня составляет 82,01 рубля за доллар.
            """;

        string text = WebPageExtractor.Extract(page);

        Assert.Contains("82,01", text);
    }

    [Fact]
    public void LooksLikeStub_RejectsReaderPlaceholders()
    {
        // Кэш-снимок с отказом источника: у ридера это «успешный» ответ без фактов.
        const string denied = """
            Title: Курс валют
            URL Source: https://vbr.ru/banki/kurs-valut/cbrf/usd/
            Warning: This is a cached snapshot of the original page.
            403. доступ заблокирован
            """;
        Assert.True(WebPageExtractor.LooksLikeStub(denied));

        Assert.True(WebPageExtractor.LooksLikeStub("AbuseAlleviationError: anonymous access blocked"));
        Assert.True(WebPageExtractor.LooksLikeStub(string.Empty));
        Assert.True(WebPageExtractor.LooksLikeStub(null));
    }

    [Fact]
    public void LooksLikeStub_AcceptsRealContent()
    {
        const string page = """
            Title: Курс доллара США к рублю
            URL Source: https://www.banki.ru/products/currency/usd/
            Markdown Content:
            Сегодня 1 USD = 82,01 RUB по данным Центробанка России на 19 ноября 2026 года.
            """;
        Assert.False(WebPageExtractor.LooksLikeStub(page));

        // Слово-маркер глубоко в тексте — это уже статья, а не заглушка ридера.
        string deep = new string('a', 1000) + " 403 forbidden captcha";
        Assert.False(WebPageExtractor.LooksLikeStub(deep));
    }
}
