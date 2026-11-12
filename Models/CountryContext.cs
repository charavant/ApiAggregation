namespace ApiAggregation.Models;

public sealed record CountryContext(
    string Code,
    double Latitude,
    double Longitude);
