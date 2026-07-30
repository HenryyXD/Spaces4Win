using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

public enum VisibilityOwnership
{
    Visible,
    UserMinimized,
    HiddenBySpaces4Win,
    ExternallyHidden,
    Unknown
}

/// <summary>
/// Tracks why managed windows are invisible and applies hide/show without activating.
/// Prefer DWM cloaking over SW_HIDE so maximized/snapped size is preserved across switches.
/// </summary>
public sealed class WindowVisibilityService
{
    private readonly object _sync = new();
    private readonly Dictionary<IntPtr, VisibilityOwnership> _ownership = new();
    private readonly HashSet<IntPtr> _restoreAsMinimized = new();
    private readonly HashSet<IntPtr> _cloakedByUs = new();
    private readonly HashSet<IntPtr> _internalTransition = new();
    private int _internalDepth;

    public event EventHandler<string>? Log;

    /// <summary>Called after a window is hidden into an inactive workspace.</summary>
    public event EventHandler<IntPtr>? WindowHidden;

    /// <summary>Called when a window is shown (workspace switch in). Cache for this hwnd should be invalidated.</summary>
    public event EventHandler<IntPtr>? WindowShown;

    /// <summary>
    /// When true, hide with SW_HIDE instead of DWM cloak so inactive windows leave Alt-Tab/taskbar.
    /// Tradeoff: snap/maximize size may shrink on show; taskbar jump-to-workspace stops working.
    /// </summary>
    public bool PreferSwHide { get; set; }

    public void BeginInternalOperation()
    {
        Interlocked.Increment(ref _internalDepth);
    }

    public void EndInternalOperation()
    {
        if (Interlocked.Decrement(ref _internalDepth) <= 0)
        {
            Interlocked.Exchange(ref _internalDepth, 0);
            lock (_sync)
            {
                _internalTransition.Clear();
            }
        }
    }

    public bool IsInternalTransition(IntPtr hwnd)
    {
        if (_internalDepth > 0)
        {
            return true;
        }

        lock (_sync)
        {
            return _internalTransition.Contains(hwnd);
        }
    }

    public void TrackAsVisible(IntPtr hwnd)
    {
        lock (_sync)
        {
            _ownership[hwnd] = NativeMethods.IsIconic(hwnd)
                ? VisibilityOwnership.UserMinimized
                : VisibilityOwnership.Visible;
            _restoreAsMinimized.Remove(hwnd);
            _cloakedByUs.Remove(hwnd);
        }
    }

    /// <summary>
    /// Layout restore: un-minimize windows that were only minimized by shutdown
    /// reveal, unless the saved layout says the user had them minimized.
    /// </summary>
    public void TrackForLayoutRestore(IntPtr hwnd, bool userMinimized)
    {
        if (!userMinimized && NativeMethods.IsWindow(hwnd) && NativeMethods.IsIconic(hwnd))
        {
            MarkTransition(hwnd);
            BeginInternalOperation();
            try
            {
                // Restore without stealing focus from Spaces4Win / the shell.
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"Layout restore un-minimize failed for {hwnd}: {ex.Message}");
            }
            finally
            {
                EndInternalOperation();
            }
        }

        lock (_sync)
        {
            var keepMinimized = userMinimized || NativeMethods.IsIconic(hwnd);
            _ownership[hwnd] = keepMinimized
                ? VisibilityOwnership.UserMinimized
                : VisibilityOwnership.Visible;
            _restoreAsMinimized.Remove(hwnd);
            _cloakedByUs.Remove(hwnd);
        }
    }

    public void Forget(IntPtr hwnd)
    {
        lock (_sync)
        {
            _ownership.Remove(hwnd);
            _restoreAsMinimized.Remove(hwnd);
            _cloakedByUs.Remove(hwnd);
            _internalTransition.Remove(hwnd);
        }
    }

    public VisibilityOwnership GetOwnership(IntPtr hwnd)
    {
        lock (_sync)
        {
            return _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
        }
    }

    public IReadOnlyDictionary<IntPtr, VisibilityOwnership> Snapshot()
    {
        lock (_sync)
        {
            return _ownership.ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }

    public void HideForWorkspace(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            Forget(hwnd);
            return;
        }

        MarkTransition(hwnd);
        BeginInternalOperation();
        try
        {
            // Already invisible (not merely cloaked) and not minimized → leave alone.
            if (!NativeMethods.IsWindowVisible(hwnd) &&
                !NativeMethods.IsIconic(hwnd) &&
                !WindowClassifier.IsCloaked(hwnd))
            {
                lock (_sync)
                {
                    if (!_ownership.TryGetValue(hwnd, out var existing) ||
                        existing != VisibilityOwnership.HiddenBySpaces4Win)
                    {
                        _ownership[hwnd] = VisibilityOwnership.ExternallyHidden;
                    }
                }

                return;
            }

            var wasMinimized = NativeMethods.IsIconic(hwnd);
            if (wasMinimized)
            {
                // Keep minimize semantics for user-minimized windows.
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                lock (_sync)
                {
                    _restoreAsMinimized.Add(hwnd);
                    _cloakedByUs.Remove(hwnd);
                    _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
                }

                return;
            }

            // Cloak preserves maximize/snap size — SW_HIDE + SW_SHOWNA often shrinks windows.
            // PreferSwHide forces SW_HIDE so inactive windows leave Alt-Tab / taskbar.
            if (!PreferSwHide && TrySetCloaked(hwnd, cloak: true))
            {
                lock (_sync)
                {
                    _cloakedByUs.Add(hwnd);
                    _restoreAsMinimized.Remove(hwnd);
                    _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
                }

                WindowHidden?.Invoke(this, hwnd);
                return;
            }

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            lock (_sync)
            {
                _cloakedByUs.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
            }

            WindowHidden?.Invoke(this, hwnd);
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Hide failed for {hwnd}: {ex.Message}");
        }
        finally
        {
            EndInternalOperation();
        }
    }

    public void ShowForWorkspace(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            Forget(hwnd);
            return;
        }

        MarkTransition(hwnd);
        BeginInternalOperation();
        try
        {
            VisibilityOwnership ownership;
            bool asMinimized;
            bool cloakedByUs;
            lock (_sync)
            {
                ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
                asMinimized = _restoreAsMinimized.Contains(hwnd) || ownership == VisibilityOwnership.UserMinimized;
                cloakedByUs = _cloakedByUs.Contains(hwnd);
            }

            if (ownership == VisibilityOwnership.ExternallyHidden)
            {
                return;
            }

            if (ownership is VisibilityOwnership.HiddenBySpaces4Win or VisibilityOwnership.Unknown or VisibilityOwnership.UserMinimized)
            {
                if (asMinimized)
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
                }
                else if (cloakedByUs || WindowClassifier.IsCloaked(hwnd))
                {
                    TrySetCloaked(hwnd, cloak: false);
                }
                else if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    // Never force-show a window we did not hide. Invisible helpers
                    // (CicMarshalWnd, MIT message windows, AWT toolkit, …) would
                    // otherwise be pulled onto the desktop by SW_SHOWNA.
                    if (ownership != VisibilityOwnership.HiddenBySpaces4Win &&
                        ownership != VisibilityOwnership.UserMinimized)
                    {
                        lock (_sync)
                        {
                            _ownership[hwnd] = VisibilityOwnership.ExternallyHidden;
                        }

                        return;
                    }

                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
                }
            }

            lock (_sync)
            {
                _restoreAsMinimized.Remove(hwnd);
                _cloakedByUs.Remove(hwnd);
                _ownership[hwnd] = NativeMethods.IsIconic(hwnd)
                    ? VisibilityOwnership.UserMinimized
                    : VisibilityOwnership.Visible;
            }

            WindowShown?.Invoke(this, hwnd);
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Show failed for {hwnd}: {ex.Message}");
        }
        finally
        {
            EndInternalOperation();
        }
    }

    /// <summary>
    /// On app exit: reveal Spaces4Win-hidden windows without activation.
    /// Cloaked windows are uncloaked (size preserved). SW_HIDE fallbacks are shown
    /// minimized only when they were user-minimized before hide; otherwise SHOWNA.
    /// </summary>
    public void RevealHiddenAsMinimizedNoActivate(IEnumerable<IntPtr> hwnds)
    {
        BeginInternalOperation();
        try
        {
            foreach (var hwnd in hwnds)
            {
                try
                {
                    if (!NativeMethods.IsWindow(hwnd))
                    {
                        Forget(hwnd);
                        continue;
                    }

                    bool cloakedByUs;
                    bool restoreAsMinimized;
                    VisibilityOwnership ownership;
                    lock (_sync)
                    {
                        ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
                        cloakedByUs = _cloakedByUs.Contains(hwnd);
                        restoreAsMinimized = _restoreAsMinimized.Contains(hwnd);
                    }

                    if (ownership != VisibilityOwnership.HiddenBySpaces4Win)
                    {
                        continue;
                    }

                    RevealOne(hwnd, cloakedByUs, restoreAsMinimized);
                }
                catch (Exception ex)
                {
                    Log?.Invoke(this, $"Reveal minimized failed for {hwnd}: {ex.Message}");
                }
            }
        }
        finally
        {
            EndInternalOperation();
        }
    }

    /// <summary>
    /// Crash/unclean recovery: uncloak or SHOWNA without requiring in-memory ownership.
    /// Used on cold start when the journal lists HWNDs but ownership maps are empty.
    /// </summary>
    public void ForceRevealNoActivate(IEnumerable<IntPtr> hwnds)
    {
        BeginInternalOperation();
        try
        {
            foreach (var hwnd in hwnds)
            {
                try
                {
                    if (!NativeMethods.IsWindow(hwnd))
                    {
                        Forget(hwnd);
                        continue;
                    }

                    bool cloakedByUs;
                    bool restoreAsMinimized;
                    lock (_sync)
                    {
                        cloakedByUs = _cloakedByUs.Contains(hwnd);
                        restoreAsMinimized = _restoreAsMinimized.Contains(hwnd);
                    }

                    var cloaked = cloakedByUs || WindowClassifier.IsCloaked(hwnd);
                    var invisible = !NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd);

                    if (!cloaked && !invisible && !restoreAsMinimized)
                    {
                        // Already visible to the user — just normalize ownership.
                        lock (_sync)
                        {
                            _cloakedByUs.Remove(hwnd);
                            _restoreAsMinimized.Remove(hwnd);
                            _ownership[hwnd] = NativeMethods.IsIconic(hwnd)
                                ? VisibilityOwnership.UserMinimized
                                : VisibilityOwnership.Visible;
                        }

                        continue;
                    }

                    RevealOne(hwnd, cloakedByUs || cloaked, restoreAsMinimized || NativeMethods.IsIconic(hwnd));
                }
                catch (Exception ex)
                {
                    Log?.Invoke(this, $"Force reveal failed for {hwnd}: {ex.Message}");
                }
            }
        }
        finally
        {
            EndInternalOperation();
        }
    }

    private void RevealOne(IntPtr hwnd, bool cloakedByUs, bool restoreAsMinimized)
    {
        MarkTransition(hwnd);
        if (cloakedByUs || WindowClassifier.IsCloaked(hwnd))
        {
            TrySetCloaked(hwnd, cloak: false);
            lock (_sync)
            {
                _cloakedByUs.Remove(hwnd);
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.Visible;
            }
        }
        else if (restoreAsMinimized)
        {
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
            lock (_sync)
            {
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.UserMinimized;
            }
        }
        else
        {
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWNA);
            lock (_sync)
            {
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.Visible;
            }
        }
    }

    private enum PeekRevealKind
    {
        None = 0,
        Uncloak,
        ShowNa
    }

    /// <summary>
    /// Temporarily reveal a Spaces4Win-hidden window (no activate) so PrintWindow / DWM
    /// can capture content, then restore cloak or SW_HIDE. Ownership stays HiddenBySpaces4Win.
    /// </summary>
    public void WithPeekVisible(IntPtr hwnd, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        BeginInternalOperation();
        try
        {
            var kind = TryBeginPeek(hwnd);
            try
            {
                if (kind != PeekRevealKind.None)
                {
                    // Brief beat so DWM has a composited frame before capture.
                    Thread.Sleep(16);
                }

                action();
            }
            finally
            {
                EndPeek(hwnd, kind);
            }
        }
        finally
        {
            EndInternalOperation();
        }
    }

    /// <summary>
    /// Batch peek for overview open: one internal operation around all hwnds.
    /// Calls <paramref name="action"/> once per hwnd while that hwnd is briefly revealed.
    /// </summary>
    public void WithPeekVisible(IEnumerable<IntPtr> hwnds, Action<IntPtr> action)
    {
        ArgumentNullException.ThrowIfNull(hwnds);
        ArgumentNullException.ThrowIfNull(action);

        BeginInternalOperation();
        try
        {
            foreach (var hwnd in hwnds)
            {
                if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                {
                    continue;
                }

                var kind = TryBeginPeek(hwnd);
                try
                {
                    if (kind != PeekRevealKind.None)
                    {
                        Thread.Sleep(16);
                    }

                    action(hwnd);
                }
                finally
                {
                    EndPeek(hwnd, kind);
                }
            }
        }
        finally
        {
            EndInternalOperation();
        }
    }

    private PeekRevealKind TryBeginPeek(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return PeekRevealKind.None;
        }

        VisibilityOwnership ownership;
        bool cloakedByUs;
        bool restoreAsMinimized;
        lock (_sync)
        {
            ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
            cloakedByUs = _cloakedByUs.Contains(hwnd);
            restoreAsMinimized = _restoreAsMinimized.Contains(hwnd);
        }

        if (ownership != VisibilityOwnership.HiddenBySpaces4Win)
        {
            return PeekRevealKind.None;
        }

        // User-minimized hide path — do not SW_SHOW (would change minimize semantics).
        if (restoreAsMinimized)
        {
            return PeekRevealKind.None;
        }

        MarkTransition(hwnd);

        if (cloakedByUs || WindowClassifier.IsCloaked(hwnd))
        {
            if (TrySetCloaked(hwnd, cloak: false))
            {
                return PeekRevealKind.Uncloak;
            }

            return PeekRevealKind.None;
        }

        if (!NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
            return PeekRevealKind.ShowNa;
        }

        return PeekRevealKind.None;
    }

    private void EndPeek(IntPtr hwnd, PeekRevealKind kind)
    {
        if (kind == PeekRevealKind.None || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        // Caps+Ctrl move+follow (and similar): Hide → background peek → Switch/Show can
        // race EndPeek. If ownership is no longer HiddenBySpaces4Win, the window is on
        // an active workspace — do not recloak/SW_HIDE it.
        VisibilityOwnership ownership;
        lock (_sync)
        {
            ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
        }

        if (ownership != VisibilityOwnership.HiddenBySpaces4Win)
        {
            return;
        }

        try
        {
            switch (kind)
            {
                case PeekRevealKind.Uncloak:
                    TrySetCloaked(hwnd, cloak: true);
                    lock (_sync)
                    {
                        // Re-check: ShowForWorkspace may have won the race during TrySetCloaked.
                        if (_ownership.TryGetValue(hwnd, out var again) &&
                            again != VisibilityOwnership.HiddenBySpaces4Win)
                        {
                            TrySetCloaked(hwnd, cloak: false);
                            return;
                        }

                        _cloakedByUs.Add(hwnd);
                        _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
                    }

                    break;

                case PeekRevealKind.ShowNa:
                    lock (_sync)
                    {
                        if (_ownership.TryGetValue(hwnd, out var again) &&
                            again != VisibilityOwnership.HiddenBySpaces4Win)
                        {
                            return;
                        }
                    }

                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    lock (_sync)
                    {
                        if (_ownership.TryGetValue(hwnd, out var again) &&
                            again != VisibilityOwnership.HiddenBySpaces4Win)
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
                            return;
                        }

                        _cloakedByUs.Remove(hwnd);
                        _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Peek restore failed for {hwnd}: {ex.Message}");
        }
    }

    private static bool TrySetCloaked(IntPtr hwnd, bool cloak)
    {
        try
        {
            var value = cloak ? 1 : 0;
            return NativeMethods.DwmSetWindowAttribute(
                       hwnd,
                       NativeMethods.DWMWA_CLOAK,
                       ref value,
                       sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private void MarkTransition(IntPtr hwnd)
    {
        lock (_sync)
        {
            _internalTransition.Add(hwnd);
        }
    }
}
