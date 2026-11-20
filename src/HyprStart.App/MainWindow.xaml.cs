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
using HyprStart.Core.Settings;
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
        => FitTo(availableWidth, PinTileGeometry.ColumnCount(availableWidth));

    /// <summary>
    /// Подгоняет плитку под ширину контента при заданном числе колонок.
    /// Колонки считает раскладка (она знает, скрыт ли мини-чат), а плитка лишь
    /// подстраивает ширину — иначе сетка и плитки считали бы колонки по-разному.
    /// </summary>
    public void FitTo(double availableWidth, int columns)
    {
        double width = PinTileGeometry.TileWidth(availableWidth, columns);
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

    /// <summary>
    /// true у служебной подсказки "MINI AI не настроен": такие строки снимаются,
    /// как только пользователь задал ключ в настройках.
    /// </summary>
    public bool IsSetupHint { get; set; }

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
        ChatRole.User => Localizer.T("ChatRoleUser"),
        ChatRole.Assistant => Localizer.T("ChatRoleAssistant"),
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
    /// <summary>
    /// Число колонок сетки плиток «Закреплено». Растёт, когда панель MINI AI скрыта
    /// и «Закреплено» занимает всю правую ширину. Именно DependencyProperty, а не
    /// поиск UniformGrid по визуальному дереву: панель живёт в ItemsPanelTemplate,
    /// и до первого рендера её просто нет в дереве (поиск возвращал null, и сетка
    /// оставалась в 5 колонок). Разметка цепляется сюда через Binding.
    /// </summary>
    public static readonly DependencyProperty PinColumnsProperty =
        DependencyProperty.Register(nameof(PinColumns), typeof(int), typeof(MainWindow),
            new PropertyMetadata(PinTileGeometry.Columns));

    public int PinColumns
    {
        get => (int)GetValue(PinColumnsProperty);
        set => SetValue(PinColumnsProperty, value);
    }

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
    private SettingsWindow? _settingsWindow;
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

        PanelApps.Header = TextFx.Spaced(Localizer.T("PanelAppsHeader"));
        PanelPinned.Header = TextFx.Spaced(Localizer.T("PanelPinnedHeader"));
        PanelFrequent.Header = TextFx.Spaced(Localizer.T("PanelFrequentHeader"));
        PanelQuick.Header = TextFx.Spaced(Localizer.T("PanelQuickHeader"));
        PanelChat.Header = TextFx.Spaced(Localizer.T("PanelChatHeader"));

        InitChat();

        // Смена языка в настройках: перечитываем заголовки панелей и строки чата.
        Localizer.LanguageChanged += OnLanguageChanged;

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
        PanelApps.Header = TextFx.Spaced(Localizer.T("PanelAppsHeader")) + "   " + index.Entries.Count;

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
        menu.Items.Add(MenuAction("MenuLaunch", () => Launch(vm.Entry)));
        menu.Items.Add(MenuAction("MenuLaunchAsAdmin", () => LaunchAdmin(vm.Entry)));
        menu.Items.Add(MenuAction(_pins.Contains(vm.Entry) ? "MenuUnpin" : "MenuPin", () =>
        {
            _pins.Toggle(vm.Entry);
            RefreshPins();
        }));

        menu.Items.Add(MenuAction("MenuRemoveFromFrequent", () =>
        {
            _usage.Remove(vm.Entry.Target);
            RefreshFrequent();
        }));

        menu.PlacementTarget = e.OriginalSource as UIElement;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
