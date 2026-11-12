using ApiAggregation.Models;
using ApiAggregation.Providers;
using Microsoft.Extensions.Caching.Memory;

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
    public async Task<(CountryOverview? Overview, Boolean MissingCoordinates)> GetAsync(String code, CancellationToken cancellationToken)
    {
      CountryData? countryFromLookup = null;

      if (!this.capitalDictionary.TryGetValue(code, out var countryContext))
      {
        var countryCacheKey = (this.worldBank.SectionName, code);

        if (!this.memoryCache.TryGetValue<CountryData>(countryCacheKey, out countryFromLookup))
        {
          countryFromLookup = await this.worldBank.GetCountryAsync(code, cancellationToken);

          if (countryFromLookup is not null)
          {
            this.memoryCache.Set(countryCacheKey, countryFromLookup, TimeSpan.FromHours(24));
          }
        }

        if (countryFromLookup is null)
        {
          return (null, false);
        }

        if (countryFromLookup.Latitude is not Double latitude ||
            countryFromLookup.Longitude is not Double longitude)
        {
          return (null, true);
        }

        countryContext = new CountryContext(code, latitude, longitude);
      }

      var sections = new Dictionary<String, Object>();
      IEnumerable<ICountrySectionProvider> providers = this.sectionProviders;

      if (countryFromLookup is not null)
      {
        // Reuse the profile we fetched to resolve the coordinates.
        sections.Add(this.worldBank.SectionName, countryFromLookup);
        providers = providers.Where(provider => provider.SectionName != this.worldBank.SectionName);
      }

      var sectionTasks = providers.Select(async provider =>
      {
        var data = await this.GetSectionAsync(provider, countryContext, cancellationToken);
        return (Name: provider.SectionName, Data: data);
      }).ToArray();

      var providerSections = await Task.WhenAll(sectionTasks);

      foreach (var section in providerSections)
      {
        sections.Add(section.Name, section.Data);
      }

      return (new CountryOverview(code, sections), false);
    }

    private async Task<Object> GetSectionAsync(ICountrySectionProvider provider, CountryContext country, CancellationToken cancellationToken)
    {
      if (provider.SectionName == "daylight")
      {
        return await this.GetDaylightAsync(provider, country, cancellationToken);
      }

      var cacheKey = (provider.SectionName, country.Code);

      if (this.memoryCache.TryGetValue<Object>(cacheKey, out var cachedData) && cachedData is not null)
      {
        return cachedData;
      }

      var data = await provider.FetchAsync(country, cancellationToken);
      var lifetime = TimeSpan.FromHours(24);

      this.memoryCache.Set(cacheKey, data, lifetime);

      return data;
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

      var data = await provider.FetchAsync(country, cancellationToken);

      if (data is DaylightData daylight && daylight.TimeZoneId is { Length: > 0 } timeZoneId && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var resolvedTimeZone))
      {
        this.memoryCache.Set(timeZoneKey, resolvedTimeZone, TimeSpan.FromDays(7));
        this.memoryCache.Set((locationKey, daylight.Date), daylight, TimeSpan.FromDays(1));
      }

      return data;
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