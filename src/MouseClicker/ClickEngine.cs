namespace MouseClicker;

/// <summary>Repeats left clicks at the cursor on a background thread, with optional random jitter.</summary>
internal sealed class ClickEngine : IDisposable
{
    private readonly object _gate = new();
    private object? _session;
    private int _intervalMs = 100;
    private int _jitterPercent;
    private long _clickCount;
    private long _lastClickTicks;

    public bool IsRunning
    {
        get { lock (_gate) return _session is not null; }
    }

    /// <summary>Clicks sent since the app launched.</summary>
    public long ClickCount => Interlocked.Read(ref _clickCount);

    /// <summary>Time of the most recent click, or null if none has been sent since the app launched.</summary>
    public DateTime? LastClickTime
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastClickTicks);
            return ticks == 0 ? null : new DateTime(ticks);
        }
    }

    public void Configure(int intervalMs, int jitterPercent)
    {
        _intervalMs = Math.Max(1, intervalMs);
        _jitterPercent = Math.Clamp(jitterPercent, 0, 100);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_session is not null) return;

            // Each run gets its own session token, so a Stop followed by Start cannot leave two loops running.
            var session = new object();
            _session = session;
            new Thread(() => Run(session)) { IsBackground = true, Name = "ClickEngine" }.Start();
        }
    }

    public void Stop()
    {
        lock (_gate) _session = null;
    }

    public void Dispose() => Stop();

    private void Run(object session)
    {
        while (IsCurrent(session))
        {
            NativeMethods.ClickLeft();
            Interlocked.Increment(ref _clickCount);
            Interlocked.Exchange(ref _lastClickTicks, DateTime.Now.Ticks);
            Thread.Sleep(NextDelay());
        }
    }

    private bool IsCurrent(object session)
    {
        lock (_gate) return ReferenceEquals(_session, session);
    }

    private int NextDelay()
    {
        int interval = _intervalMs;
        int jitter = _jitterPercent;
        if (jitter == 0) return interval;

        // Vary the delay by up to +/- jitter percent.
        double spread = (Random.Shared.NextDouble() * 2 - 1) * jitter / 100.0;
        return Math.Max(1, (int)Math.Round(interval * (1 + spread)));
    }
}
