namespace HyprStart.Core.Storage;

/// <summary>Закреплённый элемент: приложение или (позже) папка-группа.</summary>
public sealed class PinnedItem
{
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;

    /// <summary>0 — ярлык, 1 — UWP.</summary>
    public int Kind { get; set; }

    /// <summary>Необязательная группа: закреплённое рисуется секциями по этому имени.</summary>
    public string? Group { get; set; }

    /// <summary>true — папка-группа внутри закреплённого, приложений в ней нет.</summary>
    public bool IsFolder { get; set; }

    /// <summary>Содержимое папки (только для IsFolder).</summary>
    public List<PinnedItem> Children { get; set; } = new();

    public bool SameAs(string target, int kind)
        => !IsFolder && Kind == kind && string.Equals(Target, target, StringComparison.OrdinalIgnoreCase);
}

public sealed class PinnedData
{
    public List<PinnedItem> Items { get; set; } = new();
}

public sealed class UsageRecord
{
    public int Count { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.Now;
}

public sealed class UsageData
{
    /// <summary>Ключ — цель приложения (путь к .lnk или AUMID).</summary>
    public Dictionary<string, UsageRecord> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
