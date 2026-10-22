using Vitals.Config;
using Vitals.Sensors;

namespace Vitals.Sampling;

/// <summary>
/// Owns the sensor sources and turns one call into one snapshot. No threading
/// here, so the headless commands and the TUI share exactly the same data path.
/// </summary>
public sealed class SampleEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly WinSource _win = new();
    private readonly LhmSource _lhm = new();
    private readonly ProcessSampler _processes = new();
    private List<ProcessSample> _lastProcesses = new();
    private long _lastProcessTick;

    public SampleEngine(AppConfig config)
    {
        _config = config;

        _win.Initialize();

        if (!config.NoDriver)
        {
            _lhm.Initialize();
        }

        var notes = new List<string>();
        if (config.NoDriver)
        {
            notes.Add("hardware sensors disabled by --no-driver");
        }
        else if (!_lhm.IsAvailable)
        {
            notes.Add($"hardware sensors unavailable: {_lhm.UnavailableReason ?? "unknown"}");
        }
        else if (!_lhm.PawnIoInstalled)
        {
            notes.Add("PawnIO missing: install PawnIO for CPU and motherboard sensors");
        }
        else if (!_lhm.Elevated)
        {
            notes.Add("not elevated: run vitals.cmd for full sensor access");
        }

        // Even in --no-driver mode, report facts about the process environment:
        // status.elevated and status.pawnIoInstalled describe the machine, while
        // lhmAvailable describes whether the sensor backend was enabled.
        var elevated = config.NoDriver ? LhmSource.IsProcessElevated() : _lhm.Elevated;
        var pawnIo = config.NoDriver ? LhmSource.IsPawnIoInstalled() : _lhm.PawnIoInstalled;

        Status = new SensorStatus(
            elevated,
            _lhm.IsAvailable && !config.NoDriver,
            config.NoDriver ? "disabled by --no-driver" : _lhm.UnavailableReason,
            pawnIo,
            notes,
            AppInfo.Version);
    }

    public SensorStatus Status { get; }

    public Snapshot Sample()
    {
        var builder = new SnapshotBuilder();
        _win.Sample(builder);
        _lhm.Sample(builder);

        var now = Environment.TickCount64;
        if (now - _lastProcessTick >= _config.ProcessIntervalMs)
        {
            RefreshProcesses();
        }

        builder.Processes.AddRange(_lastProcesses);

        if (_lhm.IsAvailable && !_lhm.Elevated)
        {
            builder.Note("not elevated: temperatures may be missing");
        }

        if (_config.NoDriver)
        {
            builder.Note("driver-free mode");
        }

        return builder.Build(Status);
    }

    /// <summary>Two quick samples so rate counters have a baseline.</summary>
    public void WarmUp()
    {
        Sample();
        Thread.Sleep(250);

        // The first process pass is only a baseline (all zeros). Refresh it so
        // one-shot commands such as `snapshot --json` report real percentages.
        RefreshProcesses();
        Sample();
    }

    private void RefreshProcesses()
    {
        _lastProcesses = _processes.Sample(_config.ProcessCount);
        _lastProcessTick = Environment.TickCount64;
    }

    public void Dispose()
    {
        _win.Dispose();
        _lhm.Dispose();
        _processes.Dispose();
    }
}
