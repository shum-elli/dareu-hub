using DareuLocal.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    IEnumerable<Choice> PollingChoices()
    {
        var p = endpoint?.Product;
        var values = (endpoint?.Wireless == true ? p?.PollingWireless : p?.PollingUsb) ?? Array.Empty<int[]>();
        if (values.Length == 0)
            values = new[] { new[] { 8, 125 }, new[] { 4, 250 }, new[] { 2, 500 }, new[] { 1, 1000 }, new[] { 16, 2000 }, new[] { 32, 4000 }, new[] { 64, 8000 } };
        return values.Where(x => x.Length == 2).Select(x => new Choice(x[0], $"{x[1]} Hz"));
    }
    void CatalogPage(StackPanel body)
    {
        var selected = Column();
        if (endpoint != null)
            DevicePreview(selected, endpoint.Product);
        
        body.Children.Add(Card("设备资料库", Text($"已收录 {DeviceCatalog.All.Count} 条硬件记录，{DeviceCatalog.All.Count(x => x.CanConfigure)} 条完成基础设置协议适配；AE6 V2 与 EK75 已做实机验证。其他型号仍需逐台验证。"), Text("参考本机官方驱动 1.5.8.22 的型号模块、按键与功能定义。硬件变体会分别保留，避免仅凭产品名称套用协议。")));
        var search = new TextBox { PlaceholderText = "搜索型号，例如 A950、A98、EK87、Z82" };
        var filter = Choices("分类", new[] { new Choice(-1, "全部设备"), new Choice(0, "鼠标"), new Choice(1, "键盘"), new Choice(2, "接收器"), new Choice(3, "基础设置已适配"), new Choice(4, "磁轴设备") }, -1);
        var results = new ListView { Height = 300, SelectionMode = ListViewSelectionMode.Single };
        var count = Text("");
        void RefreshList()
        {
            var list = DeviceCatalog.All.Where(x => (x.Name + " " + x.Model + " " + x.Pid.ToString("X4")).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            int f = Id(filter);
            list = f switch
            {
                0 or 1 or 2 => list.Where(x => x.Type == f),
                3 => list.Where(x => x.CanConfigure),
                4 => list.Where(x => x.HasTravel),
                _ => list
            };
            var rows = list.OrderBy(x => x.Name).ToList();
            results.ItemsSource = rows.Select(x => new CatalogItem(x, $"{x.Name}   ·   {x.Model}   ·   {x.Pid:X4}   /   {(x.CanConfigure ? "基础设置已适配" : "识别与资料展示")}")).ToList();
            results.DisplayMemberPath = "Label";
            count.Text = $"{rows.Count} 条匹配记录";
        }
        search.TextChanged += (s, e) => RefreshList();
        filter.SelectionChanged += (s, e) => RefreshList();
        results.SelectionChanged += (s, e) => { if (results.SelectedItem is CatalogItem item) { selected.Children.Clear(); DevicePreview(selected, item.Product); } };
        RefreshList();
        var listing = Card("选择要查看的型号", search, filter, count, results);
        var split = new Grid { ColumnSpacing = 18, RowSpacing = 18 };
        split.Children.Add(listing);
        split.Children.Add(selected);
        void Arrange(double width)
        {
            int columns = width >= 760 ? 2 : 1;
            if (split.ColumnDefinitions.Count == columns) return;
            split.ColumnDefinitions.Clear();
            split.RowDefinitions.Clear();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columns == 2 ? 1.15 : 1, GridUnitType.Star) });
            if (columns == 2) split.ColumnDefinitions.Add(new ColumnDefinition());
            split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (columns == 1) split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(selected, columns == 2 ? 1 : 0);
            Grid.SetRow(selected, columns == 2 ? 0 : 1);
        }
        Arrange(Math.Max(400, Root.ActualWidth - 300));
        split.SizeChanged += (s, e) => Arrange(e.NewSize.Width);
        body.Children.Add(split);
    }
    sealed record CatalogItem(ProductDefinition Product, string Label);
    void DevicePreview(StackPanel body, ProductDefinition? product)
    {
        if (product == null)
        {
            body.Children.Add(Card("设备资料待补充", Text("已检测到达尔优硬件，但尚无该型号的协议资料，因此未开放设备设置。")));
            return;
        }
        var picture = ProductImage(product);
        var details = Column();
        if (picture != null)
            details.Children.Add(new Image { Source = picture, Height = 160, MaxWidth = 400, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
        details.Children.Add(Text(product.CanConfigure ? "基础设置协议已适配" : "已收录型号资料，设置协议待适配", 16));
        var features = new List<string>();
        if (product.Type == 0)
        {
            features.Add($"DPI 范围：{product.DpiMin}–{product.DpiMax}");
            if (product.ButtonCount > 0)
                features.Add($"可配置按键：{product.ButtonCount}");
        }
        if (product.KeyCount > 0)
            features.Add($"键盘布局：{product.KeyCount} 个按键单元");
        if (product.HasLighting)
            features.Add("RGB 灯光");
        if (product.HasMatrix)
            features.Add("具有逐键灯光矩阵");
        if (product.HasBattery)
            features.Add("电量与充电状态");
        if (product.HasTravel)
            features.Add("磁轴 / 键程功能（尚待适配）");
        details.Children.Add(Text(string.Join("\n", features)));
        if (!product.CanConfigure)
            details.Children.Add(Text("该型号可在设备中心显示，但本版不执行配置写入。请继续使用官方驱动完成尚未适配的功能。"));
        else if (product.Pid is not (0x1330 or 0x0045 or 0x1264 or 0x1265))
            details.Children.Add(Text("本型号使用已适配的基础协议，尚未在当前电脑上完成实机验证；可用功能以设备实际响应为准。"));
        var provenance = Column();
        provenance.Children.Add(Text($"硬件标识：260D:{product.Pid:X4}\n型号代码：{product.Model}\n协议系列：{product.FirmwareType}"));
        if (product.NativeModules.Length > 0)
            provenance.Children.Add(Text("官方模块：" + string.Join("、", product.NativeModules)));
        details.Children.Add(new Expander { Header = "型号资料", Content = provenance, HorizontalAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(Card(product.Name, details));
    }
}
