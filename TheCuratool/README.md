# The Curatool

A storyteller tool for **Blood on the Clocktower** — "The Curator" loric implementation. A draft-style game-mode helper where players are offered up to 3 candidate characters in a secret order, with full setup rule automation.

## Quick Start

### Prerequisites
- .NET 9 SDK
- PostgreSQL (for full app; tests can use in-memory for unit tests)
- Visual Studio 2026 (or Code/Rider)

### Local Development

```bash
# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Run the web app
cd src/TheCuratool.Web
dotnet run
# Visit http://localhost:5000
```

### Docker (Phase 9)

```bash
docker compose up --build
# Web: http://localhost:8080
# PostgreSQL: localhost:5432
```

The compose stack builds from `src/TheCuratool.Web/Dockerfile` and starts:
- `db` (`postgres:16`) with persistent volume `curatool-postgres`
- `web` (TheCuratool.Web) with `ConnectionStrings__Postgres` wired to the `db` service

### Kubernetes (Phase 10)

Create a real secret value file (or patch `deploy/k8s/postgres-secret.yaml`) before applying.

```bash
# Apply all manifests via kustomize
kubectl apply -k deploy/k8s/

# Verify
kubectl get pods -n curatool
kubectl get svc -n curatool

# Access web service
kubectl port-forward -n curatool svc/curatool 8080:80
# Visit http://localhost:8080
```

The k8s deployment includes:
- PostgreSQL deployment + ClusterIP service + PVC (`5Gi` request)
- Web deployment + ClusterIP service
- `/healthz` readiness/liveness probes
- `ConfigMap` (`Database__AutoMigrate`) and `Secret` (including connection string)
- Optional ingress manifest with host placeholder `curatool.local`


## Architecture

### Project Structure

```
TheCuratool/
├── src/
│   ├── TheCuratool.Domain/          # Core models, enums, domain logic
│   ├── TheCuratool.Application/     # Services, business logic
│   ├── TheCuratool.Infrastructure/  # EF Core, repositories, persistence
│   └── TheCuratool.Web/             # Blazor Server UI + minimal API endpoints
├── tests/
│   └── TheCuratool.UnitTests/       # xUnit tests
├── deploy/
│   ├── docker-compose.yml
│   └── k8s/
├── data/
│   ├── characters.json              # Character definitions & setup rules
│   └── lorics.json                  # Loric definitions
├── Directory.Build.props
├── .editorconfig
├── .gitignore
├── README.md
└── TheCuratool.slnx
```

### Key Concepts

- **Character Types:** Townsfolk, Outsider, Minion, Demon, Traveller, Fabled
- **Setup Rules:** Automated distribution adjustments (Baron, Godfather, Drunk, etc.)
- **Availability Constraints:** Dynamic filtering (Kazali, Summoner, Atheist special cases)
- **Loric ("The Curator"):** Core feature — order players, present up to 3 valid choices, save chosen role
- **Hidden Flags:** Storyteller-only state (Drunk, Lunatic) that affects setup math without revealing intent

## Implementation Phases

1. ✅ **Phase 1** — Project Scaffolding & Solution Structure
2. ✅ **Phase 2** — Character & Script Domain Model
3. ✅ **Phase 3** — Setup Rule Engine
4. ✅ **Phase 4** — Script JSON Import
5. ✅ **Phase 5** — Draft Engine
6. ✅ **Phase 6** — Persistence (PostgreSQL + EF Core)
7. ✅ **Phase 7** — Web UI (Blazor Server)
8. ✅ **Phase 8** — Minimal REST API
9. ✅ **Phase 9** — Docker
10. ✅ **Phase 10** — Kubernetes
11. ✅ **Phase 11** — Polish & Documentation

## Special Cases

Beyond the base phases, The Curatool implements a full set of Blood on the Clocktower
special-case setup behaviors. The complete design and acceptance criteria live in
[plan-special-cases.md](plan-special-cases.md). Summary of implemented S-phases:

| Phase | Area | Status |
|-------|------|--------|
| ✅ S1 | Setup rule coverage — Fang Gu (+1 Outsider), Vigormortis (−1 Outsider), Summoner (ReplaceDemon), Godfather (true ±1), Hermit, Lord of Typhon (unconstrained Outsider + Minion delta), Marionette draft exclusion | Done |
| ✅ S2 | Required pair — Choirboy → King with out-of-script auto-add | Done |
| ✅ S3 | Hidden-flag setup suppression — Drunk/Lunatic tokens ignore their own setup rules | Done |
| ✅ S4 | Legion game mode — derived Legion count, "Evil" sentinel offers, `ResolveEvilSlot` | Done |
| ✅ S5 | Non-Legion "Evil" offer — opt-in evil sentinel on any curated offer | Done |
| ✅ S6 | Unresolved Minion slots — Kazali / Lord of Typhon ST-assigned slots, `ResolveMinionSlot` | Done |
| ✅ S7 | Dynamic-setup — Alchemist / Boffin borrowed-ability assignment with feasibility greying-out | Done |
| ✅ S8 | Script validation — Choirboy-without-King and Legion-on-script informational diagnostics | Done |
| ✅ S9 | Web UI surfacing — Legion controls, evil-option toggle, ST-assigned/dynamic resolution UI | Done |
| ✅ S10 | Persistence round-trip — Legion settings, evil sentinels, ST-assigned/borrowed-ability fields | Done |
| ✅ S11 | Documentation — this section + plan status tracking | Done |

## Code Style

- **Nullability:** `Nullable=enable`; avoid nullable annotations (`?`) unless essential. Use explicit types (Result<T>, Option<T>, etc.) for optional outcomes.
- **Enums:** All enums include an explicit `Unknown = 0` value.
- **Usings:** `ImplicitUsings=disable`. Maintain explicit `GlobalUsings.cs` per project.
- **Warnings:** `TreatWarningsAsErrors=true` — all warnings become errors.
- **Formatting:** Follow `.editorconfig` (4-space indent, file-scoped namespaces, etc.).

## REST API Reference

The Swagger UI is available at `/swagger` when running in Development mode.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/scripts` | Upload and parse a script JSON |
| `POST` | `/api/sessions` | Create a new draft session |
| `GET` | `/api/sessions/{id}` | Get full session state |
| `GET` | `/api/sessions/{id}/suggestions` | Get up to 3 character suggestions for the next player |
| `GET` | `/api/sessions/{id}/remaining` | Get all remaining valid characters |
| `POST` | `/api/sessions/{id}/curated-offer` | Lock a curated offer for a player slot |
| `POST` | `/api/sessions/{id}/atheist-commitment` | Confirm Atheist commitment for a player slot |
| `POST` | `/api/sessions/{id}/choices` | Record a player's confirmed character choice |

## Contributing

### Adding a New Character

Edit `data/characters.json` with a new entry:

```json
{
  "id": "mynewchar",
  "displayName": "My New Char",
  "type": "townsfolk",
  "setupRules": [
    { "kind": "SomeRule", "param": "value" }
  ],
  "availabilityConstraints": []
}
```

No recompilation needed; restart the app.

### Adding a New Setup Rule Type

1. Implement `ISetupRule` in `TheCuratool.Domain`.
2. Register deserialization in `ScriptParser` (Phase 4).
3. Add unit tests in `TheCuratool.UnitTests`.

## License

(Specify as appropriate for your project.)

## References

- [Blood on the Clocktower](https://bloodontheclocktower.com/)
- Setup Rules & Community Scripts: [Script Database](https://clocktower.online/scripts/)
