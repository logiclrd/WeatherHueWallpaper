namespace WeatherHueWallpaper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Deliberately silent: this application has no console or user-facing error dialogs.
        try
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "--register":
                        StartupRegistration.Register();
                        return;
                    case "--unregister":
                        StartupRegistration.Unregister();
                        return;
                    case "--stop":
                        SingleInstance.TerminatePreviousInstances();
                        return;
                    case "--run":
                        break;
                    default:
                        return;
                }
            }

            // Older copies of this exact executable are replaced, not merely ignored.
            SingleInstance.TerminatePreviousInstances();
            using var lease = SingleInstance.TryAcquire();
            if (lease is null)
                return;

            var paths = AppPaths.Create();
            var config = AppConfig.LoadOrCreate(paths);
            using var app = new WallpaperApplication(paths, config);
            app.Start();

            // Timers and the STA wallpaper worker run in the background.
            // The foreground main thread simply keeps the process alive.
            Thread.Sleep(Timeout.Infinite);
        }
        catch
        {
            // Silently ignore startup errors too.
        }
    }
}
