# The Curatool — Implementation Plan

A storyteller tool for the Blood on the Clocktower **"The Curator"** loric: a draft-style game-mode helper where players are offered up to 3 candidate characters in a secret order.

This plan is divided into **self-contained phases**. Each phase has goals, deliverables, acceptance criteria, and references to relevant files / concepts so that an LLM (or developer) can pick up any single phase and execute it without needing the full conversation context.

---

## High-Level Architecture

- **Backend:** ASP.NET Core (.NET 9) Web API. The solution already contains `TheCuratool.slnx`.
- **Frontend:** Blazor Server (single-app, simple) OR a static SPA served by the API. Default choice: **Blazor Server** to minimize moving parts.
- **Database:** PostgreSQL (via Entity Framework Core with `Npgsql.EntityFrameworkCore.PostgreSQL`).
- **Containerization:** Multi-stage `Dockerfile` for the API + Blazor app. PostgreSQL runs as a separate container/pod.
- **Deployment:** Kubernetes manifests (Deployment, Service, ConfigMap, Secret, optional Ingress, PVC for Postgres) plus a `docker-compose.yml` for local dev.
- **Script source:** The user's script JSONs (e.g. [NoVortox.json](NoVortox.json)) — an array whose first element is a `_meta` object and remaining elements are character ids (strings) or full objects.

### Copilot Instruction Alignment (Mandatory)

- **Nullable usage:** Do not use nullable annotations (`?`) by default. Model optional outcomes with explicit types (e.g. `Result<T>`, `Option<T>`, dedicated state records) and only allow nullable timestamps where idiomatic and explicitly approved.
- **Enums:** Every enum must include an explicit empty value at `0` (e.g. `Unknown = 0`).
- **Usings:** Implicit usings are disabled. Add explicit SDK/System usings in `GlobalUsings.cs` or local files.
- **Architecture checkpoints required in the plan and implementation:**
  - Response deduplication behavior.
  - Blazor Server BFF vs JWT decision.
  - Handling of authenticated storyteller/respondent display names.
  - Deliberate v1 omissions and edge-case controls (closing sessions, rate limiting, Redis purpose, pagination).

### Key Domain Concepts

- **Character types:** `Townsfolk`, `Outsider`, `Minion`, `Demon`, `Traveller`, `Fabled`.
- **Player count → base distribution** (official BotC table for 5–15 players).
- **Setup rules:** Some characters / lorics modify the distribution (e.g. Baron `+2 Outsiders / -2 Townsfolk`, Godfather `±1 Outsider`, Sentinel loric, Drunk replaces a Townsfolk, Huntsman requires Damsel, Lil' Monsta replaces Demon slot with extra Minion, etc.).
- **Pre-draft marionette toggle:** Before drafting starts, storyteller can explicitly enable "Use Marionette"; this applies a setup adjustment of `+1 Townsfolk / -1 Minion` for this session.
- **Availability constraints:** Some characters become unavailable based on already chosen roles (e.g. Kazali unavailable once any Minion is chosen, Summoner unavailable once any Demon is chosen, Atheist restricted to explicit first-pick no-evil setup commitment unless selected as Drunk).
- **Hidden storyteller flags:** A selected role can be flagged as Drunk or Lunatic in storyteller-only state, which changes effective setup counting without revealing to the player.
- **Loric ("The Curator"):** Order players, present up to 3 valid choices, save chosen role, advance.

---

## Phase 1 — Project Scaffolding & Solution Structure

**Goal:** Set up the .NET solution with the projects needed and CI-friendly layout.

**Deliverables:**
- Update `TheCuratool.slnx` to include:
  - `src/TheCuratool.Domain` (class library) — models, enums, setup rules.
  - `src/TheCuratool.Application` (class library) — services (draft engine, script parser, distribution calculator).
  - `src/TheCuratool.Infrastructure` (class library) — EF Core DbContext, repositories, migrations.
  - `src/TheCuratool.Web` (Blazor Server app + minimal API endpoints).
  - `tests/TheCuratool.UnitTests` (xUnit).
- Root files: `.editorconfig`, `.gitignore` (VisualStudio template), `Directory.Build.props` (target `net9.0`, `Nullable=enable`, `ImplicitUsings=disable`, `TreatWarningsAsErrors=true`).
- `README.md` with quick-start.

**Acceptance:** `dotnet build` succeeds; `dotnet test` runs with zero tests passing.

---

## Phase 2 — Character & Script Domain Model

**Goal:** Define the data model for characters, scripts, and game state — independent of UI or DB.

**Deliverables (in `TheCuratool.Domain`):**
- `enum CharacterType { Unknown = 0, Townsfolk = 1, Outsider = 2, Minion = 3, Demon = 4, Traveller = 5, Fabled = 6 }`
- `record CharacterDefinition(string Id, string DisplayName, CharacterType Type, IReadOnlyList<ISetupRule> SetupRules, IReadOnlyList<IAvailabilityConstraint> AvailabilityConstraints, bool IsUnknown)`
- `record Script(string Name, string Author, IReadOnlyList<CharacterDefinition> Characters)`
- `record PlayerSlot(int DraftOrder, Guid PlayerId, PlayerChoice Choice)` where `PlayerChoice` is a non-null state model (`Unchosen` or `Chosen(characterId, offeredIds, hiddenFlags)`) and `hiddenFlags` is storyteller-only metadata (e.g. `IsDrunk`, `IsLunatic`).
- `record GameSession(Guid Id, Script Script, int PlayerCount, IReadOnlyList<PlayerSlot> Players, GameStatus Status)`
- A **character database** seed (JSON file `data/characters.json` in `TheCuratool.Domain`) that maps known character ids → `{ displayName, type, setupRules, availabilityConstraints }`. Cover at minimum the Trouble Brewing, Bad Moon Rising, Sects & Violets, and common experimental characters that have setup or availability behavior.
- Encode explicit availability constraints: `kazali` (blocked if any Minion already chosen), `summoner` (blocked if any Demon already chosen), `atheist` (special first-pick no-evil commitment unless selected as Drunk).
- Ensure **Alsaahir** has no setup rule entries in `characters.json`.
- Fallback rule: characters NOT in the database default to `Townsfolk` with no setup rules and a `DisplayName` derived by `TitleCase(id)`. Mark them as `IsUnknown = true` so the UI can flag for storyteller review.
- Metadata optionality should be handled without nullable annotations (e.g. empty string for missing author, or `Result<T>` validation model).

**Acceptance:** Unit tests load `characters.json`, assert known ids resolve correctly, and unknown ids title-case nicely (e.g. `nodashii` → `No Dashii` is **NOT** required — simple capitalization `Nodashii` is acceptable per spec).

---

## Phase 3 — Setup Rule Engine

**Goal:** Codify setup-modifying behavior in a small, expandable rule system, with a separate availability-constraint layer. **Only setup-related rules** are modeled — no night-order, ability, or reminder logic.

**Design:**

```csharp
public interface ISetupRule
{
	// Applied to a working SetupCounts when this character (or loric) is in play.
	// Returns possible resulting counts (most rules return exactly one; ST-choice rules can return multiple).
	IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext ctx);
}

public record SetupCounts(int Townsfolk, int Outsiders, int Minions, int Demons);

public record SetupContext(int PlayerCount, IReadOnlyList<string> InPlayCharacterIds, IReadOnlyList<string> ActiveLoricIds);

public interface IAvailabilityConstraint
{
	bool IsAvailable(AvailabilityContext context);
}

public record AvailabilityContext(
	IReadOnlyList<string> ChosenCharacterIds,
	bool HasAnyMinion,
	bool HasAnyDemon,
	int PicksMade,
	bool IsDrunkFlagApplied,
	bool IsLunaticFlagApplied);
```

**Built-in rule types:**
- `OutsiderDeltaRule(int delta)` — Baron (+2), Godfather (±1, ST choice), Balloonist (+0/+1), Huntsman (+1 if adds Damsel, ST choice), etc.
- `ReplaceTownsfolkRule` — Drunk pattern only.
- `MarionetteSessionAdjustmentRule` — applied only when the storyteller enables pre-draft "Use Marionette" (`+1 Townsfolk / -1 Minion`).
- `RequiresCharacterRule(string requiredId)` — Huntsman ↔ Damsel pairing.
- `MinionSwapRule` — Lil' Monsta (extra Minion, no Demon token to a player).
- `StoryTellerChoiceRule(IEnumerable<ISetupRule> options)` — wraps multi-outcome rules.
- `LoricSetupRule` — e.g. **Sentinel** loric (±1 Outsider). Loric rules apply independently of character pool.

**Built-in availability constraints:**
- `BlockedIfAnyChosenOfTypeConstraint(CharacterType type)` — used by Kazali (Minion) and Summoner (Demon).
- `AtheistFirstPickConstraint` — only available as first committed pick when no Minion/Demon has been chosen, unless the storyteller marks the pick as Drunk.

**Deliverables:**
- `ISetupRule` + concrete implementations.
- `IAvailabilityConstraint` + concrete implementations.
- `SetupCalculator` service: given `Script`, `PlayerCount`, `ChosenCharacterIds`, `ActiveLoricIds`, storyteller hidden flags, and session setup options (including `UseMarionette`) → returns the **set of currently-valid target counts** plus the **base distribution** for the player count.
- JSON-driven rule registration: `characters.json` entries can declare rules like `{ "id": "baron", "type": "Minion", "setup": [{ "kind": "OutsiderDelta", "delta": 2 }] }` and availability constraints like `{ "kind": "BlockedIfAnyChosenOfType", "type": "Minion" }` so new characters/scripts can be added without recompiling.

**Acceptance:** Unit tests for Baron, Godfather (both choices), Drunk, Huntsman+Damsel, Sentinel loric, Kazali blocked after Minion pick, Summoner blocked after Demon pick, Atheist special-case gating, and a no-op character.

---

## Phase 4 — Script JSON Import

**Goal:** Parse the BotC script JSON format used by the community (e.g. [NoVortox.json](NoVortox.json)).

**Deliverables:**
- `ScriptParser` service in `TheCuratool.Application` that accepts:
  - The raw JSON text.
  - Or a stream from upload.
- Handles both forms of entries:
  - String shorthand: `"baron"`.
  - Object form: `{ "id": "baron", "name": "Baron", "team": "minion", ... }` (homebrew scripts).
- Extracts `_meta` for script name/author.
- Maps each character id through the character database (Phase 2). Unknown ids surface as `IsUnknown = true` with a generated display name.
- Validates: script must contain at least 1 Townsfolk. Missing Demons should be a warning (not an error) to support Summoner-style scripts; warn if no Outsiders / Minions are listed (still allowed).

**Acceptance:** Unit test loads `NoVortox.json` and asserts:
- `Name == "No Vortox"`, `Author == "Gemberkoekje"`.
- 27 characters parsed.
- `godfather` resolves to type `Minion` with an `OutsiderDelta(±1)` setup rule.
- `drunk` resolves to type `Outsider`.
- `nodashii` and `vortox` resolve to type `Demon`.
- parser emits warning (not error) when a script has no Demon tokens.

---

## Phase 5 — Draft Engine

**Goal:** Implement the core "Curator" draft loop.

**Deliverables (in `TheCuratool.Application`):**
- `DraftEngine` service:
  - `StartSession(Script, int playerCount, IReadOnlyList<string> activeLoricIds) → GameSession` — assigns a random draft order to N anonymous players.
  - `GetRemainingValidCharacters(GameSession) → IReadOnlyList<CharacterDefinition>` — filters the full script down to characters that can still legally be picked given:
	- Characters already chosen.
	- Current type counts vs. target counts (taking setup rules and storyteller hidden flags into account).
	- Required-pair rules (Huntsman without Damsel ⇒ Damsel becomes mandatory for some remaining seat).
	- Availability constraints (Kazali/Summoner/Atheist special handling).
	- Constraint resolution priority is explicit: **hard feasibility first**, then pair requirements, then soft type-balance preference. If a required-pair rule conflicts with a type ceiling, required-pair wins (e.g. Damsel can exceed outsider ceiling and storyteller resolves final discrepancy).
	- Huntsman feasibility gate: Huntsman is not valid if there is no remaining feasible path to include Damsel (for example, all outsider slots are already occupied by non-Damsel picks and no override path exists).
  - `ConfirmAtheistCommitment(sessionId, playerSlotIndex) → GameSession` — explicit storyteller confirmation required before Atheist can be selected as a normal (non-Drunk) pick.
  - `SuggestThree(GameSession) → IReadOnlyList<CharacterDefinition>` — picks up to 3:
	- Use variety as a **default preference**, not a hard rule.
	- Prefer one from each distinct `CharacterType` when practical.
	- Degrade gracefully near endgame or constrained states (forced picks / narrow legal pools) by returning the most sensible legal set.
	- Returns 1, 2, or 3 entries depending on what's actually valid.
  - `CreateCuratedOffer(sessionId, playerSlotIndex, offeredIds) → GameSession` — validates that offered ids are 1..3 unique roles from `GetRemainingValidCharacters` and stores them as the official curated offer for that player.
  - `RecordChoice(sessionId, playerSlotIndex, chosenCharacterId, offeredIds, hiddenFlags) → GameSession`.
  - Curated-offer consistency rule: once a curated offer exists for a slot, `RecordChoice` must use the exact same `offeredIds`; mismatches are rejected as conflict to preserve idempotency and prevent back-button drift.
  - `GetMakeupSummary(GameSession) → MakeupSummary` — current vs. target counts per type and list of chosen characters per type.

**Acceptance:** Unit tests cover:
- 7-player NoVortox draft completes without contradiction.
- Forced-pick scenario (Huntsman chosen early, last seat must be Damsel) returns exactly that character.
- Kazali and Summoner become unavailable at the correct time.
- Atheist requires explicit commitment unless chosen as Drunk.
- Suggestion variety preference degrades gracefully under constrained pools.
- Hidden Lunatic flag on a Demon and hidden Drunk flag on a Townsfolk both count as Outsider for setup math.
- A Demon flagged as Lunatic keeps one real Demon slot mandatory; a draft cannot complete without at least one non-Lunatic Demon assignment.

---

## Phase 6 — Persistence (PostgreSQL + EF Core)

**Goal:** Persist sessions so a storyteller can refresh/resume.

**Deliverables (in `TheCuratool.Infrastructure`):**
- `CuratoolDbContext` with tables:
  - `Scripts` (id, name, author, raw_json, created_at).
  - `GameSessions` (id, script_id, player_count, active_lorics jsonb, status, created_at).
  - `PlayerSlots` (id, session_id, draft_order, chosen_character_id, offered_character_ids jsonb, hidden_flags jsonb).
- Connection string from `ConnectionStrings:Postgres` configuration; defaults wired through `appsettings.json` and overridable via env var `ConnectionStrings__Postgres`.
- Initial EF Core migration `InitialCreate`.
- Repository interfaces in Application, implementations in Infrastructure. DI registration via `AddCuratoolInfrastructure(IServiceCollection, IConfiguration)`.
- Migration runs automatically on app startup behind a feature flag (`Database:AutoMigrate=true`).

**Acceptance:** `dotnet ef migrations add InitialCreate` succeeds; integration test (Testcontainers for Postgres, optional) round-trips a session.

---

## Phase 7 — Web UI (Blazor Server)

**Goal:** Storyteller-facing UI for the full workflow.

**Pages / components:**
1. **Home / Script Load** — upload `.json` file OR paste JSON into a `textarea`. Shows parsed script summary and list of characters (highlight unknowns).
2. **Setup** — input player count (5–15+), select active lorics (checkbox list including **The Curator** itself — required — and others such as **Sentinel**), and set pre-draft options including **Use Marionette**. Display base distribution and any setup-derived target ranges.
3. **Draft** — main screen:
   - Left: ordered list of player slots with status (pending / current / chosen — character name hidden behind a "Reveal" toggle for ST).
   - Center: current player card showing draft number. Two buttons: **Random 3** and **Curate** (storyteller picks 3 from the valid remaining list).
   - Right: live makeup summary (`Townsfolk: 2/7 — Gossip, Dreamer · Outsiders: 1/2 — Goon · Minions: 0/1 · Demons: 0/1`).
   - After 3 are presented, ST clicks which one the player chose; engine records and advances.
4. **Summary** — final grimoire-style listing of all assignments, exportable as JSON.

**Tech notes:**
- State held in a scoped Blazor service backed by the DB.
- Validation messages when no 3 distinct picks are possible.
- Keyboard shortcuts (`R` = random, `1/2/3` = pick offered character) — nice-to-have.

**Acceptance:** Full end-to-end manual run with `NoVortox.json` at 7 players completes; refreshing browser resumes the session.

---

## Phase 8 — Minimal REST API (Optional but Recommended)

**Goal:** Expose draft engine via JSON endpoints so external clients / tests can use it.

**Endpoints:**
- `POST /api/scripts` (upload JSON, returns id).
- `POST /api/sessions` `{ scriptId, playerCount, activeLorics }`.
- `GET  /api/sessions/{id}`.
- `GET  /api/sessions/{id}/suggestions` (random 3) and `/remaining` (full valid list).
- `POST /api/sessions/{id}/curated-offer` `{ playerSlot, offeredIds }`.
- `POST /api/sessions/{id}/atheist-commitment` `{ playerSlot }`.
- `POST /api/sessions/{id}/choices` `{ playerSlot, chosenCharacterId, offeredIds, hiddenFlags }`.

**Acceptance:** Swagger / OpenAPI document published at `/swagger`.

---

## Phase 9 — Docker

**Goal:** Containerize the app.

**Deliverables:**
- `src/TheCuratool.Web/Dockerfile` — multi-stage:
  - Stage 1: `mcr.microsoft.com/dotnet/sdk:9.0` — restore, publish.
  - Stage 2: `mcr.microsoft.com/dotnet/aspnet:9.0` — copy publish output, expose port 8080, `ENTRYPOINT ["dotnet","TheCuratool.Web.dll"]`.
- `.dockerignore` (exclude `bin`, `obj`, `.vs`, `*.user`, tests).
- `docker-compose.yml` at repo root:
  - `db`: `postgres:16` with volume + healthcheck + env (`POSTGRES_USER/PASSWORD/DB`).
  - `web`: built from Dockerfile, depends_on db, env `ConnectionStrings__Postgres`.
- Document `docker compose up` in README.

**Acceptance:** `docker compose up --build` brings up the app on `http://localhost:8080` connected to Postgres.

---

## Phase 10 — Kubernetes Manifests

**Goal:** Ready-to-apply k8s manifests under `deploy/k8s/`.

**Deliverables:**
- `namespace.yaml` — `curatool` namespace.
- `postgres-pvc.yaml` — PersistentVolumeClaim (e.g. 5Gi).
- `postgres-deployment.yaml` + `postgres-service.yaml` (ClusterIP) — using `postgres:16` image.
- `postgres-secret.yaml` — `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` (template with placeholder; document `kubectl create secret` alternative).
- `web-configmap.yaml` — non-secret config (`Database__AutoMigrate=true`, etc.).
- `web-deployment.yaml` — `replicas: 1`, readiness/liveness probes hitting `/healthz`, env from secret + configmap, image placeholder `ghcr.io/<owner>/thecuratool:latest`.
- `web-service.yaml` — ClusterIP on port 80 → 8080.
- `ingress.yaml` — optional, with host placeholder.
- Add ASP.NET Core health checks (`AddHealthChecks().AddNpgSql(...)`) and map `/healthz`.
- README section: build & push image, `kubectl apply -k deploy/k8s/`.
- Add a `kustomization.yaml` to wire it all together.

**Acceptance:** `kubectl apply -k deploy/k8s/` against a kind/minikube cluster brings up both pods and the app is reachable via port-forward.

---

## Phase 11 — Polish & Documentation

**Goal:** Make it pleasant to use and extend.

**Deliverables:**
- README sections: overview, screenshots, local dev, docker, k8s, how to add new characters / setup rules (point at `characters.json` schema).
- A `CONTRIBUTING-CHARACTERS.md` explaining the JSON shape for adding new characters/rules.
- Logging via `ILogger` (structured), basic error pages.
- Light theme + dark theme toggle (nice-to-have).
- GitHub Actions workflow `.github/workflows/ci.yml`: build, test, build Docker image, push on tag (optional).

**Acceptance:** A new contributor can add a homebrew character (with a setup rule) by editing only `characters.json`, restart, and see it work.

---

## Cross-Cutting Architecture Decisions (Required by Copilot Instructions)

- **Response deduplication:** `RecordChoice` and related APIs must be idempotent by `(SessionId, DraftOrder)` and reject duplicate conflicting submissions while allowing safe retries. When curated offers are used, idempotency is evaluated against `(SessionId, DraftOrder, OfferedSetHash)` semantics: same set is retry-safe, different set is conflict.
- **Auth model decision:** Use **Blazor Server BFF-style session/cookie auth** when auth is enabled; avoid JWT complexity in v1 unless external API clients require it.
- **Authenticated names handling:** Store both technical identity (`UserId`) and display name (`DisplayName`) for storyteller actions; keep display name mutable without breaking audit trails.
- **Deliberate v1 omissions / controls:**
  - Session closing flow is minimal (manual finalization only).
  - Basic rate limiting only on mutation endpoints.
  - Redis is optional and only introduced for distributed cache/session coordination at scale.
  - Pagination is intentionally omitted for v1 list endpoints with small result sets.

## Cross-Cutting Notes for Implementers

- **Display name fallback:** If a character is not in the database, use `char.ToUpper(id[0]) + id[1..]` (simple capitalize-first-letter) per spec.
- **Nullability convention:** Do not introduce nullable reference annotations in domain/application models unless explicitly unavoidable and documented.
- **Usings convention:** Keep implicit usings disabled and maintain explicit `GlobalUsings.cs` files per project.
- **Loric handling:** Lorics are NOT in the script JSON. They are selected separately in the Setup page. **The Curator** loric is always implicitly active in this tool. Other lorics with setup effects (Sentinel, etc.) are listed in `data/lorics.json` with `ISetupRule` entries.
- **Drunk/Lunatic hidden flags:**
  - A Townsfolk can be flagged as **Drunk** by the storyteller; this is storyteller-only, counts as an Outsider slot, and that Townsfolk token is not shown to players.
  - A Demon can be flagged as **Lunatic** by the storyteller; this is storyteller-only and counts as an Outsider slot rather than a Demon slot.
  - Setup abilities tied to the underlying token are ignored when flagged as Drunk/Lunatic for draft setup purposes (e.g. a Drunk Atheist is allowed even after Demon/Minion picks).
  - If a Demon token is flagged as Lunatic, one real Demon slot remains open and must be satisfied by a non-Lunatic Demon before draft completion.
- **No night order, no ability resolution** — the tool is strictly setup + draft.

---

## Suggested Order for an LLM Picking This Up

Run phases in order. Phases 1–5 produce a working CLI-testable core. Phase 6 adds persistence. Phase 7 produces a usable UI. Phases 8–10 are deployment. Phase 11 is polish.

---

## Implementation Status

### ✅ Completed

**Phase 1 — Project Scaffolding & Solution Structure**
- Solution file (`TheCuratool.slnx`) updated with all 5 project references
- Projects created: Domain, Application, Infrastructure, Web, UnitTests
- Configuration files: `Directory.Build.props`, `.editorconfig`, `.gitignore`
- `GlobalUsings.cs` files in each project with explicit SDK namespaces
- `Program.cs` entry point for Web project (minimal API baseline)
- `README.md` with quick-start, architecture overview, and phase tracking
- ✅ Revalidated in current workspace run:
  - `dotnet build` succeeds (0 warnings, 0 errors)
  - `dotnet test` runs successfully (0 tests discovered, 0 failed)

### ✅ Completed

**Phase 2 — Character & Script Domain Model**
- Added domain enum and records: `CharacterType`, `CharacterDefinition`, `Script`, `PlayerSlot`, `PlayerChoice`, `HiddenFlags`, `GameSession`, and `GameStatus`
- Added setup/availability abstractions and seed rule/constraint models for Phase 2 parsing (`ISetupRule`, `IAvailabilityConstraint`, `OutsiderDeltaSetupRule`, `BlockedIfAnyChosenOfTypeConstraint`, `AtheistFirstPickConstraint`)
- Added `src/TheCuratool.Domain/data/characters.json` character database covering Trouble Brewing, Bad Moon Rising, Sects & Violets, and key experimental characters
- Encoded required availability constraints for `kazali`, `summoner`, and `atheist`
- Ensured `alsaahir` has no setup rule entries
- Implemented `CharacterDatabase` loader with unknown fallback behavior (`Townsfolk`, no rules/constraints, `TitleCase` display name, `IsUnknown = true`)
- Added unit tests for known-id resolution and unknown-id fallback capitalization

### ✅ Completed

**Phase 3 — Setup Rule Engine**
- Implemented executable setup rule contracts (`ISetupRule.Apply`) and availability evaluation (`IAvailabilityConstraint.IsAvailable`)
- Added core setup domain primitives: `SetupCounts`, `SetupContext`, and `AvailabilityContext`
- Added concrete setup rules: `OutsiderDeltaSetupRule`, `ReplaceTownsfolkSetupRule`, `MarionetteSessionAdjustmentRule`, `RequiresCharacterSetupRule`, `MinionSwapSetupRule`, `StoryTellerChoiceSetupRule`, and `LoricSetupRule`
- Extended JSON-driven rule registration in `CharacterDatabase` to support additional setup kinds and storyteller-choice rule compositions
- Added loric modeling and JSON data loading with `LoricDatabase` and `data/lorics.json` (including Sentinel setup effects)
- Added `SetupCalculator` in `TheCuratool.Application` with base player-count distributions (5–15) and valid-target outcome expansion from character, loric, hidden-flag, and marionette inputs
- Added/updated unit tests for Baron, Godfather choice outcomes, Drunk, Huntsman+Damsel, Sentinel loric behavior, Kazali/Summoner availability blocking, Atheist gating with Drunk override, and no-op characters

### ✅ Completed

**Phase 4 — Script JSON Import**
- Added `ScriptParser` service in `TheCuratool.Application` with overloads for raw JSON text and upload streams.
- Implemented support for both script entry forms:
  - String shorthand entries (for example `"baron"`).
  - Object entries with `id` (for example homebrew-style entries with extra fields).
- Implemented `_meta` extraction for script `name` and `author`.
- Mapped all parsed character ids through `CharacterDatabase`, preserving unknown fallback behavior (`IsUnknown = true` with generated display names).
- Added parser diagnostics via `ScriptParseResult`:
  - Validation error if no Townsfolk are present.
  - Warnings (not errors) for missing Demon tokens and for scripts with no Outsiders / no Minions.
- Added `ScriptParserTests` with NoVortox fixture coverage and parser validation tests for warning/error behavior.

### ✅ Completed

**Phase 5 — Draft Engine**
- Added `DraftEngine` service in `TheCuratool.Application` with end-to-end draft session lifecycle operations:
  - `StartSession` (randomized draft order with active lorics + marionette option tracking)
  - `GetRemainingValidCharacters` with ordered resolution: hard feasibility, pair feasibility, and availability constraints
  - `SuggestThree` with type-variety preference and graceful degradation for constrained pools
  - `CreateCuratedOffer`, `ConfirmAtheistCommitment`, `RecordChoice`, and `GetMakeupSummary`
- Implemented curated-offer consistency/idempotency conflict guard in `RecordChoice` by rejecting offered-set mismatches once a curated offer exists for the slot.
- Implemented Atheist gating behavior requiring explicit commitment unless the pick is hidden-flagged as Drunk.
- Added hidden-flag-aware setup counting for draft math and summary output:
  - hidden Drunk Townsfolk counts as Outsider
  - hidden Lunatic Demon counts as Outsider
- Enforced completion invariant that a session cannot finalize without at least one non-Lunatic Demon assignment when target setup requires a Demon slot.
- Added support models for phase behavior and summary reporting:
  - `MakeupSummary`
  - `DraftStateSnapshot`
  - `DraftMath`
  - extended `PlayerChoice.UnchosenChoice` state for curated offers + Atheist commitment tracking
  - extended `GameSession` to carry active lorics and marionette option
- Added `DraftEngineTests` covering all Phase 5 acceptance cases:
  - 7-player NoVortox completion
  - forced Damsel end-state after early Huntsman
  - Kazali/Summoner availability transitions
  - Atheist commitment and Drunk override
  - SuggestThree constrained-pool degradation
  - hidden Drunk/Lunatic setup counting behavior
  - mandatory real Demon requirement with Lunatic-flagged Demon
  - curated-offer mismatch rejection

### ✅ Completed

**Phase 6 — Persistence (PostgreSQL + EF Core)**
- Created EF Core entity models: `ScriptEntity`, `GameSessionEntity`, `PlayerSlotEntity` in `TheCuratool.Infrastructure/Entities`
- Implemented `CuratoolDbContext` in `src/TheCuratool.Infrastructure/Data` with:
  - Three `DbSet` properties (Scripts, GameSessions, PlayerSlots) configured as lazy properties using `Set<T>()`
  - OnModelCreating with proper entity mappings, relationships (1:N, cascade delete), indexes, and default values
  - JSON column handling for complex types (ActiveLorics, OfferedCharacterIds, HiddenFlags)
- Created repository interfaces in `src/TheCuratool.Application/Abstractions/Repositories`:
  - `IScriptRepository` with AddAsync, GetByIdAsync, GetAllAsync, SaveChangesAsync
  - `IGameSessionRepository` with AddAsync, GetByIdAsync, UpdateAsync, SaveChangesAsync
- Implemented repository classes in `src/TheCuratool.Infrastructure/Repositories`:
  - `ScriptRepository` with proper script parsing via `ScriptParser` integration
  - `GameSessionRepository` with entity-to-domain mapping and intelligent slot update logic (preserves existing slots, updates by DraftOrder)
- Added `ServiceCollectionExtensions` for DI registration with connection string configuration from `ConnectionStrings:Postgres`
- Created EF Core migration `InitialCreate` (schema: Scripts, GameSessions, PlayerSlots with proper constraints)
- Added `appsettings.json` and `appsettings.Development.json` with PostgreSQL connection defaults and auto-migration flag
- Integrated auto-migration into `Program.cs` with feature flag `Database:AutoMigrate`
- Added health check endpoint `/healthz` to Program.cs
- Created integration tests (`GameSessionPersistenceTests`, `ScriptPersistenceTests`) using in-memory EF Core database:
  - Tests for AddAsync, GetByIdAsync, UpdateAsync with real entity updates
  - Tests for hidden flag persistence (IsDrunk, IsLunatic)
  - Tests for active lorics and marionette option persistence
  - Round-trip persistence validation for script and session data
- Added `Microsoft.EntityFrameworkCore.InMemory` to test project dependencies
- ✅ Build succeeds with 0 warnings, 0 errors
- ✅ All 33 tests pass (including 5 new persistence tests)

### ✅ Completed

**Phase 7 — Web UI (Blazor Server)**
- Expanded Blazor storyteller workflow across `Index`, `Setup`, `Draft`, and `Summary` pages.
- Home/Script Load now shows parsed summary with unknown-character highlighting and full character list review.
- Setup page now supports:
  - player-count and marionette controls,
  - active-loric selection from `lorics.json`,
  - enforced always-active **The Curator** loric,
  - setup distribution recalculation and draft start.
- Draft page now supports:
  - player-order board with current-slot highlighting,
  - reveal toggle for chosen roles,
  - **Random 3** and **Curate** offer flow,
  - curated offer selection/confirmation,
  - Atheist commitment action,
  - hidden storyteller flags (Drunk/Lunatic) on picks,
  - live makeup summary panel.
- Summary page now includes final assignment table and JSON export payload.
- `DraftSessionState` was upgraded to orchestrate Phase 7 UI behavior and persistence-backed session lifecycle.
- Added persistence-backed session resume support in web UI flow:
  - session is persisted on draft start,
  - `Draft.razor` and `Summary.razor` support session-id routes (`/draft/{id}`, `/summary/{id}`),
  - pages can rehydrate session state from repository by session id.
- Added `DraftSessionStateTests` coverage for:
  - setup loading + Curator loric enforcement,
  - draft start/offer/choice progression,
  - summary export payload,
  - persisted session rehydration by session id.
- ✅ Revalidated in current workspace run:
  - `dotnet build` succeeds (0 warnings, 0 errors)
  - `dotnet test` succeeds with 42 passing tests

### ✅ Completed

**Phase 8 — Minimal REST API (Optional but Recommended)**
- Added a minimal REST API surface in `src/TheCuratool.Web/Api` covering:
  - `POST /api/scripts`
  - `POST /api/sessions`
  - `GET /api/sessions/{id}`
  - `GET /api/sessions/{id}/suggestions`
  - `GET /api/sessions/{id}/remaining`
  - `POST /api/sessions/{id}/curated-offer`
  - `POST /api/sessions/{id}/atheist-commitment`
  - `POST /api/sessions/{id}/choices`
- Added request/response DTOs and mapping logic for script upload, session state, character lists, player slots, and makeup summaries.
- Enabled Swagger/OpenAPI publishing in the web app and exposed the UI at `/swagger`.
- Extended the draft workflow for API usage by adding `DraftEngine.TrackSession(...)` so persisted sessions can be rehydrated across requests before read/mutation operations.
- Updated persistence contracts and repository implementations to round-trip stored script identifiers and to persist sessions against existing scripts instead of placeholder rows.
- Added API integration tests (`Phase8ApiTests`) covering:
  - script upload and metadata response
  - session creation and retrieval
  - suggestions and remaining valid character endpoints
  - curated offer and choice mutation flow
  - Swagger document availability at `/swagger/v1/swagger.json`

### ✅ Completed

**Phase 9 — Docker**
- Verified and finalized root container assets:
  - `src/TheCuratool.Web/Dockerfile` multi-stage build (`sdk:9.0` → `aspnet:9.0`) publishing `TheCuratool.Web.dll` and exposing port `8080`.
  - `.dockerignore` updated to exclude build output/artifacts and tests from docker context.
  - `docker-compose.yml` configured with:
    - `db` service (`postgres:16`) + persistent volume + healthcheck (`pg_isready`).
    - `web` service built from `src/TheCuratool.Web/Dockerfile`, depending on healthy db, with `ConnectionStrings__Postgres` and `Database__AutoMigrate` env wiring.
- README docker instructions aligned to `docker compose up --build` flow.

### ✅ Completed

**Phase 10 — Kubernetes Manifests**
- Completed `deploy/k8s/` manifest set and kustomize wiring:
  - `namespace.yaml` (`curatool` namespace).
  - `postgres-pvc.yaml` (PVC request `5Gi`).
  - `postgres-deployment.yaml` + `postgres-service.yaml` (`postgres:16`, ClusterIP, readiness/liveness probes, secret env wiring).
  - `postgres-secret.yaml` template with placeholders and `CONNECTION_STRING` key.
  - `web-configmap.yaml` for non-secret app config (`Database__AutoMigrate`).
  - `web-deployment.yaml` (replicas 1, image placeholder `ghcr.io/<owner>/thecuratool:latest`, env from secret/configmap, `/healthz` readiness+liveness probes).
  - `web-service.yaml` (ClusterIP port `80` → target `8080`).
  - `ingress.yaml` optional ingress with host placeholder.
  - `kustomization.yaml` updated to include all resources + namespace.
- Web app health checks upgraded for k8s:
  - Added `AspNetCore.HealthChecks.NpgSql` package.
  - Configured `AddHealthChecks().AddNpgSql(...)` and mapped `/healthz` via `MapHealthChecks`.
- README k8s section updated with `kubectl apply -k deploy/k8s/`, namespace-aware verification, and port-forward usage.

### ✅ Completed

**Phase S3 — Hidden Flag Setup Suppression**
- Confirmed setup-rule suppression in `SetupCalculator` for hidden-flagged picks (`IsDrunk` / `IsLunatic`) while preserving hidden-flag type reassignment behavior in draft math.
- Added regression unit tests in `SetupCalculatorTests`:
  - Drunk-flagged Baron does not apply outsider delta.
  - Lunatic-flagged Fang Gu does not apply outsider delta.
  - Drunk-flagged Godfather does not produce storyteller-choice outsider outcomes.
- Existing draft invariant remains covered: lunatic-flagged Demon picks still require a real non-lunatic Demon before completion.

### ✅ Completed

**Phase S4 — Legion Game Mode**
- Added Legion setup options in session/setup models:
  - `SessionSetupOptions.IsLegionGame`
  - `SessionSetupOptions.LegionCount`
  - `GameSession.IsLegionGame`
  - `GameSession.LegionCount`
- Implemented Legion-mode setup branch in `SetupCalculator`:
  - Legion-mode distribution replaces normal setup math.
  - Default Legion count is derived from the normal setup's good-player baseline.
  - Storyteller Legion-count override is honored.
- Added Legion draft-flow behavior in `DraftEngine`:
  - `legion` is excluded from non-Legion games even when present on script.
  - Legion evil seats return only the `"evil"` sentinel offer.
  - Choosing `"evil"` stores the sentinel with empty hidden flags.
  - Added idempotent `ResolveEvilSlot(sessionId, draftOrder, actualCharacterId, hiddenFlags)` resolution action.
  - Makeup summary marks unresolved evil sentinel picks as `Evil (ST-assigned)`.
- Added `legion` character definition to `characters.json`.
- Added Setup UI support:
  - Legion toggle gated by script content.
  - Legion count input shown when Legion mode is enabled.
- Persisted Legion session settings through API/infrastructure mappings.
- Added Legion coverage tests in `SetupCalculatorTests` and `DraftEngineTests`.

### ✅ Completed

**Phase S6 — Unresolved Minion Slots (Kazali / Lord of Typhon)**
- Added ST-assigned minion-slot state to `PlayerSlot` (`IsStAssigned`, `BorrowedAbilityCharacterId`) and integrated it into draft progression so those slots are removed from the active draft queue.
- Extended `DraftEngine.RecordChoice` to auto-mark remaining minion slots as ST-assigned when `kazali` or `lord_of_typhon` is chosen.
- Added `ResolveMinionSlot(sessionId, draftOrder, characterId)` for idempotent storyteller resolution of ST-assigned minion slots.
- Updated summary/count math so unresolved ST-assigned minion slots surface as `ST-assigned night one`, and resolved slots show the assigned minion.
- Extended `PlayerSlotEntity` and repository/data mappings to round-trip `IsStAssigned` and `BorrowedAbilityCharacterId`.
- Added `DraftEngineTests` coverage for:
  - Kazali reducing remaining minion draft slots into ST-assigned slots
  - Lord of Typhon creating the extra minion assignment slot
  - `ResolveMinionSlot` replacing unresolved summary entries with resolved minions

### ✅ Completed

**Phase S8 — Script Validation Updates**
- Extended `ScriptParseResult` with an explicit informational diagnostics channel (`Info`) in addition to warnings/errors.
- Updated `ScriptParser` validation to emit informational diagnostics when:
  - `choirboy` is present without `king` (King auto-add reminder).
  - `legion` is present (Legion setup toggle reminder).
- Preserved existing validation severity semantics:
  - no new errors were introduced beyond the existing no-Townsfolk error.
  - missing demon/minion/outsider remain warnings.
- Updated API upload response contract to return parser info diagnostics alongside warnings.
- Extended `ScriptParserTests` with dedicated fixtures for Choirboy-without-King and Legion-on-script diagnostics.

### ✅ Completed

**Phase S9 — Web UI Surfacing**
- **Setup.razor:** Legion checkbox and LegionCount input already implemented with default calculation based on normal game good-count baseline, gated by script content.
- **Draft.razor:**
  - "Add Evil option" toggle already implemented on Curate panel with proper checkbox binding.
  - Inline ability picker for dynamic-setup characters (Alchemist/Boffin) already present after RecordChoice.
  - Added visual markers for unresolved ST-assigned slots showing "ST-assigned" status and "awaiting ST resolution" message in player order table.
- **Summary.razor:**
  - Added unresolved ST-assigned slot resolution UI with dropdown to select resolution character.
  - ResolveSlot method detects evil sentinel vs minion slots and calls appropriate resolution methods.
  - Dynamic-setup confirmation banner already exists showing warning when setup confirmation is needed.
  - Extended table to show unresolved slots distinctly.
- **DraftSessionState:**
  - Added `ResolveEvilSlotAsync(draftOrder, actualCharacterId, hiddenFlags)` wrapper method for evil-slot resolution.
  - Added `ResolveMinionSlotAsync(draftOrder, characterId)` wrapper method for minion-slot resolution.
  - All other pass-through methods already present: `GetDynamicAbilityOptions`, `AssignDynamicAbilityAsync`.
- **DraftSessionStateTests:**
  - Extended with S9-specific test scenarios:
    - `StartDraft_WithLegionScript_AllowsLegionGameOption`: verifies normal draft without Legion.
    - `ResolveEvilSlotAsync_WhenCalled_UpdatesSessionAndClears`: tests evil-slot resolution flow.
    - `ResolveMinionSlotAsync_WhenCalled_UpdatesSessionWithMinion`: tests minion-slot resolution flow.
    - `DynamicAbilityBanner_ShowsWhenAlchemistChosen`: tests dynamic-setup confirmation banner state.
- ✅ Build succeeds: 0 warnings, 0 errors.
- ✅ All tests pass: 97 tests (93 original + 4 new S9-specific tests).

### ✅ Completed

**Phase S10 — Persistence Round-Trip**
- All entity models have required fields: `IsLegionGame` and `LegionCount` on `GameSessionEntity`, `IsStAssigned` and `BorrowedAbilityCharacterId` on `PlayerSlotEntity`.
- Migration `AddBorrowedAbility` persists the ST-assigned and borrowed-ability fields to the database.
- `GameSessionRepository` mapping handles all special-case fields in both directions (entity-to-domain and domain-to-entity).
- Added comprehensive persistence round-trip tests in `GameSessionPersistenceTests`:
  - `LegionSession_RoundTrips_WithEvilSentinelsAndLegionCount`: verifies Legion game settings and "evil" sentinel choices persist correctly.
  - `UnresolvedStAssignedSlot_RoundTrips_WithEmptyBorrowedAbilityCharacterId`: verifies ST-assigned slots without resolution persist with `IsStAssigned=true` and empty `BorrowedAbilityCharacterId`.
  - `ResolvedStAssignedMinionSlot_RoundTrips_WithBorrowedAbilityCharacterId`: verifies ST-assigned minion slots resolved via `ResolveMinionSlot` persist with the assigned character id.
  - `ResolvedEvilSlot_RoundTrips_WithActualCharacterAssignment`: verifies evil sentinel slots resolved via `ResolveEvilSlot` persist with the actual character stored in `BorrowedAbilityCharacterId`.
  - `LegionSession_WithMultipleEvilSentinels_RoundTrips`: verifies complex Legion sessions with multiple evil sentinels (some resolved, some unresolved) persist correctly.
- ✅ Build succeeds: 0 warnings, 0 errors.
- ✅ All tests pass: 102 tests (97 original + 5 new S10-specific persistence tests).

### ✅ Completed

**Phase S1 — Setup Rule Coverage Fixes**
- Added new setup rule kinds in `TheCuratool.Domain`: `ReplaceDemonSetupRule`, `UnconstrainedOutsiderDeltaSetupRule`, `MinionDeltaSetupRule`, `SwapOutsiderForTownsfolkSetupRule`, and `NoSetupRule`.
- Registered all new rule kinds in the `CharacterDatabase` JSON loader.
- Updated `characters.json` per the reference table: `fang_gu` (+1 Outsider), `vigormortis` (−1 Outsider), `summoner` (`ReplaceDemon` + Demon-blocked availability), `godfather` (true ±1 via `StoryTellerChoice`), new `hermit` (NoOp / swap), new `lord_of_typhon` (unconstrained Outsider + extra Minion, Minion-blocked).
- Marionette retained for script-validation but flagged `isDraftExcluded` so it is filtered from `GetRemainingValidCharacters`, `SuggestThree`, and `CreateCuratedOffer`.
- Removed a duplicate `summoner` definition that existed in `characters.json`.
- Covered by `SetupCalculatorTests` (Fang Gu, Vigormortis, Summoner, Godfather, Hermit, Lord of Typhon, Marionette exclusion) and `DraftEngineTests` draft-exclusion regression.

### ✅ Completed

**Phase S2 — Required-Pair: Choirboy ↔ King**
- Added `autoAddIfMissing` flag on `RequiresCharacterSetupRule` and `IsOutOfScript` marker on `CharacterDefinition`.
- `DraftEngine` injects King as an out-of-script character when Choirboy is chosen and King is not on the script, surfacing it in the makeup summary.
- Added `choirboy` and `king` entries to `characters.json`.
- Covered by `DraftEngineTests`: both-on-script-neither-chosen, Choirboy-with-King-on-script, and Choirboy-without-King out-of-script auto-add.

### ✅ Completed

**Phase S5 — Non-Legion "Evil" Offer**
- `CreateCuratedOffer` accepts the `"evil"` sentinel alongside real character ids.
- `RecordChoice` accepts and stores `"evil"` (empty hidden flags) and bypasses character-database validation for the sentinel only.
- Resolution reuses the `ResolveEvilSlot` action from Phase S4; "Add Evil option" toggle present on the Draft Curate panel.
- Covered by `DraftEngineTests`: curated offer with evil sentinel accepted, and evil choice resolvable outside Legion mode.

### ✅ Completed

**Phase S7 — Dynamic-Setup Flag (Alchemist / Boffin) + Inline Ability Assignment**
- Added `IsDynamicSetup` and `DynamicAbilityScope` on `CharacterDefinition`, plus `BorrowedAbilityCharacterId` state for borrowed abilities.
- Added `AbilityOption` record and `DraftEngine` APIs: `GetAlchemistAbilityOptions`, `GetBoffinAbilityOptions`, and `AssignDynamicAbility`, with speculative feasibility checks producing human-readable greying-out reasons.
- `DraftMath.RequiresStorytellerSetupConfirmation` flags unresolved dynamic-setup slots until assigned.
- Inline ability picker on `Draft.razor` and late-resolution UI on `Summary.razor`; round-trip persistence via `BorrowedAbilityCharacterId`.
- Added `alchemist` and `boffin` entries to `characters.json`.
- Covered by `DraftEngineTests` (scope filtering, Baron/Godfather/Huntsman greying-out, assignment rejection, count effects, no-rule clearing) and persistence round-trip tests.

### ✅ Completed

**Phase S11 — Documentation**
- Added a "Special Cases" section to `README.md` linking to `plan-special-cases.md` with a per-S-phase status table.
- Recorded the S-phase completion entries in this `## Implementation Status` section.
- Added completion status markers to S1, S4, S9, and S11 headers in `plan-special-cases.md`.
- ✅ Build succeeds: 0 warnings, 0 errors.
- ✅ All 102 tests pass.

