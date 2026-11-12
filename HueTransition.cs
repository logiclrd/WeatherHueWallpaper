using System.Security.Cryptography;

namespace WeatherHueWallpaper;

internal sealed class HueTransition
{
    private bool _initialized;
    private double _current;
    private double _start;
    private double _target;
    private int _step;
    private int _duration;

    public double Advance(double templateAverageHue, int minutes)
    {
        if (!_initialized)
        {
            _current = Wrap(templateAverageHue);
            _initialized = true;
            _step = 0;
            _duration = 0;
        }

        if (_step >= _duration)
        {
            _start = _current;
            _target = PickBiasedHue();
            _step = 0;
            _duration = Math.Clamp(minutes, 1, 1440);
        }

        _step++;
        double t = (double)_step / _duration;
        double eased = (1 - Math.Cos(Math.PI * t)) / 2;
        double shortestArc = ((_target - _start + 540) % 360) - 180;
        _current = Wrap(_start + shortestArc * eased);
        return _current;
    }

    private static double PickBiasedHue()
    {
        // Rejection sampling implements exactly the density
        // weight(hue) = 1 + 3 sin²(1.5 hue).
        // At RGB primaries (0,120,240): weight 1.
        // At equal two-channel mixtures (60,180,300): weight 4.
        while (true)
        {
            double hue = RandomNumberGenerator.GetInt32(0, 3_600_000) / 10_000.0;
            double weight = 1 + 3 * Math.Pow(Math.Sin(1.5 * hue * Math.PI / 180), 2);
            double test = RandomNumberGenerator.GetInt32(0, 1_000_000) / 1_000_000.0;
            if (test * 4 <= weight)
                return hue;
        }
    }

    public static double Wrap(double hue) => ((hue % 360) + 360) % 360;
}
