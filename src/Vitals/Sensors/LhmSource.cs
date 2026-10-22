using System.Security.Principal;
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

    private readonly List<IHardware> _all = new();
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

    public void Dispose() => _computer?.Close();

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
        var cpu = _all.FirstOrDefault(h =>
            h.HardwareType == HardwareType.Cpu &&
            !h.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase));

        if (cpu is null)
        {
            return;
        }

        builder.CpuTempC = NonZero(Value(Pick(cpu, SensorType.Temperature, "Tctl/Tdie", "Tdie", "CPU Package", "Core Average", "Package")));
        builder.CpuPowerW = NonZero(Value(Pick(cpu, SensorType.Power, "Package", "PPT", "CPU Package")));
        builder.CpuClockMhz = NonZero(Value(Pick(cpu, SensorType.Clock, "Cores (Average)", "Core Average")));

        // LHM exposes per-core loads as sensors on the CPU itself ("CPU Core #1").
        // Clocks come from "Core #N"; the "(Effective)" variants are near zero at
        // idle, so they are explicitly skipped.
        var clockByIndex = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock &&
                        s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) &&
                        !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
            .Select(s => (Index: ParseCoreIndex(s.Name), Sensor: s))
            .Where(x => x.Index > 0 && x.Sensor.Value is > 0)
            .GroupBy(x => x.Index)
            .ToDictionary(g => g.Key, g => (ISensor?)g.First().Sensor);

        var loads = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Load &&
                        s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
            .Select(s => (Index: ParseCoreIndex(s.Name), Sensor: s))
            .Where(x => x.Index > 0)
            .OrderBy(x => x.Index);

        foreach (var (index, sensor) in loads)
        {
            clockByIndex.TryGetValue(index, out var clockSensor);
            builder.Cores.Add(new CoreSample(index - 1, Value(sensor), Value(clockSensor), null));
        }

        if (builder.CpuClockMhz is null)
        {
            var clocks = builder.Cores.Where(c => c.ClockMhz.HasValue).Select(c => c.ClockMhz!.Value).ToList();
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
            var totalMb = Value(PickStrict(gpu, SensorType.SmallData, "GPU Memory Total"));

            builder.Gpus.Add(new GpuSample(
                CleanName(gpu.Name),
                Value(Pick(gpu, SensorType.Load, "GPU Core", "GPU Total", "D3D 3D")),
                NonZero(Value(Pick(gpu, SensorType.Temperature, "GPU Core", "GPU Temperature"))),
                NonZero(Value(PickStrict(gpu, SensorType.Temperature, "GPU Hot Spot", "Hot Spot", "Hotspot", "Junction"))),
                NonZero(Value(PickStrict(gpu, SensorType.Temperature, "GPU Memory Junction", "Memory Junction", "GPU Memory"))),
                NonZero(Value(Pick(gpu, SensorType.Fan, "GPU Fan"))),
                NonZero(Value(Pick(gpu, SensorType.Control, "GPU Fan"))),
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
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value is float value && value > 0)
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

                builder.Readings.Add(new Reading(
                    sensor.Identifier.ToString(),
                    Name,
                    hardware.Name,
                    sensor.Name,
                    MapKind(sensor.SensorType),
                    value,
                    UnitFor(sensor.SensorType)));
            }
        }
    }

    private static ISensor? Pick(IHardware hardware, SensorType type, params string[] preferred)
    {
        ISensor? fallback = null;

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != type || sensor.Value is null)
            {
                continue;
            }

            fallback ??= sensor;

            foreach (var name in preferred)
            {
                if (sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return sensor;
                }
            }
        }

        return fallback;
    }

    private static ISensor? PickStrict(IHardware hardware, SensorType type, params string[] names)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != type || sensor.Value is null)
            {
                continue;
            }

            foreach (var name in names)
            {
                if (sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return sensor;
                }
            }
        }

        return null;
    }

    private static double? Value(ISensor? sensor) => sensor?.Value is float value ? value : null;

    private static double? NonZero(double? value) => value is > 0 ? value : null;

    internal static bool IsUsableStorageTemperature(string name, double? value) =>
        value is > 0 &&
        !name.Contains("Warning", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Critical", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Max", StringComparison.OrdinalIgnoreCase);

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

    private static string CleanName(string name) => name
        .Replace("AMD ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("Radeon ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim();

    private static string ShortName(string name)
    {
        name = CleanName(name);
        return name.Length <= 18 ? name : name[..17] + "…";
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
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (key is not null)
            {
                return true;
            }

            using var wow = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return wow is not null;
        }
        catch
        {
            return false;
        }
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
