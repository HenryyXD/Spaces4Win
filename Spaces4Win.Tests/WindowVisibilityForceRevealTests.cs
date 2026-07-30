using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public sealed class WindowVisibilityForceRevealTests
{
    [Fact]
    public void PreferSwHide_defaults_false()
    {
        var visibility = new WindowVisibilityService();
        Assert.False(visibility.PreferSwHide);
        visibility.PreferSwHide = true;
        Assert.True(visibility.PreferSwHide);
    }

    [Fact]
    public void ForceReveal_ignores_unknown_alive_zero_handles()
    {
        var visibility = new WindowVisibilityService();
        // Should not throw on empty / invalid hwnds.
        visibility.ForceRevealNoActivate(Array.Empty<IntPtr>());
        visibility.ForceRevealNoActivate(new[] { IntPtr.Zero });
        Assert.Equal(VisibilityOwnership.Unknown, visibility.GetOwnership(IntPtr.Zero));
    }
}
