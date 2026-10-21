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

    public Snapshot Build(SensorStatus status) => new(
        DateTimeOffset.Now,
        CpuLoadPercent,
        CpuTempC,
        CpuPowerW,
        CpuClockMhz,
        Cores,
        Gpu,
        Memory,
        Volumes,
        DiskReadBytesPerSecond,
        DiskWriteBytesPerSecond,
        NetReceiveBytesPerSecond,
        NetSendBytesPerSecond,
        Processes,
        Temps,
        Readings,
        status with { Notes = Notes.ToArray() });
}
