using Spectre.Console;
using Spectre.Console.Testing;
using Vitals.Config;
using Vitals.Sampling;
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
    }
}
