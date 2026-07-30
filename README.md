# Spaces4Win

**Per-monitor virtual workspaces for Windows** — like macOS Spaces / Mission Control, but each display keeps its own independent spaces.

Windows’ built-in Virtual Desktops switch **every monitor at once**. Spaces4Win instead shows and hides windows **per monitor**, so you can keep research on the left display and coding spaces on the right without them fighting each other.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)](#requirements)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

> **Screenshot / GIF welcome** — drop a short clip of per-monitor switching into `Assets/` and link it here for the first public release.

---

## Features

- **Independent spaces per monitor** — switch one display without touching the others
- **Sparse workspace ids (1–9)** — create only the spaces you use (e.g. `1`, `3`, `7`); gaps are fine
- **CapsLock chord hotkeys** — CapsLock acts as a modifier while held; editable in Settings
- **Floating dot indicators** — click-through overlay per monitor (optional; movable)
- **Overview (Exposé)** — see every space on every monitor and jump or drag windows
- **Sticky windows** — pin a window so it stays visible across spaces on that monitor
- **Live tracking** — new and moved windows join the right space via WinEvents (no polling)
- **Safe shutdown** — hidden windows come back minimized (no flash, no focus steal)
- **Layout restore** — remembers which open apps belong where; never auto-launches missing apps
- **Admin / UIPI handling** — Ask, always elevate, or never ask when elevated windows appear
- **Start with Windows** — Task Scheduler task (Highest run level when always-elevate is on)
- **Portable `.exe`** — self-contained; no installer and no separate .NET runtime
- **English + Portuguese (Brazil)** UI

---

## Download

1. Open **[Releases](https://github.com/HenryyXD/Spaces4Win/releases)**.
2. Download `Spaces4Win.exe` from the latest release.
3. Run it.

The app lives in the **system tray**. Left-click the icon (or **Settings…** in the menu) to configure monitors, hotkeys, and appearance.

### Requirements

- Windows 10 or 11 (x64)
- No .NET install needed for the published release build

---

## Quick start

1. Start Spaces4Win → tray icon appears.
2. Move the mouse over a monitor and press **`CapsLock+1`** … **`CapsLock+9`** to switch or create that space on **that** monitor.
3. Open **Settings** from the tray to tweak hotkeys, indicators, theme, language, and startup.
4. Press **`CapsLock+\``** for Overview on all monitors.

Config lives in `%APPDATA%\Spaces4Win\` (`config.json`, `window-layout.json`).

---

## Default hotkeys

Hotkeys apply to the monitor under the **mouse cursor** (unless noted). All chords are editable in Settings. Top-row and numpad digits both work.

| Shortcut | Action |
|----------|--------|
| `CapsLock+1` … `9` | Switch to that workspace, or **create it sparsely** if missing |
| `CapsLock+Shift+1` … `9` | Move the **focused** window to that workspace (create if needed) |
| `CapsLock+Ctrl+1` … `9` | Move focused window and **follow** |
| `CapsLock+←` / `→` | Previous / next **existing** workspace |
| `CapsLock+Shift+←` / `→` | Move window to previous/next existing (no follow, no create at edge) |
| `CapsLock+Ctrl+←` / `→` | Move and follow; **create** at the edge if needed |
| `CapsLock+Alt+←` / `→` | Insert new workspace left/right with focused window, renumber, **stay** |
| `CapsLock+Ctrl+Alt+←` / `→` | Insert left/right, renumber, and **follow** |
| `CapsLock+Tab` | Toggle last workspace; hold CapsLock and press Tab again to cycle with an on-screen picker |
| `CapsLock+Q` | Window switcher for the active workspace (hold CapsLock to cycle; release to activate) |
| `CapsLock+S` | Toggle **sticky** (visible on all workspaces of that monitor) |
| `CapsLock+R` | Compact workspace ids on the target monitor to consecutive `1…N` |
| `CapsLock+F` | Toggle borderless fullscreen for the focused window (stays on the current workspace) |
| `CapsLock+Shift+F` | Move focused window to a free workspace without following |
| `CapsLock+Ctrl+F` | Move focused window to a free workspace and switch to it |
| `CapsLock+Backspace` | Delete active workspace (windows move to previous id, else next; cannot delete the last one) |
| `CapsLock+\`` | Overview (Exposé) on all monitors |

**CapsLock behavior:** while held as part of a chord it does **not** toggle Caps Lock. A CapsLock press/release **without** a chord still toggles it normally.

---

## Administrator windows (UIPI)

Windows blocks a normal process from hiding or moving elevated windows (for example, an IDE started as admin). In **Settings → Administrator / UIPI**:

| Option | Behavior |
|--------|----------|
| **Ask** (default) | Prompt to restart elevated when an admin window cannot be controlled |
| **Always run as administrator** | Prefer an elevated process; with Start with Windows, logon uses a Highest task (no UAC at sign-in). Does **not** force UAC on every manual launch |
| **Never ask** | Skip elevated windows; no prompts |

You can also use **Restart as administrator** from Settings.

---

## How it works (short)

Spaces4Win does **not** use `IVirtualDesktopManager`. It:

1. Tracks each monitor and a sparse set of workspace ids on it.
2. Classifies top-level windows (Alt+Tab-style heuristics).
3. Hides inactive-space windows (prefers **DWM cloaking** over `SW_HIDE` so snap/maximize size survives) and shows the target space **only on that monitor**.
4. Listens with `SetWinEventHook` so creates, destroys, and moves stay in sync.
5. Uses a low-level keyboard hook so CapsLock can act as a modifier.

For contributors and AI-assisted work, see [`AGENTS.md`](AGENTS.md) and [`docs/`](docs/).

---

## Build from source

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```bash
git clone https://github.com/HenryyXD/Spaces4Win.git
cd Spaces4Win
dotnet build -c Release
dotnet test Spaces4Win.Tests/Spaces4Win.Tests.csproj -c Release
```

### Portable single-file publish

```bash
dotnet publish Spaces4Win.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\Spaces4Win.exe`

Trimming (`PublishTrimmed`) is **not** used — WPF relies heavily on reflection.

### Release tags

Pushing a tag `v*` (for example `v0.1.0`) runs [`.github/workflows/release.yml`](.github/workflows/release.yml), which publishes `Spaces4Win.exe` to a GitHub Release.

---

## Contributing

Issues and pull requests are welcome — bug reports, feature ideas, docs, and fixes alike.

Please read **[CONTRIBUTING.md](CONTRIBUTING.md)** before opening a PR. In short:

- Use issues to discuss larger changes before coding when you can
- Keep PRs focused; include a clear description and how you tested
- Run `dotnet test` on the test project when behavior changes
- Be respectful in discussion

Good first directions: accessibility polish, docs/screenshots, edge-case window classification, localization, and tests around workspace policies.

---

## Roadmap ideas

Not commitments — useful if you want to help:

- [ ] First-run tips / short onboarding
- [ ] More transition styles and polish
- [ ] Better multi-DPI / exotic multi-monitor edge cases
- [ ] Packaging options beyond portable exe (optional)
- [ ] Community screenshots and short demo video

---

## Known limitations

- Elevated windows need an elevated Spaces4Win process (or they are skipped) because of Windows UIPI
- Hiding inactive windows with `SW_HIDE` (optional setting) removes them from Alt+Tab/taskbar and can affect restore size; DWM cloak is the default for a reason
- Workspace ids are capped at **1–9** per monitor by design
- This is Windows-desktop software; it is not a tiling window manager

---

## Privacy

Spaces4Win runs locally. Settings and window→workspace layout are stored under `%APPDATA%\Spaces4Win\`. There is no telemetry in this repository.

---

## License

[MIT](LICENSE) © Spaces4Win contributors

---

## Acknowledgments

Inspired by the workflow of macOS Spaces and Mission Control, and by the practical constraints of Win32 window management on multi-monitor Windows setups.
