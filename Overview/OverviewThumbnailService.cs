using Spaces4Win.Native;

namespace Spaces4Win.Overview;

/// <summary>Owns DWM thumbnail handles for the overview destination HWND.</summary>
public sealed class OverviewThumbnailService : IDisposable
{
    private readonly IntPtr _destination;
    private readonly Dictionary<IntPtr, IntPtr> _thumbs = new();
    private bool _disposed;

    public OverviewThumbnailService(IntPtr destinationHwnd)
    {
        _destination = destinationHwnd;
    }

    public IntPtr EnsureRegistered(IntPtr sourceHwnd)
    {
        if (_disposed || sourceHwnd == IntPtr.Zero || _destination == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (_thumbs.TryGetValue(sourceHwnd, out var existing) && existing != IntPtr.Zero)
        {
            return existing;
        }

        if (NativeMethods.DwmRegisterThumbnail(_destination, sourceHwnd, out var thumb) != 0 || thumb == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        _thumbs[sourceHwnd] = thumb;
        return thumb;
    }

    public void Update(IntPtr thumb, int left, int top, int right, int bottom, byte opacity = 255, bool visible = true)
    {
        if (_disposed || thumb == IntPtr.Zero)
        {
            return;
        }

        if (right <= left + 2 || bottom <= top + 2)
        {
            return;
        }

        var dest = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            // Full-frame (including non-client chrome) for more faithful Mission Control thumbs.
            dwFlags = NativeMethods.DWM_TNP_VISIBLE
                      | NativeMethods.DWM_TNP_RECTDESTINATION
                      | NativeMethods.DWM_TNP_OPACITY,
            fVisible = visible,
            fSourceClientAreaOnly = false,
            opacity = opacity,
            rcDestination = dest
        };
        _ = NativeMethods.DwmUpdateThumbnailProperties(thumb, ref props);
    }

    public void Unregister(IntPtr sourceHwnd)
    {
        if (!_thumbs.TryGetValue(sourceHwnd, out var thumb))
        {
            return;
        }

        if (thumb != IntPtr.Zero)
        {
            NativeMethods.DwmUnregisterThumbnail(thumb);
        }

        _thumbs.Remove(sourceHwnd);
    }

    public void UnregisterAll()
    {
        foreach (var kv in _thumbs.ToList())
        {
            if (kv.Value != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.DwmUnregisterThumbnail(kv.Value);
                }
                catch
                {
                    // ignore
                }
            }
        }

        _thumbs.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }
}
