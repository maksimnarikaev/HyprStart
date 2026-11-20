using HyprStart.Core.Layout;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// ApplyGeometry() в MainWindow.Theme.cs считает размер оверлея по экрану и зовёт Relayout(),
/// который передаёт flags HideChatPanel в LayoutCalculator.Compute. Тесты фиксируют контракт
/// этого пути: при HideChatPanel = true панель «Закреплено» занимает всю ширину над «Быстрый доступ».
/// </summary>
public class ApplyGeometryHideChatTests
{
    private static LayoutSpec Spec() => LayoutSpec.Default;

    // Размеры реального монитора 1920x1080 в DIP при 100% масштабе.
    private static LayoutResult Compute(double width, double height, bool hideChat)
        => LayoutCalculator.Compute(width, height, Spec(), hideChat);

    [Fact]
    public void ApplyGeometry_WithHideChat_GivesPinnedFullWidth()
    {
        var shown = Compute(1920, 1080, hideChat: false);
        var hidden = Compute(1920, 1080, hideChat: true);

        // Панель расширяется вправо и забирает колонку чата целиком.
        Assert.True(hidden.Pinned.Width > shown.Pinned.Width,
            $"pinned должен расшириться: было {shown.Pinned.Width:0}, стало {hidden.Pinned.Width:0}");

        // Чат вырожден и горизонтали не занимает.
        Assert.Equal(0, hidden.Chat.Width, 2);

        // Правый край Pinned совпадает с правым краем нижней панели — пустого места справа нет.
        Assert.Equal(hidden.Quick.Right, hidden.Pinned.Right, 1);

        // Панель всё ещё корректна геометрически и не вылезает за экран.
        Assert.True(hidden.Pinned.Width > 0);
        Assert.True(hidden.Pinned.Height > 0);
        Assert.True(hidden.Pinned.X >= 0);
        Assert.True(hidden.Pinned.Right <= 1920 + 0.5);
    }

    [Fact]
    public void ApplyGeometry_WithHideChat_KeepsBottomPanelsAligned()
    {
        var hidden = Compute(1920, 1080, hideChat: true);

        // «Закреплено» сидит над «Быстрый доступ» и выровнено с ним по вертикали и по правому краю.
        Assert.Equal(hidden.Quick.Right, hidden.Pinned.Right, 1);
        Assert.True(hidden.Pinned.Bottom <= hidden.Quick.Y + 0.5,
            $"pinned.bottom={hidden.Pinned.Bottom:0.#} должен быть выше quick.y={hidden.Quick.Y:0.#}");
    }

    [Fact]
    public void ApplyGeometry_WithHideChat_HasNoOverlaps()
    {
        var r = Compute(1920, 1080, hideChat: true);

        Assert.False(Overlaps(r.Pinned, r.Apps), "pinned пересекается с apps");
        Assert.False(Overlaps(r.Pinned, r.Frequent), "pinned пересекается с frequent");
        Assert.False(Overlaps(r.Pinned, r.Quick), "pinned пересекается с quick");
        Assert.False(Overlaps(r.Frequent, r.Quick), "frequent пересекается с quick");
    }

    [Fact]
    public void ApplyGeometry_WithHideChat_ScalesColumnsWithWidth()
    {
        // Панель шире -> больше колонок в сетке плиток (адаптивность заявлена в FitPinTiles).
        const double chrome = 34;

        var shown = Compute(1920, 1080, hideChat: false);
        var hidden = Compute(1920, 1080, hideChat: true);

        int colsShown = PinTileGeometry.ColumnCount(shown.Pinned.Width - chrome);
        int colsHidden = PinTileGeometry.ColumnCount(hidden.Pinned.Width - chrome);

        Assert.True(colsShown > 0);
        Assert.True(colsHidden >= colsShown,
            $"при скрытом чате колонок не должно стать меньше: было {colsShown}, стало {colsHidden}");
        Assert.True(colsHidden > colsShown,
            $"широкая панель должна получить больше колонок: было {colsShown}, стало {colsHidden}");
    }

    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1600, 900)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void ApplyGeometry_AnyScreen_WithHideChat_IsSane(double w, double h)
    {
        var r = Compute(w, h, hideChat: true);

        Assert.True(r.Pinned.Width > 0);
        Assert.True(r.Pinned.Height > 0);
        Assert.True(r.Quick.Width > 0);
        Assert.Equal(r.Quick.Right, r.Pinned.Right, 1);
        Assert.True(r.Pinned.Right <= w + 0.5, $"pinned.right={r.Pinned.Right:0.#} вылез за {w}");
    }

    private static bool Overlaps(PixelRect a, PixelRect b)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0) return false;
        return a.X < b.Right - 0.5 && b.X < a.Right - 0.5
            && a.Y < b.Bottom - 0.5 && b.Y < a.Bottom - 0.5;
    }
}
