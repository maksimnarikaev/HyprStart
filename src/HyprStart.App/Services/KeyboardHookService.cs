using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
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

    // ---------- режим прямой записи комбинации ----------

    // Коды клавиш-модификаторов. Низкоуровневый хук присылает в основном
    // левый/правый вариант (LMENU/RMENU), но иногда и обобщённый — держим оба.
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private static readonly object CaptureSync = new();

    // Колбэк записи: (модификаторы, виртуальный код) -> комбинация принята.
    private static Func<uint, uint, bool>? _capture;

    // Модификаторы, нажатые ПО СОБЫТИЯМ ХУКА. GetAsyncKeyState в момент
    // WM_SYSKEYDOWN ещё не обновился для только что нажатой клавиши.
    private static uint _captureMods;

    // Временный экземпляр хука, если interceptWindowsKey=false и хука не было.
    private static KeyboardHookService? _captureTemp;

    /// <summary>Идёт запись комбинации (все клавиши глушатся).</summary>
    public static bool IsCapturing => Volatile.Read(ref _capture) is not null;

    /// <summary>
    /// Включает перехват одной комбинации прямо из WH_KEYBOARD_LL.
    /// Пока режим активен, модификаторы и основная клавиша в систему не уходят:
    /// Alt не открывает меню, Win не открывает «Пуск», окно не теряет фокус.
    /// Dispose возвращает клавиатуру в обычный режим и снимает временный хук.
    /// </summary>
    public static IDisposable BeginCapture(Func<uint, uint, bool> callback)
    {
        lock (CaptureSync)
        {
            Volatile.Write(ref _captureMods, 0u);
            Volatile.Write(ref _capture, callback);

            if (_current is null)
            {
                var temp = new KeyboardHookService(new WinKeyStateMachine());
                temp.Start();
                _captureTemp = temp;
                AppLog.Write("hook: временный перехват поднят на время записи");
            }
        }

        return new CaptureScope();
    }

    private sealed class CaptureScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            KeyboardHookService? temp;
            lock (CaptureSync)
            {
                Volatile.Write(ref _capture, null);
                Volatile.Write(ref _captureMods, 0u);
                temp = _captureTemp;
                _captureTemp = null;
            }

            if (temp is not null)
            {
                temp.Dispose();
                AppLog.Write("hook: временный перехват снят");
            }
        }
    }

    /// <summary>Бит модификатора для кода клавиши (0 — не модификатор).</summary>
    private static uint ModifierBit(int vk) => vk switch
    {
        VK_MENU or VK_LMENU or VK_RMENU => HotkeySpec.ModAlt,
        VK_CONTROL or VK_LCONTROL or VK_RCONTROL => HotkeySpec.ModControl,
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT => HotkeySpec.ModShift,
        VK_LWIN or VK_RWIN => HotkeySpec.ModWin,
        _ => 0u,
    };

    /// <summary>Страховка: модификаторы, которые физически зажаты прямо сейчас.</summary>
    private static uint ModifiersFromKeyboard()
    {
        uint mods = 0;
        if (IsPhysicallyDown(VK_MENU)) mods |= HotkeySpec.ModAlt;
        if (IsPhysicallyDown(VK_CONTROL)) mods |= HotkeySpec.ModControl;
        if (IsPhysicallyDown(VK_SHIFT)) mods |= HotkeySpec.ModShift;
        if (IsPhysicallyDown(VK_LWIN) || IsPhysicallyDown(VK_RWIN)) mods |= HotkeySpec.ModWin;
        return mods;
    }

    private static bool IsPhysicallyDown(int vk)
        => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Режим записи: true — событие проглотить. Выполняется на потоке хука,
    /// поэтому всё, что может подождать, отдаётся в Dispatcher окна настроек.
    /// </summary>
    private static bool TryCapture(int message, int vk)
    {
        var callback = Volatile.Read(ref _capture);
        if (callback is null) return false;

        bool down;
        if (message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN) down = true;
        else if (message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP) down = false;
        else return false;

        uint bit = ModifierBit(vk);
        if (bit != 0)
        {
            // Модификатор глушим в обе стороны: его нажатие не должно открыть
            // системное меню (Alt) или «Пуск» (Win) и снять фокус с настроек.
            uint mods = Volatile.Read(ref _captureMods);
            Volatile.Write(ref _captureMods, down ? (mods | bit) : (mods & ~bit));
            return true;
        }

        if (!down) return true;

        uint all = Volatile.Read(ref _captureMods) | ModifiersFromKeyboard();

        // Колбэк живёт в UI-потоке: отсюда его звать нельзя (хук должен
        // вернуться за миллисекунды, иначе Windows снимает хук).
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    try
                    {
                        if (callback(all, (uint)vk))
                            AppLog.Write($"hook: комбинация перехвачена mods=0x{all:X} vk=0x{vk:X}");
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write($"hook: ошибка записи комбинации: {ex.Message}");
                    }
                }));
        }

        return true;
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

        int message = unchecked((int)wParam.ToInt64());
        int vk = Marshal.ReadInt32(lParam);

        // Запись комбинации проверяем ДО паузы: она не должна глохнуть вместе с
        // хуком, иначе нажатия уйдут системе и окно настроек потеряет фокус.
        if (TryCapture(message, vk))
        {
            return (IntPtr)1;
        }

        // Пауза (запись комбинации в настройках): не трогаем клавиатуру вообще,
        // иначе нажатие Alt/одиночного Win во время записи уводило окно в меню.
        if (IsPaused)
        {
            return NativeMethods.CallNextHookEx(self._hookHandle, nCode, wParam, lParam);
        }
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
