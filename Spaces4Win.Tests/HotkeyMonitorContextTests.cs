using Spaces4Win.Core;
using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class HotkeyMonitorContextTests
{
    [Fact]
    public void PointerMove_SetsCursorSource()
    {
        using var ctx = new HotkeyMonitorContext(new MonitorTracker());
        ctx.NotifyForegroundWindow(IntPtr.Zero); // ignored
        ctx.NotifyPointerMoved();
        Assert.Equal(HotkeyMonitorSource.Cursor, ctx.Source);
    }

    [Fact]
    public void PreferCursorMonitor_sets_Cursor_source_after_focus_mode_intent()
    {
        using var ctx = new HotkeyMonitorContext(new MonitorTracker());
        ctx.NotifyPointerMoved();
        ctx.PreferCursorMonitor();
        Assert.Equal(HotkeyMonitorSource.Cursor, ctx.Source);
    }

    [Fact]
    public void ZeroHwnd_DoesNotSwitchToFocus()
    {
        using var ctx = new HotkeyMonitorContext(new MonitorTracker());
        ctx.NotifyPointerMoved();
        ctx.NotifyForegroundWindow(IntPtr.Zero);
        Assert.Equal(HotkeyMonitorSource.Cursor, ctx.Source);
    }
}
