using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiAggregation.Tests
{
  internal sealed class ApiTestFactory : WebApplicationFactory<Program>
  {
    #region Start Of: Constructors
    public ApiTestFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? sendAsync = null)
    {
      this.sendAsync = sendAsync ?? ((request, _) => Task.FromResult(ProviderResponses.Create(request)));
    }
    #endregion End Of: Constructors

    #region Start Of: Methods
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      // Give the real JWT handler a test-only signing key without loading developer secrets.
      builder.UseEnvironment("Testing");
      builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(TestJwt.Configuration));

      // Replace the network transport while keeping the real clients and timing handlers.
      builder.ConfigureTestServices(services =>
        services.ConfigureHttpClientDefaults(client =>
          client.ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(this.sendAsync))));
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync;
    #endregion End Of: Fields
  }

  internal sealed class StubHttpMessageHandler : HttpMessageHandler
  {
    #region Start Of: Constructors
    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
      this.sendAsync = sendAsync;
    }
    #endregion End Of: Constructors

    #region Start Of: Methods
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      return this.sendAsync(request, cancellationToken);
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync;
    #endregion End Of: Fields
  }

  internal static class ProviderResponses
  {
    #region Start Of: Methods
    public static HttpResponseMessage Create(HttpRequestMessage request)
    {
      var uri = request.RequestUri!;

      return (uri.Host, uri.AbsolutePath) switch
      {
        ("api.worldbank.org", "/v2/country") => WorldBank(countries),
        ("api.worldbank.org", _) => WorldBankProfile(uri.Segments[^1]),
        ("api.open-meteo.com", "/v1/forecast") => Weather(uri.Query),
        ("api.sunrise-sunset.org", "/v2") => Daylight(DateOnly.FromDateTime(DateTime.UtcNow)),
        _ => throw new InvalidOperationException($"Unexpected test request to {uri}.")
      };
    }

    public static HttpResponseMessage Daylight(DateOnly date)
    {
      return Json(new
      {
        date,
        tzid = "Etc/UTC",
        sunrise = FormattableString.Invariant($"{date:yyyy-MM-dd}T06:00:00+00:00"),
        sunset = FormattableString.Invariant($"{date:yyyy-MM-dd}T18:00:00+00:00"),
        day_length = 43_200
      });
    }

    public static HttpResponseMessage Json(Object body)
    {
      return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private static HttpResponseMessage WorldBank(IEnumerable<(String Code, String Name, String Capital, String Latitude, String Longitude)> records)
    {
      var rows = records.Select(country => new
      {
        iso2Code = country.Code,
        name = country.Name,
        capitalCity = country.Capital,
        latitude = country.Latitude,
        longitude = country.Longitude,
        region = new { value = "Europe & Central Asia" },
        incomeLevel = new { value = "High income" }
      }).ToArray();

      return Json(new Object[] { new { page = 1 }, rows });
    }

    private static HttpResponseMessage WorldBankProfile(String code)
    {
      var matching = countries.Where(country => country.Code == code).ToArray();

      return matching.Length > 0
        ? WorldBank(matching)
        : Json(new[] { new { message = new[] { new { id = "120" } } } });
    }

    private static HttpResponseMessage Weather(String query)
    {
      // Distinct values give the list endpoint an observable temperature sort order.
      var temperature = query.Contains("latitude=37.9792&", StringComparison.Ordinal) ? 22.5 :
        query.Contains("latitude=48.8566&", StringComparison.Ordinal) ? 15.0 : 9.0;

      return Json(new
      {
        current = new
        {
          time = 1_700_000_000,
          temperature_2m = temperature,
          relative_humidity_2m = 64,
          wind_speed_10m = 12.4
        }
      });
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private static readonly (String Code, String Name, String Capital, String Latitude, String Longitude)[] countries =
    {
      ("GR", "Greece", "Athens", "37.9792", "23.7166"),
      ("FR", "France", "Paris", "48.8566", "2.3522"),
      ("DE", "Germany", "Berlin", "52.52", "13.405"),
      ("AQ", "Antarctica", "", "", "")
    };
    #endregion End Of: Fields
  }
}