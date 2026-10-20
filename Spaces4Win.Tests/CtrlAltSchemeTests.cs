using System.Windows.Input;
using Spaces4Win.Config;

namespace Spaces4Win.Tests;

public sealed class CtrlAltSchemeTests
{
    [Fact]
    public void CtrlAlt_scheme_drops_caps_and_keeps_the_key()
    {
        var cfg = AppConfig.CreateDefault();
        cfg.UseCtrlAltInsteadOfCapsLock();

        var switch1 = cfg.SwitchWorkspaceHotkeys.Single(h => h.Workspace == 1);
        Assert.False(switch1.CapsLock);
        Assert.Equal(Key.D1, switch1.Key);
        Assert.True(switch1.Modifiers.HasFlag(ModifierKeys.Control));
        Assert.True(switch1.Modifiers.HasFlag(ModifierKeys.Alt));

        Assert.False(cfg.OverviewHotkey.CapsLock);
        Assert.Equal(Key.Oem3, cfg.OverviewHotkey.Key);
        Assert.True(cfg.OverviewHotkey.Modifiers.HasFlag(ModifierKeys.Control));
        Assert.True(cfg.OverviewHotkey.Modifiers.HasFlag(ModifierKeys.Alt));

        var save0 = cfg.SavePresetHotkeys.Single(h => h.Workspace == 0);
        Assert.False(save0.CapsLock);
        Assert.True(save0.Modifiers.HasFlag(ModifierKeys.Control));
        Assert.True(save0.Modifiers.HasFlag(ModifierKeys.Alt));
    }

    [Fact]
    public void CtrlAlt_scheme_is_idempotent()
    {
        var cfg = AppConfig.CreateDefault();
        cfg.UseCtrlAltInsteadOfCapsLock();
        var first = cfg.SwitchWorkspaceHotkeys[0].Display;
        cfg.UseCtrlAltInsteadOfCapsLock();
        Assert.Equal(first, cfg.SwitchWorkspaceHotkeys[0].Display);
    }
}
