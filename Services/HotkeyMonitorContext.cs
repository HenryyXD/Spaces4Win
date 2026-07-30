using System.Runtime.InteropServices;
using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// Decides which monitor workspace hotkeys target:
/// - pointer movement → monitor under the cursor;
/// - Alt+Tab / focus change to a managed window → that window's monitor
///   (even if the cursor is still on another display).
/// </summary>
public sealed class HotkeyMonitorContext : IDisposable
{
    private readonly MonitorTracker _monitors;
    private readonly object _sync = new();
    private readonly NativeMethods.LowLevelKeyboardProc _mouseProc;
    private readonly NativeMethods.WinEventDelegate _foregroundProc;
    private IntPtr _mouseHook;
    private IntPtr _foregroundHook;
    private HotkeyMonitorSource _source = HotkeyMonitorSource.Cursor;
    private bool _disposed;

    public HotkeyMonitorContext(MonitorTracker monitors)
    {
        _monitors = monitors;
        _mouseProc = MouseHookCallback;
        _foregroundProc = OnForegroundChanged;
    }

    public HotkeyMonitorSource Source
    {
        get
        {
            lock (_sync)
            {
                return _source;
            }
        }
    }

    public void Start()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var handle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, handle, 0);

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>
    /// Prefer the monitor under the cursor for subsequent hotkeys.
    /// Call after programmatic cursor warps (Caps+[ / ]) — WH_MOUSE_LL ignores injected moves.
    /// </summary>
    public void PreferCursorMonitor()
    {
        lock (_sync)
        {
            _source = HotkeyMonitorSource.Cursor;
        }
    }

    /// <summary>Test/helper: mark pointer movement as the active targeting mode.</summary>
    public void NotifyPointerMoved() => PreferCursorMonitor();


    /// <summary>Test/helper: mark a foreground window as the active targeting mode.</summary>
    public void NotifyForegroundWindow(IntPtr hwnd)
    {
        if (!IsFocusTarget(hwnd))
        {
            return;
        }

        lock (_sync)
        {
            _source = HotkeyMonitorSource.FocusedWindow;
        }
    }

    public string? ResolveMonitorId()
    {
        HotkeyMonitorSource source;
        lock (_sync)
        {
            source = _source;
        }

        if (source == HotkeyMonitorSource.FocusedWindow)
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (IsFocusTarget(fg))
            {
                var fromWindow = _monitors.FindMonitorForWindow(fg);
                if (fromWindow is not null)
                {
                    return fromWindow.DeviceName;
                }
            }
        }

        return _monitors.FindMonitorUnderCursor()?.DeviceName;
    }

    public MonitorInfo? ResolveMonitor()
    {
        var id = ResolveMonitorId();
        return id is null ? null : _monitors.FindByDeviceName(id);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_MOUSEMOVE)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & NativeMethods.LLMHF_INJECTED) == 0)
            {
                NotifyPointerMoved();
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
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

        NotifyForegroundWindow(hwnd);
    }

    private static bool IsFocusTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        return WindowClassifier.IsManagedWindow(hwnd);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        GC.KeepAlive(_mouseProc);
        GC.KeepAlive(_foregroundProc);
    }
}

public enum HotkeyMonitorSource
{
    Cursor = 0,
    FocusedWindow = 1
}
