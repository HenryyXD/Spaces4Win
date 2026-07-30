using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Localization;
using Spaces4Win.Services;
using Spaces4Win.Services.Animation;
using Spaces4Win.Services.Switcher;
using Spaces4Win.Services.Transition;
using Microsoft.Win32;

namespace Spaces4Win;

public sealed class AppServices : IDisposable
{
    public ConfigService ConfigService { get; } = new();
    public AppConfig Config { get; private set; } = AppConfig.CreateDefault();
    public ILocalizationService? Localization { get; set; }
    public IAnimationSettingsService AnimationSettings { get; } = new AnimationSettingsService();
    public MonitorTracker MonitorTracker { get; } = new();
    public WindowVisibilityService Visibility { get; } = new();
    public WindowCaptureCache CaptureCache { get; private set; } = null!;
    public SessionJournal Journal { get; } = new();
    public WindowLayoutStore LayoutStore { get; } = new();
    public WorkspaceManager? WorkspaceManager { get; private set; }
    public HotkeyService? HotkeyService { get; private set; }
    public WindowEventService? WindowEventService { get; private set; }
    public IndicatorService? IndicatorService { get; private set; }
    public OverviewService? OverviewService { get; private set; }
    public WorkspaceTransitionCoordinator? TransitionCoordinator { get; private set; }
    public HotkeyMonitorContext? HotkeyMonitorContext { get; private set; }
    public ForeignWorkspaceActivationService? ForeignWorkspaceActivation { get; private set; }
    public ShutdownCoordinator? ShutdownCoordinator { get; private set; }
    public SwitcherController? SwitcherController { get; private set; }
    public WindowMruTracker WindowMru { get; } = new();

    /// <summary>False when Start() exited early to relaunch elevated.</summary>
    public bool IsRunning { get; private set; }

    private bool _disposed;
    private bool _elevationPromptSuppressedThisSession;
    private bool _shutdownStarted;

    public void Start()
    {
        Config = ConfigService.Load();
        Localization?.SetLanguagePreference(Config.Language);
        AnimationSettings.Apply(Config.MotionPreference);

        // Elevate the app process first when requested. Logon via Highest task already
        // starts elevated (no UAC); manual/debug launches get one UAC for the app itself.
        if (Config.ElevationPreference == ElevationPreference.AlwaysElevate &&
            !ElevationService.IsCurrentProcessElevated())
        {
            if (ElevationService.TryRelaunchElevated())
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }
        }

        // Register/update the logon task after we know our elevation state so Highest
        // is written by an elevated process (avoids “UAC only for schtasks”).
        ApplyStartupRegistration(Config);

        var shutdownProbe = new ShutdownCoordinator(
            workspaceManager: null!,
            visibility: Visibility,
            journal: Journal,
            hotkeys: null,
            indicators: null);
        shutdownProbe.RecoverIfNeeded();

        CaptureCache = new WindowCaptureCache(Visibility);

        MonitorTracker.Start();
        WorkspaceManager = new WorkspaceManager(
            MonitorTracker,
            Visibility,
            Config.GetWorkspaceIds,
            () => Config.ElevationPreference,
            CaptureCache,
            () => Config.EnforceSingleFullscreenPerWorkspace);
        WorkspaceManager.ElevationAssistanceNeeded += OnElevationAssistanceNeeded;
        WorkspaceManager.WorkspaceIdsChanged += OnWorkspaceIdsChanged;

        // After a window is hidden, schedule a background peek-capture so the
        // overview can show real thumbnails without blocking on the UI thread.
        Visibility.WindowHidden += (_, hwnd) => CaptureCache.RequestCapture(hwnd);
        // Invalidate when window becomes active again (content changed since last capture).
        Visibility.WindowShown += (_, hwnd) => CaptureCache.Invalidate(hwnd);
        // Prune dead handles on any state change.
        WorkspaceManager.StateChanged += (_, _) => CaptureCache.PruneDeadWindows();

        HotkeyMonitorContext = new HotkeyMonitorContext(MonitorTracker);
        WorkspaceManager.AttachHotkeyMonitorContext(HotkeyMonitorContext);
        HotkeyMonitorContext.Start();

        HotkeyService = new HotkeyService(WorkspaceManager);
        HotkeyService.StatusMessage += (_, msg) => StatusMessage?.Invoke(this, msg);

        TransitionCoordinator = new WorkspaceTransitionCoordinator(
            WorkspaceManager,
            AnimationSettings,
            () => Config,
            new HudWorkspaceTransitionEngine());

        HotkeyService.SwitchOrCreateAction = (monitorId, ws) =>
            TransitionCoordinator.SwitchOrCreate(monitorId, ws);
        HotkeyService.SwitchAction = (monitorId, ws) =>
            TransitionCoordinator.Switch(monitorId, ws);
        HotkeyService.SwitchToLastAction = monitorId =>
            TransitionCoordinator.SwitchToLast(monitorId);

        SwitcherController = new SwitcherController(
            WorkspaceManager,
            WindowMru,
            (monitorId, ws) =>
            {
                TransitionCoordinator.Switch(monitorId, ws);
                return Task.CompletedTask;
            },
            () => Localization ?? throw new InvalidOperationException("Localization missing."));
        HotkeyService.WorkspaceSwitcherAction = reverse => SwitcherController.OnWorkspaceTab(reverse);
        HotkeyService.WindowSwitcherAction = reverse => SwitcherController.OnWindowQ(reverse);
        HotkeyService.CapsReleased += (_, _) => SwitcherController.Commit();
        HotkeyService.TryHandleEscapeWhileCapsHeld = () =>
        {
            if (SwitcherController is { IsActive: true })
            {
                SwitcherController.Cancel();
                return true;
            }

            if (OverviewService is { IsOpen: true })
            {
                OverviewService.Toggle();
                return true;
            }

            return false;
        };

        OverviewService = new OverviewService(
            WorkspaceManager,
            () => Localization ?? throw new InvalidOperationException("Localization missing."));
        HotkeyService.OverviewAction = () => OverviewService.Toggle();
        HotkeyService.OverviewInputFilter = (vk, mods, capsHeld) =>
            OverviewService?.TryHandleGlobalKey(vk, mods, capsHeld) == true;
        HotkeyService.GetModalKind = () =>
        {
            var switcher = SwitcherController?.ActiveModalKind ?? HotkeyModalKind.None;
            if (switcher != HotkeyModalKind.None)
            {
                return switcher;
            }

            return OverviewService?.IsOpen == true
                ? HotkeyModalKind.Overview
                : HotkeyModalKind.None;
        };
        HotkeyService.FocusAdjacentMonitorAction = direction =>
        {
            var target = MonitorTracker.FocusAdjacentMonitor(direction);
            if (target is null)
            {
                return;
            }

            // SetCursorPos is LLMHF_INJECTED — mouse hook won't flip targeting to Cursor.
            // Without this, ResolveMonitorIdForHotkeys keeps the focused window's old monitor.
            HotkeyMonitorContext?.PreferCursorMonitor();
            IndicatorService?.PulseMonitor(target.DeviceName);
        };
        HotkeyService.ResolveStatusText = key =>
            Localization?.Get(key) ?? key;
        HotkeyService.Apply(Config);

        // Restore open windows into last workspaces; never launch missing apps.
        Visibility.PreferSwHide = Config.HideInactiveFromSwitcher;
        WorkspaceManager.InitializeExistingWindows(LayoutStore.TryLoad());
        WorkspaceManager.StateChanged += OnStateChangedForJournal;
        PersistLayout();

        WindowEventService = new WindowEventService(WorkspaceManager);
        WindowEventService.Start();

        IndicatorService = new IndicatorService(WorkspaceManager, MonitorTracker, ConfigService, () => Config);
        IndicatorService.PinnedTooltip = Localization?.Get("Indicator.Pinned.Tooltip") ?? "Fixado";
        IndicatorService.WorkspaceTooltipFormatter = id =>
            Localization?.Format("Overview.Workspace", id) ?? $"Workspace {id}";
        IndicatorService.WorkspaceSwitchAction = (monitorId, ws) =>
            TransitionCoordinator.Switch(monitorId, ws);
        IndicatorService.ApplyVisualSettings(Config);
        IndicatorService.SetEnabled(Config.ShowFloatingIndicators);

        // Sticky/pinned state is reflected only on the floating indicator (double ring).

        ForeignWorkspaceActivation = new ForeignWorkspaceActivationService(
            WorkspaceManager,
            () => HotkeyService.IsCapsPhysicallyHeld,
            (monitorId, ws, hwnd) => TransitionCoordinator.SwitchAndActivate(monitorId, ws, hwnd));
        ForeignWorkspaceActivation.Start();

        ShutdownCoordinator = new ShutdownCoordinator(
            WorkspaceManager,
            Visibility,
            Journal,
            HotkeyService,
            IndicatorService,
            PersistLayout,
            () => WorkspaceManager?.ExitAllFullscreen());

        WriteUncleanJournal();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        IsRunning = true;
    }

    public event EventHandler<string>? StatusMessage;

    public void SaveAndApply(AppConfig config)
    {
        if (!IsRunning || WorkspaceManager is null || HotkeyService is null)
        {
            return;
        }

        var elevationChanged = config.ElevationPreference != Config.ElevationPreference;
        Config = config;
        ConfigService.Save(config);
        ApplyStartupRegistration(config);
        Localization?.SetLanguagePreference(config.Language);

        HotkeyService.Apply(config);
        var hideModeChanged = Visibility.PreferSwHide != config.HideInactiveFromSwitcher;
        Visibility.PreferSwHide = config.HideInactiveFromSwitcher;
        if (hideModeChanged)
        {
            WorkspaceManager.ReapplyVisibilityFromAssignments();
        }

        ApplyIndicatorVisuals(config);
        AnimationSettings.Apply(config.MotionPreference);

        if (elevationChanged)
        {
            _elevationPromptSuppressedThisSession = config.ElevationPreference == ElevationPreference.NeverAsk;
        }

        if (config.ElevationPreference == ElevationPreference.AlwaysElevate &&
            !ElevationService.IsCurrentProcessElevated())
        {
            RequestRestartElevated();
        }
    }

    private void ApplyStartupRegistration(AppConfig config)
    {
        try
        {
            StartupService.Apply(
                config.StartWithWindows,
                elevatedAtLogon: config.ElevationPreference == ElevationPreference.AlwaysElevate);
        }
        catch (Exception ex)
        {
            DebugLogStartup(ex);
            if (IsRunning)
            {
                StatusMessage?.Invoke(
                    this,
                    Localization?.Format("Settings.Startup.RegistrationFailed", ex.Message)
                    ?? $"Startup registration failed: {ex.Message}");
            }
        }
    }

    private static void DebugLogStartup(Exception ex)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Startup registration failed: {ex.Message}");
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Live indicator chrome only — no disk write / hotkey rebind.</summary>
    public void ApplyIndicatorVisuals(AppConfig config)
    {
        if (!IsRunning || IndicatorService is null)
        {
            return;
        }

        // Sync visual fields onto the live config without replacing the whole object.
        Config.IndicatorOpacity = config.IndicatorOpacity;
        Config.IndicatorScale = config.IndicatorScale;
        Config.IndicatorAnimations = config.IndicatorAnimations;
        Config.PinnedWindowIndicatorMode = config.PinnedWindowIndicatorMode;
        Config.ShowFloatingIndicators = config.ShowFloatingIndicators;

        var animations = config.IndicatorAnimations && AnimationSettings.AnimationsEnabled;
        IndicatorService.ApplyVisualSettings(new AppConfig
        {
            IndicatorOpacity = config.IndicatorOpacity,
            IndicatorScale = config.IndicatorScale,
            IndicatorAnimations = animations,
            ReduceMotion = false,
            PinnedWindowIndicatorMode = config.PinnedWindowIndicatorMode
        });
        IndicatorService.PinnedTooltip = Localization?.Get("Indicator.Pinned.Tooltip") ?? "Fixado";
        IndicatorService.WorkspaceTooltipFormatter = id =>
            Localization?.Format("Overview.Workspace", id) ?? $"Workspace {id}";
        IndicatorService.SetEnabled(config.ShowFloatingIndicators);
    }

    public void BeginMoveIndicators()
    {
        IndicatorService?.EnterMoveMode();
        StatusMessage?.Invoke(this, Localization?.Get("Status.MoveMode") ?? "Move mode");
    }

    public void FinishMoveIndicators()
    {
        IndicatorService?.ExitMoveMode(commit: true);
        StatusMessage?.Invoke(this, Localization?.Get("Status.MoveFinished") ?? "Done");
    }

    public void ToggleMoveIndicators()
    {
        if (IndicatorService?.IsMoveMode == true)
        {
            FinishMoveIndicators();
        }
        else
        {
            BeginMoveIndicators();
        }
    }

    public void ResetIndicatorPositions()
    {
        IndicatorService?.ResetPositionsToDefault();
        StatusMessage?.Invoke(this, Localization?.Get("Status.PositionsRestored") ?? "Restored");
    }

    public bool RequestRestartElevated()
    {
        if (ElevationService.IsCurrentProcessElevated())
        {
            return true;
        }

        if (!ElevationService.TryRelaunchElevated())
        {
            return false;
        }

        BeginShutdown();
        System.Windows.Application.Current.Shutdown();
        return true;
    }

    public void BeginShutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        ForeignWorkspaceActivation?.Dispose();
        ForeignWorkspaceActivation = null;
        WindowEventService?.Dispose();
        WindowEventService = null;
        ShutdownCoordinator?.Execute();
    }

    private void WriteUncleanJournal()
    {
        if (WorkspaceManager is null)
        {
            return;
        }

        var snapshot = WorkspaceManager.SnapshotManagedWindows();
        var ownership = Visibility.Snapshot();
        Journal.Write(new SessionJournalDocument
        {
            CleanShutdown = false,
            UpdatedAt = DateTimeOffset.UtcNow,
            Windows = snapshot.Select(s =>
            {
                Native.NativeMethods.GetWindowThreadProcessId(s.Hwnd, out var pid);
                ownership.TryGetValue(s.Hwnd, out var o);
                return new JournalWindowEntry
                {
                    Hwnd = s.Hwnd.ToInt64(),
                    ProcessId = pid,
                    MonitorId = s.MonitorId,
                    Workspace = s.Workspace,
                    HiddenBySpaces4Win = o == VisibilityOwnership.HiddenBySpaces4Win,
                    Ownership = o
                };
            }).ToList()
        });
    }

    private void OnStateChangedForJournal(object? sender, EventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        WriteUncleanJournal();
        PersistLayout();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || _shutdownStarted || !IsRunning)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (_shutdownStarted || WorkspaceManager is null)
            {
                return;
            }

            try
            {
                WorkspaceManager.ReapplyVisibilityFromAssignments();
                IndicatorService?.RepairLayeredWindows();
            }
            catch
            {
                // best effort after resume
            }
        });
    }

    private void PersistLayout()
    {
        if (WorkspaceManager is null)
        {
            return;
        }

        try
        {
            LayoutStore.Write(WorkspaceManager.CaptureLayout());
        }
        catch
        {
            // best effort — next StateChanged retries
        }
    }

    private void OnElevationAssistanceNeeded(object? sender, ElevationAssistanceEventArgs e)
    {
        if (_elevationPromptSuppressedThisSession)
        {
            return;
        }

        if (Config.ElevationPreference == ElevationPreference.NeverAsk)
        {
            return;
        }

        if (ElevationService.IsCurrentProcessElevated())
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (_elevationPromptSuppressedThisSession)
            {
                return;
            }

            if (Config.ElevationPreference == ElevationPreference.AlwaysElevate)
            {
                RequestRestartElevated();
                return;
            }

            var loc = Localization;
            IndicatorService?.ShowElevationToast(
                e.MonitorId,
                loc?.Get("Toast.Elevation.Message") ?? "Admin window — Restart elevated",
                loc?.Get("Toast.Elevation.Restart") ?? "Restart elevated",
                loc?.Get("Toast.Elevation.Dismiss") ?? "Dismiss",
                () => RequestRestartElevated());
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch
        {
            // SystemEvents can throw if already torn down
        }

        BeginShutdown();

        if (WorkspaceManager is not null)
        {
            WorkspaceManager.ElevationAssistanceNeeded -= OnElevationAssistanceNeeded;
            WorkspaceManager.WorkspaceIdsChanged -= OnWorkspaceIdsChanged;
            WorkspaceManager.StateChanged -= OnStateChangedForJournal;
        }

        WindowEventService?.Dispose();
        ForeignWorkspaceActivation?.Dispose();
        HotkeyService?.Dispose();
        SwitcherController?.Dispose();
        HotkeyMonitorContext?.Dispose();
        IndicatorService?.Dispose();
        OverviewService?.Dispose();
        TransitionCoordinator?.Dispose();
        MonitorTracker.Dispose();
    }

    private void OnWorkspaceIdsChanged(object? sender, WorkspaceIdsChangedEventArgs e)
    {
        if (!Config.Monitors.TryGetValue(e.MonitorId, out var cfg))
        {
            cfg = new MonitorConfig();
            Config.Monitors[e.MonitorId] = cfg;
        }

        cfg.WorkspaceIds = e.WorkspaceIds.ToList();
        ConfigService.Save(Config);
    }
}
