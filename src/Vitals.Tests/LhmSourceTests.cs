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
    [InlineData(SensorType.Temperature, "°C")]
    [InlineData(SensorType.Load, "%")]
    [InlineData(SensorType.Clock, "MHz")]
    [InlineData(SensorType.Power, "W")]
    [InlineData(SensorType.Voltage, "V")]
    public void Sensor_units_are_mapped(SensorType type, string expected)
    {
        Assert.Equal(expected, LhmSource.UnitFor(type));
    }
}
