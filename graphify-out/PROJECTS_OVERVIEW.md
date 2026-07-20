# Projects Knowledge-Graph Overview

`C:\git\projects` is a container of ~13 independent projects (almost all C#), not a
single codebase — so instead of one merged graph, each substantial project got its
**own** knowledge graph + Obsidian vault, and the smaller/low-value ones got a
short written summary. This file is the index.

**How the graphs were built:** code was extracted structurally via AST
(`graphify extract --code-only`) — deterministic, no LLM, no token cost. There was
no Gemini/Google API key present, so markdown/design docs were **not** semantically
extracted into the graphs (the code architecture is captured; the prose "why" is
not). Community numbers are real; community **names** are left as `Community N`
placeholders (naming needs an LLM backend). Ask if you want any specific project's
docs folded in or its communities named.

---

## Full graphs (each has `graph.json`, `GRAPH_REPORT.md`, `graph.html`, `obsidian/`)

| Project | Nodes | Edges | Communities | Top "god node" | Outputs |
|---|--:|--:|--:|---|---|
| **RayTracer** ♻️ | 3,828 | — | 255 | `RayTracer` (150 edges) | [report](../RayTracer/graphify-out/GRAPH_REPORT.md) · [html](../RayTracer/graphify-out/graph.html) · [vault](../RayTracer/graphify-out/obsidian) |
| **SpaceTraders** | 7,514 | 13,529 | 614 | `Application.Interfaces.Repositories` (133) | [report](../SpaceTraders/graphify-out/GRAPH_REPORT.md) · [html*](../SpaceTraders/graphify-out/graph.html) · [vault](../SpaceTraders/graphify-out/obsidian) |
| **SpaceTradersV3** | 972 | 2,145 | 67 | `SpaceTradersApiClient` (62) | [report](../SpaceTradersV3/graphify-out/GRAPH_REPORT.md) · [html](../SpaceTradersV3/graphify-out/graph.html) · [vault](../SpaceTradersV3/graphify-out/obsidian) |
| **TheCuratool** | 939 | 2,514 | 53 | `DraftEngine` / `DraftSessionState` | [report](../TheCuratool/graphify-out/GRAPH_REPORT.md) · [html](../TheCuratool/graphify-out/graph.html) · [vault](../TheCuratool/graphify-out/obsidian) |
| **ByGalacticAccord** | 567 | 1,514 | 16 | `SimulationContext` (131) | [report](../ByGalacticAccord/graphify-out/GRAPH_REPORT.md) · [html](../ByGalacticAccord/graphify-out/graph.html) · [vault](../ByGalacticAccord/graphify-out/obsidian) |
| **AdventureEngine** | 354 | 468 | 27 | `GameSessionService` (22) | [report](../AdventureEngine/graphify-out/GRAPH_REPORT.md) · [html](../AdventureEngine/graphify-out/graph.html) · [vault](../AdventureEngine/graphify-out/obsidian) |

♻️ **RayTracer** was already graphed today (its own vault included docs) — reused untouched.
\* **SpaceTraders** html is a 614-node **community-aggregated** view (the full 7,514-node graph exceeds the 5,000-node interactive limit); use the Obsidian vault for node-level detail.

### What each full graph is about
- **SpaceTraders** — largest project. Clean/hexagonal architecture (`Application.Interfaces.Repositories`, `Application.Ports`, `ShipModel` are the hubs) with a C# backend + TSX frontend. 614 communities — a big, layered system.
- **SpaceTradersV3** — a leaner rewrite; the whole graph pivots on `SpaceTradersApiClient` / `SpaceTradersPortAdapter` (betweenness 0.27) — the API-port adapter is the spine.
- **TheCuratool** — a drafting/game-setup tool; `DraftEngine`, `DraftSessionState`, `GameSession` dominate, with heavy test coverage (`DraftEngineTests` is the #1 node).
- **ByGalacticAccord** — WPF desktop simulation game; `SimulationContext` is an extreme central hub (betweenness 0.43) wiring `ActorState`, `Contract`, `Credits` to the `MainWindow` UI.
- **AdventureEngine** — a Blazor "narrator agent" adventure engine; `GameSessionService` + `NarratorAgent` (`Application.Agents`) are the core.

---

## Brief summaries (not graphed)

### sts2_decompiled — *decompiled Slay the Spire 2 + annotator tooling*
A **decompiled** copy of MegaCrit's *Slay the Spire 2* (Godot/C#, `MegaCrit.Sts2.Core.*`) — **~3,448 C# files** of machine-generated source (`--y__InlineArray*`, `-PrivateImplementationDetails-`, etc.). Alongside it are two first-party tools: **`sts2_Annotator`** (a CLI that syncs extracted game data into PostgreSQL and annotates it — see its README for the `sync-postgres` workflow) and **`sts2_Viewer`**. Not graphed: the decompiled bulk has obfuscated/auto-generated names that make a graph low-value and enormous; the first-party value is just the Annotator/Viewer, which are small.

### DatumPrikker — *Doodle-style availability poll app*
A "datumprikker" (Dutch: date-picker) poll app built on **.NET Aspire** (AppHost + ApiService + ServiceDefaults + Web BFF + Tests). PostgreSQL + EF Core, Blazor Web UI with cookie auth + Google/Microsoft login, and create/respond/results poll flows with share tokens, response dedupe (unique index on `PollOptionId, RespondentKey`), and anonymous rate limiting (30 req / 5 min). ~28 source files — a small but complete vertical slice. Not graphed: small, mostly Blazor pages + EF migrations.

### AiUsageMonitoring — *AI usage budget monitor / alerting service*
A .NET hosted service that tracks Copilot usage against a budget using **Marten event sourcing** (`UsageSnapshotRecorded`, `AlertFired`, `DailyReportSent`), with burndown calculation (ideal/actual/projected), spike detection (delta > avg×2.5), a daily-report gate, and HTML alert/report emails; Dockerized. 24 source files, 74+35 unit tests. Note per `DeploymentNotes.md`: the Claude-usage client was dropped (no API) and there's a known Copilot-client bug + missing CI/K8s manifests. Not graphed: small and self-contained.

### HackerMinigames — *multiplayer hacker-themed browser minigame suite*
ASP.NET Core (.NET 10) + **SignalR** real-time suite where every browser tab is a player sharing live game state; plain HTML/vanilla-JS front end, Kubernetes-targeted. Initial scaffold: game 1 (**Cipher Wheel**) is fully wired end-to-end (move → broadcast → win), 6 more planned. 11 source files. Not graphed: tiny scaffold.

---

## Also present (no code)
- **`.github/`** (6 workflow yml), **`Skyseed/`** (1 md), **`the-curator/`** (2 images), plus a few root markdown files — nothing to graph.

## Next steps you can ask for
- Name the communities and/or fold design docs into any specific project's graph.
- `graphify query "<question>"` against any project's `graph.json` (run from that folder).
- Build a full graph for any folder currently summarized (e.g. the sts2 Annotator tool alone).
