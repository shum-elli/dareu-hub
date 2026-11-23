using DareuLocal.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    UIElement MouseMappingDiagram(Action<int, string> selected, Action<Action<int, string, bool>>? registerBadgeUpdater = null)
    {
        var canvas = new Canvas { Width = 800, Height = 350 };
        var picture = new Image { Source = ProductImage(endpoint!.Product), Width = 220, Height = 325, Stretch = Stretch.Uniform };
        Canvas.SetLeft(picture, 290);
        Canvas.SetTop(picture, 10);
        canvas.Children.Add(picture);
        var buttons = new Dictionary<int, Button>();
        var badges = new Dictionary<int, TextBlock>();
        var dots = new Dictionary<int, Ellipse>();
        var paths = new Dictionary<int, Polyline>();
        string[] labels = { "左键单击", "右键单击", "滚轮单击", "后退侧键", "前进侧键", "DPI 按键" };
        // Physical locations on the top-view image; IDs match the existing JM button protocol.
        (double X, double Y, bool Left, double LabelY)[] anchors =
        { (363, 73, true, 32), (440, 73, false, 32), (401, 97, false, 113),
          (329, 184, true, 205), (328, 148, true, 123), (401, 142, false, 213) };
        void Select(int id, bool notify = true)
        {
            foreach (var (key, button) in buttons)
            {
                button.Background = key == id ? ThemePalette.SelectedKey : ThemePalette.Recessed;
                dots[key].Fill = key == id ? ThemePalette.AccentText : ThemePalette.Secondary;
                paths[key].Stroke = key == id ? ThemePalette.AccentText : ThemePalette.Border;
            }
            if (notify) selected(id, labels[id - 1]);
        }
        int count = Math.Clamp(endpoint.Product?.ButtonCount ?? 6, 1, 6);
        for (int i = 0; i < count; i++)
        {
            int id = i + 1;
            var a = anchors[i];
            double labelX = a.Left ? 20 : 580;
            double endX = a.Left ? labelX + 180 : labelX;
            var line = new Polyline { Stroke = ThemePalette.Border, StrokeThickness = 1.5, IsHitTestVisible = false,
                Points = new PointCollection { new Point(a.X, a.Y), new Point(a.Left ? 268 : 540, a.LabelY + 26), new Point(endX, a.LabelY + 26) } };
            paths[id] = line;
            canvas.Children.Add(line);
            var dot = new Ellipse { Width = 9, Height = 9, Fill = ThemePalette.Secondary, IsHitTestVisible = false };
            Canvas.SetLeft(dot, a.X - 4.5); Canvas.SetTop(dot, a.Y - 4.5);
            dots[id] = dot; canvas.Children.Add(dot);

            var titleLine = new TextBlock { Text = $"{id}   {labels[i]}", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var badgeLine = new TextBlock { Text = "读取中…", FontSize = 11, Foreground = ThemePalette.Secondary, TextTrimming = TextTrimming.CharacterEllipsis };
            var inner = new StackPanel { Spacing = 2 };
            inner.Children.Add(titleLine);
            inner.Children.Add(badgeLine);

            var button = Button("", () => { Select(id); return Task.CompletedTask; });
            button.Content = inner;
            button.Width = 180; button.Height = 52; button.Padding = new Thickness(12, 6, 12, 6);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = ThemePalette.Recessed;
            button.BorderThickness = new Thickness(1);
            button.BorderBrush = ThemePalette.Border;
            AutomationProperties.SetName(button, $"选择鼠标按键 {id}：{labels[i]}");
            Canvas.SetLeft(button, labelX); Canvas.SetTop(button, a.LabelY);
            buttons[id] = button; badges[id] = badgeLine; canvas.Children.Add(button);
            var hit = new Button { Width = 24, Height = 24, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            AutomationProperties.SetName(hit, $"鼠标示意图：{labels[i]}");
            ToolTipService.SetToolTip(hit, labels[i]);
            hit.Click += (_, _) => Select(id);
            Canvas.SetLeft(hit, a.X - 12); Canvas.SetTop(hit, a.Y - 12);
            canvas.Children.Add(hit);
        }
        registerBadgeUpdater?.Invoke((id, text, modified) =>
        {
            if (badges.TryGetValue(id, out var badge))
            {
                badge.Text = text;
                badge.Foreground = modified ? ThemePalette.AccentText : ThemePalette.Secondary;
            }
            if (buttons.TryGetValue(id, out var btn))
            {
                btn.BorderBrush = modified ? ThemePalette.AccentText : ThemePalette.Border;
                btn.BorderThickness = new Thickness(modified ? 1.5 : 1);
            }
        });
        Select(1, false);
        return Card("选择鼠标按键", new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 350, HorizontalAlignment = HorizontalAlignment.Stretch },
            Text("点击引线标签或鼠标上的圆点选择按键，再在下方设置功能。"));
    }

    void MouseDpiEditor(StackPanel body)
    {
        var owner = endpoint!;
        var entry = mouse!;
        var observed = entry;
        int[] values = entry.Dpi.ToArray();
        int selected = Math.Clamp(entry.Stage - 1, 0, values.Length - 1);
        int min = Math.Max(50, owner.Product?.DpiMin ?? 100), max = Math.Min(30000, owner.Product?.DpiMax ?? 30000);
        bool syncing = false, activePage = false, dpiPollBusy = false;
        (int Stage, int Value, long Revision)? pending = null;
        long revision = 0;
        DateTime due = DateTime.MinValue;
        bool Here() => activePage && page == "performance" && ReferenceEquals(endpoint, owner);
        var actual = Text("", 28); actual.Foreground = ThemePalette.Primary;
        var status = Text("按鼠标 DPI 键或拖动滑块都会实时同步。", 12);
        var live = new ToggleSwitch { Header = "实时试用", IsOn = true, OnContent = "调节后直接生效", OffContent = "仅编辑，点击保存后生效" };
        var number = Number("精确 DPI（50 的倍数）", values[selected], min, max, 50);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(values[selected], min, max), StepFrequency = 50,
            SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues, Header = "调节所选档位" };
        AutomationProperties.SetName(slider, "实时调节所选档位 DPI");
        var cards = new Grid { ColumnSpacing = 10 };
        var buttons = new List<Button>(); var titles = new List<TextBlock>(); var details = new List<TextBlock>();
        void RefreshCards()
        {
            actual.Text = $"{observed.Dpi[observed.Stage - 1]} DPI · 档位 {observed.Stage}";
            for (int i = 0; i < values.Length; i++)
            {
                buttons[i].Background = i == selected ? ThemePalette.SelectedKey : ThemePalette.Recessed;
                titles[i].Text = $"档位 {i + 1}" + (i + 1 == observed.Stage ? " · 使用中" : "");
                details[i].Text = values[i].ToString() + (values[i] != observed.Dpi[i] ? " *" : "");
                AutomationProperties.SetName(buttons[i], $"档位 {i + 1}，{values[i]} DPI" + (i + 1 == observed.Stage ? "，使用中" : ""));
            }
        }
        void SyncInputs()
        {
            syncing = true; number.Value = values[selected]; slider.Value = Math.Clamp(values[selected], min, max); syncing = false;
        }
        void Queue()
        {
            if (!live.IsOn || syncing) return;
            if (!double.IsFinite(number.Value) || number.Value != Math.Truncate(number.Value) || number.Value < min || number.Value > max)
            { status.Text = "请输入范围内的整数 DPI；尚未写入。"; return; }
            int value = (int)number.Value;
            try { if (value < min || value > max) throw new ArgumentException(); JmMouse.EncodeDpi(value); }
            catch (ArgumentException) { status.Text = "请输入范围内、50 的倍数的 DPI；尚未写入。"; return; }
            pending = (selected + 1, value, ++revision);
            due = DateTime.UtcNow.AddMilliseconds(300);
            status.Text = $"准备试用档位 {selected + 1} · {value} DPI…";
        }
        void Select(int index)
        {
            selected = index; SyncInputs(); RefreshCards(); Queue();
        }
        Color[] colors = { Color.FromArgb(255, 239, 83, 80), Color.FromArgb(255, 66, 133, 244), Color.FromArgb(255, 41, 183, 101),
            Color.FromArgb(255, 235, 184, 35), Color.FromArgb(255, 192, 84, 220), Color.FromArgb(255, 31, 184, 197), Color.FromArgb(255, 241, 131, 61) };
        for (int i = 0; i < values.Length; i++)
        {
            int index = i; cards.ColumnDefinitions.Add(new());
            var title = new TextBlock { FontSize = 13, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var value = new TextBlock { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
            titles.Add(title); details.Add(value);
            var inner = Column(12);
            inner.Children.Add(new Border { Height = 5, Background = new SolidColorBrush(colors[i % colors.Length]), CornerRadius = new CornerRadius(2) });
            inner.Children.Add(title); inner.Children.Add(value);
            var button = Button("", () => { Select(index); return Task.CompletedTask; });
            button.Content = inner; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(10, 12, 10, 18); button.CornerRadius = new CornerRadius(10);
            buttons.Add(button); Grid.SetColumn(button, i); cards.Children.Add(button);
        }
        void Observe(MouseState next, bool applied)
        {
            if (!Here()) return;
            if (next.Profile != observed.Profile || next.Dpi.Length != values.Length)
            {
                pending = null;
                controller.UpdateObservedMouseState(next, owner.Path);
                Render();
                return;
            }
            bool stageChanged = next.Stage != observed.Stage;
            bool follow = stageChanged && pending == null && live.IsOn;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == observed.Dpi[i]) values[i] = next.Dpi[i];
            observed = next;
            controller.UpdateObservedMouseState(next, owner.Path);
            if (follow) selected = next.Stage - 1;
            if (pending == null && number.FocusState == FocusState.Unfocused) SyncInputs();
            RefreshCards();
            status.Text = (applied ? "已生效并读回确认" : "已同步设备") + $" · {appSettings.GetProfileDisplayName(owner.FamilyKey, next.Profile)} · {DateTime.Now:HH:mm:ss}";
        }
        async Task Exchange()
        {
            var request = pending;
            try
            {
                var result = await WithMouse(d =>
                {
                    var before = d.ReadState();
                    if (request is not { } r || before.Profile != entry.Profile || before.Dpi.Length != values.Length)
                        return (State: before, Applied: false);
                    if (before.Dpi[r.Stage - 1] != r.Value) d.SetDpi(r.Stage, r.Value);
                    if (before.Stage != r.Stage) d.SetStage(r.Stage);
                    var after = d.ReadState();
                    if (after.Dpi[r.Stage - 1] != r.Value || after.Stage != r.Stage) throw new IOException("DPI 读回不一致");
                    return (State: after, Applied: true);
                });
                if (!Here()) return;
                if (request?.Revision == pending?.Revision) pending = null;
                Observe(result.State, result.Applied);
            }
            catch (Exception ex)
            {
                if (!Here()) return;
                pending = null;
                if (request != null) live.IsOn = false;
                actual.Text = $"上次确认 {observed.Dpi[observed.Stage - 1]} DPI · 当前未同步";
                status.Text = "同步失败：" + ex.Message + "；可唤醒设备后重试。";
                AddLog(ex.Message);
            }
        }
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        clock.Tick += async (_, _) =>
        {
            if (!Here() || !AppWindow.IsVisible || busy || dpiPollBusy) return;
            if (pending == null || DateTime.UtcNow < due) return;
            dpiPollBusy = true;
            try { await Exchange(); }
            finally { dpiPollBusy = false; }
        };
        number.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            // An unfinished or invalid numeric edit must cancel an earlier queued write.
            pending = null;
            if (!double.IsFinite(number.Value) || number.Value != Math.Truncate(number.Value) || number.Value < min || number.Value > max) { status.Text = "请输入有效整数 DPI；尚未写入。"; return; }
            values[selected] = (int)number.Value;
            syncing = true; slider.Value = Math.Clamp(number.Value, min, max); syncing = false;
            RefreshCards(); Queue();
        };
        slider.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            values[selected] = Math.Clamp((int)Math.Round(slider.Value / 50) * 50, min, max);
            SyncInputs(); RefreshCards(); Queue();
        };
        live.Toggled += (_, _) => { pending = null; if (live.IsOn) Queue(); else status.Text = "实时试用已关闭，尚未保存的值用 * 标记。"; };
        var range = new Grid(); range.ColumnDefinitions.Add(new()); range.ColumnDefinitions.Add(new());
        var high = Text($"{max} DPI"); high.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(high, 1);
        range.Children.Add(Text($"{min} DPI")); range.Children.Add(high);

        var presetButtons = new[] { 400, 800, 1600, 3200 }
            .Where(v => v >= min && v <= max)
            .Select(v =>
            {
                var btn = Button($"{v}", () =>
                {
                    values[selected] = v;
                    SyncInputs();
                    RefreshCards();
                    Queue();
                    return Task.CompletedTask;
                });
                btn.Padding = new Thickness(12, 6, 12, 6);
                return (UIElement)btn;
            }).ToArray();
        var presetsRow = Row(new UIElement[] { Text("快捷预设：", 12) }.Concat(presetButtons).ToArray());
        presetsRow.VerticalAlignment = VerticalAlignment.Center;

        var count = Choices("启用档位数量", Enumerable.Range(1, Math.Clamp(owner.Product?.StageCount ?? 5, 1, 7)).Select(i => new Choice(i, $"{i} 档")), values.Length);
        var panel = Card("DPI 灵敏度", actual, status, live, cards, slider, range, presetsRow, number,
            Text("实时试用会直接修改设备档位；关闭后可先编辑再保存。使用鼠标 DPI 键切档，页面会自动同步。", 12),
            ActionButton("保存全部 DPI 并启用所选档位", async () =>
            {
                pending = null; Integer(number); int[] snapshot = values.ToArray(); int active = selected + 1;
                foreach (int v in snapshot) { if (v < min || v > max) throw new ArgumentException($"DPI 范围为 {min}–{max}"); JmMouse.EncodeDpi(v); }
                var after = await WithMouse(d =>
                {
                    var before = d.ReadState();
                    if (before.Profile != entry.Profile || before.Dpi.Length != snapshot.Length) throw new IOException("设备配置已变化，请刷新后重新编辑。");
                    for (int i = 0; i < snapshot.Length; i++) if (snapshot[i] != before.Dpi[i]) d.SetDpi(i + 1, snapshot[i]);
                    d.SetStage(active); var next = d.ReadState();
                    if (next.Stage != active || !next.Dpi.SequenceEqual(snapshot)) throw new IOException("DPI 读回不一致");
                    return next;
                });
                Observe(after, true);
            }, true),
            ActionButton("恢复进入页面时的 DPI", async () =>
            {
                pending = null;
                var after = await WithMouse(d =>
                {
                    var before = d.ReadState();
                    if (before.Profile != entry.Profile) throw new IOException("当前板载配置已改变，请重新进入此页。");
                    if (before.Dpi.Length != entry.Dpi.Length) d.SetStageCount(entry.Dpi.Length);
                    for (int i = 0; i < entry.Dpi.Length; i++) d.SetDpi(i + 1, entry.Dpi[i]);
                    d.SetStage(entry.Stage); var next = d.ReadState();
                    if (next.Stage != entry.Stage || !next.Dpi.SequenceEqual(entry.Dpi)) throw new IOException("恢复 DPI 读回不一致");
                    return next;
                });
                values = after.Dpi.ToArray(); selected = after.Stage - 1; Observe(after, true); SyncInputs();
            }), count,
            ActionButton("更新档位数量", async () =>
            {
                pending = null;
                if (!values.SequenceEqual(observed.Dpi)) throw new ArgumentException("请先保存或恢复 DPI 修改，再调整档位数量。");
                int n = Id(count);
                await MouseWrite(d => { if (d.ReadState().Profile != entry.Profile) throw new IOException("配置已改变，请刷新"); d.SetStageCount(n); if (d.ReadState().Dpi.Length != n) throw new IOException("档位数量读回不一致"); });
                await Connect();
            }));
        var content = (StackPanel)panel.Child;
        var advanced = Column();
        foreach (var element in content.Children.Skip(content.Children.Count - 3).ToArray())
        { content.Children.Remove(element); advanced.Children.Add(element); }
        content.Children.Add(new Expander { Header = "档位数量与恢复", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(panel); RefreshCards();
        panel.Loaded += (_, _) =>
        {
            activePage = true;
            onExternalMouseStateObserved = next => { if (Here()) Observe(next, applied: false); };
            clock.Start();
        };
        panel.Unloaded += (_, _) =>
        {
            activePage = false;
            onExternalMouseStateObserved = null;
            clock.Stop();
            pending = null;
        };
    }
}
