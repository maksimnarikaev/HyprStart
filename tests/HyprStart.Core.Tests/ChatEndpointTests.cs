using HyprStart.Core.Settings;
using Xunit;

namespace HyprStart.Core.Tests;

public class ChatEndpointTests
{
    [Theory]
    [InlineData("https://api.deepseek.com/", "https://api.deepseek.com")]
    [InlineData("  https://api.openai.com/v1///  ", "https://api.openai.com/v1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizeBaseUrl_TrimsAndStripsTrailingSlashes(string? input, string expected)
    {
        Assert.Equal(expected, ChatEndpoint.NormalizeBaseUrl(input));
    }

    [Fact]
    public void BuildUrl_Empty_UsesDeepSeekDefault()
    {
        Assert.Equal("https://api.deepseek.com/chat/completions", ChatEndpoint.BuildUrl(null));
        Assert.Equal("https://api.deepseek.com/chat/completions", ChatEndpoint.BuildUrl(""));
        Assert.Equal("https://api.deepseek.com/chat/completions", ChatEndpoint.BuildUrl("   "));
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    public void BuildUrl_AppendsCompletionsPathOnce(string baseUrl, string expected)
    {
        Assert.Equal(expected, ChatEndpoint.BuildUrl(baseUrl));
    }

    [Fact]
    public void BuildUrl_DoesNotDuplicateCompletionsPath()
    {
        const string full = "https://api.groq.com/openai/v1/chat/completions";

        Assert.Equal(full, ChatEndpoint.BuildUrl(full));
        // Дублирование пути - самая частая ошибка при копировании из документации.
        Assert.DoesNotContain("/chat/completions/chat/completions", ChatEndpoint.BuildUrl(full));
    }

    [Fact]
    public void BuildUrl_BareHost_GetsHttpsScheme()
    {
        // Без схемы HttpClient падает: дописываем https, а не http.
        Assert.Equal("https://api.deepseek.com/chat/completions", ChatEndpoint.BuildUrl("api.deepseek.com"));
    }

    [Fact]
    public void ResolveApiKey_SettingsWinOverEnvironment()
    {
        var key = ChatEndpoint.ResolveApiKey("  from-settings  ", _ => "from-env");

        Assert.Equal("from-settings", key);
    }

    [Fact]
    public void ResolveApiKey_FallsBackToEnvironment()
    {
        var seen = string.Empty;
        var key = ChatEndpoint.ResolveApiKey(null, name =>
        {
            seen = name;
            return "  env-key  ";
        });

        Assert.Equal("env-key", key);
        Assert.Equal(ChatEndpoint.EnvKeyName, seen);
    }

    [Fact]
    public void ResolveApiKey_NothingSet_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ChatEndpoint.ResolveApiKey(null, _ => null));
        Assert.Equal(string.Empty, ChatEndpoint.ResolveApiKey("   ", _ => "   "));
    }

    [Theory]
    [InlineData("sk-test", true)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsConfigured_ReflectsResolvedKey(string? settingsKey, bool expected)
    {
        Assert.Equal(expected, ChatEndpoint.IsConfigured(settingsKey, _ => null));
    }

    [Fact]
    public void IsConfigured_TrueWhenOnlyEnvironmentHasKey()
    {
        Assert.True(ChatEndpoint.IsConfigured(null, _ => "env-key"));
    }

    [Theory]
    [InlineData(55.7558, 37.6173, true)]
    [InlineData(-90, 180, true)]
    [InlineData(90, -180, true)]
    [InlineData(0, 0, false)]        // нулевые координаты - признак "не задано"
    [InlineData(91, 37, false)]
    [InlineData(55, 181, false)]
    [InlineData(double.NaN, 37, false)]
    public void IsValidCoordinate_RejectsOutOfRangeAndNullIsland(double latitude, double longitude, bool expected)
    {
        Assert.Equal(expected, ChatEndpoint.IsValidCoordinate(latitude, longitude));
    }

    [Fact]
    public void WeatherLocationSettings_HasCoordinatesFollowsValidation()
    {
        var settings = new WeatherLocationSettings { Latitude = 43.03, Longitude = 44.68 };
        Assert.True(settings.HasCoordinates);

        settings.Latitude = 0;
        settings.Longitude = 0;
        Assert.False(settings.HasCoordinates);
    }

    [Fact]
    public void WeatherLocationSettings_Describe_PrefersCity()
    {
        var settings = new WeatherLocationSettings { City = "  Москва  ", Latitude = 55.75, Longitude = 37.61 };

        Assert.Equal("Москва", settings.Describe());
    }

    [Fact]
    public void WeatherLocationSettings_Describe_FallsBackToCoordinates()
    {
        var settings = new WeatherLocationSettings { City = "", Latitude = 43.03, Longitude = 44.68 };

        // Инвариантная культура: точка как разделитель при любых региональных настройках.
        Assert.Equal("43.03, 44.68", settings.Describe());
    }

    [Fact]
    public void WeatherLocationSettings_Describe_FallsBackToPlaceholder()
    {
        var settings = new WeatherLocationSettings();

        Assert.Equal("погода", settings.Describe());
    }
}