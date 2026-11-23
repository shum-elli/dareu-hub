using DareuLocal.Core;
using System.Text.Json;
if (args.Contains("--self-test"))
{
    void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    Check(Convert.ToHexString(JmMouse.Packet(3)) == "080300000000000000000000000000004A", "JM active packet / checksum");
    Check(Convert.ToHexString(JmMouse.Packet(8, 0, 10)) == "08080000000A000000000000000000003B", "JM memory packet");
    for (int dpi = 100; dpi <= 30000; dpi += 50)
        CheckDpi(dpi);
    void CheckDpi(int dpi)
    {
        if (JmMouse.DecodeDpi(JmMouse.EncodeDpi(dpi)) != dpi)
            throw new Exception("DPI codec " + dpi);
    }
    Check(JmMouse.DecodeDpi(JmMouse.EncodeDpi(50)) == 50, "Existing 50 DPI values can be read without broadening per-device write limits");
    Check(true, "599 DPI values round-trip, including 12800 / 12850 / 25600 / 25650 boundaries");
    foreach (var bad in new[] { 0, 99, 101, 30050 })
    {
        bool rejected = false;
        try
        {
            JmMouse.EncodeDpi(bad);
        }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Reject invalid DPI " + bad);
    }
    var p = TgKeyboard.Packet(0x10, 1, 3, 2, 8, 26, 0, 6, 0, 4, 0, 0, 0);
    Check(p.Length == 65 && Convert.ToHexString(p.AsSpan(0, 15)) == "001008010302001A00060004000000", "TG key map / target / profile / HID report framing");

    var macro = new[] { new MacroAction("KeyDown", 224, 10), new MacroAction("KeyDown", 4, 256), new MacroAction("KeyUp", 4, 65535), new MacroAction("KeyUp", 224, 0), new MacroAction("MouseDown", 5, 20), new MacroAction("MouseUp", 5, 0) };
    Check(MacroCodec.FromTg(MacroCodec.ToTg(macro)).SequenceEqual(macro), "TG macro round trip with modifiers, mouse and 16-bit delay");
    var jm = MacroCodec.ToJm(macro);
    Check(MacroCodec.FromJm(jm.Skip(1).Take(jm.Length - 2).ToArray()).SequenceEqual(macro), "JM macro round trip");
    Check(jm[0] == 6 && JmMouse.Checksum(jm.AsSpan(0, jm.Length - 1)) == jm[^1], "JM macro header and checksum");
    bool unsafeRejected = false;
    try
    {
        MacroCodec.ToTg(new[] { new MacroAction("KeyDown", 4) });
    }
    catch (ArgumentException) { unsafeRejected = true; }
    Check(unsafeRejected, "Unreleased macro keys rejected");

    var wireless = new
    {
        Name = "Receiver",
        Wireless = true
    };
    var wired = new
    {
        Name = "USB",
        Wireless = false
    };
    Check(ConnectionPreference.Preferred(new[] { wireless, wired }, x => x.Wireless)?.Name == "USB", "USB preferred when receiver was detected first");
    Check(ConnectionPreference.Preferred(new[] { wired, wireless }, x => x.Wireless)?.Name == "USB", "USB preferred regardless of enumeration order");
    Check(ConnectionPreference.Preferred(new[] { wireless }, x => x.Wireless)?.Name == "Receiver", "Wireless fallback after cable removal");
    Check(ConnectionPreference.Preferred(new[] { wireless }.Take(0), x => x.Wireless) == null, "Disconnected device has no selected interface");
    Check(PowerReading.FromReport(55, 1).IsCharging == true && PowerReading.FromReport(100, 2).IsCharging == true, "Both official charging flags handled without inferring from percentage");
    Check(PowerReading.FromReport(55, 0).IsCharging == false && PowerReading.FromReport(55, 3).IsCharging == null, "Not charging and unknown state kept distinct");
    Check(PowerReading.FromReport(255, 1).Level == null, "Invalid battery level is unknown instead of 255 percent");

    Check(DeviceCatalog.All.Count >= 200, "Official and web product records loaded");
    Check(DeviceCatalog.Find(0x0051)?.CanConfigure == true && DeviceCatalog.Find(0x0051)?.Name == "A98", "Standard A98 keyboard has separate layout adapter");
    Check(DeviceCatalog.Find(0x0107)?.CanConfigure == false, "Magnetic-axis protocol remains write-gated");
    Check(DeviceCatalog.Effective(0x1265)?.Family == DeviceCatalog.Find(0x1330)?.Family, "User AE6 profile override retained across USB and receiver PID");
    Check(DeviceCatalog.Find(0x1153)?.Family != DeviceCatalog.Find(0x1330)?.Family, "Different mice are not merged by device class");
    Check(DeviceCatalog.MatchReceiver(0x1261, 10, 36) == 0x1265, "Receiver CID / MID resolves correct device record");
    foreach (var product in DeviceCatalog.All.Where(x => x.CanConfigure))
    {
        string profile = Path.Combine(AppContext.BaseDirectory, "Profiles", $"{product.Pid:x4}.json");
        Check(File.Exists(profile), $"Layout available for {product.Name} {product.Pid:X4}");
        using var doc = JsonDocument.Parse(File.ReadAllText(profile));
        if (product.Type == 1)
        {
            var keys = doc.RootElement.GetProperty("Keys");
            if (keys.GetArrayLength() != product.KeyCount)
                throw new Exception("Key layout mismatch");
        }
    }
    var matrix = new RgbMatrix(2, 17, Enumerable.Range(0, 2 * 17 * 3).Select(i => (byte)i).ToArray());
    var frames = TgKeyboard.FramePackets(0x10, 1, matrix).ToArray();
    Check(frames.Length == 4 && frames.All(x => x.Length == 65), "RGB frames split at 16 pixels per row");
    Check(Convert.ToHexString(frames[0].AsSpan(0, 12)) == "00103603040000010100000F", "RGB frame matches official header / row / first / last columns");
    Check(frames[^1][8] == 0x81 && frames[^1][9] == 1 && frames[^1][10] == 16, "RGB final frame flag and row boundary");
    var silent = AudioFramePainter.Paint(6, 15, 0, 1, new byte[] { 255, 100, 50 }, 0.7);
    Check(silent.Colors.All(x => x == 0), "Audio silence renders black");
    var loud = AudioFramePainter.Paint(6, 15, 1, 0, new byte[] { 200, 100, 50 }, 0.5);
    Check(loud.Colors[0] == 100 && loud.Colors[1] == 50 && loud.Colors[2] == 25, "Audio brightness cap respected");
    var mouseMenu = DeviceFeatures.For(DeviceCatalog.Find(0x1330), true);
    Check(mouseMenu.Mapping && mouseMenu.Performance && !mouseMenu.Lighting && !mouseMenu.PerKeyRgb && !mouseMenu.AudioReactive, "Mouse menu excludes keyboard lighting, RGB and music");
    var keyboardMenu = DeviceFeatures.For(DeviceCatalog.Find(0x0045), true);
    Check(keyboardMenu.Lighting && keyboardMenu.PerKeyRgb && keyboardMenu.AudioReactive, "RGB keyboard exposes supported lighting pages");
    var noLighting = DeviceCatalog.Find(0x0045)! with { HasLighting = false, HasMatrix = false, HasBattery = false };
    var plainMenu = DeviceFeatures.For(noLighting, true);
    Check(plainMenu.Mapping && !plainMenu.Lighting && !plainMenu.PerKeyRgb && !plainMenu.AudioReactive && !plainMenu.Performance, "Non-lighting wired keyboard hides unavailable lighting and power pages");
    var monoMenu = DeviceFeatures.For(DeviceCatalog.Find(0x0045)! with { HasMatrix = false }, true);
    Check(monoMenu.Lighting && !monoMenu.PerKeyRgb && !monoMenu.AudioReactive, "Backlight without a matrix hides per-key RGB and music");
    var unknownMenu = DeviceFeatures.For(DeviceCatalog.Find(0x0107), false);
    Check(!unknownMenu.Mapping && !unknownMenu.Performance && !unknownMenu.Macros && !unknownMenu.Profiles, "Read-only models expose no unsupported configuration pages");
    return;
}



if (args.Contains("--rgb-inspect") || args.Contains("--rgb-roundtrip") || args.Contains("--rgb-preview"))
{
    DeviceIdentityProbe.ResolveReceivers();
    var endpoint = Hardware.PreferredEndpoints(Hardware.Endpoints()).First(x => x.CanConfigure && x.Kind == DeviceKind.Keyboard && x.Product?.HasMatrix == true);
    using var d = new TgKeyboard(endpoint);
    d.ReadState();
    var original = d.ReadLights().First(x => x.Region == 1);
    var size = d.MatrixSize();
    Console.WriteLine($"RGB {endpoint.Label}: {size.Rows} x {size.Columns}; original effect {original.Effect}");
    if (args.Contains("--rgb-preview"))
    {
        try
        {
            d.SetFrameEffect(18);
            d.SendFrame(AudioFramePainter.Paint(size.Rows, size.Columns, 0.2, 0, new byte[] { 255, 100, 40 }, 0.3));
            Thread.Sleep(400);
            Console.WriteLine("PASS temporary RGB frame transport");
        }
        finally { d.SetLight(original); }
        var after = d.ReadLights().First(x => x.Region == 1);
        if (after.Effect != original.Effect || after.Brightness != original.Brightness)
            throw new Exception("Lighting restore failed");
        Console.WriteLine("PASS original lighting restored after preview");
    }
    if (args.Contains("--rgb-roundtrip"))
    {
        RgbMatrix? backup = null;
        string backupPath = Path.Combine(Environment.CurrentDirectory, "work", "rgb-test-backup.json");
        try
        {
            d.SetFrameEffect(17);
            backup = d.ReadCustomMatrix();
            File.WriteAllText(backupPath, JsonSerializer.Serialize(new
            {
                Endpoint = endpoint.LogicalPid,
                d.Profile,
                Original = original,
                Matrix = backup
            }));
            Console.WriteLine("Saved previous custom slot 5 before test");
            var sample = AudioFramePainter.Paint(size.Rows, size.Columns, 0.2, 0, new byte[] { 255, 100, 40 }, 0.3);
            d.SaveCustomMatrix(5, sample);
            Console.WriteLine("PASS RGB custom slot write/read");
            d.SetFrameEffect(18);
            d.SendFrame(sample);
            Thread.Sleep(250);
            Console.WriteLine("PASS temporary frame transport");
        }
        finally { try { if (backup != null) d.SaveCustomMatrix(5, backup); } finally { d.SetLight(original); } }
        var restored = d.ReadLights().First(x => x.Region == 1);
        if (restored.Effect != original.Effect || restored.Brightness != original.Brightness)
            throw new Exception("Original lighting not restored");
        Console.WriteLine("PASS original custom slot and lighting restored");
    }
    return;
}
if (args.Contains("--power-only"))
{
    DeviceIdentityProbe.ResolveReceivers();
    foreach (var e in Hardware.PreferredEndpoints(Hardware.Endpoints()))
    {
        try
        {
            PowerReading p = DeviceIdentityProbe.ReadPower(e);
            Console.WriteLine(e.Label + " " + JsonSerializer.Serialize(p));
        }
        catch (Exception ex) { Console.WriteLine(e.Label + " ERROR " + ex.Message); Environment.ExitCode = 1; }
    }
    return;
}
if (args.Contains("--watch-mouse"))
{
    DeviceIdentityProbe.ResolveReceivers();
    var endpoint = Hardware.PreferredEndpoints(Hardware.Endpoints())
        .FirstOrDefault(x => x.Kind == DeviceKind.Mouse && x.CanConfigure);
    if (endpoint == null)
    {
        Console.WriteLine("未发现支持配置的 JM 鼠标，请先连接 USB 或 2.4G 接收器。");
        Environment.ExitCode = 1;
        return;
    }

    bool driverMode = args.Contains("--driver-mode");
    Console.WriteLine($"ENDPOINT {endpoint.Label} (PID {endpoint.Pid:X4}, Logical {endpoint.LogicalPid:X4})");

    MouseState before;
    using (var d = new JmMouse(endpoint))
        before = d.ReadState();
    Console.WriteLine($"BEFORE Profile={before.Profile} Stage={before.Stage} DPI={string.Join("/", before.Dpi)}");

    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancel.Cancel();
    };

    int reportLength = Math.Max(17, endpoint.Device.GetMaxInputReportLength());
    using (var stream = endpoint.Device.Open())
    {
        stream.ReadTimeout = 500;
        stream.WriteTimeout = 700;
        try
        {
            if (driverMode)
            {
                stream.Write(JmMouse.Packet(2, count: 1, payload: new byte[] { 1 }));
                Console.WriteLine("SENT SetDriverMode(1)");
            }

            Console.WriteLine("请在 20 秒内按下鼠标上的 DPI 键（按 Ctrl+C 可提前结束）...");
            while (!cancel.IsCancellationRequested)
            {
                var buf = new byte[reportLength];
                try
                {
                    int n = stream.Read(buf);
                    if (n > 0)
                    {
                        string cmd = n > 1 ? $"0x{buf[1]:X2}" : "--";
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LEN={n} ID=0x{buf[0]:X2} CMD={cmd} DATA={Convert.ToHexString(buf.AsSpan(0, n))}");
                    }
                }
                catch (TimeoutException) { }
            }
        }
        finally
        {
            if (driverMode)
            {
                try
                {
                    stream.Write(JmMouse.Packet(2, count: 1, payload: new byte[] { 0 }));
                    Console.WriteLine("SENT SetDriverMode(0)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WARN SetDriverMode(0) failed: " + ex.Message);
                }
            }
        }
    }

    using (var d = new JmMouse(endpoint))
    {
        var after = d.ReadState();
        Console.WriteLine($"AFTER  Profile={after.Profile} Stage={after.Stage} DPI={string.Join("/", after.Dpi)}");
    }
    return;
}
Console.WriteLine("ENDPOINTS " + string.Join("; ", Hardware.Endpoints().Select(x => x.Label)));
foreach (var endpoint in Hardware.Endpoints().Where(x => !x.Wireless))
{
    try
    {
        if (endpoint.Kind == DeviceKind.Mouse)
        {
            using var d = new JmMouse(endpoint);
            var before = d.ReadState();
            Console.WriteLine("MOUSE " + JsonSerializer.Serialize(before));
            if (args.Contains("--inspect"))
            {
                Console.WriteLine("SENSOR " + JsonSerializer.Serialize(d.ReadSensor()));
                for (int b = 1; b <= 6; b++)
                    Console.WriteLine($"BUTTON {b} " + Convert.ToHexString(d.ReadButton(b)));
            }
            if (args.Contains("--roundtrip"))
            {
                var sensor = d.ReadSensor();
                d.SetSensorValue("MotionSync", sensor.MotionSync);
                d.SetSensorValue("Angle", sensor.Angle);
                d.SetSensorValue("Ripple", sensor.Ripple);
                d.SetSensorValue("Response", sensor.Response);
                Console.WriteLine("PASS sensor read/write");
                for (int b = 1; b <= 6; b++)
                {
                    var value = d.ReadButton(b);
                    d.SetButton(b, value[..3]);
                }
                Console.WriteLine("PASS six mouse button assignments read/write");
                var raw = d.ReadMacroRaw(6);
                if (raw.Length == 0)
                {
                    try
                    {
                        d.WriteMacro(6, new("Test", new() { new("KeyDown", 4, 30), new("KeyUp", 4, 0) }));
                        Console.WriteLine("PASS mouse macro write/read");
                    }
                    finally { d.ClearMacro(6); }
                }
                else
                    Console.WriteLine("SKIP mouse macro slot 6 occupied");
            }
            if (args.Contains("--verify-same-value"))
            {
                d.SetDpi(before.Stage, before.Dpi[before.Stage - 1]);
                d.SetPolling((byte)before.PollingId);
                var after = d.ReadState();
                if (after.Profile != before.Profile || after.PollingId != before.PollingId || after.Stage != before.Stage || !after.Dpi.SequenceEqual(before.Dpi))
                    throw new Exception("Mouse readback mismatch");
                Console.WriteLine("PASS mouse DPI and polling write/read");
            }
        }
        else
        {
            using var d = new TgKeyboard(endpoint);
            var before = d.ReadState();
            Console.WriteLine("KEYBOARD " + JsonSerializer.Serialize(before));
            if (args.Contains("--inspect"))
            {
                Console.WriteLine("PROFILES " + string.Join(",", d.Profiles()));
                Console.WriteLine("LIGHTS " + JsonSerializer.Serialize(d.ReadLights()));
                Console.WriteLine("SLEEP " + d.SleepSeconds());
                Console.WriteLine("MACRO IDS " + Convert.ToHexString(d.MacroIds()));
            }
            if (args.Contains("--roundtrip"))
            {
                var assignment = d.ReadAssignment(26, 0);
                d.SetAssignment(26, 0, assignment);
                var fn = d.ReadAssignment(26, 1);
                d.SetAssignment(26, 1, fn);
                Console.WriteLine("PASS keyboard default / Fn assignment write/read");
                foreach (var light in d.ReadLights())
                    d.SetLight(light);
                Console.WriteLine("PASS both lighting regions write/read");
                int sec = d.SleepSeconds();
                d.SetSleep(sec);
                Console.WriteLine("PASS keyboard sleep write/read");
                foreach (var id in d.MacroIds())
                {
                    var raw = d.ReadMacroRaw(id);
                    MacroCodec.ValidateTgBlob(raw);
                    Console.WriteLine($"PASS macro blob {id}, length {raw.Length}");
                }
                var ids = d.MacroIds();
                byte free = Enumerable.Range(1, 16).Select(x => (byte)x).FirstOrDefault(x => !ids.Contains(x));
                if (free != 0)
                {
                    try
                    {
                        d.WriteMacro(free, new("Test", new() { new("KeyDown", 4, 30), new("KeyUp", 4, 0) }));
                        Console.WriteLine("PASS keyboard macro write/read");
                    }
                    finally { if (d.MacroIds().Contains(free)) d.DeleteMacro(free); }
                }
                else
                    Console.WriteLine("SKIP no free keyboard macro slot");
            }
            if (args.Contains("--verify-same-value"))
            {
                d.SetBrightness(before.Region, before.Brightness);
                var after = d.ReadState();
                if (after != before)
                    throw new Exception("Keyboard readback mismatch");
                Console.WriteLine("PASS keyboard brightness write/read");
            }
        }
    }
    catch (Exception e) { Console.WriteLine("ERROR " + e); Environment.ExitCode = 1; }
}
