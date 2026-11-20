using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace HyprStart.App.Services;

/// <summary>
/// Управление видимостью оверлея.
/// Для layered-окна (AllowsTransparency) перед Hide() нужно дождаться, пока WPF
/// отправит в DWM прозрачный буфер, иначе при следующем показе мелькает старая картинка.
/// Состояние ведём своим флагом _visible: Window.IsVisible не годится как источник истины,
/// потому что окно может быть видимым с нулевой прозрачностью.
/// </summary>
internal sealed class OverlayController
{
    private const long IgnoreDeactivateMs = 250;

    /// <summary>
    /// Сколько модальных окон (настройки и т.п.) сейчас держат оверлей открытым.
    /// Пока счётчик больше нуля, Deactivated не скрывает панель: иначе открытие
    /// дочернего окна мгновенно сворачивало лаунчер.
    /// </summary>
    private static int _suppressAutoHide;

    private readonly MainWindow _window;
    private readonly Action? _sampleAutoTheme;
    private long _shownAtTick;
    private bool _prewarmed;
    private bool _visible;
    private int _generation;

    public OverlayController(MainWindow window, Action? sampleAutoTheme = null)
    {
        _window = window;
        _sampleAutoTheme = sampleAutoTheme;
        _window.Deactivated += OnDeactivated;
    }

    public bool IsVisible => _visible;
    public bool IsPrewarmed => _prewarmed;
    public MainWindow Window => _window;

    /// <summary>
    /// Подавляет автоскрытие оверлея по Deactivated, пока открыто дочернее окно.
    /// Возвращает IDisposable: освобождение снимает подавление (в т.ч. при исключении).
    /// </summary>
    public static IDisposable SuppressAutoHide()
    {
        Interlocked.Increment(ref _suppressAutoHide);
        return new SuppressScope();
    }

    private sealed class SuppressScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _suppressAutoHide);
        }
    }

    public void Prewarm()
    {
        try
        {
            _window.Opacity = 0;
            _window.Show();

            _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                _window.ResetOpenVisuals();
                _window.Hide();

                // ВАЖНО: вернуть прозрачность самого окна, иначе оно навсегда останется невидимым
                _window.Opacity = 1;
                _prewarmed = true;
                AppLog.Write("overlay: prewarm ok");
            }));
        }
        catch (Exception ex)
        {
            AppLog.Error("overlay: prewarm failed", ex);
        }
    }

    public void Toggle()
    {
        if (_visible) Hide();
        else Show();
    }

    public void Show()
    {
        if (_visible && _window.IsVisible) return;

        _generation++;
        _visible = true;

        var watch = Stopwatch.StartNew();

        // 0. тема "auto": снимок экрана ДО показа окна, иначе захватим сами себя.
        //    Один BitBlt 32×18 (<2 мс), никакого фонового опроса.
        if (_sampleAutoTheme is not null)
        {
            try
            {
                _sampleAutoTheme();
            }
            catch (Exception ex)
            {
                AppLog.Error("overlay: снимок для темы auto не удался", ex);
            }
        }

        // 1. данные + гарантированно прозрачный корень
        _window.PrepareForShow();

        // 2. показываем окно (если оно осталось видимым с прошлого раза — ничего страшного)
        _window.Opacity = 1;   // страховка от «залипшей» нулевой прозрачности окна после prewarm
        if (!_window.IsVisible) _window.Show();
        _window.Activate();
        _window.Focus();

        // 3. анимация — на следующем кадре, поверх прозрачного буфера
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(_window.PlayOpenAnimation));

        // 4. будим опрос polybar и визуализатор — только теперь они нужны
        _window.ResumeActivity();

        // 4. фокус ставим сами: иначе первые нажатия уходят «в никуда» до клика по окну
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(_window.FocusForInput));

        _shownAtTick = Environment.TickCount64;
        AppLog.Write($"overlay: shown in {watch.Elapsed.TotalMilliseconds:0.0} ms (prewarmed={_prewarmed})");
    }

    public void Hide()
    {
        if (!_visible) return;

        _visible = false;
        var generation = ++_generation;
        _ = HideCoreAsync(generation);
    }

    private async Task HideCoreAsync(int generation)
    {
        // гасим визуал и даём WPF отправить прозрачный буфер в DWM до скрытия окна
        _window.ResetOpenVisuals();
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        // пока ждали кадр, меню могли снова открыть — тогда ничего не скрываем
        if (generation != _generation || _visible) return;

        _window.Hide();

        // окно скрыто — опрос WASAPI и пересчёт визуализатора больше не нужны
        _window.SuspendActivity();
        AppLog.Write("overlay: hidden");
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_visible) return;
        // модальное окно (настройки) забрало фокус — панель должна остаться открытой
        if (Volatile.Read(ref _suppressAutoHide) > 0) return;
        if (Environment.TickCount64 - _shownAtTick < IgnoreDeactivateMs) return;
        Hide();
    }
}
