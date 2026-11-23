using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using DareuLocal.Core;
using System.Text.Json;
using System.Collections.ObjectModel;
namespace DareuLocal.App;
public sealed partial class MainWindow : Window
{
    bool ready, busy, rendering, initialDashboardRefreshed;
    string page = "overview", deviceSignature = "";
    List<KeyChoice> keyLayout = new();
    readonly ObservableCollection<MacroAction> macroActions = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    string log = "";
    readonly object logLock = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    readonly DeviceController controller;
    readonly ForegroundWatcher foregroundWatcher = new();
    OsdWindow? osdWindow;
    CancellationTokenSource? deviceChangeDebounceCts;
    CancellationTokenSource? hardwareEventDebounceCts;
    Action? leaveCurrentPageCleanup;
    Action<MouseState>? onExternalMouseStateObserved;

    string userFolder => controller.UserFolder;
    AppSettings appSettings => controller.Settings;
    DeviceEndpoint? endpoint { get => controller.Endpoint; set => controller.Endpoint = value; }
    MouseState? mouse { get => controller.Mouse; set => controller.Mouse = value; }
    KeyboardState? keyboard { get => controller.Keyboard; set => controller.Keyboard = value; }
    SensorState? mouseSensor { get => controller.MouseSensor; set => controller.MouseSensor = value; }
    string? mouseSensorPath { get => controller.MouseSensorPath; set => controller.MouseSensorPath = value; }
    int mouseSensorProfile { get => controller.MouseSensorProfile; set => controller.MouseSensorProfile = value; }
    DateTime mouseSensorReadAt { get => controller.MouseSensorReadAt; set => controller.MouseSensorReadAt = value; }
    PowerReading? selectedPower { get => controller.SelectedPower; set => controller.SelectedPower = value; }
    int[] keyboardProfiles { get => controller.KeyboardProfiles; set => controller.KeyboardProfiles = value; }
    bool IsMouse => controller.IsMouse;

    public MainWindow(bool launchedMinimized = false)
    {
        controller = new DeviceController(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DareuLocal"));
        InitializeComponent();
        InitializeAppearance();
        InitializeDesktopWindow(launchedMinimized);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1260, 940));
        LoadLayout();
        ready = true;
        Scan();
        Render();

        foregroundWatcher.ForegroundProcessChanged += OnForegroundProcessChanged;
        foregroundWatcher.SetEnabled(appSettings.AutoSwitchEnabled);
        controller.MouseHardwareEvent += OnMouseHardwareEvent;
        SyncMouseWatcher();

        if (ShouldStartMinimized)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!initialDashboardRefreshed)
                {
                    initialDashboardRefreshed = true;
                    await Run(RefreshDashboard, "设备状态已更新");
                }
            });
        }

        Root.Loaded += async (s, e) =>
        {
            if (!initialDashboardRefreshed)
            {
                initialDashboardRefreshed = true;
                await Run(RefreshDashboard, "设备状态已更新");
            }
        };
        timer.Tick += async (s, e) => await DeviceTimerTick();
        timer.Start();
        Closed += (s, e) =>
        {
            timer.Stop();
            deviceChangeDebounceCts?.Cancel();
            deviceChangeDebounceCts?.Dispose();
            hardwareEventDebounceCts?.Cancel();
            hardwareEventDebounceCts?.Dispose();
            foregroundWatcher.Dispose();
            osdWindow?.Close();
            osdWindow = null;
            try { leaveCurrentPageCleanup?.Invoke(); } catch { }
            try { CleanupLiveRgbSync().GetAwaiter().GetResult(); } catch { }
            try { StopAudio().GetAwaiter().GetResult(); } catch { }
            controller.Dispose();
        };
    }

    void SaveSettings()
    {
        try
        {
            controller.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog("保存设置失败：" + ex.Message);
        }
    }

    void NotifyUser(string title, string subtitle, bool warning = false)
    {
        if (!appSettings.OsdEnabled)
            return;
        if (trayIcon != null)
        {
            trayIcon.ShowBalloon(title, subtitle, warning);
            return;
        }
        try
        {
            osdWindow ??= new OsdWindow();
            osdWindow.ShowToast(title, subtitle, lightTheme);
        }
        catch (Exception ex)
        {
            AddLog("通知提示失败：" + ex.Message);
        }
    }

    void SyncMouseWatcher()
    {
        bool attached = controller.UpdateMouseWatcher(appSettings.OsdEnabled);
        if (attached && controller.LastObservedMouseStage == null)
            _ = controller.PollMouseStageAsync();
    }

    void OnMouseHardwareEvent(DeviceEndpoint ep)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            hardwareEventDebounceCts?.Cancel();
            hardwareEventDebounceCts?.Dispose();
            var cts = new CancellationTokenSource();
            hardwareEventDebounceCts = cts;
            _ = HandleMouseHardwareEventAsync(cts.Token);
        });
    }

    async Task HandleMouseHardwareEventAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(40, ct);
            if (ct.IsCancellationRequested || busy)
                return;

            var polled = await controller.PollMouseStageAsync(ct);
            if (ct.IsCancellationRequested || polled is not { State: var state, MouseEndpoint: var mouseEp })
                return;

            if (endpoint?.Path == mouseEp.Path)
                UpdateHeaderSummary();
            UpdateTrayTooltip();
            onExternalMouseStateObserved?.Invoke(state);

            if (polled.Value.StageChanged)
            {
                NotifyUser(
                    $"{state.Dpi[state.Stage - 1]} DPI",
                    $"档位 {state.Stage} · {appSettings.GetProfileDisplayName(mouseEp.FamilyKey, state.Profile)}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddLog("鼠标硬件事件处理异常：" + ex.Message);
        }
    }

    void OnUsbDeviceChanged()
    {
        deviceChangeDebounceCts?.Cancel();
        deviceChangeDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        deviceChangeDebounceCts = cts;
        _ = HandleDeviceChangeDebouncedAsync(cts.Token);
    }

    async Task HandleDeviceChangeDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct);
            if (ct.IsCancellationRequested)
                return;

            if (audioSession is { } session)
            {
                bool present = Hardware.Endpoints().Any(x => x.Path == session.EndpointPath);
                if (!present)
                {
                    await StopAudio();
                    if (page == "music") Render();
                    Status.IsOpen = true;
                    Status.Severity = InfoBarSeverity.Warning;
                    Status.Title = "音乐律动已停止";
                    Status.Message = "设备已断开";
                }
                return;
            }

            await controller.ResolveReceiversAsync(ct);
            if (ct.IsCancellationRequested)
                return;
            bool changed = Scan();
            if (page == "overview")
                await Run(RefreshDashboard, "设备状态已更新");
            else if (changed && deviceSelected && endpoint?.CanConfigure == true)
                await Run(Connect, "连接方式已更新");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddLog("热插拔检测异常：" + ex.Message);
        }
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
        controller.BackgroundMouseEndpoint = preferred.FirstOrDefault(x => x.Kind == DeviceKind.Mouse && x.CanConfigure);
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
            controller.ResetObservedMouse();
            ConnectionText.Text = endpoint == null ? "未发现设备" : "等待连接";
            BatteryText.Text = "—";
            BatteryGraphic.Content = null;
            selectedPower = null;
            ProfileText.Text = "—";
        }
        if (previous != null && previous.FamilyKey != endpoint?.FamilyKey && page is not ("overview" or "catalog" or "settings"))
            page = "overview";
        SyncMouseWatcher();
        Render();
        return true;
    }

    async void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || busy)
            return;
        leaveCurrentPageCleanup?.Invoke();
        leaveCurrentPageCleanup = null;
        await CleanupLiveRgbSync();
        if (audioSession != null)
            try { await StopAudio(); } catch (Exception ex) { AddLog(ex.Message); }
        endpoint = DeviceBox.SelectedItem as DeviceEndpoint;
        mouse = null;
        keyboard = null;
        controller.ResetObservedMouse();
        SyncMouseWatcher();
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
            leaveCurrentPageCleanup?.Invoke();
            leaveCurrentPageCleanup = null;
            if (next != "rgb")
                await CleanupLiveRgbSync();
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
        lock (logLock)
        {
            log = $"[{DateTime.Now:HH:mm:ss}] {text}\n" + log;
            if (log.Length > 18000)
                log = log[..18000];
        }
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
            await action();
            Status.Severity = InfoBarSeverity.Success;
            Status.Title = success;
            Status.Message = "";
        }
        catch (OperationCanceledException) { Status.Severity = InfoBarSeverity.Informational; Status.Title = "已取消"; Status.Message = "设备设置未继续写入。"; }
        catch (Exception e) { Status.Severity = InfoBarSeverity.Error; Status.Title = "操作未完成"; Status.Message = e.Message; AddLog(e.ToString()); }
        finally { Progress.IsActive = false; Navigation.IsEnabled = true; DeviceBox.IsEnabled = true; RefreshButton.IsEnabled = true; WorkArea.IsEnabled = true; busy = false; }
    }

    Task<T> WithMouse<T>(Func<JmMouse, T> action, CancellationToken ct = default) => controller.WithMouseAsync(action, ct);
    Task<T> WithKeyboard<T>(Func<TgKeyboard, T> action, CancellationToken ct = default) => controller.WithKeyboardAsync(action, ct);
    Task MouseWrite(Action<JmMouse> action, CancellationToken ct = default) => controller.WriteMouseAsync(action, ct);
    Task KeyWrite(Action<TgKeyboard> action, CancellationToken ct = default) => controller.WriteKeyboardAsync(action, ct);

    void UpdateHeaderSummary()
    {
        if (endpoint == null)
        {
            ConnectionText.Text = "未发现设备";
            BatteryText.Text = "—";
            BatteryGraphic.Content = null;
            ProfileText.Text = "—";
            return;
        }
        ConnectionText.Text = endpoint.Wireless ? "2.4G 已连接" : "USB 已连接";
        BatteryText.Text = PowerCaption(selectedPower);
        BatteryGraphic.Content = BatteryIcon(selectedPower);
        int activeProfile = IsMouse ? (mouse?.Profile ?? 0) : (keyboard?.Profile ?? 0);
        ProfileText.Text = activeProfile > 0 ? appSettings.GetProfileDisplayName(endpoint.FamilyKey, activeProfile) : "—";
    }

    async Task Connect()
    {
        if (endpoint == null)
            throw new IOException("未发现设备，请检查数据线或接收器。");
        if (!endpoint.CanConfigure)
        {
            await Navigate("catalog");
            return;
        }
        var summary = await controller.ConnectCurrentAsync();
        if (summary.IsMouse)
            AddLog($"{endpoint.Name} {summary.Pid:X4} CID {summary.Cid} MID {summary.Mid} type {summary.Type}");
        else
        {
            LoadLayout(summary.Pid);
            AddLog($"{endpoint.Name} {summary.Pid:X4} target {summary.Target:X2}");
        }
        UpdateHeaderSummary();
        CheckLowBatteryNotification(endpoint, selectedPower);
        UpdateTrayTooltip();
        Render();
    }

    async Task SwitchProfileAsync(int profileId, bool fromAutoSwitch = false, string? triggerProcess = null)
    {
        var outcome = await controller.SwitchProfileAsync(profileId, fromAutoSwitch, triggerProcess);
        if (outcome?.Changed == true)
        {
            UpdateHeaderSummary();
            UpdateTrayTooltip();
            NotifyUser(outcome.DisplayName, outcome.Subtitle);
            if (!fromAutoSwitch || page != "overview")
                Render();
        }
    }

    async void OnForegroundProcessChanged(string processName)
    {
        if (!appSettings.AutoSwitchEnabled || busy)
            return;
        var selected = endpoint;
        if (selected == null || !selected.CanConfigure)
            return;
        if (audioSession != null && selected.Path == audioSession.EndpointPath)
            return;

        var rule = appSettings.MatchRule(processName, selected.FamilyKey);
        int targetProfile;
        string? matchedProc = null;
        if (rule != null)
        {
            targetProfile = rule.Profile;
            matchedProc = processName;
        }
        else if (appSettings.DefaultProfiles.TryGetValue(selected.FamilyKey, out int defProfile))
        {
            targetProfile = defProfile;
        }
        else
        {
            return;
        }

        int current = selected.Kind == DeviceKind.Mouse ? (mouse?.Profile ?? 0) : (keyboard?.Profile ?? 0);
        if (current == targetProfile)
            return;

        try
        {
            await SwitchProfileAsync(targetProfile, fromAutoSwitch: true, triggerProcess: matchedProc);
        }
        catch (Exception ex)
        {
            AddLog($"自动切换配置失败（{processName}）：" + ex.Message);
        }
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
        leaveCurrentPageCleanup?.Invoke();
        leaveCurrentPageCleanup = null;
        await CleanupLiveRgbSync();
        await Run(async () => { await StopAudio(); page = "overview"; Render(); await RefreshDashboard(); }, "设备状态已更新");
    }

    async void RefreshClicked(object s, RoutedEventArgs e) => await Run(page == "overview" ? RefreshDashboard : Connect, page == "overview" ? "设备状态已更新" : "已读取当前配置");

    void Render()
    {
        if (!ready)
            return;
        leaveCurrentPageCleanup?.Invoke();
        leaveCurrentPageCleanup = null;
        rendering = true;
        UpdateDeviceNavigation();
        var titles = new Dictionary<string, (string, string)> { { "overview", ("我的设备", "选择要设置的设备。有线和 2.4G 同时接入时，优先使用有线。") }, { "performance", ("性能与电源", IsMouse ? "调整灵敏度、响应速度与休眠时间。" : "管理键盘休眠，兼顾续航和随时可用。") }, { "mapping", ("按键映射", IsMouse ? "为鼠标按键分配常用操作。" : "点击键盘上的按键，编辑默认层或 Fn 层。") }, { "lighting", ("键盘灯光", "按区域设置灯效、颜色、速度与亮度。") }, { "macros", ("宏编辑器", "添加按下、松开和延时，保存在本地或写入设备。") }, { "profiles", ("配置与备份", "切换板载配置、管理别名与前台应用自动切换，或备份恢复配置。") }, { "rgb", ("逐键 RGB", "按键选择、画笔涂色与板载自定义灯光。") }, { "music", ("音乐律动", "跟随系统播放声音，实时频谱点亮键盘。") }, { "catalog", ("设备库与适配", "根据型号加载设备图片、按键布局与可用功能。") }, { "settings", ("设置与诊断", "桌面驻留、通知、更新检查与脱敏通信记录。") } };
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
        b.Click += async (s, e) => { try { await action(); } catch (Exception ex) { Status.IsOpen = true; Status.Severity = InfoBarSeverity.Error; Status.Title = "操作未完成"; Status.Message = ex.Message; AddLog(ex.ToString()); } };
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
    static string PollingName(int id) => id switch { 8 => "125 Hz", 4 => "250 Hz", 2 => "500 Hz", 1 => "1000 Hz", 16 => "2000 Hz", 32 => "4000 Hz", 64 => "8000 Hz", _ => $"设备值 {id}" };

    void Performance(StackPanel body)
    {
        if (IsMouse)
        {
            var state = mouse!;
            var engineStatus = Text("调整回报率或传感器选项后，点击下方“应用更改”一次性写入并读回校验。", 12);
            var polling = Choices("回报率", PollingChoices(), state.PollingId);
            var pollingCard = Card("回报率", polling, Text("更高回报率会增加功耗和系统负载，可用上限取决于设备及连接模式。", 12));

            var sensorHost = Column();
            sensorHost.Children.Add(Text("读取后显示设备当前的传感器参数。"));
            var sensorCard = Card("传感器与引擎", sensorHost);

            Action? applySensor = null;
            Action? revertSensor = null;

            void ShowSensor(SensorState v)
            {
                sensorHost.Children.Clear();
                sensorHost.Children.Add(Text($"上次读取：{mouseSensorReadAt:HH:mm:ss}", 12));
                var editor = BuildSensorEditor(sensorHost, v);
                applySensor = editor.Apply;
                revertSensor = editor.Revert;
            }
            if (mouseSensor is { } cached && mouseSensorPath == endpoint!.Path && mouseSensorProfile == mouse!.Profile)
                ShowSensor(cached);

            async Task ReadSensor()
            {
                var reading = await WithMouse(d => (State: d.ReadState(), Sensor: d.ReadSensor()));
                controller.UpdateObservedMouseState(reading.State, endpoint!.Path);
                mouseSensor = reading.Sensor;
                mouseSensorPath = endpoint!.Path;
                mouseSensorProfile = reading.State.Profile;
                mouseSensorReadAt = DateTime.Now;
                polling.SelectedItem = ((List<Choice>)polling.ItemsSource).FirstOrDefault(x => x.Id == reading.State.PollingId);
                ShowSensor(reading.Sensor);
                engineStatus.Text = $"已同步设备参数 · {mouseSensorReadAt:HH:mm:ss}";
            }

            SensorEditorRefs BuildSensorEditor(StackPanel panel, SensorState s)
            {
                var supports = endpoint!.Product?.SensorSupports ?? Array.Empty<int>();
                if (supports.Length == 0) supports = DeviceCatalog.Find(endpoint.LogicalPid)?.SensorSupports ?? Array.Empty<int>();

                RadioButtons? engineRadio = null;
                TextBlock? engineCurrent = null;
                if (supports.Contains(5) || endpoint.Product?.Pid == 0x1330)
                {
                    if (s.Mode is >= 0 and <= 2)
                    {
                        var names = new[] { "办公模式", "游戏模式", "超感竞技模式" };
                        engineRadio = new RadioButtons { Header = "引擎模式", MaxColumns = 3, ItemsSource = names, SelectedIndex = s.Mode };
                        engineCurrent = Text("当前引擎：" + names[s.Mode], 12);
                        panel.Children.Add(engineRadio);
                        panel.Children.Add(engineCurrent);
                    }
                    else
                    {
                        panel.Children.Add(Text("当前固件未返回可用的引擎模式，暂不显示写入控件。"));
                    }
                }

                ToggleSwitch? motionSync = supports.Contains(0) && s.MotionSync is 0 or 1
                    ? new ToggleSwitch { Header = "Motion Sync", IsOn = s.MotionSync == 1, OnContent = "开启", OffContent = "关闭" }
                    : null;
                if (motionSync != null) panel.Children.Add(motionSync);
                else if (supports.Contains(0)) panel.Children.Add(Text("Motion Sync：设备未返回可用值"));

                var lod = Choices("LOD 抬升高度", new[] { new Choice(0, "0.7 mm"), new Choice(1, "1 mm"), new Choice(2, "2 mm"), new Choice(3, "保持设备当前设置") }, s.Lod);
                panel.Children.Add(lod);

                var response = Number("按键响应时间（ms）", s.Response, 0, 30);
                panel.Children.Add(response);

                ToggleSwitch? angle = supports.Contains(3) && s.Angle is 0 or 1
                    ? new ToggleSwitch { Header = "直线修正", IsOn = s.Angle == 1, OnContent = "开启", OffContent = "关闭" }
                    : null;
                if (angle != null) panel.Children.Add(angle);
                else if (supports.Contains(3)) panel.Children.Add(Text("直线修正：设备未返回可用值"));

                ToggleSwitch? ripple = supports.Contains(4) && s.Ripple is 0 or 1
                    ? new ToggleSwitch { Header = "波纹修正", IsOn = s.Ripple == 1, OnContent = "开启", OffContent = "关闭" }
                    : null;
                if (ripple != null) panel.Children.Add(ripple);
                else if (supports.Contains(4)) panel.Children.Add(Text("波纹修正：设备未返回可用值"));

                void Revert()
                {
                    if (engineRadio != null && s.Mode is >= 0 and <= 2) engineRadio.SelectedIndex = s.Mode;
                    if (motionSync != null) motionSync.IsOn = s.MotionSync == 1;
                    lod.SelectedItem = ((List<Choice>)lod.ItemsSource).FirstOrDefault(x => x.Id == s.Lod);
                    response.Value = s.Response;
                    if (angle != null) angle.IsOn = s.Angle == 1;
                    if (ripple != null) ripple.IsOn = s.Ripple == 1;
                }

                return new SensorEditorRefs(
                    () =>
                    {
                        byte pollId = (byte)Id(polling);
                        int? modeVal = engineRadio?.SelectedIndex is int m and >= 0 and <= 2 ? m : null;
                        int? motionVal = motionSync != null ? (motionSync.IsOn ? 1 : 0) : null;
                        int lodChoice = Id(lod);
                        int? lodVal = lodChoice == 3 ? null : lodChoice;
                        int respVal = Integer(response);
                        int? angleVal = angle != null ? (angle.IsOn ? 1 : 0) : null;
                        int? rippleVal = ripple != null ? (ripple.IsOn ? 1 : 0) : null;

                        _ = ApplyEngineAndPollingAsync(pollId, modeVal, motionVal, lodVal, respVal, angleVal, rippleVal, engineCurrent);
                    },
                    Revert);
            }

            async Task ApplyEngineAndPollingAsync(byte pollId, int? modeVal, int? motionVal, int? lodVal, int respVal, int? angleVal, int? rippleVal, TextBlock? engineCurrent)
            {
                engineStatus.Text = "正在写入并校验设备参数…";
                try
                {
                    var result = await WithMouse(d =>
                    {
                        var beforeState = d.ReadState();
                        if (beforeState.PollingId != pollId)
                        {
                            d.SetPolling(pollId);
                            if (d.ReadState().PollingId != pollId)
                                throw new IOException("回报率读回不一致");
                        }

                        var beforeSensor = d.ReadSensor();
                        if (modeVal is int m && m != beforeSensor.Mode) d.SetSensorValue("Mode", m);
                        if (motionVal is int ms && ms != beforeSensor.MotionSync) d.SetSensorValue("MotionSync", ms);
                        if (lodVal is int lv && lv != beforeSensor.Lod) d.SetSensorValue("LOD", lv);
                        if (respVal != beforeSensor.Response) d.SetSensorValue("Response", respVal);
                        if (angleVal is int av && av != beforeSensor.Angle) d.SetSensorValue("Angle", av);
                        if (rippleVal is int rv && rv != beforeSensor.Ripple) d.SetSensorValue("Ripple", rv);

                        return (State: d.ReadState(), Sensor: d.ReadSensor());
                    });

                    controller.UpdateObservedMouseState(result.State, endpoint!.Path);
                    mouseSensor = result.Sensor;
                    mouseSensorPath = endpoint!.Path;
                    mouseSensorProfile = result.State.Profile;
                    mouseSensorReadAt = DateTime.Now;
                    if (engineCurrent != null && result.Sensor.Mode is >= 0 and <= 2)
                        engineCurrent.Text = "当前引擎：" + new[] { "办公模式", "游戏模式", "超感竞技模式" }[result.Sensor.Mode];
                    engineStatus.Text = $"已应用并读回确认 · {PollingName(result.State.PollingId)} · {mouseSensorReadAt:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    engineStatus.Text = "写入失败：" + ex.Message;
                    AddLog(ex.ToString());
                }
            }

            var engineActionBar = Card("批量应用",
                engineStatus,
                Row(
                    Button("应用传感器与回报率更改", () =>
                    {
                        if (applySensor != null)
                        {
                            applySensor();
                            return Task.CompletedTask;
                        }
                        byte pollId = (byte)Id(polling);
                        return ApplyEngineAndPollingAsync(pollId, null, null, null, mouseSensor?.Response ?? 0, null, null, null);
                    }, true),
                    Button("还原", () =>
                    {
                        polling.SelectedItem = ((List<Choice>)polling.ItemsSource).FirstOrDefault(x => x.Id == mouse!.PollingId);
                        revertSensor?.Invoke();
                        engineStatus.Text = "已还原为上次读取的参数。";
                        return Task.CompletedTask;
                    }),
                    Button("重新读取", ReadSensor)));

            var powerStatus = Text("修改休眠时间后点击应用即可生效，不会重置当前页面。", 12);
            var sleep = Number("休眠时间（秒，0 为关闭）", mouse!.SleepSeconds, 0, 2550, 10);
            var powerCard = Card("电源管理", sleep, powerStatus, Button("应用休眠设置", async () =>
            {
                int seconds = Integer(sleep);
                powerStatus.Text = "正在保存休眠时间…";
                var updated = await WithMouse(d =>
                {
                    d.SetSleep(seconds);
                    var next = d.ReadState();
                    if (next.SleepSeconds != seconds)
                        throw new IOException("休眠读回不一致");
                    return next;
                });
                controller.UpdateObservedMouseState(updated, endpoint!.Path);
                powerStatus.Text = $"休眠时间已更新为 {(seconds == 0 ? "关闭" : $"{seconds} 秒")} · {DateTime.Now:HH:mm:ss}";
            }, true));

            var engine = Column();
            engine.Children.Add(pollingCard);
            engine.Children.Add(sensorCard);
            engine.Children.Add(engineActionBar);

            var dpiContainer = Column();
            MouseDpiEditor(dpiContainer);

            var tabs = new Pivot();
            tabs.Items.Add(new PivotItem { Header = "DPI 灵敏度", Content = dpiContainer });
            tabs.Items.Add(new PivotItem { Header = "引擎与回报率", Content = engine });
            tabs.Items.Add(new PivotItem { Header = "电源", Content = powerCard });
            body.Children.Add(tabs);
            ReadPageOnLoad(sensorCard, body, ReadSensor, "已读取引擎与传感器参数");
            return;
        }

        var kbPowerStatus = Text("自动读取设备当前休眠时间。", 12);
        var kbSleep = Number("休眠时间（秒，0 为关闭）", 0, 0, 65535, 1);
        kbSleep.IsEnabled = false;
        var kbContent = Column();
        kbContent.Children.Add(kbSleep);
        kbContent.Children.Add(kbPowerStatus);
        kbContent.Children.Add(Row(
            Button("重新读取休眠时间", async () =>
            {
                kbPowerStatus.Text = "正在读取休眠时间…";
                kbSleep.Value = await WithKeyboard(d => d.SleepSeconds());
                kbSleep.IsEnabled = true;
                kbPowerStatus.Text = $"已读取休眠时间 · {DateTime.Now:HH:mm:ss}";
            }),
            Button("应用休眠设置", async () =>
            {
                if (!kbSleep.IsEnabled)
                    throw new IOException("请先读取当前休眠时间");
                int seconds = Integer(kbSleep);
                kbPowerStatus.Text = "正在保存休眠时间…";
                await KeyWrite(d => d.SetSleep(seconds));
                kbPowerStatus.Text = $"休眠时间已更新为 {(seconds == 0 ? "关闭" : $"{seconds} 秒")} · {DateTime.Now:HH:mm:ss}";
            }, true)));
        var kbPowerCard = Card("电源管理", kbContent);
        body.Children.Add(kbPowerCard);
        ReadPageOnLoad(kbPowerCard, body, async () =>
        {
            kbSleep.Value = await WithKeyboard(d => d.SleepSeconds());
            kbSleep.IsEnabled = true;
            kbPowerStatus.Text = $"已读取休眠时间 · {DateTime.Now:HH:mm:ss}";
        }, "已读取休眠时间");
    }

    sealed record SensorEditorRefs(Action Apply, Action Revert);
}
