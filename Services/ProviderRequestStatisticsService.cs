using ApiAggregation.Models;

namespace ApiAggregation.Services
{
  public sealed class ProviderRequestStatisticsService
  {
    #region Start Of: Constructors
    public ProviderRequestStatisticsService(IEnumerable<String> providerNames)
    {
      this.providerBuckets = providerNames.ToDictionary(name => name, _ => new (Int64 Count, Double TotalMilliseconds)[bucketNames.Length]);
    }
    #endregion End Of: Constructors

    #region Start Of: Methods
    public void Record(String providerName, Double elapsedMilliseconds)
    {
      var bucketIndex = elapsedMilliseconds switch
      {
        < 100 => 0,
        <= 200 => 1,
        _ => 2
      };

      lock (this.syncRoot)
      {
        if (!this.providerBuckets.TryGetValue(providerName, out var buckets))
        {
          buckets = new (Int64 Count, Double TotalMilliseconds)[bucketNames.Length];
          this.providerBuckets.Add(providerName, buckets);
        }

        var bucket = buckets[bucketIndex];
        buckets[bucketIndex] = (bucket.Count + 1, bucket.TotalMilliseconds + elapsedMilliseconds);
      }
    }

    public IReadOnlyDictionary<String, ProviderRequestStatistics> GetSnapshot()
    {
      // Take the snapshot under the same lock as writes so totals match their buckets.
      lock (this.syncRoot)
      {
        var snapshot = new Dictionary<String, ProviderRequestStatistics>();

        foreach (var (providerName, buckets) in this.providerBuckets)
        {
          var bucketSnapshots = new Dictionary<String, RequestBucketStatistics>();
          Int64 totalRequests = 0;
          Double totalMilliseconds = 0;

          for (var index = 0; index < buckets.Length; index++)
          {
            var bucket = buckets[index];
            totalRequests += bucket.Count;
            totalMilliseconds += bucket.TotalMilliseconds;

            bucketSnapshots.Add(bucketNames[index], new RequestBucketStatistics(bucket.Count, GetAverage(bucket.Count, bucket.TotalMilliseconds)));
          }

          snapshot.Add(providerName, new ProviderRequestStatistics(totalRequests, GetAverage(totalRequests, totalMilliseconds), bucketSnapshots));
        }

        return snapshot;
      }
    }

    private static Double? GetAverage(Int64 count, Double totalMilliseconds)
    {
      return count == 0 ? null : Math.Round(totalMilliseconds / count, 2);
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private static readonly String[] bucketNames = { "fast", "average", "slow" };

    private readonly Object syncRoot = new();
    private readonly Dictionary<String, (Int64 Count, Double TotalMilliseconds)[]> providerBuckets;
    #endregion End Of: Fields
  }
}