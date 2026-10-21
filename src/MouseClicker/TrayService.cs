using System.Drawing;
using System.Windows.Forms;

namespace MouseClicker;

/// <summary>The notification-area icon: its menu, and the balloon shown when clicking starts.</summary>
internal sealed class TrayService : IDisposable
{
    private NotifyIcon? _icon;
    private bool _keepVisible;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    /// <summary>Whether the icon stays in the tray all the time (minimize-to-tray), as opposed to only while a balloon shows.</summary>
    public void SetPermanent(bool permanent)
    {
        _keepVisible = permanent;
        if (permanent) Ensure().Visible = true;
        else if (_icon is not null) _icon.Visible = false;
    }

    public void ShowBalloon(string text)
    {
        NotifyIcon icon = Ensure();

        // The icon only needs to exist for the balloon; hide it again once the notification is dismissed.
        icon.BalloonTipClosed -= OnBalloonClosed;
        icon.BalloonTipClosed += OnBalloonClosed;
        icon.Visible = true;
        icon.ShowBalloonTip(3000, "Mouse Clicker", text, ToolTipIcon.Info);
    }

    private void OnBalloonClosed(object? sender, EventArgs e)
    {
        if (_icon is not null && !_keepVisible) _icon.Visible = false;
    }

    private NotifyIcon Ensure()
    {
        if (_icon is not null) return _icon;

        Icon icon;
        try
        {
            icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            icon = SystemIcons.Application;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon { Icon = icon, Text = "Mouse Clicker", ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        return _icon;
    }

    public void Dispose()
    {
        if (_icon is null) return;

        // Hide the icon first so it does not linger in the tray after exit.
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
