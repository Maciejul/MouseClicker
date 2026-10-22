using Avalonia;

namespace MouseClicker;

internal static class Program
{
    private const string InstanceName = @"Local\MouseClicker-7f3c2d1e";
    private const string ShowEventName = @"Local\MouseClicker-7f3c2d1e-show";

    [STAThread]
    private static int Main(string[] args)
    {
        // Two copies would each install a keyboard hook and each send clicks, so a second launch just wakes the first.
        using var mutex = new Mutex(initiallyOwned: true, InstanceName, out bool firstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!firstInstance)
        {
            showEvent.Set();
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            App.ReleaseHeldInput();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        App.ShowSignal = showEvent;
        App.StartMinimizedArg = args.Contains("--minimized");
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
