using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class ForeignActivationPolicyTests
{
    [Theory]
    [InlineData(true, 2, 1, false, ForeignActivationPolicy.ActionKind.Ignore)]
    [InlineData(false, 1, 1, false, ForeignActivationPolicy.ActionKind.Ignore)]
    [InlineData(false, 2, 1, false, ForeignActivationPolicy.ActionKind.SwitchToWindowWorkspace)]
    [InlineData(false, 2, 1, true, ForeignActivationPolicy.ActionKind.MoveWindowToCurrentWorkspace)]
    [InlineData(false, 3, 1, true, ForeignActivationPolicy.ActionKind.MoveWindowToCurrentWorkspace)]
    [InlineData(true, 2, 1, true, ForeignActivationPolicy.ActionKind.Ignore)]
    public void Decide_MatchesSpec(
        bool sticky,
        int windowWs,
        int activeWs,
        bool capsHeld,
        ForeignActivationPolicy.ActionKind expected)
    {
        var actual = ForeignActivationPolicy.Decide(sticky, windowWs, activeWs, capsHeld);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, VisibilityOwnership.Visible, true)]
    [InlineData(false, VisibilityOwnership.HiddenBySpaces4Win, true)]
    [InlineData(false, VisibilityOwnership.ExternallyHidden, true)]
    [InlineData(false, VisibilityOwnership.Visible, false)]
    public void ShouldIgnoreEvent_InternalOrHidden(
        bool isInternal,
        VisibilityOwnership ownership,
        bool expectedIgnore)
    {
        var hwnd = (IntPtr)42;
        var actual = ForeignActivationPolicy.ShouldIgnoreEvent(
            isInternal,
            ownership,
            hwnd,
            currentForeground: hwnd);
        Assert.Equal(expectedIgnore, actual);
    }

    [Fact]
    public void ShouldIgnoreEvent_StaleForeground_WhenFocusMovedOn()
    {
        var reported = (IntPtr)1;
        var current = (IntPtr)2;
        Assert.True(ForeignActivationPolicy.ShouldIgnoreEvent(
            isInternalTransition: false,
            VisibilityOwnership.Visible,
            reported,
            current));
    }

    [Fact]
    public void ShouldIgnoreEvent_AllowsLiveForeground_MatchingHwnd()
    {
        var hwnd = (IntPtr)7;
        Assert.False(ForeignActivationPolicy.ShouldIgnoreEvent(
            isInternalTransition: false,
            VisibilityOwnership.Visible,
            hwnd,
            currentForeground: hwnd));
    }
}
