using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WeatherHueWallpaper;

// Methods must remain in native IDesktopWallpaper vtable order.
// Monitor positioning uses Win32 display enumeration because GetMonitorRECT
// can return E_FAIL on some machines.
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
        // Screen.AllScreens describes active screens and their virtual desktop
        // coordinates; IDesktopWallpaper can also enumerate detached monitors.
        var screens = Screen.AllScreens;
        if (screens.Length != 3)
            return Array.Empty<AttachedMonitor>();

        // Never correlate the two enumerations by index. Use the monitor
        // device-interface path returned by EDD_GET_DEVICE_INTERFACE_NAME.
        var comIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint count = _api.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string id = _api.GetMonitorDevicePathAt(i);
            if (!string.IsNullOrWhiteSpace(id))
                comIds.Add(id);
        }

        var attached = new List<AttachedMonitor>(screens.Length);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Screen screen in screens)
        {
            string? id = FindMatchingDeviceInterface(screen.DeviceName, comIds);
            if (id is null || !used.Add(id))
            {
                Debug.WriteLine(
                    $"WeatherHueWallpaper: Could not unambiguously map active screen '{screen.DeviceName}' " +
                    $"to an IDesktopWallpaper monitor. COM paths: {string.Join("; ", comIds)}");
                return Array.Empty<AttachedMonitor>();
            }

            var bounds = screen.Bounds;
            attached.Add(new AttachedMonitor(id, new NativeRect
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Right,
                Bottom = bounds.Bottom
            }));
        }

        return attached;
    }

    private static string? FindMatchingDeviceInterface(
        string gdiDeviceName, HashSet<string> comIds)
    {
        const uint EddGetDeviceInterfaceName = 0x00000001;
        string? match = null;

        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice
            {
                cb = Marshal.SizeOf<DisplayDevice>()
            };

            if (!EnumDisplayDevices(gdiDeviceName, index, ref device,
                                    EddGetDeviceInterfaceName))
                break;

            string? candidate = device.DeviceID;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (comIds.TryGetValue(candidate, out string? canonical))
            {
                // Multiple distinct matches for one GDI display are ambiguous.
                if (match is not null &&
                    !string.Equals(match, canonical, StringComparison.OrdinalIgnoreCase))
                    return null;

                match = canonical;
            }
        }

        return match;
    }

    public string GetWallpaper(AttachedMonitor monitor) => _api.GetWallpaper(monitor.Id);

    public void SetWallpaper(AttachedMonitor monitor, string filename) =>
        _api.SetWallpaper(monitor.Id, filename);

    public void Dispose()
    {
        try { Marshal.FinalReleaseComObject(_api); } catch { }
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    // Native DISPLAY_DEVICEW fixed-size WCHAR fields (sizes are in characters).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceKey;
    }

}

internal sealed record AttachedMonitor(string Id, NativeRect Bounds);
