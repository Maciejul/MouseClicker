using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private readonly DispatcherTimer _pendingTimedStop = new() { Interval = TimeSpan.FromMilliseconds(TapWindowMs) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _lastForeground;
    private System.Windows.Forms.NotifyIcon? _tray;

    private bool _ready;                  // Guards handlers that fire while the controls are being filled in.
    private CaptureTarget _capture;       // Which key button is waiting for a new key, if any.
    private bool _hotkeyDown;             // Suppresses auto-repeat while the hotkey stays held.
    private bool _holdTriggerDown;        // Suppresses auto-repeat while the hold trigger stays held.
    private bool _profileKeyDown;         // Suppresses auto-repeat while the profile key stays held.
    private bool _syncingInterval;        // Stops the slider and the number box from updating each other in a loop.
    private bool _launchedMinimized;
    private int _captureCountdown;

    // Tap counting for the hold trigger.
    private int _tapCount;
    private long _lastTapTick;
    private bool _timedBeforeTap;         // Whether a timed hold was already running when this run of taps began.
    private bool _downHold;               // The current hold lasts only while the trigger key is down.
    private int _shownRemaining = -1;     // Whole seconds last drawn for a timed hold, so the text only redraws on change.

    private const int MaxIntervalMs = 1000;
    private const int TapWindowMs = 400;
    private const int MaxTimedSeconds = 300;

    private System.Windows.Media.ImageSource? _activeIcon;
    private System.Windows.Media.ImageSource? _idleIcon;

    private enum CaptureTarget
    {
        None,
        Hotkey,
        HoldTrigger,
        HeldKey,
        ProfileKey,
    }

    public MainWindow()
    {
        _settings = AppSettings.Load();
        InitializeComponent();

        Title = $"Mouse Clicker v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";

        LoadUi();
        ApplyHoldConfig();
        UpdateHoldStatus();

        _hook.KeyDown += OnGlobalKeyDown;
        _hook.KeyUp += OnGlobalKeyUp;
        _hook.Install();

        _ready = true;
        PushSettings();
        PushHoldSettings();
        UpdateStatus();
        UpdateTrayVisibility();

        _statsTimer.Tick += OnStatsTick;
        _statsTimer.Start();

        _startButtonLock.Tick += OnStartButtonLockElapsed;

        _focusWatch.Tick += OnFocusWatchTick;
        _focusWatch.Start();

        _repressTimer.Tick += (_, _) =>
        {
            _repressTimer.Stop();
            _holder.Reassert();
        };

        _pendingTimedStop.Tick += (_, _) =>
        {
            // No second tap arrived, so the tap meant "stop".
            _pendingTimedStop.Stop();
            if (!_holder.IsTimed) return;

            _holder.Release();
            UpdateHoldStatus();
            ShowToast($"Released {HeldDescription()}");
        };

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBorder.Visibility = Visibility.Collapsed;
        };

        _captureTimer.Tick += OnCaptureTick;

        _engine.Stopped += reason => Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateStatus();
            ShowToast(reason);
        }));

        _launchedMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
        Loaded += (_, _) =>
        {
            if (_launchedMinimized && _settings.MinimizeToTray)
            {
                Dispatcher.BeginInvoke(new Action(Hide));
            }
        };
    }

    // Fills every control from the settings. Used at startup and after a profile is applied.
    private void LoadUi()
    {
        bool wasReady = _ready;
        _ready = false;

        IntervalBox.Text = Math.Clamp(_settings.IntervalMs, 10, MaxIntervalMs).ToString();
        IntervalSlider.Value = Math.Clamp(_settings.IntervalMs, 10, 500);
        JitterSlider.Value = _settings.JitterPercent;
        JitterLabel.Text = $"Random jitter: {_settings.JitterPercent}%";
        NotifyCheck.IsChecked = _settings.NotifyOnStart;
        ToggleRadio.IsChecked = _settings.Mode == TriggerMode.Toggle;
        HoldRadio.IsChecked = _settings.Mode == TriggerMode.Hold;

        ClickButtonCombo.SelectedIndex = (int)_settings.ClickButton;
        DoubleClickCheck.IsChecked = _settings.DoubleClick;
        ClickTargetCombo.SelectedIndex = (int)_settings.ClickTarget;
        StopClicksBox.Text = _settings.StopAfterClicks.ToString();
        StopMinutesBox.Text = _settings.StopAfterMinutes.ToString();
        UpdateTargetText();

        HoldEnabledCheck.IsChecked = _settings.HoldEnabled;
        StopOnHeldKeyCheck.IsChecked = _settings.StopHoldOnHeldKey;
        TapToggleRadio.IsChecked = _settings.SingleTap == SingleTapBehavior.Toggle;
        TapWhileRadio.IsChecked = _settings.SingleTap == SingleTapBehavior.WhileDown;
        TapNoneRadio.IsChecked = _settings.SingleTap == SingleTapBehavior.Nothing;
        MaxHoldBox.Text = _settings.MaxHoldSeconds.ToString();
        HoldTimedCheck.IsChecked = _settings.HoldTimedEnabled;
        DoubleTapBox.Text = _settings.DoubleTapSeconds.ToString();
        TripleTapBox.Text = _settings.TripleTapSeconds.ToString();
        ExtendCheck.IsChecked = _settings.ExtendOnTap;
        ExtendBox.Text = _settings.ExtendSeconds.ToString();
        HoldTargetCombo.SelectedIndex = (int)_settings.HoldTargetKind;
        HeldMouseCombo.SelectedIndex = (int)_settings.HeldMouseButton;

        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        StartMinimizedCheck.IsChecked = _settings.StartMinimized;
        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;

        UpdateHotkeyText();
        UpdateHoldTriggerText();
        UpdateHeldKeyText();
        UpdateProfileKeyText();
        RefreshProfileCombo();

        _ready = wasReady;
    }

    private void OnFocusWatchTick(object? sender, EventArgs e)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == _lastForeground) return;
        _lastForeground = foreground;

        // Windows drops the injected key state when focus moves between windows, so press the held key again.
        _holder.Reassert();
    }

    private void OnStatsTick(object? sender, EventArgs e)
    {
        ClickCountText.Text = _engine.ClickCount.ToString("N0");
        LastClickText.Text = _engine.LastClickTime?.ToString("HH:mm:ss.fff") ?? "-";

        string description = HeldDescription();
        if (_holder.Tick())
        {
            _downHold = false;
            _shownRemaining = -1;
            UpdateHoldStatus();
            ShowToast($"Released {description}");
        }
        else if (_holder.IsTimed)
        {
            int remaining = _holder.RemainingWholeSeconds;
            if (remaining != _shownRemaining)
            {
                _shownRemaining = remaining;
                UpdateHoldStatus();
            }
        }
    }

    private static int ReadInt(System.Windows.Controls.TextBox box, int min, int max, int current)
    {
        return int.TryParse(box.Text, out int value) ? Math.Clamp(value, min, max) : current;
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

        if (ClickButtonCombo.SelectedIndex >= 0) _settings.ClickButton = (ClickButton)ClickButtonCombo.SelectedIndex;
        _settings.DoubleClick = DoubleClickCheck.IsChecked == true;
        _settings.StopAfterClicks = ReadInt(StopClicksBox, 0, 1_000_000_000, _settings.StopAfterClicks);
        _settings.StopAfterMinutes = ReadInt(StopMinutesBox, 0, 100_000, _settings.StopAfterMinutes);

        _engine.Configure(_settings.IntervalMs, _settings.JitterPercent, ClickOptions.From(_settings));
        _settings.Save();
    }

    private void PushHoldSettings()
    {
        if (!_ready) return;

        _settings.SingleTap = TapWhileRadio.IsChecked == true ? SingleTapBehavior.WhileDown
            : TapNoneRadio.IsChecked == true ? SingleTapBehavior.Nothing
            : SingleTapBehavior.Toggle;
        _settings.MaxHoldSeconds = ReadInt(MaxHoldBox, 0, MaxTimedSeconds, _settings.MaxHoldSeconds);
        _settings.HoldTimedEnabled = HoldTimedCheck.IsChecked == true;
        _settings.DoubleTapSeconds = ReadInt(DoubleTapBox, 1, MaxTimedSeconds, _settings.DoubleTapSeconds);
        _settings.TripleTapSeconds = ReadInt(TripleTapBox, 1, MaxTimedSeconds, _settings.TripleTapSeconds);
        _settings.ExtendOnTap = ExtendCheck.IsChecked == true;
        _settings.ExtendSeconds = ReadInt(ExtendBox, 1, MaxTimedSeconds, _settings.ExtendSeconds);
        if (HoldTargetCombo.SelectedIndex >= 0) _settings.HoldTargetKind = (HoldTarget)HoldTargetCombo.SelectedIndex;
        if (HeldMouseCombo.SelectedIndex >= 0) _settings.HeldMouseButton = (ClickButton)HeldMouseCombo.SelectedIndex;

        HeldMouseCombo.Visibility = _settings.HoldTargetKind == HoldTarget.Key ? Visibility.Collapsed : Visibility.Visible;
        ApplyHoldConfig();
        _settings.Save();
        UpdateHoldStatus();
    }

    private void ApplyHoldConfig()
    {
        _holder.Configure(_settings.HeldKeyVk, _settings.HoldTargetKind, _settings.HeldMouseButton);
        if (!_holder.IsHolding) _downHold = false;
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

    private void OnClickOptionsChanged(object sender, RoutedEventArgs e) => PushSettings();

    private void OnClickNumberChanged(object sender, TextChangedEventArgs e) => PushSettings();

    private void OnHoldOptionsChanged(object sender, RoutedEventArgs e) => PushHoldSettings();

    private void OnHoldNumberChanged(object sender, TextChangedEventArgs e) => PushHoldSettings();

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

    private void OnProfileKeyButtonClick(object sender, RoutedEventArgs e) => StartCapture(CaptureTarget.ProfileKey, ProfileKeyButton);

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
        if (!_settings.HoldEnabled)
        {
            _pendingTimedStop.Stop();
            _downHold = false;
            _holder.Release();
        }

        _settings.Save();
        UpdateHoldStatus();
    }

    // Click target ------------------------------------------------------------------------------------------------

    private void OnClickTargetChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        if (ClickTargetCombo.SelectedIndex >= 0) _settings.ClickTarget = (ClickTargetKind)ClickTargetCombo.SelectedIndex;
        UpdateTargetText();
        PushSettings();
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        // The user needs a moment to move the cursor over the spot, so wait a few seconds before reading it.
        _captureCountdown = 3;
        CaptureButton.IsEnabled = false;
        CaptureButton.Content = $"Move the cursor... {_captureCountdown}";
        _captureTimer.Start();
    }

    private void OnCaptureTick(object? sender, EventArgs e)
    {
        _captureCountdown--;
        if (_captureCountdown > 0)
        {
            CaptureButton.Content = $"Move the cursor... {_captureCountdown}";
            return;
        }

        _captureTimer.Stop();
        PickedTarget picked = TargetPicker.Pick();

        if (_settings.ClickTarget == ClickTargetKind.Point)
        {
            _settings.PointX = picked.X;
            _settings.PointY = picked.Y;
        }
        else if (picked.Process is null)
        {
            ShowToast("No window found at that spot");
        }
        else
        {
            _settings.WindowProcess = picked.Process;
            _settings.WindowOffsetX = picked.OffsetX;
            _settings.WindowOffsetY = picked.OffsetY;
        }

        PushSettings();
        UpdateTargetText();
    }

    private void UpdateTargetText()
    {
        _captureTimer.Stop();
        CaptureButton.Content = "Capture (3 s)";
        CaptureButton.IsEnabled = _settings.ClickTarget != ClickTargetKind.Cursor;
        CaptureButton.Visibility = _settings.ClickTarget == ClickTargetKind.Cursor ? Visibility.Collapsed : Visibility.Visible;

        TargetText.Text = _settings.ClickTarget switch
        {
            ClickTargetKind.Point => $"Point {_settings.PointX}, {_settings.PointY}. Capture reads the cursor after 3 s.",
            ClickTargetKind.Window => _settings.WindowProcess is null
                ? "No window captured yet. Capture reads the window under the cursor after 3 s."
                : $"{_settings.WindowProcess}, +{_settings.WindowOffsetX}, +{_settings.WindowOffsetY}. Follows the window if it moves.",
            _ => "Clicks wherever the cursor is.",
        };
    }

    // Keys --------------------------------------------------------------------------------------------------------

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

            // The keys share one global hook, so one key cannot drive two actions.
            if (!IsKeyInUse(vk, except: target))
            {
                SetKeyForTarget(target, vk);
                _settings.Save();
            }
        }

        UpdateHotkeyText();
        UpdateHoldTriggerText();
        UpdateHeldKeyText();
        UpdateProfileKeyText();
    }

    private bool IsKeyInUse(int vk, CaptureTarget except)
    {
        return (except != CaptureTarget.Hotkey && vk == _settings.HotkeyVk)
            || (except != CaptureTarget.HoldTrigger && vk == _settings.HoldTriggerVk)
            || (except != CaptureTarget.HeldKey && vk == _settings.HeldKeyVk)
            || (except != CaptureTarget.ProfileKey && vk == _settings.ProfileNextVk);
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
                ApplyHoldConfig();
                UpdateHoldStatus();
                break;
            case CaptureTarget.ProfileKey:
                _settings.ProfileNextVk = vk;
                break;
        }
    }

    // Global hotkey handling. These run for every key press on the system, including while this window is not focused.
    private void OnGlobalKeyDown(int vk, bool injected)
    {
        if (_capture != CaptureTarget.None) return;

        if (!injected && vk == _settings.HeldKeyVk && _settings.StopHoldOnHeldKey)
        {
            _pendingTimedStop.Stop();
            _downHold = false;
            _holder.Release();
            UpdateHoldStatus();
        }

        if (vk == _settings.HoldTriggerVk && _settings.HoldEnabled)
        {
            if (_holdTriggerDown) return;
            _holdTriggerDown = true;

            HandleTriggerDown();
            UpdateHoldStatus();
            return;
        }

        if (vk == _settings.ProfileNextVk && !injected)
        {
            if (_profileKeyDown) return;
            _profileKeyDown = true;

            NextProfile();
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

            // A hold that lasts while the key is down ends with the key, unless a double tap turned it into a timed hold.
            if (_downHold)
            {
                _downHold = false;
                _holder.Release();
                UpdateHoldStatus();
            }

            return;
        }

        if (vk == _settings.ProfileNextVk)
        {
            _profileKeyDown = false;
            return;
        }

        if (!injected && vk == _settings.HeldKeyVk && _settings.StopHoldOnHeldKey)
        {
            _pendingTimedStop.Stop();
            _downHold = false;
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

    // Hold trigger taps -------------------------------------------------------------------------------------------

    private void HandleTriggerDown()
    {
        // Taps only group into doubles and triples when timed holds are on; otherwise every press is a single tap.
        long now = Environment.TickCount64;
        _tapCount = _settings.HoldTimedEnabled && now - _lastTapTick <= TapWindowMs ? _tapCount + 1 : 1;
        _lastTapTick = now;

        if (_tapCount == 1) HandleFirstTap();
        else HandleMoreTaps();
    }

    private void HandleFirstTap()
    {
        _timedBeforeTap = _holder.IsTimed;

        if (_holder.IsTimed && _settings.ExtendOnTap && _settings.HoldTimedEnabled)
        {
            // A second tap inside the window adds time. If none comes, the tap meant "stop".
            _pendingTimedStop.Stop();
            _pendingTimedStop.Start();
            return;
        }

        if (_holder.IsHolding)
        {
            _downHold = false;
            _holder.Release();
            return;
        }

        switch (_settings.SingleTap)
        {
            case SingleTapBehavior.Toggle:
                _holder.Press();
                break;

            case SingleTapBehavior.WhileDown:
                _holder.Press();
                _downHold = true;
                if (_settings.MaxHoldSeconds > 0) _holder.StartTimed(_settings.MaxHoldSeconds);
                break;
        }
    }

    private void HandleMoreTaps()
    {
        _pendingTimedStop.Stop();

        if (_timedBeforeTap && _settings.ExtendOnTap)
        {
            _holder.Extend(_settings.ExtendSeconds, MaxTimedSeconds);
            ShowToast($"+{_settings.ExtendSeconds} s");
            return;
        }

        _downHold = false;
        int seconds = _tapCount >= 3 ? _settings.TripleTapSeconds : _settings.DoubleTapSeconds;
        _holder.StartTimed(seconds);
        _shownRemaining = -1;
    }

    // Profiles ----------------------------------------------------------------------------------------------------

    private void RefreshProfileCombo()
    {
        bool wasReady = _ready;
        _ready = false;

        ProfileCombo.Items.Clear();
        foreach (Profile profile in _settings.Profiles) ProfileCombo.Items.Add(profile.Name);
        ProfileCombo.SelectedItem = _settings.ActiveProfile;
        ProfileNameBox.Text = _settings.ActiveProfile ?? "";

        _ready = wasReady;
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ProfileCombo.SelectedItem is not string name) return;
        ApplyProfileByName(name);
    }

    private void OnProfileSave(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameBox.Text.Trim();
        if (name.Length == 0) name = $"Profile {_settings.Profiles.Count + 1}";

        PushSettings();
        PushHoldSettings();
        AppSettings snapshot = _settings.Snapshot();

        Profile? existing = _settings.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _settings.Profiles.Add(new Profile { Name = name, Data = snapshot });
        }
        else
        {
            name = existing.Name;
            existing.Data = snapshot;
        }

        _settings.ActiveProfile = name;
        _settings.Save();
        RefreshProfileCombo();
        ShowToast($"Saved profile {name}");
    }

    private void OnProfileDelete(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;

        _settings.Profiles.RemoveAll(p => p.Name == name);
        _settings.ActiveProfile = null;
        _settings.Save();
        RefreshProfileCombo();
        ShowToast($"Deleted profile {name}");
    }

    private void NextProfile()
    {
        if (_settings.Profiles.Count == 0)
        {
            ShowToast("No profiles saved");
            return;
        }

        int index = _settings.Profiles.FindIndex(p => p.Name == _settings.ActiveProfile);
        ApplyProfileByName(_settings.Profiles[(index + 1) % _settings.Profiles.Count].Name);
    }

    private void ApplyProfileByName(string name)
    {
        Profile? profile = _settings.Profiles.FirstOrDefault(p => p.Name == name);
        if (profile is null) return;

        // Stop whatever the old profile was doing so its keys and timers do not carry over.
        _engine.Stop();
        _pendingTimedStop.Stop();
        _downHold = false;
        _holder.Release();

        _settings.ApplyProfile(profile.Data);
        _settings.ActiveProfile = name;

        LoadUi();
        ApplyHoldConfig();
        PushSettings();
        PushHoldSettings();
        UpdateStatus();
        ShowToast($"Profile: {name}");
    }

    // Startup and tray --------------------------------------------------------------------------------------------

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.StartMinimized = StartMinimizedCheck.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _settings.Save();

        ApplyStartup();
        UpdateTrayVisibility();
    }

    private void ApplyStartup()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;

            if (_settings.StartWithWindows)
            {
                string command = $"\"{Environment.ProcessPath}\"" + (_settings.StartMinimized ? " --minimized" : "");
                key.SetValue("MouseClicker", command);
            }
            else
            {
                key.DeleteValue("MouseClicker", throwOnMissingValue: false);
            }
        }
        catch (Exception)
        {
            // Autostart is a convenience; the app still works if the registry is locked down.
        }
    }

    private void EnsureTray()
    {
        if (_tray is not null) return;

        System.Drawing.Icon icon;
        try
        {
            icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        }
        catch (Exception)
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => Close());

        _tray = new System.Windows.Forms.NotifyIcon { Icon = icon, Text = "Mouse Clicker", ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void UpdateTrayVisibility()
    {
        if (_settings.MinimizeToTray)
        {
            EnsureTray();
            _tray!.Visible = true;
        }
        else if (_tray is not null)
        {
            _tray.Visible = false;
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray) Hide();
    }

    private void ShowStartNotification()
    {
        if (!_settings.NotifyOnStart) return;

        EnsureTray();
        // The tray icon only needs to exist for the balloon; hide it again once the notification is dismissed.
        _tray!.BalloonTipClosed -= OnBalloonClosed;
        _tray.BalloonTipClosed += OnBalloonClosed;
        _tray.Visible = true;
        _tray.ShowBalloonTip(3000, "Mouse Clicker", $"Clicking every {_settings.IntervalMs} ms. Total clicks: {_engine.ClickCount:N0}", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void OnBalloonClosed(object? sender, EventArgs e)
    {
        if (_tray is not null && !_settings.MinimizeToTray) _tray.Visible = false;
    }

    // Display -----------------------------------------------------------------------------------------------------

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static string KeyName(int vk) => KeyInterop.KeyFromVirtualKey(vk).ToString();

    private void UpdateHotkeyText() => HotkeyButton.Content = KeyName(_settings.HotkeyVk);

    private void UpdateHoldTriggerText() => HoldTriggerButton.Content = KeyName(_settings.HoldTriggerVk);

    private void UpdateHeldKeyText() => HeldKeyButton.Content = KeyName(_settings.HeldKeyVk);

    private void UpdateProfileKeyText() => ProfileKeyButton.Content = KeyName(_settings.ProfileNextVk);

    private string HeldDescription() => _settings.HoldTargetKind switch
    {
        HoldTarget.Key => $"{KeyName(_settings.HeldKeyVk)} key",
        HoldTarget.Mouse => $"{_settings.HeldMouseButton} mouse button",
        _ => $"{KeyName(_settings.HeldKeyVk)} key and {_settings.HeldMouseButton} mouse button",
    };

    private void UpdateHoldStatus()
    {
        HoldStatusText.Text = _holder.IsHolding
            ? $"Holding {HeldDescription()}" + (_holder.IsTimed ? $", {_holder.RemainingWholeSeconds} s left" : "")
            : "Not holding";
        UpdateActivityStatus();
    }

    private void UpdateActivityStatus()
    {
        bool clicking = _engine.IsRunning;
        bool holding = _holder.IsHolding;
        string holdLabel = _holder.IsTimed ? $"Hold {_holder.RemainingWholeSeconds} s" : "Hold active";

        ActivityText.Text = (clicking, holding) switch
        {
            (true, true) => $"Mouse clicker + {holdLabel}",
            (true, false) => "Mouse clicker active",
            (false, true) => holdLabel,
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
        _pendingTimedStop.Stop();
        _toastTimer.Stop();
        _captureTimer.Stop();
        if (_tray is not null)
        {
            // Hide the icon first so it does not linger in the tray after exit.
            _tray.Visible = false;
            _tray.Dispose();
        }

        _hook.KeyDown -= OnGlobalKeyDown;
        _hook.KeyUp -= OnGlobalKeyUp;
        _hook.Dispose();
        _engine.Dispose();
        // Never leave the held key or button stuck down after the app exits.
        _holder.Dispose();
    }
}
