namespace ApiAggregation.Models;

public sealed record CountryData(
    string Name,
    string? Capital,
    string? Region,
    string? IncomeLevel,
    double? Latitude,
    double? Longitude);
