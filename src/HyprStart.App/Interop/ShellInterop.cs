using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HyprStart.App.Interop;

/// <summary>
/// Извлечение иконок через оболочку: IShellItemImageFactory даёт нормальные 256px
/// и для .lnk, и для UWP (в отличие от SHGetFileInfo).
/// </summary>
internal static class ShellInterop
{
    internal const int SIIGBF_BIGGERSIZEOK = 0x0001;
    internal const int SIIGBF_ICONONLY = 0x0004;

    /// <summary>Разрешает оболочке увеличивать мелкую иконку — иначе глиф 32px остаётся
    /// точкой внутри прозрачного 256-пиксельного холста.</summary>
    internal const int SIIGBF_SCALEUP = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;

        public SIZE(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Сохраняет иконку цели в PNG. Возвращает false, если оболочка не дала картинку.</summary>
    internal static bool TrySaveIcon(string parsingName, string outputPath, int size)
    {
        IntPtr bitmapHandle = IntPtr.Zero;

        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var item);
            if (item is not IShellItemImageFactory factory) return false;

            factory.GetImage(new SIZE(size, size), SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK | SIIGBF_SCALEUP, out bitmapHandle);
            if (bitmapHandle == IntPtr.Zero) return false;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // обрезаем прозрачные поля и растягиваем глиф на весь квадрат
            var filled = FillCanvas(TrimTransparent(source), size);

            // Pbgra32 (premultiplied) → Bgra32, иначе полупрозрачные края иконок темнеют
            var converted = new FormatConvertedBitmap(filled, PixelFormats.Bgra32, null, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(converted));

            using var stream = File.Create(outputPath);
            encoder.Save(stream);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            TryDeleteEmpty(outputPath);
        }
    }

    /// <summary>Обрезает прозрачные поля иконки, чтобы глиф занимал всю площадь.</summary>
    private static BitmapSource TrimTransparent(BitmapSource source)
    {
        try
        {
            var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;

            var pixels = new byte[stride * height];
            formatted.CopyPixels(pixels, stride, 0);

            int minX = width, minY = height, maxX = -1, maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;

                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x * 4 + 3] <= 24) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY) return formatted;

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;

            // глиф и так занимает почти всё полотно — обрезать нечего
            if (cropWidth >= width * 0.75 && cropHeight >= height * 0.75) return formatted;

            return new CroppedBitmap(formatted, new Int32Rect(minX, minY, cropWidth, cropHeight));
        }
        catch
        {
            return source;
        }
    }

    /// <summary>Вписывает картинку в квадратный холст с сохранением пропорций.</summary>
    private static BitmapSource FillCanvas(BitmapSource source, int size)
    {
        try
        {
            double scale = Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
            double drawWidth = source.PixelWidth * scale;
            double drawHeight = source.PixelHeight * scale;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(source, new Rect(
                    (size - drawWidth) / 2.0,
                    (size - drawHeight) / 2.0,
                    drawWidth,
                    drawHeight));
            }

            var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }
        catch
        {
            return source;
        }
    }

    private static void TryDeleteEmpty(string path)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
