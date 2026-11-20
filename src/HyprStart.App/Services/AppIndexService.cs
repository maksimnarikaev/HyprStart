using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HyprStart.Core.Library;

namespace HyprStart.App.Services;

/// <summary>Готовый элемент списка для UI: запись + уже декодированная (замороженная) иконка.</summary>
public sealed class AppItemVm
{
    public AppItemVm(AppEntry entry, ImageSource? icon)
    {
        Entry = entry;
        Icon = icon;
    }

    public AppEntry Entry { get; }
    public ImageSource? Icon { get; }
    public string Name => Entry.Name;
}

/// <summary>
/// Индекс приложений: сборка, кэш на диске, загрузка готовых элементов для UI.
/// Кэш живёт 12 часов; иконки — отдельными PNG в подпапке icons.
/// </summary>
internal sealed class AppIndexService
{
    public const int CacheVersion = 3;

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AppIndexService()
    {
        CacheDir = Path.Combine(AppPaths.Root, "cache");
        IconsDir = Path.Combine(CacheDir, "icons");
        IndexFile = Path.Combine(CacheDir, "apps.index.json");
    }

    public string CacheDir { get; }
    public string IconsDir { get; }
    public string IndexFile { get; }

    public AppIndex LoadOrBuild()
    {
        var cached = TryLoad();
        if (cached is not null) return cached;

        AppLog.Write("index: кэш отсутствует или устарел, собираем заново");
        return Build(forceIcons: false);
    }

    public AppIndex Build(bool forceIcons)
    {
        Directory.CreateDirectory(IconsDir);

        var started = DateTime.Now;
        var shortcuts = ShortcutScanner.Scan();
        var uwp = UwpAppEnumerator.GetApps();
        var entries = AppIndexBuilder.Build(shortcuts, uwp);

        int placeholders = 0;

        // Извлечение иконок — самая дорогая часть сборки индекса, распараллеливаем.
        // Каждая запись пишет свой PNG (имя = хэш цели), пересечений нет.
        int done = 0;

        Parallel.ForEach(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            entry =>
            {
                entry.IconFile = IconCache.EnsureIcon(entry, IconsDir, forceIcons);

                if (IconCache.IsPlaceholder(entry.IconFile))
                {
                    Interlocked.Increment(ref placeholders);
                }

                Interlocked.Increment(ref done);
            });

        _ = done;

        var index = new AppIndex
        {
            Version = CacheVersion,
            IndexedAt = started,
            Entries = entries,
            IconCount = entries.Count - placeholders,
            PlaceholderCount = placeholders,
        };

        Save(index);
        AppLog.Write($"index: собрано {entries.Count} приложений " +
                     $"(ярлыки={shortcuts.Count}, uwp={uwp.Count}), иконок={index.IconCount}, " +
                     $"заглушек={placeholders}, {(DateTime.Now - started).TotalMilliseconds:0} мс");
        return index;
    }

    public List<AppItemVm> CreateItems(AppIndex index, int decodeSize = 96)
    {
        var items = new List<AppItemVm>(index.Entries.Count);
        foreach (var entry in index.Entries)
        {
            items.Add(new AppItemVm(entry, LoadIcon(entry.IconFile, decodeSize)));
        }

        return items;
    }

    private ImageSource? LoadIcon(string? iconFile, int decodeSize)
    {
        if (string.IsNullOrEmpty(iconFile)) return null;

        var path = Path.Combine(IconsDir, iconFile);
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = decodeSize;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze(); // замороженную картинку можно отдать в UI-поток
            return image;
        }
        catch (Exception ex)
        {
            AppLog.Error($"index: иконка не читается ({iconFile})", ex);
            return null;
        }
    }

    private AppIndex? TryLoad()
    {
        try
        {
            if (!File.Exists(IndexFile)) return null;

            var index = JsonSerializer.Deserialize<AppIndex>(File.ReadAllText(IndexFile), JsonOptions);
            if (index is null || index.Version != CacheVersion) return null;
            if (DateTime.Now - index.IndexedAt > MaxAge) return null;
            if (index.Entries.Count == 0) return null;

            // точечная проверка: иконки первых записей на месте
            foreach (var entry in index.Entries.Take(10))
            {
                if (entry.IconFile is null) return null;
                if (!File.Exists(Path.Combine(IconsDir, entry.IconFile))) return null;
            }

            AppLog.Write($"index: из кэша {index.Entries.Count} приложений, возраст {(DateTime.Now - index.IndexedAt).TotalMinutes:0} мин");
            return index;
        }
        catch (Exception ex)
        {
            AppLog.Error("index: кэш не читается", ex);
            return null;
        }
    }

    private void Save(AppIndex index)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(IndexFile, JsonSerializer.Serialize(index, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Error("index: не удалось сохранить кэш", ex);
        }
    }
}
