using System.Runtime.InteropServices;

namespace HyprStart.App.Services;

/// <summary>
/// Один быстрый GDI-снимок рабочего стола, уменьшенный до микро-буфера (32×18).
/// Захват делается только в момент открытия оверлея и только если включена тема "auto":
/// фонового опроса нет вообще, тик в простое остаётся нулевым.
/// Стоимость: одна BitBlt + GetDIBits в буфер 32×18×4 = 2.3 КБ — порядка 1 мс.
/// </summary>
internal static class ScreenSampler
{
    public const int SampleWidth = 32;
    public const int SampleHeight = 18;

    private const uint Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const int BiRgb = 0;

    /// <summary>
    /// Возвращает BGRA-буфер 32×18×4, снятый с виртуального рабочего экрана.
    /// null — если снимок невозможен (сессия заблокирована, RDP, ошибка GDI).
    /// </summary>
    public static byte[]? Capture()
    {
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            int width = GetSystemMetrics(SystemMetricXVirtualScreen)
                        + GetSystemMetrics(SystemMetricCxVirtualScreen);
            int height = GetSystemMetrics(SystemMetricYVirtualScreen)
                         + GetSystemMetrics(SystemMetricCyVirtualScreen);

            if (width <= 0 || height <= 0) return null;

            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;

            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) return null;

            bitmap = CreateCompatibleBitmap(screenDc, SampleWidth, SampleHeight);
            if (bitmap == IntPtr.Zero) return null;

            oldBitmap = SelectObject(memoryDc, bitmap);

            // HALFTONE + SetStretchBltMode: усреднение при сжатии вместо выкидывания пикселей.
            // Без этого один случайный пиксель на 60×60 экранных даёт грязный результат.
            SetStretchBltMode(memoryDc, StretchModeHalftone);
            SetBrushOrgEx(memoryDc, 0, 0, IntPtr.Zero);

            bool ok = StretchBlt(
                memoryDc, 0, 0, SampleWidth, SampleHeight,
                screenDc,
                GetSystemMetrics(SystemMetricXVirtualScreen),
                GetSystemMetrics(SystemMetricYVirtualScreen),
                width, height,
                Srccopy);

            if (!ok) return null;

            var header = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = SampleWidth,
                    Height = -SampleHeight,   // минус = top-down: строка 0 сверху, как ждёт экстрактор
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };

            var buffer = new byte[SampleWidth * SampleHeight * 4];
            int scanned = GetDIBits(memoryDc, bitmap, 0, SampleHeight, buffer, ref header, DibRgbColors);

            return scanned == SampleHeight ? buffer : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const int SystemMetricXVirtualScreen = 76;
    private const int SystemMetricYVirtualScreen = 77;
    private const int SystemMetricCxVirtualScreen = 78;
    private const int SystemMetricCyVirtualScreen = 79;
    private const int StretchModeHalftone = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        IntPtr destDc, int xDest, int yDest, int widthDest, int heightDest,
        IntPtr srcDc, int xSrc, int ySrc, int widthSrc, int heightSrc,
        uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hDc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool SetBrushOrgEx(IntPtr hDc, int x, int y, IntPtr point);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hDc, IntPtr bitmap, uint start, uint lines,
        byte[] bits, ref BitmapInfo info, uint usage);
}
