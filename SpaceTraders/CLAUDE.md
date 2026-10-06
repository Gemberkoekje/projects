## Claude's role in this project

Claude fixes things that don't work the way they are intended to. It does not tune strategy.

- In scope: crashes and repeating errors, loops, ships or plans stuck in a state,
  state that never advances, events or data that are never recorded, unbounded growth
  (database, logs), wasted API calls, and behaviour that contradicts the code's own
  intent (comments, tests, health rules, docs).
- Out of scope: budgets, thresholds, priorities, how credits are split between
  building, trading and mining, which strategy to follow, and settings values.
  At most, mention an observation under "Noticed" and change nothing.
- Not sure whether something is intended? Ask, with the evidence. Don't guess.
- Every fix starts with a test that reproduces the misbehaviour.
- Touching a file that has build warnings? Consider fixing them in the same change.
  Mention any you leave, and why.

The current plan, the open issues and the decisions are in `PLAN.md`; finished slices and fixed bugs
are in `docs/archive/PLAN_HISTORY.md`.

## graphify

Knowledge graph at graphify-out/ (god nodes, communities, cross-file
relationships). If you generated one, an Obsidian vault lives at
graphify-out/obsidian/.

INVOCATION: the `graphify` CLI may not be on PATH. The interpreter recorded at
graphify-out/.graphify_python always works — prefer it:
  bash:       "$(cat graphify-out/.graphify_python)" -m graphify <args>
  powershell: & (Get-Content graphify-out/.graphify_python) -m graphify <args>
(If that file is missing, run graphify once in this folder to recreate it.)

Rules:
- Codebase questions: run `... -m graphify query "<question>"` first (also
  `path "<A>" "<B>"`, `explain "<concept>"`). Every result line carries
  `src=<file>` — the real source. Use query to LOCATE, then Read/Edit the actual
  src= file. Never treat graph or vault text as the source of truth for a fix;
  it is derived and may lag the code.
- Staleness: the graph/vault reflect the last extraction and go stale on edit.
  `... -m graphify update .` re-runs AST and refreshes CODE-derived nodes cheaply
  (no API cost). Nodes from SEMANTIC extraction (docs, config/YAML, papers,
  images) are NOT refreshed by AST-only update — those need a full re-extract.
  So: code edits -> `update .`; doc/config edits -> re-extract, or treat the
  graph as describing the prior state.
- graphify-out/obsidian/ (if present) is the human entry point (open in
  Obsidian): _COMMUNITY_*.md per subsystem, [[wikilinks]] between notes,
  graph.canvas for the spatial view. For agent work prefer `query` — same
  relationships, already scoped, with real file paths.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review.
