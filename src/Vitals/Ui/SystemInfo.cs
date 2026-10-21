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
            var build = key?.GetValue("CurrentBuild") as string;

            if (!string.IsNullOrWhiteSpace(product))
            {
                return $"{product.Replace("Microsoft ", string.Empty)} {display} ({build})".Trim();
            }
        }
        catch
        {
            // Ignore.
        }

        return RuntimeInformation.OSDescription;
    }

    public static string Dot(bool ascii) => ascii ? "|" : "·";
}
