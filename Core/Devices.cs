using HidSharp;
using System.Diagnostics;
namespace DareuLocal.Core;

public sealed record MouseState(int Profile, int Battery, int PollingId, int Stage, int[] Dpi, int SleepSeconds, int Cid, int Mid, int Type);
public sealed partial class JmMouse : IDisposable
{
    readonly HidStream stream;
    public DeviceEndpoint Endpoint
    {
        get;
    }
    public JmMouse(DeviceEndpoint? endpoint = null)
    {
        Endpoint = endpoint ?? Hardware.Endpoints().FirstOrDefault(x => x.Kind == DeviceKind.Mouse) ?? throw new IOException("未发现鼠标，请检查 USB 或 2.4G 连接。");
        stream = Endpoint.Device.Open();
        stream.ReadTimeout = 150;
        stream.WriteTimeout = 700;
    }
    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (var b in bytes)
            sum += b;
        return unchecked((byte)(85 - sum));
    }
    public static byte[] Packet(byte command, ushort address = 0, byte count = 0, byte[]? payload = null)
    {
        var p = new byte[17];
        p[0] = 8;
        p[1] = command;
        p[3] = (byte)(address >> 8);
        p[4] = (byte)address;
        p[5] = count;
        if (payload != null)
            payload.CopyTo(p, 6);
        p[16] = Checksum(p.AsSpan(0, 16));
        return p;
    }
    byte[] Exchange(byte[] p)
    {
        stream.Write(p);
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 900)
        {
            var b = new byte[17];
            int n;
            try
            {
                n = stream.Read(b);
            }
            catch (TimeoutException) { continue; }
            if (n == 17 && b[0] == 8 && b[1] == p[1])
                return b[1..];
        }
        throw new IOException($"鼠标命令 0x{p[1]:X2} 超时；请唤醒鼠标并关闭官网驱动后重试。");
    }
    public byte[] ReadMemory(ushort address, int length)
    {
        if (length < 1 || length > 1024)
            throw new ArgumentOutOfRangeException(nameof(length));
        var result = new List<byte>();
        for (int i = 0; i < length; i += 10)
        {
            int n = Math.Min(10, length - i);
            var b = Exchange(Packet(8, (ushort)(address + i), (byte)n));
            result.AddRange(b.Skip(5).Take(n));
        }
        return result.ToArray();
    }
    void WriteMemory(ushort address, byte[] data)
    {
        if (data.Length > 10)
            throw new ArgumentException("单次最多 10 字节");
        Exchange(Packet(7, address, (byte)data.Length, data));
    }
    public static int DecodeDpi(int id)
    {
        for (int i = 0; i < 600; i++)
        {
            if (EncodeDpi((i + 1) * 50) == id)
                return (i + 1) * 50;
        }
        throw new IOException($"未知 DPI 编码 {id:X4}");
    }
    public static int EncodeDpi(int dpi)
    {
        if (dpi < 50 || dpi > 30000 || dpi % 50 != 0)
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 编码范围为 50–30000，步进 50");
        int i = dpi / 50 - 1;
        return i < 256 ? i : i < 512 ? (i - 256) | 0x4400 : (i - 512) | 0x8800;
    }
    public MouseState ReadState()
    {
        if (Endpoint.Wireless && Exchange(Packet(3))[5] != 1)
            throw new IOException("接收器已连接，但鼠标未唤醒。");
        var info = Exchange(Packet(1, count: 8, payload: new byte[] { 23, 79, 155, 201 }));
        int profile = Exchange(Packet(14))[5] + 1;
        int battery = Endpoint.Product?.HasBattery == true ? Exchange(Packet(4))[5] : -1;
        var memory = ReadMemory(0, 44);
        int count = memory[2];
        if (count < 1 || count > 7)
            throw new IOException("DPI 档位数异常，未开放写入。");
        var dpi = new int[count];
        for (int i = 0; i < count; i++)
        {
            int pos = 12 + i * 4;
            dpi[i] = DecodeDpi(memory[pos] | memory[pos + 2] << 8);
        }
        int stage = memory[4] + 1;
        if (stage < 1 || stage > count)
            throw new IOException("DPI 档位索引异常。");
        var sleep = ReadMemory(181, 4);
        return new(profile, battery, memory[0], stage, dpi, sleep[0] == 0 ? 0 : sleep[2] * 10, info[9], info[10], info[11]);
    }
    public void ResolveReceiverIdentity()
    {
        if (!Endpoint.Wireless)
            return;
        var info = Exchange(Packet(1, count: 8, payload: new byte[] { 23, 79, 155, 201 }));
        var pid = DeviceCatalog.MatchReceiver(Endpoint.Pid, info[9], info[10]);
        if (pid.HasValue)
            DeviceCatalog.Remember(Endpoint.Device.DevicePath, new[] { new ReceiverBinding(pid.Value, 0) });
    }
    public PowerReading ReadPower()
    {
        if (Endpoint.Product?.Type == 0 && !Endpoint.Product.HasBattery)
            return new(null, -1);
        if (Endpoint.Wireless && Exchange(Packet(3))[5] != 1)
            throw new IOException("鼠标尚未唤醒");
        var b = Exchange(Packet(4));
        return PowerReading.FromReport(b[5], b[6]);
    }
    public void SetDpi(int stage, int dpi)
    {
        if (stage < 1 || stage > 7)
            throw new ArgumentOutOfRangeException(nameof(stage));
        if (dpi < Math.Max(50, Endpoint.Product?.DpiMin ?? 100) || dpi > Math.Min(30000, Endpoint.Product?.DpiMax ?? 30000))
            throw new ArgumentException("DPI 超出该型号范围");
        int id = EncodeDpi(dpi);
        var b = new byte[] { (byte)id, (byte)id, (byte)(id >> 8), 0 };
        b[3] = Checksum(b.AsSpan(0, 3));
        WriteMemory((ushort)(12 + 4 * (stage - 1)), b);
    }
    public void SetStage(int stage)
    {
        if (stage < 1 || stage > 7)
            throw new ArgumentOutOfRangeException(nameof(stage));
        byte value = (byte)(stage - 1);
        WriteMemory(4, new[] { value, Checksum(new[] { value }) });
    }
    public void SetPolling(byte id)
    {
        if (!new byte[] { 8, 4, 2, 1, 16, 32, 64 }.Contains(id))
            throw new ArgumentException("不支持的回报率");
        WriteMemory(0, new[] { id, Checksum(new[] { id }) });
    }
    public void SetProfile(int profile)
    {
        if (profile < 1 || profile > 4)
            throw new ArgumentOutOfRangeException(nameof(profile));
        Exchange(Packet(15, count: 1, payload: new[] { (byte)(profile - 1) }));
        Thread.Sleep(350);
    }
    public void SetSleep(int seconds)
    {
        if (seconds < 0 || seconds > 2550 || seconds % 10 != 0)
            throw new ArgumentException("休眠时间需为 0–2550 秒，步进 10");
        byte enabled = (byte)(seconds == 0 ? 0 : 1);
        WriteMemory(181, new[] { enabled, Checksum(new[] { enabled }) });
        if (seconds > 0)
        {
            byte v = (byte)(seconds / 10);
            WriteMemory(183, new[] { v, Checksum(new[] { v }) });
        }
    }
    public void Dispose() => stream.Dispose();
}
public sealed record KeyboardState(int Pid, int Target, int Profile, int Battery, int Brightness, int Region);
public sealed partial class TgKeyboard : IDisposable
{
    readonly HidStream stream; byte target; public int Profile
    {
        get; private set;
    }
    public DeviceEndpoint Endpoint
    {
        get;
    }
    public TgKeyboard(DeviceEndpoint? endpoint = null)
    {
        Endpoint = endpoint ?? Hardware.Endpoints().FirstOrDefault(x => x.Kind == DeviceKind.Keyboard) ?? throw new IOException("未发现 EK75，请检查 USB 或 2.4G 连接。");
        stream = Endpoint.Device.Open();
        stream.ReadTimeout = 700;
        stream.WriteTimeout = 700;
    }
    public static byte[] Packet(byte target, byte cls, byte cmd, byte profile, int size, params byte[] payload)
    {
        var b = new byte[65];
        b[1] = target;
        b[2] = (byte)size;
        b[3] = cls;
        b[4] = cmd;
        b[5] = profile;
        payload.CopyTo(b, 7);
        return b;
    }
    byte[] Exchange(byte cls, byte cmd, byte profile, int size, params byte[] payload)
    {
        var p = Packet(target, cls, cmd, profile, size, payload);
        p[0] = (byte)(Endpoint.PhysicalProtocol == 1 ? 10 : 0);
        int attempts = (cmd & 0x80) != 0 ? 3 : 1;
        byte[] last = Array.Empty<byte>();
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            stream.SetFeature(p);
            Thread.Sleep(Endpoint.Wireless ? 60 : 30);
            for (int i = 0; i < 30; i++)
            {
                var b = new byte[65];
                b[0] = p[0];
                stream.GetFeature(b);
                var d = b[1..];
                last = d;
                int status = d[0] & 15;
                if (status == 2 && d[2] == cls && d[3] == cmd)
                    return d;
                if (status is 15 or 5)
                    throw new IOException("设备拒绝了此命令，当前固件可能不支持该功能。");
                if (status is 3 or 6)
                    break;
                Thread.Sleep(10);
            }
            if (attempt + 1 < attempts)
                Thread.Sleep(80);
        }
        var error = new IOException("键盘命令超时，请唤醒设备并关闭其他驱动后重试。");
        error.Data["Protocol"] = $"{cls:X2}/{cmd:X2}";
        error.Data["LastReply"] = Convert.ToHexString(last);
        throw error;
    }
    public KeyboardState ReadState()
    {
        int pid = ResolveTarget();
        Profile = Exchange(5, 0x83, 0, 0)[4];
        int battery = Endpoint.Product?.HasBattery == true ? Exchange(7, 0x80, 0, 3)[7] : -1;
        if (Endpoint.Product?.HasLighting != true)
            return new(pid, target, Profile, battery, 0, 0);
        var regions = Exchange(3, 0x80, 0, 1);
        int total = regions[6];
        if (total < 1 || total > 20)
            throw new IOException("灯光区域数量异常");
        var regionData = Exchange(3, 0x80, 0, total + 2, (byte)total, 0);
        int region = regionData[8];
        int brightness = Exchange(3, 0x83, (byte)Profile, 2, (byte)region)[7];
        return new(pid, target, Profile, battery, brightness, region);
    }
    public ReceiverBinding[] ResolveReceiverIdentities()
    {
        target = 0;
        var reply = Exchange(0, 0xa0, 0, 7);
        int count = reply[6];
        if (count < 1 || count > 18)
            throw new IOException("接收器设备列表异常");
        var result = new List<ReceiverBinding>();
        for (int i = 0; i < count; i++)
        {
            int offset = 7 + 3 * i;
            int pid = reply[offset + 1] << 8 | reply[offset + 2];
            if (reply[offset] == 1 && DeviceCatalog.Find(pid)?.Type is 0 or 1)
                result.Add(new(pid, (byte)((i + 1) << 4)));
        }
        DeviceCatalog.Remember(Endpoint.Device.DevicePath, result);
        return result.ToArray();
    }
    int ResolveTarget()
    {
        target = 0;
        if (!Endpoint.Wireless)
            return Endpoint.LogicalPid;
        if (Endpoint.LinkedPid.HasValue)
        {
            target = Endpoint.Target;
            return Endpoint.LogicalPid;
        }
        var list = ResolveReceiverIdentities();
        var first = list.FirstOrDefault() ?? throw new IOException("接收器已接入，设备未唤醒");
        target = first.Target;
        return first.Pid;
    }
    public PowerReading ReadPower()
    {
        int pid = ResolveTarget();
        if (DeviceCatalog.Effective(pid)?.HasBattery == false)
            return new(null, -1);
        var b = Exchange(7, 0x80, 0, 3);
        return PowerReading.FromReport(b[7], b[6]);
    }
    public void SetProfile(int profile)
    {
        if (profile < 1 || profile > 4)
            throw new ArgumentException("配置编号需为 1–4");
        Exchange(5, 3, (byte)profile, 0);
        Thread.Sleep(600);
    }
    public void SetBrightness(int region, int brightness)
    {
        if (brightness < 0 || brightness > 255)
            throw new ArgumentOutOfRangeException(nameof(brightness));
        Exchange(3, 3, (byte)Profile, 2, (byte)region, (byte)brightness);
    }
    public byte[] ReadKey(byte key, byte layer) => Exchange(1, 0x83, (byte)Profile, 8, key, layer)[8..14];
    public void SetKey(byte key, byte layer, byte hidCode) => SetAssignment(key, layer, new Assignment(6, new byte[] { 0, hidCode, 0, 0, 0 }));
    public void Dispose() => stream.Dispose();
}
