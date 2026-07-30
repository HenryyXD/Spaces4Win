using Spaces4Win.Overview;

namespace Spaces4Win.Tests;

public sealed class OverviewArrowNavigationTests
{
    private static List<OverviewArrowNavigation.Group> SampleTwoMonitors() =>
    [
        // Monitor 0: WS1 (2 cards), WS2 (1 card)
        new(MonitorIndex: 0, MonitorLeft: 0, WorkspaceId: 1, WorkspaceOrder: 0, CardCount: 2),
        new(MonitorIndex: 0, MonitorLeft: 0, WorkspaceId: 2, WorkspaceOrder: 1, CardCount: 1),
        // Monitor 1: WS1 (3 cards), WS2 (2 cards)
        new(MonitorIndex: 1, MonitorLeft: 1920, WorkspaceId: 1, WorkspaceOrder: 0, CardCount: 3),
        new(MonitorIndex: 1, MonitorLeft: 1920, WorkspaceId: 2, WorkspaceOrder: 1, CardCount: 2),
    ];

    [Fact]
    public void Right_WithinWorkspace_AdvancesCard()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(0, 0),
            System.Windows.Input.Key.Right);
        Assert.Equal(new OverviewArrowNavigation.Position(0, 1), next);
    }

    [Fact]
    public void Right_AtEnd_GoesToSameWorkspaceOnNextMonitor()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(0, 1),
            System.Windows.Input.Key.Right);
        Assert.Equal(new OverviewArrowNavigation.Position(2, 0), next);
    }

    [Fact]
    public void Right_AtEndOfLastMonitor_Stays()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(2, 2),
            System.Windows.Input.Key.Right);
        Assert.Equal(new OverviewArrowNavigation.Position(2, 2), next);
    }

    [Fact]
    public void Left_AtStart_GoesToLastCardOnPreviousMonitorSameWorkspace()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(2, 0),
            System.Windows.Input.Key.Left);
        Assert.Equal(new OverviewArrowNavigation.Position(0, 1), next);
    }

    [Fact]
    public void Down_ChangesWorkspaceOnSameMonitor_KeepsColumnClamped()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(0, 1),
            System.Windows.Input.Key.Down);
        // WS2 on monitor 0 has only 1 card → clamp to 0
        Assert.Equal(new OverviewArrowNavigation.Position(1, 0), next);
    }

    [Fact]
    public void Down_DoesNotCrossMonitors()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(1, 0),
            System.Windows.Input.Key.Down);
        Assert.Equal(new OverviewArrowNavigation.Position(1, 0), next);
    }

    [Fact]
    public void Up_ChangesToPreviousWorkspaceSameMonitor()
    {
        var groups = SampleTwoMonitors();
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(3, 1),
            System.Windows.Input.Key.Up);
        Assert.Equal(new OverviewArrowNavigation.Position(2, 1), next);
    }

    [Fact]
    public void Right_SkipsMissingEquivalentWorkspace_Stays()
    {
        var groups = new List<OverviewArrowNavigation.Group>
        {
            new(0, 0, WorkspaceId: 1, WorkspaceOrder: 0, CardCount: 1),
            new(1, 1920, WorkspaceId: 2, WorkspaceOrder: 0, CardCount: 2),
        };
        var next = OverviewArrowNavigation.TryMove(
            groups,
            new OverviewArrowNavigation.Position(0, 0),
            System.Windows.Input.Key.Right);
        Assert.Equal(new OverviewArrowNavigation.Position(0, 0), next);
    }
}
