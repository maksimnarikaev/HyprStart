namespace HyprStart.Core.Library;

public enum AppEntryKind
{
    /// <summary>Ярлык .lnk из меню «Программы».</summary>
    Shortcut = 0,

    /// <summary>UWP/Store-приложение, запускается по AUMID.</summary>
    Uwp = 1,
}

public sealed class AppEntry
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Для Shortcut — путь к .lnk, для Uwp — AUMID.</summary>
    public string Target { get; set; } = string.Empty;

    public AppEntryKind Kind { get; set; }

    /// <summary>Имя папки верхнего уровня в меню «Программы» (null — корень).</summary>
    public string? Group { get; set; }

    /// <summary>Подсказка, откуда брать иконку (для .url из Steam — путь к .ico из файла ярлыка).</summary>
    public string? IconHint { get; set; }

    /// <summary>Имя PNG-файла в папке кэша иконок.</summary>
    public string? IconFile { get; set; }

    /// <summary>Нормализованное имя для поиска (нижний регистр, ё→е).</summary>
    public string SearchKey { get; set; } = string.Empty;

    /// <summary>Нормализованное имя папки для поиска.</summary>
    public string GroupKey { get; set; } = string.Empty;
}

public sealed class AppIndex
{
    public int Version { get; set; }
    public DateTime IndexedAt { get; set; }
    public int IconCount { get; set; }
    public int PlaceholderCount { get; set; }
    public List<AppEntry> Entries { get; set; } = new();
}
