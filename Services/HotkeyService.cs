using System.Runtime.InteropServices;
using System.Windows.Input;
using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

/// <summary>
/// Global hotkeys via WH_KEYBOARD_LL so CapsLock can act as a modifier.
/// Physical CapsLock is swallowed while held. Lock state only changes when CapsLock
/// is pressed and released alone (no number chord).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private readonly List<(HotkeyBinding Binding, Action Action, HotkeyRole Role)> _bindings = new();
    private IntPtr _hook = IntPtr.Zero;
    private bool _capsHeld;
    private bool _capsUsedAsChord;
    private bool _ignoreInjectedCaps;
    private bool _disposed;
    /// <summary>VK of the chord key currently held after a successful hotkey match (blocks auto-repeat).</summary>
    private uint? _chordKeyHeld;

    public HotkeyService(WorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager;
        _hookProc = HookCallback;
    }

    public event EventHandler<string>? RegistrationFailed;
    public event EventHandler<string>? StatusMessage;

    /// <summary>Fired on the UI thread when physical CapsLock is released.</summary>
    public event EventHandler? CapsReleased;

    /// <summary>Optional: when Caps is held and Escape is pressed (e.g. dismiss switcher).</summary>
    public Func<bool>? TryHandleEscapeWhileCapsHeld { get; set; }

    /// <summary>
    /// While Overview is open, handle Esc / Ctrl+F / arrows / typing even if a foreign
    /// app still has foreground (WPF KeyDown alone is unreliable for transparent overlays).
    /// Return true to swallow the key.
    /// </summary>
    /// <summary>
    /// While overview is open: (vk, modifiers, capsPhysicallyHeld) → swallow if handled.
    /// Pass physical Caps from the hook — GetAsyncKeyState(VK_CAPITAL) is unreliable after we swallow Caps.
    /// </summary>
    public Func<uint, ModifierKeys, bool, bool>? OverviewInputFilter { get; set; }

    /// <summary>Active Caps+Tab / Caps+Q / Caps+` session; blocks other chords while set.</summary>
    public Func<HotkeyModalKind>? GetModalKind { get; set; }

    /// <summary>Optional action for CapsLock+` overview (set before Apply).</summary>
    public Action? OverviewAction { get; set; }

    /// <summary>Optional CapsLock+[ / ] focus previous/next monitor (direction -1 / +1).</summary>
    public Action<int>? FocusAdjacentMonitorAction { get; set; }

    /// <summary>Optional animated switch (set before Apply). Falls back to WorkspaceManager.</summary>
    public Action<string, int>? SwitchOrCreateAction { get; set; }

    /// <summary>Optional animated switch without creating (adjacent arrows).</summary>
    public Action<string, int>? SwitchAction { get; set; }

    /// <summary>Optional animated last-workspace switch.</summary>
    public Action<string>? SwitchToLastAction { get; set; }

    /// <summary>Caps+Tab workspace cycle switcher. Argument is reverse (Shift held).</summary>
    public Action<bool>? WorkspaceSwitcherAction { get; set; }

    /// <summary>Caps+Q window switcher. Argument is reverse (Shift held).</summary>
    public Action<bool>? WindowSwitcherAction { get; set; }

    /// <summary>Optional localization for status toasts (key → text).</summary>
    public Func<string, string>? ResolveStatusText { get; set; }

    /// <summary>
    /// True while the physical CapsLock key is held (hook-tracked).
    /// Independent of the CapsLock toggle/LED state.
    /// </summary>
    public bool IsCapsPhysicallyHeld => _capsHeld;

    public void Apply(AppConfig config)
    {
        _bindings.Clear();

        foreach (var binding in config.SwitchWorkspaceHotkeys)
        {
            var workspace = binding.Workspace;
            _bindings.Add((Clone(binding), () =>
            {
                var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
                if (monitorId is null)
                {
                    return;
                }

                if (SwitchOrCreateAction is not null)
                {
                    SwitchOrCreateAction(monitorId, workspace);
                }
                else
                {
                    _workspaceManager.SwitchOrCreateWorkspace(monitorId, workspace);
                }
            }, HotkeyRole.General));
        }

        foreach (var binding in config.MoveWindowHotkeys)
        {
            var workspace = binding.Workspace;
            _bindings.Add((Clone(binding), () =>
                _workspaceManager.MoveActiveWindowToWorkspace(workspace), HotkeyRole.General));
        }

        foreach (var binding in config.MoveWindowAndFollowHotkeys)
        {
            var workspace = binding.Workspace;
            _bindings.Add((Clone(binding), () =>
            {
                // Do not activate the next window on the current workspace — key-repeat
                // would then move every remaining window before/during the follow switch.
                var monitorId = _workspaceManager.MoveActiveWindowToWorkspace(
                    workspace,
                    activateRemainingOnCurrent: false,
                    applyVisibility: false);
                if (monitorId is null)
                {
                    return;
                }

                if (SwitchOrCreateAction is not null)
                {
                    SwitchOrCreateAction(monitorId, workspace);
                }
                else
                {
                    _workspaceManager.SwitchOrCreateWorkspace(monitorId, workspace);
                }
            }, HotkeyRole.General));
        }

        var toggle = config.ToggleLastWorkspaceHotkey ?? AppConfig.CreateDefaultToggleLastHotkey();
        _bindings.Add((Clone(toggle), () =>
        {
            if (WorkspaceSwitcherAction is not null)
            {
                WorkspaceSwitcherAction(false);
                return;
            }

            var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
            if (monitorId is null)
            {
                return;
            }

            if (SwitchToLastAction is not null)
            {
                SwitchToLastAction(monitorId);
            }
            else
            {
                _workspaceManager.SwitchToLastWorkspace(monitorId);
            }
        }, HotkeyRole.WorkspaceSwitcher));

        var toggleReverse = Clone(toggle);
        toggleReverse.Modifiers |= ModifierKeys.Shift;
        _bindings.Add((toggleReverse, () =>
        {
            if (WorkspaceSwitcherAction is not null)
            {
                WorkspaceSwitcherAction(true);
                return;
            }

            var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
            if (monitorId is null)
            {
                return;
            }

            if (SwitchToLastAction is not null)
            {
                SwitchToLastAction(monitorId);
            }
            else
            {
                _workspaceManager.SwitchToLastWorkspace(monitorId);
            }
        }, HotkeyRole.WorkspaceSwitcher));

        var windowSwitcher = config.WindowSwitcherHotkey ?? AppConfig.CreateDefaultWindowSwitcherHotkey();
        _bindings.Add((Clone(windowSwitcher), () =>
        {
            WindowSwitcherAction?.Invoke(false);
        }, HotkeyRole.WindowSwitcher));

        var windowSwitcherReverse = Clone(windowSwitcher);
        windowSwitcherReverse.Modifiers |= ModifierKeys.Shift;
        _bindings.Add((windowSwitcherReverse, () =>
        {
            WindowSwitcherAction?.Invoke(true);
        }, HotkeyRole.WindowSwitcher));

        var sticky = config.ToggleStickyHotkey ?? AppConfig.CreateDefaultToggleStickyHotkey();
        _bindings.Add((Clone(sticky), () =>
        {
            var result = _workspaceManager.ToggleStickyForActiveWindow();
            if (result is null)
            {
                Status(StatusKeys.NoManageableWindow, "No manageable window focused.");
            }
            else if (result == true)
            {
                Status(StatusKeys.StickyOn, "Window is now sticky (visible on all workspaces).");
            }
            else
            {
                Status(StatusKeys.StickyOff, "Window is no longer sticky.");
            }
        }, HotkeyRole.General));

        var compact = config.CompactWorkspacesHotkey ?? AppConfig.CreateDefaultCompactWorkspacesHotkey();
        _bindings.Add((Clone(compact), () =>
        {
            var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
            if (monitorId is null)
            {
                Status(StatusKeys.NoMonitor, "No monitor available.");
                return;
            }

            var ok = _workspaceManager.CompactWorkspaces(monitorId);
            Status(
                ok ? StatusKeys.WorkspacesCompacted : StatusKeys.AlreadyDense,
                ok ? "Workspaces compacted." : "Already dense.");
        }, HotkeyRole.General));

        var fullscreen = config.FullscreenWorkspaceHotkey ?? AppConfig.CreateDefaultFullscreenWorkspaceHotkey();
        _bindings.Add((Clone(fullscreen), () =>
        {
            var result = _workspaceManager.ToggleFullscreenInPlace();
            if (result is null)
            {
                Status(StatusKeys.NoManageableWindow, "No manageable window focused.");
            }
            else if (result == true)
            {
                Status(StatusKeys.FullscreenOn, "Entered fullscreen.");
            }
            else
            {
                Status(StatusKeys.FullscreenOff, "Exited fullscreen.");
            }
        }, HotkeyRole.General));

        var moveFree = config.MoveToFreeWorkspaceHotkey ?? AppConfig.CreateDefaultMoveToFreeWorkspaceHotkey();
        _bindings.Add((Clone(moveFree), () =>
        {
            var result = _workspaceManager.MoveActiveWindowToFreeWorkspace(follow: false);
            if (result == WorkspaceManager.ShiftWindowResult.Ok)
            {
                Status(StatusKeys.MovedToFreeWorkspace, "Window moved to a free workspace.");
            }
            else
            {
                ReportShiftResult(result);
            }
        }, HotkeyRole.General));

        var moveFreeFollow = config.MoveToFreeWorkspaceFollowHotkey ?? AppConfig.CreateDefaultMoveToFreeWorkspaceFollowHotkey();
        _bindings.Add((Clone(moveFreeFollow), () =>
        {
            var result = _workspaceManager.MoveActiveWindowToFreeWorkspace(follow: true);
            if (result == WorkspaceManager.ShiftWindowResult.Ok)
            {
                Status(StatusKeys.MovedToFreeWorkspaceFollow, "Switched to the new free workspace.");
            }
            else
            {
                ReportShiftResult(result);
            }
        }, HotkeyRole.General));

        var delete = config.DeleteWorkspaceHotkey ?? AppConfig.CreateDefaultDeleteWorkspaceHotkey();
        _bindings.Add((Clone(delete), () =>
        {
            var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
            if (monitorId is null)
            {
                return;
            }

            var ok = _workspaceManager.DeleteCurrentWorkspace(monitorId);
            Status(
                ok ? StatusKeys.WorkspaceDeletedMoved : StatusKeys.CannotDeleteOnlyMonitor,
                ok
                    ? "Workspace deleted — windows moved to the fallback workspace."
                    : "Cannot delete the only workspace on this monitor.");
        }, HotkeyRole.General));

        var overview = config.OverviewHotkey ?? AppConfig.CreateDefaultOverviewHotkey();
        _bindings.Add((Clone(overview), () => OverviewAction?.Invoke(), HotkeyRole.Overview));

        var previous = config.PreviousWorkspaceHotkey ?? AppConfig.CreateDefaultPreviousWorkspaceHotkey();
        _bindings.Add((Clone(previous), () => SwitchAdjacent(-1), HotkeyRole.General));

        var next = config.NextWorkspaceHotkey ?? AppConfig.CreateDefaultNextWorkspaceHotkey();
        _bindings.Add((Clone(next), () => SwitchAdjacent(+1), HotkeyRole.General));

        // Caps+Alt+←/→ — insert, stay on former workspace
        var insertLeft = config.ShiftWindowLeftHotkey ?? AppConfig.CreateDefaultShiftWindowLeftHotkey();
        _bindings.Add((Clone(insertLeft), () => ReportShiftResult(
            _workspaceManager.ShiftActiveWindowToNewWorkspace(-1, follow: false)), HotkeyRole.General));

        var insertRight = config.ShiftWindowRightHotkey ?? AppConfig.CreateDefaultShiftWindowRightHotkey();
        _bindings.Add((Clone(insertRight), () => ReportShiftResult(
            _workspaceManager.ShiftActiveWindowToNewWorkspace(+1, follow: false)), HotkeyRole.General));

        // Caps+Ctrl+Alt+←/→ — insert and follow
        var insertFollowLeft = config.ShiftWindowFollowLeftHotkey ?? AppConfig.CreateDefaultShiftWindowFollowLeftHotkey();
        _bindings.Add((Clone(insertFollowLeft), () => ReportShiftResult(
            _workspaceManager.ShiftActiveWindowToNewWorkspace(-1, follow: true)), HotkeyRole.General));

        var insertFollowRight = config.ShiftWindowFollowRightHotkey ?? AppConfig.CreateDefaultShiftWindowFollowRightHotkey();
        _bindings.Add((Clone(insertFollowRight), () => ReportShiftResult(
            _workspaceManager.ShiftActiveWindowToNewWorkspace(+1, follow: true)), HotkeyRole.General));

        // Caps+Shift+←/→ — move to adjacent existing, no follow, no create
        var moveAdjLeft = config.MoveAdjacentLeftHotkey ?? AppConfig.CreateDefaultMoveAdjacentLeftHotkey();
        _bindings.Add((Clone(moveAdjLeft), () => ReportShiftResult(
            _workspaceManager.MoveActiveWindowToAdjacentWorkspace(-1, follow: false, createAtExtreme: false)), HotkeyRole.General));

        var moveAdjRight = config.MoveAdjacentRightHotkey ?? AppConfig.CreateDefaultMoveAdjacentRightHotkey();
        _bindings.Add((Clone(moveAdjRight), () => ReportShiftResult(
            _workspaceManager.MoveActiveWindowToAdjacentWorkspace(+1, follow: false, createAtExtreme: false)), HotkeyRole.General));

        // Caps+Ctrl+←/→ — move+follow; create (insert+follow) at extreme
        var moveFollowLeft = config.MoveAdjacentFollowLeftHotkey ?? AppConfig.CreateDefaultMoveAdjacentFollowLeftHotkey();
        _bindings.Add((Clone(moveFollowLeft), () => ReportShiftResult(
            _workspaceManager.MoveActiveWindowToAdjacentWorkspace(-1, follow: true, createAtExtreme: true)), HotkeyRole.General));

        var moveFollowRight = config.MoveAdjacentFollowRightHotkey ?? AppConfig.CreateDefaultMoveAdjacentFollowRightHotkey();
        _bindings.Add((Clone(moveFollowRight), () => ReportShiftResult(
            _workspaceManager.MoveActiveWindowToAdjacentWorkspace(+1, follow: true, createAtExtreme: true)), HotkeyRole.General));

        var focusPrev = config.FocusPreviousMonitorHotkey ?? AppConfig.CreateDefaultFocusPreviousMonitorHotkey();
        _bindings.Add((Clone(focusPrev), () => FocusAdjacentMonitorAction?.Invoke(-1), HotkeyRole.General));

        var focusNext = config.FocusNextMonitorHotkey ?? AppConfig.CreateDefaultFocusNextMonitorHotkey();
        _bindings.Add((Clone(focusNext), () => FocusAdjacentMonitorAction?.Invoke(+1), HotkeyRole.General));

        EnsureHook();
    }

    private void ReportShiftResult(WorkspaceManager.ShiftWindowResult result)
    {
        switch (result)
        {
            case WorkspaceManager.ShiftWindowResult.Ok:
            case WorkspaceManager.ShiftWindowResult.NoAdjacent:
                break;
            case WorkspaceManager.ShiftWindowResult.NoWindow:
                Status(StatusKeys.NoManageableWindow, "No manageable window focused.");
                break;
            case WorkspaceManager.ShiftWindowResult.AtLimit:
                Status(StatusKeys.MaxWorkspaces, "Maximum of 9 workspaces reached on this monitor.");
                break;
            default:
                Status(StatusKeys.CouldNotMoveWindow, "Could not move the focused window.");
                break;
        }
    }

    private void Status(string key, string englishFallback)
    {
        var text = ResolveStatusText?.Invoke(key);
        if (string.IsNullOrWhiteSpace(text) || text == key)
        {
            text = englishFallback;
        }

        StatusMessage?.Invoke(this, text);
    }

    private static class StatusKeys
    {
        public const string NoManageableWindow = "Status.NoManageableWindow";
        public const string StickyOn = "Status.StickyOn";
        public const string StickyOff = "Status.StickyOff";
        public const string NoMonitor = "Status.NoMonitor";
        public const string WorkspacesCompacted = "Status.WorkspacesCompacted";
        public const string AlreadyDense = "Status.AlreadyDense";
        public const string FullscreenOn = "Status.FullscreenOn";
        public const string FullscreenOff = "Status.FullscreenOff";
        public const string MovedToFreeWorkspace = "Status.MovedToFreeWorkspace";
        public const string MovedToFreeWorkspaceFollow = "Status.MovedToFreeWorkspaceFollow";
        public const string WorkspaceDeletedMoved = "Status.WorkspaceDeletedMoved";
        public const string CannotDeleteOnlyMonitor = "Status.CannotDeleteOnlyMonitor";
        public const string MaxWorkspaces = "Status.MaxWorkspaces";
        public const string CouldNotMoveWindow = "Status.CouldNotMoveWindow";
    }

    private void SwitchAdjacent(int direction)
    {
        var monitorId = _workspaceManager.ResolveMonitorIdForHotkeys();
        if (monitorId is null)
        {
            return;
        }

        var target = _workspaceManager.ResolveAdjacentExistingWorkspace(monitorId, direction);
        if (target is null)
        {
            return;
        }

        if (SwitchAction is not null)
        {
            SwitchAction(monitorId, target.Value);
        }
        else
        {
            _workspaceManager.SwitchWorkspace(monitorId, target.Value);
        }
    }

    public void Clear()
    {
        _bindings.Clear();
        RemoveHook();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        GC.KeepAlive(_hookProc);
    }

    private void EnsureHook()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var handle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, handle, 0);
        if (_hook == IntPtr.Zero)
        {
            RegistrationFailed?.Invoke(this, "Failed to install keyboard hook for CapsLock hotkeys.");
        }
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _capsHeld = false;
        _capsUsedAsChord = false;
        _chordKeyHeld = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var msg = wParam.ToInt32();
        var isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
        var isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        var injected = (info.flags & NativeMethods.LLKHF_INJECTED) != 0;

        if (isUp && _chordKeyHeld == info.vkCode)
        {
            _chordKeyHeld = null;
        }

        // Synthetic CapsLock (alone-tap toggle) must reach the system.
        if (injected && info.vkCode == NativeMethods.VK_CAPITAL)
        {
            if (_ignoreInjectedCaps)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
        }

        if (info.vkCode == NativeMethods.VK_CAPITAL && !injected)
        {
            if (isDown)
            {
                if (!_capsHeld)
                {
                    _capsHeld = true;
                    _capsUsedAsChord = false;
                }

                // Swallow physical CapsLock so the lock LED/state does not flip yet.
                return (IntPtr)1;
            }

            if (isUp)
            {
                var usedAsChord = _capsUsedAsChord;
                _capsHeld = false;
                _capsUsedAsChord = false;
                _chordKeyHeld = null;

                RaiseCapsReleased();

                if (!usedAsChord)
                {
                    // CapsLock alone: apply a real toggle after the hook returns.
                    QueueCapsLockToggle();
                }

                // Swallow physical key-up as well (we already handled alone vs chord).
                return (IntPtr)1;
            }
        }

        if (isDown &&
            info.vkCode == NativeMethods.VK_ESCAPE &&
            _capsHeld &&
            TryHandleEscapeWhileCapsHeld?.Invoke() == true)
        {
            _capsUsedAsChord = true;
            return (IntPtr)1;
        }

        // Overview must win over Caps+←/→ workspace chords and over apps that kept focus.
        if (isDown &&
            (GetModalKind?.Invoke() ?? HotkeyModalKind.None) == HotkeyModalKind.Overview &&
            OverviewInputFilter?.Invoke(info.vkCode, ReadModifiers(), _capsHeld) == true)
        {
            if (_capsHeld)
            {
                _capsUsedAsChord = true;
            }

            return (IntPtr)1;
        }

        if (isDown && TryMatchAndInvoke(info.vkCode))
        {
            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void RaiseCapsReleased()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            CapsReleased?.Invoke(this, EventArgs.Empty);
            return;
        }

        _ = dispatcher.BeginInvoke(() => CapsReleased?.Invoke(this, EventArgs.Empty));
    }

    private bool TryMatchAndInvoke(uint vkCode)
    {
        // Auto-repeat while the chord digit is held must not re-fire (would move
        // every window after focus advances on Caps+Shift, or race Caps+Ctrl follow).
        if (_chordKeyHeld == vkCode)
        {
            return true;
        }

        var key = NormalizeDigitKey(VirtualKeyToKey(vkCode));
        if (key is null)
        {
            return false;
        }

        var modifiers = ReadModifiers();
        HotkeyBinding? found = null;
        Action? action = null;
        var role = HotkeyRole.General;
        var bestScore = -1;

        foreach (var (binding, act, bindingRole) in _bindings)
        {
            if (NormalizeDigitKey(binding.Key) != key)
            {
                continue;
            }

            if (binding.CapsLock != _capsHeld)
            {
                continue;
            }

            if (binding.Modifiers != modifiers)
            {
                continue;
            }

            var score = ModifierCount(binding.Modifiers) + (binding.CapsLock ? 10 : 0);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            found = binding;
            action = act;
            role = bindingRole;
        }

        if (found is null || action is null)
        {
            return false;
        }

        var modal = GetModalKind?.Invoke() ?? HotkeyModalKind.None;
        if (!HotkeyModalGate.IsAllowed(modal, role))
        {
            // Swallow competing Caps chords so overlays are not interrupted.
            if (found.CapsLock)
            {
                _capsUsedAsChord = true;
            }

            _chordKeyHeld = vkCode;
            return true;
        }

        if (found.CapsLock)
        {
            _capsUsedAsChord = true;
        }

        _chordKeyHeld = vkCode;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action);
        }

        return true;
    }

    private void QueueCapsLockToggle()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            SendCapsLockToggle();
            return;
        }

        _ = dispatcher.BeginInvoke(SendCapsLockToggle);
    }

    private void SendCapsLockToggle()
    {
        _ignoreInjectedCaps = true;
        try
        {
            var inputs = new[]
            {
                CreateCapsInput(keyUp: false),
                CreateCapsInput(keyUp: true)
            };

            var size = Marshal.SizeOf<NativeMethods.INPUT>();
            var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
            if (sent != inputs.Length)
            {
                // Fallback if SendInput is blocked.
                NativeMethods.keybd_event(NativeMethods.VK_CAPITAL, 0, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(NativeMethods.VK_CAPITAL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }
        finally
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                _ignoreInjectedCaps = false;
            }
            else
            {
                _ = dispatcher.BeginInvoke(() => _ignoreInjectedCaps = false);
            }
        }
    }

    private static NativeMethods.INPUT CreateCapsInput(bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = NativeMethods.VK_CAPITAL,
                wScan = 0x3A,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    private static ModifierKeys ReadModifiers()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (IsDown(NativeMethods.VK_SHIFT))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsDown(NativeMethods.VK_CONTROL))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsDown(NativeMethods.VK_MENU))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static int ModifierCount(ModifierKeys modifiers)
    {
        var count = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) count++;
        if (modifiers.HasFlag(ModifierKeys.Control)) count++;
        if (modifiers.HasFlag(ModifierKeys.Shift)) count++;
        if (modifiers.HasFlag(ModifierKeys.Windows)) count++;
        return count;
    }

    private static Key? VirtualKeyToKey(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39)
        {
            return Key.D0 + (int)(vk - 0x30);
        }

        if (vk is >= 0x60 and <= 0x69)
        {
            return Key.NumPad0 + (int)(vk - 0x60);
        }

        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        return key == Key.None ? null : key;
    }

    private static Key? NormalizeDigitKey(Key? key) => key switch
    {
        null => null,
        Key.NumPad0 => Key.D0,
        Key.NumPad1 => Key.D1,
        Key.NumPad2 => Key.D2,
        Key.NumPad3 => Key.D3,
        Key.NumPad4 => Key.D4,
        Key.NumPad5 => Key.D5,
        Key.NumPad6 => Key.D6,
        Key.NumPad7 => Key.D7,
        Key.NumPad8 => Key.D8,
        Key.NumPad9 => Key.D9,
        _ => key
    };

    private static HotkeyBinding Clone(HotkeyBinding source) => new()
    {
        Workspace = source.Workspace,
        CapsLock = source.CapsLock,
        Modifiers = source.Modifiers,
        Key = source.Key
    };
}
