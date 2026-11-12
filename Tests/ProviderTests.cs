using ApiAggregation.Models;
using ApiAggregation.Providers;
using Xunit;

namespace ApiAggregation.Tests
{
  public sealed class ProviderTests
  {
    #region Start Of: Methods
    [Fact]
    public async Task WorldBankReadsProfileAndCatalogCoordinates()
    {
      using var client = CreateClient("https://api.worldbank.org/", ProviderResponses.Create);
      var provider = new WorldBankClient(client);

      var profile = await provider.GetCountryAsync("GR", CancellationToken.None);
      var catalog = await provider.GetCapitalCatalogAsync(CancellationToken.None);

      Assert.NotNull(profile);
      Assert.Equal("Athens", profile.Capital);
      Assert.Equal(37.9792, profile.Latitude);
      Assert.Equal(23.7166, profile.Longitude);
      Assert.Equal(profile.Latitude, catalog["gr"].Latitude);
      Assert.Equal(profile.Longitude, catalog["gr"].Longitude);
      Assert.False(catalog.ContainsKey("AQ"));
      Assert.Null(await provider.GetCountryAsync("ZZ", CancellationToken.None));
    }

    [Fact]
    public async Task OpenMeteoMapsCurrentWeatherAndUtcTime()
    {
      using var client = CreateClient("https://api.open-meteo.com/", ProviderResponses.Create);
      var provider = new OpenMeteoProvider(client);
      var country = new CountryContext("GR", 37.9792, 23.7166);

      var weather = Assert.IsType<WeatherData>(await provider.FetchAsync(country, CancellationToken.None));

      Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), weather.ObservedAtUtc);
      Assert.Equal(22.5, weather.TemperatureCelsius);
      Assert.Equal(64, weather.RelativeHumidityPercent);
      Assert.Equal(12.4, weather.WindSpeedKph);
    }

    [Fact]
    public async Task OpenMeteoRejectsIncompleteWeather()
    {
      using var client = CreateClient("https://api.open-meteo.com/",
        _ => ProviderResponses.Json(new { current = new { temperature_2m = 22.5 } }));
      var provider = new OpenMeteoProvider(client);
      var country = new CountryContext("GR", 37.9792, 23.7166);

      await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchAsync(country, CancellationToken.None));
    }

    [Fact]
    public async Task SunriseSunsetPreservesLocalOffsetAndNullableEvents()
    {
      using var client = CreateClient("https://api.sunrise-sunset.org/", _ => ProviderResponses.Json(new
      {
        date = "2026-05-20",
        tzid = "Europe/Oslo",
        sunrise = "2026-05-20T01:00:00+02:00",
        sunset = (String?)null,
        day_length = (Int32?)null
      }));
      var provider = new SunriseSunsetProvider(client);
      var country = new CountryContext("NO", 69.65, 18.96);

      var daylight = Assert.IsType<DaylightData>(await provider.FetchAsync(country, CancellationToken.None));

      Assert.Equal(new DateOnly(2026, 5, 20), daylight.Date);
      Assert.Equal(TimeSpan.FromHours(2), daylight.Sunrise?.Offset);
      Assert.Null(daylight.Sunset);
      Assert.Null(daylight.DaylightSeconds);
      Assert.Equal("Europe/Oslo", daylight.TimeZoneId);
      Assert.Equal("https://sunrise-sunset.org/", daylight.SourceUrl);
    }

    private static HttpClient CreateClient(String baseAddress, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
      return new HttpClient(new StubHttpMessageHandler((request, _) => Task.FromResult(respond(request))))
      {
        BaseAddress = new Uri(baseAddress)
      };
    }
    #endregion End Of: Methods
  }
}