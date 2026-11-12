namespace ApiAggregation.Models
{
  public sealed class ProviderRequestStatistics
  {
    #region Start Of: Constructors
    public ProviderRequestStatistics(Int64 totalRequests, Double? averageResponseTimeMs, IReadOnlyDictionary<String, RequestBucketStatistics> buckets)
    {
      this.TotalRequests = totalRequests;
      this.AverageResponseTimeMs = averageResponseTimeMs;
      this.Buckets = buckets;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public Int64 TotalRequests { get; }

    public Double? AverageResponseTimeMs { get; }

    public IReadOnlyDictionary<String, RequestBucketStatistics> Buckets { get; }
    #endregion End Of: Properties
  }

  public sealed class RequestBucketStatistics
  {
    #region Start Of: Constructors
    public RequestBucketStatistics(Int64 count, Double? averageResponseTimeMs)
    {
      this.Count = count;
      this.AverageResponseTimeMs = averageResponseTimeMs;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public Int64 Count { get; }

    public Double? AverageResponseTimeMs { get; }
    #endregion End Of: Properties
  }
}