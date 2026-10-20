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
}
