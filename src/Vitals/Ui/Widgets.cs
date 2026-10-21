using Spectre.Console;
using Spectre.Console.Rendering;
using Vitals.Config;
using Vitals.Sampling;
using Vitals.Sensors;

namespace Vitals.Ui;

public enum ProcessSort
{
    Cpu,
    Memory,
    Name,
}

public sealed class RenderOptions
{
    public required AppConfig Config { get; init; }

    public required Theme Theme { get; set; }

    public required bool Ascii { get; init; }

    public ProcessSort Sort { get; set; } = ProcessSort.Cpu;

    public bool Paused { get; set; }

    public string? Toast { get; set; }

    public int Width { get; set; } = 120;

    public int Height { get; set; } = 32;
}

public static class Widgets
{
    public static Layout CreateLayout(RenderOptions options)
    {
        var root = new Layout("Root");
        var header = new Layout("Header") { Size = 3 };
        var content = new Layout("Content");
        var footer = new Layout("Footer") { Size = 2 };

        root.SplitRows(header, content, footer);
        return root;
    }

    public static void Update(Layout layout, Snapshot snapshot, History history, RenderOptions options)
    {
        Update(layout, "Header", BuildHeader(snapshot, options));
        Update(layout, "Content", BuildBody(snapshot, history, options));
        Update(layout, "Footer", BuildFooter(options));
    }

    /// <summary>
    /// Panels are content-sized and stacked inside a two-column table. This keeps
    /// sections glued together instead of leaving gaps in ratio-based layout slots.
    /// </summary>
    private static IRenderable BuildBody(Snapshot snapshot, History history, RenderOptions options)
    {
        if (options.Width < 100)
        {
            var width = Math.Max(40, options.Width);
            return new Rows(
                BuildCpu(snapshot, history, options, width),
                BuildGpu(snapshot, history, options, width),
                BuildMemory(snapshot, history, options, width),
                BuildDiskNet(snapshot, options, width),
                BuildTemps(snapshot, options, width),
                BuildProcesses(snapshot, options, width));
        }

        var available = Math.Max(90, options.Width - 2);
        var leftWidth = Math.Max(52, (int)Math.Round(available * 0.58));
        var rightWidth = Math.Max(34, available - leftWidth - 1);

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = false,
        };

        table.AddColumn(new TableColumn(string.Empty)
        {
            Width = leftWidth + 1,
            Padding = new Padding(0, 0, 1, 0),
        });
        table.AddColumn(new TableColumn(string.Empty)
        {
            Width = rightWidth,
            Padding = new Padding(0, 0, 0, 0),
        });

        table.AddRow(
            new Rows(
                BuildCpu(snapshot, history, options, leftWidth),
                BuildMemory(snapshot, history, options, leftWidth),
                BuildDiskNet(snapshot, options, leftWidth)),
            new Rows(
                BuildGpu(snapshot, history, options, rightWidth),
                BuildTemps(snapshot, options, rightWidth),
                BuildProcesses(snapshot, options, rightWidth)));

        return table;
    }

    private static void Update(Layout root, string name, IRenderable content)
    {
        Layout? target = null;
        try
        {
            target = root[name];
        }
        catch
        {
            // Panel not present in compact layouts.
        }

        target?.Update(content);
    }

    private static IRenderable BuildHeader(Snapshot snapshot, RenderOptions options)
    {
        var theme = options.Theme;
        var dot = SystemInfo.Dot(options.Ascii);

        var identity = new List<string>
        {
            $"[bold {theme.Accent}]{Markup.Escape(Environment.MachineName)}[/]",
            $"[{theme.Muted}]{Markup.Escape(SystemInfo.CpuName)}[/]",
        };

        if (snapshot.Gpu is { } gpu)
        {
            identity.Add($"[{theme.Muted}]{Markup.Escape(gpu.Name)}[/]");
        }

        identity.Add($"[{theme.Muted}]{Markup.Escape(SystemInfo.OsName)}[/]");
        identity.Add($"[{theme.Muted}]up {Fmt.Uptime()}[/]");

        var status = new List<string>();
        if (options.Paused)
        {
            status.Add("[bold yellow]paused[/]");
        }
        else
        {
            status.Add("[green]live[/]");
        }

        if (!snapshot.Status.Elevated)
        {
            status.Add("[yellow]no admin[/]");
        }

        if (!snapshot.Status.PawnIoInstalled)
        {
            status.Add("[yellow]no PawnIO[/]");
        }

        if (!snapshot.Status.LhmAvailable)
        {
            status.Add("[yellow]driver off[/]");
        }

        if (snapshot.Status.LhmError is { } error)
        {
            status.Add($"[red]{Markup.Escape(Fmt.Truncate(error, 50))}[/]");
        }

        if (options.Toast is { } toast)
        {
            status.Add($"[bold {theme.Accent}]{Markup.Escape(Fmt.Truncate(toast, 70))}[/]");
        }

        var note = snapshot.Status.Notes.FirstOrDefault();
        var detail = note ?? $"board {SystemInfo.Board}  {dot}  sampling {options.Config.IntervalMs} ms";

        return new Markup(
            $"{string.Join($"  {dot}  ", identity)}\n" +
            $"[{theme.Muted}]{string.Join($"  {dot}  ", status)}[/]\n" +
            $"[{theme.Muted}]{Markup.Escape(Fmt.Truncate(detail, Math.Max(20, options.Width - 4)))}[/]");
    }

    private static IRenderable BuildCpu(Snapshot snapshot, History history, RenderOptions options, int width)
    {
        var thresholds = options.Config.Thresholds;
        var theme = options.Theme;
        var load = snapshot.CpuLoadPercent;
        var color = load >= 90 ? "red" : load >= 70 ? "yellow" : "green";
        var sparkWidth = options.Width >= 130 ? 30 : 18;

        var lines = new List<IRenderable>
        {
            new Markup($"[bold {color}]{load,3:0}%[/]  {Fmt.Gauge(load, 16, color, options.Ascii)}  " +
                       $"[{theme.Muted}]{Fmt.Spark(history.CpuLoad.Values, sparkWidth, options.Ascii)}[/]"),
        };

        var details = new List<string>
        {
            $"[grey]temp[/] {Fmt.TempMarkup(snapshot.CpuTempC, thresholds.CpuTemp, options.Ascii)}",
        };

        if (snapshot.CpuPowerW is { } power)
        {
            details.Add($"[grey]pkg[/] [{theme.Accent}]{power:0} W[/]");
        }

        if (snapshot.CpuClockMhz is { } clock)
        {
            details.Add($"[grey]clock[/] [{theme.Accent}]{clock:0} MHz[/]");
        }

        details.Add($"[grey]peak[/] [{theme.Accent}]{history.CpuLoad.Peak:0}%[/]");
        lines.Add(new Markup(string.Join("   ", details)));

        if (snapshot.Cores.Count > 0)
        {
            var perRow = options.Width >= 140 ? 6 : 4;
            for (var i = 0; i < snapshot.Cores.Count; i += perRow)
            {
                var cells = snapshot.Cores
                    .Skip(i)
                    .Take(perRow)
                    .Select(core =>
                    {
                        var percent = core.LoadPercent ?? 0;
                        var coreColor = percent >= 90 ? "red" : percent >= 70 ? "yellow" : "green";
                        return $"[grey]c{core.Index}[/] [{coreColor}]{Fmt.Bar(percent, 5, options.Ascii)}[/] {percent,3:0}%";
                    });

                lines.Add(new Markup(string.Join("  ", cells)));
            }
        }
        else if (snapshot.Status.LhmAvailable)
        {
            lines.Add(new Markup($"[{theme.Muted}]per-core sensors unavailable[/]"));
        }

        return Pane("CPU", width, lines.ToArray());
    }

    private static IRenderable BuildGpu(Snapshot snapshot, History history, RenderOptions options, int width)
    {
        var theme = options.Theme;

        if (snapshot.Gpu is not { } gpu)
        {
            return Pane("GPU", width, new Markup($"[{theme.Muted}]no GPU sensors (install PawnIO and run elevated)[/]"));
        }

        var thresholds = options.Config.Thresholds;
        var load = gpu.LoadPercent ?? 0;
        var color = load >= 90 ? "red" : load >= 70 ? "yellow" : "green";
        var sparkWidth = options.Width >= 130 ? 24 : 14;

        var lines = new List<IRenderable>
        {
            new Markup($"[bold {color}]{load,3:0}%[/]  {Fmt.Gauge(load, 16, color, options.Ascii)}  " +
                       $"[{theme.Muted}]{Fmt.Spark(history.GpuLoad.Values, sparkWidth, options.Ascii)}[/]"),
        };

        var temperatures = new List<string>
        {
            $"[grey]edge[/] {Fmt.TempMarkup(gpu.EdgeC, thresholds.GpuEdge, options.Ascii)}",
        };

        if (gpu.HotspotC is not null)
        {
            temperatures.Add($"[grey]hot[/] {Fmt.TempMarkup(gpu.HotspotC, thresholds.GpuHotspot, options.Ascii)}");
        }

        if (gpu.MemoryTempC is not null)
        {
            temperatures.Add($"[grey]vram[/] {Fmt.TempMarkup(gpu.MemoryTempC, thresholds.GpuMemory, options.Ascii)}");
        }

        lines.Add(new Markup(string.Join("   ", temperatures)));

        var facts = new List<string>();
        if (gpu.PowerW is { } power)
        {
            facts.Add($"{power:0} W");
        }

        if (gpu.ClockMhz is { } clock)
        {
            facts.Add($"{clock:0} MHz");
        }

        if (gpu.MemoryUsedBytes is { } used && gpu.MemoryTotalBytes is { } total && total > 0)
        {
            facts.Add($"{used / 1024d / 1024d / 1024d:0.0}/{total / 1024d / 1024d / 1024d:0.0} GB");
        }

        if (gpu.FanRpm is { } rpm)
        {
            var fan = gpu.FanPercent is { } fanPercent ? $"fan {fanPercent:0}% {rpm:0} rpm" : $"fan {rpm:0} rpm";
            facts.Add(fan);
        }
        else if (gpu.FanPercent is { } fanPercent)
        {
            facts.Add($"fan {fanPercent:0}%");
        }

        if (facts.Count > 0)
        {
            lines.Add(new Markup($"[{theme.Muted}]{string.Join("   ", facts)}[/]"));
        }

        return Pane("GPU", width, lines.ToArray());
    }

    private static IRenderable BuildMemory(Snapshot snapshot, History history, RenderOptions options, int width)
    {
        var theme = options.Theme;
        if (snapshot.Memory is not { } memory)
        {
            return Pane("MEMORY", width, new Markup($"[{theme.Muted}]no memory data[/]"));
        }

        var color = Fmt.LevelColor(memory.UsedPercent, options.Config.Thresholds.Memory);
        var sparkWidth = options.Width >= 130 ? 26 : 16;

        var line1 = new Markup(
            $"[bold {color}]{memory.UsedPercent,3:0}%[/]  {Fmt.Gauge(memory.UsedPercent, 16, color, options.Ascii)}  " +
            $"[{theme.Accent}]{Fmt.Bytes(memory.UsedBytes)}[/][grey] / {Fmt.Bytes(memory.TotalBytes)}[/]");

        var line2 = new Markup(
            $"[grey]commit[/] [{theme.Accent}]{Fmt.Bytes(memory.CommitBytes)}[/][grey] / {Fmt.Bytes(memory.CommitLimitBytes)}[/]   " +
            $"[grey]cache[/] [{theme.Accent}]{Fmt.Bytes(memory.CachedBytes)}[/]   " +
            $"[{theme.Muted}]{Fmt.Spark(history.MemoryUsed.Values, sparkWidth, options.Ascii)}[/]");

        return Pane("MEMORY", width, line1, line2);
    }

    private static IRenderable BuildDiskNet(Snapshot snapshot, RenderOptions options, int width)
    {
        var theme = options.Theme;
        var lines = new List<IRenderable>();

        if (snapshot.Volumes.Count > 0)
        {
            foreach (var volume in snapshot.Volumes.Take(3))
            {
                var color = volume.UsedPercent >= 95 ? "red" : volume.UsedPercent >= 85 ? "yellow" : "green";
                lines.Add(new Markup(
                    $"[grey]{Markup.Escape(volume.Name)}[/] {Fmt.Gauge(volume.UsedPercent, 10, color, options.Ascii)} " +
                    $"[{color}]{volume.UsedPercent,3:0}%[/] [grey]{Fmt.Bytes(volume.TotalBytes - volume.FreeBytes)} / {Fmt.Bytes(volume.TotalBytes)}[/]"));
            }
        }

        lines.Add(new Markup(
            $"[grey]disk[/] [{theme.Accent}]R {Fmt.Rate(snapshot.DiskReadBytesPerSecond)}[/]  " +
            $"[{theme.Accent}]W {Fmt.Rate(snapshot.DiskWriteBytesPerSecond)}[/]"));

        if (snapshot.NetReceiveBytesPerSecond is { } rx && snapshot.NetSendBytesPerSecond is { } tx)
        {
            var arrowDown = options.Ascii ? "down" : "↓";
            var arrowUp = options.Ascii ? "up" : "↑";
            lines.Add(new Markup(
                $"[grey]net[/]  [{theme.Accent}]{arrowDown} {Fmt.Rate(rx)}[/]  " +
                $"[{theme.Accent}]{arrowUp} {Fmt.Rate(tx)}[/]"));
        }

        return Pane("DISK / NET", width, lines.ToArray());
    }

    private static IRenderable BuildTemps(Snapshot snapshot, RenderOptions options, int width)
    {
        var theme = options.Theme;
        var thresholds = options.Config.Thresholds;
        var chips = new List<(string Label, double Value, Threshold Limits)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string label, double? value, Threshold limits)
        {
            if (value is null || !seen.Add(label))
            {
                return;
            }

            chips.Add((label, value.Value, limits));
        }

        Add("cpu", snapshot.CpuTempC, thresholds.CpuTemp);
        Add("gpu", snapshot.Gpu?.EdgeC, thresholds.GpuEdge);
        Add("hot", snapshot.Gpu?.HotspotC, thresholds.GpuHotspot);
        Add("vram", snapshot.Gpu?.MemoryTempC, thresholds.GpuMemory);

        foreach (var temp in snapshot.Temps)
        {
            Add(Fmt.Truncate(temp.Label, 11), temp.Celsius, new Threshold(80, 95));
        }

        if (chips.Count == 0)
        {
            return Pane("TEMPS", width, new Markup($"[{theme.Muted}]no temperature sensors[/]"));
        }

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = true,
            ShowRowSeparators = false,
        };

        table.AddColumn(new TableColumn(string.Empty)
        {
            Width = 20,
            NoWrap = true,
            Padding = new Padding(0, 0, 1, 0),
        });
        table.AddColumn(new TableColumn(string.Empty)
        {
            Width = 20,
            NoWrap = true,
            Padding = new Padding(0, 0, 1, 0),
        });

        for (var i = 0; i < Math.Min(chips.Count, 10); i += 2)
        {
            var left = Chip(chips[i].Label, chips[i].Value, chips[i].Limits, options);
            var right = i + 1 < chips.Count && i + 1 < 10
                ? Chip(chips[i + 1].Label, chips[i + 1].Value, chips[i + 1].Limits, options)
                : new Markup(string.Empty);

            table.AddRow(left, right);
        }

        return Pane("TEMPS", width, table);
    }

    private static IRenderable Chip(string label, double value, Threshold limits, RenderOptions options)
    {
        var color = Fmt.LevelColor(value, limits);
        var unit = options.Ascii ? "C" : "°";
        return new Markup($"[grey]{Markup.Escape(label)}[/] [{color}]{value:0}{unit}[/]");
    }

    private static IRenderable BuildProcesses(Snapshot snapshot, RenderOptions options, int width)
    {
        var theme = options.Theme;

        var ordered = options.Sort switch
        {
            ProcessSort.Memory => snapshot.Processes.OrderByDescending(p => p.WorkingSetBytes),
            ProcessSort.Name => snapshot.Processes.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => snapshot.Processes.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes),
        };

        var room = Math.Max(3, (options.Height - 20) / 2);
        var count = Math.Min(options.Config.ProcessCount, room);

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = true,
        };

        table.AddColumn(new TableColumn(string.Empty) { NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty) { Width = 7, NoWrap = true, Alignment = Justify.Right });
        table.AddColumn(new TableColumn(string.Empty) { Width = 9, NoWrap = true, Alignment = Justify.Right });

        foreach (var process in ordered.Take(count))
        {
            var name = Fmt.Truncate(process.Name, 24);
            var color = process.CpuPercent >= 50 ? "red" : process.CpuPercent >= 10 ? "yellow" : "grey";

            table.AddRow(
                new Markup($"[white]{Markup.Escape(name)}[/]"),
                new Markup($"[{color}]{process.CpuPercent,5:0.0}%[/]"),
                new Markup($"[{theme.Muted}]{Fmt.Bytes(process.WorkingSetBytes)}[/]"));
        }

        if (count == 0)
        {
            table.AddRow(new Markup($"[{theme.Muted}]no processes[/]"), new Markup(string.Empty), new Markup(string.Empty));
        }

        return Pane($"PROCESSES by {options.Sort.ToString().ToUpperInvariant()}", width, table);
    }

    private static IRenderable BuildFooter(RenderOptions options)
    {
        var keys = new[]
        {
            "q quit",
            "space pause",
            "r peaks",
            "s sort",
            "t theme",
            "e export",
            "[ ] interval",
        };

        var parts = keys.Select(key => $"[grey]{Markup.Escape(key)}[/]").ToList();
        parts.Add($"[{options.Theme.Muted}]interval {options.Config.IntervalMs} ms[/]");

        return new Markup(string.Join("   ", parts));
    }

    private static Panel Pane(string title, int width, params IRenderable[] content) => new(new Rows(content))
    {
        Header = new PanelHeader($" {title} "),
        Border = BoxBorder.Rounded,
        BorderStyle = new Style(Color.Grey),
        Padding = new Padding(1, 0, 1, 0),
        Width = Math.Max(24, width),
    };
}
