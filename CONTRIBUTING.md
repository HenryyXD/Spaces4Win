# Contributing to Spaces4Win

Thanks for helping improve Spaces4Win. This document explains how to report bugs, propose ideas, and submit pull requests.

## Code of conduct (short)

- Be respectful and constructive
- Assume good intent
- Focus on the problem and the code, not the person

Harassment or bad-faith behavior is not welcome.

## Ways to contribute

- **Bug reports** — something broken or surprising
- **Feature requests** — ideas that fit per-monitor workspaces
- **Documentation** — README, `docs/`, comments that help the next person
- **Code** — fixes, tests, small features
- **Localization** — strings under `Resources/` (en / pt-BR today)

## Before you start

1. Search [existing issues](https://github.com/HenryyXD/Spaces4Win/issues) for duplicates.
2. For larger features, open an issue first so we can align on design.
3. Skim [`AGENTS.md`](AGENTS.md) and [`docs/invariants.md`](docs/invariants.md) if you change workspace or visibility behavior — those rules exist for a reason.

## Reporting bugs

Include as much of this as you can:

- Spaces4Win version (or commit) and Windows version
- Number of monitors / rough layout
- Steps to reproduce
- Expected vs actual behavior
- Whether affected windows were **elevated (admin)**
- Relevant Settings (hide from switcher, elevation preference, etc.)
- Logs or screenshots if useful

## Feature requests

Describe:

- The problem you are trying to solve
- How you would use it day to day
- Whether it must stay **per-monitor independent**

## Development setup

Requirements: Windows + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/HenryyXD/Spaces4Win.git
cd Spaces4Win
dotnet build
dotnet test Spaces4Win.Tests/Spaces4Win.Tests.csproj
```

Useful layout:

| Area | Path |
|------|------|
| Composition / startup | `AppServices.cs`, `App.xaml.cs` |
| Workspace core | `Core/` |
| Hotkeys, visibility, overview, elevation | `Services/` |
| Settings UI | `Settings/` |
| Tests | `Spaces4Win.Tests/` |
| Agent-oriented docs | `docs/`, `AGENTS.md` |

## Pull request checklist

- [ ] PR description explains **why**, not only what
- [ ] Change is focused (avoid drive-by refactors in the same PR)
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` succeeds (add/adjust tests when changing behavior)
- [ ] User-facing strings go through resources when appropriate (`Resources/Strings*.resx`)
- [ ] Docs updated if you change an invariant or public behavior (`docs/JOURNAL.md` + topic file, or README)
- [ ] No secrets, personal paths, or machine-specific config committed

### Commit / PR style

- Prefer clear, short messages (e.g. `Fix sticky window reappearing after monitor unplug`)
- One logical change per PR when practical
- Link related issues (`Fixes #123`)

## What usually gets merged faster

- Bug fixes with a test or a clear repro
- Docs and UX clarity
- Small, reviewable PRs

## What needs discussion first

- Changing default hotkeys or sparse-id rules
- New window-hiding strategies that affect Alt+Tab / taskbar
- Anything that breaks per-monitor independence
- Auto-launching apps on restore (intentionally unsupported today)

## Release process (maintainers)

1. Merge to the default branch.
2. Tag `vX.Y.Z` and push the tag.
3. GitHub Actions builds the portable `Spaces4Win.exe` and attaches it to the Release.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
