using System.Text.Json;
using ApiAggregation.Models;

namespace ApiAggregation.Providers
{
  public sealed class OpenMeteoProvider : ICountrySectionProvider
  {
    #region Start Of: Constructors
    public OpenMeteoProvider(HttpClient httpClient)
    {
      this.httpClient = httpClient;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public string SectionName => "weather";
    #endregion End Of: Properties

    #region Start Of: Methods
    public async Task<Object> FetchAsync(CountryContext country, CancellationToken cancellationToken)
    {
      // Query strings use a decimal point regardless of Windows regional settings.
      var location = FormattableString.Invariant(
        $"latitude={country.Latitude}&longitude={country.Longitude}");
      var requestUri = $"v1/forecast?{location}" +
        "&current=temperature_2m,relative_humidity_2m,wind_speed_10m" +
        "&temperature_unit=celsius&wind_speed_unit=kmh&timeformat=unixtime";

      using var response = await this.httpClient.GetAsync(requestUri, cancellationToken);
      response.EnsureSuccessStatusCode();

      await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

      var root = document.RootElement;

      // Check that the provider supplied the four values our model needs.
      if (root.ValueKind != JsonValueKind.Object ||
          !root.TryGetProperty("current", out var current) ||
          current.ValueKind != JsonValueKind.Object ||
          !current.TryGetProperty("time", out var time) ||
          time.ValueKind != JsonValueKind.Number ||
          !time.TryGetInt64(out var unixSeconds) ||
          !current.TryGetProperty("temperature_2m", out var temperature) ||
          temperature.ValueKind != JsonValueKind.Number ||
          !temperature.TryGetDouble(out var temperatureCelsius) ||
          !current.TryGetProperty("relative_humidity_2m", out var humidity) ||
          humidity.ValueKind != JsonValueKind.Number ||
          !humidity.TryGetInt32(out var relativeHumidityPercent) ||
          !current.TryGetProperty("wind_speed_10m", out var windSpeed) ||
          windSpeed.ValueKind != JsonValueKind.Number ||
          !windSpeed.TryGetDouble(out var windSpeedKph))
      {
        throw new InvalidDataException("Open-Meteo did not return the expected weather fields.");
      }

      var observedAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

      return new WeatherData(observedAtUtc, temperatureCelsius, relativeHumidityPercent, windSpeedKph);
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly HttpClient httpClient;
    #endregion End Of: Fields
  }
}
