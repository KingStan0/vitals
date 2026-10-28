using LibreHardwareMonitor.Hardware;
using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class LhmSourceTests
{
    [Theory]
    [InlineData("Temperature", 44.0, true)]
    [InlineData("Temperature", 0.0, false)]
    [InlineData("Warning Temperature", 80.0, false)]
    [InlineData("Critical Temperature", 90.0, false)]
    [InlineData("Max Temperature", 90.0, false)]
    public void Storage_temperature_filter_skips_limits_and_zero(string name, double value, bool expected)
    {
        Assert.Equal(expected, LhmSource.IsUsableStorageTemperature(name, value));
    }

    [Fact]
    public void Storage_temperature_filter_rejects_missing_values()
    {
        Assert.False(LhmSource.IsUsableStorageTemperature("Temperature", null));
    }

    [Theory]
    [InlineData("CPU", 60.0, true)]
    [InlineData("Warning Temperature", 80.0, false)]
    [InlineData("Critical Temperature", 90.0, false)]
    [InlineData("System", 0.0, false)]
    public void Board_temperature_filter_skips_limits_and_zero(string name, double value, bool expected)
    {
        Assert.Equal(expected, LhmSource.IsUsableBoardTemperature(name, value));
    }

    [Theory]
    [InlineData("CPU Core #1", 1)]
    [InlineData("CPU Core #12", 12)]
    [InlineData("Core #3", 3)]
    [InlineData("Core #0", 0)]
    [InlineData("Effective core", 0)]
    public void Core_index_is_parsed_from_the_sensor_name(string name, int expected)
    {
        Assert.Equal(expected, LhmSource.ParseCoreIndex(name));
    }

    [Theory]
    [InlineData("Core #1", true)]
    [InlineData("CPU Core #1", true)]
    [InlineData("Core #1 (Effective)", false)]
    [InlineData("GPU Core", false)]
    [InlineData("Bus Speed", false)]
    public void Core_clock_names_accept_amd_and_intel_variants(string name, bool expected)
    {
        Assert.Equal(expected, LhmSource.IsCoreClockName(name));
    }

    [Theory]
    [InlineData(SensorType.Temperature, "°C")]
    [InlineData(SensorType.Load, "%")]
    [InlineData(SensorType.Clock, "MHz")]
    [InlineData(SensorType.Power, "W")]
    [InlineData(SensorType.Voltage, "V")]
    public void Sensor_units_are_mapped(SensorType type, string expected)
    {
        Assert.Equal(expected, LhmSource.UnitFor(type));
    }

    [Fact]
    public void Preferred_sensor_name_wins_over_enumeration_order()
    {
        var names = new[] { "GPU Memory Junction", "GPU Hot Spot" };
        var preferred = new[] { "GPU Hot Spot", "Hot Spot", "Hotspot", "Junction" };

        Assert.Equal(1, LhmSource.PickPreferredIndex(names, preferred));
    }

    [Fact]
    public void Preferred_sensor_name_returns_minus_one_when_nothing_matches()
    {
        var names = new[] { "GPU Core", "GPU Memory Junction" };

        Assert.Equal(-1, LhmSource.PickPreferredIndex(names, new[] { "GPU Hot Spot" }));
    }

    [Theory]
    [InlineData("GPU Hot Spot", true)]
    [InlineData("Hot Spot", true)]
    [InlineData("GPU Memory Junction", false)]
    [InlineData("Memory Junction", false)]
    public void Hotspot_filter_excludes_memory_junction_sensors(string name, bool expected)
    {
        Assert.Equal(expected, LhmSource.IsHotspotTemperatureName(name));
    }

    [Fact]
    public void Core_offsets_advance_by_the_maximum_socket_index()
    {
        var offset = 0;
        var cores = new List<int>();

        foreach (var socket in new[] { new[] { 1, 2, 4 }, new[] { 1 } })
        {
            foreach (var index in socket)
            {
                cores.Add(offset + index - 1);
            }

            offset = LhmSource.AdvanceCoreOffset(offset, socket.Max());
        }

        Assert.Equal(new[] { 0, 1, 3, 4 }, cores);
    }

    [Fact]
    public void PawnIo_is_detected_from_a_registry_key()
    {
        var checkedKeys = new List<string>();

        var installed = LhmSource.IsPawnIoInstalled(
            key =>
            {
                checkedKeys.Add(key);
                return key.Contains("Services", StringComparison.Ordinal);
            },
            _ => false);

        Assert.True(installed);
        Assert.Contains(checkedKeys, key => key.EndsWith("PawnIO", StringComparison.Ordinal));
    }

    [Fact]
    public void PawnIo_is_detected_from_the_driver_file_without_a_registry_key()
    {
        var checkedFiles = new List<string>();

        var installed = LhmSource.IsPawnIoInstalled(
            _ => false,
            path =>
            {
                checkedFiles.Add(path);
                return true;
            });

        Assert.True(installed);
        Assert.Contains(checkedFiles, path =>
            path.EndsWith(Path.Combine("drivers", "PawnIO.sys"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PawnIo_is_reported_missing_when_neither_registry_nor_driver_exists()
    {
        Assert.False(LhmSource.IsPawnIoInstalled(_ => false, _ => false));
    }
}
