namespace ApiAggregation.Models
{
  public sealed record CountryOverview
  {
    #region Start Of: Constructors
    public CountryOverview(String countryCode, IReadOnlyDictionary<String, Object> sections)
    {
      this.CountryCode = countryCode;
      this.Sections = sections;
    }
    #endregion End Of: Constructors

    #region Start Of: Properties
    public String CountryCode { get; init; }

    public IReadOnlyDictionary<String, Object> Sections { get; init; }
    #endregion End Of: Properties
  }
}
