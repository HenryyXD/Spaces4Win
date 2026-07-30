---
summary: ElevationPreference Ask/AlwaysElevate/NeverAsk for UIPI; Task Scheduler Spaces4Win\Autostart; ShutdownCoordinator minimized reveal + SessionJournal recovery.
when: Admin window can’t hide, UAC loops, autostart, crash leave windows hidden, exit flash.
sources: [Services/ElevationService.cs, Services/StartupService.cs, Services/ShutdownCoordinator.cs, Services/SessionJournal.cs, ElevationPromptWindow.xaml.cs, AppServices.cs]
---

# Elevation, startup, shutdown

## UIPI / elevation

Unelevated Spaces4Win cannot hide/move elevated HWNDs. Preference (`ElevationPreference`):

| Value | Behavior |
| --- | --- |
| `Ask` | Prompt (`ElevationPromptWindow`) when assistance needed |
| `AlwaysElevate` | Prefer elevated process; Highest logon task when Start with Windows; **not** UAC on every manual start if already handled |
| `NeverAsk` | Skip elevated windows; no prompts |

`WorkspaceManager` raises `ElevationAssistanceNeeded`; `AppServices` handles prompt / relaunch. `ElevationService` detects elevation and relaunches with runas.

## Start with Windows

`StartupService` registers Task Scheduler task `Spaces4Win\Autostart` (clears legacy Run key). With Always elevate, task RunLevel Highest so login has no UAC. Registration runs after elevation state is known (`AppServices.Start`).

## Shutdown

`ShutdownCoordinator.Execute()` (idempotent):

1. Disable commands; clear hotkeys.
2. Persist window layout (includes `isFullscreen` while still borderless).
3. Exit all Caps+F managed fullscreen windows to maximized (restore chrome).
4. Reveal Spaces4Win-hidden windows as **minimized** (no activation / no flash).
5. Tear down indicators / journal bookkeeping.

Graceful external exit: `Spaces4Win.exe --quit` → `AppControlChannel` event → same path as tray Exit. **Do not force-kill** (leaves windows hidden until next start / `--recover-hidden`).

Single instance: `SingleInstanceGuard` (`Local\Spaces4Win.SingleInstance`). A second normal launch exits immediately. Elevated relaunch uses `--elevating` and waits for the previous process to exit (mutex held until `OnExit`).

## Crash recovery

`SessionJournal` records in-flight hidden state. Early in `Start`, a probe `ShutdownCoordinator.RecoverIfNeeded()` restores windows if the previous session died mid-hide. Standalone: `Spaces4Win.exe --recover-hidden`.
