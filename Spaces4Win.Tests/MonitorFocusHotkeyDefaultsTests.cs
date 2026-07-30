using Spaces4Win.Config;

namespace Spaces4Win.Tests;

public sealed class MonitorFocusHotkeyDefaultsTests
{
    [Fact]
    public void Defaults_use_caps_and_brackets()
    {
        var prev = AppConfig.CreateDefaultFocusPreviousMonitorHotkey();
        var next = AppConfig.CreateDefaultFocusNextMonitorHotkey();

        Assert.True(prev.CapsLock);
        Assert.True(next.CapsLock);
        Assert.Equal(System.Windows.Input.Key.OemOpenBrackets, prev.Key);
        Assert.Equal(System.Windows.Input.Key.OemCloseBrackets, next.Key);
    }

    [Fact]
    public void CreateDefault_includes_focus_monitor_bindings()
    {
        var cfg = AppConfig.CreateDefault();
        Assert.Equal(System.Windows.Input.Key.OemOpenBrackets, cfg.FocusPreviousMonitorHotkey.Key);
        Assert.Equal(System.Windows.Input.Key.OemCloseBrackets, cfg.FocusNextMonitorHotkey.Key);
        Assert.False(cfg.HideInactiveFromSwitcher);
    }
}
