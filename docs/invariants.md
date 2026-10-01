---
summary: Non-negotiable behaviors — sparse per-monitor spaces, cloak default, no app auto-launch, minimized shutdown reveal, no MainWindow overlays, classifier heuristics.
when: Before merging behavior changes; agent self-check; regression design.
sources: [docs/workspaces.md, docs/visibility.md, docs/elevation-startup.md]
---

# Invariants

Break these only with an explicit product decision + tests + doc/JOURNAL update.

1. **Per-monitor independence** — switching space on monitor A never changes active workspace on B.
2. **Sparse ids** — creating workspace 5 must not auto-create 2–4. Gaps are allowed.
3. **Id range** — workspaces are 1–9 only; at least one workspace per monitor always remains.
4. **Sticky stays visible** — sticky HWNDs are not hidden on switch on that monitor.
5. **Cloak by default** — prefer DWM cloak; `SW_HIDE` only when `HideInactiveFromSwitcher` is enabled (document the Alt-Tab/taskbar tradeoff).
6. **No focus steal on bulk show** — workspace show uses non-activating show; explicit activate is a separate step.
7. **Shutdown reveal** — inactive hidden windows return **minimized**; coordinator is idempotent.
8. **Layout never launches apps** — cold-start `window-layout.json` restore only remaps already-open windows. **Session presets** (`presets.json`) may launch missing apps after explicit user confirm in the preset browser.
9. **Internal ops don’t re-enter** — visibility/event loops must honor `BeginInternalOperation` / internal transition flags.
10. **Overlays ≠ MainWindow** — keep `Application.MainWindow` null/non-overlay so WPF-UI theme apply doesn’t target HUD/overview/indicator windows.
11. **Classifier** — extend style/ownership heuristics + minimal shell classes; avoid per-app title blacklists without strong cause.
12. **Hotkey CapsLock** — holding CapsLock for chords must not toggle lock; lone CapsLock still toggles.
13. **Tray hide ≠ taskbar minimize** — `EVENT_OBJECT_HIDE` (tray) unmanages; taskbar-minimized (`IsIconic` / `WS_MINIMIZE`) stays on the workspace and is `SW_HIDE`/`SHOWMINNOACTIVE` across switches. Never `Forget` a Spaces4Win-hidden HWND without revealing (except true tray `TOOLWINDOW` after our hide).

## Anti-patterns

- Polling window lists instead of WinEvents (except bounded one-shots).
- Calling `Switch*` from UI without going through the same path hotkeys use when animation is expected (`TransitionCoordinator`).
- Hiding Spaces4Win’s own overlay HWNDs into workspaces.
- Force-push / rewriting history, or editing docs outside this repo’s `docs/` + `AGENTS.md` for Spaces4Win context.
