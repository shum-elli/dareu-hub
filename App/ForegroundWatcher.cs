using System.Runtime.InteropServices;
using DareuLocal.Core;

namespace DareuLocal.App;

/// <summary>
/// Watches Win32 foreground window switches via SetWinEventHook (WINEVENT_OUTOFCONTEXT)
/// with near-zero background CPU cost.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    const uint EventSystemForeground = 0x0003;
    const uint WineventOutOfContext = 0x0000;
    const uint ProcessQueryLimitedInformation = 0x1000;

    readonly WinEventDelegate callback;
    readonly int selfPid = Environment.ProcessId;
    nint hook;
    string lastProcess = "";
    bool disposed;

    public event Action<string>? ForegroundProcessChanged;

    public ForegroundWatcher()
    {
        callback = OnWinEvent;
    }

    public void SetEnabled(bool enabled)
    {
        if (disposed)
            return;
        if (enabled && hook == 0)
        {
            lastProcess = "";
            hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, callback, 0, 0, WineventOutOfContext);
        }
        else if (!enabled && hook != 0)
        {
            UnhookWinEvent(hook);
            hook = 0;
            lastProcess = "";
        }
    }

    public void ProbeCurrentForeground()
    {
        if (disposed || hook == 0)
            return;
        nint hwnd = GetForegroundWindow();
        if (hwnd != 0)
            HandleWindow(hwnd);
    }

    void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (disposed || eventType != EventSystemForeground || hwnd == 0)
            return;
        HandleWindow(hwnd);
    }

    void HandleWindow(nint hwnd)
    {
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)selfPid)
                return;

            string? processName = TryGetProcessName(pid);
            if (string.IsNullOrEmpty(processName))
                return;

            if (string.Equals(processName, lastProcess, StringComparison.Ordinal))
                return;

            lastProcess = processName;
            ForegroundProcessChanged?.Invoke(processName);
        }
        catch
        {
            // Ignore transient access-denied or exited foreground processes.
        }
    }

    static string? TryGetProcessName(uint pid)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
            return null;
        try
        {
            var buffer = new char[512];
            uint size = (uint)buffer.Length;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size) || size == 0)
                return null;
            string fullPath = new(buffer, 0, (int)size);
            string normalized = AppSettings.NormalizeProcessName(fullPath);
            return normalized.Length > 0 ? normalized : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (hook != 0)
        {
            UnhookWinEvent(hook);
            hook = 0;
        }
        GC.KeepAlive(callback);
    }

    delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(nint hObject);
}
