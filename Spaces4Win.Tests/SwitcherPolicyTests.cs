using Spaces4Win.Config;
using Spaces4Win.Services.Switcher;

namespace Spaces4Win.Tests;

public sealed class SwitcherPolicyTests
{
    [Fact]
    public void InitialWorkspace_Prefers_LastActive_When_Different()
    {
        var ids = new[] { 1, 2, 3 };
        Assert.Equal(2, SwitcherPolicy.InitialWorkspaceSelection(ids, activeWorkspace: 3, lastActiveWorkspace: 2));
    }

    [Fact]
    public void InitialWorkspace_FallsBack_To_Next_When_Last_Equals_Active()
    {
        var ids = new[] { 1, 2, 3 };
        Assert.Equal(2, SwitcherPolicy.InitialWorkspaceSelection(ids, activeWorkspace: 1, lastActiveWorkspace: 1));
        Assert.Equal(1, SwitcherPolicy.InitialWorkspaceSelection(ids, activeWorkspace: 3, lastActiveWorkspace: 3));
    }

    [Fact]
    public void AdvanceWorkspace_Wraps()
    {
        var ids = new[] { 1, 3, 5 };
        Assert.Equal(5, SwitcherPolicy.AdvanceWorkspaceSelection(ids, 3));
        Assert.Equal(1, SwitcherPolicy.AdvanceWorkspaceSelection(ids, 5));
        Assert.Equal(3, SwitcherPolicy.AdvanceWorkspaceSelection(ids, 5, reverse: true));
        Assert.Equal(5, SwitcherPolicy.AdvanceWorkspaceSelection(ids, 1, reverse: true));
    }

    [Fact]
    public void InitialWorkspace_Reverse_Selects_Previous()
    {
        var ids = new[] { 1, 2, 3 };
        Assert.Equal(2, SwitcherPolicy.InitialWorkspaceSelection(ids, 3, lastActiveWorkspace: 1, reverse: true));
        Assert.Equal(3, SwitcherPolicy.InitialWorkspaceSelection(ids, 1, lastActiveWorkspace: 2, reverse: true));
    }

    [Fact]
    public void InitialWindowIndex_Selects_Previous_When_Multiple()
    {
        Assert.Equal(-1, SwitcherPolicy.InitialWindowIndex(0));
        Assert.Equal(0, SwitcherPolicy.InitialWindowIndex(1));
        Assert.Equal(1, SwitcherPolicy.InitialWindowIndex(4));
        Assert.Equal(3, SwitcherPolicy.InitialWindowIndex(4, reverse: true));
    }

    [Fact]
    public void AdvanceWindowIndex_Wraps()
    {
        Assert.Equal(0, SwitcherPolicy.AdvanceWindowIndex(3, 2));
        Assert.Equal(2, SwitcherPolicy.AdvanceWindowIndex(3, 1));
        Assert.Equal(0, SwitcherPolicy.AdvanceWindowIndex(3, 1, reverse: true));
        Assert.Equal(2, SwitcherPolicy.AdvanceWindowIndex(3, 0, reverse: true));
    }

    [Fact]
    public void OrderWindows_Foreground_Then_Mru_Then_ZOrder()
    {
        var candidates = new nint[] { 10, 20, 30, 40 };
        var mru = new nint[] { 30, 10 };
        var z = new nint[] { 40, 20, 30, 10 };
        var ordered = SwitcherPolicy.OrderWindows(candidates, mru, z, foreground: 20);
        Assert.Equal(new nint[] { 20, 30, 10, 40 }, ordered.ToArray());
    }

    [Fact]
    public void CreateDefault_includes_window_switcher_caps_q()
    {
        var hk = AppConfig.CreateDefaultWindowSwitcherHotkey();
        Assert.True(hk.CapsLock);
        Assert.Equal(System.Windows.Input.Key.Q, hk.Key);
        Assert.Equal(System.Windows.Input.Key.Q, AppConfig.CreateDefault().WindowSwitcherHotkey.Key);
    }
}

public sealed class WindowMruTrackerTests
{
    [Fact]
    public void Touch_Moves_To_Front_And_Respects_Capacity()
    {
        var mru = new WindowMruTracker(capacity: 3);
        mru.Touch("A", new IntPtr(1));
        mru.Touch("A", new IntPtr(2));
        mru.Touch("A", new IntPtr(3));
        mru.Touch("A", new IntPtr(4));
        Assert.Equal(new nint[] { 4, 3, 2 }, mru.GetNewestFirst("A").ToArray());

        mru.Touch("A", new IntPtr(2));
        Assert.Equal(new nint[] { 2, 4, 3 }, mru.GetNewestFirst("A").ToArray());
    }

    [Fact]
    public void Forget_Removes_Across_Monitors()
    {
        var mru = new WindowMruTracker();
        mru.Touch("A", new IntPtr(9));
        mru.Touch("B", new IntPtr(9));
        mru.Forget(new IntPtr(9));
        Assert.Empty(mru.GetNewestFirst("A"));
        Assert.Empty(mru.GetNewestFirst("B"));
    }
}
