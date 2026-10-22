namespace Vitals.Sensors;

/// <summary>Mutable scratch space that sensor sources write into once per tick.</summary>
public sealed class SnapshotBuilder
{
    public double CpuLoadPercent { get; set; }

    public double? CpuTempC { get; set; }

    public double? CpuPowerW { get; set; }

    public double? CpuClockMhz { get; set; }

    public List<CoreSample> Cores { get; } = new();

    public GpuSample? Gpu { get; set; }

    public List<GpuSample> Gpus { get; } = new();

    public MemorySample? Memory { get; set; }

    public List<VolumeSample> Volumes { get; } = new();

    public double DiskReadBytesPerSecond { get; set; }

    public double DiskWriteBytesPerSecond { get; set; }

    public double? NetReceiveBytesPerSecond { get; set; }

    public double? NetSendBytesPerSecond { get; set; }

    public List<ProcessSample> Processes { get; } = new();

    public List<TempSample> Temps { get; } = new();

    public List<Reading> Readings { get; } = new();

    public HashSet<string> Notes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Note(string message) => Notes.Add(message);

    /// <summary>
    /// Produces an immutable snapshot and rounds sensor noise to the precision
    /// the dashboard and JSON consumers actually use.
    /// </summary>
    public Snapshot Build(SensorStatus status)
    {
        var gpus = (Gpus.Count > 0
                ? Gpus.Select(Round)
                : Gpu is null
                    ? Enumerable.Empty<GpuSample>()
                    : new[] { Round(Gpu) })
            .ToArray();
        var primaryGpu = Gpu is null ? PickPrimaryGpu(gpus) : Round(Gpu);

        return new Snapshot(
            DateTimeOffset.Now,
            Round(CpuLoadPercent, 1),
            Round(CpuTempC, 1),
            Round(CpuPowerW, 1),
            Round(CpuClockMhz, 0),
            Cores.Select(core => core with
            {
                LoadPercent = Round(core.LoadPercent, 1),
                ClockMhz = Round(core.ClockMhz, 0),
                TempC = Round(core.TempC, 1),
            }).ToArray(),
            primaryGpu,
            gpus,
            Memory is null
                ? null
                : Memory with
                {
                    TotalBytes = Round(Memory.TotalBytes, 0),
                    AvailableBytes = Round(Memory.AvailableBytes, 0),
                    CommitBytes = Round(Memory.CommitBytes, 0),
                    CommitLimitBytes = Round(Memory.CommitLimitBytes, 0),
                    CachedBytes = Round(Memory.CachedBytes, 0),
                },
            Volumes.Select(volume => volume with
            {
                TotalBytes = Round(volume.TotalBytes, 0),
                FreeBytes = Round(volume.FreeBytes, 0),
            }).ToArray(),
            Round(DiskReadBytesPerSecond, 0),
            Round(DiskWriteBytesPerSecond, 0),
            Round(NetReceiveBytesPerSecond, 0),
            Round(NetSendBytesPerSecond, 0),
            Processes.Select(process => process with
            {
                CpuPercent = Round(process.CpuPercent, 2),
            }).ToArray(),
            Temps.Select(temp => temp with { Celsius = Round(temp.Celsius, 1) }).ToArray(),
            Readings.Select(reading => reading with { Value = Round(reading.Value, 3) }).ToArray(),
            status with
            {
                Notes = status.Notes
                    .Concat(Notes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            });
    }

    private static GpuSample? PickPrimaryGpu(IEnumerable<GpuSample> gpus) => gpus
        .OrderByDescending(gpu => gpu.MemoryTotalBytes ?? 0)
        .ThenByDescending(gpu => gpu.PowerW ?? 0)
        .FirstOrDefault();

    private static GpuSample Round(GpuSample gpu) => gpu with
    {
        LoadPercent = Round(gpu.LoadPercent, 1),
        EdgeC = Round(gpu.EdgeC, 1),
        HotspotC = Round(gpu.HotspotC, 1),
        MemoryTempC = Round(gpu.MemoryTempC, 1),
        FanRpm = Round(gpu.FanRpm, 0),
        FanPercent = Round(gpu.FanPercent, 1),
        PowerW = Round(gpu.PowerW, 1),
        ClockMhz = Round(gpu.ClockMhz, 0),
        MemoryClockMhz = Round(gpu.MemoryClockMhz, 0),
        MemoryUsedBytes = Round(gpu.MemoryUsedBytes, 0),
        MemoryTotalBytes = Round(gpu.MemoryTotalBytes, 0),
    };

    private static double Round(double value, int digits) =>
        double.IsFinite(value) ? Math.Round(value, digits, MidpointRounding.AwayFromZero) : 0;

    private static double? Round(double? value, int digits) =>
        value is { } number && double.IsFinite(number)
            ? Math.Round(number, digits, MidpointRounding.AwayFromZero)
            : null;
}
