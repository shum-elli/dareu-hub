using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Text.Json;
namespace DareuLocal.App;
internal static class ThemePalette
{
    public static readonly SolidColorBrush Page = new();
    public static readonly SolidColorBrush Header = new();
    public static readonly SolidColorBrush Card = new();
    public static readonly SolidColorBrush Secondary = new();
    public static readonly SolidColorBrush Primary = new();
    public static readonly SolidColorBrush Recessed = new();
    public static readonly SolidColorBrush Border = new();
    public static readonly SolidColorBrush Key = new();
    public static readonly SolidColorBrush SelectedKey = new();
    public static readonly SolidColorBrush AccentText = new();
    public static readonly SolidColorBrush AccentFill = new(Color.FromArgb(255, 170, 66, 20));
    public static readonly SolidColorBrush OnAccent = new(Color.FromArgb(255, 255, 255, 255));
    static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
    public static void Apply(bool light)
    {
        Page.Color = light ? Rgb(243, 245, 248) : Rgb(20, 23, 28);
        Header.Color = light ? Rgb(255, 255, 255) : Rgb(28, 32, 39);
        Card.Color = light ? Rgb(255, 255, 255) : Rgb(29, 35, 44);
        Secondary.Color = light ? Rgb(76, 89, 107) : Rgb(183, 193, 207);
        Primary.Color = light ? Rgb(28, 36, 48) : Rgb(240, 243, 248);
        Recessed.Color = light ? Rgb(231, 236, 243) : Rgb(24, 29, 36);
        Border.Color = light ? Rgb(202, 211, 224) : Rgb(52, 61, 74);
        Key.Color = light ? Rgb(232, 237, 244) : Rgb(48, 56, 69);
        SelectedKey.Color = light ? Rgb(250, 211, 182) : Rgb(181, 76, 30);
        AccentText.Color = light ? Rgb(168, 61, 17) : Rgb(255, 138, 80);
    }
}
public sealed partial class MainWindow
{
    bool lightTheme;
    string AppearanceFile => Path.Combine(userFolder, "appearance.json");
    void InitializeAppearance()
    {
        try
        {
            lightTheme = File.Exists(AppearanceFile) &&
                JsonSerializer.Deserialize<string>(File.ReadAllText(AppearanceFile)) == "Light";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            lightTheme = false;
        }
        ApplyAppearance();
    }
    void ThemeClicked(object sender, RoutedEventArgs e)
    {
        lightTheme = !lightTheme;
        ApplyAppearance();
        try
        {
            Directory.CreateDirectory(userFolder);
            File.WriteAllText(AppearanceFile, JsonSerializer.Serialize(lightTheme ? "Light" : "Dark"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.IsOpen = true;
            Status.Severity = InfoBarSeverity.Warning;
            Status.Title = "主题已切换，但偏好未能保存";
            Status.Message = ex.Message;
        }
    }
    void ApplyAppearance()
    {
        ThemePalette.Apply(lightTheme);
        Root.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        Root.Background = ThemePalette.Page;
        HeaderBar.Background = ThemePalette.Header;
        BrandTitle.Foreground = ThemePalette.AccentText;
        ThemeButton.Content = lightTheme ? "☀ 白天" : "☾ 黑夜";
        ToolTipService.SetToolTip(ThemeButton, lightTheme ? "切换到黑夜主题" : "切换到白天主题");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeButton,
            lightTheme ? "当前白天主题，点击切换黑夜" : "当前黑夜主题，点击切换白天");
        Green.Color = lightTheme ? Color.FromArgb(255, 8, 113, 70) : Color.FromArgb(255, 115, 218, 161);
        Orange.Color = lightTheme ? Color.FromArgb(255, 164, 69, 19) : Color.FromArgb(255, 255, 161, 101);
        Muted.Color = lightTheme ? Color.FromArgb(255, 82, 97, 118) : Color.FromArgb(255, 151, 165, 183);
        // Shared brushes update existing controls, preserving edits and selection during a theme switch.
        try
        {
            var title = AppWindow.TitleBar;
            title.BackgroundColor = title.ButtonBackgroundColor = ThemePalette.Header.Color;
            title.ForegroundColor = title.ButtonForegroundColor = ThemePalette.Primary.Color;
            title.ButtonHoverBackgroundColor = ThemePalette.Key.Color;
            title.ButtonHoverForegroundColor = ThemePalette.Primary.Color;
            title.InactiveBackgroundColor = title.ButtonInactiveBackgroundColor = ThemePalette.Header.Color;
            title.InactiveForegroundColor = title.ButtonInactiveForegroundColor = ThemePalette.Secondary.Color;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            // Older Windows versions may keep the system caption colors.
        }
    }
}
