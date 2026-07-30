# Spaces4Win — agent guide

Per-monitor virtual workspaces for Windows (WPF, .NET 8). Independent sparse spaces 1–9 per display via HWND hide/show (not Windows Virtual Desktops).

## How to use this docs tree

1. Read this file (always).
2. Match the task to **one** row in the index below; `Read` only that file.
3. Open source paths listed there — do not dump the whole tree into context.
4. After a non-obvious finding, append a short entry to [`docs/JOURNAL.md`](docs/JOURNAL.md).

Do **not** load every `docs/*.md` up front. Do **not** edit `C:/Workdir/CLAUDE` from this repo.

## Commands

```bash
dotnet build -c Debug
dotnet test Spaces4Win.Tests/Spaces4Win.Tests.csproj -c Debug
# Graceful stop before rebuild (never taskkill — leaves windows hidden):
bin/Debug/net8.0-windows/Spaces4Win.exe --quit
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Config / layout live under `%APPDATA%\Spaces4Win\` (`config.json`, `window-layout.json`).

## Doc index (progressive disclosure)

| When the task involves… | Read |
| --- | --- |
| Product intent, user model, vs Virtual Desktops | [`docs/product.md`](docs/product.md) |
| Composition root, service graph, startup order | [`docs/architecture.md`](docs/architecture.md) |
| Sparse workspaces, sticky, switch/move/insert/delete | [`docs/workspaces.md`](docs/workspaces.md) |
| Cloak vs SW_HIDE, ownership, classifier, WinEvents | [`docs/visibility.md`](docs/visibility.md) |
| CapsLock chords, monitor targeting, foreign activation | [`docs/hotkeys.md`](docs/hotkeys.md) |
| Mission Control overview, thumbnails, drag | [`docs/overview.md`](docs/overview.md) |
| Floating dots, move mode, pinned indicator | [`docs/indicators.md`](docs/indicators.md) |
| HUD/curtain transitions, cancel-on-rapid-switch | [`docs/transitions.md`](docs/transitions.md) |
| `AppConfig`, settings UI, persistence paths | [`docs/settings-config.md`](docs/settings-config.md) |
| UIPI elevation, Task Scheduler autostart, shutdown | [`docs/elevation-startup.md`](docs/elevation-startup.md) |
| Hard rules agents must not violate | [`docs/invariants.md`](docs/invariants.md) |
| Domain vocabulary (monitorId, sticky, sparse…) | [`docs/glossary.md`](docs/glossary.md) |
| How to grow this docs tree safely | [`docs/how-to-extend-docs.md`](docs/how-to-extend-docs.md) |
| Dated discoveries / corrections | [`docs/JOURNAL.md`](docs/JOURNAL.md) |

Full cue list (same content, longer summaries): [`docs/INDEX.md`](docs/INDEX.md).

## Hard constraints (always)

- Workspaces are **per-monitor** and **sparse** (ids 1–9; gaps allowed; never auto-fill intermediates).
- Prefer **DWM cloak** over `SW_HIDE` unless `HideInactiveFromSwitcher` is on.
- Shutdown must reveal inactive windows as **minimized** (no flash / no focus steal); layout persist before reveal.
- Layout restore **never launches** missing apps.
- Prefer small, focused diffs; keep docs incremental via JOURNAL + targeted topic edits.

## Explaining the app to a human (via an AI agent)

Ask the agent to: read `AGENTS.md` → `docs/product.md` → `docs/architecture.md` → then only domains the user cares about. Use glossary for terms.
