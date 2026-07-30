using System.Drawing;
using System.Windows.Interop;
using Spaces4Win.Native;

namespace Spaces4Win.Core;

/// <summary>
/// Tracks connected monitors and adapts when display topology changes (WM_DISPLAYCHANGE).
/// </summary>
public sealed class MonitorTracker : IDisposable
{
    private readonly object _sync = new();
    private HwndSource? _hwndSource;
    private List<MonitorInfo> _monitors = new();
    private bool _disposed;

    public event EventHandler? MonitorsChanged;

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get
        {
            lock (_sync)
            {
                return _monitors.ToList();
            }
        }
    }

    public void Start()
    {
        Refresh();

        var parameters = new HwndSourceParameters("Spaces4Win.DisplayWatcher")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = unchecked((int)0x80000000) // WS_POPUP
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    public void Refresh()
    {
        var screens = System.Windows.Forms.Screen.AllScreens
            .Select(s => new MonitorInfo(
                DeviceName: s.DeviceName,
                Bounds: s.Bounds,
                WorkingArea: s.WorkingArea,
                IsPrimary: s.Primary))
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .ToList();

        lock (_sync)
        {
            _monitors = screens;
        }

        MonitorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public MonitorInfo? FindByDeviceName(string deviceName)
    {
        lock (_sync)
        {
            return _monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public MonitorInfo? FindByPoint(Point point)
    {
        lock (_sync)
        {
            return _monitors.FirstOrDefault(m => m.Bounds.Contains(point))
                   ?? _monitors.FirstOrDefault(m => m.IsPrimary)
                   ?? _monitors.FirstOrDefault();
        }
    }

    public MonitorInfo? FindByHMonitor(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            return null;
        }

        var bounds = Rectangle.FromLTRB(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right,
            info.rcMonitor.Bottom);

        lock (_sync)
        {
            return _monitors.FirstOrDefault(m => m.Bounds == bounds)
                   ?? _monitors.FirstOrDefault(m => m.Bounds.IntersectsWith(bounds));
        }
    }

    public MonitorInfo? FindMonitorUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
        {
            return null;
        }

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return FindByHMonitor(hMonitor) ?? FindByPoint(new Point(pt.X, pt.Y));
    }

    /// <summary>
    /// Move the cursor to the center of the previous (-1) or next (+1) monitor work area.
    /// Order is left-to-right, then top-to-bottom. Returns the target monitor, or null.
    /// </summary>
    public MonitorInfo? FocusAdjacentMonitor(int direction)
    {
        if (direction == 0)
        {
            return null;
        }

        List<MonitorInfo> ordered;
        lock (_sync)
        {
            ordered = _monitors
                .OrderBy(m => m.Bounds.X)
                .ThenBy(m => m.Bounds.Y)
                .ToList();
        }

        if (ordered.Count == 0)
        {
            return null;
        }

        var current = FindMonitorUnderCursor() ?? ordered.FirstOrDefault(m => m.IsPrimary) ?? ordered[0];
        var index = ordered.FindIndex(m =>
            string.Equals(m.DeviceName, current.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
        }

        var next = index + (direction < 0 ? -1 : 1);
        if (next < 0 || next >= ordered.Count)
        {
            return null;
        }

        var target = ordered[next];
        var area = target.WorkingArea.IsEmpty ? target.Bounds : target.WorkingArea;
        var cx = area.Left + area.Width / 2;
        var cy = area.Top + area.Height / 2;
        NativeMethods.SetCursorPos(cx, cy);
        return target;
    }

    public MonitorInfo? FindMonitorForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return FindByHMonitor(hMonitor);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            Refresh();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}

public sealed record MonitorInfo(string DeviceName, Rectangle Bounds, Rectangle WorkingArea, bool IsPrimary)
{
    public string DisplayLabel
    {
        get
        {
            var shortName = DeviceName.Replace(@"\\.\", string.Empty);
            var role = IsPrimary ? "Primary" : "Secondary";
            return $"{shortName} ({role}) — {Bounds.Width}×{Bounds.Height}";
        }
    }
}
