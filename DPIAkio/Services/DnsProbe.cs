using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DPIAkio.Services;

public enum ProbeStatus { Ok, Fake, Timeout, Error }

public sealed record ProbeResult(ProbeStatus Status, int Ms, IPAddress[] Addresses);

/// <summary>Minimal raw UDP DNS client used to measure resolvers and detect poisoned answers.</summary>
public static class DnsProbe
{
    public static async Task<ProbeResult> QueryAsync(string server, int port, string host, int timeoutMs = 2000)
    {
        if (!IPAddress.TryParse(server.Trim(), out var ip)) return new(ProbeStatus.Error, 0, []);

        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = BuildQuery(id, host);
        try
        {
            using var udp = new UdpClient(ip.AddressFamily);
            udp.Connect(ip, port);
            using var cts = new CancellationTokenSource(timeoutMs);
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(query, cts.Token);
            while (true)
            {
                var res = await udp.ReceiveAsync(cts.Token);
                var b = res.Buffer;
                if (b.Length < 12 || ((b[0] << 8) | b[1]) != id) continue;
                sw.Stop();
                var ips = ParseA(b);
                var fake = ips.Length == 0 || ips.Any(IsBogus);
                return new(fake ? ProbeStatus.Fake : ProbeStatus.Ok, (int)sw.ElapsedMilliseconds, ips);
            }
        }
        catch (OperationCanceledException) { return new(ProbeStatus.Timeout, timeoutMs, []); }
        catch { return new(ProbeStatus.Error, 0, []); }
    }

    private static byte[] BuildQuery(ushort id, string host)
    {
        var q = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            q.Add((byte)bytes.Length);
            q.AddRange(bytes);
        }
        q.AddRange([0, 0, 1, 0, 1]); // root, QTYPE=A, QCLASS=IN
        return q.ToArray();
    }

    private static IPAddress[] ParseA(byte[] b)
    {
        var result = new List<IPAddress>();
        try
        {
            if ((b[3] & 0x0F) != 0) return []; // RCODE != NOERROR
            int qd = (b[4] << 8) | b[5], an = (b[6] << 8) | b[7], pos = 12;
            for (int i = 0; i < qd; i++) pos = SkipName(b, pos) + 4;
            for (int i = 0; i < an; i++)
            {
                pos = SkipName(b, pos);
                int type = (b[pos] << 8) | b[pos + 1];
                pos += 8; // type, class, ttl
                int len = (b[pos] << 8) | b[pos + 1];
                pos += 2;
                if (type == 1 && len == 4) result.Add(new IPAddress(b.AsSpan(pos, 4)));
                pos += len;
            }
        }
        catch (IndexOutOfRangeException) { }
        catch (ArgumentOutOfRangeException) { }
        return result.ToArray();
    }

    private static int SkipName(byte[] b, int pos)
    {
        while (true)
        {
            byte l = b[pos];
            if (l == 0) return pos + 1;
            if ((l & 0xC0) == 0xC0) return pos + 2;
            pos += l + 1;
        }
    }

    /// <summary>Loopback / unspecified / private answers are what ISPs return for blocked sites.</summary>
    private static bool IsBogus(IPAddress ip)
    {
        var a = ip.GetAddressBytes();
        return a[0] == 0 || a[0] == 127 || a[0] == 10
            || (a[0] == 192 && a[1] == 168)
            || (a[0] == 172 && a[1] >= 16 && a[1] <= 31);
    }
}
