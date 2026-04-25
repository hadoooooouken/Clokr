using System.Drawing;
using System.Windows;
using Application = System.Windows.Application;
using Clokr.Services;
using Clokr.ViewModels;
using WinForms = System.Windows.Forms;

namespace Clokr;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private WinForms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Single instance check ───────────────────────────
        _singleInstanceMutex = new Mutex(true, "Clokr_SingleInstance_Mutex", out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show("Clokr is already running.", "Clokr", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // ── Initialize services ─────────────────────────────
        var powerCfgService = new PowerCfgService();
        var powerMonitorService = new PowerMonitorService();
        var settingsService = new SettingsService();
        var autoStartService = new AutoStartService();
        var dttManagementService = new DttManagementService();

        // ── Create ViewModel ────────────────────────────────
        _viewModel = new MainViewModel(powerCfgService, powerMonitorService, settingsService, autoStartService, dttManagementService);

        // ── Apply settings on startup if enabled ────────────
        _viewModel.ApplySettingsOnStartup();

        // ── Create main window ──────────────────────────────
        _mainWindow = new MainWindow();
        _mainWindow.SetViewModel(_viewModel);

        // ── Setup system tray icon ──────────────────────────
        SetupNotifyIcon();

        // ── Show window or start minimized ──────────────────
        if (_viewModel.StartMinimized)
        {
            // Don't show the window, just the tray icon
        }
        else
        {
            _mainWindow.Show();
        }
    }

    private void SetupNotifyIcon()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "Clokr - CPU Frequency Manager for Hybrid Core Processors",
            Icon = LoadAppIcon(),
            Visible = true
        };

        // Context menu
        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Open Clokr", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("Reset to Defaults", null, (_, _) =>
        {
            _viewModel?.ResetCommand.Execute(null);
            _viewModel?.ApplySettingsCommand.Execute(null);
        });
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null || _viewModel == null) return;

        try
        {
            // Try to show existing window
            _mainWindow.Show();
        }
        catch (InvalidOperationException)
        {
            // If window was truly closed (not just hidden), re-create it
            _mainWindow = new MainWindow();
            _mainWindow.SetViewModel(_viewModel);
            _mainWindow.Show();
        }

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        if (_mainWindow != null)
        {
            _mainWindow.IsExiting = true;
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Ensure settings are saved and services are cleaned up
        _viewModel?.SaveSettingsCommand.Execute(null);
        _viewModel?.Cleanup();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    /// <summary>
    /// Loads the app icon from the executable, with fallback to a programmatic icon.
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var appIcon = Icon.ExtractAssociatedIcon(exePath);
                if (appIcon != null) return appIcon;
            }
        }
        catch { /* fallback to programmatic icon */ }

        // Programmatic fallback: blue circle with "C"
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(96, 205, 255));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        using var font = new Font("Segoe UI", 16, System.Drawing.FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(20, 20, 30));
        var size = g.MeasureString("C", font);
        g.DrawString("C", font, textBrush,
            (32 - size.Width) / 2,
            (32 - size.Height) / 2);

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
