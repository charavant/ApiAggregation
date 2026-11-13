using ApiAggregation.Services;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ApiAggregation.Tests
{
  public sealed class ProviderPerformanceMonitorTests(ITestOutputHelper output)
  {
    [Theory]
    [InlineData(150, true, false)]
    [InlineData(151, true, true)]
    [InlineData(151, false, false)]
    public void CheckPerformanceWarnsOnlyAboveThresholdWithBaseline(
      Double recentAverage, Boolean hasBaseline, Boolean expectWarning)
    {
      const String provider = "Open-Meteo";
      var clock = new TestClock();
      var statistics = new ProviderRequestStatisticsService(new[] { provider }, clock);

      if (hasBaseline)
      {
        statistics.Record(provider, 80);
        statistics.Record(provider, 120);
      }

      clock.Now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
      statistics.Record(provider, recentAverage - 10);
      statistics.Record(provider, recentAverage + 10);

      var window = statistics.GetPerformanceWindows()[provider];
      output.WriteLine(
        $"{provider}: recent average {window.RecentAverageMs} ms ({window.RecentCount} attempts), " +
        $"baseline average {window.BaselineAverageMs?.ToString() ?? "unavailable"} " +
        $"({window.BaselineCount} attempts).");
      Assert.Equal(2, window.RecentCount);
      Assert.Equal(recentAverage, window.RecentAverageMs);
      Assert.Equal(hasBaseline ? 2 : 0, window.BaselineCount);
      Assert.Equal(hasBaseline ? 100d : (Double?)null, window.BaselineAverageMs);

      var sink = new TestLogSink();
      using var serilog = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
      using var loggerFactory = new SerilogLoggerFactory(serilog);
      var monitor = new ProviderPerformanceMonitorService(
        statistics, loggerFactory.CreateLogger<ProviderPerformanceMonitorService>());

      monitor.CheckPerformance();

      if (expectWarning)
      {
        var warning = Assert.Single(sink.Events);
        output.WriteLine(warning.RenderMessage());
        Assert.Equal(LogEventLevel.Warning, warning.Level);
        Assert.Equal(new ScalarValue(provider), warning.Properties["Provider"]);
        Assert.Equal(new ScalarValue(recentAverage), warning.Properties["RecentAverageMs"]);
        Assert.Equal(new ScalarValue(100d), warning.Properties["BaselineAverageMs"]);
      }
      else
      {
        Assert.Empty(sink.Events);
        output.WriteLine("No warning logged.");
      }
    }

    private sealed class TestClock : TimeProvider
    {
      public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

      public override DateTimeOffset GetUtcNow() => this.Now;
    }

    private sealed class TestLogSink : ILogEventSink
    {
      public List<LogEvent> Events { get; } = new();

      public void Emit(LogEvent logEvent) => this.Events.Add(logEvent);
    }
  }
}
