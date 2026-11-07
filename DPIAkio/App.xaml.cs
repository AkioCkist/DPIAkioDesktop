using DPIAkio.Services;
using Microsoft.UI.Xaml;

namespace DPIAkio;

public partial class App : Application
{
#if DEBUG
    // Separate name so F5 in Visual Studio always opens a window even while the release build sits in the tray.
    private const string MutexName = @"Local\DPIAkio.SingleInstance.Debug";
    private const string ShowEventName = @"Local\DPIAkio.Show.Debug";
#else
    private const string MutexName = @"Local\DPIAkio.SingleInstance";
    private const string ShowEventName = @"Local\DPIAkio.Show";
#endif

    private MainWindow? _window;
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single instance: two instances would fight over goodbyedpi.exe.
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Exit();
            return;
        }

        bool startInTray = Environment.GetCommandLineArgs().Contains(StartupTask.TrayArg);
        _window = new MainWindow(startInTray);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var dispatcher = _window.DispatcherQueue;
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => dispatcher.TryEnqueue(() => _window?.ShowFromTray()), null, Timeout.Infinite, false);

        if (!startInTray) _window.Activate();
    }
}
