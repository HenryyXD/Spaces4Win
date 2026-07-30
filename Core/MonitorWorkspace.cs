using System.Drawing;

namespace Spaces4Win.Core;

/// <summary>
/// One physical monitor and its independent sparse set of workspaces.
/// Workspace ids are stable (1–9); missing numbers are not filled implicitly.
/// </summary>
public sealed class MonitorWorkspace
{
    public const int MaxWorkspaceId = 9;

    private readonly Dictionary<int, HashSet<nint>> _workspaces = new();
    private readonly Dictionary<int, nint> _lastFocused = new();

    public MonitorWorkspace(string monitorId, Rectangle bounds, IEnumerable<int>? initialWorkspaceIds = null)
    {
        MonitorId = monitorId;
        Bounds = bounds;

        var ids = (initialWorkspaceIds ?? new[] { 1 })
            .Where(id => id is >= 1 and <= MaxWorkspaceId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (ids.Count == 0)
        {
            ids.Add(1);
        }

        foreach (var id in ids)
        {
            _workspaces[id] = new HashSet<nint>();
        }

        ActiveWorkspace = ids[0];
        LastActiveWorkspace = ids[0];
    }

    public string MonitorId { get; }

    public Rectangle Bounds { get; set; }

    public Rectangle WorkArea { get; set; }

    public int WorkspaceCount => _workspaces.Count;

    public IReadOnlyList<int> WorkspaceIds => _workspaces.Keys.OrderBy(k => k).ToList();

    public int ActiveWorkspace { get; private set; }

    public int LastActiveWorkspace { get; private set; }

    public HashSet<nint> StickyWindows { get; } = new();

    public IReadOnlyDictionary<int, HashSet<nint>> Workspaces => _workspaces;

    public bool HasWorkspace(int number) => _workspaces.ContainsKey(number);

    /// <summary>
    /// Ensures workspace <paramref name="id"/> exists without creating intermediate ids.
    /// Returns true if it was newly created.
    /// </summary>
    public bool EnsureWorkspace(int id)
    {
        if (id is < 1 or > MaxWorkspaceId)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (_workspaces.ContainsKey(id))
        {
            return false;
        }

        _workspaces[id] = new HashSet<nint>();
        return true;
    }

    /// <summary>
    /// Resolves deletion fallback: largest id &lt; deleted, else smallest id &gt; deleted.
    /// </summary>
    public static int? ResolveDeletionFallback(IReadOnlyList<int> existingSorted, int deletedId)
    {
        if (existingSorted.Count <= 1 || !existingSorted.Contains(deletedId))
        {
            return null;
        }

        var previous = existingSorted.Where(id => id < deletedId).DefaultIfEmpty().Max();
        if (previous > 0)
        {
            return previous;
        }

        return existingSorted.Where(id => id > deletedId).DefaultIfEmpty().Min();
    }

    /// <summary>
    /// Next/previous among existing ids only. Does not wrap or invent missing numbers.
    /// <paramref name="direction"/> &lt; 0 = previous, &gt; 0 = next.
    /// </summary>
    public static int? ResolveAdjacentExisting(IReadOnlyList<int> existingSorted, int activeId, int direction)
    {
        if (existingSorted.Count == 0 || direction == 0)
        {
            return null;
        }

        var idx = -1;
        for (var i = 0; i < existingSorted.Count; i++)
        {
            if (existingSorted[i] == activeId)
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
        {
            return null;
        }

        var next = idx + Math.Sign(direction);
        if (next < 0 || next >= existingSorted.Count)
        {
            return null;
        }

        return existingSorted[next];
    }

    /// <summary>
    /// Drops HWNDs that are no longer alive from workspace and sticky sets.
    /// </summary>
    public void DropClosedWindows(Func<nint, bool> isAlive)
    {
        ArgumentNullException.ThrowIfNull(isAlive);

        foreach (var set in _workspaces.Values)
        {
            foreach (var hwnd in set.Where(h => !isAlive(h)).ToList())
            {
                set.Remove(hwnd);
            }
        }

        foreach (var hwnd in StickyWindows.Where(h => !isAlive(h)).ToList())
        {
            StickyWindows.Remove(hwnd);
        }
    }

    /// <summary>
    /// Removes every inactive workspace that has no assigned windows.
    /// The active workspace is kept even when empty (removed once the user leaves it).
    /// Always retains at least one workspace. Sticky-only does not count as occupancy.
    /// </summary>
    public bool PruneEmptyInactiveWorkspaces()
    {
        var removedAny = false;
        while (WorkspaceCount > 1)
        {
            var emptyInactive = WorkspaceIds
                .Where(id => id != ActiveWorkspace && _workspaces[id].Count == 0)
                .ToList();
            if (emptyInactive.Count == 0)
            {
                break;
            }

            foreach (var id in emptyInactive)
            {
                if (WorkspaceCount <= 1)
                {
                    break;
                }

                if (TryRemoveWorkspace(id, out _))
                {
                    removedAny = true;
                }
            }
        }

        return removedAny;
    }

    /// <summary>
    /// Removes a workspace and moves its windows to the fallback workspace in one step.
    /// </summary>
    public bool TryRemoveWorkspace(int workspaceNumber, out int fallbackWorkspace)
    {
        fallbackWorkspace = ActiveWorkspace;
        var ids = WorkspaceIds;
        var fallback = ResolveDeletionFallback(ids, workspaceNumber);
        if (fallback is null)
        {
            return false;
        }

        fallbackWorkspace = fallback.Value;

        foreach (var hwnd in _workspaces[workspaceNumber])
        {
            _workspaces[fallbackWorkspace].Add(hwnd);
        }

        _workspaces.Remove(workspaceNumber);
        _lastFocused.Remove(workspaceNumber);

        if (ActiveWorkspace == workspaceNumber)
        {
            LastActiveWorkspace = fallbackWorkspace;
            ActiveWorkspace = fallbackWorkspace;
        }
        else if (LastActiveWorkspace == workspaceNumber)
        {
            LastActiveWorkspace = ActiveWorkspace;
        }

        if (!HasWorkspace(LastActiveWorkspace))
        {
            LastActiveWorkspace = ActiveWorkspace;
        }

        return true;
    }

    public void SetActiveWorkspace(int workspaceNumber)
    {
        if (!_workspaces.ContainsKey(workspaceNumber))
        {
            throw new ArgumentOutOfRangeException(nameof(workspaceNumber));
        }

        if (workspaceNumber == ActiveWorkspace)
        {
            return;
        }

        LastActiveWorkspace = ActiveWorkspace;
        ActiveWorkspace = workspaceNumber;
    }

    public void RememberFocusedWindow(int workspaceNumber, nint hwnd)
    {
        if (!_workspaces.ContainsKey(workspaceNumber) || hwnd == 0)
        {
            return;
        }

        _lastFocused[workspaceNumber] = hwnd;
    }

    public nint? GetLastFocusedWindow(int workspaceNumber)
    {
        if (_lastFocused.TryGetValue(workspaceNumber, out var hwnd) && hwnd != 0)
        {
            return hwnd;
        }

        return null;
    }

    public void AssignWindow(nint hwnd, int workspaceNumber)
    {
        if (hwnd == 0)
        {
            return;
        }

        if (!_workspaces.ContainsKey(workspaceNumber))
        {
            workspaceNumber = ActiveWorkspace;
        }

        RemoveWindow(hwnd);
        StickyWindows.Remove(hwnd);
        _workspaces[workspaceNumber].Add(hwnd);
    }

    public bool RemoveWindow(nint hwnd)
    {
        var removed = StickyWindows.Remove(hwnd);
        foreach (var set in _workspaces.Values)
        {
            removed |= set.Remove(hwnd);
        }

        foreach (var key in _lastFocused.Where(kv => kv.Value == hwnd).Select(kv => kv.Key).ToList())
        {
            _lastFocused.Remove(key);
        }

        return removed;
    }

    public bool IsSticky(nint hwnd) => StickyWindows.Contains(hwnd);

    public int? FindWorkspaceOf(nint hwnd)
    {
        foreach (var (number, set) in _workspaces)
        {
            if (set.Contains(hwnd))
            {
                return number;
            }
        }

        return null;
    }

    /// <summary>
    /// Densifies sparse workspace ids to 1..N in sorted order (e.g. 2,3,5,7,8 → 1..5).
    /// Remaps window sets, last-focused, ActiveWorkspace and LastActiveWorkspace.
    /// Sticky set is unchanged. Returns false only when there are no workspaces.
    /// </summary>
    public bool TryCompactWorkspaceIds(out bool changed)
    {
        changed = false;
        var ids = WorkspaceIds.ToList();
        if (ids.Count == 0)
        {
            return false;
        }

        var alreadyDense = true;
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] != i + 1)
            {
                alreadyDense = false;
                break;
            }
        }

        if (alreadyDense)
        {
            return true;
        }

        var map = new Dictionary<int, int>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            map[ids[i]] = i + 1;
        }

        var newWorkspaces = new Dictionary<int, HashSet<nint>>();
        var newLastFocused = new Dictionary<int, nint>();
        foreach (var oldId in ids)
        {
            var newId = map[oldId];
            newWorkspaces[newId] = _workspaces[oldId];
            if (_lastFocused.TryGetValue(oldId, out var lf) && lf != 0)
            {
                newLastFocused[newId] = lf;
            }
        }

        _workspaces.Clear();
        foreach (var kv in newWorkspaces)
        {
            _workspaces[kv.Key] = kv.Value;
        }

        _lastFocused.Clear();
        foreach (var kv in newLastFocused)
        {
            _lastFocused[kv.Key] = kv.Value;
        }

        ActiveWorkspace = map.TryGetValue(ActiveWorkspace, out var mappedActive)
            ? mappedActive
            : 1;
        LastActiveWorkspace = map.TryGetValue(LastActiveWorkspace, out var mappedLast)
            ? mappedLast
            : ActiveWorkspace;

        if (!HasWorkspace(ActiveWorkspace))
        {
            ActiveWorkspace = WorkspaceIds[0];
        }

        if (!HasWorkspace(LastActiveWorkspace))
        {
            LastActiveWorkspace = ActiveWorkspace;
        }

        changed = true;
        return true;
    }

    /// <summary>
    /// Extracts <paramref name="hwnd"/> into a new workspace inserted left (direction &lt; 0)
    /// or right (direction &gt; 0) of the active workspace, then renumbers densely to 1..N.
    /// Returns false if the window is missing, would exceed <see cref="MaxWorkspaceId"/>,
    /// or the operation would leave no workspaces.
    /// </summary>
    public bool TryInsertWorkspaceWithWindow(nint hwnd, int direction, out int newActiveId)
    {
        newActiveId = ActiveWorkspace;
        if (hwnd == 0 || direction == 0)
        {
            return false;
        }

        var sourceId = FindWorkspaceOf(hwnd);
        if (sourceId is null || sourceId.Value != ActiveWorkspace)
        {
            return false;
        }

        var ids = WorkspaceIds.ToList();
        var activeIndex = ids.IndexOf(ActiveWorkspace);
        if (activeIndex < 0)
        {
            return false;
        }

        // Building more than MaxWorkspaceId non-empty slots is not allowed.
        var remainingOnSource = _workspaces[ActiveWorkspace].Count - 1;
        if (remainingOnSource > 0 && WorkspaceCount >= MaxWorkspaceId)
        {
            return false;
        }

        var insertAt = direction < 0 ? activeIndex : activeIndex + 1;
        return TryInsertAtIndexWithWindow(
            hwnd,
            insertAt,
            followInserted: true,
            lastActiveDirection: direction,
            out newActiveId);
    }

    /// <summary>
    /// Overview gap drop: insert <paramref name="hwnd"/> into a new workspace between
    /// <paramref name="aboveId"/> and <paramref name="belowId"/>, then renumber densely to 1..N.
    /// Keeps the former active workspace when it still has windows; does not follow the inserted one.
    /// </summary>
    public bool TryInsertWorkspaceBetweenWithWindow(
        nint hwnd,
        int? aboveId,
        int? belowId,
        out int insertedWorkspaceId)
    {
        insertedWorkspaceId = ActiveWorkspace;
        if (hwnd == 0)
        {
            return false;
        }

        var ids = WorkspaceIds.ToList();
        int insertAt;
        if (aboveId is int above)
        {
            var ia = ids.IndexOf(above);
            if (ia < 0)
            {
                return false;
            }

            insertAt = ia + 1;
        }
        else if (belowId is int below)
        {
            var ib = ids.IndexOf(below);
            if (ib < 0)
            {
                return false;
            }

            insertAt = ib;
        }
        else
        {
            insertAt = ids.Count;
        }

        // Occupied slots after extract would exceed max.
        var sourceId = FindWorkspaceOf(hwnd);
        var remainingOnSource = sourceId is int sid
            ? _workspaces[sid].Count - (_workspaces[sid].Contains(hwnd) ? 1 : 0)
            : 0;
        var stickyOnly = IsSticky(hwnd);
        if (!stickyOnly && remainingOnSource > 0 && WorkspaceCount >= MaxWorkspaceId)
        {
            return false;
        }

        if (stickyOnly && WorkspaceCount >= MaxWorkspaceId)
        {
            return false;
        }

        return TryInsertAtIndexWithWindow(
            hwnd,
            insertAt,
            followInserted: false,
            lastActiveDirection: 0,
            out insertedWorkspaceId);
    }

    private bool TryInsertAtIndexWithWindow(
        nint hwnd,
        int insertAt,
        bool followInserted,
        int lastActiveDirection,
        out int resultWorkspaceId)
    {
        resultWorkspaceId = ActiveWorkspace;
        var ids = WorkspaceIds.ToList();
        insertAt = Math.Clamp(insertAt, 0, ids.Count);

        var remainingActive = _workspaces.TryGetValue(ActiveWorkspace, out var activeSet)
            ? activeSet.Where(h => h != hwnd).ToHashSet()
            : new HashSet<nint>();

        var slots = new List<(HashSet<nint> Windows, nint? LastFocused)>(ids.Count + 1);
        foreach (var id in ids)
        {
            var copy = new HashSet<nint>(_workspaces[id]);
            copy.Remove(hwnd);
            nint? last = _lastFocused.TryGetValue(id, out var lf) && lf != 0 && lf != hwnd ? lf : null;
            slots.Add((copy, last));
        }

        StickyWindows.Remove(hwnd);
        slots.Insert(insertAt, (new HashSet<nint> { hwnd }, hwnd));

        // Drop empty slots (origin may be empty after extract).
        slots = slots.Where(s => s.Windows.Count > 0).ToList();
        if (slots.Count == 0 || slots.Count > MaxWorkspaceId)
        {
            return false;
        }

        var insertedIndex = -1;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].Windows.Contains(hwnd))
            {
                insertedIndex = i;
                break;
            }
        }

        if (insertedIndex < 0)
        {
            return false;
        }

        _workspaces.Clear();
        _lastFocused.Clear();
        for (var i = 0; i < slots.Count; i++)
        {
            var id = i + 1;
            _workspaces[id] = slots[i].Windows;
            if (slots[i].LastFocused is nint focused &&
                focused != 0 &&
                slots[i].Windows.Contains(focused))
            {
                _lastFocused[id] = focused;
            }
        }

        resultWorkspaceId = insertedIndex + 1;

        if (followInserted)
        {
            ActiveWorkspace = resultWorkspaceId;
            // Caps+Tab should return to the workspace we extracted from, when it still exists.
            var previousSlotIndex = lastActiveDirection < 0 ? insertedIndex + 1 : insertedIndex - 1;
            LastActiveWorkspace = previousSlotIndex >= 0 && previousSlotIndex < slots.Count
                ? previousSlotIndex + 1
                : ActiveWorkspace;
        }
        else
        {
            // Prefer the renumbered former active workspace (overview does not follow).
            var mappedActive = -1;
            if (remainingActive.Count > 0)
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    if (remainingActive.SetEquals(slots[i].Windows))
                    {
                        mappedActive = i + 1;
                        break;
                    }
                }

                if (mappedActive < 0)
                {
                    for (var i = 0; i < slots.Count; i++)
                    {
                        if (remainingActive.All(h => slots[i].Windows.Contains(h)))
                        {
                            mappedActive = i + 1;
                            break;
                        }
                    }
                }
            }

            if (mappedActive > 0)
            {
                ActiveWorkspace = mappedActive;
            }
            else if (!HasWorkspace(ActiveWorkspace))
            {
                ActiveWorkspace = resultWorkspaceId > 1
                    ? resultWorkspaceId - 1
                    : (HasWorkspace(resultWorkspaceId + 1) ? resultWorkspaceId + 1 : resultWorkspaceId);
            }

            if (!HasWorkspace(LastActiveWorkspace))
            {
                LastActiveWorkspace = ActiveWorkspace;
            }
        }

        if (!HasWorkspace(LastActiveWorkspace))
        {
            LastActiveWorkspace = ActiveWorkspace;
        }

        return true;
    }

    public bool ContainsPoint(Point point) => Bounds.Contains(point);
}
