using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Win32;

namespace MouseClicker;

internal sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ClickEngine _engine = new();
    private readonly HoldEngine _holder = new();
    private readonly KeyboardHook _hook = new();
    private readonly HoldTriggerController _trigger;
    private readonly TrayController _tray;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _startButtonLock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _focusWatch = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _repressTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _swallowTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IntPtr _lastForeground;

    private bool _ready;                  // Guards handlers that fire while the controls are being filled in.
    private CaptureTarget _capture;       // Which key button is waiting for a new key, if any.
    private bool _hotkeyDown;             // Suppresses auto-repeat while the hotkey stays held.
    private bool _holdTriggerDown;        // Suppresses auto-repeat while the hold trigger stays held.
    private bool _profileKeyDown;         // Suppresses auto-repeat while the profile key stays held.
    private bool _syncingInterval;        // Stops the slider and the number box from updating each other in a loop.
    private bool _sanitizing;             // Stops the digits-only filter from re-entering itself.
    private bool _swallowKeys;            // Keeps the key that just set a hotkey from also pressing the focused button.
    private readonly bool _launchedMinimized;
    private int _captureCountdown;

    private int _shownRemaining = -1;     // Whole seconds last drawn for a timed hold, so the text only redraws on change.

    private const int MaxIntervalMs = 1000;
    private const int MaxTimedSeconds = HoldTriggerController.MaxTimedSeconds;

    private readonly Bitmap _idleImage = LoadBitmap("app.png");
    private readonly Bitmap _activeImage = LoadBitmap("app-active.png");
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _activeIcon;
    private readonly TextBox[] _numberBoxes;

    private enum CaptureTarget
    {
        None,
        Hotkey,
        HoldTrigger,
        HeldKey,
        ProfileKey,
    }

    // The designer and the XAML loader need a parameterless constructor.
    public MainWindow() : this(AppSettings.Load(), false)
    {
    }

    public MainWindow(AppSettings settings, bool startMinimized)
    {
        _settings = settings;
        _trigger = new HoldTriggerController(_holder, () => _settings, HeldDescription);
        InitializeComponent();

        _idleIcon = LoadIcon("app.png");
        _activeIcon = LoadIcon("app-active.png");
        _tray = new TrayController();
        _numberBoxes = new[] { IntervalBox, StopClicksBox, StopMinutesBox, MaxHoldBox, DoubleTapBox, TripleTapBox, ExtendBox };

        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        Title = $"Mouse Clicker v{version}";
        VersionLabel.Text = $"v{version}";

        WireEvents();
        LoadUi();
        ApplyHoldConfig();
        UpdateHoldStatus();

        _hook.KeyDown += OnGlobalKeyDown;
        _hook.KeyUp += OnGlobalKeyUp;
        bool hookInstalled = _hook.Install();

        _ready = true;
        PushSettings();
        PushHoldSettings();
        UpdateStatus();
        UpdateTrayVisibility();

        if (!hookInstalled)
        {
            ShowToast("Hotkeys unavailable: Windows refused the keyboard hook");
        }

        _saveTimer.Tick += (_, _) => FlushSave();

        _tray.OpenRequested += RestoreFromTray;
        _tray.ExitRequested += Close;

        _trigger.Message += ShowToast;
        _trigger.HoldChanged += () =>
        {
            _shownRemaining = -1;
            UpdateHoldStatus();
        };

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

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

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBorder.IsVisible = false;
        };

        _swallowTimer.Tick += (_, _) =>
        {
            _swallowTimer.Stop();
            _swallowKeys = false;
        };

        _captureTimer.Tick += OnCaptureTick;

        _engine.Stopped += reason => Dispatcher.UIThread.Post(() =>
        {
            UpdateStatus();
            ShowToast(reason);
        });

        _launchedMinimized = startMinimized;
        Opened += (_, _) =>
        {
            if (_launchedMinimized && _settings.MinimizeToTray)
            {
                Dispatcher.UIThread.Post(Hide);
            }
        };
        Closed += OnClosed;
    }

    private static Bitmap LoadBitmap(string name)
    {
        using Stream stream = AssetLoader.Open(new Uri($"avares://MouseClicker/Assets/{name}"));
        return new Bitmap(stream);
    }

    private static WindowIcon LoadIcon(string name)
    {
        using Stream stream = AssetLoader.Open(new Uri($"avares://MouseClicker/Assets/{name}"));
        return new WindowIcon(stream);
    }

    private PathIcon MakeIcon(string key)
    {
        var icon = new PathIcon();
        if (this.TryFindResource(key, ActualThemeVariant, out object? geometry) && geometry is Geometry data) icon.Data = data;
        return icon;
    }

    private void WireEvents()
    {
        IntervalSlider.ValueChanged += OnIntervalSliderChanged;
        IntervalBox.TextChanged += OnIntervalChanged;
        JitterSlider.ValueChanged += OnJitterChanged;
        ToggleRadio.IsCheckedChanged += OnModeChanged;
        HoldRadio.IsCheckedChanged += OnModeChanged;
        NotifySwitch.IsCheckedChanged += OnClickOptionsChanged;
        HotkeyButton.Click += (_, _) => StartCapture(CaptureTarget.Hotkey, HotkeyButton);
        StartStopButton.Click += OnStartStopClick;

        ClickButtonCombo.SelectionChanged += OnClickOptionsChanged;
        DoubleClickSwitch.IsCheckedChanged += OnClickOptionsChanged;
        ClickTargetCombo.SelectionChanged += OnClickTargetChanged;
        CaptureButton.Click += OnCaptureClick;
        StopClicksBox.TextChanged += OnClickNumberChanged;
        StopMinutesBox.TextChanged += OnClickNumberChanged;

        HoldEnabledSwitch.IsCheckedChanged += OnHoldEnabledChanged;
        StopOnHeldKeySwitch.IsCheckedChanged += OnStopOnHeldKeyChanged;
        HoldTargetCombo.SelectionChanged += OnHoldOptionsChanged;
        HoldTriggerButton.Click += (_, _) => StartCapture(CaptureTarget.HoldTrigger, HoldTriggerButton);
        HeldKeyButton.Click += (_, _) => StartCapture(CaptureTarget.HeldKey, HeldKeyButton);
        HeldMouseCombo.SelectionChanged += OnHoldOptionsChanged;
        TapToggleRadio.IsCheckedChanged += OnHoldOptionsChanged;
        TapWhileRadio.IsCheckedChanged += OnHoldOptionsChanged;
        TapNoneRadio.IsCheckedChanged += OnHoldOptionsChanged;
        MaxHoldBox.TextChanged += OnHoldNumberChanged;
        HoldTimedSwitch.IsCheckedChanged += OnHoldOptionsChanged;
        DoubleTapBox.TextChanged += OnHoldNumberChanged;
        TripleTapBox.TextChanged += OnHoldNumberChanged;
        ExtendSwitch.IsCheckedChanged += OnHoldOptionsChanged;
        ExtendBox.TextChanged += OnHoldNumberChanged;

        ProfileCombo.SelectionChanged += OnProfileSelected;
        ProfileSaveButton.Click += OnProfileSave;
        ProfileDeleteButton.Click += OnProfileDelete;
        ProfileKeyButton.Click += (_, _) => StartCapture(CaptureTarget.ProfileKey, ProfileKeyButton);

        DarkModeSwitch.IsCheckedChanged += OnDarkModeChanged;
        StartWithWindowsSwitch.IsCheckedChanged += OnStartupChanged;
        StartMinimizedSwitch.IsCheckedChanged += OnStartupChanged;
        MinimizeToTraySwitch.IsCheckedChanged += OnStartupChanged;

        foreach (TextBox box in _numberBoxes) box.LostFocus += OnNumberBoxLostFocus;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
    }

    // Fills every control from the settings. Used at startup and after a profile is applied.
    private void LoadUi()
    {
        bool wasReady = _ready;
        _ready = false;

        IntervalBox.Text = Math.Clamp(_settings.IntervalMs, 10, MaxIntervalMs).ToString();
        IntervalSlider.Value = Math.Clamp(_settings.IntervalMs, 10, 500);
        JitterSlider.Value = _settings.JitterPercent;
        JitterCard.Description = $"Random jitter: {_settings.JitterPercent}%";
        NotifySwitch.IsChecked = _settings.NotifyOnStart;
        ToggleRadio.IsChecked = _settings.Mode == TriggerMode.Toggle;
        HoldRadio.IsChecked = _settings.Mode == TriggerMode.Hold;

        ClickButtonCombo.SelectedIndex = (int)_settings.ClickButton;
        DoubleClickSwitch.IsChecked = _settings.DoubleClick;
        ClickTargetCombo.SelectedIndex = (int)_settings.ClickTarget;
        StopClicksBox.Text = _settings.StopAfterClicks.ToString();
        StopMinutesBox.Text = _settings.StopAfterMinutes.ToString();
        UpdateTargetText();

        HoldEnabledSwitch.IsChecked = _settings.HoldEnabled;
        StopOnHeldKeySwitch.IsChecked = _settings.StopHoldOnHeldKey;
        TapToggleRadio.IsChecked = _settings.SingleTap == SingleTapBehavior.Toggle;
        TapWhileRadio.IsChecked = _settings.SingleTap == SingleTapBehavior.WhileDown;
        TapNoneRadio.IsChecked = _settings.SingleTap == SingleTapBehavior.Nothing;
        MaxHoldBox.Text = _settings.MaxHoldSeconds.ToString();
        HoldTimedSwitch.IsChecked = _settings.HoldTimedEnabled;
        DoubleTapBox.Text = _settings.DoubleTapSeconds.ToString();
        TripleTapBox.Text = _settings.TripleTapSeconds.ToString();
        ExtendSwitch.IsChecked = _settings.ExtendOnTap;
        ExtendBox.Text = _settings.ExtendSeconds.ToString();
        HoldTargetCombo.SelectedIndex = (int)_settings.HoldTargetKind;
        HeldMouseCombo.SelectedIndex = (int)_settings.HeldMouseButton;

        DarkModeSwitch.IsChecked = _settings.DarkMode;
        StartWithWindowsSwitch.IsChecked = _settings.StartWithWindows;
        StartMinimizedSwitch.IsChecked = _settings.StartMinimized;
        MinimizeToTraySwitch.IsChecked = _settings.MinimizeToTray;

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

        _trigger.Poll();
        if (_holder.IsTimed)
        {
            int remaining = _holder.RemainingWholeSeconds;
            if (remaining != _shownRemaining)
            {
                _shownRemaining = remaining;
                UpdateHoldStatus();
            }
        }
    }

    private static int ReadInt(TextBox box, int min, int max, int current)
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
        _settings.NotifyOnStart = NotifySwitch.IsChecked == true;

        if (ClickButtonCombo.SelectedIndex >= 0) _settings.ClickButton = (ClickButton)ClickButtonCombo.SelectedIndex;
        _settings.DoubleClick = DoubleClickSwitch.IsChecked == true;
        _settings.StopAfterClicks = ReadInt(StopClicksBox, 0, 1_000_000_000, _settings.StopAfterClicks);
        _settings.StopAfterMinutes = ReadInt(StopMinutesBox, 0, 100_000, _settings.StopAfterMinutes);

        _engine.Configure(_settings.IntervalMs, _settings.JitterPercent, ClickOptions.From(_settings));
        QueueSave();
    }

    private void PushHoldSettings()
    {
        if (!_ready) return;

        _settings.SingleTap = TapWhileRadio.IsChecked == true ? SingleTapBehavior.WhileDown
            : TapNoneRadio.IsChecked == true ? SingleTapBehavior.Nothing
            : SingleTapBehavior.Toggle;
        _settings.MaxHoldSeconds = ReadInt(MaxHoldBox, 0, MaxTimedSeconds, _settings.MaxHoldSeconds);
        _settings.HoldTimedEnabled = HoldTimedSwitch.IsChecked == true;
        _settings.DoubleTapSeconds = ReadInt(DoubleTapBox, 1, MaxTimedSeconds, _settings.DoubleTapSeconds);
        _settings.TripleTapSeconds = ReadInt(TripleTapBox, 1, MaxTimedSeconds, _settings.TripleTapSeconds);
        _settings.ExtendOnTap = ExtendSwitch.IsChecked == true;
        _settings.ExtendSeconds = ReadInt(ExtendBox, 1, MaxTimedSeconds, _settings.ExtendSeconds);
        if (HoldTargetCombo.SelectedIndex >= 0) _settings.HoldTargetKind = (HoldTarget)HoldTargetCombo.SelectedIndex;
        if (HeldMouseCombo.SelectedIndex >= 0) _settings.HeldMouseButton = (ClickButton)HeldMouseCombo.SelectedIndex;

        // Show an option's details only while the option is in use.
        // The key and the mouse button each have their own labelled control under the trigger key.
        HeldKeyCard.IsVisible = _settings.HoldTargetKind != HoldTarget.Mouse;
        HeldMouseCard.IsVisible = _settings.HoldTargetKind != HoldTarget.Key;
        MaxHoldCard.IsVisible = _settings.SingleTap == SingleTapBehavior.WhileDown;
        TimedPanel.IsVisible = _settings.HoldTimedEnabled;
        ExtendRow.IsVisible = _settings.ExtendOnTap;
        ApplyHoldConfig();
        QueueSave();
        UpdateHoldStatus();
    }

    private void ApplyHoldConfig()
    {
        _holder.Configure(_settings.HeldKeyVk, _settings.HoldTargetKind, _settings.HeldMouseButton);
        _trigger.Sync();
    }

    // Number boxes take digits only: anything else (typed or pasted) is dropped as it arrives.
    private void SanitizeDigits(TextBox box)
    {
        string text = box.Text ?? "";
        if (text.All(char.IsAsciiDigit)) return;

        _sanitizing = true;
        int caret = box.CaretIndex;
        string cleaned = new(text.Where(char.IsAsciiDigit).ToArray());
        box.Text = cleaned;
        box.CaretIndex = Math.Min(caret, cleaned.Length);
        _sanitizing = false;
    }

    private void OnIntervalChanged(object? sender, TextChangedEventArgs e)
    {
        if (_sanitizing) return;
        SanitizeDigits(IntervalBox);
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

    private void OnIntervalSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _syncingInterval) return;

        _syncingInterval = true;
        IntervalBox.Text = ((int)e.NewValue).ToString();
        _syncingInterval = false;

        PushSettings();
    }

    private void OnClickOptionsChanged(object? sender, RoutedEventArgs e) => PushSettings();

    private void OnClickNumberChanged(object? sender, TextChangedEventArgs e)
    {
        if (_sanitizing) return;
        if (sender is TextBox box) SanitizeDigits(box);
        PushSettings();
    }

    private void OnHoldOptionsChanged(object? sender, RoutedEventArgs e) => PushHoldSettings();

    private void OnHoldNumberChanged(object? sender, TextChangedEventArgs e)
    {
        if (_sanitizing) return;
        if (sender is TextBox box) SanitizeDigits(box);
        PushHoldSettings();
    }

    private void OnJitterChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        JitterCard.Description = $"Random jitter: {(int)e.NewValue}%";
        PushSettings();
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { IsChecked: true }) return;
        _engine.Stop();
        _hotkeyDown = false;
        PushSettings();
        UpdateStatus();
    }

    private void OnStartStopClick(object? sender, RoutedEventArgs e)
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

    private void OnStopOnHeldKeyChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.StopHoldOnHeldKey = StopOnHeldKeySwitch.IsChecked == true;
        QueueSave();
    }

    private void OnHoldEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.HoldEnabled = HoldEnabledSwitch.IsChecked == true;
        if (!_settings.HoldEnabled)
        {
            _trigger.Cancel();
            _holder.Release();
        }

        QueueSave();
        UpdateHoldStatus();
    }

    // Click target ------------------------------------------------------------------------------------------------

    private void OnClickTargetChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        if (ClickTargetCombo.SelectedIndex >= 0) _settings.ClickTarget = (ClickTargetKind)ClickTargetCombo.SelectedIndex;
        UpdateTargetText();
        PushSettings();
    }

    private void OnCaptureClick(object? sender, RoutedEventArgs e)
    {
        // The user needs a moment to move the cursor over the spot, so wait a few seconds before reading it.
        _captureCountdown = 3;
        CaptureButton.IsEnabled = false;
        CaptureText.Text = $"Move the cursor... {_captureCountdown}";
        _captureTimer.Start();
    }

    private void OnCaptureTick(object? sender, EventArgs e)
    {
        _captureCountdown--;
        if (_captureCountdown > 0)
        {
            CaptureText.Text = $"Move the cursor... {_captureCountdown}";
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
        else if (picked.Elevated && !IsAppElevated())
        {
            ShowToast("That window runs as administrator; run Mouse Clicker as administrator to click in it");
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
        CaptureText.Text = "Capture (3 s)";
        CaptureButton.IsEnabled = _settings.ClickTarget != ClickTargetKind.Cursor;
        CaptureButton.IsVisible = _settings.ClickTarget != ClickTargetKind.Cursor;

        TargetCard.Description = _settings.ClickTarget switch
        {
            ClickTargetKind.Point => $"Point {_settings.PointX}, {_settings.PointY}. Capture reads the cursor after 3 s.",
            ClickTargetKind.Window => _settings.WindowProcess is null
                ? "No window captured yet. Capture reads the window under the cursor after 3 s."
                : $"{_settings.WindowProcess}, +{_settings.WindowOffsetX}, +{_settings.WindowOffsetY}. Follows the window if it moves.",
            _ => "Clicks wherever the cursor is.",
        };
    }

    // Keys --------------------------------------------------------------------------------------------------------

    private void StartCapture(CaptureTarget target, Button button)
    {
        _capture = target;
        button.Content = "Press a key...";
    }

    // The captured key comes from the global hook, which sees the real virtual-key code. This swallows the same key press
    // inside the window, so a Space or Enter that sets a hotkey does not also press the focused button.
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_swallowKeys || _capture != CaptureTarget.None) e.Handled = true;
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_swallowKeys || _capture != CaptureTarget.None) e.Handled = true;
    }

    private void FinishCapture(int vk)
    {
        CaptureTarget target = _capture;
        _capture = CaptureTarget.None;

        // 0x1B is Escape: it cancels.
        if (vk != 0x1B && !IsKeyInUse(vk, except: target))
        {
            // The keys share one global hook, so one key cannot drive two actions.
            SetKeyForTarget(target, vk);
            QueueSave();
        }

        _swallowKeys = true;
        _swallowTimer.Stop();
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
        if (_capture != CaptureTarget.None)
        {
            if (!injected) FinishCapture(vk);
            return;
        }

        if (!injected && vk == _settings.HeldKeyVk && _settings.StopHoldOnHeldKey)
        {
            _trigger.Cancel();
            _holder.Release();
            UpdateHoldStatus();
        }

        if (vk == _settings.HoldTriggerVk && _settings.HoldEnabled)
        {
            if (_holdTriggerDown) return;
            _holdTriggerDown = true;

            _trigger.TriggerDown();
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
        if (_swallowKeys)
        {
            // Give the window a moment to see this key's release before it may act on keys again.
            _swallowTimer.Stop();
            _swallowTimer.Start();
        }

        if (vk == _settings.HoldTriggerVk)
        {
            _holdTriggerDown = false;
            _trigger.TriggerUp();
            return;
        }

        if (vk == _settings.ProfileNextVk)
        {
            _profileKeyDown = false;
            return;
        }

        if (!injected && vk == _settings.HeldKeyVk && _settings.StopHoldOnHeldKey)
        {
            _trigger.Cancel();
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

    private void OnProfileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ProfileCombo.SelectedItem is not string name) return;
        ApplyProfileByName(name);
    }

    private void OnProfileSave(object? sender, RoutedEventArgs e)
    {
        PushSettings();
        PushHoldSettings();

        string name = _settings.SaveProfile(ProfileNameBox.Text);
        QueueSave();
        RefreshProfileCombo();
        ShowToast($"Saved profile {name}");
    }

    private void OnProfileDelete(object? sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        if (!_settings.DeleteProfile(name)) return;

        QueueSave();
        RefreshProfileCombo();
        ShowToast($"Deleted profile {name}");
    }

    private void NextProfile()
    {
        Profile? next = _settings.NextProfile();
        if (next is null)
        {
            ShowToast("No profiles saved");
            return;
        }

        ApplyProfileByName(next.Name);
    }

    private void ApplyProfileByName(string name)
    {
        Profile? profile = _settings.FindProfile(name);
        if (profile is null) return;

        // Stop whatever the old profile was doing so its keys and timers do not carry over.
        _engine.Stop();
        _trigger.Cancel();
        _holder.Release();

        _settings.ApplyProfile(profile.Data);
        _settings.ActiveProfile = profile.Name;

        LoadUi();
        ApplyHoldConfig();
        PushSettings();
        PushHoldSettings();
        UpdateStatus();
        ShowToast($"Profile: {profile.Name}");
    }

    // Startup and tray --------------------------------------------------------------------------------------------

    private void OnDarkModeChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.DarkMode = DarkModeSwitch.IsChecked == true;
        QueueSave();
        App.SetDark(_settings.DarkMode);
    }

    private void OnStartupChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _settings.StartWithWindows = StartWithWindowsSwitch.IsChecked == true;
        _settings.StartMinimized = StartMinimizedSwitch.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTraySwitch.IsChecked == true;
        QueueSave();

        StartupRegistration.Apply(_settings.StartWithWindows, _settings.StartMinimized);
        UpdateTrayVisibility();
    }

    private void UpdateTrayVisibility() => _tray.SetPermanent(_settings.MinimizeToTray);

    internal void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized && _settings.MinimizeToTray) Hide();
    }

    private void ShowStartNotification()
    {
        if (!_settings.NotifyOnStart) return;

        // A click-through notice at the top of the screen; it shows over a game even while this window is hidden.
        OverlayToast.ShowMessage("Mouse Clicker", $"Clicking every {_settings.IntervalMs} ms. Total clicks: {_engine.ClickCount:N0}");
    }

    // Safety ------------------------------------------------------------------------------------------------------

    /// <summary>Stops clicking and lets go of anything held. Used when the app crashes and when the user leaves the session.</summary>
    internal void EmergencyRelease()
    {
        _engine.Stop();
        _trigger.Cancel();
        _holder.Release();
    }

    // Both of these arrive on another thread.
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Dispatcher.UIThread.Post(StopEverything);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Dispatcher.UIThread.Post(StopEverything);
    }

    private void StopEverything()
    {
        EmergencyRelease();
        UpdateStatus();
        UpdateHoldStatus();
    }

    private static bool IsAppElevated() =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    // Saving ------------------------------------------------------------------------------------------------------
    // Settings change on every keystroke and slider tick, and the keyboard hook shares this thread,
    // so writes wait until the changes stop.

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        _saveTimer.Stop();
        _settings.Save();
    }

    // Number boxes ------------------------------------------------------------------------------------------------

    private void OnNumberBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        // A box can hold a value the app has clamped or ignored (an empty box, 5 ms in a 10 ms minimum).
        // Show the value that is actually in use.
        bool wasReady = _ready;
        _ready = false;
        IntervalBox.Text = Math.Clamp(_settings.IntervalMs, 10, MaxIntervalMs).ToString();
        StopClicksBox.Text = _settings.StopAfterClicks.ToString();
        StopMinutesBox.Text = _settings.StopAfterMinutes.ToString();
        MaxHoldBox.Text = _settings.MaxHoldSeconds.ToString();
        DoubleTapBox.Text = _settings.DoubleTapSeconds.ToString();
        TripleTapBox.Text = _settings.TripleTapSeconds.ToString();
        ExtendBox.Text = _settings.ExtendSeconds.ToString();
        _ready = wasReady;
    }

    // Display -----------------------------------------------------------------------------------------------------

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastBorder.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void UpdateHotkeyText() => HotkeyButton.Content = KeyNames.Name(_settings.HotkeyVk);

    private void UpdateHoldTriggerText() => HoldTriggerButton.Content = KeyNames.Name(_settings.HoldTriggerVk);

    private void UpdateHeldKeyText() => HeldKeyButton.Content = KeyNames.Name(_settings.HeldKeyVk);

    private void UpdateProfileKeyText() => ProfileKeyButton.Content = KeyNames.Name(_settings.ProfileNextVk);

    private string HeldDescription() => _settings.HoldTargetKind switch
    {
        HoldTarget.Key => $"{KeyNames.Name(_settings.HeldKeyVk)} key",
        HoldTarget.Mouse => $"{_settings.HeldMouseButton} mouse button",
        _ => $"{KeyNames.Name(_settings.HeldKeyVk)} key and {_settings.HeldMouseButton} mouse button",
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
        ActivityDot[!Shape.FillProperty] = this.GetResourceObservable(active ? "Good" : "SystemControlForegroundBaseMediumBrush").ToBinding();
        ActivityText.Classes.Set("muted", !active);

        // The title bar, taskbar, header and tray show the green icon while something is running.
        Icon = active ? _activeIcon : _idleIcon;
        HeaderIcon.Source = active ? _activeImage : _idleImage;
        _tray.SetActive(active);
    }

    private void OnStartButtonLockElapsed(object? sender, EventArgs e)
    {
        _startButtonLock.Stop();
        StartStopButton.IsEnabled = true;
    }

    private void UpdateStatus()
    {
        bool running = _engine.IsRunning;
        var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(MakeIcon(running ? "IconStop" : "IconPlay"));
        content.Children.Add(new TextBlock { Text = running ? "Stop" : "Start" });
        StartStopButton.Content = content;
        StartStopButton.Classes.Set("accent", !running);
        StartStopButton.Classes.Set("danger", running);
        UpdateActivityStatus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _statsTimer.Stop();
        _startButtonLock.Stop();
        _focusWatch.Stop();
        _repressTimer.Stop();
        _toastTimer.Stop();
        _captureTimer.Stop();
        _swallowTimer.Stop();
        FlushSave();
        _tray.Dispose();

        _hook.KeyDown -= OnGlobalKeyDown;
        _hook.KeyUp -= OnGlobalKeyUp;
        _hook.Dispose();
        _engine.Dispose();
        // Never leave the held key or button stuck down after the app exits.
        _holder.Dispose();
    }
}
