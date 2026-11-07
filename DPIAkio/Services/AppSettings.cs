using System.Text.Json;

namespace DPIAkio.Services;

public sealed class AppSettings
{
    public string ModeFlag { get; set; } = "-9";
    // ISPs (e.g. in VN) mostly block via poisoned DNS answers, so redirect DNS by default
    public bool DnsRedirect { get; set; } = true;
    /// <summary>Selected entry in <see cref="DnsCatalog"/>; null = infer from DnsAddr/DnsPort.</summary>
    public string? DnsProviderId { get; set; }
    public string DnsAddr { get; set; } = "77.88.8.8";
    public int DnsPort { get; set; } = 1253;
    public bool DnsV6Redirect { get; set; }
    public string DnsV6Addr { get; set; } = "2a02:6b8::feed:0ff";
    public int DnsV6Port { get; set; } = 1253;
    public bool UseBlacklist { get; set; }
    public string BlacklistPath { get; set; } = "";
    public string ExtraArgs { get; set; } = "";
    public bool AutoStart { get; set; }
    /// <summary>Closing the window hides it to the tray instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPIAkio");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupted file: fall back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Build goodbyedpi.exe arguments from settings.</summary>
    public string BuildArguments()
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(ModeFlag)) args.Add(ModeFlag);
        if (DnsRedirect && !string.IsNullOrWhiteSpace(DnsAddr))
        {
            args.Add($"--dns-addr {DnsAddr.Trim()}");
            args.Add($"--dns-port {DnsPort}");
        }
        if (DnsV6Redirect && !string.IsNullOrWhiteSpace(DnsV6Addr))
        {
            args.Add($"--dnsv6-addr {DnsV6Addr.Trim()}");
            args.Add($"--dnsv6-port {DnsV6Port}");
        }
        if (UseBlacklist && !string.IsNullOrWhiteSpace(BlacklistPath))
            args.Add($"--blacklist \"{BlacklistPath.Trim()}\"");
        if (!string.IsNullOrWhiteSpace(ExtraArgs)) args.Add(ExtraArgs.Trim());
        return string.Join(' ', args);
    }
}

public sealed record ModePreset(string Flag, string Title, string Description)
{
    public static readonly ModePreset[] All =
    [
        new("-9", "Mode 9 (mặc định, khuyên dùng)", "-f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload -q"),
        new("-8", "Mode 8", "-f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload"),
        new("-7", "Mode 7", "-f 2 -e 2 --wrong-chksum --reverse-frag --max-payload"),
        new("-6", "Mode 6", "-f 2 -e 2 --wrong-seq --reverse-frag --max-payload"),
        new("-5", "Mode 5", "-f 2 -e 2 --auto-ttl --reverse-frag --max-payload"),
        new("-4", "Legacy 4 (nhanh nhất)", "-p -r -s"),
        new("-3", "Legacy 3", "-p -r -s -e 40"),
        new("-2", "Legacy 2", "-p -r -s -f 2 -k 2 -n -e 40"),
        new("-1", "Legacy 1 (tương thích nhất)", "-p -r -s -f 2 -k 2 -n -e 2"),
        new("", "Tùy chỉnh", "Chỉ dùng tham số trong ô \"Tham số thêm\""),
    ];

    public override string ToString() => Title;
}
