using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Clokr.Services;

/// <summary>
/// Monitors AC/DC power source changes using SystemEvents and a fallback polling timer.
/// </summary>
public class PowerMonitorService : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _pollTimer;
    private volatile bool _lastIsOnAc;
    private bool _disposed;

    /// <summary>Fires when power source changes. True = AC, False = Battery.</summary>
    public event Action<bool>? PowerSourceChanged;

    /// <summary>Returns true if currently on AC power.</summary>
    public bool IsOnAc => WinForms.SystemInformation.PowerStatus.PowerLineStatus == WinForms.PowerLineStatus.Online;

    public PowerMonitorService()
    {
        _lastIsOnAc = IsOnAc;

        // Primary: system event
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Fallback: poll every 5 seconds in case event is unreliable on some hardware
        _pollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
        {
            CheckAndNotify();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        CheckAndNotify();
    }

    private void CheckAndNotify()
    {
        var currentIsOnAc = IsOnAc;
        if (currentIsOnAc != _lastIsOnAc)
        {
            _lastIsOnAc = currentIsOnAc;
            PowerSourceChanged?.Invoke(currentIsOnAc);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _pollTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
