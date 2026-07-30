using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Spaces4Win.Config;
using Spaces4Win.Native;
using MediaColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;

namespace Spaces4Win.Services.Transition;

/// <summary>
/// Flash-free curtain transition:
/// 1) BitBlt the work area (matches live pixels — no black first frame)
/// 2) Cover with an opaque window pinned to physical pixels
/// 3) Apply the real workspace switch under the curtain
/// 4) Slide the HWND off-screen (or fade) so the destination is revealed —
///    never animate an inner image over a dark window background (that caused black flash).
/// </summary>
public sealed class CurtainTransitionEngine : IWorkspaceTransitionEngine
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

        CurtainOverlayWindow? overlay = null;
        try
        {
            var bitmap = WorkAreaCapture.TryCapture(context.WorkAreaPx);
            if (bitmap is null)
            {
                context.ApplySwitch();
                return;
            }

            overlay = new CurtainOverlayWindow();
            overlay.Prepare(context, bitmap);
            overlay.Show();
            overlay.PinToWorkArea(context.WorkAreaPx);
            overlay.ApplyToolWindowStyle();

            // Two render passes so the screenshot is on-screen before HWND mutations.
            await WaitRenderedAsync(dispatcher, cancellationToken).ConfigureAwait(true);

            context.ApplySwitch();

            await overlay.AnimateAwayAsync(context.Options, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                overlay?.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task WaitRenderedAsync(Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Delay(16, cancellationToken).ConfigureAwait(true);
    }
}

internal static class WorkAreaCapture
{
    public static BitmapSource? TryCapture(System.Drawing.Rectangle workAreaPx)
    {
        if (workAreaPx.Width <= 0 || workAreaPx.Height <= 0)
        {
            return null;
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bmp = NativeMethods.CreateCompatibleBitmap(screenDc, workAreaPx.Width, workAreaPx.Height);
        var old = NativeMethods.SelectObject(memDc, bmp);

        try
        {
            // CAPTUREBLT includes layered/HW-composited content that SRCCOPY alone misses.
            var ok = NativeMethods.BitBlt(
                memDc,
                0,
                0,
                workAreaPx.Width,
                workAreaPx.Height,
                screenDc,
                workAreaPx.Left,
                workAreaPx.Top,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            if (!ok)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bmp,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.SelectObject(memDc, old);
            NativeMethods.DeleteObject(bmp);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

/// <summary>
/// Opaque screenshot curtain. Slide moves the HWND (reveals dest). Fade uses layered opacity.
/// </summary>
public sealed class CurtainOverlayWindow : Window
{
    private readonly WpfImage _image = new()
    {
        Stretch = Stretch.Fill,
        SnapsToDevicePixels = true
    };

    private TransitionDirection _direction = TransitionDirection.ToHigher;

    public CurtainOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        // Fallback only — Prepare replaces this with the screenshot brush.
        Background = new SolidColorBrush(MediaColor.FromRgb(0x20, 0x22, 0x28));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _image;
    }

    public void Prepare(TransitionPlayContext context, BitmapSource bitmap)
    {
        _direction = context.Direction;

        // Must be set before the HWND exists (WPF requirement).
        if (context.Options.Style == WorkspaceTransitionStyle.Fade)
        {
            AllowsTransparency = true;
        }

        // Handle first so DPI is real (PresentationSource is null before EnsureHandle).
        _ = new WindowInteropHelper(this).EnsureHandle();
        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        var sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

        Left = context.WorkAreaPx.Left / sx;
        Top = context.WorkAreaPx.Top / sy;
        Width = context.WorkAreaPx.Width / sx;
        Height = context.WorkAreaPx.Height / sy;

        // ImageBrush background = no black flash if content paints a frame late.
        Background = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        _image.Source = bitmap;
        _image.Width = Width;
        _image.Height = Height;
        Opacity = 1;
    }

    public void PinToWorkArea(System.Drawing.Rectangle workAreaPx)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            workAreaPx.Left,
            workAreaPx.Top,
            workAreaPx.Width,
            workAreaPx.Height,
            NativeMethods.SWP_NOACTIVATE);
    }

    public void ApplyToolWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        NativeMethods.ApplyExtendedStyleFrame(hwnd);
    }

    public Task AnimateAwayAsync(TransitionOptions options, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var duration = options.Duration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (options.Style == WorkspaceTransitionStyle.Fade)
        {
            var anim = new DoubleAnimation(1, 0, duration)
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, _) => tcs.TrySetResult();
            BeginAnimation(OpacityProperty, anim);
        }
        else
        {
            // Move the HWND off the work area — dest is already live underneath.
            // Do NOT slide an inner Image over a solid Background (that showed black).
            var from = Left;
            var to = _direction == TransitionDirection.ToHigher
                ? Left - Width
                : Left + Width;
            var anim = new DoubleAnimation(from, to, duration)
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, _) => tcs.TrySetResult();
            BeginAnimation(LeftProperty, anim);
        }

        return tcs.Task;
    }
}
