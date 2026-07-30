using Spaces4Win.Overview.Layout;

namespace Spaces4Win.Tests;

public class CascadeLayoutEngineTests
{
    private static CascadeLayoutRequest Req(int count, double width = 1200, double height = 180, int hover = -1, bool expanded = false)
    {
        var windows = Enumerable.Range(0, count)
            .Select(_ => new CascadeWindowInput(16.0 / 9.0, false))
            .ToList();
        return new CascadeLayoutRequest
        {
            AvailableWidth = width,
            AvailableHeight = height,
            Windows = windows,
            HoverIndex = hover,
            RowExpanded = expanded
        };
    }

    [Fact]
    public void Empty_ReturnsNoItems()
    {
        var r = CascadeLayoutEngine.Compute(Req(0));
        Assert.Empty(r.Items);
    }

    [Fact]
    public void SingleWindow_UsesMostOfWidth()
    {
        var r = CascadeLayoutEngine.Compute(Req(1, width: 1000, height: 200));
        Assert.Single(r.Items);
        Assert.True(r.Items[0].Width > 300);
        Assert.True(r.Items[0].X >= 0);
        Assert.True(r.Items[0].X + r.Items[0].Width <= 1000.1);
    }

    [Fact]
    public void TwoWindows_StayInsideBounds()
    {
        var r = CascadeLayoutEngine.Compute(Req(2, width: 800, height: 160));
        Assert.Equal(2, r.Items.Count);
        foreach (var i in r.Items)
        {
            Assert.True(i.X >= -0.1);
            Assert.True(i.X + i.Width <= 800.5);
            Assert.True(i.HitWidth > 0);
        }
    }

    [Fact]
    public void ManyWindows_CascadeKeepsMinStrip()
    {
        var r = CascadeLayoutEngine.Compute(Req(10, width: 900, height: 140));
        Assert.Equal(10, r.Items.Count);
        foreach (var i in r.Items)
        {
            Assert.True(i.HitWidth >= 1);
            Assert.True(i.Width > 0);
            Assert.True(i.X + i.HitWidth <= 900.5);
        }
    }

    [Fact]
    public void HoverMiddle_HasHighestZIndex()
    {
        var r = CascadeLayoutEngine.Compute(Req(5, hover: 2));
        var hover = r.Items.Single(i => i.Index == 2);
        Assert.Equal(r.Items.Max(i => i.ZIndex), hover.ZIndex);
        Assert.True(hover.Opacity >= r.Items.Where(i => i.Index != 2).Max(i => i.Opacity) - 0.01);
    }

    [Fact]
    public void HoverFirst_AndLast_StayInBounds()
    {
        foreach (var h in new[] { 0, 4 })
        {
            var r = CascadeLayoutEngine.Compute(Req(5, width: 1000, height: 180, hover: h));
            Assert.All(r.Items, i =>
            {
                Assert.True(i.X >= -1);
                Assert.True(i.HitX + i.HitWidth <= 1001);
            });
        }
    }

    [Fact]
    public void ExpandedRow_GrowsThumbHeight()
    {
        var collapsed = CascadeLayoutEngine.Compute(Req(3, height: 140, expanded: false));
        var expanded = CascadeLayoutEngine.Compute(Req(3, height: 140, expanded: true));
        Assert.True(expanded.Items.Average(i => i.Height) >= collapsed.Items.Average(i => i.Height) - 0.1);
    }

    [Fact]
    public void Ultrawide_And_Narrow_DoNotThrow()
    {
        _ = CascadeLayoutEngine.Compute(Req(6, width: 3200, height: 200));
        _ = CascadeLayoutEngine.Compute(Req(4, width: 320, height: 120));
        _ = CascadeLayoutEngine.Compute(Req(3, width: 600, height: 900)); // tall
    }

    [Fact]
    public void MixedAspects_NoDivideByZero()
    {
        var req = new CascadeLayoutRequest
        {
            AvailableWidth = 1000,
            AvailableHeight = 160,
            Windows =
            [
                new CascadeWindowInput(0, false),
                new CascadeWindowInput(double.NaN, false),
                new CascadeWindowInput(2.5, false)
            ]
        };
        var r = CascadeLayoutEngine.Compute(req);
        Assert.Equal(3, r.Items.Count);
        Assert.All(r.Items, i => Assert.True(i.Width > 0 && i.Height > 0));
    }

    [Fact]
    public void RowHeights_PrioritizeHoverThenActive()
    {
        var h = CascadeLayoutEngine.ComputeRowHeights(600, 4, activeIndex: 1, hoverRowIndex: 2);
        Assert.Equal(4, h.Length);
        Assert.True(h[2] >= h[1]);
        Assert.True(h[1] >= h[0] - 0.1);
        Assert.True(h.Sum() <= 600.5);
    }

    [Fact]
    public void Deterministic()
    {
        var a = CascadeLayoutEngine.Compute(Req(7, hover: 3));
        var b = CascadeLayoutEngine.Compute(Req(7, hover: 3));
        Assert.Equal(a.Items.Count, b.Items.Count);
        for (var i = 0; i < a.Items.Count; i++)
        {
            Assert.Equal(a.Items[i].X, b.Items[i].X, 3);
            Assert.Equal(a.Items[i].ZIndex, b.Items[i].ZIndex);
        }
    }
}

public class AnimationSettingsServiceTests
{
    private sealed class FakeSystem(bool reduce) : Spaces4Win.Services.Animation.ISystemMotionPreference
    {
        public bool IsReduceMotionPreferred { get; set; } = reduce;
    }

    [Fact]
    public void FollowSystem_RespectsReduceMotion()
    {
        var sys = new FakeSystem(true);
        var svc = new Spaces4Win.Services.Animation.AnimationSettingsService(sys);
        svc.Apply(Spaces4Win.Core.Motion.MotionPreference.FollowSystem);
        Assert.False(svc.AnimationsEnabled);

        sys.IsReduceMotionPreferred = false;
        svc.RefreshSystemPreference();
        Assert.True(svc.AnimationsEnabled);
    }

    [Fact]
    public void Enabled_OverridesSystemReduce()
    {
        var svc = new Spaces4Win.Services.Animation.AnimationSettingsService(new FakeSystem(true));
        svc.Apply(Spaces4Win.Core.Motion.MotionPreference.Enabled);
        Assert.True(svc.AnimationsEnabled);
    }

    [Fact]
    public void Disabled_AlwaysOff()
    {
        var svc = new Spaces4Win.Services.Animation.AnimationSettingsService(new FakeSystem(false));
        svc.Apply(Spaces4Win.Core.Motion.MotionPreference.Disabled);
        Assert.False(svc.AnimationsEnabled);
    }

    [Fact]
    public void Changed_RaisesOnPreferenceSwitch()
    {
        var svc = new Spaces4Win.Services.Animation.AnimationSettingsService(new FakeSystem(false));
        var count = 0;
        svc.Changed += (_, _) => count++;
        svc.Apply(Spaces4Win.Core.Motion.MotionPreference.Disabled);
        Assert.True(count >= 1);
    }
}

public class OverviewHitTestTests
{
    [Fact]
    public void TopmostZIndex_WinsOverlap()
    {
        var a = new Spaces4Win.Overview.WindowThumbnailViewModel
        {
            Info = new Spaces4Win.Overview.WindowThumbnailInfo { Title = "a" },
            Layout = new CascadeItemLayout
            {
                HitX = 0, HitY = 0, HitWidth = 100, HitHeight = 80, ZIndex = 1
            }
        };
        var b = new Spaces4Win.Overview.WindowThumbnailViewModel
        {
            Info = new Spaces4Win.Overview.WindowThumbnailInfo { Title = "b" },
            Layout = new CascadeItemLayout
            {
                HitX = 40, HitY = 0, HitWidth = 100, HitHeight = 80, ZIndex = 5
            }
        };
        var hit = Spaces4Win.Overview.OverviewHitTestService.HitTest([a, b], 50, 40);
        Assert.Same(b, hit);
    }
}
