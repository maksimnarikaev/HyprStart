using HyprStart.Core.Storage;

namespace HyprStart.Core.Settings;

/// <summary>Что за приложение и по каким признакам его искать на диске.</summary>
public sealed record LaunchCandidate
{
    public required string Name { get; init; }

    /// <summary>0 - главные, 1 - популярные, 2 - дополнительные. Меньше - выше в списке.</summary>
    public required int Priority { get; init; }

    /// <summary>
    /// Имена exe без расширения (в нижнем регистре): проверяются в известных
    /// каталогах установки. Так находится Steam, Discord, Telegram и т.п.
    /// </summary>
    public IReadOnlyList<string> ExeNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Относительные пути внутри <c>%LocalAppData%</c> - так живут Telegram,
    /// Discord и VS Code у обычного пользователя без прав администратора.
    /// </summary>
    public IReadOnlyList<string> LocalAppDataPaths { get; init; } = Array.Empty<string>();

    /// <summary>Относительные пути внутри <c>%ProgramFiles%</c>/<c>%ProgramFiles(x86)%</c>.</summary>
    public IReadOnlyList<string> ProgramFilesPaths { get; init; } = Array.Empty<string>();

    /// <summary>Имена ярлыков (.lnk) в меню «Пуск», которые считаются этим приложением.</summary>
    public IReadOnlyList<string> ShortcutNames { get; init; } = Array.Empty<string>();

    /// <summary>Специальная запись: Проводник запускается по explorer.exe, а не по пути.</summary>
    public bool IsExplorer { get; init; }

    /// <summary>Специальная запись: Терминал - Windows Terminal или (резерв) PowerShell.</summary>
    public bool IsTerminal { get; init; }
}

/// <summary>Построитель первой закреплённой панели на новом ПК.</summary>
public static class DefaultPins
{
    /// <summary>Сколько пинов кладём на новом ПК: панель на четыре колонки не должна рваться.</summary>
    public const int MaxPins = 12;

    /// <summary>
    /// Каталог, по которому видно, что приложение установлено у данного пользователя.
    /// Нужен, чтобы отбросить ярлыки-сироты (ссылки на снесённую программу).
    /// </summary>
    public static readonly string[] KnownExeFolders =
    {
        @"Program Files",
        @"Program Files (x86)",
        @"ProgramData\Microsoft\Windows\Start Menu\Programs",
    };

    /// <summary>
    /// Справочник популярных приложений в порядке приоритета. Опирается только на
    /// типовые места установки Windows - никаких ярлыков конкретного компьютера.
    /// </summary>
    public static IReadOnlyList<LaunchCandidate> Catalog { get; } = new[]
    {
        new LaunchCandidate
        {
            Name = "Проводник",
            Priority = 0,
            IsExplorer = true,
            ShortcutNames = new[] { "Проводник", "File Explorer" },
        },
        new LaunchCandidate
        {
            Name = "Терминал",
            Priority = 0,
            IsTerminal = true,
            ExeNames = new[] { "wt", "WindowsTerminal" },
            LocalAppDataPaths = new[] { @"Microsoft\WindowsApps\wt.exe" },
            ShortcutNames = new[] { "Terminal", "Терминал" },
        },
        new LaunchCandidate
        {
            Name = "Браузер",
            Priority = 1,
            ExeNames = new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "yandex" },
            ProgramFilesPaths = new[]
            {
                @"Google\Chrome\Application\chrome.exe",
                @"Microsoft\Edge\Application\msedge.exe",
                @"Mozilla Firefox\firefox.exe",
                @"BraveSoftware\Brave-Browser\Application\brave.exe",
            },
            ShortcutNames = new[] { "Google Chrome", "Microsoft Edge", "Firefox" },
        },
        new LaunchCandidate
        {
            Name = "Steam",
            Priority = 2,
            ExeNames = new[] { "steam" },
            LocalAppDataPaths = new[] { @"Steam\steam.exe" },
            ShortcutNames = new[] { "Steam" },
        },
        new LaunchCandidate
        {
            Name = "Telegram",
            Priority = 2,
            ExeNames = new[] { "Telegram" },
            LocalAppDataPaths = new[] { @"Telegram Desktop\Telegram.exe" },
            ShortcutNames = new[] { "Telegram", "Telegram Desktop" },
        },
        new LaunchCandidate
        {
            Name = "Discord",
            Priority = 2,
            ExeNames = new[] { "Discord", "Update" },
            LocalAppDataPaths = new[] { @"Discord\Update.exe" },
            ShortcutNames = new[] { "Discord" },
        },
        new LaunchCandidate
        {
            Name = "VS Code",
            Priority = 2,
            ExeNames = new[] { "Code" },
            LocalAppDataPaths = new[] { @"Programs\Microsoft VS Code\Code.exe" },
            ProgramFilesPaths = new[] { @"Microsoft VS Code\Code.exe" },
            ShortcutNames = new[] { "Visual Studio Code" },
        },
        new LaunchCandidate
        {
            Name = "Параметры",
            Priority = 3,
            ExeNames = new[] { "SystemSettings" },
            ShortcutNames = new[] { "Параметры" },
        },
    };

    /// <summary>
    /// Строит пины: сначала приложения из справочника в порядке приоритета,
    /// затем - любые другие ярлыки из меню «Пуск», чтобы панель не пустовала,
    /// даже если на ПК нет ни одного браузера из списка.
    /// </summary>
    /// <param name="installed">
    /// Уже найденные на этом ПК цели запуска (из индекса меню «Пуск»).
    /// Для каждой - имя и путь (или протокол/спец-команда).
    /// </param>
    /// <param name="maxPins">Сколько максимум закрепить.</param>
    public static List<PinnedItem> Build(IEnumerable<PinnedItem>? installed, int maxPins = MaxPins)
    {
        var pool = (installed ?? Enumerable.Empty<PinnedItem>())
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Target))
            .ToList();

        var result = new List<PinnedItem>();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(PinnedItem item)
        {
            if (result.Count >= maxPins) return;
            if (!usedTargets.Add(item.Target)) return;
            result.Add(item);
        }

        // 1. Приложения из справочника - в порядке приоритета, затем по имени.
        foreach (var candidate in Catalog.OrderBy(c => c.Priority).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Count >= maxPins) break;

            var match = FindForCandidate(pool, candidate);
            if (match is not null) Add(match);
        }

        // 2. Остальные ярлыки: первыми идут записи в корне меню (не служебные папки).
        foreach (var item in pool.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Count >= maxPins) break;
            Add(item);
        }

        return result;
    }

    /// <summary>
    /// Ищет в установленном наборе запись под конкретное приложение справочника.
    /// Сравнение идёт по имени ярлыка и по имени exe из цели запуска.
    /// </summary>
    public static PinnedItem? FindForCandidate(IReadOnlyList<PinnedItem> pool, LaunchCandidate candidate)
    {
        foreach (var item in pool)
        {
            if (Matches(item, candidate)) return item;
        }

        return null;
    }

    /// <summary>
    /// Запись индекса меню «Пуск» в виде, нужном для сидинга. Отдельный тип, чтобы
    /// HyprStart.Core не зависел от классов приложения (AppEntry/AppEntryKind живут в Core,
    /// но конкретику UI сюда тащить нельзя).
    /// </summary>
    public sealed record InstalledApp(string Name, string Target, int Kind, string? Group);

    /// <summary>
    /// Строит стартовые пины из реально установленных приложений.
    /// В отличие от <see cref="Build"/>, сохраняет группу и иконку - иначе на панели
    /// «Закреплено» у новых пользователей были бы пустые квадраты.
    /// </summary>
    public static List<PinnedItem> BuildFrom(
        IEnumerable<InstalledApp>? installed,
        int maxPins = MaxPins)
    {
        var pool = (installed ?? Enumerable.Empty<InstalledApp>())
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Target))
            .ToList();

        var result = new List<PinnedItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(InstalledApp app)
        {
            if (result.Count >= maxPins) return;
            if (!used.Add(app.Target)) return;

            result.Add(new PinnedItem
            {
                Name = string.IsNullOrWhiteSpace(app.Name) ? DefaultPins.ExtractExeName(app.Target) : app.Name.Trim(),
                Target = app.Target.Trim(),
                Kind = app.Kind,
                Group = app.Group,
            });
        }

        foreach (var candidate in Catalog.OrderBy(c => c.Priority).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Count >= maxPins) break;
            foreach (var app in pool)
            {
                var probe = new PinnedItem { Name = app.Name, Target = app.Target, Kind = app.Kind };
                if (Matches(probe, candidate))
                {
                    Add(app);
                    break;
                }
            }
        }

        foreach (var app in pool.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Count >= maxPins) break;
            Add(app);
        }

        return result;
    }

    /// <summary>
    /// Фильтр битых ярлыков: .lnk, чей файл уже удалён, закреплять нельзя -
    /// на панели он выглядит иконкой, но не запускается.
    /// </summary>
    /// <param name="exists">Предикат существования файла (в тестах подменяется).</param>
    public static bool IsUsableShortcut(string? target, Func<string, bool>? exists = null)
    {
        var value = (target ?? string.Empty).Trim();
        if (value.Length == 0) return false;

        // AUMID (UWP) и URI-протоколы файлами не являются - их существование не проверить.
        if (value.Contains("://", StringComparison.Ordinal)) return true;
        if (!value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return true;

        var check = exists ?? File.Exists;
        return check(value);
    }
    /// <summary>true, если запись соответствует приложению из справочника.</summary>
    public static bool Matches(PinnedItem item, LaunchCandidate candidate)
    {
        var name = (item.Name ?? string.Empty).Trim();
        var target = (item.Target ?? string.Empty).Trim();

        foreach (var alias in candidate.ShortcutNames)
        {
            if (name.Equals(alias, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith(alias, StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (candidate.IsExplorer && target.StartsWith("explorer", StringComparison.OrdinalIgnoreCase)) return true;

        var exeName = ExtractExeName(target);
        if (exeName.Length > 0)
        {
            if (candidate.IsTerminal &&
                (exeName.Equals("wt", StringComparison.OrdinalIgnoreCase) ||
                 exeName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
                 exeName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                 exeName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            foreach (var exe in candidate.ExeNames)
            {
                if (exeName.Equals(exe, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Steam/Discord держат exe в подпапке bin - допускаем частичное совпадение по имени файла.
                if (exe.Length >= 5 && exeName.StartsWith(exe, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Имя файла без расширения из пути запуска. Для URI-протоколов и
    /// пустых строк возвращает пустую строку.
    /// </summary>
    public static string ExtractExeName(string? target)
    {
        var value = (target ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;

        // "C:\Program Files\X\app.exe" --args  ->  первый токен в кавычках
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end > 1) value = value.Substring(1, end - 1);
        }
        else if (value.Contains(' '))
        {
            // Без кавычек режем только хвост с аргументами - после .exe/.com/.bat,
            // иначе "C:\Program Files\App\app.exe" превращался в "C:\Program"
            // и приложение на чужом ПК не находилось.
            value = TrimArguments(value);
        }

        if (value.Contains("://", StringComparison.Ordinal)) return string.Empty;

        var fileName = value.Replace('/', '\\');
        var slash = fileName.LastIndexOf('\\');
        if (slash >= 0) fileName = fileName[(slash + 1)..];

        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    /// <summary>
    /// Отрезает аргументы командной строки у неквотированного пути вида
    /// "C:\Program Files\App\app.exe --flag". Границей служит расширение файла;
    /// если его нет (голая команда) - режем по первому пробелу.
    /// </summary>
    private static string TrimArguments(string value)
    {
        string[] extensions = { ".exe", ".com", ".bat", ".cmd", ".msc", ".lnk" };
        var best = -1;
        foreach (var ext in extensions)
        {
            var at = value.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var stop = at + ext.Length;
            if (best < 0 || stop < best) best = stop;
        }

        if (best > 0) return value[..best];
        return value.Split(' ')[0];
    }
}
