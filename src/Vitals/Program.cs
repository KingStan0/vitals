using Vitals.Cli;
using Vitals.Config;
using Vitals.Ui;

namespace Vitals;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // Redirected output does not support changing the encoding.
        }

        var options = CliOptions.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(CliOptions.HelpText);
            return 0;
        }

        if (options.Error is { } error)
        {
            Console.Error.WriteLine($"vitals: {error}");
            Console.Error.WriteLine("run 'vitals --help'.");
            return 2;
        }

        var config = AppConfig.Load(options.ConfigPath, out var warning);
        if (warning is not null)
        {
            Console.Error.WriteLine($"vitals: {warning}");
        }

        options.ApplyTo(config);

        try
        {
            return options.Command == "tui"
                ? TuiApp.Run(options, config)
                : Commands.Run(options, config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vitals: {ex.Message}");
            if (options.Debug)
            {
                Console.Error.WriteLine(ex);
            }

            return 1;
        }
    }
}
