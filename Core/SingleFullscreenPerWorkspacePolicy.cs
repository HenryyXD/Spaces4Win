namespace Spaces4Win.Core;

/// <summary>
/// Pure policy for <see cref="Config.AppConfig.EnforceSingleFullscreenPerWorkspace"/>.
/// </summary>
public static class SingleFullscreenPerWorkspacePolicy
{
    /// <summary>
    /// Returns workspace windows (other than <paramref name="exceptHwnd"/>) that are
    /// currently fullscreen and should exit when another window joins or enters FS.
    /// </summary>
    public static IReadOnlyList<IntPtr> SelectPeersToExit(
        IntPtr exceptHwnd,
        IEnumerable<IntPtr> workspaceWindows,
        Func<IntPtr, bool> isFullscreen)
    {
        ArgumentNullException.ThrowIfNull(workspaceWindows);
        ArgumentNullException.ThrowIfNull(isFullscreen);

        var result = new List<IntPtr>();
        foreach (var hwnd in workspaceWindows)
        {
            if (hwnd == IntPtr.Zero || hwnd == exceptHwnd)
            {
                continue;
            }

            if (isFullscreen(hwnd))
            {
                result.Add(hwnd);
            }
        }

        return result;
    }
}
