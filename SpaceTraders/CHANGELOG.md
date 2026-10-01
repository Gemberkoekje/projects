# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Docs – Added (2026-10-01)
- `PLAN.md`: the plan for getting the bot safely back on the cluster, with the known issues found by reading the code (B1–B25) and the decisions taken (D1–D11).
- `docs/HOW_IT_WORKS.md`: what the code does today, written from the code.
- `docs/archive/README.md`: an index of the archived documents.
- `.claude/skills/st-investigate/SKILL.md`: draft of the skill Claude uses to investigate and fix misbehaviour.

### Docs – Changed (2026-10-01)
- Moved the old plans, designs, progress logs, strategy notes and the `docs/implementation/` and `docs/operations/` documents to `docs/archive/`.
- `README.md`, `CONTRIBUTING.md`, `docs/GLOSSARY.md` and `spacetraders.md` now match the code: no `SpaceTraders.App`, no `k8s/` folder, no `/control/sync` or `/control/ships/{symbol}/reassign`, and the burst limit as the API guide defines it.
- `CLAUDE.md`: what Claude works on in this project, and that it considers fixing build warnings in the files it touches.

### Config – Changed (2026-10-01)
- `System.Net.Http` logs at Warning instead of Information (`appsettings.json`, `appsettings.Development.json`), removing about four log lines per outbound API call.

### Code – Added (2026-10-01)
- One switch per plan: `Automation.Plan.{Scout,Contract,ProbeDeployment,Mining,Trading}.Enabled`. Scout and Contract are on by default, the other three off (D9). A plan that is off isn't bootstrapped, buys nothing, and its ships' goals wait.
- Per-ship circuit breaker: a ship that takes more than `Automation.CircuitBreaker.MaxGoalStepsPerMinute` goal steps in a minute (default 60) gets its goal blocked as `runaway`, with a warning and the `spacetraders_goal_breaker_trips_total` metric. Blocked goals are not stepped again.

### Code – Fixed (2026-10-01)
- `Automation.Enabled` now stops the bot (B5): the tick runs no plans, goal steps or contract work, and no goal step runs from an arrival, the probe handler or startup recovery either.
- A ship that was already at its goal's target no longer loops (B1, the likely cause of the full database in May). In orbit at the target, the scout, probe and mining executors dock instead of navigating to where they are; docked at the target, the survey executor orbits. Navigating to the waypoint a ship is already at does nothing and no longer publishes `ShipNavigationCompletedEvent`.

### Docs – Changed
- Removed obsolete plan-related Markdown files.
- Renamed cleaned SpaceTraders.io reference content to `spacetraders.md`.
- Updated README, contribution guidance, glossary, and implementation docs to match the current code.
- Added `docs/SPACE_TRADERS_IMPLEMENTATION_PLAN.md` to map SpaceTraders.io reference capabilities to current implementation gaps and milestones.

---

## [0.2.0] – 2026-01-01 – Automation foundation

### Code – Added
- Typed `SpaceTradersApiClient` with implemented public/authenticated endpoint subset
- `SpaceTradersApiOptions`, DI registration, `SpaceTradersApiException`
- Startup services in API host:
  - `AgentBootstrapService`
  - `StartupSyncService`
- Persistence entities and `SpaceTradersDbContext` for cached agent/ships/contracts and stored credentials
- Razor Pages dashboard overview page showing cached agent + ships

### Docs – Added
- `docs/GLOSSARY.md`
- `CONTRIBUTING.md`
- `CHANGELOG.md`

### Docs – Changed
- `README.md` aligned with currently implemented foundation scope
- `CONTRIBUTING.md` aligned with current repository state

---

## [0.1.0] – 2025-01-01 – Infrastructure foundation

### Added
- Solution with six projects: Domain, Application, Infrastructure.SpaceTradersAPI, Infrastructure.Persistence, API, App
- Initial project structure

---

[Unreleased]: https://github.com/Gemberkoekje/projects/compare/v0.1.0...HEAD
[0.2.0]: https://github.com/Gemberkoekje/projects/releases/tag/v0.2.0
[0.1.0]: https://github.com/Gemberkoekje/projects/releases/tag/v0.1.0
