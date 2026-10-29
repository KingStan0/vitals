using System.ComponentModel;
using System.Diagnostics;

namespace Vitals.Setup;

internal static class Elevation
{
    /// <summary>
    /// Relaunches the current executable through UAC and waits for it to exit.
    /// Used for <c>vitals setup</c> when the current console is not elevated.
    /// </summary>
    public static int RelaunchElevated(string[] args)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("vitals: could not determine the executable path for elevation.");
            return 1;
        }

        var start = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas",
        };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                Console.Error.WriteLine("vitals: could not start the elevated process.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Console.Error.WriteLine("vitals: elevation was cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vitals: could not elevate: {ex.Message}");
            return 1;
        }
    }
}
