using Vitals.Sensors;

namespace Vitals.Tests;

public sealed class WinSourceTests
{
    [Theory]
    [InlineData("Hyper-V Virtual Ethernet Adapter", "vEthernet (Default Switch)", true)]
    [InlineData("TAP-Windows Adapter V9", "Ethernet 2", true)]
    [InlineData("WireGuard Tunnel", "wg0", true)]
    [InlineData("Intel(R) Ethernet Connection I219-V", "Ethernet", false)]
    [InlineData("Realtek 8822CE Wireless LAN 802.11ac PCI-E NIC", "Wi-Fi", false)]
    public void Virtual_adapters_are_excluded_from_network_totals(
        string description,
        string name,
        bool expected)
    {
        Assert.Equal(expected, WinSource.IsVirtualAdapter(description, name));
    }

    [Fact]
    public void Volume_diff_adds_and_removes_counters_case_insensitively()
    {
        var (added, removed) = WinSource.DiffVolumes(
            new[] { "C:", "D:", "E:" },
            new[] { "c:", "E:", "F:" });

        Assert.Equal(new[] { "F:" }, added);
        Assert.Equal(new[] { "D:" }, removed);
    }
}
