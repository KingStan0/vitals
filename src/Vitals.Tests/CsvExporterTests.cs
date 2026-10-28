using Vitals.Export;
using Vitals.Sampling;
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

    [Fact]
    public void Writes_a_utf8_bom_and_neutralises_formula_like_text_fields()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            Readings = new[]
            {
                new Reading("-id", "+src", "@HW", "=SUM(A1)", SensorKind.Load, 1, "%"),
            },
        };

        var directory = Path.Combine(Path.GetTempPath(), $"vitals-csv-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "export.csv");

        try
        {
            CsvExporter.Write(new[] { snapshot }, path);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));

            var text = File.ReadAllText(path);
            Assert.Contains("'-id,'+src,'@HW,'=SUM(A1),Load,1,%", text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Frame_writer_produces_the_same_output_as_snapshot_writer()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            Readings = new[]
            {
                new Reading("id", "win", "CPU", "Total", SensorKind.Load, 12.3, "%"),
            },
        };

        var directory = Path.Combine(Path.GetTempPath(), $"vitals-csv-{Guid.NewGuid():N}");
        var bulkPath = Path.Combine(directory, "bulk.csv");
        var framePath = Path.Combine(directory, "frame.csv");

        try
        {
            CsvExporter.Write(new[] { snapshot }, bulkPath);
            CsvExporter.WriteFrames(
                new[] { new SessionFrame(snapshot.Timestamp, snapshot.Readings) },
                framePath);

            Assert.Equal(File.ReadAllText(bulkPath), File.ReadAllText(framePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Streaming_writer_produces_the_same_output()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            Readings = new[]
            {
                new Reading("id", "win", "CPU", "Total", SensorKind.Load, 12.3, "%"),
            },
        };

        var directory = Path.Combine(Path.GetTempPath(), $"vitals-csv-{Guid.NewGuid():N}");
        var bulkPath = Path.Combine(directory, "bulk.csv");
        var streamedPath = Path.Combine(directory, "streamed.csv");

        try
        {
            CsvExporter.Write(new[] { snapshot }, bulkPath);
            using (var writer = CsvExporter.CreateWriter(streamedPath))
            {
                CsvExporter.WriteHeader(writer);
                CsvExporter.WriteSnapshot(writer, snapshot);
            }

            Assert.Equal(File.ReadAllText(bulkPath), File.ReadAllText(streamedPath));
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
