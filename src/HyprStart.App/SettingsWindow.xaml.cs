using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HyprStart.App.Services;
using HyprStart.Core.Input;
using HyprStart.Core.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MediaBrush = System.Windows.Media.Brush;

namespace HyprStart.App;

/// <summary>
/// Компактное окно настроек в стиле лаунчера: MINI AI (любой OpenAI-совместимый
/// провайдер), хоткей вызова, тема, автозапуск и город для погоды.
/// Окно ничего не знает про лаунчер: оно правит настройки и поднимает <see cref="Saved"/>,
/// а применяет хоткей и режим чата уже MainWindow.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly ThemeService _themes;

    /// <summary>Тема, выбранная в окне: применяется сразу, чтобы её было видно.</summary>
    private string _pendingTheme;

    /// <summary>Текущая комбинация хоткея, захваченная в поле.</summary>
    private HotkeySpec _hotkey;

    /// <summary>Ключ показывается открытым текстом только по кнопке-глазу.</summary>
    private bool _keyRevealed;

    /// <summary>Язык, выбранный в окне: применяется сразу, чтобы редактор было видно.</summary>
    private string _pendingLanguage;

    public SettingsWindow(SettingsStore store, ThemeService themes)
    {
        _store = store;
        _themes = themes;
        _pendingTheme = store.Current.Theme;

        InitializeComponent();

        HotkeyBox.Content = store.Current.Hotkey;
        _hotkey = HotkeySpec.TryParse(store.Current.Hotkey, out var parsed) ? parsed : HotkeySpec.AltSpace;

        BaseUrlBox.Text = string.IsNullOrWhiteSpace(store.Current.ChatBaseUrl)
            ? ChatEndpoint.DefaultBaseUrl
            : store.Current.ChatBaseUrl;
        BaseUrlBox.ToolTip = Localizer.T("SettingsBaseUrlToolTip");

        ApiKeyBox.Text = store.Current.ChatApiKey;
        _keyRevealed = false;
        MaskKey(_keyRevealed);
        KeyHint.Text = DescribeKey(store.Current.ChatApiKey);

        ModelBox.Text = string.IsNullOrWhiteSpace(store.Current.ChatModel)
            ? ChatEndpoint.DefaultModel
            : store.Current.ChatModel;
        ModelBox.ToolTip = Localizer.T("SettingsModelToolTip");

        HideChatBox.IsChecked = store.Current.HideChatPanel;

        var weather = store.Current.Weather ??= new WeatherLocationSettings();
        GeoHint.Text = weather.HasCoordinates
            ? Localizer.Format("SettingsGeoCached", weather.Describe(Localizer.T("WeatherFallbackLabel")))
            : Localizer.T("SettingsGeoUnknown");

        CityBox.Text = weather.City;
        LatBox.Text = weather.HasCoordinates
            ? weather.Latitude.ToString("0.####", CultureInfo.InvariantCulture)
            : string.Empty;
        LonBox.Text = weather.HasCoordinates
            ? weather.Longitude.ToString("0.####", CultureInfo.InvariantCulture)
            : string.Empty;

        ThemeBox.Items.Add(ThemeService.AutoName);
        foreach (var theme in _themes.Themes.Select(t => t.Name))
            ThemeBox.Items.Add(theme);

        var selected = ThemeBox.Items
            .OfType<string>()
            .FirstOrDefault(n => string.Equals(n, _pendingTheme, StringComparison.OrdinalIgnoreCase));

        ThemeBox.SelectedItem = selected ?? ThemeBox.Items[0];
        ThemeBox.SelectionChanged += OnThemeChanged;

        // Язык: пустая строка в настройках - автоопределение по культуре ОС.
        foreach (var tag in Localizer.Supported) LanguageBox.Items.Add(Localizer.DisplayName(tag));
        _pendingLanguage = Localizer.Normalize(store.Current.Language);
        SelectLanguage(_pendingLanguage);
        LanguageBox.SelectionChanged += OnLanguageChanged;

        try
        {
            AutostartBox.IsChecked = AutostartService.IsEnabled();
        }
        catch (Exception ex)
        {
            AppLog.Error("settings: чтение автозапуска не удалось", ex);
        }

        Loaded += (_, _) => BaseUrlBox.Focus();
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    /// <summary>Настройки сохранены: окно просит применить их к лаунчеру.</summary>
    public event Action? Saved;

    /// <summary>Активная пауза глобального хоткея на время записи комбинации.</summary>
    private IDisposable? _hotkeyPause;
    private IDisposable? _hookPause;

    /// <summary>HWND окна настроек: через него снимается WndProc-хук в OnClosed.</summary>
    private IntPtr _hwnd;

    private static string DescribeKey(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            return key.Length <= 8
                ? Localizer.T("SettingsKeySet")
                : Localizer.Format("SettingsKeySetShort", key[..4], key[^4..]);
        }

        return Localizer.T("SettingsKeyMissing");
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Пока идёт запись, клавиши обрабатывает WndProc: сюда событие не должно
        // доходить, иначе Esc закрывал бы окно, а Enter сохранял настройки.
        if (HotkeyBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelAndClose();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SaveAndClose();
        }
    }

    /// <summary>
    /// Клик в любую точку поля ставит клавиатурный фокус на кнопку.
    /// Раньше фокус уходил во внутренний TextBoxView, и нажатия не доходили.
    /// </summary>
    private void OnHotkeyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HotkeyBox.IsKeyboardFocusWithin) return;

        HotkeyBox.Focus();
        Keyboard.Focus(HotkeyBox);
        e.Handled = true;
    }

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Content = Localizer.T("SettingsHotkeyCapturePrompt");
        HotkeyBox.Foreground = (MediaBrush)FindResource("AccentBrush");

        // Старая комбинация отпускается ОС: иначе её нажатие во время записи
        // сворачивало лаунчер вместо того, чтобы попасть в поле.
        _hotkeyPause ??= HotkeyService.Pause();
        _hookPause ??= KeyboardHookService.Pause();
        AppLog.Write("settings: захват комбинации начат — глобальный хоткей и хук приостановлены");
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Content = _hotkey.Describe();
        HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");

        _hotkeyPause?.Dispose();
        _hotkeyPause = null;
        _hookPause?.Dispose();
        _hookPause = null;
        AppLog.Write("settings: захват комбинации завершён — глобальный хоткей и хук возвращены");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Клавиши ловим на уровне окна: WPF не даёт надёжно поймать Alt+<клавиша>
        // в PreviewKeyDown (Alt съедается системным меню), а WM_SYSKEYDOWN приходит всегда.
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(SettingsWndProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(SettingsWndProc);

        _hwnd = IntPtr.Zero;
        base.OnClosed(e);
    }

    /// <summary>
    /// Пока фокус в поле хоткея, все WM_KEYDOWN/WM_SYSKEYDOWN потребляются здесь:
    /// модификаторы читаем через GetAsyncKeyState (WPF в этот момент ещё не обновил
    /// Keyboard.Modifiers для только что нажатой клавиши), клавишу — из wParam.
    /// </summary>
    private IntPtr SettingsWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN) return IntPtr.Zero;
        if (!HotkeyBox.IsKeyboardFocused) return IntPtr.Zero;

        // Пока поле в фокусе, окно не должно видеть ни одной клавиши:
        // Alt иначе открывает системное меню, Esc закрывает окно, Enter сохраняет.
        handled = true;

        int vk = unchecked((int)wParam.ToInt64());
        RecordHotkey(vk);
        return IntPtr.Zero;
    }

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    /// <summary>Разбирает нажатие в комбинацию и обновляет поле.</summary>
    private void RecordHotkey(int vk)
    {
        // Сами модификаторы не считаем: ждём обычную клавишу вместе с ними.
        if (vk is VkShift or VkControl or VkMenu or VkLWin or VkRWin)
        {
            AppLog.Write($"settings: модификатор vk=0x{vk:X2} — ждём основную клавишу");
            return;
        }

        if (vk == 0x1B) // VK_ESCAPE — отмена записи, а не закрытие окна.
        {
            HotkeyBox.Content = _hotkey.Describe();
            AppLog.Write("settings: запись комбинации отменена по Esc");
            EndHotkeyCapture();
            return;
        }

        uint mods = 0;
        if (IsDown(VkMenu)) mods |= HotkeySpec.ModAlt;
        if (IsDown(VkControl)) mods |= HotkeySpec.ModControl;
        if (IsDown(VkShift)) mods |= HotkeySpec.ModShift;
        if (IsDown(VkLWin) || IsDown(VkRWin)) mods |= HotkeySpec.ModWin;

        // Глобальный хоткей без модификатора перехватил бы всю клавиатуру системы.
        if (mods == 0)
        {
            HotkeyBox.Content = Localizer.T("SettingsHotkeyNeedModifier");
            return;
        }

        var built = HotkeySpec.FromKey(mods, (uint)vk);
        if (built is null)
        {
            HotkeyBox.Content = Localizer.T("SettingsHotkeyUnsupported");
            return;
        }

        _hotkey = built.Value;
        HotkeyBox.Content = _hotkey.Describe();
        HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
        AppLog.Write($"settings: распознана комбинация {_hotkey.Describe()} (mods=0x{mods:X2} vk=0x{vk:X2})");

        // Комбинация записана — запись закончена, глобальный хоткей возвращается.
        EndHotkeyCapture();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Завершает запись: снимает фокус с поля и возвращает глобальный хоткей.</summary>
    private void EndHotkeyCapture()
    {
        _hotkeyPause?.Dispose();
        _hotkeyPause = null;

        if (HotkeyBox.IsKeyboardFocusWithin)
            Keyboard.ClearFocus();
    }

    /// <summary>Модификаторы WPF -&gt; маска HotkeySpec (те же биты, что ждёт RegisterHotKey).</summary>
    private static uint CurrentModifiers()
    {
        var mods = 0u;
        var active = Keyboard.Modifiers;
        if (active.HasFlag(ModifierKeys.Alt)) mods |= HotkeySpec.ModAlt;
        if (active.HasFlag(ModifierKeys.Control)) mods |= HotkeySpec.ModControl;
        if (active.HasFlag(ModifierKeys.Shift)) mods |= HotkeySpec.ModShift;
        if (active.HasFlag(ModifierKeys.Windows)) mods |= HotkeySpec.ModWin;
        return mods;
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is not string name || string.IsNullOrWhiteSpace(name)) return;

        _pendingTheme = name;
        if (!_themes.ApplyByName(name))
        {
            AppLog.Write($"settings: тема '{name}' не найдена в themes/*.json");
        }
    }

    /// <summary>Смена языка: сразу переключаем словарь, чтобы окно перерисовалось на месте.</summary>
    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not string label) return;

        var tag = Localizer.TagForLabel(label);
        if (string.IsNullOrEmpty(tag) || tag == _pendingLanguage) return;

        _pendingLanguage = tag;
        Localizer.Apply(tag);
    }

    /// <summary>Выставляет в списке пункт, соответствующий тегу языка.</summary>
    private void SelectLanguage(string tag)
    {
        var label = Localizer.DisplayName(tag);
        foreach (var item in LanguageBox.Items)
        {
            if (item is string s && s == label)
            {
                LanguageBox.SelectedItem = s;
                return;
            }
        }

        if (LanguageBox.Items.Count > 0) LanguageBox.SelectedIndex = 0;
    }

    private void OnRevealKey(object sender, RoutedEventArgs e)
    {
        _keyRevealed = !_keyRevealed;
        MaskKey(_keyRevealed);
        ApiKeyBox.CaretIndex = ApiKeyBox.Text.Length;
    }

    /// <summary>
    /// Переключает показ ключа: поле остаётся TextBox (PasswordBox не умеет
    /// показывать значение без замены контрола), поэтому меняем только подсказку.
    /// </summary>
    private void MaskKey(bool reveal)
    {
        ApiKeyBox.ToolTip = reveal
            ? Localizer.T("SettingsKeyShown")
            : Localizer.T("SettingsKeyHidden");
    }

    private void OnDetectCity(object sender, RoutedEventArgs e)
    {
        DetectButton.IsEnabled = false;
        GeoHint.Text = Localizer.T("SettingsGeoDetecting");

        try
        {
            var point = GeoLocator.Detect();
            if (point is null)
            {
                GeoHint.Text = Localizer.T("SettingsGeoOffline");
                return;
            }

            CityBox.Text = point.City;
            LatBox.Text = point.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
            LonBox.Text = point.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
            GeoHint.Text = Localizer.Format("SettingsGeoDetected", point.City);
        }
        catch (Exception ex)
        {
            AppLog.Error("settings: геолокация не удалась", ex);
            GeoHint.Text = Localizer.T("SettingsGeoFailed");
        }
        finally
        {
            DetectButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveAndClose();

    private void OnClose(object sender, RoutedEventArgs e) => CancelAndClose();

    /// <summary>Окно без системной шапки: таскаем его за заголовок вручную.</summary>
    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Кнопку отпустили до старта перетаскивания - ничего страшного.
        }
    }

    private void SaveAndClose()
    {
        var settings = _store.Current;

        var baseUrl = ChatEndpoint.NormalizeBaseUrl(BaseUrlBox.Text);
        settings.ChatBaseUrl = baseUrl.Length == 0 ? ChatEndpoint.DefaultBaseUrl : baseUrl;
        settings.ChatApiKey = ApiKeyBox.Text.Trim();
        settings.ChatModel = string.IsNullOrWhiteSpace(ModelBox.Text)
            ? ChatEndpoint.DefaultModel
            : ModelBox.Text.Trim();
        settings.HideChatPanel = HideChatBox.IsChecked == true;

        settings.Hotkey = _hotkey.Describe();
        settings.Theme = string.IsNullOrWhiteSpace(_pendingTheme) ? settings.Theme : _pendingTheme;
        settings.Language = Localizer.Normalize(_pendingLanguage);

        var weather = settings.Weather ??= new WeatherLocationSettings();
        var city = CityBox.Text.Trim();
        var hasLat = TryParseCoordinate(LatBox.Text, out var lat);
        var hasLon = TryParseCoordinate(LonBox.Text, out var lon);

        if (city.Length > 0) weather.City = city;

        if (hasLat && hasLon && ChatEndpoint.IsValidCoordinate(lat, lon))
        {
            weather.Latitude = lat;
            weather.Longitude = lon;

            // Ручной ввод важнее автогеолокации при следующем старте.
            weather.Manual = true;
        }

        _store.Save();

        try
        {
            AutostartService.Set(AutostartBox.IsChecked == true);
        }
        catch (Exception ex)
        {
            AppLog.Error("settings: автозапуск переключить не удалось", ex);
        }

        AppLog.Write(
            $"settings: сохранены (base={settings.ChatBaseUrl}, model={settings.ChatModel}, " +
            $"hotkey={settings.Hotkey}, theme={settings.Theme}, чат скрыт={settings.HideChatPanel}, " +
            $"погода={weather.Describe(Localizer.T("WeatherFallbackLabel"))})");

        Saved?.Invoke();
        Close();
    }

    private static bool TryParseCoordinate(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Разделитель может быть запятой - в русской локали её и ждут.
        return double.TryParse(
            text.Trim().Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private void CancelAndClose()
    {
        // Тему, не сохранённую, но уже применённую вживую, возвращаем назад.
        _themes.ApplyByName(_store.Current.Theme);
        Close();
    }
}
