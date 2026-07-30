using System.Drawing;
using Spaces4Win.Config;
using Spaces4Win.Native;
using Spaces4Win.Overview;
using Spaces4Win.Services;

namespace Spaces4Win.Core;

/// <summary>
/// Coordinates independent per-monitor workspaces by showing/hiding window HWNDs.
/// </summary>
public sealed class WorkspaceManager
{
    private readonly object _sync = new();
    private readonly MonitorTracker _monitorTracker;
    private readonly Func<string, IReadOnlyList<int>> _workspaceIdsResolver;
    private readonly Func<ElevationPreference> _elevationPreference;
    private readonly WindowVisibilityService _visibility;
    private readonly WindowCaptureCache? _captureCache;
    private readonly Func<bool> _enforceSingleFullscreen;
    private readonly WindowFullscreenService _fullscreen = new();
    private HotkeyMonitorContext? _hotkeyMonitorContext;
    private bool _commandsEnabled = true;
    /// <summary>HWND currently in interactive Win32 move/size (title-bar drag / resize).</summary>
    private IntPtr _moveSizeHwnd;

    public WorkspaceManager(
        MonitorTracker monitorTracker,
        WindowVisibilityService visibility,
        Func<string, IReadOnlyList<int>>? workspaceIdsResolver = null,
        Func<ElevationPreference>? elevationPreference = null,
        WindowCaptureCache? captureCache = null,
        Func<bool>? enforceSingleFullscreen = null)
    {
        _monitorTracker = monitorTracker;
        _visibility = visibility;
        _captureCache = captureCache;
        _enforceSingleFullscreen = enforceSingleFullscreen ?? (() => true);
        _workspaceIdsResolver = workspaceIdsResolver ?? (_ => new[] { 1 });
        _elevationPreference = elevationPreference ?? (() => ElevationPreference.Ask);
        _monitorTracker.MonitorsChanged += OnMonitorsChanged;
        RebuildFromTracker();
    }

    /// <summary>Optional: cursor-vs-focus targeting for workspace hotkeys.</summary>
    public void AttachHotkeyMonitorContext(HotkeyMonitorContext context) =>
        _hotkeyMonitorContext = context;

    /// <summary>Background capture cache for overview thumbnails. May be null in tests.</summary>
    public WindowCaptureCache? CaptureCache => _captureCache;

    public WindowVisibilityService Visibility => _visibility;

    public event EventHandler? StateChanged;
    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;
    public event EventHandler<ElevationAssistanceEventArgs>? ElevationAssistanceNeeded;
    public event EventHandler<WorkspaceIdsChangedEventArgs>? WorkspaceIdsChanged;

    public bool CommandsEnabled
    {
        get => _commandsEnabled;
        set => _commandsEnabled = value;
    }

    public WindowFullscreenService Fullscreen => _fullscreen;

    public void ExitAllFullscreen() => _fullscreen.ExitAllToMaximized();

    public IReadOnlyList<MonitorWorkspace> Monitors
    {
        get
        {
            lock (_sync)
            {
                return _monitors.Values.ToList();
            }
        }
    }

    private readonly Dictionary<string, MonitorWorkspace> _monitors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Admits currently open top-level windows. When <paramref name="layout"/> is provided,
    /// remaps matching windows to their last workspace (never launches missing apps).
    /// </summary>
    public void InitializeExistingWindows(WindowLayoutDocument? layout = null)
    {
        var open = WindowClassifier.EnumerateTopLevelWindows()
            .Where(h => NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h))
            .ToList();

        var identities = open.Select(ProcessPathHelper.CaptureIdentity).ToList();
        var matches = layout is null
            ? new Dictionary<IntPtr, WindowLayoutMatcher.LayoutMatch>()
            : WindowLayoutMatcher.Match(layout, identities);

        if (layout is not null)
        {
            EnsureWorkspacesFromLayout(layout);
        }

        foreach (var hwnd in open)
        {
            var monitorInfo = _monitorTracker.FindMonitorForWindow(hwnd);
            if (monitorInfo is null)
            {
                continue;
            }

            if (matches.TryGetValue(hwnd, out var match))
            {
                RestoreMatchedWindow(hwnd, monitorInfo.DeviceName, match);
                if (match.Entry.IsFullscreen)
                {
                    _fullscreen.MarkDesired(hwnd, desired: true);
                }
            }
            else
            {
                AssignWindowToWorkspace(
                    hwnd,
                    monitorInfo.DeviceName,
                    GetActiveWorkspace(monitorInfo.DeviceName),
                    applyVisibility: false);
            }
        }

        if (layout is not null)
        {
            RestoreActiveWorkspaces(layout);
        }

        ApplyVisibilityFromAssignments();
        EnsureDesiredFullscreenOnActiveWorkspaces();

        foreach (var monitorId in Monitors.Select(m => m.MonitorId).ToList())
        {
            PruneEmptyInactiveWorkspaces(monitorId);
        }

        RaiseStateChanged();
    }

    /// <summary>Snapshot for durable restore across Spaces4Win restarts (open apps only).</summary>
    public WindowLayoutDocument CaptureLayout()
    {
        var doc = new WindowLayoutDocument();
        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                var entry = new LayoutMonitorEntry
                {
                    MonitorId = monitor.MonitorId,
                    ActiveWorkspace = monitor.ActiveWorkspace,
                    LastActiveWorkspace = monitor.LastActiveWorkspace
                };

                foreach (var (ws, set) in monitor.Workspaces)
                {
                    foreach (var hwnd in set)
                    {
                        var handle = (IntPtr)hwnd;
                        if (!NativeMethods.IsWindow(handle))
                        {
                            continue;
                        }

                        var id = ProcessPathHelper.CaptureIdentity(handle);
                        entry.Windows.Add(new LayoutWindowEntry
                        {
                            Hwnd = id.Hwnd.ToInt64(),
                            ProcessId = id.ProcessId,
                            ProcessPath = id.ProcessPath,
                            Title = id.Title,
                            ClassName = id.ClassName,
                            Workspace = ws,
                            // Capture before shutdown reveal — reflects real user minimize intent.
                            IsMinimized = NativeMethods.IsIconic(handle),
                            IsFullscreen = _fullscreen.ShouldPersistFullscreen(handle) ||
                                           WindowFullscreenService.IsGeometricFullscreen(handle, monitor.Bounds)
                        });
                    }
                }

                doc.Monitors.Add(entry);
            }
        }

        return doc;
    }

    private void EnsureWorkspacesFromLayout(WindowLayoutDocument layout)
    {
        lock (_sync)
        {
            foreach (var mon in layout.Monitors)
            {
                if (!_monitors.TryGetValue(mon.MonitorId, out var monitor))
                {
                    continue;
                }

                foreach (var window in mon.Windows)
                {
                    if (window.Workspace is >= 1 and <= MonitorWorkspace.MaxWorkspaceId)
                    {
                        monitor.EnsureWorkspace(window.Workspace);
                    }
                }

                if (mon.ActiveWorkspace is >= 1 and <= MonitorWorkspace.MaxWorkspaceId)
                {
                    monitor.EnsureWorkspace(mon.ActiveWorkspace);
                }
            }
        }
    }

    private void RestoreMatchedWindow(IntPtr hwnd, string currentMonitorId, WindowLayoutMatcher.LayoutMatch match)
    {
        var monitorId = currentMonitorId;
        var workspace = match.Entry.Workspace is >= 1 and <= MonitorWorkspace.MaxWorkspaceId
            ? match.Entry.Workspace
            : GetActiveWorkspace(monitorId);

        lock (_sync)
        {
            if (!_monitors.ContainsKey(monitorId) &&
                _monitors.ContainsKey(match.PreferredMonitorId))
            {
                monitorId = match.PreferredMonitorId;
            }

            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            foreach (var other in _monitors.Values)
            {
                other.RemoveWindow(hwnd);
            }

            monitor.EnsureWorkspace(workspace);
            monitor.AssignWindow(hwnd, workspace);
        }

        // Shutdown may have revealed inactive windows as minimized; only keep
        // minimize if the layout recorded real user minimize intent.
        _visibility.TrackForLayoutRestore(hwnd, match.Entry.IsMinimized);
    }

    private void RestoreActiveWorkspaces(WindowLayoutDocument layout)
    {
        lock (_sync)
        {
            foreach (var mon in layout.Monitors)
            {
                if (!_monitors.TryGetValue(mon.MonitorId, out var monitor))
                {
                    continue;
                }

                if (monitor.HasWorkspace(mon.ActiveWorkspace))
                {
                    // SetActiveWorkspace also updates LastActive; seed LastActive first when valid.
                    if (monitor.HasWorkspace(mon.LastActiveWorkspace) &&
                        mon.LastActiveWorkspace != mon.ActiveWorkspace)
                    {
                        monitor.SetActiveWorkspace(mon.LastActiveWorkspace);
                    }

                    monitor.SetActiveWorkspace(mon.ActiveWorkspace);
                }
            }
        }
    }

    /// <summary>
    /// Re-apply hide/show after sleep/resume or GPU reset when DWM cloak state may have leaked.
    /// </summary>
    public void ReapplyVisibilityFromAssignments() => ApplyVisibilityFromAssignments();

    private void ApplyVisibilityFromAssignments()
    {
        List<(IntPtr Hwnd, bool Show)> actions;
        lock (_sync)
        {
            actions = new List<(IntPtr, bool)>();
            foreach (var monitor in _monitors.Values)
            {
                foreach (var (ws, set) in monitor.Workspaces)
                {
                    var show = ws == monitor.ActiveWorkspace;
                    foreach (var hwnd in set)
                    {
                        actions.Add(((IntPtr)hwnd, show));
                    }
                }
            }
        }

        foreach (var (hwnd, show) in actions)
        {
            if (!NativeMethods.IsWindow(hwnd) || !WindowClassifier.IsManagedWindow(hwnd))
            {
                continue;
            }

            if (show)
            {
                _visibility.ShowForWorkspace(hwnd);
            }
            else
            {
                _visibility.HideForWorkspace(hwnd);
            }
        }
    }

    /// <summary>
    /// Switches to workspace N, creating it sparsely if missing (no intermediate ids).
    /// </summary>
    public void SwitchOrCreateWorkspace(string monitorId, int workspaceNumber)
    {
        if (!_commandsEnabled || workspaceNumber is < 1 or > MonitorWorkspace.MaxWorkspaceId)
        {
            return;
        }

        bool created;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            created = monitor.EnsureWorkspace(workspaceNumber);
        }

        if (created)
        {
            PersistIds(monitorId);
        }

        SwitchWorkspace(monitorId, workspaceNumber);
    }

    /// <summary>
    /// Finds the previous (-1) or next (+1) existing workspace id on the monitor.
    /// Returns null when none exists in that direction (no wrap, no create).
    /// </summary>
    public int? ResolveAdjacentExistingWorkspace(string monitorId, int direction)
    {
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return null;
            }

            return MonitorWorkspace.ResolveAdjacentExisting(
                monitor.WorkspaceIds,
                monitor.ActiveWorkspace,
                direction);
        }
    }

    /// <summary>Creates a sparse workspace id without switching to it.</summary>
    public bool EnsureWorkspaceExists(string monitorId, int workspaceNumber)
    {
        if (!_commandsEnabled || workspaceNumber is < 1 or > MonitorWorkspace.MaxWorkspaceId)
        {
            return false;
        }

        bool created;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return false;
            }

            created = monitor.EnsureWorkspace(workspaceNumber);
        }

        if (created)
        {
            PersistIds(monitorId);
        }

        return created;
    }

    public void SwitchWorkspace(string monitorId, int workspaceNumber)
    {
        if (!_commandsEnabled)
        {
            return;
        }

        // Resolve before taking the manager lock — may call into Win32.
        var dragFollow = ResolveDragFollowHwnd();

        MonitorWorkspace? monitor;
        HashSet<nint>? hide = null;
        HashSet<nint>? show = null;
        HashSet<nint> sticky = new();
        var previousWorkspace = 0;
        var didSwitch = false;
        IntPtr followedHwnd = IntPtr.Zero;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out monitor))
            {
                return;
            }

            if (!monitor.HasWorkspace(workspaceNumber))
            {
                return;
            }

            if (monitor.ActiveWorkspace == workspaceNumber)
            {
                // Still drop other empty inactive workspaces below.
            }
            else
            {
                previousWorkspace = monitor.ActiveWorkspace;
                hide = monitor.Workspaces[previousWorkspace].Where(h => !monitor.IsSticky(h)).ToHashSet();
                show = monitor.Workspaces[workspaceNumber].Where(h => !monitor.IsSticky(h)).ToHashSet();
                sticky = monitor.StickyWindows.ToHashSet();
                RememberOutgoingFocus(monitor, previousWorkspace, hide, sticky);

                // While the user is dragging a window on this monitor, bring it along
                // to the destination workspace instead of cloaking it mid-drag.
                if (dragFollow != IntPtr.Zero &&
                    hide.Contains(dragFollow) &&
                    WindowClassifier.IsManagedWindow(dragFollow))
                {
                    monitor.RemoveWindow(dragFollow);
                    monitor.AssignWindow(dragFollow, workspaceNumber);
                    hide.Remove(dragFollow);
                    show.Add(dragFollow);
                    followedHwnd = dragFollow;
                }

                monitor.SetActiveWorkspace(workspaceNumber);
                didSwitch = true;
            }
        }

        if (didSwitch)
        {
            // Hold internal transition across the whole batch + deferred FOREGROUND
            // callbacks (Normal priority) so Caps-held foreign-activation cannot pull
            // windows onto the destination mid Caps+1/2/3 sliding.
            _visibility.BeginInternalOperation();
            try
            {
                if (followedHwnd != IntPtr.Zero)
                {
                    _visibility.TrackAsVisible(followedHwnd);
                }

                foreach (var hwnd in hide!)
                {
                    _visibility.HideForWorkspace((IntPtr)hwnd);
                }

                foreach (var hwnd in show!)
                {
                    var handle = (IntPtr)hwnd;
                    if (followedHwnd != IntPtr.Zero && handle == followedHwnd)
                    {
                        continue;
                    }

                    if (NativeMethods.IsWindow(handle) && WindowClassifier.IsManagedWindow(handle))
                    {
                        _visibility.ShowForWorkspace(handle);
                    }
                    else
                    {
                        RemoveWindow(handle);
                    }
                }

                EnsureDesiredFullscreenForHandles(show!, monitorId);

                if (followedHwnd != IntPtr.Zero)
                {
                    // Keep the drag alive — do not steal foreground from the dragged window.
                    lock (_sync)
                    {
                        if (_monitors.TryGetValue(monitorId, out var m))
                        {
                            m.RememberFocusedWindow(workspaceNumber, followedHwnd);
                        }
                    }
                }
                else
                {
                    ActivateTopWindowForWorkspace(monitorId, workspaceNumber, show!, sticky);
                }
            }
            finally
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    _visibility.EndInternalOperation();
                }
                else
                {
                    _ = dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        () => _visibility.EndInternalOperation());
                }
            }
        }

        // Leaving an empty workspace drops it from the list (keep at least one).
        var pruned = PruneEmptyInactiveWorkspaces(monitorId);
        if (didSwitch || pruned)
        {
            RaiseWorkspaceChanged(monitorId);
            RaiseStateChanged();
        }
    }

    public void NotifyMoveSizeStarted(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        // Prefer root window for owned dialogs dragged via parent chrome.
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var candidate = root != IntPtr.Zero && WindowClassifier.IsManagedWindow(root)
            ? root
            : hwnd;
        if (!WindowClassifier.IsManagedWindow(candidate))
        {
            return;
        }

        _moveSizeHwnd = candidate;
    }

    public void NotifyMoveSizeEnded(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (_moveSizeHwnd == hwnd ||
            (root != IntPtr.Zero && _moveSizeHwnd == root))
        {
            _moveSizeHwnd = IntPtr.Zero;
        }
    }

    /// <summary>
    /// HWND the user is interactively moving/sizing, if any (tracked + Win32 fallback).
    /// </summary>
    private IntPtr ResolveDragFollowHwnd()
    {
        if (_moveSizeHwnd != IntPtr.Zero &&
            NativeMethods.IsWindow(_moveSizeHwnd) &&
            WindowClassifier.IsManagedWindow(_moveSizeHwnd))
        {
            return _moveSizeHwnd;
        }

        var live = NativeMethods.TryGetWindowInMoveSize();
        if (live == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = NativeMethods.GetAncestor(live, NativeMethods.GA_ROOT);
        var candidate = root != IntPtr.Zero && WindowClassifier.IsManagedWindow(root)
            ? root
            : live;

        return WindowClassifier.IsManagedWindow(candidate) ? candidate : IntPtr.Zero;
    }

    public bool DeleteCurrentWorkspace(string monitorId)
    {
        if (!_commandsEnabled)
        {
            return false;
        }

        HashSet<nint> show;
        HashSet<nint> sticky;
        int fallbackWs;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return false;
            }

            if (monitor.WorkspaceCount <= 1)
            {
                return false;
            }

            var deleted = monitor.ActiveWorkspace;
            if (!monitor.TryRemoveWorkspace(deleted, out var fallback))
            {
                return false;
            }

            fallbackWs = fallback;
            show = monitor.Workspaces[fallback].Where(h => !monitor.IsSticky(h)).ToHashSet();
            sticky = monitor.StickyWindows.ToHashSet();
        }

        foreach (var hwnd in show)
        {
            var handle = (IntPtr)hwnd;
            if (NativeMethods.IsWindow(handle) && WindowClassifier.IsManagedWindow(handle))
            {
                _visibility.ShowForWorkspace(handle);
            }
            else
            {
                RemoveWindow(handle);
            }
        }

        ActivateTopWindowForWorkspace(monitorId, fallbackWs, show, sticky);

        PersistIds(monitorId);
        RaiseWorkspaceChanged(monitorId);
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Densify sparse workspace ids on <paramref name="monitorId"/> to consecutive 1..N.
    /// Returns false if the monitor is missing or already dense.
    /// </summary>
    public bool CompactWorkspaces(string monitorId)
    {
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return false;
        }

        int previousActive;
        int newActive;
        bool changed;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return false;
            }

            previousActive = monitor.ActiveWorkspace;
            if (!monitor.TryCompactWorkspaceIds(out changed) || !changed)
            {
                return false;
            }

            newActive = monitor.ActiveWorkspace;
        }

        if (previousActive != newActive)
        {
            ApplyVisibilityFromAssignmentsForMonitor(monitorId);
        }

        PersistIds(monitorId);
        RaiseWorkspaceChanged(monitorId);
        RaiseStateChanged();
        return true;
    }

    public void SwitchToLastWorkspace(string monitorId)
    {
        if (!_commandsEnabled)
        {
            return;
        }

        int target;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            target = monitor.LastActiveWorkspace;
            if (!monitor.HasWorkspace(target) || target == monitor.ActiveWorkspace)
            {
                return;
            }
        }

        SwitchWorkspace(monitorId, target);
    }

    public bool? ToggleStickyForActiveWindow()
    {
        if (!_commandsEnabled)
        {
            return null;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return null;
        }

        var monitorInfo = _monitorTracker.FindMonitorForWindow(hwnd)
                          ?? _monitorTracker.FindMonitorUnderCursor();
        if (monitorInfo is null)
        {
            return null;
        }

        bool nowSticky;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorInfo.DeviceName, out var monitor))
            {
                return null;
            }

            if (monitor.StickyWindows.Contains(hwnd))
            {
                monitor.StickyWindows.Remove(hwnd);
                monitor.AssignWindow(hwnd, monitor.ActiveWorkspace);
                nowSticky = false;
            }
            else
            {
                foreach (var other in _monitors.Values)
                {
                    other.RemoveWindow(hwnd);
                    other.StickyWindows.Remove(hwnd);
                }

                monitor.StickyWindows.Add(hwnd);
                nowSticky = true;
            }
        }

        if (nowSticky)
        {
            _visibility.ShowForWorkspace(hwnd);
        }

        RaiseStateChanged();
        return nowSticky;
    }

    /// <summary>
    /// Caps+F: toggle borderless fullscreen on the focused window in place
    /// (does not move workspaces). Second press exits to maximized.
    /// Returns true when entered, false when exited, null on failure.
    /// </summary>
    public bool? ToggleFullscreenInPlace()
    {
        if (!_commandsEnabled)
        {
            return null;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return null;
        }

        var monitorInfo = ResolveHotkeyMonitor()
                          ?? _monitorTracker.FindMonitorForWindow(hwnd);
        if (monitorInfo is null)
        {
            return null;
        }

        if (!ElevationService.IsCurrentProcessElevated() && ElevationService.IsWindowElevated(hwnd))
        {
            HandleElevatedWindowBlocked(monitorInfo.DeviceName);
            if (_elevationPreference() == ElevationPreference.NeverAsk)
            {
                return null;
            }
        }

        var monitorId = monitorInfo.DeviceName;
        Rectangle bounds;
        bool alreadyFullscreen;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return null;
            }

            bounds = monitor.Bounds;
            alreadyFullscreen = _fullscreen.IsManagedFullscreen(hwnd) ||
                                _fullscreen.IsDesired(hwnd) ||
                                WindowFullscreenService.IsGeometricFullscreen(hwnd, bounds);
        }

        if (alreadyFullscreen)
        {
            _fullscreen.TryExitToMaximized(hwnd);
            RaiseStateChanged();
            return false;
        }

        ActivateManagedWindow(hwnd);
        if (_enforceSingleFullscreen())
        {
            ExitOtherFullscreenInWorkspace(monitorId, GetActiveWorkspace(monitorId), exceptHwnd: hwnd);
        }

        _fullscreen.TryEnter(hwnd, bounds);
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Caps+Shift+F / Caps+Ctrl+F: move focused window to a free/empty dedicated workspace.
    /// When <paramref name="follow"/> is true, switch to that workspace; otherwise stay.
    /// </summary>
    public ShiftWindowResult MoveActiveWindowToFreeWorkspace(bool follow)
    {
        if (!_commandsEnabled)
        {
            return ShiftWindowResult.Failed;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return ShiftWindowResult.NoWindow;
        }

        var monitorInfo = ResolveHotkeyMonitor()
                          ?? _monitorTracker.FindMonitorForWindow(hwnd);
        if (monitorInfo is null)
        {
            return ShiftWindowResult.Failed;
        }

        if (!ElevationService.IsCurrentProcessElevated() && ElevationService.IsWindowElevated(hwnd))
        {
            HandleElevatedWindowBlocked(monitorInfo.DeviceName);
            if (_elevationPreference() == ElevationPreference.NeverAsk)
            {
                return ShiftWindowResult.Failed;
            }
        }

        var monitorId = monitorInfo.DeviceName;
        int targetWorkspace;
        int activeWorkspace;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return ShiftWindowResult.Failed;
            }

            activeWorkspace = monitor.ActiveWorkspace;
            targetWorkspace = FullscreenWorkspacePolicy.ResolveFullscreenTargetWorkspace(
                monitor.WorkspaceIds,
                id => monitor.Workspaces.TryGetValue(id, out var set) ? set.Count : 0,
                id => WorkspaceHasFullscreenWindow(monitor, id));

            if (targetWorkspace == activeWorkspace &&
                monitor.Workspaces.TryGetValue(activeWorkspace, out var activeSet) &&
                activeSet.Count <= 1 &&
                activeSet.Contains(hwnd))
            {
                // Already alone on the only sensible target — nothing to do.
                return ShiftWindowResult.NoAdjacent;
            }

            if (monitor.WorkspaceCount >= MonitorWorkspace.MaxWorkspaceId &&
                !monitor.HasWorkspace(targetWorkspace))
            {
                return ShiftWindowResult.AtLimit;
            }

            monitor.EnsureWorkspace(targetWorkspace);
        }

        AssignWindowToWorkspace(hwnd, monitorId, targetWorkspace, applyVisibility: !follow);

        if (follow && targetWorkspace != activeWorkspace)
        {
            SwitchWorkspace(monitorId, targetWorkspace);
            ActivateManagedWindow(hwnd);
        }

        PersistIds(monitorId);
        RaiseWorkspaceChanged(monitorId);
        RaiseStateChanged();
        return ShiftWindowResult.Ok;
    }

    /// <summary>Obsolete name — use <see cref="ToggleFullscreenInPlace"/>.</summary>
    public bool? ToggleFullscreenDedicatedWorkspace() => ToggleFullscreenInPlace();

    private bool WorkspaceHasFullscreenWindow(MonitorWorkspace monitor, int workspaceId)
    {
        if (!monitor.Workspaces.TryGetValue(workspaceId, out var set))
        {
            return false;
        }

        foreach (var h in set)
        {
            var handle = (IntPtr)h;
            if (_fullscreen.IsDesired(handle) ||
                _fullscreen.IsManagedFullscreen(handle) ||
                WindowFullscreenService.IsGeometricFullscreen(handle, monitor.Bounds))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureDesiredFullscreenOnActiveWorkspaces()
    {
        List<(IntPtr Hwnd, Rectangle Bounds)> targets;
        lock (_sync)
        {
            targets = new List<(IntPtr, Rectangle)>();
            foreach (var monitor in _monitors.Values)
            {
                if (!monitor.Workspaces.TryGetValue(monitor.ActiveWorkspace, out var set))
                {
                    continue;
                }

                foreach (var h in set)
                {
                    targets.Add(((IntPtr)h, monitor.Bounds));
                }

                foreach (var h in monitor.StickyWindows)
                {
                    targets.Add(((IntPtr)h, monitor.Bounds));
                }
            }
        }

        foreach (var (hwnd, bounds) in targets)
        {
            _fullscreen.EnsureDesired(hwnd, bounds);
        }
    }

    private void EnsureDesiredFullscreenForHandles(IEnumerable<nint> handles, string monitorId)
    {
        Rectangle bounds;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            bounds = monitor.Bounds;
        }

        foreach (var h in handles)
        {
            _fullscreen.EnsureDesired((IntPtr)h, bounds);
        }
    }

    public enum ShiftWindowResult
    {
        Ok,
        NoWindow,
        AtLimit,
        NoAdjacent,
        Failed
    }

    /// <summary>
    /// Caps+Alt / Caps+Ctrl+Alt+←/→: extract focused window into a new workspace
    /// inserted left/right, renumber densely. When <paramref name="follow"/> is true,
    /// stay on the inserted workspace; otherwise remain on the former workspace.
    /// </summary>
    public ShiftWindowResult ShiftActiveWindowToNewWorkspace(int direction, bool follow = true)
    {
        if (!_commandsEnabled || direction == 0)
        {
            return ShiftWindowResult.Failed;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return ShiftWindowResult.NoWindow;
        }

        var monitorInfo = ResolveHotkeyMonitor()
                          ?? _monitorTracker.FindMonitorForWindow(hwnd);
        if (monitorInfo is null)
        {
            return ShiftWindowResult.Failed;
        }

        if (!ElevationService.IsCurrentProcessElevated() && ElevationService.IsWindowElevated(hwnd))
        {
            HandleElevatedWindowBlocked(monitorInfo.DeviceName);
            if (_elevationPreference() == ElevationPreference.NeverAsk)
            {
                return ShiftWindowResult.Failed;
            }
        }

        HashSet<nint> hide;
        HashSet<nint> show;
        HashSet<nint> sticky;
        int viewId;
        var monitorId = monitorInfo.DeviceName;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return ShiftWindowResult.Failed;
            }

            var remainingOnSource = monitor.FindWorkspaceOf(hwnd) == monitor.ActiveWorkspace
                ? monitor.Workspaces[monitor.ActiveWorkspace].Count - (monitor.Workspaces[monitor.ActiveWorkspace].Contains(hwnd) ? 1 : 0)
                : -1;
            if (remainingOnSource > 0 && monitor.WorkspaceCount >= MonitorWorkspace.MaxWorkspaceId)
            {
                return ShiftWindowResult.AtLimit;
            }

            if (!monitor.TryInsertWorkspaceWithWindow(hwnd, direction, out var insertedId))
            {
                return ShiftWindowResult.Failed;
            }

            if (!follow &&
                monitor.HasWorkspace(monitor.LastActiveWorkspace) &&
                monitor.LastActiveWorkspace != insertedId)
            {
                monitor.SetActiveWorkspace(monitor.LastActiveWorkspace);
            }

            viewId = monitor.ActiveWorkspace;
            hide = monitor.Workspaces
                .Where(kv => kv.Key != viewId)
                .SelectMany(kv => kv.Value)
                .ToHashSet();
            show = monitor.Workspaces[viewId].ToHashSet();
            sticky = monitor.StickyWindows.ToHashSet();
        }

        foreach (var h in hide)
        {
            _visibility.HideForWorkspace((IntPtr)h);
        }

        foreach (var h in show)
        {
            var handle = (IntPtr)h;
            if (NativeMethods.IsWindow(handle) && WindowClassifier.IsManagedWindow(handle))
            {
                _visibility.ShowForWorkspace(handle);
            }
        }

        if (follow)
        {
            ActivateManagedWindow(hwnd);
        }
        else
        {
            ActivateTopWindowForWorkspace(monitorId, viewId, show, sticky);
        }

        PersistIds(monitorId);
        RaiseWorkspaceChanged(monitorId);
        RaiseStateChanged();
        return ShiftWindowResult.Ok;
    }

    /// <summary>
    /// Caps+Shift / Caps+Ctrl+←/→: move focused window to the adjacent existing workspace.
    /// When there is no neighbor: no-op if <paramref name="createAtExtreme"/> is false;
    /// otherwise insert a new workspace in that direction and follow.
    /// </summary>
    public ShiftWindowResult MoveActiveWindowToAdjacentWorkspace(
        int direction,
        bool follow,
        bool createAtExtreme)
    {
        if (!_commandsEnabled || direction == 0)
        {
            return ShiftWindowResult.Failed;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return ShiftWindowResult.NoWindow;
        }

        var monitorInfo = ResolveHotkeyMonitor()
                          ?? _monitorTracker.FindMonitorForWindow(hwnd);
        if (monitorInfo is null)
        {
            return ShiftWindowResult.Failed;
        }

        var monitorId = monitorInfo.DeviceName;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out _))
            {
                return ShiftWindowResult.Failed;
            }
        }

        var adjacent = ResolveAdjacentExistingWorkspace(monitorId, direction);
        if (adjacent is null)
        {
            if (!createAtExtreme)
            {
                return ShiftWindowResult.NoAdjacent;
            }

            // At the edge: create by inserting (always follow when creating at extreme).
            return ShiftActiveWindowToNewWorkspace(direction, follow: true);
        }

        if (ElevationService.IsCurrentProcessElevated() == false &&
            ElevationService.IsWindowElevated(hwnd))
        {
            HandleElevatedWindowBlocked(monitorId);
            if (_elevationPreference() == ElevationPreference.NeverAsk)
            {
                return ShiftWindowResult.Failed;
            }
        }

        var moved = MoveActiveWindowToWorkspace(
            adjacent.Value,
            activateRemainingOnCurrent: !follow,
            applyVisibility: !follow);
        if (moved is null)
        {
            return ShiftWindowResult.Failed;
        }

        if (follow)
        {
            SwitchWorkspace(monitorId, adjacent.Value);
            ActivateManagedWindow(hwnd);
        }

        return ShiftWindowResult.Ok;
    }

    /// <summary>
    /// Overview drag-drop: move <paramref name="hwnd"/> to an existing workspace or into a
    /// newly created sparse id near the drop gap. Relocates the HWND when the target monitor differs.
    /// </summary>
    public OverviewMoveResult MoveWindowFromOverview(
        IntPtr hwnd,
        string targetMonitorId,
        int? existingWorkspaceId,
        int? gapAboveWorkspaceId,
        int? gapBelowWorkspaceId)
    {
        if (!_commandsEnabled || hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return OverviewMoveResult.NoWindow;
        }

        if (string.IsNullOrWhiteSpace(targetMonitorId))
        {
            return OverviewMoveResult.Failed;
        }

        if (!ElevationService.IsCurrentProcessElevated() && ElevationService.IsWindowElevated(hwnd))
        {
            HandleElevatedWindowBlocked(targetMonitorId);
            if (_elevationPreference() == ElevationPreference.NeverAsk)
            {
                return OverviewMoveResult.Failed;
            }
        }

        int workspaceId;
        var denseInsert = false;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(targetMonitorId, out var monitor))
            {
                return OverviewMoveResult.Failed;
            }

            if (existingWorkspaceId is int existing)
            {
                if (existing is < 1 or > MonitorWorkspace.MaxWorkspaceId)
                {
                    return OverviewMoveResult.Failed;
                }

                monitor.EnsureWorkspace(existing);
                workspaceId = existing;
            }
            else if (OverviewWorkspaceAllocator.NeedsDenseInsert(
                         monitor.WorkspaceIds,
                         gapAboveWorkspaceId,
                         gapBelowWorkspaceId))
            {
                denseInsert = true;
                // Relocate first when cross-monitor; insert runs after relocate below.
                workspaceId = 0;
            }
            else
            {
                var created = OverviewWorkspaceAllocator.ResolveNewWorkspaceId(
                    monitor.WorkspaceIds,
                    gapAboveWorkspaceId,
                    gapBelowWorkspaceId);
                if (created is null)
                {
                    return OverviewMoveResult.AtLimit;
                }

                monitor.EnsureWorkspace(created.Value);
                workspaceId = created.Value;
            }
        }

        RelocateWindowToMonitorIfNeeded(hwnd, targetMonitorId);

        if (denseInsert)
        {
            lock (_sync)
            {
                if (!_monitors.TryGetValue(targetMonitorId, out var monitor))
                {
                    return OverviewMoveResult.Failed;
                }

                // Window may still be tracked on another monitor — detach first.
                foreach (var other in _monitors.Values)
                {
                    other.RemoveWindow(hwnd);
                }

                if (!monitor.TryInsertWorkspaceBetweenWithWindow(
                        hwnd,
                        gapAboveWorkspaceId,
                        gapBelowWorkspaceId,
                        out workspaceId))
                {
                    return OverviewMoveResult.AtLimit;
                }
            }

            _visibility.TrackAsVisible(hwnd);
            // Apply visibility for the (possibly unchanged) active workspace.
            ApplyVisibilityFromAssignmentsForMonitor(targetMonitorId);
            PersistIds(targetMonitorId);
            RaiseWorkspaceChanged(targetMonitorId);
            RaiseStateChanged();
            return OverviewMoveResult.Ok;
        }

        AssignWindowToWorkspace(hwnd, targetMonitorId, workspaceId, applyVisibility: true);
        PersistIds(targetMonitorId);
        return OverviewMoveResult.Ok;
    }

    private void ApplyVisibilityFromAssignmentsForMonitor(string monitorId)
    {
        List<(IntPtr Hwnd, bool Show)> actions;
        HashSet<nint> sticky;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            sticky = monitor.StickyWindows.ToHashSet();
            actions = new List<(IntPtr, bool)>();
            foreach (var (ws, set) in monitor.Workspaces)
            {
                var show = ws == monitor.ActiveWorkspace;
                foreach (var h in set)
                {
                    if (sticky.Contains(h))
                    {
                        continue;
                    }

                    actions.Add(((IntPtr)h, show));
                }
            }

            foreach (var h in sticky)
            {
                actions.Add(((IntPtr)h, true));
            }
        }

        foreach (var (handle, show) in actions)
        {
            if (!NativeMethods.IsWindow(handle) || !WindowClassifier.IsManagedWindow(handle))
            {
                continue;
            }

            if (show)
            {
                _visibility.ShowForWorkspace(handle);
            }
            else
            {
                _visibility.HideForWorkspace(handle);
            }
        }

        EnsureDesiredFullscreenForHandles(
            actions.Where(a => a.Show).Select(a => (nint)a.Hwnd),
            monitorId);
    }

    public enum OverviewMoveResult
    {
        Ok,
        NoWindow,
        AtLimit,
        Failed
    }

    private void RelocateWindowToMonitorIfNeeded(IntPtr hwnd, string targetMonitorId)
    {
        var current = _monitorTracker.FindMonitorForWindow(hwnd);
        var target = _monitorTracker.FindByDeviceName(targetMonitorId);
        if (current is null || target is null)
        {
            return;
        }

        if (string.Equals(current.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var work = target.WorkingArea.IsEmpty ? target.Bounds : target.WorkingArea;
        var width = Math.Max(120, rect.Right - rect.Left);
        var height = Math.Max(60, rect.Bottom - rect.Top);
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);

        var x = work.Left + Math.Max(0, (work.Width - width) / 2);
        var y = work.Top + Math.Max(0, (work.Height - height) / 2);

        var wasMaximized = NativeMethods.IsZoomed(hwnd);
        if (wasMaximized || NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        if (wasMaximized)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMAXIMIZED);
        }
    }

    public void AssignWindowToWorkspace(IntPtr hwnd, string monitorId, int workspaceNumber, bool applyVisibility = true)
    {
        if (!WindowClassifier.IsManagedWindow(hwnd))
        {
            return;
        }

        List<IntPtr>? peersToExit = null;

        lock (_sync)
        {
            foreach (var other in _monitors.Values)
            {
                other.RemoveWindow(hwnd);
                other.StickyWindows.Remove(hwnd);
            }

            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            if (!monitor.HasWorkspace(workspaceNumber))
            {
                workspaceNumber = monitor.ActiveWorkspace;
            }

            monitor.AssignWindow(hwnd, workspaceNumber);

            if (_enforceSingleFullscreen())
            {
                peersToExit = CollectFullscreenPeers(monitor, workspaceNumber, exceptHwnd: hwnd);
            }

            if (applyVisibility)
            {
                var shouldShow = workspaceNumber == monitor.ActiveWorkspace;
                if (shouldShow)
                {
                    _visibility.ShowForWorkspace(hwnd);
                }
                else
                {
                    _visibility.HideForWorkspace(hwnd);
                }
            }
            else
            {
                _visibility.TrackAsVisible(hwnd);
            }
        }

        if (peersToExit is { Count: > 0 })
        {
            foreach (var peer in peersToExit)
            {
                _fullscreen.TryExitToMaximized(peer);
            }
        }

        var pruned = PruneEmptyInactiveWorkspaces(monitorId);
        if (pruned)
        {
            RaiseWorkspaceChanged(monitorId);
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// When EnforceSingleFullscreenPerWorkspace is on, exit every fullscreen window
    /// on <paramref name="workspaceId"/> other than <paramref name="exceptHwnd"/>.
    /// </summary>
    private void ExitOtherFullscreenInWorkspace(string monitorId, int workspaceId, IntPtr exceptHwnd)
    {
        List<IntPtr> peers;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            peers = CollectFullscreenPeers(monitor, workspaceId, exceptHwnd);
        }

        foreach (var peer in peers)
        {
            _fullscreen.TryExitToMaximized(peer);
        }
    }

    private List<IntPtr> CollectFullscreenPeers(MonitorWorkspace monitor, int workspaceId, IntPtr exceptHwnd)
    {
        if (!monitor.Workspaces.TryGetValue(workspaceId, out var set))
        {
            return [];
        }

        var windows = set.Select(h => (IntPtr)h);
        return SingleFullscreenPerWorkspacePolicy.SelectPeersToExit(
            exceptHwnd,
            windows,
            hwnd => _fullscreen.IsDesired(hwnd) ||
                    _fullscreen.IsManagedFullscreen(hwnd) ||
                    WindowFullscreenService.IsGeometricFullscreen(hwnd, monitor.Bounds)).ToList();
    }

    /// <summary>
    /// Moves the focused window to <paramref name="workspaceNumber"/> on its monitor.
    /// Returns the monitor device name on success; null if nothing was moved.
    /// </summary>
    /// <param name="activateRemainingOnCurrent">
    /// When true (Caps+Shift move without follow), focus the top window left on the
    /// active workspace — or the desktop if none remain. Pass false for move-and-follow
    /// (Caps+Ctrl): the subsequent workspace switch owns focus, and activating the next
    /// window here would let key-repeat move every remaining window too.
    /// </param>
    /// <param name="applyVisibility">
    /// When false (move+follow), only reassign membership; leave the window visible until
    /// SwitchWorkspace shows/hides. Avoids Hide→background peek-capture racing Show and
    /// recloaking the window on the destination.
    /// </param>
    public string? MoveActiveWindowToWorkspace(
        int workspaceNumber,
        bool activateRemainingOnCurrent = true,
        bool applyVisibility = true)
    {
        if (!_commandsEnabled)
        {
            return null;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return null;
        }

        var monitorInfo = ResolveHotkeyMonitor()
                          ?? _monitorTracker.FindMonitorForWindow(hwnd);
        if (monitorInfo is null)
        {
            return null;
        }

        if (!ElevationService.IsCurrentProcessElevated() && ElevationService.IsWindowElevated(hwnd))
        {
            HandleElevatedWindowBlocked(monitorInfo.DeviceName);
            if (_elevationPreference() == ElevationPreference.NeverAsk)
            {
                return null;
            }
        }

        lock (_sync)
        {
            if (_monitors.TryGetValue(monitorInfo.DeviceName, out var monitor))
            {
                monitor.EnsureWorkspace(workspaceNumber);
            }
        }

        AssignWindowToWorkspace(
            hwnd,
            monitorInfo.DeviceName,
            workspaceNumber,
            applyVisibility: applyVisibility);

        if (activateRemainingOnCurrent)
        {
            // Cloaked/hidden windows often remain the foreground HWND. Without
            // re-focusing, the next Caps+Shift move hotkey would move the same
            // window again instead of the top window left on this workspace.
            ActivateTopWindowOnActiveWorkspace(monitorInfo.DeviceName, destinationWorkspace: workspaceNumber);
        }

        PersistIds(monitorInfo.DeviceName);
        RaiseWorkspaceChanged(monitorInfo.DeviceName);
        return monitorInfo.DeviceName;
    }

    /// <summary>
    /// After a window was assigned to <paramref name="destinationWorkspace"/>, focus the
    /// top remaining window on the monitor's still-active workspace (if different).
    /// </summary>
    private void ActivateTopWindowOnActiveWorkspace(string monitorId, int destinationWorkspace)
    {
        HashSet<nint> shown;
        HashSet<nint> sticky;
        int activeWorkspace;

        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            activeWorkspace = monitor.ActiveWorkspace;
            if (destinationWorkspace == activeWorkspace)
            {
                return;
            }

            shown = monitor.Workspaces[activeWorkspace].ToHashSet();
            sticky = monitor.StickyWindows.ToHashSet();
        }

        ActivateTopWindowForWorkspace(monitorId, activeWorkspace, shown, sticky);
    }

    public void HandleWindowCreated(IntPtr hwnd)
    {
        if (_visibility.IsInternalTransition(hwnd) || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return;
        }

        // Ignore CREATE of invisible helpers; wait until the window is actually shown
        // (or minimized) so we never adopt message-only / toolkit HWNDs.
        if (!NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd))
        {
            return;
        }

        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                if (monitor.FindWorkspaceOf(hwnd) is not null || monitor.IsSticky(hwnd))
                {
                    return;
                }
            }
        }

        var monitorInfo = _monitorTracker.FindMonitorForWindow(hwnd)
                          ?? _monitorTracker.FindMonitorUnderCursor();
        if (monitorInfo is null)
        {
            return;
        }

        AssignWindowToWorkspace(hwnd, monitorInfo.DeviceName, GetActiveWorkspace(monitorInfo.DeviceName), applyVisibility: true);
    }

    public void HandleWindowDestroyed(IntPtr hwnd)
    {
        _visibility.Forget(hwnd);
        if (RemoveWindow(hwnd))
        {
            foreach (var monitorId in Monitors.Select(m => m.MonitorId).ToList())
            {
                PruneEmptyInactiveWorkspaces(monitorId);
            }

            RaiseStateChanged();
        }
    }

    public void HandleWindowMoved(IntPtr hwnd)
    {
        if (_visibility.IsInternalTransition(hwnd) || !WindowClassifier.IsManagedWindow(hwnd))
        {
            return;
        }

        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                var ws = monitor.FindWorkspaceOf(hwnd);
                if (ws is int number && number != monitor.ActiveWorkspace)
                {
                    return;
                }
            }
        }

        var monitorInfo = _monitorTracker.FindMonitorForWindow(hwnd);
        if (monitorInfo is null)
        {
            return;
        }

        bool rePinSticky = false;
        var stickyHandled = false;
        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                if (!monitor.IsSticky(hwnd))
                {
                    continue;
                }

                stickyHandled = true;
                if (!string.Equals(monitor.MonitorId, monitorInfo.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    monitor.StickyWindows.Remove(hwnd);
                    if (_monitors.TryGetValue(monitorInfo.DeviceName, out var dest))
                    {
                        dest.StickyWindows.Add(hwnd);
                        rePinSticky = true;
                    }
                }

                break;
            }
        }

        if (stickyHandled)
        {
            if (rePinSticky)
            {
                _visibility.ShowForWorkspace(hwnd);
                RaiseStateChanged();
            }

            return;
        }

        string? currentMonitorId = null;
        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                if (monitor.FindWorkspaceOf(hwnd) is not null)
                {
                    currentMonitorId = monitor.MonitorId;
                    break;
                }
            }
        }

        if (currentMonitorId is not null &&
            string.Equals(currentMonitorId, monitorInfo.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AssignWindowToWorkspace(hwnd, monitorInfo.DeviceName, GetActiveWorkspace(monitorInfo.DeviceName));
    }

    public int GetActiveWorkspace(string monitorId)
    {
        lock (_sync)
        {
            return _monitors.TryGetValue(monitorId, out var m) ? m.ActiveWorkspace : 1;
        }
    }

    /// <summary>
    /// Resolves which monitor/workspace owns <paramref name="hwnd"/>, if managed.
    /// Sticky windows report the monitor they are pinned on and <paramref name="isSticky"/> = true.
    /// </summary>
    public bool TryGetManagedPlacement(
        IntPtr hwnd,
        out string monitorId,
        out int workspaceId,
        out bool isSticky)
    {
        monitorId = "";
        workspaceId = 1;
        isSticky = false;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                if (monitor.IsSticky(hwnd))
                {
                    monitorId = monitor.MonitorId;
                    workspaceId = monitor.ActiveWorkspace;
                    isSticky = true;
                    return true;
                }

                var ws = monitor.FindWorkspaceOf(hwnd);
                if (ws is int number)
                {
                    monitorId = monitor.MonitorId;
                    workspaceId = number;
                    isSticky = false;
                    return true;
                }
            }
        }

        return false;
    }

    public IReadOnlyList<int> GetWorkspaceIds(string monitorId)
    {
        lock (_sync)
        {
            return _monitors.TryGetValue(monitorId, out var m) ? m.WorkspaceIds : new[] { 1 };
        }
    }

    public string? ResolveMonitorIdUnderCursor()
        => _monitorTracker.FindMonitorUnderCursor()?.DeviceName;

    /// <summary>
    /// Monitor targeted by CapsLock workspace hotkeys: cursor after mouse move,
    /// or focused window's monitor after Alt+Tab / window selection.
    /// </summary>
    public string? ResolveMonitorIdForHotkeys()
        => _hotkeyMonitorContext?.ResolveMonitorId() ?? ResolveMonitorIdUnderCursor();

    private MonitorInfo? ResolveHotkeyMonitor()
    {
        var id = ResolveMonitorIdForHotkeys();
        return id is null ? null : _monitorTracker.FindByDeviceName(id);
    }

    public IReadOnlyList<(IntPtr Hwnd, string MonitorId, int Workspace, bool IsActiveWorkspace)> SnapshotManagedWindows()
    {
        lock (_sync)
        {
            var list = new List<(IntPtr, string, int, bool)>();
            foreach (var monitor in _monitors.Values)
            {
                foreach (var (ws, set) in monitor.Workspaces)
                {
                    foreach (var hwnd in set)
                    {
                        list.Add(((IntPtr)hwnd, monitor.MonitorId, ws, ws == monitor.ActiveWorkspace));
                    }
                }

                foreach (var hwnd in monitor.StickyWindows)
                {
                    list.Add(((IntPtr)hwnd, monitor.MonitorId, monitor.ActiveWorkspace, true));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// After a workspace switch: focus the last-focused window of the target workspace,
    /// or the topmost (z-order) non-minimized window among shown + sticky.
    /// </summary>
    private void ActivateTopWindowForWorkspace(
        string monitorId,
        int workspaceNumber,
        HashSet<nint> shown,
        HashSet<nint> sticky)
    {
        var candidates = shown.Concat(sticky)
            .Select(h => (IntPtr)h)
            .Where(h => NativeMethods.IsWindow(h) &&
                        !NativeMethods.IsIconic(h) &&
                        WindowClassifier.IsManagedWindow(h))
            .ToHashSet();

        IntPtr preferred = IntPtr.Zero;
        lock (_sync)
        {
            if (_monitors.TryGetValue(monitorId, out var monitor))
            {
                var last = monitor.GetLastFocusedWindow(workspaceNumber);
                if (last is nint hwnd &&
                    candidates.Contains((IntPtr)hwnd) &&
                    NativeMethods.IsWindow((IntPtr)hwnd) &&
                    !NativeMethods.IsIconic((IntPtr)hwnd))
                {
                    preferred = (IntPtr)hwnd;
                }
            }
        }

            var target = preferred != IntPtr.Zero
            ? preferred
            : FindTopmostWindow(candidates);

        if (target != IntPtr.Zero)
        {
            ActivateManagedWindow(target);
        }
        else
        {
            // No visible managed window left on this workspace — clear focus so
            // a cloaked/moved window is not left as GetForegroundWindow().
            NativeMethods.TryActivateDesktop();
        }
    }

    /// <summary>Bring a managed window to the foreground and remember it as last-focused.</summary>
    public void ActivateManagedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        NativeMethods.TryActivateWindow(hwnd);

        lock (_sync)
        {
            foreach (var monitor in _monitors.Values)
            {
                if (monitor.IsSticky(hwnd))
                {
                    monitor.RememberFocusedWindow(monitor.ActiveWorkspace, hwnd);
                    break;
                }

                var ws = monitor.FindWorkspaceOf(hwnd);
                if (ws is int number)
                {
                    monitor.RememberFocusedWindow(number, hwnd);
                    break;
                }
            }
        }

        ManagedWindowActivated?.Invoke(hwnd);
    }

    /// <summary>Raised after a managed window is activated (MRU / switchers).</summary>
    public event Action<IntPtr>? ManagedWindowActivated;

    private static void RememberOutgoingFocus(
        MonitorWorkspace monitor,
        int previousWorkspace,
        HashSet<nint> hide,
        HashSet<nint> sticky)
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || !NativeMethods.IsWindow(fg))
        {
            return;
        }

        if (hide.Contains(fg) || sticky.Contains(fg) || monitor.FindWorkspaceOf(fg) == previousWorkspace)
        {
            monitor.RememberFocusedWindow(previousWorkspace, fg);
        }
    }

    /// <summary>Front-to-back z-order: first EnumWindows hit in <paramref name="candidates"/>.</summary>
    private static IntPtr FindTopmostWindow(HashSet<IntPtr> candidates)
    {
        if (candidates.Count == 0)
        {
            return IntPtr.Zero;
        }

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!candidates.Contains(hwnd))
            {
                return true;
            }

            if (WindowClassifier.IsCloaked(hwnd))
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Drops closed HWNDs then removes inactive workspaces with no windows.
    /// Active empty workspaces stay until the user switches away.
    /// </summary>
    private bool PruneEmptyInactiveWorkspaces(string monitorId)
    {
        bool pruned;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return false;
            }

            monitor.DropClosedWindows(h => NativeMethods.IsWindow((IntPtr)h));
            pruned = monitor.PruneEmptyInactiveWorkspaces();
        }

        if (pruned)
        {
            PersistIds(monitorId);
        }

        return pruned;
    }

    private void PersistIds(string monitorId)
    {
        IReadOnlyList<int> ids;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            ids = monitor.WorkspaceIds;
        }

        WorkspaceIdsChanged?.Invoke(this, new WorkspaceIdsChangedEventArgs(monitorId, ids));
    }

    private void HandleElevatedWindowBlocked(string? monitorId = null)
    {
        if (_elevationPreference() == ElevationPreference.NeverAsk)
        {
            return;
        }

        ElevationAssistanceNeeded?.Invoke(this, new ElevationAssistanceEventArgs(monitorId));
    }

    private bool RemoveWindow(IntPtr hwnd)
    {
        _fullscreen.Forget(hwnd);
        lock (_sync)
        {
            var removed = false;
            foreach (var monitor in _monitors.Values)
            {
                removed |= monitor.RemoveWindow(hwnd);
            }

            return removed;
        }
    }

    private void OnMonitorsChanged(object? sender, EventArgs e) => RebuildFromTracker();

    private void RebuildFromTracker()
    {
        lock (_sync)
        {
            var existing = _monitors.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            _monitors.Clear();

            foreach (var info in _monitorTracker.Monitors)
            {
                if (existing.TryGetValue(info.DeviceName, out var prior))
                {
                    prior.Bounds = info.Bounds;
                    prior.WorkArea = info.WorkingArea;
                    _monitors[info.DeviceName] = prior;
                }
                else
                {
                    var ids = _workspaceIdsResolver(info.DeviceName);
                    var mw = new MonitorWorkspace(info.DeviceName, info.Bounds, ids)
                    {
                        WorkArea = info.WorkingArea
                    };
                    _monitors[info.DeviceName] = mw;
                }
            }
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseWorkspaceChanged(string monitorId)
    {
        int active;
        IReadOnlyList<int> ids;
        lock (_sync)
        {
            if (!_monitors.TryGetValue(monitorId, out var monitor))
            {
                return;
            }

            active = monitor.ActiveWorkspace;
            ids = monitor.WorkspaceIds;
        }

        WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(monitorId, active, ids));
    }
}

public sealed class WorkspaceChangedEventArgs : EventArgs
{
    public WorkspaceChangedEventArgs(string monitorId, int activeWorkspace, IReadOnlyList<int> workspaceIds)
    {
        MonitorId = monitorId;
        ActiveWorkspace = activeWorkspace;
        WorkspaceIds = workspaceIds;
    }

    public string MonitorId { get; }
    public int ActiveWorkspace { get; }
    public IReadOnlyList<int> WorkspaceIds { get; }
    public int WorkspaceCount => WorkspaceIds.Count;
}

public sealed class WorkspaceIdsChangedEventArgs : EventArgs
{
    public WorkspaceIdsChangedEventArgs(string monitorId, IReadOnlyList<int> workspaceIds)
    {
        MonitorId = monitorId;
        WorkspaceIds = workspaceIds;
    }

    public string MonitorId { get; }
    public IReadOnlyList<int> WorkspaceIds { get; }
}

public sealed class ElevationAssistanceEventArgs : EventArgs
{
    public ElevationAssistanceEventArgs(string? monitorId)
    {
        MonitorId = monitorId;
    }

    public string? MonitorId { get; }
}
