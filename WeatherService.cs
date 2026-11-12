using System.Globalization;
using System.Text.Json;

namespace WeatherHueWallpaper;

internal sealed record WeatherSnapshot(double SaturationFactor, double BrightnessFactor, double Gamma)
{
    // Only used before the first successful network refresh.
    public static WeatherSnapshot Initial => new(0.9, 1.0,
        TimeOfDayGamma.Calculate(DateTime.Now, 6 * 60, 18 * 60));
}

internal sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private (double latitude, double longitude)? _cachedLocation;

    public WeatherService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WeatherHueWallpaper/1.0");
    }

    public async Task<WeatherSnapshot> FetchAsync(AppConfig config, CancellationToken token)
    {
        var (latitude, longitude) = await LocateAsync(config, token);
        string lat = latitude.ToString("R", CultureInfo.InvariantCulture);
        string lon = longitude.ToString("R", CultureInfo.InvariantCulture);
        string endpoint = "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={lat}&longitude={lon}" +
            "&current=cloud_cover,weather_code" +
            "&daily=sunrise,sunset&timezone=auto&timeformat=unixtime&forecast_days=1";

        using var response = await _http.GetAsync(endpoint, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = json.RootElement;
        var current = root.GetProperty("current");
        double cloudCover = current.GetProperty("cloud_cover").GetDouble();
        int weatherCode = current.GetProperty("weather_code").GetInt32();
        var daily = root.GetProperty("daily");
        long sunriseUnix = daily.GetProperty("sunrise")[0].GetInt64();
        long sunsetUnix = daily.GetProperty("sunset")[0].GetInt64();

        double severity = WeatherSeverity(cloudCover, weatherCode);
        double saturation = 0.90 - 0.70 * severity;
        double brightness = 1.00 - 0.55 * severity;
        var sunriseLocal = DateTimeOffset.FromUnixTimeSeconds(sunriseUnix).ToLocalTime();
        var sunsetLocal = DateTimeOffset.FromUnixTimeSeconds(sunsetUnix).ToLocalTime();
        double gamma = TimeOfDayGamma.Calculate(DateTime.Now,
            sunriseLocal.TimeOfDay.TotalMinutes,
            sunsetLocal.TimeOfDay.TotalMinutes);

        return new WeatherSnapshot(saturation, brightness, gamma);
    }

    private async Task<(double latitude, double longitude)> LocateAsync(
        AppConfig config, CancellationToken token)
    {
        if (config.Latitude is { } latitude && config.Longitude is { } longitude &&
            double.IsFinite(latitude) && double.IsFinite(longitude) &&
            latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
        {
            return (latitude, longitude);
        }

        if (_cachedLocation is { } known)
            return known;

        // Approximate IP geolocation; for accuracy, set Latitude/Longitude in config.json.
        using var response = await _http.GetAsync("https://ipwho.is/", token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = json.RootElement;
        if (root.GetProperty("success").GetBoolean() is false)
            throw new InvalidOperationException("IP geolocation unavailable.");

        latitude = root.GetProperty("latitude").GetDouble();
        longitude = root.GetProperty("longitude").GetDouble();
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new InvalidOperationException("Invalid IP geolocation.");

        _cachedLocation = (latitude, longitude);
        return _cachedLocation.Value;
    }

    internal static double WeatherSeverity(double clouds, int code)
    {
        // WMO code = 0 and cloud cover = 0 => exact clear-sky endpoint.
        // Severe thunderstorms (97, 99) => exact storm endpoint.
        double byClouds = Math.Clamp(clouds / 100.0, 0, 1) * 0.80;
        double byConditions = code switch
        {
            97 or 99 => 1.00,
            96 => 0.95,
            95 => 0.90,
            82 => 0.85,
            65 or 67 or 75 or 86 => 0.75,
            63 or 73 or 81 or 85 => 0.60,
            45 or 48 or 57 or 61 or 66 or 71 or 80 => 0.45,
            51 or 53 or 55 or 56 or 77 => 0.35,
            3 => 0.65,
            2 => 0.35,
            1 => 0.10,
            _ => 0.00
        };
        return Math.Clamp(Math.Max(byClouds, byConditions), 0, 1);
    }

    public void Dispose() => _http.Dispose();
}

internal static class TimeOfDayGamma
{
    public static double Calculate(DateTime now, double sunriseMinute, double sunsetMinute)
    {
        const double noon = 12 * 60;
        const double end = 24 * 60;
        if (!double.IsFinite(sunriseMinute) || !double.IsFinite(sunsetMinute) ||
            sunriseMinute <= 0 || sunriseMinute >= noon ||
            sunsetMinute <= noon || sunsetMinute >= end)
        {
            sunriseMinute = 6 * 60;
            sunsetMinute = 18 * 60;
        }

        double minute = now.TimeOfDay.TotalMinutes;
        if (minute <= sunriseMinute)
            return Interpolate(minute, 0, sunriseMinute, 4.0, 1.25);
        if (minute <= noon)
            return Interpolate(minute, sunriseMinute, noon, 1.25, 0.75);
        if (minute <= sunsetMinute)
            return Interpolate(minute, noon, sunsetMinute, 0.75, 1.25);
        return Interpolate(minute, sunsetMinute, end, 1.25, 4.0);
    }

    private static double Interpolate(double minute, double start, double end,
                                      double first, double last)
    {
        double t = Math.Clamp((minute - start) / (end - start), 0, 1);
        // Half cosine: zero derivative at midnight, sunrise, noon, and sunset.
        double smooth = (1 - Math.Cos(Math.PI * t)) / 2;
        return first + (last - first) * smooth;
    }
}
