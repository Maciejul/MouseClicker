namespace MouseClicker;

/// <summary>
/// Turns presses of the hold trigger key into holds: single taps, double and triple taps for timed holds,
/// and extra taps that add time. Has no UI of its own, so it can be driven from tests.
/// </summary>
internal sealed class HoldTriggerController
{
    public const int TapWindowMs = 400;
    public const int MaxTimedSeconds = 300;

    private readonly HoldEngine _holder;
    private readonly Func<AppSettings> _settings;
    private readonly Func<string> _describe;
    private readonly TimeProvider _time;

    private int _tapCount;
    private long _lastTapMs;
    private bool _timedBeforeTap;     // Whether a timed hold was already running when this run of taps began.
    private long _pendingStopAt;      // NowMs at which an unanswered tap turns into "stop"; 0 when none is waiting.

    public HoldTriggerController(HoldEngine holder, Func<AppSettings> settings, Func<string> describe, TimeProvider? time = null)
    {
        _holder = holder;
        _settings = settings;
        _describe = describe;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The current hold lasts only while the trigger key is down.</summary>
    public bool DownHold { get; private set; }

    /// <summary>A short message for the user, such as "Released W key".</summary>
    public event Action<string>? Message;

    /// <summary>The hold started, ended or changed on its own, so the status text needs a redraw.</summary>
    public event Action? HoldChanged;

    private long NowMs => (long)_time.GetElapsedTime(0, _time.GetTimestamp()).TotalMilliseconds;

    /// <summary>Drops pending taps and any hold-while-down state. Does not release the hold itself.</summary>
    public void Cancel()
    {
        _pendingStopAt = 0;
        DownHold = false;
    }

    /// <summary>Call after the hold target changes: with nothing held there is nothing to follow the key.</summary>
    public void Sync()
    {
        if (!_holder.IsHolding) DownHold = false;
    }

    public void TriggerDown()
    {
        AppSettings settings = _settings();

        // Taps only group into doubles and triples when timed holds are on; otherwise every press is a single tap.
        long now = NowMs;
        _tapCount = settings.HoldTimedEnabled && now - _lastTapMs <= TapWindowMs ? _tapCount + 1 : 1;
        _lastTapMs = now;

        if (_tapCount == 1) FirstTap(settings);
        else MoreTaps(settings);
    }

    public void TriggerUp()
    {
        // A hold that lasts while the key is down ends with the key, unless a double tap turned it into a timed hold.
        if (!DownHold) return;

        DownHold = false;
        _holder.Release();
        HoldChanged?.Invoke();
    }

    /// <summary>Call often (every 50 ms or so): ends holds whose timer ran out and taps nobody followed up.</summary>
    public void Poll()
    {
        if (_pendingStopAt != 0 && NowMs >= _pendingStopAt)
        {
            // No second tap arrived, so the tap meant "stop".
            _pendingStopAt = 0;
            if (_holder.IsTimed)
            {
                string description = _describe();
                _holder.Release();
                Message?.Invoke($"Released {description}");
                HoldChanged?.Invoke();
            }
        }

        if (_holder.IsTimed)
        {
            string description = _describe();
            if (_holder.Tick())
            {
                DownHold = false;
                Message?.Invoke($"Released {description}");
                HoldChanged?.Invoke();
            }
        }
    }

    private void FirstTap(AppSettings settings)
    {
        _timedBeforeTap = _holder.IsTimed;

        if (_holder.IsTimed && settings.ExtendOnTap && settings.HoldTimedEnabled)
        {
            // A second tap inside the window adds time. If none comes, the tap meant "stop".
            _pendingStopAt = NowMs + TapWindowMs;
            return;
        }

        if (_holder.IsHolding)
        {
            DownHold = false;
            _holder.Release();
            return;
        }

        switch (settings.SingleTap)
        {
            case SingleTapBehavior.Toggle:
                _holder.Press();
                break;

            case SingleTapBehavior.WhileDown:
                _holder.Press();
                DownHold = true;
                if (settings.MaxHoldSeconds > 0) _holder.StartTimed(settings.MaxHoldSeconds);
                break;
        }
    }

    private void MoreTaps(AppSettings settings)
    {
        _pendingStopAt = 0;

        if (_timedBeforeTap && settings.ExtendOnTap)
        {
            _holder.Extend(settings.ExtendSeconds, MaxTimedSeconds);
            Message?.Invoke($"+{settings.ExtendSeconds} s");
            return;
        }

        DownHold = false;
        int seconds = _tapCount >= 3 ? settings.TripleTapSeconds : settings.DoubleTapSeconds;
        _holder.StartTimed(seconds);
    }
}
