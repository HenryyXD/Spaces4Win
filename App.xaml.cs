using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using H.NotifyIcon;
using Spaces4Win.Localization;
using Spaces4Win.Settings;
using Spaces4Win.Settings.ViewModels;
using Spaces4Win.Services;

namespace Spaces4Win;

public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;
    private AppServices? _services;
    private LocalizationService? _localization;
    private IDisposable? _quitWatch;
    private SingleInstanceGuard? _singleInstance;

    public static AppServices Services =>
        ((App)Current)._services
        ?? throw new InvalidOperationException("App services are not initialized.");

    public static ILocalizationService Localization =>
        ((App)Current)._localization
        ?? throw new InvalidOperationException("Localization is not initialized.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryHandleControlCommand(e.Args))
        {
            return;
        }

        // Normal second launch exits immediately. Elevated relaunch waits for the old host to exit.
        var wait = e.Args.Any(a => a.Equals("--elevating", StringComparison.OrdinalIgnoreCase))
            ? TimeSpan.FromSeconds(15)
            : TimeSpan.Zero;
        _singleInstance = SingleInstanceGuard.TryAcquire(wait);
        if (_singleInstance is null)
        {
            Shutdown(0);
            // OnExplicitShutdown: ensure the process does not linger without a host.
            Environment.Exit(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            // Keep tray host alive; overlays/theme must not take down the process.
            args.Handled = true;
            try
            {
                System.Diagnostics.Debug.WriteLine(args.Exception);
            }
            catch
            {
                // ignore
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(args.ExceptionObject);
            }
            catch
            {
                // ignore
            }
        };

        _localization = new LocalizationService();
        _services = new AppServices();
        _services.Localization = _localization;
        _services.Start();

        if (!_services.IsRunning)
        {
            return;
        }

        // Overlays must never remain Application.MainWindow (WPF-UI theme Apply targets it).
        MainWindow = null;

        _localization.SetLanguagePreference(_services.Config.Language);
        ThemeHelper.ApplyTheme(_services.Config.Theme, _services.Config.ReduceMotion);

        _notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
        RebuildTrayMenu();
        _notifyIcon.ForceCreate();

        _services.StatusMessage += OnStatusMessage;
        _localization.CultureChanged += (_, _) => Dispatcher.Invoke(RebuildTrayMenu);
        if (_services.IndicatorService is not null)
        {
            _services.IndicatorService.MoveModeChanged += (_, _) => Dispatcher.Invoke(RebuildTrayMenu);
            _services.IndicatorService.AppContextMenuAction = target =>
                Dispatcher.Invoke(() => ShowTrayContextMenu(target));
        }

        _quitWatch = AppControlChannel.StartListening(() =>
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Shutdown();
                }
                catch
                {
                    // ignore
                }
            }));
    }

    /// <summary>
    /// CLI: <c>--quit</c> graceful exit (reveal windows); <c>--recover-hidden</c> journal reveal only.
    /// </summary>
    private bool TryHandleControlCommand(string[] args)
    {
        if (args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase)))
        {
            var code = AppControlChannel.QuitAndWait(TimeSpan.FromSeconds(20));
            Shutdown(code);
            return true;
        }

        if (args.Any(a =>
                a.Equals("--recover-hidden", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--recover", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var visibility = new WindowVisibilityService();
                var journal = new SessionJournal();
                new ShutdownCoordinator(
                    workspaceManager: null,
                    visibility: visibility,
                    journal: journal,
                    hotkeys: null,
                    indicators: null).RecoverIfNeeded();
            }
            catch
            {
                // best effort
            }

            Shutdown(0);
            return true;
        }

        return false;
    }

    private void RebuildTrayMenu()
    {
        if (_notifyIcon is null || _localization is null)
        {
            return;
        }

        var loc = _localization;
        _notifyIcon.ToolTipText = ElevationService.IsCurrentProcessElevated()
            ? loc.Get("App.Tray.TooltipAdmin")
            : loc.Get("App.Tray.Tooltip");

        _notifyIcon.ContextMenu = BuildAppContextMenu();
    }

    private ContextMenu BuildAppContextMenu()
    {
        var loc = _localization!;
        var moveLabel = _services?.IndicatorService?.IsMoveMode == true
            ? loc.Get("Tray.FinishMoveIndicators")
            : loc.Get("Tray.MoveIndicators");

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem(loc.Get("Tray.Settings"), OpenSettings_Click));
        menu.Items.Add(CreateMenuItem(moveLabel, ToggleMoveIndicators_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(loc.Get("Tray.Exit"), Exit_Click));
        return menu;
    }

    /// <summary>
    /// Same menu as the tray icon — used when the icon is missing from the tray/taskbar.
    /// </summary>
    private void ShowTrayContextMenu(FrameworkElement? placementTarget)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var menu = BuildAppContextMenu();
        _notifyIcon.ContextMenu = menu;
        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void OnStatusMessage(object? sender, string message)
    {
        // UI-only feedback via settings when open.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _quitWatch?.Dispose();
        _quitWatch = null;

        if (_services is not null)
        {
            _services.StatusMessage -= OnStatusMessage;
            _services.BeginShutdown();
        }

        _services?.Dispose();
        _notifyIcon?.Dispose();
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ToggleMoveIndicators_Click(object sender, RoutedEventArgs e)
    {
        if (_services?.IndicatorService?.IsMoveMode == true)
        {
            _services.FinishMoveIndicators();
            return;
        }

        _services?.BeginMoveIndicators();
    }

    private void NotifyIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e) => ShowSettings();

    private void Exit_Click(object sender, RoutedEventArgs e) => Shutdown();

    private void ShowSettings()
    {
        if (_services is null || !_services.IsRunning || _localization is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            // SystemThemeWatcher.Watch applies system theme to Application.MainWindow.
            // Clear first so it cannot paint Mica onto an AllowsTransparency indicator.
            MainWindow = null;

            var session = new SettingsSession(_services, _localization);
            var shell = new SettingsShellViewModel(session, _localization);
            var locator = new SettingsSessionLocator
            {
                Session = session,
                Localization = _localization,
                Shell = shell
            };
            var provider = new SettingsPageProvider(locator);
            _settingsWindow = new SettingsWindow(shell, _localization, provider);
            _settingsWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(MainWindow, _settingsWindow))
                {
                    MainWindow = null;
                }

                _settingsWindow = null;
                ThemeHelper.RestoreOverlayWindows();
            };
        }

        MainWindow = _settingsWindow;
        _settingsWindow.Show();
        _settingsWindow.Activate();
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        ThemeHelper.RestoreOverlayWindows();
    }
}
