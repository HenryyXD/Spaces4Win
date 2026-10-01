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
| `%APPDATA%\Spaces4Win\presets.json` | `PresetStore` (named session presets slots 0–9) |

`AppConfig.CreateDefault()` seeds first-run defaults (including CapsLock chord tables).

## Major config groups

- Startup / elevation (`StartWithWindows`, `ElevationPreference`)
- Indicators & theme (`Theme`, `AccentColor`, `Language`, motion)
- Hide mode (`HideInactiveFromSwitcher`)
- Single fullscreen per workspace (`EnforceSingleFullscreenPerWorkspace`, default **true**)
- Transition style/speed
- Per-monitor `Monitors` dictionary (`MonitorConfig`: workspace ids, indicator offsets, …)
- Large set of `HotkeyBinding` properties (switch, move, follow, adjacent, insert, overview, sticky, compact, fullscreen, delete, …)

`window-layout.json` entries include `isMinimized`, `isFullscreen`, and `isSticky` (Caps+F / sticky intent).

**Session presets** (`presets.json`): 10 named slots (UI 1–10, storage 0–9). Save = `CaptureLayout`; load confirms then closes extras / places matches / launches missing. Hotkeys: Caps+P browser, Caps+Ctrl+Alt+digit save, Caps+Alt+digit open focused. Rename in browser (F2) or Settings → Workspaces.

When adding a property: default in `CreateDefault`, bind in the right Settings page VM, persist via `SettingsSession` save path, document in this file’s table if user-visible.

## Settings UI

- Shell: `Settings/SettingsWindow.xaml` + WPF-UI navigation (`SettingsPageProvider`)
- Session: `Settings/ViewModels/SettingsSession.cs` (load/edit/save/apply live services)
- Pages under `Settings/Pages/*` — thin code-behind; logic in `PageViewModels.cs`
- Controls: monitor cards, workspace dots, page header
- Tokens: `Themes/SettingsTokens.xaml`

Apply path should update running `AppServices` (hotkeys, indicators, visibility preference, etc.) without requiring full process restart when possible.
