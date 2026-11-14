namespace ApiAggregation.Models
{
  // Baseline attempts predate the recent window. An empty period has no average.
  public sealed record ProviderPerformanceWindow(
    Int64 RecentCount,
    Double? RecentAverageMs,
    Int64 BaselineCount,
    Double? BaselineAverageMs);
}