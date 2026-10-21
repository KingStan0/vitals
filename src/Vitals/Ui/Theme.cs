using Spectre.Console;

namespace Vitals.Ui;

public sealed class Theme
{
    private Theme(string name, string accent, string muted)
    {
        Name = name;
        Accent = accent;
        Muted = muted;
    }

    public string Name { get; }

    /// <summary>Markup colour name used for emphasis.</summary>
    public string Accent { get; }

    /// <summary>Markup colour name used for secondary text.</summary>
    public string Muted { get; }

    public static readonly Theme Default = new("default", "cyan1", "grey");

    public static readonly Theme[] All =
    {
        Default,
        new("amber", "orange1", "grey"),
        new("mono", "white", "grey"),
    };

    public static Theme ByName(string? name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Default;

    public Theme Next() => All[(Array.IndexOf(All, this) + 1) % All.Length];
}
