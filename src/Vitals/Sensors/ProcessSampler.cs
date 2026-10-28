using System.Diagnostics;

namespace Vitals.Sensors;

/// <summary>
/// Per-process CPU and working set. CPU is normalised across all logical cores.
/// The first sample after start always reports 0% because there is no baseline.
/// </summary>
public sealed class ProcessSampler
{
    private const int StartTimeProbeMs = 30_000;

    private readonly Dictionary<int, Baseline> _previous = new();

    public List<ProcessSample> Sample()
    {
        // A monotonic clock keeps CPU percentages correct across wall-clock
        // adjustments (NTP, manual clock changes) and is cheaper than DateTime.
        var now = Environment.TickCount64;
        var result = new List<ProcessSample>(256);
        var alive = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                using (process)
                {
                    if (process.Id <= 4)
                    {
                        continue;
                    }

                    var name = process.ProcessName;
                    var cpuTime = process.TotalProcessorTime;
                    alive.Add(process.Id);

                    // Opening StartTime is relatively expensive. Probe it once
                    // for a new PID, and afterwards only when the process name
                    // changes, the CPU counter goes backwards, or a bounded
                    // interval elapses. The periodic probe catches a recycled
                    // PID whose new process has already accumulated more CPU
                    // time than the old one.
                    var hasPrevious = _previous.TryGetValue(process.Id, out var previous);
                    DateTime? started;
                    var startCheckedAt = now;
                    if (!hasPrevious)
                    {
                        started = TryGetStartTime(process);
                    }
                    else
                    {
                        started = previous.Started;
                        startCheckedAt = previous.StartCheckedAt;

                        if (ShouldProbeStartTime(
                                previous.Name,
                                name,
                                previous.Cpu,
                                cpuTime,
                                now - previous.StartCheckedAt))
                        {
                            var currentStart = TryGetStartTime(process);
                            startCheckedAt = now;
                            if (ShouldResetBaseline(previous.Name, name, previous.Started, currentStart))
                            {
                                hasPrevious = false;
                            }

                            started = currentStart;
                        }
                    }

                    var percent = 0.0;
                    if (hasPrevious && !IsReusedPid(previous.Started, started))
                    {
                        var elapsed = (now - previous.At) / 1000.0;
                        if (elapsed > 0.05)
                        {
                            percent = Math.Max(
                                0.0,
                                (cpuTime - previous.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100.0);
                        }
                    }

                    _previous[process.Id] = new Baseline(cpuTime, now, started, name, startCheckedAt);

                    result.Add(new ProcessSample(
                        process.Id,
                        name,
                        percent,
                        process.WorkingSet64));
                }
            }
            catch
            {
                // Processes can exit or be protected between enumeration and read.
            }
        }

        foreach (var stale in _previous.Keys.Where(pid => !alive.Contains(pid)).ToArray())
        {
            _previous.Remove(stale);
        }

        return result
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.WorkingSetBytes)
            .ToList();
    }

    public void Dispose() => _previous.Clear();

    internal static bool ShouldProbeStartTime(
        string previousName,
        string currentName,
        TimeSpan previousCpu,
        TimeSpan currentCpu,
        long sinceProbeMs) =>
        !string.Equals(previousName, currentName, StringComparison.Ordinal) ||
        currentCpu < previousCpu ||
        sinceProbeMs >= StartTimeProbeMs;

    internal static bool ShouldResetBaseline(
        string previousName,
        string currentName,
        DateTime? previousStart,
        DateTime? currentStart) =>
        !string.Equals(previousName, currentName, StringComparison.Ordinal) ||
        IsReusedPid(previousStart, currentStart);

    /// <summary>
    /// A PID can be recycled between refreshes. Only discard the delta when both
    /// start times are known and differ; protected processes often hide their
    /// start time, and dropping those deltas would report them as permanently
    /// idle.
    /// </summary>
    private static bool IsReusedPid(DateTime? previous, DateTime? current) =>
        previous is { } before && current is { } after && before != after;

    private static DateTime? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            // Some protected processes do not expose their start time.
            return null;
        }
    }

    private readonly record struct Baseline(
        TimeSpan Cpu,
        long At,
        DateTime? Started,
        string Name,
        long StartCheckedAt);
}
