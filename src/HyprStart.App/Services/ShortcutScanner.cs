using System.IO;
using HyprStart.Core.Library;

namespace HyprStart.App.Services;

/// <summary>
/// Скан ярлыков из меню «Программы» (пользователь + все пользователи).
/// Берём .lnk и .url, но из .url — только ссылки Steam (steam://rungameid/...):
/// игры Steam лежат именно такими файлами, иначе их нет в списке.
/// Иконку для них подсказывает сам .url (ключ IconFile).
/// </summary>
internal static class ShortcutScanner
{
    public static List<AppEntry> Scan()
    {
        var result = new List<AppEntry>();

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            result.AddRange(ScanFolder(root, "*.lnk", ReadShortcut));
            result.AddRange(ScanFolder(root, "*.url", ReadSteamShortcut));
        }

        AppLog.Write($"index: найдено ярлыков {result.Count}");
        return result;
    }

    private static IEnumerable<AppEntry> ScanFolder(string root, string pattern, Func<string, string, AppEntry?> factory)
    {
        List<string> files;

        try
        {
            files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error($"index: не удалось прочитать {root}", ex);
            return Array.Empty<AppEntry>();
        }

        var result = new List<AppEntry>(files.Count);

        foreach (var file in files)
        {
            try
            {
                var entry = factory(root, file);
                if (entry is not null) result.Add(entry);
            }
            catch (Exception ex)
            {
                AppLog.Error($"index: пропущен {file}", ex);
            }
        }

        return result;
    }

    private static AppEntry? ReadShortcut(string root, string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(name)) return null;

        return new AppEntry
        {
            Name = name,
            Target = file,
            Kind = AppEntryKind.Shortcut,
            Group = TopFolder(root, file),
        };
    }

    /// <summary>.url пропускаем, если это не steam:// — веб-ссылки в меню не нужны.</summary>
    private static AppEntry? ReadSteamShortcut(string root, string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var (url, iconFile) = ParseInternetShortcut(file);
        if (url is null || !url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) return null;

        return new AppEntry
        {
            Name = name,
            Target = file,
            Kind = AppEntryKind.Shortcut,
            Group = TopFolder(root, file),
            // IconFile в .url часто с индексом ресурса: "…\game.ico,0" — индекс отрезаем,
            // иначе File.Exists не находит файл и иконка теряется.
            IconHint = string.IsNullOrWhiteSpace(iconFile) ? null : iconFile.Split(',')[0].Trim(),
        };
    }

    private static (string? Url, string? IconFile) ParseInternetShortcut(string file)
    {
        string? url = null;
        string? icon = null;

        foreach (var line in File.ReadLines(file))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                url = line[4..].Trim();
            }
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
            {
                icon = line[9..].Trim();
            }
        }

        return (url, icon);
    }

    /// <summary>Первый сегмент относительного пути = папка верхнего уровня.</summary>
    private static string? TopFolder(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var separator = relative.IndexOf(Path.DirectorySeparatorChar);
        if (separator <= 0) return null;

        var folder = relative[..separator].Trim();
        return folder.Length == 0 ? null : folder;
    }
}
