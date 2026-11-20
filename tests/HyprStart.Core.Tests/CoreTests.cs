using HyprStart.Core.Input;
using HyprStart.Core.Library;
using HyprStart.Core.Layout;
using Xunit;

namespace HyprStart.Core.Tests;

public class WinKeyStateMachineTests
{
    private static WinKeyStateMachine NewMachine(bool pointerDown = false)
        => new(() => pointerDown);

    [Fact]
    public void TapWin_IsSuppressedAndOpens()
    {
        var machine = NewMachine();

        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyDown, WinKeyStateMachine.VkLWin, 1000));
        Assert.Equal(HookDecision.SuppressAndOpen, machine.Process(WinKeyStateMachine.WmKeyUp, WinKeyStateMachine.VkLWin, 1050));
        Assert.Equal(1, machine.SuppressedTaps);
    }

    [Fact]
    public void WinPlusKey_PassesThrough()
    {
        var machine = NewMachine();

        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyDown, WinKeyStateMachine.VkLWin, 1000));
        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyDown, 'E', 1010));
        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyUp, 'E', 1030));
        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyUp, WinKeyStateMachine.VkLWin, 1040));
        Assert.Equal(0, machine.SuppressedTaps);
    }

    [Fact]
    public void WinPlusMouseClick_IsNotTap()
    {
        var machine = NewMachine(pointerDown: true);

        machine.Process(WinKeyStateMachine.WmKeyDown, WinKeyStateMachine.VkLWin, 1000);
        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyUp, WinKeyStateMachine.VkLWin, 1020));
    }

    [Fact]
    public void StaleWinDown_DoesNotSwallowRelease()
    {
        var machine = NewMachine();

        machine.Process(WinKeyStateMachine.WmKeyDown, WinKeyStateMachine.VkLWin, 1000);
        // keyup пришёл через 6 секунд — считаем состояние потерянным, ввод не ломаем
        Assert.Equal(HookDecision.Pass, machine.Process(WinKeyStateMachine.WmKeyUp, WinKeyStateMachine.VkLWin, 7000));
    }

    [Fact]
    public void SecondTap_WorksAfterCombo()
    {
        var machine = NewMachine();

        machine.Process(WinKeyStateMachine.WmKeyDown, WinKeyStateMachine.VkLWin, 1000);
        machine.Process(WinKeyStateMachine.WmKeyDown, 'R', 1010);
        machine.Process(WinKeyStateMachine.WmKeyUp, 'R', 1020);
        machine.Process(WinKeyStateMachine.WmKeyUp, WinKeyStateMachine.VkLWin, 1030);

        machine.Process(WinKeyStateMachine.WmKeyDown, WinKeyStateMachine.VkLWin, 1100);
        Assert.Equal(HookDecision.SuppressAndOpen, machine.Process(WinKeyStateMachine.WmKeyUp, WinKeyStateMachine.VkLWin, 1120));
    }
}

public class HotkeySpecTests
{
    [Theory]
    [InlineData("Alt+Space", 0x20u)]
    [InlineData("Ctrl+Alt+H", 0x48u)]
    [InlineData("Shift+F12", 0x7Bu)]
    [InlineData("Win+Space", 0x20u)]
    public void ParsesValid(string text, uint expectedKey)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));
        Assert.Equal(expectedKey, spec.VirtualKey);
        Assert.True(spec.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alt+")]
    [InlineData("Space")]
    [InlineData("Alt+Ctrl")]
    [InlineData("Alt+Foo")]
    public void RejectsInvalid(string text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _));
    }
}

public class MathEvaluatorTests
{
    [Theory]
    [InlineData("2+2", "4")]
    [InlineData("2+2*2", "6")]
    [InlineData("(2+2)*2", "8")]
    [InlineData("10/4", "2.5")]
    [InlineData("-3 + 5", "2")]
    [InlineData("2,5 * 2", "5")]
    public void Evaluates(string input, string expected)
    {
        Assert.True(MathEvaluator.TryEvaluate(input, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("калькулятор")]
    [InlineData("42")]
    [InlineData("")]
    [InlineData("2+")]
    [InlineData("(2+2")]
    [InlineData("5/0")]
    public void RejectsNonMath(string input)
        => Assert.False(MathEvaluator.TryEvaluate(input, out _));
}

public class AppIndexBuilderTests
{
    private static AppEntry Shortcut(string name, string target, string? group = null)
        => new() { Name = name, Target = target, Kind = AppEntryKind.Shortcut, Group = group };

    [Theory]
    [InlineData("Uninstall Steam")]
    [InlineData("Документация")]
    [InlineData("Documentation")]
    [InlineData("Readme")]
    [InlineData("Справка")]
    [InlineData("Руководство пользователя")]
    [InlineData("Помощь")]
    [InlineData("Удалить HoYoPlay")]
    public void FiltersNoise(string name)
    {
        var result = AppIndexBuilder.Build(new[] { Shortcut(name, "a.lnk") }, Array.Empty<AppEntry>());
        Assert.Empty(result);
    }

    [Fact]
    public void RemovesDuplicateTargets()
    {
        var result = AppIndexBuilder.Build(
            new[] { Shortcut("Steam", "steam.lnk"), Shortcut("Steam", "STEAM.LNK") },
            Array.Empty<AppEntry>());

        Assert.Single(result);
    }

    [Fact]
    public void KeepsSameNameInDifferentFolders()
    {
        var result = AppIndexBuilder.Build(
            new[] { Shortcut("Tools", "a.lnk", "Папка A"), Shortcut("Tools", "b.lnk", "Папка B") },
            Array.Empty<AppEntry>());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void KeepsFolderAndNormalizesSearchKey()
    {
        var result = AppIndexBuilder.Build(new[] { Shortcut("Ёжик", "e.lnk", "Игры") }, Array.Empty<AppEntry>());

        var entry = Assert.Single(result);
        Assert.Equal("Игры", entry.Group);
        Assert.Equal("ежик", entry.SearchKey);
        Assert.Equal("игры", entry.GroupKey);
    }
}

public class AppTreeBuilderTests
{
    [Fact]
    public void MergesSameFoldersAndSortsRoot()
    {
        var entries = new[]
        {
            new AppEntry { Name = "Zeta", Target = "z", Kind = AppEntryKind.Shortcut },
            new AppEntry { Name = "Alpha", Target = "a", Kind = AppEntryKind.Shortcut },
            new AppEntry { Name = "Paint", Target = "p1", Kind = AppEntryKind.Shortcut, Group = "Tools" },
            new AppEntry { Name = "Calc", Target = "p2", Kind = AppEntryKind.Shortcut, Group = "tools" },
        };

        var nodes = AppTreeBuilder.Build(entries);

        Assert.Equal(3, nodes.Count);
        Assert.Equal("Alpha", nodes[0].Name);
        Assert.Equal("Tools", nodes[1].Name);
        Assert.True(nodes[1].IsFolder);
        Assert.Equal(2, nodes[1].Children.Count);
        Assert.Equal("Calc", nodes[1].Children[0].Name);
        Assert.Equal("Zeta", nodes[2].Name);
    }
}

public class AppSearchTests
{
    private static readonly List<AppEntry> Entries = new()
    {
        new AppEntry { Name = "Калькулятор", Target = "calc", Kind = AppEntryKind.Uwp, SearchKey = "калькулятор" },
        new AppEntry { Name = "Камера", Target = "cam", Kind = AppEntryKind.Uwp, SearchKey = "камера" },
        new AppEntry { Name = "Фотографии", Target = "photo", Kind = AppEntryKind.Uwp, SearchKey = "фотографии", GroupKey = "служебные" },
    };

    [Fact]
    public void EmptyQuery_ReturnsEverything()
        => Assert.Equal(3, AppSearch.Filter(Entries, "   ").Count);

    [Fact]
    public void PrefixRanksFirst()
    {
        var result = AppSearch.Filter(Entries, "кал");
        Assert.Equal("Калькулятор", result[0].Name);
    }

    [Fact]
    public void FindsInsideFoldersByGroupName()
    {
        var result = AppSearch.Filter(Entries, "служеб");
        Assert.Single(result);
        Assert.Equal("Фотографии", result[0].Name);
    }

    [Fact]
    public void SupportsSubsequence()
    {
        var result = AppSearch.Filter(Entries, "клк");
        Assert.Contains(result, entry => entry.Name == "Калькулятор");
    }
}

public class LayoutCalculatorTests
{
    [Fact]
    public void FullHd_LayoutIsConsistent()
    {
        var layout = LayoutCalculator.Compute(1920, 1080, LayoutSpec.Default);

        Assert.Equal(36, layout.Apps.X);
        Assert.Equal(36, layout.Apps.Y);
        Assert.True(layout.Apps.Width > 400);
        Assert.True(layout.Pinned.X > layout.Apps.Right);
        Assert.Equal(layout.Pinned.X, layout.Frequent.X);
        Assert.True(layout.Quick.X > layout.Frequent.Right);
        Assert.True(Math.Abs(layout.Polybar.Bottom - (1080 - 36)) < 0.01);
        Assert.All(new[] { layout.Apps, layout.Pinned, layout.Frequent, layout.Quick },
            rect => Assert.True(rect.Width > 0 && rect.Height > 0));
    }

    [Fact]
    public void TinyScreen_DoesNotProduceNegativeSizes()
    {
        var layout = LayoutCalculator.Compute(320, 200, LayoutSpec.Default);

        Assert.All(new[] { layout.Apps, layout.Pinned, layout.Frequent, layout.Quick, layout.Polybar },
            rect => Assert.True(rect.Width >= 0 && rect.Height >= 0));
    }

    [Fact]
    public void GapIsHonoured()
    {
        var spec = LayoutSpec.Default with { Gap = 15 };
        var layout = LayoutCalculator.Compute(1920, 1080, spec);

        Assert.Equal(15, layout.Pinned.X - layout.Apps.Right);
    }

    [Fact]
    public void PinnedAndChatSplitTopRightCellInHalf()
    {
        var spec = LayoutSpec.Default;
        var layout = LayoutCalculator.Compute(1920, 1080, spec);

        // Мини-чат стоит справа от закреплённого, на той же строке и с тем же gap.
        Assert.Equal(layout.Pinned.Y, layout.Chat.Y);
        Assert.Equal(layout.Pinned.Height, layout.Chat.Height);
        Assert.Equal(spec.Gap, layout.Chat.X - layout.Pinned.Right);

        // Половины равны (допуск на округление в 1 DIP).
        Assert.True(Math.Abs(layout.Pinned.Width - layout.Chat.Width) <= 1,
            $"pinned={layout.Pinned.Width} chat={layout.Chat.Width}");

        // Верхняя ячейка занята целиком: чат упирается в правый край контента.
        Assert.Equal(layout.Pinned.Right, layout.Pinned.X + layout.Pinned.Width);
        Assert.Equal(layout.Chat.X + layout.Chat.Width, layout.Chat.Right);
    }

    [Fact]
    public void TinyScreen_ChatDoesNotOverflow()
    {
        var layout = LayoutCalculator.Compute(320, 200, LayoutSpec.Default);

        Assert.True(layout.Chat.Width >= 0 && layout.Chat.Height >= 0);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1366, 768)]
    public void OverlaySize_IsOnePixelShorterThanScreen(double width, double height)
    {
        var (w, h) = LayoutCalculator.OverlaySize(width, height);

        // Ширина не режется — зазоров по бокам нет.
        Assert.Equal(width, w);
        // Ровно минус 1: окно перестаёт быть «полноэкранным» для Windows/Wallpaper Engine.
        Assert.Equal(height - 1, h);
        Assert.Equal(height - LayoutCalculator.FullscreenTrim, h);
    }

    [Fact]
    public void OverlaySize_FullHd_Is1920x1079()
    {
        var (w, h) = LayoutCalculator.OverlaySize(1920, 1080);

        Assert.Equal(1920, w);
        Assert.Equal(1079, h);
    }

    [Fact]
    public void OverlaySize_NeverNegative()
    {
        Assert.Equal(0, LayoutCalculator.OverlaySize(0, 0).Height);
        Assert.Equal(0, LayoutCalculator.OverlaySize(-100, -100).Height);
    }
}

/// <summary>
/// Сетка «ЗАКРЕПЛЕНО» 4x2: 8 плиток с бейджами Alt+1..9 должны помещаться
/// в карточку целиком, без прокрутки, на типичных разрешениях.
/// </summary>
public class PinTileGeometryTests
{
    // Внутренние отступы PanelStyle (рамка + padding) — как в MainWindow.FitPinTiles.
    private const double Chrome = 34;

    [Fact]
    public void TileWidth_NeverExceedsColumnWidth()
    {
        foreach (double inner in new[] { 120.0, 200, 400, 620, 1200 })
        {
            double tile = PinTileGeometry.TileWidth(inner);
            double perColumn = (inner - (PinTileGeometry.Columns - 1) * PinTileGeometry.TileGap) / PinTileGeometry.Columns;

            Assert.True(tile <= perColumn + 0.001, $"inner={inner}: tile={tile} > column={perColumn}");
            Assert.True(tile <= PinTileGeometry.MaxTileWidth, $"inner={inner}: tile={tile} > max");
        }
    }

    [Fact]
    public void TileWidth_CapsAtMaxOnWideCards()
    {
        Assert.Equal(PinTileGeometry.MaxTileWidth, PinTileGeometry.TileWidth(2000));
    }

    [Fact]
    public void FiveTilesPerRow_FitInnerWidth_WithoutWrap()
    {
        // Живой баг: при inner=626 пятая плитка переносилась на вторую строку.
        double inner = 626;
        double tile = PinTileGeometry.TileWidth(inner);
        double used = PinTileGeometry.Columns * tile + (PinTileGeometry.Columns - 1) * PinTileGeometry.TileGap;

        Assert.Equal(5, PinTileGeometry.Columns);
        Assert.True(used <= inner + 0.001, $"5 плиток занимают {used:0.#} > inner={inner}");
    }

    [Fact]
    public void GridOfEightTiles_FitsTypicalPinnedCard()
    {
        // Живая раскладка 1920x1080: pinned = 660x600.
        var layout = LayoutCalculator.Compute(1920, 1080, LayoutSpec.Default);
        double inner = layout.Pinned.Width - Chrome;

        Assert.True(PinTileGeometry.FitsWithoutScroll(inner, layout.Pinned.Height - 56),
            $"сетка {PinTileGeometry.GridHeight(PinTileGeometry.TileHeight(PinTileGeometry.TileWidth(inner))):0.#} " +
            $"не влезает в {layout.Pinned.Height}");
    }

    [Fact]
    public void TileHeight_IsCompactAndWidthIndependent()
    {
        // Компактная плитка: ~96 px высоты независимо от ширины (было ~136 при inner=626).
        Assert.Equal(PinTileGeometry.TileHeightValue, PinTileGeometry.TileHeight(100));
        Assert.Equal(PinTileGeometry.TileHeightValue, PinTileGeometry.TileHeight(2000));
        Assert.Equal(96, PinTileGeometry.TileHeightValue);
        Assert.True(PinTileGeometry.IconSize >= 40 && PinTileGeometry.IconSize <= 48,
            $"иконка {PinTileGeometry.IconSize} вне разумного диапазона 40..48");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void DegenerateInputs_ReturnZero(double width)
    {
        Assert.Equal(0, PinTileGeometry.TileWidth(width));
        Assert.Equal(0, PinTileGeometry.GridHeight(PinTileGeometry.TileHeight(width)));
        Assert.False(PinTileGeometry.FitsWithoutScroll(width, 500));
    }

    [Fact]
    public void TileHeight_StaysCompactForAnyWidth()
    {
        // Было: высота выводилась из ширины (x1.15, минимум 76) — вокруг иконки пухло пустое место.
        Assert.Equal(PinTileGeometry.TileHeightValue, PinTileGeometry.TileHeight(10));
        Assert.Equal(PinTileGeometry.TileHeightValue, PinTileGeometry.TileHeight(120));
        Assert.True(PinTileGeometry.TileHeight(120) < 120, "плитка должна быть ниже 120 px");
    }
}
