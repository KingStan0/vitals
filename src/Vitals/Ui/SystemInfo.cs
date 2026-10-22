using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Vitals.Ui;

public static class SystemInfo
{
    public static string CpuName { get; } = ReadProcessorName();

    public static string Board { get; } = ReadBoardName();

    public static string OsName { get; } = ReadOsName();

    private static string ReadProcessorName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Replace("(R)", string.Empty).Replace("(TM)", string.Empty).Replace("  ", " ").Trim();
            }
        }
        catch
        {
            // Fall through to a generic label.
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    private static string ReadBoardName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var board = key?.GetValue("BaseBoardProduct") as string;
            var vendor = key?.GetValue("BaseBoardManufacturer") as string;

            if (!string.IsNullOrWhiteSpace(board))
            {
                return string.IsNullOrWhiteSpace(vendor) ? board : $"{vendor} {board}".Trim();
            }
        }
        catch
        {
            // Ignore.
        }

        return "unknown board";
    }

    private static string ReadOsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string;
            var display = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuildNumber") as string
                        ?? key?.GetValue("CurrentBuild") as string;

            return FormatOsName(product, display, build);
        }
        catch
        {
            // Ignore.
        }

        return RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// Windows 11 still reports ProductName as "Windows 10 ..." for
    /// compatibility. The build number is the reliable discriminator.
    /// </summary>
    public static string FormatOsName(string? product, string? display, string? build)
    {
        product = product?.Replace("Microsoft ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(product))
        {
            return RuntimeInformation.OSDescription;
        }

        if (int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out var buildNumber) &&
            buildNumber >= 22000 &&
            product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            product = "Windows 11" + product["Windows 10".Length..];
        }

        var parts = new List<string> { product };
        if (!string.IsNullOrWhiteSpace(display))
        {
            parts.Add(display);
        }

        if (!string.IsNullOrWhiteSpace(build))
        {
            parts.Add($"({build})");
        }

        return string.Join(" ", parts);
    }

    public static string Dot(bool ascii) => ascii ? "|" : "·";
}
