using System.Text.Json;
using Vitals.Config;
using Vitals.Sampling;
using Vitals.Sensors;
using Vitals.Ui;

namespace Vitals.Cli;

public static class Commands
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Run(CliOptions options, AppConfig config)
    {
        return options.Command switch
        {
            "sensors" => Sensors(options, config),
            "snapshot" => Snapshot(options, config),
            "watch" => Watch(options, config),
            "export" => ExportCsv(options, config),
            _ => 2,
        };
    }

    private static int Sensors(CliOptions options, AppConfig config)
    {
        using var engine = new SampleEngine(config);
        var snapshot = engine.Sample();

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot.Readings, PrettyJson));
            return 0;
        }

        var status = snapshot.Status;
        Console.WriteLine($"vitals sensors  ·  {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"elevated: {(status.Elevated ? "yes" : "no")}   " +
                          $"librehardwaremonitor: {(status.LhmAvailable ? "available" : "unavailable")}   " +
                          $"pawnio: {(status.PawnIoInstalled ? "installed" : "missing")}");

        if (status.LhmError is not null)
        {
            Console.WriteLine($"lhm error: {status.LhmError}");
        }

        foreach (var note in status.Notes)
        {
            Console.WriteLine($"note: {note}");
        }

        if (snapshot.Readings.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No sensors were reported. Install PawnIO and run vitals from an elevated terminal.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"{"ID",-42} {"KIND",-12} {"VALUE",10} {"UNIT",-6} HARDWARE / NAME");

        foreach (var reading in snapshot.Readings
                     .OrderBy(r => r.Source, StringComparer.Ordinal)
                     .ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var value = reading.Value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
            Console.WriteLine(
                $"{reading.Id,-42} {reading.Kind,-12} {value,10} {reading.Unit,-6} {reading.Hardware} / {reading.Name}");
        }

        return 0;
    }

    private static int Snapshot(CliOptions options, AppConfig config)
    {
        using var engine = new SampleEngine(config);
        engine.WarmUp();
        var snapshot = engine.Sample();

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot, PrettyJson));
            return 0;
        }

        PrintSummary(snapshot);
        return 0;
    }

    private static int Watch(CliOptions options, AppConfig config)
    {
        using var engine = new SampleEngine(config);
        engine.WarmUp();

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        var started = DateTime.UtcNow;
        var next = Environment.TickCount64;

        while (!stop.IsCancellationRequested)
        {
            var snapshot = engine.Sample();

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(snapshot, CompactJson));
            }
            else
            {
                PrintSummary(snapshot);
            }

            if (options.Duration is { } duration && DateTime.UtcNow - started >= duration)
            {
                break;
            }

            next += config.IntervalMs;
            var delay = next - Environment.TickCount64;
            if (delay > 0)
            {
                Thread.Sleep((int)delay);
            }
            else
            {
                next = Environment.TickCount64;
            }
        }

        return 0;
    }

    private static int ExportCsv(CliOptions options, AppConfig config)
    {
        var path = options.OutPath ?? Path.Combine(
            Environment.CurrentDirectory,
            $"vitals-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        using var engine = new SampleEngine(config);
        engine.WarmUp();

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        var snapshots = new List<Snapshot>();
        var started = DateTime.UtcNow;
        var next = Environment.TickCount64;
        Console.Error.WriteLine($"recording to {Path.GetFullPath(path)} — press Ctrl+C to stop");

        while (!stop.IsCancellationRequested)
        {
            snapshots.Add(engine.Sample());

            if (options.Duration is { } duration && DateTime.UtcNow - started >= duration)
            {
                break;
            }

            next += config.IntervalMs;
            var delay = next - Environment.TickCount64;
            if (delay > 0)
            {
                Thread.Sleep((int)delay);
            }
            else
            {
                next = Environment.TickCount64;
            }
        }

        Export.CsvExporter.Write(snapshots, path);
        Console.Error.WriteLine($"wrote {snapshots.Count} samples ({snapshots.Sum(s => s.Readings.Count)} readings) to {Path.GetFullPath(path)}");
        return 0;
    }

    private static void PrintSummary(Snapshot snapshot)
    {
        var parts = new List<string>
        {
            $"cpu {snapshot.CpuLoadPercent,5:0.0}%",
        };

        if (snapshot.CpuTempC is { } cpuTemp)
        {
            parts.Add($"cpu-temp {cpuTemp:0.0}°C");
        }

        if (snapshot.CpuPowerW is { } cpuPower)
        {
            parts.Add($"cpu-pwr {cpuPower:0.0}W");
        }

        if (snapshot.Memory is { } memory)
        {
            parts.Add($"mem {memory.UsedPercent:0.0}% ({memory.UsedBytes / 1024d / 1024d / 1024d:0.0} GB)");
        }

        if (snapshot.Gpu is { } gpu)
        {
            parts.Add($"gpu {gpu.LoadPercent:0}%");
            if (gpu.EdgeC is { } edge)
            {
                parts.Add($"gpu-temp {edge:0.0}°C");
            }

            if (gpu.HotspotC is { } hotspot)
            {
                parts.Add($"hotspot {hotspot:0.0}°C");
            }
        }

        parts.Add($"disk R {Fmt.Rate(snapshot.DiskReadBytesPerSecond)}  W {Fmt.Rate(snapshot.DiskWriteBytesPerSecond)}");

        if (snapshot.NetReceiveBytesPerSecond is { } rx && snapshot.NetSendBytesPerSecond is { } tx)
        {
            parts.Add($"net down {Fmt.Rate(rx)}  up {Fmt.Rate(tx)}");
        }

        Console.WriteLine($"{snapshot.Timestamp:HH:mm:ss}  {string.Join("  ", parts)}");
    }
}
