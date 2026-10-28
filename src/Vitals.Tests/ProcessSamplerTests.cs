using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class ProcessSamplerTests
{
    [Fact]
    public void Start_time_probe_is_not_needed_for_a_stable_process()
    {
        Assert.False(ProcessSampler.ShouldProbeStartTime(
            "chrome",
            "chrome",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(12),
            1_000));
    }

    [Theory]
    [InlineData("chrome", "notepad", 10.0, 12.0, 1_000)]
    [InlineData("chrome", "chrome", 12.0, 10.0, 1_000)]
    [InlineData("chrome", "chrome", 10.0, 12.0, 30_000)]
    public void Start_time_probe_runs_when_a_pid_looks_suspicious(
        string previousName,
        string currentName,
        double previousCpu,
        double currentCpu,
        long sinceProbeMs)
    {
        Assert.True(ProcessSampler.ShouldProbeStartTime(
            previousName,
            currentName,
            TimeSpan.FromSeconds(previousCpu),
            TimeSpan.FromSeconds(currentCpu),
            sinceProbeMs));
    }

    [Fact]
    public void Baseline_resets_when_the_process_name_changes()
    {
        Assert.True(ProcessSampler.ShouldResetBaseline(
            "chrome",
            "notepad",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Baseline_resets_when_the_start_time_changes()
    {
        Assert.True(ProcessSampler.ShouldResetBaseline(
            "chrome",
            "chrome",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Baseline_is_kept_when_the_process_is_the_same_or_start_time_is_hidden()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(ProcessSampler.ShouldResetBaseline("chrome", "chrome", start, start));
        Assert.False(ProcessSampler.ShouldResetBaseline("chrome", "chrome", null, null));
    }
}
