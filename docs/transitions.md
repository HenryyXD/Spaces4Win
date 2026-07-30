---
summary: WorkspaceTransitionCoordinator cancels in-flight HUD on rapid switch; engines HUD/curtain/DWM/null; SwitchOrCreate goes through coordinator.
when: Animation glitches, overlapping overlays, reduce-motion, transition style/speed.
sources: [Services/Transition/, Services/Animation/AnimationSettingsService.cs, Config/WorkspaceTransitionOptions.cs]
---

# Workspace transitions

## Coordinator

`WorkspaceTransitionCoordinator` implements `IWorkspaceNavigator`:

- `SwitchOrCreate` / `Switch` / `SwitchToLast` / `SwitchAndActivate`
- Per-monitor in-flight state; **rapid switches cancel** the current HUD and start the latest target
- Uses `IAnimationSettingsService` + config (`WorkspaceTransitionStyle`, `WorkspaceTransitionSpeed`, `MotionPreference`)

Hotkey switch/create/last are bound to the coordinator in `AppServices` (not bare manager calls).

## Engines (`IWorkspaceTransitionEngine`)

| Engine | Notes |
| --- | --- |
| `HudWorkspaceTransitionEngine` | Default wired in `AppServices` — HUD overlay |
| `CurtainTransitionEngine` | Alternate curtain overlay |
| `DwmWorkspaceTransitionEngine` | DWM-oriented path |
| `NullWorkspaceTransitionEngine` | Instant; no overlay |

Shared types: `TransitionModels.cs` (`TransitionRequest`, `TransitionScene`, `TransitionResult`, …). Overlay windows must stay out of workspace management (classifier / owned by us).

## Motion preference

`MotionPreference` / legacy `ReduceMotion` synced on load/save. System “reduce motion” should shorten or skip decorative animation via `AnimationSettingsService`.
