using ApiAggregation.Models;

namespace ApiAggregation.Services
{
  public sealed class ProviderRequestStatisticsService
  {
    #region Start Of: Constructors
    public ProviderRequestStatisticsService(IEnumerable<String> providerNames, TimeProvider? timeProvider = null)
    {
      this.timeProvider = timeProvider ?? TimeProvider.System;
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

        if (!this.recentRequests.TryGetValue(providerName, out var requests))
        {
          requests = new Queue<(DateTimeOffset, Double)>();
          this.recentRequests.Add(providerName, requests);
        }

        var now = this.timeProvider.GetUtcNow();
        requests.Enqueue((now, elapsedMilliseconds));
        RemoveExpiredRequests(requests, now - recentWindow);
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

    public IReadOnlyDictionary<String, ProviderPerformanceWindow> GetPerformanceWindows()
    {
      lock (this.syncRoot)
      {
        var cutoff = this.timeProvider.GetUtcNow() - recentWindow;
        var snapshot = new Dictionary<String, ProviderPerformanceWindow>();

        foreach (var (providerName, buckets) in this.providerBuckets)
        {
          var totalCount = buckets.Sum(bucket => bucket.Count);
          var totalMilliseconds = buckets.Sum(bucket => bucket.TotalMilliseconds);

          Int64 recentCount = 0;
          Double recentMilliseconds = 0;

          if (this.recentRequests.TryGetValue(providerName, out var requests))
          {
            RemoveExpiredRequests(requests, cutoff);
            recentCount = requests.Count;
            recentMilliseconds = requests.Sum(request => request.ElapsedMilliseconds);
          }

          var baselineCount = totalCount - recentCount;
          var baselineMilliseconds = totalMilliseconds - recentMilliseconds;

          snapshot.Add(providerName, new ProviderPerformanceWindow(
            recentCount,
            CalculateAverage(recentCount, recentMilliseconds),
            baselineCount,
            CalculateAverage(baselineCount, baselineMilliseconds)));
        }

        return snapshot;
      }
    }

    private static Double? CalculateAverage(Int64 count, Double totalMilliseconds)
    {
      return count == 0 ? null : totalMilliseconds / count;
    }

    private static Double? GetAverage(Int64 count, Double totalMilliseconds)
    {
      return count == 0 ? null : Math.Round(totalMilliseconds / count, 2);
    }

    private static void RemoveExpiredRequests(Queue<(DateTimeOffset RecordedAt, Double ElapsedMilliseconds)> requests, DateTimeOffset cutoff)
    {
      while (requests.TryPeek(out var request) && request.RecordedAt < cutoff)
      {
        requests.Dequeue();
      }
    }
    #endregion End Of: Methods

    #region Start Of: Fields
    private static readonly String[] bucketNames = { "fast", "average", "slow" };

    private readonly Object syncRoot = new();

    private readonly TimeProvider timeProvider;

    private readonly Dictionary<String, (Int64 Count, Double TotalMilliseconds)[]> providerBuckets;

    private static readonly TimeSpan recentWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<String, Queue<(DateTimeOffset RecordedAt, Double ElapsedMilliseconds)>> recentRequests = new();
    #endregion End Of: Fields
  }
}
