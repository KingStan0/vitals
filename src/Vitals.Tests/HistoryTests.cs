using Vitals.Sampling;
using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void Series_drops_oldest_values_at_capacity()
    {
        var series = new Series(3);
        series.Add(1);
        series.Add(2);
        series.Add(3);
        series.Add(4);

        Assert.Equal(new double[] { 2, 3, 4 }, series.Values.ToArray());
    }

    [Fact]
    public void Series_tracks_peak_and_can_reset_it()
    {
        var series = new Series(10);
        series.Add(10);
        series.Add(90);
        series.Add(20);

        Assert.Equal(90, series.Peak);

        series.ResetPeak();
        Assert.Equal(0, series.Peak);
    }

    [Fact]
    public void History_adds_snapshot_values()
    {
        var history = new History(10);
        history.Add(SnapshotFixture.Create());

        Assert.Equal(42d, history.CpuLoad.Last!.Value);
        Assert.Equal(61d, history.CpuTemp.Last!.Value);
        Assert.Equal(70d, history.GpuLoad.Last!.Value);
    }
}

internal static class SnapshotFixture
{
    public static Snapshot Create() => new(
        DateTimeOffset.Now,
        CpuLoadPercent: 42,
        CpuTempC: 61,
        CpuPowerW: 65,
        CpuClockMhz: 4400,
        Cores: new[] { new CoreSample(0, 55, 4400, 60) },
        Gpu: new GpuSample("RX 5700", 70, 72, 88, 80, 1500, 45, 120, 1700, 875, 4, 8),
        Memory: new MemorySample(32, 12, 10, 40, 4),
        Volumes: new[] { new VolumeSample("C:", "System", 1000, 400) },
        DiskReadBytesPerSecond: 50_000_000,
        DiskWriteBytesPerSecond: 10_000_000,
        NetReceiveBytesPerSecond: 2_000_000,
        NetSendBytesPerSecond: 500_000,
        Processes: new[] { new ProcessSample(1234, "chrome", 12.3, 1_800_000_000) },
        Temps: new[] { new TempSample("VRM MOS", 78, "board") },
        Readings: Array.Empty<Reading>(),
        Status: new SensorStatus(true, true, null, true, Array.Empty<string>()));
}
