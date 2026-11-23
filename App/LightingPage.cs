using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DareuLocal.Core;
using Windows.UI;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    static readonly string[] EffectNames = { "关闭", "常亮", "呼吸", "霓虹", "按键响应", "波浪", "雨滴", "聚合", "涟漪", "跑马灯", "旋转", "星光", "热力", "自定义 1", "自定义 2", "自定义 3", "自定义 4", "自定义 5", "实时帧", "环绕", "彩虹", "光波", "流光", "启动", "区域响应", "行响应", "瀑布", "扫描", "心跳", "流萤", "心形呼吸", "月光呼吸", "星光呼吸" };
    static readonly string[] PresetHexColors = { "#FF5722", "#FF9800", "#FFEB3B", "#4CAF50", "#00BCD4", "#2196F3", "#9C27B0", "#FFFFFF" };

    void Lights(StackPanel body)
    {
        if (IsMouse)
        {
            body.Children.Add(Card(endpoint?.Name ?? "鼠标", Text(endpoint?.Product?.HasLighting == true ? "此鼠标的独立灯效协议尚未适配。可先使用按键映射、DPI 与性能设置。" : "此型号未提供 RGB 灯光区域。可在按键映射中配置 DPI 切换，在性能页调整 DPI 与回报率。")));
            return;
        }
        if (endpoint?.Product?.HasLighting != true)
        {
            body.Children.Add(Card("灯光", Text("此型号未提供灯光区域。")));
            return;
        }
        var host = Column();
        async Task ReadLights()
        {
            var lights = await WithKeyboard(d => d.ReadLights());
            host.Children.Clear(); foreach (var light in lights) host.Children.Add(LightEditor(light));
        }
        var panel = Card("灯光区域", Text("自动读取当前灯光设置。可分别编辑主背光和侧灯，保存只影响所选区域。"), ActionButton("重新读取灯光", ReadLights), host);
        body.Children.Add(panel);
        ReadPageOnLoad(panel, body, ReadLights, "已读取灯光设置");
    }

    UIElement LightEditor(LightingState state)
    {
        var currentState = state;
        var lightStatus = Text("点击色块可编辑或删除，点击预设颜色可快速添加（最多 17 个颜色）。", 12);
        var effects = Choices("灯效", state.Effects.Select(i => new Choice(i, i < EffectNames.Length ? EffectNames[i] : $"灯效 {i}")), state.Effect);
        var brightness = Slider("亮度（%）", (int)Math.Round(state.Brightness * 100.0 / 255), 100);
        var speed = Choices("速度", new[] { new Choice(0, "默认"), new Choice(1, "慢"), new Choice(2, "中"), new Choice(3, "快") }, state.Speed);
        var reverse = new ToggleSwitch { Header = "反向播放", IsOn = state.Flag != 0, OnContent = "反向", OffContent = "正向" };

        var swatchColors = state.Colors.Select(c => c.ToArray()).ToList();
        int selectedSwatch = swatchColors.Count > 0 ? 0 : -1;
        var swatchRows = Column(8);
        var swatchCountText = Text("", 12);

        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            Color = swatchColors.Count > 0
                ? Color.FromArgb(255, swatchColors[0][0], swatchColors[0][1], swatchColors[0][2])
                : Color.FromArgb(255, 255, 119, 51),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        void RebuildSwatches()
        {
            swatchRows.Children.Clear();
            swatchCountText.Text = swatchColors.Count == 0
                ? "当前调色板为空（部分灯效可留空或点击预设色添加）"
                : $"已选 {swatchColors.Count}/17 个颜色" + (selectedSwatch >= 0 && selectedSwatch < swatchColors.Count
                    ? $" · 当前选中 #{swatchColors[selectedSwatch][0]:X2}{swatchColors[selectedSwatch][1]:X2}{swatchColors[selectedSwatch][2]:X2}"
                    : "");

            if (swatchColors.Count == 0)
                return;

            foreach (var chunk in swatchColors.Select((Color, Index) => (Color, Index)).Chunk(8))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var item in chunk)
                {
                    int idx = item.Index;
                    byte[] c = item.Color;
                    bool isSelected = idx == selectedSwatch;
                    var btn = new Button
                    {
                        Width = 36,
                        Height = 36,
                        Padding = new Thickness(0),
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(Color.FromArgb(255, c[0], c[1], c[2])),
                        BorderThickness = new Thickness(isSelected ? 2.5 : 1),
                        BorderBrush = isSelected ? ThemePalette.AccentText : ThemePalette.Border
                    };
                    ToolTipService.SetToolTip(btn, $"颜色 {idx + 1}：#{c[0]:X2}{c[1]:X2}{c[2]:X2}");
                    btn.Click += (_, _) =>
                    {
                        selectedSwatch = idx;
                        picker.Color = Color.FromArgb(255, c[0], c[1], c[2]);
                        RebuildSwatches();
                    };
                    row.Children.Add(btn);
                }
                swatchRows.Children.Add(row);
            }
        }

        RebuildSwatches();

        var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string hex in PresetHexColors)
        {
            byte[] rgb = ParseColor(hex);
            var presetBtn = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(255, rgb[0], rgb[1], rgb[2])),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemePalette.Border
            };
            ToolTipService.SetToolTip(presetBtn, $"添加预设色 {hex}");
            presetBtn.Click += (_, _) =>
            {
                picker.Color = Color.FromArgb(255, rgb[0], rgb[1], rgb[2]);
                if (swatchColors.Count >= 17)
                {
                    lightStatus.Text = "最多支持 17 个颜色，请先删除不需要的色块。";
                    return;
                }
                swatchColors.Add(rgb.ToArray());
                selectedSwatch = swatchColors.Count - 1;
                RebuildSwatches();
            };
            presetRow.Children.Add(presetBtn);
        }

        var paletteEditor = Column(10);
        paletteEditor.Children.Add(picker);
        paletteEditor.Children.Add(Row(
            Button("+ 添加当前色", () =>
            {
                if (swatchColors.Count >= 17)
                    throw new ArgumentException("最多使用 17 个颜色");
                swatchColors.Add(new[] { picker.Color.R, picker.Color.G, picker.Color.B });
                selectedSwatch = swatchColors.Count - 1;
                RebuildSwatches();
                return Task.CompletedTask;
            }),
            Button("替换选中色", () =>
            {
                if (selectedSwatch < 0 || selectedSwatch >= swatchColors.Count)
                    throw new ArgumentException("请先点击选择一个色块");
                swatchColors[selectedSwatch] = new[] { picker.Color.R, picker.Color.G, picker.Color.B };
                RebuildSwatches();
                return Task.CompletedTask;
            }),
            Button("删除选中色", () =>
            {
                if (selectedSwatch < 0 || selectedSwatch >= swatchColors.Count)
                    throw new ArgumentException("请先点击选择一个色块");
                swatchColors.RemoveAt(selectedSwatch);
                selectedSwatch = swatchColors.Count == 0 ? -1 : Math.Min(selectedSwatch, swatchColors.Count - 1);
                RebuildSwatches();
                return Task.CompletedTask;
            })));

        var expander = new Expander { Header = "自定义拾色器", HorizontalAlignment = HorizontalAlignment.Stretch, Content = paletteEditor };

        return Card(
            state.Region == 1 ? "主背光" : $"灯光区域 {state.Region}",
            effects,
            brightness,
            speed,
            reverse,
            swatchCountText,
            swatchRows,
            Text("预设色板（点击添加到调色板）：", 12),
            presetRow,
            expander,
            lightStatus,
            Row(
                Button("保存此区域灯光", async () =>
                {
                    var next = currentState with
                    {
                        Effect = Id(effects),
                        Flag = reverse.IsOn ? 1 : 0,
                        Brightness = (int)Math.Round(brightness.Value * 255 / 100),
                        Speed = Id(speed),
                        Colors = swatchColors.Select(c => c.ToArray()).ToArray()
                    };
                    lightStatus.Text = "正在写入并校验灯光设置…";
                    await KeyWrite(d => d.SetLight(next));
                    currentState = next;
                    lightStatus.Text = $"区域 {state.Region} 灯光已保存并读回一致 · {DateTime.Now:HH:mm:ss}";
                }, true),
                Button("还原读取时的灯光", async () =>
                {
                    lightStatus.Text = "正在还原灯光设置…";
                    await KeyWrite(d => d.SetLight(state));
                    currentState = state;
                    effects.SelectedItem = ((List<Choice>)effects.ItemsSource).FirstOrDefault(x => x.Id == state.Effect);
                    brightness.Value = (int)Math.Round(state.Brightness * 100.0 / 255);
                    speed.SelectedItem = ((List<Choice>)speed.ItemsSource).FirstOrDefault(x => x.Id == state.Speed);
                    reverse.IsOn = state.Flag != 0;
                    swatchColors = state.Colors.Select(c => c.ToArray()).ToList();
                    selectedSwatch = swatchColors.Count > 0 ? 0 : -1;
                    if (selectedSwatch >= 0)
                        picker.Color = Color.FromArgb(255, swatchColors[0][0], swatchColors[0][1], swatchColors[0][2]);
                    RebuildSwatches();
                    lightStatus.Text = $"已还原为进入页面时的灯光设置 · {DateTime.Now:HH:mm:ss}";
                })));
    }

    static byte[] ParseColor(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            throw new ArgumentException("颜色格式应为 #RRGGBB，例如 #FF7733");
        return new[] { (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb };
    }
}
