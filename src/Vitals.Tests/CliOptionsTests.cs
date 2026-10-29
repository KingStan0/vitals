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
    public void Parses_version_flag()
    {
        var options = CliOptions.Parse(new[] { "--version" });

        Assert.True(options.ShowVersion);
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
    [InlineData("snapshot")]
    [InlineData("sensors")]
    public void Rejects_interval_for_one_shot_commands(string command)
    {
        var options = CliOptions.Parse(new[] { command, "--interval", "500" });

        Assert.NotNull(options.Error);
        Assert.Contains("--interval", options.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tui")]
    [InlineData("watch")]
    [InlineData("export")]
    public void Allows_interval_for_sampling_commands(string command)
    {
        var options = CliOptions.Parse(new[] { command, "--interval", "500" });

        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("--out")]
    [InlineData("--config")]
    public void Rejects_empty_and_whitespace_paths(string option)
    {
        var empty = CliOptions.Parse(new[] { "watch", option, string.Empty });
        var whitespace = CliOptions.Parse(new[] { "watch", option, "   " });

        Assert.NotNull(empty.Error);
        Assert.NotNull(whitespace.Error);
    }

    [Fact]
    public void Help_and_version_are_terminal_actions_before_invalid_arguments()
    {
        var help = CliOptions.Parse(new[] { "--bogus", "--help" });
        var version = CliOptions.Parse(new[] { "--interval", "10", "--version" });
        var both = CliOptions.Parse(new[] { "--version", "--help", "--bogus" });

        Assert.True(help.ShowHelp);
        Assert.Null(help.Error);
        Assert.True(version.ShowVersion);
        Assert.Null(version.Error);
        Assert.True(both.ShowHelp);
        Assert.True(both.ShowVersion);
        Assert.Null(both.Error);
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("1.5s", 1.5)]
    public void Parses_durations(string text, double seconds)
    {
        var options = CliOptions.Parse(new[] { "watch", "--duration", text });

        Assert.Null(options.Error);
        Assert.Equal(TimeSpan.FromSeconds(seconds), options.Duration);
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("-1s")]
    [InlineData("1x")]
    [InlineData("1ms")]
    [InlineData("1e300d")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Rejects_invalid_or_overflowing_durations(string text)
    {
        var options = CliOptions.Parse(new[] { "watch", "--duration", text });

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void Rejects_multiple_commands()
    {
        var options = CliOptions.Parse(new[] { "snapshot", "watch" });

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void Rejects_json_for_the_dashboard()
    {
        var options = CliOptions.Parse(new[] { "--json" });

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void Rejects_duration_for_one_shot_commands()
    {
        var options = CliOptions.Parse(new[] { "snapshot", "--duration", "10s" });

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void Rejects_out_for_snapshot_but_allows_it_for_watch()
    {
        var snapshot = CliOptions.Parse(new[] { "snapshot", "--out", "x.json" });
        var watch = CliOptions.Parse(new[] { "watch", "--out", "x.ndjson" });

        Assert.NotNull(snapshot.Error);
        Assert.Null(watch.Error);
        Assert.Equal("x.ndjson", watch.OutPath);
    }

    [Fact]
    public void Rejects_missing_option_values()
    {
        var options = CliOptions.Parse(new[] { "snapshot", "--out", "--json" });

        Assert.NotNull(options.Error);
    }

    [Fact]
    public void Accepts_setup_command_and_install_flags()
    {
        var options = CliOptions.Parse(new[] { "setup", "--yes", "--no-start", "--force" });

        Assert.Equal("setup", options.Command);
        Assert.True(options.Yes);
        Assert.True(options.NoStart);
        Assert.True(options.Force);
        Assert.Null(options.Error);
    }

    [Fact]
    public void Accepts_setup_check()
    {
        var options = CliOptions.Parse(new[] { "setup", "--check" });

        Assert.Equal("setup", options.Command);
        Assert.True(options.Check);
        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--interval", "500")]
    [InlineData("--duration", "10s")]
    [InlineData("--out", "x")]
    [InlineData("--render-once")]
    [InlineData("--no-driver")]
    public void Rejects_sampling_and_output_options_for_setup(params string[] extra)
    {
        var args = new[] { "setup" }.Concat(extra).ToArray();
        var options = CliOptions.Parse(args);

        Assert.NotNull(options.Error);
    }

    [Theory]
    [InlineData("--yes")]
    [InlineData("--no-start")]
    [InlineData("--force")]
    public void Rejects_install_flags_with_setup_check(string flag)
    {
        var options = CliOptions.Parse(new[] { "setup", "--check", flag });

        Assert.NotNull(options.Error);
    }

    [Theory]
    [InlineData("snapshot", "--yes")]
    [InlineData("watch", "--no-start")]
    [InlineData("tui", "--check")]
    [InlineData("sensors", "--force")]
    public void Rejects_setup_flags_for_other_commands(string command, string flag)
    {
        var options = CliOptions.Parse(new[] { command, flag });

        Assert.NotNull(options.Error);
        Assert.Contains("setup", options.Error!, StringComparison.Ordinal);
    }
}
