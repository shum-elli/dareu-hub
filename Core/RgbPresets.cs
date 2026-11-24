namespace DareuLocal.Core;

/// <summary>
/// Generates procedural per-key RGB color presets for any keyboard LED matrix size.
/// </summary>
public static class RgbPresets
{
    public static readonly (int Id, string Name)[] All =
    {
        (0, "赛博霓虹"),
        (1, "日落余晖"),
        (2, "极光森林"),
        (3, "彩虹全彩"),
        (4, "冰蓝竞技")
    };

    public static RgbMatrix Generate(int rows, int columns, int presetId)
    {
        if (rows < 1 || rows > 32 || columns < 1 || columns > 64 || rows * columns > 1024)
            throw new ArgumentException("灯光矩阵尺寸错误");
        if (presetId is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(presetId), "不支持的 RGB 预设编号");

        var pixels = new byte[rows * columns * 3];
        for (int r = 0; r < rows; r++)
        {
            double yr = rows <= 1 ? 0.5 : (double)r / (rows - 1);
            for (int c = 0; c < columns; c++)
            {
                double xr = columns <= 1 ? 0.5 : (double)c / (columns - 1);
                (byte R, byte G, byte B) rgb = presetId switch
                {
                    0 => Lerp((0, 240, 255), (255, 0, 122), Math.Clamp(xr * 0.85 + yr * 0.15, 0, 1)),
                    1 => Lerp((255, 94, 26), (122, 28, 172), Math.Clamp((xr + yr) * 0.5, 0, 1)),
                    2 => Lerp((0, 230, 118), (41, 121, 255), 0.5 + 0.5 * Math.Sin(xr * Math.PI * 1.6 + yr * 1.4)),
                    3 => AudioFramePainter.HsvToRgb(360.0 * c / Math.Max(1, columns), 1.0, 1.0),
                    4 => GamingClusterColor(r, c, rows, columns),
                    _ => (255, 255, 255)
                };

                int idx = (r * columns + c) * 3;
                pixels[idx] = rgb.R;
                pixels[idx + 1] = rgb.G;
                pixels[idx + 2] = rgb.B;
            }
        }

        var matrix = new RgbMatrix(rows, columns, pixels);
        matrix.Validate();
        return matrix;
    }

    static (byte R, byte G, byte B) GamingClusterColor(int row, int col, int rows, int columns)
    {
        bool wasdZone = row >= 2 && row <= Math.Min(rows - 1, 4) && col >= 1 && col <= Math.Min(columns - 1, 4);
        bool arrowZone = row >= Math.Max(0, rows - 2) && col >= Math.Max(0, columns - 4) && col <= columns - 1;
        return (wasdZone || arrowZone) ? ((byte)255, (byte)109, (byte)0) : ((byte)24, (byte)56, (byte)84);
    }

    static (byte R, byte G, byte B) Lerp((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return (
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t)
        );
    }
}
