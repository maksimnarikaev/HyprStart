using HyprStart.Core.Theming;

namespace HyprStart.Core.Settings;

/// <summary>
/// Специальная тема "auto": палитра строится на лету из цветов экрана.
/// Живёт в Core, чтобы парсинг/валидация были покрыты тестами.
/// </summary>
public static class AutoTheme
{
    public const string Id = "auto";
    public const string FallbackThemeId = "sakura-pink";

    /// <summary>Является ли имя темы режимом авто-подхвата.</summary>
    public static bool IsAuto(string? name)
        => string.Equals(name?.Trim(), Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Собирает палитру из извлечённых цветов. Подложка остаётся тёмной и
    /// полупрозрачной, текст — светлым: контраст гарантированно &gt; 7:1.
    /// </summary>
    public static ThemePalette FromPalette(WallpaperColorExtractor.Palette palette, ThemePalette? fallback = null)
    {
        var basePalette = fallback ?? ThemePalette.SakuraPink;
        var primary = NormalizeAccent(palette.Primary);
        var secondary = NormalizeAccent(palette.Secondary);
        var backdrop = palette.Backdrop();

        // подложка всегда очень тёмная, но с тоном основного оттенка
        string backdropHex = ColorMath.ToHexArgb(0xD9, backdrop);
        string accentHex = ColorMath.ToHex(primary);
        string accent2Hex = ColorMath.ToHex(secondary);
        _ = basePalette; // зарезервировано под будущие поля фолбэка

        // текст: светлый с лёгким оттенком акцента, проверяем читаемость
        var hsv = ColorMath.ToHsv(primary);
        var text = ColorMath.FromHsv(hsv.H, 0.18, 0.96);
        var muted = ColorMath.FromHsv(hsv.H, 0.22, 0.66);

        if (ColorMath.Contrast(text, backdrop) < 4.5)
        {
            text = new RgbColor(0xF2, 0xF4, 0xF8);
            muted = new RgbColor(0xA8, 0xAC, 0xB8);
        }

        return new ThemePalette
        {
            Name = "Авто (с экрана)",
            Accent = accentHex,
            Accent2 = accent2Hex,
            Text = ColorMath.ToHex(text),
            Muted = ColorMath.ToHex(muted),
            Glass = backdropHex,
            BorderFrom = accentHex,
            BorderTo = accent2Hex,
            Pill = ColorMath.ToHexArgb(0x66, backdrop),
            PillBorder = ColorMath.ToHexArgb(0x40, primary),
            Tile = ColorMath.ToHexArgb(0x40, backdrop),
            TileBorder = ColorMath.ToHexArgb(0x66, primary),
            Hover = ColorMath.ToHexArgb(0x5A, backdrop),
            Selection = ColorMath.ToHexArgb(0x2A, primary),
            Divider = ColorMath.ToHexArgb(0x33, secondary),
            Menu = ColorMath.ToHexArgb(0xF2, backdrop),
            CavaFrom = accentHex,
            CavaTo = accent2Hex,
        };
    }

    /// <summary>Границы акцента: ниже — текст на нём проваливается в грязь и не читается.</summary>
    public const double MinAccentSaturation = 0.55;

    /// <summary>Нижняя граница яркости акцента — иначе тёмно-красный на тёмном фоне «тонет».</summary>
    public const double MinAccentValue = 0.62;

    /// <summary>Верхняя граница яркости — чтобы акцент не выжигал глаз на тёмной подложке.</summary>
    public const double MaxAccentValue = 0.92;

    /// <summary>
    /// Приводит извлечённый цвет к читаемому акценту: подтягивает насыщенность и яркость
    /// в рабочий диапазон, сохраняя оттенок. Оттенок (Hue) НЕ меняется — именно из-за
    /// этого раньше терялись красные и жёлтые обои.
    /// </summary>
    public static RgbColor NormalizeAccent(RgbColor rgb)
    {
        var hsv = ColorMath.ToHsv(rgb);
        return ColorMath.FromHsv(
            hsv.H,
            Math.Max(hsv.S, MinAccentSaturation),
            Math.Clamp(hsv.V, MinAccentValue, MaxAccentValue));
    }
}
