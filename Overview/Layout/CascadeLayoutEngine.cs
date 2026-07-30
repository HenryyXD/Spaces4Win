namespace Spaces4Win.Overview.Layout;

public readonly record struct CascadeWindowInput(double AspectRatio, bool IsMinimized);

public sealed class CascadeLayoutRequest
{
    public double AvailableWidth { get; init; }
    public double AvailableHeight { get; init; }
    public IReadOnlyList<CascadeWindowInput> Windows { get; init; } = Array.Empty<CascadeWindowInput>();
    public int HoverIndex { get; init; } = -1;
    public bool RowExpanded { get; init; }
    public double DpiScale { get; init; } = 1.0;
    public double MinVisibleFraction { get; init; } = 0.25;
    public double DesiredVisibleFraction { get; init; } = 0.33;
    public double MinThumbHeight { get; init; } = 72;
    public double MaxThumbHeight { get; init; } = 280;
    public double HoverScale { get; init; } = 1.12;
    public double DimmedOpacity { get; init; } = 0.72;
}

public sealed class CascadeItemLayout
{
    public int Index { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int ZIndex { get; init; }
    public double Opacity { get; init; } = 1;
    public double Scale { get; init; } = 1;
    public double HitX { get; init; }
    public double HitY { get; init; }
    public double HitWidth { get; init; }
    public double HitHeight { get; init; }
}

public sealed class CascadeLayoutResult
{
    public IReadOnlyList<CascadeItemLayout> Items { get; init; } = Array.Empty<CascadeItemLayout>();
    public double ContentWidth { get; init; }
    public double ContentHeight { get; init; }
}

/// <summary>
/// Pure layout math for cascaded window thumbnails. No WPF / HWND dependencies.
/// </summary>
public static class CascadeLayoutEngine
{
    public static CascadeLayoutResult Compute(CascadeLayoutRequest request)
    {
        var n = request.Windows.Count;
        if (n == 0 || request.AvailableWidth <= 0 || request.AvailableHeight <= 0)
        {
            return new CascadeLayoutResult
            {
                Items = Array.Empty<CascadeItemLayout>(),
                ContentWidth = Math.Max(0, request.AvailableWidth),
                ContentHeight = Math.Max(0, request.AvailableHeight)
            };
        }

        var heightBudget = Math.Clamp(
            request.AvailableHeight,
            request.MinThumbHeight,
            request.MaxThumbHeight);

        if (request.RowExpanded)
        {
            heightBudget = Math.Clamp(heightBudget * 1.35, request.MinThumbHeight, request.MaxThumbHeight);
        }

        var sizes = new (double W, double H)[n];
        for (var i = 0; i < n; i++)
        {
            var aspect = request.Windows[i].AspectRatio;
            if (double.IsNaN(aspect) || aspect <= 0.05)
            {
                aspect = 16.0 / 9.0;
            }

            aspect = Math.Clamp(aspect, 0.35, 3.5);
            var h = heightBudget;
            var w = h * aspect;
            // Cap extreme widths when many windows.
            var maxW = request.AvailableWidth * (n == 1 ? 0.92 : 0.55);
            if (w > maxW)
            {
                w = maxW;
                h = w / aspect;
            }

            sizes[i] = (w, Math.Max(h, request.MinThumbHeight * 0.85));
        }

        var hover = request.HoverIndex;
        if (hover < 0 || hover >= n)
        {
            hover = -1;
        }

        if (hover >= 0)
        {
            return ComputeHovered(request, sizes, hover);
        }

        return ComputeCollapsed(request, sizes);
    }

    private static CascadeLayoutResult ComputeCollapsed(
        CascadeLayoutRequest request,
        (double W, double H)[] sizes)
    {
        var n = sizes.Length;
        var maxH = sizes.Max(s => s.H);
        var y = Math.Max(0, (request.AvailableHeight - maxH) / 2);

        var totalNatural = sizes.Sum(s => s.W);
        var items = new CascadeItemLayout[n];

        if (totalNatural <= request.AvailableWidth + 0.5 || n == 1)
        {
            // Spread / pack without overlap.
            var gap = n == 1
                ? 0
                : Math.Max(8, (request.AvailableWidth - totalNatural) / Math.Max(1, n - 1));
            if (totalNatural + gap * (n - 1) > request.AvailableWidth)
            {
                gap = Math.Max(4, (request.AvailableWidth - totalNatural) / Math.Max(1, n - 1));
            }

            double x = n == 1 ? Math.Max(0, (request.AvailableWidth - sizes[0].W) / 2) : 0;
            if (n > 1 && totalNatural + gap * (n - 1) < request.AvailableWidth)
            {
                // Center the group.
                var group = totalNatural + gap * (n - 1);
                x = Math.Max(0, (request.AvailableWidth - group) / 2);
            }

            for (var i = 0; i < n; i++)
            {
                var (w, h) = sizes[i];
                items[i] = new CascadeItemLayout
                {
                    Index = i,
                    X = x,
                    Y = y + (maxH - h) / 2,
                    Width = w,
                    Height = h,
                    ZIndex = i,
                    Opacity = 1,
                    Scale = 1,
                    HitX = x,
                    HitY = y + (maxH - h) / 2,
                    HitWidth = w,
                    HitHeight = h
                };
                x += w + gap;
            }

            return new CascadeLayoutResult
            {
                Items = items,
                ContentWidth = request.AvailableWidth,
                ContentHeight = request.AvailableHeight
            };
        }

        // Cascaded overlap: last card fully visible; earlier cards show a strip.
        var lastW = sizes[^1].W;
        var remaining = Math.Max(1, request.AvailableWidth - lastW);
        var step = n == 1 ? 0 : remaining / (n - 1);
        var minStrip = sizes.Min(s => s.W) * request.MinVisibleFraction;
        var desiredStrip = sizes.Min(s => s.W) * request.DesiredVisibleFraction;
        step = Math.Clamp(step, minStrip, Math.Max(minStrip, desiredStrip * 1.8));

        // Recompute if last card would overflow.
        var span = step * (n - 1) + lastW;
        if (span > request.AvailableWidth && n > 1)
        {
            step = Math.Max(minStrip, (request.AvailableWidth - lastW) / (n - 1));
        }

        for (var i = 0; i < n; i++)
        {
            var (w, h) = sizes[i];
            var x = i * step;
            if (i == n - 1)
            {
                x = Math.Max(0, request.AvailableWidth - w);
            }

            var nextX = i < n - 1 ? (i + 1) * step : request.AvailableWidth;
            if (i == n - 2)
            {
                nextX = Math.Max(0, request.AvailableWidth - sizes[^1].W);
            }

            var hitW = i == n - 1 ? w : Math.Max(minStrip, Math.Min(w, nextX - x));

            items[i] = new CascadeItemLayout
            {
                Index = i,
                X = x,
                Y = y + (maxH - h) / 2,
                Width = w,
                Height = h,
                ZIndex = i,
                Opacity = 1,
                Scale = 1,
                HitX = x,
                HitY = y + (maxH - h) / 2,
                HitWidth = hitW,
                HitHeight = h
            };
        }

        return new CascadeLayoutResult
        {
            Items = items,
            ContentWidth = request.AvailableWidth,
            ContentHeight = request.AvailableHeight
        };
    }

    private static CascadeLayoutResult ComputeHovered(
        CascadeLayoutRequest request,
        (double W, double H)[] sizes,
        int hover)
    {
        var n = sizes.Length;
        var scale = request.HoverScale;
        var focusW = sizes[hover].W * scale;
        var focusH = sizes[hover].H * scale;
        focusW = Math.Min(focusW, request.AvailableWidth * 0.7);
        focusH = Math.Min(focusH, request.AvailableHeight * 0.95);

        var maxH = Math.Max(focusH, sizes.Max(s => s.H));
        var yBase = Math.Max(0, (request.AvailableHeight - maxH) / 2);

        // Place focus near center but keep some side room.
        var focusX = Math.Clamp(
            (request.AvailableWidth - focusW) / 2,
            0,
            Math.Max(0, request.AvailableWidth - focusW));

        var leftCount = hover;
        var rightCount = n - hover - 1;
        var leftSpace = Math.Max(0, focusX - 8);
        var rightSpace = Math.Max(0, request.AvailableWidth - (focusX + focusW) - 8);

        var items = new CascadeItemLayout[n];
        items[hover] = new CascadeItemLayout
        {
            Index = hover,
            X = focusX,
            Y = yBase + (maxH - focusH) / 2,
            Width = focusW,
            Height = focusH,
            ZIndex = n + 10,
            Opacity = 1,
            Scale = scale,
            HitX = focusX,
            HitY = yBase + (maxH - focusH) / 2,
            HitWidth = focusW,
            HitHeight = focusH
        };

        if (leftCount > 0)
        {
            var leftSlice = sizes.Take(hover).ToArray();
            var leftStep = leftSpace / leftCount;
            var minStrip = Math.Max(18, leftSlice.Min(s => s.W) * request.MinVisibleFraction);
            leftStep = Math.Max(minStrip, leftStep);
            for (var i = 0; i < hover; i++)
            {
                var (w, h) = sizes[i];
                var x = Math.Max(0, focusX - (hover - i) * leftStep);
                x = Math.Min(x, focusX - minStrip);
                var hitW = Math.Max(minStrip, Math.Min(w, focusX - x));
                items[i] = new CascadeItemLayout
                {
                    Index = i,
                    X = x,
                    Y = yBase + (maxH - h) / 2,
                    Width = w,
                    Height = h,
                    ZIndex = i,
                    Opacity = request.DimmedOpacity,
                    Scale = 1,
                    HitX = x,
                    HitY = yBase + (maxH - h) / 2,
                    HitWidth = hitW,
                    HitHeight = h
                };
            }
        }

        if (rightCount > 0)
        {
            var rightSlice = sizes.Skip(hover + 1).ToArray();
            var rightStep = rightSpace / rightCount;
            var minStrip = Math.Max(18, rightSlice.Min(s => s.W) * request.MinVisibleFraction);
            rightStep = Math.Max(minStrip, rightStep);
            for (var i = hover + 1; i < n; i++)
            {
                var (w, h) = sizes[i];
                var x = focusX + focusW + (i - hover - 1) * rightStep;
                x = Math.Min(x, request.AvailableWidth - minStrip);
                var hitW = Math.Max(minStrip, Math.Min(w, request.AvailableWidth - x));
                items[i] = new CascadeItemLayout
                {
                    Index = i,
                    X = x,
                    Y = yBase + (maxH - h) / 2,
                    Width = w,
                    Height = h,
                    ZIndex = n - (i - hover),
                    Opacity = request.DimmedOpacity,
                    Scale = 1,
                    HitX = x,
                    HitY = yBase + (maxH - h) / 2,
                    HitWidth = hitW,
                    HitHeight = h
                };
            }
        }

        return new CascadeLayoutResult
        {
            Items = items,
            ContentWidth = request.AvailableWidth,
            ContentHeight = request.AvailableHeight
        };
    }

    /// <summary>Distribute row heights: hover &gt; active &gt; others.</summary>
    public static double[] ComputeRowHeights(
        double availableHeight,
        int rowCount,
        int activeIndex,
        int hoverRowIndex,
        double minRowHeight = 96,
        double maxRowHeight = 360,
        double hoverMultiplier = 1.55)
    {
        if (rowCount <= 0 || availableHeight <= 0)
        {
            return Array.Empty<double>();
        }

        var baseH = Math.Clamp(availableHeight / rowCount, minRowHeight, maxRowHeight);
        var heights = Enumerable.Repeat(baseH, rowCount).ToArray();

        if (activeIndex >= 0 && activeIndex < rowCount)
        {
            heights[activeIndex] = Math.Clamp(baseH * 1.12, minRowHeight, maxRowHeight);
        }

        if (hoverRowIndex >= 0 && hoverRowIndex < rowCount)
        {
            heights[hoverRowIndex] = Math.Clamp(baseH * hoverMultiplier, minRowHeight, maxRowHeight);
        }

        var sum = heights.Sum();
        if (sum > availableHeight && sum > 0)
        {
            var scale = availableHeight / sum;
            for (var i = 0; i < rowCount; i++)
            {
                heights[i] = Math.Max(minRowHeight * 0.75, heights[i] * scale);
            }
        }

        return heights;
    }
}
