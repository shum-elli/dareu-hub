namespace DareuLocal.Core;
public sealed record RgbMatrix(int Rows, int Columns, byte[] Colors)
{
    public void Validate()
    {
        if (Rows < 1 || Rows > 32 || Columns < 1 || Columns > 64 || Rows * Columns > 1024 || Colors.Length != Rows * Columns * 3)
            throw new ArgumentException("灯光矩阵尺寸错误");
    }
}
public sealed partial class TgKeyboard
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> customReadSupport = new();
    public (int Rows, int Columns) MatrixSize(byte region = 1)
    {
        var data = Exchange(3, 0x81, 0, 1, region);
        int rows = data[9], cols = data[10];
        if (rows < 1 || rows > 32 || cols < 1 || cols > 64 || rows * cols > 1024)
            throw new IOException("设备未提供有效的逐键灯光矩阵");
        return (rows, cols);
    }
    public RgbMatrix ReadCustomMatrix(byte region = 1)
    {
        if (customReadSupport.TryGetValue(Endpoint.Path, out bool known) && !known)
            throw new IOException("当前固件不支持颜色读回，请使用本地颜色方案");
        try
        {
            var size = MatrixSize(region);
            int length = size.Rows * size.Columns * 3;
            var result = new byte[length];
            for (int offset = 0; offset < length; offset += 54)
            {
                int n = Math.Min(54, length - offset);
                var reply = Exchange(3, 0x88, (byte)Profile, 2, (byte)(offset >> 8), (byte)offset, (byte)n);
                Array.Copy(reply, 9, result, offset, n);
            }
            customReadSupport[Endpoint.Path] = true;
            return new(size.Rows, size.Columns, result);
        }
        catch (IOException) { customReadSupport[Endpoint.Path] = false; throw new IOException("当前固件不支持颜色读回，请使用本地颜色方案"); }
    }
    public static IEnumerable<byte[]> FramePackets(byte target, byte region, RgbMatrix matrix)
    {
        matrix.Validate();
        for (int row = 0; row < matrix.Rows; row++)
            for (int col = 0; col < matrix.Columns; col += 16)
            {
                int count = Math.Min(16, matrix.Columns - col);
                byte flags = (byte)(row == matrix.Rows - 1 && col + count == matrix.Columns ? 0x81 : 1);
                var data = new[] { region, flags, (byte)row, (byte)col, (byte)(col + count - 1) }.Concat(matrix.Colors.Skip((row * matrix.Columns + col) * 3).Take(count * 3)).ToArray();
                yield return Packet(target, 3, 4, 0, 6 + count * 3, data);
            }
    }
    public void SendFrame(RgbMatrix matrix, byte region = 1)
    {
        foreach (var p in FramePackets(target, region, matrix))
        {
            p[0] = (byte)(Endpoint.PhysicalProtocol == 1 ? 10 : 0);
            stream.SetFeature(p);
            if (Endpoint.Wireless)
                Thread.Sleep(3);
        }
    }
    public void SetFrameEffect(int effect, byte region = 1)
    {
        if (effect < 13 || effect > 18)
            throw new ArgumentException("自定义灯效编号无效");
        Exchange(3, 2, (byte)Profile, 5, region, (byte)effect, 0, 0, 0);
    }
    public bool SaveCustomMatrix(int slot, RgbMatrix matrix, byte region = 1)
    {
        if (slot < 1 || slot > 5)
            throw new ArgumentOutOfRangeException(nameof(slot));
        SetFrameEffect(12 + slot, region);
        SendFrame(matrix, region);
        Thread.Sleep(120);
        Exchange(3, 8, (byte)Profile, 2);
        Thread.Sleep(100);
        var active = Exchange(3, 0x82, (byte)Profile, 1, region);
        if (active[7] != slot + 12)
            throw new IOException("自定义槽位启用后的读回值不一致");
        RgbMatrix actual;
        try
        {
            actual = ReadCustomMatrix(region);
        }
        catch (IOException) { return false; }
        if (!actual.Colors.SequenceEqual(matrix.Colors))
            throw new IOException("逐键颜色写入后的读回值不一致");
        return true;
    }
}
