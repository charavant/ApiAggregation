using ApiAggregation.Services;
using Xunit;

namespace ApiAggregation.Tests
{
  public sealed class RequestStatisticsTests
  {
    #region Start Of: Methods
    [Theory]
    [InlineData(99.99, "fast")]
    [InlineData(100, "average")]
    [InlineData(200, "average")]
    [InlineData(200.01, "slow")]
    public void RequestFallsIntoExactlyOneBucket(Double elapsedMilliseconds, String expectedBucket)
    {
      var statistics = new ProviderRequestStatisticsService(new[] { "Open-Meteo" });
      statistics.Record("Open-Meteo", elapsedMilliseconds);

      var provider = statistics.GetSnapshot()["Open-Meteo"];

      Assert.Equal(1, provider.TotalRequests);
      Assert.Equal(1, provider.Buckets[expectedBucket].Count);
      Assert.Equal(1, provider.Buckets.Values.Sum(bucket => bucket.Count));
      Assert.All(provider.Buckets.Where(bucket => bucket.Key != expectedBucket),
        bucket => Assert.Null(bucket.Value.AverageResponseTimeMs));
    }

    [Fact]
    public void OverallAverageUsesAllRequestsAndKeepsProvidersSeparate()
    {
      var statistics = new ProviderRequestStatisticsService(new[] { "World Bank", "Open-Meteo" });

      foreach (var elapsedMilliseconds in new[] { 50.0, 70.0, 130.0, 250.0 })
      {
        statistics.Record("World Bank", elapsedMilliseconds);
      }

      var snapshot = statistics.GetSnapshot();
      var worldBank = snapshot["World Bank"];
      Assert.Equal(4, worldBank.TotalRequests);
      Assert.Equal(125, worldBank.AverageResponseTimeMs);
      Assert.Equal(60, worldBank.Buckets["fast"].AverageResponseTimeMs);
      Assert.Equal(0, snapshot["Open-Meteo"].TotalRequests);
      Assert.Null(snapshot["Open-Meteo"].AverageResponseTimeMs);
    }

    [Fact]
    public async Task ConcurrentWritesAndSnapshotsKeepCountsConsistent()
    {
      var statistics = new ProviderRequestStatisticsService(new[] { "Sunrise-Sunset" });

      await Parallel.ForAsync(0, 1_000, (_, _) =>
      {
        statistics.Record("Sunrise-Sunset", 150);
        var snapshot = statistics.GetSnapshot()["Sunrise-Sunset"];
        Assert.Equal(snapshot.TotalRequests, snapshot.Buckets["average"].Count);
        return ValueTask.CompletedTask;
      });

      var final = statistics.GetSnapshot()["Sunrise-Sunset"];
      Assert.Equal(1_000, final.TotalRequests);
      Assert.Equal(150, final.AverageResponseTimeMs);
    }
    #endregion End Of: Methods
  }
}