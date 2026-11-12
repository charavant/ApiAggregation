using ApiAggregation.Models;

namespace ApiAggregation.Providers
{
  public interface ICountrySectionProvider
  {
    string SectionName { get; }

    Task<object> FetchAsync(
      CountryContext country,
      CancellationToken cancellationToken);
  }
}
