using System.Windows;
using System.Windows.Threading;
using HyprStart.App.Interop;
using HyprStart.App.Services;
using HyprStart.Core.Chat;
using HyprStart.Core.Input;
using HyprStart.Core.Library;
using HyprStart.Core.Settings;
using HyprStart.Core.Theming;

namespace HyprStart.App;

public partial class App : System.Windows.Application
{
    private readonly AppIndexService _indexService = new();
    private readonly PinsStore _pins = new();
    private readonly UsageStore _usage = new();
    private ThemeService _themes = null!;

    private SettingsStore _settings = null!;
    private MainWindow _window = null!;
    private OverlayController _overlay = null!;
    private KeyboardHookService? _hook;
    private TrayService? _tray;
    private StartMenuWatcher? _watcher;
    private SingleInstance? _instance;
    private int _exitCode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool diag = HasFlag(e.Args, "--diag");
        bool selfTest = HasFlag(e.Args, "--selftest");
        bool indexMode = HasFlag(e.Args, "--index");
        bool trayArg = HasFlag(e.Args, "--tray");
        bool headless = diag || selfTest || indexMode;

        if (headless) NativeMethods.AllocConsole();
        AppLog.Init(echo: headless);

        // Диагностика веб-поиска идёт в тот же лог: иначе сбои r.jina.ai видны только
        // как «web пусто» без причины.
        QuickChatClient.WebSearchLog = message => AppLog.Write("web: " + message);

        AppLog.Write($"start: args=[{string.Join(' ', e.Args)}]");

        if (indexMode)
        {
            RunIndexMode(HasFlag(e.Args, "--rebuild"));
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            AppLog.Write("start: второй экземпляр -> просим показать оверлей и выходим");
            _instance.Signal();
            _instance.Dispose();
            _instance = null;
            Shutdown(0);
            return;
        }

        _settings = SettingsStore.Load();

        // Переносимость: на чужом ПК в settings.json нет ни координат, ни города.
        // Определяем их по IP один раз и кэшируем в настройки (офлайн - просто пропускаем).
        if (!headless) LocationBootstrapper.EnsureLocation(_settings);

        // Стартовые пины строим из РЕАЛЬНО установленных программ: жёсткий список
        // ярлыков на чужой машине дал бы битые ссылки.
        if (!headless) Bootstrapper.SeedPins(_pins, _settings);
        _themes = new ThemeService(_settings.Current.Theme);
        if (!headless) ConfigureAutostartOnFirstRun();
        RefreshAutostartPath();

        _window = new MainWindow(_settings, _pins, _usage, _themes);
        _overlay = new OverlayController(_window, () => SampleAutoTheme());
        _window.DismissRequested += () => _overlay.Hide();
        _window.HotkeyPressed += () => _overlay.Toggle();
        _window.LaunchRequested += OnLaunchRequested;
        _window.IndexRefreshRequested += OnIndexRefreshRequested;
        _window.WallpaperChanged += SampleAutoTheme;
        _window.LaunchAsAdmin += OnLaunchAsAdmin;
        _instance.ShowRequested += () => Dispatcher.BeginInvoke(new Action(() => _overlay.Toggle()), DispatcherPriority.Input);

        // Основной и единственный путь: RegisterHotKey. Хук клавиатуры — легаси, только по флагу.
        if (_settings.Current.InterceptWindowsKey)
        {
            AppLog.Write("ВНИМАНИЕ: включён перехват Win (interceptWindowsKey=true). " +
                         "Этот режим оставлял клавиатуру в состоянии «Win зажат» — не используйте его до переработки.");
            var machine = new WinKeyStateMachine(NativeMethods.IsPointerDown);
            _hook = new KeyboardHookService(machine) { Verbose = diag };
            _hook.OverlayRequested += OnOverlayRequested;
            _hook.Start();
            AppLog.Write($"start: хук установлен={_hook.IsInstalled} ошибка={_hook.InstallFailed}");
        }
        else
        {
            AppLog.Write("start: режим хоткея, перехват Win выключен");
        }

        if (!headless || trayArg)
        {
            _tray = new TrayService(_overlay, _themes, _settings, ShutdownApp);
            _tray.Install();
        }

        _overlay.Prewarm();

        // Состояние хоткея логируем после Prewarm: окно уже создано, RegisterHotKey выполнен.
        if (_hook is null)
        {
            AppLog.Write(_window.HotkeyRegistered
                ? $"start: хоткей {_window.HotkeyName} активен"
                : $"start: хоткей {_window.HotkeyName} НЕ зарегистрирован — меню открывается только из трея");
        }

        if (!headless || diag) StartIndexBuild();

        // автообновление индекса: новый ярлык в меню «Программы» — без F5
        if (!headless) _watcher = new StartMenuWatcher(OnIndexRefreshRequested);

        if (selfTest)
        {
            _ = RunSelfTestAsync();
        }
    }

    /// <summary>Режим --index: собрать индекс, напечатать статистику, выйти. Для проверки без UI.</summary>
    private void RunIndexMode(bool rebuild)
    {
        try
        {
            var index = _indexService.Build(rebuild);
            Console.WriteLine();
            Console.WriteLine($"apps={index.Entries.Count} icons={index.IconCount} placeholders={index.PlaceholderCount}");
            Console.WriteLine($"cache: {_indexService.IndexFile}");
            Console.WriteLine($"icons: {_indexService.IconsDir}");

            foreach (var entry in index.Entries.Take(6))
            {
                Console.WriteLine($"  {entry.Kind,-8} {entry.Name,-40} -> {entry.Target}");
            }

            _exitCode = index.Entries.Count > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            AppLog.Error("index: режим --index упал", ex);
            _exitCode = 1;
        }
        finally
        {
            Shutdown(_exitCode);
        }
    }

    private void StartIndexBuild()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var index = _indexService.LoadOrBuild();
                var items = _indexService.CreateItems(index);
                Dispatcher.BeginInvoke(new Action(() => _window.SetApps(index, items)), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                AppLog.Error("index: сборка индекса упала", ex);
            }
        });
    }

    /// <summary>F5 в оверлее: пересобираем индекс в фоне, не блокируя UI.</summary>
    private void OnIndexRefreshRequested()
    {
        AppLog.Write("index: F5 — пересборка по запросу");

        _ = Task.Run(() =>
        {
            try
            {
                var index = _indexService.Build(forceIcons: false);
                var items = _indexService.CreateItems(index);
                Dispatcher.BeginInvoke(new Action(() => _window.SetApps(index, items)), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                AppLog.Error("index: пересборка по F5 упала", ex);
            }
        });
    }

    private void OnLaunchAsAdmin(AppEntry entry)
    {
        _overlay.Hide();

        if (!LaunchService.Launch(entry, asAdmin: true)) return;

        _usage.Register(entry);
        _window.RefreshFrequent();
    }

    private void OnLaunchRequested(AppEntry entry)
    {
        _overlay.Hide();

        if (LaunchService.Launch(entry))
        {
            _usage.Register(entry);
            _window.RefreshFrequent();
        }
    }

    private async Task RunSelfTestAsync()
    {
        try
        {
            _exitCode = await SelfTestRunner.RunAsync(_overlay, _window.HotkeyRegistered, _hook is not null);
        }
        catch (Exception ex)
        {
            AppLog.Error("selftest: исключение", ex);
            _exitCode = 1;
        }
        finally
        {
            ShutdownApp();
        }
    }

    /// <summary>
    /// Тема "auto": один снимок экрана 32×18 перед показом окна и пересборка палитры.
    /// Никакого таймера: обои сменились — цвет подхватится при следующем открытии.
    /// </summary>
    private void SampleAutoTheme()
    {
        if (!AutoTheme.IsAuto(_settings.Current.Theme)) return;

        var sample = ScreenSampler.Capture();
        if (sample is null)
        {
            // Захват недоступен (RDP/локскрин): держим фолбэк, чтобы не мигать палитрой
            return;
        }

        var extracted = WallpaperColorExtractor.Extract(sample, ScreenSampler.SampleWidth, ScreenSampler.SampleHeight);

        // Монохромный кадр — это НЕ повод для фоллбэка: у однотонных красных или жёлтых
        // обоев второго оттенка просто нет, а акцент из первого получается корректным.
        // Фоллбэк оставляем только на случай, когда цветов не нашлось вовсе (total <= 0).
        var palette = AutoTheme.FromPalette(extracted);

        _themes.ApplyAuto(palette);
        AppLog.Write($"theme: auto подхвачен (mono={extracted.IsMonochrome} primary={palette.BorderFrom} secondary={palette.BorderTo})");
    }

    /// <summary>Вызывается на потоке хука (легаси) — только быстрая передача в UI-поток.</summary>
    private void OnOverlayRequested()
        => Dispatcher.BeginInvoke(new Action(() => _overlay.Toggle()), DispatcherPriority.Input);

    /// <summary>Путь к exe мог измениться (сборка, другой TFM) — поправляем ключ автозапуска.</summary>
    private static void RefreshAutostartPath()
    {
        if (!AutostartService.IsEnabled()) return;
        AutostartService.Set(true);
    }

    private void ConfigureAutostartOnFirstRun()
    {
        if (_settings.Current.AutostartConfigured) return;

        AutostartService.Set(true);
        _settings.Current.AutostartConfigured = true;
        _settings.Save();
        AppLog.Write("start: автозапуск настроен при первом запуске");
    }

    private void ShutdownApp()
    {
        try
        {
            _tray?.Dispose();
            _tray = null;
            _watcher?.Dispose();
            _watcher = null;
            _hook?.Dispose();
            _hook = null;
            _instance?.Dispose();
            _instance = null;
            _window.Close();
        }
        catch (Exception ex)
        {
            AppLog.Error("shutdown: ошибка очистки", ex);
        }

        Shutdown(_exitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write($"exit: код={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
