using Vitals.Config;
using Vitals.Sampling;

namespace Vitals.Tests;

public sealed class SamplerTests
{
    [Fact]
    public void Seed_publishes_the_warm_snapshot_and_keeps_one_session_frame()
    {
        var config = new AppConfig { NoDriver = true, IntervalMs = 250 };
        var engine = new SampleEngine(config);
        using var sampler = new Sampler(engine, config);
        var snapshot = SnapshotFixture.Create();

        sampler.Seed(snapshot);

        Assert.Same(snapshot, sampler.Latest);
        Assert.Single(sampler.SessionFrames());
        Assert.Equal(snapshot.CpuLoadPercent, sampler.History.CpuLoad.Last);
    }

    [Fact]
    public void Seed_cannot_be_called_twice_before_start()
    {
        var config = new AppConfig { NoDriver = true, IntervalMs = 250 };
        var engine = new SampleEngine(config);
        using var sampler = new Sampler(engine, config);
        var snapshot = SnapshotFixture.Create();

        sampler.Seed(snapshot);

        Assert.Throws<InvalidOperationException>(() => sampler.Seed(snapshot));
        Assert.Single(sampler.SessionFrames());
    }

    [Fact]
    public void Session_capacity_is_bounded_even_with_a_huge_history_setting()
    {
        var config = new AppConfig
        {
            NoDriver = true,
            HistorySeconds = 86_400,
            IntervalMs = 250,
        };
        var engine = new SampleEngine(config);
        using var sampler = new Sampler(engine, config);

        Assert.Equal(Sampler.MaxSessionFrames, sampler.SessionCapacity);
        Assert.Equal(6_000, sampler.SessionCapacity);
    }

    [Fact]
    public void Session_capacity_honors_a_short_history_at_a_long_interval()
    {
        var config = new AppConfig
        {
            NoDriver = true,
            HistorySeconds = 30,
            IntervalMs = 60_000,
        };
        var engine = new SampleEngine(config);
        using var sampler = new Sampler(engine, config);

        Assert.Equal(1, sampler.SessionCapacity);
    }

    [Fact]
    public void Starts_once_produces_a_sample_and_disposes_idempotently()
    {
        var config = new AppConfig { NoDriver = true, IntervalMs = 250 };
        var engine = new SampleEngine(config);
        var sampler = new Sampler(engine, config);

        sampler.IntervalMs = 1;
        Assert.Equal(250, sampler.IntervalMs);
        sampler.IntervalMs = 999_999;
        Assert.Equal(60_000, sampler.IntervalMs);
        sampler.IntervalMs = 250;

        sampler.Start();
        Assert.Throws<InvalidOperationException>(() => sampler.Start());

        var deadline = Environment.TickCount64 + 3000;
        while (sampler.Latest is null && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(20);
        }

        var latest = sampler.Latest;
        Assert.NotNull(latest);
        Assert.Contains(latest.Readings, reading => reading.Id == "win/cpu/load");

        sampler.Dispose();
        sampler.Dispose();
        Assert.Throws<ObjectDisposedException>(() => sampler.Start());
    }
}
