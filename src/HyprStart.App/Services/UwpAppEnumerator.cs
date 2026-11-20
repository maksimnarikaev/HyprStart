using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using HyprStart.Core.Library;

namespace HyprStart.App.Services;

/// <summary>
/// UWP/Store-приложения через shell:AppsFolder (`Get-StartApps`).
/// Из результата берём только настоящие AUMID — у обычных приложений AppID выглядит
/// как «{GUID}\путь\app.exe», и они уже пришли из ярлыков.
/// </summary>
internal static class UwpAppEnumerator
{
    private const string Command =
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress";

    public static List<AppEntry> GetApps()
    {
        var result = new List<AppEntry>();

        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(Command);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                AppLog.Write("index: не удалось запустить powershell для Get-StartApps");
                return result;
            }

            var json = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(20_000);

            if (string.IsNullOrWhiteSpace(json))
            {
                AppLog.Write($"index: Get-StartApps вернул пусто (code={process.ExitCode}, err={error.Trim()})");
                return result;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                AddFromObject(root, result);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object) AddFromObject(element, result);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("index: Get-StartApps упал", ex);
        }

        AppLog.Write($"index: найдено UWP-приложений {result.Count}");
        return result;
    }

    private static void AddFromObject(JsonElement element, List<AppEntry> target)
    {
        string? name = null;
        string? appId = null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("Name") || property.NameEquals("name"))
            {
                name = property.Value.GetString();
            }
            else if (property.NameEquals("AppID") || property.NameEquals("appId"))
            {
                appId = property.Value.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId)) return;
        if (!LooksLikeAumid(appId)) return;

        target.Add(new AppEntry
        {
            Name = name,
            Target = appId,
            Kind = AppEntryKind.Uwp,
        });
    }

    /// <summary>
    /// Настоящий AUMID выглядит как «PackageFamilyName!ApplicationId».
    /// Записи вида «{GUID}\путь\app.exe» и «Microsoft.Office.MSACCESS.EXE.15» — это
    /// desktop-приложения, которые уже пришли ярлыками, иначе список двоится.
    /// </summary>
    private static bool LooksLikeAumid(string appId)
        => appId.Contains('!')
           && !appId.Contains('\\')
           && !appId.StartsWith('{')
           && !appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}
