using System.Globalization;
using Spectre.Console;
using Vitals.Config;

namespace Vitals.Ui;

public static class Fmt
{
    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(double bytes)
    {
        var value = Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < ByteUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value.ToString("0", CultureInfo.InvariantCulture)} B"
            : $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {ByteUnits[unit]}";
    }

    public static string Rate(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Truncate(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        return text.Length <= width ? text : text[..Math.Max(1, width - 1)] + "…";
    }

    public static string Bar(double percent, int width, bool ascii)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var filled = (int)Math.Round(clamped / 100.0 * width);
        var on = ascii ? '#' : '█';
        var off = ascii ? '-' : '░';
        return new string(on, filled) + new string(off, Math.Max(0, width - filled));
    }

    public static string Gauge(double percent, int width, string color, bool ascii) =>
        $"[{color}]{Bar(percent, width, ascii)}[/]";

    public static string Spark(IReadOnlyList<double> values, int width, bool ascii)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var glyphs = ascii
            ? new[] { ' ', '.', ':', '-', '=', '+', '*', '#', '%', '@' }
            : new[] { ' ', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

        var clean = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (clean.Count == 0)
        {
            return new string(' ', width);
        }

        var window = clean.Skip(Math.Max(0, clean.Count - width)).ToList();
        var max = Math.Max(1e-9, window.Max());
        var chars = new char[width];
        var pad = width - window.Count;

        for (var i = 0; i < width; i++)
        {
            if (i < pad)
            {
                chars[i] = ' ';
                continue;
            }

            var value = window[i - pad];
            var index = (int)Math.Round(value / max * (glyphs.Length - 1));
            chars[i] = glyphs[Math.Clamp(index, 0, glyphs.Length - 1)];
        }

        return new string(chars);
    }

    public static string LevelColor(double value, Threshold threshold) =>
        value >= threshold.Crit ? "red" : value >= threshold.Warn ? "yellow" : "green";

    public static string TempMarkup(double? celsius, Threshold threshold, bool ascii)
    {
        if (celsius is null)
        {
            return "[grey]n/a[/]";
        }

        var color = LevelColor(celsius.Value, threshold);
        var unit = ascii ? "C" : "°C";
        return $"[{color}]{celsius.Value.ToString("0", CultureInfo.InvariantCulture)}{unit}[/]";
    }

    public static string Uptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}d {uptime.Hours:00}h {uptime.Minutes:00}m";
    }

    public static string Celsius(double? value, bool ascii)
    {
        if (value is null)
        {
            return "n/a";
        }

        return value.Value.ToString("0", CultureInfo.InvariantCulture) + (ascii ? "C" : "°C");
    }
}
