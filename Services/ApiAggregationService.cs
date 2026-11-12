using ApiAggregation.Models;
using ApiAggregation.Providers;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace ApiAggregation.Services
{
  public sealed class ApiAggregationService
  {
    #region Start Of: Constructors
    public ApiAggregationService(WorldBankClient worldBank, IEnumerable<ICountrySectionProvider> sectionProviders, IReadOnlyDictionary<String, CountryContext> capitalDictionary, IMemoryCache memoryCache)
    {
      this.worldBank = worldBank;
      this.sectionProviders = sectionProviders.ToArray();
      this.capitalDictionary = capitalDictionary;
      this.memoryCache = memoryCache;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public IEnumerable<String> SectionNames
    {
      get
      {
        return this.sectionProviders.Select(provider => provider.SectionName);
      }
    }
    #endregion End Of: Properties

    #region Start Of: Methods
    private static async Task<T> FetchWithRetryAsync<T>(Func<CancellationToken, Task<T>> fetchAsync, CancellationToken cancellationToken)
    {
      for (var attempt = 1; ; attempt++)
      {
        // Each attempt gets a fresh timeout and can also be canceled by the caller.
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellation.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
          return await fetchAsync(attemptCancellation.Token);
        }
        catch (Exception exception) when (
          attempt == 1 &&
          !cancellationToken.IsCancellationRequested &&
          IsTransientFailure(exception))
        {
          // Give the upstream service a short pause before the second attempt.
          await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
      }
    }

    private static Boolean IsTransientFailure(Exception exception)
    {
      return exception switch
      {
        OperationCanceledException => true,
        HttpRequestException httpException =>
          (Int32?)httpException.StatusCode is null or 408 or >= 500,
        _ => false
      };
    }

    public async Task<(CountryOverview? Overview, Boolean MissingCoordinates)> GetAsync(String code, CancellationToken cancellationToken)
    {
      var sections = new Dictionary<String, Object>();
      var sectionStatuses = new Dictionary<String, String>();
      IEnumerable<ICountrySectionProvider> providers = this.sectionProviders;

      if (!this.capitalDictionary.TryGetValue(code, out var countryContext))
      {
        var (data, status) = await this.GetCachedDataAsync(this.worldBank.SectionName, code, async attemptToken => await this.worldBank.GetCountryAsync(code, attemptToken), cancellationToken);

        if (status == "unavailable")
        {
          // Without a profile or catalog entry, we cannot locate the capital.
          var unavailable = new CountryOverview(code, sections)
          {
            SectionStatuses = this.SectionNames.ToDictionary(name => name, name => "unavailable")
          };

          return (unavailable, false);
        }

        if (data is not CountryData country)
        {
          return (null, false);
        }

        if (country.Latitude is not Double latitude || country.Longitude is not Double longitude)
        {
          return (null, true);
        }

        countryContext = new CountryContext(code, latitude, longitude);
        sections.Add(this.worldBank.SectionName, country);
        sectionStatuses.Add(this.worldBank.SectionName, status);
        providers = providers.Where(provider => provider.SectionName != this.worldBank.SectionName);
      }

      var sectionTasks = providers.Select(async provider =>
      {
        var (data, status) = await this.GetSectionAsync(provider, countryContext, cancellationToken);
        return (Name: provider.SectionName, Data: data, Status: status);
      }).ToArray();

      var providerSections = await Task.WhenAll(sectionTasks);

      foreach (var section in providerSections)
      {
        sectionStatuses.Add(section.Name, section.Status);

        if (section.Data is not null)
        {
          sections.Add(section.Name, section.Data);
        }
      }

      return (new CountryOverview(code, sections) { SectionStatuses = sectionStatuses }, false);
    }

    private async Task<(Object? Data, String Status)> GetSectionAsync(ICountrySectionProvider provider, CountryContext country, CancellationToken cancellationToken)
    {
      if (provider.SectionName != "daylight")
      {
        return await this.GetCachedDataAsync(provider.SectionName, country.Code, async attemptToken => await provider.FetchAsync(country, attemptToken), cancellationToken);
      }

      try
      {
        var daylight = await this.GetDaylightAsync(provider, country, cancellationToken);
        return (daylight, "fresh");
      }
      catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
      {
        return (null, "unavailable");
      }
    }

    private async Task<Object> GetDaylightAsync(ICountrySectionProvider provider, CountryContext country, CancellationToken cancellationToken)
    {
      var locationKey = (provider.SectionName, country.Code, country.Latitude, country.Longitude);
      var timeZoneKey = (locationKey, "time-zone");

      if (this.memoryCache.TryGetValue<TimeZoneInfo>(timeZoneKey, out var timeZone) && timeZone is not null)
      {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);

        if (this.memoryCache.TryGetValue<DaylightData>((locationKey, localDate), out var cachedDaylight) && cachedDaylight is not null)
        {
          return cachedDaylight;
        }
      }

      var data = await FetchWithRetryAsync(attemptToken => provider.FetchAsync(country, attemptToken), cancellationToken);

      if (data is DaylightData daylight && daylight.TimeZoneId is { Length: > 0 } timeZoneId && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var resolvedTimeZone))
      {
        this.memoryCache.Set(timeZoneKey, resolvedTimeZone, TimeSpan.FromDays(7));
        this.memoryCache.Set((locationKey, daylight.Date), daylight, TimeSpan.FromDays(1));
      }

      return data;
    }

    private static Boolean IsProviderFailure(Exception exception, CancellationToken cancellationToken)
    {
      return !cancellationToken.IsCancellationRequested && exception is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException;
    }

    private async Task<(Object? Data, String Status)> GetCachedDataAsync(String sectionName, String code, Func<CancellationToken, Task<Object?>> fetchAsync, CancellationToken cancellationToken)
    {
      var cacheKey = (sectionName, code);
      var freshFor = TimeSpan.FromHours(24);

      // Keep the last successful value beyond its freshness window.
      var retainFor = sectionName == "country" ? TimeSpan.FromDays(7) : TimeSpan.FromHours(25);

      if (this.memoryCache.TryGetValue<(Object Data, DateTimeOffset FetchedAt)>(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < freshFor)
      {
        return (cached.Data, "fresh");
      }

      try
      {
        var data = await FetchWithRetryAsync(fetchAsync, cancellationToken);

        if (data is not null)
        {
          this.memoryCache.Set(cacheKey, (Data: data, FetchedAt: DateTimeOffset.UtcNow), retainFor);
        }

        return (data, "fresh");
      }
      catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
      {
        // Check the cache again because retention can expire during the retry.
        if (this.memoryCache.TryGetValue<(Object Data, DateTimeOffset FetchedAt)>(
            cacheKey, out var lastKnown))
        {
          return (lastKnown.Data, "stale");
        }

        return (null, "unavailable");
      }
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly WorldBankClient worldBank;
    private readonly IReadOnlyList<ICountrySectionProvider> sectionProviders;
    private readonly IReadOnlyDictionary<String, CountryContext> capitalDictionary;
    private readonly IMemoryCache memoryCache;
    #endregion End Of: Fields
  }
}