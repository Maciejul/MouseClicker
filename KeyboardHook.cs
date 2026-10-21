using System.Runtime.InteropServices;

namespace MouseClicker;

/// <summary>Global low-level keyboard hook. Events fire on the thread that called Install (the UI thread).</summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _handle = IntPtr.Zero;

    // Set on key events the app sends itself with SendInput, so they can be told apart from real key presses.
    private const int LLKHF_INJECTED = 0x10;

    /// <summary>Key press. The bool is true when the key event was injected by the app, not typed on the keyboard.</summary>
    public event Action<int, bool>? KeyDown;
    public event Action<int, bool>? KeyUp;

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
            // KBDLLHOOKSTRUCT starts with vkCode, followed by scanCode and flags.
            int vk = Marshal.ReadInt32(lParam);
            bool injected = (Marshal.ReadInt32(lParam, 8) & LLKHF_INJECTED) != 0;
            int msg = wParam.ToInt32();

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                KeyDown?.Invoke(vk, injected);
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                KeyUp?.Invoke(vk, injected);
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
