using HyprStart.Core.Chat;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Извлечение фактов со страницы: из markdown-ответа ридера и из сырого HTML нужно
/// получить абзацы с датами/цифрами, выкинув навигацию, скрипты и куки-баннеры.
/// </summary>
public class WebPageExtractorTests
{
    private const string SampleMarkdown = """
        Title: Игра вышла вчера

        [Skip to content](#main)

        ## Свежий релиз состоялся 18 ноября 2026 года

        Разработчик выпустил патч 1.4.2, добавивший 12 новых карт и режим кооператива.
        Обновление весит 3,7 ГБ и уже доступно в Steam.

        [Подписаться на новости](https://site/news)

        Отзывы игроков в основном положительные: 87% из 4 500 обзоров.
        """;

    [Fact]
    public void Extract_KeepsFactsAndDropsNavigation()
    {
        string text = WebPageExtractor.Extract(SampleMarkdown);

        // Факты с датами и цифрами сохраняются.
        Assert.Contains("18 ноября 2026", text);
        Assert.Contains("1.4.2", text);
        Assert.Contains("3,7 ГБ", text);
        Assert.Contains("87%", text);

        // Навигация и призывы к действию вырезаны.
        Assert.DoesNotContain("Skip to content", text);
        Assert.DoesNotContain("Подписаться", text);
        Assert.DoesNotContain("]", text);
        Assert.DoesNotContain("http", text);
    }

    [Fact]
    public void Extract_StripsHtmlBoilerplate()
    {
        const string html = """
            <html><head><title>Новости</title><style>.a{color:red}</style></head>
            <body>
              <nav><a href="/">Главная</a><a href="/news">Новости</a></nav>
              <script>var x = 1;</script>
              <article>
                <h1>Патч 2.0 вышел 19 ноября 2026 года</h1>
                <p>Студия добавила 8 новых персонажей и переработала баланс оружия.</p>
              </article>
              <footer>&copy; 2026 Все права защищены</footer>
            </body></html>
            """;

        string text = WebPageExtractor.Extract(html);

        Assert.Contains("19 ноября 2026", text);
        Assert.Contains("8 новых персонажей", text);

        Assert.DoesNotContain("var x", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("Все права защищены", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public void Extract_DeduplicatesRepeatedBlocks()
    {
        const string text = """
            Официальный курс доллара на сегодня составляет 92,4 рубля по данным ЦБ.

            Официальный курс доллара на сегодня составляет 92,4 рубля по данным ЦБ.
            """;

        string extracted = WebPageExtractor.Extract(text);

        // Один и тот же абзац не должен занимать бюджет дважды.
        int first = extracted.IndexOf("92,4", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.Equal(first, extracted.LastIndexOf("92,4", StringComparison.Ordinal));
    }

    /// <summary>
    /// Регресс: на CRLF-входе разделитель блоков "\n\n" не срабатывал, блоки сливались
    /// в один, и дедупликация молча выключалась — дубликат абзаца съедал бюджет промпта.
    /// Windows-раннер GitHub Actions разворачивал исходники с CRLF, и тест падал только в CI.
    /// </summary>
    [Fact]
    public void Extract_TreatsCrlfSameAsLf()
    {
        const string paragraph =
            "Официальный курс доллара на сегодня составляет 92,4 рубля по данным ЦБ.";

        string lf = paragraph + "\n\n" + paragraph;
        string crlf = paragraph + "\r\n\r\n" + paragraph;

        Assert.Equal(WebPageExtractor.Extract(lf), WebPageExtractor.Extract(crlf));

        // И дедупликация действительно работает, а не просто «оба варианта одинаково плохи».
        string extracted = WebPageExtractor.Extract(crlf);
        int first = extracted.IndexOf("92,4", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.Equal(first, extracted.LastIndexOf("92,4", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_HonoursMaxChars()
    {
        string longLine = new('а', 5000);
        string extracted = WebPageExtractor.Extract(longLine, maxChars: 300);

        Assert.True(extracted.Length <= 300, $"длина {extracted.Length} больше бюджета");
    }

    [Fact]
    public void Extract_EmptyForBlankOrStub()
    {
        Assert.Equal(string.Empty, WebPageExtractor.Extract(null));
        Assert.Equal(string.Empty, WebPageExtractor.Extract("   "));

        // Заглушка антибота: короткие служебные строки без фактов.
        Assert.Equal(string.Empty, WebPageExtractor.Extract("Проверка браузера... Cookie"));
    }
}
