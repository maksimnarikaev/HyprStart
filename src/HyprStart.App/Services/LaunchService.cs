using System.ComponentModel;
using System.Diagnostics;
using HyprStart.Core.Library;

namespace HyprStart.App.Services;

/// <summary>Запуск приложений: ярлык — ShellExecute, UWP — через shell:AppsFolder по AUMID.</summary>
internal static class LaunchService
{
    public static bool Launch(AppEntry entry, bool asAdmin = false)
    {
        try
        {
            if (entry.Kind == AppEntryKind.Uwp)
            {
                if (asAdmin) AppLog.Write($"launch: '{entry.Name}' — UWP нельзя запустить от админа, запускаю обычн.о");

                var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                startInfo.ArgumentList.Add("shell:AppsFolder\\" + entry.Target);
                Process.Start(startInfo);
            }
            else
            {
                var startInfo = new ProcessStartInfo(entry.Target) { UseShellExecute = true };
                if (asAdmin) startInfo.Verb = "runas";
                Process.Start(startInfo);
            }

            AppLog.Write($"launch: {entry.Name} [{entry.Kind}]{(asAdmin ? " (админ)" : string.Empty)}");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 1223 = ERROR_CANCELLED: пользователь отклонил UAC
            AppLog.Write($"launch: '{entry.Name}' отменён в UAC");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error($"launch: не удалось запустить '{entry.Name}' ({entry.Target})", ex);
            return false;
        }
    }
}
