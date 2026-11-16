using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DareuLocal.Core;
using Windows.UI;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    static readonly string[] EffectNames = { "关闭", "常亮", "呼吸", "霓虹", "按键响应", "波浪", "雨滴", "聚合", "涟漪", "跑马灯", "旋转", "星光", "热力", "自定义 1", "自定义 2", "自定义 3", "自定义 4", "自定义 5", "实时帧", "环绕", "彩虹", "光波", "流光", "启动", "区域响应", "行响应", "瀑布", "扫描", "心跳", "流萤", "心形呼吸", "月光呼吸", "星光呼吸" };
    void Lights(StackPanel body)
    {
        if (IsMouse)
        {
            body.Children.Add(Card(endpoint?.Name ?? "鼠标", Text(endpoint?.Product?.HasLighting==true?"此鼠标的独立灯效协议尚未适配。可先使用按键映射、DPI 与性能设置。":"此型号未提供 RGB 灯光区域。可在按键映射中配置 DPI 切换，在性能页调整 DPI 与回报率。")));
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
        var effects = Choices("灯效", state.Effects.Select(i => new Choice(i, i < EffectNames.Length ? EffectNames[i] : $"灯效 {i}")), state.Effect);
        var brightness = Slider("亮度（%）", (int)Math.Round(state.Brightness * 100.0 / 255), 100);
        var speed = Choices("速度", new[] { new Choice(0, "默认"), new Choice(1, "慢"), new Choice(2, "中"), new Choice(3, "快") }, state.Speed);
        var reverse = new ToggleSwitch { Header = "反向播放", IsOn = state.Flag != 0, OnContent = "反向", OffContent = "正向" };
        var colors = new TextBox { Header = "灯效颜色（多个颜色用空格分隔）", Text = string.Join(" ", state.Colors.Select(x => $"#{x[0]:X2}{x[1]:X2}{x[2]:X2}")), PlaceholderText = "#FF7733 #3388FF" };
        var picker = new ColorPicker { IsAlphaEnabled = false, Color = Color.FromArgb(255, 255, 119, 51), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
        var expander = new Expander { Header = "选择颜色", HorizontalAlignment = HorizontalAlignment.Stretch, Content = Column() };
        var palette = (StackPanel)expander.Content;
        palette.Children.Add(picker);
        palette.Children.Add(Button("使用此颜色", () => { colors.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; return Task.CompletedTask; }));
        palette.Children.Add(Button("添加到调色板", () => { colors.Text += (colors.Text.Length > 0 ? " " : "") + $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; return Task.CompletedTask; }));
        return Card(state.Region == 1 ? "主背光" : $"灯光区域 {state.Region}", effects, brightness, speed, reverse, colors, expander, ActionButton("保存此区域灯光", async () => { var values = colors.Text.Split(new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseColor).ToArray(); if (values.Length > 17) throw new ArgumentException("最多使用 17 个颜色"); var next = state with { Effect = Id(effects), Flag = reverse.IsOn ? 1 : 0, Brightness = (int)Math.Round(brightness.Value * 255 / 100), Speed = Id(speed), Colors = values }; await KeyWrite(d => d.SetLight(next)); }, true), ActionButton("还原读取时的灯光", async () =>
        {
            await KeyWrite(d => d.SetLight(state));
            effects.SelectedItem = ((List<Choice>)effects.ItemsSource).FirstOrDefault(x => x.Id == state.Effect);
            brightness.Value = (int)Math.Round(state.Brightness * 100.0 / 255);
            speed.SelectedItem = ((List<Choice>)speed.ItemsSource).FirstOrDefault(x => x.Id == state.Speed);
            reverse.IsOn = state.Flag != 0;
            colors.Text = string.Join(" ", state.Colors.Select(x => $"#{x[0]:X2}{x[1]:X2}{x[2]:X2}"));
        }));
    }
    static byte[] ParseColor(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            throw new ArgumentException("颜色格式应为 #RRGGBB，例如 #FF7733");
        return new[] { (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb };
    }
}
