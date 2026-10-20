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

### 2026-10-20 — No new ghost in the diagnostic log
- Area: visibility
- Fact: `spaces4win.log` through this session has one warning (elevated relaunch did not start) and breadcrumbs for overview/switch. No new hidden-helper or shell-cloaked class showed up. The conhost, RemoteApp, and suspended Settings filters stay as they are.
- Source: `%APPDATA%\Spaces4Win\logs\spaces4win.log`
- Promoted: no

### 2026-10-20 — First-run tips and Ctrl+Alt scheme
- Area: settings | hotkeys
- Fact: `HasSeenWelcome` is false until the welcome window closes. `UseCtrlAltInsteadOfCapsLock` clears CapsLock and ORs Ctrl+Alt on every binding that still uses CapsLock. About copies the last 80 lines of `spaces4win.log` and opens `logs\`.
- Source: `WelcomeWindow`, `AppConfig.UseCtrlAltInsteadOfCapsLock`, `AboutPageViewModel.CopyDiagnostics`
- Promoted: yes → docs/settings-config.md

### 2026-10-20 — Overview X hover stayed clear and tooltip was boxes
- Area: overview
- Fact: Local `Button.Background` outranks a template trigger on the same property, so the Windows red never painted. Set `Border.Background` on the named template border. The tooltip inherited `Segoe MDL2 Assets` from the button and drew "Fechar janela" as missing glyphs; keep that font on the icon `TextBlock` only.
- Source: `OverviewWindow.CreateCloseButtonStyle`
- Promoted: no

### 2026-10-20 — Overview card X closed the menu
- Area: overview
- Fact: `PreviewMouseLeftButtonDown` on the X set `Handled`, so `Button.Click` never ran. The card mouse-up then called `SelectWindow`, which closes the overview. Leave the preview unhandled, ignore that hit for selection, post `WM_CLOSE`, and refresh after the hwnd is gone.
- Source: `OverviewWindow.BuildCardHeader`, `CloseManagedWindow`
- Promoted: overview.md

### 2026-10-20 — Overview search caret stayed at index 0
- Area: overview
- Fact: Hook typing replaces `TextBox.Text` via `SetSearchQuery`. WPF resets the caret to 0. Move it to `Text.Length` after the assignment (and again on Input priority).
- Source: `OverviewWindow.SetSearchQuery`
- Promoted: no

### 2026-10-20 — Suspended Settings frame looked open
- Area: visibility | overview
- Fact: After Configurações closes, `ApplicationFrameWindow` stays visible to `IsWindowVisible` but `DWM_CLOAKED_SHELL` (value 2). That frame was managed and showed as a card. Shell-cloaked, non-minimized frames are not managed. Our cloak remains `DWM_CLOAKED_APP`.
- Source: `WindowClassifier.IsShellCloaked`, live hwnd pid 1624 class ApplicationFrameWindow title Configurações cloak=2
- Promoted: yes → docs/visibility.md

### 2026-10-20 — Inactive workspace thumbnails were black
- Area: overview | visibility
- Fact: PrintWindow after cloak returns a flat black bitmap for composited windows (Rider/Java, WPF). Capture now runs in `WindowHiding`, while the window is still on screen (`BitBlt` + `CAPTUREBLT`). Uniform-dark frames are not cached.
- Source: `WindowVisibilityService.WindowHiding`, `OverviewWindowCapture.TryCaptureVisible`, `WindowCaptureCache.CaptureNow`
- Promoted: yes → docs/overview.md

### 2026-10-20 — Screenshot shortcuts while overview is open
- Area: overview | hotkeys
- Fact: The overview LL filter swallowed Print Screen and Win+Shift+S, so the shell never captured the Caps+` screen. Those chords (and Shift/Alt/Win holds) now pass through.
- Source: `OverviewService.ShouldPassScreenshotToSystem`
- Promoted: yes → docs/overview.md

### 2026-10-20 — Diagnostic log for crash and UI stall
- Area: elevation / startup
- Fact: `%APPDATA%\Spaces4Win\logs\spaces4win.log` records unhandled exceptions, visibility/transition/preset/shutdown failures, and UI-thread stalls (watchdog, 8s). `session.pid` flags a session that did not exit cleanly. Breadcrumbs (switch, overview, preset) are kept on the error entry.
- Source: `Services/DiagnosticLog.cs`, `App.OnStartup`
- Promoted: yes → docs/settings-config.md, docs/elevation-startup.md

### 2026-10-20 — Hidden conhost and RemoteApp hosts in overview
- Area: visibility | overview
- Fact: Invisible `ConsoleWindowClass` (`node`, `wslhost`) and `TscShellContainerClass` (`RemoteApp` / `msrdc`) were managed because they have a title and a normal rect, so they showed up as overview cards. Visible, minimized, or cloaked consoles stay managed. Workspace restore still applies when `WasHiddenByUs` even if the classifier rejects the HWND.
- Source: `WindowClassifier.IsInvisibleHelperHost`, `WorkspaceManager.ApplyVisibilityFromAssignments`
- Promoted: yes → docs/visibility.md

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
