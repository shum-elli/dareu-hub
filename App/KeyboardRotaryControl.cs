using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using DareuLocal.Core;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    sealed record RotaryGroup(KeyChoice Left, KeyChoice Right, KeyChoice? Press, double CenterX, double Top)
    {
        public IEnumerable<KeyChoice> Keys => new[] { Left, Right, Press }.OfType<KeyChoice>();
    }
    List<RotaryGroup> RotaryGroups()
    {
        var remaining = keyLayout.Where(x => x.RotarySegment).ToList();
        var groups = new List<RotaryGroup>();
        while (remaining.Count > 0)
        {
            var first = remaining[0];
            var paired = remaining.FirstOrDefault(x => x.Id != first.Id &&
                Math.Abs((x.X + x.Width / 2.0) - (first.X + first.Width / 2.0)) < 10 &&
                Math.Abs(x.Y - first.Y) < 10);
            remaining.Remove(first);
            if (paired == null) continue;
            remaining.Remove(paired);
            var parts = new[] { first, paired };
            var left = parts.FirstOrDefault(x => x.Default.Function == 8 && x.Default.Data[1] == 234)
                ?? parts.FirstOrDefault(x => x.Label.Contains('-')) ?? first;
            var right = parts.First(x => x.Id != left.Id);
            double center = (first.X + first.Width / 2.0 + paired.X + paired.Width / 2.0) / 2;
            double top = Math.Min(first.Y, paired.Y);
            var press = keyLayout.FirstOrDefault(x => !x.RotarySegment &&
                Math.Abs(x.X + x.Width / 2.0 - center) < 12 &&
                Math.Abs(x.Y + x.Height / 2.0 - (top + first.Height / 2.0)) < 12);
            groups.Add(new(left, right, press, center, top));
        }
        return groups;
    }
    void AddRotaryControls(Canvas canvas, IReadOnlyList<RotaryGroup> groups,
        double minX, double minY, Action<KeyChoice, string> selected, List<Button> buttons)
    {
        foreach (var group in groups)
        {
            // The website uses overlapping clipped half-rings. Use three disjoint hit targets here.
            var grid = new Grid { Width = 72, Height = 44, ColumnSpacing = 0 };
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            Button Make(KeyChoice key, string label, string name, int column, double height)
            {
                var button = Button(label, () =>
                {
                    foreach (var b in buttons) b.Background = ThemePalette.Key;
                    selected(key, name);
                    return Task.CompletedTask;
                });
                button.Width = 22;
                button.Height = height;
                button.Padding = new Thickness(0);
                button.FontSize = 17;
                button.CornerRadius = new CornerRadius(9);
                button.Background = ThemePalette.Key;
                button.VerticalAlignment = VerticalAlignment.Center;
                AutomationProperties.SetName(button, name);
                ToolTipService.SetToolTip(button, name);
                button.Click += (s, e) => button.Background = ThemePalette.SelectedKey;
                buttons.Add(button);
                Grid.SetColumn(button, column);
                grid.Children.Add(button);
                return button;
            }
            Make(group.Left, "−", "旋钮左转 · 音量减", 0, 38);
            if (group.Press is { } press)
            {
                var button = Make(press, "", "旋钮按下 · 静音", 1, 24);
                button.Content = new FontIcon { Glyph = "\uE74F", FontSize = 12 };
            }
            Make(group.Right, "+", "旋钮右转 · 音量加", 2, 38);
            Canvas.SetLeft(grid, group.CenterX - 36 - minX);
            Canvas.SetTop(grid, group.Top - minY);
            canvas.Width = Math.Max(canvas.Width, group.CenterX + 36 - minX + 8);
            canvas.Children.Add(grid);
        }
    }
}
