using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Spaces4Win.Native;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace Spaces4Win;

/// <summary>Non-activating status chip (save/load feedback). No Windows toast notifications.</summary>
public sealed class StatusToastWindow : Window
{
    private readonly DispatcherTimer _autoHide;
    private bool _closing;

    public StatusToastWindow(string message, TimeSpan? duration = null)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF0, 0x1C, 0x1E, 0x26)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.4
            },
            Child = new TextBlock
            {
                Text = message,
                Foreground = MediaBrushes.White,
                FontSize = 13,
                MaxWidth = 320,
                TextWrapping = TextWrapping.Wrap
            }
        };

        Content = root;

        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ApplyToolWindowStyle(hwnd);
        };

        _autoHide = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(2.2) };
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            RequestClose();
        };
    }

    public void ShowAnchoredTo(WorkspaceIndicatorWindow indicator, System.Drawing.Rectangle monitorBoundsPx)
    {
        var dpi = VisualTreeHelper.GetDpi(indicator);
        var indLeft = indicator.Left;
        var indTop = indicator.Top;
        var indW = Math.Max(indicator.ActualWidth, 1);
        var indH = Math.Max(indicator.ActualHeight, 1);

        Show();
        UpdateLayout();

        var toastW = Math.Max(ActualWidth, 1);
        var toastH = Math.Max(ActualHeight, 1);
        var monLeft = monitorBoundsPx.Left / dpi.DpiScaleX;
        var monTop = monitorBoundsPx.Top / dpi.DpiScaleY;
        var monRight = monitorBoundsPx.Right / dpi.DpiScaleX;
        var monBottom = monitorBoundsPx.Bottom / dpi.DpiScaleY;

        var left = indLeft + (indW - toastW) / 2;
        left = Math.Clamp(left, monLeft + 8, monRight - toastW - 8);

        var above = indTop - toastH - 8;
        var below = indTop + indH + 8;
        var top = above >= monTop + 8 ? above : below;
        if (top + toastH > monBottom - 8)
        {
            top = Math.Max(monTop + 8, monBottom - toastH - 8);
        }

        Left = left;
        Top = top;
        _autoHide.Start();
    }

    public void ShowCenteredOnMonitor(System.Drawing.Rectangle monitorBoundsPx)
    {
        Show();
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var toastW = Math.Max(ActualWidth, 1);
        var toastH = Math.Max(ActualHeight, 1);
        var monLeft = monitorBoundsPx.Left / dpi.DpiScaleX;
        var monTop = monitorBoundsPx.Top / dpi.DpiScaleY;
        var monW = monitorBoundsPx.Width / dpi.DpiScaleX;
        var monH = monitorBoundsPx.Height / dpi.DpiScaleY;
        Left = monLeft + (monW - toastW) / 2;
        Top = monTop + monH * 0.12;
        _autoHide.Start();
    }

    private void RequestClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _autoHide.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoHide.Stop();
        base.OnClosed(e);
    }

    private static void ApplyToolWindowStyle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        NativeMethods.ApplyExtendedStyleFrame(hwnd);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
