using Spaces4Win.Config;
using Spaces4Win.Services.Indicator;

namespace Spaces4Win.Tests;

public class IndicatorLayoutTests
{
    [Fact]
    public void Estimate_RingOnly_DoesNotIncludeG()
    {
        var e = IndicatorLayoutService.Estimate(3, showAccessibilityG: false, scale: 1.0);
        Assert.False(e.ShowsAccessibilityG);
        Assert.True(e.WidthDip > 0);
        Assert.True(e.HeightDip > 0);
    }

    [Fact]
    public void Estimate_RingPlusG_IsWider()
    {
        var without = IndicatorLayoutService.Estimate(3, showAccessibilityG: false);
        var with = IndicatorLayoutService.Estimate(3, showAccessibilityG: true);
        Assert.True(with.WidthDip > without.WidthDip);
        Assert.True(with.ShowsAccessibilityG);
    }

    [Fact]
    public void Estimate_MoreWorkspaces_IsWider()
    {
        var a = IndicatorLayoutService.Estimate(2);
        var b = IndicatorLayoutService.Estimate(5);
        Assert.True(b.WidthDip > a.WidthDip);
    }

    [Fact]
    public void ResizeHitTest_Corners_And_Edges()
    {
        Assert.Equal(IndicatorResizeEdge.Left, IndicatorResizeHitTest.HitTest(2, 20, 100, 40));
        Assert.Equal(IndicatorResizeEdge.Right, IndicatorResizeHitTest.HitTest(98, 20, 100, 40));
        Assert.Equal(IndicatorResizeEdge.Top, IndicatorResizeHitTest.HitTest(50, 2, 100, 40));
        Assert.Equal(IndicatorResizeEdge.Bottom, IndicatorResizeHitTest.HitTest(50, 38, 100, 40));
        Assert.Equal(
            IndicatorResizeEdge.Left | IndicatorResizeEdge.Top,
            IndicatorResizeHitTest.HitTest(2, 2, 100, 40));
        Assert.Equal(IndicatorResizeEdge.None, IndicatorResizeHitTest.HitTest(50, 20, 100, 40));
    }

    [Fact]
    public void ComputeScaleFromScreen_RightEdge_Grows()
    {
        var scale = IndicatorResizeHitTest.ComputeScaleFromScreen(
            IndicatorResizeEdge.Right,
            pointerScreenX: 150,
            pointerScreenY: 20,
            windowLeft: 0,
            windowTop: 0,
            anchorRight: 100,
            anchorBottom: 40,
            unscaledWidth: 100,
            unscaledHeight: 40);
        Assert.Equal(1.5, scale);
    }

    [Fact]
    public void ComputeScaleFromScreen_LeftEdge_UsesAnchor()
    {
        var scale = IndicatorResizeHitTest.ComputeScaleFromScreen(
            IndicatorResizeEdge.Left,
            pointerScreenX: 25,
            pointerScreenY: 20,
            windowLeft: 50,
            windowTop: 0,
            anchorRight: 150,
            anchorBottom: 40,
            unscaledWidth: 100,
            unscaledHeight: 40);
        Assert.Equal(1.25, scale);
    }

    [Fact]
    public void Clamp_KeepsWindowInsideWorkArea()
    {
        var work = (Left: 0d, Top: 0d, Right: 1920d, Bottom: 1080d);
        var (x, y) = IndicatorPositionService.Clamp(3000, 3000, 120, 40, work);
        Assert.True(IndicatorPositionService.FitsEntirely(x, y, 120, 40, work));
    }

    [Fact]
    public void Clamp_WithAccessibilityG_StillFits()
    {
        var work = (Left: 100d, Top: 100d, Right: 1000d, Bottom: 800d);
        var layout = IndicatorLayoutService.Estimate(5, showAccessibilityG: true, scale: 1.25);
        var (x, y) = IndicatorPositionService.Clamp(work.Right - 10, work.Bottom - 10, layout.WidthDip, layout.HeightDip, work);
        Assert.True(IndicatorPositionService.FitsEntirely(x, y, layout.WidthDip, layout.HeightDip, work));
    }

    [Fact]
    public void Clamp_NegativeRequest_SnapsToEdge_WithZeroMargin()
    {
        var work = (Left: 0d, Top: 0d, Right: 800d, Bottom: 600d);
        var (x, y) = IndicatorPositionService.Clamp(-50, -50, 100, 40, work);
        Assert.Equal(0, x, 3);
        Assert.Equal(0, y, 3);
    }

    [Fact]
    public void Clamp_NegativeRequest_HonorsExplicitMargin()
    {
        var work = (Left: 0d, Top: 0d, Right: 800d, Bottom: 600d);
        var (x, y) = IndicatorPositionService.Clamp(-50, -50, 100, 40, work, marginDip: 8);
        Assert.Equal(8, x, 3);
        Assert.Equal(8, y, 3);
    }

    [Fact]
    public void Clamp_AllowsFlushBottomRight_OverTaskbarRegion()
    {
        var bounds = (Left: 0d, Top: 0d, Right: 1920d, Bottom: 1080d);
        var (x, y) = IndicatorPositionService.Clamp(1900, 1060, 120, 40, bounds);
        Assert.Equal(1800, x, 3);
        Assert.Equal(1040, y, 3);
    }

    [Fact]
    public void DefaultBottomRight_Fits()
    {
        var work = (Left: 0d, Top: 0d, Right: 1920d, Bottom: 1080d);
        var layout = IndicatorLayoutService.Estimate(4, true, 1.0);
        var (x, y) = IndicatorPositionService.DefaultBottomRight(layout.WidthDip, layout.HeightDip, work);
        Assert.True(IndicatorPositionService.FitsEntirely(x, y, layout.WidthDip, layout.HeightDip, work));
    }

    [Fact]
    public void ToWorkAreaDip_ConvertsByDpi()
    {
        var px = new System.Drawing.Rectangle(0, 0, 3840, 2160);
        var dip = IndicatorPositionService.ToWorkAreaDip(px, 2.0, 2.0);
        Assert.Equal(0, dip.Left);
        Assert.Equal(1920, dip.Right);
        Assert.Equal(1080, dip.Bottom);
    }

    [Fact]
    public void PinnedMode_DefaultIsRingOnly()
    {
        var cfg = AppConfig.CreateDefault();
        Assert.Equal(PinnedWindowIndicatorMode.RingOnly, cfg.PinnedWindowIndicatorMode);
    }

    [Fact]
    public void PinnedMode_PersistsAsStableEnumName()
    {
        var cfg = new AppConfig { PinnedWindowIndicatorMode = PinnedWindowIndicatorMode.RingPlusG };
        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        Assert.Contains("RingPlusG", json);
        var round = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
        Assert.NotNull(round);
        Assert.Equal(PinnedWindowIndicatorMode.RingPlusG, round!.PinnedWindowIndicatorMode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Layout_PinnedVisualFlags(bool pinned, bool ringPlusG)
    {
        var showG = pinned && ringPlusG;
        var estimate = IndicatorLayoutService.Estimate(3, showG);
        Assert.Equal(showG, estimate.ShowsAccessibilityG);
        var baseline = IndicatorLayoutService.Estimate(3, false);
        if (!showG)
        {
            Assert.Equal(baseline.WidthDip, estimate.WidthDip);
        }
    }
}
