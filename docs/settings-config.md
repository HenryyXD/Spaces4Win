---
summary: AppConfig + ConfigService JSON in %APPDATA%; SettingsSession/pages (Workspaces Hotkeys Appearance Behavior Indicator Overview Startup About).
when: Adding a setting, changing defaults, binding UI, config migration.
sources: [Config/AppConfig.cs, Config/ConfigService.cs, Settings/]
---

# Settings & config

## Files

| Path | Writer |
| --- | --- |
| `%APPDATA%\Spaces4Win\config.json` | `ConfigService` (camelCase JSON, enum strings) |
| `%APPDATA%\Spaces4Win\window-layout.json` | `WindowLayoutStore` |

`AppConfig.CreateDefault()` seeds first-run defaults (including CapsLock chord tables).

## Major config groups

- Startup / elevation (`StartWithWindows`, `ElevationPreference`)
- Indicators & theme (`Theme`, `AccentColor`, `Language`, motion)
- Hide mode (`HideInactiveFromSwitcher`)
- Single fullscreen per workspace (`EnforceSingleFullscreenPerWorkspace`, default **true**)
- Transition style/speed
- Per-monitor `Monitors` dictionary (`MonitorConfig`: workspace ids, indicator offsets, …)
- Large set of `HotkeyBinding` properties (switch, move, follow, adjacent, insert, overview, sticky, compact, fullscreen, delete, …)

`window-layout.json` entries include `isMinimized` and `isFullscreen` (Caps+F borderless intent restored on start).

When adding a property: default in `CreateDefault`, bind in the right Settings page VM, persist via `SettingsSession` save path, document in this file’s table if user-visible.

## Settings UI

- Shell: `Settings/SettingsWindow.xaml` + WPF-UI navigation (`SettingsPageProvider`)
- Session: `Settings/ViewModels/SettingsSession.cs` (load/edit/save/apply live services)
- Pages under `Settings/Pages/*` — thin code-behind; logic in `PageViewModels.cs`
- Controls: monitor cards, workspace dots, page header
- Tokens: `Themes/SettingsTokens.xaml`

Apply path should update running `AppServices` (hotkeys, indicators, visibility preference, etc.) without requiring full process restart when possible.
