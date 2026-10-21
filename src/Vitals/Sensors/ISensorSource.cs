namespace Vitals.Sensors;

/// <summary>
/// A pluggable source of hardware data. Sources fill whichever parts of the
/// snapshot they can and leave the rest alone; later sources win.
/// </summary>
public interface ISensorSource : IDisposable
{
    string Name { get; }

    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    void Initialize();

    void Sample(SnapshotBuilder builder);
}
