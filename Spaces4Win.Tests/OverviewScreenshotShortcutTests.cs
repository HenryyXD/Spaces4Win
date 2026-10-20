using System.Windows.Input;
using Spaces4Win.Native;
using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class OverviewScreenshotShortcutTests
{
    [Theory]
    [InlineData(NativeMethods.VK_SNAPSHOT, ModifierKeys.None, true)]
    [InlineData(NativeMethods.VK_SNAPSHOT, ModifierKeys.Alt, true)]
    [InlineData(NativeMethods.VK_LWIN, ModifierKeys.None, true)]
    [InlineData(NativeMethods.VK_RWIN, ModifierKeys.None, true)]
    [InlineData(NativeMethods.VK_LSHIFT, ModifierKeys.None, true)]
    [InlineData(NativeMethods.VK_LMENU, ModifierKeys.None, true)]
    [InlineData((int)'S', ModifierKeys.Windows | ModifierKeys.Shift, true)]
    [InlineData((int)'S', ModifierKeys.None, false)]
    [InlineData(NativeMethods.VK_ESCAPE, ModifierKeys.None, false)]
    public void Screenshot_chords_pass_through_while_overview_is_open(int vk, ModifierKeys modifiers, bool pass)
    {
        Assert.Equal(pass, OverviewService.ShouldPassScreenshotToSystem((uint)vk, modifiers));
    }
}
