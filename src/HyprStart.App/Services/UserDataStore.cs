using System.IO;
using System.Text.Json;
using HyprStart.Core.Library;
using HyprStart.Core.Storage;

namespace HyprStart.App.Services;

/// <summary>pins.json — закреплённое, порядок задаётся перетаскиванием.</summary>
public sealed class PinsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private PinnedData _data = new();

    public PinsStore()
    {
        Path = System.IO.Path.Combine(AppPaths.Root, "pins.json");
        Load();
    }

    public string Path { get; }
    public IReadOnlyList<PinnedItem> Items => _data.Items;

    public bool Contains(AppEntry entry)
        => _data.Items.Any(i => i.SameAs(entry.Target, (int)entry.Kind));

    public bool Toggle(AppEntry entry)
    {
        var existing = _data.Items.FirstOrDefault(i => i.SameAs(entry.Target, (int)entry.Kind));
        bool added;

        if (existing is not null)
        {
            _data.Items.Remove(existing);
            added = false;
        }
        else
        {
            _data.Items.Add(new PinnedItem
            {
                Name = entry.Name,
                Target = entry.Target,
                Kind = (int)entry.Kind,
            });
            added = true;
        }

        Save();
        return added;
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _data.Items.Count) return;
        if (toIndex < 0 || toIndex >= _data.Items.Count || fromIndex == toIndex) return;

        var item = _data.Items[fromIndex];
        _data.Items.RemoveAt(fromIndex);
        _data.Items.Insert(toIndex, item);
        Save();
        AppLog.Write($"pins: перемещено {fromIndex} -> {toIndex}");
    }

    /// <summary>Перестановка плиток внутри открытой папки.</summary>
    public void MoveInFolder(PinnedItem folder, int fromIndex, int toIndex)
    {
        if (!folder.IsFolder) return;
        if (fromIndex < 0 || fromIndex >= folder.Children.Count) return;
        if (toIndex < 0 || toIndex >= folder.Children.Count || fromIndex == toIndex) return;

        var item = folder.Children[fromIndex];
        folder.Children.RemoveAt(fromIndex);
        folder.Children.Insert(toIndex, item);
        Save();
        AppLog.Write($"pins: внутри '{folder.Name}' перемещено {fromIndex} -> {toIndex}");
    }

    public void Remove(PinnedItem item)
    {
        _data.Items.Remove(item);
        foreach (var folder in _data.Items.Where(i => i.IsFolder))
        {
            folder.Children.Remove(item);
        }

        Save();
    }

    /// <summary>Закрепить обычную папку с диска (drag-and-drop из проводника).</summary>
    public bool AddPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        if (_data.Items.Any(i => string.Equals(i.Target, path, StringComparison.OrdinalIgnoreCase))) return false;

        _data.Items.Add(new PinnedItem
        {
            Name = new DirectoryInfo(path).Name,
            Target = path,
            Kind = (int)AppEntryKind.Shortcut,
        });

        Save();
        AppLog.Write($"pins: закреплена папка '{path}'");
        return true;
    }

    /// <summary>Закрепить приложение, опционально сразу в папку. Возвращает false, если уже закреплено.</summary>
    public bool Add(AppEntry entry, PinnedItem? folder = null)
    {
        if (Contains(entry)) return false;

        var item = new PinnedItem
        {
            Name = entry.Name,
            Target = entry.Target,
            Kind = (int)entry.Kind,
        };

        if (folder is { IsFolder: true })
        {
            folder.Children.Add(item);
            AppLog.Write($"pins: '{entry.Name}' добавлено в папку '{folder.Name}' (drag)");
        }
        else
        {
            _data.Items.Add(item);
            AppLog.Write($"pins: '{entry.Name}' закреплено (drag)");
        }

        Save();
        return true;
    }

    /// <summary>Новая папка-группа с уникальным именем.</summary>
    public PinnedItem AddFolder(string baseName = "Папка")
    {
        var name = baseName;
        int index = 2;

        while (_data.Items.Any(i => i.IsFolder && string.Equals(i.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            name = $"{baseName} {index++}";
        }

        var folder = new PinnedItem { Name = name, IsFolder = true };
        _data.Items.Add(folder);
        Save();
        AppLog.Write($"pins: создана папка '{name}'");
        return folder;
    }

    public void Rename(PinnedItem item, string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        item.Name = trimmed;
        Save();
        AppLog.Write($"pins: переименовано в '{trimmed}'");
    }

    /// <summary>Кладём приложение в папку закреплённого.</summary>
    public void MoveInto(PinnedItem folder, PinnedItem child)
    {
        if (!folder.IsFolder || ReferenceEquals(folder, child)) return;

        _data.Items.Remove(child);
        foreach (var existing in _data.Items.Where(i => i.IsFolder)) existing.Children.Remove(child);

        folder.Children.Add(child);
        Save();
        AppLog.Write($"pins: '{child.Name}' -> папка '{folder.Name}'");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            _data = JsonSerializer.Deserialize<PinnedData>(File.ReadAllText(Path), Options) ?? new PinnedData();
        }
        catch (Exception ex)
        {
            AppLog.Error("pins: не удалось прочитать файл", ex);
            _data = new PinnedData();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(Path, JsonSerializer.Serialize(_data, Options));
        }
        catch (Exception ex)
        {
            AppLog.Error("pins: не удалось сохранить", ex);
        }
    }
}

/// <summary>usage.json — локальный счётчик запусков для секции «Частое».</summary>
public sealed class UsageStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private UsageData _data = new();

    public UsageStore()
    {
        Path = System.IO.Path.Combine(AppPaths.Root, "usage.json");
        Load();
    }

    public string Path { get; }

    public int CountFor(string target)
        => _data.Items.TryGetValue(target, out var record) ? record.Count : 0;

    /// <summary>
    /// Снимок статистики для frecency-сортировки поиска: цель → (запусков, последний запуск).
    /// Собирается по требованию при вводе запроса, без фоновых обновлений.
    /// </summary>
    public Dictionary<string, (int Count, DateTime LastUsed)> Snapshot()
    {
        var map = new Dictionary<string, (int Count, DateTime LastUsed)>(_data.Items.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _data.Items)
        {
            map[pair.Key] = (pair.Value.Count, pair.Value.LastUsed);
        }

        return map;
    }

    public void Register(AppEntry entry)
    {
        if (!_data.Items.TryGetValue(entry.Target, out var record))
        {
            record = new UsageRecord();
            _data.Items[entry.Target] = record;
        }

        record.Count++;
        record.LastUsed = DateTime.Now;
        Save();
    }

    /// <summary>Убрать приложение из «Частого» (Delete в меню).</summary>
    public bool Remove(string target)
    {
        if (!_data.Items.Remove(target)) return false;

        Save();
        AppLog.Write($"usage: '{target}' убрано из частого");
        return true;
    }

    /// <summary>Топ часто используемых с учётом давности (свежее — выше при равном счёте).</summary>
    public List<(string Target, int Count)> Top(IEnumerable<AppEntry> entries, int take)
    {
        return entries
            .Select(entry => new { entry.Target, Count = CountFor(entry.Target), Last = _data.Items.TryGetValue(entry.Target, out var r) ? r.LastUsed : DateTime.MinValue })
            .Where(item => item.Count > 0)
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.Last)
            .Take(take)
            .Select(item => (item.Target, item.Count))
            .ToList();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            _data = JsonSerializer.Deserialize<UsageData>(File.ReadAllText(Path), Options) ?? new UsageData();
            _data.Items = new Dictionary<string, UsageRecord>(_data.Items, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Error("usage: не удалось прочитать файл", ex);
            _data = new UsageData();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(Path, JsonSerializer.Serialize(_data, Options));
        }
        catch (Exception ex)
        {
            AppLog.Error("usage: не удалось сохранить", ex);
        }
    }
}
