using System.Collections.Concurrent;

namespace DareuLocal.Core;
public sealed partial class TgKeyboard
{
    static readonly ConcurrentDictionary<string, bool> bulkAssignSupport = new();

    public static byte[] BulkAssignRequestPayload(IReadOnlyList<byte> keys, byte layer)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count < 1 || keys.Count > 7)
            throw new ArgumentOutOfRangeException(nameof(keys), "单次批量读取最多支持 1–7 个按键");
        if (layer > 1)
            throw new ArgumentOutOfRangeException(nameof(layer));

        var payload = new byte[1 + 8 * keys.Count];
        payload[0] = (byte)keys.Count;
        for (int i = 0; i < keys.Count; i++)
        {
            payload[1 + 8 * i] = keys[i];
            payload[1 + 8 * i + 1] = layer;
        }
        return payload;
    }

    public static Assignment[] ParseBulkAssignReply(ReadOnlySpan<byte> reply, int count)
    {
        if (count < 1 || count > 7 || reply.Length < 6 + 8 * count + 1)
            throw new IOException("批量按键读取响应长度无效");
        var result = new Assignment[count];
        for (int i = 0; i < count; i++)
        {
            int baseOffset = 6 + 8 * i;
            byte functionId = reply[baseOffset + 3];
            byte[] data = reply.Slice(baseOffset + 4, 5).ToArray();
            result[i] = new Assignment(functionId, data);
        }
        return result;
    }

    public Assignment[] ReadAssignmentsBulk(IReadOnlyList<byte> keys, byte layer)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (layer > 1)
            throw new ArgumentOutOfRangeException(nameof(layer));
        if (keys.Count == 0)
            return Array.Empty<Assignment>();

        if (bulkAssignSupport.TryGetValue(Endpoint.Path, out bool supported) && !supported)
            return keys.Select(k => ReadAssignment(k, layer)).ToArray();

        var result = new List<Assignment>(keys.Count);
        for (int offset = 0; offset < keys.Count; offset += 7)
        {
            var chunk = keys.Skip(offset).Take(7).ToArray();
            try
            {
                var payload = BulkAssignRequestPayload(chunk, layer);
                var reply = Exchange(1, 0x88, (byte)Profile, payload.Length, payload);
                result.AddRange(ParseBulkAssignReply(reply, chunk.Length));
            }
            catch (IOException)
            {
                bulkAssignSupport[Endpoint.Path] = false;
                for (int i = offset; i < keys.Count; i++)
                    result.Add(ReadAssignment(keys[i], layer));
                return result.ToArray();
            }
        }

        bulkAssignSupport[Endpoint.Path] = true;
        return result.ToArray();
    }

    public bool? TryReadSocd()
    {
        try
        {
            var reply = Exchange(1, 0x89, (byte)Profile, 1);
            return reply[6] is 0 or 1 ? reply[6] == 1 : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void SetSocd(bool enabled)
    {
        Exchange(1, 9, (byte)Profile, 1, (byte)(enabled ? 1 : 0));
        if (TryReadSocd() != enabled)
            throw new IOException("SOCD 设置读回不一致");
    }

    public (bool WinLocked, bool MacMode)? TryReadWinMacState()
    {
        try
        {
            var reply = Exchange(1, 0x86, (byte)Profile, 2);
            return (reply[6] != 0, reply[7] != 0);
        }
        catch (IOException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public Assignment ReadAssignment(byte key, byte layer)
    {
        var b = ReadKey(key, layer);
        return new(b[0], b[1..]);
    }
    public void SetAssignment(byte key, byte layer, Assignment a)
    {
        if (a.Data.Length != 5 || layer > 1)
            throw new ArgumentException("无效按键映射");
        Exchange(1, 3, (byte)Profile, 8, new[] { key, layer, a.Function }.Concat(a.Data).ToArray());
        if (!ReadAssignment(key, layer).Bytes().SequenceEqual(a.Bytes()))
            throw new IOException("键盘按键映射读回不一致");
    }
    byte[] MultiRead(byte cls, byte cmd, byte profile, byte[] prefix, int width = 1)
    {
        var first = Exchange(cls, (byte)(cmd | 128), profile, 1, prefix);
        int total = width == 1 ? first[6 + prefix.Length] : (first[6 + prefix.Length] << 8 | first[7 + prefix.Length]);
        if (total > 8192)
            throw new IOException("分包响应过大");
        var result = new List<byte>();
        for (int offset = 0; offset < total; offset += 48)
        {
            int n = Math.Min(48, total - offset);
            var data = prefix.Concat(CountOffset(total, offset, width)).ToArray();
            var reply = Exchange(cls, (byte)(cmd | 128), profile, n + data.Length, data);
            result.AddRange(reply.Skip(6 + data.Length).Take(n));
        }
        return result.ToArray();
    }
    static byte[] CountOffset(int total, int offset, int width) => width == 1 ? new[] { (byte)total, (byte)offset } : new[] { (byte)(total >> 8), (byte)total, (byte)(offset >> 8), (byte)offset };
    void MultiWrite(byte cls, byte cmd, byte profile, byte[] prefix, byte[] data, int width = 1)
    {
        if (data.Length > (width == 1 ? 255 : 8192))
            throw new ArgumentException("数据超出容量");
        for (int offset = 0; offset < data.Length; offset += 48)
        {
            var payload = prefix.Concat(CountOffset(data.Length, offset, width)).Concat(data.Skip(offset).Take(48)).ToArray();
            Exchange(cls, cmd, profile, payload.Length, payload);
        }
    }
    public int[] Profiles() => MultiRead(5, 0, 0, Array.Empty<byte>()).Where(x => x != 0).Select(x => (int)x).ToArray();
    public LightingState[] ReadLights()
    {
        var regions = MultiRead(3, 0, 0, Array.Empty<byte>()).Distinct().ToArray();
        if (regions.Length > 16)
            throw new IOException("灯光区域数异常");
        return regions.Select(ReadLight).ToArray();
    }
    public LightingState ReadLight(byte region)
    {
        var a = Exchange(3, 0x81, 0, 1, region);
        int n = a[11];
        if (n > 50)
            throw new IOException("灯效列表异常");
        var effects = a.Skip(12).Take(n).Where(x => x != 18).Select(x => (int)x).ToArray();
        var r = Exchange(3, 0x82, (byte)Profile, 1, region);
        int count = r[10];
        if (count > 17)
            throw new IOException("灯效颜色数异常");
        byte[][] colors = Enumerable.Range(0, count).Select(i => r.Skip(11 + i * 3).Take(3).ToArray()).ToArray();
        int brightness = Exchange(3, 0x83, (byte)Profile, 2, region)[7];
        return new(region, r[7], r[8], r[9], brightness, colors, effects);
    }
    public void SetLight(LightingState light)
    {
        if (!light.Effects.Contains(light.Effect) || light.Colors.Length > 17 || light.Colors.Any(x => x.Length != 3) || light.Brightness < 0 || light.Brightness > 255 || light.Speed < 0 || light.Speed > 3)
            throw new ArgumentException("无效灯光参数");
        var data = new[] { (byte)light.Region, (byte)light.Effect, (byte)light.Flag, (byte)light.Speed, (byte)light.Colors.Length }.Concat(light.Colors.SelectMany(x => x)).ToArray();
        Exchange(3, 2, (byte)Profile, data.Length, data);
        SetBrightness(light.Region, light.Brightness);
        var actual = ReadLight((byte)light.Region);
        if (actual.Effect != light.Effect || actual.Brightness != light.Brightness || actual.Speed != light.Speed || actual.Flag != light.Flag || !actual.Colors.SelectMany(x => x).SequenceEqual(light.Colors.SelectMany(x => x)))
            throw new IOException("灯效读回不一致");
    }
    public int SleepSeconds()
    {
        var b = Exchange(7, 0x82, (byte)Profile, 3);
        return b[6] == 0 ? 0 : b[7] << 8 | b[8];
    }
    public void SetSleep(int seconds)
    {
        if (seconds < 0 || seconds > 65535)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        Exchange(7, 2, (byte)Profile, 3, (byte)(seconds == 0 ? 0 : 1), (byte)(seconds >> 8), (byte)seconds);
        if (SleepSeconds() != seconds)
            throw new IOException("休眠设置读回不一致");
    }
    public byte[] MacroIds() => MultiRead(6, 1, 0, Array.Empty<byte>()).Where(x => x != 0).Distinct().ToArray();
    public byte[] ReadMacroRaw(byte slot) => MultiRead(6, 5, 0, new[] { slot }, 2);
    public DeviceMacro ReadMacro(byte slot) => new($"Macro {slot}", MacroCodec.FromTg(ReadMacroRaw(slot)));
    public void WriteMacro(byte slot, DeviceMacro macro) => WriteMacroRaw(slot, macro.Name, MacroCodec.ToTg(macro.Actions));
    public void WriteMacroRaw(byte slot, string name, byte[] data)
    {
        if (slot < 1 || slot > 16)
            throw new ArgumentOutOfRangeException(nameof(slot));
        MacroCodec.ValidateTgBlob(data);
        if (data.Length == 0)
            throw new ArgumentException("宏不能为空");
        Exchange(6, 2, 0, 3, slot, (byte)(data.Length >> 8), (byte)data.Length);
        MultiWrite(6, 4, 0, new[] { slot }, System.Text.Encoding.UTF8.GetBytes(name).Take(30).ToArray());
        MultiWrite(6, 5, 0, new[] { slot }, data, 2);
        if (!ReadMacroRaw(slot).SequenceEqual(data))
            throw new IOException("键盘宏读回不一致");
    }
    public void DeleteMacro(byte slot)
    {
        Exchange(6, 3, 0, 0, slot);
        if (MacroIds().Contains(slot))
            throw new IOException("宏删除后仍存在");
    }
    public int Firmware()
    {
        var b = Exchange(0, 0x80, 0, 3);
        return b[6] << 16 | b[7] << 8 | b[8];
    }
    public void ResetProfile()
    {
        Exchange(5, 5, (byte)Profile, 0);
        Thread.Sleep(600);
    }
}
