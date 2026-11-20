namespace HyprStart.Core.Layout;

/// <summary>
/// Геометрия плиток «ЗАКРЕПЛЕНО»: 5 колонок, число строк определяется числом пинов.
/// Ширина единицы = min(по ширине, 124): на широком экране плитки не растягиваются
/// до гигантских, на узком — ужимаются, а не вылезают за край. Высота компактная
/// фиксированная (<see cref="TileHeightValue"/>) — без пустого места вокруг иконки.
/// Живёт в Core (чистая математика без WPF), чтобы покрывать раскладку тестами.
/// </summary>
public static class PinTileGeometry
{
    /// <summary>Колонок в сетке закреплённого по умолчанию: 5 в ряду, при 7-8 пинах — 5 + 2..3.</summary>
    public const int Columns = 5;

    /// <summary>
    /// Верхняя граница числа колонок. Панель «Закреплено» при скрытом мини-чате
    /// занимает всю ширину правой колонки, и плитки 124 DIP дали бы слишком много
    /// колонок — ограничиваем, чтобы сетка читалась как сетка, а не как строка.
    /// </summary>
    public const int MaxColumns = 8;

    /// <summary>
    /// Строк больше не фиксируем: UniformGrid сам добавляет ряды по числу плиток.
    /// Константа оставлена для совместимости (её читают тесты раскладки).
    /// </summary>
    public const int Rows = 2;

    /// <summary>
    /// Потолок размера плитки по ширине (DIP). Должен быть не меньше слота при
    /// inner=626: (626 - 4*8)/5 = 118.8, иначе пятая плитка уезжает на вторую строку.
    /// </summary>
    public const double MaxTileWidth = 124;

    /// <summary>Отступ между плитками по горизонтали и вертикали (DIP).</summary>
    public const double TileGap = 8;

    /// <summary>
    /// Высота плитки (DIP). Была производной от ширины (~136 px при inner=626), но вокруг
    /// иконки оставалось много пустого места — фиксируем компактные 96 px: иконка 44x44,
    /// подпись в одну-две строки. Высота больше не зависит от ширины плитки.
    /// </summary>
    public const double TileHeightValue = 96;

    /// <summary>Размер иконки приложения внутри плитки (DIP): 44x44 вместо прежних 51.</summary>
    public const double IconSize = 44;

    /// <summary>
    /// Сколько колонок уместить в доступной ширине при «идеальной» ширине плитки
    /// <see cref="MaxTileWidth"/>. Растёт, когда панель шире (скрыт мини-чат),
    /// и не опускается ниже <see cref="Columns"/>, чтобы привычная сетка 5xN
    /// не превращалась в 3xN на узкой панели.
    /// </summary>
    public static int ColumnCount(double availableWidth)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
            return Columns;

        // n плиток и (n-1) промежутков должны уложиться в availableWidth.
        double fits = Math.Floor((availableWidth + TileGap) / (MaxTileWidth + TileGap));
        if (double.IsNaN(fits) || fits < Columns) return Columns;
        return fits > MaxColumns ? MaxColumns : (int)fits;
    }

    /// <summary>Размер плитки по ширине для доступной ширины контента (адаптивное число колонок).</summary>
    public static double TileWidth(double availableWidth)
        => TileWidth(availableWidth, ColumnCount(availableWidth));

    /// <summary>
    /// Размер плитки по ширине для явно заданного числа колонок.
    /// Нужен тестам и раскладке, которая уже посчитала колонки для конкретной панели.
    /// </summary>
    public static double TileWidth(double availableWidth, int columns)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0) return 0;
        if (columns < 1) columns = 1;

        double perColumn = (availableWidth - (columns - 1) * TileGap) / columns;
        return perColumn <= 0 ? 0 : Math.Min(perColumn, MaxTileWidth);
    }

    /// <summary>Высота плитки: компактная фиксированная (см. <see cref="TileHeightValue"/>).</summary>
    public static double TileHeight(double tileWidth)
    {
        if (double.IsNaN(tileWidth) || double.IsInfinity(tileWidth) || tileWidth <= 0) return 0;
        return TileHeightValue;
    }

    /// <summary>Полная высота сетки — используется для решения о прокрутке.</summary>
    public static double GridHeight(double tileHeight) =>
        tileHeight <= 0 ? 0 : tileHeight;

    /// <summary>Влезает ли сетка 4x2 в доступную высоту без прокрутки.</summary>
    public static bool FitsWithoutScroll(double availableWidth, double availableHeight)
        => FitsWithoutScroll(availableWidth, availableHeight, ColumnCount(availableWidth));

    /// <summary>
    /// Влезает ли один ряд плиток в доступную высоту без прокрутки при заданном
    /// числе колонок. Принимает ширину контента (не ширину плитки) — так же, как
    /// раскладка, которая уже посчитала колонки для конкретной панели.
    /// </summary>
    public static bool FitsWithoutScroll(double availableWidth, double availableHeight, int columns)
    {
        double height = GridHeight(TileHeight(TileWidth(availableWidth, columns)));
        return height > 0 && height <= availableHeight;
    }
}
