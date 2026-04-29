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
| 2 | Data model enrichment | [x] | Waypoint, ship, market, and shipyard persistence extended; 6 integration tests added. |
| 3 | Contract-first onboarding automation | [x] | Contract objective planning, assignment metadata, idempotent mutations, milestone logging, onboarding orchestration, and phase-3 tests implemented. |
| 4 | Market and supply-chain automation | [x] | Trade scoring upgraded with volume/supply/activity/fuel-time penalties, probe-first market scouting, and Market UI expanded with detail, route comparison, and production-chain views. |
| 5 | Navigation, fuel, jump, and exploration planning | [ ] | Not started. |
| 6 | Extraction, survey, siphon, and resource logistics | [ ] | Not started. |
| 7 | Fleet outfitting, maintenance, repair, and scrapping | [ ] | Not started. |
| 8 | Reset awareness and operational reliability | [ ] | Not started. |
| 9 | Razor Pages dashboard expansion | [ ] | Not started. |
| 10 | Advanced gameplay backlog | [-] | Backlog by design. |

## Phase deliverables status

### Phase 0 deliverables

- [x] Build succeeds from workspace root.
  - Verified by running `dotbase build SpaceTraders.slnx` from `C:\git\projects\SpaceTraders`.
- [x] Documentation matches code and has a clear gap matrix.

### Phase 1 deliverables

- [x] Typed client and command support for all core missing quickstart/gameplay endpoints.
- [x] Tests proving state guards and cache updates after each mutating command.

### Phase 2 deliverables

- [x] Persistence can represent the SpaceTraders concepts currently present in `spacetraders.md` and returned by the typed client.
- [x] Existing dashboard pages continue to load with older cached data (backward-compatible ALTER TABLE IF NOT EXISTS migrations).

### Phase 3 deliverables

- [x] A fresh agent can progress through the first contract without manual intervention.
  - Implemented startup-triggered phase 3 onboarding orchestration with sync, accept, assignment, delivery/fulfill progression, and negotiation fallback.

### Phase 4 deliverables

- [x] Better trade opportunity ranking and UI explainability.
  - Expanded `TradeAnalyser` with normalized symbols, supply-chain map export, and route scoring that includes category type, trade volume, supply/activity, distance, estimated fuel/travel time, opportunity-cost, cooldown, and rate-limit penalties.
  - Extended `TradeOpportunityDto` and `trade_opportunities` persistence schema with phase-4 scoring/explainability fields (`BuyType`, `SellType`, `EffectiveTradeVolume`, fuel/time/penalty metrics, `RouteScore`) using backward-compatible initializer ALTER statements.
  - Updated trade opportunity repository ranking and best-route selection to use `RouteScore` while preserving supply-chain prioritization.
  - Added probe-first scout targeting in `ShipAssignmentPlanner` to prioritize stale market/shipyard waypoints for visibility coverage.
  - Expanded Razor Pages `Market/Index` into combined market detail, route comparison, and production-chain explorer views.
  - Added/updated application tests for phase-4 metrics population and production-chain mapping.

## Phase 3 checklist

Goal: reliably complete the early SpaceTraders loop described in the quickstart.

- [x] Extend contract API/application models with terms and deliverables metadata.
  - `SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts.Contract` now includes terms/payment/deliverables.
  - `ContractModel`/`ContractActionResult` in `ApiPortModels.cs` and adapter mappings now carry terms/deadline/deliverables.
- [x] Persist contract terms/deadline/deliverables for planning.
  - Extended `CachedContract`, `ContractDto`, `ContractRepository`, and `SyncContractsHandler`.
  - Added backward-compatible initializer migration columns in `SpaceTradersDatabaseInitializer`.
- [x] Add assignment metadata for contract cargo target, delivery waypoint, and required units.
  - Extended `AssignShipCommand`, `ShipAssignmentDto`, `ShipAssignmentRecord`, and `ShipAssignmentRepository` with `RequiredUnits`.
  - Added EF mapping and migration column for `RequiredUnits`.
- [x] Add a contract objective planner that maps contract deliverables to ship roles.
  - Added `IContractObjectivePlanner`/`ContractObjectivePlanner`; integrated into `ShipAssignmentPlanner`.
- [x] Add idempotency checks for contract accept/deliver/fulfill.
  - Implemented cache-aware guards in `AcceptContractHandler`, `DeliverContractHandler`, and `FulfillContractHandler`.
- [x] Add activity log entries for each contract milestone.
  - Added milestone events (`ContractNegotiatedEvent`, `ContractDeliveryRecordedEvent`) and logging in `LogActivityHandler`.
- [x] Add tests for expired contract and partial delivery scenarios.
  - Added `Phase3ContractAutomationTests` covering expired accept, partial delivery, and contract objective planning.
- [x] End-to-end onboarding automation flow (register/sync/accept/purchase/navigate/extract/sell/deliver/fulfill/negotiate fallback).
  - Added `RunPhase3OnboardingCommand` + `Phase3OnboardingHandler`, invoked from `StartupSyncService`.
  - Added contract docked/in-orbit role handlers to execute contract assignments using state-scoped command acceptors.

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
- `dotnet build SpaceTraders.slnx` after Phase 2 changes
  - Result: build successful.
- `run_tests` for `Phase2DataModelTests`
  - Result: 6 passed, 0 failed.
- `run_tests` for full `SpaceTraders.Infrastructure.Tests` project after Phase 2
  - Result: 30 passed, 0 failed.
- `run_tests` for phase 3-focused application tests (`Phase3ContractAutomationTests`, `ContractMutationHandlerTests`, `NegotiateContractHandlerTests`, `ShipAssignmentPlannerTests`)
  - Result: 11 passed, 0 failed.
- `dotnet build SpaceTraders.slnx` after Phase 3 changes
  - Result: build successful.
- `run_tests` for `SpaceTraders.Application.Tests.Services.TradeAnalyserTests` after Phase 4 changes
  - Result: 14 passed, 0 failed.
- `run_tests` for `SpaceTraders.Application.Tests.Services.ShipAssignmentPlannerTests` after Phase 4 changes
  - Result: 3 passed, 0 failed.
- `dotnet build SpaceTraders.slnx` after Phase 4 changes
  - Result: build successful.
