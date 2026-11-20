using HyprStart.Core.Layout;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Раскладка «Закреплено» при скрытой панели MINI AI: панель должна занимать всю
/// ширину правой колонки (над «Быстрым доступом») без пустой полосы справа,
/// а сетка плиток — добавлять колонки под увеличенную ширину.
/// </summary>
public class PinnedPanelLayoutTests
{
    private const double Width = 1920;
    private const double Height = 1080;

    [Fact]
    public void HideChatPanel_GrowsPinnedToFullRightColumn()
    {
        var visible = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: false);
        var hidden = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        Assert.True(hidden.Pinned.Width > visible.Pinned.Width,
            $"скрытый чат должен расширять Pinned: {visible.Pinned.Width} -> {hidden.Pinned.Width}");
        Assert.True(hidden.Pinned.Height > 0);
        Assert.Equal(visible.Pinned.X, hidden.Pinned.X);
        Assert.Equal(visible.Pinned.Y, hidden.Pinned.Y);
        Assert.Equal(visible.Pinned.Height, hidden.Pinned.Height);
    }

    [Fact]
    public void HideChatPanel_PinnedTakesWholeWidthAboveQuick()
    {
        var layout = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        // Правая колонка = от левого края Pinned до правого края Quick (обе нижние панели).
        // Левая граница правой колонки — X у Pinned и у Frequent (Quick стоит правее, за разделителем).
        Assert.Equal(layout.Quick.Right, layout.Pinned.Right, 1);
        Assert.Equal(layout.Frequent.X, layout.Pinned.X, 1);
        Assert.True(layout.Pinned.Width > layout.Frequent.Width);
        // «Закреплено» стоит выше «Быстрого доступа» и не пересекается с ним.
        Assert.True(layout.Pinned.Bottom <= layout.Quick.Y);
    }

    [Fact]
    public void HideChatPanel_ChatIsDegenerate()
    {
        var layout = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        Assert.Equal(0, layout.Chat.Width);
        Assert.True(layout.Chat.Width >= 0);
        // Нулевая ширина — значит чат не перекрывает «Закреплено».
        Assert.False(layout.Chat.Width > 0 && layout.Chat.X < layout.Pinned.Right);
    }

    [Fact]
    public void HideChatPanel_AllPanelsHavePositiveSizeAndStayInside()
    {
        var layout = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        foreach (var (name, rect) in new[]
                 {
                     (nameof(layout.Apps), layout.Apps),
                     (nameof(layout.Pinned), layout.Pinned),
                     (nameof(layout.Frequent), layout.Frequent),
                     (nameof(layout.Quick), layout.Quick),
                     (nameof(layout.Polybar), layout.Polybar),
                 })
        {
            Assert.True(rect.Width > 0, $"{name}.Width должно быть > 0, получено {rect.Width}");
            Assert.True(rect.Height > 0, $"{name}.Height должно быть > 0, получено {rect.Height}");
            Assert.True(rect.X >= 0, $"{name}.X отрицательный");
            Assert.True(rect.Y >= 0, $"{name}.Y отрицательный");
            Assert.True(rect.Right <= Width, $"{name} выходит за правую границу");
            Assert.True(rect.Bottom <= Height, $"{name} выходит за нижнюю границу");
        }
    }

    [Fact]
    public void HideChatPanel_NoOverlapsBetweenPanels()
    {
        var layout = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);
        var panels = new (string Name, PixelRect Rect)[]
        {
            (nameof(layout.Apps), layout.Apps),
            (nameof(layout.Pinned), layout.Pinned),
            (nameof(layout.Frequent), layout.Frequent),
            (nameof(layout.Quick), layout.Quick),
            (nameof(layout.Polybar), layout.Polybar),
        };

        for (int i = 0; i < panels.Length; i++)
        {
            for (int j = i + 1; j < panels.Length; j++)
            {
                Assert.False(Overlaps(panels[i].Rect, panels[j].Rect),
                    $"{panels[i].Name} и {panels[j].Name} накладываются друг на друга");
            }
        }
    }

    [Fact]
    public void HideChatPanel_PinnedRightTouchesQuickRight()
    {
        var layout = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        // Нет пустого пространства справа: правые края верхней и нижней панели совпадают.
        Assert.Equal(layout.Quick.Right, layout.Pinned.Right, 1);
        Assert.Equal(layout.Frequent.Y, layout.Quick.Y);
        Assert.Equal(layout.Frequent.Height, layout.Quick.Height);
    }

    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void HideChatPanel_HoldsOnAnyScreen(double screenWidth, double screenHeight)
    {
        var (w, h) = LayoutCalculator.OverlaySize(screenWidth, screenHeight);
        var layout = LayoutCalculator.Compute(w, h, LayoutSpec.Default, hideChatPanel: true);

        Assert.True(layout.Pinned.Width > 0);
        Assert.True(layout.Pinned.Height > 0);
        Assert.True(layout.Pinned.Right <= w);
        Assert.Equal(0, layout.Chat.Width);
        Assert.Equal(layout.Quick.Right, layout.Pinned.Right, 1);
    }

    [Fact]
    public void HideChatPanel_GridGainsColumns()
    {
        var visible = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: false);
        var hidden = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        const double chrome = 34;
        double innerVisible = visible.Pinned.Width - chrome;
        double innerHidden = hidden.Pinned.Width - chrome;

        int colsVisible = PinTileGeometry.ColumnCount(innerVisible);
        int colsHidden = PinTileGeometry.ColumnCount(innerHidden);

        Assert.True(colsHidden > colsVisible,
            $"сетка должна добавлять колонки: {colsVisible} -> {colsHidden} (inner {innerVisible} -> {innerHidden})");
        Assert.True(colsHidden <= PinTileGeometry.MaxColumns);
        // Плитки не вылезают за ширину панели.
        double tile = PinTileGeometry.TileWidth(innerHidden, colsHidden);
        double used = tile * colsHidden + PinTileGeometry.TileGap * (colsHidden - 1);
        Assert.True(used <= innerHidden + 0.001, $"плитки шире панели: {used} > {innerHidden}");
        Assert.True(tile > 0);
    }

    [Fact]
    public void ColumnCount_NeverDropsBelowDefault()
    {
        Assert.Equal(PinTileGeometry.Columns, PinTileGeometry.ColumnCount(0));
        Assert.Equal(PinTileGeometry.Columns, PinTileGeometry.ColumnCount(-10));
        Assert.Equal(PinTileGeometry.Columns, PinTileGeometry.ColumnCount(double.NaN));
        Assert.Equal(PinTileGeometry.Columns, PinTileGeometry.ColumnCount(100));
    }

    [Fact]
    public void ColumnCount_IsCappedAtMax()
    {
        Assert.Equal(PinTileGeometry.MaxColumns, PinTileGeometry.ColumnCount(100000));
    }

    [Fact]
    public void ColumnCount_GrowsMonotonicallyWithWidth()
    {
        int previous = 0;
        for (double width = 100; width <= 3000; width += 25)
        {
            int columns = PinTileGeometry.ColumnCount(width);
            Assert.True(columns >= previous, $"колонки убывают при росте ширины ({width})");
            previous = columns;
        }
        Assert.True(previous > PinTileGeometry.Columns);
    }

    [Fact]
    public void FitsWithoutScroll_UsesWidthNotTileWidth()
    {
        // При скрытом чате ширина панели почти вдвое больше: ряд плиток должен считаться
        // по доступной ширине контента, а не по ширине одной плитки.
        Assert.True(PinTileGeometry.FitsWithoutScroll(626, 300, PinTileGeometry.ColumnCount(626)));
        Assert.False(PinTileGeometry.FitsWithoutScroll(626, 40, PinTileGeometry.ColumnCount(626)));
        Assert.False(PinTileGeometry.FitsWithoutScroll(0, 300, PinTileGeometry.Columns));
    }

    [Fact]
    public void HideChatPanel_IsIdempotentForSameInput()
    {
        var a = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);
        var b = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DefaultOverload_MatchesExplicitFalse()
    {
        var three = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default);
        var four = LayoutCalculator.Compute(Width, Height, LayoutSpec.Default, hideChatPanel: false);

        Assert.Equal(three, four);
        Assert.True(three.Chat.Width > 0);
    }

    private static bool Overlaps(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty || b.IsEmpty) return false;
        return a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
    }
}
