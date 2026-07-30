using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Spaces4Win.Native;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace Spaces4Win;

/// <summary>
/// Non-activating chip anchored near a floating workspace indicator.
/// </summary>
public sealed class IndicatorToastWindow : Window
{
    private readonly DispatcherTimer _autoHide;
    private bool _closing;

    public event EventHandler? RestartRequested;
    public event EventHandler? Dismissed;

    public IndicatorToastWindow(string message, string actionLabel, string dismissLabel)
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
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF0, 0x1C, 0x1E, 0x26)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.4
            }
        };

        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = MediaBrushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            MaxWidth = 260,
            TextWrapping = TextWrapping.Wrap
        });

        var restart = new Button
        {
            Content = actionLabel,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        restart.Click += (_, _) =>
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
            RequestClose();
        };

        var dismiss = new Button
        {
            Content = dismissLabel,
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            Opacity = 0.85
        };
        dismiss.Click += (_, _) =>
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            RequestClose();
        };

        row.Children.Add(restart);
        row.Children.Add(dismiss);
        root.Child = row;
        Content = root;

        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ApplyToolWindowStyle(hwnd);
        };

        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            Dismissed?.Invoke(this, EventArgs.Empty);
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
