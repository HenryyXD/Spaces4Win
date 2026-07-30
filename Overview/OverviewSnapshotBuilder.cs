using System.IO;
using Spaces4Win.Core;
using Spaces4Win.Native;
using Spaces4Win.Services;

namespace Spaces4Win.Overview;

/// <summary>Builds an immutable overview snapshot from WorkspaceManager (no DWM).</summary>
public static class OverviewSnapshotBuilder
{
    public static WorkspaceOverviewSnapshot Build(WorkspaceManager manager, string monitorId)
    {
        var monitor = manager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            return new WorkspaceOverviewSnapshot { MonitorId = monitorId };
        }

        var rows = new List<WorkspaceRowSnapshot>();
        foreach (var ws in monitor.WorkspaceIds.OrderBy(i => i))
        {
            var hwnds = monitor.Workspaces[ws]
                .Concat(ws == monitor.ActiveWorkspace ? monitor.StickyWindows : Enumerable.Empty<nint>())
                .Select(h => (IntPtr)h)
                .Where(WindowClassifier.IsOverviewEligible)
                .Distinct()
                .ToList();

            var windows = new List<WindowThumbnailInfo>();
            foreach (var hwnd in hwnds)
            {
                var title = WindowClassifier.GetWindowTitle(hwnd).Trim();
                var iconic = NativeMethods.IsIconic(hwnd);
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                var path = ProcessPathHelper.TryGetProcessPath(pid);
                var appName = !string.IsNullOrWhiteSpace(path)
                    ? Path.GetFileNameWithoutExtension(path)
                    : title;

                // Prefer a sane aspect; tiny/minimized rects fall back to 16:9.
                double aspect = 16.0 / 9.0;
                if (!iconic && NativeMethods.GetWindowRect(hwnd, out var rect))
                {
                    var w = rect.Right - rect.Left;
                    var h = rect.Bottom - rect.Top;
                    if (w >= 120 && h >= 60)
                    {
                        aspect = Math.Clamp(w / (double)h, 0.45, 3.2);
                    }
                }

                windows.Add(new WindowThumbnailInfo
                {
                    Hwnd = hwnd,
                    WorkspaceId = ws,
                    Title = title,
                    AppName = appName,
                    AspectRatio = aspect,
                    IsMinimized = iconic,
                    IsSticky = monitor.IsSticky(hwnd)
                });
            }

            rows.Add(new WorkspaceRowSnapshot
            {
                WorkspaceId = ws,
                IsActive = ws == monitor.ActiveWorkspace,
                Windows = windows
            });
        }

        return new WorkspaceOverviewSnapshot
        {
            MonitorId = monitorId,
            ActiveWorkspaceId = monitor.ActiveWorkspace,
            Rows = rows
        };
    }
}
