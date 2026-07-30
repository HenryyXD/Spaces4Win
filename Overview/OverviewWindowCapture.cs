using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spaces4Win.Native;

namespace Spaces4Win.Overview;

/// <summary>
/// Captures a window bitmap for overview cards when DWM thumbnails are blank
/// (cloaked / SW_HIDE / register failure). Result is a freezable ImageSource
/// that WPF can clip with rounded corners (unlike DWM dest rects).
/// </summary>
public static class OverviewWindowCapture
{
    private const int MaxWidthPx = 2560;
    private const int MaxHeightPx = 1600;

    public static ImageSource? TryCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 8 || height < 8)
        {
            return null;
        }

        // Absurd sizes (multi-monitor spanning ghosts) → skip; icon fallback instead.
        if (width > MaxWidthPx || height > MaxHeightPx)
        {
            return null;
        }

        var windowDc = NativeMethods.GetWindowDC(hwnd);
        if (windowDc == IntPtr.Zero)
        {
            windowDc = NativeMethods.GetDC(hwnd);
        }

        if (windowDc == IntPtr.Zero)
        {
            return null;
        }

        var memDc = NativeMethods.CreateCompatibleDC(windowDc);
        var bmp = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
        if (memDc == IntPtr.Zero || bmp == IntPtr.Zero)
        {
            if (bmp != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bmp);
            }

            if (memDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memDc);
            }

            NativeMethods.ReleaseDC(hwnd, windowDc);
            return null;
        }

        var old = NativeMethods.SelectObject(memDc, bmp);
        try
        {
            // Full-content first (DirectComposition); fall back to classic PrintWindow.
            var ok = NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT)
                     || NativeMethods.PrintWindow(hwnd, memDc, 0);
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
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.SelectObject(memDc, old);
            NativeMethods.DeleteObject(bmp);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(hwnd, windowDc);
        }
    }
}
