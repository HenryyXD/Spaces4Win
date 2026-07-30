---
summary: VisibilityOwnership, DWM cloak vs SW_HIDE, internal transition guards, WindowClassifier heuristics, SetWinEventHook tracking, layout store.
when: Windows vanish incorrectly, Alt-Tab issues, restore/minimize bugs, classifier false positives.
sources: [Services/WindowVisibilityService.cs, Core/WindowClassifier.cs, Services/WindowEventService.cs, Services/WindowLayoutStore.cs, Services/WindowLayoutMatcher.cs]
---

# Visibility & window tracking

## `WindowVisibilityService`

Tracks **why** a HWND is invisible:

| `VisibilityOwnership` | Meaning |
| --- | --- |
| `Visible` | Normal shown |
| `UserMinimized` | User iconic |
| `HiddenBySpaces4Win` | We hid/cloaked for inactive space |
| `ExternallyHidden` | Something else hid it |
| `Unknown` | Unclassified |

**Prefer DWM cloaking** over `SW_HIDE` so maximize/snap geometry survives switches.  
If `PreferSwHide` / config `HideInactiveFromSwitcher`: use `SW_HIDE` (leaves Alt-Tab/taskbar; may hurt restore size; breaks taskbar jump-to-inactive-space).

Show paths use **no activate** (`SW_SHOWNOACTIVATE` / uncloak) unless an explicit activate step runs after switch.

### Internal operations

`BeginInternalOperation` / `EndInternalOperation` (+ `_internalTransition`) suppress event feedback loops while we hide/show ourselves.

**Overview peek:** `WithPeekVisible` briefly uncloaks (or `SW_SHOWNA`) Spaces-hidden windows for `PrintWindow` capture, then restores cloak/hide without changing ownership away from `HiddenBySpaces4Win`. Used so Caps+` can show real thumbs for inactive workspaces. `EndPeek` no-ops if ownership is no longer `HiddenBySpaces4Win` (move+follow must not be recloaked by a racing background capture).

Shutdown / crash paths can mark windows to **restore as minimized**.

## `WindowClassifier`

Decides managed vs ignored top-level windows using **style/ownership heuristics** (Alt-Tab / taskbar style; GlazeWM-inspired), plus a small shell class denylist. Avoid growing per-app title blacklists — extend heuristics carefully and add tests.

## `WindowEventService`

`SetWinEventHook` for create/show/destroy/location (and related) so new or dragged windows join the correct monitor’s **active** workspace. Must respect sticky, elevation failures, and internal transitions.

## Layout persistence

- **`WindowLayoutStore`**: `%APPDATA%\Spaces4Win\window-layout.json`.
- **`WindowLayoutMatcher`**: remaps open processes to saved workspace slots by identity (path/title heuristics).
- On start: `InitializeExistingWindows(layout)` — **never launches** missing apps.
- On shutdown: persist layout **before** revealing hidden windows.
