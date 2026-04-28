# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

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
