using System.Diagnostics;
using System.IO;

namespace Clokr.Services;

/// <summary>
/// Manages Windows auto-start via Task Scheduler to support admin privileges.
/// </summary>
public class AutoStartService
{
    private const string TaskName = "Clokr";

    /// <summary>Returns true if the scheduled task for Clokr exists.</summary>
    public bool IsEnabled()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Enables auto-start by creating a scheduled task with highest privileges.</summary>
    public void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var escapedExePath = System.Security.SecurityElement.Escape(exePath);

        try
        {
            // Use XML task definition to have full control over conditions.
            // This disables all power/idle conditions so the task runs regardless of power source.
            var taskXml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Start Clokr on user logon with admin privileges</Description>
                  </RegistrationInfo>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>false</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <WakeToRun>false</WakeToRun>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{escapedExePath}</Command>
                    </Exec>
                  </Actions>
                </Task>
                """;

            // Write XML to a temp file, import it, then clean up
            var tempXmlPath = Path.Combine(Path.GetTempPath(), $"clokr_task_{Guid.NewGuid():N}.xml");
            File.WriteAllText(tempXmlPath, taskXml, System.Text.Encoding.Unicode);

            try
            {
                string args = $"/create /tn \"{TaskName}\" /xml \"{tempXmlPath}\" /f";

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit();
            }
            finally
            {
                try { File.Delete(tempXmlPath); } catch { /* ignore cleanup errors */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enable autostart: {ex.Message}");
        }
    }

    /// <summary>Disables auto-start by removing the scheduled task.</summary>
    public void Disable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{TaskName}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to disable autostart: {ex.Message}");
        }
    }

    /// <summary>Sets auto-start enabled or disabled.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }
}
