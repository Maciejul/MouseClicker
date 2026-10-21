using System.IO;
using System.Reflection;
using System.Text.Json;

namespace MouseClicker;

internal enum TriggerMode
{
    /// <summary>Press the hotkey once to start, press again to stop.</summary>
    Toggle,

    /// <summary>Clicks only while the hotkey is held down.</summary>
    Hold,
}

internal enum ClickButton
{
    Left,
    Right,
    Middle,
}

internal enum ClickTargetKind
{
    /// <summary>Click wherever the cursor is.</summary>
    Cursor,

    /// <summary>Click a fixed point on the screen.</summary>
    Point,

    /// <summary>Click a spot inside a window, found again by process name.</summary>
    Window,
}

internal enum HoldTarget
{
    Key,
    Mouse,
    Both,
}

internal enum SingleTapBehavior
{
    /// <summary>One tap starts the hold, the next tap releases it.</summary>
    Toggle,

    /// <summary>The hold lasts while the trigger key is down.</summary>
    WhileDown,

    /// <summary>A single tap does nothing; only the timed double and triple taps act.</summary>
    Nothing,
}

/// <summary>Marks settings that belong to the app, not to a profile.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class AppLevelAttribute : Attribute
{
}

internal sealed class Profile
{
    public string Name { get; set; } = "";
    public AppSettings Data { get; set; } = new();
}

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int IntervalMs { get; set; } = 100;
    public int JitterPercent { get; set; } = 10;

    /// <summary>Win32 virtual-key code. 0x75 is F6.</summary>
    public int HotkeyVk { get; set; } = 0x75;

    public TriggerMode Mode { get; set; } = TriggerMode.Toggle;

    public ClickButton ClickButton { get; set; } = ClickButton.Left;

    public bool DoubleClick { get; set; }

    /// <summary>Stop clicking after this many clicks. 0 means no limit.</summary>
    public int StopAfterClicks { get; set; }

    /// <summary>Stop clicking after this many minutes. 0 means no limit.</summary>
    public int StopAfterMinutes { get; set; }

    public ClickTargetKind ClickTarget { get; set; } = ClickTargetKind.Cursor;
    public int PointX { get; set; }
    public int PointY { get; set; }

    /// <summary>Process name of the window to click in, without .exe.</summary>
    public string? WindowProcess { get; set; }

    public int WindowOffsetX { get; set; }
    public int WindowOffsetY { get; set; }

    /// <summary>Win32 virtual-key code of the key that starts and releases the held key or mouse button. 0x76 is F7.</summary>
    public int HoldTriggerVk { get; set; } = 0x76;

    /// <summary>Whether the hold trigger is active. When off, the trigger key does nothing and any hold is released.</summary>
    public bool HoldEnabled { get; set; } = true;

    /// <summary>Release a held key when you physically press or release that same key.</summary>
    public bool StopHoldOnHeldKey { get; set; }

    /// <summary>Win32 virtual-key code of the key that stays pressed while the hold trigger is active. 0x57 is W.</summary>
    public int HeldKeyVk { get; set; } = 0x57;

    public HoldTarget HoldTargetKind { get; set; } = HoldTarget.Key;

    public ClickButton HeldMouseButton { get; set; } = ClickButton.Left;

    public SingleTapBehavior SingleTap { get; set; } = SingleTapBehavior.Toggle;

    /// <summary>Longest a "while pressed" hold may last, in seconds. 0 means no limit.</summary>
    public int MaxHoldSeconds { get; set; }

    /// <summary>Double and triple taps start a timed hold.</summary>
    public bool HoldTimedEnabled { get; set; }

    public int DoubleTapSeconds { get; set; } = 5;
    public int TripleTapSeconds { get; set; } = 10;

    /// <summary>Tapping during a timed hold adds time instead of stopping it.</summary>
    public bool ExtendOnTap { get; set; }

    public int ExtendSeconds { get; set; } = 5;

    /// <summary>Show a Windows notification each time clicking starts.</summary>
    public bool NotifyOnStart { get; set; }

    [AppLevel]
    public List<Profile> Profiles { get; set; } = new();

    [AppLevel]
    public string? ActiveProfile { get; set; }

    /// <summary>Win32 virtual-key code of the key that switches to the next profile. 0x77 is F8.</summary>
    [AppLevel]
    public int ProfileNextVk { get; set; } = 0x77;

    [AppLevel]
    public bool StartWithWindows { get; set; }

    [AppLevel]
    public bool StartMinimized { get; set; }

    [AppLevel]
    public bool MinimizeToTray { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MouseClicker",
        "settings.json");

    /// <summary>A copy of the profile-level settings, with no profiles of its own.</summary>
    public AppSettings Snapshot()
    {
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions)) ?? new AppSettings();
        copy.Profiles = new List<Profile>();
        copy.ActiveProfile = null;
        return copy;
    }

    /// <summary>Copies every profile-level setting from a saved profile. App-level settings stay as they are.</summary>
    public void ApplyProfile(AppSettings profile)
    {
        foreach (PropertyInfo property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetCustomAttribute<AppLevelAttribute>() is not null) continue;
            property.SetValue(this, property.GetValue(profile));
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Settings are a convenience; failing to save should not stop the clicker.
        }
    }
}
