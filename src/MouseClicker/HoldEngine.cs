namespace MouseClicker;

/// <summary>Holds a key, a mouse button, or both until told to release them. Runs on the UI thread, alongside the keyboard hook.</summary>
internal sealed class HoldEngine : IDisposable
{
    private int _vk;
    private HoldTarget _target = HoldTarget.Key;
    private ClickButton _button = ClickButton.Left;
    private long _endsAt;   // TickCount64 at which a timed hold ends; 0 when the hold has no timer.

    public bool IsHolding { get; private set; }

    /// <summary>True while a hold with a countdown is running.</summary>
    public bool IsTimed => IsHolding && _endsAt != 0;

    public int RemainingWholeSeconds => IsTimed
        ? (int)Math.Ceiling(Math.Max(0, _endsAt - Environment.TickCount64) / 1000.0)
        : 0;

    private bool UsesKey => _target != HoldTarget.Mouse;
    private bool UsesMouse => _target != HoldTarget.Key;

    public void Configure(int vk, HoldTarget target, ClickButton button)
    {
        // Release first so changing the setting mid-hold does not leave anything stuck down.
        if (IsHolding && (vk != _vk || target != _target || button != _button))
        {
            Release();
        }

        _vk = vk;
        _target = target;
        _button = button;
    }

    public void Toggle()
    {
        if (IsHolding) Release();
        else Press();
    }

    public void Press()
    {
        if (IsHolding) return;

        if (UsesKey) NativeMethods.SendKey((ushort)_vk, down: true);
        if (UsesMouse) NativeMethods.SendMouse(_button, isDown: true);
        IsHolding = true;
    }

    /// <summary>Holds for a set time, then releases. Starts the hold if it is not already on, and resets the timer if it is.</summary>
    public void StartTimed(double seconds)
    {
        Press();
        _endsAt = Environment.TickCount64 + (long)(Math.Max(1, seconds) * 1000);
    }

    /// <summary>Adds time to a timed hold, without letting the time left go past the cap.</summary>
    public void Extend(double seconds, double capSeconds)
    {
        if (!IsTimed) return;

        long now = Environment.TickCount64;
        long extended = Math.Min(_endsAt + (long)(seconds * 1000), now + (long)(capSeconds * 1000));
        _endsAt = Math.Max(_endsAt, extended);
    }

    /// <summary>Releases the hold if its timer has run out. Returns true when it did.</summary>
    public bool Tick()
    {
        if (!IsTimed || Environment.TickCount64 < _endsAt) return false;

        Release();
        return true;
    }

    /// <summary>Presses the held key down again without changing state. Used when focus returns to a window that lost the key.</summary>
    public void Reassert()
    {
        if (IsHolding && UsesKey) NativeMethods.SendKey((ushort)_vk, down: true);
    }

    public void Release()
    {
        if (!IsHolding) return;

        if (UsesMouse) NativeMethods.SendMouse(_button, isDown: false);
        if (UsesKey) NativeMethods.SendKey((ushort)_vk, down: false);
        IsHolding = false;
        _endsAt = 0;
    }

    public void Dispose() => Release();
}
