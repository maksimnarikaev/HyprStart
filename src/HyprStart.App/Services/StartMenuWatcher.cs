using System.IO;
using System.Windows.Threading;

namespace HyprStart.App.Services;

/// <summary>
/// Следит за папками «Программы» (пользователь + все пользователи): новый или удалённый
/// ярлык запускает пересборку индекса. События склеиваются паузой 4 с, иначе установка
/// одной программы вызывает десятки перестроек.
/// </summary>
internal sealed class StartMenuWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _debounce;
    private readonly Action _onChanged;

    public StartMenuWatcher(Action onChanged)
    {
        _onChanged = onChanged;

        // таймер создаём первым и без лямбды, замыкающейся на поле:
        // так нет ни nullable-предупреждений, ни использования до присваивания
        _debounce = new DispatcherTimer(
            TimeSpan.FromSeconds(4),
            DispatcherPriority.Background,
            OnDebounceElapsed,
            Dispatcher.CurrentDispatcher);

        _debounce.Stop();

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                 })
        {
            Attach(root);
        }
    }

    public int WatchedFolders => _watchers.Count;

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        AppLog.Write("watch: ярлыки изменились — пересобираю индекс");
        _onChanged();
    }

    private void Attach(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };

            watcher.Created += (_, e) => OnChanged(e.FullPath);
            watcher.Deleted += (_, e) => OnChanged(e.FullPath);
            watcher.Renamed += (_, e) => OnChanged(e.FullPath);
            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            AppLog.Error($"watch: не удалось следить за {root}", ex);
        }
    }

    private void OnChanged(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        _debounce.Stop();

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
