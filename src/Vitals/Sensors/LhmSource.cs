using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Vitals.Sensors;

/// <summary>
/// LibreHardwareMonitor backend. This is the only practical way to read CPU,
/// motherboard and storage temperatures on Windows. It needs an elevated
/// process for most sensors and PawnIO (https://pawnio.eu) for low-level access.
/// </summary>
public sealed class LhmSource : ISensorSource
{
    private const int StorageUpdateIntervalMs = 5000;

    private static readonly ConditionalWeakTable<ISensor, string>.CreateValueCallback IdentifierFactory =
        sensor => sensor.Identifier.ToString();

    private readonly List<IHardware> _all = new();

    // Sensor identifiers are stable but expensive to format every tick. Weak keys
    // avoid pinning sensors if LibreHardwareMonitor rebuilds its topology.
    private readonly ConditionalWeakTable<ISensor, string> _identifiers = new();
    private readonly UpdateVisitor _visitor = new();
    private Computer? _computer;
    private long _lastStorageUpdate;

    public string Name => "lhm";

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public bool PawnIoInstalled { get; private set; }

    public bool Elevated { get; private set; }

    public void Initialize()
    {
        Elevated = IsProcessElevated();
        PawnIoInstalled = IsPawnIoInstalled();
        _all.Clear();

        // Make storage due on the first sample regardless of system uptime.
        _lastStorageUpdate = Environment.TickCount64 - StorageUpdateIntervalMs;

        try
        {
            _computer?.Close();
        }
        catch
        {
            // Ignore cleanup failures from a previous initialization.
        }

        _computer = null;
        IsAvailable = false;
        UnavailableReason = null;

        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = false,
                IsControllerEnabled = false,
            };

            _computer.Open();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            try
            {
                _computer?.Close();
            }
            catch
            {
                // Ignore cleanup failures and report the original error.
            }

            UnavailableReason = $"{ex.GetType().Name}: {ex.Message}";
            _computer = null;
            IsAvailable = false;
        }
    }

    public void Sample(SnapshotBuilder builder)
    {
        if (_computer is null)
        {
            return;
        }

        var storageDue = Environment.TickCount64 - _lastStorageUpdate >= StorageUpdateIntervalMs;
        if (storageDue)
        {
            _lastStorageUpdate = Environment.TickCount64;
        }

        try
        {
            _visitor.UpdateStorage = storageDue;
            _computer.Accept(_visitor);

            _all.Clear();
            foreach (var hardware in _computer.Hardware)
            {
                Collect(hardware);
            }

            ReadCpu(builder);
            ReadGpus(builder);
            ReadTemperatures(builder);
            ReadCatalog(builder);
        }
        catch (Exception ex)
        {
            builder.Note($"sensor read failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _computer?.Close();
        }
        catch
        {
            // Shutdown is best effort.
        }

        _computer = null;
        _all.Clear();
        IsAvailable = false;
    }

    private void Collect(IHardware hardware)
    {
        _all.Add(hardware);
        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub);
        }
    }

    private void ReadCpu(SnapshotBuilder builder)
    {
        var cpus = _all
            .Where(h => h.HardwareType == HardwareType.Cpu &&
                        !h.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cpus.Count == 0)
        {
            return;
        }

        // Multi-socket machines expose one CPU hardware per socket. Aggregate
        // package sensors instead of reporting only the first socket.
        builder.CpuTempC = MaxNonZero(cpus.Select(cpu =>
            NonZero(Value(Pick(cpu, SensorType.Temperature, "Tctl/Tdie", "Tdie", "CPU Package", "Core Average", "Package")))));
        builder.CpuPowerW = SumNonZero(cpus.Select(cpu =>
            NonZero(Value(Pick(cpu, SensorType.Power, "Package", "PPT", "CPU Package")))));
        builder.CpuClockMhz = AverageNonZero(cpus.Select(cpu =>
            NonZero(Value(Pick(cpu, SensorType.Clock, "Cores (Average)", "Core Average")))));

        // LHM exposes per-core loads as sensors on the CPU itself ("CPU Core #1").
        // Clocks come from "Core #N" on AMD and often "CPU Core #N" on Intel;
        // the "(Effective)" variants are near zero at idle and are skipped.
        var coreOffset = 0;
        foreach (var cpu in cpus)
        {
            var clockByIndex = cpu.Sensors
                .Where(s => s.SensorType == SensorType.Clock &&
                            IsCoreClockName(s.Name) &&
                            s.Value is > 0)
                .Select(s => (Index: ParseCoreIndex(s.Name), Sensor: s))
                .Where(x => x.Index > 0)
                .GroupBy(x => x.Index)
                .ToDictionary(g => g.Key, g => (ISensor?)g.First().Sensor);

            var loads = cpu.Sensors
                .Where(s => s.SensorType == SensorType.Load &&
                            s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                .Select(s => (Index: ParseCoreIndex(s.Name), Sensor: s))
                .Where(x => x.Index > 0)
                .OrderBy(x => x.Index)
                .ToList();

            foreach (var (index, sensor) in loads)
            {
                clockByIndex.TryGetValue(index, out var clockSensor);
                builder.Cores.Add(new CoreSample(
                    coreOffset + index - 1,
                    Value(sensor),
                    Value(clockSensor),
                    null));
            }

            // The load indices are normally 1..N, but a socket can expose a
            // sparse set (for example 1, 2, 4). Advancing by the count would
            // make the next socket collide with the first socket's last core.
            coreOffset = AdvanceCoreOffset(coreOffset, loads.Count > 0 ? loads[^1].Index : 0);
        }

        if (builder.CpuClockMhz is null)
        {
            var clocks = builder.Cores
                .Where(c => c.ClockMhz.HasValue)
                .Select(c => c.ClockMhz!.Value)
                .ToList();

            if (clocks.Count > 0)
            {
                builder.CpuClockMhz = clocks.Average();
            }
        }
    }

    private void ReadGpus(SnapshotBuilder builder)
    {
        foreach (var gpu in _all.Where(h => h.HardwareType is HardwareType.GpuAmd
                     or HardwareType.GpuNvidia
                     or HardwareType.GpuIntel))
        {
            // Use strict matches for VRAM: the generic fallback could otherwise
            // report "GPU Memory Used" as the total and corrupt primary-GPU choice.
            var usedMb = Value(PickStrict(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"));
            var totalMb = Value(PickStrict(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total"));

            builder.Gpus.Add(new GpuSample(
                CleanName(gpu.Name),
                Value(Pick(gpu, SensorType.Load, "GPU Core", "GPU Total", "D3D 3D")),
                NonZero(Value(Pick(gpu, SensorType.Temperature, "GPU Core", "GPU Temperature"))),
                NonZero(Value(PickStrictWhere(
                    gpu,
                    SensorType.Temperature,
                    sensor => IsHotspotTemperatureName(sensor.Name),
                    "GPU Hot Spot",
                    "Hot Spot",
                    "Hotspot",
                    "Junction"))),
                NonZero(Value(PickStrict(gpu, SensorType.Temperature, "GPU Memory Junction", "Memory Junction", "GPU Memory"))),
                // Zero is a meaningful fan reading (passive mode). Do not turn it
                // into n/a as if the sensor were missing.
                Value(Pick(gpu, SensorType.Fan, "GPU Fan")),
                Value(Pick(gpu, SensorType.Control, "GPU Fan")),
                NonZero(Value(Pick(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Core"))),
                NonZero(Value(Pick(gpu, SensorType.Clock, "GPU Core", "GPU Clock"))),
                NonZero(Value(PickStrict(gpu, SensorType.Clock, "GPU Memory", "Memory Clock"))),
                usedMb is null ? null : usedMb * 1024 * 1024,
                totalMb is null ? null : totalMb * 1024 * 1024));
        }

        // Prefer the discrete GPU for the headline panel when a laptop exposes
        // both an integrated and a discrete adapter. All adapters remain in Gpus.
        builder.Gpu = builder.Gpus
            .OrderByDescending(gpu => gpu.MemoryTotalBytes ?? 0)
            .ThenByDescending(gpu => gpu.PowerW ?? 0)
            .FirstOrDefault();
    }

    private void ReadTemperatures(SnapshotBuilder builder)
    {
        foreach (var hardware in _all)
        {
            if (hardware.HardwareType is HardwareType.Storage)
            {
                var sensor = hardware.Sensors.FirstOrDefault(s =>
                    s.SensorType == SensorType.Temperature &&
                    IsUsableStorageTemperature(s.Name, s.Value));

                if (sensor?.Value is float temp)
                {
                    builder.Temps.Add(new TempSample(ShortName(hardware.Name), temp, "storage"));
                }

                continue;
            }

            if (hardware.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO)
            {
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature &&
                        sensor.Value is float value &&
                        IsUsableBoardTemperature(sensor.Name, value))
                    {
                        builder.Temps.Add(new TempSample(ShortName(sensor.Name), value, "board"));
                    }
                }
            }
        }
    }

    private void ReadCatalog(SnapshotBuilder builder)
    {
        foreach (var hardware in _all)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is not float value)
                {
                    continue;
                }

                var identifier = _identifiers.GetValue(sensor, IdentifierFactory);

                builder.Readings.Add(new Reading(
                    identifier,
                    Name,
                    hardware.Name,
                    sensor.Name,
                    MapKind(sensor.SensorType),
                    SnapshotBuilder.Round(value, 3),
                    UnitFor(sensor.SensorType)));
            }
        }
    }

    private static ISensor? Pick(IHardware hardware, SensorType type, params string[] preferred)
    {
        var candidates = hardware.Sensors
            .Where(sensor => sensor.SensorType == type && sensor.Value is not null)
            .ToList();

        return PickPreferred(candidates, preferred) ?? candidates.FirstOrDefault();
    }

    private static ISensor? PickStrict(IHardware hardware, SensorType type, params string[] names) =>
        PickStrictWhere(hardware, type, _ => true, names);

    private static ISensor? PickStrictWhere(
        IHardware hardware,
        SensorType type,
        Func<ISensor, bool> predicate,
        params string[] names)
    {
        var candidates = hardware.Sensors
            .Where(sensor => sensor.SensorType == type &&
                             sensor.Value is not null &&
                             predicate(sensor))
            .ToList();

        return PickPreferred(candidates, names);
    }

    /// <summary>
    /// Name order is the priority, not sensor enumeration order. A card can
    /// expose both "GPU Hot Spot" and "GPU Memory Junction"; matching the first
    /// sensor that contains "Junction" would report the memory temperature as
    /// the hot spot.
    /// </summary>
    private static ISensor? PickPreferred(IReadOnlyList<ISensor> candidates, IReadOnlyList<string> names)
    {
        var index = PickPreferredIndex(
            candidates.Select(sensor => sensor.Name).ToArray(),
            names);

        return index >= 0 ? candidates[index] : null;
    }

    internal static int PickPreferredIndex(IReadOnlyList<string> names, IReadOnlyList<string> preferred)
    {
        for (var nameIndex = 0; nameIndex < preferred.Count; nameIndex++)
        {
            for (var sensorIndex = 0; sensorIndex < names.Count; sensorIndex++)
            {
                if (names[sensorIndex].Contains(preferred[nameIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return sensorIndex;
                }
            }
        }

        return -1;
    }

    internal static bool IsHotspotTemperatureName(string name) =>
        !name.Contains("Memory", StringComparison.OrdinalIgnoreCase);

    private static double? Value(ISensor? sensor) => sensor?.Value is float value ? value : null;

    private static double? NonZero(double? value) => value is > 0 ? value : null;

    internal static bool IsCoreClockName(string name) =>
        (name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)) &&
        !name.Contains("Effective", StringComparison.OrdinalIgnoreCase);

    private static double? MaxNonZero(IEnumerable<double?> values)
    {
        double? max = null;
        foreach (var value in values)
        {
            if (value is { } number && (max is null || number > max.Value))
            {
                max = number;
            }
        }

        return max;
    }

    private static double? SumNonZero(IEnumerable<double?> values)
    {
        double sum = 0;
        var any = false;
        foreach (var value in values)
        {
            if (value is { } number)
            {
                sum += number;
                any = true;
            }
        }

        return any ? sum : null;
    }

    private static double? AverageNonZero(IEnumerable<double?> values)
    {
        double sum = 0;
        var count = 0;
        foreach (var value in values)
        {
            if (value is { } number)
            {
                sum += number;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }

    internal static bool IsUsableStorageTemperature(string name, double? value) =>
        IsUsableBoardTemperature(name, value) &&
        !name.Contains("Max", StringComparison.OrdinalIgnoreCase);

    internal static bool IsUsableBoardTemperature(string name, double? value) =>
        value is > 0 &&
        !name.Contains("Warning", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Critical", StringComparison.OrdinalIgnoreCase);

    internal static int ParseCoreIndex(string name)
    {
        var hash = name.IndexOf('#');
        if (hash < 0)
        {
            return 0;
        }

        var index = 0;
        for (var i = hash + 1; i < name.Length && char.IsDigit(name[i]); i++)
        {
            index = index * 10 + (name[i] - '0');
        }

        return index;
    }

    internal static int AdvanceCoreOffset(int currentOffset, int maxCoreIndex) =>
        currentOffset + Math.Max(0, maxCoreIndex);

    private static string CleanName(string name) => name
        .Replace("AMD ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("Radeon ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim();

    private static string ShortName(string name)
    {
        name = CleanName(name);
        if (name.Length <= 18)
        {
            return name;
        }

        // Truncate on text elements, not UTF-16 code units, so a surrogate pair
        // or combining sequence can never be split in half.
        var elements = StringInfo.GetTextElementEnumerator(name);
        var builder = new StringBuilder(21);
        var count = 0;
        while (count < 15 && elements.MoveNext())
        {
            builder.Append(elements.GetTextElement());
            count++;
        }

        return builder.Append("...").ToString();
    }

    private static SensorKind MapKind(SensorType type) => type switch
    {
        SensorType.Load => SensorKind.Load,
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Current => SensorKind.Current,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Control => SensorKind.Control,
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.Data,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Level => SensorKind.Level,
        SensorType.Factor => SensorKind.Factor,
        SensorType.TimeSpan => SensorKind.Time,
        _ => SensorKind.Other,
    };

    internal static string UnitFor(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Load => "%",
        SensorType.Fan => "RPM",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "MB/s",
        _ => string.Empty,
    };

    internal static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsPawnIoInstalled()
    {
        try
        {
            return IsPawnIoInstalled(
                keyPath =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    return key is not null;
                },
                File.Exists);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsPawnIoInstalled(Func<string, bool> keyExists, Func<string, bool> fileExists)
    {
        const string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
        const string uninstallWowKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
        const string serviceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";

        if (keyExists(uninstallKey) || keyExists(uninstallWowKey) || keyExists(serviceKey))
        {
            return true;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrWhiteSpace(windows) &&
               fileExists(Path.Combine(windows, "System32", "drivers", "PawnIO.sys"));
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public bool UpdateStorage { get; set; } = true;

        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            if (hardware.HardwareType != HardwareType.Storage || UpdateStorage)
            {
                hardware.Update();
            }

            foreach (var sub in hardware.SubHardware)
            {
                sub.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
