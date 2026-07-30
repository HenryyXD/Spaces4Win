using System.Text;
using System.Windows.Threading;
using Spaces4Win.Core;
using Spaces4Win.Localization;
using Spaces4Win.Native;
using Spaces4Win.Services;

namespace Spaces4Win.Services.Switcher;

/// <summary>
/// Caps+Tab workspace cycle and Caps+Q window switcher (hold Caps to cycle, release to commit).
/// </summary>
public sealed class SwitcherController : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly WindowMruTracker _mru;
    private readonly Func<string, int, Task>? _switchWorkspaceAsync;
    private readonly Func<ILocalizationService>? _localization;
    private readonly object _sync = new();
    private SwitcherOverlayWindow? _overlay;
    private SessionKind _kind;
    private string? _monitorId;
    private List<int> _workspaceIds = new();
    private int _activeWorkspace;
    private int _selectedWorkspace;
    private List<IntPtr> _windows = new();
    private int _selectedWindowIndex = -1;
    private bool _busy;
    private bool _disposed;

    private enum SessionKind
    {
        None,
        Workspace,
        Window
    }

    public SwitcherController(
        WorkspaceManager workspaceManager,
        WindowMruTracker mru,
        Func<string, int, Task>? switchWorkspaceAsync = null,
        Func<ILocalizationService>? localization = null)
    {
        _workspaceManager = workspaceManager;
        _mru = mru;
        _switchWorkspaceAsync = switchWorkspaceAsync;
        _localization = localization;
        _workspaceManager.ManagedWindowActivated += OnManagedWindowActivated;
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _kind != SessionKind.None;
            }
        }
    }

    /// <summary>Modal kind for hotkey gating while the overlay is open.</summary>
    public HotkeyModalKind ActiveModalKind
    {
        get
        {
            lock (_sync)
            {
                return _kind switch
                {
                    SessionKind.Workspace => HotkeyModalKind.WorkspaceSwitcher,
                    SessionKind.Window => HotkeyModalKind.WindowSwitcher,
                    _ => HotkeyModalKind.None
                };
            }
        }
    }

    public void OnWorkspaceTab(bool reverse = false)
    {
        if (_disposed || !_workspaceManager.CommandsEnabled)
        {
            return;
        }

        RunOnUi(() =>
        {
            try
            {
                if (_busy)
                {
                    return;
                }

                SessionKind kind;
                lock (_sync)
                {
                    kind = _kind;
                    if (kind == SessionKind.Window)
                    {
                        return;
                    }
                }

                if (kind == SessionKind.None)
                {
                    BeginWorkspaceSession(reverse);
                }
                else
                {
                    AdvanceWorkspaceSession(reverse);
                }
            }
            catch
            {
                ResetSessionSilent();
            }
        });
    }

    public void OnWindowQ(bool reverse = false)
    {
        if (_disposed || !_workspaceManager.CommandsEnabled)
        {
            return;
        }

        RunOnUi(() =>
        {
            try
            {
                if (_busy)
                {
                    return;
                }

                SessionKind kind;
                lock (_sync)
                {
                    kind = _kind;
                    if (kind == SessionKind.Workspace)
                    {
                        return;
                    }
                }

                if (kind == SessionKind.None)
                {
                    BeginWindowSession(reverse);
                }
                else
                {
                    AdvanceWindowSession(reverse);
                }
            }
            catch
            {
                ResetSessionSilent();
            }
        });
    }

    public void Commit()
    {
        if (_disposed)
        {
            return;
        }

        RunOnUi(() => _ = CommitAsync());
    }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        RunOnUi(() => _ = CancelAsync());
    }

    private void BeginWorkspaceSession(bool reverse)
    {
        var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
        if (monitorId is null)
        {
            return;
        }

        var monitor = _workspaceManager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            return;
        }

        var ids = monitor.WorkspaceIds.ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var selected = SwitcherPolicy.InitialWorkspaceSelection(
            ids,
            monitor.ActiveWorkspace,
            monitor.LastActiveWorkspace,
            reverse);

        lock (_sync)
        {
            _kind = SessionKind.Workspace;
            _monitorId = monitorId;
            _workspaceIds = ids;
            _activeWorkspace = monitor.ActiveWorkspace;
            _selectedWorkspace = selected;
        }

        var items = ids.Select(id => new SwitcherWorkspaceItem { Id = id }).ToList();
        var selectedIndex = ids.IndexOf(selected);
        var overlay = EnsureOverlay();
        overlay.OutsideClicked = Cancel;
        overlay.ShowWorkspaces(monitor.Bounds, items, Math.Max(0, selectedIndex), monitor.ActiveWorkspace);
    }

    private void AdvanceWorkspaceSession(bool reverse)
    {
        int selected;
        List<int> ids;
        lock (_sync)
        {
            if (_kind != SessionKind.Workspace || _workspaceIds.Count == 0)
            {
                return;
            }

            _selectedWorkspace = SwitcherPolicy.AdvanceWorkspaceSelection(
                _workspaceIds,
                _selectedWorkspace,
                reverse);
            selected = _selectedWorkspace;
            ids = _workspaceIds;
        }

        var idx = ids.IndexOf(selected);
        _overlay?.SetSelection(Math.Max(0, idx));
    }

    private void BeginWindowSession(bool reverse)
    {
        var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
        if (monitorId is null)
        {
            return;
        }

        var monitor = _workspaceManager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            return;
        }

        var candidates = CollectCandidateWindows(monitor);
        if (candidates.Count == 0)
        {
            return;
        }

        var ordered = SwitcherPolicy.OrderWindows(
            candidates,
            _mru.GetNewestFirst(monitorId),
            ZOrderFrontToBack(candidates),
            NativeMethods.GetForegroundWindow());

        var initial = SwitcherPolicy.InitialWindowIndex(ordered.Count, reverse);
        if (initial < 0)
        {
            return;
        }

        var windows = ordered.Select(h => (IntPtr)h).ToList();
        lock (_sync)
        {
            _kind = SessionKind.Window;
            _monitorId = monitorId;
            _windows = windows;
            _selectedWindowIndex = initial;
        }

        var items = windows.Select(h => new SwitcherWindowItem
        {
            Hwnd = h,
            Title = GetTitle(h)
        }).ToList();

        var overlay = EnsureOverlay();
        overlay.OutsideClicked = Cancel;
        overlay.ShowWindows(monitor.Bounds, items, initial);
    }

    private void AdvanceWindowSession(bool reverse)
    {
        int index;
        lock (_sync)
        {
            if (_kind != SessionKind.Window || _windows.Count == 0)
            {
                return;
            }

            _selectedWindowIndex = SwitcherPolicy.AdvanceWindowIndex(
                _windows.Count,
                _selectedWindowIndex,
                reverse);
            index = _selectedWindowIndex;
        }

        _overlay?.SetSelection(Math.Max(0, index));
    }

    private async Task CommitAsync()
    {
        SessionKind kind;
        string? monitorId;
        int selectedWs;
        int activeWs;
        IntPtr selectedHwnd;
        SwitcherOverlayWindow? overlay;

        lock (_sync)
        {
            kind = _kind;
            if (kind == SessionKind.None || _busy)
            {
                return;
            }

            monitorId = _monitorId;
            selectedWs = _selectedWorkspace;
            activeWs = _activeWorkspace;
            selectedHwnd = _selectedWindowIndex >= 0 && _selectedWindowIndex < _windows.Count
                ? _windows[_selectedWindowIndex]
                : IntPtr.Zero;
            overlay = _overlay;
            _kind = SessionKind.None;
            _monitorId = null;
            _overlay = null;
            _busy = true;
        }

        try
        {
            if (overlay is not null)
            {
                await overlay.HideAnimatedAsync().ConfigureAwait(true);
                overlay.Close();
            }

            if (kind == SessionKind.Workspace &&
                monitorId is not null &&
                selectedWs != activeWs)
            {
                if (_switchWorkspaceAsync is not null)
                {
                    await _switchWorkspaceAsync(monitorId, selectedWs).ConfigureAwait(true);
                }
                else
                {
                    _workspaceManager.SwitchWorkspace(monitorId, selectedWs);
                }
            }
            else if (kind == SessionKind.Window && selectedHwnd != IntPtr.Zero)
            {
                _workspaceManager.ActivateManagedWindow(selectedHwnd);
            }
        }
        finally
        {
            lock (_sync)
            {
                _busy = false;
            }
        }
    }

    private async Task CancelAsync()
    {
        SwitcherOverlayWindow? overlay;
        lock (_sync)
        {
            if (_kind == SessionKind.None || _busy)
            {
                return;
            }

            overlay = _overlay;
            _kind = SessionKind.None;
            _monitorId = null;
            _overlay = null;
            _busy = true;
        }

        try
        {
            if (overlay is not null)
            {
                await overlay.HideAnimatedAsync().ConfigureAwait(true);
                overlay.Close();
            }
        }
        finally
        {
            lock (_sync)
            {
                _busy = false;
            }
        }
    }

    private void ResetSessionSilent()
    {
        SwitcherOverlayWindow? overlay;
        lock (_sync)
        {
            overlay = _overlay;
            _kind = SessionKind.None;
            _monitorId = null;
            _overlay = null;
            _busy = false;
        }

        try
        {
            overlay?.Close();
        }
        catch
        {
            // ignore
        }
    }

    private SwitcherOverlayWindow EnsureOverlay()
    {
        if (_overlay is not null)
        {
            return _overlay;
        }

        _overlay = new SwitcherOverlayWindow
        {
            Localization = _localization
        };
        return _overlay;
    }

    private static List<nint> CollectCandidateWindows(MonitorWorkspace monitor)
    {
        var set = new HashSet<nint>();
        if (monitor.Workspaces.TryGetValue(monitor.ActiveWorkspace, out var active))
        {
            foreach (var h in active)
            {
                if (IsSwitcherEligible((IntPtr)h))
                {
                    set.Add(h);
                }
            }
        }

        foreach (var h in monitor.StickyWindows)
        {
            if (IsSwitcherEligible((IntPtr)h))
            {
                set.Add(h);
            }
        }

        return set.ToList();
    }

    private static bool IsSwitcherEligible(IntPtr hwnd) =>
        hwnd != IntPtr.Zero &&
        NativeMethods.IsWindow(hwnd) &&
        WindowClassifier.IsOverviewEligible(hwnd);

    private static List<nint> ZOrderFrontToBack(IReadOnlyCollection<nint> candidates)
    {
        var set = candidates.ToHashSet();
        var list = new List<nint>(set.Count);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (set.Contains(hwnd))
            {
                list.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private void OnManagedWindowActivated(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_workspaceManager.TryGetManagedPlacement(hwnd, out var monitorId, out _, out _))
        {
            _mru.Touch(monitorId, hwnd);
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspaceManager.ManagedWindowActivated -= OnManagedWindowActivated;
        try
        {
            _overlay?.Close();
        }
        catch
        {
            // ignore
        }

        _overlay = null;
    }
}
