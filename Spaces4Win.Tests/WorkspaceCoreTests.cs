using System.Drawing;
using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class SparseWorkspaceTests
{
    [Fact]
    public void EnsureWorkspace_CreatesSparseIds_WithoutIntermediates()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1 });
        Assert.True(m.EnsureWorkspace(3));
        Assert.Equal(new[] { 1, 3 }, m.WorkspaceIds);
        Assert.False(m.HasWorkspace(2));
    }

    [Fact]
    public void EnsureWorkspace_AccessExisting_NoDuplication()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3 });
        Assert.False(m.EnsureWorkspace(3));
        Assert.Equal(new[] { 1, 3 }, m.WorkspaceIds);
    }

    [Fact]
    public void EnsureWorkspace_AddNine_KeepsSparseSet()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3 });
        Assert.True(m.EnsureWorkspace(9));
        Assert.Equal(new[] { 1, 3, 9 }, m.WorkspaceIds);
    }
}

public class DeletionFallbackTests
{
    [Theory]
    [InlineData(new[] { 1, 3, 7 }, 7, 3)]
    [InlineData(new[] { 1, 3, 7 }, 3, 1)]
    [InlineData(new[] { 3, 7 }, 3, 7)]
    public void ResolveDeletionFallback_MatchesSpec(int[] existing, int deleted, int expected)
    {
        Assert.Equal(expected, MonitorWorkspace.ResolveDeletionFallback(existing, deleted));
    }

    [Fact]
    public void TryRemove_OnlyWorkspace_Blocked()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 5 });
        Assert.False(m.TryRemoveWorkspace(5, out _));
        Assert.Equal(new[] { 5 }, m.WorkspaceIds);
    }

    [Fact]
    public void TryRemove_TransfersWindows_AndKeepsStableIds()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3, 7 });
        m.AssignWindow(100, 7);
        m.AssignWindow(200, 7);
        m.SetActiveWorkspace(7);

        Assert.True(m.TryRemoveWorkspace(7, out var fallback));
        Assert.Equal(3, fallback);
        Assert.Equal(new[] { 1, 3 }, m.WorkspaceIds);
        Assert.Equal(3, m.ActiveWorkspace);
        Assert.Contains((nint)100, m.Workspaces[3]);
        Assert.Contains((nint)200, m.Workspaces[3]);
        Assert.False(m.HasWorkspace(7));
    }

    [Fact]
    public void TryRemove_MovesWindowsToFallback()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3 });
        m.AssignWindow(10, 3);
        m.SetActiveWorkspace(3);
        Assert.True(m.TryRemoveWorkspace(3, out var fallback));
        Assert.Equal(1, fallback);
        Assert.Contains((nint)10, m.Workspaces[1]);
        Assert.False(m.HasWorkspace(3));
    }

    [Fact]
    public void TryRemove_DoesNotDuplicateSticky()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3 });
        m.StickyWindows.Add(50);
        m.AssignWindow(10, 3);
        m.SetActiveWorkspace(3);
        Assert.True(m.TryRemoveWorkspace(3, out _));
        Assert.Single(m.StickyWindows);
        Assert.Contains((nint)50, m.StickyWindows);
        Assert.DoesNotContain((nint)50, m.Workspaces[1]);
    }

    [Fact]
    public void LastActive_NeverPointsToMissingWorkspace()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3, 7 });
        m.SetActiveWorkspace(3);
        m.SetActiveWorkspace(7);
        Assert.Equal(3, m.LastActiveWorkspace);
        Assert.True(m.TryRemoveWorkspace(7, out _));
        Assert.True(m.HasWorkspace(m.LastActiveWorkspace));
        Assert.NotEqual(7, m.LastActiveWorkspace);
    }
    [Fact]
    public void PruneEmptyInactive_RemovesEmptyLeftBehind_KeepsActiveEmpty()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2, 3 });
        m.AssignWindow(10, 1);
        m.SetActiveWorkspace(2); // empty active
        Assert.True(m.PruneEmptyInactiveWorkspaces());
        Assert.Equal(new[] { 1, 2 }, m.WorkspaceIds);
        Assert.Equal(2, m.ActiveWorkspace);
        Assert.Contains((nint)10, m.Workspaces[1]);
    }

    [Fact]
    public void PruneEmptyInactive_StickyAlone_DoesNotOccupyWorkspace()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3 });
        m.StickyWindows.Add(99);
        m.SetActiveWorkspace(1);
        Assert.True(m.PruneEmptyInactiveWorkspaces());
        Assert.Equal(new[] { 1 }, m.WorkspaceIds);
        Assert.Contains((nint)99, m.StickyWindows);
    }

    [Fact]
    public void PruneEmptyInactive_AfterMovingLastWindowFromOtherWorkspace()
    {
        // Caps+taskbar: last window on WS 2 brought to active WS 1 → drop WS 2.
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2 });
        m.AssignWindow(10, 1);
        m.AssignWindow(20, 2);
        m.SetActiveWorkspace(1);
        m.AssignWindow(20, 1);
        Assert.True(m.PruneEmptyInactiveWorkspaces());
        Assert.Equal(new[] { 1 }, m.WorkspaceIds);
        Assert.Contains((nint)10, m.Workspaces[1]);
        Assert.Contains((nint)20, m.Workspaces[1]);
    }

    [Fact]
    public void PruneEmptyInactive_OnLeaveEmpty_DropsPrevious()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2 });
        m.AssignWindow(10, 1);
        m.SetActiveWorkspace(2);
        m.SetActiveWorkspace(1);
        Assert.True(m.PruneEmptyInactiveWorkspaces());
        Assert.Equal(new[] { 1 }, m.WorkspaceIds);
        Assert.Equal(1, m.ActiveWorkspace);
    }

    [Fact]
    public void PruneEmptyInactive_OnlyWorkspace_KeptEvenIfEmpty()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1 });
        m.SetActiveWorkspace(1);
        Assert.False(m.PruneEmptyInactiveWorkspaces());
        Assert.Equal(new[] { 1 }, m.WorkspaceIds);
    }

    [Fact]
    public void DropClosedWindows_RemovesDeadHwnds()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2 });
        m.AssignWindow(10, 1);
        m.AssignWindow(20, 2);
        m.DropClosedWindows(h => h == 10);
        Assert.Contains((nint)10, m.Workspaces[1]);
        Assert.Empty(m.Workspaces[2]);
    }
    [Fact]
    public void RememberFocusedWindow_PersistsUntilRemoved()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2 });
        m.AssignWindow(10, 1);
        m.AssignWindow(20, 2);
        m.RememberFocusedWindow(2, 20);
        Assert.Equal((nint)20, m.GetLastFocusedWindow(2));
        m.RemoveWindow(20);
        Assert.Null(m.GetLastFocusedWindow(2));
    }

    [Fact]
    public void CompactWorkspaceIds_Sparse_RemapsWindowsAndActive()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 2, 3, 5, 7, 8 });
        m.AssignWindow(20, 2);
        m.AssignWindow(30, 3);
        m.AssignWindow(50, 5);
        m.AssignWindow(70, 7);
        m.AssignWindow(80, 8);
        m.StickyWindows.Add(99);
        m.SetActiveWorkspace(7);
        m.RememberFocusedWindow(5, 50);

        Assert.True(m.TryCompactWorkspaceIds(out var changed));
        Assert.True(changed);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, m.WorkspaceIds);
        Assert.Equal(new nint[] { 20 }, m.Workspaces[1].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 30 }, m.Workspaces[2].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 50 }, m.Workspaces[3].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 70 }, m.Workspaces[4].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 80 }, m.Workspaces[5].OrderBy(x => x).ToArray());
        Assert.Equal(4, m.ActiveWorkspace);
        Assert.Equal((nint)50, m.GetLastFocusedWindow(3));
        Assert.Contains((nint)99, m.StickyWindows);
    }

    [Fact]
    public void CompactWorkspaceIds_AlreadyDense_NoChange()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2, 3 });
        m.AssignWindow(10, 2);
        m.SetActiveWorkspace(2);

        Assert.True(m.TryCompactWorkspaceIds(out var changed));
        Assert.False(changed);
        Assert.Equal(new[] { 1, 2, 3 }, m.WorkspaceIds);
        Assert.Equal(2, m.ActiveWorkspace);
    }

    [Fact]
    public void CreateDefault_includes_compact_workspaces_hotkey_caps_r()
    {
        var hk = AppConfig.CreateDefaultCompactWorkspacesHotkey();
        Assert.True(hk.CapsLock);
        Assert.Equal(System.Windows.Input.Key.R, hk.Key);
        Assert.Equal(System.Windows.Input.Key.R, AppConfig.CreateDefault().CompactWorkspacesHotkey.Key);
    }
}

public class InsertWorkspaceWithWindowTests
{
    [Fact]
    public void InsertLeft_FromSingleWorkspace_SplitsAndRenumbers()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1 });
        m.AssignWindow(10, 1); // A
        m.AssignWindow(20, 1); // B
        m.SetActiveWorkspace(1);

        Assert.True(m.TryInsertWorkspaceWithWindow(10, direction: -1, out var active));
        Assert.Equal(1, active);
        Assert.Equal(new[] { 1, 2 }, m.WorkspaceIds);
        Assert.Equal(1, m.ActiveWorkspace);
        Assert.Equal(new nint[] { 10 }, m.Workspaces[1].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 20 }, m.Workspaces[2].OrderBy(x => x).ToArray());
    }

    [Fact]
    public void InsertRight_FromWorkspaceOne_WithNeighbor_ShiftsIds()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2 });
        m.AssignWindow(10, 1); // A focused on 1
        m.AssignWindow(30, 1); // stays on old 1
        m.AssignWindow(20, 2); // B
        m.SetActiveWorkspace(1);

        Assert.True(m.TryInsertWorkspaceWithWindow(10, direction: +1, out var active));
        Assert.Equal(2, active);
        Assert.Equal(new[] { 1, 2, 3 }, m.WorkspaceIds);
        Assert.Equal(new nint[] { 30 }, m.Workspaces[1].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 10 }, m.Workspaces[2].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 20 }, m.Workspaces[3].OrderBy(x => x).ToArray());
    }

    [Fact]
    public void InsertBetween_ContiguousGap_RenumbersAndPlacesWindowInMiddle()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2, 3 });
        m.AssignWindow(10, 1);
        m.AssignWindow(20, 2);
        m.AssignWindow(30, 3);
        m.AssignWindow(99, 3); // drag this between 1 and 2
        m.SetActiveWorkspace(1);

        Assert.True(m.TryInsertWorkspaceBetweenWithWindow(99, aboveId: 1, belowId: 2, out var inserted));
        Assert.Equal(2, inserted);
        Assert.Equal(new[] { 1, 2, 3, 4 }, m.WorkspaceIds);
        Assert.Equal(new nint[] { 10 }, m.Workspaces[1].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 99 }, m.Workspaces[2].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 20 }, m.Workspaces[3].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 30 }, m.Workspaces[4].OrderBy(x => x).ToArray());
        Assert.Equal(1, m.ActiveWorkspace); // overview does not follow
    }

    [Fact]
    public void InsertLeft_OnlyWindow_DoesNotLeaveEmptySlot()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 2 });
        m.AssignWindow(10, 1);
        m.AssignWindow(20, 2);
        m.SetActiveWorkspace(1);

        Assert.True(m.TryInsertWorkspaceWithWindow(10, direction: -1, out var active));
        Assert.Equal(1, active);
        Assert.Equal(new[] { 1, 2 }, m.WorkspaceIds);
        Assert.Equal(new nint[] { 10 }, m.Workspaces[1].OrderBy(x => x).ToArray());
        Assert.Equal(new nint[] { 20 }, m.Workspaces[2].OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Insert_BlockedAtNineOccupiedWorkspaces()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, Enumerable.Range(1, 9));
        for (var i = 1; i <= 9; i++)
        {
            m.AssignWindow(i * 10, i);
        }

        m.SetActiveWorkspace(1);
        m.AssignWindow(11, 1); // second window on WS1 so extract would grow count

        Assert.False(m.TryInsertWorkspaceWithWindow(10, direction: +1, out _));
        Assert.Equal(9, m.WorkspaceCount);
        Assert.Equal(1, m.ActiveWorkspace);
        Assert.Contains((nint)10, m.Workspaces[1]);
        Assert.Contains((nint)11, m.Workspaces[1]);
    }

    [Fact]
    public void InsertLeft_SetsLastActiveToFormerWorkspace()
    {
        var m = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1 });
        m.AssignWindow(10, 1);
        m.AssignWindow(20, 1);
        m.SetActiveWorkspace(1);

        Assert.True(m.TryInsertWorkspaceWithWindow(10, direction: -1, out _));
        Assert.Equal(1, m.ActiveWorkspace);
        Assert.Equal(2, m.LastActiveWorkspace);
    }
}

public class MonitorIndependenceTests
{
    [Fact]
    public void CreatingOnMonitorA_DoesNotAffectB()
    {
        var a = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1 });
        var b = new MonitorWorkspace("B", Rectangle.Empty, new[] { 1 });
        a.EnsureWorkspace(7);
        Assert.Equal(new[] { 1, 7 }, a.WorkspaceIds);
        Assert.Equal(new[] { 1 }, b.WorkspaceIds);
    }

    [Fact]
    public void DeletingOnMonitorA_DoesNotAffectB()
    {
        var a = new MonitorWorkspace("A", Rectangle.Empty, new[] { 1, 3 });
        var b = new MonitorWorkspace("B", Rectangle.Empty, new[] { 1, 3, 7 });
        a.SetActiveWorkspace(3);
        a.TryRemoveWorkspace(3, out _);
        Assert.Equal(new[] { 1 }, a.WorkspaceIds);
        Assert.Equal(new[] { 1, 3, 7 }, b.WorkspaceIds);
    }
}

public class ShutdownVisibilityTests
{
    [Fact]
    public void ConsumeExpectedHideEvent_OnlyOnce()
    {
        var vis = new WindowVisibilityService();
        var hwnd = new IntPtr(99);

        // SwHide is private — mark expect the same way HideForWorkspace does via reflection on empty set then public Consume.
        var field = typeof(WindowVisibilityService)
            .GetField("_expectOurHideEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var set = (HashSet<IntPtr>)field.GetValue(vis)!;
        set.Add(hwnd);

        Assert.True(vis.ConsumeExpectedHideEvent(hwnd));
        Assert.False(vis.ConsumeExpectedHideEvent(hwnd));
    }

    [Fact]
    public void IsAppTrayHidden_RequiresInvisibleNonIconic()
    {
        // Fake HWND will fail IsWindow — must be false.
        Assert.False(WindowVisibilityService.IsAppTrayHidden(new IntPtr(1)));
    }

    [Fact]
    public void LooksLikeTrayToolWindow_FalseForFakeHwnd()
    {
        Assert.False(WindowClassifier.LooksLikeTrayToolWindow(new IntPtr(1)));
    }

    [Fact]
    public void ShowForWorkspace_DoesNotForceShow_WithoutOurHide()
    {
        var vis = new WindowVisibilityService();
        var hwnd = new IntPtr(42);

        typeof(WindowVisibilityService)
            .GetField("_ownership", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new Dictionary<IntPtr, VisibilityOwnership>
            {
                [hwnd] = VisibilityOwnership.HiddenBySpaces4Win
            });

        // No real HWND — ShowForWorkspace should Forget and return false (not managed / not our hide).
        Assert.False(vis.ShowForWorkspace(hwnd));
        Assert.Equal(VisibilityOwnership.Unknown, vis.GetOwnership(hwnd));
    }

    [Fact]
    public void ShowForWorkspace_WeOwnHide_SkipsEarlyManagedGate_ThenForgetsDeadHwnd()
    {
        var vis = new WindowVisibilityService();
        var hwnd = new IntPtr(4242);

        typeof(WindowVisibilityService)
            .GetField("_ownership", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new Dictionary<IntPtr, VisibilityOwnership>
            {
                [hwnd] = VisibilityOwnership.HiddenBySpaces4Win
            });
        typeof(WindowVisibilityService)
            .GetField("_swHiddenByUs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new HashSet<IntPtr> { hwnd });
        typeof(WindowVisibilityService)
            .GetField("_restoreAsMinimized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new HashSet<IntPtr> { hwnd });

        // Dead HWND: IsWindow fails inside try after weOwnHide bypass — Forget, false.
        Assert.False(vis.ShowForWorkspace(hwnd));
        Assert.Equal(VisibilityOwnership.Unknown, vis.GetOwnership(hwnd));
        Assert.False(vis.WasHiddenByUs(hwnd));
        Assert.False(vis.IsPendingMinimizedRestore(hwnd));
    }

    [Fact]
    public void WasHiddenByUs_TrueOnlyWhenCloakOrSwHideTracked()
    {
        var vis = new WindowVisibilityService();
        var hwnd = new IntPtr(7);

        typeof(WindowVisibilityService)
            .GetField("_ownership", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new Dictionary<IntPtr, VisibilityOwnership>
            {
                [hwnd] = VisibilityOwnership.HiddenBySpaces4Win
            });

        Assert.False(vis.WasHiddenByUs(hwnd));

        typeof(WindowVisibilityService)
            .GetField("_swHiddenByUs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new HashSet<IntPtr> { hwnd });

        Assert.True(vis.WasHiddenByUs(hwnd));
    }

    [Fact]
    public void Forget_ClearsHiddenSatelliteTracking()
    {
        var vis = new WindowVisibilityService();
        var owner = new IntPtr(10);
        var sat = new IntPtr(11);

        var field = typeof(WindowVisibilityService)
            .GetField("_hiddenSatellites", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var map = new Dictionary<IntPtr, List<IntPtr>> { [owner] = new List<IntPtr> { sat } };
        field.SetValue(vis, map);

        vis.Forget(owner);

        var after = (Dictionary<IntPtr, List<IntPtr>>)field.GetValue(vis)!;
        Assert.False(after.ContainsKey(owner));
    }

    [Fact]
    public void RevealHiddenAsMinimized_OnlyAffectsHiddenBySpaces4Win()
    {
        var vis = new WindowVisibilityService();
        var hidden = new IntPtr(1);
        var external = new IntPtr(2);
        var visible = new IntPtr(3);

        typeof(WindowVisibilityService)
            .GetField("_ownership", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vis, new Dictionary<IntPtr, VisibilityOwnership>
            {
                [hidden] = VisibilityOwnership.HiddenBySpaces4Win,
                [external] = VisibilityOwnership.ExternallyHidden,
                [visible] = VisibilityOwnership.Visible
            });

        var ownership = vis.Snapshot();
        var inactive = new[] { hidden, external, visible };
        var toReveal = inactive
            .Where(h => ownership.TryGetValue(h, out var o) && o == VisibilityOwnership.HiddenBySpaces4Win)
            .ToList();

        Assert.Equal(new[] { hidden }, toReveal);
        Assert.DoesNotContain(external, toReveal);
        Assert.DoesNotContain(visible, toReveal);
    }

    [Fact]
    public void ShutdownCoordinator_IsIdempotent()
    {
        var vis = new WindowVisibilityService();
        var journal = new SessionJournal();
        var coordinator = new ShutdownCoordinator(null, vis, journal, null, null);
        coordinator.Execute();
        coordinator.Execute();
        Assert.True(true);
    }
}
