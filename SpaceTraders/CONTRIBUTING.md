# CONTRIBUTING.md

## Welcome

Contributions are welcome. This repository is currently in a **foundation phase**.
Please keep changes small, focused, and aligned with the current implementation.

---

## Current Scope

Implemented today:
- SpaceTraders typed HTTP client
- PostgreSQL persistence with EF Core
- API host with bootstrap, startup sync, startup recovery, game loop, contract watch, activity pruning, ship refresh, metrics, and leader election hosted services
- Internal Minimal API endpoints for status, settings, control, metrics, and health
- Razor Pages dashboard for agent, fleet, contracts, market, API usage, activity log, and settings views
- Kubernetes manifests and root-level Dockerfiles for API and app deployment

Current architecture and operational behavior are documented in `docs/implementation/` and `docs/operations/`.

---

## Project Conventions

### No Secrets in Code
Never commit tokens, passwords, connection strings, or API keys.
Use `dotnet user-secrets` for local development.

### Keep Changes Minimal
Prefer incremental PRs over large refactors.
Do not introduce major architectural dependencies unless required.

### PostgreSQL as Local Store
Use PostgreSQL in development and production paths.

---

## Coding Style

- Follow existing C# conventions in each project.
- Keep nullable reference types enabled.
- Use `CancellationToken` on async APIs.
- Prefer small, testable units where possible.
- Do not log full SpaceTraders account or agent tokens. Mask tokens in diagnostics.

---

## Pull Request Checklist

- [ ] `dotnet build` succeeds.
- [ ] No secrets added to source control.
- [ ] Docs updated when behavior/configuration changes.
- [ ] `CHANGELOG.md` updated under `## [Unreleased]` for notable changes.

---

## Commit Message Format

```text
<type>: <short summary>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`.
