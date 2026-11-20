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

// окно настроек, горячие клавиши, перетаскивание в закреплённое
public partial class MainWindow : Window
{
    // ---------- окно настроек ----------

    /// <summary>
    /// Открывает компактное окно настроек. Второй Ctrl+, просто поднимает уже
    /// открытое окно: два экземпляра правили бы один settings.json наперегонки.
    /// </summary>
    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // Пока открыто окно настроек, оверлей не должен скрываться по Deactivated,
        // иначе клик по шестерёнке сворачивал весь лаунчер.
        var suppress = OverlayController.SuppressAutoHide();

        SettingsWindow window;
        try
        {
            window = new SettingsWindow(_settings, _themes) { Owner = this };
        }
        catch
        {
            // XAML-ошибка в настройках не должна убивать процесс: логируем и живём дальше.
            suppress.Dispose();
            AppLog.Write("settings: не удалось создать окно настроек");
            throw;
        }

        window.Saved += OnSettingsSaved;
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            suppress.Dispose();

            // Возвращаем фокус панели: без Activate() клавиатура остаётся у закрытого окна,
            // а SearchBox.Focus() на скрытом оверлее только сбивает прицел.
            if (IsVisible)
            {
                Activate();
                SearchBox.Focus();
            }
        };

        _settingsWindow = window;

        try
        {
            window.Show();
        }
        catch
        {
            // если Show() упал, окно не откроется и Closed не придёт — снимаем подавление вручную
            _settingsWindow = null;
            suppress.Dispose();
            AppLog.Write("settings: Show() завершился ошибкой");
            throw;
        }
    }

    /// <summary>
    /// Применяет сохранённые настройки: глобальный хоткей, видимость панели MINI AI
    /// и подсказку в чате, если ключ так и не задали.
    /// </summary>
    private void OnSettingsSaved()
    {
        ApplyHotkey();
        ApplyChatVisibility();
        UpdateChatSetupHint();

        AppLog.Write(
            $"settings: применены (hotkey={HotkeyName}, чат скрыт={_settings.Current.HideChatPanel}, " +
            $"base={_settings.Current.ChatBaseUrl}, model={_settings.Current.ChatModel})");
    }

    /// <summary>Перерегистрирует глобальный хоткей по текущим настройкам.</summary>
    private void ApplyHotkey()
    {
        if (_hotkey is null) return;

        var spec = HotkeySpec.TryParse(_settings.Current.Hotkey, out var parsed) ? parsed : HotkeySpec.AltSpace;
        bool ok = _hotkey.Apply(spec);
        AppLog.Write(ok
            ? $"hotkey: зарегистрирован {spec.Describe()}"
            : $"hotkey: {spec.Describe()} занят другим приложением");
    }

    /// <summary>Скрывает карточку MINI AI, если так решили в настройках.</summary>
    private void ApplyChatVisibility()
    {
        PanelChat.Visibility = _settings.Current.HideChatPanel ? Visibility.Collapsed : Visibility.Visible;

        // Флаг влияет не только на видимость: при скрытой панели MINI AI «Закреплено»
        // забирает всю правую колонку. Без пересчёта геометрии панель оставалась узкой
        // (и сетка плиток — с прежним числом колонок) до следующего изменения размера окна.
        ApplyGeometry();
        if (_lastLayout is { } layout) FitPinTiles(layout.Pinned, _settings.Current.HideChatPanel);
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

        // Ctrl+, - окно настроек (стандарт для приложений); работает из любого поля окна.
        if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenSettings();
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
}
