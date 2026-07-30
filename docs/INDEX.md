---
summary: Machine-oriented map of Spaces4Win docs; one precise cue per file so agents can skip unread topics.
when: Need a richer index than AGENTS.md, or regenerating routing after adding a doc.
---

# Docs index

Each row is a **tier-1 cue**: match the task, then `Read` only that path.

| Cue (what this file contains) | Path |
| --- | --- |
| User-facing product model; why per-monitor spaces exist; portable exe / tray host | [`product.md`](product.md) |
| `App` → `AppServices.Start` wiring; who owns WorkspaceManager, hotkeys, overview, transitions | [`architecture.md`](architecture.md) |
| `MonitorWorkspace` sparse ids 1–9; sticky; switch/create/move/follow/insert/delete/compact APIs on `WorkspaceManager` | [`workspaces.md`](workspaces.md) |
| `WindowVisibilityService` ownership enum; cloak vs SW_HIDE; `WindowClassifier` Alt-Tab heuristics; `WindowEventService` hooks | [`visibility.md`](visibility.md) |
| `HotkeyService` CapsLock LL hook; Caps+Tab/Q switchers; `HotkeyMonitorContext`; `ForeignActivationPolicy` | [`hotkeys.md`](hotkeys.md) |
| `OverviewService` multi-monitor Exposé; `OverviewWindow`; cascade layout; search; drag ghost | [`overview.md`](overview.md) |
| `IndicatorService` / `WorkspaceIndicatorWindow`; move mode; pinned ring modes | [`indicators.md`](indicators.md) |
| `WorkspaceTransitionCoordinator` + HUD/curtain engines; rapid-switch cancel | [`transitions.md`](transitions.md) |
| `%APPDATA%` config/layout; `AppConfig` knobs; Settings pages + `SettingsSession` | [`settings-config.md`](settings-config.md) |
| `ElevationPreference` / UIPI; Task Scheduler `Spaces4Win\Autostart`; `ShutdownCoordinator` + journal recovery | [`elevation-startup.md`](elevation-startup.md) |
| Non-negotiable behavioral invariants and anti-patterns | [`invariants.md`](invariants.md) |
| Short definitions of domain terms used across docs and code | [`glossary.md`](glossary.md) |
| Rules for adding/splitting docs without bloating AGENTS.md | [`how-to-extend-docs.md`](how-to-extend-docs.md) |
| Chronological agent/human discoveries that correct or extend topic docs | [`JOURNAL.md`](JOURNAL.md) |

## Source map (folders)

| Folder | Role |
| --- | --- |
| `Core/` | Workspace model, monitors, window classification |
| `Services/` | Hotkeys, visibility, events, indicators, overview, transition, elevation, layout |
| `Config/` | `AppConfig`, JSON load/save |
| `Overview/` | Snapshot/thumbnail/layout helpers for Exposé |
| `Settings/` | WPF-UI settings shell and pages |
| `Native/` | P/Invoke |
| `Localization/` + `Resources/` | en / pt-BR strings |
| `Spaces4Win.Tests/` | xUnit coverage for core policies |
