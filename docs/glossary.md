---
summary: Glossary of Spaces4Win domain terms — sparse, sticky, cloak, monitorId, foreign activation, journal, follow.
when: Unfamiliar term in code/docs; writing user-facing explanations with an AI agent.
---

# Glossary

| Term | Meaning |
| --- | --- |
| **Sparse workspace** | Only explicitly created ids exist (e.g. `{1,3,7}`); intermediates are not auto-created |
| **monitorId** | Stable key for a display (typically WinForms/device name string) |
| **Active workspace** | The visible space id on one monitor |
| **Last active** | Previous active id on that monitor (toggle-last) |
| **Sticky / pinned** | Window shown on all spaces of its monitor; not hidden on switch |
| **Follow** | After moving a window to another space, also switch to that space |
| **Cloak** | DWM cloaking hides a window without `SW_HIDE` (preserves geometry better) |
| **Foreign activation** | Foregrounding a window that belongs to a non-active space (e.g. taskbar) |
| **UIPI** | Integrity isolation — unelevated process can’t manipulate elevated HWNDs |
| **Journal** | Session record used to recover if the process dies while windows are hidden |
| **Overview / Exposé** | Full-monitor UI of all spaces/windows for picking or dragging |
| **HUD transition** | Animated overlay during space switch (cancellable on rapid input) |
| **Ownership** | `VisibilityOwnership` — who caused a window to be invisible |
| **Internal operation** | Bulk hide/show where WinEvents must be ignored to avoid feedback |
| **Layout document** | Saved window→workspace mapping for next start remap |
