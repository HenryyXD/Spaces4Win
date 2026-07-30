using System.Drawing;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// Session-scoped borderless fullscreen for managed windows (Caps+F).
/// Strips caption/thickframe and sizes to monitor Bounds; restores to maximized on exit.
/// </summary>
public sealed class WindowFullscreenService
{
    private readonly object _sync = new();
    private readonly Dictionary<nint, FullscreenState> _states = new();

    private const int ChromeMask =
        NativeMethods.WS_CAPTION |
        NativeMethods.WS_THICKFRAME |
        NativeMethods.WS_SYSMENU |
        NativeMethods.WS_MINIMIZEBOX |
        NativeMethods.WS_MAXIMIZEBOX;

    public bool IsManagedFullscreen(IntPtr hwnd)
    {
        lock (_sync)
        {
            return _states.TryGetValue(hwnd, out var s) && s.IsManaged;
        }
    }

    public bool IsDesired(IntPtr hwnd)
    {
        lock (_sync)
        {
            return _states.TryGetValue(hwnd, out var s) && s.Desired;
        }
    }

    public void MarkDesired(IntPtr hwnd, bool desired)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_sync)
        {
            if (!desired)
            {
                if (_states.TryGetValue(hwnd, out var existing) && !existing.IsManaged)
                {
                    _states.Remove(hwnd);
                }
                else if (_states.TryGetValue(hwnd, out existing))
                {
                    existing.Desired = false;
                }

                return;
            }

            if (!_states.TryGetValue(hwnd, out var state))
            {
                state = new FullscreenState();
                _states[hwnd] = state;
            }

            state.Desired = true;
        }
    }

    public bool ShouldPersistFullscreen(IntPtr hwnd) =>
        IsDesired(hwnd) || IsManagedFullscreen(hwnd);

    public static bool IsGeometricFullscreen(IntPtr hwnd, Rectangle bounds)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        return w >= bounds.Width - 2 && h >= bounds.Height - 2 &&
               Math.Abs(rect.Left - bounds.Left) <= 2 &&
               Math.Abs(rect.Top - bounds.Top) <= 2;
    }

    public bool TryEnter(IntPtr hwnd, Rectangle bounds)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        var stylePtr = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_STYLE);
        var style = unchecked((int)stylePtr.ToInt64());

        lock (_sync)
        {
            if (!_states.TryGetValue(hwnd, out var state))
            {
                state = new FullscreenState();
                _states[hwnd] = state;
            }

            if (!state.IsManaged)
            {
                state.SavedStyle = style;
            }

            state.Desired = true;
            state.IsManaged = true;
        }

        var borderless = style & ~ChromeMask;
        // Clear maximize bit so SetWindowPos can size to full Bounds (including taskbar).
        borderless &= ~NativeMethods.WS_MAXIMIZE;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_STYLE, new IntPtr(borderless));
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        return true;
    }

    public bool TryExitToMaximized(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        int? restoreStyle = null;
        lock (_sync)
        {
            if (_states.TryGetValue(hwnd, out var state))
            {
                if (state.IsManaged)
                {
                    restoreStyle = state.SavedStyle;
                }

                _states.Remove(hwnd);
            }
        }

        if (restoreStyle is int saved)
        {
            NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_STYLE, new IntPtr(saved));
        }
        else
        {
            var stylePtr = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_STYLE);
            var style = unchecked((int)stylePtr.ToInt64());
            if ((style & NativeMethods.WS_CAPTION) == 0)
            {
                style |= ChromeMask;
                NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));
            }
        }

        NativeMethods.ApplyExtendedStyleFrame(hwnd);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMAXIMIZED);
        return true;
    }

    public void ExitAllToMaximized()
    {
        List<IntPtr> managed;
        lock (_sync)
        {
            managed = _states
                .Where(kv => kv.Value.IsManaged)
                .Select(kv => (IntPtr)kv.Key)
                .ToList();
            // Desired-only (not yet entered) needs no chrome restore; drop tracking.
            foreach (var key in _states.Keys.Where(k => !_states[k].IsManaged).ToList())
            {
                _states.Remove(key);
            }
        }

        foreach (var hwnd in managed)
        {
            if (NativeMethods.IsWindow(hwnd))
            {
                TryExitToMaximized(hwnd);
            }
            else
            {
                Forget(hwnd);
            }
        }
    }

    /// <summary>
    /// Re-enter borderless for desired windows that are currently shown (active workspace).
    /// </summary>
    public void EnsureDesired(IntPtr hwnd, Rectangle bounds)
    {
        if (!IsDesired(hwnd) || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        if (IsManagedFullscreen(hwnd) && IsGeometricFullscreen(hwnd, bounds))
        {
            return;
        }

        TryEnter(hwnd, bounds);
    }

    public void Forget(IntPtr hwnd)
    {
        lock (_sync)
        {
            _states.Remove(hwnd);
        }
    }

    private sealed class FullscreenState
    {
        public bool Desired;
        public bool IsManaged;
        public int SavedStyle;
    }
}
