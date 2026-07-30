using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Spaces4Win.Localization;
using Spaces4Win.Native;
using Spaces4Win.Overview;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfRect = System.Windows.Rect;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Spaces4Win.Services.Switcher;

public enum SwitcherOverlayMode
{
    Workspaces,
    Windows
}

public sealed class SwitcherWorkspaceItem
{
    public required int Id { get; init; }
}

public sealed class SwitcherWindowItem
{
    public required IntPtr Hwnd { get; init; }
    public required string Title { get; init; }
}

/// <summary>
/// Modern Alt+Tab-style overlay for Caps+Tab (workspace pills) and Caps+Q (window cards).
/// </summary>
public sealed class SwitcherOverlayWindow : Window
{
    // Card outer footprint (thumb + padding + gap) — keeps selection scale inside box.
    private const double WindowCardSlotWidth = 232;
    private const double WindowCardThumbWidth = 180;
    private const double WindowCardThumbHeight = 110;
    private const double WindowCardThumbRadius = 12;
    private const double WindowCardThumbInset = 3;
    private const double WindowCardMarginX = 10;
    private const double WindowCardMarginY = 8;
    private const double WindowCardPaddingX = 14;
    private const double WindowCardPaddingY = 12;
    private const double OverlayPadX = 64; // shell horizontal padding × 2
    private const double OverlayMinWidthWindows = 640;
    private const double OverlayMinHeightWindows = 310;
    private const double TitleMaxLinesWidthPad = 48;

    private readonly Border _backdrop;
    private readonly Border _shell;
    private readonly TextBlock _title;
    private readonly StackPanel _row;
    private readonly List<FrameworkElement> _items = new();
    private OverviewThumbnailService? _thumbs;
    private int _selectedIndex = -1;
    private SwitcherOverlayMode _mode;
    private System.Drawing.Rectangle _monitorBoundsPx;

    /// <summary>Optional localization for titles / untitled fallback.</summary>
    public Func<ILocalizationService>? Localization { get; set; }

    /// <summary>Fired when the user clicks outside the switcher chrome (cancel).</summary>
    public Action? OutsideClicked { get; set; }

    public SwitcherOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = true;
        SizeToContent = SizeToContent.Manual;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _title = new TextBlock
        {
            FontFamily = new MediaFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 0, 16)
        };

        _row = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        var stack = new StackPanel
        {
            Orientation = WpfOrientation.Vertical,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        stack.Children.Add(_title);
        stack.Children.Add(_row);

        _shell = new Border
        {
            Child = stack,
            Padding = new Thickness(32, 28, 32, 30),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF0, 0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            // No DropShadowEffect — layered/transparent WPF windows + effects can AV on some GPUs.
            Opacity = 0,
            RenderTransform = new ScaleTransform(0.92, 0.92),
            RenderTransformOrigin = new WpfPoint(0.5, 0.5),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            ClipToBounds = false,
            IsHitTestVisible = true
        };
        _shell.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _shell.MouseRightButtonDown += (_, e) => e.Handled = true;

        _backdrop = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x01, 0x00, 0x00, 0x00)),
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            VerticalAlignment = WpfVerticalAlignment.Stretch,
            IsHitTestVisible = true
        };
        _backdrop.MouseLeftButtonDown += OnBackdropMouseDown;
        _backdrop.MouseRightButtonDown += OnBackdropMouseDown;

        var root = new Grid();
        root.Children.Add(_backdrop);
        root.Children.Add(_shell);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            ClearMainWindowIfOwned();
            ApplyToolWindowStyle();
            EnsureTopmost();
        };
        Loaded += (_, _) =>
        {
            ClearMainWindowIfOwned();
            ApplyToolWindowStyle();
            EnsureTopmost();
        };
    }

    private void OnBackdropMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        OutsideClicked?.Invoke();
    }

    public void ShowWorkspaces(
        System.Drawing.Rectangle monitorBoundsPx,
        IReadOnlyList<SwitcherWorkspaceItem> items,
        int selectedIndex,
        int activeWorkspace)
    {
        _mode = SwitcherOverlayMode.Workspaces;
        _monitorBoundsPx = monitorBoundsPx;
        DisposeThumbs();
        _row.Children.Clear();
        _items.Clear();
        ClearFixedOverlaySize();

        for (var i = 0; i < items.Count; i++)
        {
            var id = items[i].Id;
            var pill = BuildWorkspacePill(id, isActiveSlot: id == activeWorkspace);
            _row.Children.Add(pill);
            _items.Add(pill);
        }

        SetSelection(selectedIndex, animate: false);
        CoverMonitor();
        ShowAnimated();
        CoverMonitor();
    }

    public void ShowWindows(
        System.Drawing.Rectangle monitorBoundsPx,
        IReadOnlyList<SwitcherWindowItem> items,
        int selectedIndex)
    {
        _mode = SwitcherOverlayMode.Windows;
        _monitorBoundsPx = monitorBoundsPx;
        DisposeThumbs();
        _row.Children.Clear();
        _items.Clear();

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _thumbs = new OverviewThumbnailService(hwnd);

        for (var i = 0; i < items.Count; i++)
        {
            var card = BuildWindowCard(items[i]);
            _row.Children.Add(card);
            _items.Add(card);
        }

        ApplyFixedWindowsOverlaySize(items.Count);
        // Identity scale before first thumb pass (ctor starts at 0.92).
        if (_shell.RenderTransform is ScaleTransform prep)
        {
            prep.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            prep.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            prep.ScaleX = 1;
            prep.ScaleY = 1;
        }

        SetSelection(selectedIndex, animate: false);
        CoverMonitor();
        ShowAnimated();
        CoverMonitor();
        UpdateLayout();
        ScheduleThumbnailRefresh();
    }

    public void SetSelection(int index, bool animate = true)
    {
        if (_items.Count == 0)
        {
            _selectedIndex = -1;
            _title.Text = "";
            return;
        }

        index = Math.Clamp(index, 0, _items.Count - 1);
        _selectedIndex = index;

        for (var i = 0; i < _items.Count; i++)
        {
            ApplyItemSelected(_items[i], i == index, animate);
        }

        if (_mode == SwitcherOverlayMode.Workspaces &&
            _items[index].Tag is int ws)
        {
            _title.Text = FormatWorkspaceTitle(ws);
        }
        else if (_mode == SwitcherOverlayMode.Windows &&
                 _items[index].Tag is SwitcherWindowItem win)
        {
            // Width is locked — never let the title reflow the shell.
            _title.Text = string.IsNullOrWhiteSpace(win.Title)
                ? UntitledWindowLabel()
                : win.Title.Trim();
        }

        if (_mode == SwitcherOverlayMode.Windows)
        {
            ScheduleThumbnailRefresh();
        }
    }

    private void ApplyFixedWindowsOverlaySize(int windowCount)
    {
        var slots = Math.Max(windowCount, 2);
        var contentWidth = slots * WindowCardSlotWidth;
        var width = Math.Max(OverlayMinWidthWindows, contentWidth + OverlayPadX);
        var height = OverlayMinHeightWindows;

        // Shell is sized; the window itself covers the full monitor for click-outside.
        _shell.MinWidth = width;
        _shell.Width = width;
        _shell.MinHeight = height;
        _shell.ClearValue(HeightProperty);
        _title.Width = Math.Max(120, width - OverlayPadX - TitleMaxLinesWidthPad);
        _title.MaxWidth = _title.Width;
        ClipToBounds = false;
    }

    private void ClearFixedOverlaySize()
    {
        _shell.ClearValue(MinWidthProperty);
        _shell.ClearValue(WidthProperty);
        _shell.ClearValue(MinHeightProperty);
        _shell.ClearValue(HeightProperty);
        _title.ClearValue(WidthProperty);
        _title.ClearValue(MaxWidthProperty);
    }

    /// <summary>Fill the monitor so clicks outside the chrome cancel the switcher.</summary>
    private void CoverMonitor()
    {
        if (_monitorBoundsPx.IsEmpty)
        {
            return;
        }

        _ = new WindowInteropHelper(this).EnsureHandle();
        UpdateLayout();
        var dpi = GetDpi();
        SizeToContent = SizeToContent.Manual;
        IsHitTestVisible = true;
        Left = _monitorBoundsPx.Left / dpi.X;
        Top = _monitorBoundsPx.Top / dpi.Y;
        Width = Math.Max(_monitorBoundsPx.Width / dpi.X, 1);
        Height = Math.Max(_monitorBoundsPx.Height / dpi.Y, 1);
        MinWidth = Width;
        MinHeight = Height;
    }

    public async Task HideAnimatedAsync()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fade.Completed += (_, _) => tcs.TrySetResult();
        _shell.BeginAnimation(OpacityProperty, fade);

        // Never scale while DWM thumbs are registered — dest rects ignore WPF RenderTransform.
        if (_mode != SwitcherOverlayMode.Windows &&
            _shell.RenderTransform is ScaleTransform st)
        {
            var scale = new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        await tcs.Task.ConfigureAwait(true);
        DisposeThumbs();
        Hide();
    }

    private void ShowAnimated()
    {
        if (!IsVisible)
        {
            _ = new WindowInteropHelper(this).EnsureHandle();
            ClearMainWindowIfOwned();
            Show();
            ClearMainWindowIfOwned();
        }

        EnsureTopmost();

        if (_shell.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (_mode == SwitcherOverlayMode.Windows)
            {
                // Identity scale so PointToScreen / TransformToVisual match DWM dest.
                st.ScaleX = 1;
                st.ScaleY = 1;
            }
            else
            {
                st.ScaleX = 0.92;
                st.ScaleY = 0.92;
                var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
        }

        _shell.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void RecenterOnMonitor() => CoverMonitor();

    private void ClearMainWindowIfOwned()
    {
        var app = Application.Current;
        if (app is not null && ReferenceEquals(app.MainWindow, this))
        {
            app.MainWindow = null;
        }
    }

    private Border BuildWorkspacePill(int id, bool isActiveSlot)
    {
        var label = new TextBlock
        {
            Text = id.ToString(),
            FontFamily = new MediaFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        var pill = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(26),
            Margin = new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(MediaColor.FromArgb(
                isActiveSlot ? (byte)0x55 : (byte)0x33, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.5),
            Child = label,
            Tag = id,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new WpfPoint(0.5, 0.5)
        };
        return pill;
    }

    private Border BuildWindowCard(SwitcherWindowItem item)
    {
        var icon = WindowIconHelper.GetIcon(item.Hwnd);
        var thumbRadius = WindowCardThumbRadius;

        var fallback = BuildThumbFallback(icon);
        var thumbHost = new Border
        {
            Tag = "thumb",
            Background = new SolidColorBrush(MediaColor.FromArgb(0x66, 0x1A, 0x1E, 0x28)),
            CornerRadius = new CornerRadius(Math.Max(0, thumbRadius - WindowCardThumbInset)),
            ClipToBounds = true,
            Child = fallback
        };

        // Rounded frame: DWM is rectangular and paints above WPF, so inset the thumb
        // inside a CornerRadius border — the frame bg shows as rounded corners.
        // When DWM fails / blank, the WPF fallback keeps the same rounded look.
        var thumbFrame = new Border
        {
            Width = WindowCardThumbWidth,
            Height = WindowCardThumbHeight,
            CornerRadius = new CornerRadius(thumbRadius),
            Background = new SolidColorBrush(MediaColor.FromArgb(0x88, 0x12, 0x16, 0x20)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(WindowCardThumbInset),
            ClipToBounds = true,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Child = thumbHost,
            Tag = "thumbFrame"
        };
        thumbFrame.Clip = new RectangleGeometry(
            new WpfRect(0, 0, WindowCardThumbWidth, WindowCardThumbHeight),
            thumbRadius,
            thumbRadius);

        var captionRow = new DockPanel
        {
            LastChildFill = true,
            Width = WindowCardThumbWidth,
            MinHeight = 22,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            ClipToBounds = false
        };

        if (icon is not null)
        {
            var iconImage = new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4, 2, 8, 2),
                VerticalAlignment = WpfVerticalAlignment.Center,
                SnapsToDevicePixels = true,
                Tag = "icon"
            };
            DockPanel.SetDock(iconImage, Dock.Left);
            captionRow.Children.Add(iconImage);
        }

        var caption = new TextBlock
        {
            Text = Truncate(item.Title, 32),
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = WpfVerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(0, 2, 4, 2)
        };
        captionRow.Children.Add(caption);

        var stack = new StackPanel
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            ClipToBounds = false
        };
        stack.Children.Add(thumbFrame);
        stack.Children.Add(captionRow);

        return new Border
        {
            Child = stack,
            Width = WindowCardSlotWidth - (WindowCardMarginX * 2),
            Margin = new Thickness(WindowCardMarginX, WindowCardMarginY, WindowCardMarginX, WindowCardMarginY),
            Padding = new Thickness(WindowCardPaddingX, WindowCardPaddingY, WindowCardPaddingX, WindowCardPaddingY),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Tag = item,
            ClipToBounds = false
        };
    }

    private static UIElement BuildThumbFallback(ImageSource? icon)
    {
        if (icon is null)
        {
            return new TextBlock
            {
                Text = "—",
                FontSize = 28,
                Foreground = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center
            };
        }

        return new System.Windows.Controls.Image
        {
            Source = icon,
            Width = 48,
            Height = 48,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Opacity = 0.9
        };
    }

    private void ApplyItemSelected(FrameworkElement element, bool selected, bool animate)
    {
        if (element is not Border border)
        {
            return;
        }

        border.BorderBrush = selected
            ? new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF))
            : new SolidColorBrush(MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        border.BorderThickness = new Thickness(2);
        border.Background = selected
            ? new SolidColorBrush(MediaColor.FromArgb(0x40, 0x4C, 0xC2, 0xFF))
            : new SolidColorBrush(MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

        // Caps+Q: selection is border-only — never scale cards (keeps thumbs full size).
        if (_mode == SwitcherOverlayMode.Windows)
        {
            if (border.RenderTransform is ScaleTransform winScale)
            {
                winScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                winScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                winScale.ScaleX = 1;
                winScale.ScaleY = 1;
            }

            return;
        }

        if (border.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            border.RenderTransform = st;
            border.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        }

        var target = selected ? 1.04 : 1.0;
        if (animate)
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        else
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleX = target;
            st.ScaleY = target;
        }
    }

    private int _thumbRefreshGeneration;

    /// <summary>
    /// DWM dest rects need laid-out ActualWidth/position after CoverMonitor.
    /// Retry across dispatcher passes until hosts are sized (fixes first-open "zoado").
    /// </summary>
    private void ScheduleThumbnailRefresh()
    {
        if (_mode != SwitcherOverlayMode.Windows || _thumbs is null)
        {
            return;
        }

        var generation = ++_thumbRefreshGeneration;

        void Attempt(int tryIndex)
        {
            if (generation != _thumbRefreshGeneration || _thumbs is null || _mode != SwitcherOverlayMode.Windows)
            {
                return;
            }

            CoverMonitor();
            UpdateLayout();
            _shell.UpdateLayout();

            if (RefreshThumbnails() || tryIndex >= 10)
            {
                return;
            }

            var priority = tryIndex < 3
                ? System.Windows.Threading.DispatcherPriority.Loaded
                : System.Windows.Threading.DispatcherPriority.ContextIdle;
            Dispatcher.BeginInvoke(() => Attempt(tryIndex + 1), priority);
        }

        Dispatcher.BeginInvoke(() => Attempt(0), System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(() => Attempt(1), System.Windows.Threading.DispatcherPriority.Render);
        Dispatcher.BeginInvoke(() => Attempt(2), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    /// <summary>Returns true when every card got a valid thumb update or intentional fallback.</summary>
    private bool RefreshThumbnails()
    {
        if (_thumbs is null || _mode != SwitcherOverlayMode.Windows)
        {
            return true;
        }

        var allReady = true;
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i] is not Border card || card.Tag is not SwitcherWindowItem win)
            {
                continue;
            }

            if (card.Child is not StackPanel sp ||
                sp.Children.Count == 0 ||
                sp.Children[0] is not Border thumbFrame)
            {
                continue;
            }

            Border? thumbHost = null;
            if (Equals(thumbFrame.Tag, "thumb"))
            {
                thumbHost = thumbFrame;
            }
            else if (thumbFrame.Child is Border inner && Equals(inner.Tag, "thumb"))
            {
                thumbHost = inner;
            }

            if (thumbHost is null)
            {
                continue;
            }

            // Layout not ready yet — keep fallback, retry later (do not write a 1×1 DWM dest).
            if (thumbHost.ActualWidth < 8 || thumbHost.ActualHeight < 8 ||
                ActualWidth < 8 || ActualHeight < 8)
            {
                allReady = false;
                continue;
            }

            var source = win.Hwnd;
            if (!NativeMethods.IsWindow(source))
            {
                continue;
            }

            var thumb = _thumbs.EnsureRegistered(source);
            if (thumb == IntPtr.Zero)
            {
                EnsureThumbFallback(thumbHost, win.Hwnd);
                continue;
            }

            // Blank / useless DWM sources (e.g. system ghosts) → keep rounded WPF fallback.
            if (NativeMethods.DwmQueryThumbnailSourceSize(thumb, out var src) != 0 ||
                src.cx < 8 ||
                src.cy < 8)
            {
                _thumbs.Unregister(source);
                EnsureThumbFallback(thumbHost, win.Hwnd);
                continue;
            }

            WpfPoint topLeft;
            WpfPoint bottomRight;
            try
            {
                // Same approach as OverviewWindow: DIPs relative to this window × DPI → DWM client px.
                // Avoid PointToScreen (sensitive to transient RenderTransforms / multi-monitor origin).
                var toWindow = thumbHost.TransformToVisual(this);
                topLeft = toWindow.Transform(new WpfPoint(0, 0));
                bottomRight = toWindow.Transform(
                    new WpfPoint(thumbHost.ActualWidth, thumbHost.ActualHeight));
            }
            catch
            {
                allReady = false;
                continue;
            }

            var dpi = GetDpi();
            var left = (int)Math.Round(topLeft.X * dpi.X);
            var top = (int)Math.Round(topLeft.Y * dpi.Y);
            var right = (int)Math.Round(bottomRight.X * dpi.X);
            var bottom = (int)Math.Round(bottomRight.Y * dpi.Y);
            if (right - left < 8 || bottom - top < 8)
            {
                allReady = false;
                continue;
            }

            // Live DWM thumb fills the inset host; rounded frame stays outside dest rect.
            thumbHost.Child = null;
            _thumbs.Update(thumb, left, top, right, bottom, opacity: 255, visible: true);
        }

        return allReady;
    }

    private static void EnsureThumbFallback(Border thumbHost, IntPtr hwnd)
    {
        if (thumbHost.Child is not null)
        {
            return;
        }

        thumbHost.Child = BuildThumbFallback(WindowIconHelper.GetIcon(hwnd));
    }

    private void DisposeThumbs()
    {
        _thumbs?.Dispose();
        _thumbs = null;
    }

    private void ApplyToolWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        EnsureTopmost();
    }

    private void EnsureTopmost()
    {
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    private (double X, double Y) GetDpi()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = PresentationSource.FromVisual(this);
            if (source is null && hwnd != IntPtr.Zero)
            {
                source = HwndSource.FromHwnd(hwnd);
            }

            var x = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var y = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            if (x <= 0) x = 1;
            if (y <= 0) y = 1;
            return (x, y);
        }
        catch
        {
            return (1, 1);
        }
    }

    private string Truncate(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return UntitledWindowLabel();
        }

        text = text.Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    private string FormatWorkspaceTitle(int workspaceId)
    {
        var loc = Localization?.Invoke();
        return loc?.Format("Overview.Workspace", workspaceId) ?? $"Workspace {workspaceId}";
    }

    private string UntitledWindowLabel()
    {
        var loc = Localization?.Invoke();
        return loc?.Get("Switcher.UntitledWindow") ?? "Window";
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeThumbs();
        base.OnClosed(e);
    }
}
