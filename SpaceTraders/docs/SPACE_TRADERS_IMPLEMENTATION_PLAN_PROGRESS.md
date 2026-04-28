# SpaceTraders Implementation Plan Progress

This document tracks implementation progress for `docs/SPACE_TRADERS_IMPLEMENTATION_PLAN.md`.

Legend:

- [ ] Not started
- [~] In progress
- [x] Completed
- [-] Deferred / backlog

## Overall phase status

| Phase | Title | Status | Notes |
|------|-------|--------|-------|
| 0 | Documentation and build hygiene | [x] | Root build-path issue fixed, docs aligned to implementation, matrix added, smoke tests expanded. |
| 1 | API client parity for core gameplay | [x] | All 10 endpoints added: typed client, port adapter, commands, and 14 unit tests. |
| 2 | Data model enrichment | [ ] | Not started. |
| 3 | Contract-first onboarding automation | [ ] | Not started. |
| 4 | Market and supply-chain automation | [ ] | Not started. |
| 5 | Navigation, fuel, jump, and exploration planning | [ ] | Not started. |
| 6 | Extraction, survey, siphon, and resource logistics | [ ] | Not started. |
| 7 | Fleet outfitting, maintenance, repair, and scrapping | [ ] | Not started. |
| 8 | Reset awareness and operational reliability | [ ] | Not started. |
| 9 | Razor Pages dashboard expansion | [ ] | Not started. |
| 10 | Advanced gameplay backlog | [-] | Backlog by design. |

## Phase 0 checklist

Goal: make the repository easy to build, understand, and validate before expanding behavior.

- [x] Fix solution/project path issues that prevent `dotnet build SpaceTraders.slnx` from running from `C:\git\projects\SpaceTraders`.
  - Removed analyzer source compile from root `.net.csproj` so Roslyn analyzer source is compiled only by `SpaceTraders.Analyzers.csproj`.
- [x] Correct `docs/implementation/CURRENT_IMPLEMENTATION_OVERVIEW.md` to avoid claiming divergence health checks are implemented until they exist.
  - Updated health endpoint wording to current implemented endpoints.
- [x] Keep `spacetraders.md` as source/reference material, but do not treat all historical changelog entries as current required behavior.
  - Reflected in implementation overview scope text and matrix framing.
- [x] Add a short implemented-vs-reference matrix to documentation.
  - Added to `docs/implementation/CURRENT_IMPLEMENTATION_OVERVIEW.md`.
- [x] Add smoke tests for current local run assumptions where practical.
  - Expanded API integration smoke coverage for path-base assumptions and auth behavior (`ApiIntegrationTests`).

## Phase deliverables status

### Phase 0 deliverables

- [x] Build succeeds from workspace root.
  - Verified by running `dotnet build SpaceTraders.slnx` from `C:\git\projects\SpaceTraders`.
- [x] Documentation matches code and has a clear gap matrix.

### Phase 1 deliverables

- [x] Typed client and command support for all core missing quickstart/gameplay endpoints.
- [x] Tests proving state guards and cache updates after each mutating command.

## Phase 1 checklist

Goal: expose the SpaceTraders endpoints needed by the quickstart and current automation lifecycle.

- [x] `GET /my/ships/{shipSymbol}/cargo` → `GetShipCargoCommand` / `GetShipCargoHandler`
- [x] `POST /my/ships/{shipSymbol}/jettison` → `JettisonCargoCommand` / `JettisonCargoHandler`
- [x] `POST /my/ships/{shipSymbol}/negotiate/contract` → `NegotiateContractCommand` / `NegotiateContractHandler`
- [x] `PATCH /my/ships/{shipSymbol}/nav` → `PatchShipNavCommand` / `PatchShipNavHandler`
- [x] `POST /my/ships/{shipSymbol}/survey` → `SurveyCommand` / `SurveyHandler`
- [x] `POST /my/ships/{shipSymbol}/extract/survey` → `ExtractWithSurveyAsync` on `ISpaceTradersPort` (reuses `ExtractionActionResult`)
- [x] `POST /my/ships/{shipSymbol}/siphon` → `SiphonResourcesCommand` / `SiphonResourcesHandler`
- [x] `POST /my/ships/{shipSymbol}/warp` → `WarpShipCommand` / `WarpShipHandler`
- [x] `POST /my/ships/{shipSymbol}/jump` → `JumpShipCommand` / `JumpShipHandler`
- [x] `POST /my/ships/{shipSymbol}/chart` → `CreateChartCommand` / `CreateChartHandler`
- [x] Request/response models added to `SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet.Phase1ActionModels`.
- [x] Interface methods added to `ISpaceTradersApiClient` and `ISpaceTradersPort`.
- [x] `PatchWrappedAsync` helper added to `SpaceTradersApiClient` for PATCH endpoints.
- [x] `SpaceTradersPortAdapter` maps all new results to application port models.
- [x] Application port models added to `ApiPortModels.cs` (`SurveyModel`, `SurveyActionResult`, `SiphonActionResult`, `WarpActionResult`, `JumpActionResult`, `ChartActionResult`, `JettisonActionResult`, `NegotiateContractActionResult`).
- [x] All mutating command handlers update the local cache (nav, fuel, or cargo) before returning.
- [x] All state-gated handlers (survey, siphon, warp, jump) publish `ShipStateMismatchEvent` when the ship is in the wrong state.
- [x] 14 unit tests added in `Phase1CommandHandlerTests` and `NegotiateContractHandlerTests`.

## Validation log

- `dotnet build SpaceTraders.slnx`
  - Before fixes: failed due to analyzer Roslyn types unresolved in `.net.csproj`.
  - After fixes: succeeded from workspace root.
- `run_tests` for `SpaceTraders.API.Tests.ApiIntegrationTests`
  - Result: 18 passed, 0 failed.
- `run_tests` for `Phase1CommandHandlerTests`, `NegotiateContractHandlerTests`
  - Result: 14 passed, 0 failed.
- `dotnet build SpaceTraders.slnx` after Phase 1 changes
  - Result: build successful.

## Update protocol

When a phase changes status:

1. Update the overall phase status table.
2. Check off completed items in the phase checklist.
3. Record validation commands and outcomes.
4. Keep notes concise and factual.
