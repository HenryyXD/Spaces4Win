using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Spaces4Win.Config;
using Spaces4Win.Native;
using Spaces4Win.Services.Indicator;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPoint = System.Windows.Point;
using WpfToolTip = System.Windows.Controls.ToolTip;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Spaces4Win;

public partial class WorkspaceIndicatorWindow : Window
{
    private readonly Dictionary<int, Border> _dots = new();
    private readonly HashSet<int> _activeVisual = new();
    private bool _moveMode;
    private bool _dragging;
    private bool _resizing;
    private IndicatorResizeEdge _resizeEdge = IndicatorResizeEdge.None;
    private IndicatorResizeEdge _hoverEdge = IndicatorResizeEdge.None;
    private double _resizeAnchorRight;
    private double _resizeAnchorBottom;
    private double _resizeUnscaledWidth = 1;
    private double _resizeUnscaledHeight = 1;
    private WpfPoint _dragOffset;
    private double _opacity = 0.35;
    private double _scale = 1.0;
    private bool _animations = true;
    private System.Drawing.Rectangle _workAreaPx;
    private HwndSource? _hwndSource;
    private int _lastActiveWorkspace = int.MinValue;
    private string _lastIdsKey = "";
    private bool _activePinned;
    private PinnedWindowIndicatorMode _pinnedMode = PinnedWindowIndicatorMode.RingOnly;
    private string _pinnedTooltip = "Fixado";
    private TextBlock? _accessibilityG;

    public string MonitorId { get; }

    /// <summary>Optional localized label for workspace tooltips.</summary>
    public Func<int, string>? WorkspaceLabelFormatter { get; set; }

    public event EventHandler<int>? WorkspaceSelected;
    public event EventHandler? PositionCommitted;
    public event EventHandler? MoveCancelled;
    public event EventHandler? ContextMenuRequested;
    /// <summary>Raised while resizing (live) and on mouse-up with the final scale (0.75–1.5).</summary>
    public event EventHandler<IndicatorScaleChangedEventArgs>? ScaleChanged;

    public WorkspaceIndicatorWindow(string monitorId)
    {
        MonitorId = monitorId;
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        Capsule.MouseLeftButtonDown += OnCapsuleMouseLeftButtonDown;
        Capsule.MouseMove += OnCapsuleMouseMove;
        Capsule.MouseLeave += OnCapsuleMouseLeave;
        MouseRightButtonUp += OnMouseRightButtonUp;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            UpdateCapsuleChrome();
            ApplyToolWindowStyle();
            EnsureTopmost();
            EnsureInsideWorkArea();
        };
        ContentRendered += (_, _) =>
        {
            UpdateCapsuleChrome();
            EnsureTopmost();
            EnsureInsideWorkArea();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(hwnd);
        ApplyToolWindowStyle();
        EnsureTopmost();
    }

    public void ApplyVisualSettings(
        double opacity,
        double scale,
        bool animations,
        PinnedWindowIndicatorMode pinnedMode = PinnedWindowIndicatorMode.RingOnly)
    {
        // Keep the user-facing 0.3–1.0 range — do not compress it.
        _opacity = Math.Clamp(opacity, 0.3, 1.0);
        _scale = Math.Clamp(scale, 0.75, 1.5);
        _animations = animations;
        _pinnedMode = pinnedMode;
        Background = MediaBrushes.Transparent;
        Capsule.LayoutTransform = new ScaleTransform(_scale, _scale);
        UpdateCapsuleChrome();
        ApplyPinnedVisuals();
        ApplyToolWindowStyle();
        RelayoutAndClamp(persist: false);
    }

    public void SetPinnedTooltipText(string text)
    {
        _pinnedTooltip = string.IsNullOrWhiteSpace(text) ? "Fixado" : text;
        ApplyPinnedVisuals();
    }

    /// <summary>
    /// Reflects whether the foreground window on this monitor is sticky/pinned across workspaces.
    /// </summary>
    public void SetActivePinned(bool pinned)
    {
        if (_activePinned == pinned)
        {
            return;
        }

        _activePinned = pinned;
        ApplyPinnedVisuals();
        RelayoutAndClamp(persist: true);
    }

    public void SetMoveMode(bool enabled)
    {
        _moveMode = enabled;
        Focusable = enabled;
        if (!_moveMode)
        {
            _dragging = false;
            if (IsMouseCaptured && !_resizing)
            {
                ReleaseMouseCapture();
            }
        }
        else
        {
            Focus();
        }

        UpdateCapsuleCursor();
        UpdateCapsuleChrome();
    }

    /// <summary>Apply scale without recentering; used during edge resize and for peer indicators.</summary>
    public void ApplyScale(double scale, IndicatorResizeEdge anchorEdges = IndicatorResizeEdge.None)
    {
        scale = Math.Clamp(scale, 0.75, 1.5);
        if (Math.Abs(scale - _scale) < 0.0005 && anchorEdges == IndicatorResizeEdge.None)
        {
            return;
        }

        var prevW = Math.Max(ActualWidth, 1);
        var prevH = Math.Max(ActualHeight, 1);
        var prevLeft = Left;
        var prevTop = Top;
        _scale = scale;
        Capsule.LayoutTransform = new ScaleTransform(_scale, _scale);
        UpdateLayout();
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);

        if (anchorEdges.HasFlag(IndicatorResizeEdge.Left))
        {
            Left = prevLeft + prevW - width;
        }

        if (anchorEdges.HasFlag(IndicatorResizeEdge.Top))
        {
            Top = prevTop + prevH - height;
        }

        if (anchorEdges == IndicatorResizeEdge.None)
        {
            RelayoutAndClamp(persist: false, prevW, prevH);
        }
        else
        {
            EnsureInsideWorkArea();
        }
    }

    public void UpdateDots(IReadOnlyList<int> workspaceIds, int activeWorkspace)
    {
        var ordered = workspaceIds.OrderBy(i => i).ToList();
        var idsKey = string.Join(',', ordered);
        if (idsKey == _lastIdsKey && activeWorkspace == _lastActiveWorkspace && _dots.Count == ordered.Count)
        {
            ApplyPinnedVisuals();
            return;
        }

        var idsChanged = idsKey != _lastIdsKey;
        var activeChanged = activeWorkspace != _lastActiveWorkspace;
        var hadContent = _dots.Count > 0;
        _lastIdsKey = idsKey;
        _lastActiveWorkspace = activeWorkspace;

        UpdateLayout();
        var previousWidth = Math.Max(ActualWidth, 1);
        var previousHeight = Math.Max(ActualHeight, 1);

        var keep = ordered.ToHashSet();
        foreach (var id in _dots.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            DotsHost.Items.Remove(_dots[id]);
            _dots.Remove(id);
            _activeVisual.Remove(id);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var id = ordered[i];
            var shouldBeActive = id == activeWorkspace;
            if (!_dots.TryGetValue(id, out var dot))
            {
                dot = CreateDot(id);
                _dots[id] = dot;
                DotsHost.Items.Insert(Math.Min(i, DotsHost.Items.Count), dot);
                SetDotActive(dot, shouldBeActive, animate: false, pinned: shouldBeActive && _activePinned);
                if (shouldBeActive)
                {
                    _activeVisual.Add(id);
                }

                continue;
            }

            var currentIndex = DotsHost.Items.IndexOf(dot);
            if (currentIndex != i && currentIndex >= 0)
            {
                DotsHost.Items.RemoveAt(currentIndex);
                DotsHost.Items.Insert(Math.Min(i, DotsHost.Items.Count), dot);
            }

            var wasActive = _activeVisual.Contains(id);
            if (shouldBeActive == wasActive)
            {
                if (shouldBeActive)
                {
                    SetDotActive(dot, true, animate: false, pinned: _activePinned);
                }

                continue;
            }

            SetDotActive(dot, shouldBeActive, animate: shouldBeActive && activeChanged && _animations, pinned: shouldBeActive && _activePinned);
            if (shouldBeActive)
            {
                _activeVisual.Add(id);
            }
            else
            {
                _activeVisual.Remove(id);
            }
        }

        ApplyPinnedVisuals();

        if (idsChanged && hadContent)
        {
            RelayoutAndClamp(persist: true, previousWidth, previousHeight);
        }
        else
        {
            RelayoutAndClamp(persist: false);
        }
    }

    private void ApplyPinnedVisuals()
    {
        foreach (var (id, dot) in _dots)
        {
            var active = id == _lastActiveWorkspace;
            SetDotActive(dot, active, animate: false, pinned: active && _activePinned);
        }

        EnsureAccessibilityG();
    }

    private void EnsureAccessibilityG()
    {
        var showG = _activePinned && _pinnedMode == PinnedWindowIndicatorMode.RingPlusG;
        if (showG)
        {
            if (_accessibilityG is null)
            {
                _accessibilityG = new TextBlock
                {
                    Text = "G",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new MediaFontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF)),
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 2, 0),
                    IsHitTestVisible = false
                };
                CapsuleContent.Children.Add(_accessibilityG);
            }

            _accessibilityG.Visibility = Visibility.Visible;
        }
        else if (_accessibilityG is not null)
        {
            _accessibilityG.Visibility = Visibility.Collapsed;
        }
    }

    private void RelayoutAndClamp(bool persist, double? previousWidth = null, double? previousHeight = null)
    {
        UpdateLayout();
        if (_workAreaPx.IsEmpty)
        {
            return;
        }

        var dpi = GetDpi();
        var work = IndicatorPositionService.ToWorkAreaDip(_workAreaPx, dpi.X, dpi.Y);
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);

        double x = Left;
        double y = Top;

        if (previousWidth is double pw && previousHeight is double ph)
        {
            var midX = Left + pw / 2.0;
            var midY = Top + ph / 2.0;
            var centerX = (work.Left + work.Right) / 2.0;
            var centerY = (work.Top + work.Bottom) / 2.0;
            if (midX >= centerX)
            {
                x += pw - width;
            }

            if (midY >= centerY)
            {
                y += ph - height;
            }
        }

        var clamped = IndicatorPositionService.Clamp(x, y, width, height, work);
        Left = clamped.X;
        Top = clamped.Y;
        EnsureTopmost();

        if (persist && (Math.Abs(clamped.X - x) > 0.1 || Math.Abs(clamped.Y - y) > 0.1 || previousWidth is not null))
        {
            PositionCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PlaceInWorkArea(System.Drawing.Rectangle workAreaPx, double? savedLeftDip, double? savedTopDip)
    {
        _workAreaPx = workAreaPx;
        UpdateLayout();
        var dpi = GetDpi();
        var work = IndicatorPositionService.ToWorkAreaDip(workAreaPx, dpi.X, dpi.Y);
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);

        double x;
        double y;
        if (savedLeftDip is double sl && savedTopDip is double st)
        {
            x = sl;
            y = st;
        }
        else
        {
            var def = IndicatorPositionService.DefaultBottomRight(width, height, work);
            x = def.X;
            y = def.Y;
        }

        var clamped = IndicatorPositionService.Clamp(x, y, width, height, work);
        Left = clamped.X;
        Top = clamped.Y;
        EnsureTopmost();
    }

    public void EnsureInsideWorkArea() => RelayoutAndClamp(persist: false);

    private void UpdateCapsuleChrome()
    {
        // Full slider range maps to visible alpha (0.3 → ~77, 1.0 → 255).
        var alpha = (byte)Math.Clamp((int)Math.Round(_opacity * 255), 77, 255);
        Capsule.Background = new SolidColorBrush(MediaColor.FromArgb(alpha, 0x1E, 0x24, 0x30));
        Capsule.BorderBrush = _moveMode
            ? new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF))
            : new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        Capsule.BorderThickness = new Thickness(_moveMode ? 2 : 1);
    }

    private Border CreateDot(int id)
    {
        const double size = IndicatorLayoutMetrics.DotSizeDip;
        var outerSize = size + IndicatorLayoutMetrics.OuterRingExtraDip;
        var text = new TextBlock
        {
            Text = id.ToString(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new MediaFontFamily("Segoe UI"),
            Foreground = MediaBrushes.White,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var inner = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Child = text,
            Background = new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        var outer = new Border
        {
            Width = outerSize,
            Height = outerSize,
            CornerRadius = new CornerRadius(outerSize / 2),
            Margin = new Thickness(IndicatorLayoutMetrics.DotMarginDip, 0, IndicatorLayoutMetrics.DotMarginDip, 0),
            // No Padding: BorderThickness would shrink the slot and left-align a fixed-size child.
            Padding = new Thickness(0),
            Background = MediaBrushes.Transparent,
            BorderBrush = MediaBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = inner,
            Tag = id,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = FormatWorkspaceLabel(id),
            VerticalAlignment = WpfVerticalAlignment.Center,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        outer.PreviewMouseLeftButtonDown += OnDotMouseLeftButtonDown;
        return outer;
    }

    private void SetDotActive(Border outer, bool active, bool animate, bool pinned)
    {
        if (outer.Child is not Border inner)
        {
            return;
        }

        if (outer.RenderTransform is ScaleTransform existing)
        {
            existing.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            existing.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        outer.RenderTransform = null;

        if (active)
        {
            inner.Background = new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF));
            inner.BorderBrush = MediaBrushes.Transparent;
            inner.BorderThickness = new Thickness(0);
            if (inner.Child is TextBlock tb)
            {
                tb.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0x10, 0x14));
            }

            if (pinned)
            {
                outer.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0x4C, 0xC2, 0xFF));
                outer.BorderThickness = new Thickness(IndicatorLayoutMetrics.OuterRingThicknessDip);
                outer.Background = MediaBrushes.Transparent;
                outer.ToolTip = CreatePinnedToolTip();
                ToolTipService.SetInitialShowDelay(outer, 180);
                ToolTipService.SetBetweenShowDelay(outer, 80);
                ToolTipService.SetShowDuration(outer, 2200);
            }
            else
            {
                outer.BorderBrush = MediaBrushes.Transparent;
                outer.BorderThickness = new Thickness(0);
                outer.Background = MediaBrushes.Transparent;
                outer.ToolTip = FormatWorkspaceLabel(_lastActiveWorkspace);
                ToolTipService.SetInitialShowDelay(outer, 600);
            }

            if (animate)
            {
                var transform = new ScaleTransform(1, 1);
                outer.RenderTransform = transform;
                outer.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
                var anim = new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };
                anim.Completed += (_, _) =>
                {
                    transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    if (ReferenceEquals(outer.RenderTransform, transform))
                    {
                        outer.RenderTransform = null;
                    }
                };
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
        }
        else
        {
            inner.Background = new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            inner.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            inner.BorderThickness = new Thickness(1);
            if (inner.Child is TextBlock tb)
            {
                tb.Foreground = MediaBrushes.White;
            }

            outer.BorderBrush = MediaBrushes.Transparent;
            outer.BorderThickness = new Thickness(0);
            outer.Background = MediaBrushes.Transparent;
            if (outer.Tag is int id)
            {
                outer.ToolTip = FormatWorkspaceLabel(id);
            }
        }
    }

    private string FormatWorkspaceLabel(int workspaceId)
    {
        return WorkspaceLabelFormatter?.Invoke(workspaceId) ?? $"Workspace {workspaceId}";
    }

    private WpfToolTip CreatePinnedToolTip()
    {
        return new WpfToolTip
        {
            Content = _pinnedTooltip,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            HasDropShadow = false,
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 11,
            Background = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0x1E, 0x24, 0x30)),
            Foreground = MediaBrushes.White,
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1)
        };
    }

    private void OnDotMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_moveMode || sender is not Border { Tag: int id })
        {
            return;
        }

        WorkspaceSelected?.Invoke(this, id);
        e.Handled = true;
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            return;
        }

        ContextMenuRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnCapsuleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging || _resizing)
        {
            return;
        }

        var pos = e.GetPosition(Capsule);
        // Hit-test in unscaled capsule space (LayoutTransform does not affect GetPosition).
        _hoverEdge = IndicatorResizeHitTest.HitTest(pos.X, pos.Y, Capsule.ActualWidth, Capsule.ActualHeight);
        UpdateCapsuleCursor();
    }

    private void OnCapsuleMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_resizing)
        {
            return;
        }

        _hoverEdge = IndicatorResizeEdge.None;
        UpdateCapsuleCursor();
    }

    private void UpdateCapsuleCursor()
    {
        var resizeCursor = CursorForEdge(_hoverEdge);
        if (resizeCursor is not null)
        {
            Capsule.Cursor = resizeCursor;
            return;
        }

        Capsule.Cursor = _moveMode
            ? System.Windows.Input.Cursors.SizeAll
            : System.Windows.Input.Cursors.Hand;
    }

    private static System.Windows.Input.Cursor? CursorForEdge(IndicatorResizeEdge edge) => edge switch
    {
        IndicatorResizeEdge.None => null,
        IndicatorResizeEdge.Left or IndicatorResizeEdge.Right => System.Windows.Input.Cursors.SizeWE,
        IndicatorResizeEdge.Top or IndicatorResizeEdge.Bottom => System.Windows.Input.Cursors.SizeNS,
        IndicatorResizeEdge.Left | IndicatorResizeEdge.Top
            or IndicatorResizeEdge.Right | IndicatorResizeEdge.Bottom => System.Windows.Input.Cursors.SizeNWSE,
        IndicatorResizeEdge.Right | IndicatorResizeEdge.Top
            or IndicatorResizeEdge.Left | IndicatorResizeEdge.Bottom => System.Windows.Input.Cursors.SizeNESW,
        _ => System.Windows.Input.Cursors.SizeAll
    };

    private void OnCapsuleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(Capsule);
        var edge = IndicatorResizeHitTest.HitTest(pos.X, pos.Y, Capsule.ActualWidth, Capsule.ActualHeight);
        if (edge != IndicatorResizeEdge.None)
        {
            BeginResize(edge);
            e.Handled = true;
            return;
        }

        if (!_moveMode)
        {
            return;
        }

        _dragging = true;
        _dragOffset = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void BeginResize(IndicatorResizeEdge edge)
    {
        _resizing = true;
        _resizeEdge = edge;
        _hoverEdge = edge;
        UpdateLayout();
        var w = Math.Max(ActualWidth, 1);
        var h = Math.Max(ActualHeight, 1);
        _resizeUnscaledWidth = w / Math.Max(_scale, 0.01);
        _resizeUnscaledHeight = h / Math.Max(_scale, 0.01);
        _resizeAnchorRight = Left + w;
        _resizeAnchorBottom = Top + h;
        UpdateCapsuleCursor();
        CaptureMouse();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_resizing)
        {
            var screen = PointToScreen(e.GetPosition(this));
            var dpi = GetDpi();
            var px = screen.X / dpi.X;
            var py = screen.Y / dpi.Y;
            var scale = IndicatorResizeHitTest.ComputeScaleFromScreen(
                _resizeEdge,
                px,
                py,
                Left,
                Top,
                _resizeAnchorRight,
                _resizeAnchorBottom,
                _resizeUnscaledWidth,
                _resizeUnscaledHeight);
            ApplyScale(scale, _resizeEdge);
            ScaleChanged?.Invoke(this, new IndicatorScaleChangedEventArgs(scale, isFinal: false));
            e.Handled = true;
            return;
        }

        if (!_dragging || !_moveMode)
        {
            return;
        }

        var screenPos = PointToScreen(e.GetPosition(this));
        var dpiScale = GetDpi();
        var work = IndicatorPositionService.ToWorkAreaDip(_workAreaPx, dpiScale.X, dpiScale.Y);
        var x = screenPos.X / dpiScale.X - _dragOffset.X;
        var y = screenPos.Y / dpiScale.Y - _dragOffset.Y;
        var clamped = IndicatorPositionService.Clamp(x, y, ActualWidth, ActualHeight, work);
        Left = clamped.X;
        Top = clamped.Y;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();
            RelayoutAndClamp(persist: false);
            EnsureTopmost();
            ScaleChanged?.Invoke(this, new IndicatorScaleChangedEventArgs(_scale, isFinal: true));
            PositionCommitted?.Invoke(this, EventArgs.Empty);
            _resizeEdge = IndicatorResizeEdge.None;
            var pos = e.GetPosition(Capsule);
            _hoverEdge = IndicatorResizeHitTest.HitTest(pos.X, pos.Y, Capsule.ActualWidth, Capsule.ActualHeight);
            UpdateCapsuleCursor();
            e.Handled = true;
            return;
        }

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        RelayoutAndClamp(persist: false);
        EnsureTopmost();
        PositionCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_resizing)
        {
            _resizing = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            _resizeEdge = IndicatorResizeEdge.None;
            UpdateCapsuleCursor();
            e.Handled = true;
            return;
        }

        if (!_moveMode)
        {
            return;
        }

        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        MoveCancelled?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>
    /// Reasserts TOPMOST without activating so the capsule stays above the taskbar
    /// (both share the TOPMOST band; Explorer often restacks above us).
    /// </summary>
    public void EnsureTopmost()
    {
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        const uint flags = NativeMethods.SWP_NOMOVE
                           | NativeMethods.SWP_NOSIZE
                           | NativeMethods.SWP_NOACTIVATE;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    private void ApplyToolWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        ex &= ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        EnsureTopmost();
    }

    private (double X, double Y) GetDpi()
    {
        var source = PresentationSource.FromVisual(this) ?? _hwndSource;
        var x = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var y = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        if (x <= 0) x = 1;
        if (y <= 0) y = 1;
        return (x, y);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hwndSource = null;
        base.OnClosed(e);
    }
}

public sealed class IndicatorScaleChangedEventArgs : EventArgs
{
    public IndicatorScaleChangedEventArgs(double scale, bool isFinal)
    {
        Scale = Math.Clamp(scale, 0.75, 1.5);
        IsFinal = isFinal;
    }

    public double Scale { get; }
    public bool IsFinal { get; }
}
