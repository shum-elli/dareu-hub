using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DareuLocal.Core;
using Windows.UI;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    static List<Choice> HidKeys()
    {
        var v = new List<Choice>();
        for (int i = 0; i < 26; i++)
            v.Add(new(4 + i, ((char)('A' + i)).ToString()));
        for (int i = 1; i <= 9; i++)
            v.Add(new(29 + i, i.ToString()));
        v.Add(new(39, "0"));
        v.AddRange(new[] { new Choice(40, "Enter"), new Choice(41, "Esc"), new Choice(42, "Backspace"), new Choice(43, "Tab"), new Choice(44, "Space"), new Choice(45, "-"), new Choice(46, "="), new Choice(47, "["), new Choice(48, "]"), new Choice(49, "\\"), new Choice(51, ";"), new Choice(52, "'"), new Choice(53, "`"), new Choice(54, ","), new Choice(55, "."), new Choice(56, "/"), new Choice(57, "Caps Lock") });
        for (int i = 1; i <= 12; i++)
            v.Add(new(57 + i, $"F{i}"));
        v.AddRange(new[] { new Choice(70, "Print Screen"), new Choice(71, "Scroll Lock"), new Choice(72, "Pause"), new Choice(73, "Insert"), new Choice(74, "Home"), new Choice(75, "Page Up"), new Choice(76, "Delete"), new Choice(77, "End"), new Choice(78, "Page Down"), new Choice(79, "→"), new Choice(80, "←"), new Choice(81, "↓"), new Choice(82, "↑"), new Choice(224, "Left Ctrl"), new Choice(225, "Left Shift"), new Choice(226, "Left Alt"), new Choice(227, "Left Win"), new Choice(228, "Right Ctrl"), new Choice(229, "Right Shift"), new Choice(230, "Right Alt"), new Choice(231, "Right Win") });
        return v;
    }
    void Mapping(StackPanel body)
    {
        bool isMouse = IsMouse;
        var owner = endpoint;
        int selected = isMouse ? 1 : keyLayout.Any(k => k.Id == 26) ? 26 : keyLayout[0].Id;
        var selectedLabel = Text(isMouse ? "左键" : keyLayout.First(k => k.Id == selected).Label, 20);
        var current = Text("正在读取当前映射…");
        var original = Text("", 12);
        var layer = new RadioButtons { ItemsSource = new[] { "标准模式", "Fn 模式" }, MaxColumns = 2, SelectedIndex = 0,
            Visibility = isMouse ? Visibility.Collapsed : Visibility.Visible };
        var functions = new[] { new Choice(6, "键盘按键 / 快捷键"), new Choice(1, "鼠标功能"), new Choice(8, "多媒体"),
            new Choice(12, "宏"), new Choice(0, "禁用"), new Choice(2, "双击左键"), new Choice(19, "DPI 循环"),
            new Choice(16, "DPI 增加"), new Choice(17, "DPI 减少"), new Choice(27, "切换配置"), new Choice(30, "切换灯光"), new Choice(47, "锁定 Win 键") };
        var allowed = functions.Where(x => (isMouse ? x.Id != 47 : x.Id is not (16 or 17 or 19)) &&
            (x.Id != 30 || owner?.Product?.HasLighting == true));
        var type = Choices("功能分类", allowed);
        var keyCode = Choices("目标按键", HidKeys());
        var mouseCode = Choices("鼠标操作", new[] { new Choice(1, "左键"), new Choice(2, "右键"), new Choice(3, "中键"), new Choice(4, "后退"), new Choice(5, "前进") });
        var media = Choices("多媒体操作", new[] { new Choice(0xe9, "音量增加"), new Choice(0xea, "音量减少"), new Choice(0xe2, "静音"), new Choice(0xcd, "播放 / 暂停"), new Choice(0xb5, "下一曲"), new Choice(0xb6, "上一曲") });
        var slot = Number("宏槽位", 1, 1, isMouse ? 6 : 16);
        var repeat = Number("重复次数", 1, 1, 250);
        var ctrl = new CheckBox { Content = "Ctrl" }; var shift = new CheckBox { Content = "Shift" };
        var alt = new CheckBox { Content = "Alt" }; var win = new CheckBox { Content = "Win" };
        var mods = Row(ctrl, shift, alt, win);
        var fields = Column(8);
        foreach (var element in new UIElement[] { keyCode, mouseCode, media, slot, repeat }) fields.Children.Add(element);
        Func<Task>? undo = null;
        Button save = null!;
        Button undoButton = null!;
        bool Here() => page == "mapping" && ReferenceEquals(endpoint, owner) && ReferenceEquals(WorkArea.Content, body);
        void Update()
        {
            int f = type.SelectedItem is Choice c ? c.Id : -1;
            keyCode.Visibility = mods.Visibility = f == 6 ? Visibility.Visible : Visibility.Collapsed;
            mouseCode.Visibility = f == 1 ? Visibility.Visible : Visibility.Collapsed;
            media.Visibility = f == 8 ? Visibility.Visible : Visibility.Collapsed;
            slot.Visibility = repeat.Visibility = f == 12 ? Visibility.Visible : Visibility.Collapsed;
            if (isMouse) { slot.Header = "宏槽位（跟随所选按键）"; slot.Value = selected; slot.IsEnabled = false; }
            if (save != null) save.IsEnabled = f >= 0;
        }
        void Choose(ComboBox box, int id) => box.SelectedItem = ((List<Choice>)box.ItemsSource).FirstOrDefault(c => c.Id == id);
        void Populate(Assignment a)
        {
            Choose(type, a.Function);
            ctrl.IsChecked = (a.Data[0] & 1) != 0; shift.IsChecked = (a.Data[0] & 2) != 0;
            alt.IsChecked = (a.Data[0] & 4) != 0; win.IsChecked = (a.Data[0] & 8) != 0;
            if (a.Function == 6) { Choose(keyCode, a.Data[1]); if (keyCode.SelectedItem == null || (a.Data[0] & 240) != 0) type.SelectedItem = null; }
            if (a.Function == 1) { Choose(mouseCode, a.Data[0]); if (mouseCode.SelectedItem == null) type.SelectedItem = null; }
            if (a.Function == 8) { Choose(media, a.Data[1]); if (media.SelectedItem == null) type.SelectedItem = null; }
            if (a.Function == 12)
            {
                int times = (a.Data[1] << 8) | a.Data[2];
                if (a.Data[0] is < 1 or > 16 || times is < 1 or > 250) type.SelectedItem = null;
                else { slot.Value = a.Data[0]; repeat.Value = times; }
            }
            Update();
        }
        async Task ReadSelected()
        {
            if (!Here()) return;
            int key = selected, selectedLayer = layer.SelectedIndex;
            save.IsEnabled = false; current.Text = "正在读取当前映射…"; type.SelectedItem = null;
            try
            {
                if (isMouse)
                {
                    var raw = await WithMouse(d => d.ReadButton(key));
                    if (!Here() || selected != key) return;
                    current.Text = "当前映射：" + DescribeMouse(raw);
                    original.Text = "";
                    int f = raw[0] switch { 0 => 0, 1 => 1, 2 => raw[1] switch { 1 => 19, 2 => 16, 3 => 17, _ => -1 }, 4 when raw[2] == 2 => 2, 6 => 12, 8 => 30, 9 => 27, _ => -1 };
                    Choose(type, f);
                    if (f == 1) { int id = Array.IndexOf(new byte[] { 1, 2, 4, 8, 16 }, raw[1]) + 1; Choose(mouseCode, id); if (id == 0) type.SelectedItem = null; }
                    if (f == 12) { slot.Value = key; repeat.Value = raw[2]; }
                    // Existing shortcut blobs can contain combinations not represented by this editor.
                    if (f < 0) current.Text += "；选择功能分类可重新设置。";
                }
                else
                {
                    var value = await WithKeyboard(d => d.ReadAssignment((byte)key, (byte)selectedLayer));
                    if (!Here() || selected != key || layer.SelectedIndex != selectedLayer) return;
                    current.Text = "当前映射：" + Describe(value);
                    var layoutKey = keyLayout.First(x => x.Id == key);
                    original.Text = "出厂映射：" + Describe(selectedLayer == 0 ? layoutKey.Default : layoutKey.FnDefault);
                    Populate(value);
                    if (type.SelectedItem == null) current.Text += "；此映射可查看，选择功能分类可重新设置。";
                }
                Update();
            }
            catch { current.Text = "当前映射读取失败，请重试；尚未写入设置。"; throw; }
        }
        Task SelectKey(int id, string label)
        {
            selected = id; selectedLabel.Text = label;
            return Run(ReadSelected, "已读取所选按键映射");
        }
        type.SelectionChanged += (_, _) => Update();
        layer.SelectionChanged += async (_, _) => { if (Here()) await Run(ReadSelected, "已读取此层映射"); };
        undoButton = ActionButton("撤销上一次改键", async () =>
        {
            if (undo == null) return;
            await undo(); undo = null; undoButton.IsEnabled = false; await ReadSelected();
        });
        undoButton.IsEnabled = false;
        save = ActionButton("保存映射", async () =>
        {
            Func<Task>? nextUndo = null;
            byte modifier = (byte)((ctrl.IsChecked == true ? 1 : 0) | (shift.IsChecked == true ? 2 : 0) | (alt.IsChecked == true ? 4 : 0) | (win.IsChecked == true ? 8 : 0));
            int f = Id(type);
            if (IsMouse)
            {
                int b = selected;
                var old = await WithMouse(d => d.ReadButton(b));
                var oldKeys = await WithMouse(d => d.ReadMemory((ushort)(256 + 32 * (b - 1)), 32));
                nextUndo = () => MouseWrite(d => d.RestoreButton(b, old, oldKeys));
                if (f == 6)
                {
                    byte code = (byte)Id(keyCode);
                    await MouseWrite(d => d.SetButtonKey(b, code, modifier));
                }
                else if (f == 8)
                {
                    ushort code = (ushort)Id(media);
                    await MouseWrite(d => d.SetButtonMedia(b, code));
                }
                else if (f == 12)
                {
                    if (Integer(slot) != b)
                        throw new ArgumentException("鼠标宏槽位与按键一一对应，请把目标槽位设为当前按键编号 " + b);
                    int times = Integer(repeat);
                    await MouseWrite(d => d.BindMacro(b, times));
                }
                else
                {
                    byte[] data = f switch
                    {
                        0 => new byte[3],
                        1 => new byte[] { 1, (byte)(1 << (Id(mouseCode) - 1)), 0 },
                        2 => new byte[] { 4, 10, 2 },
                        19 => new byte[] { 2, 1, 0 },
                        16 => new byte[] { 2, 2, 0 },
                        17 => new byte[] { 2, 3, 0 },
                        27 => new byte[] { 9, 0, 0 },
                        30 => new byte[] { 8, 4, 0 },
                        _ => throw new ArgumentException("鼠标不支持此功能")
                    };
                    await MouseWrite(d => d.SetButton(b, data));
                }
            }
            else
            {
                byte b = (byte)selected, l = (byte)layer.SelectedIndex;
                var old = await WithKeyboard(d => d.ReadAssignment(b, l));
                nextUndo = () => KeyWrite(d => d.SetAssignment(b, l, old));
                var value = f switch
                {
                    6 => Assignment.Keyboard((byte)Id(keyCode), modifier),
                    1 => Assignment.Mouse((byte)Id(mouseCode)),
                    8 => new Assignment(8, new byte[] { 0, (byte)Id(media), 0, 0, 0 }),
                    12 => new Assignment(12, new byte[] { (byte)Integer(slot), (byte)(Integer(repeat) >> 8), (byte)Integer(repeat), 0, 0 }),
                    _ => new Assignment((byte)f, new byte[5])
                };
                await KeyWrite(d => d.SetAssignment(b, l, value));
            }
            undo = nextUndo; undoButton.IsEnabled = true;
            await ReadSelected();
        }, true);
        save.IsEnabled = false;
        var reset = ActionButton("恢复此键默认", async () =>
        {
            int key = selected; byte l = (byte)layer.SelectedIndex;
            if (isMouse)
            {
                var previous = await WithMouse(d => (d.ReadButton(key), d.ReadMemory((ushort)(256 + 32 * (key - 1)), 32)));
                await MouseWrite(d => d.ResetButton(key)); undo = () => MouseWrite(d => d.RestoreButton(key, previous.Item1, previous.Item2));
            }
            else
            {
                var previous = await WithKeyboard(d => d.ReadAssignment((byte)key, l));
                var k = keyLayout.First(x => x.Id == key);
                await KeyWrite(d => d.SetAssignment((byte)key, l, l == 0 ? k.Default : k.FnDefault));
                undo = () => KeyWrite(d => d.SetAssignment((byte)key, l, previous));
            }
            undoButton.IsEnabled = true; await ReadSelected();
        });
        var editorGrid = new Grid { ColumnSpacing = 20 };
        editorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        editorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
        editorGrid.Children.Add(type); Grid.SetColumn(fields, 1); editorGrid.Children.Add(fields);
        var actions = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        foreach (var button in new[] { save, reset, undoButton, ActionButton("重新读取", ReadSelected) })
        {
            int i = actions.Children.Count;
            if (i < 2) actions.ColumnDefinitions.Add(new());
            if (i % 2 == 0) actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(button, i / 2); Grid.SetColumn(button, i % 2); actions.Children.Add(button);
        }
        var summary = new Grid { ColumnSpacing = 16 };
        summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); summary.ColumnDefinitions.Add(new());
        summary.Children.Add(selectedLabel); Grid.SetColumn(current, 1); summary.Children.Add(current);
        var editor = Card($"按键设置 · Profile {(isMouse ? mouse!.Profile : keyboard!.Profile)}", layer, summary, original, editorGrid, mods, actions);
        ((StackPanel)editor.Child).Spacing = 8;
        void ArrangeActions(double width)
        {
            int columns = width >= 720 ? 4 : 2;
            if (actions.ColumnDefinitions.Count == columns) return;
            actions.ColumnDefinitions.Clear(); actions.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) actions.ColumnDefinitions.Add(new());
            for (int i = 0; i < (actions.Children.Count + columns - 1) / columns; i++) actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < actions.Children.Count; i++) { Grid.SetColumn((FrameworkElement)actions.Children[i], i % columns); Grid.SetRow((FrameworkElement)actions.Children[i], i / columns); }
        }
        ArrangeActions(800); actions.SizeChanged += (_, e) => ArrangeActions(e.NewSize.Width);
        // The editor stays above the keyboard, so the current mapping is visible immediately.
        body.Children.Add(editor);
        if (isMouse)
        {
            body.Children.Remove(editor);
            body.Children.Add(MouseMappingDiagram((id, label) => { _ = SelectKey(id, label); }));
            body.Children.Add(editor);
        }
        else
        {
            var canvas = new Canvas { Width = Math.Max(640, keyLayout.Max(x => x.X + x.Width) - keyLayout.Min(x => x.X)), Height = Math.Max(280, keyLayout.Max(x => x.Y + x.Height) - keyLayout.Min(x => x.Y)) };
            double minX = keyLayout.Min(x => x.X), minY = keyLayout.Min(x => x.Y);
            var buttons = new List<Button>();
            var rotaryGroups = RotaryGroups();
            var rotaryIds = rotaryGroups.SelectMany(x => x.Keys).Select(x => x.Id).ToHashSet();
            foreach (var key in keyLayout)
            {
                if (rotaryIds.Contains(key.Id)) continue;
                var b = Button(key.Label, () => { foreach (var button in buttons) button.Background = ThemePalette.Key; return SelectKey(key.Id, key.Label); });
                b.Width = Math.Max(24, key.Width - 3); b.Height = Math.Max(24, key.Height - 3);
                b.Padding = new Thickness(1); b.FontSize = 10;
                b.Background = key.Id == selected ? ThemePalette.SelectedKey : ThemePalette.Key;
                Canvas.SetLeft(b, key.X - minX); Canvas.SetTop(b, key.Y - minY);
                ToolTipService.SetToolTip(b, key.Label);
                buttons.Add(b); b.Click += (_, _) => b.Background = ThemePalette.SelectedKey; canvas.Children.Add(b);
            }
            AddRotaryControls(canvas, rotaryGroups, minX, minY, (key, name) => { _ = SelectKey(key.Id, name); }, buttons);
            body.Children.Add(Card("选择键盘按键", new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 300, HorizontalAlignment = HorizontalAlignment.Stretch }));
        }
        Update();
        ReadPageOnLoad(editor, body, ReadSelected, "已读取当前映射");
    }
    static string MediaName(int code) => code switch
    {
        0xe9 => "音量增加", 0xea => "音量减少", 0xe2 => "静音", 0xcd => "播放 / 暂停", 0xb5 => "下一曲", 0xb6 => "上一曲", _ => $"多媒体 0x{code:X}"
    };
    static string MouseName(int code) => code switch { 1 => "左键", 2 => "右键", 3 => "中键", 4 => "后退", 5 => "前进", _ => $"鼠标按键 {code}" };
    static string Describe(Assignment a)
    {
        if (a.Function == 6)
        {
            var names = new[] { "Ctrl", "Shift", "Alt", "Win", "右 Ctrl", "右 Shift", "右 Alt", "右 Win" };
            var parts = Enumerable.Range(0, 8).Where(i => (a.Data[0] & (1 << i)) != 0).Select(i => names[i]).ToList();
            if (a.Data[1] != 0) parts.Add(HidKeys().FirstOrDefault(x => x.Id == a.Data[1])?.Label ?? $"键码 0x{a.Data[1]:X2}");
            return parts.Count > 0 ? string.Join(" + ", parts) : "键盘功能（无按键）";
        }
        return a.Function switch
        {
            0 => "禁用", 1 => MouseName(a.Data[0]), 2 => "双击左键", 8 => MediaName(a.Data[1]),
            12 => $"宏 {a.Data[0]} · {(a.Data[1] << 8) | a.Data[2]} 次", 13 or 14 or 15 => $"宏 {a.Data[0]} · 模式 {a.Function}",
            16 => "DPI 增加", 17 => "DPI 减少", 19 => "DPI 循环", 27 => "切换配置", 30 => "切换灯光", 47 => "锁定 Win 键",
            _ => $"设备功能 {a.Function} · {Convert.ToHexString(a.Data)}"
        };
    }
    static string DescribeMouse(byte[] data) => data[0] switch
    {
        0 => "禁用", 1 => MouseName(Array.IndexOf(new byte[] { 1, 2, 4, 8, 16 }, data[1]) + 1),
        2 => data[1] switch { 1 => "DPI 循环", 2 => "DPI 增加", 3 => "DPI 减少", _ => "DPI 功能" },
        4 => "双击 / 连点", 5 => "快捷键 / 多媒体", 6 => $"宏槽位 {data[1] + 1} · {data[2]} 次", 8 => "切换灯光", 9 => "切换配置", _ => "设备功能 · " + Convert.ToHexString(data)
    };
}
