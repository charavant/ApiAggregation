using ApiAggregation.Models;

namespace ApiAggregation.Providers;

public sealed class WorldBankClient
{
    #region Fields

    private readonly HttpClient httpClient;

    #endregion

    #region Constructors

    public WorldBankClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    #endregion

    #region Public Methods

    public Task<CountryData?> GetCountryAsync(
        string code,
        CancellationToken cancellationToken)
    {
        // TODO: Fetch and map the World Bank country response.
        throw new NotImplementedException();
    }

    #endregion
}
