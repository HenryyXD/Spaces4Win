---
summary: Sparse workspace ids 1–9 per MonitorWorkspace; sticky; Caps+F fullscreen; WorkspaceManager switch/move/follow/insert/delete/compact and focus memory.
when: Changing switch semantics, sticky, renumber, deletion fallback, multi-monitor assignment.
sources: [Core/MonitorWorkspace.cs, Core/WorkspaceManager.cs, Core/MonitorTracker.cs]
---

# Workspaces (core)

## Model

- **`MonitorTracker`**: enumerates displays (`Screen.AllScreens`), reacts to display changes; monitor key is typically device name.
- **`MonitorWorkspace`**: one physical monitor + dictionary of workspace id → `HashSet<HWND>`.
- **Sparse**: only existing ids live in the dict. `EnsureWorkspace(n)` creates **only** `n` (no fill of 2 when jumping 1→3).
- **Ids**: 1–9 (`MaxWorkspaceId = 9`). At least one workspace always exists.
- **`ActiveWorkspace` / `LastActiveWorkspace`**: for toggle-last and transitions.
- **`StickyWindows`**: visible on every space of that monitor; excluded from hide sets on switch.

## `WorkspaceManager` responsibilities

- Rebuild monitors when topology changes.
- Admit/classify windows; assign to active space of the correct monitor.
- Switch / create / delete / insert-with-renumber / compact ids to `1…N`.
- Move window (and optional follow).
- Toggle sticky for focused window.
- Focus tracking per workspace; activate a sensible window after switch.
- Track interactive Win32 move/size (`_moveSizeHwnd`) for drag-follow behavior.
- Raise `WorkspaceChanged`, `WorkspaceIdsChanged`, `ElevationAssistanceNeeded`, `StateChanged`.
- `CommandsEnabled` gate (disabled during shutdown).

## Important semantics

| Operation | Behavior |
| --- | --- |
| Switch or create N | Ensure N exists sparsely, then hide non-sticky of old / show of new |
| Delete active | Windows go to previous id else next; cannot delete last workspace |
| Insert left/right | Renumber; may stay or follow depending on hotkey |
| Compact (`CapsLock+R`) | Sort existing ids and remap densely to `1…N`; sticky unchanged; no-op if already dense |
| Fullscreen (`CapsLock+F`) | Borderless in place on current workspace; toggle exits to maximized; layout `IsFullscreen`. With `EnforceSingleFullscreenPerWorkspace` (default on), entering FS exits any other FS window on that workspace; moving a window into a workspace that already has FS also exits that FS so both stay on the taskbar. |
| Send free (`CapsLock+Shift+F`) | Move focused window to free workspace; stay on current |
| Send free + follow (`CapsLock+Ctrl+F`) | Move to free workspace and switch |
| Adjacent move | Existing only, or create at edge when follow-create chords |
| Sticky | Not hidden on switch; foreign-activation ignore |

## Monitor targeting

Hotkeys resolve which monitor via `HotkeyMonitorContext` / `ResolveMonitorIdForHotkeys()` (cursor vs focus policy). Switching on one monitor never mutates another monitor’s active id.

## Source of truth for ids in config

`AppConfig.GetWorkspaceIds(monitorId)` feeds initial ids into `WorkspaceManager`; id list changes persist back through settings/config when the manager raises `WorkspaceIdsChanged`.
