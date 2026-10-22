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
                    if (!TryTakeValue(args, ref i, out var outPath))
                    {
                        options.Error = "--out expects a file path";
                        return options;
                    }

                    options.OutPath = outPath;
                    continue;
                case "--config":
                case "-c":
                    if (!TryTakeValue(args, ref i, out var configPath))
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
        }

        return options;
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

        value = args[++index];
        return true;
    }

    private static bool TryParseDuration(string text, out TimeSpan span)
    {
        span = default;
        text = text.Trim().ToLowerInvariant();

        var unit = text.Length > 0 ? text[^1] : '\0';
        var numberPart = char.IsDigit(unit) ? text : text[..^1];

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return false;
        }

        span = unit switch
        {
            's' => TimeSpan.FromSeconds(number),
            'm' => TimeSpan.FromMinutes(number),
            'h' => TimeSpan.FromHours(number),
            'd' => TimeSpan.FromDays(number),
            _ => TimeSpan.FromSeconds(number),
        };

        return true;
    }

    public const string HelpText = """
        vitals — a fast, open-source Windows hardware monitor

        USAGE
          vitals [options]                 live TUI dashboard
          vitals snapshot [--json]         one-shot reading
          vitals watch [--json]            stream readings (NDJSON with --json)
          vitals export --out FILE         record a session to CSV
          vitals sensors [--json]          dump every raw sensor (for bug reports)

        OPTIONS
          -i, --interval <ms>    sampling interval, default 1000
          -d, --duration <time>  stop after 30s / 10m / 1h (watch, export)
          -o, --out <path>       output path
          -c, --config <path>    config file, default %APPDATA%\vitals\config.json
              --no-driver        skip LibreHardwareMonitor entirely
              --ascii            ASCII-only glyphs
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
