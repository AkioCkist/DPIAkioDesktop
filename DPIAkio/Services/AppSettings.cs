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

/// <param name="Description">Raw goodbyedpi flags the mode expands to.</param>
/// <param name="Explanation">Plain-language summary: what it does and when to pick it.</param>
public sealed record ModePreset(string Flag, string Title, string Description, string Explanation)
{
    public static readonly ModePreset[] All =
    [
        new("-9", "Mode 9 (mặc định, khuyên dùng)", "-f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload -q",
            "Giống Mode 8 và chặn thêm QUIC (HTTP/3), buộc trình duyệt và app dùng HTTPS qua TCP để các thủ thuật bên dưới có tác dụng. " +
            "Hợp với đa số nhà mạng; nên thử đầu tiên. Không ảnh hưởng packet game (UDP)."),
        new("-8", "Mode 8", "-f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload",
            "Gửi hai loại gói giả (SEQ sai + checksum sai) để đánh lừa DPI, rồi gửi yêu cầu thật đã chia nhỏ theo thứ tự ngược. " +
            "Dùng khi Mode 9 làm hỏng app nào đó cần QUIC."),
        new("-7", "Mode 7", "-f 2 -e 2 --wrong-chksum --reverse-frag --max-payload",
            "Chỉ dùng gói giả checksum sai: nhẹ, an toàn. Có thể không hoạt động trong máy ảo hoặc sau một số router tự sửa checksum."),
        new("-6", "Mode 6", "-f 2 -e 2 --wrong-seq --reverse-frag --max-payload",
            "Chỉ dùng gói giả SEQ sai. Thử khi Mode 7 không ăn (router của bạn sửa checksum)."),
        new("-5", "Mode 5", "-f 2 -e 2 --auto-ttl --reverse-frag --max-payload",
            "Gói giả có TTL vừa đủ tới thiết bị DPI của nhà mạng nhưng hết hạn trước khi tới server. Mạnh nhất nhưng có thể làm lỗi vài trang; " +
            "chỉ dùng khi Mode 6–9 đều không được."),
        new("-4", "Legacy 4 (nhanh nhất)", "-p -r -s",
            "Chỉ sửa header HTTP và chặn gói chặn giả. Nhanh nhất nhưng KHÔNG vượt được chặn HTTPS — gần như vô dụng với web hiện nay."),
        new("-3", "Legacy 3", "-p -r -s -e 40",
            "Như Legacy 4 + chia nhỏ gói HTTPS ở byte 40. Nhanh, vượt được DPI đời cũ."),
        new("-2", "Legacy 2", "-p -r -s -f 2 -k 2 -n -e 40",
            "Như Legacy 3 + chia nhỏ cả HTTP. Cân bằng giữa tốc độ và tương thích cho DPI đời cũ."),
        new("-1", "Legacy 1 (tương thích nhất)", "-p -r -s -f 2 -k 2 -n -e 2",
            "Chia nhỏ tối đa (2 byte) cả HTTP lẫn HTTPS. Tương thích nhất với DPI đời cũ nhưng chậm nhất."),
        new("", "Tùy chỉnh", "",
            "Không dùng mode có sẵn — chỉ chạy các tham số bạn nhập trong ô \"Tham số thêm\" ở mục Nâng cao."),
    ];

    /// <summary>Meaning of each goodbyedpi flag, shown under the selected mode.</summary>
    public static readonly Dictionary<string, string> FlagHelp = new()
    {
        ["-p"] = "Chặn DPI thụ động: bỏ các gói redirect/RST giả mà nhà mạng chèn vào để cắt kết nối.",
        ["-q"] = "Chặn QUIC/HTTP3 (UDP 443) để trình duyệt chuyển sang HTTPS qua TCP.",
        ["-r"] = "Đổi \"Host:\" thành \"hoSt:\" trong yêu cầu HTTP.",
        ["-s"] = "Bỏ dấu cách sau \"Host:\" trong yêu cầu HTTP.",
        ["-f"] = "Chia nhỏ gói HTTP tại byte này, để DPI không đọc được tên miền liền một mạch.",
        ["-k"] = "Chia nhỏ gói HTTP trên kết nối keep-alive (dùng lại kết nối).",
        ["-n"] = "Không chờ xác nhận mảnh đầu khi dùng -k (nhanh hơn).",
        ["-e"] = "Chia nhỏ gói HTTPS (TLS ClientHello) tại byte này, tách tên miền SNI khỏi tầm đọc của DPI.",
        ["--wrong-seq"] = "Gửi trước một gói giả có số SEQ sai: DPI đọc nó, server thì bỏ qua.",
        ["--wrong-chksum"] = "Gửi trước một gói giả có checksum sai: DPI đọc nó, server thì bỏ qua.",
        ["--auto-ttl"] = "Gửi gói giả với TTL tự tính để nó chết trước khi tới server.",
        ["--reverse-frag"] = "Gửi các mảnh theo thứ tự ngược — hợp với web không chịu được gói bị chia nhỏ thông thường.",
        ["--max-payload"] = "Bỏ qua gói dữ liệu > 1200 byte (tải file, video) để tiết kiệm CPU.",
    };

    /// <summary>Splits "-f 2 -e 2 --wrong-seq" into ("-f 2", help), ("-e 2", help), ("--wrong-seq", help).</summary>
    public IEnumerable<(string Flag, string Help)> ExplainFlags()
    {
        var tokens = Description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            var flag = tokens[i];
            var shown = flag;
            if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out _)) shown += " " + tokens[++i];
            yield return (shown, FlagHelp.GetValueOrDefault(flag, ""));
        }
    }

    public override string ToString() => Title;
}
