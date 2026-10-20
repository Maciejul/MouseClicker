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
