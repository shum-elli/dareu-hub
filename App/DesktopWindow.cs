using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Windows.Foundation;
using Windows.Graphics;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace DareuLocal.App;
public sealed partial class MainWindow
{
    TrayIcon? trayIcon;
    void InitializeDesktopWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "DareuLocal.ico");
        AppWindow.SetIcon(icon);
        ToolTipService.SetToolTip(TitleDragRegion, "拖动移动窗口，双击最大化或还原");
        void AdjustCaptionInset()
        {
            double scale = Root.XamlRoot?.RasterizationScale ?? 1;
            HeaderBar.Padding = new Thickness(24, 4, Math.Max(150, AppWindow.TitleBar.RightInset / scale + 12), 4);
        }
        Root.SizeChanged += (_, _) => AdjustCaptionInset();
        string previousRegions = "";
        void UpdateTitleRegions()
        {
            if (!HeaderBar.IsLoaded || Root.XamlRoot == null) return;
            double scale = Root.XamlRoot.RasterizationScale;
            int width = Math.Max(0, (int)Math.Round(HeaderBar.ActualWidth * scale) - AppWindow.TitleBar.RightInset);
            int height = (int)Math.Round(HeaderBar.ActualHeight * scale);
            var controls = new[] { BackHomeButton, (FrameworkElement)DeviceBox, RefreshButton, ThemeButton };
            var passthrough = new List<RectInt32>();
            foreach (var control in controls)
            {
                if (control.Visibility != Visibility.Visible || control.ActualWidth <= 0) continue;
                var rect = control.TransformToVisual(Root).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                passthrough.Add(new RectInt32((int)Math.Floor(rect.X * scale) - 2, (int)Math.Floor(rect.Y * scale) - 2,
                    (int)Math.Ceiling(rect.Width * scale) + 4, (int)Math.Ceiling(rect.Height * scale) + 4));
            }
            var caption = new List<RectInt32>();
            int left = 0;
            foreach (var rect in passthrough.OrderBy(r => r.X))
            {
                int right = Math.Clamp(rect.X, 0, width);
                if (right > left) caption.Add(new RectInt32(left, 0, right - left, height));
                left = Math.Max(left, Math.Clamp(rect.X + rect.Width, 0, width));
            }
            if (left < width) caption.Add(new RectInt32(left, 0, width - left, height));
            string key = string.Join(";", caption.Concat(passthrough).Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}"));
            if (key == previousRegions) return;
            previousRegions = key;
            var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            source.SetRegionRects(NonClientRegionKind.Caption, caption.ToArray());
            source.SetRegionRects(NonClientRegionKind.Passthrough, passthrough.ToArray());
        }
        HeaderBar.Loaded += (_, _) => { AdjustCaptionInset(); UpdateTitleRegions(); };
        Root.LayoutUpdated += (_, _) => UpdateTitleRegions();
        try
        {
            trayIcon = new TrayIcon(this, icon, RestoreFromTray, Close);
        }
        catch (Win32Exception ex)
        {
            // Keep ordinary taskbar minimization available if Explorer rejects the tray icon.
            AddLog("托盘图标未能初始化：" + ex.Message);
        }
        Closed += (_, _) => { trayIcon?.Dispose(); trayIcon = null; };
    }
    public void RestoreFromTray()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        TrayIcon.ShowWindow(hwnd, TrayIcon.IsIconic(hwnd) ? 9 : 5);
        AppWindow.Show();
        Activate();
        TrayIcon.SetForegroundWindow(hwnd);
    }
}
// A native notification-area icon keeps the WinUI app free of WinForms dependencies.
internal sealed class TrayIcon : IDisposable
{
    const uint CallbackMessage = 0x8001;
    readonly MainWindow window;
    readonly Action restore, exit;
    readonly nint hwnd;
    readonly SubclassProc callback;
    readonly uint taskbarCreated;
    nint icon;
    bool registered, disposed;
    public TrayIcon(MainWindow window, string file, Action restore, Action exit)
    {
        this.window = window; this.restore = restore; this.exit = exit;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        callback = WindowProcedure;
        icon = LoadImage(0, file, 1, 32, 32, 0x10);
        if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetWindowSubclass(hwnd, callback, 1, 0))
        { DestroyIcon(icon); icon = 0; throw new Win32Exception(Marshal.GetLastWin32Error()); }
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        if (!AddIcon())
        { Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "系统通知区域暂不可用"); }
    }
    NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = icon,
        Tip = "Dareu Local · 点击打开，右键查看菜单", Info = "", InfoTitle = ""
    };
    bool AddIcon()
    {
        var data = Data();
        registered = ShellNotifyIcon(0, ref data);
        if (registered) { data.Version = 4; ShellNotifyIcon(4, ref data); }
        return registered;
    }
    nint WindowProcedure(nint handle, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        if (!disposed)
        {
            if (message == taskbarCreated) AddIcon();
            else if (message == 5 && wParam == 1 && registered)
                window.DispatcherQueue.TryEnqueue(() => { if (!disposed && registered) window.AppWindow.Hide(); });
            else if (message == CallbackMessage)
            {
                uint action = (uint)((long)lParam & 0xffff);
                if (action is 0x400 or 0x401 or 0x202)
                    window.DispatcherQueue.TryEnqueue(() => { if (!disposed) restore(); });
                else if (action is 0x7b or 0x205)
                    window.DispatcherQueue.TryEnqueue(ShowMenu);
            }
        }
        return DefSubclassProc(handle, message, wParam, lParam);
    }
    void ShowMenu()
    {
        if (disposed) return;
        nint menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            AppendMenu(menu, 0, 1, "打开 Dareu Local");
            AppendMenu(menu, 0, 2, "隐藏到托盘");
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, 3, "退出");
            SetForegroundWindow(hwnd);
            GetCursorPos(out var point);
            uint command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, 0);
            PostMessage(hwnd, 0, 0, 0);
            if (command == 1) restore();
            else if (command == 2) window.AppWindow.Hide();
            else if (command == 3) exit();
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (registered) { var data = Data(); ShellNotifyIcon(2, ref data); registered = false; }
        RemoveWindowSubclass(hwnd, callback, 1);
        if (icon != 0) { DestroyIcon(icon); icon = 0; }
        GC.KeepAlive(callback);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool ShellNotifyIcon(uint command, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint LoadImage(nint instance, string file, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
