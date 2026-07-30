using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// Idempotent shutdown: reveal inactive hidden windows as minimized, no activation.
/// Persists window→workspace layout before reveal so the next start can remap open apps.
/// </summary>
public sealed class ShutdownCoordinator
{
    private readonly WorkspaceManager? _workspaceManager;
    private readonly WindowVisibilityService _visibility;
    private readonly SessionJournal _journal;
    private readonly HotkeyService? _hotkeys;
    private readonly IndicatorService? _indicators;
    private readonly Action? _persistLayout;
    private readonly Action? _exitAllFullscreen;
    private bool _ran;

    public ShutdownCoordinator(
        WorkspaceManager? workspaceManager,
        WindowVisibilityService visibility,
        SessionJournal journal,
        HotkeyService? hotkeys,
        IndicatorService? indicators,
        Action? persistLayout = null,
        Action? exitAllFullscreen = null)
    {
        _workspaceManager = workspaceManager;
        _visibility = visibility;
        _journal = journal;
        _hotkeys = hotkeys;
        _indicators = indicators;
        _persistLayout = persistLayout;
        _exitAllFullscreen = exitAllFullscreen;
    }

    public void Execute()
    {
        if (_ran)
        {
            return;
        }

        _ran = true;

        if (_workspaceManager is not null)
        {
            _workspaceManager.CommandsEnabled = false;
        }

        _hotkeys?.Clear();

        if (_workspaceManager is null)
        {
            return;
        }

        try
        {
            _persistLayout?.Invoke();
        }
        catch
        {
            // still proceed with reveal
        }

        try
        {
            _exitAllFullscreen?.Invoke();
        }
        catch
        {
            // still proceed with reveal
        }

        var snapshot = _workspaceManager.SnapshotManagedWindows();
        var ownership = _visibility.Snapshot();

        foreach (var (hwnd, _, _, isActive) in snapshot)
        {
            if (!isActive)
            {
                continue;
            }

            if (ownership.TryGetValue(hwnd, out var o) && o == VisibilityOwnership.HiddenBySpaces4Win)
            {
                _visibility.ShowForWorkspace(hwnd);
            }
        }

        var inactiveHidden = snapshot
            .Where(s => !s.IsActiveWorkspace)
            .Select(s => s.Hwnd)
            .Where(h => ownership.TryGetValue(h, out var o) && o == VisibilityOwnership.HiddenBySpaces4Win)
            .Distinct()
            .ToList();

        _visibility.RevealHiddenAsMinimizedNoActivate(inactiveHidden);

        _journal.Write(new SessionJournalDocument
        {
            CleanShutdown = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            Windows = Array.Empty<JournalWindowEntry>().ToList()
        });
        _journal.Clear();

        _indicators?.Dispose();
    }

    public void RecoverIfNeeded()
    {
        var doc = _journal.TryLoad();
        if (doc is null || doc.CleanShutdown)
        {
            _journal.Clear();
            return;
        }

        var toReveal = new List<IntPtr>();
        foreach (var entry in doc.Windows.Where(w => w.HiddenBySpaces4Win))
        {
            var hwnd = new IntPtr(entry.Hwnd);
            if (!NativeMethods.IsWindow(hwnd))
            {
                continue;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (entry.ProcessId != 0 && pid != entry.ProcessId)
            {
                continue;
            }

            toReveal.Add(hwnd);
        }

        // Ownership maps are empty on cold start — bypass the HiddenBySpaces4Win gate.
        _visibility.ForceRevealNoActivate(toReveal);
        _journal.Clear();
    }
}
