namespace HyprStart.Core.Settings;

/// <summary>
/// Палитра темы. Цвета хранятся строками #AARRGGBB / #RRGGBB — парсинг и валидация
/// живут в Core, применение к ресурсам WPF — в App.
/// </summary>
public sealed class ThemePalette
{
    public string Name { get; set; } = "Без имени";

    /// <summary>Фон панелей (стекло).</summary>
    public string Glass { get; set; } = "#D9182540";

    public string Divider { get; set; } = "#4D7FA9F0";
    public string Accent { get; set; } = "#A9C4F5";

    /// <summary>Второй акцент — конец градиента рамки и cava.</summary>
    public string Accent2 { get; set; } = "#5B8DEF";
    public string Text { get; set; } = "#F5FAFF";
    public string Muted { get; set; } = "#C3D3EA";
    public string Pill { get; set; } = "#66142033";
    public string PillBorder { get; set; } = "#337FA9F0";

    /// <summary>Рамка панелей: градиент от … </summary>
    public string BorderFrom { get; set; } = "#C7DCFF";

    /// <summary>… до (по диагонали 45°).</summary>
    public string BorderTo { get; set; } = "#5B8DEF";

    /// <summary>Фон плитки закреплённого.</summary>
    public string Tile { get; set; } = "#40070B14";

    /// <summary>Обводка плитки.</summary>
    public string TileBorder { get; set; } = "#667FA9F0";

    /// <summary>Подсветка под курсором.</summary>
    public string Hover { get; set; } = "#5A0E1B2E";

    /// <summary>Выделенная строка/подсветка пункта меню.</summary>
    public string Selection { get; set; } = "#2A5B8DEF";

    /// <summary>Фон контекстных меню.</summary>
    public string Menu { get; set; } = "#F5131C30";

    /// <summary>Градиент cava: верх …</summary>
    public string CavaFrom { get; set; } = "#EAF2FF";

    /// <summary>… низ.</summary>
    public string CavaTo { get; set; } = "#5B8DEF";

    public static ThemePalette IceBlue => new()
    {
        Name = "Ice blue",
        Glass = "#F80A0F1C",
    };

    public static ThemePalette ForestMint => new()
    {
        Name = "Forest mint",
        Glass = "#EB0E1F1A",
        Divider = "#4D4FA98A",
        Accent = "#7FE3B8",
        Text = "#F2FBF6",
        Muted = "#C2DCCD",
        Pill = "#66101C18",
        PillBorder = "#337FE3B8",
        BorderFrom = "#9BE8C6",
        BorderTo = "#4FA98A",
        Tile = "#3E0A1A12",
        TileBorder = "#665FBF97",
        Hover = "#5A122A20",
        Selection = "#2A4FA98A",
        Menu = "#F50D1B16",
        CavaFrom = "#E4F2EA",
        CavaTo = "#3E8F72",
    };

    /// <summary>Розовый неон в духе Rose Pine / Cyberpunk: слива + лаванда.</summary>
    public static ThemePalette SakuraPink => new()
    {
        Name = "Sakura pink",
        Glass = "#EB18121E",
        Divider = "#5EC792EA",
        Accent = "#FF75B5",
        Accent2 = "#C792EA",
        Text = "#FBD1EE",
        Muted = "#CDB3DA",
        Pill = "#661E1626",
        PillBorder = "#40FF75B5",
        BorderFrom = "#FF75B5",
        BorderTo = "#C792EA",
        Tile = "#400D0812",
        TileBorder = "#66FF75B5",
        Hover = "#5A241A2E",
        Selection = "#2AFF75B5",
        Menu = "#F2151019",
        CavaFrom = "#FF75B5",
        CavaTo = "#C792EA",
    };
}
