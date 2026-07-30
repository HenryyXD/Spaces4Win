using Spaces4Win.Core;
using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public sealed class DragFollowWorkspaceTests
{
    [Fact]
    public void NotifyMoveSize_tracks_and_clears()
    {
        var tracker = new MonitorTracker();
        tracker.Refresh();
        var visibility = new WindowVisibilityService();
        var wm = new WorkspaceManager(tracker, visibility, _ => new[] { 1, 2 });

        // Fake HWND — NotifyMoveSizeStarted only stores when IsManagedWindow passes.
        // With an invalid HWND it must no-op / clear safely.
        wm.NotifyMoveSizeStarted(IntPtr.Zero);
        wm.NotifyMoveSizeEnded(IntPtr.Zero);
        wm.NotifyMoveSizeEnded(new IntPtr(42));
    }

    [Fact]
    public void SwitchWorkspace_without_drag_still_switches()
    {
        var tracker = new MonitorTracker();
        tracker.Refresh();
        var monitors = tracker.Monitors;
        if (monitors.Count == 0)
        {
            return; // headless CI without displays
        }

        var monitorId = monitors[0].DeviceName;
        var visibility = new WindowVisibilityService();
        var wm = new WorkspaceManager(tracker, visibility, _ => new[] { 1, 2 });

        wm.SwitchOrCreateWorkspace(monitorId, 2);
        var active = wm.Monitors.First(m => m.MonitorId == monitorId).ActiveWorkspace;
        Assert.Equal(2, active);
    }
}
