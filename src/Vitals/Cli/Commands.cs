using System.Text;
using System.Text.Json;
using Vitals.Config;
using Vitals.Sampling;
using Vitals.Sensors;
using Vitals.Setup;
using Vitals.Ui;

namespace Vitals.Cli;

public static class Commands
{
    public static int Run(CliOptions options, AppConfig config)
    {
        return options.Command switch
        {
            "sensors" => Sensors(options, config),
            "snapshot" => Snapshot(options, config),
            "watch" => Watch(options, config),
            "export" => ExportCsv(options, config),
            "setup" => Setup(options, config),
            _ => 2,
        };
    }

    private static int Sensors(CliOptions options, AppConfig config)
    {
        using var engine = new SampleEngine(config);

        // One warm-up snapshot supplies both the rate baseline and the readings,
        // so one-shot commands do not force a second (expensive) LHM update.
        var snapshot = engine.WarmUp();

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot.Readings, JsonOutput.Pretty));
            return 0;
        }

        return PrintSensorDump(snapshot, config.Ascii, Console.Out);
    }

    internal static int PrintSensorDump(Snapshot snapshot, bool ascii, TextWriter output)
    {
        var status = snapshot.Status;
        var dot = SystemInfo.Dot(ascii);
        output.WriteLine($"vitals sensors  {dot}  {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        output.WriteLine($"elevated: {(status.Elevated ? "yes" : "no")}   " +
                         $"librehardwaremonitor: {(status.LhmAvailable ? "available" : "unavailable")}   " +
                         $"pawnio: {(status.PawnIoInstalled ? "installed" : "missing")}");

        if (status.LhmError is not null)
        {
            output.WriteLine($"lhm error: {Fmt.OneLine(status.LhmError)}");
        }

        foreach (var note in status.Notes)
        {
            output.WriteLine($"note: {Fmt.OneLine(note)}");
        }

        if (snapshot.Readings.Count == 0)
        {
            output.WriteLine();
            output.WriteLine("No sensors were reported. Run 'vitals setup' and use an elevated terminal.");
            return 1;
        }

        output.WriteLine();
        output.WriteLine($"{"ID",-42} {"KIND",-12} {"VALUE",10} {"UNIT",-6} HARDWARE / NAME");

        foreach (var reading in snapshot.Readings
                     .OrderBy(r => r.Source, StringComparer.Ordinal)
                     .ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var value = reading.Value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
            var unit = ascii
                ? reading.Unit.Replace("°C", "C", StringComparison.Ordinal)
                : reading.Unit;
            var id = Fmt.OneLine(reading.Id);
            var hardware = Fmt.OneLine(reading.Hardware);
            var name = Fmt.OneLine(reading.Name);
            output.WriteLine(
                $"{id,-42} {reading.Kind,-12} {value,10} {Fmt.OneLine(unit),-6} {hardware} / {name}");
        }

        return 0;
    }

    private static int Setup(CliOptions options, AppConfig config)
    {
        if (options.Check)
        {
            var installed = LhmSource.IsPawnIoInstalled();
            Console.WriteLine(installed
                ? "PawnIO is installed."
                : $"PawnIO is not installed (vitals can install {PawnIoSetup.Version}).");
            return installed ? 0 : 1;
        }

        if (!LhmSource.IsProcessElevated())
        {
            Console.Error.WriteLine("vitals: setup needs administrator rights.");
            Console.Error.WriteLine("        run 'vitals.cmd setup' or start an elevated terminal.");
            return 1;
        }

        if (LhmSource.IsPawnIoInstalled() && !options.Force)
        {
            Console.WriteLine("PawnIO is already installed.");
        }
        else
        {
            if (!options.Yes && !SetupPrompt.Confirm(Console.Out, Console.In))
            {
                Console.WriteLine("PawnIO was not installed.");
                return 1;
            }

            var result = PawnIoSetup.EnsureInstalled(new PawnIoSetupOptions
            {
                Force = options.Force,
                Debug = options.Debug,
            });

            if (result == PawnIoSetupResult.Failed)
            {
                return 1;
            }

            if (result == PawnIoSetupResult.RebootRequired)
            {
                Console.WriteLine("Restart Windows before using the PawnIO-backed sensors.");
                if (!options.NoStart)
                {
                    Console.WriteLine("Press Enter to start the dashboard, or Ctrl+C to exit.");
                    Console.ReadLine();
                }
            }
        }

        if (options.NoStart)
        {
            return 0;
        }

        return TuiApp.Run(options, config, setupPrompt: false);
    }

    private static int Snapshot(CliOptions options, AppConfig config)
    {
        using var engine = new SampleEngine(config);
        var snapshot = engine.WarmUp();

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot, JsonOutput.Pretty));
            return 0;
        }

        PrintSummary(snapshot, config.Ascii);
        return 0;
    }

    private static int Watch(CliOptions options, AppConfig config)
    {
        using var engine = new SampleEngine(config);

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        // Warm up before touching --out so a sensor failure cannot leave an
        // empty or truncated output file behind.
        var snapshot = engine.WarmUp();

        using var file = options.OutPath is null ? null : CreateTextWriter(options.OutPath);
        var output = file ?? Console.Out;
        if (file is not null)
        {
            var stopHint = options.Duration is { } limit
                ? $"stopping after {limit.TotalSeconds:0.###}s"
                : "press Ctrl+C to stop";
            Console.Error.WriteLine($"writing to {Path.GetFullPath(options.OutPath!)} - {stopHint}");
        }

        var started = Environment.TickCount64;
        var next = started;

        while (!stop.IsCancellationRequested)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(snapshot, JsonOutput.Compact));
            }
            else
            {
                PrintSummary(snapshot, config.Ascii, output);
            }

            if (file is not null)
            {
                file.Flush();
            }

            if (!WaitForNextSample(started, ref next, config.IntervalMs, options.Duration, stop.Token))
            {
                break;
            }

            snapshot = engine.Sample();
        }

        return 0;
    }

    private static StreamWriter CreateTextWriter(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(path, false, new UTF8Encoding(false));
    }

    private static int ExportCsv(CliOptions options, AppConfig config)
    {
        var path = options.OutPath ?? Path.Combine(
            Environment.CurrentDirectory,
            $"vitals-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        using var engine = new SampleEngine(config);

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        // Warm up before creating the CSV so a sensor failure cannot leave an
        // empty or header-only file behind.
        var snapshot = engine.WarmUp();

        var stopHint = options.Duration is { } limit
            ? $"stopping after {limit.TotalSeconds:0.###}s"
            : "press Ctrl+C to stop";
        Console.Error.WriteLine($"recording to {Path.GetFullPath(path)} - {stopHint}");

        using var writer = Export.CsvExporter.CreateWriter(path);
        Export.CsvExporter.WriteHeader(writer);

        var started = Environment.TickCount64;
        var next = started;
        var sampleCount = 0;
        var readingCount = 0;

        while (!stop.IsCancellationRequested)
        {
            Export.CsvExporter.WriteSnapshot(writer, snapshot);
            writer.Flush();
            sampleCount++;
            readingCount += snapshot.Readings.Count;

            if (!WaitForNextSample(started, ref next, config.IntervalMs, options.Duration, stop.Token))
            {
                break;
            }

            snapshot = engine.Sample();
        }

        Console.Error.WriteLine($"wrote {sampleCount} samples ({readingCount} readings) to {Path.GetFullPath(path)}");
        return 0;
    }

    /// <summary>
    /// Waits for the next sample using a monotonic clock. Unlike Thread.Sleep,
    /// this wakes immediately on Ctrl+C, and it caps the wait at the remaining
    /// --duration so a short run does not overshoot the requested time.
    /// </summary>
    private static bool WaitForNextSample(
        long started,
        ref long next,
        int intervalMs,
        TimeSpan? duration,
        CancellationToken token)
    {
        next += intervalMs;
        var now = Environment.TickCount64;
        var delay = next - now;

        if (duration is { } limit)
        {
            var remaining = (long)Math.Ceiling(limit.TotalMilliseconds) - (now - started);
            if (remaining <= 0)
            {
                return false;
            }

            delay = Math.Min(delay, remaining);
        }

        if (delay <= 0)
        {
            next = now;
            return !token.IsCancellationRequested;
        }

        return !token.WaitHandle.WaitOne((int)Math.Min(delay, int.MaxValue));
    }

    private static void PrintSummary(Snapshot snapshot, bool ascii, TextWriter? output = null)
    {
        var parts = new List<string>
        {
            $"cpu {snapshot.CpuLoadPercent,5:0.0}%",
        };

        if (snapshot.CpuTempC is { } cpuTemp)
        {
            parts.Add($"cpu-temp {cpuTemp:0.0}{Fmt.TempUnit(ascii)}");
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
            parts.Add(gpu.LoadPercent is { } gpuLoad ? $"gpu {gpuLoad:0}%" : "gpu n/a");
            if (gpu.EdgeC is { } edge)
            {
                parts.Add($"gpu-temp {edge:0.0}{Fmt.TempUnit(ascii)}");
            }

            if (gpu.HotspotC is { } hotspot)
            {
                parts.Add($"hotspot {hotspot:0.0}{Fmt.TempUnit(ascii)}");
            }
        }

        parts.Add($"disk R {Fmt.Rate(snapshot.DiskReadBytesPerSecond)}  W {Fmt.Rate(snapshot.DiskWriteBytesPerSecond)}");

        if (snapshot.NetReceiveBytesPerSecond is { } rx && snapshot.NetSendBytesPerSecond is { } tx)
        {
            parts.Add($"net down {Fmt.Rate(rx)}  up {Fmt.Rate(tx)}");
        }

        (output ?? Console.Out).WriteLine($"{snapshot.Timestamp:HH:mm:ss}  {string.Join("  ", parts)}");
    }
}
