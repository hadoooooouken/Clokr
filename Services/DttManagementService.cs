using System;
using System.Diagnostics;
using System.Management;

namespace Clokr.Services;

public class DttManagementService
{
    public void SetDttState(bool disable)
    {
        ManageDevices(disable);
        ManageServices(disable);
    }

    private void ManageDevices(bool disable)
    {
        string command = disable ? "/disable-device" : "/enable-device";
        
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%Dynamic Tuning Technology%' OR Name LIKE '%Innovation Platform Framework%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                string? deviceId = obj["DeviceID"]?.ToString();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    RunProcess("pnputil", $"{command} \"{deviceId}\"");
                }
            }
        }
        catch { }
    }

    private void ManageServices(bool disable)
    {
        try
        {
            using var svcSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Service WHERE DisplayName LIKE '%Dynamic Tuning%' OR DisplayName LIKE '%Innovation Platform Framework%'");
            foreach (ManagementObject obj in svcSearcher.Get())
            {
                string? svcName = obj["Name"]?.ToString();
                if (!string.IsNullOrEmpty(svcName))
                {
                    if (disable)
                    {
                        RunProcess("sc", $"stop \"{svcName}\"");
                        RunProcess("sc", $"config \"{svcName}\" start= disabled");
                    }
                    else
                    {
                        RunProcess("sc", $"config \"{svcName}\" start= auto");
                        RunProcess("sc", $"start \"{svcName}\"");
                    }
                }
            }
        }
        catch { }
    }

    private void RunProcess(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch { }
    }
}
