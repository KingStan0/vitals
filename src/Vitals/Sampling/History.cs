using Vitals.Sensors;

namespace Vitals.Sampling;

public sealed class Series
{
    private readonly object _sync = new();
    private readonly Queue<double> _values;
    private double _peak;

    public Series(int capacity)
    {
        Capacity = Math.Max(2, capacity);
        _values = new Queue<double>(Capacity);
    }

    public int Capacity { get; }

    public double Peak
    {
        get
        {
            lock (_sync)
            {
                return _peak;
            }
        }
    }

    public double? Last
    {
        get
        {
            lock (_sync)
            {
                return _values.Count > 0 ? _values.Last() : null;
            }
        }
    }

    public void Add(double value)
    {
        lock (_sync)
        {
            _values.Enqueue(value);
            while (_values.Count > Capacity)
            {
                _values.Dequeue();
            }

            if (double.IsFinite(value) && value > _peak)
            {
                _peak = value;
            }
        }
    }

    public IReadOnlyList<double> Values
    {
        get
        {
            lock (_sync)
            {
                return _values.ToArray();
            }
        }
    }

    public void ResetPeak()
    {
        lock (_sync)
        {
            _peak = 0;
        }
    }
}

/// <summary>Fixed-length history used for sparklines and peaks.</summary>
public sealed class History
{
    public History(int capacity)
    {
        CpuLoad = new Series(capacity);
        CpuTemp = new Series(capacity);
        CpuPower = new Series(capacity);
        GpuLoad = new Series(capacity);
        GpuEdge = new Series(capacity);
        GpuHotspot = new Series(capacity);
        MemoryUsed = new Series(capacity);
        NetRx = new Series(capacity);
        NetTx = new Series(capacity);
        DiskRead = new Series(capacity);
        DiskWrite = new Series(capacity);
    }

    public Series CpuLoad { get; }

    public Series CpuTemp { get; }

    public Series CpuPower { get; }

    public Series GpuLoad { get; }

    public Series GpuEdge { get; }

    public Series GpuHotspot { get; }

    public Series MemoryUsed { get; }

    public Series NetRx { get; }

    public Series NetTx { get; }

    public Series DiskRead { get; }

    public Series DiskWrite { get; }

    public void Add(Snapshot snapshot)
    {
        CpuLoad.Add(snapshot.CpuLoadPercent);
        CpuTemp.Add(snapshot.CpuTempC ?? double.NaN);
        CpuPower.Add(snapshot.CpuPowerW ?? double.NaN);
        GpuLoad.Add(snapshot.Gpu?.LoadPercent ?? double.NaN);
        GpuEdge.Add(snapshot.Gpu?.EdgeC ?? double.NaN);
        GpuHotspot.Add(snapshot.Gpu?.HotspotC ?? double.NaN);
        MemoryUsed.Add(snapshot.Memory?.UsedPercent ?? double.NaN);
        NetRx.Add(snapshot.NetReceiveBytesPerSecond ?? double.NaN);
        NetTx.Add(snapshot.NetSendBytesPerSecond ?? double.NaN);
        DiskRead.Add(snapshot.DiskReadBytesPerSecond);
        DiskWrite.Add(snapshot.DiskWriteBytesPerSecond);
    }

    public void ResetPeaks()
    {
        foreach (var series in All())
        {
            series.ResetPeak();
        }
    }

    private IEnumerable<Series> All()
    {
        yield return CpuLoad;
        yield return CpuTemp;
        yield return CpuPower;
        yield return GpuLoad;
        yield return GpuEdge;
        yield return GpuHotspot;
        yield return MemoryUsed;
        yield return NetRx;
        yield return NetTx;
        yield return DiskRead;
        yield return DiskWrite;
    }
}
