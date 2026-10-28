using Vitals.Config;
using Vitals.Sensors;

namespace Vitals.Sampling;

/// <summary>One exported frame: just the timestamp and raw readings.</summary>
public sealed record SessionFrame(DateTimeOffset Timestamp, IReadOnlyList<Reading> Readings);

/// <summary>
/// Background sampler. The UI thread only ever reads <see cref="Latest"/>, so
/// no rendering happens off-thread and Spectre's Live display stays happy.
/// </summary>
public sealed class Sampler : IDisposable
{
    /// <summary>
    /// Upper bound for the in-memory export buffer. A full snapshot is large
    /// (core, GPU, process and sensor collections); only the timestamp and raw
    /// readings are retained for CSV export, and even those are capped so a
    /// misconfigured historySeconds cannot exhaust memory.
    /// </summary>
    internal const int MaxSessionFrames = 6_000;

    private readonly SampleEngine _engine;
    private readonly object _lifecycleLock = new();
    private readonly object _sessionLock = new();
    private readonly Queue<SessionFrame> _session = new();
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly int _historySeconds;
    private Thread? _thread;
    private int _intervalMs;
    private int _seeded;
    private int _started;
    private int _disposed;
    private int _delayFirstSample;
    private volatile Snapshot? _latest;
    private volatile bool _paused;
    private volatile string? _lastError;

    public Sampler(SampleEngine engine, AppConfig config)
    {
        _engine = engine;
        _historySeconds = config.HistorySeconds;
        IntervalMs = config.IntervalMs;
        History = new History(300);
    }

    public History History { get; }

    public int IntervalMs
    {
        get => Volatile.Read(ref _intervalMs);
        set => Volatile.Write(ref _intervalMs, Math.Clamp(value, 250, 60_000));
    }

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public Snapshot? Latest => _latest;

    public string? LastError => _lastError;

    internal int SessionCapacity => Math.Clamp(
        _historySeconds * 1000 / Math.Max(250, IntervalMs),
        1,
        MaxSessionFrames);

    /// <summary>
    /// Publishes the warm-up snapshot before the background loop starts. This
    /// gives the first dashboard frame real throughput/CPU percentages instead
    /// of zeroes; the loop waits one interval before replacing it.
    /// </summary>
    public void Seed(Snapshot snapshot)
    {
        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(Sampler));
            }

            if (_started != 0)
            {
                throw new InvalidOperationException("The sampler has already been started.");
            }

            if (_seeded != 0)
            {
                throw new InvalidOperationException("The sampler has already been seeded.");
            }

            _seeded = 1;
            History.Add(snapshot);

            lock (_sessionLock)
            {
                _session.Enqueue(new SessionFrame(snapshot.Timestamp, snapshot.Readings));
                TrimSession();
            }

            _latest = snapshot;
            Volatile.Write(ref _delayFirstSample, 1);
        }
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(Sampler));
            }

            if (_started != 0)
            {
                throw new InvalidOperationException("The sampler has already been started.");
            }

            _started = 1;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "vitals-sampler",
            };
            _thread.Start();
        }
    }

    public IReadOnlyList<SessionFrame> SessionFrames()
    {
        lock (_sessionLock)
        {
            return _session.ToArray();
        }
    }

    private void Loop()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var next = Environment.TickCount64;
        if (Interlocked.Exchange(ref _delayFirstSample, 0) != 0)
        {
            next += IntervalMs;
            var seedDelay = next - Environment.TickCount64;
            if (seedDelay > 0 && _stopEvent.Wait((int)seedDelay))
            {
                return;
            }
        }

        while (!_stopEvent.IsSet)
        {
            if (!_paused && Volatile.Read(ref _disposed) == 0)
            {
                try
                {
                    var snapshot = _engine.Sample();
                    History.Add(snapshot);

                    // Keep only what CSV export needs. Retaining whole snapshots
                    // also pins their process and sensor collections for the
                    // lifetime of the dashboard.
                    lock (_sessionLock)
                    {
                        _session.Enqueue(new SessionFrame(snapshot.Timestamp, snapshot.Readings));
                        TrimSession();
                    }

                    _latest = snapshot;
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    // Keep the last good frame on screen and surface the error in
                    // the dashboard header instead of writing over the live view.
                    _lastError = $"sampling failed: {ex.Message}";
                }
            }

            next += IntervalMs;
            var delay = next - Environment.TickCount64;
            if (delay < 0)
            {
                next = Environment.TickCount64;
                delay = 0;
            }

            if (delay > 0 && _stopEvent.Wait((int)delay))
            {
                return;
            }
        }
    }

    private void TrimSession()
    {
        while (_session.Count > SessionCapacity)
        {
            _session.Dequeue();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
            _stopEvent.Set();

            // A driver call can block indefinitely on failing hardware. Do not
            // tear down the engine underneath a still-running sampling thread;
            // the thread is a background thread and will exit with the process.
            if (_thread is not null && !_thread.Join(TimeSpan.FromSeconds(5)))
            {
                return;
            }

            _stopEvent.Dispose();
            _engine.Dispose();
        }
    }
}
