---
summary: Product intent — per-monitor sparse workspaces via HWND visibility, tray-only WPF host, not IVirtualDesktopManager.
when: Explaining the app, onboarding, comparing to Virtual Desktops / GlazeWM / FancyZones.
sources: [README.md, App.xaml.cs]
---

# Product

## One sentence

Spaces4Win gives each physical monitor its own sparse set of virtual workspaces (ids 1–9), switched by showing/hiding windows on that monitor only.

## Why not Windows Virtual Desktops

`IVirtualDesktopManager` spans **all** monitors together. Spaces4Win needs independence: switching space on monitor A must not change monitor B. Implementation: manage top-level HWND visibility (prefer DWM cloak) per monitor.

## User-facing surface

- Runs from the **system tray** (no main window as app chrome).
- Floating **dot indicators** per monitor (optional).
- Global **CapsLock-chord** hotkeys (editable).
- **Overview** (Mission Control / Exposé) on all monitors (`CapsLock+\`` by default).
- Settings window (WPF-UI) for monitors, hotkeys, appearance, behavior, elevation, startup.
- UI languages: System / en / pt-BR.

## Persistence

| File | Purpose |
| --- | --- |
| `%APPDATA%\Spaces4Win\config.json` | Preferences, hotkeys, per-monitor workspace id lists |
| `%APPDATA%\Spaces4Win\window-layout.json` | Window→workspace remap for apps still open after restart |

Restore never auto-launches apps that are not running.

## Distribution

Self-contained single-file `Spaces4Win.exe` (`net8.0-windows`, win-x64). Trimming disabled (WPF reflection).

Human-oriented feature/hotkey tables: see root [`README.md`](../README.md).
