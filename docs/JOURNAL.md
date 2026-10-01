# Docs journal (incremental)

Append-only log of discoveries and corrections. Newest first.  
Promote durable facts into topic files; leave a one-line pointer here if useful.

## Format

```
### YYYY-MM-DD — short title
- Area: workspaces | visibility | hotkeys | …
- Fact: …
- Source: path or test name
- Promoted: yes/no → docs/….md
```

---

### 2026-08-16 — Session presets (named layouts)
- Area: presets | hotkeys | settings
- Fact: 10 slots in `presets.json` (UI 1–10). Caps+P browser + confirm load; Caps+Ctrl+Alt+digit save + toast; Caps+Alt+digit focus slot. Apply closes unmatched managed windows, places matches, launches missing paths. Cold-start layout still never launches.
- Source: `Services/Presets/*`, `AppConfig` preset hotkeys, Settings Workspaces
- Promoted: yes → docs/hotkeys.md, docs/settings-config.md, docs/invariants.md

### 2026-08-16 — Taskbar minimize vs minimize-to-tray
- Area: visibility | events
- Fact: Iconic (Chrome) stays on the workspace (`SW_HIDE`/`SHOWMINNOACTIVE`). App `EVENT_OBJECT_HIDE` / active-space settle (`MainWindowHandle == 0`, `IsAppTrayHidden`) unmanages tray apps. Never gate show on `MainWindowHandle` after our hide (strands Chrome). After iconic `SW_HIDE`, treat `WS_MINIMIZE` like iconic; do not `Forget` without reveal; `_expectOurHideEvent` filters our hide. Owned satellites keep modal progress with the owner. Peek uncloak only (no `SW_SHOWNA`).
- Source: `WindowVisibilityService`, `WorkspaceManager` hide/minimize handlers, `WindowClassifier`, `TryActivateWindow`
- Promoted: yes → docs/visibility.md, docs/invariants.md

### 2026-07-30 — Caps+Ctrl move+follow window stayed cloaked
- Area: visibility | hotkeys
- Fact: Move hid the window → `WindowCaptureCache` peek on a background thread → follow `ShowForWorkspace` → `EndPeek` recloaked. Fix: `EndPeek` skips restore unless ownership is still `HiddenBySpaces4Win`; move+follow uses `applyVisibility: false`.
- Source: `WindowVisibilityService.EndPeek`, `HotkeyService` MoveWindowAndFollow, `MoveActiveWindowToWorkspace`
- Promoted: yes → docs/visibility.md

### 2026-07-29 — Enforce single fullscreen per workspace
- Area: workspaces | settings
- Fact: `EnforceSingleFullscreenPerWorkspace` (default true). Move into a WS that already has FS (or Caps+F while another is FS) exits the existing borderless FS via `TryExitToMaximized` so both windows stay on the taskbar.
- Source: `Core/SingleFullscreenPerWorkspacePolicy.cs`, `WorkspaceManager.AssignWindowToWorkspace` / `ToggleFullscreenInPlace`
- Promoted: yes → docs/workspaces.md, docs/settings-config.md

### 2026-07-29 — Graceful `--quit` instead of taskkill
- Area: elevation / shutdown
- Fact: Force-kill skips `ShutdownCoordinator` → windows stay cloaked/SW_HIDE until next start. Added `AppControlChannel` + `Spaces4Win.exe --quit` / `--recover-hidden`.
- Source: `Services/AppControlChannel.cs`, `App.xaml.cs`
- Promoted: yes → docs/elevation-startup.md

### 2026-07-28 — Initial docs tree
- Area: meta
- Fact: Created progressive-disclosure docs under `docs/` + root `AGENTS.md` for AI navigation (not CLAUDE).
- Source: repository structure as of this date (`AppServices`, `WorkspaceManager`, services listed in INDEX)
- Promoted: yes → all initial topic files
