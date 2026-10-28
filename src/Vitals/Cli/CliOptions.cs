using System.Globalization;
using Vitals.Config;

namespace Vitals.Cli;

public sealed class CliOptions
{
    public string Command { get; private set; } = "tui";

    public bool Json { get; private set; }

    public bool NoColor { get; private set; }

    public bool Ascii { get; private set; }

    public bool NoDriver { get; private set; }

    public bool Debug { get; private set; }

    public bool RenderOnce { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool ShowVersion { get; private set; }

    public int? IntervalMs { get; private set; }

    public TimeSpan? Duration { get; private set; }

    public string? OutPath { get; private set; }

    public string? ConfigPath { get; private set; }

    public string? Error { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        // Help and version are terminal actions even when another argument is
        // invalid, so scan for them before the normal parser can return early.
        foreach (var arg in args)
        {
            if (arg is "-h" or "--help")
            {
                options.ShowHelp = true;
            }
            else if (arg == "--version")
            {
                options.ShowVersion = true;
            }
        }

        if (options.ShowHelp || options.ShowVersion)
        {
            return options;
        }

        var commandSeen = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    continue;
                case "--version":
                    options.ShowVersion = true;
                    continue;
                case "--json":
                    options.Json = true;
                    continue;
                case "--no-color":
                    options.NoColor = true;
                    continue;
                case "--ascii":
                    options.Ascii = true;
                    continue;
                case "--no-driver":
                    options.NoDriver = true;
                    continue;
                case "--debug":
                    options.Debug = true;
                    continue;
                case "--render-once":
                    options.RenderOnce = true;
                    continue;
                case "--interval":
                case "-i":
                    if (!TryTakeValue(args, ref i, out var interval) ||
                        !int.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ||
                        ms < 250 || ms > 60_000)
                    {
                        options.Error = "--interval expects milliseconds between 250 and 60000";
                        return options;
                    }

                    options.IntervalMs = ms;
                    continue;
                case "--duration":
                case "-d":
                    if (!TryTakeValue(args, ref i, out var duration) || !TryParseDuration(duration, out var span))
                    {
                        options.Error = "--duration expects a value like 30s, 10m or 1h";
                        return options;
                    }

                    options.Duration = span;
                    continue;
                case "--out":
                case "-o":
                    if (!TryTakeValue(args, ref i, out var outPath) || string.IsNullOrWhiteSpace(outPath))
                    {
                        options.Error = "--out expects a file path";
                        return options;
                    }

                    options.OutPath = outPath;
                    continue;
                case "--config":
                case "-c":
                    if (!TryTakeValue(args, ref i, out var configPath) || string.IsNullOrWhiteSpace(configPath))
                    {
                        options.Error = "--config expects a file path";
                        return options;
                    }

                    options.ConfigPath = configPath;
                    continue;
            }

            if (arg.StartsWith('-'))
            {
                options.Error = $"unknown option: {arg}";
                return options;
            }

            if (commandSeen)
            {
                options.Error = "only one command may be specified";
                return options;
            }

            options.Command = arg switch
            {
                "tui" or "run" => "tui",
                "sensors" => "sensors",
                "snapshot" => "snapshot",
                "watch" => "watch",
                "export" => "export",
                _ => string.Empty,
            };

            if (options.Command.Length == 0)
            {
                options.Error = $"unknown command: {arg}";
                return options;
            }

            commandSeen = true;
        }

        options.Validate();
        return options;
    }

    private void Validate()
    {
        if (Command == "tui")
        {
            if (Json)
            {
                Error = "--json requires a command: snapshot, watch or sensors";
            }
            else if (Duration is not null)
            {
                Error = "--duration is only supported by watch and export";
            }
            else if (OutPath is not null)
            {
                Error = "--out is only supported by watch and export";
            }
        }
        else if (Command is "snapshot" or "sensors")
        {
            if (IntervalMs is not null)
            {
                Error = "--interval is only supported by the dashboard, watch and export";
            }
            else if (OutPath is not null)
            {
                Error = "--out is only supported by watch and export";
            }
            else if (Duration is not null)
            {
                Error = "--duration is only supported by watch and export";
            }
            else if (RenderOnce)
            {
                Error = "--render-once is only supported by the dashboard";
            }
        }
        else if (Command == "watch")
        {
            if (RenderOnce)
            {
                Error = "--render-once is only supported by the dashboard";
            }
        }
        else if (Command == "export")
        {
            if (Json)
            {
                Error = "--json is not supported by export";
            }
            else if (RenderOnce)
            {
                Error = "--render-once is only supported by the dashboard";
            }
        }
    }

    public void ApplyTo(AppConfig config)
    {
        if (IntervalMs is { } interval)
        {
            config.IntervalMs = interval;
        }

        if (Ascii)
        {
            config.Ascii = true;
        }

        config.NoDriver = NoDriver;
        config.Normalize();
    }

    private static bool TryTakeValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        var candidate = args[index + 1];
        if (LooksLikeOption(candidate))
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool LooksLikeOption(string value) =>
        value.StartsWith("--", StringComparison.Ordinal) ||
        value is "-h" or "-i" or "-d" or "-o" or "-c";

    private static bool TryParseDuration(string text, out TimeSpan span)
    {
        span = default;
        text = text.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return false;
        }

        var unit = text[^1];
        var hasUnit = unit is 's' or 'm' or 'h' or 'd';
        var numberPart = hasUnit ? text[..^1] : text;

        if (numberPart.Length == 0 ||
            !double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number) ||
            number <= 0)
        {
            return false;
        }

        if (!hasUnit && !char.IsDigit(unit))
        {
            return false;
        }

        try
        {
            span = unit switch
            {
                's' => TimeSpan.FromSeconds(number),
                'm' => TimeSpan.FromMinutes(number),
                'h' => TimeSpan.FromHours(number),
                'd' => TimeSpan.FromDays(number),
                _ => TimeSpan.FromSeconds(number),
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        return span > TimeSpan.Zero;
    }

    public const string HelpText = """
        vitals - a fast, open-source Windows hardware monitor

        USAGE
          vitals [options]                 live TUI dashboard
          vitals snapshot [--json]         one-shot reading
          vitals watch [--json]            stream readings (NDJSON with --json)
          vitals export --out FILE         record a session to CSV
          vitals sensors [--json]          dump every raw sensor (for bug reports)

        OPTIONS
          -i, --interval <ms>    sampling interval (TUI, watch, export), default 1000
          -d, --duration <time>  stop after 30s / 10m / 1h (watch, export)
          -o, --out <path>       output path (watch, export)
          -c, --config <path>    config file, default %APPDATA%\vitals\config.json
              --json             machine-readable output (snapshot, watch, sensors)
              --no-driver        skip LibreHardwareMonitor entirely
              --ascii            ASCII-only glyphs (TUI and human-readable output)
              --no-color         disable colour
              --render-once      render one dashboard frame and exit (testing)
              --debug            verbose errors
              --version          print the version and exit
          -h, --help             this text

        TUI KEYS
          q quit    space pause    r reset peaks    s sort processes
          t theme   e export CSV   [ ] slower/faster refresh
        """;
}
