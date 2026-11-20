using System.Runtime.InteropServices;
using System.Threading;
using HyprStart.App.Interop;
using HyprStart.Core.Input;

namespace HyprStart.App.Services;

/// <summary>
/// ЛЕГАСИ, ПО УМОЛЧАНИЮ ВЫКЛЮЧЕН (interceptWindowsKey=false).
///
/// Глобальный перехват клавиатуры (WH_KEYBOARD_LL) на отдельном потоке с собственным
/// message loop. Колбэк не блокируется и не аллоцирует в горячем пути.
/// Раз в 60 секунд хук переустанавливается: Windows молча снимает его при таймауте.
///
/// ИЗВЕСТНЫЙ ДЕФЕКТ (почему выключен): чтобы не открывалось нативное «Пуск», хук
/// подавлял keyup клавиши Win, но keydown пропускал. ОС при этом остаётся в состоянии
/// «Win зажат»: любые дальнейшие клавиши трактуются как Win+&lt;клавиша&gt; — ввод и Backspace
/// перестают работать, вместо них открываются проводник/поиск/параметры.
/// Лечится только переработкой на инъекции клавиш (см. бэклог в PLAN.md), до этого
/// режим включать нельзя. Штатный путь открытия меню — RegisterHotKey (HotkeyService).
/// </summary>
internal sealed class KeyboardHookService : IDisposable
{
    private const int WM_QUIT = 0x0012;
    private const int WatchdogPeriodMs = 60_000;

    // Делегаты держим в статических полях — иначе GC соберёт их и хук упадёт.
    private static NativeMethods.LowLevelKeyboardProc? _proc;
    private static KeyboardHookService? _current;

    // Пауза на время записи комбинации в окне настроек: хук не должен
    // принимать нажатия, пока пользователь переназначает глобальный хоткей.
    private static int _pauseCount;

    private readonly WinKeyStateMachine _machine;
    private readonly ManualResetEventSlim _installed = new(false);

    private Thread? _thread;
    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _threadId;
    private System.Threading.Timer? _watchdog;
    private volatile bool _running;
    private volatile bool _verbose;
    private volatile bool _installFailed;

    public KeyboardHookService(WinKeyStateMachine machine) => _machine = machine;

    /// <summary>Вызывается на потоке хука. Подписчик обязан вернуться немедленно.</summary>
    public event Action? OverlayRequested;

    public bool IsInstalled => Volatile.Read(ref _hookHandle) != IntPtr.Zero;

    /// <summary>Хук временно не реагирует на клавиатуру (идёт запись комбинации).</summary>
    public static bool IsPaused => Volatile.Read(ref _pauseCount) > 0;

    /// <summary>
    /// Приостановить обработку хука. Возвращает scope: Dispose снимает паузу.
    /// Вложенные вызовы безопасны — счётчик, а не флаг.
    /// </summary>
    public static IDisposable Pause()
    {
        Interlocked.Increment(ref _pauseCount);
        return new PauseScope();
    }

    private sealed class PauseScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Interlocked.Decrement(ref _pauseCount) < 0) Volatile.Write(ref _pauseCount, 0);
        }
    }
    public bool InstallFailed => _installFailed;
    public WinKeyStateMachine Machine => _machine;

    /// <summary>Печатать каждое событие клавиатуры в консоль/лог (режим --diag).</summary>
    public bool Verbose
    {
        get => _verbose;
        set => _verbose = value;
    }

    public void Start()
    {
        _proc = HookProc;
        _current = this;
        _running = true;

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "HyprStart.KeyboardHook",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();

        if (!_installed.Wait(TimeSpan.FromSeconds(3)))
        {
            AppLog.Write("hook: таймаут установки");
        }
    }

    private void ThreadMain()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        InstallHook();
        _installed.Set();

        if (_running) StartWatchdog();

        while (_running)
        {
            int result = NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (result is 0 or -1) break;

            if (msg.message == NativeMethods.WM_APP_WATCHDOG)
            {
                ReinstallHook();
                continue;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        UninstallHook();
        _watchdog?.Dispose();
        AppLog.Write("hook: поток перехвата остановлен");
    }

    private void InstallHook()
    {
        var module = NativeMethods.GetModuleHandle(null);
        var handle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc!, module, 0);

        if (handle == IntPtr.Zero)
        {
            _installFailed = true;
            AppLog.Write($"hook: SetWindowsHookEx FAILED, win32={Marshal.GetLastWin32Error()}");
            return;
        }

        Volatile.Write(ref _hookHandle, handle);
        _installFailed = false;
        AppLog.Write($"hook: установлен handle=0x{handle.ToInt64():X}, thread={_threadId}");
    }

    private void ReinstallHook()
    {
        UninstallHook();
        InstallHook();
        AppLog.Write("hook: переустановлен по сторожевому таймеру");
    }

    private void UninstallHook()
    {
        var handle = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
        if (handle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(handle);
    }

    private void StartWatchdog()
    {
        _watchdog = new System.Threading.Timer(
            _ =>
            {
                var id = _threadId;
                if (id == 0) return;
                NativeMethods.PostThreadMessage(id, NativeMethods.WM_APP_WATCHDOG, IntPtr.Zero, IntPtr.Zero);
            },
            state: null,
            dueTime: WatchdogPeriodMs,
            period: WatchdogPeriodMs);
    }

    /// <summary>ВНИМАНИЕ: вызывается на потоке хука. Только быстрые операции.</summary>
    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var self = _current;
        if (nCode < 0 || self is null)
        {
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // Пауза (запись комбинации в настройках): не трогаем клавиатуру вообще,
        // иначе нажатие Alt/одиночного Win во время записи уводило окно в меню.
        if (IsPaused)
        {
            return NativeMethods.CallNextHookEx(self._hookHandle, nCode, wParam, lParam);
        }

        int message = unchecked((int)wParam.ToInt64());
        int vk = Marshal.ReadInt32(lParam);
        var decision = self._machine.Process(message, vk);

        if (self._verbose)
        {
            AppLog.Write($"hook: событие msg=0x{message:X} vk=0x{vk:X} -> {decision}");
        }

        if (decision == HookDecision.SuppressAndOpen)
        {
            AppLog.Write("hook: тап Win -> подавлен, открываем оверлей");
            self.OverlayRequested?.Invoke();
            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(self._hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _running = false;
        _watchdog?.Dispose();
        _watchdog = null;

        var id = _threadId;
        if (id != 0)
        {
            NativeMethods.PostThreadMessage(id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(500);
        UninstallHook();
        _current = null;
        _installed.Dispose();
    }
}
