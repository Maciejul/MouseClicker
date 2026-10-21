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
    private readonly DispatcherTimer _focusWatch = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _repressTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IntPtr _lastForeground;
    private System.Windows.Forms.NotifyIcon? _tray;

    private bool _ready;                  // Guards handlers that fire while InitializeComponent runs.
    private CaptureTarget _capture;       // Which key button is waiting for a new key, if any.
    private bool _hotkeyDown;             // Suppresses auto-repeat while the hotkey stays held.
    private bool _holdTriggerDown;        // Suppresses auto-repeat while the hold trigger stays held.
    private bool _syncingInterval;        // Stops the slider and the number box from updating each other in a loop.
    private const int MaxIntervalMs = 1000;

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

        Title = $"Mouse Clicker v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";

        IntervalBox.Text = Math.Clamp(_settings.IntervalMs, 10, MaxIntervalMs).ToString();
        IntervalSlider.Value = Math.Clamp(_settings.IntervalMs, 10, 500);
        JitterSlider.Value = _settings.JitterPercent;
        NotifyCheck.IsChecked = _settings.NotifyOnStart;
        ToggleRadio.IsChecked = _settings.Mode == TriggerMode.Toggle;
        HoldRadio.IsChecked = _settings.Mode == TriggerMode.Hold;
        UpdateHotkeyText();
        UpdateHoldTriggerText();
        UpdateHeldKeyText();
        HoldEnabledCheck.IsChecked = _settings.HoldEnabled;
        StopOnHeldKeyCheck.IsChecked = _settings.StopHoldOnHeldKey;

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

        _focusWatch.Tick += OnFocusWatchTick;
        _focusWatch.Start();

        _repressTimer.Tick += (_, _) =>
        {
            _repressTimer.Stop();
            _holder.Reassert();
        };
    }

    private void OnFocusWatchTick(object? sender, EventArgs e)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == _lastForeground) return;
        _lastForeground = foreground;

        // Windows drops the injected key state when focus moves between windows, so press the held key again.
        _holder.Reassert();
    }

    private void UpdateStats()
    {
        ClickCountText.Text = _engine.ClickCount.ToString("N0");
        LastClickText.Text = _engine.LastClickTime?.ToString("HH:mm:ss.fff") ?? "-";
    }

    private void PushSettings()
    {
        if (!_ready) return;

        if (int.TryParse(IntervalBox.Text, out int ms))
        {
            _settings.IntervalMs = Math.Clamp(ms, 10, MaxIntervalMs);
        }

        _settings.JitterPercent = (int)JitterSlider.Value;
        _settings.Mode = HoldRadio.IsChecked == true ? TriggerMode.Hold : TriggerMode.Toggle;
        _settings.NotifyOnStart = NotifyCheck.IsChecked == true;

        _engine.Configure(_settings.IntervalMs, _settings.JitterPercent);
        _settings.Save();
    }

    private void OnIntervalChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncingInterval) return;

        if (int.TryParse(IntervalBox.Text, out int ms))
        {
            // Typed values above the maximum are clamped to it.
            if (ms > MaxIntervalMs)
            {
                ms = MaxIntervalMs;
                _syncingInterval = true;
                IntervalBox.Text = ms.ToString();
                IntervalBox.CaretIndex = IntervalBox.Text.Length;
                _syncingInterval = false;
            }

            // Typed values above the slider's range leave the slider at its maximum.
            _syncingInterval = true;
            IntervalSlider.Value = Math.Clamp(ms, 10, 500);
            _syncingInterval = false;
        }

        PushSettings();
    }

    private void OnIntervalSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncingInterval) return;

        _syncingInterval = true;
        IntervalBox.Text = ((int)e.NewValue).ToString();
        _syncingInterval = false;

        PushSettings();
    }

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

    private void OnStopOnHeldKeyChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.StopHoldOnHeldKey = StopOnHeldKeyCheck.IsChecked == true;
        _settings.Save();
    }

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
    private void OnGlobalKeyDown(int vk, bool injected)
    {
        if (_capture != CaptureTarget.None) return;

        if (!injected && vk == _settings.HeldKeyVk && _settings.StopHoldOnHeldKey)
        {
            _holder.Release();
            UpdateHoldStatus();
        }

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

    private void OnGlobalKeyUp(int vk, bool injected)
    {
        if (vk == _settings.HoldTriggerVk)
        {
            _holdTriggerDown = false;
            return;
        }

        if (!injected && vk == _settings.HeldKeyVk && _settings.StopHoldOnHeldKey)
        {
            _holder.Release();
            UpdateHoldStatus();
        }

        // A real press of the held key clears the injected key state, so press it again if the hold is still on.
        // Wait briefly so the press lands after the physical release, not in the same frame.
        if (!injected && vk == _settings.HeldKeyVk)
        {
            _repressTimer.Stop();
            _repressTimer.Start();
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
        Icon = active ? (_activeIcon ??= BuildIcon(recolorGreen: true)) : (_idleIcon ??= BuildIcon(recolorGreen: false));
    }

    // Builds the app icon for the title bar and taskbar. When recolorGreen is set, the blue and green
    // channels are swapped, which turns the icon's blue into green and keeps its shading.
    // Returns null if the icon cannot be built, which leaves the default window icon in place.
    private static System.Windows.Media.ImageSource? BuildIcon(bool recolorGreen)
    {
        try
        {
            // The app's own icon is embedded in the exe, so read it from there.
            using System.Drawing.Icon exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                exeIcon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            if (recolorGreen)
            {
                // Pixels are stored as B, G, R, A. Swapping B and G moves blue into the green channel.
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    (pixels[i], pixels[i + 1]) = (pixels[i + 1], pixels[i]);
                }
            }

            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, pixels, stride);
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
        StartStopButton.Tag = running ? "Running" : null;
        UpdateActivityStatus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statsTimer.Stop();
        _startButtonLock.Stop();
        _focusWatch.Stop();
        _repressTimer.Stop();
        _tray?.Dispose();
        _hook.KeyDown -= OnGlobalKeyDown;
        _hook.KeyUp -= OnGlobalKeyUp;
        _hook.Dispose();
        _engine.Dispose();
        // Never leave the held key stuck down after the app exits.
        _holder.Dispose();
    }
}
