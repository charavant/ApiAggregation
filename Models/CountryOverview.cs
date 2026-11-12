namespace ApiAggregation.Models;

public sealed record CountryOverview(
    string CountryCode,
    IReadOnlyDictionary<string, object> Sections);
