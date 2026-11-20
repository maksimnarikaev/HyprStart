namespace HyprStart.Core.Library;

/// <summary>
/// Поиск по приложениям: префикс > вхождение > подпоследовательность.
/// Учитывается и имя папки — можно найти «фотографии в служебных».
/// </summary>
public static class AppSearch
{
    public static List<AppEntry> Filter(IReadOnlyList<AppEntry> entries, string? query)
        => Filter(entries, query, null, DateTime.Now);

    /// <summary>
    /// Вариант с учётом статистики запусков (frecency): при совпадении префикса
    /// часто и недавно запускаемые приложения поднимаются выше, но баллы за
    /// качество совпадения остаются главными.
    /// </summary>
    /// <param name="usage">Цель приложения → (запусков, последний запуск).</param>
    public static List<AppEntry> Filter(
        IReadOnlyList<AppEntry> entries,
        string? query,
        IReadOnlyDictionary<string, (int Count, DateTime LastUsed)>? usage,
        DateTime now)
    {
        if (entries.Count == 0) return new List<AppEntry>();
        if (string.IsNullOrWhiteSpace(query)) return new List<AppEntry>(entries);

        var needle = AppIndexBuilder.Normalize(query);
        if (needle.Length == 0) return new List<AppEntry>(entries);

        var scored = new List<(AppEntry Entry, double Score)>();

        foreach (var entry in entries)
        {
            int score = Score(entry.SearchKey, needle);
            if (score == 0 && entry.GroupKey.Length > 0)
            {
                int groupScore = Score(entry.GroupKey, needle);
                if (groupScore > 0) score = groupScore / 2; // папка весит меньше имени
            }

            if (score > 0)
            {
                double total = score;
                if (usage is not null && usage.TryGetValue(entry.Target, out var record))
                {
                    total += Frecency.Bonus(record.Count, record.LastUsed, now);
                }

                scored.Add((entry, total));
            }
        }

        scored.Sort(static (a, b) =>
        {
            if (a.Score != b.Score) return b.Score.CompareTo(a.Score);
            return string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        var result = new List<AppEntry>(scored.Count);
        foreach (var pair in scored) result.Add(pair.Entry);
        return result;
    }

    internal static int Score(string key, string needle)
    {
        if (key.Length == 0) return 0;

        if (key.StartsWith(needle, StringComparison.Ordinal))
        {
            return 1000 - key.Length;
        }

        int index = key.IndexOf(needle, StringComparison.Ordinal);
        if (index >= 0)
        {
            bool wordStart = index == 0 || key[index - 1] == ' ';
            return (wordStart ? 700 : 500) - index;
        }

        int position = 0;
        foreach (char ch in key)
        {
            if (position < needle.Length && needle[position] == ch) position++;
            if (position == needle.Length) break;
        }

        return position == needle.Length ? 100 : 0;
    }
}
