using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Spaces4Win.Native;
using MediaColor = System.Windows.Media.Color;

namespace Spaces4Win.Overview;

/// <summary>
/// Floating no-activate ghost that follows the cursor during overview card drag.
/// Hosts a DWM thumbnail of the dragged HWND when possible.
/// </summary>
public sealed class OverviewDragGhostWindow : Window
{
    private readonly OverviewThumbnailService _thumbs;
    private readonly IntPtr _sourceHwnd;
    private readonly Border _previewHost;
    private bool _disposed;

    public OverviewDragGhostWindow(IntPtr sourceHwnd, double widthDip, double heightDip)
    {
        _sourceHwnd = sourceHwnd;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        Width = widthDip;
        Height = heightDip;
        Opacity = 0.92;

        _previewHost = new Border
        {
            Width = widthDip,
            Height = heightDip,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xE0, 0x18, 0x1A, 0x22)),
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF)),
            BorderThickness = new Thickness(2),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.45
            }
        };
        Content = _previewHost;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyToolWindowStyle(hwnd);
        _thumbs = new OverviewThumbnailService(hwnd);
    }

    public void ShowAtScreenDip(double leftDip, double topDip)
    {
        Left = leftDip;
        Top = topDip;
        if (!IsVisible)
        {
            Show();
        }

        UpdateThumbnail();
    }

    public void UpdateThumbnail()
    {
        if (_disposed || _sourceHwnd == IntPtr.Zero)
        {
            return;
        }

        var thumb = _thumbs.EnsureRegistered(_sourceHwnd);
        if (thumb == IntPtr.Zero)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var w = Math.Max(8, ActualWidth > 0 ? ActualWidth : Width);
        var h = Math.Max(8, ActualHeight > 0 ? ActualHeight : Height);
        _thumbs.Update(
            thumb,
            0,
            0,
            (int)Math.Round(w * dpi.DpiScaleX),
            (int)Math.Round(h * dpi.DpiScaleY),
            opacity: 230);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeThumbs();
        base.OnClosed(e);
    }

    public void DisposeThumbs()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _thumbs.Dispose();
    }

    private static void ApplyToolWindowStyle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW
              | NativeMethods.WS_EX_NOACTIVATE
              | NativeMethods.WS_EX_TRANSPARENT;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        NativeMethods.ApplyExtendedStyleFrame(hwnd);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }
}
