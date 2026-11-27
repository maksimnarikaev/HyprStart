using System.Text.Json;
using HyprStart.Core.Settings;
using HyprStart.Core.Storage;
using Xunit;

namespace HyprStart.Core.Tests;

/// <summary>
/// Контракт схемы настроек v6: то, от чего зависит переносимость и миграция v5 -> v6.
/// Сам Migrate живёт в HyprStart.App (private), поэтому здесь проверяется то,
/// что миграция обязана обеспечить: дефолты новых полей и чтение старого JSON.
/// </summary>
public class SettingsSchemaTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void Version_IsSeven()
    {
        Assert.Equal(7, SettingsSchema.Version);
    }

    [Fact]
    public void AppSettings_DefaultVersionIsZero_SoMigrationRuns()
    {
        // Ключевой инвариант: файл без поля version считается старым.
        Assert.Equal(0, new AppSettings().Version);
    }

    [Fact]
    public void AppSettings_NewPortabilityFieldsHaveSafeDefaults()
    {
        var settings = new AppSettings();

        // Пустой Base URL означает "взять DeepSeek по умолчанию" (ChatEndpoint.BuildUrl).
        Assert.Equal(string.Empty, settings.ChatBaseUrl);
        Assert.False(settings.HideChatPanel);
        Assert.NotNull(settings.Weather);
        Assert.Equal(string.Empty, settings.Weather.City);
        Assert.False(settings.Weather.HasCoordinates);
        Assert.False(settings.Weather.Manual);
        Assert.False(settings.SeedPinsLogged);
    }

    [Fact]
    public void AppSettings_DefaultHotkeyIsAltSpace()
    {
        var settings = new AppSettings();

        Assert.Equal("Alt+Space", settings.Hotkey);
        Assert.True(HotkeyIsValid(settings.Hotkey));
    }

    private static bool HotkeyIsValid(string value)
        => HyprStart.Core.Input.HotkeySpec.TryParse(value, out _);

    [Fact]
    public void LegacyV5Json_DeserializesWithoutCrashing()
    {
        // Так выглядел файл до v6: без base URL, без weather, без hideChatPanel.
        const string legacy = """
        {
          "version": 5,
          "theme": "Ice blue",
          "hotkey": "Alt+Space",
          "chatModel": "deepseek-chat",
          "chatApiKey": "sk-old",
          "chatProxy": "",
          "layout": { "margin": 36, "gap": 15 }
        }
        """;

        var settings = JsonSerializer.Deserialize<AppSettings>(legacy, Options);

        Assert.NotNull(settings);
        Assert.Equal(5, settings!.Version);
        Assert.Equal("sk-old", settings.ChatApiKey);
        // Новые поля получили дефолты - миграция увидит Version < 6 и дозаполнит.
        Assert.Equal(string.Empty, settings.ChatBaseUrl);
        Assert.NotNull(settings.Weather);
        Assert.False(settings.HideChatPanel);
    }

    [Fact]
    public void MissingVersionField_IsTreatedAsOldSchema()
    {
        const string ancient = """{ "theme": "Ice blue", "hotkey": "Alt+Space" }""";

        var settings = JsonSerializer.Deserialize<AppSettings>(ancient, Options);

        Assert.NotNull(settings);
        Assert.Equal(0, settings!.Version);
        Assert.True(settings.Version < SettingsSchema.Version);
    }

    [Fact]
    public void V6Json_RoundTripsAllPortabilityFields()
    {
        var original = new AppSettings
        {
            Version = SettingsSchema.Version,
            ChatBaseUrl = "https://openrouter.ai/api/v1",
            ChatModel = "openai/gpt-4o-mini",
            HideChatPanel = true,
            ShowPanelGrid = false,
            SeedPinsLogged = true,
            Weather = new WeatherLocationSettings
            {
                City = "Казань",
                Latitude = 55.7887,
                Longitude = 49.1221,
                Manual = true,
            },
        };

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal(SettingsSchema.Version, restored!.Version);
        Assert.Equal("https://openrouter.ai/api/v1", restored.ChatBaseUrl);
        Assert.Equal("openai/gpt-4o-mini", restored.ChatModel);
        Assert.True(restored.HideChatPanel);
        Assert.False(restored.ShowPanelGrid);
        Assert.True(restored.SeedPinsLogged);
        Assert.Equal("Казань", restored.Weather.City);
        Assert.Equal(55.7887, restored.Weather.Latitude, 4);
        Assert.True(restored.Weather.Manual);
        Assert.True(restored.Weather.HasCoordinates);
    }

    [Fact]
    public void V6Json_UsesCamelCasePropertyNames()
    {
        var json = JsonSerializer.Serialize(new AppSettings(), Options);

        Assert.Contains("chatBaseUrl", json);
        Assert.Contains("hideChatPanel", json);
        Assert.Contains("showPanelGrid", json);
        Assert.Contains("weather", json);
        Assert.Contains("seedPinsLogged", json);
    }

    [Fact]
    public void PanelLayoutSettings_ToSpecCopiesAllFields()
    {
        var layout = new PanelLayoutSettings
        {
            Margin = 36,
            Gap = 15,
            PolybarHeight = 26,
            LeftColumnPercent = 27,
            RightTopPercent = 62,
            FrequentPercent = 55,
        };

        var spec = layout.ToSpec();

        Assert.Equal(layout.Margin, spec.Margin);
        Assert.Equal(layout.Gap, spec.Gap);
        Assert.Equal(layout.PolybarHeight, spec.PolybarHeight);
        Assert.Equal(layout.LeftColumnPercent, spec.LeftColumnPercent);
        Assert.Equal(layout.RightTopPercent, spec.RightTopPercent);
        Assert.Equal(layout.FrequentPercent, spec.FrequentPercent);
    }

    [Fact]
    public void PinnedData_NewFileStartsWithNoPins_SoSeedingRuns()
    {
        // Первый запуск: пинов нет -> bootstrapper подсеивает найденные приложения.
        Assert.Empty(new PinnedData().Items);
        Assert.False(new AppSettings().SeedPinsLogged);
    }
}