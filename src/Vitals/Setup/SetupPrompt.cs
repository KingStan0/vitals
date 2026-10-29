using Vitals.Cli;
using Vitals.Config;

namespace Vitals.Setup;

internal static class SetupPrompt
{
    /// <summary>
    /// The first-launch prompt is only shown for an interactive, elevated
    /// dashboard. Headless commands, driver-free mode and non-elevated runs
    /// never install anything implicitly.
    /// </summary>
    public static bool ShouldPrompt(AppConfig config, CliOptions cli, bool installed, bool elevated) =>
        !installed &&
        elevated &&
        !cli.RenderOnce &&
        !cli.NoDriver &&
        config.PromptForPawnIo;

    public static bool Confirm(TextWriter output, TextReader input)
    {
        output.WriteLine();
        output.WriteLine("PawnIO is not installed.");
        output.WriteLine("It enables CPU package temperatures, motherboard sensors and package power.");
        output.WriteLine($"vitals can download the official installer (v{PawnIoSetup.Version}, about 3.4 MB)");
        output.WriteLine("from GitHub, verify its SHA-256, and install it with administrator rights.");
        output.Write("Install PawnIO now? [y/N] ");

        var answer = input.ReadLine();
        if (answer is null)
        {
            return false;
        }

        answer = answer.Trim();
        return answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
