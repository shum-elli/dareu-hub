using Microsoft.UI.Xaml;
namespace DareuLocal.App;
public partial class App : Application
{
    Window? window;
    Mutex? singleton; EventWaitHandle? restoreSignal; RegisteredWaitHandle? restoreWait;
    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) => { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DareuLocal-error.log"), e.Exception.ToString()); };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        singleton = new Mutex(true, @"Local\DareuLocal-SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DareuLocal-Restore");
            signal.Set(); singleton.Dispose(); singleton = null; Exit(); return;
        }
        restoreSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DareuLocal-Restore");
        window = new MainWindow();
        restoreWait = ThreadPool.RegisterWaitForSingleObject(restoreSignal, (_, _) =>
        {
            if (window is MainWindow main) main.DispatcherQueue.TryEnqueue(main.RestoreFromTray);
        }, null, Timeout.Infinite, false);
        window.Closed += (_, _) =>
        {
            restoreWait?.Unregister(null); restoreSignal?.Dispose();
            singleton?.ReleaseMutex(); singleton?.Dispose(); singleton = null;
        };
        window.Activate();
    }
}
