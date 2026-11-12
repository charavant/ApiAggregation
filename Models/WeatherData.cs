namespace ApiAggregation.Models
{
  public sealed record WeatherData
  {
    #region Start Of: Constructors
    public WeatherData(DateTimeOffset observedAtUtc, Double temperatureCelsius, Int32 relativeHumidityPercent, Double windSpeedKph)
    {
      this.ObservedAtUtc = observedAtUtc;
      this.TemperatureCelsius = temperatureCelsius;
      this.RelativeHumidityPercent = relativeHumidityPercent;
      this.WindSpeedKph = windSpeedKph;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public DateTimeOffset ObservedAtUtc { get; init; }

    public Double TemperatureCelsius { get; init; }

    public Int32 RelativeHumidityPercent { get; init; }

    public Double WindSpeedKph { get; init; }
    #endregion End Of: Properties
  }
}
