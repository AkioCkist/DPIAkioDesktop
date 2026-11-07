using DPIAkio.Services;
using Microsoft.UI.Xaml;

namespace DPIAkio;

public partial class App : Application
{
    private const string MutexName = @"Local\DPIAkio.SingleInstance";
    private const string ShowEventName = @"Local\DPIAkio.Show";

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
