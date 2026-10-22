using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace MouseClicker;

/// <summary>The notification-area icon and its menu. Visible only while "Minimize to the tray" is on.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly WindowIcon _idle;
    private readonly WindowIcon _active;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayController()
    {
        _idle = Load("app.png");
        _active = Load("app-active.png");

        var menu = new NativeMenu();
        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => OpenRequested?.Invoke();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Add(open);
        menu.Add(exit);

        _icon = new TrayIcon { Icon = _idle, ToolTipText = "Mouse Clicker", Menu = menu, IsVisible = false };
        _icon.Clicked += (_, _) => OpenRequested?.Invoke();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
    }

    private static WindowIcon Load(string name)
    {
        using Stream stream = AssetLoader.Open(new Uri($"avares://MouseClicker/Assets/{name}"));
        return new WindowIcon(stream);
    }

    /// <summary>Whether the icon stays in the tray all the time (minimize-to-tray).</summary>
    public void SetPermanent(bool permanent) => _icon.IsVisible = permanent;

    /// <summary>Green icon while clicking or holding, the normal one otherwise.</summary>
    public void SetActive(bool active) => _icon.Icon = active ? _active : _idle;

    public void Dispose()
    {
        // Hide the icon first so it does not linger in the tray after exit.
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
