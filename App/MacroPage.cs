using Windows.Storage.Pickers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DareuLocal.Core;
using System.Text.Json;
using System.Diagnostics;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    void MacroPage(StackPanel body)
    {
        Directory.CreateDirectory(Path.Combine(userFolder, "Macros"));
        var name = new TextBox { Header = "宏名称", Text = "新宏" };
        var library = new ComboBox { Header = "本地宏库", HorizontalAlignment = HorizontalAlignment.Stretch };
        var paths = Directory.GetFiles(Path.Combine(userFolder, "Macros"), "*.json");
        library.ItemsSource = paths.Select(Path.GetFileNameWithoutExtension).ToList();
        var list = new ListView { ItemsSource = macroActions, Height = 260, SelectionMode = ListViewSelectionMode.Single };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='8'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='100'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Display}'/><TextBlock Grid.Column='1' Text='{Binding Timing}'/></Grid></DataTemplate>");
        var action = Choices("动作", new[] { new Choice(0, "按下键盘键"), new Choice(1, "松开键盘键"), new Choice(2, "按下鼠标键"), new Choice(3, "松开鼠标键") }, 0);
        var key = Choices("键盘键", HidKeys(), 4);
        var button = Choices("鼠标键", new[] { new Choice(1, "左键"), new Choice(2, "右键"), new Choice(3, "中键"), new Choice(4, "后退"), new Choice(5, "前进") }, 1);
        button.Visibility = Visibility.Collapsed;
        action.SelectionChanged += (s, e) => { bool isM = Id(action) >= 2; key.Visibility = isM ? Visibility.Collapsed : Visibility.Visible; button.Visibility = isM ? Visibility.Visible : Visibility.Collapsed; };
        var delay = Number("动作之后的延时（ms）", 50, 0, 65535);
        var stepDelayBox = Number("修改选中步骤延时（ms）", 0, 0, 65535);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < macroActions.Count)
                stepDelayBox.Value = macroActions[list.SelectedIndex].Delay;
        };
        var kinds = new[] { "KeyDown", "KeyUp", "MouseDown", "MouseUp" };
        body.Children.Add(Card("宏动作",
            name,
            Text("每个按下动作都需要对应的松开动作。延时位于当前动作之后；写入前会检查完整性。"),
            list,
            Row(stepDelayBox,
                Button("更新此步延时", () =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= macroActions.Count)
                        throw new ArgumentException("请先在列表中选择一条动作");
                    macroActions[i] = macroActions[i] with { Delay = Integer(stepDelayBox) };
                    list.SelectedIndex = i;
                    return Task.CompletedTask;
                }),
                Button("批量设为统一延时", () =>
                {
                    if (macroActions.Count == 0)
                        throw new ArgumentException("当前宏动作列表为空");
                    int uniform = Integer(stepDelayBox);
                    for (int i = 0; i < macroActions.Count; i++)
                        macroActions[i] = macroActions[i] with { Delay = uniform };
                    return Task.CompletedTask;
                })),
            action, key, button, delay,
            Row(
                Button("添加动作", () => { int i = Id(action); macroActions.Add(new(kinds[i], i < 2 ? Id(key) : Id(button), Integer(delay))); return Task.CompletedTask; }),
                Button("添加完整点击（按下 + 松开）", () =>
                {
                    int i = Id(action);
                    bool isM = i >= 2;
                    int code = isM ? Id(button) : Id(key);
                    int d = Integer(delay);
                    macroActions.Add(new(isM ? "MouseDown" : "KeyDown", code, d));
                    macroActions.Add(new(isM ? "MouseUp" : "KeyUp", code, d));
                    return Task.CompletedTask;
                })),
            Row(
                Button("删除选中", () => { if (list.SelectedIndex >= 0) macroActions.RemoveAt(list.SelectedIndex); return Task.CompletedTask; }),
                Button("上移", () => { int i = list.SelectedIndex; if (i > 0) { macroActions.Move(i, i - 1); list.SelectedIndex = i - 1; } return Task.CompletedTask; }),
                Button("下移", () => { int i = list.SelectedIndex; if (i >= 0 && i < macroActions.Count - 1) { macroActions.Move(i, i + 1); list.SelectedIndex = i + 1; } return Task.CompletedTask; })),
            Button("清空动作", () => { macroActions.Clear(); return Task.CompletedTask; })));

        bool recording = false;
        var heldKeys = new HashSet<int>();
        var heldMouse = new HashSet<int>();
        long stamp = 0;
        var delayMode = Choices("录制延时模式", new[] { new Choice(0, "真实延时"), new Choice(1, "固定延时"), new Choice(2, "无延时 (0 ms)") }, 0);
        var fixedDelay = Number("固定延时（ms）", 30, 0, 65535);
        fixedDelay.Visibility = Visibility.Collapsed;
        delayMode.SelectionChanged += (_, _) => { fixedDelay.Visibility = Id(delayMode) == 1 ? Visibility.Visible : Visibility.Collapsed; };

        var recordBox = new TextBox { PlaceholderText = "开始录制后，在此框内按下键盘或点击鼠标按键", AcceptsReturn = true, Height = 90 };
        var recordStatus = Text("只在此输入框聚焦时记录键盘与鼠标按键，不会监听其他应用。");

        void AddRecorded(string kind, int code)
        {
            long now = Stopwatch.GetTimestamp();
            if (macroActions.Count > 0 && stamp != 0)
            {
                int mode = Id(delayMode);
                int elapsed = mode switch
                {
                    1 => Math.Clamp((int)Math.Round(double.IsFinite(fixedDelay.Value) ? fixedDelay.Value : 30), 0, 65535),
                    2 => 0,
                    _ => (int)Math.Min(65535, Stopwatch.GetElapsedTime(stamp, now).TotalMilliseconds)
                };
                macroActions[^1] = macroActions[^1] with { Delay = elapsed };
            }
            macroActions.Add(new(kind, code));
            stamp = now;
        }
        void Stop()
        {
            if (!recording)
                return;
            recording = false;
            foreach (int k in heldKeys)
                AddRecorded("KeyUp", k);
            heldKeys.Clear();
            foreach (int mb in heldMouse)
                AddRecorded("MouseUp", mb);
            heldMouse.Clear();
            recordStatus.Text = "录制已停止，可编辑动作或延时后保存。";
        }
        recordBox.KeyDown += (s, e) => { if (!recording) return; e.Handled = true; int hid = VirtualToHid((int)e.Key); if (hid != 0 && heldKeys.Add(hid)) AddRecorded("KeyDown", hid); };
        recordBox.KeyUp += (s, e) => { if (!recording) return; e.Handled = true; int hid = VirtualToHid((int)e.Key); if (heldKeys.Remove(hid)) AddRecorded("KeyUp", hid); };
        recordBox.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!recording) return;
            int mb = PointerKindToMouseButton(e.GetCurrentPoint(recordBox).Properties.PointerUpdateKind);
            if (mb != 0 && heldMouse.Add(mb))
            {
                AddRecorded("MouseDown", mb);
                e.Handled = true;
            }
        }), true);
        recordBox.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (!recording) return;
            int mb = PointerKindToMouseButton(e.GetCurrentPoint(recordBox).Properties.PointerUpdateKind);
            if (mb != 0 && heldMouse.Remove(mb))
            {
                AddRecorded("MouseUp", mb);
                e.Handled = true;
            }
        }), true);
        recordBox.LostFocus += (s, e) => Stop();

        body.Children.Add(Card("键盘与鼠标录制",
            delayMode,
            fixedDelay,
            recordStatus,
            recordBox,
            Row(
                Button("开始录制", () =>
                {
                    recording = true;
                    stamp = 0;
                    recordStatus.Text = "正在录制：按键与框内鼠标点击会记录到宏列表。";
                    recordBox.Focus(FocusState.Programmatic);
                    return Task.CompletedTask;
                }),
                Button("停止录制", () => { Stop(); return Task.CompletedTask; }))));

        DeviceMacro Current()
        {
            Stop();
            var m = new DeviceMacro(name.Text.Trim(), macroActions.ToList());
            if (string.IsNullOrWhiteSpace(m.Name))
                throw new ArgumentException("请输入宏名称");
            MacroCodec.Validate(m.Actions);
            return m;
        }
        void Load(DeviceMacro macro)
        {
            name.Text = macro.Name;
            macroActions.Clear();
            foreach (var a in macro.Actions)
                macroActions.Add(a);
        }
        body.Children.Add(Card("本地宏库", library, ActionButton("载入选中的本地宏", () => { if (library.SelectedIndex < 0) throw new ArgumentException("请选择本地宏"); Load(JsonSerializer.Deserialize<DeviceMacro>(File.ReadAllText(paths[library.SelectedIndex]), Json)!); return Task.CompletedTask; }), ActionButton("保存到本地宏库", () => { var m = Current(); string stem = string.Concat(m.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim('.', ' '); if (stem.Length == 0) stem = "Macro"; File.WriteAllText(Path.Combine(userFolder, "Macros", stem + ".json"), JsonSerializer.Serialize(m, Json)); paths = Directory.GetFiles(Path.Combine(userFolder, "Macros"), "*.json"); library.ItemsSource = paths.Select(Path.GetFileNameWithoutExtension).ToList(); return Task.CompletedTask; }), Row(ActionButton("导出宏文件", async () => { var m = Current(); string? path = await PickSave("Macro", ".json"); if (path != null) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(m, Json)); }), ActionButton("导入宏文件", async () => { string? path = await PickOpen(); if (path != null) { var m = JsonSerializer.Deserialize<DeviceMacro>(await File.ReadAllTextAsync(path), Json) ?? throw new ArgumentException("宏文件无效"); MacroCodec.Validate(m.Actions); Load(m); } }))));
        var slot = Number(IsMouse ? "鼠标槽位（与按键一一对应）" : "键盘宏槽位", 1, 1, IsMouse ? (endpoint?.Product?.ButtonCount ?? 6) : 16);
        var repeat = Number("鼠标宏重复次数", 1, 1, 250);
        body.Children.Add(Card("设备宏", Text(IsMouse ? $"鼠标有 {endpoint?.Product?.ButtonCount ?? 6} 个对应按键的宏槽位，每个最多 70 个动作。上传后可绑定到同编号按键。" : "上传到键盘宏槽位后，在按键映射页选择宏并指定槽位与触发模式。"), slot, ActionButton("从设备读取此槽位", async () => { int id = Integer(slot); DeviceMacro m = IsMouse ? await WithMouse(d => new DeviceMacro($"Macro {id}", MacroCodec.FromJm(d.ReadMacroRaw(id)))) : await WithKeyboard(d => d.ReadMacro((byte)id)); Load(m); }), ActionButton("上传宏到设备", async () => { var m = Current(); int id = Integer(slot); await Confirm("写入设备宏", $"将覆盖槽位 {id}。本地宏库中的内容不会被删除。", async () => { await SaveMacroBackup(id); if (IsMouse) await MouseWrite(d => d.WriteMacro(id, m)); else await KeyWrite(d => d.WriteMacro((byte)id, m)); }); }, true), repeat, ActionButton("将此槽位绑定到对应鼠标键", async () => { if (!IsMouse) throw new ArgumentException("键盘请在按键映射页绑定宏"); int id = Integer(slot), times = Integer(repeat); await MouseWrite(d => d.BindMacro(id, times)); })));
    }

    static int PointerKindToMouseButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => 1,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 2,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 3,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => 4,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => 5,
        _ => 0
    };

    static int VirtualToHid(int key) => key switch
    {
        >= 65 and <= 90 => key - 65 + 4,
        >= 49 and <= 57 => key - 49 + 30,
        48 => 39,
        >= 112 and <= 123 => key - 112 + 58,
        13 => 40,
        27 => 41,
        8 => 42,
        9 => 43,
        32 => 44,
        189 => 45,
        187 => 46,
        219 => 47,
        221 => 48,
        220 => 49,
        186 => 51,
        222 => 52,
        192 => 53,
        188 => 54,
        190 => 55,
        191 => 56,
        20 => 57,
        44 => 70,
        145 => 71,
        19 => 72,
        45 => 73,
        36 => 74,
        33 => 75,
        46 => 76,
        35 => 77,
        34 => 78,
        39 => 79,
        37 => 80,
        40 => 81,
        38 => 82,
        17 or 162 => 224,
        16 or 160 => 225,
        18 or 164 => 226,
        91 => 227,
        163 => 228,
        161 => 229,
        165 => 230,
        92 => 231,
        _ => 0
    };

    async Task SaveMacroBackup(int slot)
    {
        Directory.CreateDirectory(Path.Combine(userFolder, "Backups"));
        byte[] raw = IsMouse ? await WithMouse(d => d.ReadMacroRaw(slot)) : await WithKeyboard(d => d.ReadMacroRaw((byte)slot));
        await File.WriteAllTextAsync(Path.Combine(userFolder, "Backups", $"macro-{(IsMouse ? "mouse" : "keyboard")}-{slot}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json"), JsonSerializer.Serialize(new
        {
            Slot = slot,
            Device = IsMouse ? "Mouse" : "Keyboard",
            Raw = raw
        }, Json));
    }

    async Task<string?> PickSave(string name, string extension, string fileTypeLabel = "JSON 文件")
    {
        var picker = new FileSavePicker { SuggestedFileName = name };
        picker.FileTypeChoices.Add(fileTypeLabel, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSaveFileAsync())?.Path;
    }

    async Task<string?> PickOpen(string extension = ".json")
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
