namespace HyprStart.Core.Library;

/// <summary>Узел дерева: папка (Entry == null) или приложение.</summary>
public sealed class AppTreeNode
{
    public string Name { get; init; } = string.Empty;
    public AppEntry? Entry { get; init; }
    public List<AppTreeNode> Children { get; init; } = new();

    public bool IsFolder => Entry is null;
    public int Count => Children.Count;
}

public static class AppTreeBuilder
{
    /// <summary>
    /// Корень = папки и приложения верхнего уровня, перемешанные по алфавиту (как в Windows).
    /// Папки склеиваются по нормализованному имени: регистр, лишние пробелы и разные
    /// написания одной и той же папки из общих и пользовательских программ больше не дублируются.
    /// </summary>
    public static List<AppTreeNode> Build(IReadOnlyList<AppEntry> entries)
    {
        var nodes = new List<AppTreeNode>();

        var folders = entries
            .Where(e => !string.IsNullOrEmpty(e.Group))
            .GroupBy(e => AppIndexBuilder.Normalize(e.Group))
            .Select(group => new AppTreeNode
            {
                Name = PickDisplayName(group.Select(e => e.Group!)),
                Children = group
                    .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(e => new AppTreeNode { Name = e.Name, Entry = e })
                    .ToList(),
            });

        var rootApps = entries
            .Where(e => string.IsNullOrEmpty(e.Group))
            .Select(e => new AppTreeNode { Name = e.Name, Entry = e });

        nodes.AddRange(folders);
        nodes.AddRange(rootApps);
        nodes.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        return nodes;
    }

    /// <summary>Из вариантов написания берём самый частый, при равенстве — первый по алфавиту.</summary>
    private static string PickDisplayName(IEnumerable<string> names)
    {
        return names
            .GroupBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .First()
            .Key
            .Trim();
    }
}
