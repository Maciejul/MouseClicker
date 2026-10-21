using System.Runtime.InteropServices;

namespace MouseClicker;

internal static partial class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MAPVK_VK_TO_VSC_EX = 4;
    private const uint GA_ROOT = 2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    // Hook procedure. It is passed as a function pointer, so the caller must keep the delegate alive.
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint mapType);

    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint period);

    // Mouse and keyboard data share one union, which starts at offset 8 on 64-bit Windows (the app is x64 only).
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    /// <summary>Returns the top-level window under a screen point, or zero if there is none.</summary>
    public static IntPtr RootWindowFromPoint(POINT point)
    {
        IntPtr hwnd = WindowFromPoint(point);
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT);
    }

    /// <summary>
    /// True when the window's process runs elevated. Windows drops input sent from a normal app to an elevated one
    /// without reporting an error. Null when it cannot be told (for example, access denied).
    /// </summary>
    public static bool? IsWindowElevated(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;

        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token)) return null;
            try
            {
                return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static (uint Down, uint Up) MouseFlags(ClickButton button) => button switch
    {
        ClickButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
        ClickButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
        _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
    };

    private static bool Send(INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;

    /// <summary>Sends a click, or a double click, with the given mouse button at the current cursor position. False if Windows rejected the input.</summary>
    public static bool Click(ClickButton button, bool doubleClick)
    {
        var (down, up) = MouseFlags(button);
        var inputs = new INPUT[doubleClick ? 4 : 2];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].type = INPUT_MOUSE;
            inputs[i].mi.dwFlags = i % 2 == 0 ? down : up;
        }

        return Send(inputs);
    }

    /// <summary>Presses or releases a mouse button without clicking.</summary>
    public static bool SendMouse(ClickButton button, bool isDown)
    {
        var (down, up) = MouseFlags(button);
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].mi.dwFlags = isDown ? down : up;
        return Send(inputs);
    }

    /// <summary>
    /// Sends a key press or release for a virtual-key code. The key goes out as a scan code, which is what games
    /// that read DirectInput or raw input look at; keys with no scan code fall back to the virtual-key code.
    /// </summary>
    public static bool SendKey(ushort vk, bool down)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;

        uint scan = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (scan != 0)
        {
            flags |= KEYEVENTF_SCANCODE;
            if ((scan & 0xFF00) == 0xE000) flags |= KEYEVENTF_EXTENDEDKEY;
            inputs[0].ki.wScan = (ushort)(scan & 0xFF);
        }
        else
        {
            inputs[0].ki.wVk = vk;
        }

        inputs[0].ki.dwFlags = flags;
        return Send(inputs);
    }
}

/// <summary>Where held keys and buttons are sent. Lets the hold logic run in tests without real input.</summary>
internal interface IInputSink
{
    void SendKey(ushort vk, bool down);
    void SendMouse(ClickButton button, bool down);
}

internal sealed class SystemInput : IInputSink
{
    public void SendKey(ushort vk, bool down) => NativeMethods.SendKey(vk, down);

    public void SendMouse(ClickButton button, bool down) => NativeMethods.SendMouse(button, down);
}
