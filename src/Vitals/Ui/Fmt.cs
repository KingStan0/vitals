using System.Globalization;
using System.Text;
using Spectre.Console;
using Vitals.Config;

namespace Vitals.Ui;

public static class Fmt
{
    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB", "PB" };
    private static readonly char[] AsciiSparkGlyphs = { ' ', '.', ':', '-', '=', '+', '*', '#', '%', '@' };
    private static readonly char[] UnicodeSparkGlyphs = { ' ', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    /// <summary>
    /// Replaces control characters so a sensor, process or machine name cannot
    /// inject newlines or terminal escapes into a dashboard line.
    /// </summary>
    public static string OneLine(string text)
    {
        var hasControl = false;
        foreach (var ch in text)
        {
            if (char.IsControl(ch))
            {
                hasControl = true;
                break;
            }
        }

        if (!hasControl)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(char.IsControl(ch) ? ' ' : ch);
        }

        return builder.ToString();
    }

    public static string Bytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes <= 0)
        {
            return "0 B";
        }

        var value = bytes;
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

    public static string Truncate(string text, int width, bool ascii = false)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        text = OneLine(text);

        // Measure display cells rather than UTF-16 code units so wide glyphs
        // (CJK, emoji) cannot overflow the panel and surrogate pairs are never
        // split. The suffix occupies one cell.
        if (DisplayWidth(text) <= width)
        {
            return text;
        }

        var budget = width - 1;
        var used = 0;
        var builder = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            var cells = RuneWidth(rune);
            if (used + cells > budget)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += cells;
        }

        return builder.Append(ascii ? "." : "…").ToString();
    }

    internal static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += RuneWidth(rune);
        }

        return width;
    }

    private static int RuneWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
        {
            return 0;
        }

        var value = rune.Value;
        if (value is < 32 or (>= 0x7f and < 0xa0))
        {
            return 0;
        }

        // Approximation of the common East Asian wide/fullwidth and emoji
        // ranges. Everything else is a single terminal cell.
        return IsWide(value) ? 2 : 1;
    }

    private static bool IsWide(int value) =>
        (value is >= 0x1100 and <= 0x115F) ||
        value is 0x2329 or 0x232A ||
        ((value is >= 0x2E80 and <= 0xA4CF) && value != 0x303F) ||
        (value is >= 0xAC00 and <= 0xD7A3) ||
        (value is >= 0xF900 and <= 0xFAFF) ||
        (value is >= 0xFE10 and <= 0xFE19) ||
        (value is >= 0xFE30 and <= 0xFE6F) ||
        (value is >= 0xFF00 and <= 0xFF60) ||
        (value is >= 0xFFE0 and <= 0xFFE6) ||
        (value is >= 0x1F300 and <= 0x1FAFF) ||
        (value is >= 0x20000 and <= 0x3FFFD);

    public static string Bar(double percent, int width, bool ascii)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var clamped = double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : 0;
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

        var glyphs = ascii ? AsciiSparkGlyphs : UnicodeSparkGlyphs;

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
        return $"[{color}]{celsius.Value.ToString("0", CultureInfo.InvariantCulture)}{TempUnit(ascii)}[/]";
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

        return value.Value.ToString("0", CultureInfo.InvariantCulture) + TempUnit(ascii);
    }

    public static string TempUnit(bool ascii) => ascii ? "C" : "°C";
}
