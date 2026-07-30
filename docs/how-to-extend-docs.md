---
summary: How to grow Spaces4Win docs with progressive disclosure — short AGENTS index rows, YAML summary cues, JOURNAL first, split when a topic exceeds ~150 lines.
when: Adding features, correcting stale docs, teaching an AI agent how the app works.
---

# How to extend these docs

## Goals

- **Token-cheap**: agents load `AGENTS.md` always; topic files only on demand.
- **Incremental**: new knowledge lands in `JOURNAL.md` the same day, then promotes into topic files.
- **Accurate**: prefer pointers to source over pasting large code blocks.

## Workflow for a new finding

1. Append to [`JOURNAL.md`](JOURNAL.md) (date, area, fact, optional source path).
2. If the fact is durable, patch the matching topic file (or add a new one).
3. If you added a **new** topic file:
   - Add YAML frontmatter with `summary:` (one precise sentence — inputs/outputs/conditions, not “covers X”).
   - Add one row to `AGENTS.md` index **and** `INDEX.md`.
4. Keep `AGENTS.md` under ~120 lines. Move narrative into topics.

## Writing rules

- Start each topic with frontmatter: `summary`, `when`, optional `sources`.
- Use tables for mappings; bullets for invariants.
- Link to files with repo-relative paths; name types/symbols agents can grep.
- Do not duplicate README hotkey tables — link to `README.md` for user-facing lists.
- Do not invent APIs; if unsure, note uncertainty in JOURNAL and verify in code.

## Explaining the app through an AI agent

Suggested prompt pattern:

> Read `AGENTS.md`, then `docs/product.md` and `docs/architecture.md`. Explain how Spaces4Win works for a new user, then go deeper on \<topic\> using only the matching doc + cited source files.

For deep dives, name the domain (`visibility`, `hotkeys`, …) so the agent skips unrelated docs.
