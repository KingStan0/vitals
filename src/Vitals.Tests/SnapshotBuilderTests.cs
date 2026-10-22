using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class SnapshotBuilderTests
{
    private static SensorStatus Status() => new(
        false,
        false,
        "test",
        false,
        new[] { "base note" },
        AppInfo.Version);

    [Fact]
    public void Build_rounds_noisy_sensor_values_and_keeps_status_notes()
    {
        var discrete = new GpuSample(
            "AMD Radeon RX 5700", 70.04, 65.55, 88.84, 80.06,
            1500.4, 44.44, 119.94, 1699.6, 874.4,
            4_000_000_000.4, 8_000_000_000.6);
        var apu = new GpuSample(
            "AMD Radeon Graphics", 5.55, 50.44, null, null,
            null, null, 10.04, 1900.4, null,
            400_000_000, 512_000_000);

        var builder = new SnapshotBuilder
        {
            CpuLoadPercent = 12.345,
            CpuTempC = 65.55,
            CpuPowerW = 22.222,
            CpuClockMhz = 3799.6,
            Memory = new MemorySample(
                32_000_000_000.4,
                12_000_000_000.6,
                8_000_000_000.4,
                34_000_000_000.4,
                13_000_000_000.6),
            DiskReadBytesPerSecond = 1234.6,
            DiskWriteBytesPerSecond = 0,
            NetReceiveBytesPerSecond = 2048.4,
        };

        builder.Cores.Add(new CoreSample(0, 55.55, 4399.6, 64.44));
        builder.Gpus.Add(apu);
        builder.Gpus.Add(discrete);
        builder.Volumes.Add(new VolumeSample("C:", "System", 999_999_999_999.4, 400_000_000_000.6));
        builder.Processes.Add(new ProcessSample(1, "test", 5.5556, 100));
        builder.Temps.Add(new TempSample("VRM", 64.55, "board"));
        builder.Readings.Add(new Reading("id", "win", "CPU", "Total", SensorKind.Load, 12.34567, "%"));
        builder.Note("base note");
        builder.Note("extra note");

        var snapshot = builder.Build(Status());

        Assert.Equal(12.3, snapshot.CpuLoadPercent);
        Assert.Equal(65.6, snapshot.CpuTempC);
        Assert.Equal(22.2, snapshot.CpuPowerW);
        Assert.Equal(3800, snapshot.CpuClockMhz);
        Assert.Equal(55.6, snapshot.Cores[0].LoadPercent);
        Assert.Equal(4400, snapshot.Cores[0].ClockMhz);
        Assert.Equal(64.4, snapshot.Cores[0].TempC);
        Assert.Equal(999_999_999_999, snapshot.Volumes[0].TotalBytes);
        Assert.Equal(400_000_000_001, snapshot.Volumes[0].FreeBytes);
        Assert.Equal(1235, snapshot.DiskReadBytesPerSecond);
        Assert.Equal(2048, snapshot.NetReceiveBytesPerSecond);
        Assert.Equal(5.56, snapshot.Processes[0].CpuPercent);
        Assert.Equal(64.6, snapshot.Temps[0].Celsius);
        Assert.Equal(12.346, snapshot.Readings[0].Value);
        Assert.Equal(2, snapshot.Gpus.Count);
        Assert.Equal("AMD Radeon RX 5700", snapshot.Gpu!.Name);
        Assert.Equal(1500, snapshot.Gpu.FanRpm);
        Assert.Equal(119.9, snapshot.Gpu.PowerW);
        Assert.Equal(new[] { "base note", "extra note" }, snapshot.Status.Notes);
        Assert.Equal(AppInfo.Version, snapshot.Status.AppVersion);
    }

    [Fact]
    public void Build_picks_the_largest_gpu_when_no_primary_was_supplied()
    {
        var builder = new SnapshotBuilder();
        builder.Gpus.Add(new GpuSample("Integrated", 1, 40, null, null, null, null, null, null, null, 256_000_000, 512_000_000));
        builder.Gpus.Add(new GpuSample("Discrete", 2, 50, null, null, null, null, 100, null, null, 1_000_000_000, 8_000_000_000));

        var snapshot = builder.Build(Status());

        Assert.Equal("Discrete", snapshot.Gpu!.Name);
        Assert.Equal(2, snapshot.Gpus.Count);
    }

    [Fact]
    public void Build_replaces_non_finite_values_so_json_stays_valid()
    {
        var builder = new SnapshotBuilder
        {
            CpuLoadPercent = double.NaN,
            CpuTempC = double.PositiveInfinity,
            Gpu = new GpuSample(
                "GPU", double.NaN, double.PositiveInfinity, null, null,
                double.NegativeInfinity, double.NaN, double.PositiveInfinity, double.NaN, null, 0, 0),
        };
        builder.Temps.Add(new TempSample("bad", double.NaN, "board"));
        builder.Readings.Add(new Reading("id", "source", "hardware", "name", SensorKind.Temperature, double.NaN, "°C"));

        var snapshot = builder.Build(Status());

        Assert.Equal(0, snapshot.CpuLoadPercent);
        Assert.Null(snapshot.CpuTempC);
        Assert.Null(snapshot.Gpu!.LoadPercent);
        Assert.Null(snapshot.Gpu.EdgeC);
        Assert.Null(snapshot.Gpu.FanRpm);
        Assert.Null(snapshot.Gpu.PowerW);
        Assert.Equal(0, snapshot.Temps[0].Celsius);
        Assert.Null(snapshot.Readings[0].Value);
    }
}
