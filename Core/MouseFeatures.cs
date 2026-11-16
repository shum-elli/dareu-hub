namespace DareuLocal.Core;
public sealed partial class JmMouse
{
    public SensorState ReadSensor()
    {
        var a = ReadMemory(160, 27);
        int lod = ReadMemory(10, 2)[0];
        return new(a[11], lod, a[9], a[15], a[17], a[25]);
    }
    public void SetSensorValue(string name, int value)
    {
        ushort address = name switch
        {
            "MotionSync" => 171,
            "LOD" => 10,
            "Response" => 169,
            "Angle" => 175,
            "Ripple" => 177,
            "Mode" => 185,
            _ => throw new ArgumentException("未知传感器选项")
        };
        bool valid = name switch
        {
            "MotionSync" or "Angle" or "Ripple" => value is 0 or 1,
            "LOD" => value is >= 0 and <= 2,
            "Response" => value is >= 0 and <= 30,
            "Mode" => value is >= 0 and <= 2,
            _ => false
        };
        if (!valid)
            throw new ArgumentOutOfRangeException(nameof(value));
        WriteMemory(address, new[] { (byte)value, Checksum(new[] { (byte)value }) });
        if (ReadMemory(address, 1)[0] != value)
            throw new IOException("传感器设置读回不一致");
    }
    public void RestoreSensor(SensorState saved)
    {
        var supports = DeviceCatalog.Find(Endpoint.LogicalPid)?.SensorSupports ?? Array.Empty<int>();
        var options = new (int Id, ushort Address, int Value, int Max)[] { (0, (ushort)171, saved.MotionSync, 1), (1, (ushort)10, saved.Lod, 3), (2, (ushort)169, saved.Response, 30), (3, (ushort)175, saved.Angle, 1), (4, (ushort)177, saved.Ripple, 1), (5, (ushort)185, saved.Mode, 2) };
        foreach (var o in options.Where(x => supports.Contains(x.Id)))
            if (o.Value < 0 || o.Value > o.Max)
                throw new ArgumentException("备份传感器数据超出范围");
        foreach (var o in options.Where(x => supports.Contains(x.Id)))
        {
            byte value = (byte)o.Value;
            WriteMemory(o.Address, new[] { value, Checksum(new[] { value }) });
            if (ReadMemory(o.Address, 1)[0] != value)
                throw new IOException("传感器恢复读回不一致");
        }
    }
    public void SetStageCount(int count)
    {
        if (count < 1 || count > Math.Clamp(Endpoint.Product?.StageCount ?? 5, 1, 7))
            throw new ArgumentException("档位数量超出该型号能力");
        var state = ReadState();
        byte c = (byte)count;
        WriteMemory(2, new[] { c, Checksum(new[] { c }) });
        if (state.Stage > count)
            SetStage(count);
    }
    public byte[] ReadButton(int button)
    {
        ValidateButton(button);
        return ReadMemory((ushort)(96 + 4 * (button - 1)), 4);
    }
    void ValidateButton(int b)
    {
        if (b < 1 || b > Math.Clamp(Endpoint.Product?.ButtonCount ?? 6, 1, 6))
            throw new ArgumentOutOfRangeException(nameof(b));
    }
    public void SetButton(int button, byte[] assignment)
    {
        ValidateButton(button);
        if (assignment.Length != 3)
            throw new ArgumentException("按键数据长度错误");
        var bytes = assignment.Concat(new[] { Checksum(assignment) }).ToArray();
        WriteMemory((ushort)(96 + 4 * (button - 1)), bytes);
        if (!ReadButton(button).SequenceEqual(bytes))
            throw new IOException("鼠标按键读回不一致");
    }
    void WriteBlock(ushort address, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 10)
            WriteMemory((ushort)(address + i), data.Skip(i).Take(10).ToArray());
    }
    public void ResetButton(int button)
    {
        ValidateButton(button);
        string file = Path.Combine(AppContext.BaseDirectory, "Profiles", $"{Endpoint.Product!.Pid:x4}.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var b = doc.RootElement.GetProperty("Buttons").EnumerateArray().First(x => x.GetProperty("KeyID").GetInt32() == button);
        int f = b.GetProperty("default-function-Id").GetInt32();
        var a = b.GetProperty("default-function-data").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var bytes = f switch
        {
            1 => new byte[] { 1, (byte)(1 << (a[0] - 1)), 0 },
            19 => new byte[] { 2, 1, 0 },
            16 => new byte[] { 2, 2, 0 },
            17 => new byte[] { 2, 3, 0 },
            _ => throw new IOException("此默认功能尚未适配")
        };
        SetButton(button, bytes);
    }
    public void SetButtonKey(int button, byte code, byte modifier)
    {
        ValidateButton(button);
        if ((modifier & 0xF0) != 0)
            throw new ArgumentException("鼠标快捷键请使用左侧修饰键");
        var pressed = new List<(byte, byte)>();
        for (int bit = 0; bit < 4; bit++)
            if ((modifier & (1 << bit)) != 0)
                pressed.Add((128, (byte)(1 << (bit % 4))));
        if (code != 0)
            pressed.Add((129, code));
        if (pressed.Count == 0 || pressed.Count > 3)
            throw new ArgumentException("鼠标快捷键最多由三个按键组成");
        var actions = new List<byte>();
        foreach (var (type, key) in pressed)
            actions.AddRange(new byte[] { type, key, 0 });
        foreach (var (type, key) in pressed.AsEnumerable().Reverse())
            actions.AddRange(new byte[] { (byte)(type & 15 | 64), key, 0 });
        var data = new[] { (byte)(2 * pressed.Count) }.Concat(actions).ToArray();
        data = data.Concat(new[] { Checksum(data) }).ToArray();
        WriteBlock((ushort)(256 + 32 * (button - 1)), data);
        if (!ReadMemory((ushort)(256 + 32 * (button - 1)), data.Length).SequenceEqual(data))
            throw new IOException("快捷键数据读回不一致");
        SetButton(button, new byte[] { 5, 0, 0 });
    }
    public byte[] ReadMacroRaw(int slot)
    {
        ValidateButton(slot);
        ushort a = (ushort)(768 + 384 * (slot - 1) + 31);
        int n = ReadMemory(a, 1)[0];
        if (n == 0 || n == 255)
            return Array.Empty<byte>();
        if (n > 70)
            throw new IOException("宏动作数超过安全范围");
        return ReadMemory((ushort)(a + 1), 5 * n);
    }
    public void WriteMacro(int slot, DeviceMacro macro)
    {
        var full = MacroCodec.ToJm(macro.Actions);
        WriteMacroRaw(slot, full[1..^1]);
    }
    public void WriteMacroRaw(int slot, byte[] raw)
    {
        ValidateButton(slot);
        MacroCodec.ValidateJmBlob(raw);
        if (raw.Length == 0)
        {
            ClearMacro(slot);
            return;
        }
        var data = new[] { (byte)(raw.Length / 5) }.Concat(raw).ToArray();
        data = data.Concat(new[] { Checksum(data) }).ToArray();
        var name = System.Text.Encoding.ASCII.GetBytes($"Macro {slot}");
        var bytes = Enumerable.Repeat((byte)255, 31).ToList();
        bytes[0] = (byte)name.Length;
        for (int i = 0; i < name.Length; i++)
            bytes[1 + i] = name[i];
        bytes.AddRange(data);
        WriteBlock((ushort)(768 + 384 * (slot - 1)), bytes.ToArray());
        if (!ReadMacroRaw(slot).SequenceEqual(raw))
            throw new IOException("鼠标宏读回不一致");
    }
    public void BindMacro(int button, int repeats)
    {
        if (repeats < 1 || repeats > 250)
            throw new ArgumentException("重复次数 1–250");
        SetButton(button, new byte[] { 6, (byte)(button - 1), (byte)repeats });
    }
    public void RestoreButton(int button, byte[] assignment, byte[] keys)
    {
        ValidateButton(button);
        if (assignment.Length != 4 || keys.Length != 32)
            throw new ArgumentException("按键备份长度错误");
        WriteBlock((ushort)(256 + 32 * (button - 1)), keys);
        SetButton(button, assignment[..3]);
    }
    public void SetButtonMedia(int button, ushort usage)
    {
        ValidateButton(button);
        var a = new byte[] { 2, 130, (byte)usage, (byte)(usage >> 8), 66, (byte)usage, (byte)(usage >> 8), 0 };
        a[^1] = Checksum(a.AsSpan(0, a.Length - 1));
        WriteBlock((ushort)(256 + 32 * (button - 1)), a);
        SetButton(button, new byte[] { 5, 0, 0 });
    }
    public void ClearMacro(int slot)
    {
        ValidateButton(slot);
        WriteMemory((ushort)(768 + 384 * (slot - 1) + 31), new byte[] { 0 });
        if (ReadMacroRaw(slot).Length != 0)
            throw new IOException("宏清空读回不一致");
    }
    public int Firmware()
    {
        var r = Exchange(Packet(18));
        return r[5] << 8 | r[6];
    }
    public void FactoryReset()
    {
        stream.Write(Packet(9));
        Thread.Sleep(1200);
    }
}
