namespace DareuLocal.Core;
public static class MacroCodec
{
    public static void ValidateTgBlob(byte[] raw)
    {
        if (raw.Length > 8192)
            throw new ArgumentException("宏数据过大");
        for (int i = 0; i < raw.Length;)
        {
            int type = raw[i];
            if (type is 0 or 254)
            {
                if (raw.Skip(i + 1).Any(b => b != 0 && b != 254))
                    throw new ArgumentException("宏结束标记之后存在数据");
                break;
            }
            int len = type switch
            {
                1 or 2 or 4 or 5 or 6 or 7 or 10 or 15 or 16 => 2,
                8 or 9 or 11 => 3,
                12 => 4,
                3 or 13 => 5,
                14 => 6,
                _ => throw new ArgumentException("未知宏事件")
            };
            if (i + len > raw.Length)
                throw new ArgumentException("宏事件数据截断");
            i += len;
        }
    }
    public static void ValidateJmBlob(byte[] raw)
    {
        if (raw.Length % 5 != 0 || raw.Length > 350)
            throw new ArgumentException("鼠标宏容量或长度错误");
        for (int i = 0; i < raw.Length; i += 5)
            if (raw[i] is not (64 or 65 or 66 or 67 or 68 or 128 or 129 or 130 or 131 or 132))
                throw new ArgumentException("未知鼠标宏事件");
    }
    public static void Validate(IReadOnlyList<MacroAction> actions)
    {
        if (actions.Count < 1 || actions.Count > 250)
            throw new ArgumentException("宏需包含 1–250 个动作");
        var held = new HashSet<string>();
        foreach (var a in actions)
        {
            if (a.Delay < 0 || a.Delay > 65535)
                throw new ArgumentException("每个动作的延时范围为 0–65535 毫秒");
            if (a.Kind is not ("KeyDown" or "KeyUp" or "MouseDown" or "MouseUp"))
                throw new ArgumentException("不支持的宏动作");
            if (a.Kind.StartsWith("Key") && (a.Code < 4 || a.Code > 231) || a.Kind.StartsWith("Mouse") && (a.Code < 1 || a.Code > 5))
                throw new ArgumentException("无效宏按键");
            string key = (a.Kind.StartsWith("Key") ? "K" : "M") + a.Code;
            if (a.Kind.EndsWith("Down"))
            {
                if (!held.Add(key))
                    throw new ArgumentException("宏中存在重复按下");
            }
            else if (!held.Remove(key))
                throw new ArgumentException("宏中存在未配对的松开");
        }
        if (held.Count != 0)
            throw new ArgumentException("请为所有按下动作添加松开，避免按键持续按下");
    }
    static byte ButtonMask(int button) => button switch { 1 => 1, 2 => 2, 3 => 4, 4 => 8, 5 => 16, _ => throw new ArgumentException("无效鼠标键") };
    public static byte[] ToTg(IReadOnlyList<MacroAction> actions)
    {
        Validate(actions);
        var data = new List<byte>();
        int mask = 0;
        foreach (var a in actions)
        {
            if (a.Kind.StartsWith("Key"))
            {
                data.Add((byte)(a.Kind == "KeyDown" ? 4 : 5));
                data.Add((byte)a.Code);
            }
            else
            {
                int bit = ButtonMask(a.Code);
                mask = a.Kind == "MouseDown" ? mask | bit : mask & ~bit;
                data.AddRange(new byte[] { 1, (byte)mask });
            }
            if (a.Delay > 0)
            {
                if (a.Delay <= 255)
                    data.AddRange(new[] { (byte)10, (byte)a.Delay });
                else
                    data.AddRange(new[] { (byte)11, (byte)(a.Delay >> 8), (byte)a.Delay });
            }
        }
        return data.ToArray();
    }
    public static List<MacroAction> FromTg(byte[] data)
    {
        var result = new List<MacroAction>();
        int previous = 0;
        for (int i = 0; i < data.Length;)
        {
            byte code = data[i++];
            if (code is 0 or 254)
                break;
            if (i >= data.Length)
                throw new IOException("宏数据截断");
            if (code is 4 or 5)
                result.Add(new(code == 4 ? "KeyDown" : "KeyUp", data[i++]));
            else if (code == 1)
            {
                int mask = data[i++];
                int changed = mask ^ previous;
                for (int b = 1; b <= 5; b++)
                    if ((changed & ButtonMask(b)) != 0)
                        result.Add(new((mask & ButtonMask(b)) != 0 ? "MouseDown" : "MouseUp", b));
                previous = mask;
            }
            else if (code is 10 or 11)
            {
                int delay = data[i++];
                if (code == 11)
                {
                    if (i >= data.Length)
                        throw new IOException("宏延时数据截断");
                    delay = delay << 8 | data[i++];
                }
                if (result.Count > 0)
                    result[^1] = result[^1] with
                    {
                        Delay = delay
                    };
            }
            else
                throw new IOException($"宏包含当前编辑器不支持的事件 {code}，不会自动覆盖。");
        }
        return result;
    }
    public static byte[] ToJm(IReadOnlyList<MacroAction> actions)
    {
        Validate(actions);
        if (actions.Count > 70)
            throw new ArgumentException("鼠标每个宏最多 70 个动作");
        var data = new List<byte> { (byte)actions.Count };
        foreach (var a in actions)
        {
            byte type, key;
            if (a.Kind.StartsWith("Key"))
            {
                bool modifier = a.Code >= 224;
                type = (byte)(a.Kind == "KeyDown" ? (modifier ? 128 : 129) : (modifier ? 64 : 65));
                key = modifier ? (byte)(1 << (a.Code - 224)) : (byte)a.Code;
            }
            else
            {
                type = (byte)(a.Kind == "MouseDown" ? 132 : 68);
                key = ButtonMask(a.Code);
            }
            data.AddRange(new[] { type, key, (byte)0, (byte)(a.Delay >> 8), (byte)a.Delay });
        }
        data.Add(JmMouse.Checksum(data.ToArray()));
        return data.ToArray();
    }
    public static List<MacroAction> FromJm(byte[] data)
    {
        if (data.Length % 5 != 0)
            throw new IOException("鼠标宏数据长度错误");
        var result = new List<MacroAction>();
        for (int i = 0; i < data.Length; i += 5)
        {
            byte t = data[i], v = data[i + 1];
            int code = v;
            string kind;
            if (t is 128 or 64)
            {
                if (v == 0 || (v & (v - 1)) != 0)
                    throw new IOException("未知修饰键宏编码");
                code = 224 + (int)Math.Log2(v);
                kind = t == 128 ? "KeyDown" : "KeyUp";
            }
            else if (t is 129 or 65)
                kind = t == 129 ? "KeyDown" : "KeyUp";
            else if (t is 132 or 68)
            {
                kind = t == 132 ? "MouseDown" : "MouseUp";
                code = Enumerable.Range(1, 5).FirstOrDefault(x => ButtonMask(x) == v);
                if (code == 0)
                    throw new IOException("未知鼠标宏编码");
            }
            else
                throw new IOException("当前宏包含不支持的事件，不会自动覆盖");
            result.Add(new(kind, code, data[i + 3] << 8 | data[i + 4]));
        }
        return result;
    }
}
