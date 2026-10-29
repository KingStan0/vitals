using Vitals.Config;

namespace Vitals.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void Load_clamps_ranges_and_normalizes_thresholds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitals-config-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "intervalMs": 1,
                  "processIntervalMs": 999999,
                  "processCount": 100,
                  "historySeconds": 1,
                  "thresholds": {
                    "cpuTemp": { "warn": -5, "crit": -1 },
                    "gpuMemory": { "warn": 300, "crit": 500 }
                  }
                }
                """);

            var config = AppConfig.Load(path, out var warning);

            Assert.Null(warning);
            Assert.Equal(path, config.LoadedFrom);
            Assert.Equal(250, config.IntervalMs);
            Assert.Equal(60_000, config.ProcessIntervalMs);
            Assert.Equal(64, config.ProcessCount);
            Assert.Equal(30, config.HistorySeconds);
            Assert.Equal(0, config.Thresholds.CpuTemp.Warn);
            Assert.Equal(10, config.Thresholds.CpuTemp.Crit);
            Assert.Equal(200, config.Thresholds.GpuMemory.Warn);
            Assert.Equal(200, config.Thresholds.GpuMemory.Crit);
            Assert.Equal(80, config.Thresholds.GpuEdge.Warn);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Threshold_at_the_upper_bound_keeps_a_valid_critical_value()
    {
        var threshold = new Threshold(200, 100);
        threshold.Normalize();

        Assert.Equal(200, threshold.Warn);
        Assert.Equal(200, threshold.Crit);
    }

    [Fact]
    public void Invalid_json_falls_back_to_defaults_with_a_warning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitals-config-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, "{ not json");
            var config = AppConfig.Load(path, out var warning);

            Assert.NotNull(warning);
            Assert.Contains("could not read", warning);
            Assert.Null(config.LoadedFrom);
            Assert.Equal(1000, config.IntervalMs);
            Assert.Equal(8, config.ProcessCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_explicit_config_warns_and_uses_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitals-missing-{Guid.NewGuid():N}.json");

        var config = AppConfig.Load(path, out var warning);

        Assert.NotNull(warning);
        Assert.Contains("config not found", warning);
        Assert.Equal(1000, config.IntervalMs);
        Assert.NotNull(config.Thresholds);
    }

    [Fact]
    public void Prompt_for_pawn_io_defaults_to_true_and_can_be_disabled()
    {
        Assert.True(new AppConfig().PromptForPawnIo);

        var path = Path.Combine(Path.GetTempPath(), $"vitals-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "promptForPawnIo": false }""");

            var config = AppConfig.Load(path, out var warning);

            Assert.Null(warning);
            Assert.False(config.PromptForPawnIo);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
