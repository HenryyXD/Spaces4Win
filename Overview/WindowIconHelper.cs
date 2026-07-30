using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spaces4Win.Native;
using Spaces4Win.Services;

namespace Spaces4Win.Overview;

/// <summary>Resolves a window's app icon for Task View-style overview cards.</summary>
public static class WindowIconHelper
{
    private static readonly Dictionary<IntPtr, ImageSource?> Cache = new();

    public static ImageSource? GetIcon(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        if (Cache.TryGetValue(hwnd, out var cached))
        {
            return cached;
        }

        var source = TryFromWindow(hwnd) ?? TryFromProcess(hwnd);
        Cache[hwnd] = source;
        return source;
    }

    public static void Forget(IntPtr hwnd) => Cache.Remove(hwnd);

    public static void ClearCache() => Cache.Clear();

    private static ImageSource? TryFromWindow(IntPtr hwnd)
    {
        var handles = new[]
        {
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero),
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero),
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL, IntPtr.Zero),
            NativeMethods.GetClassLongPtrCompat(hwnd, NativeMethods.GCL_HICON),
            NativeMethods.GetClassLongPtrCompat(hwnd, NativeMethods.GCL_HICONSM)
        };

        foreach (var hIcon in handles)
        {
            var image = ToImageSource(hIcon, copy: true);
            if (image is not null)
            {
                return image;
            }
        }

        return null;
    }

    private static ImageSource? TryFromProcess(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var path = ProcessPathHelper.TryGetProcessPath(pid);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            return ToImageSource(icon.Handle, copy: true);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ToImageSource(IntPtr hIcon, bool copy, int sizePx = 32)
    {
        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        IntPtr owned = IntPtr.Zero;
        try
        {
            var handle = hIcon;
            if (copy)
            {
                owned = NativeMethods.CopyIcon(hIcon);
                if (owned == IntPtr.Zero)
                {
                    return null;
                }

                handle = owned;
            }

            var px = Math.Clamp(sizePx, 16, 64);
            var source = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(px, px));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (owned != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(owned);
            }
        }
    }
}