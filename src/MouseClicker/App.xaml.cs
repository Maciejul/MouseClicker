using System.Windows;

namespace MouseClicker;

public partial class App : System.Windows.Application
{
    private const string InstanceName = @"Local\MouseClicker-7f3c2d1e";
    private const string ShowEventName = @"Local\MouseClicker-7f3c2d1e-show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two copies would each install a keyboard hook and each send clicks, so a second launch just wakes the first.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool firstInstance);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!firstInstance)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            ReleaseHeldInput();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);
            ReleaseHeldInput();
        };
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Error("Unobserved task exception", args.Exception);

        var listener = new Thread(WaitForShowRequest) { IsBackground = true, Name = "InstanceListener" };
        listener.Start();

        base.OnStartup(e);
    }

    private void WaitForShowRequest()
    {
        while (_showEvent!.WaitOne())
        {
            Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.RestoreFromTray());
        }
    }

    // A crash must not leave a key or mouse button stuck down.
    private void ReleaseHeldInput()
    {
        try
        {
            (MainWindow as MainWindow)?.EmergencyRelease();
        }
        catch (Exception)
        {
            // Nothing more can be done here.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
