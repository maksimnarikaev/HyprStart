using HyprStart.Core.Chat;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Страницы-справочники (курсы, календари, статистика) отдают сначала сетку-таблицу
/// и перечень названий, а сам факт лежит ниже. Бюджет из 1500 символов должен
/// доставать до значения, иначе модель не сможет назвать цифру.
/// </summary>
public class WebPageExtractorPriorityTests
{
    [Fact]
    public void Extract_DropsCalendarGrid()
    {
        var page = new System.Text.StringBuilder();
        page.AppendLine("Официальные курсы валют");
        page.AppendLine();
        page.AppendLine("| Пн | Вт | Ср | Чт | Пт | Сб | Вс |");
        page.AppendLine("| 1 | 2 | 3 | 4 | 5 | 6 | 7 |");
        page.AppendLine("| 8 | 9 | 10 | 11 | 12 | 13 | 14 |");
        page.AppendLine();
        page.AppendLine("Центральный банк установил с 19.11.2026 курс доллара США 82,2281 рубля.");

        string text = WebPageExtractor.Extract(page.ToString());

        Assert.DoesNotContain("| 8 | 9 | 10 |", text);
        Assert.Contains("82,2281", text);
    }

    [Fact]
    public void Extract_KeepsFactValueAheadOfPlainList()
    {
        var page = new System.Text.StringBuilder();
        page.AppendLine("Dota 2 CS2 Мир танков ГТА 6 Патч 7.42 Матчи Турниры Игры Обзоры Новости");
        page.AppendLine();
        page.AppendLine("Курс доллара США на 19.11.2026 составил 82,2281 рубля, евро — 90,3605 рубля.");

        string text = WebPageExtractor.Extract(page.ToString(), maxChars: 120);

        Assert.Contains("82,2281", text);
    }

    [Fact]
    public void Extract_SkipsReaderMetadataTitle()
    {
        var page = new System.Text.StringBuilder();
        page.AppendLine("Title: Курс доллара к рублю");
        page.AppendLine("URL Source: https://www.banki.ru/products/currency/usd/");
        page.AppendLine("Markdown Content:");
        page.AppendLine();
        page.AppendLine("Курс доллара США к рублю по данным Центробанка за неделю и прогноз на завтра.");
        page.AppendLine();
        page.AppendLine("1 USD = 82,01 RUB, изменение за сутки -1,00 RUB.");

        string text = WebPageExtractor.Extract(page.ToString());

        Assert.DoesNotContain("URL Source:", text);
        Assert.DoesNotContain("Markdown Content", text);
        Assert.Contains("82,01", text);
    }

    [Fact]
    public void Extract_KeepsOrderSoFirstParagraphSurvives()
    {
        // Явные переводы строк, а не AppendLine: Environment.NewLine на Windows даёт CRLF,
        // и тест зависел бы от платформы (именно так он и разошёлся локально и в CI).
        string page = "# Свежий релиз\n\n"
            + "Патч 2.0 вышел 19 ноября 2026 года и добавил четыре новых региона.\n\n"
            + "Разработчики обещают поддержку модов и редактор уровней в декабре.\n";

        string text = WebPageExtractor.Extract(page);

        // Заголовок сохраняется в выдаче. Порядок не фиксируем: блоки с признаками факта
        // (дата в «Патч 2.0 …») намеренно поднимаются выше, это часть контракта Extract.
        Assert.Contains("Свежий релиз", text);
        Assert.True(
            text.IndexOf("Патч 2.0", System.StringComparison.Ordinal) <
            text.IndexOf("модов", System.StringComparison.Ordinal),
            "порядок абзацев страницы должен сохраняться");
    }

    [Fact]
    public void Extract_StubPageWithoutFactsStillReturnsText()
    {
        string page = "Короткая заметка без цифр и дат, но с осмысленным текстом о правилах обмена.";

        string text = WebPageExtractor.Extract(page);

        Assert.Contains("правилах обмена", text);
    }
}
