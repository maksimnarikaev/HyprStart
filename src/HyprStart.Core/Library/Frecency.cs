namespace HyprStart.Core.Library;

/// <summary>
/// Вес частоты использования для сортировки поиска: смесь частоты и свежести,
/// затухающая по полураспаду. Считается из usage.json, без фоновых задач.
/// </summary>
public static class Frecency
{
    /// <summary>Период полураспада свежести: за 14 дней вес падает вдвое.</summary>
    public const double HalfLifeDays = 14;

    /// <summary>Сколько очков ранжирования даёт одно запуск (при совпадении префикса).</summary>
    public const double MaxBonus = 400;

    /// <summary>
    /// Вес записи: log2(1+count) * свежесть. Один запуск вчера ≈ 1.0,
    /// сто запусков месяц назад ≈ 1.5 — свежесть важнее «исторической массы».
    /// </summary>
    public static double Weight(int count, DateTime lastUsed, DateTime now)
    {
        if (count <= 0) return 0;

        double ageDays = Math.Max(0, (now - lastUsed).TotalDays);
        double freshness = Math.Pow(0.5, ageDays / HalfLifeDays);
        return Math.Log2(1 + count) * freshness;
    }

    /// <summary>Бонус к очкам ранжирования (0..MaxBonus), обрезанный по максимуму.</summary>
    public static double Bonus(int count, DateTime lastUsed, DateTime now)
        => Math.Min(MaxBonus, Weight(count, lastUsed, now) * 120);
}
