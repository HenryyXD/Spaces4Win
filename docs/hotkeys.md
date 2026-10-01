---
summary: CapsLock as modifier via LL keyboard hook; HotkeyService chords; HotkeyMonitorContext targeting; ForeignActivationPolicy CapsLock+taskbar move.
when: Hotkey bugs, wrong monitor switched, taskbar click jumps wrong space, CapsLock lock state.
sources: [Services/HotkeyService.cs, Services/HotkeyMonitorContext.cs, Services/ForeignActivationPolicy.cs, Services/ForeignWorkspaceActivationService.cs, Config/AppConfig.cs]
---

# Hotkeys & activation

## CapsLock model

`HotkeyService` uses a **low-level keyboard hook**:

- While CapsLock is held as part of a chord, it acts as a **modifier** (does not toggle lock).
- A CapsLock press/release **without** a chord still toggles CapsLock.
- Top-row and numpad digits both bind where configured (`D1`–`D9` / numpad equivalents in bindings).

Bindings live on `AppConfig` (`SwitchWorkspaceHotkeys`, move/follow/adjacent/insert/overview/…); `HotkeyService.Apply(config)` registers them.

## Actions (wired in `AppServices`)

Most **switch** paths go through `WorkspaceTransitionCoordinator` (animated), not raw `WorkspaceManager.Switch*` alone:

- Switch or create / switch / switch-to-last
- Overview toggle
- Focus adjacent monitor (+ indicator pulse)

Move / sticky / compact / fullscreen / delete / insert typically call `WorkspaceManager` directly (see `HotkeyService` for the full dispatch table). Defaults are documented in `README.md`.

`CapsLock+R` → `CompactWorkspaces(ResolveMonitorIdForHotkeys())`: densify sparse ids to `1…N` on that monitor only (sticky unchanged).

`CapsLock+F` → `ToggleFullscreenInPlace()`: enter/exit borderless fullscreen on the current workspace (no move). Flag persists in `window-layout.json` (`isFullscreen`); shutdown exits chrome before reveal.

`CapsLock+Shift+F` → `MoveActiveWindowToFreeWorkspace(follow: false)`: send focused window to a free/empty (or least-busy) workspace without switching.

`CapsLock+Ctrl+F` → `MoveActiveWindowToFreeWorkspace(follow: true)`: same move, then switch to that workspace.

`CapsLock+Tab` → workspace switcher overlay (`SwitcherController`): first Tab selects last/next workspace; more Tabs while Caps held cycle; Caps release commits via `TransitionCoordinator.Switch`. Esc cancels.

`CapsLock+Q` → window switcher for the active workspace (+ sticky): MRU-ordered cards; first Q selects previous window; hold Caps + Q cycles; release activates.

`CapsLock+P` → session-preset browser (named layouts). `CapsLock+Ctrl+Alt+0..9` saves current layout to that slot (toast). `CapsLock+Alt+0..9` opens the browser focused on that slot; Enter confirms load (may close extras and launch apps). F2 renames.

## Monitor targeting — `HotkeyMonitorContext`

Resolves which monitor a chord applies to:

- After **real** mouse move → monitor under cursor (`HotkeyMonitorSource.Cursor`).
- After focus change to a managed window (Alt+Tab / click) → that window’s monitor (`FocusedWindow`), even if the cursor stayed elsewhere.
- `CapsLock+[ / ]` warps the cursor via `SetCursorPos` (injected). The LL mouse hook **ignores** injected moves, so `AppServices` calls `PreferCursorMonitor()` after a successful warp — otherwise chords keep targeting the previous focused window’s monitor.

Attached to `WorkspaceManager` so core APIs and hotkeys share policy.

## Foreign workspace activation

When a managed window on another workspace becomes foreground (e.g. taskbar click):

`ForeignActivationPolicy.Decide(isSticky, windowWs, activeWs, capsPhysicallyHeld)`:

| Case | Action |
| --- | --- |
| Sticky or already active ws | `Ignore` |
| CapsLock physically held | `MoveWindowToCurrentWorkspace` |
| Else | `SwitchToWindowWorkspace` |

`ShouldIgnoreEvent` drops spurious FOREGROUND callbacks during Caps+digit sliding (internal transition, already hidden by us, or stale hwnd ≠ current foreground) so Caps-held moves only apply to real taskbar/user activations.

Implemented by `ForeignWorkspaceActivationService` (subscribes to foreground changes). `SwitchWorkspace` keeps `BeginInternalOperation` until Dispatcher Background so deferred hook callbacks still see the suppress.
