using System.Diagnostics;

namespace MouseClicker;

/// <summary>What to click, where, and when to stop on its own.</summary>
internal sealed record ClickOptions(
    ClickButton Button,
    bool DoubleClick,
    int StopAfterClicks,
    int StopAfterMinutes,
    ClickTargetKind Target,
    int PointX,
    int PointY,
    string? WindowProcess,
    int OffsetX,
    int OffsetY)
{
    public static ClickOptions From(AppSettings s) => new(
        s.ClickButton, s.DoubleClick, s.StopAfterClicks, s.StopAfterMinutes,
        s.ClickTarget, s.PointX, s.PointY, s.WindowProcess, s.WindowOffsetX, s.WindowOffsetY);
}

/// <summary>Repeats clicks on a background thread, with optional random jitter.</summary>
internal sealed class ClickEngine : IDisposable
{
    private readonly object _gate = new();
    private object? _session;
    private int _intervalMs = 100;
    private int _jitterPercent;
    private volatile ClickOptions _options = ClickOptions.From(new AppSettings());
    private long _clickCount;
    private long _lastClickTicks;
    private IntPtr _window;
    private string? _windowProcess;

    /// <summary>Raised on the click thread when the engine stops itself, with the reason.</summary>
    public event Action<string>? Stopped;

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

    public void Configure(int intervalMs, int jitterPercent, ClickOptions options)
    {
        _intervalMs = Math.Max(1, intervalMs);
        _jitterPercent = Math.Clamp(jitterPercent, 0, 100);
        _options = options;
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
        long runClicks = 0;
        var elapsed = Stopwatch.StartNew();

        while (IsCurrent(session))
        {
            ClickOptions options = _options;

            if (!TryMoveToTarget(options))
            {
                StopSession(session, "Stopped: target window not found");
                return;
            }

            NativeMethods.Click(options.Button, options.DoubleClick);
            runClicks++;
            Interlocked.Increment(ref _clickCount);
            Interlocked.Exchange(ref _lastClickTicks, DateTime.Now.Ticks);

            if (options.StopAfterClicks > 0 && runClicks >= options.StopAfterClicks)
            {
                StopSession(session, $"Stopped after {runClicks:N0} clicks");
                return;
            }

            if (options.StopAfterMinutes > 0 && elapsed.Elapsed.TotalMinutes >= options.StopAfterMinutes)
            {
                StopSession(session, $"Stopped after {options.StopAfterMinutes} min");
                return;
            }

            Thread.Sleep(NextDelay());
        }
    }

    private bool IsCurrent(object session)
    {
        lock (_gate) return ReferenceEquals(_session, session);
    }

    private void StopSession(object session, string reason)
    {
        bool stopped = false;
        lock (_gate)
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
                stopped = true;
            }
        }

        if (stopped) Stopped?.Invoke(reason);
    }

    private bool TryMoveToTarget(ClickOptions options)
    {
        switch (options.Target)
        {
            case ClickTargetKind.Point:
                NativeMethods.SetCursorPos(options.PointX, options.PointY);
                return true;

            case ClickTargetKind.Window:
                if (!TryFindWindow(options.WindowProcess, out NativeMethods.RECT rect)) return false;
                NativeMethods.SetCursorPos(rect.Left + options.OffsetX, rect.Top + options.OffsetY);
                return true;

            default:
                return true;
        }
    }

    private bool TryFindWindow(string? processName, out NativeMethods.RECT rect)
    {
        rect = default;
        if (string.IsNullOrEmpty(processName)) return false;

        // Look the window up again when the process name changed or the old window is gone.
        if (_windowProcess != processName || !NativeMethods.IsWindow(_window))
        {
            _windowProcess = processName;
            _window = IntPtr.Zero;

            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        _window = process.MainWindowHandle;
                        break;
                    }
                }
            }
        }

        return _window != IntPtr.Zero && NativeMethods.GetWindowRect(_window, out rect);
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
