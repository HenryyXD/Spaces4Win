using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Spaces4Win.Core;
using Spaces4Win.Localization;
using Spaces4Win.Native;
using Spaces4Win.Overview;
using WpfPoint = System.Windows.Point;

namespace Spaces4Win.Services;

public sealed class OverviewService : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly Func<ILocalizationService> _loc;
    private readonly List<OverviewWindow> _open = new();
    private bool _closingAll;
    private bool _disposed;
    private bool _syncingSearch;

    public OverviewService(WorkspaceManager workspaceManager, Func<ILocalizationService> loc)
    {
        _workspaceManager = workspaceManager;
        _loc = loc;
    }

    public IReadOnlyList<OverviewWindow> OpenWindows => _open;

    public bool IsOpen => _open.Count > 0;

    /// <summary>
    /// Toggle Mission Control-style overview on every connected monitor.
    /// Esc / selecting a window / toggling again closes all overlays together.
    /// Drag-drop refreshes overlays in place without closing.
    /// </summary>
    public void Toggle()
    {
        if (_open.Count > 0)
        {
            CloseAll();
            return;
        }

        var monitors = _workspaceManager.Monitors.ToList();
        if (monitors.Count == 0)
        {
            return;
        }

        var loc = _loc();
        OverviewWindow? preferred = null;
        var focusMonitorId = _workspaceManager.ResolveMonitorIdForHotkeys();

        foreach (var monitor in monitors)
        {
            var bounds = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
            var window = new OverviewWindow(
                monitor.MonitorId,
                bounds,
                _workspaceManager,
                this,
                loc,
                requestRefresh: RefreshAll);
            window.Closed += OnOverviewClosed;
            _open.Add(window);
            window.Show();

            if (preferred is null ||
                string.Equals(monitor.MonitorId, focusMonitorId, StringComparison.OrdinalIgnoreCase))
            {
                preferred = window;
            }
        }

        preferred?.ClaimKeyboardFocus();
        Application.Current?.Dispatcher.BeginInvoke(
            () =>
            {
                SelectFirstGlobally(activateHost: false);
                preferred?.ClaimKeyboardFocus();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Legacy name kept for call sites; opens overview on all monitors.</summary>
    public void ToggleForMonitorUnderCursor() => Toggle();

    /// <summary>
    /// LL-hook entry while overview is open. Swallows keys so Rider/etc. do not see them.
    /// Returns false only for CapsLock itself so HotkeyService can track hold/release.
    /// Caps+` closes here (do not rely on GetAsyncKeyState after Caps is swallowed).
    /// </summary>
    public bool TryHandleGlobalKey(uint vkCode, ModifierKeys modifiers, bool capsHeld)
    {
        if (!IsOpen || _disposed)
        {
            return false;
        }

        if (vkCode == NativeMethods.VK_CAPITAL)
        {
            return false;
        }

        // Caps+` (and OemTilde / VK_OEM_3) closes overview — same as Esc / toggle.
        if (capsHeld &&
            modifiers == ModifierKeys.None &&
            IsOverviewToggleVirtualKey(vkCode))
        {
            RunOnUi(CloseAll);
            return true;
        }

        var key = KeyInterop.KeyFromVirtualKey((int)vkCode);
        if (key == Key.None)
        {
            return false;
        }

        RunOnUi(() => DispatchGlobalKey(key, modifiers));
        return true;
    }

    private static bool IsOverviewToggleVirtualKey(uint vkCode)
    {
        // US / many layouts: VK_OEM_3 (0xC0). WPF may report Oem3 or OemTilde.
        if (vkCode == 0xC0)
        {
            return true;
        }

        var key = KeyInterop.KeyFromVirtualKey((int)vkCode);
        return key is Key.Oem3 or Key.OemTilde;
    }

    private void DispatchGlobalKey(Key key, ModifierKeys modifiers)
    {
        if (!IsOpen || _disposed)
        {
            return;
        }

        if (key == Key.Escape)
        {
            if (_open.Any(w => !string.IsNullOrEmpty(w.SearchQuery)))
            {
                ClearSearchEverywhere();
            }
            else
            {
                CloseAll();
            }

            return;
        }

        // Defense in depth if a path dispatches Oem3 without the Caps+` early return.
        if ((key is Key.Oem3 or Key.OemTilde) && modifiers == ModifierKeys.None)
        {
            var capsHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CAPITAL) & 0x8000) != 0;
            if (capsHeld)
            {
                CloseAll();
                return;
            }
        }

        if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            ActivateSearchEverywhere();
            return;
        }

        if ((key is Key.Left or Key.Right) && modifiers == ModifierKeys.Control)
        {
            FocusAdjacent(key == Key.Right ? 1 : -1);
            return;
        }

        if ((key is Key.Left or Key.Right or Key.Up or Key.Down) &&
            (modifiers is ModifierKeys.None or ModifierKeys.Shift))
        {
            NavigateSelection(key);
            return;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ActivateSelected();
            return;
        }

        if ((modifiers is ModifierKeys.None or ModifierKeys.Shift) &&
            key is >= Key.D1 and <= Key.D9)
        {
            var workspace = key - Key.D1 + 1;
            var host = _open.FirstOrDefault(w => w.IsActive) ?? _open.FirstOrDefault();
            host?.SwitchOrCreateWorkspaceFromKeyboard(workspace);
            return;
        }

        if (key == Key.Back && modifiers == ModifierKeys.None)
        {
            BackspaceSearch();
            return;
        }

        if ((modifiers is ModifierKeys.None or ModifierKeys.Shift) &&
            TryMapTypedChar(key, modifiers.HasFlag(ModifierKeys.Shift), out var ch))
        {
            AppendSearchChar(ch);
        }
    }

    private void AppendSearchChar(char ch)
    {
        var host = _open.FirstOrDefault(w => w.IsActive) ?? _open.FirstOrDefault();
        if (host is null)
        {
            return;
        }

        ActivateSearchEverywhere(host);
        host.SetSearchQuery((host.SearchQuery ?? "") + ch, notifyService: true);
    }

    private void BackspaceSearch()
    {
        var host = _open.FirstOrDefault(w => !string.IsNullOrEmpty(w.SearchQuery))
                   ?? _open.FirstOrDefault(w => w.IsActive)
                   ?? _open.FirstOrDefault();
        if (host is null)
        {
            return;
        }

        var q = host.SearchQuery ?? "";
        if (q.Length == 0)
        {
            return;
        }

        host.SetSearchQuery(q[..^1], notifyService: true);
    }

    private static bool TryMapTypedChar(Key key, bool shift, out char ch)
    {
        ch = '\0';
        if (key is >= Key.A and <= Key.Z)
        {
            var letter = (char)('a' + (key - Key.A));
            ch = shift ? char.ToUpperInvariant(letter) : letter;
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            ch = (char)('0' + (key - Key.D0));
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            ch = (char)('0' + (key - Key.NumPad0));
            return true;
        }

        ch = key switch
        {
            Key.Space => ' ',
            Key.OemMinus => shift ? '_' : '-',
            Key.OemPlus => shift ? '+' : '=',
            Key.OemComma => shift ? '<' : ',',
            Key.OemPeriod => shift ? '>' : '.',
            Key.OemQuestion => shift ? '?' : '/',
            Key.OemQuotes => shift ? '"' : '\'',
            Key.OemSemicolon => shift ? ':' : ';',
            Key.OemOpenBrackets => shift ? '{' : '[',
            Key.OemCloseBrackets => shift ? '}' : ']',
            Key.OemPipe => shift ? '|' : '\\',
            Key.OemTilde => shift ? '~' : '`',
            _ => '\0'
        };
        return ch != '\0';
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action, DispatcherPriority.Send);
    }

    public OverviewWindow? FindByHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        return _open.FirstOrDefault(w => w.NativeHandle == hwnd);
    }

    /// <summary>Focus the next/previous overview window sorted left-to-right, top-to-bottom.</summary>
    public void FocusAdjacent(int direction)
    {
        if (_open.Count <= 1 || direction == 0)
        {
            return;
        }

        var ordered = OrderWindows();
        var activeIdx = ordered.FindIndex(w => w.IsActive);
        if (activeIdx < 0)
        {
            activeIdx = 0;
        }

        var nextIdx = activeIdx + Math.Sign(direction);
        if (nextIdx < 0 || nextIdx >= ordered.Count)
        {
            return;
        }

        var target = ordered[nextIdx];
        target.ClaimKeyboardFocus();
    }

    /// <summary>Show/focus search on every overview; filters stay synced across monitors.</summary>
    public void ActivateSearchEverywhere(OverviewWindow? preferred = null)
    {
        preferred ??= _open.FirstOrDefault(w => w.IsActive) ?? _open.FirstOrDefault();
        foreach (var window in _open)
        {
            window.ShowSearchBox(focus: false);
        }

        preferred?.ShowSearchBox(focus: true, selectAll: string.IsNullOrEmpty(preferred.SearchQuery));
        preferred?.ClaimKeyboardFocus();
    }

    /// <summary>Propagate search text so every monitor filters the same query.</summary>
    public void BroadcastSearchQuery(string query, OverviewWindow? source)
    {
        if (_syncingSearch)
        {
            return;
        }

        _syncingSearch = true;
        try
        {
            foreach (var window in _open)
            {
                if (ReferenceEquals(window, source))
                {
                    continue;
                }

                window.SetSearchQuery(query, notifyService: false);
            }

            SelectFirstGlobally(activateHost: false);
        }
        finally
        {
            _syncingSearch = false;
        }
    }

    public void ClearSearchEverywhere()
    {
        if (_syncingSearch)
        {
            return;
        }

        _syncingSearch = true;
        try
        {
            foreach (var window in _open)
            {
                window.ClearSearchLocal();
            }

            SelectFirstGlobally(activateHost: true);
        }
        finally
        {
            _syncingSearch = false;
        }
    }

    /// <summary>
    /// Horizontal: cards in the current workspace, then the same workspace on the
    /// left/right monitor (no wrap). Vertical: previous/next workspace on the same
    /// monitor only (column index clamped, no wrap).
    /// </summary>
    public void NavigateSelection(System.Windows.Input.Key key)
    {
        var built = BuildNavModel();
        if (built.Groups.Count == 0 || built.Slots.Count == 0)
        {
            ClearAllSelections();
            return;
        }

        var currentPos = ResolveNavPosition(built);
        if (currentPos is null)
        {
            ApplyNavSlot(built.Slots[0], activateHost: true);
            return;
        }

        var next = OverviewArrowNavigation.TryMove(built.Groups, currentPos.Value, key);
        if (next is null)
        {
            return;
        }

        var slotIndex = built.Slots.FindIndex(s =>
            s.GroupIndex == next.Value.GroupIndex &&
            s.CardIndexInWorkspace == next.Value.CardIndex);
        if (slotIndex < 0)
        {
            return;
        }

        ApplyNavSlot(built.Slots[slotIndex], activateHost: true);
    }

    public void ActivateSelected()
    {
        var flat = BuildFlatMatches();
        var index = ResolveGlobalSelectionIndex(flat);
        if (index < 0 || index >= flat.Count)
        {
            return;
        }

        flat[index].Window.TryActivateSelected();
    }

    public void SelectFirstGlobally(bool activateHost = true)
    {
        var built = BuildNavModel();
        if (built.Slots.Count == 0)
        {
            ClearAllSelections();
            return;
        }

        ApplyNavSlot(built.Slots[0], activateHost);
    }

    private sealed record NavSlot(
        OverviewWindow Window,
        int GroupIndex,
        int CardIndexInWorkspace,
        int SelectionIndex);

    private sealed record NavModel(
        List<OverviewArrowNavigation.Group> Groups,
        List<NavSlot> Slots);

    private NavModel BuildNavModel()
    {
        var groups = new List<OverviewArrowNavigation.Group>();
        var slots = new List<NavSlot>();
        var monitors = OrderWindowsByHorizontal();

        for (var monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
        {
            var window = monitors[monitorIndex];
            var entries = window.GetMatchingNavEntries();
            if (entries.Count == 0)
            {
                continue;
            }

            var byWorkspace = entries
                .GroupBy(e => e.WorkspaceId)
                .OrderBy(g => g.Key)
                .ToList();

            for (var wsOrder = 0; wsOrder < byWorkspace.Count; wsOrder++)
            {
                var wsGroup = byWorkspace[wsOrder].ToList();
                var groupIndex = groups.Count;
                groups.Add(new OverviewArrowNavigation.Group(
                    MonitorIndex: monitorIndex,
                    MonitorLeft: window.MonitorBounds.Left,
                    WorkspaceId: byWorkspace[wsOrder].Key,
                    WorkspaceOrder: wsOrder,
                    CardCount: wsGroup.Count));

                for (var cardIndex = 0; cardIndex < wsGroup.Count; cardIndex++)
                {
                    slots.Add(new NavSlot(
                        window,
                        groupIndex,
                        cardIndex,
                        wsGroup[cardIndex].SelectionIndex));
                }
            }
        }

        return new NavModel(groups, slots);
    }

    private OverviewArrowNavigation.Position? ResolveNavPosition(NavModel model)
    {
        foreach (var window in _open)
        {
            if (window.SelectedMatchIndex < 0)
            {
                continue;
            }

            var slot = model.Slots.FirstOrDefault(s =>
                ReferenceEquals(s.Window, window) &&
                s.SelectionIndex == window.SelectedMatchIndex);
            if (slot is not null)
            {
                return new OverviewArrowNavigation.Position(slot.GroupIndex, slot.CardIndexInWorkspace);
            }
        }

        return null;
    }

    private void ApplyNavSlot(NavSlot slot, bool activateHost)
    {
        ClearAllSelections();
        slot.Window.SetSelectedMatchIndex(slot.SelectionIndex);
        slot.Window.ScrollSelectedIntoView();
        if (activateHost && slot.Window.SearchBoxFocused == false)
        {
            slot.Window.ClaimKeyboardFocus();
        }
    }

    private List<(OverviewWindow Window, int LocalIndex)> BuildFlatMatches()
    {
        var flat = new List<(OverviewWindow Window, int LocalIndex)>();
        foreach (var window in OrderWindowsByHorizontal())
        {
            var count = window.MatchingCardCount;
            for (var i = 0; i < count; i++)
            {
                flat.Add((window, i));
            }
        }

        return flat;
    }

    private List<OverviewWindow> OrderWindowsByHorizontal() =>
        _open
            .OrderBy(w => w.MonitorBounds.Left)
            .ThenBy(w => w.MonitorBounds.Top)
            .ToList();

    private List<OverviewWindow> OrderWindows() => OrderWindowsByHorizontal();

    private int ResolveGlobalSelectionIndex(List<(OverviewWindow Window, int LocalIndex)> flat)
    {
        for (var i = 0; i < flat.Count; i++)
        {
            var (window, local) = flat[i];
            if (window.SelectedMatchIndex == local)
            {
                return i;
            }
        }

        return -1;
    }

    private void ClearAllSelections()
    {
        foreach (var window in _open)
        {
            window.SetSelectedMatchIndex(-1);
        }
    }

    public OverviewDropResolution? TryResolveDropTarget(WpfPoint screenPx)
    {
        foreach (var window in _open)
        {
            if (!window.ContainsScreenPoint(screenPx))
            {
                continue;
            }

            var tag = window.TryHitDropTarget(screenPx);
            if (tag is not null)
            {
                return new OverviewDropResolution(window, tag);
            }
        }

        return null;
    }

    public void RefreshAll()
    {
        foreach (var window in _open.ToList())
        {
            window.Reload();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseAll();
    }

    private void OnOverviewClosed(object? sender, EventArgs e)
    {
        if (sender is OverviewWindow window)
        {
            window.Closed -= OnOverviewClosed;
            _open.Remove(window);
        }

        // Selecting a window or Esc on one monitor dismisses every overlay.
        if (!_closingAll && _open.Count > 0)
        {
            CloseAll();
        }
    }

    private void CloseAll()
    {
        if (_closingAll)
        {
            return;
        }

        _closingAll = true;
        try
        {
            foreach (var window in _open.ToList())
            {
                window.Close();
            }

            _open.Clear();
        }
        finally
        {
            _closingAll = false;
        }
    }
}

public sealed record OverviewDropResolution(OverviewWindow Window, OverviewWindow.DropTargetTag Tag);
