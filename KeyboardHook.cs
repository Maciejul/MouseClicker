using System.Runtime.InteropServices;

namespace MouseClicker;

/// <summary>Global low-level keyboard hook. Events fire on the thread that called Install (the UI thread).</summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _handle = IntPtr.Zero;

    public event Action<int>? KeyDown;
    public event Action<int>? KeyUp;

    public KeyboardHook()
    {
        // Keep a field reference so the delegate is not garbage collected while the hook is installed.
        _proc = HookCallback;
    }

    public void Install()
    {
        _handle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // KBDLLHOOKSTRUCT starts with vkCode.
            int vk = Marshal.ReadInt32(lParam);
            int msg = wParam.ToInt32();

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                KeyDown?.Invoke(vk);
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                KeyUp?.Invoke(vk);
        }

        return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
