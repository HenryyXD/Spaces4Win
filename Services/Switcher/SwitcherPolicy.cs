namespace Spaces4Win.Services.Switcher;

/// <summary>Pure selection policy for Caps+Tab / Caps+Q switchers (testable).</summary>
public static class SwitcherPolicy
{
    /// <summary>
    /// First Tab: last active if valid and different, else next existing id (wrap).
    /// First Shift+Tab: previous existing id from active (wrap).
    /// </summary>
    public static int InitialWorkspaceSelection(
        IReadOnlyList<int> existingSorted,
        int activeWorkspace,
        int lastActiveWorkspace,
        bool reverse = false)
    {
        if (existingSorted.Count == 0)
        {
            return 1;
        }

        if (existingSorted.Count == 1)
        {
            return existingSorted[0];
        }

        if (reverse)
        {
            return StepWorkspace(existingSorted, activeWorkspace, direction: -1);
        }

        if (existingSorted.Contains(lastActiveWorkspace) && lastActiveWorkspace != activeWorkspace)
        {
            return lastActiveWorkspace;
        }

        return StepWorkspace(existingSorted, activeWorkspace, direction: +1);
    }

    public static int AdvanceWorkspaceSelection(
        IReadOnlyList<int> existingSorted,
        int selected,
        bool reverse = false)
    {
        if (existingSorted.Count == 0)
        {
            return selected;
        }

        return StepWorkspace(existingSorted, selected, reverse ? -1 : +1);
    }

    /// <summary>
    /// First Q: index 1 (previous window) when 2+ windows; otherwise 0.
    /// First Shift+Q: last index (wrap reverse from foreground).
    /// Returns -1 when the list is empty.
    /// </summary>
    public static int InitialWindowIndex(int count, bool reverse = false)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (count == 1)
        {
            return 0;
        }

        return reverse ? count - 1 : 1;
    }

    public static int AdvanceWindowIndex(int count, int selected, bool reverse = false)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (selected < 0)
        {
            return 0;
        }

        var delta = reverse ? -1 : 1;
        return (selected + delta + count) % count;
    }

    /// <summary>
    /// Orders candidate HWNDs: foreground first (if present), then MRU hits, then remaining z-order.
    /// </summary>
    public static List<nint> OrderWindows(
        IReadOnlyList<nint> candidates,
        IReadOnlyList<nint> mruNewestFirst,
        IReadOnlyList<nint> zOrderFrontToBack,
        nint foreground)
    {
        var set = candidates.Where(h => h != 0).ToHashSet();
        var result = new List<nint>(set.Count);
        var used = new HashSet<nint>();

        void Add(nint h)
        {
            if (h == 0 || !set.Contains(h) || !used.Add(h))
            {
                return;
            }

            result.Add(h);
        }

        Add(foreground);
        foreach (var h in mruNewestFirst)
        {
            Add(h);
        }

        foreach (var h in zOrderFrontToBack)
        {
            Add(h);
        }

        foreach (var h in set)
        {
            Add(h);
        }

        return result;
    }

    private static int StepWorkspace(IReadOnlyList<int> ids, int current, int direction)
    {
        var idx = IndexOf(ids, current);
        if (idx < 0)
        {
            return direction < 0 ? ids[^1] : ids[0];
        }

        var n = ids.Count;
        return ids[(idx + direction + n) % n];
    }

    private static int IndexOf(IReadOnlyList<int> ids, int value)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
