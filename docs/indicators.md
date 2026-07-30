---
summary: Per-monitor floating WorkspaceIndicatorWindow dots; IndicatorService move mode; pinned-window ring modes; toast helper.
when: Indicator positioning, opacity/scale, click-through, pinned visuals, tray “move indicators”.
sources: [Services/IndicatorService.cs, WorkspaceIndicatorWindow.xaml.cs, IndicatorToastWindow.cs, Services/Indicator/, Config/PinnedWindowIndicatorMode.cs]
---

# Indicators

## Role

Optional floating **dot strip** per monitor showing sparse workspace ids and the active space. Click a dot to switch. Tray/menu **Move indicators** enters reposition mode **without** opening Settings. Hover capsule **edges** for resize cursors; drag to change `IndicatorScale` (anchored opposite edge).

Owned by `IndicatorService`, which creates/updates `WorkspaceIndicatorWindow` instances from `WorkspaceManager` state and `AppConfig` (opacity, scale, animations, show/hide, pinned mode).

## Config knobs (`AppConfig`)

- `ShowFloatingIndicators`
- `IndicatorOpacity`, `IndicatorScale`, `IndicatorAnimations`
- `PinnedWindowIndicatorMode` (how sticky/pinned windows are emphasized — default ring-only)
- `HideIndicatorInFullscreen` (experimental policy hook)
- Per-monitor offsets may live under `MonitorConfig`

## Related UI

- `IndicatorToastWindow` — transient status toasts near indicators.
- Settings → Indicator / Appearance pages.
- Tray: Move / Finish move indicators; indicator can open the same app context menu if tray icon is missing.

Layout helpers: `Services/Indicator/IndicatorLayoutServices.cs`.
