using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace MouseClicker;

/// <summary>The Avalonia application: owns the theme (light/dark) and the main window.</summary>
internal sealed class App : Application
{
    // Set by Program before the UI starts.
    internal static EventWaitHandle? ShowSignal { get; set; }
    internal static bool StartMinimizedArg { get; set; }

    private static MainWindow? _main;
    private AppSettings? _settings;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _settings = AppSettings.Load();
        RequestedThemeVariant = _settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // A crash must not leave a key or mouse button stuck down. The exception still ends the app.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            ReleaseHeldInput();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _main = new MainWindow(_settings ?? AppSettings.Load(), StartMinimizedArg);
            desktop.MainWindow = _main;
            StartShowListener();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Switches every window, the toast and the tray menu between the light and dark palette at once.</summary>
    internal static void SetDark(bool on)
    {
        if (Current != null) Current.RequestedThemeVariant = on ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void StartShowListener()
    {
        EventWaitHandle? signal = ShowSignal;
        if (signal is null) return;

        var listener = new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.UIThread.Post(() => _main?.RestoreFromTray());
        })
        { IsBackground = true, Name = "InstanceListener" };
        listener.Start();
    }

    internal static void ReleaseHeldInput()
    {
        try
        {
            _main?.EmergencyRelease();
        }
        catch (Exception)
        {
            // Nothing more can be done here.
        }
    }
}
