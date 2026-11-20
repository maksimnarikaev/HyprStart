namespace HyprStart.Core.Theming;

/// <summary>Цвет в HSV: H 0..360, S/V 0..1.</summary>
public readonly record struct HsvColor(double H, double S, double V);

/// <summary>RGB-цвет 0..255.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>
/// Умный экстрактор палитры из крошечного сэмпла экрана (обычно 32×18 px).
/// Отбрасывает тёмные и ненасыщенные пиксели, ищет два доминирующих оттенка
/// из разных секторов цветового круга. Никаких зависимостей и циклов —
/// один проход по ~576 пикселям, десятки микросекунд.
/// </summary>
public static class WallpaperColorExtractor
{
    /// <summary>Секторов на цветовом круге для гистограммы.</summary>
    public const int HueBuckets = 12;

    /// <summary>Минимальная насыщенность: серое и «грязное» отбрасываем.</summary>
    public const double MinSaturation = 0.22;

    /// <summary>Минимальная яркость: ночные сцены не должны давать грязно-серый.</summary>
    public const double MinValue = 0.16;

    public readonly record struct Palette(RgbColor Primary, RgbColor Secondary, bool IsMonochrome)
    {
        /// <summary>Цвет подложки: очень тёмный тон основного оттенка.</summary>
        public RgbColor Backdrop(double value = 0.08)
        {
            var hsv = ColorMath.ToHsv(Primary);
            return ColorMath.FromHsv(hsv.H, Math.Min(0.45, hsv.S * 0.55), value);
        }
    }

    /// <summary>
    /// Извлекает палитру из BGRA-пикселей (порядок как у GDI BitBlt: B,G,R,A).
    /// </summary>
    /// <param name="bgra">Сырые пиксели, минимум <paramref name="width"/>*<paramref name="height"/>*4 байт.</param>
    /// <param name="width">Ширина сэмпла.</param>
    /// <param name="height">Высота сэмпла.</param>
    /// <param name="stride">Длина строки в байтах (по умолчанию width*4).</param>
    public static Palette Extract(ReadOnlySpan<byte> bgra, int width, int height, int stride = 0)
    {
        if (width <= 0 || height <= 0) return Monochrome();
        if (stride <= 0) stride = width * 4;

        Span<double> weight = stackalloc double[HueBuckets];
        Span<double> sinSum = stackalloc double[HueBuckets];
        Span<double> cosSum = stackalloc double[HueBuckets];
        Span<double> satSum = stackalloc double[HueBuckets];
        Span<double> valSum = stackalloc double[HueBuckets];

        double total = 0;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            if (rowOffset + width * 4 > bgra.Length) break;

            for (int x = 0; x < width; x++)
            {
                int offset = rowOffset + x * 4;
                var rgb = new RgbColor(bgra[offset + 2], bgra[offset + 1], bgra[offset]);
                var hsv = ColorMath.ToHsv(rgb);
                if (hsv.S < MinSaturation || hsv.V < MinValue) continue;

                int bucket = (int)(hsv.H / (360.0 / HueBuckets)) % HueBuckets;
                // Вес: насыщенность важнее яркости, но без квадрата — иначе приглушённый
                // красный (S≈0.45) проигрывал вдвое любой мелкой яркой подсветке.
                double w = (0.35 + 0.65 * hsv.S) * (0.4 + 0.6 * hsv.V);
                double radians = hsv.H * Math.PI / 180.0;

                weight[bucket] += w;
                sinSum[bucket] += w * Math.Sin(radians);
                cosSum[bucket] += w * Math.Cos(radians);
                satSum[bucket] += w * hsv.S;
                valSum[bucket] += w * hsv.V;
                total += w;
            }
        }

        if (total <= 0) return Monochrome();

        int first = ArgMax(weight);
        if (weight[first] <= 0) return Monochrome();

        // Ищем второй доминирующий сектор, достаточно удалённый по цветовому кругу.
        int second = -1;
        double bestSecond = 0;
        for (int i = 0; i < HueBuckets; i++)
        {
            if (i == first || weight[i] <= 0) continue;
            if (HueDistance(BucketCenter(first), BucketCenter(i)) < 60) continue;
            if (weight[i] <= bestSecond) continue;
            bestSecond = weight[i];
            second = i;
        }

        // Сцена монохромна, если второго оттенка нет вовсе или он еле заметен.
        // Раньше монохромный кадр уходил в фоллбэк (розовый) — из-за этого красные и
        // жёлтые обои никогда не давали свой акцент. Теперь второй тон выводим из первого.
        bool monochrome = second < 0 || bestSecond < weight[first] * 0.08;

        var primary = BucketColor(first, sinSum, cosSum, satSum, valSum, weight);
        var secondary = monochrome
            ? DeriveSecond(primary)
            : BucketColor(second, sinSum, cosSum, satSum, valSum, weight);

        if (!monochrome && HueDistance(ColorMath.ToHsv(primary).H, ColorMath.ToHsv(secondary).H) < 25)
        {
            secondary = DeriveSecond(primary);
        }

        return new Palette(primary, secondary, monochrome);
    }

    /// <summary>
    /// Второй тон для монохромной сцены: сдвиг по кругу на 35°, чуть мягче по насыщенности
    /// и чуть светлее — так градиент читается, но остаётся в гамме обоев.
    /// </summary>
    private static RgbColor DeriveSecond(RgbColor primary)
    {
        var hsv = ColorMath.ToHsv(primary);
        return ColorMath.FromHsv(
            hsv.H + 35,
            Math.Clamp(hsv.S * 0.9, 0.25, 1),
            Math.Clamp(hsv.V * 1.1, 0.35, 1));
    }

    private static int BucketCenter(int bucket) => (int)((bucket + 0.5) * (360.0 / HueBuckets));

    private static double HueDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Суммарный вес секторов в пределах ±50° от данного — «однотонная» масса.</summary>
    private static double NearWeight(ReadOnlySpan<double> weight, int bucket)
    {
        double center = BucketCenter(bucket);
        double sum = 0;
        for (int i = 0; i < HueBuckets; i++)
        {
            if (HueDistance(center, BucketCenter(i)) <= 50) sum += weight[i];
        }

        return sum;
    }


    private static int ArgMax(ReadOnlySpan<double> values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best]) best = i;
        }

        return best;
    }

    /// <summary>Усреднённый цвет сектора: через векторную сумму (без шва на 0/360).</summary>
    private static RgbColor BucketColor(
        int bucket,
        ReadOnlySpan<double> sinSum,
        ReadOnlySpan<double> cosSum,
        ReadOnlySpan<double> satSum,
        ReadOnlySpan<double> valSum,
        ReadOnlySpan<double> weight)
    {
        double w = weight[bucket];
        if (w <= 0) return new RgbColor(0x18, 0x12, 0x1E);

        double hue = Math.Atan2(sinSum[bucket], cosSum[bucket]) * 180.0 / Math.PI;
        if (hue < 0) hue += 360;

        double sat = Math.Clamp(satSum[bucket] / w, 0.35, 0.95);
        // поднимаем яркость до комфортной для акцента
        double val = Math.Clamp(valSum[bucket] / w * 1.25, 0.55, 0.95);
        return ColorMath.FromHsv(hue, sat, val);
    }

    /// <summary>Розово-лиловая палитра sakura-pink — фолбэк для монохромных сцен.</summary>
    public static Palette Monochrome() => new(
        new RgbColor(0xFF, 0x75, 0xB5),
        new RgbColor(0xC7, 0x92, 0xEA),
        true);
}

/// <summary>HSV/RGB/Lab-подобные преобразования без зависимостей.</summary>
public static class ColorMath
{
    public static HsvColor ToHsv(RgbColor rgb)
    {
        double r = rgb.R / 255.0, g = rgb.G / 255.0, b = rgb.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0.00001)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }

        if (hue < 0) hue += 360;
        double saturation = max <= 0 ? 0 : delta / max;
        return new HsvColor(hue, saturation, max);
    }

    public static RgbColor FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new RgbColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Относительная яркость по WCAG — нужна для проверки читаемости текста.</summary>
    public static double Luminance(RgbColor rgb)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    /// <summary>Контраст двух цветов (1..21).</summary>
    public static double Contrast(RgbColor a, RgbColor b)
    {
        double la = Luminance(a), lb = Luminance(b);
        double lighter = Math.Max(la, lb), darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static string ToHex(RgbColor rgb) => $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    public static string ToHexArgb(byte alpha, RgbColor rgb) => $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    /// <summary>Парсит #RGB / #RRGGBB / #AARRGGBB. Возвращает null при мусоре.</summary>
    public static RgbColor? ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length == 8) value = value[2..]; // отбрасываем альфу
        if (value.Length != 6) return null;

        if (!byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)) return null;
        if (!byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)) return null;
        if (!byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b)) return null;

        return new RgbColor(r, g, b);
    }
}
