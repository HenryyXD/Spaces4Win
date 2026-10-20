using System.Collections.Concurrent;
using System.Windows.Media;
using Spaces4Win.Core;
using Spaces4Win.Native;
using Spaces4Win.Overview;

namespace Spaces4Win.Services;

/// <summary>
/// Per-HWND bitmap cache populated in background immediately after a window is cloaked
/// or hidden (peek-uncloak → PrintWindow → recloak). The overview reads from this cache
/// without any blocking or Thread.Sleep on the UI thread.
/// </summary>
public sealed class WindowCaptureCache
{
    private readonly WindowVisibilityService _visibility;
    private readonly ConcurrentDictionary<IntPtr, ImageSource?> _cache = new();
    // Guards concurrent captures for the same hwnd.
    private readonly ConcurrentDictionary<IntPtr, byte> _inFlight = new();

    public WindowCaptureCache(WindowVisibilityService visibility)
    {
        _visibility = visibility;
    }

    /// <summary>Try to get a previously captured frame, or null if not yet available.</summary>
    public ImageSource? Get(IntPtr hwnd) =>
        _cache.TryGetValue(hwnd, out var v) ? v : null;

    /// <summary>
    /// Grab a thumbnail while the window is still on screen. Call before cloak/SW_HIDE.
    /// A flat black PrintWindow is discarded so a later peek can try again.
    /// </summary>
    public void CaptureNow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || Get(hwnd) is not null)
        {
            return;
        }

        var shot = OverviewWindowCapture.TryCaptureVisible(hwnd)
                   ?? OverviewWindowCapture.TryCapture(hwnd);
        if (shot is not null)
        {
            _cache[hwnd] = shot;
        }
    }

    /// <summary>Returns true if a capture attempt has been made (result may be null on failure).</summary>
    public bool HasEntry(IntPtr hwnd) => _cache.ContainsKey(hwnd);

    /// <summary>
    /// Schedule a background peek-capture for <paramref name="hwnd"/>. No-op if a capture
    /// is already in flight or the cache already has an entry. Call after HideForWorkspace.
    /// </summary>
    public void RequestCapture(IntPtr hwnd, Action<IntPtr>? onComplete = null)
    {
        if (!NativeMethods.IsWindow(hwnd) || Get(hwnd) is not null)
        {
            return;
        }

        if (!_inFlight.TryAdd(hwnd, 1))
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                ImageSource? result = null;
                _visibility.WithPeekVisible(hwnd, () =>
                {
                    result = OverviewWindowCapture.TryCapture(hwnd);
                });
                if (result is not null)
                {
                    _cache[hwnd] = result;
                }

                onComplete?.Invoke(hwnd);
            }
            catch
            {
                _cache[hwnd] = null;
            }
            finally
            {
                _inFlight.TryRemove(hwnd, out _);
            }
        });
    }

    /// <summary>Forcibly refresh the capture for a window (e.g. after it moves workspaces).</summary>
    public void Invalidate(IntPtr hwnd)
    {
        _cache.TryRemove(hwnd, out _);
    }

    /// <summary>Remove stale entries for windows that no longer exist.</summary>
    public void PruneDeadWindows()
    {
        foreach (var hwnd in _cache.Keys.ToList())
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                _cache.TryRemove(hwnd, out _);
            }
        }
    }

    public void Clear()
    {
        _cache.Clear();
    }
}
