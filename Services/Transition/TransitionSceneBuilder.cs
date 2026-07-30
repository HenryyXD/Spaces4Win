using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services.Transition;

/// <summary>Builds immutable transition scenes from WorkspaceManager (no DWM).</summary>
public static class TransitionSceneBuilder
{
    public static TransitionScene? TryBuild(WorkspaceManager manager, string monitorId, int workspaceId)
    {
        var monitor = manager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null || !monitor.HasWorkspace(workspaceId))
        {
            return null;
        }

        var hwnds = monitor.Workspaces[workspaceId]
            .Concat(workspaceId == monitor.ActiveWorkspace ? monitor.StickyWindows : Enumerable.Empty<nint>())
            .Select(h => (IntPtr)h)
            .Where(WindowClassifier.IsOverviewEligible)
            .Distinct()
            .ToList();

        var windows = new List<TransitionWindowSnapshot>();
        foreach (var hwnd in hwnds)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                continue;
            }

            windows.Add(new TransitionWindowSnapshot(
                hwnd,
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom,
                NativeMethods.IsIconic(hwnd)));
        }

        return new TransitionScene
        {
            WorkspaceId = workspaceId,
            Windows = windows
        };
    }
}
