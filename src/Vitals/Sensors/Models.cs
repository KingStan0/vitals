namespace Vitals.Sensors;

/// <summary>Normalised sensor categories, so the UI never has to know about LHM's enum.</summary>
public enum SensorKind
{
    Load,
    Temperature,
    Clock,
    Power,
    Voltage,
    Current,
    Fan,
    Control,
    Data,
    Throughput,
    Level,
    Factor,
    Time,
    Other,
}

/// <summary>A single raw reading. <see cref="Id"/> is stable across frames and LHM versions.</summary>
public sealed record Reading(
    string Id,
    string Source,
    string Hardware,
    string Name,
    SensorKind Kind,
    double? Value,
    string Unit);

public sealed record CoreSample(
    int Index,
    double? LoadPercent,
    double? ClockMhz,
    double? TempC);

public sealed record GpuSample(
    string Name,
    double? LoadPercent,
    double? EdgeC,
    double? HotspotC,
    double? MemoryTempC,
    double? FanRpm,
    double? FanPercent,
    double? PowerW,
    double? ClockMhz,
    double? MemoryClockMhz,
    double? MemoryUsedBytes,
    double? MemoryTotalBytes);

public sealed record MemorySample(
    double TotalBytes,
    double AvailableBytes,
    double CommitBytes,
    double CommitLimitBytes,
    double CachedBytes)
{
    public double UsedBytes => TotalBytes - AvailableBytes;

    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes / TotalBytes * 100.0;

    public double CommitPercent => CommitLimitBytes <= 0 ? 0 : CommitBytes / CommitLimitBytes * 100.0;
}

public sealed record VolumeSample(string Name, string Label, double TotalBytes, double FreeBytes)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : (TotalBytes - FreeBytes) / TotalBytes * 100.0;
}

public sealed record ProcessSample(int Pid, string Name, double CpuPercent, long WorkingSetBytes);

public sealed record TempSample(string Label, double Celsius, string Group);

public sealed record SensorStatus(
    bool Elevated,
    bool LhmAvailable,
    string? LhmError,
    bool PawnIoInstalled,
    IReadOnlyList<string> Notes);

public sealed record Snapshot(
    DateTimeOffset Timestamp,
    double CpuLoadPercent,
    double? CpuTempC,
    double? CpuPowerW,
    double? CpuClockMhz,
    IReadOnlyList<CoreSample> Cores,
    GpuSample? Gpu,
    MemorySample? Memory,
    IReadOnlyList<VolumeSample> Volumes,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond,
    double? NetReceiveBytesPerSecond,
    double? NetSendBytesPerSecond,
    IReadOnlyList<ProcessSample> Processes,
    IReadOnlyList<TempSample> Temps,
    IReadOnlyList<Reading> Readings,
    SensorStatus Status);
