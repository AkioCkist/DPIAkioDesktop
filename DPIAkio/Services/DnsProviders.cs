using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace DPIAkio.Services;

/// <summary>A public DNS resolver reachable over plain UDP (goodbyedpi can only redirect plain DNS).</summary>
public sealed class DnsProvider : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public string Description { get; init; } = "";
    public string V4 { get; init; } = "";
    public string V6 { get; init; } = "";
    public int Port { get; init; } = 53;

    public bool IsCustom => Id == DnsCatalog.CustomId;

    public string Addresses => IsCustom
        ? "Nhập địa chỉ ở ô bên dưới"
        : V4 + (Port != 53 ? $":{Port}" : "") + (V6.Length > 0 ? $"   ·   {V6}" : "   ·   không có IPv6");

    private string _probeText = "";
    public string ProbeText { get => _probeText; set => Set(ref _probeText, value); }

    private Brush? _probeBrush;
    public Brush? ProbeBrush { get => _probeBrush; set => Set(ref _probeBrush, value); }

    /// <summary>Last measured latency, null when not measured or failed.</summary>
    public int? LastLatency { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class DnsGroup(string key, IEnumerable<DnsProvider> items) : List<DnsProvider>(items)
{
    public string Key { get; } = key;
}

public static class DnsCatalog
{
    public const string CustomId = "custom";

    public const string GPopular = "Phổ biến · nhanh";
    public const string GAds = "Chặn quảng cáo";
    public const string GSecurity = "Bảo mật · chặn mã độc";
    public const string GFamily = "Gia đình · chặn web người lớn";
    public const string GAsia = "Châu Á";
    public const string GEurope = "Châu Âu · Nga";
    public const string GOther = "Khác";

    public static readonly string[] Groups = [GPopular, GAds, GSecurity, GFamily, GAsia, GEurope, GOther];

    public static readonly DnsProvider Custom = new()
    {
        Id = CustomId, Name = "Tùy chỉnh", Group = GOther, Description = "Tự nhập IP và cổng của DNS server",
    };

    public static readonly DnsProvider[] All =
    [
        // ---- Popular / fast, no filtering ----
        new() { Id = "cloudflare", Name = "Cloudflare", Group = GPopular, V4 = "1.1.1.1", V6 = "2606:4700:4700::1111",
                Description = "Nhanh nhất ở VN thường là cái này, không lọc, chú trọng riêng tư" },
        new() { Id = "google", Name = "Google Public DNS", Group = GPopular, V4 = "8.8.8.8", V6 = "2001:4860:4860::8888",
                Description = "Ổn định, phủ sóng toàn cầu, không lọc" },
        new() { Id = "quad9-unsecured", Name = "Quad9 (không lọc)", Group = GPopular, V4 = "9.9.9.10", V6 = "2620:fe::10",
                Description = "Quad9 bản không chặn gì, không lưu IP người dùng" },
        new() { Id = "controld-free", Name = "Control D (không lọc)", Group = GPopular, V4 = "76.76.2.0", V6 = "2606:1a40::",
                Description = "Resolver miễn phí của Control D, không lọc" },
        new() { Id = "dnssb", Name = "DNS.SB", Group = GPopular, V4 = "185.222.222.222", V6 = "2a09::",
                Description = "Không lọc, không log, anycast" },
        new() { Id = "adguard-nofilter", Name = "AdGuard (không lọc)", Group = GPopular, V4 = "94.140.14.140", V6 = "2a10:50c0::1:ff",
                Description = "Hạ tầng AdGuard, không chặn gì" },

        // ---- Ad blocking ----
        new() { Id = "adguard", Name = "AdGuard DNS", Group = GAds, V4 = "94.140.14.14", V6 = "2a10:50c0::ad1:ff",
                Description = "Chặn quảng cáo, tracker và web lừa đảo" },
        new() { Id = "adguard-5353", Name = "AdGuard DNS (cổng 5353)", Group = GAds, V4 = "94.140.14.14", V6 = "2a10:50c0::ad1:ff", Port = 5353,
                Description = "Như trên nhưng dùng cổng 5353, phòng khi nhà mạng chặn cổng 53" },
        new() { Id = "controld-ads", Name = "Control D (chặn quảng cáo)", Group = GAds, V4 = "76.76.2.2", V6 = "2606:1a40::2",
                Description = "Chặn quảng cáo, tracker và mã độc" },
        new() { Id = "controld-social", Name = "Control D (quảng cáo + mạng xã hội)", Group = GAds, V4 = "76.76.2.3", V6 = "2606:1a40::3",
                Description = "Chặn thêm cả mạng xã hội" },

        // ---- Security / malware ----
        new() { Id = "quad9", Name = "Quad9", Group = GSecurity, V4 = "9.9.9.9", V6 = "2620:fe::fe",
                Description = "Chặn domain mã độc, lừa đảo; tổ chức phi lợi nhuận" },
        new() { Id = "cloudflare-malware", Name = "Cloudflare (chặn mã độc)", Group = GSecurity, V4 = "1.1.1.2", V6 = "2606:4700:4700::1112",
                Description = "1.1.1.1 cộng thêm chặn mã độc" },
        new() { Id = "controld-malware", Name = "Control D (chặn mã độc)", Group = GSecurity, V4 = "76.76.2.1", V6 = "2606:1a40::1",
                Description = "Chặn mã độc" },
        new() { Id = "cleanbrowsing-security", Name = "CleanBrowsing Security", Group = GSecurity, V4 = "185.228.168.9", V6 = "2a0d:2a00:1::2",
                Description = "Chặn mã độc, phishing" },
        new() { Id = "opendns", Name = "OpenDNS (Cisco)", Group = GSecurity, V4 = "208.67.222.222", V6 = "2620:119:35::35",
                Description = "Chặn phishing, có từ lâu" },
        new() { Id = "opendns-5353", Name = "OpenDNS (cổng 5353)", Group = GSecurity, V4 = "208.67.222.222", V6 = "2620:119:35::35", Port = 5353,
                Description = "OpenDNS qua cổng 5353, phòng khi cổng 53 bị chặn" },
        new() { Id = "comodo", Name = "Comodo Secure DNS", Group = GSecurity, V4 = "8.26.56.26",
                Description = "Chặn web độc hại, chỉ có IPv4" },

        // ---- Family ----
        new() { Id = "cloudflare-family", Name = "Cloudflare for Families", Group = GFamily, V4 = "1.1.1.3", V6 = "2606:4700:4700::1113",
                Description = "Chặn mã độc + nội dung người lớn" },
        new() { Id = "adguard-family", Name = "AdGuard Family", Group = GFamily, V4 = "94.140.14.15", V6 = "2a10:50c0::bad1:ff",
                Description = "Chặn quảng cáo + người lớn + bật Safe Search" },
        new() { Id = "cleanbrowsing-family", Name = "CleanBrowsing Family", Group = GFamily, V4 = "185.228.168.168", V6 = "2a0d:2a00:1::",
                Description = "Chặn người lớn, proxy/VPN, bật Safe Search" },
        new() { Id = "opendns-family", Name = "OpenDNS FamilyShield", Group = GFamily, V4 = "208.67.222.123", V6 = "2620:119:35::123",
                Description = "Chặn nội dung người lớn" },
        new() { Id = "controld-family", Name = "Control D Family", Group = GFamily, V4 = "76.76.2.4", V6 = "2606:1a40::4",
                Description = "Chặn người lớn, quảng cáo, mã độc" },

        // ---- Asia ----
        new() { Id = "alidns", Name = "AliDNS (Alibaba)", Group = GAsia, V4 = "223.5.5.5", V6 = "2400:3200::1",
                Description = "Trung Quốc, nhanh trong khu vực" },
        new() { Id = "dnspod", Name = "DNSPod (Tencent)", Group = GAsia, V4 = "119.29.29.29", V6 = "2402:4e00::",
                Description = "Trung Quốc" },
        new() { Id = "114dns", Name = "114DNS", Group = GAsia, V4 = "114.114.114.114",
                Description = "Trung Quốc, chỉ có IPv4" },
        new() { Id = "baidu", Name = "Baidu DNS", Group = GAsia, V4 = "180.76.76.76", V6 = "2400:da00::6666",
                Description = "Trung Quốc" },
        new() { Id = "quad101", Name = "Quad101 (TWNIC)", Group = GAsia, V4 = "101.101.101.101", V6 = "2001:de4::101",
                Description = "Đài Loan, không lọc" },

        // ---- Europe / Russia ----
        new() { Id = "yandex", Name = "Yandex (cổng 1253)", Group = GEurope, V4 = "77.88.8.8", V6 = "2a02:6b8::feed:0ff", Port = 1253,
                Description = "Cấu hình mặc định của GoodbyeDPI; cổng lạ nên khó bị nhà mạng chặn" },
        new() { Id = "yandex-safe", Name = "Yandex Safe (cổng 1253)", Group = GEurope, V4 = "77.88.8.88", V6 = "2a02:6b8::feed:bad", Port = 1253,
                Description = "Yandex + chặn web độc hại" },
        new() { Id = "dns4eu", Name = "DNS4EU (không lọc)", Group = GEurope, V4 = "86.54.11.100", V6 = "2a13:1001::86:54:11:100",
                Description = "Dự án DNS công cộng của EU" },
        new() { Id = "dns4eu-protective", Name = "DNS4EU Protective", Group = GEurope, V4 = "86.54.11.1", V6 = "2a13:1001::86:54:11:1",
                Description = "DNS4EU + chặn mã độc, lừa đảo" },
        new() { Id = "fdn", Name = "FDN", Group = GEurope, V4 = "80.67.169.12", V6 = "2001:910:800::12",
                Description = "Pháp, tổ chức phi lợi nhuận, không lọc" },

        Custom,
    ];

    public static DnsProvider? Find(string? id) => id == null ? null : All.FirstOrDefault(p => p.Id == id);

    public static DnsProvider? Match(string addr, int port) =>
        All.FirstOrDefault(p => !p.IsCustom && p.V4 == addr.Trim() && p.Port == port);
}
