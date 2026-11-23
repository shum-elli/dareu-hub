using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace DareuLocal.App;

internal sealed class OsdWindow : Window
{
    const int SwHide = 0;
    const int SwShowNoActivate = 4;

    readonly Border frame;
    readonly Border accentBar;
    readonly TextBlock titleBlock;
    readonly TextBlock subtitleBlock;
    readonly DispatcherTimer hideTimer;
    readonly nint hwnd;
    bool disposed;

    public OsdWindow()
    {
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        accentBar = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = ThemePalette.AccentText
        };

        titleBlock = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        subtitleBlock = new TextBlock
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        textStack.Children.Add(titleBlock);
        textStack.Children.Add(subtitleBlock);

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(accentBar);
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        frame = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 16, 12),
            Child = row
        };
        Content = frame;

        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (!disposed)
                ShowWindow(hwnd, SwHide);
        };

        Closed += (_, _) =>
        {
            disposed = true;
            hideTimer.Stop();
        };
    }

    public void ShowToast(string title, string subtitle, bool lightTheme)
    {
        if (disposed)
            return;

        frame.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        frame.Background = new SolidColorBrush(lightTheme ? Color.FromArgb(245, 255, 255, 255) : Color.FromArgb(245, 29, 35, 44));
        frame.BorderBrush = new SolidColorBrush(lightTheme ? Color.FromArgb(255, 202, 211, 224) : Color.FromArgb(255, 62, 72, 88));
        accentBar.Background = new SolidColorBrush(lightTheme ? Color.FromArgb(255, 168, 61, 17) : Color.FromArgb(255, 255, 138, 80));
        titleBlock.Foreground = new SolidColorBrush(lightTheme ? Color.FromArgb(255, 28, 36, 48) : Color.FromArgb(255, 240, 243, 248));
        subtitleBlock.Foreground = new SolidColorBrush(lightTheme ? Color.FromArgb(255, 76, 89, 107) : Color.FromArgb(255, 183, 193, 207));

        titleBlock.Text = title;
        subtitleBlock.Text = subtitle;

        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        int w = (int)Math.Round(280 * scale);
        int h = (int)Math.Round(74 * scale);
        int margin = (int)Math.Round(20 * scale);

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int x = workArea.X + workArea.Width - w - margin;
        int y = workArea.Y + workArea.Height - h - margin;

        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        ShowWindow(hwnd, SwShowNoActivate);

        hideTimer.Stop();
        hideTimer.Start();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(nint hwnd);
}
