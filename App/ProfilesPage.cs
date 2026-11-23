using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DareuLocal.Core;
using System.Net.Http;
using System.Text.Json;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    void Profiles(StackPanel body)
    {
        var currentEndpoint = endpoint!;
        string familyKey = currentEndpoint.FamilyKey;
        int currentProfile = IsMouse ? mouse!.Profile : keyboard!.Profile;
        var profileIds = IsMouse ? Enumerable.Range(1, 4).ToArray() : keyboardProfiles;

        var profileChoices = profileIds.Select(i => new Choice(i, appSettings.GetProfileDisplayName(familyKey, i))).ToList();
        var profile = Choices("目标板载配置", profileChoices, currentProfile);

        var aliasBox = new TextBox
        {
            Header = $"Profile {currentProfile} 本地别名（例如 办公 / FPS / 剪辑）",
            Text = appSettings.ProfileAliases.TryGetValue(familyKey, out var map) && map.TryGetValue(currentProfile, out var existingAlias)
                ? existingAlias
                : ""
        };
        var aliasStatus = Text("", 12);

        body.Children.Add(Card("板载配置与别名",
            Text("切换后会重新读取该配置。别名仅保存在本机，用于托盘菜单和 OSD 提示。"),
            profile,
            ActionButton("切换配置", () => SwitchProfileAsync(Id(profile)), true),
            aliasBox,
            aliasStatus,
            Button("保存当前配置别名", () =>
            {
                appSettings.SetProfileAlias(familyKey, currentProfile, aliasBox.Text);
                SaveSettings();
                UpdateHeaderSummary();
                UpdateTrayTooltip();
                profile.ItemsSource = profileIds.Select(i => new Choice(i, appSettings.GetProfileDisplayName(familyKey, i))).ToList();
                profile.SelectedItem = ((List<Choice>)profile.ItemsSource).FirstOrDefault(x => x.Id == currentProfile);
                aliasStatus.Text = $"已更新为：{appSettings.GetProfileDisplayName(familyKey, currentProfile)}";
                return Task.CompletedTask;
            })));

        // Per-app foreground auto-switching
        var autoSwitchToggle = new ToggleSwitch
        {
            Header = "启用前台应用自动切换",
            IsOn = appSettings.AutoSwitchEnabled,
            OnContent = "已开启（低开销 Win32 事件监听）",
            OffContent = "已关闭"
        };
        autoSwitchToggle.Toggled += (_, _) =>
        {
            appSettings.AutoSwitchEnabled = autoSwitchToggle.IsOn;
            foregroundWatcher.SetEnabled(appSettings.AutoSwitchEnabled);
            SaveSettings();
        };

        var defaultChoices = new[] { new Choice(0, "保持当前配置") }
            .Concat(profileIds.Select(i => new Choice(i, appSettings.GetProfileDisplayName(familyKey, i))))
            .ToList();
        int currentDefault = appSettings.DefaultProfiles.TryGetValue(familyKey, out var defId) ? defId : 0;
        var defaultProfileBox = Choices("未匹配任何规则时切回", defaultChoices, currentDefault);
        defaultProfileBox.SelectionChanged += (_, _) =>
        {
            int val = Id(defaultProfileBox);
            if (val == 0) appSettings.DefaultProfiles.Remove(familyKey);
            else appSettings.DefaultProfiles[familyKey] = val;
            SaveSettings();
        };

        var procBox = new TextBox { Header = "进程名或 .exe 文件", PlaceholderText = "例如 cs2 或 valorant.exe" };
        var ruleProfileBox = Choices("目标配置", profileIds.Select(i => new Choice(i, appSettings.GetProfileDisplayName(familyKey, i))), currentProfile);
        var rulesPanel = Column(8);
        var ruleStatus = Text("", 12);

        void RebuildRules()
        {
            rulesPanel.Children.Clear();
            var deviceRules = appSettings.AppRules.Where(r => r.FamilyKey == familyKey).ToList();
            if (deviceRules.Count == 0)
            {
                rulesPanel.Children.Add(Text("暂无应用切换规则。", 12));
                return;
            }
            foreach (var r in deviceRules)
            {
                var rule = r;
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var label = Text($"{rule.ProcessName}.exe  →  {appSettings.GetProfileDisplayName(familyKey, rule.Profile)}");
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
                var del = Button("删除", () =>
                {
                    appSettings.AppRules.RemoveAll(x => x.FamilyKey == familyKey && x.ProcessName == rule.ProcessName);
                    SaveSettings();
                    RebuildRules();
                    return Task.CompletedTask;
                });
                del.Padding = new Thickness(12, 6, 12, 6);
                Grid.SetColumn(del, 1);
                row.Children.Add(del);
                rulesPanel.Children.Add(row);
            }
        }
        RebuildRules();

        body.Children.Add(Card("按前台应用自动切换",
            Text("当指定程序窗口处于前台时自动切到目标板载配置；切回桌面或其他未绑定程序时切回默认配置。"),
            autoSwitchToggle,
            defaultProfileBox,
            procBox,
            Row(
                Button("浏览 .exe", async () =>
                {
                    string? picked = await PickOpen(".exe");
                    if (picked != null)
                        procBox.Text = AppSettings.NormalizeProcessName(picked);
                }),
                Button("添加规则", () =>
                {
                    string proc = AppSettings.NormalizeProcessName(procBox.Text);
                    if (proc.Length == 0)
                        throw new ArgumentException("请输入进程名或选择 .exe 文件");
                    int target = Id(ruleProfileBox);
                    appSettings.AppRules.RemoveAll(r => r.FamilyKey == familyKey && r.ProcessName == proc);
                    appSettings.AppRules.Add(new AppProfileRule(proc, familyKey, target));
                    SaveSettings();
                    procBox.Text = "";
                    ruleStatus.Text = $"已添加规则：{proc}.exe → {appSettings.GetProfileDisplayName(familyKey, target)}";
                    RebuildRules();
                    return Task.CompletedTask;
                }, true)),
            ruleProfileBox,
            ruleStatus,
            rulesPanel));

        var restoreStatus = Text("自动备份位置：" + Path.Combine(userFolder, "Backups"), 12);
        body.Children.Add(Card("备份与恢复",
            Text("备份包含当前配置的按键、灯效、性能设置及设备宏；逐键 RGB 颜色方案请在逐键 RGB 页单独导出。导入支持分步校验与失败自动回滚。"),
            ActionButton("导出当前设备配置", async () =>
            {
                string? path = await PickSave((endpoint?.Product?.Model ?? "DAREU") + "-backup", ".json");
                if (path == null) return;
                var backup = await CaptureBackup();
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup, Json));
            }),
            ActionButton("导入配置备份", async () =>
            {
                string? path = await PickOpen();
                if (path == null) return;
                var backup = JsonSerializer.Deserialize<DeviceBackup>(await File.ReadAllTextAsync(path), Json) ?? throw new ArgumentException("备份文件无效");
                ValidateBackup(backup);
                await Confirm("恢复设备配置", $"将把 {backup.Device} 备份写入当前 Profile，并覆盖备份中对应的设置和宏槽位。继续前会自动备份当前状态，如中途失败将自动回滚。", async () =>
                {
                    var current = await CaptureBackup();
                    await SaveAutomatic(current);
                    await RestoreBackup(backup, current, (step, total, name) =>
                        DispatcherQueue.TryEnqueue(() => restoreStatus.Text = $"正在恢复 ({step}/{total})：{name}…"));
                    restoreStatus.Text = $"备份恢复完成并已校验 · {DateTime.Now:HH:mm:ss}";
                    await Connect();
                });
            }),
            restoreStatus));

        body.Children.Add(Card("恢复默认",
            Text("仅在需要重置时使用。应用会在重置前自动备份当前状态。"),
            ActionButton(IsMouse ? "恢复鼠标出厂设置" : "恢复当前键盘配置", () => Confirm("恢复默认设置", "会覆盖设备当前设置。请确认已准备好恢复默认。", async () =>
            {
                await SaveAutomatic(await CaptureBackup());
                if (IsMouse) await MouseWrite(d => d.FactoryReset());
                else await KeyWrite(d => d.ResetProfile());
                await Connect();
            }))));
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

    async Task RestoreBackup(DeviceBackup b, DeviceBackup rollbackSnapshot, Action<int, int, string> onProgress)
    {
        ValidateBackup(b);
        ValidateBackup(rollbackSnapshot);

        BackupTransactionResult txResult;
        if (IsMouse)
        {
            txResult = await WithMouse(d =>
            {
                var steps = BuildMouseRestoreSteps(d, b);
                void Rollback()
                {
                    foreach (var s in BuildMouseRestoreSteps(d, rollbackSnapshot))
                        s.Execute();
                }
                return BackupTransaction.Execute(steps, Rollback, onProgress);
            });
        }
        else
        {
            txResult = await WithKeyboard(d =>
            {
                var supported = d.ReadLights();
                foreach (var light in b.Lights!)
                {
                    var match = supported.FirstOrDefault(x => x.Region == light.Region) ?? throw new ArgumentException("设备不支持备份中的灯光区域");
                    if (!match.Effects.Contains(light.Effect)) throw new ArgumentException("设备不支持备份中的灯效");
                }
                var steps = BuildKeyboardRestoreSteps(d, b);
                void Rollback()
                {
                    foreach (var s in BuildKeyboardRestoreSteps(d, rollbackSnapshot))
                        s.Execute();
                }
                return BackupTransaction.Execute(steps, Rollback, onProgress);
            });
        }

        if (!txResult.Succeeded)
        {
            string detail = txResult.RolledBack
                ? $"步骤“{txResult.FailedStep}”失败（{txResult.Error?.Message}），已自动回滚到恢复前状态。"
                : $"步骤“{txResult.FailedStep}”失败（{txResult.Error?.Message}），且自动回滚失败（{txResult.RollbackError?.Message}），请检查 %LOCALAPPDATA%/DareuLocal/Backups 中的自动备份。";
            throw new IOException(detail, txResult.Error);
        }
    }

    static List<RestoreStep> BuildMouseRestoreSteps(JmMouse d, DeviceBackup b)
    {
        var m = b.Mouse!;
        var steps = new List<RestoreStep>
        {
            new("性能与 DPI 档位", () =>
            {
                d.SetStageCount(m.Dpi.Length);
                for (int i = 0; i < m.Dpi.Length; i++) d.SetDpi(i + 1, m.Dpi[i]);
                d.SetStage(m.Stage);
                d.SetPolling((byte)m.PollingId);
                d.SetSleep(m.SleepSeconds);
                var actual = d.ReadState();
                if (!actual.Dpi.SequenceEqual(m.Dpi) || actual.Stage != m.Stage || actual.PollingId != m.PollingId || actual.SleepSeconds != m.SleepSeconds)
                    throw new IOException("恢复后的鼠标性能设置读回不一致");
            })
        };
        if (b.Sensor != null)
            steps.Add(new("传感器与引擎", () => d.RestoreSensor(b.Sensor)));
        steps.Add(new("鼠标按键映射", () =>
        {
            for (int i = 1; i <= d.Endpoint.Product!.ButtonCount; i++)
                d.RestoreButton(i, b.Buttons![i - 1], b.ButtonKeys![i - 1]);
        }));
        steps.Add(new("鼠标宏槽位", () =>
        {
            foreach (var pair in b.Macros!)
                if (pair.Value.Length > 0) d.WriteMacroRaw(pair.Key, pair.Value);
                else d.ClearMacro(pair.Key);
        }));
        return steps;
    }

    static List<RestoreStep> BuildKeyboardRestoreSteps(TgKeyboard d, DeviceBackup b)
    {
        var steps = new List<RestoreStep>
        {
            new("键盘按键映射", () =>
            {
                foreach (var key in b.Keys!)
                    d.SetAssignment(key.Key, key.Layer, key.Value);
            }),
            new("键盘灯光", () =>
            {
                foreach (var light in b.Lights!)
                    d.SetLight(light);
            }),
            new("键盘休眠时间", () => d.SetSleep(b.Sleep)),
            new("键盘宏槽位", () =>
            {
                foreach (var pair in b.Macros!)
                    if (pair.Value.Length > 0) d.WriteMacroRaw((byte)pair.Key, $"Macro {pair.Key}", pair.Value);
            })
        };
        return steps;
    }

    void SettingsPage(StackPanel body)
    {
        string versionStr = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";
        var updateStatus = Text("仅在点击按钮时向 GitHub 查询最新版本，平时完全离线运行。", 12);

        body.Children.Add(Card("Dareu Local " + versionStr,
            Text("原生 WinUI 3 · 设备设置在本机完成 · 无需浏览器"),
            Text("已接入多型号设备资料与按能力适配。AE6 V2 继续按用户指定的 1330 参数工作；其他型号分别加载自身布局与能力。"),
            Text("已加入桌面驻留、应用自动切换、逐键画笔 RGB 与频谱音乐律动。蓝牙、磁轴 RT/键程、LCD 与固件刷写仍待适配。"),
            updateStatus,
            Button("检查新版本", async () => await CheckForUpdatesAsync(versionStr, updateStatus))));

        // Desktop residency & notification settings
        var desktopStatus = Text("所有设置保存在本机：%LOCALAPPDATA%\\DareuLocal\\settings.json", 12);
        bool loadingToggles = true;

        var startWithWin = new ToggleSwitch { Header = "开机自动启动", IsOn = appSettings.StartWithWindows, OnContent = "开启", OffContent = "关闭" };
        var startMin = new ToggleSwitch { Header = "启动时最小化到托盘", IsOn = appSettings.StartMinimized, OnContent = "静默启动", OffContent = "正常显示窗口" };
        var closeToTray = new ToggleSwitch { Header = "点击关闭按钮时最小化到托盘", IsOn = appSettings.CloseToTray, OnContent = "隐藏到托盘", OffContent = "直接退出" };
        var lowBattAlert = new ToggleSwitch { Header = "低电量系统气泡提示", IsOn = appSettings.LowBatteryAlertEnabled, OnContent = "开启", OffContent = "关闭" };
        var lowBattThreshold = Number("低电量提醒阈值（%）", appSettings.LowBatteryThreshold, 5, 50, 5);
        var osdToggle = new ToggleSwitch { Header = "DPI / 配置切换系统通知", IsOn = appSettings.OsdEnabled, OnContent = "开启", OffContent = "关闭" };

        startWithWin.Toggled += (_, _) =>
        {
            if (loadingToggles) return;
            try
            {
                AppSettingsStore.ApplyStartWithWindows(startWithWin.IsOn, startMin.IsOn);
                appSettings.StartWithWindows = startWithWin.IsOn;
                SaveSettings();
                desktopStatus.Text = startWithWin.IsOn ? "已写入当前用户开机启动项。" : "已移除开机启动项。";
            }
            catch (Exception ex)
            {
                loadingToggles = true;
                startWithWin.IsOn = appSettings.StartWithWindows;
                loadingToggles = false;
                desktopStatus.Text = "设置开机自启失败：" + ex.Message;
                AddLog(ex.ToString());
            }
        };

        startMin.Toggled += (_, _) =>
        {
            if (loadingToggles) return;
            appSettings.StartMinimized = startMin.IsOn;
            SaveSettings();
            if (appSettings.StartWithWindows)
            {
                try { AppSettingsStore.ApplyStartWithWindows(true, startMin.IsOn); }
                catch (Exception ex) { desktopStatus.Text = "更新开机启动参数失败：" + ex.Message; }
            }
        };

        closeToTray.Toggled += (_, _) =>
        {
            if (loadingToggles) return;
            appSettings.CloseToTray = closeToTray.IsOn;
            SaveSettings();
        };

        lowBattAlert.Toggled += (_, _) =>
        {
            if (loadingToggles) return;
            appSettings.LowBatteryAlertEnabled = lowBattAlert.IsOn;
            SaveSettings();
        };

        osdToggle.Toggled += (_, _) =>
        {
            if (loadingToggles) return;
            appSettings.OsdEnabled = osdToggle.IsOn;
            SaveSettings();
            SyncMouseWatcher();
        };

        loadingToggles = false;

        body.Children.Add(Card("桌面驻留与通知",
            startWithWin,
            startMin,
            closeToTray,
            lowBattAlert,
            Row(lowBattThreshold, Button("保存电量阈值", () =>
            {
                appSettings.LowBatteryThreshold = Math.Clamp(Integer(lowBattThreshold), 5, 50);
                controller.ResetLowBatteryAlerts();
                SaveSettings();
                desktopStatus.Text = $"低电量提醒阈值已设为 {appSettings.LowBatteryThreshold}%";
                return Task.CompletedTask;
            })),
            osdToggle,
            desktopStatus));

        body.Children.Add(Card("连接的接口",
            Text(string.Join("\n", Hardware.Endpoints().Select(x => x.Label))),
            Button("重新扫描", () => { deviceSignature = ""; if (!Scan()) Render(); return Task.CompletedTask; })));

        body.Children.Add(Card("固件",
            Text("读取版本信息不会升级或修改固件。固件更新请使用对应型号官方工具。"),
            ActionButton("读取当前固件版本", async () =>
            {
                int version = IsMouse ? await WithMouse(d => d.Firmware()) : await WithKeyboard(d => d.Firmware());
                AddLog("固件版本：" + version.ToString("X6"));
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "当前固件版本", Content = version.ToString("X6"), CloseButtonText = "关闭", RequestedTheme = Root.RequestedTheme };
                await dialog.ShowAsync();
            })));

        body.Children.Add(Card("通信诊断",
            new TextBox { Text = log, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 320 },
            Row(
                ActionButton("保存诊断记录", async () =>
                {
                    Directory.CreateDirectory(userFolder);
                    await File.WriteAllTextAsync(Path.Combine(userFolder, "diagnostics.txt"), log);
                }),
                ActionButton("导出脱敏诊断包", async () =>
                {
                    string? path = await PickSave("DareuLocal-diagnostics", ".md", "Markdown 诊断报告");
                    if (path == null) return;
                    string report = DiagnosticsExporter.BuildReport(versionStr, Hardware.Endpoints().Select(x => x.Label), log);
                    await File.WriteAllTextAsync(path, report);
                }, true)),
            Text("导出脱敏诊断包会自动抹去用户名、HID 实例路径和序列号，可直接用于提交 GitHub Issue。")));
    }

    async Task CheckForUpdatesAsync(string currentVersion, TextBlock statusLabel)
    {
        statusLabel.Text = "正在查询 GitHub Releases…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DareuLocal/" + currentVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync("https://api.github.com/repos/shum-elli/dareu-local/releases/latest");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "https://github.com/shum-elli/dareu-local/releases" : "https://github.com/shum-elli/dareu-local/releases";
            string notes = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            string cleanTag = tag.TrimStart('v', 'V');
            bool hasUpdate = Version.TryParse(cleanTag, out var latestVer) &&
                Version.TryParse(currentVersion, out var curVer) &&
                latestVer > curVer;

            statusLabel.Text = hasUpdate ? $"发现新版本：{tag}（当前 {currentVersion}）" : $"当前已是最新版本（{currentVersion}）";
            string summary = hasUpdate
                ? $"最新版本：{tag}\n当前版本：{currentVersion}\n\n{(notes.Length > 600 ? notes[..600] + "…" : notes)}"
                : $"当前版本 {currentVersion} 已是最新（最新发布：{tag}）。";

            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = hasUpdate ? "发现新版本" : "检查更新",
                Content = summary,
                PrimaryButtonText = hasUpdate ? "前往下载页" : "打开 Releases",
                CloseButtonText = "关闭",
                RequestedTheme = Root.RequestedTheme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "检查更新失败：" + ex.Message;
            AddLog("检查更新失败：" + ex.Message);
        }
    }
}
