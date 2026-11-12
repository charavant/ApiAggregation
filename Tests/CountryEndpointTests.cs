using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiAggregation.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApiAggregation.Tests
{
  public sealed class CountryEndpointTests
  {
    #region Start Of: Methods
    [Fact]
    public async Task CountryResponseUsesAllProvidersAndRepeatingItUsesTheCache()
    {
      using var factory = new ApiTestFactory();
      using var client = factory.CreateClient();

      var startup = await GetStatsAsync(client);
      Assert.Equal(1, startup["World Bank"].TotalRequests);
      Assert.Equal(0, startup["Open-Meteo"].TotalRequests);
      Assert.Equal(0, startup["Sunrise-Sunset"].TotalRequests);

      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/gr");

      Assert.Equal("GR", country.GetProperty("countryCode").GetString());
      var sections = country.GetProperty("sections");
      Assert.Equal("Athens", sections.GetProperty("country").GetProperty("capital").GetString());
      Assert.Equal(22.5, sections.GetProperty("weather").GetProperty("temperatureCelsius").GetDouble());
      Assert.Equal(43_200, sections.GetProperty("daylight").GetProperty("daylightSeconds").GetInt32());
      Assert.All(country.GetProperty("sectionStatuses").EnumerateObject(),
        section => Assert.Equal("fresh", section.Value.GetString()));

      var afterFirstRequest = await GetStatsAsync(client);
      Assert.All(afterFirstRequest, provider =>
        Assert.Equal(startup[provider.Key].TotalRequests + 1, provider.Value.TotalRequests));

      var repeated = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");
      var afterRepeat = await GetStatsAsync(client);

      Assert.True(JsonElement.DeepEquals(country, repeated));
      Assert.All(afterRepeat, provider =>
        Assert.Equal(afterFirstRequest[provider.Key].TotalRequests, provider.Value.TotalRequests));
    }

    [Theory]
    [InlineData("name", "asc", "FR,DE,GR")]
    [InlineData("name", "desc", "GR,DE,FR")]
    [InlineData("temperature", "asc", "DE,FR,GR")]
    [InlineData("temperature", "desc", "GR,FR,DE")]
    public async Task ListNormalizesCodesAndSortsBeforeSelectingSections(
      String sort, String order, String expectedCodes)
    {
      using var factory = new ApiTestFactory();
      using var client = factory.CreateClient();
      var countries = await client.GetFromJsonAsync<JsonElement[]>(
        $"/api/countries?codes=gr,GR,FR,DE&sections=daylight&sort={sort}&order={order}");

      Assert.NotNull(countries);
      Assert.Equal(expectedCodes.Split(','), countries.Select(country => country.GetProperty("countryCode").GetString()));
      Assert.All(countries, country =>
      {
        Assert.Equal("daylight", Assert.Single(country.GetProperty("sections").EnumerateObject()).Name);
        Assert.Equal("daylight", Assert.Single(country.GetProperty("sectionStatuses").EnumerateObject()).Name);
      });
    }

    [Theory]
    [InlineData("/api/countries/GRC")]
    [InlineData("/api/countries")]
    [InlineData("/api/countries?codes=GR,")]
    [InlineData("/api/countries?codes=GR,FR,DE,GB,IT,ES")]
    [InlineData("/api/countries?codes=GR&sort=population")]
    [InlineData("/api/countries?codes=GR&order=sideways")]
    [InlineData("/api/countries?codes=GR&sections=population")]
    public async Task InvalidInputDoesNotTriggerProviderCalls(String requestUri)
    {
      using var factory = new ApiTestFactory();
      using var client = factory.CreateClient();
      using var response = await client.GetAsync(requestUri);

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      var stats = await GetStatsAsync(client);
      Assert.Equal(1, stats.Values.Sum(provider => provider.TotalRequests)); // Startup catalog only.
    }

    [Theory]
    [InlineData("ZZ", HttpStatusCode.NotFound)]
    [InlineData("AQ", HttpStatusCode.UnprocessableEntity)]
    public async Task CatalogMissPreservesUnknownCountryAndMissingCoordinatesResponses(
      String code, HttpStatusCode expectedStatus)
    {
      using var factory = new ApiTestFactory();
      using var client = factory.CreateClient();
      using var response = await client.GetAsync($"/api/countries/{code}");

      Assert.Equal(expectedStatus, response.StatusCode);
      var stats = await GetStatsAsync(client);
      Assert.Equal(2, stats["World Bank"].TotalRequests);
      Assert.Equal(0, stats["Open-Meteo"].TotalRequests);
      Assert.Equal(0, stats["Sunrise-Sunset"].TotalRequests);
    }

    [Fact]
    public async Task CatalogFailureFallsBackToOneProfileLookup()
    {
      using var factory = new ApiTestFactory((request, _) => Task.FromResult(
        request.RequestUri!.AbsolutePath == "/v2/country"
          ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
          : ProviderResponses.Create(request)));
      using var client = factory.CreateClient();
      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");

      Assert.Equal(3, country.GetProperty("sections").EnumerateObject().Count());
      var stats = await GetStatsAsync(client);
      Assert.Equal(2, stats["World Bank"].TotalRequests);
    }

    [Fact]
    public async Task AllThreeProvidersStartBeforeAnyOfThemFinishes()
    {
      var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var started = 0;

      using var factory = new ApiTestFactory(async (request, cancellationToken) =>
      {
        if (request.RequestUri!.AbsolutePath != "/v2/country")
        {
          // Hold each provider at the transport until all three have arrived.
          if (Interlocked.Increment(ref started) == 3)
          {
            allStarted.TrySetResult();
          }

          await allStarted.Task.WaitAsync(cancellationToken);
        }

        return ProviderResponses.Create(request);
      });
      using var client = factory.CreateClient();
      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");

      Assert.Equal(3, started);
      Assert.Equal(3, country.GetProperty("sections").EnumerateObject().Count());
    }

    [Fact]
    public async Task WeatherOutageIsRetriedAndReturnsPartialData()
    {
      using var factory = new ApiTestFactory((request, _) => Task.FromResult(
        request.RequestUri!.Host == "api.open-meteo.com"
          ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
          : ProviderResponses.Create(request)));
      using var client = factory.CreateClient();

      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");
      Assert.Equal("unavailable", country.GetProperty("sectionStatuses").GetProperty("weather").GetString());
      var sections = country.GetProperty("sections");
      Assert.True(sections.TryGetProperty("country", out _));
      Assert.True(sections.TryGetProperty("daylight", out _));
      Assert.False(sections.TryGetProperty("weather", out _));

      var stats = await GetStatsAsync(client);
      Assert.Equal(2, stats["Open-Meteo"].TotalRequests);
      Assert.Equal(2, stats["Open-Meteo"].Buckets.Values.Sum(bucket => bucket.Count));

      using var weatherOnly = await client.GetAsync("/api/countries?codes=GR&sections=weather");
      Assert.Equal(HttpStatusCode.ServiceUnavailable, weatherOnly.StatusCode);
      var countries = await weatherOnly.Content.ReadFromJsonAsync<JsonElement[]>();
      var unavailable = Assert.Single(countries!);
      Assert.Empty(unavailable.GetProperty("sections").EnumerateObject());
      Assert.Equal("unavailable", unavailable.GetProperty("sectionStatuses").GetProperty("weather").GetString());
    }

    [Fact]
    public async Task NonTransientWeatherFailureIsNotRetried()
    {
      using var factory = new ApiTestFactory((request, _) => Task.FromResult(
        request.RequestUri!.Host == "api.open-meteo.com"
          ? new HttpResponseMessage(HttpStatusCode.BadRequest)
          : ProviderResponses.Create(request)));
      using var client = factory.CreateClient();

      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");

      Assert.Equal("unavailable", country.GetProperty("sectionStatuses").GetProperty("weather").GetString());
      var stats = await GetStatsAsync(client);
      Assert.Equal(1, stats["Open-Meteo"].TotalRequests);
    }

    [Fact]
    public async Task WeatherTimeoutIsCountedAndTheRetryCanRecover()
    {
      var weatherAttempts = 0;
      using var factory = new ApiTestFactory(async (request, cancellationToken) =>
      {
        if (request.RequestUri!.Host == "api.open-meteo.com" &&
            Interlocked.Increment(ref weatherAttempts) == 1)
        {
          // Longer than the attempt budget, but bounded if timeout propagation regresses.
          await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        return ProviderResponses.Create(request);
      });
      using var client = factory.CreateClient();

      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");

      Assert.Equal("fresh", country.GetProperty("sectionStatuses").GetProperty("weather").GetString());
      var stats = await GetStatsAsync(client);
      Assert.Equal(2, stats["Open-Meteo"].TotalRequests);
      Assert.True(stats["Open-Meteo"].Buckets["slow"].Count >= 1);
    }

    [Fact]
    public async Task CountryEndpointReturns503WhenEveryProviderFails()
    {
      using var factory = new ApiTestFactory((request, _) => Task.FromResult(
        request.RequestUri!.AbsolutePath == "/v2/country"
          ? ProviderResponses.Create(request)
          : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
      using var client = factory.CreateClient();
      using var response = await client.GetAsync("/api/countries/GR");

      Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
      var body = await response.Content.ReadFromJsonAsync<JsonElement>();
      Assert.Empty(body.GetProperty("sections").EnumerateObject());
      var statuses = body.GetProperty("sectionStatuses");
      Assert.Equal("unavailable", statuses.GetProperty("country").GetString());
      Assert.Equal("unavailable", statuses.GetProperty("weather").GetString());
      Assert.Equal("unavailable", statuses.GetProperty("daylight").GetString());
      var stats = await GetStatsAsync(client);
      Assert.Equal(3, stats["World Bank"].TotalRequests); // Includes startup.
      Assert.Equal(2, stats["Open-Meteo"].TotalRequests);
      Assert.Equal(2, stats["Sunrise-Sunset"].TotalRequests);
    }

    [Fact]
    public async Task WeatherRefreshFailureUsesLastKnownData()
    {
      using var factory = new ApiTestFactory((request, _) => Task.FromResult(
        request.RequestUri!.Host == "api.open-meteo.com"
          ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
          : ProviderResponses.Create(request)));
      using var client = factory.CreateClient();
      var cache = factory.Services.GetRequiredService<IMemoryCache>();
      var fetchedAt = DateTimeOffset.UtcNow.AddHours(-24.5);

      // Seed a retained value past the freshness window without waiting a day.
      Object lastKnown = new WeatherData(fetchedAt, 19.5, 61, 11.0);
      cache.Set(("weather", "GR"), (Data: lastKnown, FetchedAt: fetchedAt), TimeSpan.FromMinutes(30));

      var country = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");

      Assert.Equal("stale", country.GetProperty("sectionStatuses").GetProperty("weather").GetString());
      Assert.Equal(19.5, country.GetProperty("sections").GetProperty("weather").GetProperty("temperatureCelsius").GetDouble());
      var stats = await GetStatsAsync(client);
      Assert.Equal(2, stats["Open-Meteo"].TotalRequests);
    }

    [Fact]
    public async Task DaylightRefreshesWhenTheCachedDateIsYesterday()
    {
      var daylightCalls = 0;
      var today = DateOnly.FromDateTime(DateTime.UtcNow);
      using var factory = new ApiTestFactory((request, _) => Task.FromResult(
        request.RequestUri!.Host == "api.sunrise-sunset.org"
          ? ProviderResponses.Daylight(Interlocked.Increment(ref daylightCalls) == 1 ? today.AddDays(-1) : today)
          : ProviderResponses.Create(request)));
      using var client = factory.CreateClient();
      var first = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");
      var refreshed = await client.GetFromJsonAsync<JsonElement>("/api/countries/GR");

      Assert.Equal(today.AddDays(-1), first.GetProperty("sections").GetProperty("daylight").GetProperty("date").Deserialize<DateOnly>());
      Assert.Equal(today, refreshed.GetProperty("sections").GetProperty("daylight").GetProperty("date").Deserialize<DateOnly>());
      var stats = await GetStatsAsync(client);
      Assert.Equal(2, stats["Sunrise-Sunset"].TotalRequests);
      Assert.Equal(1, stats["Open-Meteo"].TotalRequests);
    }

    private static async Task<Dictionary<String, ProviderRequestStatistics>> GetStatsAsync(HttpClient client)
    {
      var snapshot = await client.GetFromJsonAsync<Dictionary<String, ProviderRequestStatistics>>("/api/stats");
      return Assert.IsType<Dictionary<String, ProviderRequestStatistics>>(snapshot);
    }
    #endregion End Of: Methods
  }
}