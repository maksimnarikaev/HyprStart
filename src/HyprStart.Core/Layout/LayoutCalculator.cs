namespace HyprStart.Core.Layout;

/// <summary>
/// Геометрия оверлея. Считается по ПОЛНОМУ размеру экрана (Screen.Bounds), а не по WorkingArea:
/// панель задач в автоскрытии, место под ней принадлежит оверлею.
/// </summary>
public sealed record LayoutSpec
{
    public double Margin { get; init; } = 36;
    public double Gap { get; init; } = 10;
    public double PolybarHeight { get; init; } = 26;
    public double LeftColumnPercent { get; init; } = 27;
    public double RightTopPercent { get; init; } = 62;
    public double FrequentPercent { get; init; } = 55;

    public static LayoutSpec Default => new();
}

public sealed record LayoutResult(
    PixelRect Apps,
    PixelRect Pinned,
    PixelRect Chat,
    PixelRect Frequent,
    PixelRect Quick,
    PixelRect Polybar);

public static class LayoutCalculator
{
    /// <summary>
    /// Насколько оверлей ниже физического экрана. Ровно 1 пиксель: окно, перекрывающее
    /// экран «от края до края», Windows считает полноэкранным приложением, и Wallpaper Engine
    /// ставит обои на паузу. Минус один пиксель сохраняет визуально бесшовный вид,
    /// но выводит окно из категории fullscreen.
    /// </summary>
    public const int FullscreenTrim = 1;

    /// <summary>
    /// Размер оверлея из размеров экрана: по высоте отнимаем <see cref="FullscreenTrim"/>,
    /// чтобы Windows/Wallpaper Engine не классифицировали окно как полноэкранное.
    /// 1920x1080 → 1920x1079.
    /// </summary>
    public static (double Width, double Height) OverlaySize(double screenWidth, double screenHeight)
    {
        double width = Math.Max(0, screenWidth);
        double height = Math.Max(0, screenHeight - FullscreenTrim);
        return (width, height);
    }

    /// <summary>
    /// Раскладка 4 панелей + polybar внутри прямоугольника (в DIP).
    /// Все значения клампятся, поэтому даже при крошечном экране результат неотрицательный.
    /// </summary>
    /// <param name="width">Ширина оверлея (DIP).</param>
    /// <param name="height">Высота оверлея (DIP).</param>
    /// <param name="spec">Параметры раскладки; null - <see cref="LayoutSpec.Default"/>.</param>
    /// <param name="hideChatPanel">
    /// Панель MINI AI скрыта: «Закреплено» расширяется на всю ширину правой колонки
    /// и встаёт над «Быстрым доступом», а не делит с чатом верхнюю ячейку пополам.
    /// Панель чата при этом вырождается в прямоугольник нулевой ширины.
    /// </param>
    public static LayoutResult Compute(
        double width,
        double height,
        LayoutSpec? spec = null,
        bool hideChatPanel = false)
    {
        spec ??= LayoutSpec.Default;

        double margin = Clamp(spec.Margin, 0, Math.Min(width, height) / 2.0);
        double gap = Clamp(spec.Gap, 0, 40);
        double polybarHeight = Clamp(spec.PolybarHeight, 0, height / 4.0);

        double contentWidth = Math.Max(0, width - 2 * margin);
        double contentHeight = Math.Max(0, height - 2 * margin - polybarHeight - gap);

        double leftWidth = Round(contentWidth * Percent(spec.LeftColumnPercent));
        leftWidth = Math.Min(leftWidth, Math.Max(0, contentWidth - gap));
        double rightWidth = Math.Max(0, contentWidth - leftWidth - gap);

        double pinnedHeight = Round(contentHeight * Percent(spec.RightTopPercent));
        pinnedHeight = Math.Min(pinnedHeight, Math.Max(0, contentHeight - gap));
        double bottomHeight = Math.Max(0, contentHeight - pinnedHeight - gap);

        double frequentWidth = Round(rightWidth * Percent(spec.FrequentPercent));
        frequentWidth = Math.Min(frequentWidth, Math.Max(0, rightWidth - gap));
        double quickWidth = Math.Max(0, rightWidth - frequentWidth - gap);

        double rightX = margin + leftWidth + gap;
        double bottomY = margin + pinnedHeight + gap;

        // Ширина «Закреплено» и мини-чата.
        // Обычный режим: верхнюю правую ячейку делим пополам, gap тот же, что между
        // остальными карточками, — сетка остаётся ровной.
        // Режим hideChatPanel: чата нет, поэтому «Закреплено» забирает всю правую
        // колонку целиком — справа не остаётся пустой полосы.
        double pinnedWidth;
        double chatWidth;
        double chatX;

        if (hideChatPanel)
        {
            pinnedWidth = rightWidth;
            chatWidth = 0;
            // Чат вырожден: ставим его в правый край правой колонки нулевой ширины.
            chatX = rightX + rightWidth;
        }
        else
        {
            pinnedWidth = Math.Max(0, Round((rightWidth - gap) / 2.0));
            pinnedWidth = Math.Min(pinnedWidth, Math.Max(0, rightWidth - gap));
            chatWidth = Math.Max(0, rightWidth - pinnedWidth - gap);
            chatX = rightX + pinnedWidth + gap;
        }

        return new LayoutResult(
            Apps: new PixelRect(margin, margin, leftWidth, contentHeight),
            Pinned: new PixelRect(rightX, margin, pinnedWidth, pinnedHeight),
            Chat: new PixelRect(chatX, margin, chatWidth, pinnedHeight),
            Frequent: new PixelRect(rightX, bottomY, frequentWidth, bottomHeight),
            Quick: new PixelRect(rightX + frequentWidth + gap, bottomY, quickWidth, bottomHeight),
            Polybar: new PixelRect(margin, height - margin - polybarHeight, contentWidth, polybarHeight));
    }

    private static double Percent(double value) => Clamp(value, 0, 100) / 100.0;

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return min;
        return value < min ? min : value > max ? max : value;
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
