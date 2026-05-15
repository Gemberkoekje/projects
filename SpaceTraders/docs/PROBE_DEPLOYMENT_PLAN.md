# Probe Satellite Deployment Plan

## Overview

Deploy Probe Satellites (`SHIP_PROBE`) to every market and shipyard waypoint across all known systems. A docked probe at a waypoint keeps market prices and shipyard inventory continuously fresh, enabling better trading and fleet expansion decisions.

---

## Starting Assumptions

These are typical starting conditions, not hardcoded values. The system adapts automatically to actual agent state at runtime.

- **Agent**: Starting credits vary per run (~170–180K is typical)
- **Headquarters**: Derived at runtime from agent data; determines the home system
- **Active Probes**: Any probes already docked at a waypoint are treated as already deployed and skipped
- **Probe Cost**: ~23,000 credits each (varies per shipyard)
- **Probe Power**: Solar reactor — zero fuel capacity; must use DRIFT flight mode
- **Navigation**: All probes operate in DRIFT mode (fuel-free, solar-powered)

### Typical Home System Layout (example only)

The home system typically contains a handful of shipyards and markets. Exact waypoints are discovered at runtime from cached waypoint data. The following is an example from a known run and is used only for integration testing:

| Example Waypoint | Type | Has Market | Role |
|---|---|---|---|
| e.g. X1-PT96-H53 | MOON | ✅ | First shipyard — probe may already be present |
| e.g. X1-PT96-A2 | MOON | ✅ | Second shipyard |
| e.g. X1-PT96-C39 | ORBITAL_STATION | ✅ | Third shipyard |

In production, waypoints are never referenced by symbol. The plan selects targets based on their properties: `HasShipyard`, `HasMarket`, and whether a probe is already docked there.

---

## Budget & Phasing Strategy

Probe cost and available credits differ per run. All thresholds below are expressed as rules, not hardcoded amounts.

### Phase 0: Bootstrap (IMMEDIATE)
- **Goal**: Cover every shipyard in the home system with a probe
- **Cost**: N probes × probe cost, where N = uncovered shipyards in the home system
- **Reserve check**: Only proceed if remaining credits stay above the safe reserve (100K) after purchase
- **Gate**: Must achieve +25K revenue within 7 days to proceed to Phase 1

### Phase 1: Foundation (After reaching 200K+ capital)
- **Goal**: Cover all known markets in the home system
- **Cost**: Remaining uncovered market waypoints × probe cost
- **Gate**: Only proceed if Phase 0 ROI validated

### Phase 2: Expansion (After reaching 1M+ capital)
- **Goal**: Deploy to secondary waypoints and adjacent systems
- **Reserve**: Maintain 100K+ minimum at all times
- **Gate**: Capital must support the reserve buffer after all planned purchases

### Phase 3: Saturation (Long-term)
- **Goal**: Complete network coverage as sustainable revenue allows
- **Cadence**: Continue until all known waypoints are covered; re-evaluate quarterly

---

## Financial Safeguards

- **Critical Floor**: 50,000 credits — halt all spending below this
- **Safe Operating Reserve**: 100,000 credits — preferred minimum before any new purchase
- **Per-Phase Allocation**: Max 25% of available capital to probe deployment per phase
- **Bankruptcy Prevention**: If balance drops below the critical floor, stop all deployments
- **Profitability Gate**: Each phase must show positive ROI before the next phase starts

---

## Deployment Priority

The `ProbeDeploymentPlanService` selects targets in the following order, derived from cached waypoint data:

1. **CRITICAL**: All shipyards in the home system that do not yet have a probe docked
2. **HIGH**: All remaining markets in the home system without a probe
3. **MEDIUM**: Waypoints in adjacent or secondary systems
4. **LOW**: Newly discovered waypoints added as the map expands

No waypoint symbol is hardcoded. Selection happens at runtime by querying `IWaypointRepository.GetBySystemAsync` and filtering on `HasShipyard` / `HasMarket`.

---

## Technical Implementation

### DRIFT Mode Navigation

**Challenge**: Probes have zero fuel capacity and cannot refuel.

**Solution**: Set flight mode to `DRIFT` before or upon arrival, allowing solar-powered navigation with no fuel cost.

#### Command Sequence

```
1. ProbeDeploymentPlanService selects next uncovered target from cached waypoints
2. DeployProbeCommand published → DeployProbeGoal assigned to an available probe
3. DeployProbeGoalExecutor navigates probe to target (DRIFT mode set if not already)
4. On arrival: probe docks at waypoint
5. PatchShipNavCommand("DRIFT") confirms flight mode
6. ProbeDeploymentPlanService.AdvanceAsync marks waypoint as covered
7. Next target dispatched automatically
```

#### Key Implementation Points

- **Target selection**: `IWaypointRepository.GetBySystemAsync(homeSystem)` filtered by `HasMarket || HasShipyard`
- **Available probe selection**: Any `SHIP_PROBE` that is not in transit and not already at a deployed waypoint
- **DRIFT activation**: `PatchShipNavCommand` issued by `DeployProbeGoalExecutor` when the probe reaches its target and docks; also set pre-departure if the probe has zero fuel capacity
- **Plan persistence**: `ProbeDeploymentPlanState` stores target and deployed waypoint lists in the database; survives restarts
- **Target reconciliation**: on each `EnsureBootstrappedAsync` tick, existing plans are reconciled with current cached waypoints in the home system; any newly discovered market/shipyard waypoints are appended and plan status is recomputed
- **Game loop integration**: `ProbeDeploymentPlanService.EnsureBootstrappedAsync` is called every 5 seconds by `GameLoopService`; idempotent — creates the plan once, then reconciles and resumes any pending dispatches

---

## Implementation Status

### Completed
- [x] `DeployProbeGoal` and `DeployProbeGoalExecutor` — navigate, dock, set DRIFT, advance plan
- [x] `DeployProbeCommand` — assigns goal to probe and triggers first execution step
- [x] `PatchShipNavCommand` — sets flight mode via `ISpaceTradersPort.PatchShipNavAsync`
- [x] `ProbeDeploymentPlanService` — bootstraps plan from live waypoint data, dispatches probes, marks progress
  - [x] Shipyard-first (Phase 0) ordering — shipyard waypoints dispatched before market-only waypoints
  - [x] Phase 1 capital gate — market-only targets deferred until credits ≥ 200K
  - [x] Multi-probe dispatch — all idle probes dispatched in a single resume pass
  - [x] Inline probe purchase — when no probe is available, buys one via `ISpaceTradersPort.PurchaseShipAsync`, persists the new ship, and immediately dispatches with `DeployProbeCommand`
  - [x] Budget guard on purchase — `IBudgetPolicy.EvaluateAsync` checked before any inline purchase
  - [x] Purchase skipped when no selling shipyard is cached — retries automatically on next tick
- [x] `ProbeDeploymentPlanState` — persisted plan with target and deployed waypoint lists; nullable `ShipyardWaypointSymbols` for backward compatibility with older persisted plans
- [x] `PurchaseShipCommand` — generic reusable ship-purchase command (available for other fleet-expansion flows)
- [x] `GameLoopService` integration — `EnsureBootstrappedAsync` called every tick
- [x] Unit tests (`ProbeDeploymentPlanServiceTests`) — 19 passing tests covering bootstrap, resumption, phase ordering, multi-dispatch, inline purchase fallback, budget denial, in-transit probe exclusion, and already-deployed probe exclusion

### Pending
- [ ] Frigate trading revenue to fund probe purchases (Phase 0 may stall if starting credits are insufficient after the reserve check)
- [ ] Phase 1 auto-trigger — currently gated only on capital threshold; no explicit ROI measurement or 7-day gate logic implemented yet
- [ ] Phase 2 / Phase 3 expansion — deployment beyond the home system not yet implemented; `SelectNextTargetAsync` only queries the headquarters system
- [ ] Probe purchase for market-only Phase 1 targets — `TryPurchaseProbeAsync` is called for any target, but the Phase 1 capital gate must already be satisfied before `SelectNextTargetAsync` returns a market-only target; the interaction is correct but not separately tested
- [x] Newly discovered waypoints — existing plans are reconciled against cached home-system waypoints each tick; newly discovered market/shipyard waypoints are appended to targets and completed plans are automatically re-opened when new coverage is required

---

## Risk Mitigation

| Risk | Mitigation |
|---|---|
| Credits drop below reserve | Budget check before each probe purchase; halt if below 100K preferred reserve |
| No available probe to dispatch | Plan pauses dispatch; `EnsureBootstrappedAsync` retries every 5 s |
| DRIFT mode not set before navigation | `DeployProbeGoalExecutor` checks `FuelCapacity == 0` and issues `PatchShipNavCommand` pre-departure |
| Waypoint data not yet cached | Bootstrap skips if `GetBySystemAsync` returns no results; retries next tick |
| Plan state lost on restart | `ProbeDeploymentPlanState` is persisted; `EnsureBootstrappedAsync` reloads and resumes |

---

## Success Metrics

### Phase 0
- [ ] All shipyards in the home system have a probe docked
- [ ] DRIFT mode confirmed on every deployed probe (`FlightMode == "DRIFT"`)
- [ ] Market data freshness: < 1 hour at all covered shipyards
- [ ] Revenue impact: +25K credits within 7 days (gate to Phase 1)

### Ongoing
- [ ] Capital maintained above 50K critical floor at all times
- [ ] Capital maintained above 100K preferred reserve
- [ ] All known market and shipyard waypoints eventually covered
- [ ] Probe uptime: 100% docked at assigned waypoint
- [ ] Revenue growth: month-over-month positive trajectory

---

## Conclusion

The deployment is fully driven by runtime data. No waypoint symbol, system name, or probe count is hardcoded in production code. The plan bootstraps from whatever the agent's headquarters system contains, deploys probes to all markets and shipyards within budget, and scales to adjacent systems as capital allows.

**Core Principle**: Probes are a runtime investment decided by actual agent state, not a fixed script. Deploy to whatever waypoints exist, measure ROI, and scale conservatively.
