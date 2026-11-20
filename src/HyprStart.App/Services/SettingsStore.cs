using System.IO;
using System.Text.Json;
using HyprStart.Core.Settings;

namespace HyprStart.App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private SettingsStore(string path, AppSettings current, bool wasCreated)
    {
        Path = path;
        Current = current;
        WasCreated = wasCreated;
    }

    public string Path { get; }
    public AppSettings Current { get; }

    /// <summary>true, если файла не было и настройки созданы по умолчанию (первый запуск).</summary>
    public bool WasCreated { get; }

    public static SettingsStore Load()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var path = System.IO.Path.Combine(AppPaths.Root, "settings.json");

        if (!File.Exists(path))
        {
            var fresh = new AppSettings { Version = SettingsSchema.Version };
            var store = new SettingsStore(path, fresh, wasCreated: true);
            store.Save();
            AppLog.Write($"settings: создан {path}");
            return store;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            loaded.Layout ??= new PanelLayoutSettings();

            var store = new SettingsStore(path, loaded, wasCreated: false);
            if (Migrate(loaded))
            {
                store.Save();
            }

            AppLog.Write($"settings: загружен {path} (версия {loaded.Version})");
            return store;
        }
        catch (Exception ex)
        {
            AppLog.Error("settings: не удалось прочитать файл, берём дефолты", ex);
            return new SettingsStore(path, new AppSettings(), wasCreated: false);
        }
    }

    /// <summary>Возвращает true, если настройки были изменены и их надо перезаписать.</summary>
    private static bool Migrate(AppSettings settings)
    {
        bool changed = false;

        if (settings.Version < 2)
        {
            // v1 -> v2: перехват Win оставлял клавиатуру в состоянии «Win зажат».
            // Принудительно выключаем и переводим открытие меню на горячую клавишу.
            if (settings.InterceptWindowsKey)
            {
                settings.InterceptWindowsKey = false;
                AppLog.Write("settings: миграция v1->v2 — перехват Win принудительно выключен");
            }

            if (string.IsNullOrWhiteSpace(settings.Hotkey))
            {
                settings.Hotkey = "Alt+Space";
            }

            settings.Version = 2;
            changed = true;
            AppLog.Write("settings: миграция v1->v2 выполнена");
        }

        if (settings.Version < 3)
        {
            // v2 -> v3: гэпы между панелями 14–16px, цветная обводка 1.5px
            if (Math.Abs(settings.Layout.Gap - 10) < 0.01)
            {
                settings.Layout.Gap = 15;
            }

            if (string.IsNullOrWhiteSpace(settings.PanelBorderColor))
            {
                settings.PanelBorderColor = "#6E8BFF";
            }

            if (settings.PanelBorderThickness <= 0)
            {
                settings.PanelBorderThickness = 1.5;
            }

            settings.Version = 3;
            changed = true;
            AppLog.Write("settings: миграция v2->v3 выполнена (gap/gap-обводка)");
        }

        if (settings.Version < 4)
        {
            // v3 -> v4: включаем акриловую подложку с тёмной тонировкой
            settings.AcrylicBackdrop = true;

            if (string.IsNullOrWhiteSpace(settings.AcrylicTint))
            {
                settings.AcrylicTint = "#B00B0E17";
            }

            settings.Version = 4;
            changed = true;
            AppLog.Write("settings: миграция v3->v4 выполнена (акрил включён)");
        }

        if (settings.Version < 5)
        {
            // v4 -> v5: внешние отступы 36px, чтобы меню парило над рабочим столом
            if (Math.Abs(settings.Layout.Margin - 12) < 0.01)
            {
                settings.Layout.Margin = 36;
            }

            settings.Version = 5;
            changed = true;
            AppLog.Write("settings: миграция v4->v5 выполнена (margin 36)");
        }

        if (settings.Version < 6)
        {
            // v5 -> v6: переносимость. Base URL мини-чата вынесен в настройки,
            // появились погодные координаты и флаг скрытия панели MINI AI.
            settings.ChatBaseUrl ??= string.Empty;
            settings.Weather ??= new WeatherLocationSettings();

            // Старый файл мог не иметь координат вовсе: тогда их определит
            // автогеолокация при старте (см. LocationBootstrapper), а не хардкод.
            if (Math.Abs(settings.Weather.Latitude) < 0.000001 &&
                Math.Abs(settings.Weather.Longitude) < 0.000001)
            {
                settings.Weather.City = string.Empty;
            }

            settings.Version = 6;
            changed = true;
            AppLog.Write("settings: миграция v5->v6 выполнена (base URL, погода, скрытие чата)");
        }
        return changed;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Error("settings: не удалось сохранить", ex);
        }
    }
}
