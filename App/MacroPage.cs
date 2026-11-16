using Windows.Storage.Pickers;
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
        action.SelectionChanged += (s, e) => { bool mouse = Id(action) >= 2; key.Visibility = mouse ? Visibility.Collapsed : Visibility.Visible; button.Visibility = mouse ? Visibility.Visible : Visibility.Collapsed; };
        var delay = Number("动作之后的延时（ms）", 50, 0, 65535);
        var kinds = new[] { "KeyDown", "KeyUp", "MouseDown", "MouseUp" };
        body.Children.Add(Card("宏动作", name, Text("每个按下动作都需要对应的松开动作。延时位于当前动作之后；写入前会检查完整性。"), list, action, key, button, delay, Button("添加动作", () => { int i = Id(action); macroActions.Add(new(kinds[i], i < 2 ? Id(key) : Id(button), Integer(delay))); return Task.CompletedTask; }), Row(Button("删除选中", () => { if (list.SelectedIndex >= 0) macroActions.RemoveAt(list.SelectedIndex); return Task.CompletedTask; }), Button("上移", () => { int i = list.SelectedIndex; if (i > 0) { macroActions.Move(i, i - 1); list.SelectedIndex = i - 1; } return Task.CompletedTask; }), Button("下移", () => { int i = list.SelectedIndex; if (i >= 0 && i < macroActions.Count - 1) { macroActions.Move(i, i + 1); list.SelectedIndex = i + 1; } return Task.CompletedTask; })), Button("清空动作", () => { macroActions.Clear(); return Task.CompletedTask; })));
        bool recording = false;
        var held = new HashSet<int>();
        long stamp = 0;
        var recordBox = new TextBox { PlaceholderText = "开始录制后，点击这里输入按键", AcceptsReturn = true, Height = 80 };
        var recordStatus = Text("只在此输入框聚焦时记录，不会监听其他应用。");
        void AddRecorded(string kind, int code)
        {
            long now = Stopwatch.GetTimestamp();
            if (macroActions.Count > 0 && stamp != 0)
            {
                int elapsed = (int)Math.Min(65535, Stopwatch.GetElapsedTime(stamp, now).TotalMilliseconds);
                macroActions[^1] = macroActions[^1] with
                {
                    Delay = elapsed
                };
            }
            macroActions.Add(new(kind, code));
            stamp = now;
        }
        void Stop()
        {
            if (!recording)
                return;
            recording = false;
            foreach (int k in held)
                AddRecorded("KeyUp", k);
            held.Clear();
            recordStatus.Text = "录制已停止，可编辑动作后保存。";
        }
        recordBox.KeyDown += (s, e) => { if (!recording) return; e.Handled = true; int hid = VirtualToHid((int)e.Key); if (hid != 0 && held.Add(hid)) AddRecorded("KeyDown", hid); };
        recordBox.KeyUp += (s, e) => { if (!recording) return; e.Handled = true; int hid = VirtualToHid((int)e.Key); if (held.Remove(hid)) AddRecorded("KeyUp", hid); };
        recordBox.LostFocus += (s, e) => Stop();
        body.Children.Add(Card("键盘录制", recordStatus, recordBox, Row(Button("开始录制", () => { recording = true; stamp = 0; recordStatus.Text = "正在录制：按键仅记录到宏列表。"; recordBox.Focus(FocusState.Programmatic); return Task.CompletedTask; }), Button("停止录制", () => { Stop(); return Task.CompletedTask; }))));
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
        body.Children.Add(Card("设备宏", Text(IsMouse ? $"鼠标有 {endpoint?.Product?.ButtonCount ?? 6} 个对应按键的宏槽位，每个最多 70 个动作。上传后可绑定到同编号按键。" : "上传到键盘宏槽位后，在按键映射页选择宏并指定槽位。"), slot, ActionButton("从设备读取此槽位", async () => { int id = Integer(slot); DeviceMacro m = IsMouse ? await WithMouse(d => new DeviceMacro($"Macro {id}", MacroCodec.FromJm(d.ReadMacroRaw(id)))) : await WithKeyboard(d => d.ReadMacro((byte)id)); Load(m); }), ActionButton("上传宏到设备", async () => { var m = Current(); int id = Integer(slot); await Confirm("写入设备宏", $"将覆盖槽位 {id}。本地宏库中的内容不会被删除。", async () => { await SaveMacroBackup(id); if (IsMouse) await MouseWrite(d => d.WriteMacro(id, m)); else await KeyWrite(d => d.WriteMacro((byte)id, m)); }); }, true), repeat, ActionButton("将此槽位绑定到对应鼠标键", async () => { if (!IsMouse) throw new ArgumentException("键盘请在按键映射页绑定宏"); int id = Integer(slot), times = Integer(repeat); await MouseWrite(d => d.BindMacro(id, times)); })));
    }
    static int VirtualToHid(int key) => key switch { >= 65 and <= 90 => key - 65 + 4, >= 49 and <= 57 => key - 49 + 30, 48 => 39, >= 112 and <= 123 => key - 112 + 58, 13 => 40, 27 => 41, 8 => 42, 9 => 43, 32 => 44, 16 => 225, 17 => 224, 18 => 226, 91 => 227, 92 => 231, 37 => 80, 38 => 82, 39 => 79, 40 => 81, 46 => 76, _ => 0 };
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
    async Task<string?> PickSave(string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = name };
        picker.FileTypeChoices.Add("JSON 文件", new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSaveFileAsync())?.Path;
    }
    async Task<string?> PickOpen()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
