using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// When the user activates a window that lives on another workspace (e.g. taskbar click):
/// switch to that workspace and keep the window on top (HUD/indicator update via transition),
/// unless CapsLock is physically held — then move the window into the current workspace instead.
/// Caps lock LED/toggle state is irrelevant; only the physical key-down matters.
/// </summary>
public sealed class ForeignWorkspaceActivationService : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly Func<bool> _isCapsPhysicallyHeld;
    private readonly Action<string, int, IntPtr> _switchAndActivate;
    private readonly NativeMethods.WinEventDelegate _foregroundProc;
    private IntPtr _foregroundHook;
    private int _suppressDepth;
    private bool _disposed;

    public ForeignWorkspaceActivationService(
        WorkspaceManager workspaceManager,
        Func<bool> isCapsPhysicallyHeld,
        Action<string, int, IntPtr> switchAndActivate)
    {
        _workspaceManager = workspaceManager;
        _isCapsPhysicallyHeld = isCapsPhysicallyHeld;
        _switchAndActivate = switchAndActivate;
        _foregroundProc = OnForegroundChanged;
    }

    public void Start()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            return;
        }

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }

        if (Volatile.Read(ref _suppressDepth) > 0)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() => HandleForeground(hwnd));
    }

    private void HandleForeground(IntPtr hwnd)
    {
        if (_disposed || Volatile.Read(ref _suppressDepth) > 0)
        {
            return;
        }

        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        var ownership = _workspaceManager.Visibility.GetOwnership(hwnd);
        var currentForeground = NativeMethods.GetForegroundWindow();
        if (ForeignActivationPolicy.ShouldIgnoreEvent(
                _workspaceManager.Visibility.IsInternalTransition(hwnd),
                ownership,
                hwnd,
                currentForeground))
        {
            return;
        }

        if (!_workspaceManager.TryGetManagedPlacement(hwnd, out var monitorId, out var workspaceId, out var isSticky))
        {
            return;
        }

        var active = _workspaceManager.GetActiveWorkspace(monitorId);
        var capsHeld = _isCapsPhysicallyHeld();
        var action = ForeignActivationPolicy.Decide(isSticky, workspaceId, active, capsHeld);
        if (action == ForeignActivationPolicy.ActionKind.Ignore)
        {
            return;
        }

        Interlocked.Increment(ref _suppressDepth);
        try
        {
            if (action == ForeignActivationPolicy.ActionKind.MoveWindowToCurrentWorkspace)
            {
                // Moves onto the active workspace; AssignWindowToWorkspace prunes the
                // source if it becomes empty (last window left with Caps+taskbar click).
                _workspaceManager.AssignWindowToWorkspace(hwnd, monitorId, active, applyVisibility: true);
                _workspaceManager.ActivateManagedWindow(hwnd);
            }
            else
            {
                _switchAndActivate(monitorId, workspaceId, hwnd);
            }
        }
        finally
        {
            // Keep suppress briefly so hide/show/activate churn does not re-enter.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                Interlocked.Decrement(ref _suppressDepth);
            }
            else
            {
                _ = dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    () => Interlocked.Decrement(ref _suppressDepth));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        GC.KeepAlive(_foregroundProc);
    }
}
