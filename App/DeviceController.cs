using DareuLocal.Core;

namespace DareuLocal.App;

internal sealed record DashboardReading(DeviceEndpoint Endpoint, PowerReading? Power, bool HasBoth, string? Error);

internal sealed record ConnectSummary(bool IsMouse, int Pid, byte Target, int Cid, int Mid, int Type);

internal sealed record ProfileSwitchOutcome(bool Changed, string DisplayName, string Subtitle);

/// <summary>
/// Owns hardware session state, serialized HID execution, settings persistence,
/// profile switching, and background DPI/battery tracking independent of WinUI controls.
/// </summary>
internal sealed class DeviceController : IDisposable
{
    readonly HashSet<string> notifiedLowBattery = new();
    readonly object watcherLock = new();
    JmMouseWatcher? mouseWatcher;

    public event Action<DeviceEndpoint>? MouseHardwareEvent;

    public string UserFolder { get; }
    public AppSettingsStore SettingsStore { get; }
    public AppSettings Settings { get; }
    public DeviceSessionQueue Queue { get; } = new();

    public DeviceEndpoint? Endpoint { get; set; }
    public DeviceEndpoint? BackgroundMouseEndpoint { get; set; }
    public MouseState? Mouse { get; set; }
    public KeyboardState? Keyboard { get; set; }
    public SensorState? MouseSensor { get; set; }
    public string? MouseSensorPath { get; set; }
    public int MouseSensorProfile { get; set; }
    public DateTime MouseSensorReadAt { get; set; }
    public PowerReading? SelectedPower { get; set; }
    public int[] KeyboardProfiles { get; set; } = { 1, 2 };

    public int? LastObservedMouseStage { get; set; }
    public int? LastObservedMouseProfile { get; set; }
    public string? LastObservedMousePath { get; set; }

    public bool IsMouse => Endpoint?.Kind == DeviceKind.Mouse;

    public DeviceController(string userFolder)
    {
        UserFolder = userFolder;
        SettingsStore = new AppSettingsStore(userFolder);
        Settings = SettingsStore.Load();
    }

    public void SaveSettings() => SettingsStore.Save(Settings);

    public void UpdateObservedMouseState(MouseState state, string? path = null)
    {
        Mouse = state;
        LastObservedMouseStage = state.Stage;
        LastObservedMouseProfile = state.Profile;
        if (path != null)
            LastObservedMousePath = path;
    }

    public void ResetObservedMouse()
    {
        LastObservedMouseStage = null;
        LastObservedMouseProfile = null;
        LastObservedMousePath = null;
    }

    public Task<T> WithMouseAsync<T>(Func<JmMouse, T> action, CancellationToken ct = default)
    {
        var selected = Endpoint ?? throw new IOException("请先选择设备");
        if (selected.Kind != DeviceKind.Mouse || !selected.CanConfigure)
            throw new IOException("请先选择鼠标");
        return Queue.EnqueueAsync(token => Task.Run(() =>
        {
            using var d = new JmMouse(selected);
            return action(d);
        }, token), ct);
    }

    public Task<T> WithKeyboardAsync<T>(Func<TgKeyboard, T> action, CancellationToken ct = default)
    {
        var selected = Endpoint ?? throw new IOException("请先选择设备");
        if (selected.Kind != DeviceKind.Keyboard || !selected.CanConfigure)
            throw new IOException("请先选择键盘");
        return Queue.EnqueueAsync(token => Task.Run(() =>
        {
            using var d = new TgKeyboard(selected);
            d.ReadState();
            return action(d);
        }, token), ct);
    }

    public Task WriteMouseAsync(Action<JmMouse> action, CancellationToken ct = default) =>
        WithMouseAsync(d => { action(d); return true; }, ct);

    public Task WriteKeyboardAsync(Action<TgKeyboard> action, CancellationToken ct = default) =>
        WithKeyboardAsync(d => { action(d); return true; }, ct);

    public Task ResolveReceiversAsync(CancellationToken ct = default) =>
        Queue.EnqueueAsync(token => Task.Run(DeviceIdentityProbe.ResolveReceivers, token), ct);

    public Task<PowerReading> ReadPowerAsync(DeviceEndpoint target, CancellationToken ct = default) =>
        Queue.EnqueueAsync(token => Task.Run(() => DeviceIdentityProbe.ReadPower(target), token), ct);

    public Task<List<DashboardReading>> ReadDashboardAsync(
        IReadOnlyList<DeviceEndpoint> all,
        IReadOnlyList<DeviceEndpoint> preferred,
        CancellationToken ct = default)
    {
        return Queue.EnqueueAsync(token => Task.Run(() => preferred.Select(e =>
        {
            bool both = all.Any(x => x.FamilyKey == e.FamilyKey && x.Wireless) &&
                        all.Any(x => x.FamilyKey == e.FamilyKey && !x.Wireless);
            try { return new DashboardReading(e, DeviceIdentityProbe.ReadPower(e), both, null); }
            catch (Exception ex) { return new DashboardReading(e, null, both, ex.Message); }
        }).ToList(), token), ct);
    }

    public async Task<ConnectSummary> ConnectCurrentAsync(CancellationToken ct = default)
    {
        var selected = Endpoint ?? throw new IOException("未发现设备，请检查数据线或接收器。");
        if (!selected.CanConfigure)
            throw new InvalidOperationException("当前设备暂不支持写入配置");

        if (selected.Kind == DeviceKind.Mouse)
        {
            var reading = await WithMouseAsync(d =>
            {
                var state = d.ReadState();
                var power = d.ReadPower();
                SensorState? sensor = null;
                try { sensor = d.ReadSensor(); } catch (IOException) { }
                return (State: state, Power: power, Sensor: sensor);
            }, ct);

            MouseSensor = reading.Sensor;
            MouseSensorPath = selected.Path;
            MouseSensorProfile = reading.State.Profile;
            MouseSensorReadAt = DateTime.Now;
            SelectedPower = reading.Power;
            Keyboard = null;
            UpdateObservedMouseState(reading.State, selected.Path);
            BackgroundMouseEndpoint = selected;

            return new ConnectSummary(true, selected.Pid, 0, reading.State.Cid, reading.State.Mid, reading.State.Type);
        }
        else
        {
            var reading = await WithKeyboardAsync(d => (State: d.ReadState(), Power: d.ReadPower(), Profiles: d.Profiles()), ct);
            Keyboard = reading.State;
            SelectedPower = reading.Power;
            KeyboardProfiles = reading.Profiles;
            Mouse = null;
            ResetObservedMouse();

            return new ConnectSummary(false, reading.State.Pid, (byte)reading.State.Target, 0, 0, 0);
        }
    }

    public async Task<ProfileSwitchOutcome?> SwitchProfileAsync(
        int profileId,
        bool fromAutoSwitch = false,
        string? triggerProcess = null,
        CancellationToken ct = default)
    {
        var selected = Endpoint;
        if (selected == null || !selected.CanConfigure)
            return null;

        bool changed = false;
        if (selected.Kind == DeviceKind.Mouse)
        {
            var next = await WithMouseAsync(d =>
            {
                var before = d.ReadState();
                if (before.Profile == profileId)
                    return (Changed: false, State: before);
                d.SetProfile(profileId);
                var after = d.ReadState();
                if (after.Profile != profileId)
                    throw new IOException("配置切换读回不一致");
                return (Changed: true, State: after);
            }, ct);
            UpdateObservedMouseState(next.State, selected.Path);
            changed = next.Changed;
        }
        else if (selected.Kind == DeviceKind.Keyboard)
        {
            var next = await WithKeyboardAsync(d =>
            {
                var before = d.ReadState();
                if (!d.Profiles().Contains(profileId))
                    throw new ArgumentException("设备不支持此配置编号");
                if (before.Profile == profileId)
                    return (Changed: false, State: before);
                d.SetProfile(profileId);
                var after = d.ReadState();
                if (after.Profile != profileId)
                    throw new IOException("配置切换读回不一致");
                return (Changed: true, State: after);
            }, ct);
            Keyboard = next.State;
            changed = next.Changed;
        }

        string display = Settings.GetProfileDisplayName(selected.FamilyKey, profileId);
        string subtitle = fromAutoSwitch
            ? (triggerProcess != null ? $"已匹配应用：{triggerProcess}" : $"{selected.Name} · 默认配置")
            : selected.Name;
        return new ProfileSwitchOutcome(changed, display, subtitle);
    }

    public async Task<(MouseState State, bool StageChanged, DeviceEndpoint MouseEndpoint)?> PollMouseStageAsync(CancellationToken ct = default)
    {
        var mouseEp = (Endpoint?.Kind == DeviceKind.Mouse && Endpoint.CanConfigure)
            ? Endpoint
            : BackgroundMouseEndpoint;
        if (mouseEp == null || !mouseEp.CanConfigure || mouseEp.Kind != DeviceKind.Mouse)
            return null;

        MouseState next;
        try
        {
            next = await Queue.EnqueueAsync(token => Task.Run(() =>
            {
                using var d = new JmMouse(mouseEp);
                return d.ReadState();
            }, token), ct);
        }
        catch (IOException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }

        bool sameDevice = LastObservedMousePath == mouseEp.Path;
        bool stageChanged = sameDevice &&
                            LastObservedMouseStage is int prevStage &&
                            prevStage != next.Stage;

        if (Endpoint?.Path == mouseEp.Path)
            Mouse = next;
        LastObservedMousePath = mouseEp.Path;
        LastObservedMouseStage = next.Stage;
        LastObservedMouseProfile = next.Profile;

        return (next, stageChanged, mouseEp);
    }

    public string? EvaluateLowBatteryWarning(DeviceEndpoint dev, PowerReading? power)
    {
        if (!Settings.LowBatteryAlertEnabled || power?.Level is not int level)
            return null;

        if (level <= Settings.LowBatteryThreshold && power.IsCharging != true)
        {
            if (notifiedLowBattery.Add(dev.FamilyKey))
                return $"{dev.Name} 当前电量 {level}%，请及时充电。";
        }
        else if (level > Settings.LowBatteryThreshold + 5 || power.IsCharging == true)
        {
            notifiedLowBattery.Remove(dev.FamilyKey);
        }
        return null;
    }

    public void ResetLowBatteryAlerts() => notifiedLowBattery.Clear();

    public bool UpdateMouseWatcher(bool enabled)
    {
        var mouseEp = (Endpoint?.Kind == DeviceKind.Mouse && Endpoint.CanConfigure)
            ? Endpoint
            : BackgroundMouseEndpoint;

        lock (watcherLock)
        {
            if (!enabled || mouseEp == null || !mouseEp.CanConfigure || mouseEp.Kind != DeviceKind.Mouse)
            {
                mouseWatcher?.Dispose();
                mouseWatcher = null;
                return false;
            }

            if (mouseWatcher?.EndpointPath == mouseEp.Path)
                return false;

            mouseWatcher?.Dispose();
            mouseWatcher = null;

            var watcher = new JmMouseWatcher(mouseEp);
            watcher.HardwareEventReceived += OnWatcherHardwareEvent;
            mouseWatcher = watcher;
            return true;
        }
    }

    void OnWatcherHardwareEvent(DeviceEndpoint ep, byte[] _) => MouseHardwareEvent?.Invoke(ep);

    public void Dispose()
    {
        lock (watcherLock)
        {
            mouseWatcher?.Dispose();
            mouseWatcher = null;
        }
        Queue.Dispose();
    }
}
