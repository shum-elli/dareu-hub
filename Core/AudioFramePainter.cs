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
}
