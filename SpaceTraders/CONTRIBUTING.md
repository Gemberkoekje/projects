# CONTRIBUTING.md

## Welcome

Contributions are welcome. Please keep changes small, focused, and aligned with the current plan.

---

## Where things stand

- `docs/HOW_IT_WORKS.md` describes what the code does today.
- `PLAN.md` holds the plan, the known issues (B-numbers) and the decisions already taken
  (D-numbers). Work happens in its slices, one PR per slice.
- `CLAUDE.md` defines what Claude works on in this project: things that don't work the way they
  are intended to, not strategy or settings.
- `docs/archive/` holds earlier plans. They don't describe the current code.

---

## Project Conventions

### No Secrets in Code
Never commit tokens, passwords, connection strings, or API keys.
Use `dotnet user-secrets` for local development.

### Keep Changes Minimal
Prefer incremental PRs over large refactors.
Do not introduce major architectural dependencies unless required.

### Fixes Start With a Test
A bug fix starts with a test that reproduces the misbehaviour, then makes it pass.

### PostgreSQL as Local Store
Use PostgreSQL in development and production paths.

### One Instance per Account
The SpaceTraders rate limit is per IP address and per account. Don't run a local instance against
the same account while the cluster instance is running.

---

## Coding Style

- Follow existing C# conventions in each project.
- Keep nullable reference types enabled.
- Use `CancellationToken` on async APIs.
- Prefer small, testable units where possible.
- Do not log full SpaceTraders account or agent tokens. Mask tokens in diagnostics.

---

## Pull Request Checklist

- [ ] `dotnet build SpaceTraders.slnx` succeeds without new warnings.
- [ ] `dotnet test SpaceTraders.slnx --filter "Category!=Integration"` passes.
- [ ] No secrets added to source control.
- [ ] `docs/HOW_IT_WORKS.md` updated when behaviour changes.
- [ ] `PLAN.md` updated: slice marked done, known issues and decisions current.
- [ ] `CHANGELOG.md` updated under `## [Unreleased]` for notable changes.

---

## Commit Message Format

```text
<type>: <short summary>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`.
