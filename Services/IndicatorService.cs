using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// Per-monitor floating workspace dots. Click a dot to switch workspace.
/// Active pinned (sticky) windows are shown with a double ring on the active dot.
/// </summary>
public sealed class IndicatorService : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly MonitorTracker _monitorTracker;
    private readonly ConfigService _configService;
    private readonly Func<AppConfig> _config;
    private readonly Dictionary<string, WorkspaceIndicatorWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _compositionPrimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IndicatorToastWindow> _elevationToasts = new(StringComparer.OrdinalIgnoreCase);
    private readonly NativeMethods.WinEventDelegate _foregroundCallback;
    private IntPtr _foregroundHook;
    private bool _enabled;
    private bool _moveMode;
    private bool _disposed;
    private double _opacity = 0.88;
    private double _scale = 1.0;
    private bool _animations = true;
    private PinnedWindowIndicatorMode _pinnedMode = PinnedWindowIndicatorMode.RingOnly;
    private int _topmostDebounceVersion;
    private DateTimeOffset _lastElevationToastUtc = DateTimeOffset.MinValue;

    public string PinnedTooltip { get; set; } = "Fixado";

    /// <summary>Localized tooltip for workspace dots (id → label).</summary>
    public Func<int, string>? WorkspaceTooltipFormatter { get; set; }

    /// <summary>Optional animated workspace switch (indicator clicks).</summary>
    public Action<string, int>? WorkspaceSwitchAction { get; set; }

    /// <summary>Show the app tray context menu (Settings / Move / Exit).</summary>
    public Action<System.Windows.FrameworkElement>? AppContextMenuAction { get; set; }

    public bool IsMoveMode => _moveMode;

    public event EventHandler? MoveModeChanged;

    public IndicatorService(
        WorkspaceManager workspaceManager,
        MonitorTracker monitorTracker,
        ConfigService configService,
        Func<AppConfig> config)
    {
        _workspaceManager = workspaceManager;
        _monitorTracker = monitorTracker;
        _configService = configService;
        _config = config;
        _foregroundCallback = OnForegroundWinEvent;
        _workspaceManager.WorkspaceChanged += OnWorkspaceChanged;
        _workspaceManager.StateChanged += OnStateChanged;
        _monitorTracker.MonitorsChanged += OnMonitorsChanged;
    }

    public void ApplyVisualSettings(AppConfig config)
    {
        _opacity = config.IndicatorOpacity;
        _scale = config.IndicatorScale;
        _animations = config.IndicatorAnimations && !config.ReduceMotion;
        _pinnedMode = config.PinnedWindowIndicatorMode;
        foreach (var window in _windows.Values)
        {
            window.ApplyVisualSettings(_opacity, _scale, _animations, _pinnedMode);
            window.SetPinnedTooltipText(PinnedTooltip);
            window.WorkspaceLabelFormatter = WorkspaceTooltipFormatter;
        }
    }

    /// <summary>
    /// Recreate indicator HWNDs after WPF-UI theme/backdrop touches (keeps composition prime).
    /// </summary>
    public void RepairLayeredWindows()
    {
        if (_disposed || !_enabled)
        {
            return;
        }

        var moveMode = _moveMode;
        foreach (var id in _windows.Keys.ToList())
        {
            DestroyWindow(id);
        }

        RefreshAll();
        if (moveMode)
        {
            EnterMoveMode();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (_enabled)
        {
            EnsureForegroundHook();
            RefreshAll();
            RefreshStickyHints();
        }
        else
        {
            CloseAll();
            TearDownForegroundHook();
        }
    }

    public void EnterMoveMode()
    {
        if (!_enabled || _moveMode)
        {
            return;
        }

        _moveMode = true;
        foreach (var window in _windows.Values)
        {
            window.SetMoveMode(true);
        }

        MoveModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExitMoveMode(bool commit)
    {
        if (!_moveMode)
        {
            return;
        }

        _moveMode = false;
        foreach (var window in _windows.Values)
        {
            if (commit)
            {
                PersistPosition(window);
            }
            else
            {
                PlaceWindow(window);
            }

            window.SetMoveMode(false);
        }

        MoveModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMoveMode()
    {
        if (_moveMode)
        {
            ExitMoveMode(commit: true);
        }
        else
        {
            EnterMoveMode();
        }
    }

    public void ResetPositionsToDefault()
    {
        var config = _config();
        foreach (var monitor in _workspaceManager.Monitors)
        {
            if (!config.Monitors.TryGetValue(monitor.MonitorId, out var cfg))
            {
                cfg = new MonitorConfig();
                config.Monitors[monitor.MonitorId] = cfg;
            }

            cfg.IndicatorLeftDip = null;
            cfg.IndicatorTopDip = null;
        }

        _configService.Save(config);
        RefreshAll();
    }

    /// <summary>Briefly assert topmost on the indicator for the given monitor (focus-monitor hotkey feedback).</summary>
    public void PulseMonitor(string monitorId)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(monitorId))
        {
            return;
        }

        if (_windows.TryGetValue(monitorId, out var window))
        {
            window.EnsureTopmost();
        }
    }

    /// <summary>
    /// Non-modal elevation chip near the indicator. One toast per monitor; debounced globally.
    /// </summary>
    public void ShowElevationToast(
        string? monitorId,
        string message,
        string actionLabel,
        string dismissLabel,
        Action onRestart)
    {
        if (_disposed || !_enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if ((now - _lastElevationToastUtc).TotalSeconds < 2.5)
        {
            return;
        }

        var resolvedId = monitorId;
        if (string.IsNullOrWhiteSpace(resolvedId) || !_windows.ContainsKey(resolvedId))
        {
            resolvedId = _monitorTracker.FindMonitorUnderCursor()?.DeviceName
                         ?? _windows.Keys.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(resolvedId) || !_windows.TryGetValue(resolvedId, out var indicator))
        {
            return;
        }

        if (!indicator.IsVisible)
        {
            return;
        }

        if (_elevationToasts.TryGetValue(resolvedId, out var existing))
        {
            try { existing.Close(); } catch { /* ignore */ }
            _elevationToasts.Remove(resolvedId);
        }

        var monitor = _monitorTracker.FindByDeviceName(resolvedId);
        var bounds = monitor?.Bounds ?? System.Drawing.Rectangle.Empty;
        if (bounds.IsEmpty)
        {
            return;
        }

        _lastElevationToastUtc = now;
        var toast = new IndicatorToastWindow(message, actionLabel, dismissLabel);
        toast.RestartRequested += (_, _) => onRestart();
        toast.Closed += (_, _) => _elevationToasts.Remove(resolvedId);
        _elevationToasts[resolvedId] = toast;
        toast.ShowAnchoredTo(indicator, bounds);
    }

    public void RefreshAll()
    {
        if (!_enabled)
        {
            CloseAll();
            return;
        }

        var monitors = _workspaceManager.Monitors;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in monitors)
        {
            keep.Add(monitor.MonitorId);
            if (!_windows.ContainsKey(monitor.MonitorId))
            {
                CreateWindowForMonitor(monitor, primeComposition: true);
                continue;
            }

            var window = _windows[monitor.MonitorId];
            ConfigureWindow(window, monitor);
            if (!window.IsVisible)
            {
                window.Show();
            }
        }

        foreach (var id in _windows.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            DestroyWindow(id);
            _compositionPrimed.Remove(id);
        }

        EnsureAllTopmost();
        RefreshStickyHints();
    }

    private void CreateWindowForMonitor(MonitorWorkspace monitor, bool primeComposition)
    {
        var needsPrime = primeComposition && !_compositionPrimed.Contains(monitor.MonitorId);
        var window = BuildWindow(monitor.MonitorId);
        _windows[monitor.MonitorId] = window;

        if (needsPrime)
        {
            window.Opacity = 0;
            window.IsHitTestVisible = false;
            void OnPrimed(object? sender, EventArgs e)
            {
                window.ContentRendered -= OnPrimed;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    return;
                }

                _ = dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(50);
                    if (_disposed || !_enabled)
                    {
                        return;
                    }

                    _compositionPrimed.Add(monitor.MonitorId);
                    DestroyWindow(monitor.MonitorId);

                    var real = BuildWindow(monitor.MonitorId);
                    _windows[monitor.MonitorId] = real;
                    real.Show();
                    ConfigureWindow(real, monitor);
                    RefreshStickyHints();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            window.ContentRendered += OnPrimed;
            window.Show();
            ConfigureWindow(window, monitor);
            return;
        }

        window.Show();
        ConfigureWindow(window, monitor);
    }

    private WorkspaceIndicatorWindow BuildWindow(string monitorId)
    {
        var window = new WorkspaceIndicatorWindow(monitorId);
        window.WorkspaceSelected += OnWorkspaceSelected;
        window.PositionCommitted += OnPositionCommitted;
        window.MoveCancelled += OnMoveCancelled;
        window.ContextMenuRequested += OnContextMenuRequested;
        window.ScaleChanged += OnScaleChanged;
        // Never let an overlay become Application.MainWindow — theme Apply targets it.
        window.SourceInitialized += (_, _) =>
        {
            var app = System.Windows.Application.Current;
            if (app is not null && ReferenceEquals(app.MainWindow, window))
            {
                app.MainWindow = null;
            }
        };
        return window;
    }

    private void ConfigureWindow(WorkspaceIndicatorWindow window, MonitorWorkspace monitor)
    {
        window.ApplyVisualSettings(_opacity, _scale, _animations, _pinnedMode);
        window.SetPinnedTooltipText(PinnedTooltip);
        window.WorkspaceLabelFormatter = WorkspaceTooltipFormatter;
        window.UpdateDots(monitor.WorkspaceIds, monitor.ActiveWorkspace);
        PlaceWindow(window);
        window.SetMoveMode(_moveMode);
        window.EnsureTopmost();
    }

    private void DestroyWindow(string monitorId)
    {
        if (!_windows.TryGetValue(monitorId, out var window))
        {
            return;
        }

        window.WorkspaceSelected -= OnWorkspaceSelected;
        window.PositionCommitted -= OnPositionCommitted;
        window.MoveCancelled -= OnMoveCancelled;
        window.ContextMenuRequested -= OnContextMenuRequested;
        window.ScaleChanged -= OnScaleChanged;
        window.Close();
        _windows.Remove(monitorId);
    }

    private void OnContextMenuRequested(object? sender, EventArgs e)
    {
        if (sender is System.Windows.FrameworkElement target)
        {
            AppContextMenuAction?.Invoke(target);
        }
    }

    private void OnWorkspaceSelected(object? sender, int workspaceId)
    {
        if (sender is not WorkspaceIndicatorWindow window)
        {
            return;
        }

        if (WorkspaceSwitchAction is not null)
        {
            WorkspaceSwitchAction(window.MonitorId, workspaceId);
        }
        else
        {
            _workspaceManager.SwitchWorkspace(window.MonitorId, workspaceId);
        }
    }

    private void PlaceWindow(WorkspaceIndicatorWindow window)
    {
        var monitor = _workspaceManager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, window.MonitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            return;
        }

        var config = _config();
        double? left = null;
        double? top = null;
        if (config.Monitors.TryGetValue(monitor.MonitorId, out var cfg))
        {
            left = cfg.IndicatorLeftDip;
            top = cfg.IndicatorTopDip;
        }

        // Full monitor bounds (includes taskbar strip) so the indicator can be placed anywhere.
        window.PlaceInWorkArea(monitor.Bounds, left, top);
        window.EnsureInsideWorkArea();
    }

    private void PersistPosition(WorkspaceIndicatorWindow window)
    {
        var config = _config();
        if (!config.Monitors.TryGetValue(window.MonitorId, out var cfg))
        {
            cfg = new MonitorConfig();
            config.Monitors[window.MonitorId] = cfg;
        }

        cfg.IndicatorLeftDip = window.Left;
        cfg.IndicatorTopDip = window.Top;
        _configService.Save(config);
    }

    private void OnPositionCommitted(object? sender, EventArgs e)
    {
        if (sender is WorkspaceIndicatorWindow window)
        {
            PersistPosition(window);
        }
    }

    private void OnScaleChanged(object? sender, IndicatorScaleChangedEventArgs e)
    {
        _scale = e.Scale;
        foreach (var window in _windows.Values)
        {
            if (ReferenceEquals(window, sender))
            {
                continue;
            }

            window.ApplyScale(e.Scale);
        }

        if (!e.IsFinal)
        {
            return;
        }

        var config = _config();
        config.IndicatorScale = e.Scale;
        _configService.Save(config);
    }

    private void OnMoveCancelled(object? sender, EventArgs e) => ExitMoveMode(commit: false);

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e)
    {
        if (!_enabled)
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
            if (_windows.TryGetValue(e.MonitorId, out var window))
            {
                window.UpdateDots(e.WorkspaceIds, e.ActiveWorkspace);
                window.EnsureInsideWorkArea();
                RefreshStickyHints();
            }
            else
            {
                RefreshAll();
            }
        });
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_enabled)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        _ = dispatcher?.BeginInvoke(() =>
        {
            foreach (var monitor in _workspaceManager.Monitors)
            {
                if (_windows.TryGetValue(monitor.MonitorId, out var window))
                {
                    window.UpdateDots(monitor.WorkspaceIds, monitor.ActiveWorkspace);
                }
                else
                {
                    RefreshAll();
                    return;
                }
            }

            RefreshStickyHints();
        });
    }

    private void OnMonitorsChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        _ = dispatcher?.BeginInvoke(RefreshAll);
    }

    private void EnsureForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            return;
        }

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void TearDownForegroundHook()
    {
        if (_foregroundHook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
    }

    private void OnForegroundWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        _ = dispatcher?.BeginInvoke(() =>
        {
            RefreshStickyHints();
            ScheduleEnsureAllTopmost();
        });
    }

    /// <summary>
    /// Debounced reassert so rapid Alt+Tab / taskbar clicks do not spam SetWindowPos.
    /// </summary>
    private void ScheduleEnsureAllTopmost()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        var version = Interlocked.Increment(ref _topmostDebounceVersion);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(150);
            if (version != _topmostDebounceVersion || _disposed || !_enabled)
            {
                return;
            }

            EnsureAllTopmost();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void EnsureAllTopmost()
    {
        foreach (var window in _windows.Values)
        {
            window.EnsureTopmost();
        }
    }

    private void RefreshStickyHints()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        var fg = NativeMethods.GetForegroundWindow();
        foreach (var monitor in _workspaceManager.Monitors)
        {
            if (!_windows.TryGetValue(monitor.MonitorId, out var window))
            {
                continue;
            }

            var pinned = fg != IntPtr.Zero && monitor.IsSticky(fg);
            window.SetActivePinned(pinned);
        }
    }

    private void CloseAll()
    {
        foreach (var toast in _elevationToasts.Values.ToList())
        {
            try { toast.Close(); } catch { /* ignore */ }
        }

        _elevationToasts.Clear();

        foreach (var id in _windows.Keys.ToList())
        {
            DestroyWindow(id);
        }

        _windows.Clear();
        _compositionPrimed.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspaceManager.WorkspaceChanged -= OnWorkspaceChanged;
        _workspaceManager.StateChanged -= OnStateChanged;
        _monitorTracker.MonitorsChanged -= OnMonitorsChanged;
        TearDownForegroundHook();
        CloseAll();
        GC.KeepAlive(_foregroundCallback);
    }
}
