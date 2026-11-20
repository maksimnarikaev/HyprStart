using System.Runtime.InteropServices;

namespace HyprStart.App.Interop;

/// <summary>Все Win32-вызовы приложения в одном месте.</summary>
internal static class NativeMethods
{
    // ---- хук клавиатуры (легаси, по умолчанию ВЫКЛЮЧЕН) ----
    internal const int WH_KEYBOARD_LL = 13;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;

    internal const int VK_LBUTTON = 0x01;
    internal const int VK_RBUTTON = 0x02;
    internal const int VK_MBUTTON = 0x04;

    internal const int WM_APP_WATCHDOG = 0x8000 + 1;

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ---- горячая клавиша ----
    internal const int WM_HOTKEY = 0x0312;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const byte VK_MENU = 0x12;
    internal const byte VK_SPACE = 0x20;
    internal const int KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    internal static extern bool AllocConsole();

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    internal static bool IsPointerDown()
    {
        unchecked
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                   || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                   || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        }
    }
}
