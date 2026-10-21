using System.Diagnostics;

namespace MouseClicker;

/// <summary>A screen position, plus the window under it and the position relative to that window.</summary>
internal readonly record struct PickedTarget(int X, int Y, string? Process, int OffsetX, int OffsetY);

internal static class TargetPicker
{
    /// <summary>Reads the cursor position and the top-level window under it.</summary>
    public static PickedTarget Pick()
    {
        NativeMethods.GetCursorPos(out var point);

        string? process = null;
        int offsetX = 0;
        int offsetY = 0;

        IntPtr hwnd = NativeMethods.RootWindowFromPoint(point);
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                process = Process.GetProcessById((int)pid).ProcessName;
                offsetX = point.X - rect.Left;
                offsetY = point.Y - rect.Top;
            }
            catch (Exception)
            {
                // The process exited between the lookup and now; treat it as no window.
                process = null;
            }
        }

        return new PickedTarget(point.X, point.Y, process, offsetX, offsetY);
    }
}
