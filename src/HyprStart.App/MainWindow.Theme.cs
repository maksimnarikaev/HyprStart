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

// polybar, рабочий цикл виджета, cava-визуализация
public partial class MainWindow : Window
{
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

        // Ленивая инициализация медиа-событий: только когда оверлей реально показан.
        // WinRT (Windows.Media.Control) грузит Microsoft.Windows.SDK.NET.dll (~54 МБ),
        // поэтому до первого показа к нему не прикасаемся.
        if (!_trackHooked)
        {
            _trackHooked = true;
            _ = _monitor.HookTrackEventsAsync();
        }

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

        // Окно настроек живёт отдельным top-level окном и не наследует скрытие оверлея,
        // поэтому закрываем его руками — иначе оно висит поверх всех приложений.
        CloseSettings();

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
}
