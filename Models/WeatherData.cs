namespace ApiAggregation.Models;

public sealed record WeatherData(
    DateTimeOffset ObservedAtUtc,
    double TemperatureCelsius,
    int RelativeHumidityPercent,
    double WindSpeedKph);
