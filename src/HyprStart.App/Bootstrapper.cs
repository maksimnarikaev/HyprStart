using System;
using System.Linq;
using System.Net.Http;
using HyprStart.App.Services;
using HyprStart.Core.Library;
using HyprStart.Core.Settings;

namespace HyprStart.App;

/// <summary>
/// Однократная инициализация настроек на новом ПК: геолокация для погоды
/// и стартовые пины из реально установленных приложений.
/// </summary>
internal static class LocationBootstrapper
{
    private static readonly HttpClient Client = new() { Timeout = GeoLocator.Timeout };

    /// <summary>
    /// Если координаты и город не заданы - определяем по IP и кэшируем в settings.json.
    /// Сеть недоступна - молча выходим: погода просто останется без города.
    /// Ручные координаты пользователя не перетираются никогда.
    /// </summary>
    public static void EnsureLocation(SettingsStore store)
    {
        var settings = store.Current;
        if(settings.Weather.Manual || !string.IsNullOrWhiteSpace(settings.Weather.City))
        {
            return;
        }

        GeoPoint? found = null;

        // Провайдеры перебираем по порядку: каждый отдаёт свой формат, поэтому
        // для каждого свой парсер. Первый успешный ответ и забираем.
        var parsers = new Func<string?, GeoPoint?>[]
        {
            GeoLocator.ParseIpApi,
            GeoLocator.ParseIpWhoIs,
            GeoLocator.ParseGeoJs,
        };

        for (var i = 0; i < GeoLocator.Endpoints.Count && found is null; i++)
        {
            try
            {
                var body = Client.GetStringAsync(GeoLocator.Endpoints[i]).GetAwaiter().GetResult();
                found = parsers[i](body);
            }
            catch
            {
                // Провайдер молчит или заблокирован - идём к следующему.
            }
        }

        if (found is null)
        {
            AppLog.Write("geo: ни один провайдер не ответил, погода без города");
            return;
        }

        settings.Weather.City = found.City;
        settings.Weather.Latitude = found.Latitude;
        settings.Weather.Longitude = found.Longitude;
        store.Save();
        AppLog.Write($"geo: определено {found.City} ({found.Latitude:F2},{found.Longitude:F2}) - сохранено");
    }
}

/// <summary>Подсев стартовых пинов при первом запуске.</summary>
internal static class Bootstrapper
{
    /// <summary>
    /// Строит пины из индекса установленных программ. Ничего не делает, если
    /// пользователь уже что-то закрепил или подсев уже выполнялся.
    /// </summary>
    public static void SeedPins(PinsStore pins, SettingsStore settings)
    {
        if(pins.Items.Count > 0) return;
        if(settings.Current.SeedPinsLogged) return;

        try
        {
            var index = new AppIndexService().LoadOrBuild();
            var installed = index.Entries
                .Select(e => new DefaultPins.InstalledApp(e.Name, e.Target, (int)e.Kind, e.Group))
                .ToList();

            var seeded = pins.SeedDefaults(installed);
            settings.Current.SeedPinsLogged = true;
            settings.Save();
            AppLog.Write($"pins: стартовый набор {seeded} приложений (в индексе {installed.Count})");
        }
        catch (Exception ex)
        {
            AppLog.Error("pins: подсев стартового набора не удался", ex);
        }
    }
}
