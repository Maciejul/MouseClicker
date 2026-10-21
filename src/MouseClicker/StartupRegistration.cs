using Microsoft.Win32;

namespace MouseClicker;

/// <summary>Adds or removes the app's "start with Windows" entry under the current user's Run key.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MouseClicker";

    public static void Apply(bool enabled, bool minimized)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"" + (minimized ? " --minimized" : ""));
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            // Autostart is a convenience; the app still works if the registry is locked down.
            Log.Error("Could not update the autostart entry", ex);
        }
    }
}
