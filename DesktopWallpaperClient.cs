using System.Runtime.InteropServices;

namespace WeatherHueWallpaper;

// The first five methods MUST appear in native IDesktopWallpaper vtable order.
// GetMonitorRECT is PreserveSig so S_FALSE (detached monitor) remains observable.
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                      [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint monitorIndex);

    uint GetMonitorDevicePathCount();

    [PreserveSig]
    int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                       out NativeRect rect);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal sealed class DesktopWallpaperClient : IDisposable
{
    private static readonly Guid DesktopWallpaperClsid =
        new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    private readonly IDesktopWallpaper _api = (IDesktopWallpaper)Activator.CreateInstance(
        Type.GetTypeFromCLSID(DesktopWallpaperClsid, throwOnError: true)!)!;

    public IReadOnlyList<AttachedMonitor> GetAttachedMonitors()
    {
        var attached = new List<AttachedMonitor>();
        uint count = _api.GetMonitorDevicePathCount();

        for (uint i = 0; i < count; i++)
        {
            var id = _api.GetMonitorDevicePathAt(i);
            int hr = _api.GetMonitorRECT(id, out NativeRect bounds);
            if (hr == 0)  // S_OK. S_FALSE denotes a detached monitor.
                attached.Add(new AttachedMonitor(id, bounds));
            else if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        return attached;
    }

    public string GetWallpaper(AttachedMonitor monitor) => _api.GetWallpaper(monitor.Id);

    public void SetWallpaper(AttachedMonitor monitor, string filename) =>
        _api.SetWallpaper(monitor.Id, filename);

    public void Dispose()
    {
        try { Marshal.FinalReleaseComObject(_api); } catch { }
    }
}

internal sealed record AttachedMonitor(string Id, NativeRect Bounds);
