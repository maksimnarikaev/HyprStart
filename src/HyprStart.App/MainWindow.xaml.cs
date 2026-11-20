using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HyprStart.App.Interop;
using HyprStart.App.Services;
using HyprStart.Core.Chat;
using HyprStart.Core.Input;
using HyprStart.Core.Library;
using HyprStart.Core.Layout;
using WinForms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace HyprStart.App;

/// <summary>Сводка по индексу: сколько приложений и папок, откуда взяты данные.</summary>
public sealed class AppRowVm
{
    private AppRowVm() { }

    public string Name { get; private init; } = string.Empty;
    public ImageSource? Icon { get; private init; }
    public bool IsFolder { get; private init; }
    public bool IsExpanded { get; private init; }
    public int Indent { get; private init; }
    public int Count { get; private init; }
    public string? FolderName { get; private init; }

    public Thickness IndentMargin => new(Indent * 14.0, 0, 0, 0);
    public string Chevron => IsFolder ? (IsExpanded ? "?" : "?") : string.Empty;
    public string CountText => IsFolder ? Count.ToString() : string.Empty;
    public string FolderHint => FolderName ?? string.Empty;
    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AppVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public static AppRowVm Folder(string name, int count, bool expanded, int indent) => new()
    {
        Name = name,
        IsFolder = true,
        Count = count,
        IsExpanded = expanded,
        Indent = indent,
    };

    public static AppRowVm App(AppEntry entry, int indent, string? folderName, ImageSource? icon) => new()
    {
        Name = entry.Name,
        IsFolder = false,
        Indent = indent,
        FolderName = folderName,
        Icon = icon,
        Entry = entry,
    };

    public AppEntry? Entry { get; private init; }

    public bool IsMath { get; private init; }
    public string MathValue { get; private init; } = string.Empty;
    public HyprStart.Core.Actions.ActionItem? Action { get; private init; }

    /// <summary>Строка действия: питание и прочие команды оболочки Windows.</summary>
    public static AppRowVm FromAction(HyprStart.Core.Actions.ActionItem item) => new()
    {
        Name = item.Name,
        Action = item,
        FolderName = item.Payload,
        IsFolder = false,
        Indent = 0,
    };

    /// <summary>Строка вычисления выражения в поиске.</summary>
    public static AppRowVm Math(string display, string value) => new()
    {
        Name = display,
        IsMath = true,
        MathValue = value,
        IsFolder = false,
        Indent = 0,
    };
}

/// <summary>Плитка закреплённого: иконка и бейдж-индекс для Alt+1..9.</summary>
public sealed class PinVm : System.ComponentModel.INotifyPropertyChanged
{
    private double _tileWidth = PinTileGeometry.MaxTileWidth;
    private double _tileHeight = PinTileGeometry.TileHeight(PinTileGeometry.MaxTileWidth);
    private ImageSource? _icon;

    public PinVm(HyprStart.Core.Storage.PinnedItem item, ImageSource? icon, IReadOnlyList<ImageSource?> miniIcons, int index)
    {
        Item = item;
        Icon = icon;
        MiniIcons = miniIcons;
        Index = index;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public HyprStart.Core.Storage.PinnedItem Item { get; }
    public IReadOnlyList<ImageSource?> MiniIcons { get; }
    public int Index { get; }

    /// <summary>
    /// Иконка плитки. Изменяемая: цели вне индекса приложений (например, .url-ярлык Steam,
    /// которого нет в кэше) дорисовывают иконку в фоне и обновляют плитку через привязку.
    /// </summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            Raise(nameof(Icon));
        }
    }

    /// <summary>Ширина плитки: пересчитывается из фактической ширины карточки (сетка 4x2).</summary>
    public double TileWidth
    {
        get => _tileWidth;
        set
        {
            if (Math.Abs(_tileWidth - value) < 0.5) return;
            _tileWidth = value;
            Raise(nameof(TileWidth));
        }
    }

    /// <summary>Высота плитки: производная от ширины, чтобы пропорции были стабильны.</summary>
    public double TileHeight
    {
        get => _tileHeight;
        set
        {
            if (Math.Abs(_tileHeight - value) < 0.5) return;
            _tileHeight = value;
            Raise(nameof(TileHeight));
        }
    }

    /// <summary>Подгоняет обе стороны плитки под доступную ширину контента.</summary>
    public void FitTo(double availableWidth)
    {
        double width = PinTileGeometry.TileWidth(availableWidth);
        TileWidth = width;
        TileHeight = PinTileGeometry.TileHeight(width);
    }

    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public string Name => Item.Name;
    public bool IsFolder => Item.IsFolder;
    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AppVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Виджет-индекс 1..9 в углу плитки; с 10-й позиции бейдж не рисуем.</summary>
    public string IndexBadge => Index >= 0 && Index < 9 ? (Index + 1).ToString() : string.Empty;

    public Visibility IndexVisibility => IndexBadge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Строка диалога мини-чата: реплика пользователя, ответ ИИ или служебная подсказка.</summary>
public sealed class ChatLineVm : System.ComponentModel.INotifyPropertyChanged
{
    private string _text;
    private bool _isNotice;

    public ChatLineVm(ChatRole role, string text, bool isNotice = false)
    {
        Role = role;
        _text = text ?? string.Empty;
        _isNotice = isNotice;
    }

    public ChatRole Role { get; }

    public bool IsNotice
    {
        get => _isNotice;
        set
        {
            if (_isNotice == value) return;
            _isNotice = value;
            foreach (var name in new[] { nameof(IsNotice), nameof(BubbleBrush), nameof(BubbleBorderBrush) })
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
            }
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public string RoleLabel => Role switch
    {
        ChatRole.User => "ВЫ",
        ChatRole.Assistant => "ИИ",
        _ => "·",
    };

    // Свои реплики — акцентным оттенком, ответы ИИ — нейтральной плиткой, подсказки — приглушённо.
    public MediaBrush BubbleBrush => IsNotice
        ? System.Windows.Media.Brushes.Transparent
        : Role == ChatRole.User
            ? new SolidColorBrush(MediaColor.FromArgb(0x2A, 0x5B, 0x8D, 0xEF))
            : new SolidColorBrush(MediaColor.FromArgb(0x33, 0x14, 0x20, 0x33));

    public MediaBrush BubbleBorderBrush => IsNotice
        ? System.Windows.Media.Brushes.Transparent
        : new SolidColorBrush(MediaColor.FromArgb(0x33, 0x7F, 0xA9, 0xF0));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Плитка частого приложения.</summary>
public sealed class FrequentVm
{
    public FrequentVm(AppEntry entry, ImageSource? icon, int count)
    {
        Entry = entry;
        Icon = icon;
        Count = count;
    }

    public AppEntry Entry { get; }
    public ImageSource? Icon { get; }
    public int Count { get; }
    public string Name => Entry.Name;
    public string CountText => "×" + Count;
}

public partial class MainWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly PinsStore _pins;
    private readonly UsageStore _usage;
    private readonly SystemMonitor _monitor = new();
    private readonly ThemeService _themes;
    private readonly HashSet<string> _expanded = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Иконки целей, которых нет в индексе приложений (закреплённый .url-ярлык Steam,
    /// exe вне меню «Программы» и т.п.). Заполняется тем же IconCache, что и индекс,
    /// но лениво и в фоне — чтобы не блокировать UI при открытии папки.
    /// </summary>
    private readonly Dictionary<string, ImageSource?> _extraIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _extraIconsPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _iconsDir = Path.Combine(AppPaths.Root, "cache", "icons");

    private List<AppEntry> _entries = new();
    private List<AppRowVm> _rows = new();
    private readonly ObservableCollection<AppRowVm> _rowsSource = new();
    private bool _suppressSearchFade;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _meterTimer;
    private bool _awake;
    private HotkeyService? _hotkey;
    private LayoutResult? _lastLayout;
    private IntPtr _hwnd = IntPtr.Zero;
    private double _dpiScale = 1.0;
    private bool _appsLoaded;
    private int _tick;
    private string? _pendingPower;
    private System.Windows.Point _pinDragStart;
    private int _pinDragIndex = -1;

    // --- мини-чат MINI AI ---
    private readonly ChatHistory _chatHistory = new();
    private readonly ObservableCollection<ChatLineVm> _chatLines = new();
    private CancellationTokenSource? _chatCts;
    private bool _chatBusy;
    private ChatLineVm? _chatStreamLine;

    public MainWindow(SettingsStore settings, PinsStore pins, UsageStore usage, ThemeService themes)
    {
        _settings = settings;
        _pins = pins;
        _usage = usage;
        _themes = themes;

        AllowsTransparency = true;
        InitializeComponent();

        PanelApps.Header = TextFx.Spaced("ПРИЛОЖЕНИЯ");
        PanelPinned.Header = TextFx.Spaced("ЗАКРЕПЛЕНО");
        PanelFrequent.Header = TextFx.Spaced("ЧАСТОЕ");
        PanelQuick.Header = TextFx.Spaced("БЫСТРЫЙ ДОСТУП");
        PanelChat.Header = TextFx.Spaced("MINI AI");

        InitChat();

        UpdatePlaceholder();

        // Список живёт в своём источнике: строки дерева заменяются множественно,
        // чтобы не дёргать уведомления на каждый элемент по отдельности.
        AppsList.ItemsSource = _rowsSource;

        AppsList.PreviewMouseLeftButtonUp += OnAppsListMouseUp;
        AppsList.PreviewMouseRightButtonDown += OnAppsListRightButton;
        AppsList.PreviewMouseLeftButtonDown += OnAppsListMouseDown;
        AppsList.MouseMove += OnAppsListMouseMove;
        FrequentList.PreviewMouseLeftButtonUp += OnFrequentClick;
        FrequentList.PreviewMouseRightButtonDown += OnFrequentRightButton;

        // Фокус панели: подсветка следует за мышью при движении внутри окна.
        // Пока мышь стоит, MouseEnter не сработает повторно, поэтому добавляем и MouseMove.
        foreach (var panel in Panels)
        {
            var captured = panel;
            panel.MouseEnter += (_, _) => SetPanelFocus(captured);
        }

        SetPanelFocus(PanelApps, animate: false);

        // Слушаем смену темы, чтобы перекрасить локальные кисти панелей
        _themes.Applied += OnThemeApplied;

        // Пилюля-переключатель темы в polybar
        ThemePillText.Text = _themes.CurrentName;        ThemePill.MouseLeftButtonUp += (_, _) =>
        {
            var next = _themes.CycleNext();
            if (next is null) return;

            ThemePillText.Text = next;
            _settings.Current.Theme = next;
            _settings.Save();
            RefreshFrequent();
            AppLog.Write($"ui: тема переключена на {next}");
        };

        // --- прокрутка/клик по polybar: громкость колёсиком (±2%) и кнопкой (Mute) ---
        PillVolHost.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            AudioControl.Step(e.Delta > 0 ? 1 : -1);
            Dispatcher.BeginInvoke(new Action(RefreshVolumeText));
        };

        PillVolHost.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;

            AudioControl.ToggleMute();
            e.Handled = true;
            Dispatcher.BeginInvoke(new Action(RefreshVolumeText));
        };

        PillCpuHost.MouseLeftButtonUp += (_, _) => RunShell("taskmgr.exe");
        PillRamHost.MouseLeftButtonUp += (_, _) => RunShell("taskmgr.exe");
        PinsList.PreviewMouseLeftButtonDown += OnPinsMouseDown;
        PinsList.MouseMove += OnPinsMouseMove;
        PinsList.Drop += OnPinsDrop;
        PinsList.AllowDrop = true;

        _tickTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), Dispatcher);
        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Render, (_, _) => UpdateMeter(), Dispatcher);

        BuildMeterBars();

        // Медиа: подписка на события GSMTC вместо опроса каждые 5 секунд.
        // Так тик не тратит CPU; если сессия появится или сменится, придёт событие сам.
        _monitor.TrackChanged += OnTrackChanged;
        _ = _monitor.HookTrackEventsAsync();

        // Таймеры телеметрии и эквалайзера в скрытом виде не крутятся:
        // они запускаются только из OverlayController (Show -> ResumeActivity, Hide -> SuspendActivity).
        _tickTimer.Stop();
        _meterTimer.Stop();
    }

    public event Action? DismissRequested;
    public event Action? HotkeyPressed;
    public event Action? IndexRefreshRequested;

    /// <summary>Сменились обои рабочего стола — повод пересчитать палитру темы «auto».</summary>
    public event Action? WallpaperChanged;
    public event Action<AppEntry>? LaunchRequested;
    public event Action<AppEntry>? LaunchAsAdmin;

    public bool HotkeyRegistered => _hotkey?.IsRegistered == true;
    public string HotkeyName => _hotkey?.Spec.Describe() ?? "—";

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is not HwndSource source) return;
        _hwnd = source.Handle;

        // Намеренно убираем системное меню окна: снимаем WS_SYSMENU
        // и глотаем SC_KEYMENU (Alt+Space), чтобы Windows не открывала свой заголовок.
        try
        {
            var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
            style &= ~WsSysMenu;
            SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr(style));
        }
        catch (Exception ex)
        {
            AppLog.Error("ui: не удалось снять WS_SYSMENU", ex);
        }

        source.AddHook(WndProc);

        if (source.CompositionTarget is not null)
        {
            var matrix = source.CompositionTarget.TransformToDevice;
            _dpiScale = matrix.M11 > 0 ? matrix.M11 : 1.0;
        }

        _hotkey = new HotkeyService(source, () => HotkeyPressed?.Invoke());
        var spec = HotkeySpec.TryParse(_settings.Current.Hotkey, out var parsed) ? parsed : HotkeySpec.AltSpace;
        _hotkey.Apply(spec);

        ApplyGeometry();
    }

    /// <summary>Сбрасывает визуальное состояние перед показом: прозрачность 0 и масштаб 0.97.</summary>
    public void ResetOpenVisuals()
    {
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 0;

        Root.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        _scale ??= new ScaleTransform(0.97, 0.97);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _scale.ScaleX = 0.97;
        _scale.ScaleY = 0.97;
        Root.RenderTransform = _scale;

        // Сбрасываем прозрачность панелей к норме, иначе анимация выглядит ступенчато
        foreach (var panel in Panels)
        {
            panel.BeginAnimation(OpacityProperty, null);
            panel.Opacity = 1;
        }
    }

    /// <summary>Подготовка к новому показу: очистка, фокус и сброс анимаций.</summary>
    public void PrepareForShow()
    {
        ResetOpenVisuals();

        // На время обновления списка гасим фейд у строк, чтобы не мигать на пустом запросе
        _suppressSearchFade = true;

        try
        {
            bool hadQuery = SearchBox.Text.Length > 0;
            if (hadQuery)
            {
                SearchBox.Clear();
            }
            else if (AppsList.ItemsSource is null && _appsLoaded)
            {
                RebuildTree();
            }

            UpdatePlaceholder();
        }
        finally
        {
            _suppressSearchFade = false;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }));
    }

    /// <summary>Проигрывает лёгкую анимацию появления: fade окна + сдвиг панелей.</summary>
    public void PlayOpenAnimation()
    {
        Root.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        _scale ??= new ScaleTransform(0.97, 0.97);
        Root.RenderTransform = _scale;

        var duration = TimeSpan.FromMilliseconds(140);
        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        // Анимируем прозрачность и масштаб: стартуем с 0 и 0.97, финал задаётся в конце
        Root.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration)
        {
            EasingFunction = ease,
        });

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });

        var panels = new FrameworkElement[] { PanelApps, PanelPinned, PanelFrequent, PanelQuick, Polybar };

        for (int i = 0; i < panels.Length; i++)
        {
            var panel = panels[i];
            var shift = new TranslateTransform(0, 12);
            panel.RenderTransform = shift;

            var slide = new System.Windows.Media.Animation.DoubleAnimation(12, 0, duration)
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 14),
                EasingFunction = ease,
            };

            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 45);
            shift.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        // Страховка: если окно так и не стало видимым (сбой анимации), добиваем вручную
        var watchdog = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(260),
        };

        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();

            if (Root.Opacity < 0.9)
            {
                Root.BeginAnimation(OpacityProperty, null);
                Root.Opacity = 1;
                AppLog.Write($"overlay: анимация не завершилась (opacity={Root.Opacity:0.##}) — принудительно показали");
            }
            else
            {
                AppLog.Write($"overlay: показано, opacity={Root.Opacity:0.##}");
            }
        };

        watchdog.Start();
    }

    private MediaBrush TintBrush() => (MediaBrush)FindResource("GlassTintBrush");

    public void SetApps(AppIndex index, IReadOnlyList<AppItemVm> items)
    {
        _entries = index.Entries;
        _icons.Clear();
        foreach (var item in items)
        {
            _icons[item.Entry.Target] = item.Icon;
        }

        _appsLoaded = true;
        PanelApps.Header = TextFx.Spaced("ПРИЛОЖЕНИЯ") + "   " + index.Entries.Count;

        RebuildTree();
        RefreshPins();
        RefreshFrequent();
        AppLog.Write($"ui: загружено в UI {items.Count} приложений, папок {AppTreeBuilder.Build(_entries).Count(n => n.IsFolder)}");

        // Диагностический сценарий: --animtest раскрывает первую папку дерева
        if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--animtest", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ProbeFolderAnimation));
        }
    }

    /// <summary>Диагностика: раскрывает первую папку, чтобы в логе видеть тайминги анимации.</summary>
    public void ProbeFolderAnimation()
    {
        var folder = _rows.FirstOrDefault(row => row.IsFolder);
        if (folder is null)
        {
            AppLog.Write("animtest: папок в дереве не найдено");
            return;
        }

        AppLog.Write($"animtest: раскрываем папку '{folder.Name}'");
        ToggleFolder(folder.Name);
    }

    // ---------- поиск / дерево ----------

    private void RebuildTree()
    {
        // Важно: перед подменой ItemsSource запоминаем текущую прокрутку,
        // затем возвращаем её обратно, иначе список прыгает в самое начало.
        var scroll = FindDescendant<ScrollViewer>(AppsList);
        double offset = scroll?.VerticalOffset ?? 0;

        var query = SearchBox.Text.Trim();
        var rows = query.Length > 0 ? BuildSearchRows(query) : BuildTreeRows();

        // Команды ">" и строки настроек; ранее результатов заменяем их списком команд.
        if (query.StartsWith(HyprStart.Core.Actions.ActionCatalog.Prefix, StringComparison.Ordinal))
        {
            var commandText = query[HyprStart.Core.Actions.ActionCatalog.Prefix.Length..].Trim();
            rows = HyprStart.Core.Actions.ActionCatalog.MatchCommands(commandText)
                .Select(AppRowVm.FromAction)
                .ToList();
        }
        else if (query.Length >= 2)
        {
            var settings = HyprStart.Core.Actions.ActionCatalog.MatchSettings(query);
            for (int i = settings.Count - 1; i >= 0; i--)
            {
                rows.Insert(0, AppRowVm.FromAction(settings[i]));
            }
        }

        // Калькулятор — в начало: посчитанное значение, Enter копирует его в буфер.
        if (query.Length > 0 && MathEvaluator.TryEvaluate(query, out var mathResult))
        {
            rows.Insert(0, AppRowVm.Math("= " + mathResult, mathResult));
        }

        _rowsSource.Clear();
        foreach (var row in rows) _rowsSource.Add(row);
        _rows = rows;

        if (query.Length > 0 && rows.Count > 0)
        {
            AppsList.SelectedIndex = 0;
        }
        else if (rows.Count == 0)
        {
            AppsList.SelectedIndex = -1;
        }

        if (scroll is null) return;

        AppsList.UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => scroll.ScrollToVerticalOffset(offset)));
    }

    private List<AppRowVm> BuildTreeRows()
    {
        var rows = new List<AppRowVm>();

        foreach (var node in AppTreeBuilder.Build(_entries))
        {
            if (node.IsFolder)
            {
                bool expanded = _expanded.Contains(node.Name);
                rows.Add(AppRowVm.Folder(node.Name, node.Count, expanded, 0));

                if (!expanded) continue;

                foreach (var child in node.Children)
                {
                    rows.Add(AppRowVm.App(child.Entry!, 1, null, IconFor(child.Entry!)));
                }
            }
            else
            {
                rows.Add(AppRowVm.App(node.Entry!, 0, null, IconFor(node.Entry!)));
            }
        }

        return rows;
    }

    private List<AppRowVm> BuildSearchRows(string query)
    {
        var rows = new List<AppRowVm>();

        // frecency: берём в расчёт статистику запусков, чтобы частое поднималось выше
        foreach (var entry in AppSearch.Filter(_entries, query, _usage.Snapshot(), DateTime.Now))
        {
            rows.Add(AppRowVm.App(entry, 0, entry.Group, IconFor(entry)));
        }

        return rows;
    }

    private ImageSource? IconFor(AppEntry entry)
        => _icons.TryGetValue(entry.Target, out var icon) ? icon : null;

    /// <summary>Тип записи закреплённого элемента: 0 — ярлык, 1 — UWP (см. PinnedItem.Kind).</summary>
    private static AppEntryKind KindOf(HyprStart.Core.Storage.PinnedItem item)
        => item.Kind == 1 ? AppEntryKind.Uwp : AppEntryKind.Shortcut;

    /// <summary>
    /// Иконка для цели закреплённого элемента: сначала из индекса приложений, затем из
    /// добора по кэшу иконок. Если иконки ещё нет, запускает фоновое извлечение
    /// (тот же <see cref="IconCache.EnsureIcon"/>, что и для корневых плиток: понимает
    /// .lnk, .url с IconLocation и UWP), а плитка обновится через привязку.
    /// </summary>
    /// <param name="target">Цель элемента: путь к .lnk/.url/.exe или AUMID.</param>
    /// <param name="name">Имя для глифа-заглушки, если иконку извлечь не удалось.</param>
    /// <param name="kind">Тип записи: ярлык или UWP.</param>
    private ImageSource? IconForTarget(string target, string name, AppEntryKind kind = AppEntryKind.Shortcut)
    {
        if (string.IsNullOrEmpty(target)) return null;

        if (_icons.TryGetValue(target, out var known)) return known;
        if (_extraIcons.TryGetValue(target, out var extra)) return extra;
        if (!_extraIconsPending.Add(target)) return null; // уже извлекается в фоне

        _ = Task.Run(() =>
        {
            ImageSource? image = null;
            try
            {
                var entry = new AppEntry { Name = name, Target = target, Kind = kind };
                string file = IconCache.EnsureIcon(entry, _iconsDir, refresh: false);
                image = LoadIconFile(file);
            }
            catch (Exception ex)
            {
                AppLog.Error($"icon: добор иконки не удался ({target})", ex);
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _extraIconsPending.Remove(target);
                if (image is null)
                {
                    AppLog.Write($"icon: добор не дал картинки для '{name}' ({Path.GetFileName(target)})");
                    return;
                }

                _extraIcons[target] = image;
                AppLog.Write($"icon: добран из кэша для '{name}' ({Path.GetFileName(target)})");

                // Обновляем только плитки с этой целью — без полной перестройки списков
                foreach (var vm in PinsList.ItemsSource?.Cast<PinVm>() ?? Enumerable.Empty<PinVm>())
                {
                    if (string.Equals(vm.Item.Target, target, StringComparison.OrdinalIgnoreCase))
                        vm.Icon = image;
                }
                foreach (var vm in FolderList.ItemsSource?.Cast<PinVm>() ?? Enumerable.Empty<PinVm>())
                {
                    if (string.Equals(vm.Item.Target, target, StringComparison.OrdinalIgnoreCase))
                        vm.Icon = image;
                }
            }), DispatcherPriority.Background);
        });

        return null;
    }

    /// <summary>Читает PNG из кэша иконок в замороженный ImageSource (безопасно для UI-потока).</summary>
    private ImageSource? LoadIconFile(string? iconFile)
    {
        if (string.IsNullOrEmpty(iconFile)) return null;

        string path = Path.Combine(_iconsDir, iconFile);
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 96;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image as ImageSource;
        }
        catch (Exception ex)
        {
            AppLog.Error($"icon: не читается из кэша ({iconFile})", ex);
            return null;
        }
    }

    private void ToggleFolder(string name)
    {
        // В поиске папки не раскрываем
        if (SearchBox.Text.Trim().Length > 0) return;

        int index = -1;
        for (int i = 0; i < _rowsSource.Count; i++)
        {
            if (_rowsSource[i].IsFolder && string.Equals(_rowsSource[i].Name, name, StringComparison.CurrentCultureIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0) return;

        var children = _entries
            .Where(entry => !string.IsNullOrEmpty(entry.Group)
                            && string.Equals(AppIndexBuilder.Normalize(entry.Group), AppIndexBuilder.Normalize(name), StringComparison.Ordinal))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (_expanded.Contains(name))
        {
            _expanded.Remove(name);
            _rowsSource[index] = AppRowVm.Folder(name, children.Count, expanded: false, indent: 0);

            // Удаляем строки потомков: подряд все строки с отступом больше нуля
            int removeFrom = index + 1;
            while (removeFrom < _rowsSource.Count && _rowsSource[removeFrom].Indent > 0)
            {
                _rowsSource.RemoveAt(removeFrom);
            }

            AppLog.Write($"ui: папка '{name}' скрыта");
        }
        else
        {
            _expanded.Add(name);
            _rowsSource[index] = AppRowVm.Folder(name, children.Count, expanded: true, indent: 0);

            int insertAt = index + 1;
            foreach (var entry in children)
            {
                _rowsSource.Insert(insertAt++, AppRowVm.App(entry, 1, null, IconFor(entry)));
            }

            AnimateFolderRowsAt(index + 1, children.Count);

            AppLog.Write($"ui: папка '{name}' раскрыта, детей {children.Count}");
        }

        _rows = _rowsSource.ToList();
    }

    /// <summary>
    /// Раскрытие папки анимируем по строкам: сдвиг + прозрачность,
    /// без таймеров и без пересчёта всего дерева — только новые строки.
    /// </summary>
    private void AnimateFolderRowsAt(int startIndex, int count)
    {
        if (count <= 0) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var duration = TimeSpan.FromMilliseconds(170);
            var ease = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            };

            int animated = 0;

            for (int i = 0; i < count; i++)
            {
                if (AppsList.ItemContainerGenerator.ContainerFromIndex(startIndex + i) is not ListBoxItem item) continue;

                var shift = new TranslateTransform(0, 10);
                item.RenderTransform = shift;
                item.Opacity = 1;

                var delay = TimeSpan.FromMilliseconds(i * 16);

                var opacity = new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration)
                {
                    BeginTime = delay,
                    EasingFunction = ease,
                };

                var slide = new System.Windows.Media.Animation.DoubleAnimation(10, 0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = ease,
                };

                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(opacity, 45);
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 45);

                item.BeginAnimation(OpacityProperty, opacity);
                shift.BeginAnimation(TranslateTransform.YProperty, slide);
                animated++;
            }

            if (animated == 0)
            {
                AppsList.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(120)));
            }
        }));
    }

    /// <summary>
    /// Анимация: ищем контейнеры строк раскрытой папки (все строки с отступом > 0)
    /// и прокручиваем их на месте, если строка отрисована. Прокрутка создаёт иллюзию,
    /// будто содержимое проявилось последовательно, а не возникло разом.
    /// </summary>
    private void AnimateFolderRows(string folderName)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            int folderIndex = _rows.FindIndex(row => row.IsFolder && row.Name == folderName);
            if (folderIndex < 0) return;

            var containers = new List<ListBoxItem>();

            for (int i = folderIndex + 1; i < _rows.Count && _rows[i].Indent > 0; i++)
            {
                if (AppsList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item) containers.Add(item);
            }

            if (containers.Count == 0)
            {
                // Контейнеры не сгенерированы — значит окно ещё не успело их создать
                AppsList.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(140)));
                AppLog.Write($"ui: папка '{folderName}' открылась без анимации строк");
                return;
            }

            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            };

            for (int i = 0; i < containers.Count; i++)
            {
                var element = containers[i];
                var shift = new TranslateTransform(0, 10);
                element.RenderTransform = shift;
                element.Opacity = 1;

                var delay = TimeSpan.FromMilliseconds(i * 18);

                // Всё делаем легко: только сдвиг RenderTransform и Opacity,
                // чтобы список не дёргать на каждом кадре и не тратить CPU впустую.
                var opacity = new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration)
                {
                    BeginTime = delay,
                    EasingFunction = ease,
                };

                var slide = new System.Windows.Media.Animation.DoubleAnimation(10, 0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = ease,
                };

                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(opacity, 45);
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 45);

                element.BeginAnimation(OpacityProperty, opacity);
                shift.BeginAnimation(TranslateTransform.YProperty, slide);
            }

            AppLog.Write($"ui: раскрыта папка '{folderName}', анимировано строк {containers.Count}");
        }));
    }

    // ---------- частое: клики и меню ----------

    private void OnFrequentClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var container = FindAncestor<ContentPresenter>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not FrequentVm vm) return;

        Launch(vm.Entry);
        e.Handled = true;
    }

    private void OnFrequentRightButton(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestor<ContentPresenter>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not FrequentVm vm) return;

        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenuStyle") };
        menu.Items.Add(MenuAction("Запустить", () => Launch(vm.Entry)));
        menu.Items.Add(MenuAction("Запустить от имени администратора", () => LaunchAdmin(vm.Entry)));
        menu.Items.Add(MenuAction(_pins.Contains(vm.Entry) ? "Закрепить" : "Открепить", () =>
        {
            _pins.Toggle(vm.Entry);
            RefreshPins();
        }));

        menu.Items.Add(MenuAction("Убрать из частого", () =>
        {
            _usage.Remove(vm.Entry.Target);
            RefreshFrequent();
        }));

        menu.PlacementTarget = e.OriginalSource as UIElement;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---------- закреплённое / папки ----------

    private HyprStart.Core.Storage.PinnedItem? _openFolder;
    private HyprStart.Core.Storage.PinnedItem? _renameTarget;
    private bool _pinsWired;

    private void RefreshPins()
    {
        WirePinsOnce();

        var source = _openFolder is null
            ? _pins.Items
            : (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_openFolder.Children;

        var items = new List<PinVm>(source.Count);

        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var mini = new List<ImageSource?>();

            if (item.IsFolder)
            {
                foreach (var child in item.Children.Take(4))
                {
                    mini.Add(IconForTarget(child.Target, child.Name, KindOf(child)));
                }
            }

            items.Add(new PinVm(item, IconForTarget(item.Target, item.Name, KindOf(item)), mini, i));
        }

        PinsList.ItemsSource = items;
        FolderList.ItemsSource = items;

        PinsRoot.Visibility = _openFolder is null ? Visibility.Visible : Visibility.Collapsed;
        FolderRoot.Visibility = _openFolder is null ? Visibility.Collapsed : Visibility.Visible;
        PinsHint.Visibility = _openFolder is null && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderTitle.Text = _openFolder?.Name ?? string.Empty;

        PanelPinned.Header = TextFx.Spaced("ЗАКРЕПЛЕНО") + (_pins.Items.Count > 0 ? "   " + _pins.Items.Count : string.Empty);

        // Свежие плитки создаются с размером по умолчанию — сразу подгоняем под карточку.
        if (_lastLayout is { } layout) FitPinTiles(layout.Pinned);
    }

    private void WirePinsOnce()
    {
        if (_pinsWired) return;
        _pinsWired = true;

        PanelPinned.Tag = CreateFolderButton();

        PinsList.PreviewMouseLeftButtonUp += OnPinsClick;
        FolderList.PreviewMouseLeftButtonUp += OnPinsClick;
        PinsList.PreviewMouseRightButtonDown += OnPinsRightButton;
        FolderList.PreviewMouseRightButtonDown += OnPinsRightButton;

        FolderList.AllowDrop = true;
        FolderList.PreviewMouseLeftButtonDown += OnPinsMouseDown;
        FolderList.MouseMove += OnPinsMouseMove;
        FolderList.Drop += OnPinsDrop;

        // Файлы из проводника добавляем в закреплённое, а не в открытую папку
        PanelPinned.AllowDrop = true;
        PanelPinned.DragOver += OnPinnedDragOver;
        PanelPinned.Drop += OnPinnedFilesDrop;
    }

    private static void OnPinnedDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPinnedFilesDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return;

        bool added = false;

        foreach (var path in paths)
        {
            if (System.IO.Directory.Exists(path) && _pins.AddPath(path)) added = true;
        }

        if (added) RefreshPins();
        e.Handled = true;
    }

    private System.Windows.Controls.Button CreateFolderButton()
    {
        var button = new System.Windows.Controls.Button
        {
            Content = "+ папка",
            Style = (Style)FindResource("QuickButtonStyle"),
            Margin = new Thickness(0),
            Padding = new Thickness(8, 1, 8, 1),
            FontSize = 10.5,
        };

        button.Click += OnAddFolder;
        return button;
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var folder = _pins.AddFolder();
        RefreshPins();
        BeginRename(folder);
    }

    private void BeginRename(HyprStart.Core.Storage.PinnedItem item)
    {
        _renameTarget = item;
        RenameBox.Text = item.Name;
        RenameBox.Visibility = Visibility.Visible;

        // Ставим фокус после отрисовки, чтобы TextBox был уже в дереве визуалов.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            RenameBox.Focus();
            Keyboard.Focus(RenameBox);
            RenameBox.SelectAll();
            AppLog.Write($"pins: переименование '{item.Name}'");
        }));
    }

    private void EndRename(bool apply)
    {
        var target = _renameTarget;
        _renameTarget = null;
        RenameBox.Visibility = Visibility.Collapsed;

        if (apply && target is not null)
        {
            _pins.Rename(target, RenameBox.Text);
            RefreshPins();
        }
    }

    private void OnRenameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            EndRename(true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndRename(false);
        }
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e) => EndRename(true);

    private void OnFolderBack(object sender, RoutedEventArgs e)
    {
        _openFolder = null;
        RefreshPins();
        AnimateView(PinsRoot);
    }

    /// <summary>Открыть вид папки пинов: содержимое её списка плиток.</summary>
    private void ShowFolderView(HyprStart.Core.Storage.PinnedItem folder)
    {
        _openFolder = folder;
        RefreshPins();
        AnimateView(FolderRoot);
    }

    private static void AnimateView(FrameworkElement view)
    {
        var duration = TimeSpan.FromMilliseconds(170);
        var ease = new System.Windows.Media.Animation.QuadraticEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        var shift = new TranslateTransform(0, 14);
        view.RenderTransform = shift;
        view.Opacity = 1;

        var opacity = new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(14, 0, duration) { EasingFunction = ease };

        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(opacity, 45);
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 45);

        view.BeginAnimation(OpacityProperty, opacity);
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>Бросок пина на плитку: в корень закреплённого, а если бросили прямо на папку-плитку,
    /// то внутрь этой папки. Так проще перетаскивать между группами.</summary>
    private void DropIntoPinned(AppEntry entry)
    {
        var hit = PanelPinned.InputHitTest(Mouse.GetPosition(PanelPinned)) as DependencyObject;
        if (hit is null)
        {
            AppLog.Write($"drag: '{entry.Name}' брошен вне области закреплённого");
            return;
        }

        var list = _openFolder is null
            ? _pins.Items
            : (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_openFolder.Children;

        int index = IndexOfPin(hit);
        var folder = index >= 0 && index < list.Count && list[index].IsFolder ? list[index] : _openFolder;

        if (_pins.Add(entry, folder)) RefreshPins();
    }

    /// <summary>
    /// Ищем презентацию внутри плитки: поднимаемся по дереву до плитки
    /// ContentPresenter, где лежит наш шаблон (PinVm). Если не поднялись — это был content-лист
    /// другого типа, и плитки в нём не подходят нам по типу.
    /// </summary>
    private static PinVm? PinFromSource(DependencyObject? source)
    {
        var current = source;

        while (current is not null)
        {
            if (current is ContentPresenter presenter && presenter.DataContext is PinVm pin)
            {
                return pin;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnPinsClick(object sender, MouseButtonEventArgs e)
    {
        // Клик уже обработан мышью при отпускании — перерисовку не запускаем
        if (_pinDragging)
        {
            _pinDragging = false;
            _pinDragIndex = -1;
            return;
        }

        var pin = PinFromSource(e.OriginalSource as DependencyObject);
        if (pin is null) return;

        if (pin.Item.IsFolder)
        {
            ShowFolderView(pin.Item);
        }
        else
        {
            LaunchPinned(pin.Item);
        }

        e.Handled = true;
    }

    private void LaunchPinned(HyprStart.Core.Storage.PinnedItem item)
    {
        var entry = _entries.FirstOrDefault(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase))
                    ?? new AppEntry { Name = item.Name, Target = item.Target, Kind = (AppEntryKind)item.Kind };

        Launch(entry);
    }

    /// <summary>
    /// Alt+1..9 (или Ctrl+1..9) запускают закреплённое по номеру плитки.
    /// При зажатом Alt WPF присылает Key.System, а цифра лежит в SystemKey —
    /// поэтому читаем её оттуда, иначе сочетание молча не срабатывает.
    /// Если открыта папка пинов, номера адресуют ЕЁ элементы, а не корневой список.
    /// </summary>
    private bool TryActivatePinnedByDigit(System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) == 0) return false;
        if (SearchBox.Text.Length > 0) return false;

        // Фокус в мини-чате: цифры принадлежат тексту сообщения, а не плиткам.
        // Без этой защиты Alt+<цифра> внутри чата запускал приложение и гасил оверлей.
        if (ReferenceEquals(Keyboard.FocusedElement, ChatInput)) return false;

        // Alt+цифра: e.Key == Key.System, а настоящая клавиша — в e.SystemKey.
        // Без этого сочетание доходило до нас как Key.System и молча игнорировалось.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        int digit = PinDigitFromKey(key);
        if (digit <= 0) return false;

        // Список, который сейчас видит пользователь: корень или содержимое открытой папки.
        // Без этого Alt+1 внутри папки запускал первую плитку корня, а не папки.
        var list = _openFolder is null
            ? (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_pins.Items
            : _openFolder.Children;

        // Гасим событие до проверки границ: иначе Windows проиграет системный
        // звук меню на Alt+digit даже тогда, когда такой плитки нет.
        e.Handled = true;

        int index = PinDigit.ResolveIndex(digit, _pins.Items.Count, list.Count);
        if (index < 0) return true;

        var item = list[index];
        AppLog.Write($"ui: Alt+{digit} -> '{item.Name}' ({( _openFolder is null ? "root" : "folder:" + _openFolder.Name )}, focused={Keyboard.FocusedElement?.GetType().Name})");

        if (item.IsFolder)
        {
            ShowFolderView(item);
            return true;
        }

        LaunchPinned(item);
        DismissRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// Номер плитки по клавише цифры: D1..D9 и NumPad1..NumPad9; 0 — не цифра.
    /// Логика живёт в <see cref="HyprStart.Core.Input.PinDigit"/> и покрыта тестами,
    /// чтобы UI-копия не разъехалась с проверенным правилом.
    /// </summary>
    private static int PinDigitFromKey(Key key)
        => HyprStart.Core.Input.PinDigit.FromWpfKey((int)key, (int)Key.D0, (int)Key.NumPad0);

    private void OnPinsRightButton(object sender, MouseButtonEventArgs e)
    {
        var pin = PinFromSource(e.OriginalSource as DependencyObject);
        if (pin is null) return;

        var menu = new ContextMenu
        {
            Background = (MediaBrush)FindResource("GlassTintBrush"),
            Foreground = (MediaBrush)FindResource("TextBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = 12,
        };

        if (pin.Item.IsFolder)
        {
            menu.Items.Add(MenuAction("Переименовать", () => BeginRename(pin.Item)));
            menu.Items.Add(MenuAction("Удалить папку", () =>
            {
                _pins.Remove(pin.Item);
                if (ReferenceEquals(_openFolder, pin.Item)) _openFolder = null;
                RefreshPins();
            }));
        }
        else if (System.IO.Directory.Exists(pin.Item.Target))
        {
            menu.Items.Add(MenuAction("Открыть", () => LaunchPinned(pin.Item)));
            menu.Items.Add(MenuAction("Открыть в терминале", () => OpenInTerminal(pin.Item.Target)));
            menu.Items.Add(MenuAction("Убрать", () => { _pins.Remove(pin.Item); RefreshPins(); }));
        }
        else
        {
            menu.Items.Add(MenuAction("Запустить", () => LaunchPinned(pin.Item)));
            menu.Items.Add(MenuAction("Запустить от имени администратора", () => LaunchPinnedAsAdmin(pin.Item)));

            var intoFolder = new MenuItem
            {
                Header = "→ папка",
                Style = MenuItemStyle,
            };

            foreach (var folder in _pins.Items.Where(i => i.IsFolder && !ReferenceEquals(i, _openFolder)).ToList())
            {
                var target = folder;
                intoFolder.Items.Add(MenuAction(folder.Name, () =>
                {
                    _pins.MoveInto(target, pin.Item);
                    RefreshPins();
                }));
            }

            if (intoFolder.Items.Count == 0)
            {
                intoFolder.Items.Add(new MenuItem
                {
                    Header = "нет папок",
                    IsEnabled = false,
                    Style = MenuItemStyle,
                });
            }

            menu.Items.Add(intoFolder);
            menu.Items.Add(MenuAction("Убрать", () =>
            {
                _pins.Remove(pin.Item);
                RefreshPins();
            }));
        }

        if (FindAncestor<Border>(e.OriginalSource as DependencyObject) is { } anchor) anchor.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Запуск закреплённого от админа (также по Ctrl + Shift+Enter).</summary>
    private void LaunchAdmin(AppEntry entry)
    {
        AppLog.Write($"ui: '{entry.Name}' запрошен от администратора");
        LaunchAsAdmin?.Invoke(entry);
    }

    private void LaunchPinnedAsAdmin(HyprStart.Core.Storage.PinnedItem item)
    {
        if (System.IO.Directory.Exists(item.Target)) return; // папка уже открыта в проводнике

        var entry = _entries.FirstOrDefault(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase))
                    ?? new AppEntry { Name = item.Name, Target = item.Target, Kind = (AppEntryKind)item.Kind };

        LaunchAdmin(entry);
    }

    /// <summary>Обновляет тексты пилюль из Theme.xaml (ресурсы — на случай смены темы).</summary>
    private static Style? ContextMenuStyle => System.Windows.Application.Current?.TryFindResource(typeof(ContextMenu)) as Style;

    private static Style? MenuItemStyle => System.Windows.Application.Current?.TryFindResource(typeof(MenuItem)) as Style;

    private void RefreshVolumeText()
    {
        _monitor.SampleVolume();
        PillVol.Text = _monitor.VolumePercent >= 0 ? $"VOL {_monitor.VolumePercent}%" : "VOL —";
    }

    /// <summary>Путь к папке для администратора нужен: в трее путь был бы не виден.</summary>
    private static string? ResolvePath(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path) && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return null;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return path;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return path;

            var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            return shortcut?.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string ?? path;
        }
        catch
        {
            return path;
        }
    }

    private void ShowInExplorer(AppEntry entry)
    {
        var path = ResolvePath(entry.Target);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            AppLog.Write($"ui: запрашиваем админа '{path}'");
        }
        catch (Exception ex)
        {
            AppLog.Error($"ui: не удалось запросить администратора '{path}'", ex);
        }
    }

    /// <summary>Открыть в Windows Terminal, а не отдельным cmd/powershell.</summary>
    private void OpenInTerminal(string path)
    {
        if (!System.IO.Directory.Exists(path)) return;

        try
        {
            var startInfo = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            AppLog.Write($"ui: терминал в '{path}'");
            return;
        }
        catch
        {
            // Windows Terminal не установлен — откатываемся на cmd
        }

        try
        {
            var fallback = new ProcessStartInfo("cmd.exe") { UseShellExecute = true };
            fallback.ArgumentList.Add("/k");
            fallback.ArgumentList.Add($"cd /d \"{path}\"");
            Process.Start(fallback);
        }
        catch (Exception ex)
        {
            AppLog.Error($"ui: не удалось открыть терминал в '{path}'", ex);
        }
    }

    private MenuItem MenuAction(string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = MenuItemStyle,
        };

        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Значок для пункта меню папки: подбираем по префиксу названия.</summary>
    private static string GlyphFor(string header)
    {
        if (header.StartsWith("Запустить", StringComparison.Ordinal)) return "?";
        if (header.StartsWith("Запустить от имени администратора", StringComparison.Ordinal)) return "?";
        if (header.StartsWith("Открыть", StringComparison.Ordinal)) return "?";
        if (header.StartsWith("Переименовать", StringComparison.Ordinal)) return "?";
        if (header.StartsWith("Убрать", StringComparison.Ordinal)) return "?";
        if (header.StartsWith("Удалить", StringComparison.Ordinal)) return "?";
        return "•";
    }

    public void RefreshFrequent()
    {
        var vms = new List<FrequentVm>();
        foreach (var (target, count) in _usage.Top(_entries, 8))
        {
            var entry = _entries.FirstOrDefault(e => string.Equals(e.Target, target, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            vms.Add(new FrequentVm(entry, IconFor(entry), count));
        }

        FrequentList.ItemsSource = vms;
        FrequentHint.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelFrequent.Header = TextFx.Spaced("ЧАСТОЕ") + (vms.Count > 0 ? "   " + vms.Count : string.Empty);
    }

    private void OnAppsListRightButton(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not AppRowVm { IsFolder: false, Entry: { } entry }) return;

        var menu = new ContextMenu
        {
            Background = (MediaBrush)FindResource("GlassTintBrush"),
            Foreground = (MediaBrush)FindResource("TextBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = 12,
        };

        var pinItem = new MenuItem { Header = _pins.Contains(entry) ? "Закрепить" : "Открепить" };
        pinItem.Click += (_, _) =>
        {
            bool added = _pins.Toggle(entry);
            AppLog.Write($"pins: '{entry.Name}' {(added ? "закреплён" : "откреплён")}");
            RefreshPins();
        };
        menu.Items.Add(pinItem);

        var launchItem = new MenuItem { Header = "Открыть" };
        launchItem.Click += (_, _) => Launch(entry);
        menu.Items.Add(launchItem);

        var adminItem = new MenuItem
        {
            Header = "Запустить от имени администратора",
            Style = MenuItemStyle,
        };
        adminItem.Click += (_, _) => LaunchAdmin(entry);
        menu.Items.Add(adminItem);
        menu.Items.Add(MenuAction("Показать в проводнике", () => ShowInExplorer(entry)));

        if (FindAncestor<Border>(e.OriginalSource as DependencyObject) is { } anchor) anchor.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---------- перетаскивание плиток (только внутренние, для OLE-групп) ----------

    private bool _pinDragging;
    private long _pinPressAt;

    /// <summary>Порог перетаскивания плитки: чтобы не мешать клику, ждём удержание и drag.</summary>
    private const double PinDragThreshold = 16;
    private const long PinDragHoldMs = 220;

    private void OnPinsMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pinDragStart = e.GetPosition(PinsList);
        _pinDragIndex = IndexOfPin(e.OriginalSource as DependencyObject);
        _pinDragging = false;
        _pinPressAt = Environment.TickCount64;
    }

    private void OnPinsMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pinDragIndex < 0) return;

        var position = e.GetPosition(PinsList);

        if (!_pinDragging)
        {
            // Первое движение по плитке, чтобы отличить клик от начала захвата: настоящий drag начинается
            // только после паузы удержания
            if (Environment.TickCount64 - _pinPressAt < PinDragHoldMs) return;
            if (Math.Abs(position.X - _pinDragStart.X) < PinDragThreshold
                && Math.Abs(position.Y - _pinDragStart.Y) < PinDragThreshold) return;

            _pinDragging = true;
        }

        // Если навести на другую плитку списка-цели, меняем индексы наведения местами
        var source = (UIElement)sender;
        var hit = IndexOfPin(source.InputHitTest(e.GetPosition(source)) as DependencyObject);
        if (hit < 0 || hit == _pinDragIndex) return;

        if (_openFolder is null) _pins.Move(_pinDragIndex, hit);
        else _pins.MoveInFolder(_openFolder, _pinDragIndex, hit);

        _pinDragIndex = hit;
        RefreshPins();
    }

    private void OnPinsDrop(object sender, System.Windows.DragEventArgs e)
    {
        // Перетаскивание текста из другого списка
        if (e.Data.GetDataPresent(typeof(AppEntry)))
        {
            if (e.Data.GetData(typeof(AppEntry)) is AppEntry dropped)
            {
                var list = _openFolder is null
                    ? _pins.Items
                    : (IReadOnlyList<HyprStart.Core.Storage.PinnedItem>)_openFolder.Children;

                int index = IndexOfPin(e.OriginalSource as DependencyObject);
                var folder = index >= 0 && index < list.Count && list[index].IsFolder ? list[index] : _openFolder;

                _pins.Add(dropped, folder);
                RefreshPins();
            }

            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(typeof(int))) return;

        int from = (int)e.Data.GetData(typeof(int))!;
        int to = IndexOfPin(e.OriginalSource as DependencyObject);

        if (to < 0) to = _pins.Items.Count - 1;
        _pins.Move(from, to);
        RefreshPins();
        e.Handled = true;
    }
    private int IndexOfPin(DependencyObject? source)
    {
        var element = FindAncestor<ContentPresenter>(source);
        if (element?.DataContext is PinVm pin)
        {
            var list = (IReadOnlyList<PinVm>)PinsList.ItemsSource;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], pin)) return i;
            }
        }

        return -1;
    }

    // ---------- быстрые ----------

    private void OnQuickDownloads(object sender, RoutedEventArgs e) => RunShell("shell:Downloads");
    private void OnQuickExplorer(object sender, RoutedEventArgs e) => RunShell("explorer.exe");

    private void OnQuickSettings(object sender, RoutedEventArgs e) => RunShell("ms-settings:");

    private void OnQuickTerminal(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true });
        }
        catch
        {
            RunShell("powershell.exe");
        }
    }

    private void OnMediaPrev(object sender, RoutedEventArgs e) => _ = RunMediaAsync(session => session.TrySkipPreviousAsync().AsTask(), 0xB1);
    private void OnMediaPlayPause(object sender, RoutedEventArgs e) => _ = RunMediaAsync(session => session.TryTogglePlayPauseAsync().AsTask(), 0xB3);
    private void OnMediaNext(object sender, RoutedEventArgs e) => _ = RunMediaAsync(session => session.TrySkipNextAsync().AsTask(), 0xB0);

    /// <summary>
    /// Управляющий GSMTC: если сессия есть и жива, шлём команду ей,
    /// иначе (браузер / YouTube), если API не умеет — шлём глобальный медиа-ключ.
    /// </summary>
    private async Task RunMediaAsync(
        Func<Windows.Media.Control.GlobalSystemMediaTransportControlsSession, Task<bool>> action,
        byte fallbackVk)
    {
        bool ok = await _monitor.ControlPlaybackAsync(action, fallbackVk);
        AppLog.Write($"media: медиа {(ok ? "ок" : "мимо цели")} (код-клавиша 0x{fallbackVk:X2})");

        if (ok) OnTrackChanged();
    }

    private void RunShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            AppLog.Write($"quick: {target}");
            DismissRequested?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"quick: не удалось открыть {target}", ex);
        }
    }

    private void OnSleep(object sender, RoutedEventArgs e) => AskPower("sleep", "Усыпить ПК?");
    private void OnRestart(object sender, RoutedEventArgs e) => AskPower("restart", "Перезагрузить?");
    private void OnShutdown(object sender, RoutedEventArgs e) => AskPower("shutdown", "Выключить?");

    private void AskPower(string action, string question)
    {
        _pendingPower = action;
        PowerConfirmText.Text = question;
        PowerConfirm.Visibility = Visibility.Visible;
    }

    private void OnPowerCancel(object sender, RoutedEventArgs e)
    {
        _pendingPower = null;
        PowerConfirm.Visibility = Visibility.Collapsed;
    }

    private void OnPowerConfirm(object sender, RoutedEventArgs e)
    {
        var action = _pendingPower;
        _pendingPower = null;
        PowerConfirm.Visibility = Visibility.Collapsed;
        if (action is null) return;

        try
        {
            switch (action)
            {
                case "sleep":
                    SetSuspendState(false, false, false);
                    break;
                case "restart":
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true });
                    break;
                case "shutdown":
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { UseShellExecute = true });
                    break;
            }

            AppLog.Write($"power: {action} подтверждён");
        }
        catch (Exception ex)
        {
            AppLog.Error($"power: не удалось выполнить {action}", ex);
        }
    }

    // ---------- подсветка / рамка ----------

    /// <summary>Одна кисть на карточку: красим её рамку градиентом, а изменение прозрачности анимируем.</summary>
    private readonly Dictionary<HeaderedContentControl, LinearGradientBrush> _panelBrushes = new();

    /// <summary>Порядок карточек, участвующих в подсветке фокуса.</summary>
    private HeaderedContentControl[] Panels => new[] { PanelApps, PanelPinned, PanelChat, PanelFrequent, PanelQuick };

    private MediaBrush BrushFor(HeaderedContentControl panel)
    {
        if (!_panelBrushes.TryGetValue(panel, out var brush))
        {
            brush = new LinearGradientBrush(Colors.White, Colors.White, 45);
            _panelBrushes[panel] = brush;
            panel.BorderBrush = brush;
        }

        // Берём цвета из градиента темы, чтобы рамка всегда подходила к палитре карточки.
        if (System.Windows.Application.Current?.TryFindResource("PanelBorderGradient") is LinearGradientBrush theme
            && theme.GradientStops.Count >= 2
            && brush.GradientStops.Count >= 2)
        {
            brush.GradientStops[0].Color = theme.GradientStops[0].Color;
            brush.GradientStops[1].Color = theme.GradientStops[1].Color;
        }

        return brush;
    }

    /// <summary>
    /// Плавная смена (120 мс): меняем прозрачность и толщину,
    /// у активной толщина 1.5, у остальных 1.0 — так виден фокус.
    /// На MouseLeave каждая карточка возвращается к обычному виду сама.
    /// </summary>
    private void SetPanelFocus(HeaderedContentControl? active, bool animate = true)
    {
        _focused = active;

        foreach (var panel in Panels)
        {
            var brush = BrushFor(panel);
            bool isActive = ReferenceEquals(panel, active);

            double target = isActive ? 1.0 : DimmedBorderOpacity;
            double thickness = isActive ? 1.5 : 1.0;

            if (animate)
            {
                var animation = new System.Windows.Media.Animation.DoubleAnimation(
                    target,
                    TimeSpan.FromMilliseconds(120));
                brush.BeginAnimation(MediaBrush.OpacityProperty, animation);

                panel.BeginAnimation(BorderThicknessProperty,
                    new System.Windows.Media.Animation.ThicknessAnimation(
                        new Thickness(thickness), TimeSpan.FromMilliseconds(120)));
            }
            else
            {
                brush.BeginAnimation(MediaBrush.OpacityProperty, null);
                brush.Opacity = target;

                panel.BeginAnimation(BorderThicknessProperty, null);
                panel.BorderThickness = new Thickness(thickness);
            }
        }
    }

    /// <summary>Прозрачность рамки неактивной карточки: чтобы активная была заметна.</summary>
    private const double DimmedBorderOpacity = 0.28;

    private void OnThemeApplied()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ThemePillText.Text = _themes.CurrentName;
            SetPanelFocus(_focused ?? PanelApps, animate: false);
            RefreshCavaGradient();
            AppLog.Write($"ui: тема сменилась на {_themes.CurrentName}");
        }));
    }

    private void ApplyFilterFromSearch()
    {
        UpdatePlaceholder();
        if (!_appsLoaded) return;

        RebuildTree();

        // Если мы фильтруем поиск, карточка приложения получает фокус сразу:
        if (_suppressSearchFade) return;

        AppsList.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(110)));
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilterFromSearch();

    private void UpdatePlaceholder()
        => SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Режим переименования пина: поглощаем весь клавиатурный ввод, без навигации.
        if (_renameTarget is not null)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                EndRename(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EndRename(false);
            }

            return;
        }

        // Alt+1..9 / Ctrl+1..9 — запуск закреплённого по номеру плитки
        if (TryActivatePinnedByDigit(e)) return;

        // Tab из поиска уводит фокус в мини-чат (Shift+Tab — как обычный Tab).
        // ВАЖНО: этот блок обязан стоять ВЫШЕ проверки фокуса на ChatInput —
        // иначе после перевода фокуса сюда мы бы выходили раньше и чат
        // становился недостижим с клавиатуры.
        if (e.Key == Key.Tab && ReferenceEquals(Keyboard.FocusedElement, SearchBox))
        {
            e.Handled = true;
            ChatInput.Focus();
            return;
        }

        // Поле мини-чата — отдельный ввод: его клавиши не должны доходить до навигации
        // по списку (иначе Enter в чате запускал верхний результат поиска).
        // Собственная обработка (Enter/Shift+Enter/Esc/Ctrl+L/Tab) — в OnChatInputPreviewKeyDown.
        if (ReferenceEquals(Keyboard.FocusedElement, ChatInput)) return;

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox && SearchBox.Text.Length > 0)
                {
                    SearchBox.Clear();
                }
                else if (SearchBox.Text.Length > 0)
                {
                    SearchBox.Clear();
                }
                else
                {
                    DismissRequested?.Invoke();
                }
                break;

            case Key.Down:
                e.Handled = true;
                MoveSelection(1);
                break;

            case Key.Up:
                e.Handled = true;
                MoveSelection(-1);
                break;

            case Key.PageDown:
                e.Handled = true;
                MoveSelection(10);
                break;

            case Key.PageUp:
                e.Handled = true;
                MoveSelection(-10);
                break;

            case Key.Left:
                if (IsPlaybackContext())
                {
                    e.Handled = true;
                    OnMediaPrev(this, new RoutedEventArgs());
                }
                break;

            case Key.Right:
                if (IsPlaybackContext())
                {
                    e.Handled = true;
                    OnMediaNext(this, new RoutedEventArgs());
                }
                break;

            case Key.Space:
            case Key.System:
                // Space (в т.ч. с зажатым Alt) или кнопка «медиа» == Play/Pause,
                // если фильтр поиска пустой и имя не редактируем
                if ((e.Key == Key.Space || e.SystemKey == Key.Space)
                    && string.IsNullOrEmpty(SearchBox.Text)
                    && _renameTarget is null)
                {
                    e.Handled = true;
                    AudioControl.PlayPause();
                }
                break;

            case Key.Delete:
                // Delete убирает выбранное приложение из частого
                if (SearchBox.Text.Length == 0 && AppsList.SelectedItem is AppRowVm { IsFolder: false, Entry: { } selectedEntry })
                {
                    e.Handled = true;
                    if (_usage.Remove(selectedEntry.Target)) RefreshFrequent();
                }
                break;

            case Key.Enter:
                e.Handled = true;

                // Shift+Enter — запуск от администратора
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    if (AppsList.SelectedItem is AppRowVm { IsFolder: false, Entry: { } adminEntry })
                    {
                        AppLog.Write($"ui: '{adminEntry.Name}' от администратора");
                        LaunchAsAdmin?.Invoke(adminEntry);
                    }
                }
                else
                {
                    ActivateSelected();
                }
                break;

            case Key.F5:
                e.Handled = true;
                IndexRefreshRequested?.Invoke();
                break;

            case Key.Back:
                // По Backspace стираем символ поиска (вне поля, для удобства)
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
                e.Handled = true;
                SearchBox.Focus();
                if (SearchBox.Text.Length > 0) SearchBox.Text = SearchBox.Text[..^1];
                SearchBox.CaretIndex = SearchBox.Text.Length;
                break;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Пробел в пустом поиске не попадает в текст: запускаем воспроизведение.
        if (e.Text == " " && IsPlaybackContext())
        {
            e.Handled = true;
            return;
        }

        // Любой ввод текста (кроме фокуса в поле) уходит в поиск:
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    /// <summary>
    /// Перехват ввода глобально, чтобы быстро набрать запрос и найти приложение.
    /// Когда в SearchBox нет символов и ничего не редактируем,
    /// иначе клавиша уходит в поле: Space — пробел, стрелки — перемещение.
    /// </summary>
    private bool IsPlaybackContext()
        => _renameTarget is null && string.IsNullOrEmpty(SearchBox.Text);

    private void MoveSelection(int delta)
    {
        if (AppsList.Items.Count == 0) return;

        int index = Math.Clamp(AppsList.SelectedIndex + delta, 0, AppsList.Items.Count - 1);
        AppsList.SelectedIndex = index;
        if (AppsList.SelectedItem is not null) AppsList.ScrollIntoView(AppsList.SelectedItem);
    }

    private void ActivateSelected()
    {
        if (AppsList.SelectedItem is not AppRowVm row)
        {
            if (AppsList.Items.Count > 0 && AppsList.Items[0] is AppRowVm first) Activate(first);
            return;
        }

        Activate(row);
    }

    /// <summary>Запуск быстрого действия: пользователь выбрал ">" или команду системы.</summary>
    private void ExecuteAction(HyprStart.Core.Actions.ActionItem action)
    {
        AppLog.Write($"action: {action.Payload}");

        try
        {
            switch (action.Payload)
            {
                case "lock":
                    Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = true });
                    break;
                case "sleep":
                    SetSuspendState(false, false, false);
                    break;
                case "restart":
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true });
                    break;
                case "shutdown":
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { UseShellExecute = true });
                    break;
                case "taskmgr":
                    Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                    break;
                case "settings":
                    Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
                    break;
                default:
                    Process.Start(new ProcessStartInfo(action.Payload) { UseShellExecute = true });
                    break;
            }

            if (action.Kind == HyprStart.Core.Actions.ActionKind.Command) DismissRequested?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"action: не удалось выполнить {action.Payload}", ex);
        }
    }

    private void Activate(AppRowVm row)
    {
        if (row.Action is { } action)
        {
            ExecuteAction(action);
            return;
        }

        if (row.IsMath)
        {
            try
            {
                System.Windows.Clipboard.SetText(row.MathValue);
                AppLog.Write($"math: {row.MathValue} скопировано в буфер");
            }
            catch (Exception ex)
            {
                AppLog.Error("math: буфер обмена недоступен", ex);
            }

            // Enter по результату = копирование в буфер: закрываем оверлей.
            DismissRequested?.Invoke();
            return;
        }

        if (row.IsFolder)
        {
            ToggleFolder(row.Name);
            return;
        }

        if (row.Entry is { } entry) Launch(entry);
    }

    private void Launch(AppEntry entry)
    {
        AppLog.Write($"ui: запуск '{entry.Name}'");
        LaunchRequested?.Invoke(entry);
    }

    // ---------- перетаскивание из списка в закреплённое (без OLE) ----------

    private System.Windows.Point _appsDragStart;
    private AppEntry? _dragEntry;
    private bool _appDragged;
    private long _appsPressAt;

    /// <summary>Порог перетаскивания из списка: удержание + смещение курсора.</summary>
    private const double AppDragThreshold = 18;
    private const long AppDragHoldMs = 250;

    private void OnAppsListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _appsDragStart = e.GetPosition(AppsList);
        _dragEntry = (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as AppRowVm)?.Entry;
        _appDragged = false;
        _appsPressAt = Environment.TickCount64;
    }

    private void OnAppsListMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragEntry is null) return;

        var position = e.GetPosition(AppsList);
        if (!_appDragged)
        {
            if (Environment.TickCount64 - _appsPressAt < AppDragHoldMs) return;
            if (Math.Abs(position.X - _appsDragStart.X) < AppDragThreshold
                && Math.Abs(position.Y - _appsDragStart.Y) < AppDragThreshold) return;

            _appDragged = true;
            AppsList.CaptureMouse();
            AppLog.Write($"drag: бросок '{_dragEntry.Name}'");
        }
    }

    private void OnAppsListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Если перетаскивали приложение из списка, то по отпусканию кладём его
        if (_appDragged)
        {
            _appDragged = false;
            AppsList.ReleaseMouseCapture();

            var entry = _dragEntry;
            _dragEntry = null;

            if (entry is not null) DropIntoPinned(entry);
            return;
        }

        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not AppRowVm row) return;

        Activate(row);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ---------- polybar ----------

    // ---------- рабочий цикл виджета ----------

    /// <summary>Старт виджета: запускаем таймеры и первый опрос монитора.</summary>
    public void ResumeActivity()
    {
        if (_awake) return;

        _awake = true;
        _tick = 0;
        OnTick();                 // первый тик сразу, без ожидания таймера

        _tickTimer.Start();
        _meterTimer.Start();
        AppLog.Write("perf: активность включена");
    }

    /// <summary>
    /// Пауза виджета: останавливаем таймеры и мониторинг. На холостом ходу
    /// ~1.2% уходит на обновление графики и опрос WASAPI 16 раз в секунду,
    /// поэтому в скрытом состоянии таймеры останавливаются целиком.
    /// </summary>
    public void SuspendActivity()
    {
        // Скрытие оверлея рвёт активный HTTP-запрос: в скрытом виде никакой работы.
        CancelChatRequest();

        if (!_awake) return;

        _awake = false;
        _tickTimer.Stop();
        _meterTimer.Stop();
        AppLog.Write("perf: активность приостановлена");
    }

    private void OnTick()
    {
        _tick++;
        UpdateClock();

        _monitor.SampleCpu();
        _monitor.SampleRam();

        // Обновляем Text только при изменении значений, чтобы не дёргать разметку
        SetTextIfChanged(PillCpu, $"CPU {_monitor.CpuPercent}%");
        SetTextIfChanged(PillRam, $"RAM {_monitor.RamPercent}%");

        if (_tick % 3 == 0)
        {
            _monitor.SampleVolume();
            SetTextIfChanged(PillVol, _monitor.VolumePercent >= 0 ? $"VOL {_monitor.VolumePercent}%" : "VOL —");
        }

        // Слушаем изменения GSMTC (HookTrackEventsAsync), но не ждём их на каждом тике
    }

    /// <summary>Меняем текст только при реальном изменении, чтобы не перезапускать measure/arrange.</summary>
    private static void SetTextIfChanged(TextBlock target, string value)
    {
        if (!string.Equals(target.Text, value, StringComparison.Ordinal)) target.Text = value;
    }

    /// <summary>Событие GSMTC: применяем трек на UI-потоке, если нужно.</summary>
    private void OnTrackChanged()
    {
        if (Dispatcher.CheckAccess()) SetTrackText();
        else Dispatcher.BeginInvoke(new Action(SetTrackText));
    }

    // ---------- cava-визуализация ----------

    private readonly System.Windows.Shapes.Rectangle[] _bars = new System.Windows.Shapes.Rectangle[30];
    private readonly double[] _levels = new double[30];
    private readonly double[] _jitter = new double[30];

    /// <summary>Сжимаем по Y на плейсхолдере: масштабом не трогаем, а только сжимаем.</summary>
    private readonly ScaleTransform[] _barScales = new ScaleTransform[30];

    /// <summary>Шкала по всем барам массива, а не клонируем.</summary>
    private const double MeterMinScale = 0.02;

    /// <summary>Минимальная высота бара (на случай полной тишины в cava).</summary>
    private const double MeterFalloff = 0.82;

    /// <summary>Замедление падения: за каждый тик теряем лишь долю.</summary>
    private const double MeterSilenceFloor = 0.006;
    private ScaleTransform? _scale;

    /// <summary>Градиент заливки бара: сверху AccentTo, а к низу Accent.</summary>
    private readonly LinearGradientBrush _cavaBrush = new()
    {
        StartPoint = new System.Windows.Point(0.5, 0),
        EndPoint = new System.Windows.Point(0.5, 1),
        GradientStops =
        {
            new GradientStop(Colors.White, 0),
            new GradientStop(Colors.White, 1),
        },
    };

    private HeaderedContentControl? _focused;

    private void BuildMeterBars()
    {
        RefreshCavaGradient();

        for (int i = 0; i < _bars.Length; i++)
        {
            var scale = new ScaleTransform(1, MeterMinScale);

            // RenderTransformOrigin="0.5,1": растим бар от пола,
            // ScaleY не ломает Measure/Arrange и не требует перерасчёта.
            var bar = new System.Windows.Shapes.Rectangle
            {
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Height = double.NaN,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Fill = _cavaBrush,
                RadiusX = 2.5,
                RadiusY = 2.5,
                RenderTransformOrigin = new System.Windows.Point(0.5, 1),
                RenderTransform = scale,
            };

            _jitter[i] = 0.75 + 0.25 * Random.Shared.NextDouble();
            _bars[i] = bar;
            _barScales[i] = scale;
            MeterBars.Children.Add(bar);
        }
    }

    /// <summary>Заливка одинаковая для всех баров (одна CavaGradient).</summary>
    private void RefreshCavaGradient()
    {
        if (System.Windows.Application.Current?.TryFindResource("CavaGradient") is not LinearGradientBrush source
            || source.GradientStops.Count < 2
            || _cavaBrush.GradientStops.Count < 2)
        {
            return;
        }

        _cavaBrush.GradientStops[0].Color = source.GradientStops[0].Color;
        _cavaBrush.GradientStops[1].Color = source.GradientStops[1].Color;
    }

    /// <summary>
    /// Пиковый уровень (IAudioMeterInformation) превращаем в 30 полос:
    /// затухание к хвосту массива + незначительный джиттер по полосам.
    /// Тишина распознаётся как тихая шкала (?0.82 за тик), как в Linux cava.
    /// Двигаем только ScaleTransform.ScaleY: Measure/Arrange больше не дёргаем.
    /// </summary>
    private void UpdateMeter()
    {
        _monitor.SamplePeak();
        float peak = _monitor.Peak;

        // Быстрый рост, медленное падение: бар поднимается сразу.
        // За тик падаем только на долю, чтобы не дёргался.
        bool quiet = peak <= 0.02f && IsMeterFlat();
        if (quiet)
        {
            SetTrackText();
            return;
        }

        bool active = peak > MeterMinScale;

        for (int i = 0; i < _bars.Length; i++)
        {
            double shape = Math.Max(0.12, 1.0 - i * 0.028);

            if (active && Random.Shared.NextDouble() < 0.05)
            {
                _jitter[i] = 0.7 + 0.3 * Random.Shared.NextDouble();
            }

            double target = peak * shape * _jitter[i];

            // Ростом в один тик догоняем пик, при спаде тормозим плавно
            _levels[i] = target > _levels[i] ? target : Math.Max(0, _levels[i] * MeterFalloff);
            if (_levels[i] < MeterSilenceFloor) _levels[i] = 0;

            double scale = Math.Max(MeterMinScale, Math.Min(1, _levels[i]));

            if (Math.Abs(_barScales[i].ScaleY - scale) > 0.004) _barScales[i].ScaleY = scale;
        }

        SetTrackText();
    }

    private void SetTrackText()
        => SetTextIfChanged(QuickTrack, "▶ " + (_monitor.Track.Length == 0 ? "—" : _monitor.Track));

    private bool IsMeterFlat()
    {
        for (int i = 0; i < _levels.Length; i++)
        {
            if (_levels[i] > MeterSilenceFloor) return false;
        }

        return true;
    }

    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) return null;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;

            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    private void UpdateClock() => PillClock.Text = DateTime.Now.ToString("HH:mm:ss");

    // ---------- геометрия ----------

    private void ApplyGeometry()
    {
        var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position) ?? WinForms.Screen.PrimaryScreen;
        if (screen is null) return;

        var bounds = screen.Bounds;

        // Высота на 1 пиксель меньше экрана: иначе Windows считает окно полноэкранным
        // и Wallpaper Engine ставит обои на паузу. См. LayoutCalculator.FullscreenTrim.
        var overlay = LayoutCalculator.OverlaySize(bounds.Width, bounds.Height);

        Left = bounds.X / _dpiScale;
        Top = bounds.Y / _dpiScale;
        Width = overlay.Width / _dpiScale;
        Height = overlay.Height / _dpiScale;

        Relayout();
        AppLog.Write($"geometry: {bounds.Width}x{bounds.Height} dpi={_dpiScale:0.###} size={Width:0}x{Height:0} (trim={LayoutCalculator.FullscreenTrim})");
    }

    // ---------- мини-чат MINI AI ----------

    /// <summary>Клиент создаётся на лету из настроек: ключ мог появиться в env уже после старта.</summary>
    private QuickChatClient CreateChatClient()
    {
        var settings = _settings.Current;
        var options = new QuickChatOptions
        {
            ApiKey = QuickChatOptions.ResolveApiKey(settings.ChatApiKey),
            Model = string.IsNullOrWhiteSpace(settings.ChatModel) ? QuickChatOptions.DefaultModel : settings.ChatModel,
            ProxyUrl = settings.ChatProxy ?? string.Empty,
        };

        return new QuickChatClient(options);
    }

    private void InitChat()
    {
        ChatMessages.ItemsSource = _chatLines;
        UpdateChatPlaceholder();

        // Чат стартует чистым: лента пуста, подсказку «спроси ИИ…» рисует XAML.
        // Сообщение показываем только если работать нечем — без ключа.
        if (string.IsNullOrWhiteSpace(QuickChatOptions.ResolveApiKey(_settings.Current.ChatApiKey)))
        {
            AppendChatLine(ChatRole.System,
                $"Нет ключа. Задай переменную окружения {QuickChatOptions.EnvKeyName} " +
                "или chatApiKey в settings.json и перезапусти.", notice: true);
        }
    }

    private void UpdateChatPlaceholder()
        => ChatPlaceholder.Visibility = ChatInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnChatInputTextChanged(object sender, TextChangedEventArgs e) => UpdateChatPlaceholder();

    private void AppendChatLine(ChatRole role, string text, bool notice = false)
    {
        var line = new ChatLineVm(role, text, notice);
        _chatLines.Add(line);
        ChatHint.Visibility = Visibility.Collapsed;
        if (!notice) _chatStreamLine = role == ChatRole.Assistant ? line : null;
        ScrollChatToEnd();
    }

    private void ScrollChatToEnd()
    {
        // Автопрокрутка на следующем кадре диспетчера: контейнеры ещё не измерены.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try { ChatScroll.ScrollToEnd(); } catch { /* окно могло закрыться */ }
        }));
    }

    /// <summary>Кнопка «Очистить» в шапке и Ctrl+L: сброс истории и ленты.</summary>
    private void ClearChat()
    {
        CancelChatRequest();
        _chatHistory.Clear();
        _chatLines.Clear();
        _chatStreamLine = null;
        ChatHint.Visibility = Visibility.Visible;
        AppLog.Write("chat: контекст очищен");
    }

    private void OnChatClear(object sender, RoutedEventArgs e) => ClearChat();

    /// <summary>Отменяет активный HTTP-запрос и отпускает токен.</summary>
    private void CancelChatRequest()
    {
        var cts = _chatCts;
        _chatCts = null;
        if (cts is null) return;

        try { cts.Cancel(); } catch { /* уже отменён */ }
        // Dispose отложен: поток стрима ещё читает токен.
        cts.Dispose();
    }

    /// <summary>
    /// Обработчик клавиш поля чата. ВАЖНО: подписан на PreviewKeyDown, а не KeyDown —
    /// у многострочного TextBox (AcceptsReturn=True) встроенный TextEditor забирает Enter
    /// себе (вставляет перевод строки) и до KeyDown событие не доходит, поэтому отправка
    /// не срабатывала. Preview-стадия приходит раньше, и e.Handled=true её отменяет.
    /// </summary>
    private void OnChatInputPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (ChatInput.Text.Length > 0) ChatInput.Clear();
                else SearchBox.Focus();
                break;

            case Key.Tab:
                // Повторный Tab из чата — обратно в поиск; Shift+Tab — тоже симметрично.
                e.Handled = true;
                SearchBox.Focus();
                break;

            case Key.L when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                e.Handled = true;
                ClearChat();
                break;

            case Key.Enter:
                // Shift+Enter вставляет перенос строки средствами TextBox.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) break;

                e.Handled = true;
                _ = SendChatAsync();
                break;
        }
    }

    /// <summary>Отправка запроса: блокирует повторную отправку до конца генерации.</summary>
    private async Task SendChatAsync()
    {
        if (_chatBusy) return;

        string prompt = ChatInput.Text.Trim();
        if (prompt.Length == 0) return;

        ChatInput.Clear();
        AppendChatLine(ChatRole.User, prompt);
        _chatHistory.Add(ChatRole.User, prompt);

        var client = CreateChatClient();
        if (!client.HasApiKey)
        {
            AppendChatLine(ChatRole.System,
                $"Нет ключа: задай {QuickChatOptions.EnvKeyName} или chatApiKey в settings.json.", notice: true);
            return;
        }

        _chatBusy = true;
        // ChatInput на время стрима НЕ отключаем: иначе WPF уводит фокус на первый
        // доступный контрол (SearchBox) и после ответа печатать приходится заново.
        // Повторную отправку блокирует флаг _chatBusy.
        var cts = new CancellationTokenSource();
        _chatCts = cts;

        var reply = new ChatLineVm(ChatRole.Assistant, string.Empty);
        _chatLines.Add(reply);
        _chatStreamLine = reply;
        ScrollChatToEnd();

        // Системный промпт всегда несёт дату и город: иначе модель отвечает
        // «у меня нет доступа к текущей дате» и путает местоположение.
        string systemPrompt = ChatContext.BuildSystemPrompt(
            QuickChatOptions.SystemPrompt, DateTime.Now, ChatContext.DefaultCity);

        // Погода: сначала точные цифры из Open-Meteo (без ключа и капчи). Ветка wttr.in
        // остаётся запасной — если Open-Meteo не ответил, пробуем её.
        if (ChatContext.IsWeatherRequest(prompt))
        {
            string live = await client.GetWeatherLiveAsync(cts.Token);
            if (live.Length == 0)
            {
                AppLog.Write("chat: open-meteo пусто, пробую wttr.in");
                live = await client.GetWeatherAsync(ChatContext.DefaultCity, cts.Token);
            }

            if (live.Length == 0)
            {
                reply.Text = "Не удалось получить сводку погоды (Open-Meteo и wttr.in недоступны).";
                reply.IsNotice = true;
                FinishChat(cts);
                return;
            }

            systemPrompt = ChatContext.BuildWeatherPrompt(
                QuickChatOptions.SystemPrompt, DateTime.Now, ChatContext.DefaultCity, live);
            AppLog.Write($"chat: погода -> {live}");
        }

        // Веб-контекст добавляется ВСЕГДА при маркерах актуальности — включая погоду.
        // Раньше он жил в else-ветке и для погодных вопросов терялся.
        var (webQuery, needsSearch) = WebSearch.Parse(prompt);
        if (needsSearch && webQuery.Length > 0)
        {
            // Жёсткий таймаут на всю цепочку «выдача + страницы»: чат не должен висеть
            // из-за медленного внешнего источника, лучше ответить без веб-контекста.
            using var webCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            webCts.CancelAfter(WebSearch.TotalTimeout);

            IReadOnlyList<WebSearchResult> found;
            try
            {
                found = await client.SearchWebAsync(webQuery, webCts.Token);
            }
            catch (OperationCanceledException) when (!cts.IsCancellationRequested)
            {
                AppLog.Write($"chat: web таймаут {WebSearch.TotalTimeout.TotalSeconds:0} с по «{webQuery}»");
                found = Array.Empty<WebSearchResult>();
            }

            if (found.Count > 0)
            {
                systemPrompt += WebSearch.BuildContextPrompt(webQuery, found);
                int withText = found.Count(r => r.PageText.Length > 0);
                AppLog.Write($"chat: web -> {found.Count} результатов, текст страниц у {withText} по «{webQuery}»");
            }
            else
            {
                AppLog.Write($"chat: web пусто по «{webQuery}»");
            }
        }

        try
        {
            var result = await client.StreamAsync(
                _chatHistory.BuildRequest(systemPrompt),
                delta =>
                {
                    // Дельты приходят из фонового потока — маршалим в UI.
                    Dispatcher.InvokeAsync(() => reply.Text += delta);
                },
                cts.Token);

            if (result.IsCancelled)
            {
                if (string.IsNullOrEmpty(reply.Text)) _chatLines.Remove(reply);
                AppLog.Write("chat: запрос отменён");
            }
            else if (!result.Ok)
            {
                reply.Text = string.IsNullOrEmpty(reply.Text) ? result.Error : reply.Text + "\n" + result.Error;
                reply.IsNotice = true;
            }
            else
            {
                string text = reply.Text.Trim();
                if (text.Length == 0) _chatLines.Remove(reply);
                else reply.Text = text;
                _chatHistory.Add(ChatRole.Assistant, text);
            }
        }
        catch (Exception ex)
        {
            reply.Text = "Ошибка: " + ex.Message;
            AppLog.Write("chat: " + ex.Message);
        }
        finally
        {
            FinishChat(cts);
        }
    }

    /// <summary>Снимает состояние «занято» после ответа или ошибки чата и возвращает фокус в ввод.</summary>
    private void FinishChat(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_chatCts, cts)) _chatCts = null;
        cts.Dispose();
        _chatBusy = false;
        _chatStreamLine = null;
        ScrollChatToEnd();
        // Фокус в поле ввода: следующий вопрос можно печатать сразу.
        ChatInput.Focus();
    }

    private void Relayout()
    {
        var spec = _settings.Current.Layout.ToSpec();
        var result = LayoutCalculator.Compute(Width, Height, spec);
        _lastLayout = result;

        Place(PanelApps, result.Apps);
        Place(PanelPinned, result.Pinned);
        Place(PanelChat, result.Chat);
        Place(PanelFrequent, result.Frequent);
        Place(PanelQuick, result.Quick);
        Place(Polybar, result.Polybar);

        FitPinTiles(result.Pinned);

        AppLog.Write($"layout: apps={result.Apps} pinned={result.Pinned} chat={result.Chat} " +
                     $"frequent={result.Frequent} quick={result.Quick} polybar={result.Polybar}");
    }

    /// <summary>
    /// Подгоняет плитки закреплённого под ширину карточки: сетка 4x2 должна
    /// влезать целиком (8 плиток = бейджи Alt+1..9), прокрутка — только запасной вариант.
    /// </summary>
    private void FitPinTiles(PixelRect pinned)
    {
        // Внутренние отступы PanelStyle: рамка + padding заголовка и контента.
        const double chrome = 34;

        double inner = pinned.Width - chrome;
        if (inner <= 0) return;

        if (PinsList.ItemsSource is IReadOnlyList<PinVm> pins)
        {
            foreach (var pin in pins) pin.FitTo(inner);
        }

        if (FolderList.ItemsSource is IReadOnlyList<PinVm> folderPins)
        {
            foreach (var pin in folderPins) pin.FitTo(inner);
        }

        double gridHeight = PinTileGeometry.GridHeight(PinTileGeometry.TileHeight(PinTileGeometry.TileWidth(inner)));
        PinsScroll.VerticalScrollBarVisibility = PinTileGeometry.FitsWithoutScroll(inner, pinned.Height - 56)
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;

        AppLog.Write($"pins: inner={inner:0} tile={PinTileGeometry.TileWidth(inner):0.#}" +
                     $"x{PinTileGeometry.TileHeight(PinTileGeometry.TileWidth(inner)):0.#} grid={gridHeight:0}");
    }

    private static void Place(FrameworkElement element, PixelRect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = Math.Max(0, rect.Width);
        element.Height = Math.Max(0, rect.Height);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        base.OnClosed(e);
    }

    private const int GwlStyle = -16;
    private const long WsSysMenu = 0x00080000;
    private const int WmSysCommand = 0x0112;
    private const int ScKeyMenu = 0xF100;

    /// <summary>Windows сообщает об изменении системных настроек — в том числе обоев.</summary>
    private const int WmSettingChange = 0x001A;

    /// <summary>lParam = эта строка → сменились обои рабочего стола.</summary>
    private const string WallpaperSetting = "ConvertedWallpaper";

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>Глушим системное меню окна: Alt+Space не должно открывать восстановить/закрыть.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (wParam.ToInt32() & 0xFFF0) == ScKeyMenu)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmSettingChange && lParam != IntPtr.Zero &&
            System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == WallpaperSetting)
        {
            // Обои сменились (в т.ч. из Wallpaper Engine) — пересчитываем палитру сразу,
            // не дожидаясь следующего открытия оверлея. Снимок идёт в UI-потоке окна.
            handled = false;
            Dispatcher.BeginInvoke(new Action(() => WallpaperChanged?.Invoke()));
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    /// <summary>Фокус на поиске при показе: при клике по иконке в трее активируем окно.</summary>
    public void FocusForInput()
    {
        Activate();
        Focus();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        FocusManager.SetFocusedElement(this, SearchBox);
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
