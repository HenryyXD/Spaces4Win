using Spaces4Win.Core;

namespace Spaces4Win.Overview;

/// <summary>
/// Picks a new sparse workspace id when dropping a window into an empty gap
/// in the overview. Prefers a free id between the neighbors; otherwise the next
/// free id after the workspace above (or below when there is none above).
/// Contiguous neighbors (no free hole) require insert+renumber via
/// <see cref="MonitorWorkspace.TryInsertWorkspaceBetweenWithWindow"/>.
/// </summary>
public static class OverviewWorkspaceAllocator
{
    /// <summary>
    /// True when above/below are adjacent in the existing list and there is no
    /// free numeric id between them — overview must insert and renumber.
    /// </summary>
    public static bool NeedsDenseInsert(
        IReadOnlyList<int> existingSorted,
        int? aboveId,
        int? belowId)
    {
        if (aboveId is not int above || belowId is not int below)
        {
            // Drop above the first workspace with no free slot before it.
            if (aboveId is null && belowId is int belowOnly && existingSorted.Count > 0)
            {
                var first = existingSorted[0];
                if (belowOnly != first)
                {
                    return false;
                }

                for (var id = 1; id < belowOnly; id++)
                {
                    if (!existingSorted.Contains(id))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        var ia = -1;
        var ib = -1;
        for (var i = 0; i < existingSorted.Count; i++)
        {
            if (existingSorted[i] == above)
            {
                ia = i;
            }

            if (existingSorted[i] == below)
            {
                ib = i;
            }
        }

        if (ia < 0 || ib != ia + 1)
        {
            return false;
        }

        // Adjacent in the UI list — only dense-insert when no hole exists to fill.
        return below <= above + 1;
    }

    public static int? ResolveNewWorkspaceId(
        IReadOnlyList<int> existingSorted,
        int? aboveId,
        int? belowId)
    {
        var occupied = existingSorted
            .Where(id => id is >= 1 and <= MonitorWorkspace.MaxWorkspaceId)
            .ToHashSet();

        if (occupied.Count >= MonitorWorkspace.MaxWorkspaceId)
        {
            return null;
        }

        if (aboveId is int above && belowId is int below && below > above + 1)
        {
            for (var id = above + 1; id < below; id++)
            {
                if (!occupied.Contains(id))
                {
                    return id;
                }
            }
        }

        var anchor = aboveId ?? belowId;
        if (anchor is int start)
        {
            for (var id = start + 1; id <= MonitorWorkspace.MaxWorkspaceId; id++)
            {
                if (!occupied.Contains(id))
                {
                    return id;
                }
            }
        }

        // Dropping above the first workspace with no room after it — try slots before it.
        if (aboveId is null && belowId is int belowOnly)
        {
            for (var id = 1; id < belowOnly; id++)
            {
                if (!occupied.Contains(id))
                {
                    return id;
                }
            }
        }

        for (var id = 1; id <= MonitorWorkspace.MaxWorkspaceId; id++)
        {
            if (!occupied.Contains(id))
            {
                return id;
            }
        }

        return null;
    }
}
