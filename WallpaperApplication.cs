using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WeatherHueWallpaper;

internal sealed class WallpaperApplication : IDisposable
{
	private readonly AppPaths _paths;
	private readonly AppConfig _config;
	private readonly WeatherService _weather = new();
	private readonly HueTransition _hues = new();
	private readonly AutoResetEvent _wallpaperWake = new(false);
	private readonly CancellationTokenSource _shutdown = new();
	private readonly Logger _logger = new();
	private readonly object _weatherLock = new();

	private System.Threading.Timer? _weatherTimer;
	private System.Threading.Timer? _wallpaperTimer;
	private Thread? _wallpaperThread;
	private WeatherSnapshot _snapshot = WeatherSnapshot.Initial;
	private bool _weatherRunning;
	private bool _weatherPending;
	private bool _preferSlotA = true;

	public WallpaperApplication(AppPaths paths, AppConfig config)
	{
		_paths = paths;
		_config = config;
	}

	public void Start()
	{
		_logger.Start();

		_logger.Log("Starting up");

		_wallpaperThread = new Thread(WallpaperThread)
		{
			IsBackground = true,
			Name = "Wallpaper COM STA"
		};
		_wallpaperThread.SetApartmentState(ApartmentState.STA);
		_wallpaperThread.Start();

		// Independently scheduled timers; both begin immediately.
		_weatherTimer = new System.Threading.Timer(_ => { try { RequestWeather(); } catch { } }, null,
				TimeSpan.Zero, TimeSpan.FromMinutes(5));
		_wallpaperTimer = new System.Threading.Timer(_ => { try { _wallpaperWake.Set(); } catch { } }, null,
				TimeSpan.Zero, TimeSpan.FromMinutes(1));
	}

	private void RequestWeather()
	{
		_logger.Log("RequestWeather");

		lock (_weatherLock)
		{
			if (_shutdown.IsCancellationRequested)
				return;

			if (_weatherRunning)
			{
				_logger.Log("-> coalesced into existing request");

				// Coalesce all missed timer ticks into exactly one immediate retry.
				_weatherPending = true;
			}
			else
			{
				_logger.Log("-> requesting weather");

				_weatherRunning = true;

				_ = Task.Run(WeatherLoopAsync);
			}
		}
	}

	private async Task WeatherLoopAsync()
	{
		_logger.Log("WeatherLoop: starting");

		while (!_shutdown.IsCancellationRequested)
		{
			try
			{
				if (Screen.AllScreens.Length == 3)
				{
					_logger.Log("WeatherLoop: Fetching weather from remote server");
					var fresh = await _weather.FetchAsync(_config, _shutdown.Token);
					Volatile.Write(ref _snapshot, fresh);
				}
				else
					_logger.Log("WeatherLoop: Screen count is not 3");
			}
			catch (Exception e)
			{
				_logger.Log("WeatherLoop: Exception:");
				_logger.Log(e.ToString());
				// Keep the previous snapshot if the monitor count, network,
				// geolocation, or weather API is unavailable.
			}

			lock (_weatherLock)
			{
				if (_weatherPending && !_shutdown.IsCancellationRequested)
				{
					_logger.Log("WeatherLoop: looping");
					_weatherPending = false;
					// Continue immediately with another complete processing round.
				}
				else
				{
					_logger.Log("WeatherLoop: exiting");
					_weatherRunning = false;
					_weatherPending = false;
					return;
				}
			}
		}

		lock (_weatherLock)
		{
			_weatherRunning = false;
			_weatherPending = false;
		}
	}

	private void WallpaperThread()
	{
		var signals = new WaitHandle[] { _wallpaperWake, _shutdown.Token.WaitHandle };
		while (!_shutdown.IsCancellationRequested)
		{
			int result;
			try { result = WaitHandle.WaitAny(signals); }
			catch { return; }
			if (result != 0 || _shutdown.IsCancellationRequested)
				return;

			_logger.Log("Wallpaper: Generating...");

			try
			{
				GenerateWallpaper();
			}
			catch (Exception e)
			{
				_logger.Log("Wallpaper: Generation failed:");
				_logger.Log(e.ToString());
			}
		}
	}

	private void GenerateWallpaper()
	{
		using var wallpaper = new DesktopWallpaperClient();
		var monitors = wallpaper.GetAttachedMonitors();
		if (monitors.Count != 3)
		{
			_logger.Log("GenerateWallpaper: Monitor count is not 3");
			return;
		}

		// RECT.Right is the monitor's position in the virtual desktop, not
		// its width or enumeration order.
		var rightmost = monitors
			.OrderByDescending(m => m.Bounds.Right)
			.ThenByDescending(m => m.Bounds.Left)
			.First();

		_logger.Log("GenerateWallpaper: Rightmost monitor is ({0}, {1})-({2}, {3})", rightmost.Bounds.Left, rightmost.Bounds.Top, rightmost.Bounds.Right, rightmost.Bounds.Bottom);

		// Never overwrite a file assigned to ANY currently attached display.
		var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var monitor in monitors)
		{
			var current = wallpaper.GetWallpaper(monitor);
			if (!string.IsNullOrWhiteSpace(current))
				referenced.Add(Path.GetFullPath(current));
		}

		string preferred = _preferSlotA ? _paths.SlotA : _paths.SlotB;
		string alternate = _preferSlotA ? _paths.SlotB : _paths.SlotA;
		string output =
			!referenced.Contains(preferred) ? preferred :
			!referenced.Contains(alternate) ? alternate : string.Empty;
		if (output.Length == 0)
		{
			_logger.Log("GGenerateWallpaper: Failed to pick a slot");
			return;
		}

		_logger.Log("GenerateWallpaper: Picked slot: {0}", output);

		var template = _paths.ResolveTemplate(_config.TemplateFile);
		using var image = ImageProcessor.LoadAs32Bit(template);
		double averageHue = ImageProcessor.AverageHue(image);
		double desiredHue = _hues.Advance(averageHue, _config.HueTransitionMinutes);
		double delta = desiredHue - averageHue;

		_logger.Log("GenerateWallpaper: Hue: {0} + {1} = {2}", averageHue, delta, desiredHue);

		var weather = Volatile.Read(ref _snapshot);

		_logger.Log("GenerateWallpaper: Creating frame");

		ImageProcessor.Apply(image, delta, weather);

		_logger.Log("GenerateWallpaper: Saving frame");

		image.Save(output, ImageFormat.Png);

		_logger.Log("GenerateWallpaper: Setting new wallpaper");

		wallpaper.SetWallpaper(rightmost, output);
		_preferSlotA = string.Equals(output, _paths.SlotB, StringComparison.OrdinalIgnoreCase);
	}

	public void Dispose()
	{
		_shutdown.Cancel();
		_weatherTimer?.Dispose();
		_wallpaperTimer?.Dispose();
		try { _wallpaperWake.Set(); } catch { }
		try { _wallpaperThread?.Join(millisecondsTimeout: 5000); } catch { }
		_weather.Dispose();
		_wallpaperWake.Dispose();
		_shutdown.Dispose();
	}
}
