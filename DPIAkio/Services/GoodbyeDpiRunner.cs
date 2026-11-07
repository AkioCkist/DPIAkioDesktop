using System.Diagnostics;
using System.Text;

namespace DPIAkio.Services;

/// <summary>Starts/stops goodbyedpi.exe and streams its console output.</summary>
public sealed class GoodbyeDpiRunner : IDisposable
{
    private Process? _process;

    public event Action<string>? Output;
    public event Action? Exited;

    public static string BinDir => Path.Combine(AppContext.BaseDirectory, "goodbyedpi");
    public static string ExePath => Path.Combine(BinDir, "goodbyedpi.exe");

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Returns missing required files (empty when everything is in place).</summary>
    public static IReadOnlyList<string> MissingFiles()
    {
        string[] required = ["goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys"];
        return required.Where(f => !File.Exists(Path.Combine(BinDir, f))).ToList();
    }

    public void Start(string arguments)
    {
        if (IsRunning) return;

        var psi = new ProcessStartInfo(ExePath, arguments)
        {
            WorkingDirectory = BinDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
        p.Exited += (_, _) => Exited?.Invoke();
        p.Start();
        // Packet processing must not be starved when a game pegs the CPU (DNS/HTTPS latency spikes).
        // goodbyedpi is idle unless web/DNS packets arrive, so this costs games nothing.
        try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _process = p;
    }

    public void Stop()
    {
        var p = _process;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
        }
        catch { }
        finally
        {
            p.Dispose();
            _process = null;
        }
    }

    /// <summary>Kill any goodbyedpi.exe instance not started by us (e.g. left over from a crash).</summary>
    public static int KillStrayInstances()
    {
        int n = 0;
        foreach (var p in Process.GetProcessesByName("goodbyedpi"))
        {
            try { p.Kill(); n++; } catch { }
            p.Dispose();
        }
        return n;
    }

    /// <summary>Drop cached (possibly poisoned) DNS answers, e.g. after switching networks.</summary>
    public static bool FlushDnsCache()
    {
        try { return DnsFlushResolverCache(); } catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("dnsapi.dll")]
    private static extern bool DnsFlushResolverCache();

    public void Dispose() => Stop();
}
