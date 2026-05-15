# Contract Plan Implementation Plan

## Objective
Add a new **Contract plan** next to the existing scout plan. This plan automates mineral contract fulfillment end-to-end with clear stop behavior for unsupported contracts.

## Phase 1 Scope
- Handle accepted, active contracts with pending **mineral** deliverables.
- If deliverable is not mineral-based, stop/defer this plan.
- Prefer an idle mining-capable ship.
- If no idle miner exists and budget permits, buy `SHIP_MINING_DRONE`.
- Add mine flow with explicit target mineral, source waypoint, and required amount.
- Add fulfill flow to navigate and deliver full/partial cargo.

## Functional Requirements

### Contract API Lifecycle and Caching
1. Use contract **list/get endpoints** to load current contract state.
2. Cache contract results and avoid repeated calls in the same execution cycle.
3. Contract list/get should be called once per refresh cycle, then read from cache/repository.
4. If no current contract exists, use **Negotiate Contract** to request a new contract.
5. For a negotiated or pending contract, call **Accept Contract (POST)** before execution.
6. During execution, use **Deliver Cargo To Contract (POST)** for each delivery step.
7. Only when all deliverables are fully satisfied, call **Fulfill Contract (POST)**.

### Contract Selection
1. Load accepted, unfulfilled contracts from cached repository state.
2. Select pending deliverable by nearest deadline.
3. Validate trade symbol as mineral.
4. If non-mineral, mark plan deferred and stop.

### Ship Allocation
1. Find free ship with mining capability.
2. If none, evaluate reserve/budget.
3. If affordable, purchase `SHIP_MINING_DRONE` from known shipyard.
4. Bind selected/purchased ship to contract plan.

### Mine Command
Fields:
- `ShipSymbol`
- `TradeSymbol`
- `SourceWaypoint`
- `RequiredUnitsTotal`

Behavior:
1. Navigate to asteroid.
2. Ensure in-orbit.
3. Extract.
4. Wait cooldown.
5. Extract repeatedly until cargo full or trip target met.
6. Jettison non-target cargo when needed.
7. Never demand more in one trip than cargo can hold.

### Fulfill Contract Command
Behavior:
1. Navigate to destination waypoint.
2. Dock.
3. Deliver target mineral units from cargo using Deliver Cargo To Contract POST.
4. Support partial delivery.
5. Repeat mine/deliver loop until deliverable fulfilled.
6. When all deliverables are complete, call Fulfill Contract POST.

## Architecture Changes

### Plan State
Add `ContractMineralPlanState` persisted via `IPlanRepository`:
- `PlanId`
- `ContractId`
- `ShipSymbol`
- `TradeSymbol`
- `SourceWaypoint`
- `DestinationWaypoint`
- `UnitsRequired`
- `UnitsFulfilled`
- `Status` (`None`, `Active`, `PendingBudget`, `DeferredUnsupported`, `Completed`)
- `CreatedAt`, `UpdatedAt`

### Plan Service
Add `IContractPlanService`:
- bootstrap/resume
- choose deliverable
- mineral classification
- ship selection/purchase trigger
- progress updates and completion
- contract lifecycle operations (negotiate/accept/deliver/fulfill) coordinated via cached state

### Commands
Add:
- `MineResourceVolumeCommand`
- `FulfillContractDeliveryCommand`

Reuse existing navigation/orbit/dock/refuel/cooldown scheduling subcommand flow.

## Execution Flow
1. Refresh contracts once from list/get endpoint and persist to cache.
2. If no contract exists, negotiate a new contract.
3. Accept contract if not accepted.
4. Evaluate contract + deliverable.
5. Validate mineral support.
6. Acquire/assign miner ship.
7. Compute remaining units.
8. Clamp per-trip target to cargo capacity.
9. Mine loop at source.
10. Navigate and deliver at destination.
11. Update plan progress.
12. If all deliverables are fulfilled, call Fulfill Contract POST.
13. Complete or repeat.

## Safeguards
- No asteroid source: plan blocked with reason.
- State mismatch: publish mismatch event and retry.
- Transient failures: idempotent upsert + retry policy.
- Duplicate assignment guard per active plan/ship.
- Avoid contract API overuse by reading from cache after initial refresh.

## Tests

### Unit
- Contract list/get is called once per refresh cycle and then cached.
- If no contract exists, negotiate contract is triggered.
- Contract accept POST is called when contract is not accepted.
- Mineral contract starts plan.
- Non-mineral contract defers plan.
- Idle miner preferred.
- Miner purchase requested when needed and affordable.
- Mine target clamped by cargo.
- Non-target cargo is discarded.
- Partial delivery works through Deliver Cargo To Contract POST.
- Fulfill Contract POST is called only when all deliverables are complete.
- Plan completes when fulfilled.

### Integration
- End-to-end with existing miner.
- End-to-end with purchased miner.
- Budget insufficient path stays pending.
- Restart resumes persisted plan correctly.
- End-to-end contract lifecycle: negotiate -> accept -> deliver (one or more) -> fulfill.

## Milestones
1. Contract lifecycle cache + list/get-once behavior.
2. Negotiate/accept integration.
3. State + repository + mineral classification.
4. Ship selection + purchase integration.
5. Mine command loop.
6. Fulfill command loop.
7. End-to-end orchestration + telemetry + tests.

## Out of Scope (Now)
- Non-mineral fulfillment strategies.
- Multi-ship parallel contract execution.
- Advanced cross-system optimization.
