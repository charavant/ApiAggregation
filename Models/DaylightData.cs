namespace ApiAggregation.Models;

public sealed record DaylightData(
    DateOnly Date,
    DateTimeOffset? Sunrise,
    DateTimeOffset? Sunset,
    int DaylightSeconds,
    string SourceUrl);
