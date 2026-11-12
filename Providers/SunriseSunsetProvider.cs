using ApiAggregation.Models;

namespace ApiAggregation.Providers;

public sealed class SunriseSunsetProvider : ICountrySectionProvider
{
    #region Fields

    private readonly HttpClient httpClient;

    #endregion

    #region Constructors

    public SunriseSunsetProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    #endregion

    #region Properties

    public string SectionName => "daylight";

    #endregion

    #region Public Methods

    public Task<object> FetchAsync(
        CountryContext country,
        CancellationToken cancellationToken)
    {
        // TODO: Request the capital's daylight data and return a DaylightData record.
        throw new NotImplementedException();
    }

    #endregion
}
