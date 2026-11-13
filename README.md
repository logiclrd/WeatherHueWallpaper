# WeatherHueWallpaper

A silent, per-user background wallpaper program for **Windows 10/11** using **.NET 9**. It changes only the **rightmost attached monitor** and does nothing unless **exactly three monitors are attached**.

## Build and run

Requires the .NET 9 SDK to build (Windows, or another OS with Windows-targeting packs restored). The produced program is a Windows GUI-subsystem executable (no console window or GUI).

From the repository root, run `build-release.cmd` or:

```powershell
dotnet publish .\WeatherHueWallpaper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\publish
```

Keep the `publish` folder in a stable location. It contains `WeatherHueWallpaper.exe` and the included demonstration `template.png`. Run `WeatherHueWallpaper.exe` to start the worker. The first successful wallpaper update requires exactly three attached monitors.

For an ARM64 Windows device, use `-r win-arm64` instead.

## Configuration

On first launch, the application creates:

`%LOCALAPPDATA%\WeatherHueWallpaper\config.json`

The default settings are:

```json
{
  "TemplateFile": "template.png",
  "Latitude": null,
  "Longitude": null,
  "HueTransitionMinutes": 60
}
```

* **TemplateFile** can be an absolute path, a filename under `%LOCALAPPDATA%\WeatherHueWallpaper`, or a filename beside the executable. The included `template.png` is a demo image, not a required aesthetic.
* **Latitude / Longitude**: For accurate local weather and sunrise/sunset, fill in both coordinates in decimal degrees (e.g., `49.8951` and `-97.1384`). If both are `null`, the app requests an approximate location from `https://ipwho.is/`, caching the result for the lifetime of the process. VPNs and ISP routing can make this inaccurate. The location service receives your public IP address.
* **HueTransitionMinutes**: Number of one-minute steps for a transition, default **60**. Changes to config take effect when you restart the program.

To replace the template, drop a new `template.png` into the per-user folder or set `TemplateFile` to an existing PNG/JPEG/BMP. If you use the fallback beside the executable, that file is read on each update.

## Command line

Run from a terminal or PowerShell (the process produces no text output):

```powershell
.\publish\WeatherHueWallpaper.exe --register
.\publish\WeatherHueWallpaper.exe --unregister
.\publish\WeatherHueWallpaper.exe --stop
.\publish\WeatherHueWallpaper.exe --run
```

* `--register`: adds the **current published executable path** to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. It launches at user logon. It does not create a Windows service, require administrator privileges, or start the worker immediately.
* `--unregister`: removes that per-user logon entry, but does not terminate a running worker.
* `--stop`: terminates any existing instances launched from the **WeatherHueWallpaper.exe** application in the current Windows session, including copies launched from other folders.
* `--run` (or no arguments): starts the background worker, first terminating earlier copies of the same named application in the current Windows session. A named mutex prevents concurrent surviving workers in that user/session.

`--register` stores a command line in the Run key, so **do not move or delete the published executable afterwards** without registering the new location. This uses Microsoft's standard per-user Run mechanism.

## Operational behavior

### Weather task (every five minutes)

* If there are not exactly three active displays (as reported by Windows Forms `Screen.AllScreens`), skip the tick.
* Fetch current cloud cover, current WMO weather code, and today's sunrise/sunset from [Open-Meteo](https://open-meteo.com/en/docs).
* Map clear skies (cloud cover 0, weather code 0) to saturation factor **0.90** and brightness factor **1.00**. Map severe thunderstorms (codes 97/99) to **0.20** and **0.45**. Intermediate conditions use an explicit severity score (0 to 1) formed from cloud cover and WMO-code severity, then linear interpolation between the endpoints. All other weather code mappings are in `WeatherService.cs`.
* Set gamma to **4.0** at midnight, **1.25** at sunrise/sunset, and **0.75** at 12:00 local noon. Each pair of adjacent anchors uses `a + (b-a)*(1-cos(pi*t))/2`: continuous with zero slope at each anchor. When the expected sunrise-before-noon / sunset-after-noon order is impossible, fall back to 06:00 / 18:00 anchors. On initial startup before a successful fetch, the same fallback anchors and clear-sky factors apply.
* Atomic replacement of the complete weather snapshot prevents the wallpaper thread from seeing half-updated values. If a weather round is still running when a tick arrives, coalesce skipped ticks into **one pending immediate round**, then clear the pending flag.

Network failures leave the last complete weather snapshot unchanged. The API requests are HTTPS and have a 20-second timeout.

### Wallpaper task (every minute)

* This is an independent timer whose work runs serially on a dedicated **STA thread** for COM. A tick arriving during image processing is coalesced, not allowed to overwrite the current operation.
* Use `Screen.AllScreens` to require exactly three active displays and obtain their virtual-desktop rectangles. For each GDI display, call `EnumDisplayDevicesW` with `EDD_GET_DEVICE_INTERFACE_NAME` and match its monitor interface path to an ID from `IDesktopWallpaper.GetMonitorDevicePathAt`. Detached monitors may still appear in the COM enumeration, so do not match by index. Require an unambiguous, distinct match for every active display; otherwise skip the update. Choose the matched monitor with the greatest rectangle `Right` coordinate. This avoids `GetMonitorRECT`, which returned `0x80004005` (`E_FAIL`) on the target system.
* Determine all wallpapers currently assigned to attached monitors. Select one of two stable temporary PNG paths, **never choosing a path currently in use by any attached monitor**. If both paths are in use, skip the tick rather than overwrite either one. Alternate slots after each successful wallpaper update.
* Reload the original template image. Compute the **circular**, saturation/value/alpha-weighted mean hue (so hues near 0° and 360° average correctly). Rotate each pixel's HSV hue by the difference between its template average and the current transition hue, wrapping at 360°. Multiply HSV saturation by the current weather saturation factor. Multiply HSV value by the weather brightness factor, then apply `value = value ^ gamma` (thus a larger gamma darkens). Preserve alpha.
* Choose a new target hue when the previous transition finishes. Target selection is biased with the normalized weight proportional to `1 + 3*sin(1.5*hue)^2` (hue in radians): equal two-channel mixtures (yellow/cyan/magenta at 60°/180°/300°) have **4×** the likelihood density of pure RGB primaries (red/green/blue at 0°/120°/240°). Interpolate along the shortest hue-circle arc with a half-cosine ease-in/out over 60 one-minute steps by default.
* Write the PNG, then call `IDesktopWallpaper.SetWallpaper` for the rightmost monitor only.

The two output files live at:

`%LOCALAPPDATA%\WeatherHueWallpaper\wallpaper-a.png`

`%LOCALAPPDATA%\WeatherHueWallpaper\wallpaper-b.png`

## Troubleshooting and limitations

The program intentionally produces **no output or dialogs** for errors. If nothing happens, check that exactly three displays are connected, the template exists, the executable is running, and the config location is correct. Manual coordinates bypass IP-geolocation uncertainty. This is a user-session program, **not** a Windows service: Windows desktop wallpaper APIs need a logged-in desktop session.

No location is read using Windows' precise-location permission system; auto-location is inferred from the public IP. Open-Meteo supplies forecast/model conditions, not a guaranteed live meteorological observation. Weather and gamma are sampled on each five-minute refresh, not recalculated every image minute.

On restart, the hue transition starts from the template's average hue rather than persisting the prior transition. Existing wallpaper paths are inspected before writing, including immediately after restart, so neither active file is overwritten.

## Sources for relevant APIs

* [Microsoft IDesktopWallpaper interface](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper)
* [Microsoft monitor counts: detached monitors may be included](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-getmonitordevicepathcount)
* [Microsoft Run/RunOnce keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
* [Open-Meteo weather forecast API](https://open-meteo.com/en/docs)

## Verification status

The original project was reported to compile and run after retargeting to `net9.0-windows`. The `GetMonitorRECT` failure was reproduced on the target system; the revised monitor-path matching code has **not** yet been compiled or verified there. The implementation was reviewed without a Windows desktop or .NET SDK in the authoring environment. Validate three-monitor mapping and wallpaper updates in Visual Studio before registering for logon.

If a screen cannot be matched to exactly one COM monitor, the wallpaper round is skipped. In a Debug build, mapping failures are written to the Visual Studio Output window via `Debug.WriteLine` (no UI errors are shown).
