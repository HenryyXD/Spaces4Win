using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Spaces4Win.Config;
using Spaces4Win.Native;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSize = System.Windows.Size;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Spaces4Win.Services.Transition;

/// <summary>
/// Instant workspace switch + lightweight per-monitor HUD feedback.
/// Real windows change immediately; only a small toast animates on that monitor.
/// </summary>
public sealed class HudWorkspaceTransitionEngine : IWorkspaceTransitionEngine
{
    public async Task PlayAsync(TransitionPlayContext context, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher
                         ?? throw new InvalidOperationException("No WPF dispatcher");

        if (!dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(
                    () => PlayAsync(context, cancellationToken),
                    DispatcherPriority.Normal)
                .Task.Unwrap()
                .ConfigureAwait(true);
            return;
        }

        context.ApplySwitch();

        if (context.Options.Style == WorkspaceTransitionStyle.None)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceTransitionHudWindow? hud = null;
        try
        {
            hud = new WorkspaceTransitionHudWindow();
            hud.PrepareContent(context);
            hud.Show();
            hud.ApplyChrome();
            hud.PlaceCentered(context);
            hud.EnsureTopmost();
            cancellationToken.ThrowIfCancellationRequested();
            await hud.PlayAsync(context.Options, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                hud?.Close();
            }
            catch
            {
                // ignore
            }
        }
    }
}

/// <summary>Small toast centered on the target monitor work area.</summary>
public sealed class WorkspaceTransitionHudWindow : Window
{
    private readonly Border _card;
    private readonly TextBlock _label;
    private readonly StackPanel _dots;
    private TransitionDirection _direction = TransitionDirection.ToHigher;
    private double _centerLeft;
    private double _centerTop;

    public WorkspaceTransitionHudWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _label = new TextBlock
        {
            FontFamily = new MediaFontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        _dots = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var stack = new StackPanel { Orientation = WpfOrientation.Vertical };
        stack.Children.Add(_label);
        stack.Children.Add(_dots);

        _card = new Border
        {
            Padding = new Thickness(22, 14, 22, 12),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xE0, 0x1E, 0x24, 0x30)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = stack,
            // No RenderTransform — translating content clips against the HWND client area.
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        Content = _card;
        Opacity = 0;
    }

    public void PrepareContent(TransitionPlayContext context)
    {
        _direction = context.Direction;
        var to = context.Incoming.WorkspaceId;
        _label.Text = context.Direction switch
        {
            TransitionDirection.ToHigher => $"{to} ›",
            TransitionDirection.ToLower => $"‹ {to}",
            _ => to.ToString()
        };

        _dots.Children.Clear();
        foreach (var id in context.WorkspaceIds)
        {
            _dots.Children.Add(CreateDot(id == to));
        }
    }

    public void PlaceCentered(TransitionPlayContext context)
    {
        UpdateLayout();
        var dpi = GetDpiForWorkArea(context.WorkAreaPx);
        var workLeft = context.WorkAreaPx.Left / dpi.X;
        var workTop = context.WorkAreaPx.Top / dpi.Y;
        var workW = context.WorkAreaPx.Width / dpi.X;
        var workH = context.WorkAreaPx.Height / dpi.Y;

        var w = ActualWidth > 1 ? ActualWidth : Math.Max(DesiredSize.Width, 1);
        var h = ActualHeight > 1 ? ActualHeight : Math.Max(DesiredSize.Height, 1);

        _centerLeft = workLeft + (workW - w) / 2;
        _centerTop = workTop + (workH - h) / 2 - Math.Min(40, workH * 0.06);

        // Keep fully inside the work area (no neighbor spill).
        _centerLeft = Math.Clamp(_centerLeft, workLeft, workLeft + Math.Max(0, workW - w));
        _centerTop = Math.Clamp(_centerTop, workTop, workTop + Math.Max(0, workH - h));

        Left = _centerLeft;
        Top = _centerTop;

        // Physical-pixel move, then restack above fullscreen HWNDs.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var pxLeft = (int)Math.Round(_centerLeft * dpi.X);
            var pxTop = (int)Math.Round(_centerTop * dpi.Y);
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                pxLeft,
                pxTop,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        EnsureTopmost();
    }

    public void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW
              | NativeMethods.WS_EX_NOACTIVATE
              | NativeMethods.WS_EX_TRANSPARENT
              | NativeMethods.WS_EX_LAYERED;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        NativeMethods.ApplyExtendedStyleFrame(hwnd);
        EnsureTopmost();
    }

    /// <summary>
    /// Reasserts TOPMOST so the HUD paints above borderless fullscreen apps
    /// (Rider, games in windowed fullscreen, etc.).
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

    public Task PlayAsync(TransitionOptions options, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storyboard = new Storyboard();

        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(() =>
        {
            try
            {
                storyboard.Stop();
            }
            catch
            {
                // ignore
            }

            tcs.TrySetCanceled(cancellationToken);
            registration.Dispose();
        });

        if (cancellationToken.IsCancellationRequested)
        {
            registration.Dispose();
            tcs.TrySetCanceled(cancellationToken);
            return tcs.Task;
        }

        var enter = options.Duration;
        var holdMs = options.Speed.HudHoldMilliseconds();
        var exit = TimeSpan.FromMilliseconds(Math.Max(70, enter.TotalMilliseconds * 0.55));
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        var slidePx = Math.Min(48, Math.Max(24, ActualWidth * 0.35));
        if (options.Style == WorkspaceTransitionStyle.Slide)
        {
            Left = _direction == TransitionDirection.ToHigher
                ? _centerLeft + slidePx
                : _centerLeft - slidePx;
        }
        else
        {
            Left = _centerLeft;
        }

        Top = _centerTop;

        var fadeIn = new DoubleAnimation(0, 1, enter) { EasingFunction = easeOut };
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);

        if (options.Style == WorkspaceTransitionStyle.Slide)
        {
            var slideIn = new DoubleAnimation(Left, _centerLeft, enter) { EasingFunction = easeOut };
            Storyboard.SetTarget(slideIn, this);
            Storyboard.SetTargetProperty(slideIn, new PropertyPath(LeftProperty));
            storyboard.Children.Add(slideIn);
        }

        var fadeOut = new DoubleAnimation(1, 0, exit)
        {
            BeginTime = enter + TimeSpan.FromMilliseconds(holdMs),
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeOut);

        if (options.Style == WorkspaceTransitionStyle.Slide)
        {
            var outLeft = _direction == TransitionDirection.ToHigher
                ? _centerLeft - slidePx * 0.5
                : _centerLeft + slidePx * 0.5;
            var slideOut = new DoubleAnimation(_centerLeft, outLeft, exit)
            {
                BeginTime = enter + TimeSpan.FromMilliseconds(holdMs),
                EasingFunction = easeIn
            };
            Storyboard.SetTarget(slideOut, this);
            Storyboard.SetTargetProperty(slideOut, new PropertyPath(LeftProperty));
            storyboard.Children.Add(slideOut);
        }

        storyboard.Completed += (_, _) =>
        {
            registration.Dispose();
            tcs.TrySetResult();
        };
        storyboard.Begin();
        return tcs.Task;
    }

    private static Ellipse CreateDot(bool active) => new()
    {
        Width = active ? 8 : 6,
        Height = active ? 8 : 6,
        Margin = new Thickness(3, 0, 3, 0),
        Fill = active
            ? new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF))
            : new SolidColorBrush(MediaColor.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
        SnapsToDevicePixels = true
    };

    private (double X, double Y) GetDpiForWorkArea(System.Drawing.Rectangle workAreaPx)
    {
        _ = new WindowInteropHelper(this).EnsureHandle();

        // Prefer DPI of the monitor that owns this work area (per-monitor aware).
        var center = new NativeMethods.POINT
        {
            X = workAreaPx.Left + workAreaPx.Width / 2,
            Y = workAreaPx.Top + workAreaPx.Height / 2
        };
        var hMon = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMon != IntPtr.Zero &&
            NativeMethods.GetDpiForMonitor(hMon, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 &&
            dpiX > 0 && dpiY > 0)
        {
            return (dpiX / 96.0, dpiY / 96.0);
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        var sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
        return (sx, sy);
    }
}
