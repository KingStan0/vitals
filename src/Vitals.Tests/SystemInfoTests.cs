using Vitals.Ui;

namespace Vitals.Tests;

public sealed class SystemInfoTests
{
    [Fact]
    public void Windows_11_build_is_not_reported_as_windows_10()
    {
        var name = SystemInfo.FormatOsName("Windows 10 Pro", "26H2", "26300");

        Assert.Equal("Windows 11 Pro 26H2 (26300)", name);
    }

    [Fact]
    public void Windows_10_build_keeps_its_name()
    {
        var name = SystemInfo.FormatOsName("Windows 10 Pro", "22H2", "19045");

        Assert.Equal("Windows 10 Pro 22H2 (19045)", name);
    }

    [Fact]
    public void Microsoft_prefix_is_removed()
    {
        var name = SystemInfo.FormatOsName("Microsoft Windows 11 Enterprise", "24H2", "26100");

        Assert.Equal("Windows 11 Enterprise 24H2 (26100)", name);
    }
}
