namespace ApiAggregation.Models
{
  public sealed record CountryContext
  {
    #region Start Of: Constructors
    public CountryContext(String code, Double latitude, Double longitude)
    {
      this.Code = code;
      this.Latitude = latitude;
      this.Longitude = longitude;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public String Code { get; init; }

    public Double Latitude { get; init; }

    public Double Longitude { get; init; }
    #endregion End Of: Properties
  }
}
