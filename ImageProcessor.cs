using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WeatherHueWallpaper;

internal static class ImageProcessor
{
    public static Bitmap LoadAs32Bit(string templateFile)
    {
        // A new copy is read from the template on each one-minute update.
        using var original = new Bitmap(templateFile);
        var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height));
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public static unsafe double AverageHue(Bitmap bitmap)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bits = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            double sumX = 0;
            double sumY = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                byte* row = (byte*)bits.Scan0 + y * bits.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte* pixel = row + x * 4;  // B, G, R, A
                    var hsv = ToHsv(pixel[2], pixel[1], pixel[0]);
                    double weight = hsv.Saturation * hsv.Value * (pixel[3] / 255.0);
                    if (weight <= 0)
                        continue;
                    double rad = hsv.Hue * Math.PI / 180;
                    sumX += weight * Math.Cos(rad);
                    sumY += weight * Math.Sin(rad);
                }
            }

            // A circle-aware average avoids incorrectly averaging 359° and 1° to 180°.
            return Math.Abs(sumX) + Math.Abs(sumY) < 1e-9
                ? 0
                : HueTransition.Wrap(Math.Atan2(sumY, sumX) * 180 / Math.PI);
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
    }

    public static unsafe void Apply(Bitmap bitmap, double hueDelta, WeatherSnapshot weather)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bits = bitmap.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                byte* row = (byte*)bits.Scan0 + y * bits.Stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte* pixel = row + x * 4;
                    if (pixel[3] == 0)
                        continue;

                    var hsv = ToHsv(pixel[2], pixel[1], pixel[0]);
                    double hue = HueTransition.Wrap(hsv.Hue + hueDelta);
                    double saturation = Math.Clamp(
                        hsv.Saturation * weather.SaturationFactor, 0, 1);
                    double linearValue = Math.Clamp(
                        hsv.Value * weather.BrightnessFactor, 0, 1);
                    // Higher nighttime gamma darkens; gamma < 1 at noon brightens.
                    double value = Math.Pow(linearValue, weather.Gamma);
                    FromHsv(hue, saturation, value, out byte r, out byte g, out byte b);
                    pixel[2] = r;
                    pixel[1] = g;
                    pixel[0] = b;
                    // Preserve the input alpha byte.
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
    }

    private static (double Hue, double Saturation, double Value) ToHsv(
        byte red, byte green, byte blue)
    {
        double r = red / 255.0;
        double g = green / 255.0;
        double b = blue / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        if (delta == 0)
            return (0, 0, max);

        double hue;
        if (max == r)
            hue = 60 * (((g - b) / delta) % 6);
        else if (max == g)
            hue = 60 * ((b - r) / delta + 2);
        else
            hue = 60 * ((r - g) / delta + 4);

        double saturation = max == 0 ? 0 : delta / max;
        return (HueTransition.Wrap(hue), saturation, max);
    }

    private static void FromHsv(double hue, double saturation, double value,
                                out byte r, out byte g, out byte b)
    {
        double c = value * saturation;
        double section = hue / 60;
        double x = c * (1 - Math.Abs(section % 2 - 1));
        double m = value - c;
        double rr, gg, bb;
        switch ((int)section)
        {
            case 0: rr = c; gg = x; bb = 0; break;
            case 1: rr = x; gg = c; bb = 0; break;
            case 2: rr = 0; gg = c; bb = x; break;
            case 3: rr = 0; gg = x; bb = c; break;
            case 4: rr = x; gg = 0; bb = c; break;
            default: rr = c; gg = 0; bb = x; break;
        }

        r = (byte)Math.Round(Math.Clamp(rr + m, 0, 1) * 255);
        g = (byte)Math.Round(Math.Clamp(gg + m, 0, 1) * 255);
        b = (byte)Math.Round(Math.Clamp(bb + m, 0, 1) * 255);
    }
}
