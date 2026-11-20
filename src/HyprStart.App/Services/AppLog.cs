using System.IO;
using System.Text;

namespace HyprStart.App.Services;

/// <summary>Простой лог в %LOCALAPPDATA%\HyprStart\logs\app.log с ротацией на 1 МБ.</summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();
    private static string _path = string.Empty;
    private static bool _echo;

    public static string LogDirectory { get; private set; } = string.Empty;

    public static void Init(bool echo)
    {
        _echo = echo;
        LogDirectory = Path.Combine(AppPaths.Root, "logs");
        Directory.CreateDirectory(LogDirectory);
        _path = Path.Combine(LogDirectory, "app.log");
        TryRotate();
        Write($"=== HyprStart start, pid={Environment.ProcessId} ===");
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        if (_echo)
        {
            Console.WriteLine(line);
        }

        if (_path.Length == 0) return;

        try
        {
            lock (Gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // лог не должен ронять приложение
        }
    }

    public static void Error(string message, Exception? ex = null)
        => Write(ex is null ? $"ERROR {message}" : $"ERROR {message}: {ex}");

    private static void TryRotate()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var info = new FileInfo(_path);
            if (info.Length < MaxBytes) return;
            var old = Path.Combine(LogDirectory, "app.old.log");
            File.Copy(_path, old, overwrite: true);
            File.WriteAllText(_path, string.Empty, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }
}

/// <summary>Пути приложения: %LOCALAPPDATA%\HyprStart.</summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyprStart");
}
