using Spaces4Win.Core;

namespace Spaces4Win.Tests;

public class AdjacentWorkspaceTests
{
    [Theory]
    [InlineData(new[] { 1, 3, 5 }, 3, -1, 1)]
    [InlineData(new[] { 1, 3, 5 }, 3, +1, 5)]
    [InlineData(new[] { 1, 3, 5 }, 1, -1, null)]
    [InlineData(new[] { 1, 3, 5 }, 5, +1, null)]
    [InlineData(new[] { 2 }, 2, +1, null)]
    [InlineData(new[] { 1, 2, 4 }, 2, +1, 4)] // skips missing 3
    public void ResolveAdjacentExisting_NoWrapNoCreate(
        int[] ids, int active, int direction, int? expected)
    {
        var result = MonitorWorkspace.ResolveAdjacentExisting(ids, active, direction);
        Assert.Equal(expected, result);
    }
}
