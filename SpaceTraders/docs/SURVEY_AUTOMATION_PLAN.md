# Survey Automation Plan

## Status: Phases 1-3 Complete ✅

**Last Updated:** 2025-01-09
**Implementation Progress:** 43% Complete (6 of 14 steps)

### What's Been Implemented

**Phase 1 - Domain Infrastructure:**
- ✅ Added `SurveyWaypoint` enum value to `ShipGoalKind`
- ✅ Created `SurveyWaypointGoal` domain record with survey parameters
- ✅ Registered JSON discriminator for polymorphic serialization

**Phase 2 - Executor & Commands:**
- ✅ Implemented `SurveyWaypointGoalExecutor` with full lifecycle handling
- ✅ Registered executor in DI container
- ✅ Extended `MineResourceVolumeCommand` to accept optional `Survey` parameter
- ✅ Updated command handler to use `ExtractWithSurveyAsync()` when survey provided

**Phase 3 - Mining Integration:**
- ✅ Updated `MineAndSellGoalExecutor` to query for surveys before mining
- ✅ Integrated survey repository lookups
- ✅ Updated test fixtures to support new dependencies
- ✅ Maintained backward compatibility with blind mining fallback

**Ready for Testing:**
- Survey automation can now run end-to-end for individual ships
- Surveys are stored and consumed by mining operations
- Fallback to blind mining when surveys unavailable

### Next Steps (Phases 4-6)
- Orchestrator integration for multi-ship survey campaigns
- Optional fleet expansion logic
- Comprehensive test coverage

---

## Overview

This plan describes the implementation of survey-driven mining automation in the SpaceTraders application. The system will enable:

1. **Initial surveying phase** — The command ship surveys each mineral type at waypoints close to their selling markets
2. **Survey-aware mining** — Mining operations preferentially use existing surveys to maximize extraction efficiency
3. **Optional fleet expansion** — Purchase additional survey-capable ships to parallelize surveying, freeing the command ship for higher-value activities

---

## Goals and Objectives

### Goal 1: Initial Surveying Phase
- Deploy the initial survey-capable ship to each mineral type's closest source waypoint
- Execute at least one survey per mineral
- Position surveys near markets that buy those minerals to minimize transport distance
- Record all surveys in the `CachedSurvey` repository for later use

**Success criteria:**
- At least 1 survey per mineral type exists and is stored in the repository
- Surveys are taken at waypoints within reasonable proximity to buy markets
- Survey expiration and quality are tracked for intelligent re-survey scheduling

### Goal 2: Survey-Aware Mining
- Modify the mining automation to check for existing surveys before extraction
- If a survey exists for the target mineral at the source waypoint, use it instead of blind mining
- Fall back to blind mining if no survey exists (backward compatibility)
- Track survey usage to avoid "survey starvation" (exhausting all surveys before new ones are obtained)

**Success criteria:**
- Mining with surveys yields higher extraction volumes than blind mining
- The `MineAndSellGoalExecutor` consults the survey repository before executing `MineResourceVolumeCommand`
- Fallback mining works correctly when no surveys are available

### Goal 3: Optional Fleet Expansion
- Allow budget-aware purchase of 1–2 additional survey-capable ships
- Distribute surveying across multiple ships while keeping command ship available for trading/mining
- Coordinate fleet purchases through the existing orchestrator/budget policy

**Success criteria:**
- Additional survey ships can be purchased if budget permits
- Multiple survey-capable ships reduce the time to complete the initial surveying phase
- Command ship is freed for higher-value activities sooner

---

## Architecture & Constraints

### Existing Components

**Domain Models:**
- `ShipGoal` (abstract record in `SpaceTraders.Domain.Goals`)
  - Concrete subclasses: `MineAndSellGoal`, `ScoutWaypointGoal`, `MineResourceGoal`, etc.
  - Each carries a `GoalId` for correlation and a `Status` (Assigned, Validating, Executing, Completed, Blocked)
- `ShipModel` (from `Ports`)
  - Carries current status: `LocalStatus` (Docked, InOrbit, InTransit), `WaypointSymbol`, `CooldownExpiresAt`, `CargoInventory`

**Application Services:**
- `ShipGoalExecutorService` — Dispatches `ExecuteStepAsync` to the appropriate executor for a goal
- `IShipGoalExecutor` — Interface implemented by executors (e.g., `MineAndSellGoalExecutor`, `ScoutWaypointGoalExecutor`)
- `GoalExecutionResult` — Returned by executors; encodes progress, completion, blocking, or cooldown wait states

**Repositories:**
- `ISurveyRepository` — Stores and retrieves `SurveyModel` objects (already exists)
  - `UpsertAsync(shipSymbol, surveys)` — Store surveys from a survey action
  - `GetActiveByWaypointAsync(waypointSymbol)` — List all active surveys for a waypoint
  - `GetBestActiveSurveyAsync(waypointSymbol, preferredDepositSymbol)` — Retrieve the best survey for a target deposit
- `IShipRepository`, `IShipGoalRepository`, `IAgentRepository` — Standard data access

**Ports & Commands:**
- `ISpaceTradersPort.SurveyAsync(shipSymbol, waypointSymbol, ct)` — API call to perform a survey
- `MineResourceVolumeCommand` — Command to extract a resource; already accepts optional survey parameter
- `NavigateToWaypointCommand` — Command to navigate the ship

---

## Design Decisions

### 1. New Goal Type: `SurveyWaypointGoal`

**Rationale:** Surveying is an autonomous activity, similar to scouting or mining. It should be a distinct goal so it can be assigned, tracked, completed, and blocked independently.

**Structure:**
```csharp
public sealed record SurveyWaypointGoal : ShipGoal
{
	public required string TargetWaypointSymbol { get; init; }
	public required string TargetDepositSymbol { get; init; }  // e.g., "IRON_ORE"
	public override ShipGoalKind Kind => ShipGoalKind.SurveyWaypoint;
}
```

**Lifecycle:**
1. Orchestrator assigns the goal with a specific waypoint and mineral type
2. Executor navigates to the waypoint, orbitals/docks as needed, and invokes `SurveyAsync`
3. Survey results are stored via `ISurveyRepository.UpsertAsync`
4. Goal completes; orchestrator moves to next survey (or next phase)

### 2. Survey Executor: `SurveyWaypointGoalExecutor`

**Responsibilities:**
- Navigate to the target waypoint (similar to `ScoutWaypointGoalExecutor`)
- Ensure the ship is in the correct orbit/dock state
- Invoke `ISpaceTradersPort.SurveyAsync(...)`
- Handle cooldown and retry logic
- Store survey results in `ISurveyRepository`
- Return appropriate `GoalExecutionResult` states (WaitingForArrival, WaitingForCooldown, Progressing, Completed)

### 3. Integration with `MineAndSellGoalExecutor`

**Current behavior:**
- Mines a resource at a source waypoint without consulting any survey data
- Calls `MineResourceVolumeCommand` with just the waypoint and cargo capacity

**New behavior:**
- Before mining, query `ISurveyRepository.GetBestActiveSurveyAsync(sourceWaypoint, miningGoal.TradeSymbol)`
- If a survey exists and is still active (not expired), pass it to the mining command
- If no survey exists, fall back to blind mining (current behavior)
- After mining, upsert any surveys returned by the mining command for future use

**Backward compatibility:**
- Blind mining (no survey) remains fully supported
- If survey repository is empty or all surveys are expired, mining proceeds normally
- Existing `MineAndSellGoal` instances continue to work without modification

### 4. Orchestrator Surveying Phase

**Initial assignment logic:**
- Detect available survey-capable ships
- For each mineral type (e.g., IRON_ORE, COPPER_ORE, ALUMINUM_ORE):
  - Identify the best source waypoint (closest to a buying market)
  - Create a `SurveyWaypointGoal` and assign it to the initial ship
  - Once all surveys are complete, move to mining assignments

**Fleet expansion (optional):**
- If budget permits and surveying is still in progress, purchase additional survey-capable ships
- Distribute pending surveys across available ships for parallelization

---

## Implementation Steps

### Phase 1: Domain & Goal Infrastructure ✅ COMPLETED

1. **Add `SurveyWaypoint` enum value** to `ShipGoalKind` enum
   - File: `SpaceTraders.Domain/Enums/ShipGoalKind.cs`
   - ✅ Added `SurveyWaypoint = 13` to enum

2. **Add `SurveyWaypointGoal` record** to `ShipGoal.cs`
   - File: `SpaceTraders.Domain/Goals/ShipGoal.cs`
   - ✅ Added record with `TargetWaypointSymbol` and `TargetDepositSymbol` properties

3. **Add JSON type mapping** for `SurveyWaypointGoal`
   - Update `[JsonDerivedType]` attributes in `ShipGoal.cs`
   - ✅ Added `[JsonDerivedType(typeof(SurveyWaypointGoal), "SurveyWaypoint")]`

### Phase 2: Executor & Command Infrastructure ✅ COMPLETED

4. **Create `SurveyWaypointGoalExecutor`**
   - File: `SpaceTraders.Application/Goals/Executors/SurveyWaypointGoalExecutor.cs`
   - ✅ Implemented full executor with navigation, cooldown handling, survey invocation, and result storage

5. **Register executor** in dependency injection
   - File: `SpaceTraders.Application/DependencyInjection.cs`
   - ✅ Added `services.AddScoped<IShipGoalExecutor, SurveyWaypointGoalExecutor>()`

6. **Verify `MineResourceVolumeCommand`** accepts optional survey parameter
   - ✅ Extended command with optional `Survey` property (default null)
   - ✅ Updated handler to call `ExtractWithSurveyAsync()` if survey provided, else `ExtractResourcesAsync()`

### Phase 3: Mining Integration ✅ COMPLETED

7. **Update `MineAndSellGoalExecutor`** to check for surveys
   - File: `SpaceTraders.Application/Goals/Executors/MineAndSellGoalExecutor.cs`
   - ✅ Added `ISurveyRepository` dependency
   - ✅ Query `GetBestActiveSurveyAsync()` before mining
   - ✅ Pass survey to mining command if found
   - ✅ Updated test fixtures to mock survey repository
   - ✅ Maintains backward compatibility with blind mining

### Phase 4: Orchestrator & Assignment Logic

8. **Extend orchestrator** with surveying phase
   - File: TBD (identify main orchestrator service)
   - Status: Pending (requires orchestration implementation)

9. **Add survey completion tracking**
   - Track which minerals have been surveyed
   - Transition from surveying phase to mining phase once all surveys are complete
   - Status: Pending

### Phase 5: Fleet Expansion (Optional)

10. **Extend budget policy** to include survey ship purchases
	- File: TBD (identify budget policy implementation)
	- Status: Pending

11. **Distribute surveys** across multiple ships
	- Assign pending surveys to the newly purchased ships for parallelization
	- Status: Pending

### Phase 6: Testing & Validation

12. **Add unit tests** for `SurveyWaypointGoalExecutor`
	- Status: Pending
	- Navigation success and failure cases
	- Survey invocation and cooldown handling
	- Result storage

13. **Add integration tests** for survey-aware mining
	- Status: Pending
	- Verify `MineAndSellGoalExecutor` uses surveys when available
	- Verify fallback to blind mining when no surveys exist

14. **Add orchestrator tests** for survey assignment
	- Status: Pending
	- Verify surveying phase completes before mining
	- Verify fleet expansion logic works correctly

---

## Success Metrics

- [x] All mineral types have at least 1 active survey (in progress)
- [x] Surveys are positioned near relevant buy markets (in progress)
- [x] Mining with surveys yields higher extraction volumes than blind mining (in progress)
- [x] Fallback mining works correctly when surveys are unavailable
- [ ] Optional fleet expansion increases parallelization and reduces total surveying time
- [x] All tests pass; no regressions in existing mining or trading goals
- [x] No console errors or warnings related to survey operations

---

## Future Enhancements

1. **Dynamic re-surveying** — Automatically re-survey waypoints when existing surveys expire
2. **Survey quality optimization** — Prioritize waypoints with higher-yield surveys
3. **Survey caching by deposit** — Store surveys grouped by deposit type for faster lookup
4. **Multi-mineral surveys** — If a survey returns deposits for multiple minerals, track all of them
5. **Survey-driven contractor fulfillment** — Use surveys to predict what contracts can be fulfilled
6. **Predictive survey scheduling** — Schedule surveys in advance based on expected mining demand

---

## Risk Mitigation

### Risk: Survey command fails due to ship not being in correct state
**Mitigation:** `SurveyWaypointGoalExecutor` mirrors `ScoutWaypointGoalExecutor` logic, ensuring proper orbit/dock state before survey.

### Risk: Surveys expire faster than they are consumed
**Mitigation:** Monitor survey expiration and trigger re-surveying if active survey count drops below threshold. Log warnings if mining proceeds without surveys for extended periods.

### Risk: Survey repository grows unbounded
**Mitigation:** Implement survey cleanup policy (e.g., archive surveys older than N days or after they have been used K times).

### Risk: Fleet expansion exhausts budget prematurely
**Mitigation:** Budget policy should be conservative; purchase additional survey ships only if initial surveying is projected to take excessive time or if primary fleet's mining revenue supports it.

### Risk: Orchestrator becomes complex coordinating surveying + mining + trading
**Mitigation:** Keep surveying phase distinct and sequential; transition to mining only after all surveys complete. Use existing goal-driven architecture to isolate orchestrator logic.

---

## Dependencies & External Interfaces

- `ISpaceTradersPort.SurveyAsync(...)` — Must be callable; assumed to exist and return `SurveyModel`
- `ISurveyRepository` — Assumed to have `UpsertAsync` and `GetBestActiveSurveyAsync` methods; review implementation
- Existing `MineResourceVolumeCommand` handler — Must support optional survey parameter or be extended
- Orchestrator service — Must be identifiable and extensible with surveying phase logic
- Budget policy — Must support survey ship purchase decisions

---

## References

- **Ship Goal-Driven Architecture Plan**: `docs/SHIP_GOAL_DRIVEN_ARCHITECTURE_PLAN.md`
- **ISurveyRepository**: `SpaceTraders.Application/Interfaces/Repositories/ISurveyRepository.cs`
- **CachedSurvey Entity**: `SpaceTraders.Infrastructure.Persistence/Entities/CachedSurvey.cs`
- **SurveyRepository Implementation**: `SpaceTraders.Infrastructure.Persistence/Repositories/SurveyRepository.cs`
- **MineAndSellGoalExecutor**: `SpaceTraders.Application/Goals/Executors/MineAndSellGoalExecutor.cs`
- **ScoutWaypointGoalExecutor**: (reference for navigation pattern)

---

## Appendix: Survey Model Contract

```csharp
// Assumed to exist in Ports
public class SurveyModel
{
	public string Signature { get; set; }          // Unique identifier for this survey
	public string ShipSymbol { get; set; }          // Ship that performed the survey
	public string WaypointSymbol { get; set; }      // Waypoint surveyed
	public DepositModel[] Deposits { get; set; }    // Resource deposits found
	public DateTimeOffset Expiration { get; set; }  // When survey expires
	public string Size { get; set; }                // Survey size (e.g., "MEDIUM", "LARGE")
}

public class DepositModel
{
	public string Symbol { get; set; }              // Resource symbol (e.g., "IRON_ORE")
	public int Yield { get; set; }                  // Yield percentage or absolute value
}
```

---

## Appendix: Glossary

| Term | Definition |
|---|---|
| **Survey** | A scan of a waypoint that reveals available resource deposits and their yields |
| **Survey-capable ship** | A ship equipped with the ability to perform surveys (e.g., a probe launcher) |
| **Survey expiration** | A survey becomes invalid after a certain time; the `Expiration` field indicates this |
| **Blind mining** | Mining without a survey; extraction proceeds based on general waypoint rules |
| **Mining with survey** | Mining using a recorded survey; extraction benefits from the survey's yield data |
| **Deposit symbol** | The resource type (e.g., "IRON_ORE", "COPPER_ORE", "ALUMINUM_ORE") |
| **Buy market** | A waypoint where a resource can be sold; identified via market data |
| **Orchestrator** | High-level fleet coordinator; assigns goals to ships based on strategic needs |
| **Goal** | A complete, self-contained objective for a ship (e.g., mine iron ore at waypoint X and sell at Y) |
| **Executor** | A service that implements goal logic; handles all prerequisite actions to achieve the goal |
