using System.Management;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clokr.Models;
using Clokr.Services;

namespace Clokr.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PowerCfgService _powerCfgService;
    private readonly PowerMonitorService _powerMonitorService;
    private readonly SettingsService _settingsService;
    private readonly AutoStartService _autoStartService;
    private AppSettings _settings;
    private bool _isInitialized;

    // ── Boost Mode ──────────────────────────────────────────

    [ObservableProperty]
    private BoostMode _acBoostMode;

    [ObservableProperty]
    private BoostMode _dcBoostMode;

    // ── Frequency Limits ────────────────────────────────────

    [ObservableProperty]
    private int _acClass0Mhz;

    [ObservableProperty]
    private int _dcClass0Mhz;

    [ObservableProperty]
    private int _acClass1Mhz;

    [ObservableProperty]
    private int _dcClass1Mhz;

    [ObservableProperty]
    private int _acClass2Mhz;

    [ObservableProperty]
    private int _dcClass2Mhz;

    // ── UI Topology Bindings ────────────────────────────────

    [ObservableProperty]
    private bool _isClass1Visible;

    [ObservableProperty]
    private bool _isClass2Visible;

    [ObservableProperty]
    private string _class0Name = "All Cores";

    [ObservableProperty]
    private string _class1Name = "";

    [ObservableProperty]
    private string _class2Name = "";

    // ── Settings ────────────────────────────────────────────

    [ObservableProperty]
    private bool _applyOnStartup;

    [ObservableProperty]
    private bool _autoApplyOnPowerChange;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _disableDtt;

    // ── Status ──────────────────────────────────────────────

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isOnAc;

    [ObservableProperty]
    private string _cpuName = "Detecting...";

    // ── Available Boost Modes ───────────────────────────────

    public IReadOnlyList<BoostMode> BoostModes { get; } =
        Enum.GetValues<BoostMode>();

    private readonly DttManagementService _dttManagementService;
    private readonly CpuTopologyService _cpuTopologyService;

    // ── Constructor ─────────────────────────────────────────

    public MainViewModel(
        PowerCfgService powerCfgService,
        PowerMonitorService powerMonitorService,
        SettingsService settingsService,
        AutoStartService autoStartService,
        DttManagementService dttManagementService,
        CpuTopologyService cpuTopologyService)
    {
        _powerCfgService = powerCfgService;
        _powerMonitorService = powerMonitorService;
        _settingsService = settingsService;
        _autoStartService = autoStartService;
        _dttManagementService = dttManagementService;
        _cpuTopologyService = cpuTopologyService;
        
        _settings = settingsService.Load();

        // Load settings into properties
        LoadFromSettings(_settings);

        // Sync StartWithWindows with actual Task Scheduler state
        StartWithWindows = _autoStartService.IsEnabled();

        _isInitialized = true;

        // Ensure DTT state matches the user's setting
        Task.Run(() => _dttManagementService.SetDttState(DisableDtt));

        // Monitor power source
        IsOnAc = _powerMonitorService.IsOnAc;
        _powerMonitorService.PowerSourceChanged += OnPowerSourceChanged;

        // Get CPU name
        Task.Run(DetectCpuName);
    }

    // ── Commands ────────────────────────────────────────────

    [RelayCommand]
    private void ApplySettings()
    {
        try
        {
            SaveToSettings();
            _settingsService.Save(_settings);
            // _autoStartService is now handled automatically by StartWithWindows property change

            var ac = new PowerProfile
            {
                BoostMode = AcBoostMode,
                Class0Mhz = AcClass0Mhz,
                Class1Mhz = AcClass1Mhz,
                Class2Mhz = AcClass2Mhz
            };
            var dc = new PowerProfile
            {
                BoostMode = DcBoostMode,
                Class0Mhz = DcClass0Mhz,
                Class1Mhz = DcClass1Mhz,
                Class2Mhz = DcClass2Mhz
            };

            _powerCfgService.ApplySettings(ac, dc);
            StatusText = "✅ Settings applied successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Error: {ex.Message}";
        }
    }



    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            SaveToSettings();
            _settingsService.Save(_settings);
            // _autoStartService is now handled automatically by StartWithWindows property change
            StatusText = "✅ Settings saved";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Error saving: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Reset()
    {
        AcBoostMode = BoostMode.Aggressive;
        DcBoostMode = BoostMode.Aggressive;
        AcClass0Mhz = 0;
        DcClass0Mhz = 0;
        AcClass1Mhz = 0;
        DcClass1Mhz = 0;
        AcClass2Mhz = 0;
        DcClass2Mhz = 0;
        StatusText = "♻️ Values reset to defaults. Click Apply to save.";
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>
    /// Called on startup if ApplyOnStartup is enabled.
    /// </summary>
    public void ApplySettingsOnStartup()
    {
        if (_settings.ApplyOnStartup)
        {
            try
            {
                var ac = _settings.Ac;
                var dc = _settings.Dc;
                _powerCfgService.ApplySettings(ac, dc);
                StatusText = "✅ Startup settings applied";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Startup apply error: {ex.Message}";
            }
        }
    }

    public void Cleanup()
    {
        _powerMonitorService.PowerSourceChanged -= OnPowerSourceChanged;
        _powerMonitorService.Dispose();
    }

    // ── Settings Change Handlers ────────────────────────────

    partial void OnApplyOnStartupChanged(bool value) => AutoSaveSettings();
    partial void OnAutoApplyOnPowerChangeChanged(bool value) => AutoSaveSettings();
    partial void OnMinimizeToTrayChanged(bool value) => AutoSaveSettings();
    partial void OnStartMinimizedChanged(bool value) => AutoSaveSettings();
    
    partial void OnDisableDttChanged(bool value)
    {
        if (_isInitialized)
        {
            Task.Run(() => _dttManagementService.SetDttState(value));
            AutoSaveSettings();
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_isInitialized) return;
        _autoStartService.SetEnabled(value);
        AutoSaveSettings();
    }

    private void AutoSaveSettings()
    {
        if (!_isInitialized) return;
        SaveToSettings();
        _settingsService.Save(_settings);
    }

    // ── Private Methods ─────────────────────────────────────

    [RelayCommand]
    private void IncrementFrequency(string propertyName)
    {
        AdjustFrequency(propertyName, 100);
    }

    [RelayCommand]
    private void DecrementFrequency(string propertyName)
    {
        AdjustFrequency(propertyName, -100);
    }

    private void AdjustFrequency(string propertyName, int delta)
    {
        switch (propertyName)
        {
            case "AcClass0Mhz": AcClass0Mhz = Math.Max(0, AcClass0Mhz + delta); break;
            case "DcClass0Mhz": DcClass0Mhz = Math.Max(0, DcClass0Mhz + delta); break;
            case "AcClass1Mhz": AcClass1Mhz = Math.Max(0, AcClass1Mhz + delta); break;
            case "DcClass1Mhz": DcClass1Mhz = Math.Max(0, DcClass1Mhz + delta); break;
            case "AcClass2Mhz": AcClass2Mhz = Math.Max(0, AcClass2Mhz + delta); break;
            case "DcClass2Mhz": DcClass2Mhz = Math.Max(0, DcClass2Mhz + delta); break;
        }
    }

    private void OnPowerSourceChanged(bool isOnAc)
    {
        // Must update UI on dispatcher thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsOnAc = isOnAc;
            var source = isOnAc ? "AC" : "Battery";
            StatusText = $"⚡ Power source changed to {source}";

            if (AutoApplyOnPowerChange)
            {
                ApplySettings();
            }
        });
    }


    private void LoadFromSettings(AppSettings s)
    {
        AcBoostMode = s.Ac.BoostMode;
        DcBoostMode = s.Dc.BoostMode;
        AcClass0Mhz = s.Ac.Class0Mhz;
        DcClass0Mhz = s.Dc.Class0Mhz;
        AcClass1Mhz = s.Ac.Class1Mhz;
        DcClass1Mhz = s.Dc.Class1Mhz;
        AcClass2Mhz = s.Ac.Class2Mhz;
        DcClass2Mhz = s.Dc.Class2Mhz;

        ApplyOnStartup = s.ApplyOnStartup;
        AutoApplyOnPowerChange = s.AutoApplyOnPowerChange;
        MinimizeToTray = s.MinimizeToTray;
        StartWithWindows = s.StartWithWindows;
        StartMinimized = s.StartMinimized;
        DisableDtt = s.DisableDtt;
    }

    private void SaveToSettings()
    {
        _settings.Ac = new PowerProfile
        {
            BoostMode = AcBoostMode,
            Class0Mhz = AcClass0Mhz,
            Class1Mhz = AcClass1Mhz,
            Class2Mhz = AcClass2Mhz
        };
        _settings.Dc = new PowerProfile
        {
            BoostMode = DcBoostMode,
            Class0Mhz = DcClass0Mhz,
            Class1Mhz = DcClass1Mhz,
            Class2Mhz = DcClass2Mhz
        };
        _settings.ApplyOnStartup = ApplyOnStartup;
        _settings.AutoApplyOnPowerChange = AutoApplyOnPowerChange;
        _settings.MinimizeToTray = MinimizeToTray;
        _settings.StartWithWindows = StartWithWindows;
        _settings.StartMinimized = StartMinimized;
        _settings.DisableDtt = DisableDtt;
    }

    private void DetectCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CpuName = name;
                        UpdateTopology(name);
                        
                        // DEBUG: Uncomment one of these lines to force a specific UI layout for testing
                        // UpdateTopology("intel ultra"); // Forces 3 classes (P-Cores, E-Cores, LPE-Cores)
                        // UpdateTopology("intel core");  // Forces 2 classes (P-Cores, E-Cores)
                    });
                    return;
                }
            }
        }
        catch
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                CpuName = "Unknown CPU";
            });
        }
    }

    private void UpdateTopology(string cpuName)
    {
        var lowerName = cpuName.ToLowerInvariant();
        
        // Query the OS directly for the number of core classes
        int classCount = _cpuTopologyService.GetCoreClassCount();

        if (classCount == 3)
        {
            // Core Ultra / Meteor Lake -> 3 classes
            IsClass2Visible = true;
            IsClass1Visible = true;
            Class2Name = "🏎️ P-Cores (Performance)";
            Class1Name = "🌿 E-Cores (Efficient)";
            Class0Name = "🍃 LPE-Cores (Low Power Efficient)";
        }
        else if (classCount == 2)
        {
            // Hybrid (Alder/Raptor Lake, etc.) -> 2 classes
            IsClass2Visible = false;
            IsClass1Visible = true;
            Class2Name = "";
            Class1Name = "🏎️ P-Cores (Performance)";
            Class0Name = "🌿 E-Cores (Efficient)";
        }
        else
        {
            // Single class (Older Intel, AMD, etc.) -> 1 class
            IsClass2Visible = false;
            IsClass1Visible = false;
            Class2Name = "";
            Class1Name = "";
            Class0Name = "🏎️ All Cores";
        }

        // Unhide only the settings that apply to this CPU
        _powerCfgService.UnhideSettings(classCount);
    }
}
