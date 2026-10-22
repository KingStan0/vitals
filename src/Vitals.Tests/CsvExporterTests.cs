using Vitals.Export;
using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class CsvExporterTests
{
    [Fact]
    public void Escapes_commas_quotes_and_newlines_and_writes_null_values_as_empty()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            Readings = new[]
            {
                new Reading("id,1", "lhm", "HW \"x\"", "Name\nline", SensorKind.Load, 12.3456, "%"),
                new Reading("id2", "win", "CPU", "Total", SensorKind.Temperature, null, "°C"),
            },
        };

        var directory = Path.Combine(Path.GetTempPath(), $"vitals-csv-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "export.csv");

        try
        {
            CsvExporter.Write(new[] { snapshot }, path);
            var text = File.ReadAllText(path);

            Assert.StartsWith("timestamp,id,source,hardware,name,kind,value,unit", text);
            Assert.Contains("\"id,1\",lhm,\"HW \"\"x\"\"\",\"Name\nline\",Load,12.3456,%", text);
            Assert.Contains("id2,win,CPU,Total,Temperature,,°C", text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
