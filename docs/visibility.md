---
summary: VisibilityOwnership, DWM cloak vs SW_HIDE, taskbar-minimize vs tray-hide, owned satellites, WinEvents, layout store.
when: Windows vanish incorrectly, Alt-Tab issues, tray apps reopen, restore/minimize bugs, classifier false positives.
sources: [Services/WindowVisibilityService.cs, Core/WindowClassifier.cs, Services/WindowEventService.cs, Services/WindowLayoutStore.cs, Services/WindowLayoutMatcher.cs]
---

# Visibility & window tracking

## `WindowVisibilityService`

Tracks **why** a HWND is invisible:

| `VisibilityOwnership` | Meaning |
| --- | --- |
| `Visible` | Normal shown |
| `UserMinimized` | User iconic (taskbar minimize) |
| `HiddenBySpaces4Win` | We hid/cloaked for inactive space |
| `ExternallyHidden` | Something else hid it (legacy marker) |
| `Unknown` | Unclassified |

**Prefer DWM cloaking** over `SW_HIDE` so maximize/snap geometry survives switches.  
If `PreferSwHide` / config `HideInactiveFromSwitcher`: use `SW_HIDE` (leaves Alt-Tab/taskbar; may hurt restore size; breaks taskbar jump-to-inactive-space).

Show paths use **no activate** (`SW_SHOWNA` / uncloak) unless an explicit activate step runs after switch.

### Minimized vs tray

| Category | Detection | Behavior |
| --- | --- | --- |
| Taskbar minimize | `IsIconic` / `WS_MINIMIZE` | Stays on workspace; inactive → `SW_HIDE` / `SHOWMINNOACTIVE` |
| Minimize-to-tray | `EVENT_OBJECT_HIDE`, settle (`IsAppTrayHidden` / `MainWindowHandle == 0` while still on active space), or `LooksLikeTrayToolWindow` | Unmanage; do not `SHOWMINNOACTIVE` |

**Do not** gate restore on `MainWindowHandle == 0` after *our* `SW_HIDE` — that clears the process main window for Chrome too. Use `MainWindowHandle` only on the active space (we have not hidden the HWND yet).

**Do not** `Forget` a Spaces4Win-hidden HWND without revealing — after iconic `SW_HIDE`, `IsIconic` may clear and size/title heuristics can reject the window; treat `WS_MINIMIZE` like iconic and keep pending restore until show or a true tray `TOOLWINDOW` hide.

Init adopts visible **or** iconic windows. Overview may show “Minimized” for taskbar-minimized participants.

### Internal operations

`BeginInternalOperation` / `EndInternalOperation` (+ `_internalTransition`) suppress event feedback loops while we hide/show ourselves. `_expectOurHideEvent` / `WasHiddenByUs` distinguish our `SW_HIDE` from tray `EVENT_OBJECT_HIDE`.

**Overview peek:** `WithPeekVisible` briefly **uncloaks** Spaces-hidden windows for `PrintWindow` — never `SW_SHOWNA` (would pull tray apps onto the desktop). `EndPeek` no-ops if ownership is no longer `HiddenBySpaces4Win` (move+follow must not be recloaked by a racing background capture).

### Owned satellites

When hiding a managed owner, also hide owned top-level windows + `GetLastActivePopup` (modal copy/delete progress), restore on show/shutdown reveal.

## `WindowClassifier`

Decides managed vs ignored top-level windows using **style/ownership heuristics** (Alt-Tab / taskbar style; GlazeWM-inspired), plus a small shell class denylist. Avoid growing per-app title blacklists — extend heuristics carefully and add tests.

## `WindowEventService`

`SetWinEventHook` for create/show/**hide**/destroy/location/move-size/**minimize start/end** so new or dragged windows join the correct monitor’s **active** workspace, tray hides unmanage, and taskbar minimize stays tracked. Must respect sticky, elevation failures, and internal transitions.

## Layout persistence

- **`WindowLayoutStore`**: `%APPDATA%\Spaces4Win\window-layout.json`.
- **`WindowLayoutMatcher`**: remaps open processes to saved workspace slots by identity (path/title heuristics).
- On start: `InitializeExistingWindows(layout)` — **never launches** missing apps; skips iconic HWNDs.
- On shutdown: persist layout **before** revealing hidden windows.
