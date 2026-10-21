using System.Globalization;
using System.Text;
using Vitals.Sensors;

namespace Vitals.Export;

public static class CsvExporter
{
    public static void Write(IEnumerable<Snapshot> snapshots, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("timestamp,id,source,hardware,name,kind,value,unit");

        foreach (var snapshot in snapshots)
        {
            var timestamp = snapshot.Timestamp.ToString("O", CultureInfo.InvariantCulture);

            foreach (var reading in snapshot.Readings)
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
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
