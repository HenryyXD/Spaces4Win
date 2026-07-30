using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class HotkeyModalGateTests
{
    [Theory]
    [InlineData(HotkeyModalKind.None, HotkeyRole.General, true)]
    [InlineData(HotkeyModalKind.None, HotkeyRole.WorkspaceSwitcher, true)]
    [InlineData(HotkeyModalKind.WorkspaceSwitcher, HotkeyRole.WorkspaceSwitcher, true)]
    [InlineData(HotkeyModalKind.WorkspaceSwitcher, HotkeyRole.WindowSwitcher, false)]
    [InlineData(HotkeyModalKind.WorkspaceSwitcher, HotkeyRole.Overview, false)]
    [InlineData(HotkeyModalKind.WorkspaceSwitcher, HotkeyRole.General, false)]
    [InlineData(HotkeyModalKind.WindowSwitcher, HotkeyRole.WindowSwitcher, true)]
    [InlineData(HotkeyModalKind.WindowSwitcher, HotkeyRole.WorkspaceSwitcher, false)]
    [InlineData(HotkeyModalKind.Overview, HotkeyRole.Overview, true)]
    [InlineData(HotkeyModalKind.Overview, HotkeyRole.General, false)]
    public void Gate_allows_only_own_overlay_chord(HotkeyModalKind modal, HotkeyRole role, bool expected)
    {
        Assert.Equal(expected, HotkeyModalGate.IsAllowed(modal, role));
    }
}
