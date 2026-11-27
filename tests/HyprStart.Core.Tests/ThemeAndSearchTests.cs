using HyprStart.Core.Library;
using HyprStart.Core.Settings;
using HyprStart.Core.Storage;
using HyprStart.Core.Theming;
using Xunit;

namespace HyprStart.Core.Tests;

public class ThemeAndSearchTests
{
    // ---------- WallpaperColorExtractor ----------

    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
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

    [Fact]
    public void Extract_ThrowsAwayDarkAndDullPixels()
    {
        // половина кадра — почти чёрный шум, половина — насыщенный розовый
        var buffer = Solid(32, 18, 6, 5, 8);
        for (int i = 0; i < 32 * 9; i++)
        {
            buffer[i * 4 + 0] = 0xB5; // B
            buffer[i * 4 + 1] = 0x75; // G
            buffer[i * 4 + 2] = 0xFF; // R
            buffer[i * 4 + 3] = 255;  // A — без него пиксель пропадает
        }

        var palette = WallpaperColorExtractor.Extract(buffer, 32, 18);

        // один оттенок на кадре — градиент строить не из чего, но акцент обязан быть розовым
        var hsv = ColorMath.ToHsv(palette.Primary);
        Assert.InRange(hsv.H, 315, 345);   // розовый сектор
        Assert.True(hsv.S > 0.3);
        Assert.True(hsv.V > 0.5);
    }

    [Fact]
    public void Extract_MostlyDarkSceneStillGetsVividAccent()
    {
        // 90% кадра — тёмный шум, 10% — насыщенный розовый: акцент не должен посереть
        var buffer = Solid(32, 18, 6, 5, 8);
        for (int i = 0; i < 32 * 18 / 10; i++)
        {
            buffer[i * 4 + 0] = 0xB5;
            buffer[i * 4 + 1] = 0x75;
            buffer[i * 4 + 2] = 0xFF;
            buffer[i * 4 + 3] = 255;
        }

        var palette = WallpaperColorExtractor.Extract(buffer, 32, 18);
        var hsv = ColorMath.ToHsv(palette.Primary);
        Assert.InRange(hsv.H, 315, 345);
        Assert.True(hsv.S > 0.3, $"насыщенность акцента просела: {hsv.S:F2}");
        Assert.True(hsv.V > 0.5, $"яркость акцента просела: {hsv.V:F2}");
    }

    [Fact]
    public void Extract_MonochromeSceneFallsBackToSakuraPink()
    {
        var grey = Solid(32, 18, 40, 40, 42); // ровный тёмно-серый, без насыщенности
        var palette = WallpaperColorExtractor.Extract(grey, 32, 18);

        Assert.True(palette.IsMonochrome);
        Assert.Equal(new RgbColor(0xFF, 0x75, 0xB5), palette.Primary);
        Assert.Equal(new RgbColor(0xC7, 0x92, 0xEA), palette.Secondary);
    }

    [Fact]
    public void Extract_PicksTwoDistinctHuesForGradient()
    {
        var buffer = Solid(32, 18, 0, 0, 0);
        // левая половина — синий, правая — оранжевый
        for (int y = 0; y < 18; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int o = (y * 32 + x) * 4;
                bool left = x < 16;
                buffer[o + 2] = left ? (byte)0x2A : (byte)0xFF; // R
                buffer[o + 1] = left ? (byte)0x6C : (byte)0x9A; // G
                buffer[o + 0] = left ? (byte)0xF0 : (byte)0x22; // B
                buffer[o + 3] = 255;
            }
        }

        var palette = WallpaperColorExtractor.Extract(buffer, 32, 18);
        double h1 = ColorMath.ToHsv(palette.Primary).H;
        double h2 = ColorMath.ToHsv(palette.Secondary).H;

        Assert.False(palette.IsMonochrome);
        double distance = Math.Abs(h1 - h2);
        if (distance > 180) distance = 360 - distance;
        Assert.True(distance >= 60, $"оттенки слишком близки: {h1:N0} и {h2:N0}");
    }

    [Fact]
    public void Extract_EmptyBufferIsMonochrome()
    {
        var palette = WallpaperColorExtractor.Extract(ReadOnlySpan<byte>.Empty, 0, 0);
        Assert.True(palette.IsMonochrome);
    }

    // ---------- AutoTheme ----------

    [Fact]
    public void AutoTheme_BackdropIsDarkAndTextReadable()
    {
        var palette = new WallpaperColorExtractor.Palette(
            new RgbColor(0x34, 0xD3, 0x99),
            new RgbColor(0xFF, 0xC4, 0x57),
            false);

        var theme = AutoTheme.FromPalette(palette);
        var backdrop = ColorMath.ParseHex(theme.Glass);
        var text = ColorMath.ParseHex(theme.Text);

        Assert.NotNull(backdrop);
        Assert.NotNull(text);
        Assert.True(ColorMath.Luminance(backdrop!.Value) < 0.1, "подложка должна остаться тёмной");
        Assert.True(ColorMath.Contrast(text!.Value, backdrop.Value) > 4.5, "текст должен читаться");
    }

    [Fact]
    public void AutoTheme_IsAutoIsCaseInsensitive()
    {
        Assert.True(AutoTheme.IsAuto("auto"));
        Assert.True(AutoTheme.IsAuto(" AUTO "));
        Assert.False(AutoTheme.IsAuto("sakura-pink"));
        Assert.False(AutoTheme.IsAuto(null));
    }

    /// <summary>
    /// Режим "auto" — не тема из списка. Если бы имя палитры совпало с "auto",
    /// ThemeService.Find("auto") вернул бы её при старте, и live-подхват с экрана
    /// никогда бы не срабатывал (процесс применяет фолбэк вместо снимка).
    /// </summary>
    [Fact]
    public void AutoTheme_NameIsNotAutoIdSoItNeverShadowsLiveMode()
    {
        Assert.False(AutoTheme.IsAuto(AutoTheme.FallbackThemeId));

        var palette = new WallpaperColorExtractor.Palette(
            new RgbColor(0xFF, 0x75, 0xB5),
            new RgbColor(0xC7, 0x92, 0xEA),
            false);

        var theme = AutoTheme.FromPalette(palette);

        Assert.False(AutoTheme.IsAuto(theme.Name), "палитра из кадра не должна называться 'auto'");
        Assert.False(string.Equals(theme.Name, AutoTheme.Id, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AutoTheme.Id, AutoTheme.Id.ToLowerInvariant());
    }

    // ---------- Sakura pink ----------

    [Fact]
    public void SakuraPink_HasDocumentedColors()
    {
        var theme = ThemePalette.SakuraPink;
        Assert.Equal("#FF75B5", theme.Accent);
        Assert.Equal("#C792EA", theme.Accent2);
        Assert.Equal("#FBD1EE", theme.Text);
        Assert.StartsWith("#EB", theme.Glass);
    }

    [Fact]
    public void AllBuiltInThemes_AreValid()
    {
        foreach (var theme in new[] { ThemePalette.IceBlue, ThemePalette.ForestMint, ThemePalette.SakuraPink })
        {
            foreach (var (field, value) in new (string, string)[]
                     {
                         ("glass", theme.Glass), ("divider", theme.Divider), ("accent", theme.Accent),
                         ("accent2", theme.Accent2), ("text", theme.Text), ("muted", theme.Muted),
                         ("pill", theme.Pill), ("pillBorder", theme.PillBorder), ("borderFrom", theme.BorderFrom),
                         ("borderTo", theme.BorderTo), ("tile", theme.Tile), ("tileBorder", theme.TileBorder),
                         ("hover", theme.Hover), ("selection", theme.Selection), ("menu", theme.Menu),
                         ("cavaFrom", theme.CavaFrom), ("cavaTo", theme.CavaTo),
                     })
            {
                Assert.NotNull(ColorMath.ParseHex(value));
                _ = field;
            }
        }
    }

    // ---------- Frecency ----------

    [Fact]
    public void Frecency_RecentBeatsOld()
    {
        var now = new DateTime(2026, 11, 18);
        double fresh = Frecency.Weight(5, now.AddDays(-1), now);
        double stale = Frecency.Weight(5, now.AddDays(-60), now);

        Assert.True(fresh > stale);
    }

    [Fact]
    public void Frecency_BonusIsCapped()
    {
        var now = DateTime.Now;
        Assert.InRange(Frecency.Bonus(10_000, now, now), 0, Frecency.MaxBonus);
        Assert.Equal(0, Frecency.Bonus(0, now, now));
    }

    // ---------- Поиск с учётом статистики ----------

    private static AppEntry Entry(string name, string target) => new()
    {
        Name = name,
        Target = target,
        SearchKey = AppIndexBuilder.Normalize(name),
    };

    [Fact]
    public void Search_FrecencyLiftsFrequentlyUsedAboveAlphabetical()
    {
        var entries = new List<AppEntry>
        {
            Entry("Code", @"c:\links\code.lnk"),
            Entry("Codex", @"c:\links\codex.lnk"),
        };

        // без статистики — короче имя выше
        var plain = AppSearch.Filter(entries, "code");
        Assert.Equal("Code", plain[0].Name);

        // с историей у Codex — он поднимается
        var usage = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"c:\links\codex.lnk"] = (40, DateTime.Now),
        };
        var ranked = AppSearch.Filter(entries, "code", usage, DateTime.Now);
        Assert.Equal("Codex", ranked[0].Name);
    }

    [Fact]
    public void Search_DoesNotPromoteAppsThatDoNotMatch()
    {
        var entries = new List<AppEntry>
        {
            Entry("Firefox", @"c:\links\firefox.lnk"),
            Entry("Notepad", @"c:\links\notepad.lnk"),
        };

        var usage = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"c:\links\notepad.lnk"] = (999, DateTime.Now),
        };

        var result = AppSearch.Filter(entries, "fire", usage, DateTime.Now);
        Assert.Single(result);
        Assert.Equal("Firefox", result[0].Name);
    }

    [Fact]
    public void Search_QualityStillDominatesFrecency()
    {
        // "ff" — префикс у одной записи и подпоследовательность у другой
        var entries = new List<AppEntry>
        {
            Entry("ffmpeg", @"c:\links\ffmpeg.lnk"),
            Entry("Firefox", @"c:\links\firefox.lnk"),
        };

        var usage = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            [@"c:\links\firefox.lnk"] = (200, DateTime.Now),
        };

        var result = AppSearch.Filter(entries, "ff", usage, DateTime.Now);
        Assert.Equal("ffmpeg", result[0].Name);
    }

    // ---------- Буфер обмена калькулятора ----------

    [Fact]
    public void MathEvaluator_ResultForEnterCopy()
    {
        Assert.True(HyprStart.Core.Input.MathEvaluator.TryEvaluate("2+2*2", out var value));
        Assert.Equal("6", value);
    }
}
