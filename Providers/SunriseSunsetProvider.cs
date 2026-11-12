using System.Globalization;
using System.Text.Json;
using ApiAggregation.Models;

namespace ApiAggregation.Providers
{
  public sealed class SunriseSunsetProvider : ICountrySectionProvider
  {
    #region Start Of: Constructors
    public SunriseSunsetProvider(HttpClient httpClient)
    {
      this.httpClient = httpClient;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public string SectionName => "daylight";
    #endregion End Of: Properties

    #region Start Of: Methods
    public async Task<Object> FetchAsync(CountryContext country, CancellationToken cancellationToken)
    {
      var requestUri = FormattableString.Invariant($"v2?lat={country.Latitude}&lng={country.Longitude}");

      using var response = await this.httpClient.GetAsync(requestUri, cancellationToken);
      response.EnsureSuccessStatusCode();

      await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

      var root = document.RootElement;

      // The date describes the capital's local calendar day.
      if (root.ValueKind != JsonValueKind.Object ||
          !root.TryGetProperty("date", out var dateValue) ||
          dateValue.ValueKind != JsonValueKind.String ||
          !DateOnly.TryParseExact(dateValue.GetString(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
          !root.TryGetProperty("day_length", out var dayLength))
      {
        throw new InvalidDataException("Sunrise-Sunset did not return the expected daylight fields.");
      }

      // A null duration is valid when only one of the two solar events occurs.
      Int32? daylightSeconds = null;

      if (dayLength.ValueKind != JsonValueKind.Null)
      {
        if (dayLength.ValueKind != JsonValueKind.Number ||
            !dayLength.TryGetInt32(out var seconds))
        {
          throw new InvalidDataException("Sunrise-Sunset returned an invalid day length.");
        }

        daylightSeconds = seconds;
      }

      var sunrise = ReadEventTime(root, "sunrise");
      var sunset = ReadEventTime(root, "sunset");

      var timeZoneId = root.TryGetProperty("tzid", out var timeZoneValue) && timeZoneValue.ValueKind == JsonValueKind.String ? timeZoneValue.GetString() : null;
      return new DaylightData(date, sunrise, sunset, daylightSeconds, "https://sunrise-sunset.org/")
      {
        TimeZoneId = timeZoneId
      };
    }

    private static DateTimeOffset? ReadEventTime(JsonElement root, String propertyName)
    {
      if (root.TryGetProperty(propertyName, out var value))
      {
        if (value.ValueKind == JsonValueKind.Null)
        {
          return null;
        }

        if (value.ValueKind == JsonValueKind.String &&
            value.TryGetDateTimeOffset(out var eventTime))
        {
          return eventTime;
        }
      }

      throw new InvalidDataException($"Sunrise-Sunset returned an invalid {propertyName}.");
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly HttpClient httpClient;
    #endregion End Of: Fields
  }
}
