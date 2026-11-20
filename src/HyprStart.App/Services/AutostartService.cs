using Microsoft.Win32;

namespace HyprStart.App.Services;

/// <summary>Автозапуск через HKCU\...\Run — без прав администратора и без планировщика.</summary>
internal static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HyprStart";

    public static string CommandLine()
    {
        var exe = Environment.ProcessPath ?? "HyprStart.exe";
        return $"\"{exe}\" --tray";
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("autostart: чтение не удалось", ex);
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                key.SetValue(ValueName, CommandLine(), RegistryValueKind.String);
                AppLog.Write($"autostart: enabled -> {CommandLine()}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                AppLog.Write("autostart: disabled");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("autostart: запись не удалась", ex);
        }
    }
}
