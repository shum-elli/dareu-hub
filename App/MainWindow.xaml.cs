using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using DareuLocal.Core;
using System.Text.Json;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
namespace DareuLocal.App;
public sealed partial class MainWindow : Window
{
    bool ready, busy, rendering; string page = "overview", deviceSignature = ""; DeviceEndpoint? endpoint;
    int[] keyboardProfiles = { 1, 2 }; MouseState? mouse; KeyboardState? keyboard; List<KeyChoice> keyLayout = new(); readonly ObservableCollection<MacroAction> macroActions = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) }; string log = "";
    readonly string userFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DareuLocal");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    bool IsMouse => endpoint?.Kind == DeviceKind.Mouse;
    public MainWindow()
    {
        InitializeComponent();
        InitializeAppearance();
        InitializeDesktopWindow();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1260, 940));
        LoadLayout();
        ready = true;
        Scan();
        Render();
        Root.Loaded += async (s, e) => await Run(RefreshDashboard, "设备状态已更新");
        timer.Tick += async (s, e) => await DeviceTimerTick();
        timer.Start();
        Closed += (s, e) => { timer.Stop(); try { StopAudio().GetAwaiter().GetResult(); } catch { } };
    }
    void LoadLayout(int pid = 0x0045)
    {
        keyLayout.Clear();
        string path = Path.Combine(AppContext.BaseDirectory, "Profiles", $"{pid:x4}.json");
        if (!File.Exists(path))
            return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Keys", out var layout))
            return;
        foreach (var k in layout.EnumerateArray())
        {
            Assignment Get(string prefix)
            {
                var f = k.TryGetProperty(prefix + "function-Id", out var v) ? v.GetByte() : (byte)0;
                var d = k.TryGetProperty(prefix + "function-data", out var a) ? a.EnumerateArray().Select(x => x.GetByte()).ToArray() : new byte[5];
                return new(f, d);
            }
            int Read(string field, int fallback = 0) => k.TryGetProperty(field, out var x) ? x.GetInt32() : fallback;
            keyLayout.Add(new(k.GetProperty("KeyID").GetByte(), k.GetProperty("KeyString").GetString()!, Get("default-"), Get("default-fn-"), Read("margin-left"), Read("margin-top"), Read("Width", 38), Read("Height", 38), k.TryGetProperty("CssClassName", out var css) && css.GetString()?.Contains("half-ring", StringComparison.OrdinalIgnoreCase) == true));
        }
    }
    bool Scan()
    {
        var all = Hardware.Endpoints();
        var preferred = Hardware.PreferredEndpoints(all);
        string signature = string.Join("|", all.Select(x => x.Path + ":" + x.LogicalPid).OrderBy(x => x));
        if (signature == deviceSignature)
            return false;
        deviceSignature = signature;
        var previous = endpoint;
        rendering = true;
        DeviceBox.ItemsSource = preferred;
        endpoint = preferred.FirstOrDefault(x => x.FamilyKey == previous?.FamilyKey) ?? preferred.FirstOrDefault();
        DeviceBox.SelectedItem = endpoint;
        rendering = false;
        if (previous?.Path != endpoint?.Path)
        {
            mouse = null;
            keyboard = null;
            ConnectionText.Text = endpoint == null ? "未发现设备" : "等待连接";
            BatteryText.Text = "—";
            BatteryGraphic.Content = null;
            selectedPower = null;
            ProfileText.Text = "—";
        }
        if (previous != null && previous.FamilyKey != endpoint?.FamilyKey && page is not ("overview" or "catalog" or "settings"))
            page = "overview";
        Render();
        return true;
    }
    async void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || busy)
            return;
        if (audioSession != null)
            try { await StopAudio(); } catch (Exception ex) { AddLog(ex.Message); }
        endpoint = DeviceBox.SelectedItem as DeviceEndpoint;
        mouse = null;
        keyboard = null;
        ConnectionText.Text = "等待连接";
        BatteryText.Text = "—";
        BatteryGraphic.Content = null;
        selectedPower = null;
        ProfileText.Text = "—";
        Render();
        if (endpoint?.CanConfigure == true)
            await Run(Connect, "已切换设备");
    }
    async void PageChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!ready || updatingNavigation)
            return;
        if (args.SelectedItem is NavigationViewItem item)
        {
            string next = item.Tag?.ToString() ?? "overview";
            if (audioSession != null && next != "music")
                try
                {
                    await StopAudio();
                }
                catch (Exception ex) { AddLog(ex.Message); }
            page = next;
            Render();
            if (page == "overview" && !busy)
                await Run(RefreshDashboard, "设备状态已更新");
        }
    }
    void AddLog(string text)
    {
        log = $"[{DateTime.Now:HH:mm:ss}] {text}\n" + log;
        if (log.Length > 18000)
            log = log[..18000];
    }
    async Task Run(Func<Task> action, string success = "操作完成")
    {
        if (busy)
            return;
        busy = true;
        Status.IsOpen = true;
        Progress.IsActive = true;
        Navigation.IsEnabled = false;
        DeviceBox.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        WorkArea.IsEnabled = false;
        Status.Severity = InfoBarSeverity.Informational;
        Status.Title = "正在处理";
        Status.Message = "请保持设备连接，操作完成后会显示结果。";
        try
        {
            if (dpiOperation is { } currentDpiOperation) await currentDpiOperation;
            await action();
            Status.Severity = InfoBarSeverity.Success;
            Status.Title = success;
            Status.Message = "";
        }
        catch (OperationCanceledException) { Status.Severity = InfoBarSeverity.Informational; Status.Title = "已取消"; Status.Message = "设备设置未继续写入。"; }
        catch (Exception e) { Status.Severity = InfoBarSeverity.Error; Status.Title = "操作未完成"; Status.Message = e.Message; AddLog(e.ToString()); }
        finally { Progress.IsActive = false; Navigation.IsEnabled = true; DeviceBox.IsEnabled = true; RefreshButton.IsEnabled = true; WorkArea.IsEnabled = true; busy = false; }
    }
    Task<T> WithMouse<T>(Func<JmMouse, T> action)
    {
        var selected = endpoint ?? throw new IOException("请先选择设备");
        if (selected.Kind != DeviceKind.Mouse || !selected.CanConfigure)
            throw new IOException("请先选择鼠标");
        return Task.Run(() => { using var d = new JmMouse(selected); return action(d); });
    }
    Task<T> WithKeyboard<T>(Func<TgKeyboard, T> action)
    {
        var selected = endpoint ?? throw new IOException("请先选择设备");
        if (selected.Kind != DeviceKind.Keyboard || !selected.CanConfigure)
            throw new IOException("请先选择键盘");
        return Task.Run(() => { using var d = new TgKeyboard(selected); d.ReadState(); return action(d); });
    }
    Task MouseWrite(Action<JmMouse> action) => WithMouse(d => { action(d); return true; });
    Task KeyWrite(Action<TgKeyboard> action) => WithKeyboard(d => { action(d); return true; });
    async Task Connect()
    {
        if (endpoint == null)
            throw new IOException("未发现设备，请检查数据线或接收器。");
        if (!endpoint.CanConfigure)
        {
            await Navigate("catalog");
            return;
        }
        if (IsMouse)
        {
            var reading = await WithMouse(d =>
            {
                var state = d.ReadState(); var power = d.ReadPower();
                SensorState? sensor = null;
                try { sensor = d.ReadSensor(); } catch (IOException) { }
                return (State: state, Power: power, Sensor: sensor);
            });
            mouseSensor = reading.Sensor; mouseSensorPath = endpoint.Path;
            mouseSensorProfile = reading.State.Profile; mouseSensorReadAt = DateTime.Now;
            mouse = reading.State;
            selectedPower = reading.Power;
            keyboard = null;
            BatteryText.Text = PowerCaption(selectedPower);
            BatteryGraphic.Content = BatteryIcon(selectedPower);
            ProfileText.Text = $"Profile {mouse.Profile}";
            AddLog($"{endpoint.Name} {endpoint.Pid:X4} CID {mouse.Cid} MID {mouse.Mid} type {mouse.Type}");
        }
        else
        {
            var reading = await WithKeyboard(d => (State: d.ReadState(), Power: d.ReadPower()));
            keyboard = reading.State;
            LoadLayout(keyboard.Pid);
            selectedPower = reading.Power;
            keyboardProfiles = await WithKeyboard(d => d.Profiles());
            mouse = null;
            BatteryText.Text = PowerCaption(selectedPower);
            BatteryGraphic.Content = BatteryIcon(selectedPower);
            ProfileText.Text = $"Profile {keyboard.Profile}";
            AddLog($"{endpoint.Name} {keyboard.Pid:X4} target {keyboard.Target:X2}");
        }
        ConnectionText.Text = endpoint.Wireless ? "2.4G 已连接" : "USB 已连接";
        Render();
    }
    void ReadPageOnLoad(FrameworkElement view, StackPanel body, Func<Task> read, string success)
    {
        var owner = endpoint;
        bool requested = false;
        view.Loaded += async (_, _) =>
        {
            if (requested) return;
            requested = true;
            bool Here() => ReferenceEquals(WorkArea.Content, body) && ReferenceEquals(endpoint, owner);
            for (int i = 0; i < 50 && busy && Here(); i++) await Task.Delay(100);
            if (!Here() || busy) return;
            await Run(read, success);
            if (Here() && Status.Severity == InfoBarSeverity.Success) Status.IsOpen = false;
        };
    }
    async void HomeClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        await Run(async () => { await StopAudio(); page = "overview"; Render(); await RefreshDashboard(); }, "设备状态已更新");
    }
    async void RefreshClicked(object s, RoutedEventArgs e) => await Run(page == "overview" ? RefreshDashboard : Connect, page == "overview" ? "设备状态已更新" : "已读取当前配置");
    void Render()
    {
        if (!ready)
            return;
        rendering = true;
        UpdateDeviceNavigation();
        var titles = new Dictionary<string, (string, string)> { { "overview", ("我的设备", "选择要设置的设备。有线和 2.4G 同时接入时，优先使用有线。") }, { "performance", ("性能与电源", IsMouse ? "调整灵敏度、响应速度与休眠时间。" : "管理键盘休眠，兼顾续航和随时可用。") }, { "mapping", ("按键映射", IsMouse ? "为鼠标按键分配常用操作。" : "点击键盘上的按键，编辑默认层或 Fn 层。") }, { "lighting", ("键盘灯光", "按区域设置灯效、颜色、速度与亮度。") }, { "macros", ("宏编辑器", "添加按下、松开和延时，保存在本地或写入设备。") }, { "profiles", ("配置与备份", "切换板载配置，将当前设置备份到本地。") }, { "rgb", ("逐键 RGB", "按键选择、颜色方案与板载自定义灯光。") }, { "music", ("音乐律动", "跟随系统播放声音，实时点亮键盘。") }, { "catalog", ("设备库与适配", "根据型号加载设备图片、按键布局与可用功能。") }, { "settings", ("设置与诊断", "连接状态、软件信息和通信记录。") } };
        var title = titles[page];
        PageTitle.Text = title.Item1;
        PageSubtitle.Text = title.Item2;
        var body = Column();
        WorkArea.Content = body;
        bool home = page == "overview";
        SummaryPanel.Visibility = home || page is "catalog" or "mapping" or "music" or "lighting" ? Visibility.Collapsed : Visibility.Visible;
        DeviceBox.Visibility = home || !deviceSelected ? Visibility.Collapsed : Visibility.Visible;
        BackHomeButton.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        HomeHeader.Visibility = home || !deviceSelected ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Content = home ? "刷新设备" : "连接 / 刷新";
        Status.Visibility = home || page == "catalog" ? Visibility.Collapsed : Visibility.Visible;
        if (home)
        {
            Overview(body);
            rendering = false;
            return;
        }
        if (page == "catalog")
        {
            CatalogPage(body);
            rendering = false;
            return;
        }
        if (page == "settings")
        {
            SettingsPage(body);
            rendering = false;
            return;
        }
        if (page == "macros")
        {
            MacroPage(body);
            rendering = false;
            return;
        }
        if (endpoint != null && !endpoint.CanConfigure)
        {
            DevicePreview(body, endpoint.Product);
            rendering = false;
            return;
        }
        if (endpoint == null || mouse == null && keyboard == null)
        {
            body.Children.Add(Card("连接你的设备", Text("选择上方的 USB 或 2.4G 接口，再点击连接 / 刷新。设置不会自动写入设备。"), Button("连接设备", () => Run(Connect, "已连接"))));
            rendering = false;
            return;
        }
        switch (page)
        {
            case "rgb":
                CustomRgbPage(body);
                break;
            case "music":
                MusicPage(body);
                break;
            case "overview":
                Overview(body);
                break;
            case "performance":
                Performance(body);
                break;
            case "mapping":
                Mapping(body);
                break;
            case "lighting":
                Lights(body);
                break;
            case "profiles":
                Profiles(body);
                break;
        }
        rendering = false;
    }
    static StackPanel Column(double spacing = 16) => new() { Spacing = spacing };
    static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = ThemePalette.Secondary };
    static Border Card(string title, params UIElement[] items)
    {
        var c = Column();
        c.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var item in items)
            c.Children.Add(item);
        return new Border { Background = ThemePalette.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Child = c };
    }
    Button Button(string label, Func<Task> action, bool accent = false)
    {
        var b = new Button { Content = label, Padding = new Thickness(18, 10, 18, 10) };
        if (accent)
            { b.Background = ThemePalette.AccentFill; b.Foreground = ThemePalette.OnAccent; }
        b.Click += async (s, e) => { try { await action(); } catch (Exception ex) { Status.Severity = InfoBarSeverity.Error; Status.Title = "操作未完成"; Status.Message = ex.Message; AddLog(ex.ToString()); } };
        return b;
    }
    Button ActionButton(string label, Func<Task> action, bool accent = false) => Button(label, () => Run(action, "操作完成"), accent);
    static ComboBox Choices(string header, IEnumerable<Choice> values, int selected = -1)
    {
        var c = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = values.ToList() };
        c.SelectedItem = ((List<Choice>)c.ItemsSource).FirstOrDefault(x => x.Id == selected);
        return c;
    }
    static int Id(ComboBox c) => c.SelectedItem is Choice item ? item.Id : throw new ArgumentException("请完成选项选择");
    static NumberBox Number(string title, double value, int min, int max, int step = 1) => new() { Header = title, Value = value, Minimum = min, Maximum = max, SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    static int Integer(NumberBox box)
    {
        if (!double.IsFinite(box.Value) || box.Value != Math.Truncate(box.Value))
            throw new ArgumentException("请输入有效整数");
        return checked((int)box.Value);
    }
    static Slider Slider(string header, int value, int max) => new() { Header = header, Value = value, Minimum = 0, Maximum = max, StepFrequency = 1 };
    static StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var item in items)
            row.Children.Add(item);
        return row;
    }
    async Task Confirm(string title, string message, Func<Task> action)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = message, PrimaryButtonText = "继续", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, RequestedTheme = Root.RequestedTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await action();
        else
            throw new OperationCanceledException();
    }
    Task Navigate(string tag)
    {
        foreach (var i in Navigation.MenuItems.OfType<NavigationViewItem>())
            if (i.Tag?.ToString() == tag)
            {
                Navigation.SelectedItem = i;
                break;
            }
        return Task.CompletedTask;
    }
    static string PollingName(int id) => id switch { 1 => "1000 Hz", 16 => "2000 Hz", 32 => "4000 Hz", 64 => "8000 Hz", _ => $"设备值 {id}" };
    void Performance(StackPanel body)
    {
        if (IsMouse)
        {
            var state = mouse!;
            MouseDpiEditor(body);
            var polling = Choices("回报率", PollingChoices(), state.PollingId);
            body.Children.Add(Card("回报率", polling, Text("更高回报率会增加功耗和系统负载，可用上限取决于设备及连接模式。"), ActionButton("保存回报率", async () => { byte id = (byte)Id(polling); await MouseWrite(d => { d.SetPolling(id); if (d.ReadState().PollingId != id) throw new IOException("回报率读回不一致"); }); await Connect(); })));
            var sensorHost = Column();
            sensorHost.Children.Add(Text("读取后显示设备当前的传感器参数。"));
            void ShowSensor(SensorState v)
            {
                sensorHost.Children.Clear();
                sensorHost.Children.Add(Text($"上次读取：{mouseSensorReadAt:HH:mm:ss}", 12));
                SensorEditor(sensorHost, v);
            }
            if (mouseSensor is { } cached && mouseSensorPath == endpoint!.Path && mouseSensorProfile == mouse!.Profile) ShowSensor(cached);
            async Task ReadSensor()
            {
                var v = await WithMouse(d => { d.ReadState(); return d.ReadSensor(); });
                mouseSensor = v; mouseSensorPath = endpoint!.Path; mouseSensorProfile = mouse!.Profile; mouseSensorReadAt = DateTime.Now;
                ShowSensor(v);
            }
            var sensorCard = Card("传感器与引擎", sensorHost, ActionButton("重新读取高级参数", ReadSensor));
            body.Children.Add(sensorCard);
            ReadPageOnLoad(sensorCard, body, ReadSensor, "已读取引擎与传感器参数");
        }
        var sleep = Number("休眠时间（秒，0 为关闭）", IsMouse ? mouse!.SleepSeconds : 0, 0, IsMouse ? 2550 : 65535, IsMouse ? 10 : 1);
        var content = Column();
        content.Children.Add(sleep);
        sleep.IsEnabled = IsMouse;
        if (!IsMouse)
            content.Children.Add(ActionButton("重新读取休眠时间", async () => { sleep.Value = await WithKeyboard(d => d.SleepSeconds()); sleep.IsEnabled = true; }));
        content.Children.Add(ActionButton("保存休眠时间", async () => { if (!sleep.IsEnabled) throw new IOException("请先读取当前休眠时间"); int seconds = Integer(sleep); if (IsMouse) await MouseWrite(d => { d.SetSleep(seconds); if (d.ReadState().SleepSeconds != seconds) throw new IOException("休眠读回不一致"); }); else await KeyWrite(d => d.SetSleep(seconds)); await Connect(); }));
        var powerCard = Card("电源管理", content);
        body.Children.Add(powerCard);
        if (!IsMouse) ReadPageOnLoad(powerCard, body, async () => { sleep.Value = await WithKeyboard(d => d.SleepSeconds()); sleep.IsEnabled = true; }, "已读取休眠时间");
        if (IsMouse)
        {
            var sections = body.Children.ToArray();
            body.Children.Clear();
            var engine = Column(); engine.Children.Add(sections[2]); engine.Children.Add(sections[1]);
            var tabs = new Pivot();
            tabs.Items.Add(new PivotItem { Header = "DPI 灵敏度", Content = sections[0] });
            tabs.Items.Add(new PivotItem { Header = "引擎与回报率", Content = engine });
            tabs.Items.Add(new PivotItem { Header = "电源", Content = powerCard });
            body.Children.Add(tabs);
        }
    }
    SensorState? mouseSensor;
    string? mouseSensorPath;
    int mouseSensorProfile;
    DateTime mouseSensorReadAt;
    void SensorEditor(StackPanel panel, SensorState s)
    {
        var supports = endpoint!.Product?.SensorSupports ?? Array.Empty<int>();
        if (supports.Length == 0) supports = DeviceCatalog.Find(endpoint.LogicalPid)?.SensorSupports ?? Array.Empty<int>();
        void Toggle(string name, string field, int value)
        {
            if (value is not (0 or 1))
            {
                panel.Children.Add(Text(name + "：设备未返回可用值"));
                return;
            }
            var toggle = new ToggleSwitch { Header = name, IsOn = value == 1, OnContent = "开启", OffContent = "关闭" };
            panel.Children.Add(toggle);
            panel.Children.Add(ActionButton("保存 " + name, () => { int next = toggle.IsOn ? 1 : 0; return MouseWrite(d => d.SetSensorValue(field, next)); }));
        }
        if (supports.Contains(5) || endpoint.Product?.Pid == 0x1330)
        {
            if (s.Mode is >= 0 and <= 2)
            {
                var engine = new RadioButtons { Header = "引擎模式", MaxColumns = 3,
                    ItemsSource = new[] { "办公模式", "游戏模式", "超感竞技模式" }, SelectedIndex = s.Mode };
                var current = Text("当前引擎：" + new[] { "办公模式", "游戏模式", "超感竞技模式" }[s.Mode]);
                panel.Children.Add(engine); panel.Children.Add(current);
                panel.Children.Add(ActionButton("应用引擎模式", async () =>
                {
                    int mode = engine.SelectedIndex;
                    await MouseWrite(d => d.SetSensorValue("Mode", mode));
                    var actual = await WithMouse(d => d.ReadSensor());
                    if (actual.Mode != mode) throw new IOException("引擎模式读回不一致");
                    current.Text = "当前引擎：" + new[] { "办公模式", "游戏模式", "超感竞技模式" }[actual.Mode];
                }, true));
            }
            else panel.Children.Add(Text("当前固件未返回可用的引擎模式，暂不显示写入控件。"));
        }
        if (supports.Contains(0))
            Toggle("Motion Sync", "MotionSync", s.MotionSync);
        var lod = Choices("LOD 抬升高度", new[] { new Choice(0, "0.7 mm"), new Choice(1, "1 mm"), new Choice(2, "2 mm"), new Choice(3, "保持设备当前设置") }, s.Lod);
        panel.Children.Add(lod);
        panel.Children.Add(ActionButton("保存 LOD", () => { int value = Id(lod); return value == 3 ? Task.CompletedTask : MouseWrite(d => d.SetSensorValue("LOD", value)); }));
        var response = Number("按键响应时间（ms）", s.Response, 0, 30);
        panel.Children.Add(response);
        panel.Children.Add(ActionButton("保存响应时间", () => { int value = Integer(response); return MouseWrite(d => d.SetSensorValue("Response", value)); }));
        if (supports.Contains(3))
            Toggle("直线修正", "Angle", s.Angle);
        if (supports.Contains(4))
            Toggle("波纹修正", "Ripple", s.Ripple);
    }
}
