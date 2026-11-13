namespace ApiAggregation.Models
{
  public sealed record ProviderPerformanceWindow(Int64 RecentCount, Double? RecentAverageMs, Int64 BaselineCount, Double? BaselineAverageMs);
}
