using Spaces4Win.Core;
using Spaces4Win.Native;
using System.IO;

namespace Spaces4Win.Services.Presets;

/// <summary>
/// Pure planning for preset apply: which open HWNDs to keep/close and which layout rows to launch.
/// </summary>
public static class PresetApplyPlanner
{
    public sealed record Plan(
        IReadOnlyList<WindowLayoutMatcher.LayoutMatch> Keep,
        IReadOnlyDictionary<IntPtr, WindowLayoutMatcher.LayoutMatch> KeepByHwnd,
        IReadOnlyList<IntPtr> Close,
        IReadOnlyList<(string MonitorId, LayoutWindowEntry Entry)> Launch);

    public static Plan Build(WindowLayoutDocument layout, IReadOnlyList<WindowIdentity> openWindows)
    {
        var matches = WindowLayoutMatcher.Match(layout, openWindows);
        var keep = matches.Values.ToList();
        var matchedHwnds = matches.Keys.ToHashSet();
        var close = openWindows
            .Select(w => w.Hwnd)
            .Where(h => !matchedHwnds.Contains(h))
            .Distinct()
            .ToList();

        var claimedEntries = new HashSet<LayoutWindowEntry>(
            matches.Values.Select(m => m.Entry));

        var launch = new List<(string MonitorId, LayoutWindowEntry Entry)>();
        foreach (var mon in layout.Monitors)
        {
            foreach (var entry in mon.Windows)
            {
                if (claimedEntries.Contains(entry))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ProcessPath))
                {
                    continue;
                }

                launch.Add((mon.MonitorId, entry));
            }
        }

        return new Plan(keep, matches, close, launch);
    }
}

/// <summary>
/// Applies a session preset: close extras, place matches, launch missing (explicit user confirm only).
/// </summary>
public sealed class PresetApplyService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly Func<LayoutWindowEntry, bool>? _launchProcess;

    public PresetApplyService(
        WorkspaceManager workspaceManager,
        Func<LayoutWindowEntry, bool>? launchProcess = null)
    {
        _workspaceManager = workspaceManager;
        _launchProcess = launchProcess ?? DefaultLaunch;
    }

    public event EventHandler<string>? Log;

    public async Task<PresetApplyResult> ApplyAsync(
        PresetSlot slot,
        CancellationToken cancellationToken = default)
    {
        if (slot.Layout.Monitors.Count == 0)
        {
            return new PresetApplyResult(0, 0, 0, 0, Array.Empty<string>());
        }

        _workspaceManager.EnsureWorkspacesFromLayoutDocument(slot.Layout);

        var open = WindowClassifier.EnumerateTopLevelWindows()
            .Where(h => NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h))
            .Select(ProcessPathHelper.CaptureIdentity)
            .ToList();

        var plan = PresetApplyPlanner.Build(slot.Layout, open);

        foreach (var hwnd in plan.Close)
        {
            try
            {
                if (NativeMethods.IsWindow(hwnd))
                {
                    NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"Close failed for {hwnd}: {ex.Message}");
            }
        }

        foreach (var (hwnd, match) in plan.KeepByHwnd)
        {
            PlaceMatched(hwnd, match);
        }

        var launched = 0;
        var launchFailures = new List<string>();
        foreach (var (monitorId, entry) in plan.Launch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (_launchProcess!(entry))
                {
                    launched++;
                }
                else
                {
                    launchFailures.Add(string.IsNullOrWhiteSpace(entry.Title)
                        ? entry.ProcessPath
                        : entry.Title);
                }
            }
            catch (Exception ex)
            {
                launchFailures.Add(entry.Title.Length > 0 ? entry.Title : entry.ProcessPath);
                Log?.Invoke(this, $"Launch failed for {entry.ProcessPath}: {ex.Message}");
            }
        }

        // Wait for windows even if Start returned quickly — shell launches are async.
        if (plan.Launch.Count > 0)
        {
            await PlaceLaunchedWindowsAsync(slot.Layout, plan, cancellationToken).ConfigureAwait(true);
        }

        _workspaceManager.EnsureWorkspacesFromLayoutDocument(slot.Layout);
        RestoreActiveWorkspacesPublic(slot.Layout);
        _workspaceManager.ReapplyVisibilityFromAssignments();

        return new PresetApplyResult(
            plan.KeepByHwnd.Count,
            plan.Close.Count,
            launched,
            launchFailures.Count,
            launchFailures);
    }

    private void PlaceMatched(IntPtr hwnd, WindowLayoutMatcher.LayoutMatch match)
    {
        var monitorInfo = _workspaceManager.Monitors
            .FirstOrDefault(m => string.Equals(m.MonitorId, match.PreferredMonitorId, StringComparison.OrdinalIgnoreCase))
            ?? _workspaceManager.Monitors.FirstOrDefault();

        var monitorId = monitorInfo?.MonitorId ?? match.PreferredMonitorId;
        if (match.Entry.IsSticky)
        {
            _workspaceManager.SetSticky(hwnd, monitorId, sticky: true);
            return;
        }

        var workspace = match.Entry.Workspace is >= 1 and <= 9
            ? match.Entry.Workspace
            : _workspaceManager.GetActiveWorkspace(monitorId);

        _workspaceManager.EnsureWorkspaceExists(monitorId, workspace);
        _workspaceManager.AssignWindowToWorkspace(hwnd, monitorId, workspace, applyVisibility: true);

        if (match.Entry.IsMinimized && NativeMethods.IsWindow(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
        }
    }

    private async Task PlaceLaunchedWindowsAsync(
        WindowLayoutDocument layout,
        PresetApplyPlanner.Plan originalPlan,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        var remaining = originalPlan.Launch.ToList();

        while (remaining.Count > 0 && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(true);

            var open = WindowClassifier.EnumerateTopLevelWindows()
                .Where(h => NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h))
                .Select(ProcessPathHelper.CaptureIdentity)
                .ToList();

            // Only match against still-unplaced launch rows.
            var partial = new WindowLayoutDocument
            {
                Monitors = remaining
                    .GroupBy(t => t.MonitorId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new LayoutMonitorEntry
                    {
                        MonitorId = g.Key,
                        Windows = g.Select(t => t.Entry).ToList()
                    })
                    .ToList()
            };

            var matches = WindowLayoutMatcher.Match(partial, open);
            if (matches.Count == 0)
            {
                continue;
            }

            foreach (var (hwnd, match) in matches)
            {
                PlaceMatched(hwnd, match);
                remaining.RemoveAll(t => ReferenceEquals(t.Entry, match.Entry) ||
                                         (PathsEqual(t.Entry.ProcessPath, match.Entry.ProcessPath) &&
                                          t.Entry.Workspace == match.Entry.Workspace &&
                                          string.Equals(t.Entry.Title, match.Entry.Title, StringComparison.Ordinal)));
            }
        }
    }

    private void RestoreActiveWorkspacesPublic(WindowLayoutDocument layout)
    {
        foreach (var mon in layout.Monitors)
        {
            if (mon.ActiveWorkspace is < 1 or > 9)
            {
                continue;
            }

            if (_workspaceManager.Monitors.All(m =>
                    !string.Equals(m.MonitorId, mon.MonitorId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _workspaceManager.EnsureWorkspaceExists(mon.MonitorId, mon.ActiveWorkspace);
            _workspaceManager.SwitchWorkspace(mon.MonitorId, mon.ActiveWorkspace);
        }
    }

    private static bool DefaultLaunch(LayoutWindowEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ProcessPath))
        {
            return false;
        }

        // Host processes are not real apps — starting them does nothing useful.
        var fileName = Path.GetFileName(entry.ProcessPath);
        if (fileName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("SystemSettings.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("TextInputHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(entry.ProcessPath))
        {
            return false;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = entry.ProcessPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(entry.ProcessPath) ?? string.Empty
        };

        // With UseShellExecute=true, Start often returns null even when the app launched.
        _ = System.Diagnostics.Process.Start(psi);
        return true;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record PresetApplyResult(
    int Kept,
    int Closed,
    int Launched,
    int LaunchFailures,
    IReadOnlyList<string> FailedTitles);
