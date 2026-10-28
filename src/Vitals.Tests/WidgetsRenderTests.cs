using Spectre.Console;
using Spectre.Console.Testing;
using Vitals.Config;
using Vitals.Sampling;
using Vitals.Sensors;
using Vitals.Ui;

namespace Vitals.Tests;

public sealed class WidgetsRenderTests
{
    private static RenderOptions Options(int width = 120, int height = 40, bool ascii = false) => new()
    {
        Config = new AppConfig(),
        Theme = Theme.Default,
        Ascii = ascii,
        Width = width,
        Height = height,
    };

    [Fact]
    public void Full_layout_renders_without_throwing()
    {
        var options = Options();
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(SnapshotFixture.Create());

        Widgets.Update(layout, SnapshotFixture.Create(), history, options);

        var console = new TestConsole();
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        console.Write(layout);

        var output = console.Output;
        Assert.Contains("CPU", output);
        Assert.Contains("GPU", output);
        Assert.Contains("MEMORY", output);
        Assert.Contains("PROCESSES", output);
        Assert.Contains("chrome", output);
        Assert.Contains("61", output);
    }

    [Fact]
    public void Compact_layout_renders_all_sections()
    {
        var options = Options(80, 30);
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(SnapshotFixture.Create());

        Widgets.Update(layout, SnapshotFixture.Create(), history, options);

        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Height = 30;
        console.Write(layout);

        Assert.Contains("CPU", console.Output);
        Assert.Contains("DISK / NET", console.Output);
    }

    [Fact]
    public void Ascii_mode_has_no_unicode_block_glyphs()
    {
        var options = Options(120, 40, ascii: true);
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(SnapshotFixture.Create());

        Widgets.Update(layout, SnapshotFixture.Create(), history, options);

        var console = new TestConsole();
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        console.Write(layout);

        Assert.DoesNotContain("█", console.Output);
        Assert.DoesNotContain("░", console.Output);
        Assert.DoesNotContain("▁", console.Output);
        Assert.DoesNotContain("╭", console.Output);
        Assert.DoesNotContain("…", console.Output);
        Assert.Contains("+", console.Output);
    }

    [Fact]
    public void Gpu_without_load_shows_unavailable_instead_of_zero()
    {
        var fixture = SnapshotFixture.Create();
        var snapshot = fixture with { Gpu = fixture.Gpu! with { LoadPercent = null } };
        var options = Options();
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(snapshot);

        Widgets.Update(layout, snapshot, history, options);

        var console = new TestConsole();
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        console.Write(layout);

        Assert.Contains("load n/a", console.Output);
    }

    [Fact]
    public void Memory_sort_uses_every_process_not_only_the_cpu_trimmed_list()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            Processes = new[] { new ProcessSample(1, "cpu-hog", 50, 1_000) },
            AllProcesses = new[]
            {
                new ProcessSample(1, "cpu-hog", 50, 1_000),
                new ProcessSample(2, "memory-hog", 0.5, 9_000_000_000),
            },
        };
        var options = Options();
        options.Sort = ProcessSort.Memory;
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(snapshot);

        Widgets.Update(layout, snapshot, history, options);

        var console = new TestConsole();
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        console.Write(layout);

        Assert.Contains("memory-hog", console.Output);
    }

    [Fact]
    public void Very_narrow_layout_renders_all_sections()
    {
        var options = Options(40, 30);
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(SnapshotFixture.Create());

        Widgets.Update(layout, SnapshotFixture.Create(), history, options);

        var console = new TestConsole();
        console.Profile.Width = 40;
        console.Profile.Height = 30;
        console.Write(layout);

        Assert.Contains("CPU", console.Output);
        Assert.Contains("MEMORY", console.Output);
        Assert.Contains("DISK / NET", console.Output);
    }

    [Fact]
    public void Paused_state_is_rendered_in_the_header()
    {
        var options = Options();
        options.Paused = true;
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(SnapshotFixture.Create());

        Widgets.Update(layout, SnapshotFixture.Create(), history, options);

        var console = new TestConsole();
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        console.Write(layout);

        Assert.Contains("paused", console.Output);
    }

    [Fact]
    public void Temperature_label_width_reserves_space_for_three_digit_values()
    {
        Assert.Equal(11, Widgets.TemperatureLabelWidth(17));
        Assert.Equal(4, Widgets.TemperatureLabelWidth(7));
    }

    [Fact]
    public void Three_digit_temperatures_render_at_compact_width()
    {
        var fixture = SnapshotFixture.Create();
        var snapshot = fixture with
        {
            CpuTempC = 105,
            Gpu = fixture.Gpu! with { EdgeC = 100, HotspotC = 110, MemoryTempC = 99 },
            Temps = new[] { new TempSample("Very Long Board Sensor", 101, "board") },
        };
        var options = Options(40, 40);
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(snapshot);

        Widgets.Update(layout, snapshot, history, options);

        var console = new TestConsole();
        console.Profile.Width = 40;
        console.Profile.Height = 40;
        console.Write(layout);

        Assert.Contains("105", console.Output);
        Assert.Contains("110", console.Output);
    }

    [Fact]
    public void Wide_header_status_text_does_not_overflow_the_terminal()
    {
        var fixture = SnapshotFixture.Create();
        var snapshot = fixture with
        {
            Status = fixture.Status with
            {
                Elevated = false,
                PawnIoInstalled = false,
            },
        };
        var options = Options(40, 40);
        options.Error = new string('温', 15);
        var layout = Widgets.CreateLayout(options);
        var history = new History(300);
        history.Add(snapshot);

        Widgets.Update(layout, snapshot, history, options);

        var console = new TestConsole();
        console.Profile.Width = 40;
        console.Profile.Height = 40;
        console.Write(layout);

        foreach (var line in console.Output.Split('\n'))
        {
            Assert.True(
                Fmt.DisplayWidth(line.TrimEnd('\r')) <= 40,
                $"line is wider than the terminal: {line}");
        }
    }
}
