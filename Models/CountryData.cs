namespace ApiAggregation.Models
{
  public sealed record CountryData
  {
    #region Start Of: Constructors
    public CountryData(String name, String? capital, String? region, String? incomeLevel, Double? latitude, Double? longitude)
    {
      this.Name = name;
      this.Capital = capital;
      this.Region = region;
      this.IncomeLevel = incomeLevel;
      this.Latitude = latitude;
      this.Longitude = longitude;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public String Name { get; init; }

    public String? Capital { get; init; }

    public String? Region { get; init; }

    public String? IncomeLevel { get; init; }

    public Double? Latitude { get; init; }

    public Double? Longitude { get; init; }
    #endregion End Of: Properties
  }
}
