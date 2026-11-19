using Spectre.Console;

namespace Vitals.Ui;

/// <summary>
/// The single dashboard palette: Catppuccin Mocha.
/// https://github.com/catppuccin/catppuccin#-palette
/// </summary>
public static class Theme
{
    /// <summary>Mauve, used for values and emphasis.</summary>
    public const string Accent = "#cba6f7";

    /// <summary>Text, used for primary labels such as process names.</summary>
    public const string Text = "#cdd6f4";

    /// <summary>Subtext 0, used for secondary text.</summary>
    public const string Muted = "#a6adc8";

    /// <summary>Overlay 1, used for dim labels and units.</summary>
    public const string Overlay = "#7f849c";

    public const string Green = "#a6e3a1";

    public const string Yellow = "#f9e2af";

    public const string Red = "#f38ba8";

    /// <summary>Overlay 0, used for panel borders.</summary>
    public static readonly Color BorderColor = new(0x6c, 0x70, 0x86);
}
