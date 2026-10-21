using Vitals.Cli;

namespace Vitals.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Defaults_to_tui()
    {
        var options = CliOptions.Parse(Array.Empty<string>());

        Assert.Equal("tui", options.Command);
        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("watch")]
    [InlineData("export")]
    [InlineData("sensors")]
    public void Accepts_known_commands(string command)
    {
        var options = CliOptions.Parse(new[] { command });

        Assert.Equal(command, options.Command);
        Assert.Null(options.Error);
    }

    [Fact]
    public void Parses_flags_and_values()
    {
        var options = CliOptions.Parse(new[]
        {
            "watch", "--json", "--interval", "500", "--duration", "10m", "--out", "x.ndjson",
        });

        Assert.Equal("watch", options.Command);
        Assert.True(options.Json);
        Assert.Equal(500, options.IntervalMs);
        Assert.Equal(TimeSpan.FromMinutes(10), options.Duration);
        Assert.Equal("x.ndjson", options.OutPath);
        Assert.Null(options.Error);
    }

    [Fact]
    public void Rejects_unknown_command()
    {
        var options = CliOptions.Parse(new[] { "frobnicate" });

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void Rejects_bad_interval()
    {
        var options = CliOptions.Parse(new[] { "snapshot", "--interval", "10" });

        Assert.NotNull(options.Error);
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    public void Parses_durations(string text, int seconds)
    {
        var options = CliOptions.Parse(new[] { "watch", "--duration", text });

        Assert.Null(options.Error);
        Assert.Equal(TimeSpan.FromSeconds(seconds), options.Duration);
    }
}
