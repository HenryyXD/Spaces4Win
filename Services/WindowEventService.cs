using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// Event-driven window tracking via SetWinEventHook (no polling).
/// </summary>
public sealed class WindowEventService : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly Dictionary<IntPtr, long> _lastLocationTick = new();
    private IntPtr _hookCreateDestroy;
    private IntPtr _hookLocation;
    private IntPtr _hookMoveSize;
    private IntPtr _hookMinimize;
    private bool _disposed;

    /// <summary>Raised on the UI dispatcher after a tracked window moves/resizes.</summary>
    public event Action<IntPtr>? WindowLocationChanged;

    /// <summary>Raised on the UI dispatcher when a window is destroyed.</summary>
    public event Action<IntPtr>? WindowDestroyed;

    // Keep delegate alive for the lifetime of the hook.
    public WindowEventService(WorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager;
        _callback = OnWinEvent;
    }

    public void Start()
    {
        const uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;

        // CREATE..HIDE so tray / app SW_HIDE is observed.
        _hookCreateDestroy = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_CREATE,
            NativeMethods.EVENT_OBJECT_HIDE,
            IntPtr.Zero,
            _callback,
            0,
            0,
            flags);

        _hookLocation = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _callback,
            0,
            0,
            flags);

        // Title-bar drag / interactive resize — used so workspace switches follow the window.
        _hookMoveSize = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
            NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            flags);

        // Minimize start/end: taskbar minimize stays on workspace; restore updates ownership.
        _hookMinimize = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            flags);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }

        // Track move/size on the hook thread so a subsequent CapsLock switch
        // (also marshaled to the dispatcher) still sees the drag HWND.
        if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZESTART)
        {
            _workspaceManager.NotifyMoveSizeStarted(hwnd);
            return;
        }

        if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZEEND)
        {
            _workspaceManager.NotifyMoveSizeEnded(hwnd);
            return;
        }

        // Marshal back to UI/dispatcher thread for safety with WPF + manager locks.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            switch (eventType)
            {
                case NativeMethods.EVENT_OBJECT_CREATE:
                case NativeMethods.EVENT_OBJECT_SHOW:
                    _workspaceManager.HandleWindowCreated(hwnd);
                    break;
                case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
                    // Restore from taskbar minimize — refresh ownership only.
                    // Never Assign here: tray apps' ShowInTaskbar flicker can fire
                    // MINIMIZEEND and would wrongly re-adopt a just-dropped HWND.
                    _workspaceManager.HandleWindowRestored(hwnd);
                    break;
                case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
                    _workspaceManager.HandleWindowMinimized(hwnd);
                    break;
                case NativeMethods.EVENT_OBJECT_HIDE:
                    _workspaceManager.HandleWindowHidden(hwnd);
                    break;
                case NativeMethods.EVENT_OBJECT_DESTROY:
                    _lastLocationTick.Remove(hwnd);
                    _workspaceManager.NotifyMoveSizeEnded(hwnd);
                    _workspaceManager.HandleWindowDestroyed(hwnd);
                    WindowDestroyed?.Invoke(hwnd);
                    break;
                case NativeMethods.EVENT_OBJECT_LOCATIONCHANGE:
                    // LOCATIONCHANGE is extremely chatty while dragging; throttle per HWND.
                    var now = Environment.TickCount64;
                    if (_lastLocationTick.TryGetValue(hwnd, out var last) && now - last < 75)
                    {
                        return;
                    }

                    _lastLocationTick[hwnd] = now;
                    _workspaceManager.HandleWindowMoved(hwnd);
                    WindowLocationChanged?.Invoke(hwnd);
                    break;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hookCreateDestroy != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookCreateDestroy);
            _hookCreateDestroy = IntPtr.Zero;
        }

        if (_hookLocation != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookLocation);
            _hookLocation = IntPtr.Zero;
        }

        if (_hookMoveSize != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookMoveSize);
            _hookMoveSize = IntPtr.Zero;
        }

        if (_hookMinimize != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookMinimize);
            _hookMinimize = IntPtr.Zero;
        }

        GC.KeepAlive(_callback);
    }
}
