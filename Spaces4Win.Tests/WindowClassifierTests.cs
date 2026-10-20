using Spaces4Win.Core;

namespace Spaces4Win.Tests;

public class WindowClassifierTests
{
    [Theory]
    [InlineData("ConsoleWindowClass", false, false, false, true)]
    [InlineData("consolewindowclass", false, false, false, true)]
    [InlineData("TscShellContainerClass", false, false, false, true)]
    [InlineData("ConsoleWindowClass", true, false, false, false)]
    [InlineData("ConsoleWindowClass", false, true, false, false)]
    [InlineData("ConsoleWindowClass", false, false, true, false)]
    [InlineData("TscShellContainerClass", true, false, false, false)]
    [InlineData("Chrome_WidgetWin_1", false, false, false, false)]
    [InlineData("", false, false, false, false)]
    public void InvisibleHelperHost_matches_hidden_console_and_remoteapp_shell(
        string className,
        bool visible,
        bool minimized,
        bool cloaked,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowClassifier.IsInvisibleHelperHost(className, visible, minimized, cloaked));
    }

    [Theory]
    [InlineData("ApplicationFrameWindow", true, false, true)]
    [InlineData("ApplicationFrameWindow", true, true, false)]
    [InlineData("ApplicationFrameWindow", false, false, false)]
    [InlineData("Chrome_WidgetWin_1", true, false, false)]
    public void Suspended_uwp_frame_is_not_an_open_window(
        string className,
        bool shellCloaked,
        bool minimized,
        bool suspended)
    {
        Assert.Equal(
            suspended,
            WindowClassifier.IsSuspendedUwpFrame(className, shellCloaked, minimized));
    }
}
