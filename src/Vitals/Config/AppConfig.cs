using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitals.Config;

public sealed class Threshold
{
    public Threshold()
    {
    }

    public Threshold(double warn, double crit)
    {
        Warn = warn;
        Crit = crit;
    }

    public double Warn { get; set; }

    public double Crit { get; set; }

    internal void Normalize()
    {
        Warn = Math.Clamp(Warn, 0, 200);
        Crit = Math.Clamp(Crit, 0, 200);

        if (Crit <= Warn)
        {
            Crit = Warn >= 200 ? 200 : Math.Min(Warn + 10, 200);
        }
    }
}

public sealed class Thresholds
{
    public Threshold CpuTemp { get; set; } = new(85, 95);

    public Threshold GpuEdge { get; set; } = new(80, 90);

    public Threshold GpuHotspot { get; set; } = new(95, 105);

    public Threshold GpuMemory { get; set; } = new(90, 100);

    public Threshold Memory { get; set; } = new(85, 95);
}

public sealed class AppConfig
{
    public int IntervalMs { get; set; } = 1000;

    public int ProcessIntervalMs { get; set; } = 2000;

    public int ProcessCount { get; set; } = 8;

    public int HistorySeconds { get; set; } = 300;

    public bool Ascii { get; set; }

    public bool PromptForPawnIo { get; set; } = true;

    public string Theme { get; set; } = "default";

    public Thresholds Thresholds { get; set; } = new();

    [JsonIgnore]
    public string? LoadedFrom { get; set; }

    [JsonIgnore]
    public bool NoDriver { get; set; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vitals");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "config.json");

    public static AppConfig Load(string? explicitPath, out string? warning)
    {
        warning = null;
        var path = explicitPath ?? DefaultPath;

        if (!File.Exists(path))
        {
            if (explicitPath is not null)
            {
                warning = $"config not found: {explicitPath}";
            }

            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            config.LoadedFrom = path;
            config.Normalize();
            return config;
        }
        catch (Exception ex)
        {
            warning = $"could not read {path}: {ex.Message}";
            return new AppConfig();
        }
    }

    public void Normalize()
    {
        IntervalMs = Math.Clamp(IntervalMs, 250, 60_000);
        ProcessIntervalMs = Math.Clamp(ProcessIntervalMs, 250, 60_000);
        ProcessCount = Math.Clamp(ProcessCount, 3, 64);
        HistorySeconds = Math.Clamp(HistorySeconds, 30, 86_400);

        Thresholds ??= new Thresholds();
        Thresholds.CpuTemp ??= new Threshold(85, 95);
        Thresholds.GpuEdge ??= new Threshold(80, 90);
        Thresholds.GpuHotspot ??= new Threshold(95, 105);
        Thresholds.GpuMemory ??= new Threshold(90, 100);
        Thresholds.Memory ??= new Threshold(85, 95);

        Thresholds.CpuTemp.Normalize();
        Thresholds.GpuEdge.Normalize();
        Thresholds.GpuHotspot.Normalize();
        Thresholds.GpuMemory.Normalize();
        Thresholds.Memory.Normalize();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
