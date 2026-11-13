namespace ApiAggregation.Services
{
  public sealed class ProviderPerformanceMonitorService(ProviderRequestStatisticsService statistics, ILogger<ProviderPerformanceMonitorService> logger) : BackgroundService
  {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        CheckPerformance();
      }
    }

    internal void CheckPerformance()
    {
      foreach (var (providerName, window) in statistics.GetPerformanceWindows())
      {
        if (window.RecentAverageMs is not Double recentAverage ||
            window.BaselineAverageMs is not Double baselineAverage)
        {
          continue;
        }

        if (recentAverage > baselineAverage * 1.5)
        {
          logger.LogWarning(
            "{Provider} performance anomaly. Recent average {RecentAverageMs:F2} ms " +
            "across {RecentCount} attempts. Baseline average {BaselineAverageMs:F2} ms " +
            "across {BaselineCount} attempts.",
            providerName,
            recentAverage,
            window.RecentCount,
            baselineAverage,
            window.BaselineCount);
        }
      }
    }
  }
}
