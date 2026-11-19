using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Memory_percentages_are_clamped_to_a_sane_range()
    {
        var full = new MemorySample(100, 0, 100, 100, 0);
        var overCommitted = new MemorySample(100, 0, 250, 200, 0);
        var overAvailable = new MemorySample(100, 250, 10, 100, 0);
        var noLimit = new MemorySample(0, 0, 10, 0, 0);

        Assert.Equal(100, full.UsedBytes);
        Assert.Equal(100, full.UsedPercent);
        Assert.Equal(100, full.CommitPercent);

        Assert.Equal(100, overCommitted.UsedPercent);
        Assert.Equal(100, overCommitted.CommitPercent);

        Assert.Equal(0, overAvailable.UsedBytes);
        Assert.Equal(0, overAvailable.UsedPercent);

        Assert.Equal(0, noLimit.UsedPercent);
        Assert.Equal(0, noLimit.CommitPercent);
    }

    [Fact]
    public void Volume_percentage_is_clamped_to_a_sane_range()
    {
        Assert.Equal(0, new VolumeSample("C:", "", 0, 0).UsedPercent);
        Assert.Equal(0, new VolumeSample("C:", "", 100, 150).UsedPercent);
        Assert.Equal(100, new VolumeSample("C:", "", 100, -10).UsedPercent);
        Assert.Equal(25, new VolumeSample("C:", "", 100, 75).UsedPercent);
    }
}
