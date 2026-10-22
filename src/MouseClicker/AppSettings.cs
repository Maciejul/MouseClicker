using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Names, not numbers, so reordering an enum later cannot change what a saved profile means. Old numbers still load.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Bumped when the file layout changes in a way old versions cannot read.</summary>
    public const int CurrentSchemaVersion = 1;

    private string? _path;

    [AppLevel]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

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

    /// <summary>Dark colour scheme for the window and tray menu. Applies and saves at once.</summary>
    [AppLevel]
    public bool DarkMode { get; set; }

    [AppLevel]
    public bool StartWithWindows { get; set; }

    [AppLevel]
    public bool StartMinimized { get; set; }

    [AppLevel]
    public bool MinimizeToTray { get; set; }

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MouseClicker",
        "settings.json");

    /// <summary>A copy of the profile-level settings, with no profiles of its own.</summary>
    public AppSettings Snapshot()
    {
        var copy = new AppSettings();
        copy.ApplyProfile(this);
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

    // Profiles ----------------------------------------------------------------------------------------------------
    // Names are matched without regard to case everywhere ("Racing" and "racing" are the same profile),
    // and the stored name keeps the spelling it was first saved with.

    public Profile? FindProfile(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Profiles.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The name a new profile gets when the user leaves the name box empty: the first "Profile N" not taken.</summary>
    public string NewProfileName()
    {
        for (int n = Profiles.Count + 1; ; n++)
        {
            string name = $"Profile {n}";
            if (FindProfile(name) is null) return name;
        }
    }

    /// <summary>Saves the current profile-level settings under a name, replacing a profile of the same name. Returns the stored name.</summary>
    public string SaveProfile(string? requestedName)
    {
        string name = requestedName?.Trim() ?? "";
        if (name.Length == 0) name = NewProfileName();

        Profile? existing = FindProfile(name);
        if (existing is null)
        {
            Profiles.Add(new Profile { Name = name, Data = Snapshot() });
        }
        else
        {
            name = existing.Name;
            existing.Data = Snapshot();
        }

        ActiveProfile = name;
        return name;
    }

    public bool DeleteProfile(string? name)
    {
        Profile? profile = FindProfile(name);
        if (profile is null) return false;

        Profiles.Remove(profile);
        if (string.Equals(ActiveProfile, profile.Name, StringComparison.OrdinalIgnoreCase)) ActiveProfile = null;
        return true;
    }

    /// <summary>The profile after the active one, wrapping around. Null when there are no profiles.</summary>
    public Profile? NextProfile()
    {
        if (Profiles.Count == 0) return null;

        int index = Profiles.FindIndex(p => string.Equals(p.Name, ActiveProfile, StringComparison.OrdinalIgnoreCase));
        return Profiles[(index + 1) % Profiles.Count];
    }

    /// <summary>Repairs a loaded file: blank names get a name, repeated names are dropped, and the active name must exist.</summary>
    private void Normalize()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<Profile>();
        foreach (Profile profile in Profiles)
        {
            profile.Name = profile.Name?.Trim() ?? "";
            profile.Data ??= new AppSettings();
            if (profile.Name.Length == 0)
            {
                int n = kept.Count + 1;
                while (seen.Contains($"Profile {n}")) n++;
                profile.Name = $"Profile {n}";
            }

            if (seen.Add(profile.Name)) kept.Add(profile);
        }

        Profiles = kept;
        ActiveProfile = FindProfile(ActiveProfile)?.Name;
    }

    // Storage -----------------------------------------------------------------------------------------------------

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        AppSettings loaded;
        try
        {
            loaded = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file instead of letting the next save overwrite it, then start from defaults.
            Log.Error($"Could not read {path}; keeping a copy as .bak", ex);
            try
            {
                File.Move(path, path + ".bak", overwrite: true);
            }
            catch (Exception)
            {
                // If even that fails there is nothing more to do.
            }

            loaded = new AppSettings();
        }

        loaded._path = path;
        loaded.Normalize();
        return loaded;
    }

    public void Save()
    {
        string path = _path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write a temp file first and swap it in, so a crash mid-write cannot leave a half-written settings file.
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        catch (Exception ex)
        {
            // Settings are a convenience; failing to save should not stop the clicker.
            Log.Error($"Could not save {path}", ex);
        }
    }
}
