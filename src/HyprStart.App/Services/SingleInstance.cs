using System.Threading;

namespace HyprStart.App.Services;

/// <summary>
/// Один экземпляр приложения. Второй запуск не стартует второй хук,
/// а просто просит уже работающий экземпляр показать оверлей.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\HyprStart.SingleInstance";
    private const string EventName = @"Local\HyprStart.ShowOverlay";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private Thread? _listener;
    private volatile bool _running;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirst = createdNew;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        if (IsFirst)
        {
            _running = true;
            _listener = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "HyprStart.SingleInstanceListener",
            };
            _listener.Start();
        }
    }

    public bool IsFirst { get; }

    public event Action? ShowRequested;

    public void Signal()
    {
        try
        {
            _showEvent.Set();
        }
        catch (Exception ex)
        {
            AppLog.Error("single-instance: не удалось подать сигнал", ex);
        }
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                if (!_showEvent.WaitOne(500)) continue;
                ShowRequested?.Invoke();
            }
            catch (Exception ex)
            {
                AppLog.Error("single-instance: ошибка ожидания", ex);
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _listener?.Join(300);
        _showEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // ignore
        }
        _mutex.Dispose();
    }
}
