using Spaces4Win.Overview;

namespace Spaces4Win.Tests;

public class OverviewWorkspaceAllocatorTests
{
    [Fact]
    public void PrefersGapBetweenAboveAndBelow()
    {
        var id = OverviewWorkspaceAllocator.ResolveNewWorkspaceId([1, 2, 5], aboveId: 2, belowId: 5);
        Assert.Equal(3, id);
    }

    [Fact]
    public void ContiguousNeighbors_NeedDenseInsert()
    {
        Assert.True(OverviewWorkspaceAllocator.NeedsDenseInsert([1, 2, 3], aboveId: 1, belowId: 2));
        Assert.True(OverviewWorkspaceAllocator.NeedsDenseInsert([1, 2, 3], aboveId: 2, belowId: 3));
        Assert.False(OverviewWorkspaceAllocator.NeedsDenseInsert([1, 2, 5], aboveId: 2, belowId: 5));
        Assert.False(OverviewWorkspaceAllocator.NeedsDenseInsert([1, 2], aboveId: 2, belowId: null));
    }

    [Fact]
    public void UsesNextAfterAbove_WhenContiguous_StillResolvesNextFree()
    {
        // Allocator hole-fill path is unused when NeedsDenseInsert is true;
        // ResolveNewWorkspaceId still returns next free for callers that skip insert.
        var id = OverviewWorkspaceAllocator.ResolveNewWorkspaceId([1, 2, 3], aboveId: 2, belowId: 3);
        Assert.Equal(4, id);
    }

    [Fact]
    public void UsesNextAfterBelow_WhenNoAbove()
    {
        var id = OverviewWorkspaceAllocator.ResolveNewWorkspaceId([1, 3], aboveId: null, belowId: 1);
        Assert.Equal(2, id);
    }

    [Fact]
    public void UsesNextAfterLast_WhenDroppingBelow()
    {
        var id = OverviewWorkspaceAllocator.ResolveNewWorkspaceId([1, 2], aboveId: 2, belowId: null);
        Assert.Equal(3, id);
    }

    [Fact]
    public void ReturnsNull_AtLimit()
    {
        var full = Enumerable.Range(1, 9).ToList();
        Assert.Null(OverviewWorkspaceAllocator.ResolveNewWorkspaceId(full, aboveId: 5, belowId: 6));
    }
}
