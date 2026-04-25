# CONTRIBUTING.md

## Welcome

Contributions are welcome. This repository is currently in a **foundation phase**.
Please keep changes small, focused, and aligned with the current implementation.

---

## Current Scope

Implemented today:
- SpaceTraders typed HTTP client
- PostgreSQL persistence with EF Core
- API host with bootstrap + startup sync hosted services
- Razor Pages dashboard (overview with cached agent + ships)

Planned architecture and future phases are documented in `docs/plan/`.

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
