using ApiAggregation.Models;
using ApiAggregation.Services;
using System.Diagnostics;

namespace ApiAggregation.Providers
{
  public sealed class TimingHandler : DelegatingHandler
  {
    #region Start Of: Constructors
    public TimingHandler(String providerName, ProviderRequestStatisticsService statistics)
    {
      this.providerName = providerName;
      this.statistics = statistics;
    }
    #endregion End Of: Constructors

    #region Start Of: Methods
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var stopwatch = Stopwatch.StartNew();

      try
      {
        return await base.SendAsync(request, cancellationToken);
      }
      finally
      {
        // Record each attempt through this handler, including failures and cancellations.
        this.statistics.Record(this.providerName, stopwatch.Elapsed.TotalMilliseconds);
      }
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private readonly String providerName;
    private readonly ProviderRequestStatisticsService statistics;
    #endregion End Of: Fields
  }
}