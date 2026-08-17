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
    /// <summary>HWNDs we hid with SW_HIDE (iconic path, PreferSwHide, or cloak fallback).</summary>
    private readonly HashSet<IntPtr> _swHiddenByUs = new();
    private readonly HashSet<IntPtr> _internalTransition = new();
    /// <summary>
    /// HWNDs for which the next EVENT_OBJECT_HIDE is from our ShowWindow(SW_HIDE).
    /// Consumed by the hide handler so tray app hides are not ignored after a workspace hide.
    /// </summary>
    private readonly HashSet<IntPtr> _expectOurHideEvent = new();
    /// <summary>
    /// Owned / last-active-popup HWNDs we hid together with a managed owner so
    /// modal progress/dialog clusters stay consistent across workspace switches.
    /// </summary>
    private readonly Dictionary<IntPtr, List<IntPtr>> _hiddenSatellites = new();
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
            _swHiddenByUs.Remove(hwnd);
        }
    }

    /// <summary>User minimized to the taskbar — stays on the workspace.</summary>
    public void TrackAsUserMinimized(IntPtr hwnd)
    {
        lock (_sync)
        {
            _ownership[hwnd] = VisibilityOwnership.UserMinimized;
            _restoreAsMinimized.Remove(hwnd);
            _cloakedByUs.Remove(hwnd);
            _swHiddenByUs.Remove(hwnd);
        }
    }

    /// <summary>
    /// True when Spaces4Win cloaked or SW_HID this HWND for an inactive workspace.
    /// </summary>
    public bool WasHiddenByUs(IntPtr hwnd)
    {
        lock (_sync)
        {
            return _cloakedByUs.Contains(hwnd) || _swHiddenByUs.Contains(hwnd);
        }
    }

    /// <summary>
    /// If the next hide event is from our SW_HIDE, consume and return true (ignore it).
    /// Otherwise return false — caller should treat it as tray/app hide and unmanage.
    /// </summary>
    public bool ConsumeExpectedHideEvent(IntPtr hwnd)
    {
        lock (_sync)
        {
            return _expectOurHideEvent.Remove(hwnd);
        }
    }

    public bool IsPendingMinimizedRestore(IntPtr hwnd)
    {
        lock (_sync)
        {
            return _restoreAsMinimized.Contains(hwnd);
        }
    }

    /// <summary>
    /// Fully hidden by the app (minimize-to-tray), not merely iconic / cloaked by us.
    /// </summary>
    public static bool IsAppTrayHidden(IntPtr hwnd) =>
        NativeMethods.IsWindow(hwnd) &&
        !NativeMethods.IsWindowVisible(hwnd) &&
        !NativeMethods.IsIconic(hwnd) &&
        !WindowClassifier.IsCloaked(hwnd);

    private void ExpectOurHide(IntPtr hwnd)
    {
        lock (_sync)
        {
            _expectOurHideEvent.Add(hwnd);
        }
    }

    private void SwHide(IntPtr hwnd)
    {
        ExpectOurHide(hwnd);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
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
            _swHiddenByUs.Remove(hwnd);
        }
    }

    public void Forget(IntPtr hwnd)
    {
        lock (_sync)
        {
            _ownership.Remove(hwnd);
            _restoreAsMinimized.Remove(hwnd);
            _cloakedByUs.Remove(hwnd);
            _swHiddenByUs.Remove(hwnd);
            _internalTransition.Remove(hwnd);
            _expectOurHideEvent.Remove(hwnd);
            _hiddenSatellites.Remove(hwnd);
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

    /// <summary>
    /// Hide for inactive workspace. Returns false when the HWND should be unmanaged
    /// (already tray-hidden / externally gone).
    /// </summary>
    public bool HideForWorkspace(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            Forget(hwnd);
            return false;
        }

        MarkTransition(hwnd);
        BeginInternalOperation();
        try
        {
            // Already invisible (not merely cloaked) and not minimized → tray / external.
            if (!NativeMethods.IsWindowVisible(hwnd) &&
                !NativeMethods.IsIconic(hwnd) &&
                !WindowClassifier.IsCloaked(hwnd))
            {
                if (!WasHiddenByUs(hwnd))
                {
                    Forget(hwnd);
                    return false;
                }

                return true;
            }

            var satellites = EnumerateVisibilitySatellites(hwnd);

            // Taskbar-minimized: SW_HIDE so they leave the taskbar on other spaces;
            // restore with SHOWMINNOACTIVE when the workspace is shown again.
            if (NativeMethods.IsIconic(hwnd))
            {
                // Already tray-resident while still reporting iconic — do not own it.
                if (WindowClassifier.IsTrayResidentProcess(hwnd))
                {
                    Forget(hwnd);
                    return false;
                }

                SwHide(hwnd);
                HideSatellites(hwnd, satellites, preferCloak: false);
                lock (_sync)
                {
                    _restoreAsMinimized.Add(hwnd);
                    _cloakedByUs.Remove(hwnd);
                    _swHiddenByUs.Add(hwnd);
                    _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
                }

                return true;
            }

            if (!PreferSwHide && TrySetCloaked(hwnd, cloak: true))
            {
                HideSatellites(hwnd, satellites, preferCloak: true);
                lock (_sync)
                {
                    _cloakedByUs.Add(hwnd);
                    _swHiddenByUs.Remove(hwnd);
                    _restoreAsMinimized.Remove(hwnd);
                    _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
                }

                WindowHidden?.Invoke(this, hwnd);
                return true;
            }

            SwHide(hwnd);
            HideSatellites(hwnd, satellites, preferCloak: false);
            lock (_sync)
            {
                _cloakedByUs.Remove(hwnd);
                _swHiddenByUs.Add(hwnd);
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.HiddenBySpaces4Win;
            }

            WindowHidden?.Invoke(this, hwnd);
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Hide failed for {hwnd}: {ex.Message}");
            return false;
        }
        finally
        {
            EndInternalOperation();
        }
    }

    /// <summary>
    /// Show for active workspace. Returns false when the HWND must be dropped
    /// (tray-hidden, style no longer managed).
    /// </summary>
    public bool ShowForWorkspace(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            Forget(hwnd);
            return false;
        }

        MarkTransition(hwnd);
        BeginInternalOperation();
        try
        {
            VisibilityOwnership ownership;
            bool restoreMinimized;
            bool cloakedByUs;
            bool swHiddenByUs;
            lock (_sync)
            {
                ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
                restoreMinimized = _restoreAsMinimized.Contains(hwnd);
                cloakedByUs = _cloakedByUs.Contains(hwnd);
                swHiddenByUs = _swHiddenByUs.Contains(hwnd);
            }

            var weOwnHide = swHiddenByUs || cloakedByUs || restoreMinimized
                            || ownership == VisibilityOwnership.HiddenBySpaces4Win;

            // Never gate "our hide" on IsManagedWindow first — SW_HIDE of an iconic
            // window can clear IsIconic and fail size/title heuristics (Chrome), which
            // used to Forget without reveal and strand the HWND.
            if (!weOwnHide && !WindowClassifier.IsManagedWindow(hwnd))
            {
                Forget(hwnd);
                return false;
            }

            if (ownership == VisibilityOwnership.ExternallyHidden)
            {
                Forget(hwnd);
                return false;
            }

            // Still taskbar-minimized on the active workspace — leave alone.
            if (NativeMethods.IsIconic(hwnd) && !swHiddenByUs && !cloakedByUs)
            {
                lock (_sync)
                {
                    _ownership[hwnd] = VisibilityOwnership.UserMinimized;
                    _restoreAsMinimized.Remove(hwnd);
                }

                return true;
            }

            if (ownership is VisibilityOwnership.HiddenBySpaces4Win
                or VisibilityOwnership.Unknown
                or VisibilityOwnership.UserMinimized)
            {
                if (restoreMinimized)
                {
                    // Never gate on MainWindowHandle here — our SW_HIDE clears it for
                    // Chrome too. Tray apps should already have been Dropped (HIDE /
                    // settle / TOOLWINDOW) before this path runs.
                    if (WindowClassifier.LooksLikeTrayToolWindow(hwnd))
                    {
                        Forget(hwnd);
                        return false;
                    }

                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
                }
                else if (cloakedByUs || WindowClassifier.IsCloaked(hwnd))
                {
                    TrySetCloaked(hwnd, cloak: false);
                }
                else if (!NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd))
                {
                    if (!swHiddenByUs)
                    {
                        Forget(hwnd);
                        return false;
                    }

                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
                }

                ShowSatellites(hwnd);
            }

            lock (_sync)
            {
                _restoreAsMinimized.Remove(hwnd);
                _cloakedByUs.Remove(hwnd);
                _swHiddenByUs.Remove(hwnd);
                _ownership[hwnd] = NativeMethods.IsIconic(hwnd)
                    ? VisibilityOwnership.UserMinimized
                    : VisibilityOwnership.Visible;
            }

            WindowShown?.Invoke(this, hwnd);
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Show failed for {hwnd}: {ex.Message}");
            return false;
        }
        finally
        {
            EndInternalOperation();
        }
    }

    /// <summary>
    /// On app exit: reveal Spaces4Win-hidden windows without activation.
    /// Cloaked → uncloak; previously-minimized SW_HIDE → SHOWMINNOACTIVE; else SHOWNA.
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
                    bool restoreMinimized;
                    VisibilityOwnership ownership;
                    lock (_sync)
                    {
                        ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
                        cloakedByUs = _cloakedByUs.Contains(hwnd);
                        restoreMinimized = _restoreAsMinimized.Contains(hwnd);
                    }

                    if (ownership != VisibilityOwnership.HiddenBySpaces4Win)
                    {
                        continue;
                    }

                    RevealOne(hwnd, cloakedByUs, restoreMinimized);
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
                    bool restoreMinimized;
                    lock (_sync)
                    {
                        cloakedByUs = _cloakedByUs.Contains(hwnd);
                        restoreMinimized = _restoreAsMinimized.Contains(hwnd);
                    }

                    var cloaked = cloakedByUs || WindowClassifier.IsCloaked(hwnd);
                    var invisible = !NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd);

                    if (!cloaked && !invisible && !restoreMinimized)
                    {
                        lock (_sync)
                        {
                            _cloakedByUs.Remove(hwnd);
                            _swHiddenByUs.Remove(hwnd);
                            _restoreAsMinimized.Remove(hwnd);
                            _ownership[hwnd] = NativeMethods.IsIconic(hwnd)
                                ? VisibilityOwnership.UserMinimized
                                : VisibilityOwnership.Visible;
                        }

                        continue;
                    }

                    RevealOne(hwnd, cloakedByUs || cloaked, restoreMinimized || NativeMethods.IsIconic(hwnd));
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

    private void RevealOne(IntPtr hwnd, bool cloakedByUs, bool restoreMinimized)
    {
        MarkTransition(hwnd);
        if (cloakedByUs || WindowClassifier.IsCloaked(hwnd))
        {
            TrySetCloaked(hwnd, cloak: false);
            lock (_sync)
            {
                _cloakedByUs.Remove(hwnd);
                _swHiddenByUs.Remove(hwnd);
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.Visible;
            }
        }
        else if (restoreMinimized)
        {
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
            lock (_sync)
            {
                _cloakedByUs.Remove(hwnd);
                _swHiddenByUs.Remove(hwnd);
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.UserMinimized;
            }
        }
        else
        {
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWNA);
            lock (_sync)
            {
                _cloakedByUs.Remove(hwnd);
                _swHiddenByUs.Remove(hwnd);
                _restoreAsMinimized.Remove(hwnd);
                _ownership[hwnd] = VisibilityOwnership.Visible;
            }
        }

        ShowSatellites(hwnd);
    }

    private enum PeekRevealKind
    {
        None = 0,
        Uncloak
    }

    /// <summary>
    /// Temporarily uncloak a Spaces4Win-hidden window for PrintWindow capture.
    /// Never SW_SHOWNA (would pull tray-hidden windows onto the desktop).
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
        bool restoreMinimized;
        lock (_sync)
        {
            ownership = _ownership.TryGetValue(hwnd, out var o) ? o : VisibilityOwnership.Unknown;
            cloakedByUs = _cloakedByUs.Contains(hwnd);
            restoreMinimized = _restoreAsMinimized.Contains(hwnd);
        }

        if (ownership != VisibilityOwnership.HiddenBySpaces4Win)
        {
            return PeekRevealKind.None;
        }

        // Do not SW_SHOWNA peek for SW_HIDE'd minimized windows.
        if (restoreMinimized)
        {
            return PeekRevealKind.None;
        }

        MarkTransition(hwnd);

        if (cloakedByUs || WindowClassifier.IsCloaked(hwnd))
        {
            return TrySetCloaked(hwnd, cloak: false) ? PeekRevealKind.Uncloak : PeekRevealKind.None;
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
        // an active workspace — do not recloak it.
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
            if (kind != PeekRevealKind.Uncloak)
            {
                return;
            }

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

    private static List<IntPtr> EnumerateVisibilitySatellites(IntPtr owner)
    {
        var list = new List<IntPtr>();
        var seen = new HashSet<IntPtr> { owner };

        NativeMethods.EnumWindows((h, _) =>
        {
            if (h == IntPtr.Zero || !seen.Add(h) || !NativeMethods.IsWindow(h))
            {
                return true;
            }

            if (NativeMethods.GetWindow(h, NativeMethods.GW_OWNER) != owner)
            {
                return true;
            }

            if (NativeMethods.IsWindowVisible(h) ||
                NativeMethods.IsIconic(h) ||
                WindowClassifier.IsCloaked(h))
            {
                list.Add(h);
            }

            return true;
        }, IntPtr.Zero);

        var popup = NativeMethods.GetLastActivePopup(owner);
        if (popup != IntPtr.Zero &&
            seen.Add(popup) &&
            NativeMethods.IsWindow(popup) &&
            (NativeMethods.IsWindowVisible(popup) ||
             NativeMethods.IsIconic(popup) ||
             WindowClassifier.IsCloaked(popup)))
        {
            list.Add(popup);
        }

        return list;
    }

    private void HideSatellites(IntPtr owner, List<IntPtr> satellites, bool preferCloak)
    {
        if (satellites.Count == 0)
        {
            lock (_sync)
            {
                _hiddenSatellites.Remove(owner);
            }

            return;
        }

        var hidden = new List<IntPtr>(satellites.Count);
        foreach (var sat in satellites)
        {
            MarkTransition(sat);
            try
            {
                if (!NativeMethods.IsWindow(sat))
                {
                    continue;
                }

                if (!NativeMethods.IsWindowVisible(sat) &&
                    !NativeMethods.IsIconic(sat) &&
                    !WindowClassifier.IsCloaked(sat))
                {
                    continue;
                }

                if (preferCloak && !PreferSwHide && TrySetCloaked(sat, cloak: true))
                {
                    hidden.Add(sat);
                    continue;
                }

                SwHide(sat);
                hidden.Add(sat);
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"Satellite hide failed for {sat}: {ex.Message}");
            }
        }

        lock (_sync)
        {
            if (hidden.Count > 0)
            {
                _hiddenSatellites[owner] = hidden;
            }
            else
            {
                _hiddenSatellites.Remove(owner);
            }
        }
    }

    private void ShowSatellites(IntPtr owner)
    {
        List<IntPtr>? satellites;
        lock (_sync)
        {
            if (!_hiddenSatellites.Remove(owner, out satellites) || satellites is null)
            {
                return;
            }
        }

        foreach (var sat in satellites)
        {
            MarkTransition(sat);
            try
            {
                if (!NativeMethods.IsWindow(sat))
                {
                    continue;
                }

                if (WindowClassifier.IsCloaked(sat))
                {
                    TrySetCloaked(sat, cloak: false);
                }
                else if (!NativeMethods.IsWindowVisible(sat) && !NativeMethods.IsIconic(sat))
                {
                    NativeMethods.ShowWindow(sat, NativeMethods.SW_SHOWNA);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"Satellite show failed for {sat}: {ex.Message}");
            }
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
