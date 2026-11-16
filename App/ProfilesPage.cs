using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DareuLocal.Core;
using System.Text.Json;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    void Profiles(StackPanel body)
    {
        var profile = Choices("目标板载配置", (IsMouse ? Enumerable.Range(1, 4) : keyboardProfiles).Select(i => new Choice(i, $"Profile {i}")), IsMouse ? mouse!.Profile : keyboard!.Profile);
        body.Children.Add(Card("板载配置", Text("切换后会重新读取该配置。导入备份仅写入当前板载配置。"), profile, ActionButton("切换配置", async () => { int id = Id(profile); if (IsMouse) await MouseWrite(d => { d.SetProfile(id); if (d.ReadState().Profile != id) throw new IOException("配置切换读回不一致"); }); else await KeyWrite(d => { if (!d.Profiles().Contains(id)) throw new ArgumentException("设备不支持此配置编号"); d.SetProfile(id); if (d.ReadState().Profile != id) throw new IOException("配置切换读回不一致"); }); await Connect(); }, true)));
        body.Children.Add(Card("备份与恢复", Text("备份包含当前配置的按键、灯效、性能设置及设备宏；逐键 RGB 颜色方案请在逐键 RGB 页单独导出。导入前会先保存一份当前状态，避免覆盖后无法找回。"), ActionButton("导出当前设备配置", async () => { string? path = await PickSave((endpoint?.Product?.Model ?? "DAREU") + "-backup", ".json"); if (path == null) return; var backup = await CaptureBackup(); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup, Json)); }), ActionButton("导入配置备份", async () => { string? path = await PickOpen(); if (path == null) return; var backup = JsonSerializer.Deserialize<DeviceBackup>(await File.ReadAllTextAsync(path), Json) ?? throw new ArgumentException("备份文件无效"); ValidateBackup(backup); await Confirm("恢复设备配置", $"将把 {backup.Device} 备份写入当前 Profile，并覆盖备份中对应的设置和宏槽位。继续前会自动备份当前状态。", async () => { var current = await CaptureBackup(); await SaveAutomatic(current); await RestoreBackup(backup); await Connect(); }); }), Text("自动备份位置：" + Path.Combine(userFolder, "Backups"))));
        body.Children.Add(Card("恢复默认", Text("仅在需要重置时使用。应用会在重置前自动备份当前状态。"), ActionButton(IsMouse ? "恢复鼠标出厂设置" : "恢复当前键盘配置", () => Confirm("恢复默认设置", "会覆盖设备当前设置。请确认已准备好恢复默认。", async () => { await SaveAutomatic(await CaptureBackup()); if (IsMouse) await MouseWrite(d => d.FactoryReset()); else await KeyWrite(d => d.ResetProfile()); await Connect(); }))));
    }
    async Task<DeviceBackup> CaptureBackup()
    {
        if (IsMouse)
            return await WithMouse(d => { var state = d.ReadState(); var sensor = d.ReadSensor(); var buttons = Enumerable.Range(1, d.Endpoint.Product!.ButtonCount).Select(d.ReadButton).ToArray(); var keyData = Enumerable.Range(1, d.Endpoint.Product!.ButtonCount).Select(i => d.ReadMemory((ushort)(256 + 32 * (i - 1)), 32)).ToArray(); var macros = Enumerable.Range(1, d.Endpoint.Product!.ButtonCount).ToDictionary(i => i, d.ReadMacroRaw); return new DeviceBackup(2, "Mouse", d.Endpoint.Product!.Pid, state.Profile, state, sensor, buttons, keyData, null, null, state.SleepSeconds, macros); });
        return await WithKeyboard(d => { var state = d.ReadState(); var keys = new List<KeyBackup>(); foreach (var key in keyLayout) for (byte layer = 0; layer < 2; layer++) keys.Add(new(key.Id, layer, d.ReadAssignment(key.Id, layer))); var macros = d.MacroIds().ToDictionary(i => (int)i, i => d.ReadMacroRaw(i)); return new DeviceBackup(2, "Keyboard", state.Pid, state.Profile, null, null, null, null, keys, d.ReadLights(), d.SleepSeconds(), macros); });
    }
    Task SaveAutomatic(DeviceBackup backup)
    {
        Directory.CreateDirectory(Path.Combine(userFolder, "Backups"));
        return File.WriteAllTextAsync(Path.Combine(userFolder, "Backups", $"{backup.Device}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json"), JsonSerializer.Serialize(backup, Json));
    }
    void ValidateBackup(DeviceBackup b)
    {
        if (b.Version != 2 || b.Device != (IsMouse ? "Mouse" : "Keyboard") || DeviceCatalog.Effective(b.Pid)?.Family != endpoint?.Product?.Family)
            throw new ArgumentException("备份格式或设备类型不匹配");
        if (b.Macros == null)
            throw new ArgumentException("备份缺少宏数据");
        foreach (var pair in b.Macros)
        {
            if (pair.Key < 1 || pair.Key > (IsMouse ? (endpoint?.Product?.ButtonCount ?? 6) : 16) || pair.Value.Length > 8192)
                throw new ArgumentException("宏槽位或长度无效");
            if (pair.Value.Length > 0)
            {
                if (IsMouse)
                    MacroCodec.ValidateJmBlob(pair.Value);
                else
                    MacroCodec.ValidateTgBlob(pair.Value);
            }
        }
        if (IsMouse)
        {
            var m = b.Mouse ?? throw new ArgumentException("备份缺少鼠标设置");
            if (b.Sensor is { } sensor && (sensor.MotionSync is < 0 or > 1 || sensor.Lod is < 0 or > 3 || sensor.Response is < 0 or > 30 || sensor.Angle is < 0 or > 1 || sensor.Ripple is < 0 or > 1 || sensor.Mode is < 0 or > 2))
                throw new ArgumentException("传感器备份数值超出范围");
            if (m.Dpi == null || m.Dpi.Length < 1 || m.Dpi.Length > Math.Clamp(endpoint?.Product?.StageCount ?? 5, 1, 7) || m.Stage < 1 || m.Stage > m.Dpi.Length || !new[] { 8, 4, 2, 1, 16, 32, 64 }.Contains(m.PollingId) || m.SleepSeconds < 0 || m.SleepSeconds > 2550 || m.SleepSeconds % 10 != 0)
                throw new ArgumentException("鼠标性能备份无效");
            foreach (int v in m.Dpi)
            {
                JmMouse.EncodeDpi(v);
                if (v < Math.Max(50, endpoint!.Product!.DpiMin) || v > Math.Min(30000, endpoint.Product.DpiMax))
                    throw new ArgumentException("备份 DPI 超出此型号范围");
            }
            if (b.Buttons?.Length != endpoint!.Product!.ButtonCount || b.ButtonKeys?.Length != endpoint.Product.ButtonCount || b.Buttons.Any(x => x.Length != 4) || b.ButtonKeys.Any(x => x.Length != 32))
                throw new ArgumentException("按键备份长度无效");
        }
        else
        {
            if (b.Keys == null || b.Lights == null || b.Sleep < 0 || b.Sleep > 65535)
                throw new ArgumentException("键盘备份数据不完整");
            if (b.Keys.Count != keyLayout.Count * 2 || b.Keys.Select(x => (x.Key, x.Layer)).Distinct().Count() != b.Keys.Count || b.Keys.Any(x => x.Layer > 1 || !keyLayout.Any(k => k.Id == x.Key) || x.Value.Data.Length != 5))
                throw new ArgumentException("键盘按键备份无效");
            if (b.Lights.Any(x => x.Colors.Length > 17 || x.Colors.Any(c => c.Length != 3) || !x.Effects.Contains(x.Effect) || x.Brightness < 0 || x.Brightness > 255 || x.Speed < 0 || x.Speed > 255))
                throw new ArgumentException("灯光备份无效");
        }
    }
    async Task RestoreBackup(DeviceBackup b)
    {
        ValidateBackup(b);
        if (IsMouse)
        {
            await MouseWrite(d => { var m = b.Mouse!; d.SetStageCount(m.Dpi.Length); for (int i = 0; i < m.Dpi.Length; i++) d.SetDpi(i + 1, m.Dpi[i]); d.SetStage(m.Stage); d.SetPolling((byte)m.PollingId); d.SetSleep(m.SleepSeconds); if (b.Sensor != null) d.RestoreSensor(b.Sensor); for (int i = 1; i <= d.Endpoint.Product!.ButtonCount; i++) d.RestoreButton(i, b.Buttons![i - 1], b.ButtonKeys![i - 1]); foreach (var pair in b.Macros!) if (pair.Value.Length > 0) d.WriteMacroRaw(pair.Key, pair.Value); else d.ClearMacro(pair.Key); var actual = d.ReadState(); if (!actual.Dpi.SequenceEqual(m.Dpi) || actual.Stage != m.Stage || actual.PollingId != m.PollingId || actual.SleepSeconds != m.SleepSeconds) throw new IOException("恢复后的鼠标性能设置读回不一致"); });
        }
        else
            await KeyWrite(d => { var supported = d.ReadLights(); foreach (var light in b.Lights!) { var match = supported.FirstOrDefault(x => x.Region == light.Region) ?? throw new ArgumentException("设备不支持备份中的灯光区域"); if (!match.Effects.Contains(light.Effect)) throw new ArgumentException("设备不支持备份中的灯效"); } foreach (var key in b.Keys!) d.SetAssignment(key.Key, key.Layer, key.Value); foreach (var light in b.Lights!) d.SetLight(light); d.SetSleep(b.Sleep); foreach (var pair in b.Macros!) if (pair.Value.Length > 0) d.WriteMacroRaw((byte)pair.Key, $"Macro {pair.Key}", pair.Value); });
    }
    void SettingsPage(StackPanel body)
    {
        body.Children.Add(Card("Dareu Local " + typeof(App).Assembly.GetName().Version?.ToString(3), Text("原生 WinUI 3 · 设备设置在本机完成 · 无需浏览器"), Text("已接入多型号设备资料与按能力适配。AE6 V2 继续按用户指定的 1330 参数工作；其他型号分别加载自身布局与能力。"), Text("已加入逐键 RGB 与系统音频律动。蓝牙、磁轴 RT/键程、LCD 与固件刷写仍待适配；其他型号的基础设置标注为协议适配，尚需实机验证。")));
        body.Children.Add(Card("连接的接口", Text(string.Join("\n", Hardware.Endpoints().Select(x => x.Label))), Button("重新扫描", () => { deviceSignature = ""; Scan(); return Task.CompletedTask; })));
        body.Children.Add(Card("固件", Text("读取版本信息不会升级或修改固件。固件更新请使用对应型号官方工具。"), ActionButton("读取当前固件版本", async () => { int version = IsMouse ? await WithMouse(d => d.Firmware()) : await WithKeyboard(d => d.Firmware()); AddLog("固件版本：" + version.ToString("X6")); var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "当前固件版本", Content = version.ToString("X6"), CloseButtonText = "关闭", RequestedTheme = Root.RequestedTheme }; await dialog.ShowAsync(); })));
        body.Children.Add(Card("通信诊断", new TextBox { Text = log, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 320 }, ActionButton("保存诊断记录", async () => { Directory.CreateDirectory(userFolder); await File.WriteAllTextAsync(Path.Combine(userFolder, "diagnostics.txt"), log); }), Text("记录保存在本机：" + Path.Combine(userFolder, "diagnostics.txt"))));
    }
}
