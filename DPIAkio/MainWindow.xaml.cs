using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using DPIAkio.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DPIAkio;

public sealed partial class MainWindow : Window
{
    private const int MaxLogChars = 60_000;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly GoodbyeDpiRunner _runner = new();
    private bool _loading = true;
    private bool _wantRunning;   // user wants GoodbyeDPI on (drives watchdog + network-change restarts)
    private bool _exiting;
    private bool _visible;
    private readonly TrayIcon _tray;
    private readonly DispatcherQueueTimer _netTimer;
    private readonly DispatcherQueueTimer _restartTimer;
    private readonly List<DateTime> _restarts = [];

    public MainWindow(bool startInTray = false)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        AppWindow.SetIcon(iconPath);
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(880 * scale), (int)(900 * scale)));
        _visible = !startInTray;

        _runner.Output += line => DispatcherQueue.TryEnqueue(() => AppendLog(line));
        _runner.Exited += () => DispatcherQueue.TryEnqueue(OnProcessExited);

        // Tray
        _tray = new TrayIcon(iconPath, "DPIAkio");
        _tray.Clicked += ShowFromTray;
        _tray.MenuProvider = () =>
        [
            (_runner.IsRunning ? "Thoát App" : "Bật GUI",
                PowerButton.IsEnabled ? () => PowerButton_Click(this, new RoutedEventArgs()) : null),
            ("Mở DPIAkio", ShowFromTray),
            ("-", null),
            ("Thoát", ExitApp),
        ];
        AppWindow.Closing += (_, e) =>
        {
            if (_exiting || !_settings.CloseToTray) return;
            e.Cancel = true;
            HideToTray();
        };
        Closed += (_, _) =>
        {
            _exiting = true;
            SaveFromUi();
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _wantRunning = false;
            _runner.Stop();
            _tray.Dispose();
        };

        // Network changes (Wi-Fi <-> LAN, reconnects, resume from sleep): debounce, then flush DNS and
        // make sure goodbyedpi is alive. WinDivert itself is interface-agnostic, so no restart is needed.
        _netTimer = DispatcherQueue.CreateTimer();
        _netTimer.Interval = TimeSpan.FromSeconds(3);
        _netTimer.IsRepeating = false;
        _netTimer.Tick += (_, _) => HandleNetworkChange();
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        _restartTimer = DispatcherQueue.CreateTimer();
        _restartTimer.Interval = TimeSpan.FromSeconds(2);
        _restartTimer.IsRepeating = false;
        _restartTimer.Tick += (_, _) => { if (_wantRunning && !_runner.IsRunning) StartDpi(); };

        foreach (var m in ModePreset.All) ModeCombo.Items.Add(m);
        DnsGroupCombo.Items.Add(AllGroups);
        foreach (var g in DnsCatalog.Groups) DnsGroupCombo.Items.Add(g);
        DnsGroupCombo.SelectedIndex = 0;
        LoadToUi();
        _loading = false;
        RefreshPreview();
        CheckFiles();
        UpdateStatus();

        _ = RefreshStartupToggleAsync();

        // Launched by the logon task (--tray) => the user wants protection on.
        if ((_settings.AutoStart || startInTray) && GoodbyeDpiRunner.MissingFiles().Count == 0)
            StartDpi();
        if (startInTray) TrimMemory();
    }

    // ---------- window / tray ----------

    public void ShowFromTray()
    {
        if (_exiting) return;
        AppWindow.Show();
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
        _visible = true;
        RenderLog();
    }

    private void HideToTray()
    {
        AppWindow.Hide();
        _visible = false; // stop rendering log lines while hidden
        TrimMemory();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Give RAM back to the OS while sitting in the tray (more headroom for the game).</summary>
    private static void TrimMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }

    // ---------- Windows startup ----------

    private async Task RefreshStartupToggleAsync()
    {
        // Refresh the task when it exists so it follows the exe if the app folder was moved.
        var registered = await Task.Run(() => StartupTask.IsRegistered() && StartupTask.Register().Ok);
        _loading = true;
        StartupToggle.IsOn = registered;
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = StartupToggle.IsOn;
        StartupToggle.IsEnabled = false;
        var (ok, output) = await Task.Run(() => on ? StartupTask.Register() : StartupTask.Unregister());
        AppendLog(ok
            ? (on ? "[DPIAkio] Đã đăng ký chạy cùng Windows (Task Scheduler: DPIAkio)." : "[DPIAkio] Đã gỡ chạy cùng Windows.")
            : $"[DPIAkio] Lỗi Task Scheduler: {output}");
        if (!ok)
        {
            _loading = true;
            StartupToggle.IsOn = !on;
            _loading = false;
        }
        StartupToggle.IsEnabled = true;
    }

    // ---------- network changes ----------

    private void OnNetworkAddressChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => { _netTimer.Stop(); _netTimer.Start(); });

    private void HandleNetworkChange()
    {
        if (!_wantRunning || _exiting) return;
        var online = NetworkInterface.GetIsNetworkAvailable();
        var flushed = GoodbyeDpiRunner.FlushDnsCache();
        AppendLog($"[DPIAkio] Mạng thay đổi{(online ? "" : " (đang mất mạng)")}. "
                  + (flushed ? "Đã xóa cache DNS cũ." : "Không xóa được cache DNS."));
        if (!_runner.IsRunning)
        {
            AppendLog("[DPIAkio] goodbyedpi không chạy, bật lại.");
            StartDpi();
        }
    }

    // ---------- settings <-> UI ----------

    private void LoadToUi()
    {
        var idx = Array.FindIndex(ModePreset.All, m => m.Flag == _settings.ModeFlag);
        ModeCombo.SelectedIndex = idx >= 0 ? idx : 0;
        DnsToggle.IsOn = _settings.DnsRedirect;
        DnsAddrBox.Text = _settings.DnsAddr;
        DnsPortBox.Value = _settings.DnsPort;
        DnsV6Toggle.IsOn = _settings.DnsV6Redirect;
        DnsV6AddrBox.Text = _settings.DnsV6Addr;
        DnsV6PortBox.Value = _settings.DnsV6Port;
        BlacklistCheck.IsChecked = _settings.UseBlacklist;
        BlacklistBox.Text = _settings.BlacklistPath;
        ExtraArgsBox.Text = _settings.ExtraArgs;
        AutoStartCheck.IsChecked = _settings.AutoStart;
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;

        _selectedProvider = DnsCatalog.Find(_settings.DnsProviderId)
            ?? DnsCatalog.Match(_settings.DnsAddr, _settings.DnsPort)
            ?? DnsCatalog.Custom;
        ApplyDnsFilter();
    }

    private void SaveFromUi()
    {
        if (ModeCombo.SelectedItem is ModePreset m) _settings.ModeFlag = m.Flag;
        _settings.DnsRedirect = DnsToggle.IsOn;
        _settings.DnsProviderId = _selectedProvider?.Id;
        _settings.DnsAddr = DnsAddrBox.Text;
        _settings.DnsPort = PortOr(DnsPortBox.Value, 53);
        _settings.DnsV6Redirect = DnsV6Toggle.IsOn;
        _settings.DnsV6Addr = DnsV6AddrBox.Text;
        _settings.DnsV6Port = PortOr(DnsV6PortBox.Value, 53);
        _settings.UseBlacklist = BlacklistCheck.IsChecked == true;
        _settings.BlacklistPath = BlacklistBox.Text;
        _settings.ExtraArgs = ExtraArgsBox.Text;
        _settings.AutoStart = AutoStartCheck.IsChecked == true;
        _settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _settings.Save();
    }

    private static int PortOr(double v, int fallback) =>
        double.IsNaN(v) ? fallback : (int)Math.Clamp(v, 1, 65535);

    private void Option_Changed(object sender, object e)
    {
        if (_loading) return;
        SaveFromUi();
        RefreshPreview();
    }

    private void NumberBox_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => Option_Changed(sender, args);

    private void RefreshPreview()
    {
        var mode = ModeCombo.SelectedItem as ModePreset;
        ModeDesc.Text = mode?.Description ?? "";
        ModeDesc.Visibility = string.IsNullOrEmpty(ModeDesc.Text) ? Visibility.Collapsed : Visibility.Visible;
        ShowModeExplanation(mode);
        CommandPreview.Text = "goodbyedpi.exe " + _settings.BuildArguments();
        bool custom = _selectedProvider?.IsCustom ?? true;
        DnsAddrBox.IsEnabled = DnsPortBox.IsEnabled = DnsToggle.IsOn && custom;
        DnsV6Toggle.IsEnabled = DnsToggle.IsOn && (custom || _selectedProvider!.V6.Length > 0);
        DnsV6AddrBox.IsEnabled = DnsV6PortBox.IsEnabled = DnsV6Toggle.IsEnabled && DnsV6Toggle.IsOn && custom;
        DnsList.IsEnabled = DnsSearchBox.IsEnabled = DnsGroupCombo.IsEnabled = DnsToggle.IsOn;
        BlacklistBox.IsEnabled = BlacklistCheck.IsChecked == true;
    }

    private ModePreset? _explainedMode;

    private void ShowModeExplanation(ModePreset? mode)
    {
        if (mode == _explainedMode) return; // RefreshPreview runs on every keystroke; rebuild only on change
        _explainedMode = mode;
        ModeExplain.Text = mode?.Explanation ?? "";
        ModeFlagsPanel.Children.Clear();
        if (mode == null) return;

        foreach (var (flag, help) in mode.ExplainFlags())
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = flag,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = Res("AccentTextFillColorPrimaryBrush"),
                IsTextSelectionEnabled = true,
            });
            var desc = new TextBlock
            {
                Text = help,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = Res("TextFillColorSecondaryBrush"),
            };
            Grid.SetColumn(desc, 1);
            row.Children.Add(desc);
            ModeFlagsPanel.Children.Add(row);
        }
    }

    private void CheckFiles()
    {
        var missing = GoodbyeDpiRunner.MissingFiles();
        MissingBar.IsOpen = missing.Count > 0;
        MissingBar.Message = $"Không tìm thấy: {string.Join(", ", missing)} trong {GoodbyeDpiRunner.BinDir}";
        PowerButton.IsEnabled = missing.Count == 0;
    }

    // ---------- DNS provider picker ----------

    private const string AllGroups = "Tất cả nhóm";
    private const string ProbeHost = "steamcommunity.com";
    private DnsProvider? _selectedProvider;
    private bool _rebuildingList;

    private void DnsFilter_Changed(object sender, object e)
    {
        if (DnsGroupCombo.SelectedItem == null) return;
        ApplyDnsFilter();
    }

    private void ApplyDnsFilter()
    {
        var q = DnsSearchBox.Text.Trim();
        var group = DnsGroupCombo.SelectedItem as string ?? AllGroups;
        var items = DnsCatalog.All.Where(p =>
            (group == AllGroups || p.Group == group) &&
            (q.Length == 0
             || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
             || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
             || p.V4.Contains(q) || p.V6.Contains(q, StringComparison.OrdinalIgnoreCase)));

        var groups = items.GroupBy(p => p.Group)
            .OrderBy(g => Array.IndexOf(DnsCatalog.Groups, g.Key))
            .Select(g => new DnsGroup(g.Key, g))
            .ToList();

        _rebuildingList = true;
        DnsList.ItemsSource = new CollectionViewSource { IsSourceGrouped = true, Source = groups }.View;
        if (_selectedProvider != null && groups.Any(g => g.Contains(_selectedProvider)))
        {
            DnsList.SelectedItem = _selectedProvider;
            DnsList.ScrollIntoView(_selectedProvider);
        }
        _rebuildingList = false;
    }

    private void DnsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ignore deselection caused by filtering / list rebuilds
        if (_rebuildingList || DnsList.SelectedItem is not DnsProvider p || p == _selectedProvider) return;
        _selectedProvider = p;

        if (!p.IsCustom)
        {
            var wasLoading = _loading;
            _loading = true; // fill boxes without saving half-updated state
            DnsAddrBox.Text = p.V4;
            DnsPortBox.Value = p.Port;
            DnsV6AddrBox.Text = p.V6;
            DnsV6PortBox.Value = p.Port;
            if (p.V6.Length == 0) DnsV6Toggle.IsOn = false;
            _loading = wasLoading;
        }
        Option_Changed(sender, e);
    }

    private async void ProbeAll_Click(object sender, RoutedEventArgs e)
    {
        ProbeAllButton.IsEnabled = false;
        DnsProbeStatus.Text = $"Đang đo… (truy vấn {ProbeHost})";
        var targets = DnsCatalog.All.Where(p => !p.IsCustom).ToList();
        foreach (var p in targets) { p.ProbeText = "…"; p.ProbeBrush = Res("TextFillColorTertiaryBrush"); p.LastLatency = null; }

        await Task.WhenAll(targets.Select(async p =>
        {
            var r = await DnsProbe.QueryAsync(p.V4, p.Port, ProbeHost);
            (p.ProbeText, p.ProbeBrush) = r.Status switch
            {
                ProbeStatus.Ok => ($"{r.Ms} ms", Res(r.Ms < 80 ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush")),
                ProbeStatus.Fake => ("IP giả", Res("SystemFillColorCriticalBrush")),
                ProbeStatus.Timeout => ("Không phản hồi", Res("TextFillColorTertiaryBrush")),
                _ => ("Lỗi", Res("SystemFillColorCriticalBrush")),
            };
            if (r.Status == ProbeStatus.Ok) p.LastLatency = r.Ms;
        }));

        var ok = targets.Where(p => p.LastLatency != null).OrderBy(p => p.LastLatency).ToList();
        DnsProbeStatus.Text = ok.Count == 0
            ? "Không DNS nào trả đúng IP. Nếu goodbyedpi đang bật, tắt đi rồi đo lại."
            : $"{ok.Count}/{targets.Count} DNS trả đúng IP. Nhanh nhất: {string.Join(", ", ok.Take(3).Select(p => $"{p.Name} ({p.LastLatency} ms)"))}.";
        ProbeAllButton.IsEnabled = true;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    // ---------- start / stop ----------

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runner.IsRunning) StopDpi();
        else StartDpi();
    }

    private void StartDpi()
    {
        SaveFromUi();
        var killed = GoodbyeDpiRunner.KillStrayInstances();
        if (killed > 0) AppendLog($"[DPIAkio] Đã tắt {killed} tiến trình goodbyedpi.exe cũ.");

        var args = _settings.BuildArguments();
        AppendLog($"[DPIAkio] Chạy: goodbyedpi.exe {args}");
        try
        {
            _wantRunning = true;
            _runner.Start(args);
        }
        catch (Exception ex)
        {
            AppendLog($"[DPIAkio] Lỗi khởi động: {ex.Message}");
        }
        UpdateStatus();
    }

    private void StopDpi()
    {
        _wantRunning = false;
        _restartTimer.Stop();
        _runner.Stop();
        AppendLog("[DPIAkio] Đã tắt.");
        UpdateStatus();
    }

    private void OnProcessExited()
    {
        // A stale Exited from an older process must not touch the current one.
        if (_runner.IsRunning) { UpdateStatus(); return; }
        _runner.Stop();

        if (_wantRunning && !_exiting)
        {
            // Watchdog: restart automatically, but give up on a crash loop (3 exits within 1 minute).
            _restarts.RemoveAll(t => DateTime.Now - t > TimeSpan.FromMinutes(1));
            if (_restarts.Count < 3)
            {
                _restarts.Add(DateTime.Now);
                AppendLog("[DPIAkio] goodbyedpi.exe thoát bất ngờ, tự bật lại sau 2 giây…");
                _restartTimer.Start();
            }
            else
            {
                _wantRunning = false;
                AppendLog("[DPIAkio] goodbyedpi.exe thoát 3 lần trong 1 phút, ngừng tự bật lại. Xem nhật ký ở trên.");
            }
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool on = _runner.IsRunning;
        StatusTitle.Text = on ? "Đang bật" : "Đã tắt";
        StatusDetail.Text = on
            ? $"GoodbyeDPI đang chạy với {(ModeCombo.SelectedItem as ModePreset)?.Title ?? "tùy chỉnh"}"
            : "Bấm Bật để bắt đầu vượt DPI";
        StatusIcon.Glyph = on ? "" : "";
        StatusRing.Fill = on
            ? (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"]
            : (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        StatusIcon.Foreground = on
            ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        PowerText.Text = on ? "Tắt" : "Bật";
        PowerGlyph.Glyph = on ? "" : "";
        OptionsHost.IsEnabled = !on;
        RunningHint.IsOpen = on;
        _tray.SetTooltip(on ? "DPIAkio — đang bật" : "DPIAkio — đã tắt");
    }

    // ---------- log ----------

    private readonly System.Text.StringBuilder _log = new();

    private void AppendLog(string line)
    {
        _log.Append($"{DateTime.Now:HH:mm:ss}  {line}\n");
        if (_log.Length > MaxLogChars) _log.Remove(0, _log.Length - MaxLogChars);
        if (_visible) RenderLog(); // no UI work at all while hidden in the tray
    }

    private void RenderLog()
    {
        LogBox.Text = _log.ToString();
        LogBox.SelectionStart = LogBox.Text.Length;
        if (FindScrollViewer(LogBox) is { } sv) sv.ChangeView(null, sv.ScrollableHeight, null, true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        LogBox.Text = "";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{GoodbyeDpiRunner.BinDir}\"") { UseShellExecute = true });

    // ---------- file picker (Win32: WinRT pickers don't work in elevated apps) ----------

    private void BrowseBlacklist_Click(object sender, RoutedEventArgs e)
    {
        var buf = new char[1024];
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WindowNative.GetWindowHandle(this),
            lpstrFilter = "Text files (*.txt)\0*.txt\0All files (*.*)\0*.*\0\0",
            nMaxFile = buf.Length,
            lpstrInitialDir = GoodbyeDpiRunner.BinDir,
            Flags = 0x00001000 | 0x00000800 | 0x00080000, // FILEMUSTEXIST | PATHMUSTEXIST | EXPLORER
        };
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            ofn.lpstrFile = handle.AddrOfPinnedObject();
            if (GetOpenFileNameW(ref ofn))
            {
                BlacklistBox.Text = new string(buf).TrimEnd('\0').Split('\0')[0];
                BlacklistCheck.IsChecked = true;
            }
        }
        finally { handle.Free(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
