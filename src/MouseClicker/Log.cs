using System.IO;

namespace MouseClicker;

/// <summary>Appends errors to %APPDATA%\MouseClicker\error.log. Never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 256 * 1024;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MouseClicker",
        "error.log");

    public static void Error(string context, Exception? exception = null)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Keep the log from growing without bound: start over once it gets large.
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {context}{(exception is null ? "" : $": {exception}")}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never be the thing that fails.
        }
    }
}
