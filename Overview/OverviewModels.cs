using Spaces4Win.Overview.Layout;

namespace Spaces4Win.Overview;

public sealed class WindowThumbnailInfo
{
    public IntPtr Hwnd { get; init; }
    public int WorkspaceId { get; init; }
    public string Title { get; init; } = "";
    public string AppName { get; init; } = "";
    public double AspectRatio { get; set; } = 16.0 / 9.0;
    public bool IsMinimized { get; init; }
    public bool IsSticky { get; init; }
}

public sealed class WorkspaceOverviewSnapshot
{
    public string MonitorId { get; init; } = "";
    public int ActiveWorkspaceId { get; init; }
    public IReadOnlyList<WorkspaceRowSnapshot> Rows { get; init; } = Array.Empty<WorkspaceRowSnapshot>();
}

public sealed class WorkspaceRowSnapshot
{
    public int WorkspaceId { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<WindowThumbnailInfo> Windows { get; init; } = Array.Empty<WindowThumbnailInfo>();
}

public sealed class WindowThumbnailViewModel
{
    public required WindowThumbnailInfo Info { get; init; }
    public CascadeItemLayout Layout { get; set; } = new();
    public IntPtr ThumbHandle { get; set; }
}

public sealed class WorkspaceOverviewRowViewModel
{
    public required WorkspaceRowSnapshot Snapshot { get; init; }
    public double RowHeight { get; set; }
    public bool IsExpanded { get; set; }
    public int HoverIndex { get; set; } = -1;
    public List<WindowThumbnailViewModel> Windows { get; } = new();
}

public static class OverviewHitTestService
{
    public static WindowThumbnailViewModel? HitTest(
        IEnumerable<WindowThumbnailViewModel> windows,
        double x,
        double y)
    {
        WindowThumbnailViewModel? best = null;
        var bestZ = int.MinValue;
        foreach (var w in windows)
        {
            var l = w.Layout;
            if (x >= l.HitX && x <= l.HitX + l.HitWidth &&
                y >= l.HitY && y <= l.HitY + l.HitHeight &&
                l.ZIndex >= bestZ)
            {
                bestZ = l.ZIndex;
                best = w;
            }
        }

        return best;
    }
}
