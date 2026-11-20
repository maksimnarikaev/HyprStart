using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace HyprStart.App.Services;

/// <summary>Живые данные для polybar: CPU, RAM, громкость, текущий трек и уровень звука.</summary>
internal sealed class SystemMonitor
{
    private readonly object _gate = new();
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;

    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;
    private IAudioMeterInformation? _meter;

    public int CpuPercent { get; private set; }
    public int RamPercent { get; private set; }
    public int VolumePercent { get; private set; } = -1;
    public string Track { get; private set; } = "—";

    /// <summary>Пиковый уровень выходного потока 0..1 (IAudioMeterInformation).</summary>
    public float Peak { get; private set; }

    public void SampleCpu()
    {
        if (!SysNative.GetSystemTimes(out var idle, out var kernel, out var user)) return;

        ulong idleTicks = ToUInt64(idle);
        ulong kernelTicks = ToUInt64(kernel);
        ulong userTicks = ToUInt64(user);

        if (_prevKernel != 0)
        {
            ulong idleDelta = idleTicks - _prevIdle;
            ulong totalDelta = (kernelTicks - _prevKernel) + (userTicks - _prevUser);

            if (totalDelta > 0)
            {
                CpuPercent = (int)Math.Round(100.0 * (totalDelta - idleDelta) / totalDelta);
                CpuPercent = Math.Clamp(CpuPercent, 0, 100);
            }
        }

        _prevIdle = idleTicks;
        _prevKernel = kernelTicks;
        _prevUser = userTicks;
    }

    public void SampleRam()
    {
        var status = new SysNative.MemoryStatusEx { Length = (uint)Marshal.SizeOf<SysNative.MemoryStatusEx>() };
        if (SysNative.GlobalMemoryStatusEx(ref status)) RamPercent = (int)status.MemoryLoad;
    }

    public void SampleVolume()
    {
        lock (_gate)
        {
            try
            {
                if (!EnsureAudioInterfaces()) return;
                if (_volume!.GetMasterVolumeLevelScalar(out float level) == 0)
                {
                    VolumePercent = (int)Math.Round(level * 100);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("polybar: громкость недоступна", ex);
                ResetAudioInterfaces();
            }
        }
    }

    /// <summary>Пик выходного потока — основа для cava-столбиков. Вызывается часто (60 мс).</summary>
    public void SamplePeak()
    {
        lock (_gate)
        {
            try
            {
                if (!EnsureAudioInterfaces()) return;
                if (_meter!.GetPeakValue(out float peak) == 0)
                {
                    Peak = Math.Clamp(peak, 0f, 1f);
                }
            }
            catch
            {
                ResetAudioInterfaces();
                Peak = 0;
            }
        }
    }

    /// <summary>
    /// Адресное управление плеером: находим подходящую сессию и вызываем её метод напрямую.
    /// Если GSMTC не смог — фоллбэк на системную мультимедиа-клавишу.
    /// </summary>
    public async Task<bool> ControlPlaybackAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action,
        byte fallbackVk)
    {
        try
        {
            var session = await FindSessionAsync();
            if (session is not null && await action(session)) return true;

            AppLog.Write($"media: GSMTC не сработал, фоллбэк vk=0x{fallbackVk:X2}");
            PressMediaKey(fallbackVk);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("media: команда плееру упала", ex);
            return false;
        }
    }

    private static void PressMediaKey(byte vk)
    {
        SysNative.keybd_event(vk, 0, 0, UIntPtr.Zero);
        Thread.Sleep(25);
        SysNative.keybd_event(vk, 0, 0x0002, UIntPtr.Zero);
    }

    // Менеджер GSMTC создаётся один раз: RequestAsync — самая дорогая операция
    // во всём опросе (COM-активация ~десятки мс), а вызывался он каждые 3 секунды.
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static readonly SemaphoreSlim ManagerGate = new(1, 1);

    private static async Task<GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync()
    {
        if (_manager is not null) return _manager;

        await ManagerGate.WaitAsync();
        try
        {
            return _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("media: менеджер сессий недоступен", ex);
            return null;
        }
        finally
        {
            ManagerGate.Release();
        }
    }

    /// <summary>
    /// Выбор сессии по приоритету: 1) Playing; 2) с непустым Title и доступными
    /// кнопками Play/Pause (это и есть «поставленная на паузу» Яндекс Музыка или YouTube);
    /// иначе null — вызывающий сделает фоллбэк на клавишу.
    /// </summary>
    private static async Task<GlobalSystemMediaTransportControlsSession?> FindSessionAsync()
    {
        var manager = await GetManagerAsync();
        if (manager is null) return null;
        var sessions = manager.GetSessions();

        foreach (var session in sessions)
        {
            if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return session;
            }
        }

        var candidates = new List<GlobalSystemMediaTransportControlsSession>(sessions);
        var current = manager.GetCurrentSession();
        if (current is not null && !candidates.Contains(current)) candidates.Insert(0, current);

        foreach (var session in candidates)
        {
            var info = session.GetPlaybackInfo();
            if (!info.Controls.IsPlayEnabled && !info.Controls.IsPauseEnabled) continue;

            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                if (!string.IsNullOrWhiteSpace(properties?.Title)) return session;
            }
            catch
            {
                // сессия умерла между опросом и чтением свойств — идём дальше
            }
        }

        return null;
    }

    /// <summary>
    /// Трек: сначала текущая сессия, затем любая со статусом Playing —
    /// так стабильно подхватываются Яндекс Музыка, браузеры и прочие плееры.
    /// </summary>
    public async Task SampleTrackAsync()
    {
        try
        {
            var session = await FindSessionAsync();
            if (session is null)
            {
                Track = "—";
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            Track = FormatTrack(properties?.Artist, properties?.Title);
        }
        catch (Exception ex)
        {
            Track = "—";
            AppLog.Error("polybar: трек недоступен", ex);
        }
    }

    private static string FormatTrack(string? artist, string? title)
        => string.IsNullOrWhiteSpace(title)
            ? "—"
            : string.IsNullOrWhiteSpace(artist) ? title! : $"{artist} — {title}";

    // ---------- событийный GSMTC: без периодического опроса ----------

    private GlobalSystemMediaTransportControlsSession? _trackedSession;
    private bool _eventsHooked;

    /// <summary>Срабатывает, когда трек или текущая сессия изменились (UI обновляет подпись).</summary>
    public event Action? TrackChanged;

    /// <summary>
    /// Подписка на CurrentSessionChanged / MediaPropertiesChanged кэшированного менеджера.
    /// Вызывается один раз при старте монитора; дальше тик трек не опрашивает.
    /// </summary>
    public async Task HookTrackEventsAsync()
    {
        if (_eventsHooked) return;

        var manager = await GetManagerAsync();
        if (manager is null) return;

        _eventsHooked = true;
        manager.CurrentSessionChanged += OnCurrentSessionChanged;

        AttachSession(manager.GetCurrentSession());

        // первичное заполнение: событий до подписки уже не будет
        await SampleTrackAsync();
        TrackChanged?.Invoke();
    }

    public void UnhookTrackEvents()
    {
        if (_manager is not null && _eventsHooked)
        {
            try
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
            catch (Exception ex)
            {
                AppLog.Error("media: не удалось отписаться от GSMTC", ex);
            }
        }

        AttachSession(null);
        _eventsHooked = false;
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            AttachSession(sender.GetCurrentSession());
            _ = RefreshTrackFromSessionAsync(_trackedSession);
        }
        catch (Exception ex)
        {
            AppLog.Error("media: смена сессии упала", ex);
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _ = RefreshTrackFromSessionAsync(sender);

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_trackedSession, session)) return;

        if (_trackedSession is not null)
        {
            try
            {
                _trackedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            }
            catch (Exception ex)
            {
                AppLog.Error("media: не удалось отписаться от сессии", ex);
            }
        }

        _trackedSession = session;

        if (session is not null)
        {
            try
            {
                session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            }
            catch (Exception ex)
            {
                AppLog.Error("media: не удалось подписаться на сессию", ex);
            }
        }
    }

    private async Task RefreshTrackFromSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null)
        {
            Track = "—";
            TrackChanged?.Invoke();
            return;
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            Track = FormatTrack(properties?.Artist, properties?.Title);
        }
        catch (Exception ex)
        {
            AppLog.Error("media: свойства трека недоступны", ex);
        }

        TrackChanged?.Invoke();
    }

    private bool EnsureAudioInterfaces()
    {
        if (_meter is not null && _volume is not null) return true;

        _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (_enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0 || device is null) return false;

        var volumeId = typeof(IAudioEndpointVolume).GUID;
        if (device.Activate(ref volumeId, 23, IntPtr.Zero, out var volumeObject) != 0) return false;
        _volume = volumeObject as IAudioEndpointVolume;

        var meterId = typeof(IAudioMeterInformation).GUID;
        if (device.Activate(ref meterId, 23, IntPtr.Zero, out var meterObject) != 0) return false;
        _meter = meterObject as IAudioMeterInformation;

        return _meter is not null && _volume is not null;
    }

    private void ResetAudioInterfaces()
    {
        _meter = null;
        _volume = null;
        _enumerator = null;
    }

    private static ulong ToUInt64(SysNative.FileTime time)
        => ((ulong)(uint)time.HighDateTime << 32) | (uint)time.LowDateTime;

    // ---- Core Audio ----
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
        int OpenPropertyStore(int access, out IntPtr store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int count);
        int SetMasterVolumeLevel(float level, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
        int GetMeteringChannelCount(out int count);
        int GetChannelsPeakValues(int count, [Out] float[] peaks);
        int QueryHardwareSupport(out int mask);
    }
}

internal static class SysNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public int LowDateTime;
        public int HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
