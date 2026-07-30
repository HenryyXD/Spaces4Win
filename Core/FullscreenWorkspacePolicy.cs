namespace Spaces4Win.Core;

/// <summary>Pure policy for Caps+F workspace target selection.</summary>
public static class FullscreenWorkspacePolicy
{
    /// <summary>
    /// Picks a workspace id for a dedicated fullscreen window.
    /// Priority: lowest free id in 1..Max → empty existing → no fullscreen window → fewest windows (then lowest id).
    /// </summary>
    public static int ResolveFullscreenTargetWorkspace(
        IReadOnlyList<int> existingSorted,
        Func<int, int> windowCount,
        Func<int, bool> hasFullscreenWindow)
    {
        var existing = existingSorted ?? Array.Empty<int>();
        var set = existing.ToHashSet();

        for (var id = 1; id <= MonitorWorkspace.MaxWorkspaceId; id++)
        {
            if (!set.Contains(id))
            {
                return id;
            }
        }

        var empty = existing
            .Where(id => windowCount(id) == 0)
            .OrderBy(id => id)
            .Cast<int?>()
            .FirstOrDefault();
        if (empty is int emptyId)
        {
            return emptyId;
        }

        var withoutFs = existing
            .Where(id => !hasFullscreenWindow(id))
            .OrderBy(windowCount)
            .ThenBy(id => id)
            .Cast<int?>()
            .FirstOrDefault();
        if (withoutFs is int noFsId)
        {
            return noFsId;
        }

        if (existing.Count == 0)
        {
            return 1;
        }

        return existing.OrderBy(windowCount).ThenBy(id => id).First();
    }
}
