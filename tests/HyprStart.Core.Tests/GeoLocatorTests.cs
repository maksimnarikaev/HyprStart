using HyprStart.Core.Settings;
using Xunit;

namespace HyprStart.Core.Tests;

public class GeoLocatorTests
{
    [Fact]
    public void ParseIpApi_ReadsCityAndCoordinates()
    {
        const string json = "{\"ip\":\"1.2.3.4\",\"city\":\"Vladikavkaz\",\"latitude\":43.0367,\"longitude\":44.6678,\"country\":\"RU\"}";

        var point = GeoLocator.ParseIpApi(json);

        Assert.NotNull(point);
        Assert.Equal("Vladikavkaz", point!.City);
        Assert.Equal(43.0367, point.Latitude, 4);
        Assert.Equal(44.6678, point.Longitude, 4);
    }

    [Fact]
    public void ParseIpApi_EmptyOrGarbage_ReturnsNull()
    {
        Assert.Null(GeoLocator.ParseIpApi(null));
        Assert.Null(GeoLocator.ParseIpApi(string.Empty));
        Assert.Null(GeoLocator.ParseIpApi("{}"));
        Assert.Null(GeoLocator.ParseIpApi("not json at all"));
    }

    [Fact]
    public void ParseIpWhoIs_ParsesSuccessfulResponse()
    {
        const string json = "{\"success\":true,\"city\":\"Kazan\",\"latitude\":\"55.7887\",\"longitude\":\"49.1221\"}";

        var point = GeoLocator.ParseIpWhoIs(json);

        Assert.NotNull(point);
        Assert.Equal("Kazan", point!.City);
        Assert.Equal(55.7887, point.Latitude, 4);
        Assert.Equal(49.1221, point.Longitude, 4);
    }

    [Fact]
    public void ParseIpWhoIs_UnsuccessfulResponse_ReturnsNull()
    {
        // Сервис отвечает success=false и сообщением - координат в теле нет.
        Assert.Null(GeoLocator.ParseIpWhoIs("{\"success\":false,\"message\":\"Rate limited\"}"));
    }

    [Fact]
    public void ParseGeoJs_ParsesStringCoordinates()
    {
        const string json = "{\"city\":\"Novosibirsk\",\"latitude\":\"55.0084\",\"longitude\":\"82.9357\"}";

        var point = GeoLocator.ParseGeoJs(json);

        Assert.NotNull(point);
        Assert.Equal("Novosibirsk", point!.City);
        Assert.Equal(55.0084, point.Latitude, 4);
    }

    [Fact]
    public void ParseFlat_BogonIp_ReturnsNull()
    {
        // Приватный/неопределяемый адрес даёт bogon вместо города.
        const string json = "{\"bogon\":true,\"city\":null,\"latitude\":null,\"longitude\":null}";

        Assert.Null(GeoLocator.ParseFlat(json, "\"city\"", "\"latitude\"", "\"longitude\""));
    }

    [Fact]
    public void ParseFlat_NegativeAndFractionalNumbers_AreParsed()
    {
        const string json = "{\"city\":\"Sydney\",\"latitude\":-33.8679,\"longitude\":151.2073}";

        var point = GeoLocator.ParseFlat(json, "\"city\"", "\"latitude\"", "\"longitude\"");

        Assert.NotNull(point);
        Assert.Equal(-33.8679, point!.Latitude, 4);
        Assert.Equal(151.2073, point.Longitude, 4);
    }

    [Fact]
    public void ParseFlat_NullIsland_IsRejected()
    {
        // 0/0 - "координаты не заданы", а не экватор с нулевым меридианом.
        const string json = "{\"city\":\"Nowhere\",\"latitude\":0,\"longitude\":0}";

        Assert.Null(GeoLocator.ParseFlat(json, "\"city\"", "\"latitude\"", "\"longitude\""));
    }

    [Fact]
    public void ExtractStringValue_ReadsStringValue()
    {
        Assert.Equal("Kazan", GeoLocator.ExtractStringValue("{\"city\":\"Kazan\"}", "\"city\""));
    }

    [Fact]
    public void ExtractStringValue_MissingKey_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GeoLocator.ExtractStringValue("{\"town\":\"Kazan\"}", "\"city\""));
    }

    [Theory]
    [InlineData("Russian Standard Time", "Москва")]
    [InlineData("Russia Time Zone 10", "Владивосток")]
    [InlineData("Russia Time Zone 11", "Магадан")]
    public void FromTimeZone_KnownZone_ReturnsCity(string zone, string expectedCity)
    {
        var point = GeoLocator.FromTimeZone(zone);

        Assert.NotNull(point);
        Assert.Equal(expectedCity, point!.City);
        Assert.True(point.IsUsable);
    }

    [Fact]
    public void FromTimeZone_IsCaseInsensitive()
    {
        Assert.NotNull(GeoLocator.FromTimeZone("russian standard time"));
    }

    [Fact]
    public void FromTimeZone_UnknownOrEmpty_ReturnsNull()
    {
        Assert.Null(GeoLocator.FromTimeZone("Central Standard Time"));
        Assert.Null(GeoLocator.FromTimeZone(""));
        Assert.Null(GeoLocator.FromTimeZone(null));
    }

    [Fact]
    public void Detect_UsesFirstEndpointThatAnswers()
    {
        var calls = new List<string>();
        var point = GeoLocator.Detect(url =>
        {
            calls.Add(url);
            return url.Contains("ipapi.co")
                ? "{\"city\":\"Tomsk\",\"latitude\":56.4977,\"longitude\":84.9744}"
                : null;
        });

        Assert.NotNull(point);
        Assert.Equal("Tomsk", point!.City);
        Assert.Single(calls);
    }

    [Fact]
    public void Detect_SkipsBrokenEndpointAndFallsThrough()
    {
        var point = GeoLocator.Detect(url =>
        {
            if (url.Contains("ipapi.co")) throw new InvalidOperationException("нет сети");
            if (url.Contains("ipwho.is")) return "{\"success\":false}";
            return "{\"city\":\"Omsk\",\"latitude\":\"54.9885\",\"longitude\":\"73.3242\"}";
        });

        Assert.NotNull(point);
        Assert.Equal("Omsk", point!.City);
    }

    [Fact]
    public void Detect_AllEndpointsFail_UsesTimeZoneFallback()
    {
        var point = GeoLocator.Detect(_ => throw new InvalidOperationException("нет сети"), "Russia Time Zone 4");

        Assert.NotNull(point);
        Assert.Equal("Екатеринбург", point!.City);
    }
}