using HyprStart.Core.Settings;
using HyprStart.Core.Theming;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Регрессия: красные и жёлто-янтарные обои уходили в дефолтный розовый акцент,
/// потому что монохромный кадр («второго оттенка нет») жёстко подменялся фоллбэком.
/// </summary>
public class AutoThemeColorTests
{
    private const int Width = 32;
    private const int Height = 18;

    /// <summary>BGRA-буфер как у GDI BitBlt: порядок байт B, G, R, A.</summary>
    private static byte[] Solid(byte r, byte g, byte b, int width = Width, int height = Height)
    {
        var buffer = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            buffer[i * 4 + 0] = b;
            buffer[i * 4 + 1] = g;
            buffer[i * 4 + 2] = r;
            buffer[i * 4 + 3] = 255;
        }

        return buffer;
    }

    private static WallpaperColorExtractor.Palette Extract(byte r, byte g, byte b)
        => WallpaperColorExtractor.Extract(Solid(r, g, b), Width, Height);

    /// <summary>Разница оттенков по кратчайшей дуге цветового круга.</summary>
    private static double HueDelta(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    [Theory]
    [InlineData(230, 30, 30, 0.0, 20.0)]     // красный
    [InlineData(242, 202, 40, 40.0, 65.0)]   // жёлтый
    [InlineData(240, 170, 30, 30.0, 55.0)]   // янтарный
    [InlineData(50, 220, 90, 100.0, 150.0)]  // зелёный
    [InlineData(40, 95, 235, 200.0, 240.0)]  // синий
    public void Extract_KeepsHueOfDominantBackground(byte r, byte g, byte b, double hueFrom, double hueTo)
    {
        var palette = Extract(r, g, b);
        var hsv = ColorMath.ToHsv(palette.Primary);

        // Главное: оттенок фона сохранён, а не подменён розовым (315..345).
        Assert.InRange(hsv.H, hueFrom, hueTo);
        Assert.True(hsv.S > 0.3, $"насыщенность просела: {hsv.S:0.00}");
        Assert.True(hsv.V > 0.4, $"яркость просела: {hsv.V:0.00}");
    }

    [Theory]
    [InlineData(230, 30, 30)]
    [InlineData(242, 202, 40)]
    [InlineData(240, 170, 30)]
    public void MonochromeBackground_StillProducesOwnAccent(byte r, byte g, byte b)
    {
        var palette = Extract(r, g, b);
        Assert.True(palette.IsMonochrome, "однотонный кадр должен помечаться как монохромный");

        var theme = AutoTheme.FromPalette(palette);
        var accent = ColorMath.ToHsv(ColorMath.ParseHex(theme.Accent)!.Value);

        // Раньше здесь всегда был #FF75B5 (розовый). Теперь — оттенок самих обоев.
        Assert.InRange(HueDelta(accent.H, ColorMath.ToHsv(palette.Primary).H), 0, 6);
    }

    [Fact]
    public void MonochromeBackground_GetsDistinctSecondTone()
    {
        var palette = Extract(240, 170, 30);

        // Второй тон нужен для градиента обводки: он не должен совпадать с первым,
        // но и обязан остаться в гамме обоев (сдвиг 35 градусов, а не «любой другой цвет»).
        double delta = HueDelta(
            ColorMath.ToHsv(palette.Primary).H,
            ColorMath.ToHsv(palette.Secondary).H);

        Assert.InRange(delta, 25, 45);
    }

    [Fact]
    public void NormalizeAccent_KeepsHueAndLiftsDarkColor()
    {
        var dark = new RgbColor(70, 12, 12);
        var normalized = AutoTheme.NormalizeAccent(dark);
        var hsv = ColorMath.ToHsv(normalized);

        Assert.InRange(HueDelta(hsv.H, ColorMath.ToHsv(dark).H), 0, 3);

        // После обратного преобразования HSV-RGB-HSV значения «проседают» из-за клиппинга
        // каналов, поэтому сверяемся с допуском, а не с константой вплотную.
        Assert.True(hsv.V >= AutoTheme.MinAccentValue - 0.05, $"акцент остался тёмным: {hsv.V:0.00}");
        Assert.True(hsv.S >= AutoTheme.MinAccentSaturation - 0.05, $"акцент остался бледным: {hsv.S:0.00}");
    }

    [Fact]
    public void NormalizeAccent_DoesNotBurnOutBrightColor()
    {
        var normalized = AutoTheme.NormalizeAccent(new RgbColor(255, 255, 200));
        var hsv = ColorMath.ToHsv(normalized);

        Assert.True(hsv.V <= AutoTheme.MaxAccentValue + 0.02, $"акцент пересвечен: {hsv.V:0.00}");
    }

    [Fact]
    public void FromPalette_AccentReadableOnBackdrop()
    {
        foreach ((byte r, byte g, byte b) in new (byte, byte, byte)[] { (230, 30, 30), (242, 202, 40), (50, 220, 90), (40, 95, 235) })
        {
            var theme = AutoTheme.FromPalette(Extract(r, g, b));
            var accent = ColorMath.ParseHex(theme.Accent)!.Value;
            var glass = ColorMath.ParseHex(theme.Glass)!.Value;
            var text = ColorMath.ParseHex(theme.Text)!.Value;

            // Текст должен отличаться от подложки не меньше чем в 4.5 раза (WCAG AA).
            Assert.True(ColorMath.Contrast(text, glass) >= 4.5,
                $"текст нечитаем на подложке для фона #{r:X2}{g:X2}{b:X2}");

            // И сам акцент не должен сливаться с подложкой.
            Assert.True(ColorMath.Contrast(accent, glass) >= 3.0,
                $"акцент сливается с подложкой для фона #{r:X2}{g:X2}{b:X2}");
        }
    }
}
