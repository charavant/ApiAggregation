using System.Globalization;
using System.Text.Json;
using ApiAggregation.Models;

namespace ApiAggregation.Providers
{
  public sealed class WorldBankClient : ICountrySectionProvider
  {
    #region Start Of: Constructors
    public WorldBankClient(HttpClient httpClient)
    {
      this.httpClient = httpClient;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public String SectionName => "country";
    #endregion End Of: Properties

    #region Start Of: Methods
    public async Task<CountryData?> GetCountryAsync(string code, CancellationToken cancellationToken)
    {
      // The registered HttpClient supplies the World Bank base address and timeout.
      // Pass the request's cancellation token through to the external API.
      using var response = await httpClient.GetAsync($"v2/country/{code}?format=json", cancellationToken);
      response.EnsureSuccessStatusCode();

      // Read the JSON body while the response is open. The using declarations release
      // the response, stream, and parsed document when the method exits.
      await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
      var root = document.RootElement;

      // World Bank can return HTTP 200 with error code 120 for an unknown country.
      if (IsUnknownCountry(root))
      {
        return null;
      }

      // A successful response has metadata at index 0 and country records at index 1.
      if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 2 || root[1].ValueKind != JsonValueKind.Array)
      {
        throw new InvalidDataException("Unexpected World Bank response.");
      }

      var countries = root[1];

      if (countries.GetArrayLength() == 0)
      {
        return null;
      }

      var row = countries[0];
      var name = ReadText(row, "name");

      if (string.IsNullOrWhiteSpace(name))
      {
        throw new InvalidDataException("World Bank did not return a country name.");
      }

      // Map the provider's field names and nested values to our response model.
      return new CountryData(
          name,
          ReadText(row, "capitalCity"),
          row.TryGetProperty("region", out var region) ? ReadText(region, "value") : null,
          row.TryGetProperty("incomeLevel", out var incomeLevel) ? ReadText(incomeLevel, "value") : null,
          ReadCoordinate(row, "latitude", 90),
          ReadCoordinate(row, "longitude", 180));
    }

    private static bool IsUnknownCountry(JsonElement root)
    {
      return root.ValueKind == JsonValueKind.Array &&
             root.GetArrayLength() == 1 &&
             root[0].ValueKind == JsonValueKind.Object &&
             root[0].TryGetProperty("message", out var messages) &&
             messages.ValueKind == JsonValueKind.Array &&
             messages.GetArrayLength() > 0 &&
             ReadText(messages[0], "id") == "120";
    }

    private static string? ReadText(JsonElement element, string propertyName)
    {
      if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
      {
        return property.GetString();
      }

      return null;
    }

    private static double? ReadCoordinate(JsonElement element, string propertyName, double limit)
    {
      // World Bank sends coordinates as strings with a decimal point. Parse them
      // independently of Windows regional settings and reject values outside the range.
      var text = ReadText(element, propertyName);

      if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var coordinate)
        && coordinate >= -limit && coordinate <= limit)
      {
        return coordinate;
      }

      return null;
    }

    public async Task<Object> FetchAsync(CountryContext country, CancellationToken cancellationToken)
    {
      return await this.GetCountryAsync(country.Code, cancellationToken) ?? throw new InvalidDataException($"World Bank did not return the profile for {country.Code}.");
    }

    public async Task<IReadOnlyDictionary<String, CountryContext>> GetCapitalCatalogAsync(CancellationToken cancellationToken)
    {
      using var response = await this.httpClient.GetAsync("v2/country?format=json&per_page=400", cancellationToken);
      response.EnsureSuccessStatusCode();

      await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

      var root = document.RootElement;

      if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 2 || root[1].ValueKind != JsonValueKind.Array)
      {
        throw new InvalidDataException("World Bank returned an invalid capital catalog.");
      }

      var catalog = new Dictionary<String, CountryContext>(StringComparer.OrdinalIgnoreCase);

      foreach (var row in root[1].EnumerateArray())
      {
        var code = ReadText(row, "iso2Code");
        var capital = ReadText(row, "capitalCity");

        if (code is { Length: 2 }
          && !String.IsNullOrWhiteSpace(capital)
          && ReadCoordinate(row, "latitude", 90) is Double latitude
          && ReadCoordinate(row, "longitude", 180) is Double longitude)
        {
          catalog[code] = new CountryContext(code, latitude, longitude);
        }
      }

      return catalog;
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly HttpClient httpClient;
    #endregion End Of: Fields
  }
}
