using System.Runtime.InteropServices;
using HyprStart.Core.Layout;

namespace HyprStart.App.Interop;

/// <summary>
/// Акриловое размытие подложки через недокументированный, но стабильный
/// SetWindowCompositionAttribute, плюс обрезка окна по контурам панелей
/// (SetWindowRgn), чтобы размытие жило только под панелями, а не на всём экране.
///
/// Важно: акрил работает только на НЕ-layered окне, поэтому при включении
/// AllowsTransparency должно быть false, а прозрачность обеспечивает DWM.
/// </summary>
internal static class AcrylicInterop
{
    private const int WcaAccentPolicy = 19;

    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int RgnOr = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>
    /// Без этого при AllowsTransparency=false WPF рисует НЕПРОЗРАЧНУЮ поверхность
    /// и акрил под ней не виден (панели становятся чёрными). «Лист стекла» на весь клиент.
    /// </summary>
    internal static void ExtendGlassFrame(IntPtr hwnd)
    {
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>Включает акрил. tint — в формате ABGR (0xAABBGGRR).</summary>
    internal static bool TryEnableAcrylic(IntPtr hwnd, uint tintAbgr, bool acrylic)
    {
        var policy = new AccentPolicy
        {
            AccentState = acrylic ? AccentEnableAcrylicBlurBehind : AccentEnableBlurBehind,
            AccentFlags = 2,
            GradientColor = unchecked((int)tintAbgr),
            AnimationId = 0,
        };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = buffer,
                SizeOfData = size,
            };

            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Обрезает окно по объединению прямоугольников панелей со скруглением.
    /// Клики и размытие остаются только внутри панелей; гэпы — это рабочий стол.
    /// </summary>
    internal static bool ApplyRegion(IntPtr hwnd, IReadOnlyList<PixelRect> rects, double scale, double cornerRadius)
    {
        IntPtr total = CreateRectRgn(0, 0, 0, 0);

        try
        {
            int ellipse = Math.Max(2, (int)Math.Round(cornerRadius * 2 * scale));

            foreach (var rect in rects)
            {
                if (rect.IsEmpty) continue;

                int left = (int)Math.Round(rect.X * scale);
                int top = (int)Math.Round(rect.Y * scale);
                int right = (int)Math.Round((rect.X + rect.Width) * scale);
                int bottom = (int)Math.Round((rect.Y + rect.Height) * scale);

                IntPtr part = CreateRoundRectRgn(left, top, right, bottom, ellipse, ellipse);
                if (part == IntPtr.Zero) continue;

                CombineRgn(total, total, part, RgnOr);
                DeleteObject(part);
            }

            // при успехе владение регионом переходит системе — DeleteObject не нужен
            return SetWindowRgn(hwnd, total, true) != 0;
        }
        catch
        {
            DeleteObject(total);
            return false;
        }
    }

    /// <summary>Снимает обрезку (окно снова прямоугольное).</summary>
    internal static void ClearRegion(IntPtr hwnd) => SetWindowRgn(hwnd, IntPtr.Zero, true);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);

    /// <summary>0 — региона нет/ошибка, 1 — пустой, 2 — простой, 3 — сложный. Для диагностики.</summary>
    internal static int GetRegionType(IntPtr hwnd)
    {
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            return GetWindowRgn(hwnd, region);
        }
        finally
        {
            DeleteObject(region);
        }
    }

    /// <summary>#AARRGGBB → 0xAABBGGRR (порядок, который ждёт GradientColor).</summary>
    internal static uint ToAbgr(uint aarrggbb)
    {
        uint a = (aarrggbb >> 24) & 0xFF;
        uint r = (aarrggbb >> 16) & 0xFF;
        uint g = (aarrggbb >> 8) & 0xFF;
        uint b = aarrggbb & 0xFF;
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
}
