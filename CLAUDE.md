# C:\git\projects — multi-project workspace

This directory holds ~13 independent projects (mostly C#), each self-contained.

The `## graphify` section below applies **only to the projects that actually have a
knowledge graph** — each of these has its own graphify-out/ (graph.json + Obsidian
vault):

  RayTracer/ · SpaceTraders/ · SpaceTradersV3/ · TheCuratool/ · ByGalacticAccord/ · AdventureEngine/

It does **not** apply anywhere else. sts2_decompiled/, DatumPrikker/,
AiUsageMonitoring/, and HackerMinigames/ were only summarized (not graphed) and have
no graphify-out/graph.json — ignore the graphify guidance in those folders; see
graphify-out/PROJECTS_OVERVIEW.md for their summaries. The root itself has no
graph.json either, so **cd into one of the graphed project folders above** before
running the graphify query/path/explain commands; they resolve graphify-out/
relative to the current directory.

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
