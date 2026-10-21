using System.Diagnostics;

namespace Vitals.Sensors;

/// <summary>
/// Per-process CPU and working set. CPU is normalised across all logical cores.
/// The first sample after start always reports 0% because there is no baseline.
/// </summary>
public sealed class ProcessSampler
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();

    public List<ProcessSample> Sample(int count)
    {
        var now = DateTime.UtcNow;
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

                    var cpuTime = process.TotalProcessorTime;
                    alive.Add(process.Id);

                    var percent = 0.0;
                    if (_previous.TryGetValue(process.Id, out var previous))
                    {
                        var elapsed = (now - previous.At).TotalSeconds;
                        if (elapsed > 0.05)
                        {
                            percent = Math.Max(
                                0.0,
                                (cpuTime - previous.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100.0);
                        }
                    }

                    _previous[process.Id] = (cpuTime, now);

                    result.Add(new ProcessSample(
                        process.Id,
                        process.ProcessName,
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
            .Take(count)
            .ToList();
    }

    public void Dispose() => _previous.Clear();
}
