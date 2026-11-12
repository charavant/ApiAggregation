using System.Text.Json;
using ApiAggregation.Models;
using ApiAggregation.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<WorldBankClient>(client =>
{
  client.BaseAddress = new Uri("https://api.worldbank.org/");
  client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<OpenMeteoProvider>(client =>
{
  client.BaseAddress = new Uri("https://api.open-meteo.com/");
  client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<SunriseSunsetProvider>(client =>
{
  client.BaseAddress = new Uri("https://api.sunrise-sunset.org/");
  client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddTransient<ICountrySectionProvider>(services => services.GetRequiredService<OpenMeteoProvider>());
builder.Services.AddTransient<ICountrySectionProvider>(services => services.GetRequiredService<SunriseSunsetProvider>());
var app = builder.Build();

app.MapGet("/api/countries/{code}", async (String code, WorldBankClient worldBank, IEnumerable<ICountrySectionProvider> sectionProviders, CancellationToken cancellationToken) =>
{
  var normalizedCode = code.ToUpperInvariant();

  if (normalizedCode.Length != 2 || normalizedCode.Any(letter => letter is < 'A' or > 'Z'))
  {
    return Results.BadRequest(new { message = "Use a two-letter country code." });
  }

  try
  {
    var country = await worldBank.GetCountryAsync(normalizedCode, cancellationToken);

    if (country is null)
    {
      return Results.NotFound(new { message = $"Unknown country code: {normalizedCode}." });
    }

    var sections = new Dictionary<string, object>
    {
      ["country"] = country
    };

    if (country.Latitude is not Double latitude || country.Longitude is not Double longitude)
    {
      return Results.Problem(title: "Capital coordinates are unavailable", statusCode: 422);
    }

    var countryContext = new CountryContext(normalizedCode, latitude, longitude);

    // Start every provider before waiting for the results.
    var sectionTasks = sectionProviders.Select(async provider =>
    {
      var data = await provider.FetchAsync(countryContext, cancellationToken);
      return (Name: provider.SectionName, Data: data);
    }).ToArray();

    var providerSections = await Task.WhenAll(sectionTasks);

    // Add sections after the calls finish so the dictionary is updated in one place.
    foreach (var section in providerSections)
    {
      sections[section.Name] = section.Data;
    }

    return Results.Ok(new CountryOverview(normalizedCode, sections));
  }
  catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
  {
    return Results.Problem(title: "World Bank timed out", statusCode: 504);
  }
  catch (Exception exception) when (
      exception is HttpRequestException or JsonException or InvalidDataException)
  {
    return Results.Problem(title: "World Bank lookup failed", statusCode: 502);
  }
});

app.Run();
