---
summary: Composition root App → AppServices.Start; ownership of WorkspaceManager, hooks, hotkeys, indicators, overview, transitions, shutdown.
when: Wiring a new service, debugging startup, tracing who calls whom.
sources: [App.xaml.cs, AppServices.cs]
---

# Architecture

## Host

```
App.OnStartup
  LocalizationService
  AppServices.Start()     // may Shutdown early if relaunching elevated
  ThemeHelper
  TaskbarIcon (H.NotifyIcon.Wpf)
```

`App.Services` / `App.Localization` are static accessors after init. Overlays must **not** remain `Application.MainWindow` (WPF-UI theme targets MainWindow).

## Composition root — `AppServices.Start` (order matters)

1. Load `Config` → language + motion preference.
2. Maybe relaunch elevated (`AlwaysElevate`).
3. Register/update Task Scheduler autostart.
4. `ShutdownCoordinator.RecoverIfNeeded()` (crash journal).
5. `MonitorTracker.Start()`.
6. Construct `WorkspaceManager` (visibility + workspace ids from config + elevation preference).
7. `HotkeyMonitorContext` attached + started.
8. `HotkeyService` + `WorkspaceTransitionCoordinator` (HUD engine); hotkey switch actions go through coordinator.
9. `OverviewService`; bind overview / adjacent-monitor focus actions.
10. Apply hide mode; `InitializeExistingWindows(LayoutStore)`; persist layout.
11. `WindowEventService.Start()`.
12. `IndicatorService` + foreign activation service + shutdown coordinator for real exit.
13. `IsRunning = true`.

## Service graph (conceptual)

```
MonitorTracker ──► WorkspaceManager ◄── WindowEventService
                       │
                       ├── WindowVisibilityService
                       ├── HotkeyService ──► TransitionCoordinator ──► IWorkspaceTransitionEngine
                       ├── OverviewService ──► OverviewWindow(s)
                       ├── IndicatorService
                       └── ForeignWorkspaceActivationService
```

## Key entry files

| Concern | Start here |
| --- | --- |
| Lifecycle / DI-ish wiring | `AppServices.cs` |
| Domain orchestration | `Core/WorkspaceManager.cs` |
| Per-monitor state | `Core/MonitorWorkspace.cs` |
| Win32 surface | `Native/NativeMethods.cs` |
| Settings MVVM | `Settings/ViewModels/SettingsSession.cs` |

## Tests

`Spaces4Win.Tests` — policies and core behaviors (sparse ids, deletion fallback, foreign activation, overview filter, transitions, etc.). Prefer extending tests when changing invariants.
