using DareuLocal.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DareuLocal.App;

public sealed partial class MainWindow
{
    bool updatingNavigation;
    bool devicePaneVisible, deviceSelected;

    async Task DeviceTimerTick()
    {
        if (busy) return;
        try
        {
            if (audioSession is { } session)
            {
                bool present = Hardware.Endpoints().Any(x => x.Path == session.EndpointPath);
                if (present && session.Error == null) return;
                string reason = present ? session.Error ?? "音频输出已停止" : "设备已断开";
                try { await StopAudio(); } catch (Exception ex) { AddLog(ex.Message); }
                if (page == "music") Render();
                Status.IsOpen = true; Status.Severity = InfoBarSeverity.Warning;
                Status.Title = "音乐律动已停止"; Status.Message = reason;
                return;
            }
            if (page == "overview")
                await RefreshDashboard();
            else if (endpoint != null && endpoint.CanConfigure && endpoint.Product?.HasBattery == true)
                await RefreshSelectedPowerOnly();
        }
        catch (Exception ex)
        {
            AddLog(ex.Message);
        }
    }

    async Task RefreshSelectedPowerOnly()
    {
        var current = endpoint;
        if (current == null || !current.CanConfigure || current.Product?.HasBattery != true)
            return;
        try
        {
            var power = await controller.ReadPowerAsync(current);
            if (!ReferenceEquals(endpoint, current))
                return;
            selectedPower = power;
            UpdateHeaderSummary();
            CheckLowBatteryNotification(current, power);
            UpdateTrayTooltip();
        }
        catch (IOException) { }
        catch (TimeoutException) { }
    }

    void UpdateDeviceNavigation()
    {
        if (page == "overview") deviceSelected = false;
        var features = DeviceFeatures.For(deviceSelected ? endpoint?.Product : null, deviceSelected && endpoint?.CanConfigure == true);
        var available = new HashSet<string> { "overview", "catalog", "settings" };
        if (features.Performance) available.Add("performance");
        if (features.Mapping) available.Add("mapping");
        if (features.Lighting) available.Add("lighting");
        if (features.PerKeyRgb) available.Add("rgb");
        if (features.AudioReactive) available.Add("music");
        if (features.Macros) available.Add("macros");
        if (features.Profiles) available.Add("profiles");
        if (!available.Contains(page))
            page = features.Performance ? "performance" : features.Mapping ? "mapping" : "overview";

        bool showPane = page != "overview" && deviceSelected;
        Navigation.IsPaneVisible = showPane;
        Navigation.IsPaneToggleButtonVisible = showPane;
        if (showPane != devicePaneVisible) Navigation.IsPaneOpen = showPane;
        devicePaneVisible = showPane;
        updatingNavigation = true;
        try
        {
            var items = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().ToArray();
            foreach (var item in items)
            {
                string tag = item.Tag?.ToString() ?? "";
                item.Visibility = available.Contains(tag) ? Visibility.Visible : Visibility.Collapsed;
                if (tag == "performance")
                    item.Content = endpoint?.Kind == DeviceKind.Keyboard ? "电源管理" : "性能与电源";
            }
            Navigation.SelectedItem = items.FirstOrDefault(x => x.Tag?.ToString() == page);
        }
        finally
        {
            updatingNavigation = false;
        }
    }
}
