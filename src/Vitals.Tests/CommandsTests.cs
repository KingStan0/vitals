using Vitals.Cli;
using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class CommandsTests
{
    [Fact]
    public void Sensor_dump_sanitizes_control_characters()
    {
        var fixture = SnapshotFixture.Create();
        var snapshot = fixture with
        {
            Readings = new[]
            {
                new Reading(
                    "id\n1",
                    "lhm",
                    "HW\u001b[31m",
                    "Name\nline",
                    SensorKind.Load,
                    12.3,
                    "%\t"),
            },
            Status = fixture.Status with
            {
                LhmError = "error\nescape",
                Notes = new[] { "note\rline" },
            },
        };
        var output = new StringWriter();

        var exitCode = Commands.PrintSensorDump(snapshot, ascii: false, output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
        Assert.Contains("HW [31m", text, StringComparison.Ordinal);
        Assert.Contains("Name line", text, StringComparison.Ordinal);
        Assert.Contains("error escape", text, StringComparison.Ordinal);
        Assert.Contains("note line", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sensor_dump_reports_when_no_readings_exist()
    {
        var fixture = SnapshotFixture.Create();
        var snapshot = fixture with { Readings = Array.Empty<Reading>() };
        var output = new StringWriter();

        var exitCode = Commands.PrintSensorDump(snapshot, ascii: true, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("No sensors were reported", output.ToString());
    }
}
