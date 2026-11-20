using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

// мини-чат MINI AI
public partial class MainWindow : Window
{
    // ---------- мини-чат MINI AI ----------

    /// <summary>Клиент создаётся на лету из настроек: ключ мог появиться в env уже после старта.</summary>
    private QuickChatClient CreateChatClient()
    {
        var settings = _settings.Current;
        var options = new QuickChatOptions
        {
            // Только ключ из настроек: системная переменная DEEPSEEK_API_KEY может
            // принадлежать другому агенту, и подхватывать её молча нельзя.
            ApiKey = QuickChatOptions.ResolveApiKey(settings.ChatApiKey, readEnv: _ => null),
            Model = string.IsNullOrWhiteSpace(settings.ChatModel) ? ChatEndpoint.DefaultModel : settings.ChatModel,
            BaseUrl = settings.ChatBaseUrl,
            ProxyUrl = settings.ChatProxy ?? string.Empty,
        };

        return new QuickChatClient(options);
    }

    private void InitChat()
    {
        ChatMessages.ItemsSource = _chatLines;
        UpdateChatPlaceholder();

        // Чат стартует чистым: лента пуста, подсказку <спроси ИИ:> рисует XAML.
        // Если ключа нет - мягкая подсказка вместо будущей ошибки запроса.
        UpdateChatSetupHint();
    }

    /// <summary>
    /// Подсказка в чате вместо красной ошибки: без ключа панель MINI AI бесполезна,
    /// но пользователь должен видеть, ЧТО именно поправить (Base URL / Key / Model).
    /// </summary>
    private void UpdateChatSetupHint()
    {
        // readEnv: _ => null - ключ считаем заданным только из настроек, без системной переменной.
        bool configured = ChatEndpoint.IsConfigured(_settings.Current.ChatApiKey, readEnv: _ => null);

        if (configured)
        {
            // Ключ появился: старую подсказку убираем, чтобы не пугала.
            for (int i = _chatLines.Count - 1; i >= 0; i--)
            {
                if (_chatLines[i].IsSetupHint) _chatLines.RemoveAt(i);
            }

            return;
        }

        foreach (var line in _chatLines)
        {
            if (line.IsSetupHint) return;
        }

        var hint = new ChatLineVm(
            ChatRole.System,
            string.Format(Localizer.T("ChatSetupHint"), ChatEndpoint.EnvKeyName),
            isNotice: true);
        hint.IsSetupHint = true;

        _chatLines.Add(hint);
        ChatHint.Visibility = Visibility.Collapsed;
        ScrollChatToEnd();
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
            // Не тупик, а приглашение настроить: открываем окно настроек сразу.
            AppendChatLine(ChatRole.System, Localizer.T("ChatNoKeyOpensSettings"), notice: true);
            UpdateChatSetupHint();
            OpenSettings();
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
                reply.Text = Localizer.T("ChatWeatherUnavailable");
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
            reply.Text = Localizer.T("ChatErrorPrefix") + ex.Message;
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
        // При скрытой панели MINI AI «Закреплено» забирает всю правую колонку:
        // флаг уходит в расчёт, а не просто прячет PanelChat.
        bool hideChat = _settings.Current.HideChatPanel;
        var result = LayoutCalculator.Compute(Width, Height, spec, hideChat);
        _lastLayout = result;

        Place(PanelApps, result.Apps);
        Place(PanelPinned, result.Pinned);
        Place(PanelChat, result.Chat);
        Place(PanelFrequent, result.Frequent);
        Place(PanelQuick, result.Quick);
        Place(Polybar, result.Polybar);

        FitPinTiles(result.Pinned, hideChat);

        AppLog.Write($"layout: apps={result.Apps} pinned={result.Pinned} chat={result.Chat} " +
                     $"frequent={result.Frequent} quick={result.Quick} polybar={result.Polybar} " +
                     $"hideChat={hideChat}");
    }

    /// <summary>
    /// Подгоняет плитки закреплённого под ширину карточки: сетка 4x2 должна
    /// влезать целиком (8 плиток = бейджи Alt+1..9), прокрутка — только запасной вариант.
    /// </summary>
    private void FitPinTiles(PixelRect pinned, bool hideChat)
    {
        // Внутренние отступы PanelStyle: рамка + padding заголовка и контента.
        const double chrome = 34;

        double inner = pinned.Width - chrome;
        if (inner <= 0) return;

        // Колонки адаптивны к ширине панели: при скрытом мини-чате панель почти вдвое
        // шире, и сетка добавляет колонки вместо того, чтобы растягивать плитки.
        int columns = PinTileGeometry.ColumnCount(inner);
        // Сетка цепляется к DependencyProperty через Binding: до первого рендера
        // UniformGrid ещё нет в визуальном дереве, и поиск по нему возвращал null,
        // оставляя жёсткие 5 колонок даже на расширенной панели.
        if (PinColumns != columns) PinColumns = columns;

        double tile = PinTileGeometry.TileWidth(inner, columns);
        if (PinsList.ItemsSource is IReadOnlyList<PinVm> pins)
        {
            foreach (var pin in pins) pin.FitTo(inner, columns);
        }

        // Папка — отдельная сетка той же ширины, значит и колонки те же.
        if (FolderList.ItemsSource is IReadOnlyList<PinVm> folderPins)
        {
            foreach (var pin in folderPins) pin.FitTo(inner, columns);
        }

        double gridHeight = PinTileGeometry.GridHeight(PinTileGeometry.TileHeight(tile));
        PinsScroll.VerticalScrollBarVisibility = PinTileGeometry.FitsWithoutScroll(inner, pinned.Height - 56, columns)
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;

        AppLog.Write($"pins: inner={inner:0} cols={columns} tile={tile:0.#}" +
                     $"x{PinTileGeometry.TileHeight(tile):0.#} grid={gridHeight:0} hideChat={hideChat}");

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

    /// <summary>
    /// Язык переключили в настройках: перечитываем строки, которые мы ставим из кода
    /// (заголовки панелей, роли в чате). XAML-разметка подхватывает DynamicResource сама.
    /// </summary>
    private void OnLanguageChanged()
    {
        PanelApps.Header = TextFx.Spaced(Localizer.T("PanelAppsHeader"))
            + (_entries.Count > 0 ? "   " + _entries.Count : string.Empty);
        PanelPinned.Header = TextFx.Spaced(Localizer.T("PanelPinnedHeader"))
            + (_pins.Items.Count > 0 ? "   " + _pins.Items.Count : string.Empty);
        PanelFrequent.Header = TextFx.Spaced(Localizer.T("PanelFrequentHeader"))
            + (_pins.Items.Count > 0 && FrequentList.Items.Count > 0
                ? "   " + FrequentList.Items.Count
                : string.Empty);
        PanelQuick.Header = TextFx.Spaced(Localizer.T("PanelQuickHeader"));
        PanelChat.Header = TextFx.Spaced(Localizer.T("PanelChatHeader"));

        // Подпись роли у уже отрисованных реплик чата.
        ChatMessages.Items.Refresh();
        UpdateChatSetupHint();
    }
}
