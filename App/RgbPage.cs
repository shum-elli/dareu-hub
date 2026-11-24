using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DareuLocal.Core;
using Windows.UI;
using System.Text.Json;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    int musicMode; double musicGain = 12, musicBrightness = 70; Color musicColor = Color.FromArgb(255, 255, 119, 51);
    AudioLightingSession? audioSession; DispatcherTimer? meterTimer;

    LightingState? liveRgbOriginal;
    DeviceEndpoint? liveRgbEndpoint;
    bool liveRgbActive;
    CancellationTokenSource? liveRgbFlushCts;
    Task? liveRgbCleanupTask;

    bool CanMatrix => DeviceFeatures.For(endpoint?.Product, endpoint?.CanConfigure == true).PerKeyRgb;

    Task CleanupLiveRgbSync()
    {
        liveRgbFlushCts?.Cancel();
        liveRgbFlushCts?.Dispose();
        liveRgbFlushCts = null;

        if (!liveRgbActive)
            return liveRgbCleanupTask ?? Task.CompletedTask;

        liveRgbActive = false;
        var orig = liveRgbOriginal;
        var targetEndpoint = liveRgbEndpoint;
        liveRgbOriginal = null;
        liveRgbEndpoint = null;

        if (orig != null && targetEndpoint != null)
        {
            var task = RestoreRgbAsync(targetEndpoint, orig);
            liveRgbCleanupTask = task;
            return task;
        }
        return Task.CompletedTask;
    }

    async Task RestoreRgbAsync(DeviceEndpoint targetEndpoint, LightingState orig)
    {
        try
        {
            await controller.Queue.EnqueueAsync(token => Task.Run(() =>
            {
                using var d = new TgKeyboard(targetEndpoint);
                d.ReadState();
                d.SetLight(orig);
            }, token)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddLog("退出逐键 RGB 实时同步恢复失败：" + ex.Message);
        }
    }

    void QueueLiveRgbFrame(RgbMatrix frame)
    {
        if (!liveRgbActive || liveRgbEndpoint == null)
            return;

        liveRgbFlushCts?.Cancel();
        liveRgbFlushCts?.Dispose();
        var cts = new CancellationTokenSource();
        liveRgbFlushCts = cts;
        var targetEndpoint = liveRgbEndpoint;
        var snapshot = new RgbMatrix(frame.Rows, frame.Columns, frame.Colors.ToArray());

        _ = FlushLiveRgbFrameAsync(targetEndpoint, snapshot, cts.Token);
    }

    async Task FlushLiveRgbFrameAsync(DeviceEndpoint targetEndpoint, RgbMatrix snapshot, CancellationToken ct)
    {
        try
        {
            await Task.Delay(60, ct);
            if (ct.IsCancellationRequested || !liveRgbActive)
                return;
            await controller.Queue.EnqueueAsync(token => Task.Run(() =>
            {
                using var d = new TgKeyboard(targetEndpoint);
                d.ReadState();
                d.SendFrame(snapshot);
            }, token), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddLog("实时预览发送失败：" + ex.Message);
        }
    }

    async Task StopAudio()
    {
        var session = audioSession;
        audioSession = null;
        meterTimer?.Stop();
        meterTimer = null;
        if (session != null)
        {
            await session.Stop().ConfigureAwait(false);
            if (session.Error != null)
                throw new IOException(session.Error);
        }
    }

    void CustomRgbPage(StackPanel body)
    {
        if (!CanMatrix)
        {
            body.Children.Add(Card("逐键 RGB", Text("选择具有灯光矩阵并已适配基础协议的键盘后，可编辑逐键颜色。")));
            return;
        }
        string file = Path.Combine(AppContext.BaseDirectory, "Profiles", $"{endpoint!.Product!.Pid:x4}.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var map = doc.RootElement.GetProperty("Lighting").GetProperty("LedMatrix").EnumerateArray().Select(row => row.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray();
        int rows = map.Length, cols = map[0].Length;
        var frame = new RgbMatrix(rows, cols, new byte[rows * cols * 3]);
        var selected = new HashSet<int>();
        var buttons = new Dictionary<int, ToggleButton>();
        var keyPositions = new Dictionary<int, List<(int Row, int Col)>>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int kid = map[r][c];
                if (!keyPositions.TryGetValue(kid, out var list))
                {
                    list = new List<(int, int)>();
                    keyPositions[kid] = list;
                }
                list.Add((r, c));
            }

        var picker = new ColorPicker { IsAlphaEnabled = false, MaxWidth = 420, Color = Color.FromArgb(255, 255, 119, 51) };
        var status = Text("选择工具后可在键盘图上点击或按住拖动涂色；也可框选分区批量着色。");
        var toolMode = new RadioButtons
        {
            Header = "绘制工具",
            MaxColumns = 4,
            ItemsSource = new[] { "画笔", "橡皮擦", "吸管", "多选" },
            SelectedIndex = 0
        };
        var liveSync = new ToggleSwitch
        {
            Header = "实时同步预览",
            IsOn = false,
            OnContent = "涂色时实时点亮键盘",
            OffContent = "仅在画布上编辑"
        };

        var canvas = new Canvas { Width = Math.Max(640, keyLayout.Max(x => x.X + x.Width) - keyLayout.Min(x => x.X)), Height = Math.Max(280, keyLayout.Max(x => x.Y + x.Height) - keyLayout.Min(x => x.Y)) };
        int minX = keyLayout.Min(x => x.X), minY = keyLayout.Min(x => x.Y);
        bool isPainting = false;

        void UpdateColors()
        {
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    int id = map[row][col];
                    if (!buttons.TryGetValue(id, out var b))
                        continue;
                    int i = (row * cols + col) * 3;
                    var color = Color.FromArgb(255, frame.Colors[i], frame.Colors[i + 1], frame.Colors[i + 2]);
                    b.Background = new SolidColorBrush(color);
                    b.Foreground = new SolidColorBrush(color.R + color.G + color.B > 390 ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 235, 235, 235));
                }
        }

        void PaintKeyColor(int id, byte r, byte g, byte bVal)
        {
            if (!keyPositions.TryGetValue(id, out var coords))
                return;
            foreach (var (rPos, cPos) in coords)
            {
                int i = (rPos * cols + cPos) * 3;
                frame.Colors[i] = r;
                frame.Colors[i + 1] = g;
                frame.Colors[i + 2] = bVal;
            }
            if (buttons.TryGetValue(id, out var btn))
            {
                var color = Color.FromArgb(255, r, g, bVal);
                btn.Background = new SolidColorBrush(color);
                btn.Foreground = new SolidColorBrush(color.R + color.G + color.B > 390 ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 235, 235, 235));
            }
            QueueLiveRgbFrame(frame);
        }

        void ApplyToolToKey(int id)
        {
            int tool = toolMode.SelectedIndex;
            if (tool == 0)
            {
                PaintKeyColor(id, picker.Color.R, picker.Color.G, picker.Color.B);
                status.Text = $"已涂色按键 #{id}（#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}）";
            }
            else if (tool == 1)
            {
                PaintKeyColor(id, 0, 0, 0);
                status.Text = $"已擦除按键 #{id} 颜色";
            }
            else if (tool == 2 && keyPositions.TryGetValue(id, out var coords) && coords.Count > 0)
            {
                var (rPos, cPos) = coords[0];
                int i = (rPos * cols + cPos) * 3;
                picker.Color = Color.FromArgb(255, frame.Colors[i], frame.Colors[i + 1], frame.Colors[i + 2]);
                status.Text = $"已拾取颜色 #{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
            }
        }

        foreach (var key in keyLayout)
        {
            if (!keyPositions.ContainsKey(key.Id))
                continue;
            var b = new ToggleButton { Content = key.Label, Width = Math.Max(24, key.Width - 3), Height = Math.Max(24, key.Height - 3), Padding = new Thickness(1), FontSize = 10, BorderThickness = new Thickness(1), BorderBrush = Muted };
            int id = key.Id;
            b.Click += (s, e) =>
            {
                if (toolMode.SelectedIndex == 3)
                {
                    if (b.IsChecked == true) selected.Add(id); else selected.Remove(id);
                    status.Text = $"已选择 {selected.Count} 个按键";
                }
                else
                {
                    b.IsChecked = selected.Contains(id);
                }
            };
            b.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (toolMode.SelectedIndex == 3)
                    return;
                if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed)
                {
                    isPainting = true;
                    ApplyToolToKey(id);
                }
            }), true);
            b.PointerEntered += (_, e) =>
            {
                if (isPainting && toolMode.SelectedIndex != 3 && e.GetCurrentPoint(b).Properties.IsLeftButtonPressed)
                    ApplyToolToKey(id);
            };
            Canvas.SetLeft(b, key.X - minX);
            Canvas.SetTop(b, key.Y - minY);
            canvas.Children.Add(b);
            buttons[id] = b;
        }

        canvas.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => isPainting = false), true);
        canvas.PointerExited += (_, _) => isPainting = false;
        UpdateColors();

        bool togglingLive = false;
        liveSync.Toggled += async (_, _) =>
        {
            if (togglingLive) return;
            togglingLive = true;
            try
            {
                if (liveSync.IsOn)
                {
                    var targetEp = endpoint!;
                    var orig = await WithKeyboard(d =>
                    {
                        var l = d.ReadLights().First(x => x.Region == 1);
                        d.SetFrameEffect(18);
                        d.SendFrame(frame);
                        return l;
                    });
                    liveRgbOriginal = orig;
                    liveRgbEndpoint = targetEp;
                    liveRgbActive = true;
                    leaveCurrentPageCleanup = () => _ = CleanupLiveRgbSync();
                    status.Text = "实时同步已开启：画布修改会立即发送到键盘。";
                }
                else
                {
                    await CleanupLiveRgbSync();
                    status.Text = "已关闭实时同步，原灯效已恢复。";
                }
            }
            catch (Exception ex)
            {
                liveSync.IsOn = false;
                liveRgbActive = false;
                status.Text = "实时同步开启失败：" + ex.Message;
                AddLog(ex.ToString());
            }
            finally
            {
                togglingLive = false;
            }
        };

        void SelectZone(Func<KeyChoice, bool> predicate, string zoneName)
        {
            selected.Clear();
            foreach (var k in keyLayout)
            {
                if (!buttons.TryGetValue(k.Id, out var btn)) continue;
                bool match = predicate(k);
                if (match) selected.Add(k.Id);
                btn.IsChecked = match;
            }
            status.Text = $"已选中 {zoneName}（{selected.Count} 个按键）";
        }

        var zoneRow = Row(
            Button("WASD", () => { SelectZone(k => k.Default.Function == 6 && k.Default.Data[1] is 26 or 4 or 22 or 7, "WASD"); return Task.CompletedTask; }),
            Button("方向键", () => { SelectZone(k => k.Default.Function == 6 && k.Default.Data[1] is 79 or 80 or 81 or 82, "方向键"); return Task.CompletedTask; }),
            Button("F 区", () => { SelectZone(k => k.Default.Function == 6 && k.Default.Data[1] is >= 58 and <= 69, "F 区"); return Task.CompletedTask; }),
            Button("数字区", () => { SelectZone(k => k.Default.Function == 6 && k.Default.Data[1] is >= 30 and <= 39, "数字区"); return Task.CompletedTask; }),
            Button("全选", () => { SelectZone(_ => true, "全部按键"); return Task.CompletedTask; }),
            Button("取消选择", () => { selected.Clear(); foreach (var b in buttons.Values) b.IsChecked = false; status.Text = "已取消选择"; return Task.CompletedTask; }),
            Button("清空画布颜色", () => { Array.Clear(frame.Colors); UpdateColors(); QueueLiveRgbFrame(frame); status.Text = "已清空全部按键颜色"; return Task.CompletedTask; }));

        var presetButtons = RgbPresets.All.Select(preset =>
        {
            var btn = Button(preset.Name, () =>
            {
                var generated = RgbPresets.Generate(rows, cols, preset.Id);
                Array.Copy(generated.Colors, frame.Colors, frame.Colors.Length);
                UpdateColors();
                QueueLiveRgbFrame(frame);
                status.Text = $"已应用主题预设：{preset.Name}";
                return Task.CompletedTask;
            });
            btn.Padding = new Thickness(12, 6, 12, 6);
            return (UIElement)btn;
        }).ToArray();
        var presetRow = Row(new UIElement[] { Text("一键主题预设：", 12) }.Concat(presetButtons).ToArray());
        presetRow.VerticalAlignment = VerticalAlignment.Center;

        var slot = Choices("自定义灯光槽位", Enumerable.Range(1, 5).Select(i => new Choice(i, $"自定义 {i}")), 1);
        body.Children.Add(Card("按键颜色",
            toolMode,
            liveSync,
            presetRow,
            new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 330 },
            status,
            zoneRow,
            new Expander { Header = "绘制颜色", Content = picker, HorizontalAlignment = HorizontalAlignment.Stretch },
            Button("将颜色应用到选中的按键", () =>
            {
                if (selected.Count == 0) throw new ArgumentException("请先使用多选或快捷分区选择按键");
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < cols; col++)
                        if (selected.Contains(map[row][col]))
                        {
                            int i = (row * cols + col) * 3;
                            frame.Colors[i] = picker.Color.R;
                            frame.Colors[i + 1] = picker.Color.G;
                            frame.Colors[i + 2] = picker.Color.B;
                        }
                UpdateColors();
                QueueLiveRgbFrame(frame);
                return Task.CompletedTask;
            }),
            slot));

        body.Children.Add(Card("设备与本地保存",
            Text("某些固件不支持读取已保存颜色。此时仍可按官方协议发送颜色，应用会核对槽位并保留本地方案。"),
            Button("临时预览 3 秒", () => Run(async () =>
            {
                bool wasLive = liveRgbActive;
                if (wasLive) await CleanupLiveRgbSync();
                await KeyWrite(d =>
                {
                    var original = d.ReadLights().First(x => x.Region == 1);
                    try { d.SetFrameEffect(18); d.SendFrame(frame); Thread.Sleep(3000); }
                    finally { d.SetLight(original); }
                });
                liveSync.IsOn = false;
            }, "预览结束，已恢复原灯效")),
            ActionButton("保存到设备自定义槽位", async () =>
            {
                if (liveRgbActive) { await CleanupLiveRgbSync(); liveSync.IsOn = false; }
                frame.Validate();
                int id = Id(slot);
                bool verified = await WithKeyboard(d => d.SaveCustomMatrix(id, frame));
                Directory.CreateDirectory(Path.Combine(userFolder, "Lighting"));
                await File.WriteAllTextAsync(Path.Combine(userFolder, "Lighting", $"{endpoint!.Product!.Pid:X4}-{id}.json"), JsonSerializer.Serialize(frame, Json));
                status.Text = verified ? "颜色写入并读回一致。" : "槽位已核对；当前固件不支持颜色读回，颜色方案已保存在本地。";
            }, true),
            ActionButton("读取此槽位的颜色", async () =>
            {
                if (liveRgbActive) { await CleanupLiveRgbSync(); liveSync.IsOn = false; }
                int id = Id(slot);
                frame = await WithKeyboard(d =>
                {
                    var original = d.ReadLights().First(x => x.Region == 1);
                    try
                    {
                        d.SetFrameEffect(12 + id);
                        var value = d.ReadCustomMatrix();
                        if (value.Rows != rows || value.Columns != cols) throw new IOException("灯光矩阵与布局不匹配");
                        return value;
                    }
                    finally { d.SetLight(original); }
                });
                UpdateColors();
            }),
            ActionButton("导出颜色方案", async () =>
            {
                string? path = await PickSave(endpoint!.Name + "-RGB", ".json");
                if (path != null) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(frame, Json));
            }),
            ActionButton("导入颜色方案", async () =>
            {
                string? path = await PickOpen();
                if (path == null) return;
                var value = JsonSerializer.Deserialize<RgbMatrix>(await File.ReadAllTextAsync(path), Json) ?? throw new ArgumentException("颜色方案无效");
                value.Validate();
                if (value.Rows != rows || value.Columns != cols) throw new ArgumentException("颜色方案的矩阵尺寸与当前键盘不同");
                frame = value;
                UpdateColors();
                QueueLiveRgbFrame(frame);
            })));
    }

    void MusicPage(StackPanel body)
    {
        if (!CanMatrix)
        {
            body.Children.Add(Card("音乐律动", Text("此功能需要已适配基础协议且具有 RGB 矩阵的键盘。")));
            return;
        }
        var mode = Choices("律动效果", new[]
        {
            new Choice(0, "整板亮度律动"),
            new Choice(1, "音量条"),
            new Choice(2, "频谱柱状图"),
            new Choice(3, "彩虹频谱"),
            new Choice(4, "中央对称律动")
        }, musicMode);
        var color = new ColorPicker { IsAlphaEnabled = false, Color = musicColor, MaxWidth = 360 };
        var gain = new Slider { Header = "灵敏度", Minimum = 1, Maximum = 60, Value = musicGain, StepFrequency = 1,
            SnapsTo = SliderSnapsTo.StepValues };
        var brightness = Slider("最大亮度（%）", (int)musicBrightness, 100);
        var meter = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
        var state = Text(audioSession == null ? "未启动" : "正在运行");
        Button start = null!, stop = null!;
        void Apply()
        {
            musicMode = Id(mode); musicGain = gain.Value; musicBrightness = brightness.Value; musicColor = color.Color;
            gain.Header = $"灵敏度 · {gain.Value:0}×";
            brightness.Header = $"最大亮度 · {brightness.Value:0}%";
            audioSession?.UpdateParameters(musicMode, new[] { musicColor.R, musicColor.G, musicColor.B }, musicGain, musicBrightness / 100);
        }
        void RunningState()
        {
            bool running = audioSession != null;
            start.IsEnabled = !running; stop.IsEnabled = running;
        }
        start = Button("开始律动", () => Run(async () =>
        {
            if (audioSession != null) return;
            Apply();
            audioSession = await AudioLightingSession.Start(endpoint!, musicMode, new[] { musicColor.R, musicColor.G, musicColor.B }, musicGain, musicBrightness / 100);
            state.Text = "正在运行 · 拖动滑块或更改颜色可实时调节"; RunningState();
            meterTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
            meterTimer.Tick += (_, _) =>
            {
                meter.Value = (audioSession?.Level ?? 0) * 100;
                if (audioSession?.Error is string error) state.Text = "律动中断：" + error;
                else if (audioSession == null) { state.Text = "已停止"; RunningState(); }
            };
            meterTimer.Start();
        }, "音乐律动已启动"), true);
        stop = Button("停止并恢复灯效", () => Run(async () =>
        {
            try { await StopAudio(); state.Text = "已停止，原灯效已恢复"; }
            finally { meter.Value = 0; RunningState(); }
        }, "已停止律动"));
        mode.SelectionChanged += (_, _) => Apply();
        gain.ValueChanged += (_, _) => Apply();
        brightness.ValueChanged += (_, _) => Apply();
        color.ColorChanged += (_, _) => Apply();
        Apply(); RunningState();
        var controls = Column(14);
        foreach (var element in new UIElement[] { mode, gain, Text("低灵敏度适合较响的声音；高灵敏度适合较轻的声音。有线模式支持更高刷新率频谱。", 12), brightness, state, meter, Row(start, stop) }) controls.Children.Add(element);
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 16 };
        grid.Children.Add(controls); grid.Children.Add(color);
        void Arrange(double width)
        {
            bool wide = width >= 720;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Add(new()); if (wide) grid.ColumnDefinitions.Add(new());
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); if (!wide) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(color, wide ? 1 : 0); Grid.SetRow(color, wide ? 0 : 1);
        }
        Arrange(800); grid.SizeChanged += (_, e) => { if ((e.NewSize.Width >= 720) != (grid.ColumnDefinitions.Count == 2)) Arrange(e.NewSize.Width); };
        body.Children.Add(Card("跟随系统播放声音", Text("运行中可以实时调节效果、灵敏度、亮度和颜色。离开页面或停止时恢复原灯效。只分析系统播放声音，不使用麦克风、不保存音频。"), grid));
    }
}
