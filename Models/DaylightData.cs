namespace ApiAggregation.Models
{
  public sealed record DaylightData
  {
    #region Start Of: Constructors
    public DaylightData(DateOnly date, DateTimeOffset? sunrise, DateTimeOffset? sunset, Int32? daylightSeconds, String sourceUrl)
    {
      this.Date = date;
      this.Sunrise = sunrise;
      this.Sunset = sunset;
      this.DaylightSeconds = daylightSeconds;
      this.SourceUrl = sourceUrl;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public DateOnly Date { get; init; }

    public DateTimeOffset? Sunrise { get; init; }

    public DateTimeOffset? Sunset { get; init; }

    public Int32? DaylightSeconds { get; init; }

    public String SourceUrl { get; init; }

    internal String? TimeZoneId { get; init; }
    #endregion End Of: Properties
  }
}