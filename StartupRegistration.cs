using Microsoft.Win32;

namespace WeatherHueWallpaper;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WeatherHueWallpaper";

    public static void Register()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) ||
                !string.Equals(Path.GetFileName(executable),
                    "WeatherHueWallpaper.exe", StringComparison.OrdinalIgnoreCase))
                return;

            var commandLine = $"\"{executable}\" --run";
            if (commandLine.Length > 260)
                return;

            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key?.SetValue(ValueName, commandLine, RegistryValueKind.String);
        }
        catch { }
    }

    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
