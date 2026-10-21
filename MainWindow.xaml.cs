using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MouseClicker;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ClickEngine _engine = new();
    private readonly HoldEngine _holder = new();
    private readonly KeyboardHook _hook = new();
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _startButtonLock = new() { Interval = TimeSpan.FromSeconds(1) };
    private System.Windows.Forms.NotifyIcon? _tray;

    private bool _ready;                  // Guards handlers that fire while InitializeComponent runs.
    private CaptureTarget _capture;       // Which key button is waiting for a new key, if any.
    private bool _hotkeyDown;             // Suppresses auto-repeat while the hotkey stays held.
    private bool _holdTriggerDown;        // Suppresses auto-repeat while the hold trigger stays held.

    private static readonly System.Windows.Media.SolidColorBrush ActiveDotBrush = MakeFrozenBrush(0x16, 0xA3, 0x4A);
    private static readonly System.Windows.Media.SolidColorBrush IdleDotBrush = MakeFrozenBrush(0x9C, 0xA3, 0xAF);
    private System.Windows.Media.ImageSource? _activeIcon;
    private System.Windows.Media.ImageSource? _idleIcon;

    private enum CaptureTarget
    {
        None,
        Hotkey,
        HoldTrigger,
        HeldKey,
    }

    public MainWindow()
    {
        _settings = AppSettings.Load();
        InitializeComponent();

        IntervalBox.Text = _settings.IntervalMs.ToString();
        JitterSlider.Value = _settings.JitterPercent;
        NotifyCheck.IsChecked = _settings.NotifyOnStart;
        ToggleRadio.IsChecked = _settings.Mode == TriggerMode.Toggle;
        HoldRadio.IsChecked = _settings.Mode == TriggerMode.Hold;
        UpdateHotkeyText();
        UpdateHoldTriggerText();
        UpdateHeldKeyText();
        HoldEnabledCheck.IsChecked = _settings.HoldEnabled;

        _holder.Configure(_settings.HeldKeyVk);
        UpdateHoldStatus();

        _hook.KeyDown += OnGlobalKeyDown;
        _hook.KeyUp += OnGlobalKeyUp;
        _hook.Install();

        _ready = true;
        PushSettings();
        UpdateStatus();

        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();

        _startButtonLock.Tick += OnStartButtonLockElapsed;
    }

    private void UpdateStats()
    {
        ClickCountText.Text = _engine.ClickCount.ToString("N0");
        LastClickText.Text = _engine.LastClickTime?.ToString("HH:mm:ss.fff") ?? "-";
    }

    private void PushSettings()
    {
        if (!_ready) return;

        if (int.TryParse(IntervalBox.Text, out int ms) && ms is >= 10 and <= 60000)
        {
            _settings.IntervalMs = ms;
        }

        _settings.JitterPercent = (int)JitterSlider.Value;
        _settings.Mode = HoldRadio.IsChecked == true ? TriggerMode.Hold : TriggerMode.Toggle;
        _settings.NotifyOnStart = NotifyCheck.IsChecked == true;

        _engine.Configure(_settings.IntervalMs, _settings.JitterPercent);
        _settings.Save();
    }

    private void OnIntervalChanged(object sender, TextChangedEventArgs e) => PushSettings();

    private void OnNotifyChanged(object sender, RoutedEventArgs e) => PushSettings();

    private void OnJitterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        JitterLabel.Text = $"Random jitter: {(int)e.NewValue}%";
        PushSettings();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _engine.Stop();
        _hotkeyDown = false;
        PushSettings();
        UpdateStatus();
    }

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        // The clicker sends clicks at the cursor, which sits on this button right after Start.
        // Lock the button briefly so those synthetic clicks cannot toggle it straight back off.
        StartStopButton.IsEnabled = false;
        _startButtonLock.Stop();
        _startButtonLock.Start();

        if (_engine.IsRunning)
        {
            _engine.Stop();
        }
        else
        {
            _engine.Start();
            ShowStartNotification();
        }

        UpdateStatus();
    }

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e) => StartCapture(CaptureTarget.Hotkey, HotkeyButton);

    private void OnHoldTriggerButtonClick(object sender, RoutedEventArgs e) => StartCapture(CaptureTarget.HoldTrigger, HoldTriggerButton);

    private void OnHeldKeyButtonClick(object sender, RoutedEventArgs e) => StartCapture(CaptureTarget.HeldKey, HeldKeyButton);

    private void OnHoldEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.HoldEnabled = HoldEnabledCheck.IsChecked == true;
        if (!_settings.HoldEnabled) _holder.Release();

        _settings.Save();
        UpdateHoldStatus();
    }

    private void StartCapture(CaptureTarget target, System.Windows.Controls.Button button)
    {
        _capture = target;
        button.Content = "Press a key...";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capture == CaptureTarget.None) return;

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        CaptureTarget target = _capture;
        _capture = CaptureTarget.None;

        if (key != Key.Escape && key != Key.None)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);

            // The three keys share one global hook, so one key cannot drive two actions.
            if (!IsKeyInUse(vk, except: target))
            {
                SetKeyForTarget(target, vk);
                _settings.Save();
            }
        }

        UpdateHotkeyText();
        UpdateHoldTriggerText();
        UpdateHeldKeyText();
    }

    private bool IsKeyInUse(int vk, CaptureTarget except)
    {
        return (except != CaptureTarget.Hotkey && vk == _settings.HotkeyVk)
            || (except != CaptureTarget.HoldTrigger && vk == _settings.HoldTriggerVk)
            || (except != CaptureTarget.HeldKey && vk == _settings.HeldKeyVk);
    }

    private void SetKeyForTarget(CaptureTarget target, int vk)
    {
        switch (target)
        {
            case CaptureTarget.Hotkey:
                _settings.HotkeyVk = vk;
                break;
            case CaptureTarget.HoldTrigger:
                _settings.HoldTriggerVk = vk;
                break;
            case CaptureTarget.HeldKey:
                _settings.HeldKeyVk = vk;
                _holder.Configure(vk);
                UpdateHoldStatus();
                break;
        }
    }

    // Global hotkey handling. These run for every key press on the system, including while this window is not focused.
    private void OnGlobalKeyDown(int vk)
    {
        if (_capture != CaptureTarget.None) return;

        if (vk == _settings.HoldTriggerVk && _settings.HoldEnabled)
        {
            if (_holdTriggerDown) return;
            _holdTriggerDown = true;

            _holder.Toggle();
            UpdateHoldStatus();
            return;
        }

        if (vk != _settings.HotkeyVk || _hotkeyDown) return;
        _hotkeyDown = true;

        if (_settings.Mode == TriggerMode.Toggle)
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
            }
            else
            {
                _engine.Start();
                ShowStartNotification();
            }
        }
        else
        {
            _engine.Start();
            ShowStartNotification();
        }

        UpdateStatus();
    }

    private void OnGlobalKeyUp(int vk)
    {
        if (vk == _settings.HoldTriggerVk)
        {
            _holdTriggerDown = false;
            return;
        }

        if (vk != _settings.HotkeyVk) return;
        _hotkeyDown = false;

        if (_settings.Mode == TriggerMode.Hold)
        {
            _engine.Stop();
            UpdateStatus();
        }
    }

    private void ShowStartNotification()
    {
        if (!_settings.NotifyOnStart) return;

        _tray ??= new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
        };
        // The tray icon only exists to host the balloon; hide it again once the notification is dismissed.
        _tray.BalloonTipClosed -= OnBalloonClosed;
        _tray.BalloonTipClosed += OnBalloonClosed;
        _tray.Visible = true;
        _tray.ShowBalloonTip(3000, "Mouse Clicker", $"Clicking every {_settings.IntervalMs} ms. Total clicks: {_engine.ClickCount:N0}", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void OnBalloonClosed(object? sender, EventArgs e)
    {
        if (_tray is not null) _tray.Visible = false;
    }

    private void UpdateHotkeyText()
    {
        HotkeyButton.Content = KeyInterop.KeyFromVirtualKey(_settings.HotkeyVk).ToString();
    }

    private void UpdateHoldTriggerText()
    {
        HoldTriggerButton.Content = KeyInterop.KeyFromVirtualKey(_settings.HoldTriggerVk).ToString();
    }

    private void UpdateHeldKeyText()
    {
        HeldKeyButton.Content = KeyInterop.KeyFromVirtualKey(_settings.HeldKeyVk).ToString();
    }

    private void UpdateHoldStatus()
    {
        HoldStatusText.Text = _holder.IsHolding
            ? $"Holding {KeyInterop.KeyFromVirtualKey(_settings.HeldKeyVk)} key"
            : "Not holding";
        UpdateActivityStatus();
    }

    private void UpdateActivityStatus()
    {
        bool clicking = _engine.IsRunning;
        bool holding = _holder.IsHolding;

        ActivityText.Text = (clicking, holding) switch
        {
            (true, true) => "Mouse clicker + Hold active",
            (true, false) => "Mouse clicker active",
            (false, true) => "Hold active",
            _ => "Idle",
        };

        bool active = clicking || holding;
        ActivityDot.Fill = (System.Windows.Media.Brush)FindResource(active ? "SuccessBrush" : "MutedBrush");
        ActivityText.Foreground = (System.Windows.Media.Brush)FindResource(active ? "TextBrush" : "MutedBrush");
        Icon = active ? (_activeIcon ??= BuildIcon(ActiveDotBrush)) : (_idleIcon ??= BuildIcon(IdleDotBrush));
    }

    private static System.Windows.Media.SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // Draws the app icon with a status dot in the corner. Used for the title bar and taskbar.
    // Returns null if the icon cannot be drawn, which leaves the default window icon in place.
    private static System.Windows.Media.ImageSource? BuildIcon(System.Windows.Media.Brush dot)
    {
        try
        {
            // The app's own icon is embedded in the exe, so read it from there.
            using System.Drawing.Icon exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!;
            var appIcon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                exeIcon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            var visual = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext context = visual.RenderOpen())
            {
                context.DrawImage(appIcon, new System.Windows.Rect(0, 0, 32, 32));
                context.DrawEllipse(dot, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2), new System.Windows.Point(25, 25), 6, 6);
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // The status icon is cosmetic; never let it stop the window from opening.
            return null;
        }
    }

    private void OnStartButtonLockElapsed(object? sender, EventArgs e)
    {
        _startButtonLock.Stop();
        StartStopButton.IsEnabled = true;
    }

    private void UpdateStatus()
    {
        bool running = _engine.IsRunning;
        StatusText.Text = running ? "Clicking..." : "Stopped";
        StartStopButton.Content = running ? "Stop" : "Start";
        UpdateActivityStatus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statsTimer.Stop();
        _startButtonLock.Stop();
        _tray?.Dispose();
        _hook.KeyDown -= OnGlobalKeyDown;
        _hook.KeyUp -= OnGlobalKeyUp;
        _hook.Dispose();
        _engine.Dispose();
        // Never leave the held key stuck down after the app exits.
        _holder.Dispose();
    }
}
