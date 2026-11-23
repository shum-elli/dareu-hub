namespace DareuLocal.Core;
public static class AudioFramePainter
{
    public static RgbMatrix Paint(int rows, int columns, double level, int mode, byte[] color, double brightness)
    {
        if (color.Length != 3 || mode < 0 || mode > 1 || !double.IsFinite(level) || !double.IsFinite(brightness))
            throw new ArgumentException("音频灯光参数无效");
        level = Math.Clamp(level, 0, 1);
        brightness = Math.Clamp(brightness, 0, 1);
        var pixels = new byte[rows * columns * 3];
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                double strength = mode == 0 ? level : (col < level * columns ? 1 : 0);
                for (int c = 0; c < 3; c++)
                    pixels[(row * columns + col) * 3 + c] = (byte)Math.Clamp(Math.Round(color[c] * strength * brightness), 0, 255);
            }
        var result = new RgbMatrix(rows, columns, pixels);
        result.Validate();
        return result;
    }

    public static RgbMatrix PaintSpectrum(int rows, int columns, double[] bands, int mode, byte[] color, double brightness)
    {
        if (bands == null || color.Length != 3 || mode < 0 || mode > 3 || !double.IsFinite(brightness))
            throw new ArgumentException("音频灯光参数无效");
        brightness = Math.Clamp(brightness, 0, 1);

        if (mode is 0 or 1)
        {
            double energy = 0;
            if (bands.Length > 0)
            {
                double max = 0, sum = 0;
                foreach (double b in bands)
                {
                    double clamped = double.IsFinite(b) ? Math.Clamp(b, 0, 1) : 0;
                    if (clamped > max) max = clamped;
                    sum += clamped;
                }
                energy = Math.Clamp(max * 0.6 + (sum / bands.Length) * 0.4, 0, 1);
            }
            return Paint(rows, columns, energy, mode, color, brightness);
        }

        var pixels = new byte[rows * columns * 3];
        for (int col = 0; col < columns; col++)
        {
            double band = 0;
            if (bands.Length > 0)
            {
                int index = bands.Length == columns
                    ? col
                    : Math.Clamp((int)Math.Floor((double)col * bands.Length / Math.Max(1, columns)), 0, bands.Length - 1);
                band = double.IsFinite(bands[index]) ? Math.Clamp(bands[index], 0, 1) : 0;
            }

            byte baseR = color[0], baseG = color[1], baseB = color[2];
            if (mode == 3)
                (baseR, baseG, baseB) = HsvToRgb(360.0 * col / Math.Max(1, columns), 1.0, 1.0);

            for (int row = 0; row < rows; row++)
            {
                double strength = Math.Clamp(band * rows - (rows - 1 - row), 0, 1);
                int idx = (row * columns + col) * 3;
                pixels[idx] = (byte)Math.Clamp(Math.Round(baseR * strength * brightness), 0, 255);
                pixels[idx + 1] = (byte)Math.Clamp(Math.Round(baseG * strength * brightness), 0, 255);
                pixels[idx + 2] = (byte)Math.Clamp(Math.Round(baseB * strength * brightness), 0, 255);
            }
        }

        var result = new RgbMatrix(rows, columns, pixels);
        result.Validate();
        return result;
    }

    static (byte R, byte G, byte B) HsvToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
        double m = value - c;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return (
            (byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255)
        );
    }
}
