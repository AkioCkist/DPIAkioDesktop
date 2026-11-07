using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace DPIAkio.Services;

/// <summary>
/// "Run at Windows startup" via a Task Scheduler logon task with highest privileges:
/// the app needs admin (WinDivert) and a Run-key entry would show a UAC prompt on every logon.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "DPIAkio";
    public const string TrayArg = "--tray";

    public static bool IsRegistered() => Schtasks($"/Query /TN \"{TaskName}\"").ExitCode == 0;

    public static (bool Ok, string Output) Register()
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DPIAkio.exe");
        var user = WindowsIdentity.GetCurrent().Name;
        string X(string s) => SecurityElement.Escape(s);

        // Defaults of a schtasks-created task are bad here: "start only on AC power" (laptops) and
        // priority 7 = BelowNormal process priority, which goodbyedpi would inherit and get starved while gaming.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Start DPIAkio (GoodbyeDPI) in the tray at logon</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{X(user)}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{X(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{X(exe)}</Command>
                  <Arguments>{TrayArg}</Arguments>
                  <WorkingDirectory>{X(Path.GetDirectoryName(exe)!)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var tmp = Path.Combine(Path.GetTempPath(), "DPIAkio-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        try
        {
            var r = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
            return (r.ExitCode == 0, r.Output);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public static (bool Ok, string Output) Unregister()
    {
        var r = Schtasks($"/Delete /TN \"{TaskName}\" /F");
        return (r.ExitCode == 0, r.Output);
    }

    private static (int ExitCode, string Output) Schtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return (p.ExitCode, output.Trim());
    }
}
