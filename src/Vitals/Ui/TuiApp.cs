using Spectre.Console;
using Vitals.Cli;
using Vitals.Config;
using Vitals.Export;
using Vitals.Sampling;
using Vitals.Sensors;

namespace Vitals.Ui;

public static class TuiApp
{
    public static int Run(CliOptions cli, AppConfig config)
    {
        if (cli.RenderOnce)
        {
            return RenderOnce(cli, config);
        }

        if (Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("vitals: the dashboard needs an interactive terminal.");
            Console.Error.WriteLine("        use 'vitals snapshot --json' or 'vitals watch --json' instead.");
            return 1;
        }

        Terminal.EnableVirtualTerminal();
        AnsiConsole.Profile.Capabilities.Ansi = true;

        var ascii = cli.Ascii || config.Ascii;
        if (ascii)
        {
            AnsiConsole.Profile.Capabilities.Unicode = false;
        }

        if (cli.NoColor)
        {
            AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        }

        using var engine = new SampleEngine(config);
        engine.WarmUp();

        using var sampler = new Sampler(engine, config);
        sampler.Start();

        var deadline = Environment.TickCount64 + 5000;
        while (sampler.Latest is null && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(50);
        }

        if (sampler.Latest is null)
        {
            Console.Error.WriteLine("vitals: could not collect a sample.");
            return 1;
        }

        var options = new RenderOptions
        {
            Config = config,
            Theme = Theme.ByName(config.Theme),
            Ascii = ascii,
            Width = AnsiConsole.Profile.Width,
            Height = AnsiConsole.Profile.Height,
        };

        var quit = false;
        var interactive = true;
        var toast = (string?)null;
        var toastUntil = 0L;

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            quit = true;
        };

        void SetToast(string message)
        {
            toast = message;
            toastUntil = Environment.TickCount64 + 3000;
        }

        void ChangeInterval(int delta)
        {
            var next = Math.Clamp(sampler.IntervalMs + delta, 250, 5000);
            sampler.IntervalMs = next;
            config.IntervalMs = next;
            SetToast($"interval {next} ms");
        }

        void HandleKey(ConsoleKey key)
        {
            switch (key)
            {
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    quit = true;
                    break;

                case ConsoleKey.Spacebar:
                    options.Paused = !options.Paused;
                    sampler.Paused = options.Paused;
                    SetToast(options.Paused ? "paused" : "resumed");
                    break;

                case ConsoleKey.R:
                    sampler.History.ResetPeaks();
                    SetToast("peaks reset");
                    break;

                case ConsoleKey.S:
                    options.Sort = options.Sort switch
                    {
                        ProcessSort.Cpu => ProcessSort.Memory,
                        ProcessSort.Memory => ProcessSort.Name,
                        _ => ProcessSort.Cpu,
                    };
                    SetToast($"sort: {options.Sort}");
                    break;

                case ConsoleKey.T:
                    options.Theme = options.Theme.Next();
                    config.Theme = options.Theme.Name;
                    SetToast($"theme: {options.Theme.Name}");
                    break;

                case ConsoleKey.E:
                    var path = Path.Combine(
                        Environment.CurrentDirectory,
                        $"vitals-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                    try
                    {
                        CsvExporter.Write(sampler.SessionSnapshot(), path);
                        SetToast($"exported {Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        SetToast($"export failed: {ex.Message}");
                    }

                    break;

                case ConsoleKey.Oem4:
                case ConsoleKey.Subtract:
                    ChangeInterval(-250);
                    break;

                case ConsoleKey.Oem6:
                case ConsoleKey.Add:
                    ChangeInterval(250);
                    break;
            }
        }

        var layout = Widgets.CreateLayout(options);

        AnsiConsole.AlternateScreen(() =>
        {
            Console.Write("\u001b[?25l");

            try
            {
                AnsiConsole.Live(layout)
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Crop)
                    .Start(context =>
                    {
                        Snapshot? rendered = null;

                        while (!quit)
                        {
                            if (interactive)
                            {
                                try
                                {
                                    while (Console.KeyAvailable)
                                    {
                                        HandleKey(Console.ReadKey(true).Key);
                                    }
                                }
                                catch (InvalidOperationException)
                                {
                                    interactive = false;
                                }
                            }

                            if (quit)
                            {
                                break;
                            }

                            if (toast is not null && Environment.TickCount64 > toastUntil)
                            {
                                toast = null;
                            }

                            options.Toast = toast;

                            var width = AnsiConsole.Profile.Width;
                            var height = AnsiConsole.Profile.Height;

                            if (width != options.Width || height != options.Height)
                            {
                                options.Width = width;
                                options.Height = height;
                                layout = Widgets.CreateLayout(options);
                                context.UpdateTarget(layout);
                                rendered = null;
                            }

                            var snapshot = sampler.Latest;
                            if (!options.Paused && snapshot is not null && !ReferenceEquals(snapshot, rendered))
                            {
                                Widgets.Update(layout, snapshot, sampler.History, options);
                                context.Refresh();
                                rendered = snapshot;
                            }

                            Thread.Sleep(40);
                        }
                    });
            }
            finally
            {
                Console.Write("\u001b[?25h");
            }
        });

        return 0;
    }

    /// <summary>
    /// Renders a single static frame as plain text. Useful for screenshots,
    /// CI smoke tests and bug reports where a live terminal is not available.
    /// </summary>
    private static int RenderOnce(CliOptions cli, AppConfig config)
    {
        using var engine = new SampleEngine(config);
        engine.WarmUp();
        var snapshot = engine.Sample();

        var ascii = cli.Ascii || config.Ascii;
        var options = new RenderOptions
        {
            Config = config,
            Theme = Theme.ByName(config.Theme),
            Ascii = ascii,
            Width = 120,
            Height = 42,
        };

        var history = new History(300);
        history.Add(snapshot);

        var layout = Widgets.CreateLayout(options);
        Widgets.Update(layout, snapshot, history, options);

        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(Console.Out),
        });

        console.Profile.Width = options.Width;
        console.Profile.Height = options.Height;
        console.Write(layout);
        return 0;
    }
}
