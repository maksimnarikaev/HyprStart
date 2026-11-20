namespace HyprStart.Core.Library;

/// <summary>
/// Слияние источников: фильтр мусора, дедупликация и сортировка.
/// Папки меню «Программы» сохраняются в <see cref="AppEntry.Group"/>.
/// </summary>
public static class AppIndexBuilder
{
    /// <summary>Мусорные ярлыки, которым не место в меню.</summary>
    private static readonly string[] NoiseWords =
    {
        "uninstall",
        "деинсталл",
        "удалить",
        "руководство",
        "помощь",
        "справка",
        "документация",
        "readme",
        "help",
        "documentation",
    };

    public static List<AppEntry> Build(IEnumerable<AppEntry> shortcuts, IEnumerable<AppEntry> uwp)
    {
        var result = new List<AppEntry>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ярлыки первыми: при совпадении имени приоритет у .lnk.
        foreach (var entry in shortcuts.Concat(uwp))
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Target)) continue;

            entry.Name = entry.Name.Trim();
            entry.Group = string.IsNullOrWhiteSpace(entry.Group) ? null : entry.Group.Trim();

            var searchKey = Normalize(entry.Name);
            var groupKey = Normalize(entry.Group);

            if (searchKey.Length == 0) continue;
            if (IsNoise(searchKey)) continue;

            if (!seenTargets.Add((int)entry.Kind + "|" + entry.Target.ToLowerInvariant())) continue;

            // Дубль по имени — только внутри одной папки: одноимённые приложения
            // в разных папках это разные программы.
            if (!seenNames.Add(groupKey + "|" + searchKey)) continue;

            entry.SearchKey = searchKey;
            entry.GroupKey = groupKey;
            result.Add(entry);
        }

        result.Sort(static (a, b) =>
        {
            int byGroup = string.Compare(a.Group ?? string.Empty, b.Group ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);
            if (byGroup != 0) return byGroup;

            int byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : a.Kind.CompareTo(b.Kind);
        });

        return result;
    }

    /// <summary>Ярлыки вида «Uninstall Steam», «Руководство», «Readme» в меню не нужны.</summary>
    public static bool IsNoise(string searchKey)
    {
        foreach (var word in NoiseWords)
        {
            if (searchKey.Contains(word, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var trimmed = text.Trim().ToLowerInvariant().Replace('ё', 'е');
        var builder = new System.Text.StringBuilder(trimmed.Length);
        bool lastSpace = false;

        foreach (char ch in trimmed)
        {
            bool isSpace = char.IsWhiteSpace(ch);
            if (isSpace)
            {
                if (lastSpace) continue;
                builder.Append(' ');
            }
            else
            {
                builder.Append(ch);
            }

            lastSpace = isSpace;
        }

        return builder.ToString();
    }
}
