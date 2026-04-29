# SpaceTraders Implementation Plan

This plan maps the SpaceTraders.io gameplay/API reference in `spacetraders.md` to the current implementation documented in `docs/implementation/CURRENT_IMPLEMENTATION_OVERVIEW.md`.

## Current baseline

Implemented today:

- .NET 10 API automation host with Wolverine command/event dispatch.
- Razor Pages dashboard backed by PostgreSQL cache.
- SpaceTraders typed HTTP client for:
  - status
  - factions
  - agents
  - systems
  - waypoints
  - registration
  - my agent
  - my ships
  - my contracts
  - navigate, dock, orbit, extract, buy, sell, refuel, purchase ship
  - accept, deliver, fulfill contracts
  - market and shipyard reads
- PostgreSQL cache for agents, ships, contracts, systems, waypoints, markets, shipyards, settings, activity logs, API usage, leader leases, credentials, assignments, and trade opportunities.
- Ship automation chain for mining, scouting, trading, transit recovery, and state-gated command issuing.
- Internal API for status, settings, control, health, and metrics.

Main gaps against `spacetraders.md`:

- Several SpaceTraders API endpoints in the reference document are not exposed by `SpaceTradersApiClient` yet.
- Persistence does not yet model all SpaceTraders concepts needed for deeper automation: waypoint traits/modifiers, orbitals, ship components, condition/integrity, cooldowns, surveys, construction, jump gates, and production chains.
- Automation covers early mining/trading/scouting behavior but not the full gameplay lifecycle: contract negotiation, surveying, siphoning, refueling logistics, jump-gate construction, outfitting, repair/scrap decisions, or reset-aware agent strategy.
- Razor Pages views are operational but do not yet expose the full game model or manual controls for all supported actions.

## Guiding principles

1. Keep the API host as the automation writer and the Razor Pages app as the operational UI.
2. Keep PostgreSQL as the local cache and durable state store.
3. Prefer adding typed client methods, command handlers, repositories, and Razor Pages incrementally by SpaceTraders gameplay area.
4. Every mutating SpaceTraders API call should update the local cache from the returned response before publishing follow-up events.
5. Commands must remain state-gated: docked handlers issue docked-only commands, in-orbit handlers issue in-orbit commands, and in-transit handlers only schedule/observe transit.
6. Never log full account or agent tokens.

## Phase 0: documentation and build hygiene

Goal: make the repository easy to build, understand, and validate before expanding behavior.

Tasks:

- Fix solution/project path issues that prevent `dotnet build SpaceTraders.slnx` from running from `C:\git\projects\SpaceTraders`.
- Correct `docs/implementation/CURRENT_IMPLEMENTATION_OVERVIEW.md` to avoid claiming divergence health checks are implemented until they exist.
- Keep `spacetraders.md` as source/reference material, but do not treat all historical changelog entries as current required behavior.
- Add a short “implemented vs reference” matrix to the documentation and keep it updated with each phase.
- Add smoke tests for current local run assumptions where practical.

Deliverables:

- Build succeeds from workspace root.
- Documentation matches the code and has a clear gap matrix.

## Phase 1: API client parity for core gameplay

Goal: expose the SpaceTraders endpoints needed by the quickstart and current automation lifecycle.

Add typed client support for:

- `GET /my/ships/{shipSymbol}/cargo`
- `POST /my/ships/{shipSymbol}/jettison`
- `POST /my/ships/{shipSymbol}/negotiate/contract`
- `PATCH /my/ships/{shipSymbol}/nav` for flight mode changes
- `POST /my/ships/{shipSymbol}/survey`
- `POST /my/ships/{shipSymbol}/extract/survey` if current API supports the explicit survey endpoint
- `POST /my/ships/{shipSymbol}/siphon`
- `POST /my/ships/{shipSymbol}/warp`
- `POST /my/ships/{shipSymbol}/jump`
- `POST /my/ships/{shipSymbol}/chart`

Implementation notes:

- Add request/response models under `SpaceTraders.Infrastructure.SpaceTradersAPI.Models.*`.
- Add interface methods to the application client abstraction.
- Add application commands for mutating calls.
- Persist returned ship, cargo, cooldown, agent, market, or contract data in the same command handling path.
- Extend unit tests for serialization, endpoint paths, and cache updates.

Deliverables:

- Typed client and command support for core missing quickstart/gameplay endpoints.
- Tests proving cache updates after each mutating command.

## Phase 2: data model enrichment

Goal: persist enough reference data to support route planning, market planning, and ship capability decisions.

Tasks:

- Extend waypoint cache to include:
  - traits JSON
  - modifiers JSON
  - orbitals/parent symbol
  - chart data when available
- Extend ship cache to include:
  - frame/reactor/engine JSON
  - modules JSON
  - cooldown data
  - condition/integrity fields or component JSON
  - nav route origin/destination details if not already covered
- Extend market cache to preserve supply/activity/trade-volume fields used by trade planning.
- Add reference-data cache for shipyard ship/component availability where needed.
- Add migration/tests for new persistence fields.

Deliverables:

- Persistence can represent the SpaceTraders concepts currently present in `spacetraders.md` and returned by the typed client.
- Existing dashboard pages continue to load with older cached data.

## Phase 3: contract-first onboarding automation

Goal: reliably complete the early SpaceTraders loop described in the quickstart.

Automation flow:

1. Register/select agent.
2. Sync agent, starter contract, ships, starting system, and starting waypoint.
3. Accept starter contract when appropriate.
4. Locate shipyard and purchase/assign mining drone when affordable.
5. Locate engineered asteroid or suitable asteroid waypoint.
6. Orbit, navigate, dock/refuel, orbit, extract.
7. Sell non-contract cargo when profitable or jettison when needed.
8. Navigate to delivery waypoint, deliver contract goods, and fulfill the contract.
9. Negotiate a replacement contract if the starter contract expires.

Tasks:

- Add a contract objective planner that maps contract deliverables to ship roles.
- Add assignment metadata for contract cargo target, delivery waypoint, and required units.
- Add idempotency checks for contract accept/deliver/fulfill.
- Add activity log entries for each contract milestone.
- Add tests for expired contract and partial delivery scenarios.

Deliverables:

- A fresh agent can progress through the first contract without manual intervention.

## Phase 4: market and supply-chain automation

Goal: use market visibility, imports/exports/exchange data, and production-chain knowledge to improve trading.

Tasks:

- Normalize trade symbols and supply-chain relationships into a service or reference table.
- Expand `TradeAnalyser` to account for:
  - import/export/exchange category
  - purchase/sell price
  - trade volume
  - supply/activity levels
  - distance, fuel cost, travel time, and cargo capacity
- Add probe/scout strategy for market visibility: cheap probes monitor important markets.
- Add trade-route scoring that includes opportunity cost, cooldowns, and rate limits.
- Add Razor Pages views for:
  - market detail
  - route comparison
  - production chain for a selected trade good

Deliverables:

- Better trade opportunity ranking and UI explainability.

## Phase 5: navigation, fuel, jump, and exploration planning

Goal: plan safe movement across systems using the reference travel/fuel rules.

Tasks:

- Add a distance/travel-time/fuel-cost service using waypoint/system coordinates and flight modes.
- Add flight mode selection for speed vs fuel conservation.
- Add refuel-from-cargo support when the API response model is available.
- Add warp and jump command handlers.
- Cache jump-gate connections when available.
- Add exploration assignments for:
  - discovering systems/waypoints
  - charting waypoints
  - finding shipyards and markets
  - finding high-value asteroids/resources

Deliverables:

- Ships can choose safer routes, refuel intelligently, and explore beyond the starting system.

## Phase 6: extraction, survey, siphon, and resource logistics

Goal: improve mining/resource automation beyond simple extraction.

Tasks:

- Add survey command support and survey persistence with expiration/deposit data.
- Add survey-aware extraction decisions.
- Add siphon support for gas giants and gas processor requirements.
- Add resource-targeting logic from contract needs and market profitability.
- Add asteroid stability/modifier awareness.
- Add cargo policies:
  - keep contract cargo
  - sell profitable surplus
  - jettison low-value cargo when needed
  - reserve fuel/hydrocarbon cargo for refueling logistics

Deliverables:

- Mining and siphoning roles select resources intentionally rather than only extracting opportunistically.

## Phase 7: fleet outfitting, maintenance, repair, and scrapping

Goal: manage long-running fleet health and ship capabilities.

Tasks:

- Add API client support for:
  - repair estimate and repair
  - scrap estimate and scrap
  - module/mount list/install/remove endpoints
- Persist ship component condition/integrity.
- Add maintenance policy:
  - repair thresholds
  - scrap thresholds
  - avoid assigning damaged ships to long routes
- Add outfitting policy:
  - miners need mining lasers and mineral processors
  - siphoners need gas siphons and gas processors
  - scouts/probes need suitable sensors when available
  - traders maximize cargo and speed
- Add Razor Pages manual controls for repair/scrap/outfitting.

Deliverables:

- Fleet roles account for ship capability and maintenance state.

## Phase 8: reset awareness and operational reliability

Goal: make automation robust across SpaceTraders server resets, pod restarts, and API instability.

Tasks:

- Use status endpoint `serverResets` data to warn before resets.
- Add reset-aware automation pause/resume policy.
- Ensure invalid reset-date tokens trigger safe re-registration or operator intervention.
- Add divergence health check that samples cached ships against the SpaceTraders API.
- Add dashboard alerting for:
  - API unavailable
  - token invalid/reset mismatch
  - cache divergence
  - automation disabled
  - contract deadlines approaching
- Add runbook documentation for reset recovery.

Deliverables:

- Operators can see and recover from resets and cache/API drift.

## Phase 9: Razor Pages dashboard expansion

Goal: expose the expanded automation and game state clearly in the existing Razor Pages app.

Pages/features to add or expand:

- Fleet page with filters by role, state, cargo, fuel, cooldown, and assignment.
- Ship command page for manual safe commands.
- Contract detail page with delivery progress and assigned ships.
- System map page using cached systems/waypoints.
- Waypoint detail page for traits, modifiers, market, shipyard, and chart data.
- Market detail and trade-route explanation pages.
- Production-chain reference pages sourced from normalized supply-chain data.
- Maintenance/outfitting pages.
- Reset/status banner.

Deliverables:

- Dashboard supports operator decisions without requiring direct database/API inspection.

## Phase 10: jump-gate construction automation

Goal: implement construction as a first-class automation objective, with early-game behavior focused on supplying jump-gate requirements.

Tasks:

- Add typed client support for construction endpoints needed to:
  - read construction status/material requirements
  - deliver construction materials
- Persist construction-site state:
  - required symbols/units
  - fulfilled/remaining units
  - completion status and timestamps
- Add a construction planner that:
  - derives required prerequisite goods for jump-gate parts
  - maps prerequisite sourcing to mining, refining/processing (where applicable), and trading roles
  - assigns ships to gather, buy, transport, and deliver required materials
- Make early-game automation prioritize jump-gate construction supply after starter-contract viability checks.
- Expand trade analysis to score routes by construction contribution, not only immediate profit.
- Add delivery idempotency and safety checks for construction material hand-ins.
- Add Razor Pages views for:
  - construction progress
  - remaining materials by symbol
  - assigned ships and inbound deliveries

Deliverables:

- Early-game fleet behavior mines/trades specifically toward jump-gate material prerequisites.
- Automation can continuously supply construction inputs until jump-gate completion.

## Suggested milestone order

1. Build/doc hygiene and gap matrix.
2. Missing core typed client endpoints.
3. Persistence enrichment for ships/waypoints/markets.
4. Contract-first onboarding automation.
5. Navigation/fuel/exploration planning.
6. Extraction/survey/siphon improvements.
7. Market and supply-chain analysis improvements with construction-prerequisite scoring.
8. Jump-gate construction automation.
9. Maintenance/outfitting.
10. Reset reliability and dashboard alerts.
11. Dashboard expansion and advanced gameplay.

## Validation strategy

For every phase:

- Add or update unit tests for command handlers and services.
- Add repository tests for persistence changes.
- Add API client tests for endpoint paths and payloads.
- Add integration tests for startup/recovery behavior when state-machine or hosted-service behavior changes.
- Run `dotnet build SpaceTraders.slnx` and targeted tests before completion.
