using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Spaces4Win.Config;
using Spaces4Win.Native;
using Spaces4Win.Overview;
using MediaColor = System.Windows.Media.Color;

namespace Spaces4Win.Services.Transition;

/// <summary>
/// Per-monitor transition overlay. DWM thumbnails are synced each frame because
/// they do not follow WPF RenderTransform.
/// </summary>
public sealed class TransitionOverlayWindow : Window
{
    private readonly List<(IntPtr Thumb, int BaseLeft, int BaseTop, int BaseRight, int BaseBottom, bool Outgoing)> _thumbs = new();
    private OverviewThumbnailService? _thumbService;
    private TransitionPlayContext? _context;
    private double _outgoingOffset;
    private double _incomingOffset;
    private byte _outgoingOpacity = 255;
    private byte _incomingOpacity = 255;
    private bool _renderingHooked;
    private EventHandler? _renderingHandler;

    public TransitionOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(MediaColor.FromArgb(0xFF, 0x0C, 0x0E, 0x12));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = true;
        Content = new Grid(); // opaque cover; thumbs drawn by DWM above
    }

    public void Prepare(TransitionPlayContext context)
    {
        _context = context;
        var dpi = GetDpiFallback();
        Left = context.WorkAreaPx.Left / dpi.X;
        Top = context.WorkAreaPx.Top / dpi.Y;
        Width = context.WorkAreaPx.Width / dpi.X;
        Height = context.WorkAreaPx.Height / dpi.Y;

        var widthDip = Width;
        if (context.Direction == TransitionDirection.ToHigher)
        {
            _outgoingOffset = 0;
            _incomingOffset = widthDip;
        }
        else
        {
            _outgoingOffset = 0;
            _incomingOffset = -widthDip;
        }
    }

    public void ActivateOverlayNoFocus()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        _thumbService = new OverviewThumbnailService(hwnd);
    }

    public void RegisterThumbnails()
    {
        if (_context is null || _thumbService is null)
        {
            return;
        }

        var work = _context.WorkAreaPx;
        RegisterScene(_context.Outgoing, work, outgoing: true);
        RegisterScene(_context.Incoming, work, outgoing: false);
    }

    private void RegisterScene(TransitionScene scene, System.Drawing.Rectangle work, bool outgoing)
    {
        foreach (var win in scene.Windows)
        {
            if (win.IsMinimized || !NativeMethods.IsWindow(win.Hwnd))
            {
                continue;
            }

            var thumb = _thumbService!.EnsureRegistered(win.Hwnd);
            if (thumb == IntPtr.Zero)
            {
                continue;
            }

            // Clip to work area in physical px, relative to overlay origin (work.Left/Top).
            var left = Math.Max(win.LeftPx, work.Left) - work.Left;
            var top = Math.Max(win.TopPx, work.Top) - work.Top;
            var right = Math.Min(win.RightPx, work.Right) - work.Left;
            var bottom = Math.Min(win.BottomPx, work.Bottom) - work.Top;
            if (right - left < 8 || bottom - top < 8)
            {
                continue;
            }

            _thumbs.Add((thumb, left, top, right, bottom, outgoing));
        }
    }

    public void SyncThumbnails()
    {
        if (_thumbService is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX;
        var sy = dpi.DpiScaleY;

        foreach (var (thumb, bl, bt, br, bb, outgoing) in _thumbs)
        {
            var offsetDip = outgoing ? _outgoingOffset : _incomingOffset;
            var opacity = outgoing ? _outgoingOpacity : _incomingOpacity;
            var ox = (int)Math.Round(offsetDip * sx);
            _thumbService.Update(
                thumb,
                bl + ox,
                bt,
                br + ox,
                bb,
                opacity,
                visible: opacity > 0);
        }
    }

    public Task AnimateAsync(TransitionOptions options, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var widthDip = Math.Max(ActualWidth, Width);
        double outTo;
        double inFrom;
        double inTo = 0;
        if (_context?.Direction == TransitionDirection.ToHigher)
        {
            outTo = -widthDip;
            inFrom = widthDip;
        }
        else
        {
            outTo = widthDip;
            inFrom = -widthDip;
        }

        _incomingOffset = inFrom;
        SyncThumbnails();

        var duration = options.Duration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (options.Style == WorkspaceTransitionStyle.Fade)
        {
            AnimateFade(duration, ease, () => tcs.TrySetResult());
        }
        else
        {
            AnimateSlide(0, outTo, inFrom, inTo, duration, ease, () => tcs.TrySetResult());
        }

        return tcs.Task;
    }

    private void AnimateSlide(
        double outFrom, double outTo,
        double inFrom, double inTo,
        TimeSpan duration,
        IEasingFunction ease,
        Action onComplete)
    {
        var start = Environment.TickCount64;
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Clamp((Environment.TickCount64 - start) / duration.TotalMilliseconds, 0, 1);
            var e = ease.Ease(t);
            _outgoingOffset = outFrom + (outTo - outFrom) * e;
            _incomingOffset = inFrom + (inTo - inFrom) * e;
            SyncThumbnails();
            if (t >= 1 && tick is not null)
            {
                CompositionTarget.Rendering -= tick;
                onComplete();
            }
        };
        CompositionTarget.Rendering += tick;
    }

    private void AnimateFade(TimeSpan duration, IEasingFunction ease, Action onComplete)
    {
        _outgoingOffset = 0;
        _incomingOffset = 0;
        var start = Environment.TickCount64;
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Clamp((Environment.TickCount64 - start) / duration.TotalMilliseconds, 0, 1);
            var e = ease.Ease(t);
            _outgoingOpacity = (byte)Math.Clamp((int)Math.Round(255 * (1 - e)), 0, 255);
            _incomingOpacity = (byte)Math.Clamp((int)Math.Round(255 * e), 0, 255);
            SyncThumbnails();
            if (t >= 1)
            {
                CompositionTarget.Rendering -= tick;
                onComplete();
            }
        };
        CompositionTarget.Rendering += tick;
    }

    private void HookRendering()
    {
        if (_renderingHooked)
        {
            return;
        }

        _renderingHandler = (_, _) => SyncThumbnails();
        CompositionTarget.Rendering += _renderingHandler;
        _renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }

        if (_renderingHandler is not null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
        }

        _renderingHooked = false;
        _renderingHandler = null;
    }

    public void Cleanup()
    {
        UnhookRendering();
        _thumbService?.Dispose();
        _thumbService = null;
        _thumbs.Clear();
    }

    private (double X, double Y) GetDpiFallback()
    {
        try
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is { } ct)
            {
                return (ct.TransformToDevice.M11, ct.TransformToDevice.M22);
            }
        }
        catch
        {
            // ignore
        }

        return (1, 1);
    }

    protected override void OnClosed(EventArgs e)
    {
        Cleanup();
        base.OnClosed(e);
    }
}
