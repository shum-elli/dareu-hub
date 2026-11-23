using DareuLocal.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using Windows.UI;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    Dictionary<string, Action<DashboardReading>> homeUpdates = new();
    TextBlock? powerUpdatedText;
    List<DashboardReading> homeDevices = new();
    DateTime lastPowerRefresh = DateTime.MinValue;
    static readonly SolidColorBrush Green = new(Color.FromArgb(255, 115, 218, 161));
    static readonly SolidColorBrush Orange = new(Color.FromArgb(255, 255, 161, 101));
    static readonly SolidColorBrush Muted = new(Color.FromArgb(255, 151, 165, 183));

    async Task RefreshDashboard()
    {
        await controller.ResolveReceiversAsync();
        Scan();
        var all = Hardware.Endpoints();
        var preferred = Hardware.PreferredEndpoints(all);
        controller.BackgroundMouseEndpoint = preferred.FirstOrDefault(x => x.Kind == DeviceKind.Mouse && x.CanConfigure);
        SyncMouseWatcher();
        homeDevices = await controller.ReadDashboardAsync(all, preferred);
        if (appSettings.OsdEnabled && controller.LastObservedMouseStage == null && controller.BackgroundMouseEndpoint != null)
            _ = controller.PollMouseStageAsync();
        lastPowerRefresh = DateTime.UtcNow;

        foreach (var d in homeDevices)
            CheckLowBatteryNotification(d.Endpoint, d.Power);
        UpdateTrayTooltip();

        if (page == "overview")
        {
            if (homeUpdates.Count != homeDevices.Count)
                Render();
            else
            {
                foreach (var state in homeDevices)
                    if (homeUpdates.TryGetValue(state.Endpoint.FamilyKey, out var update))
                        update(state);
                if (powerUpdatedText != null)
                    powerUpdatedText.Text = $"更新于 {lastPowerRefresh.ToLocalTime():HH:mm:ss}";
            }
        }
    }

    void CheckLowBatteryNotification(DeviceEndpoint dev, PowerReading? power)
    {
        if (controller.EvaluateLowBatteryWarning(dev, power) is string message)
            trayIcon?.ShowBalloon("设备电量低", message, warning: true);
    }

    void UpdateTrayTooltip()
    {
        if (trayIcon == null)
            return;
        if (homeDevices.Count > 0)
        {
            var parts = homeDevices.Select(d =>
            {
                string pwr = d.Power?.Level is int l ? $"{l}%" + (d.Power.IsCharging == true ? "⚡" : "") : (d.Endpoint.Product?.HasBattery == false ? "USB" : "待唤醒");
                return $"{d.Endpoint.Name} {pwr}";
            });
            trayIcon.UpdateTooltip("Dareu Local · " + string.Join(" | ", parts));
            return;
        }
        if (endpoint != null)
        {
            string pwr = selectedPower?.Level is int l ? $"{l}%" + (selectedPower.IsCharging == true ? "⚡" : "") : "已连接";
            trayIcon.UpdateTooltip($"Dareu Local · {endpoint.Name} {pwr}");
            return;
        }
        trayIcon.UpdateTooltip("Dareu Local · 未连接设备");
    }

    IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var items = new List<TrayMenuItem>();
        if (homeDevices.Count > 0)
        {
            foreach (var d in homeDevices)
            {
                string conn = d.Endpoint.Wireless ? "2.4G" : "USB";
                string pwr = d.Power?.Level is int l ? $"{l}%" + (d.Power.IsCharging == true ? " 充电中" : "") : (d.Endpoint.Product?.HasBattery == false ? "USB 供电" : "待唤醒");
                items.Add(new TrayMenuItem($"{d.Endpoint.Name} · {conn} · {pwr}", Disabled: true));
            }
        }
        else if (endpoint != null)
        {
            string pwr = selectedPower?.Level is int l ? $"{l}%" : "已接入";
            items.Add(new TrayMenuItem($"{endpoint.Name} · {pwr}", Disabled: true));
        }
        else
        {
            items.Add(new TrayMenuItem("未检测到设备", Disabled: true));
        }

        items.Add(new TrayMenuItem("", Separator: true));

        if (endpoint != null && endpoint.CanConfigure)
        {
            int activeProfile = IsMouse ? (mouse?.Profile ?? 0) : (keyboard?.Profile ?? 0);
            var availableProfiles = IsMouse ? new[] { 1, 2, 3, 4 } : keyboardProfiles;
            items.Add(new TrayMenuItem($"切换 {endpoint.Name} 配置", Disabled: true));
            foreach (int p in availableProfiles)
            {
                int profileId = p;
                items.Add(new TrayMenuItem(
                    appSettings.GetProfileDisplayName(endpoint.FamilyKey, profileId),
                    () => _ = SwitchProfileAsync(profileId),
                    Checked: profileId == activeProfile));
            }
            items.Add(new TrayMenuItem("", Separator: true));
        }

        items.Add(new TrayMenuItem("打开 Dareu Local", RestoreFromTray));
        items.Add(new TrayMenuItem("隐藏到托盘", () => AppWindow.Hide()));
        items.Add(new TrayMenuItem("", Separator: true));
        items.Add(new TrayMenuItem("退出", ExitFromTray));
        return items;
    }

    void Overview(StackPanel body)
    {
        homeUpdates.Clear();
        var all = Hardware.Endpoints();
        var preferred = Hardware.PreferredEndpoints(all);
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var count = Text($"{preferred.Count} 台设备已接入", 16);
        heading.Children.Add(count);
        var note = Text(lastPowerRefresh == DateTime.MinValue ? "正在读取设备状态…" : $"更新于 {lastPowerRefresh.ToLocalTime():HH:mm:ss}", 12);
        powerUpdatedText = note;
        note.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(note, 1);
        heading.Children.Add(note);
        body.Children.Add(heading);
        body.Children.Add(Row(Button("设备库", () => Navigate("catalog")), Button("设置与诊断", () => Navigate("settings"))));
        if (preferred.Count == 0)
        {
            body.Children.Add(Card("还没有发现设备", Text("连接鼠标或键盘的数据线，也可以插入 2.4G 接收器。设备接入后会自动出现在这里。")));
            return;
        }
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 20 };
        var tiles = new List<FrameworkElement>();
        foreach (var e in preferred)
        {
            var cached = homeDevices.FirstOrDefault(x => x.Endpoint.Path == e.Path);
            var state = cached ?? new DashboardReading(e, null, all.Any(x => x.FamilyKey == e.FamilyKey && x.Wireless) && all.Any(x => x.FamilyKey == e.FamilyKey && !x.Wireless), null);
            tiles.Add(DeviceTile(state));
        }
        foreach (var tile in tiles)
            grid.Children.Add(tile);
        void Arrange(double width)
        {
            int columns = width >= 760 ? 2 : 1;
            if (grid.ColumnDefinitions.Count == columns)
                return;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (int c = 0; c < columns; c++)
                grid.ColumnDefinitions.Add(new());
            for (int i = 0; i < (tiles.Count + columns - 1) / columns; i++)
                grid.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
            for (int i = 0; i < tiles.Count; i++)
            {
                Grid.SetColumn(tiles[i], i % columns);
                Grid.SetRow(tiles[i], i / columns);
            }
        }
        Arrange(Math.Max(400, Root.ActualWidth - 300));
        grid.SizeChanged += (s, e) => Arrange(e.NewSize.Width);
        body.Children.Add(grid);
        body.Children.Add(Text("点击设备卡片进入设置。电量与充电状态会自动更新。", 13));
    }

    Button DeviceTile(DashboardReading state)
    {
        bool isMouse = state.Endpoint.Kind == DeviceKind.Mouse;
        string name = state.Endpoint.Name;
        var content = Column(18);
        var top = new Grid();
        top.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        top.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var title = Column(6);
        title.Children.Add(Text(state.Endpoint.Kind switch
        {
            DeviceKind.Mouse => "鼠标",
            DeviceKind.Keyboard => "键盘",
            DeviceKind.Receiver => "接收器",
            _ => "设备"
        }, 12));
        title.Children.Add(new TextBlock { Text = name, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        top.Children.Add(title);
        bool confirmed = state.Power?.Level != null || state.Endpoint.Product?.HasBattery == false && state.Endpoint.Kind is DeviceKind.Mouse or DeviceKind.Keyboard;
        var dot = new TextBlock { Text = "●", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Foreground = confirmed ? Green : Orange };
        var statusText = new TextBlock { Text = confirmed ? "已连接" : state.Endpoint.Wireless ? "待唤醒" : "已接入", Foreground = confirmed ? Green : Orange, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var status = Row(dot, statusText);
        Grid.SetColumn(status, 1);
        top.Children.Add(status);
        content.Children.Add(top);
        var picture = new Image { Source = ProductImage(state.Endpoint.Product), Stretch = Stretch.Uniform, Height = 260, MaxWidth = isMouse ? 180 : 430, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(picture, name + " 官方设备图片");
        content.Children.Add(new Border { Height = 300, Padding = new Thickness(12, 12, 12, 10), CornerRadius = new CornerRadius(12), Background = ThemePalette.Recessed, Child = picture });
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        footer.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var connection = Column(7);
        connection.Children.Add(Text("连接方式", 12));
        connection.Children.Add(new TextBlock { Text = state.Endpoint.Wireless ? "2.4G 无线" : "USB 有线", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        footer.Children.Add(connection);
        var power = Column(6);
        power.HorizontalAlignment = HorizontalAlignment.Right;
        var batteryGraphic = new ContentControl { Content = BatteryIcon(state.Power) };
        var levelText = new TextBlock { Text = state.Power?.Level is int n ? $"{n}%" : "—", FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        var level = Row(batteryGraphic, levelText);
        power.Children.Add(level);
        var caption = Text(ChargeCaption(state.Endpoint, state.Power), 12);
        caption.Foreground = state.Power?.IsCharging == true ? Green : Muted;
        power.Children.Add(caption);
        Grid.SetColumn(power, 1);
        footer.Children.Add(power);
        content.Children.Add(footer);
        var hint = Text(state.HasBoth ? "有线与接收器均已接入，当前优先使用有线。" : state.Error != null ? (state.Endpoint.Wireless ? "请唤醒设备，再刷新状态。" : "暂时无法读取电量，请稍后刷新。") : "点击设备，进入设置  →", 12);
        content.Children.Add(hint);
        var tile = Button("", () => Run(() => OpenHomeDevice(state.Endpoint.FamilyKey), $"已打开 {name}"));
        tile.Content = content;
        tile.HorizontalAlignment = HorizontalAlignment.Stretch;
        tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tile.Padding = new Thickness(24);
        tile.CornerRadius = new CornerRadius(16);
        tile.BorderThickness = new Thickness(1);
        tile.BorderBrush = ThemePalette.Border;
        tile.Background = ThemePalette.Card;
        AutomationProperties.SetName(tile, $"设置 {name}，{(state.Endpoint.Wireless ? "2.4G 无线" : "USB 有线")}，{PowerCaption(state.Power)}");
        ToolTipService.SetToolTip(tile, state.Error ?? $"设置 {name}");
        homeUpdates[state.Endpoint.FamilyKey] = next => { levelText.Text = next.Power?.Level is int n ? $"{n}%" : "—"; batteryGraphic.Content = BatteryIcon(next.Power); caption.Text = ChargeCaption(next.Endpoint, next.Power); caption.Foreground = next.Power?.IsCharging == true ? Green : Muted; bool ok = next.Power?.Level != null || next.Endpoint.Product?.HasBattery == false && next.Endpoint.Kind is DeviceKind.Mouse or DeviceKind.Keyboard; dot.Foreground = statusText.Foreground = ok ? Green : Orange; statusText.Text = ok ? "已连接" : next.Endpoint.Wireless ? "待唤醒" : "已接入"; hint.Text = next.HasBoth ? "有线与接收器均已接入，当前优先使用有线。" : next.Error != null ? (next.Endpoint.Wireless ? "请唤醒设备，再刷新状态。" : "暂时无法读取电量，请稍后刷新。") : "点击设备，进入设置  →"; AutomationProperties.SetName(tile, $"设置 {name}，{(next.Endpoint.Wireless ? "2.4G 无线" : "USB 有线")}，{PowerCaption(next.Power)}"); ToolTipService.SetToolTip(tile, next.Error ?? $"设置 {name}"); };
        return tile;
    }

    async Task OpenHomeDevice(string family)
    {
        var preferred = Hardware.PreferredEndpoints(Hardware.Endpoints());
        var target = preferred.FirstOrDefault(x => x.FamilyKey == family) ?? throw new IOException("设备已断开，请重新连接。");
        rendering = true;
        DeviceBox.ItemsSource = preferred;
        endpoint = target;
        DeviceBox.SelectedItem = target;
        rendering = false;
        mouse = null;
        keyboard = null;
        if (target.CanConfigure)
        {
            await Connect();
            deviceSelected = true;
            await Navigate(target.Kind == DeviceKind.Mouse ? "performance" : "mapping");
        }
        else
        {
            deviceSelected = true;
            await Navigate("catalog");
            Render();
        }
    }

    BitmapImage? ProductImage(ProductDefinition? product)
    {
        if (product == null)
            return null;

        string pidPng = $"{product.Pid:x4}.png";
        string localAppDevices = Path.Combine(controller.UserFolder, "Assets", "Devices", pidPng);
        if (File.Exists(localAppDevices))
            return new BitmapImage(new Uri(localAppDevices));

        if (!string.IsNullOrWhiteSpace(product.Image))
        {
            string localAppImg = Path.Combine(controller.UserFolder, "Assets", product.Image);
            if (File.Exists(localAppImg))
                return new BitmapImage(new Uri(localAppImg));
        }

        string appDirDevices = Path.Combine(AppContext.BaseDirectory, "Assets", "Devices", pidPng);
        if (File.Exists(appDirDevices))
            return new BitmapImage(new Uri(appDirDevices));

        if (!string.IsNullOrWhiteSpace(product.Image))
        {
            string appDirImg = Path.Combine(AppContext.BaseDirectory, "Assets", product.Image);
            if (File.Exists(appDirImg))
                return new BitmapImage(new Uri($"ms-appx:///Assets/{product.Image}"));
        }

        string art = product.Type == 0 ? "GenericMouse.png" : product.Type == 1 ? "GenericKeyboard.png" : "GenericReceiver.png";
        return new BitmapImage(new Uri($"ms-appx:///Assets/{art}"));
    }

    static string ChargeCaption(DeviceEndpoint endpoint, PowerReading? power) => endpoint.Product?.HasBattery == false ? "USB 供电" : power?.StateText ?? "状态未知";
    static string PowerCaption(PowerReading? power) => power == null ? "电量未知" : $"{(power.Level is int level ? $"{level}%" : "电量未知")} · {power.StateText}";
    static Grid BatteryIcon(PowerReading? power)
    {
        var brush = power?.Level is int n ? n <= 15 ? Orange : Green : Muted;
        var grid = new Grid { Width = 43, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Border { Width = 36, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1.5), BorderBrush = brush, CornerRadius = new CornerRadius(3) });
        grid.Children.Add(new Border { Width = 3, Height = 8, Margin = new Thickness(38, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Background = brush, CornerRadius = new CornerRadius(1) });
        grid.Children.Add(new Border { Width = power?.Level is int level ? 30 * level / 100.0 : 0, Height = 12, Margin = new Thickness(3, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Background = brush, CornerRadius = new CornerRadius(1) });
        if (power?.IsCharging == true)
            grid.Children.Add(new TextBlock { Text = "ϟ", FontSize = 25, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), Margin = new Thickness(10, -5, 0, 0) });
        AutomationProperties.SetName(grid, PowerCaption(power));
        return grid;
    }
}
