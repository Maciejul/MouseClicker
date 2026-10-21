using System.IO;
using System.Text.Json;

namespace MouseClicker;

internal enum TriggerMode
{
    /// <summary>Press the hotkey once to start, press again to stop.</summary>
    Toggle,

    /// <summary>Clicks only while the hotkey is held down.</summary>
    Hold,
}

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int IntervalMs { get; set; } = 100;
    public int JitterPercent { get; set; } = 10;

    /// <summary>Win32 virtual-key code. 0x75 is F6.</summary>
    public int HotkeyVk { get; set; } = 0x75;

    public TriggerMode Mode { get; set; } = TriggerMode.Toggle;

    /// <summary>Win32 virtual-key code of the key that starts and releases the held mouse button. 0x76 is F7.</summary>
    public int HoldTriggerVk { get; set; } = 0x76;

    /// <summary>Whether the hold trigger is active. When off, the trigger key does nothing and any held key is released.</summary>
    public bool HoldEnabled { get; set; } = true;

    /// <summary>Win32 virtual-key code of the key that stays pressed while the hold trigger is active. 0x57 is W.</summary>
    public int HeldKeyVk { get; set; } = 0x57;

    /// <summary>Show a Windows notification each time clicking starts.</summary>
    public bool NotifyOnStart { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MouseClicker",
        "settings.json");

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
