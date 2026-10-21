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
    // One per run. Stop sets the wake event so the click thread does not sleep out the rest of its delay.
    private sealed class Session
    {
        public ManualResetEventSlim Wake { get; } = new(false);
    }

    private readonly object _gate = new();
    private Session? _session;
    private int _intervalMs = 100;
    private int _jitterPercent;
    private volatile ClickOptions _options = ClickOptions.From(new AppSettings());
    private long _clickCount;
    private long _lastClickTicks;
    private IntPtr _window;
    private string? _windowProcess;
    private bool _windowElevated;

    private static readonly bool AppIsElevated =
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

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
            var session = new Session();
            _session = session;
            new Thread(() => Run(session)) { IsBackground = true, Name = "ClickEngine" }.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _session?.Wake.Set();
            _session = null;
        }
    }

    public void Dispose() => Stop();

    private void Run(Session session)
    {
        // Ask Windows for 1 ms timer resolution so short intervals are not rounded up to about 15 ms.
        NativeMethods.timeBeginPeriod(1);
        try
        {
            RunLoop(session);
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
        }
    }

    private void RunLoop(Session session)
    {
        long runClicks = 0;
        var elapsed = Stopwatch.StartNew();

        while (IsCurrent(session))
        {
            ClickOptions options = _options;

            string? problem = MoveToTarget(options);
            if (problem is not null)
            {
                StopSession(session, problem);
                return;
            }

            if (!NativeMethods.Click(options.Button, options.DoubleClick))
            {
                StopSession(session, "Stopped: Windows rejected the click");
                return;
            }

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

            session.Wake.Wait(NextDelay());
        }
    }

    private bool IsCurrent(Session session)
    {
        lock (_gate) return ReferenceEquals(_session, session);
    }

    private void StopSession(Session session, string reason)
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

    /// <summary>Moves the cursor to the target. Returns why clicking cannot go on, or null when it can.</summary>
    private string? MoveToTarget(ClickOptions options)
    {
        switch (options.Target)
        {
            case ClickTargetKind.Point:
                NativeMethods.SetCursorPos(options.PointX, options.PointY);
                return null;

            case ClickTargetKind.Window:
                if (!TryFindWindow(options.WindowProcess, out NativeMethods.RECT rect)) return "Stopped: target window not found";
                if (NativeMethods.IsIconic(_window)) return "Stopped: target window is minimized";
                if (_windowElevated) return "Stopped: target window runs as administrator";
                NativeMethods.SetCursorPos(rect.Left + options.OffsetX, rect.Top + options.OffsetY);
                return null;

            default:
                return null;
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
            _windowElevated = false;

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

            // Windows drops input sent from a normal app to an elevated one without any error, so check once per window found.
            _windowElevated = _window != IntPtr.Zero
                && NativeMethods.IsWindowElevated(_window) == true
                && !AppIsElevated;
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
