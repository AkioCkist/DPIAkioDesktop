using System.Runtime.InteropServices;

namespace DPIAkio.Services;

/// <summary>One entry of the tray context menu.</summary>
public sealed record TrayMenuItem(string Text, Action? Action = null)
{
    public bool Checked { get; init; }
    public bool Enabled { get; init; } = true;
    /// <summary>Shown bold; usually the same action as left-clicking the icon.</summary>
    public bool IsDefault { get; init; }
    public IReadOnlyList<TrayMenuItem>? Children { get; init; }
    public bool IsSeparator { get; init; }

    public static readonly TrayMenuItem Separator = new("") { IsSeparator = true };
    /// <summary>Grayed, non-clickable line (status/header).</summary>
    public static TrayMenuItem Label(string text) => new(text) { Enabled = false };
    public static TrayMenuItem Sub(string text, IReadOnlyList<TrayMenuItem> children) => new(text) { Children = children };
}

/// <summary>
/// Notification-area icon backed by its own hidden Win32 window, so it keeps working while the
/// WinUI window is hidden (a hidden window costs no rendering / GPU time).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAY = 0x8000 + 1; // WM_APP + 1
    private const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    private readonly WndProcDelegate _wndProc; // must stay referenced while the window lives
    private readonly IntPtr _hwnd;
    private readonly IntPtr _hIcon;
    private readonly uint _taskbarCreated;
    private string _tip;
    private bool _disposed;

    public event Action? Clicked;

    /// <summary>Builds the context menu on demand (right-click), so it always reflects current state.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public TrayIcon(string iconPath, string tip)
    {
        _tip = tip;
        _wndProc = WndProc;
        var hInst = GetModuleHandle(null);
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInst,
            lpszClassName = "DPIAkioTrayWindow",
        };
        RegisterClassEx(ref cls);
        // Hidden top-level window (not message-only) so it receives the TaskbarCreated broadcast.
        _hwnd = CreateWindowEx(0, cls.lpszClassName, "DPIAkioTray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /*LR_LOADFROMFILE*/);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Notify(NIM_ADD);
    }

    public void SetTooltip(string tip)
    {
        _tip = tip.Length > 127 ? tip[..127] : tip;
        Notify(NIM_MODIFY);
    }

    private void Notify(uint action)
    {
        var d = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _hIcon,
            szTip = _tip,
            szInfo = "",
            szInfoTitle = "",
        };
        Shell_NotifyIcon(action, ref d);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY)
        {
            var ev = (uint)((long)lParam & 0xFFFF);
            if (ev == WM_LBUTTONUP) Clicked?.Invoke();
            else if (ev == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && !_disposed) Notify(NIM_ADD); // explorer.exe restarted
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items == null || items.Count == 0) return;
        var actions = new List<Action>(); // command id = index + 1
        var menu = BuildMenu(items, actions);
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
        int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu); // also destroys attached submenus
        if (cmd > 0 && cmd <= actions.Count) actions[cmd - 1]();
    }

    private static IntPtr BuildMenu(IReadOnlyList<TrayMenuItem> items, List<Action> actions)
    {
        var menu = CreatePopupMenu();
        foreach (var it in items)
        {
            if (it.IsSeparator)
            {
                AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                continue;
            }
            uint flags = MF_STRING | (it.Checked ? MF_CHECKED : 0);
            if (it.Children is { Count: > 0 })
            {
                var sub = BuildMenu(it.Children, actions);
                AppendMenu(menu, flags | MF_POPUP | (it.Enabled ? 0 : MF_GRAYED), (UIntPtr)(ulong)sub, it.Text);
                continue;
            }
            bool enabled = it.Enabled && it.Action != null;
            uint id = 0;
            if (enabled)
            {
                actions.Add(it.Action!);
                id = (uint)actions.Count;
            }
            AppendMenu(menu, flags | (enabled ? 0 : MF_GRAYED), (UIntPtr)id, it.Text);
            if (it.IsDefault && enabled) SetMenuDefaultItem(menu, id, 0);
        }
        return menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Notify(NIM_DELETE);
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        DestroyWindow(_hwnd);
    }

    // ---------- interop ----------

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
