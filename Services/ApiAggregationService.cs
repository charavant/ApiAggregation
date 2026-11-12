using ApiAggregation.Models;
using ApiAggregation.Providers;

namespace ApiAggregation.Services
{
  public sealed class ApiAggregationService
  {
    #region Start Of: Constructors
    public ApiAggregationService(WorldBankClient worldBank, IEnumerable<ICountrySectionProvider> sectionProviders, IReadOnlyDictionary<String, CountryContext> capitalDictionary)
    {
      this.worldBank = worldBank;
      this.sectionProviders = sectionProviders.ToArray();
      this.capitalDictionary = capitalDictionary;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public IEnumerable<String> SectionNames
    {
      get
      {
        return this.sectionProviders.Select(provider => provider.SectionName).Prepend("country");
      }
    }
    #endregion End Of: Properties

    #region Start Of: Methods
    public async Task<(CountryOverview? Overview, Boolean MissingCoordinates)> GetAsync(String code, CancellationToken cancellationToken)
    {
      CountryData? countryFromLookup = null;

      if (!this.capitalDictionary.TryGetValue(code, out var countryContext))
      {
        // On a catalog miss, the country profile also supplies the coordinates.
        countryFromLookup = await this.worldBank.GetCountryAsync(code, cancellationToken);

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
        var data = await provider.FetchAsync(countryContext, cancellationToken);
        return (Name: provider.SectionName, Data: data);
      }).ToArray();

      var providerSections = await Task.WhenAll(sectionTasks);

      foreach (var section in providerSections)
      {
        sections.Add(section.Name, section.Data);
      }

      return (new CountryOverview(code, sections), false);
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly WorldBankClient worldBank;
    private readonly IReadOnlyList<ICountrySectionProvider> sectionProviders;
    private readonly IReadOnlyDictionary<String, CountryContext> capitalDictionary;
    #endregion End Of: Fields
  }
}