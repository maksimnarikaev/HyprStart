using HyprStart.Core.Settings;
using HyprStart.Core.Storage;
using Xunit;

namespace HyprStart.Core.Tests;

public class DefaultPinsTests
{
    private static PinnedItem Item(string name, string target, int kind = 0)
        => new() { Name = name, Target = target, Kind = kind };

    [Fact]
    public void Catalog_HasNoDuplicateNames()
    {
        var names = DefaultPins.Catalog.Select(c => c.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalog_CoversBasicAppsForCleanWindows()
    {
        // Переносимый лаунчер обязан предлагать хоть что-то из коробки.
        var names = DefaultPins.Catalog.Select(c => c.Name).ToList();

        Assert.Contains("Проводник", names);
        Assert.Contains(DefaultPins.Catalog, c => c.IsTerminal);
        Assert.Contains(DefaultPins.Catalog, c => c.ExeNames.Any(e => e.Equals("steam", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Catalog_PrioritiesAreOrderedGroups()
    {
        Assert.All(DefaultPins.Catalog, c => Assert.InRange(c.Priority, 0, 3));
    }

    [Fact]
    public void Build_EmptyPool_ReturnsEmptyList()
    {
        Assert.Empty(DefaultPins.Build(null));
        Assert.Empty(DefaultPins.Build(new List<PinnedItem>()));
    }

    [Fact]
    public void Build_IgnoresItemsWithEmptyTarget()
    {
        var pool = new List<PinnedItem>
        {
            Item("Пусто", "   "),
            Item("Тоже пусто", ""),
        };

        Assert.Empty(DefaultPins.Build(pool));
    }

    [Fact]
    public void Build_PutsCatalogMatchesFirst()
    {
        // Порядок в индексе «Пуск» алфавитный, но закреплённое должно начинаться с браузера/терминала.
        var pool = new List<PinnedItem>
        {
            Item("AAA Прочее", @"C:\Tools\aaa.exe"),
            Item("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            Item("БББ Прочее", @"C:\Tools\bbb.exe"),
        };

        var pins = DefaultPins.Build(pool);

        Assert.NotEmpty(pins);
        Assert.Equal("Google Chrome", pins[0].Name);
    }

    [Fact]
    public void Build_DeduplicatesByTarget()
    {
        var pool = new List<PinnedItem>
        {
            Item("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            Item("Chrome (копия)", @"c:\program files\google\chrome\application\chrome.exe"),
        };

        var pins = DefaultPins.Build(pool);

        Assert.Single(pins);
    }

    [Fact]
    public void Build_RespectsMaxPins()
    {
        var pool = Enumerable.Range(0, 40)
            .Select(i => Item($"App{i}", $@"C:\Tools\app{i}.exe"))
            .ToList();

        Assert.Equal(5, DefaultPins.Build(pool, maxPins: 5).Count);
        Assert.True(DefaultPins.Build(pool).Count <= DefaultPins.MaxPins);
    }

    [Fact]
    public void Build_FillsFromOtherShortcuts_WhenCatalogHasNoMatch()
    {
        // На чистой Windows без браузеров из списка панель не должна пустовать.
        var pool = new List<PinnedItem>
        {
            Item("Мой калькулятор", @"C:\Tools\calc.exe"),
            Item("Своя программа", @"C:\Tools\mine.exe"),
        };

        var pins = DefaultPins.Build(pool);

        Assert.Equal(2, pins.Count);
    }

    [Fact]
    public void BuildFrom_KeepsGroupAndKind()
    {
        var installed = new List<DefaultPins.InstalledApp>
        {
            new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 0, "Интернет"),
        };

        var pins = DefaultPins.BuildFrom(installed);

        Assert.Single(pins);
        Assert.Equal("Интернет", pins[0].Group);
        Assert.Equal(0, pins[0].Kind);
    }

    [Fact]
    public void BuildFrom_EmptyName_UsesExeName()
    {
        var installed = new List<DefaultPins.InstalledApp>
        {
            new("   ", @"C:\Tools\my-app.exe", 0, null),
        };

        var pins = DefaultPins.BuildFrom(installed);

        Assert.Single(pins);
        Assert.Equal("my-app", pins[0].Name);
    }

    [Fact]
    public void BuildFrom_SkipsNullsAndEmptyTargets()
    {
        var installed = new List<DefaultPins.InstalledApp>
        {
            null!,
            new("Пустая цель", "  ", 0, null),
            new("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 0, null),
        };

        var pins = DefaultPins.BuildFrom(installed);

        Assert.Single(pins);
    }

    [Fact]
    public void BuildFrom_OrdersCatalogMatchesFirst()
    {
        var installed = new List<DefaultPins.InstalledApp>
        {
            new("AAA", @"C:\Tools\aaa.exe", 0, null),
            new("Discord", @"C:\Users\U\AppData\Local\Discord\Update.exe", 0, null),
        };

        var pins = DefaultPins.BuildFrom(installed);

        Assert.Equal("Discord", pins[0].Name);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --flag", "app")]
    [InlineData("C:/Program Files/App/app.exe", "app")]
    [InlineData("explorer", "explorer")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ExtractExeName_HandlesQuotesSlashesAndBareCommands(string? target, string expected)
    {
        Assert.Equal(expected, DefaultPins.ExtractExeName(target));
    }

    [Fact]
    public void ExtractExeName_UriProtocol_ReturnsEmpty()
    {
        // AUMID/протокол не файл - из него имя exe не вытащить.
        Assert.Equal(string.Empty, DefaultPins.ExtractExeName("https://example.com/app"));
    }

    [Fact]
    public void ExtractExeName_StripsArgumentsWithoutQuotes()
    {
        Assert.Equal("steam", DefaultPins.ExtractExeName(@"C:\Steam\steam.exe -silent"));
    }

    [Fact]
    public void Matches_ByShortcutAliasAndByExe()
    {
        var explorer = DefaultPins.Catalog.Single(c => c.IsExplorer);
        Assert.True(DefaultPins.Matches(Item("File Explorer", @"C:\Windows\explorer.exe"), explorer));

        var steam = DefaultPins.Catalog.Single(c => c.ExeNames.Contains("steam"));
        Assert.True(DefaultPins.Matches(Item("Не важно", @"C:\Program Files (x86)\Steam\steam.exe"), steam));
    }

    [Fact]
    public void Matches_DoesNotMatchUnrelatedApp()
    {
        var steam = DefaultPins.Catalog.Single(c => c.ExeNames.Contains("steam"));

        Assert.False(DefaultPins.Matches(Item("Блокнот", @"C:\Windows\notepad.exe"), steam));
    }

    [Fact]
    public void Matches_TerminalAcceptsPowerShellAndWindowsTerminal()
    {
        var terminal = DefaultPins.Catalog.Single(c => c.IsTerminal);

        Assert.True(DefaultPins.Matches(Item("x", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"), terminal));
        Assert.True(DefaultPins.Matches(Item("x", @"C:\Windows\wt.exe"), terminal));
        Assert.False(DefaultPins.Matches(Item("x", @"C:\Windows\notepad.exe"), terminal));
    }

    [Fact]
    public void IsUsableShortcut_NonShortcutTargetsAreUsable()
    {
        Assert.True(DefaultPins.IsUsableShortcut(@"C:\Windows\notepad.exe", _ => false));
        Assert.True(DefaultPins.IsUsableShortcut("https://example.com/app", _ => false));
        Assert.False(DefaultPins.IsUsableShortcut("   ", _ => true));
    }

    [Fact]
    public void IsUsableShortcut_DeadLinkFile_IsRejected()
    {
        const string dead = @"C:\Start Menu\Удалённая программа.lnk";

        Assert.False(DefaultPins.IsUsableShortcut(dead, _ => false));
        Assert.True(DefaultPins.IsUsableShortcut(dead, _ => true));
    }
}