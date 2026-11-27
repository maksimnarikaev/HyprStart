using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    /// <summary>Символ маски: ключ по умолчанию показан точками.</summary>
    private const char KeyMaskChar = '\u2022';

    /// <summary>
    /// Реальный ключ. Поле ApiKeyBox по умолчанию показывает только точки, поэтому
    /// значение живёт здесь: пользователь может нажать «Сохранить», не трогая поле,
    /// и сохранённый ключ не превратится в маску.
    /// </summary>
    private string _apiKeyValue = string.Empty;

    /// <summary>Ключ показывается открытым текстом только по кнопке-глазу.</summary>
    private bool _keyRevealed;

    /// <summary>Защита от рекурсии: подмена текста в маске не считается правкой.</summary>
    private bool _keyMaskSuppressed;

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

        _apiKeyValue = store.Current.ChatApiKey ?? string.Empty;
        _keyRevealed = false;
        ApplyKeyMask();
        KeyHint.Text = DescribeKey(_apiKeyValue);

        ModelBox.Text = string.IsNullOrWhiteSpace(store.Current.ChatModel)
            ? ChatEndpoint.DefaultModel
            : store.Current.ChatModel;
        ModelBox.ToolTip = Localizer.T("SettingsModelToolTip");

        HideChatBox.IsChecked = store.Current.HideChatPanel;
        PanelGridBox.IsChecked = store.Current.ShowPanelGrid;

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

    /// <summary>Отложенная проверка после потери активации (см. OnDeactivated).</summary>
    private DispatcherTimer? _captureRecheck;

    /// Режим прямой записи в WH_KEYBOARD_LL: пока он активен, хук глушит
    /// модификаторы и основную клавишу, поэтому Alt не открывает меню,
    /// а окно настроек не теряет активацию.
    private IDisposable? _captureScope;

    /// <summary>
    /// Идёт запись комбинации. Флаг держим сами и не завязываем на фокус:
    /// пока launcher-оверлей (Topmost) открыт поверх настроек, WM_SYSKEYDOWN
    /// может достаться другому окну того же потока, и окно настроек о нажатии
    /// не узнает — ни через PreviewKeyDown, ни через хук своего HWND.
    /// </summary>
    private bool _isCapturingHotkey;

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
        // Пока идёт запись, окно не реагирует ни на Esc, ни на Enter: эти клавиши
        // принадлежат комбинации, а не окну. Флаг, а не фокус — см. _isCapturingHotkey.
        if (_isCapturingHotkey)
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
        // Повторный клик по полю, где уже идёт запись, — отмена.
        if (_isCapturingHotkey && HotkeyBox.IsKeyboardFocusWithin)
        {
            HotkeyBox.Content = _hotkey.Describe();
            HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
            AppLog.Write("settings: запись комбинации отменена повторным кликом");
            EndHotkeyCapture();
            e.Handled = true;
            return;
        }

        BeginHotkeyCapture();

        if (!HotkeyBox.IsKeyboardFocusWithin)
        {
            HotkeyBox.Focus();
            Keyboard.Focus(HotkeyBox);
        }

        e.Handled = true;
    }

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e) => BeginHotkeyCapture();

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Поле теряет фокус — запись заканчиваем и возвращаем обычный вид.
        HotkeyBox.Content = _hotkey.Describe();
        HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
        EndHotkeyCapture();
    }

    /// <summary>Включает запись (повторный вызов безопасен) и снимает хоткей с регистрации.</summary>
    private void BeginHotkeyCapture()
    {
        HotkeyBox.Content = Localizer.T("SettingsHotkeyCapturePrompt");
        HotkeyBox.Foreground = (MediaBrush)FindResource("AccentBrush");

        if (_isCapturingHotkey) return;
        _isCapturingHotkey = true;

        // Старая комбинация отпускается ОС: иначе её нажатие во время записи
        // сворачивало лаунчер вместо того, чтобы попасть в поле.
        _hotkeyPause ??= HotkeyService.Pause();
        _hookPause ??= KeyboardHookService.Pause();
        _captureScope ??= KeyboardHookService.BeginCapture(OnHookCaptured);
        AppLog.Write("settings: захват комбинации начат — глобальный хоткей снят, клавиши читает WH_KEYBOARD_LL");
    }

    protected override void OnClosed(EventArgs e)
    {
        EndHotkeyCapture();
        base.OnClosed(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_isCapturingHotkey) return;

        // Запись глушит клавиатуру всей системы, поэтому уход в другое окно её
        // прекращает. Но не в момент нажатия Alt: модификатор — часть комбинации,
        // и раньше именно здесь Alt+Q обрывался до второй клавиши.
        RecheckCaptureAfterDeactivation();
    }

    /// <summary>
    /// Отложенная проверка после потери активации: пока модификатор зажат, запись
    /// продолжается (это начало комбинации); как только клавиши отпущены, а окно
    /// так и не вернулось в фокус — запись прекращается.
    /// </summary>
    private void RecheckCaptureAfterDeactivation()
    {
        if (_captureRecheck is null)
        {
            _captureRecheck = new DispatcherTimer(
                TimeSpan.FromMilliseconds(120),
                DispatcherPriority.Background,
                (_, _) => OnCaptureRecheckTick(),
                Dispatcher);
        }

        _captureRecheck.Start();
    }

    private void OnCaptureRecheckTick()
    {
        if (!_isCapturingHotkey || IsActive)
        {
            _captureRecheck?.Stop();
            return;
        }

        // Модификатор всё ещё зажат — это набор комбинации, а не уход из окна.
        if (CurrentModifiers() != 0) return;

        _captureRecheck?.Stop();
        AppLog.Write("settings: окно потеряло активацию — запись комбинации прекращена");
        EndHotkeyCapture();
    }

    /// <summary>
    /// Пришла основная клавиша из WH_KEYBOARD_LL. Модификаторы считает сам хук
    /// по событиям, поэтому Alt+Q распознаётся уже на первом нажатии.
    /// Возвращает true, если комбинация принята.
    /// </summary>
    private bool OnHookCaptured(uint mods, uint vk)
    {
        if (!_isCapturingHotkey) return false;
        return RecordHotkey(mods, (int)vk);
    }

    private const int VkEscape = 0x1B;

    /// <summary>Разбирает нажатие в комбинацию и обновляет поле.</summary>
    private bool RecordHotkey(uint mods, int vk)
    {
        // Esc без модификаторов — отмена записи, а не закрытие окна.
        if (vk == VkEscape && mods == 0)
        {
            HotkeyBox.Content = _hotkey.Describe();
            HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
            AppLog.Write("settings: запись комбинации отменена по Esc");
            EndHotkeyCapture();
            return true;
        }

        // Страховка на случай, если хук не успел отследить модификатор.
        if (mods == 0) mods = CurrentModifiers();

        AppLog.Write($"settings: нажата клавиша vk=0x{vk:X2} при модификаторах 0x{mods:X2}");

        // Глобальный хоткей без модификатора перехватил бы всю клавиатуру системы.
        if (mods == 0)
        {
            HotkeyBox.Content = Localizer.T("SettingsHotkeyNeedModifier");
            return false;
        }

        var built = HotkeySpec.FromKey(mods, (uint)vk);
        if (built is null)
        {
            HotkeyBox.Content = Localizer.T("SettingsHotkeyUnsupported");
            return false;
        }

        _hotkey = built.Value;
        HotkeyBox.Content = _hotkey.Describe();
        HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
        AppLog.Write($"settings: распознана комбинация {_hotkey.Describe()} (mods=0x{mods:X2} vk=0x{vk:X2})");

        // Комбинация записана — запись закончена, глобальный хоткей возвращается.
        EndHotkeyCapture();
        return true;
    }

    /// <summary>Физически зажатые модификаторы (страховка для RecordHotkey).</summary>
    private static uint CurrentModifiers()
    {
        const int vkShift = 0x10, vkControl = 0x11, vkMenu = 0x12, vkLWin = 0x5B, vkRWin = 0x5C;

        uint mods = 0;
        if (IsDown(vkMenu)) mods |= HotkeySpec.ModAlt;
        if (IsDown(vkControl)) mods |= HotkeySpec.ModControl;
        if (IsDown(vkShift)) mods |= HotkeySpec.ModShift;
        if (IsDown(vkLWin) || IsDown(vkRWin)) mods |= HotkeySpec.ModWin;
        return mods;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Выключает запись и возвращает глобальный хоткей в регистрацию.</summary>
    private void EndHotkeyCapture()
    {
        bool wasCapturing = _isCapturingHotkey;
        _isCapturingHotkey = false;

        _captureScope?.Dispose();
        _captureScope = null;

        _hotkeyPause?.Dispose();
        _hotkeyPause = null;
        _hookPause?.Dispose();
        _hookPause = null;

        _captureRecheck?.Stop();

        if (wasCapturing)
            AppLog.Write("settings: захват комбинации завершён — глобальный хоткей и хук возвращены");
    }

    
    /// <summary>
    /// Правка поля ключа. В скрытом режиме там точки — при первой же правке выбрасываем
    /// их, иначе сохранённый ключ превратился бы в "••••••••sk-...".
    /// </summary>
    private void OnApiKeyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_keyMaskSuppressed || _keyRevealed) return;

        // Флаг снимаем только после ApplyKeyMask: она тоже пишет в Text
        // и иначе вызвала бы этот обработчик повторно.
        _keyMaskSuppressed = true;
        try
        {
            _apiKeyValue = string.Empty;
            _keyRevealed = true;
            ApiKeyBox.Text = string.Empty;
            ApplyKeyMask();
        }
        finally
        {
            _keyMaskSuppressed = false;
        }
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
        // Текст, который пользователь мог набрать в замаскированном режиме,
        // забираем в _apiKeyValue - иначе переключение показа его потеряет.
        CaptureEditedKey();

        _keyRevealed = !_keyRevealed;
        ApplyKeyMask();
        ApiKeyBox.CaretIndex = ApiKeyBox.Text.Length;
    }

    /// <summary>
    /// Показывает либо реальный ключ, либо точки. Поле остаётся TextBox
    /// (PasswordBox не умеет показывать значение без замены контрола),
    /// поэтому маска - это подмена текста, а не отдельный контрол.
    /// </summary>
    private void ApplyKeyMask()
    {
        // Assigning Text raises TextChanged; the caller may already hold the flag,
        // but OnRevealKey and the constructor call us without it.
        bool guard = _keyMaskSuppressed;
        _keyMaskSuppressed = true;
        try
        {
            ApiKeyBox.Text = _keyRevealed ? _apiKeyValue : MaskOf(_apiKeyValue);
        }
        finally
        {
            _keyMaskSuppressed = guard;
        }

        ApiKeyBox.CaretIndex = ApiKeyBox.Text.Length;
        ApiKeyBox.ToolTip = _keyRevealed
            ? Localizer.T("SettingsKeyShown")
            : Localizer.T("SettingsKeyHidden");
        KeyHint.Text = DescribeKey(_apiKeyValue);
    }

    /// <summary>
    /// Одна точка на символ ключа (не фиксированная длина): по маске видно,
    /// задан ключ вообще или нет, но не видно его длину.
    /// </summary>
    private static string MaskOf(string? key)
        => string.IsNullOrEmpty(key) ? string.Empty : new string(KeyMaskChar, key.Length);

    /// <summary>
    /// Забирает набранное в поле в <see cref="_apiKeyValue"/>, если это не маска.
    /// Пока ключ скрыт, поле содержит точки - их нельзя принять за настоящий ключ.
    /// </summary>
    private void CaptureEditedKey()
    {
        if (_keyRevealed) _apiKeyValue = ApiKeyBox.Text.Trim();
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

        // Ключ берём из _apiKeyValue, а не из поля: в скрытом режиме там точки.
        CaptureEditedKey();
        settings.ChatApiKey = _apiKeyValue.Trim();
        settings.ChatModel = string.IsNullOrWhiteSpace(ModelBox.Text)
            ? ChatEndpoint.DefaultModel
            : ModelBox.Text.Trim();
        settings.HideChatPanel = HideChatBox.IsChecked == true;
        settings.ShowPanelGrid = PanelGridBox.IsChecked == true;

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
