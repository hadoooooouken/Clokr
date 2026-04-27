namespace Clokr.Models;

/// <summary>
/// Application settings serialized to JSON.
/// </summary>
public class AppSettings
{
    public PowerProfile Ac { get; set; } = new();
    public PowerProfile Dc { get; set; } = new();

    public bool ApplyOnStartup { get; set; }
    public bool AutoApplyOnPowerChange { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool DisableDtt { get; set; }
}
