namespace MouseClicker;

/// <summary>Holds a key down until told to release it. Runs on the UI thread, alongside the keyboard hook.</summary>
internal sealed class HoldEngine : IDisposable
{
    private int _vk;

    public bool IsHolding { get; private set; }

    public void Configure(int vk)
    {
        // Release the old key first so changing the setting mid-hold does not leave it stuck down.
        if (IsHolding && vk != _vk)
        {
            Release();
        }

        _vk = vk;
    }

    public void Toggle()
    {
        if (IsHolding) Release();
        else Press();
    }

    public void Press()
    {
        if (IsHolding) return;

        NativeMethods.SendKey((ushort)_vk, down: true);
        IsHolding = true;
    }

    public void Release()
    {
        if (!IsHolding) return;

        NativeMethods.SendKey((ushort)_vk, down: false);
        IsHolding = false;
    }

    public void Dispose() => Release();
}
