using Spaces4Win.Config;
using Spaces4Win.Core;

namespace Spaces4Win.Tests;

public sealed class SingleFullscreenPerWorkspacePolicyTests
{
    [Fact]
    public void SelectPeersToExit_skips_except_and_non_fullscreen()
    {
        var a = new IntPtr(1);
        var b = new IntPtr(2);
        var c = new IntPtr(3);
        var peers = SingleFullscreenPerWorkspacePolicy.SelectPeersToExit(
            exceptHwnd: a,
            workspaceWindows: [a, b, c],
            isFullscreen: hwnd => hwnd == b || hwnd == a);

        Assert.Equal([b], peers);
    }

    [Fact]
    public void SelectPeersToExit_empty_when_none_fullscreen()
    {
        var peers = SingleFullscreenPerWorkspacePolicy.SelectPeersToExit(
            exceptHwnd: new IntPtr(1),
            workspaceWindows: [new IntPtr(1), new IntPtr(2)],
            isFullscreen: _ => false);

        Assert.Empty(peers);
    }

    [Fact]
    public void AppConfig_defaults_EnforceSingleFullscreen_true()
    {
        var cfg = AppConfig.CreateDefault();
        Assert.True(cfg.EnforceSingleFullscreenPerWorkspace);
    }
}
