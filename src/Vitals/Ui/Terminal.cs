using System.Runtime.InteropServices;

namespace Vitals.Ui;

internal static class Terminal
{
    private const int StandardOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    /// <summary>Turns on ANSI escape handling for classic conhost windows.</summary>
    public static void EnableVirtualTerminal()
    {
        try
        {
            var handle = GetStdHandle(StandardOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return;
            }

            if (GetConsoleMode(handle, out var mode))
            {
                SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
            }
        }
        catch
        {
            // Best effort; Windows Terminal is already fine.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
