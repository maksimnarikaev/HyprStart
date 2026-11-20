namespace HyprStart.Core.Input;

/// <summary>
/// Разбор Alt/Ctrl+1..9 по закреплённым плиткам.
/// В WPF при зажатом Alt приходит <c>Key.System</c>, а цифра лежит в <c>SystemKey</c> —
/// этот класс инкапсулирует правило, чтобы его можно было покрыть тестами без UI.
/// </summary>
public static class PinDigit
{
    public const int Min = 1;
    public const int Max = 9;

    /// <summary>
    /// Номер плитки по виртуальному коду клавиши: 0 — не цифра,
    /// D1..D9 → 1..9, NumPad1..NumPad9 → 1..9. VK.D0 = 0x30, VK.NUMPAD0 = 0x60.
    /// </summary>
    public static int FromVirtualKey(int virtualKey)
    {
        if (virtualKey >= 0x31 && virtualKey <= 0x39) return virtualKey - 0x30;
        if (virtualKey >= 0x61 && virtualKey <= 0x69) return virtualKey - 0x60;
        return 0;
    }

    /// <summary>Номер плитки по коду клавиши WPF (Key.D1..D9 / Key.NumPad1..NumPad9).</summary>
    public static int FromWpfKey(int wpfKey, int wpfD0, int wpfNumPad0)
    {
        if (wpfKey >= wpfD0 + Min && wpfKey <= wpfD0 + Max) return wpfKey - wpfD0;
        if (wpfKey >= wpfNumPad0 + Min && wpfKey <= wpfNumPad0 + Max) return wpfKey - wpfNumPad0;
        return 0;
    }

    /// <summary>
    /// Клавиша, которую надо читать: при Alt WPF присылает Key.System,
    /// а настоящая клавиша спрятана в SystemKey.
    /// </summary>
    public static int EffectiveKey(int key, int systemKey, int keySystemValue)
        => key == keySystemValue ? systemKey : key;

    /// <summary>Влезает ли номер в список плиток.</summary>
    public static bool IsInRange(int digit, int pinCount)
        => digit >= Min && digit <= Max && digit <= pinCount;

    /// <summary>
    /// Индекс элемента в списке плиток, который должен запустить Alt+<paramref name="digit"/>.
    /// </summary>
    /// <param name="digit">Номер плитки (1..9).</param>
    /// <param name="rootCount">Сколько плиток в корне «ЗАКРЕПЛЕНО».</param>
    /// <param name="openFolderCount">
    /// Сколько элементов в открытой папке, или <c>null</c>/<c>-1</c>, если папка не открыта.
    /// Ноль — папка открыта, но пуста: тогда номера не срабатывают.
    /// </param>
    /// <returns>Индекс в актуальном списке или -1, если такого элемента нет.</returns>
    /// <remarks>
    /// Если открыта папка, цифры адресуют ЕЁ элементы, а не корень: пользователь видит
    /// на плитках номера именно открытого списка, и Alt+1 должен запускать первый его элемент.
    /// </remarks>
    public static int ResolveIndex(int digit, int rootCount, int? openFolderCount)
    {
        int count = openFolderCount is null or < 0 ? rootCount : openFolderCount.Value;
        if (digit < Min || digit > Max) return -1;
        if (digit > count) return -1;
        return digit - 1;
    }
}
