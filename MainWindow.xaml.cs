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
    private readonly KeyboardHook _hook = new();
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private System.Windows.Forms.NotifyIcon? _tray;

    private bool _ready;          // Guards handlers that fire while InitializeComponent runs.
    private bool _capturing;      // True while waiting for a new hotkey.
    private bool _hotkeyDown;     // Suppresses auto-repeat while the hotkey stays held.

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

        _hook.KeyDown += OnGlobalKeyDown;
        _hook.KeyUp += OnGlobalKeyUp;
        _hook.Install();

        _ready = true;
        PushSettings();
        UpdateStatus();

        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();
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

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        HotkeyButton.Content = "Press a key...";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        _capturing = false;
        if (key == Key.Escape || key == Key.None)
        {
            UpdateHotkeyText();
            return;
        }

        _settings.HotkeyVk = KeyInterop.VirtualKeyFromKey(key);
        UpdateHotkeyText();
        _settings.Save();
    }

    // Global hotkey handling. These run for every key press on the system, including while this window is not focused.
    private void OnGlobalKeyDown(int vk)
    {
        if (_capturing || vk != _settings.HotkeyVk || _hotkeyDown) return;
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

    private void UpdateStatus()
    {
        bool running = _engine.IsRunning;
        StatusText.Text = running ? "Clicking..." : "Stopped";
        StartStopButton.Content = running ? "Stop" : "Start";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statsTimer.Stop();
        _tray?.Dispose();
        _hook.KeyDown -= OnGlobalKeyDown;
        _hook.KeyUp -= OnGlobalKeyUp;
        _hook.Dispose();
        _engine.Dispose();
    }
}
