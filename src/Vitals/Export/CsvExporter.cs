using System.Globalization;
using System.Text;
using Vitals.Sampling;
using Vitals.Sensors;

namespace Vitals.Export;

public static class CsvExporter
{
    private static readonly char[] QuotingChars = { ',', '"', '\n', '\r' };

    public static void Write(IEnumerable<Snapshot> snapshots, string path)
    {
        using var writer = CreateWriter(path);
        WriteHeader(writer);

        foreach (var snapshot in snapshots)
        {
            WriteSnapshot(writer, snapshot);
        }
    }

    /// <summary>Writes the compact frames retained by the sampler.</summary>
    public static void WriteFrames(IEnumerable<SessionFrame> frames, string path)
    {
        using var writer = CreateWriter(path);
        WriteHeader(writer);

        foreach (var frame in frames)
        {
            WriteReadings(writer, frame.Timestamp, frame.Readings);
        }
    }

    public static StreamWriter CreateWriter(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // The BOM lets Excel detect UTF-8 instead of falling back to the local
        // ANSI code page. Other CSV consumers ignore it.
        return new StreamWriter(path, false, new UTF8Encoding(true));
    }

    public static void WriteHeader(TextWriter writer) =>
        writer.WriteLine("timestamp,id,source,hardware,name,kind,value,unit");

    public static void WriteSnapshot(TextWriter writer, Snapshot snapshot) =>
        WriteReadings(writer, snapshot.Timestamp, snapshot.Readings);

    private static void WriteReadings(
        TextWriter writer,
        DateTimeOffset timestampValue,
        IReadOnlyList<Reading> readings)
    {
        var timestamp = timestampValue.ToString("O", CultureInfo.InvariantCulture);

        foreach (var reading in readings)
        {
            writer.Write(timestamp);
            writer.Write(',');
            writer.Write(Escape(reading.Id));
            writer.Write(',');
            writer.Write(Escape(reading.Source));
            writer.Write(',');
            writer.Write(Escape(reading.Hardware));
            writer.Write(',');
            writer.Write(Escape(reading.Name));
            writer.Write(',');
            writer.Write(reading.Kind);
            writer.Write(',');
            writer.Write(reading.Value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty);
            writer.Write(',');
            writer.Write(Escape(reading.Unit));
            writer.WriteLine();
        }
    }

    private static string Escape(string value)
    {
        // Spreadsheet programs may evaluate cells beginning with a formula
        // character. Prefix those text fields with an apostrophe so exported
        // sensor names cannot become formulas.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        if (value.IndexOfAny(QuotingChars) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
