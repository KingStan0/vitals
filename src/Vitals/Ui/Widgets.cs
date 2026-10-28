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

    public string? Error { get; set; }

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
            $"[bold {theme.Accent}]{Markup.Escape(Fmt.OneLine(Environment.MachineName))}[/]",
            $"[{theme.Muted}]{Markup.Escape(Fmt.OneLine(SystemInfo.CpuName))}[/]",
        };

        if (snapshot.Gpu is { } gpu)
        {
            identity.Add($"[{theme.Muted}]{Markup.Escape(Fmt.OneLine(gpu.Name))}[/]");
        }

        identity.Add($"[{theme.Muted}]{Markup.Escape(Fmt.OneLine(SystemInfo.OsName))}[/]");
        identity.Add($"[{theme.Muted}]up {Fmt.Uptime()}[/]");

        // The identity row cannot wrap without stealing a dashboard row, so on
        // narrow terminals fall back to one truncated, accent-coloured line.
        var identityText = string.Join($"  {dot}  ", new[]
        {
            Environment.MachineName,
            SystemInfo.CpuName,
            snapshot.Gpu?.Name,
            SystemInfo.OsName,
            $"up {Fmt.Uptime()}",
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var identityBudget = Math.Max(20, options.Width);
        var identityFitted = Fmt.Truncate(identityText, identityBudget, options.Ascii);
        if (!string.Equals(identityFitted, identityText, StringComparison.Ordinal))
        {
            identity.Clear();
            identity.Add($"[{theme.Accent}]{Markup.Escape(identityFitted)}[/]");
        }

        var statusBudget = Math.Max(20, options.Width);
        var status = new List<(string Markup, string Text)>();

        void AddStatus(string markup, string text) => status.Add((markup, text));

        if (options.Paused)
        {
            AddStatus("[bold yellow]paused[/]", "paused");
        }
        else
        {
            AddStatus("[green]live[/]", "live");
        }

        // Errors come before warnings and toasts so a narrow terminal drops the
        // least important status item instead of the reason sampling failed.
        var errorBudget = Math.Min(50, Math.Max(1, statusBudget - 10));
        if (snapshot.Status.LhmError is { } error)
        {
            var text = Fmt.Truncate(error, errorBudget, options.Ascii);
            AddStatus($"[red]{Markup.Escape(text)}[/]", text);
        }

        if (options.Error is { } samplingError)
        {
            var text = Fmt.Truncate(samplingError, errorBudget, options.Ascii);
            AddStatus($"[red]{Markup.Escape(text)}[/]", text);
        }

        if (!snapshot.Status.Elevated)
        {
            AddStatus("[yellow]no admin[/]", "no admin");
        }

        if (!snapshot.Status.PawnIoInstalled)
        {
            AddStatus("[yellow]no PawnIO[/]", "no PawnIO");
        }

        if (!snapshot.Status.LhmAvailable)
        {
            AddStatus("[yellow]driver off[/]", "driver off");
        }

        if (options.Toast is { } toast)
        {
            var text = Fmt.Truncate(toast, Math.Min(70, Math.Max(1, statusBudget - 10)), options.Ascii);
            AddStatus($"[bold {theme.Accent}]{Markup.Escape(text)}[/]", text);
        }

        var keptStatus = new List<string>();
        var statusLength = 0;
        var separatorLength = Fmt.DisplayWidth(dot) + 4;
        foreach (var item in status)
        {
            var extra = (keptStatus.Count == 0 ? 0 : separatorLength) + Fmt.DisplayWidth(item.Text);
            if (keptStatus.Count > 0 && statusLength + extra > statusBudget)
            {
                break;
            }

            keptStatus.Add(item.Markup);
            statusLength += extra;
        }

        var notes = snapshot.Status.Notes.Count > 0
            ? string.Join($"  {dot}  ", snapshot.Status.Notes)
            : null;
        var detail = notes ?? $"board {SystemInfo.Board}  {dot}  sampling {options.Config.IntervalMs} ms";

        return new Markup(
            $"{string.Join($"  {dot}  ", identity)}\n" +
            $"[{theme.Muted}]{string.Join($"  {dot}  ", keptStatus)}[/]\n" +
            $"[{theme.Muted}]{Markup.Escape(Fmt.Truncate(detail, Math.Max(20, options.Width - 4), options.Ascii))}[/]");
    }

    private static IRenderable BuildCpu(Snapshot snapshot, History history, RenderOptions options, int width)
    {
        var thresholds = options.Config.Thresholds;
        var theme = options.Theme;
        var load = snapshot.CpuLoadPercent;
        var color = load >= 90 ? "red" : load >= 70 ? "yellow" : "green";
        var (gaugeWidth, sparkWidth) = HeadlineWidths(width);

        var lines = new List<IRenderable>
        {
            new Markup($"[bold {color}]{load,3:0}%[/]  {Fmt.Gauge(load, gaugeWidth, color, options.Ascii)}  " +
                       $"[{theme.Muted}]{Fmt.Spark(history.CpuLoad.Values, sparkWidth, options.Ascii)}[/]"),
        };

        var details = new List<string>
        {
            $"[grey]temp[/] {Fmt.TempMarkup(snapshot.CpuTempC, thresholds.CpuTemp, options.Ascii)}",
        };

        if (snapshot.CpuPowerW is { } power && width >= 44)
        {
            details.Add($"[grey]pkg[/] [{theme.Accent}]{power:0} W[/]");
        }

        if (snapshot.CpuClockMhz is { } clock && width >= 60)
        {
            details.Add($"[grey]clock[/] [{theme.Accent}]{clock:0} MHz[/]");
        }

        details.Add($"[grey]peak[/] [{theme.Accent}]{history.CpuLoad.Peak:0}%[/]");
        lines.Add(new Markup(string.Join("   ", details)));

        if (snapshot.Cores.Count > 0)
        {
            var perRow = Math.Clamp((width - 4) / 16, 1, 6);
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

        return Pane("CPU", width, options.Ascii, lines.ToArray());
    }

    private static IRenderable BuildGpu(Snapshot snapshot, History history, RenderOptions options, int width)
    {
        var theme = options.Theme;

        if (snapshot.Gpu is not { } gpu)
        {
            var message = snapshot.Status.LhmAvailable
                ? "no GPU sensors reported"
                : snapshot.Status.LhmError is not null
                    ? "no GPU sensors (install PawnIO and run elevated)"
                    : "no GPU sensors (driver disabled)";
            return Pane("GPU", width, options.Ascii, new Markup($"[{theme.Muted}]{message}[/]"));
        }

        var thresholds = options.Config.Thresholds;
        var (gaugeWidth, sparkWidth) = HeadlineWidths(width);

        var lines = new List<IRenderable>();
        if (gpu.LoadPercent is { } load)
        {
            var color = load >= 90 ? "red" : load >= 70 ? "yellow" : "green";
            lines.Add(new Markup($"[bold {color}]{load,3:0}%[/]  {Fmt.Gauge(load, gaugeWidth, color, options.Ascii)}  " +
                                 $"[{theme.Muted}]{Fmt.Spark(history.GpuLoad.Values, sparkWidth, options.Ascii)}[/]"));
        }
        else
        {
            // Some drivers expose temperature and power but not load. An empty
            // 0% gauge reads as idle instead of unavailable.
            lines.Add(new Markup($"[{theme.Muted}]load n/a[/]"));
        }

        var temperatures = new List<string>
        {
            $"[grey]edge[/] {Fmt.TempMarkup(gpu.EdgeC, thresholds.GpuEdge, options.Ascii)}",
        };

        if (gpu.HotspotC is not null && width >= 30)
        {
            temperatures.Add($"[grey]hot[/] {Fmt.TempMarkup(gpu.HotspotC, thresholds.GpuHotspot, options.Ascii)}");
        }

        if (gpu.MemoryTempC is not null && width >= 44)
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

        var factText = FitParts(facts, Math.Max(8, width - 4), options.Ascii);
        if (factText.Length > 0)
        {
            lines.Add(new Markup($"[{theme.Muted}]{Markup.Escape(factText)}[/]"));
        }

        return Pane("GPU", width, options.Ascii, lines.ToArray());
    }

    private static IRenderable BuildMemory(Snapshot snapshot, History history, RenderOptions options, int width)
    {
        var theme = options.Theme;
        if (snapshot.Memory is not { } memory)
        {
            return Pane("MEMORY", width, options.Ascii, new Markup($"[{theme.Muted}]no memory data[/]"));
        }

        var color = Fmt.LevelColor(memory.UsedPercent, options.Config.Thresholds.Memory);
        var (gaugeWidth, _) = HeadlineWidths(width);
        var sparkWidth = Math.Clamp(width - 47, 0, 26);

        var line1 = new Markup(
            $"[bold {color}]{memory.UsedPercent,3:0}%[/]  {Fmt.Gauge(memory.UsedPercent, gaugeWidth, color, options.Ascii)}  " +
            $"[{theme.Accent}]{Fmt.Bytes(memory.UsedBytes)}[/][grey] / {Fmt.Bytes(memory.TotalBytes)}[/]");

        var detail = width >= 32
            ? $"[grey]commit[/] [{theme.Accent}]{Fmt.Bytes(memory.CommitBytes)}[/][grey] / {Fmt.Bytes(memory.CommitLimitBytes)}[/]"
            : $"[grey]commit[/] [{theme.Accent}]{Fmt.Bytes(memory.CommitBytes)}[/]";

        if (width >= 44)
        {
            detail += $"   [grey]cache[/] [{theme.Accent}]{Fmt.Bytes(memory.CachedBytes)}[/]";
        }

        if (sparkWidth >= 4)
        {
            detail += $"   [{theme.Muted}]{Fmt.Spark(history.MemoryUsed.Values, sparkWidth, options.Ascii)}[/]";
        }

        var line2 = new Markup(detail);

        return Pane("MEMORY", width, options.Ascii, line1, line2);
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

        return Pane("DISK / NET", width, options.Ascii, lines.ToArray());
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
            Add(temp.Label, temp.Celsius, new Threshold(80, 95));
        }

        if (chips.Count == 0)
        {
            return Pane("TEMPS", width, options.Ascii, new Markup($"[{theme.Muted}]no temperature sensors[/]"));
        }

        var contentWidth = Math.Max(20, width - 4);
        var columnWidth = Math.Max(10, (contentWidth - 1) / 2);
        var labelWidth = TemperatureLabelWidth(columnWidth);

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = true,
            ShowRowSeparators = false,
        };

        table.AddColumn(new TableColumn(string.Empty)
        {
            Width = columnWidth,
            NoWrap = true,
            Padding = new Padding(0, 0, 1, 0),
        });
        table.AddColumn(new TableColumn(string.Empty)
        {
            Width = columnWidth,
            NoWrap = true,
            Padding = new Padding(0, 0, 1, 0),
        });

        for (var i = 0; i < Math.Min(chips.Count, 10); i += 2)
        {
            var left = Chip(
                Fmt.Truncate(chips[i].Label, labelWidth, options.Ascii),
                chips[i].Value,
                chips[i].Limits,
                options);
            var right = i + 1 < chips.Count && i + 1 < 10
                ? Chip(
                    Fmt.Truncate(chips[i + 1].Label, labelWidth, options.Ascii),
                    chips[i + 1].Value,
                    chips[i + 1].Limits,
                    options)
                : new Markup(string.Empty);

            table.AddRow(left, right);
        }

        return Pane("TEMPS", width, options.Ascii, table);
    }

    internal static int TemperatureLabelWidth(int columnWidth) =>
        // Reserve the separator, up to three value digits, the unit and the
        // column's right padding. Four cells left the chip one cell too wide.
        Math.Max(4, columnWidth - 6);

    private static IRenderable Chip(string label, double value, Threshold limits, RenderOptions options)
    {
        var color = Fmt.LevelColor(value, limits);
        var unit = options.Ascii ? "C" : "°";
        return new Markup($"[grey]{Markup.Escape(label)}[/] [{color}]{value:0}{unit}[/]");
    }

    private static IRenderable BuildProcesses(Snapshot snapshot, RenderOptions options, int width)
    {
        var theme = options.Theme;

        // Sort every process, not just the CPU-trimmed list that JSON carries.
        IReadOnlyList<ProcessSample> source =
            snapshot.AllProcesses.Count > 0 ? snapshot.AllProcesses : snapshot.Processes;

        var ordered = options.Sort switch
        {
            ProcessSort.Memory => source.OrderByDescending(p => p.WorkingSetBytes),
            ProcessSort.Name => source.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes),
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

        var nameWidth = Math.Max(8, width - 24);
        foreach (var process in ordered.Take(count))
        {
            var name = Fmt.Truncate(process.Name, nameWidth, options.Ascii);
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

        return Pane($"PROCESSES by {options.Sort.ToString().ToUpperInvariant()}", width, options.Ascii, table);
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

        var max = Math.Max(20, options.Width - 2);
        var interval = $"interval {options.Config.IntervalMs} ms";
        var kept = new List<string>();
        var length = Fmt.DisplayWidth(interval) + 3;

        foreach (var key in keys)
        {
            var extra = (kept.Count == 0 ? 0 : 3) + Fmt.DisplayWidth(key);
            if (length + extra > max)
            {
                break;
            }

            kept.Add(key);
            length += extra;
        }

        var parts = kept.Select(key => $"[grey]{Markup.Escape(key)}[/]").ToList();
        parts.Add($"[{options.Theme.Muted}]{Markup.Escape(Fmt.Truncate(interval, max, options.Ascii))}[/]");

        return new Markup(string.Join("   ", parts));
    }

    /// <summary>
    /// Chooses gauge and spark widths so the CPU/GPU headline fills the panel
    /// content exactly instead of wrapping on narrow terminals.
    /// </summary>
    private static (int Gauge, int Spark) HeadlineWidths(int width)
    {
        var gauge = Math.Clamp(width / 4, 8, 16);
        var spark = Math.Clamp(width - 12 - gauge, 0, 30);
        return (gauge, spark);
    }

    /// <summary>Joins as many plain-text parts as fit on one panel row.</summary>
    private static string FitParts(IEnumerable<string> parts, int maxWidth, bool ascii)
    {
        var kept = new List<string>();
        var length = 0;

        foreach (var part in parts)
        {
            var extra = (kept.Count == 0 ? 0 : 3) + Fmt.DisplayWidth(part);
            if (kept.Count > 0 && length + extra > maxWidth)
            {
                break;
            }

            kept.Add(part);
            length += extra;
        }

        var text = string.Join("   ", kept);
        return Fmt.DisplayWidth(text) <= maxWidth ? text : Fmt.Truncate(text, maxWidth, ascii);
    }

    private static Panel Pane(string title, int width, bool ascii, params IRenderable[] content) => new(new Rows(content))
    {
        Header = new PanelHeader($" {title} "),
        Border = ascii ? BoxBorder.Ascii : BoxBorder.Rounded,
        BorderStyle = new Style(Color.Grey),
        Padding = new Padding(1, 0, 1, 0),
        Width = Math.Max(24, width),
    };
}
