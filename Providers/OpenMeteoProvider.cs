using ApiAggregation.Models;

namespace ApiAggregation.Providers;

public sealed class OpenMeteoProvider : ICountrySectionProvider
{
    #region Fields

    private readonly HttpClient httpClient;

    #endregion

    #region Constructors

    public OpenMeteoProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    #endregion

    #region Properties

    public string SectionName => "weather";

    #endregion

    #region Public Methods

    public Task<object> FetchAsync(
        CountryContext country,
        CancellationToken cancellationToken)
    {
        // TODO: Request current weather and return a WeatherData record.
        throw new NotImplementedException();
    }

    #endregion
}
