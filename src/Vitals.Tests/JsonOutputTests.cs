using System.Text.Json;
using Vitals.Cli;
using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class JsonOutputTests
{
    [Fact]
    public void Snapshot_json_has_documented_camel_case_shape()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            Readings = new[]
            {
                new Reading("/amdcpu/0/load/0", "lhm", "AMD Ryzen 5 5600G", "CPU Total", SensorKind.Load, 21.3, "%"),
            },
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOutput.Pretty);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(42, root.GetProperty("cpuLoadPercent").GetDouble());
        Assert.Equal("RX 5700", root.GetProperty("gpu").GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("gpus").GetArrayLength());
        Assert.Equal("RX 5700", root.GetProperty("gpus")[0].GetProperty("name").GetString());
        Assert.Equal(AppInfo.Version, root.GetProperty("status").GetProperty("appVersion").GetString());
        Assert.Equal("Load", root.GetProperty("readings")[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("processes").ValueKind);
    }

    [Fact]
    public void Dashboard_only_process_list_is_not_serialized()
    {
        var snapshot = SnapshotFixture.Create() with
        {
            AllProcesses = new[] { new ProcessSample(1, "hidden", 1, 1) },
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOutput.Compact);

        Assert.DoesNotContain("allProcesses", json);
        Assert.DoesNotContain("hidden", json);
    }

    [Fact]
    public void Compact_json_is_one_line()
    {
        var json = JsonSerializer.Serialize(SnapshotFixture.Create(), JsonOutput.Compact);

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain("\r", json);
        Assert.StartsWith("{\"timestamp\":", json);
    }
}
