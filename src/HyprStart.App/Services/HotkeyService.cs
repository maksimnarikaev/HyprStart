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

    /// <summary>
    /// Сколько «клиентов» просят приостановить глобальный хоткей в данный момент.
    /// Пока счётчик больше нуля, зарегистрированная комбинация фактически отпущена:
    /// иначе переназначение хоткея в настройках невозможно — нажатие старой
    /// комбинации сразу сворачивало лаунчер, пока пользователь ещё набирал новую.
    /// </summary>
    private static int _pauseCount;

    /// <summary>
    /// Самый свежий живой экземпляр. Нужен, чтобы подавление, меняющее счётчик,
    /// могло немедленно снять/вернуть регистрацию, а не ждать следующего Apply.
    /// </summary>
    private static HotkeyService? _current;

    /// <summary>Глобальный хоткей сейчас приостановлен (кто-то пишет комбинацию)?</summary>
    public static bool IsPaused => Volatile.Read(ref _pauseCount) > 0;

    /// <summary>
    /// Приостанавливает глобальный хоткей на время записи комбинации и возвращает
    /// IDisposable: освобождение возвращает регистрацию (в т.ч. при исключении).
    /// Повторные вызовы безопасны — используется счётчик, а не флаг.
    /// </summary>
    public static IDisposable Pause()
    {
        Interlocked.Increment(ref _pauseCount);
        _current?.SuspendRegistration();
        return new PauseScope();
    }

    private sealed class PauseScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            if (Interlocked.Decrement(ref _pauseCount) == 0)
                _current?.ResumeRegistration();
        }
    }

    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private HotkeySpec _lastApplied;
    private bool _lastAppliedValid;

    public HotkeyService(HwndSource source, Action onPressed)
    {
        _source = source;
        _onPressed = onPressed;
        _source.AddHook(WndProc);
        _current = this;
    }

    public bool IsRegistered { get; private set; }
    public HotkeySpec Spec { get; private set; }

    /// <summary>Регистрирует (или перерегистрирует) комбинацию. Возвращает false, если занята другой программой.</summary>
    public bool Apply(HotkeySpec spec)
    {
        Unregister();
        Spec = spec;
        _lastApplied = spec;
        _lastAppliedValid = spec.IsValid;

        if (!spec.IsValid)
        {
            AppLog.Write("hotkey: некорректная комбинация в настройках, оверлей доступен только из трея");
            return false;
        }

        // Идёт запись комбинации в настройках — не захватываем клавиатуру раньше времени.
        if (IsPaused)
        {
            AppLog.Write($"hotkey: {spec.Describe()} применён, но регистрация отложена (идёт запись комбинации)");
            return true;
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

    /// <summary>Отпускает комбинацию ОС, не забывая, что она «должна быть зарегистрирована».</summary>
    private void SuspendRegistration()
    {
        if (!IsRegistered) return;
        Unregister();
        AppLog.Write($"hotkey: приостановлен {Spec.Describe()} (запись комбинации в настройках)");
    }

    /// <summary>Возвращает комбинацию ОС после окончания записи.</summary>
    private void ResumeRegistration()
    {
        if (!_lastAppliedValid || IsRegistered) return;

        bool ok = NativeMethods.RegisterHotKey(
            _source.Handle,
            HotkeyId,
            _lastApplied.Modifiers | NativeMethods.MOD_NOREPEAT,
            _lastApplied.VirtualKey);

        IsRegistered = ok;
        AppLog.Write(ok
            ? $"hotkey: возобновлён {_lastApplied.Describe()}"
            : $"hotkey: возобновить {_lastApplied.Describe()} не удалось — комбинацию заняли");
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
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
