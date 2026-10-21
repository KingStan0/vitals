using Vitals.Config;
using Vitals.Sensors;

namespace Vitals.Sampling;

/// <summary>
/// Background sampler. The UI thread only ever reads <see cref="Latest"/>, so
/// no rendering happens off-thread and Spectre's Live display stays happy.
/// </summary>
public sealed class Sampler : IDisposable
{
    private readonly SampleEngine _engine;
    private readonly object _sessionLock = new();
    private readonly List<Snapshot> _session = new();
    private readonly int _maxSessionSnapshots;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile Snapshot? _latest;
    private volatile bool _paused;

    public Sampler(SampleEngine engine, AppConfig config)
    {
        _engine = engine;
        IntervalMs = config.IntervalMs;
        _maxSessionSnapshots = Math.Max(60, config.HistorySeconds * 1000 / Math.Max(250, config.IntervalMs));
        History = new History(300);
    }

    public History History { get; }

    public int IntervalMs { get; set; }

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public Snapshot? Latest => _latest;

    public void Start()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "vitals-sampler",
        };
        _thread.Start();
    }

    public IReadOnlyList<Snapshot> SessionSnapshot()
    {
        lock (_sessionLock)
        {
            return _session.ToArray();
        }
    }

    private void Loop()
    {
        var next = Environment.TickCount64;

        while (!_stop)
        {
            if (!_paused)
            {
                try
                {
                    var snapshot = _engine.Sample();
                    History.Add(snapshot);

                    lock (_sessionLock)
                    {
                        _session.Add(snapshot);
                        if (_session.Count > _maxSessionSnapshots)
                        {
                            _session.RemoveAt(0);
                        }
                    }

                    _latest = snapshot;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"vitals: sampling failed: {ex.Message}");
                }
            }

            next += IntervalMs;
            var delay = next - Environment.TickCount64;
            if (delay < 0)
            {
                next = Environment.TickCount64;
                delay = 0;
            }

            if (delay > 0)
            {
                Thread.Sleep((int)delay);
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(2000);
        _engine.Dispose();
    }
}
