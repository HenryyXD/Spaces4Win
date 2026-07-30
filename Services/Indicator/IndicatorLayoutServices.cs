namespace Spaces4Win.Services.Indicator;

/// <summary>Shared DIP metrics for the floating workspace indicator.</summary>
public static class IndicatorLayoutMetrics
{
    public const double DotSizeDip = 22.0;
    public const double DotMarginDip = 3.0;
    /// <summary>Extra diameter reserved around each dot for padding.</summary>
    public const double OuterRingExtraDip = 6.0;
    public const double OuterRingThicknessDip = 1.5;
    public const double CapsulePaddingXDip = 16.0; // 8+8
    public const double CapsulePaddingYDip = 12.0; // 6+6
    public const double AccessibilityGWidthDip = 14.0;
    /// <summary>No inset — indicators may sit flush with monitor edges and over the taskbar.</summary>
    public const double WorkAreaMarginDip = 0.0;
    /// <summary>Edge hit thickness for resize cursors / drag (DIP, pre-scale).</summary>
    public const double ResizeHitThicknessDip = 6.0;
}

[Flags]
public enum IndicatorResizeEdge
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8
}

/// <summary>Hit-test helper for indicator edge resize.</summary>
public static class IndicatorResizeHitTest
{
    public static IndicatorResizeEdge HitTest(
        double x,
        double y,
        double width,
        double height,
        double thicknessDip = IndicatorLayoutMetrics.ResizeHitThicknessDip)
    {
        if (width <= 0 || height <= 0 || thicknessDip <= 0)
        {
            return IndicatorResizeEdge.None;
        }

        var t = Math.Min(thicknessDip, Math.Min(width, height) / 2.0);
        var edge = IndicatorResizeEdge.None;
        if (x <= t)
        {
            edge |= IndicatorResizeEdge.Left;
        }
        else if (x >= width - t)
        {
            edge |= IndicatorResizeEdge.Right;
        }

        if (y <= t)
        {
            edge |= IndicatorResizeEdge.Top;
        }
        else if (y >= height - t)
        {
            edge |= IndicatorResizeEdge.Bottom;
        }

        return edge;
    }

    /// <summary>
    /// New scale from screen DIP pointer, keeping the opposite edge(s) fixed via anchors.
    /// </summary>
    public static double ComputeScaleFromScreen(
        IndicatorResizeEdge edge,
        double pointerScreenX,
        double pointerScreenY,
        double windowLeft,
        double windowTop,
        double anchorRight,
        double anchorBottom,
        double unscaledWidth,
        double unscaledHeight)
    {
        if (edge == IndicatorResizeEdge.None || unscaledWidth <= 0 || unscaledHeight <= 0)
        {
            return 1.0;
        }

        double? sx = null;
        double? sy = null;

        if (edge.HasFlag(IndicatorResizeEdge.Right))
        {
            sx = (pointerScreenX - windowLeft) / unscaledWidth;
        }
        else if (edge.HasFlag(IndicatorResizeEdge.Left))
        {
            sx = (anchorRight - pointerScreenX) / unscaledWidth;
        }

        if (edge.HasFlag(IndicatorResizeEdge.Bottom))
        {
            sy = (pointerScreenY - windowTop) / unscaledHeight;
        }
        else if (edge.HasFlag(IndicatorResizeEdge.Top))
        {
            sy = (anchorBottom - pointerScreenY) / unscaledHeight;
        }

        var scale = (sx, sy) switch
        {
            (not null, not null) => (sx.Value + sy.Value) / 2.0,
            (not null, null) => sx.Value,
            (null, not null) => sy.Value,
            _ => 1.0
        };

        return Math.Clamp(scale, 0.75, 1.5);
    }
}

/// <summary>Pure layout estimates for the indicator capsule (testable without WPF chrome).</summary>
public static class IndicatorLayoutService
{
    public static IndicatorLayoutEstimate Estimate(
        int workspaceCount,
        bool showAccessibilityG = false,
        double scale = 1.0)
    {
        workspaceCount = Math.Max(workspaceCount, 0);
        scale = Math.Clamp(scale, 0.75, 1.5);

        var perDot = IndicatorLayoutMetrics.DotSizeDip
                     + IndicatorLayoutMetrics.OuterRingExtraDip
                     + IndicatorLayoutMetrics.DotMarginDip * 2;
        var contentWidth = workspaceCount * perDot;
        if (showAccessibilityG)
        {
            contentWidth += IndicatorLayoutMetrics.AccessibilityGWidthDip;
        }

        var width = (contentWidth + IndicatorLayoutMetrics.CapsulePaddingXDip) * scale;
        var height = (IndicatorLayoutMetrics.DotSizeDip
                      + IndicatorLayoutMetrics.OuterRingExtraDip
                      + IndicatorLayoutMetrics.CapsulePaddingYDip) * scale;

        return new IndicatorLayoutEstimate(width, height, showAccessibilityG);
    }
}

public readonly record struct IndicatorLayoutEstimate(double WidthDip, double HeightDip, bool ShowsAccessibilityG);

/// <summary>Clamps indicator top-level window bounds to a monitor work area (DIP).</summary>
public static class IndicatorPositionService
{
    public static (double Left, double Top, double Right, double Bottom) ToWorkAreaDip(
        System.Drawing.Rectangle workAreaPx,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (dpiScaleX <= 0) dpiScaleX = 1;
        if (dpiScaleY <= 0) dpiScaleY = 1;
        return (
            workAreaPx.Left / dpiScaleX,
            workAreaPx.Top / dpiScaleY,
            workAreaPx.Right / dpiScaleX,
            workAreaPx.Bottom / dpiScaleY);
    }

    public static (double X, double Y) Clamp(
        double x,
        double y,
        double width,
        double height,
        (double Left, double Top, double Right, double Bottom) workAreaDip,
        double marginDip = IndicatorLayoutMetrics.WorkAreaMarginDip)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        var minX = workAreaDip.Left + marginDip;
        var minY = workAreaDip.Top + marginDip;
        var maxX = workAreaDip.Right - width - marginDip;
        var maxY = workAreaDip.Bottom - height - marginDip;

        if (maxX < minX)
        {
            x = (workAreaDip.Left + workAreaDip.Right - width) / 2.0;
        }
        else
        {
            x = Math.Clamp(x, minX, maxX);
        }

        if (maxY < minY)
        {
            y = (workAreaDip.Top + workAreaDip.Bottom - height) / 2.0;
        }
        else
        {
            y = Math.Clamp(y, minY, maxY);
        }

        return (x, y);
    }

    public static (double X, double Y) DefaultBottomRight(
        double width,
        double height,
        (double Left, double Top, double Right, double Bottom) workAreaDip,
        double marginDip = IndicatorLayoutMetrics.WorkAreaMarginDip)
    {
        var x = workAreaDip.Right - width - marginDip;
        var y = workAreaDip.Bottom - height - marginDip;
        return Clamp(x, y, width, height, workAreaDip, marginDip);
    }

    public static bool FitsEntirely(
        double x,
        double y,
        double width,
        double height,
        (double Left, double Top, double Right, double Bottom) workAreaDip,
        double marginDip = IndicatorLayoutMetrics.WorkAreaMarginDip)
    {
        return x >= workAreaDip.Left + marginDip - 0.01
               && y >= workAreaDip.Top + marginDip - 0.01
               && x + width <= workAreaDip.Right - marginDip + 0.01
               && y + height <= workAreaDip.Bottom - marginDip + 0.01;
    }
}
