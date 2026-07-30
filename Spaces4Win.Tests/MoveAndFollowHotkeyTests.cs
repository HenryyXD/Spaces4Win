using Spaces4Win.Config;
using System.Windows.Input;

namespace Spaces4Win.Tests;

public class MoveAndFollowHotkeyTests
{
    [Fact]
    public void DefaultMoveAndFollow_IsCapsControlDigits()
    {
        var list = AppConfig.CreateDefaultMoveAndFollowHotkeys();
        Assert.Equal(9, list.Count);
        Assert.All(list, h =>
        {
            Assert.True(h.CapsLock);
            Assert.Equal(ModifierKeys.Control, h.Modifiers);
            Assert.InRange(h.Workspace, 1, 9);
        });
    }

    [Fact]
    public void DefaultMove_RemainsCapsShift_WithoutFollow()
    {
        var move = AppConfig.CreateDefaultMoveHotkeys();
        var follow = AppConfig.CreateDefaultMoveAndFollowHotkeys();
        Assert.All(move, h => Assert.Equal(ModifierKeys.Shift, h.Modifiers));
        Assert.All(follow, h => Assert.Equal(ModifierKeys.Control, h.Modifiers));
    }
}
