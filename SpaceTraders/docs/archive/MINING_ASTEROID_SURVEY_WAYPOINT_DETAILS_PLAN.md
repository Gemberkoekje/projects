# Mining Asteroid, Survey Goal, and Waypoint Details Plan

## Overview

This plan covers three related improvements to mining automation and map visibility:

1. Verify and enforce that mining ships navigate to the closest asteroid where the target resource is available.
2. Add a survey goal for the selected asteroid/resource when mining proceeds without a usable survey.
3. Allow the WebUI to show details for the waypoint where a ship is located, including waypoint type, mineable resources, and attributes.

---

## Goals and Objectives

### Goal 1: Closest Resource-Bearing Asteroid Selection

Mining automation should not blindly reuse a stale or arbitrary `SourceWaypoint`. For a target trade symbol, the system should choose the closest asteroid or asteroid-like waypoint that can produce that resource.

**Success criteria:**

- Mining goals resolve their source waypoint from known waypoint/resource data.
- The selected waypoint is the closest known valid source to the relevant ship, sell market, or route origin according to the chosen distance rule.
- `MineResourceVolumeCommand` receives a validated source waypoint.
- If no known source exists, the goal is blocked or requeued with an observable reason instead of navigating to an invalid waypoint.

### Goal 2: Survey Goal Creation When Mining Without Survey

Survey-aware mining already supports passing a `SurveyModel` into `MineResourceVolumeCommand`. When no usable survey exists and the command falls back to blind extraction, the system should enqueue a survey goal for the same asteroid and target resource so future mining cycles can improve yields.

**Success criteria:**

- Mining without a usable survey creates or refreshes a `SurveyWaypointGoal` for `(waypoint, resource)`.
- Duplicate survey goals are avoided.
- Goal insertion is idempotent across repeated mining ticks.
- Blind mining remains available as a fallback and is not blocked by survey-goal creation failures unless repository failure requires command rejection.

### Goal 3: Clickable Ship Waypoint Details

The WebUI should make the waypoint where a ship is currently located clickable and display details about that waypoint.

**Success criteria:**

- Ship location waypoints can be clicked from the relevant UI view.
- The details panel or route displays waypoint symbol, type, traits/attributes, chart status if available, and extractable resources if known.
- The UI handles loading, missing data, and stale/unknown waypoint data gracefully.
- The backend exposes enough waypoint detail data to avoid hardcoding resource knowledge in the frontend.

---

## Current Observations

- `MineResourceVolumeCommand` currently trusts `command.SourceWaypoint` and navigates there when the ship is not already at the source.
- The command already supports optional survey-based extraction through `SurveyModel Survey` and falls back to `ExtractResourcesAsync` when no usable survey is supplied.
- Existing survey automation includes `SurveyWaypointGoal` support and a `SurveyWaypointGoalExecutor`.
- Existing planning docs show survey-aware mining is partially implemented, but automatic survey-goal creation after blind mining still needs to be wired.
- The WebUI already has system and ship detail pages that are likely integration points for clickable waypoint details.

---

## Proposed Design

### 1. Resource Source Resolution

Add or reuse an application-level service that can answer:

- Which known waypoints can produce a given trade symbol?
- Which of those waypoints are valid asteroid/mining waypoints?
- Which candidate is closest to the selected origin?

Recommended behavior:

1. Query known waypoints in the relevant system.
2. Filter to mining-capable waypoint types and/or waypoints with resource traits/deposits.
3. Match the target `TradeSymbol` against known extractable resources.
4. Sort by distance from the chosen origin.
5. Return the nearest candidate with enough metadata to explain the choice.

Open decision: use the ship's current waypoint as the distance origin, or use the sell market waypoint so mining routes minimize delivery distance. For sell-driven mining goals, the sell market is usually the better default.

### 2. Mining Goal Integration

Update the mining goal executor or automation service so source selection happens before issuing `MineResourceVolumeCommand`.

Preferred ownership:

- `MineAndSellGoalExecutor` or the mining automation layer should select the source waypoint.
- `MineResourceVolumeCommand` should remain focused on executing movement/extraction for the source it is given.
- Add a defensive validation path in the command only if invalid source waypoints can still reach it from other callers.

Expected flow:

1. Mining goal identifies target resource and destination sell market.
2. Source resolver chooses the closest valid resource-bearing asteroid.
3. Executor checks `ISurveyRepository` for a usable survey at that waypoint/resource.
4. Executor sends `MineResourceVolumeCommand` with the resolved source and best usable survey when available.
5. If no source exists, mark the goal blocked with a clear reason.

### 3. Survey Goal Upsert on Blind Mining

When no usable survey is available for the resolved source waypoint/resource, upsert a survey goal.

Preferred ownership:

- Put this in the mining goal executor before or immediately after sending `MineResourceVolumeCommand` without a survey.
- Avoid placing high-level goal orchestration inside `MineResourceVolumeHandler` unless command-level callers need the behavior universally.

Expected flow:

1. Resolve source waypoint.
2. Try to load best active survey for `(sourceWaypoint, tradeSymbol)`.
3. If no usable survey exists, call a goal repository/service to upsert `SurveyWaypointGoal`.
4. Continue with blind mining for current progress.
5. Future ticks should pick up the survey goal and produce surveys for later mining.

Idempotency rule:

- Use a stable uniqueness key such as `(GoalKind.SurveyWaypoint, waypointSymbol, targetResourceSymbol)` for active/pending goals.
- Do not create a new survey goal if an active, assigned, or pending survey goal already exists for the same waypoint/resource.

### 4. Waypoint Details API

Expose waypoint details through an API endpoint or existing status endpoint extension.

Data to include:

- `symbol`
- `systemSymbol`
- `type`
- `x`, `y`
- `traits` or attributes
- `modifiers` if available
- `chart` metadata if available
- known extractable resources/deposits
- market/shipyard/jump gate availability flags if already stored
- freshness/source metadata where useful

Recommended API shapes:

- `GET /api/systems/{systemSymbol}/waypoints/{waypointSymbol}` for direct lookup, or
- Extend an existing dashboard/system-map payload if it already carries waypoint data.

Prefer a direct endpoint if the details can be requested lazily only when the user clicks a waypoint.

### 5. WebUI Interaction

Add clickable waypoint behavior in the relevant ship/system view.

Expected UI flow:

1. Render the ship's current waypoint symbol as a clickable element.
2. On click, request waypoint details from the API.
3. Show details in a side panel, modal, drawer, or existing details area.
4. Display loading and error states.
5. Show empty states for unknown traits/resources.

The UI should avoid assuming a fixed resource list. It should render whatever the backend returns.

---

## Proposed File Touchpoints

### Application

- `SpaceTraders.Application\Goals\Executors\MineAndSellGoalExecutor.cs` — resolve the closest source asteroid and upsert survey goals when mining without a survey.
- `SpaceTraders.Application\Automation\MiningAutomationService.cs` — review whether mining goals are created with source assumptions that should move into resolver logic.
- `SpaceTraders.Application\Commands\Ships\MineResourceVolumeCommand.cs` — keep command execution focused; optionally add defensive source validation if needed.
- `SpaceTraders.Application\Interfaces\Repositories\IWaypointRepository.cs` — add query support if current repository cannot retrieve resource-bearing waypoint candidates.
- `SpaceTraders.Application\Interfaces\Repositories\IShipGoalRepository.cs` — add or reuse idempotent upsert support for survey goals.

### Domain

- `SpaceTraders.Domain\Goals\SurveyWaypointGoal.cs` — confirm it carries waypoint and resource information needed for idempotent upsert.
- `SpaceTraders.Domain\Goals\MineAndSellGoal.cs` — confirm source waypoint is stored only if it remains valid or can be recomputed.

### Infrastructure

- `SpaceTraders.Infrastructure.Persistence` waypoint repository implementation — support candidate lookup and detail retrieval.
- `SpaceTraders.Infrastructure.Persistence` goal repository implementation — support duplicate-safe survey goal upsert if not already available.

### API

- `SpaceTraders.API\Endpoints` — add a waypoint details endpoint or extend an existing system/status endpoint.
- `SpaceTraders.API\Dtos` — add waypoint detail DTOs with traits/resources/attributes.
- `SpaceTraders.API\Mappers` — map persisted waypoint data into frontend-safe DTOs.

### WebUI

- `SpaceTraders.WebUI\src\Future\pages\SystemMapPage.tsx` — likely integration point for clickable waypoint details.
- `SpaceTraders.WebUI\src\Future\pages\ShipDetailPage.tsx` — likely integration point for clicking a ship's current waypoint.
- `SpaceTraders.WebUI\src\types.ts` — add waypoint detail DTO types.
- `SpaceTraders.WebUI\src\lib\api-fetch.ts` or related API client — add waypoint details request helper.

### Tests

- `tests\SpaceTraders.Application.Tests` — source selection and survey-goal upsert behavior.
- `tests\SpaceTraders.API.Tests` — waypoint details endpoint contract.
- `SpaceTraders.WebUI` tests — click behavior and details rendering.

---

## Implementation Steps

1. Inspect current mining goal execution to identify where `SourceWaypoint` is selected.
2. Inspect waypoint persistence models to determine how mineable resources and waypoint traits are stored.
3. Add a closest resource-bearing waypoint resolver or repository query.
4. Update mining goal execution to resolve and validate the mining source before creating `MineResourceVolumeCommand`.
5. Add idempotent survey-goal upsert when no usable survey is available for the resolved waypoint/resource.
6. Add unit tests for closest asteroid selection, missing source handling, and duplicate-safe survey-goal creation.
7. Add or extend an API endpoint that returns waypoint details.
8. Add API contract tests for waypoint details, including known resources and traits.
9. Add WebUI types and API client support for waypoint details.
10. Make ship waypoint symbols clickable and render waypoint details in the UI.
11. Add WebUI tests for click, loading, success, and missing-data states.
12. Run targeted tests, then run the full build/test suite if time allows.

---

## Risks and Open Questions

- Resource availability may not be stored directly on waypoints; it may need to be inferred from surveys, traits, market data, or static SpaceTraders waypoint types.
- The best distance origin needs confirmation: ship position, sell market, or current mining route origin.
- Existing goals may already carry a source waypoint; the plan should avoid stale persisted source choices.
- Survey goals must not starve mining execution by blocking blind mining entirely.
- Waypoint details may require refreshing data from the SpaceTraders API if the local cache is incomplete.

---

## Acceptance Criteria

- [ ] Mining automation selects the closest known asteroid where the requested resource is available.
- [ ] Mining goals do not navigate to asteroids that cannot produce the target resource when better data exists.
- [ ] Mining without a usable survey creates or refreshes a survey goal for the selected asteroid/resource.
- [ ] Survey-goal creation is duplicate-safe and idempotent.
- [ ] Blind mining still works when no survey is available.
- [ ] A ship's current waypoint can be clicked in the WebUI.
- [ ] Clicking a waypoint shows type, mineable resources, and attributes/traits when known.
- [ ] Missing or stale waypoint details are handled gracefully.
- [ ] Application, API, and WebUI tests cover the new behavior.
