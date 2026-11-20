using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public SettingsWindow(SettingsStore store, ThemeService themes)
    {
        _store = store;
        _themes = themes;
        _pendingTheme = store.Current.Theme;

        InitializeComponent();

        HotkeyBox.Text = store.Current.Hotkey;
        _hotkey = HotkeySpec.TryParse(store.Current.Hotkey, out var parsed) ? parsed : HotkeySpec.AltSpace;

        BaseUrlBox.Text = string.IsNullOrWhiteSpace(store.Current.ChatBaseUrl)
            ? ChatEndpoint.DefaultBaseUrl
            : store.Current.ChatBaseUrl;
        BaseUrlBox.ToolTip = "Например https://api.deepseek.com, https://openrouter.ai/api/v1, http://localhost:11434/v1";

        ApiKeyBox.Text = store.Current.ChatApiKey;
        _keyRevealed = false;
        MaskKey(_keyRevealed);
        KeyHint.Text = DescribeKey(store.Current.ChatApiKey);

        ModelBox.Text = string.IsNullOrWhiteSpace(store.Current.ChatModel)
            ? ChatEndpoint.DefaultModel
            : store.Current.ChatModel;
        ModelBox.ToolTip = "deepseek-chat, gpt-4o-mini, llama3, ... - зависит от провайдера";

        HideChatBox.IsChecked = store.Current.HideChatPanel;

        var weather = store.Current.Weather ??= new WeatherLocationSettings();
        GeoHint.Text = weather.HasCoordinates
            ? $"сейчас: {weather.Describe()}"
            : "город ещё не определён - нажмите кнопку определения по IP";

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

    private static string DescribeKey(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            return key.Length <= 8
                ? "Ключ задан."
                : $"Ключ задан ({key[..4]}…{key[^4..]}).";
        }

        return "Ключ не задан - панель MINI AI покажет подсказку вместо ошибки.";
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !HotkeyBox.IsFocused)
        {
            e.Handled = true;
            CancelAndClose();
            return;
        }

        if (e.Key == Key.Enter && !HotkeyBox.IsFocused)
        {
            e.Handled = true;
            SaveAndClose();
        }
    }

    private void OnHotkeyFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "нажмите комбинацию…";
        HotkeyBox.Foreground = (MediaBrush)FindResource("AccentBrush");
    }

    private void OnHotkeyBlur(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = _hotkey.Describe();
        HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
    }

    /// <summary>Захват комбинации: ждём модификатор + клавишу, Esc отменяет захват.</summary>
    private void OnHotkeyPreview(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            HotkeyBox.Text = _hotkey.Describe();
            Keyboard.ClearFocus();
            return;
        }

        // Сами модификаторы не считаем: ждём обычную клавишу вместе с ними.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.None or Key.DeadCharProcessed)
        {
            return;
        }

        var mods = CurrentModifiers();

        // Глобальный хоткей без модификатора перехватил бы всю клавиатуру системы.
        if (mods == 0)
        {
            HotkeyBox.Text = "нужен модификатор";
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var built = HotkeySpec.FromKey(mods, vk);
        if (built is null)
        {
            HotkeyBox.Text = "клавиша не подходит";
            return;
        }

        _hotkey = built.Value;
        HotkeyBox.Text = _hotkey.Describe();
        HotkeyBox.Foreground = (MediaBrush)FindResource("TextBrush");
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
            ? "Ключ виден. Нажмите кнопку-глаз, чтобы скрыть."
            : "Ключ хранится в settings.json и уходит только провайдеру из Base URL.";
    }

    private void OnDetectCity(object sender, RoutedEventArgs e)
    {
        DetectButton.IsEnabled = false;
        GeoHint.Text = "определяю по IP…";

        try
        {
            var point = GeoLocator.Detect();
            if (point is null)
            {
                GeoHint.Text = "не удалось определить (нет сети) - введите город и координаты вручную";
                return;
            }

            CityBox.Text = point.City;
            LatBox.Text = point.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
            LonBox.Text = point.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
            GeoHint.Text = $"определено: {point.City} - нажмите Сохранить";
        }
        catch (Exception ex)
        {
            AppLog.Error("settings: геолокация не удалась", ex);
            GeoHint.Text = "ошибка определения - введите координаты вручную";
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
            $"погода={weather.Describe()})");

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
