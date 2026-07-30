using System.Text;
using Spaces4Win.Native;

namespace Spaces4Win.Core;

/// <summary>
/// Decides which top-level HWNDs participate in per-monitor workspaces.
/// Uses Alt+Tab / taskbar-style heuristics (Raymond Chen + GlazeWM): styles and
/// ownership, not per-app class/title blacklists.
/// </summary>
public static class WindowClassifier
{
    /// <summary>
    /// Shell chrome that is top-level but never a workspace participant.
    /// Kept minimal — app helpers are rejected by style heuristics below.
    /// </summary>
    private static readonly HashSet<string> ShellClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "ForegroundStaging",
        "XamlExplorerHostIslandWindow",
        "PseudoConsoleWindow",
        "SysShadow",
        "tooltips_class32",
        "TaskListThumbnailWnd",
        "Windows.Internal.Shell.TabProxyWindow",
        "MultitaskingViewFrame",
        "EdgeUiInputTopWndClass",
        "Shell_Flyout",
        "DV2ControlHost",
        "ImmersiveLauncher",
        "NativeHWNDHost",
        "DWM Notification Window",
        "Dwm"
    };

    /// <summary>System ghost titles that sometimes pass style heuristics.</summary>
    private static readonly HashSet<string> RejectedTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "DWM Notification Window",
        "Default IME",
        "MSCTFIME UI"
    };

    public static bool IsManagedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        if (hwnd == NativeMethods.GetShellWindow())
        {
            return false;
        }

        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CHILD) != 0)
        {
            return false;
        }

        // Message-pump / modal-blocked helpers (e.g. Java AwtToolkit).
        if ((style & NativeMethods.WS_DISABLED) != 0)
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        // Same rules the shell uses for Alt+Tab / taskbar eligibility.
        // WS_EX_APPWINDOW opts a window back in despite tool/noactivate/owner.
        var forceAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;

        // GlazeWM: skip windows that cannot take focus / are absent from the task switcher.
        if (!forceAppWindow && (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
        {
            return false;
        }

        if (!forceAppWindow && (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        if (!forceAppWindow && owner != IntPtr.Zero)
        {
            return false;
        }

        // Root-owner check (Raymond Chen Alt+Tab cluster representative).
        // APPWINDOW is treated as if it had no owner.
        if (!forceAppWindow)
        {
            var rootOwner = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
            if (rootOwner != IntPtr.Zero && rootOwner != hwnd)
            {
                return false;
            }
        }

        // Owned menus without a caption bar (Notepad++ autocomplete, KeePass menus).
        if (owner != IntPtr.Zero && (style & NativeMethods.WS_CAPTION) == 0)
        {
            return false;
        }

        var className = GetClassName(hwnd);
        if (ShellClassNames.Contains(className))
        {
            return false;
        }

        // UWP host frame without a title is an empty shell host, not an app.
        if (string.Equals(className, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
        {
            var frameTitle = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(frameTitle))
            {
                return false;
            }
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        var title = GetWindowTitle(hwnd).Trim();
        if (RejectedTitles.Contains(title))
        {
            return false;
        }

        // Helpers almost always leave the title equal to the class name
        // (CicMarshalWnd, TGitCacheWindow, SpotifyLauncher, …). Real apps set a UI title.
        if (!string.IsNullOrEmpty(title) &&
            string.Equals(title, className, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Untitled ghosts that are not on-screen (or minimized).
        if (string.IsNullOrWhiteSpace(title) &&
            !NativeMethods.IsWindowVisible(hwnd) &&
            !NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        // Strip / band / 0×0 helpers. Real apps are larger or minimized.
        if (NativeMethods.GetWindowRect(hwnd, out var rect) && !NativeMethods.IsIconic(hwnd))
        {
            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0 || w < 100 || h < 50)
            {
                return false;
            }
        }

        // When the window is currently visible (and not merely cloaked), require it
        // to be the Alt+Tab representative of its owner cluster.
        if (NativeMethods.IsWindowVisible(hwnd) && !IsCloaked(hwnd) && !IsAltTabRepresentative(hwnd))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Raymond Chen classic Alt+Tab test: among an owner cluster, only the root
    /// owner (or the walk result when popups are hidden) is listed.
    /// See https://devblogs.microsoft.com/oldnewthing/20071008-00/?p=24863
    /// </summary>
    public static bool IsAltTabRepresentative(IntPtr hwnd)
    {
        var walk = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        if (walk == IntPtr.Zero)
        {
            walk = hwnd;
        }

        while (true)
        {
            var last = NativeMethods.GetLastActivePopup(walk);
            if (last == IntPtr.Zero || last == walk)
            {
                break;
            }

            if (NativeMethods.IsWindowVisible(last))
            {
                // Visible popup found — representative stays <paramref name="walk"/>
                // (typically the root owner), not the popup itself.
                break;
            }

            walk = last;
        }

        return walk == hwnd;
    }

    /// <summary>
    /// Filter for overview / transition thumbnails.
    /// Includes Spaces4Win-hidden windows (DWM cloak or SW_HIDE) so inactive
    /// workspaces still appear in Caps+` overview.
    /// </summary>
    public static bool IsOverviewEligible(IntPtr hwnd)
    {
        if (!IsManagedWindow(hwnd))
        {
            return false;
        }

        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var iconic = NativeMethods.IsIconic(hwnd);
        var cloaked = IsCloaked(hwnd);
        var visible = NativeMethods.IsWindowVisible(hwnd);

        if (NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            // Skip tiny tool surfaces only when visibly on-screen (not cloaked/hidden/min).
            if (!iconic && visible && !cloaked && (w < 120 || h < 60))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            var hr = NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_CLOAKED,
                out var cloaked,
                sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        _ = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static IEnumerable<IntPtr> EnumerateTopLevelWindows()
    {
        var list = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (IsManagedWindow(hWnd))
            {
                list.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        return list;
    }
}
