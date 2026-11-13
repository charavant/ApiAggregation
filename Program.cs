using System.Text.Json;
using ApiAggregation.Models;
using ApiAggregation.Providers;
using ApiAggregation.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var logPath = Path.Combine(builder.Environment.ContentRootPath, "logs", "log-.txt");
builder.Services.AddSerilog((context, configuration) =>
{
  configuration
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
});

const String worldBankName = "World Bank";
const String openMeteoName = "Open-Meteo";
const String sunriseSunsetName = "Sunrise-Sunset";

builder.Services.AddSingleton(new ProviderRequestStatisticsService(new[] { worldBankName, openMeteoName, sunriseSunsetName }));

builder.Services.AddHttpClient<WorldBankClient>(client =>
{
  client.BaseAddress = new Uri("https://api.worldbank.org/");
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler(services => new TimingHandler(worldBankName, services.GetRequiredService<ProviderRequestStatisticsService>()));

builder.Services.AddHttpClient<OpenMeteoProvider>(client =>
{
  client.BaseAddress = new Uri("https://api.open-meteo.com/");
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler(services => new TimingHandler(openMeteoName, services.GetRequiredService<ProviderRequestStatisticsService>()));

builder.Services.AddHttpClient<SunriseSunsetProvider>(client =>
{
  client.BaseAddress = new Uri("https://api.sunrise-sunset.org/");
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler(services => new TimingHandler(sunriseSunsetName, services.GetRequiredService<ProviderRequestStatisticsService>()));

builder.Services.AddTransient<ICountrySectionProvider>(services => services.GetRequiredService<OpenMeteoProvider>());
builder.Services.AddTransient<ICountrySectionProvider>(services => services.GetRequiredService<SunriseSunsetProvider>());
builder.Services.AddTransient<ApiAggregationService>();

builder.Services.AddTransient<ICountrySectionProvider>(services => services.GetRequiredService<WorldBankClient>());
var capitalDictionary = new Dictionary<String, CountryContext>(StringComparer.OrdinalIgnoreCase);
builder.Services.AddSingleton<IReadOnlyDictionary<String, CountryContext>>(capitalDictionary);
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<ProviderPerformanceMonitorService>();

var app = builder.Build();

// Load coordinates before serving requests so the three providers can start together.
// A failed load leaves the dictionary empty and the service resolves countries individually.
try
{
  using var scope = app.Services.CreateScope();
  var worldBank = scope.ServiceProvider.GetRequiredService<WorldBankClient>();
  var startupCatalog = await worldBank.GetCapitalCatalogAsync(CancellationToken.None);

  foreach (var entry in startupCatalog)
  {
    capitalDictionary.Add(entry.Key, entry.Value);
  }

  app.Logger.LogInformation("Loaded capital coordinates for {Count} countries.", capitalDictionary.Count);
}
catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException)
{
  app.Logger.LogWarning(exception, "Could not load capital coordinates. Using per-country lookups.");
}

app.MapGet("/api/countries/{code}", async (String code, ApiAggregationService aggregationService, CancellationToken cancellationToken) =>
{
  var normalizedCode = code.ToUpperInvariant();

  if (!IsCountryCode(normalizedCode))
  {
    return Results.BadRequest(new { message = "Use a two-letter country code." });
  }

  var (overview, error) = await LookupCountryAsync(normalizedCode, aggregationService, cancellationToken);

  if (error is not null)
  {
    return error;
  }

  return overview!.Sections.Count == 0 ? Results.Json(overview, statusCode: 503) : Results.Ok(overview);
});

app.MapGet("/api/countries", async (String? codes, String? sections, String? sort, String? order, ApiAggregationService aggregationService, CancellationToken cancellationToken) =>
{
  var countryCodes = (codes ?? String.Empty)
    .Split(',', StringSplitOptions.TrimEntries)
    .Select(code => code.ToUpperInvariant())
    .Distinct(StringComparer.Ordinal)
    .ToArray();

  if (countryCodes.Length > 5 || countryCodes.Any(code => !IsCountryCode(code)))
  {
    return Results.BadRequest(new { message = "Supply one to five distinct two-letter country codes." });
  }

  var sortKey = (sort ?? "name").Trim().ToLowerInvariant();
  var sortOrder = (order ?? "asc").Trim().ToLowerInvariant();

  if (sortKey is not ("name" or "temperature") || sortOrder is not ("asc" or "desc"))
  {
    return Results.BadRequest(new { message = "Use sort=name or temperature and order=asc or desc." });
  }

  var availableSections = aggregationService.SectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
  var selectedSections = sections is null ? availableSections : sections.Split(',', StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

  if (!selectedSections.IsSubsetOf(availableSections))
  {
    return Results.BadRequest(new { message = "Use section names from the available sections.", availableSections });
  }

  var countries = new List<CountryOverview>();

  foreach (var code in countryCodes)
  {
    var (overview, error) = await LookupCountryAsync(code, aggregationService, cancellationToken);

    if (error is not null)
    {
      return error;
    }

    if (overview is not null)
    {
      countries.Add(overview);
    }
  }

  // Sort full overviews before choosing which sections to return.
  var sortedCountries = (sortKey, sortOrder) switch
  {
    ("name", "asc") => countries.OrderBy(GetCountryName, StringComparer.OrdinalIgnoreCase),
    ("name", "desc") => countries.OrderByDescending(GetCountryName, StringComparer.OrdinalIgnoreCase),
    ("temperature", "asc") => countries.OrderBy(country => GetTemperature(country) is null).ThenBy(GetTemperature),
    _ => countries.OrderBy(country => GetTemperature(country) is null).ThenByDescending(GetTemperature)
  };

  var response = sortedCountries
    .ThenBy(country => country.CountryCode, StringComparer.Ordinal)
    .Select(country => country with
    {
      Sections = country.Sections.Where(section => selectedSections.Contains(section.Key)).ToDictionary(section => section.Key, section => section.Value),
      SectionStatuses = country.SectionStatuses.Where(section => selectedSections.Contains(section.Key)).ToDictionary(section => section.Key, section => section.Value)
    })
    .ToArray();

  return response.All(country => country.Sections.Count == 0) ? Results.Json(response, statusCode: 503) : Results.Ok(response);
});

app.MapGet("/api/stats", (ProviderRequestStatisticsService statisticsService) => Results.Ok(statisticsService.GetSnapshot()));

app.Run();

static Boolean IsCountryCode(String code)
{
  return code.Length == 2 && code.All(letter => letter is >= 'A' and <= 'Z');
}

static async Task<(CountryOverview? Overview, IResult? Error)> LookupCountryAsync(String code, ApiAggregationService aggregationService, CancellationToken cancellationToken)
{
  try
  {
    var (overview, missingCoordinates) = await aggregationService.GetAsync(code, cancellationToken);

    if (missingCoordinates)
    {
      return (null, Results.Problem(title: $"Capital coordinates are unavailable for {code}", statusCode: 422));
    }

    if (overview is null)
    {
      return (null, Results.NotFound(new { message = $"Unknown country code: {code}." }));
    }

    return (overview, null);
  }
  catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
  {
    return (null, Results.Problem(title: $"Country overview lookup timed out for {code}", statusCode: 504));
  }
  catch (Exception exception) when (
    exception is HttpRequestException or JsonException or InvalidDataException)
  {
    return (null, Results.Problem(title: $"Country overview lookup failed for {code}", statusCode: 502));
  }
}

static String GetCountryName(CountryOverview country)
{
  return country.Sections.TryGetValue("country", out var section) && section is CountryData profile ? profile.Name : country.CountryCode;
}

static Double? GetTemperature(CountryOverview country)
{
  if (country.Sections.TryGetValue("weather", out var section) && section is WeatherData weather)
  {
    return weather.TemperatureCelsius;
  }

  return null;
}