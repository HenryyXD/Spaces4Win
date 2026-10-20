using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spaces4Win.Core;
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
    private const int ThumbMaxEdge = 960;

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
        if (!TryTargetSize(width, height, out var destW, out var destH))
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
        var bmp = NativeMethods.CreateCompatibleBitmap(windowDc, destW, destH);
        if (memDc == IntPtr.Zero || bmp == IntPtr.Zero)
        {
            Release(hwnd, windowDc, memDc, bmp, IntPtr.Zero);
            return null;
        }

        var old = NativeMethods.SelectObject(memDc, bmp);
        try
        {
            NativeMethods.SetStretchBltMode(memDc, NativeMethods.COLORONCOLOR);
            var ok = PrintInto(hwnd, memDc, destW, destH, width, height);
            if (!ok || IsUniformDark(Sample(memDc, destW, destH)))
            {
                return null;
            }

            return FreezeBitmap(bmp);
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(hwnd, windowDc, memDc, bmp, old);
        }
    }

    /// <summary>
    /// Copies the pixels currently on screen where <paramref name="hwnd"/> sits.
    /// Composited windows (Rider, WPF, browsers) often PrintWindow as solid black
    /// once cloaked; this must run while the window is still visible.
    /// </summary>
    public static ImageSource? TryCaptureVisible(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        if (!NativeMethods.IsWindowVisible(hwnd) ||
            NativeMethods.IsIconic(hwnd) ||
            WindowClassifier.IsCloaked(hwnd))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (!TryTargetSize(width, height, out var destW, out var destH))
        {
            return null;
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bmp = NativeMethods.CreateCompatibleBitmap(screenDc, destW, destH);
        if (memDc == IntPtr.Zero || bmp == IntPtr.Zero)
        {
            Release(IntPtr.Zero, screenDc, memDc, bmp, IntPtr.Zero);
            return null;
        }

        var old = NativeMethods.SelectObject(memDc, bmp);
        try
        {
            NativeMethods.SetStretchBltMode(memDc, NativeMethods.COLORONCOLOR);
            var ok = NativeMethods.StretchBlt(
                memDc,
                0,
                0,
                destW,
                destH,
                screenDc,
                rect.Left,
                rect.Top,
                width,
                height,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            if (!ok || IsUniformDark(Sample(memDc, destW, destH)))
            {
                return null;
            }

            return FreezeBitmap(bmp);
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(IntPtr.Zero, screenDc, memDc, bmp, old);
        }
    }

    /// <summary>
    /// A failed PrintWindow of a cloaked GPU window is a flat near-black bitmap.
    /// A real dark theme still varies across the frame.
    /// </summary>
    internal static bool IsUniformDark(ReadOnlySpan<uint> colorRefs)
    {
        if (colorRefs.Length == 0)
        {
            return true;
        }

        var minL = 255;
        var maxL = 0;
        var sum = 0;
        var counted = 0;
        foreach (var color in colorRefs)
        {
            if (color == NativeMethods.CLR_INVALID)
            {
                continue;
            }

            var r = (int)(color & 0xFF);
            var g = (int)((color >> 8) & 0xFF);
            var b = (int)((color >> 16) & 0xFF);
            var luma = (r * 3 + g * 6 + b) / 10;
            if (luma < minL)
            {
                minL = luma;
            }

            if (luma > maxL)
            {
                maxL = luma;
            }

            sum += luma;
            counted++;
        }

        if (counted == 0)
        {
            return true;
        }

        var mean = sum / counted;
        return maxL - minL < 10 && mean < 28;
    }

    private static bool PrintInto(IntPtr hwnd, IntPtr memDc, int destW, int destH, int srcW, int srcH)
    {
        if (destW == srcW && destH == srcH)
        {
            return NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT)
                   || NativeMethods.PrintWindow(hwnd, memDc, 0);
        }

        var windowDc = NativeMethods.GetWindowDC(hwnd);
        if (windowDc == IntPtr.Zero)
        {
            return false;
        }

        var fullDc = NativeMethods.CreateCompatibleDC(windowDc);
        var fullBmp = NativeMethods.CreateCompatibleBitmap(windowDc, srcW, srcH);
        if (fullDc == IntPtr.Zero || fullBmp == IntPtr.Zero)
        {
            Release(hwnd, windowDc, fullDc, fullBmp, IntPtr.Zero);
            return false;
        }

        var old = NativeMethods.SelectObject(fullDc, fullBmp);
        try
        {
            var ok = NativeMethods.PrintWindow(hwnd, fullDc, NativeMethods.PW_RENDERFULLCONTENT)
                     || NativeMethods.PrintWindow(hwnd, fullDc, 0);
            if (!ok)
            {
                return false;
            }

            return NativeMethods.StretchBlt(
                memDc, 0, 0, destW, destH,
                fullDc, 0, 0, srcW, srcH,
                NativeMethods.SRCCOPY);
        }
        finally
        {
            Release(hwnd, windowDc, fullDc, fullBmp, old);
        }
    }

    private static bool TryTargetSize(int width, int height, out int destW, out int destH)
    {
        destW = 0;
        destH = 0;
        if (width < 8 || height < 8 || width > MaxWidthPx || height > MaxHeightPx)
        {
            return false;
        }

        var scale = Math.Min(1.0, ThumbMaxEdge / (double)Math.Max(width, height));
        destW = Math.Max(8, (int)Math.Round(width * scale));
        destH = Math.Max(8, (int)Math.Round(height * scale));
        return true;
    }

    private static uint[] Sample(IntPtr memDc, int width, int height)
    {
        const int grid = 4;
        var samples = new uint[grid * grid];
        var i = 0;
        for (var y = 0; y < grid; y++)
        {
            for (var x = 0; x < grid; x++)
            {
                var px = Math.Clamp((width * (x * 2 + 1)) / (grid * 2), 0, width - 1);
                var py = Math.Clamp((height * (y * 2 + 1)) / (grid * 2), 0, height - 1);
                samples[i++] = NativeMethods.GetPixel(memDc, px, py);
            }
        }

        return samples;
    }

    private static ImageSource FreezeBitmap(IntPtr bmp)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            bmp,
            IntPtr.Zero,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static void Release(IntPtr hwnd, IntPtr windowDc, IntPtr memDc, IntPtr bmp, IntPtr old)
    {
        if (memDc != IntPtr.Zero && old != IntPtr.Zero)
        {
            NativeMethods.SelectObject(memDc, old);
        }

        if (bmp != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(bmp);
        }

        if (memDc != IntPtr.Zero)
        {
            NativeMethods.DeleteDC(memDc);
        }

        if (windowDc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(hwnd, windowDc);
        }
    }
}
