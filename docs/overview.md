---
summary: OverviewService opens one OverviewWindow per monitor; thumbnails, cascade layout, search filter, drag-ghost; Esc closes all together.
when: Exposé/overview UI, thumbnail capture, drag between spaces, overview search.
sources: [Services/OverviewService.cs, OverviewWindow.xaml.cs, Overview/]
---

# Overview (Exposé)

## Entry

`OverviewService.Toggle()` — default hotkey `CapsLock+\`` (`AppConfig.OverviewHotkey`).

- If any overview is open → `CloseAll()` (all monitors together).
- Else open one `OverviewWindow` per connected monitor, sized to work area.
- Preferred focus monitor = hotkey-resolved monitor id.

Close triggers: Esc, selecting a window, toggling again. **Drag-drop** refreshes overlays in place without closing. While dragging a card, mouse wheel scrolls the overview under the cursor (`CaptureMouse` otherwise blocks it); dragging near the top/bottom edge auto-scrolls.

Keyboard while open (via `HotkeyService.OverviewInputFilter` LL hook — does not require WPF focus):

- **Ctrl+F** / typing → search on every monitor; query synced (`BroadcastSearchQuery`).
- **←/→** → cards in the current workspace, then the same workspace id on the left/right monitor (`OverviewArrowNavigation`); no wrap.
- **↑/↓** → previous/next workspace on the **same** monitor only (column index clamped); no wrap, no cross-monitor.
- **Enter** → activate the globally selected card.
- **Ctrl+←/→** → focus adjacent overview window chrome.
- **Esc** → clear shared search, then close all.
- **Caps+`** → closes overview explicitly in the filter (uses hook `_capsHeld`; `GetAsyncKeyState(VK_CAPITAL)` is unreliable after Caps is swallowed). Same chord opens when closed via `OverviewAction`.

While open, workspace commands may be gated via `WorkspaceManager.CommandsEnabled` (see service implementation when changing).

## Thumbnails

- Visible windows (active workspace): live `DwmRegisterThumbnail` inset inside a rounded WPF frame (`ThumbInset` / `CornerRadius` — DWM itself is always rectangular).
- Cloaked / `SW_HIDE` (inactive workspaces): `EnsurePeekCaptures` → `WindowVisibilityService.WithPeekVisible` (brief uncloak/`SW_SHOWNA`, no activate) → `OverviewWindowCapture` (`PrintWindow`) → cached WPF `Image`. Ownership stays `HiddenBySpaces4Win`. Icon fallback if capture fails.
- Do **not** leave inactive windows uncloaked while overview is open (overlay is translucent).

## Supporting pieces (`Overview/`)

| Type | Role |
| --- | --- |
| `OverviewSnapshotBuilder` | Builds per-monitor workspace/window snapshot for UI |
| `OverviewThumbnailService` | DWM/thumbnail capture lifecycle |
| `OverviewWorkspaceAllocator` | Places windows into workspace rows |
| `CascadeLayoutEngine` | Cascade geometry for thumbnails |
| `OverviewSearchFilter` | Title/process filter |
| `OverviewDragGhostWindow` | Visual during drag |
| `OverviewModels` | Snapshot + VM DTOs |
| `WindowIconHelper` | Icons for cards |

Thumbnails: `OverviewThumbnailService` → `DwmRegisterThumbnail`. Selected card scales to ~1.08 with per-frame DWM dest sync (`TransformToAncestor` both corners). Thumbs sit in a rounded inset frame (DWM is rectangular; chrome shows as radius).

## Settings

Overview-related options: Settings → Overview page (`Settings/Pages/OverviewPage.xaml` + VM in `PageViewModels.cs`).
