using HyprStart.Core.Layout;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Масштабирование DPI: панели не должны выходить за пределы экрана при 125% и 150%.
/// Окно лаунчера получает размер в физических пикселях (WinForms Screen.Bounds) и
/// переводит его в DIP делением на масштаб, поэтому раскладка считается в DIP.
/// </summary>
public class DpiScalingTests
{
    /// <summary>Симулирует путь MainWindow.ApplyGeometry: physical px -> DIP -> Compute.</summary>
    private static (double Width, double Height, LayoutResult Result) LayoutAt(int width, int height, double scale)
    {
        var overlay = LayoutCalculator.OverlaySize(width / scale, height / scale);
        var result = LayoutCalculator.Compute(overlay.Width, overlay.Height);
        return (overlay.Width, overlay.Height, result);
    }

    private static IEnumerable<PixelRect> All(LayoutResult r)
    {
        yield return r.Apps;
        yield return r.Pinned;
        yield return r.Chat;
        yield return r.Frequent;
        yield return r.Quick;
        yield return r.Polybar;
    }

    [Theory]
    [InlineData(1920, 1080, 1.00)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(1920, 1080, 1.50)]
    [InlineData(2560, 1440, 1.25)]
    [InlineData(2560, 1440, 1.50)]
    [InlineData(3840, 2160, 1.50)]
    [InlineData(1366, 768, 1.25)]
    [InlineData(1366, 768, 1.50)]
    [InlineData(1280, 720, 1.50)]
    public void PanelsStayInsideScreen_AtAnyScaling(int width, int height, double scale)
    {
        var (dipWidth, dipHeight, result) = LayoutAt(width, height, scale);

        foreach (var rect in All(result))
        {
            Assert.True(rect.X >= -0.5, $"X={rect.X} ушёл влево за экран при {scale:P0}");
            Assert.True(rect.Y >= -0.5, $"Y={rect.Y} ушёл вверх за экран при {scale:P0}");
            Assert.True(rect.X + rect.Width <= dipWidth + 0.5,
                $"правый край {rect.X + rect.Width} > {dipWidth} при {scale:P0}");
            Assert.True(rect.Y + rect.Height <= dipHeight + 0.5,
                $"нижний край {rect.Y + rect.Height} > {dipHeight} при {scale:P0}");
        }
    }

    [Theory]
    [InlineData(1920, 1080, 1.00)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(1920, 1080, 1.50)]
    [InlineData(1366, 768, 1.50)]
    public void PanelsHavePositiveSize_AtAnyScaling(int width, int height, double scale)
    {
        var (_, _, result) = LayoutAt(width, height, scale);

        // Обрезанная по краям панель обычно проявляется как нулевая ширина/высота.
        Assert.All(All(result), rect =>
        {
            Assert.True(rect.Width > 1, $"ширина {rect.Width} при {scale:P0}");
            Assert.True(rect.Height > 1, $"высота {rect.Height} при {scale:P0}");
        });
    }

    [Theory]
    [InlineData(1920, 1080, 1.00)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(1920, 1080, 1.50)]
    public void PanelsDoNotOverlap_AtAnyScaling(int width, int height, double scale)
    {
        var (_, _, result) = LayoutAt(width, height, scale);
        var pairs = new (string Name, PixelRect Rect)[]
        {
            ("apps", result.Apps),
            ("pinned", result.Pinned),
            ("chat", result.Chat),
            ("frequent", result.Frequent),
            ("quick", result.Quick),
            ("polybar", result.Polybar),
        };

        for (var i = 0; i < pairs.Length; i++)
        {
            for (var j = i + 1; j < pairs.Length; j++)
            {
                Assert.False(Overlaps(pairs[i].Rect, pairs[j].Rect),
                    $"{pairs[i].Name} пересекается с {pairs[j].Name} при {scale:P0}");
            }
        }
    }

    private static bool Overlaps(PixelRect a, PixelRect b)
    {
        const double eps = 0.5;
        return a.X < b.X + b.Width - eps && b.X < a.X + a.Width - eps
            && a.Y < b.Y + b.Height - eps && b.Y < a.Y + a.Height - eps;
    }

    [Fact]
    public void OverlaySize_TrimsOnePixelHeightOnly()
    {
        var (width, height) = LayoutCalculator.OverlaySize(1920, 1080);

        Assert.Equal(1920, width);
        Assert.Equal(1080 - LayoutCalculator.FullscreenTrim, height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-100, -100)]
    public void OverlaySize_NeverNegative(double width, double height)
    {
        var (w, h) = LayoutCalculator.OverlaySize(width, height);

        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void SmallScreen_DoesNotProduceNegativeRects()
    {
        // Экранчик 640x480 при 150% даёт 427x320 DIP - раскладка должна выжить.
        var (_, _, result) = LayoutAt(640, 480, 1.50);

        Assert.All(All(result), rect =>
        {
            Assert.True(rect.Width >= 0);
            Assert.True(rect.Height >= 0);
        });
    }

    [Fact]
    public void MarginIsClampedToHalfOfSmallestSide()
    {
        // LayoutSpec.Margin по умолчанию 36 DIP: на экране 60x60 это больше половины.
        var result = LayoutCalculator.Compute(60, 60);

        Assert.All(All(result), rect =>
        {
            Assert.True(rect.Width >= 0);
            Assert.True(rect.Height >= 0);
        });
    }
}