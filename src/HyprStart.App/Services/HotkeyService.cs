using System.Runtime.InteropServices;
using System.Windows.Interop;
using HyprStart.App.Interop;
using HyprStart.Core.Input;

namespace HyprStart.App.Services;

/// <summary>
/// Глобальная горячая клавиша через RegisterHotKey + WM_HOTKEY.
///
/// Почему так, а не хук: RegisterHotKey не вмешивается в поток клавиатуры вообще.
/// Состояние модификаторов в системе остаётся консистентным, залипнуть нечему —
/// в отличие от подавления keyup Win в WH_KEYBOARD_LL, которое оставляло ОС
/// в состоянии «Win зажат» и превращало весь ввод в Win+&lt;клавиша&gt;.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4853; // 'HS'

    private readonly HwndSource _source;
    private readonly Action _onPressed;

    public HotkeyService(HwndSource source, Action onPressed)
    {
        _source = source;
        _onPressed = onPressed;
        _source.AddHook(WndProc);
    }

    public bool IsRegistered { get; private set; }
    public HotkeySpec Spec { get; private set; }

    /// <summary>Регистрирует (или перерегистрирует) комбинацию. Возвращает false, если занята другой программой.</summary>
    public bool Apply(HotkeySpec spec)
    {
        Unregister();
        Spec = spec;

        if (!spec.IsValid)
        {
            AppLog.Write("hotkey: некорректная комбинация в настройках, оверлей доступен только из трея");
            return false;
        }

        bool ok = NativeMethods.RegisterHotKey(
            _source.Handle,
            HotkeyId,
            spec.Modifiers | NativeMethods.MOD_NOREPEAT,
            spec.VirtualKey);

        IsRegistered = ok;

        AppLog.Write(ok
            ? $"hotkey: зарегистрирован {spec.Describe()}"
            : $"hotkey: НЕ зарегистрирован {spec.Describe()} (win32={Marshal.GetLastWin32Error()}) — возможно, комбинацию заняла другая программа");

        return ok;
    }

    private void Unregister()
    {
        if (!IsRegistered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        IsRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            AppLog.Write("hotkey: нажатие -> переключаем оверлей");
            _onPressed();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
