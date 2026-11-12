using System.Diagnostics;
using System.Security.Principal;

namespace WeatherHueWallpaper;

internal static class SingleInstance
{
    public static void TerminatePreviousInstances()
    {
        try
        {
            var ownPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(ownPath))
                return;

            string processName = Path.GetFileNameWithoutExtension(ownPath);
            using var me = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == me.Id)
                            continue;

                        // Replace earlier instances of this named application even
                        // if the program was moved to a different publish directory.
                        // Restrict to this interactive Windows session and to the
                        // exact executable filename to avoid unrelated processes.
                        if (process.SessionId != me.SessionId)
                            continue;
                        var path = process.MainModule?.FileName;
                        if (path is null || !string.Equals(
                                Path.GetFileName(path), Path.GetFileName(ownPath),
                                StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Windows normally denies terminating another user's process
                        // without elevated rights; failures are silently ignored.
                        process.Kill(entireProcessTree: false);
                        process.WaitForExit(milliseconds: 5000);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static IDisposable? TryAcquire()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value
                      ?? Environment.UserName;
            var name = @"Local\WeatherHueWallpaper_" +
                       sid.Replace('-', '_').Replace('\\', '_');
            var mutex = new Mutex(initiallyOwned: false, name);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
                return new Lease(mutex);

            mutex.Dispose();
        }
        catch { }

        return null;
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); } catch { }
            mutex.Dispose();
        }
    }
}
