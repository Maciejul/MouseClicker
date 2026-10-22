using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MouseClicker;

/// <summary>
/// Small on-screen notice (like the Windows volume OSD): topmost, never takes focus, click-through, and fades out on its
/// own after 3 s. Shown top-centre of the primary monitor's work area, so it appears over borderless/windowed games.
/// Exclusive-fullscreen games will not show it. A new toast closes the previous one so they never pile up.
/// </summary>
sealed class OverlayToast : Window
{
    const int HoldMs = 3000, FadeMs = 400, TickMs = 30;
    const double ShownOpacity = 0.92;

    static OverlayToast? _current;

    // The shared "AppToastOverlay" title lets every app's toast avoid the others.
    const string ToastTitle = "AppToastOverlay";
    delegate bool EnumProc(IntPtr h, IntPtr l);
    delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, [Out] char[] buffer, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowProc(IntPtr h, int index, WndProc value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] static extern IntPtr CallWindowProc(IntPtr proc, IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const int GWL_EXSTYLE = -20, GWLP_WNDPROC = -4;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    const uint WM_NCHITTEST = 0x84;
    static readonly IntPtr HTTRANSPARENT = new(-1);

    /// <summary>Rects of visible toasts from any app that follows the shared "AppToastOverlay" title protocol.</summary>
    static List<System.Drawing.Rectangle> OtherToastRects()
    {
        var list = new List<System.Drawing.Rectangle>();
        var buffer = new char[64];
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var length = GetWindowText(h, buffer, buffer.Length);
            if (length > 0 && buffer.AsSpan(0, length).SequenceEqual(ToastTitle) && GetWindowRect(h, out var r))
                list.Add(System.Drawing.Rectangle.FromLTRB(r.L, r.T, r.R, r.B));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    readonly Border _root;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };
    int _elapsed;
    // Kept alive while the window proc is replaced.
    WndProc? _hook;
    IntPtr _previousProc;

    public static void ShowMessage(string title, string body)
    {
        _current?.Close();
        _current = new OverlayToast(title, body);
        _current.Closed += (s, _) => { if (ReferenceEquals(_current, s)) _current = null; };
        _current.Show();
    }

    OverlayToast(string title, string body)
    {
        var titleText = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 12, 24, 0) };
        var bodyText = new TextBlock { Text = body, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 2, 24, 12) };
        var accent = new Border { Height = 4, Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)) };
        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)), CornerRadius = new CornerRadius(12), ClipToBounds = true, Opacity = ShownOpacity,
            Child = new StackPanel { Children = { accent, titleText, bodyText } },
        };
        _root.Measure(Size.Infinity);
        var size = new Size(Math.Max(_root.DesiredSize.Width, 280), _root.DesiredSize.Height);

        Title = ToastTitle;
        Content = _root;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Width = size.Width;
        Height = size.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var screen = Screens.Primary;
        var scale = screen?.Scaling ?? 1.0;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var placed = ToastPlacement.Place(
            new System.Drawing.Rectangle(area.X, area.Y, area.Width, area.Height),
            new System.Drawing.Size((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)),
            (int)Math.Round(ToastPlacement.Margin * scale), (int)Math.Round(8 * scale), OtherToastRects());
        Position = new PixelPoint(placed.X, placed.Y);

        Opened += (_, _) => MakeClickThrough();
        _timer.Tick += (_, _) =>
        {
            _elapsed += TickMs;
            if (_elapsed > HoldMs) _root.Opacity = Math.Max(0, ShownOpacity * (1 - (_elapsed - HoldMs) / (double)FadeMs));
            if (_elapsed >= HoldMs + FadeMs) Close();
        };
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Takes the toast out of the Alt+Tab list and lets mouse clicks fall through to whatever is under it.</summary>
    void MakeClickThrough()
    {
        if (TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));

        // Read the old proc first: the new one calls it for every message but WM_NCHITTEST, and may run as soon as it is set.
        _previousProc = GetWindowLongPtr(hwnd, GWLP_WNDPROC);
        _hook = (h, msg, w, l) => msg == WM_NCHITTEST ? HTTRANSPARENT : CallWindowProc(_previousProc, h, msg, w, l);
        SetWindowProc(hwnd, GWLP_WNDPROC, _hook);
    }
}
