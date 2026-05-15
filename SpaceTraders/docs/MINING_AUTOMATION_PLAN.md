# Mining Automation Plan

## Overview

Add a reactive mining loop that turns high-value market signals into mining/sell goals.

When a market scan indicates a mineral import with **SCARCE** supply, the system should create a mining goal for that specific resource and sell it at that same market waypoint.

---

## Goal Definition

### Trigger Conditions

Create (or refresh) a mining goal when all conditions are true:

1. A market has been scanned
2. The scanned market **buys** a mineral resource
3. The mineral's supply at that market is `SCARCE`

### Goal Objective

- Mine the flagged mineral resource
- Deliver and sell at the triggering market waypoint
- Keep the goal active until the opportunity is no longer valid or replaced by better rules

---

## Drone Assignment & Capacity Rules

### Idle Drone Assignment

- If an idle mining drone exists, assign the goal immediately
- Reuse existing mining execution command flow for ship actions

### Purchase Fallback

If no idle mining drone exists:

- Attempt to buy a mining drone only when:
  - credits are sufficient under budget policy, and
  - current mining drone count is below configured maximum
- If purchase is not possible, keep the goal pending/idle and retry on future ticks/events

### Max Mining Drone Config

Add a configuration item:

- `MaxMiningDrones = 20` (initial default)

Rationale: mining value typically decreases later in the game compared to trading, so fleet growth should be capped.

---

## Technical Stitching Scope

Existing mining ship command support is assumed to be present.

Implementation should focus on wiring:

1. **Market scan events -> mining goal creation/update**
2. **Mining goal -> ship assignment logic**
3. **Assigned goal -> existing mining/sell commands**
4. **No drone available -> optional purchase path -> immediate assignment if purchase succeeds**
5. **Cannot purchase -> keep goal queued until ship/credits become available**

No new low-level mining command behavior is required unless gaps are discovered.

---

## Suggested Flow

1. Market scan processed
2. Filter imports where `Type` is mineral and `Supply == SCARCE`
3. Upsert mining goal keyed by `(ResourceSymbol, SellWaypointSymbol)`
4. Try assign an idle mining drone
5. If none idle, evaluate purchase policy and max-drone cap
6. If purchased, persist ship and assign goal
7. Execute existing mining/sell command sequence
8. On completion or re-evaluation, keep/requeue/close goal based on latest market data

---

## Budget & Safety Rules

- Always run purchase through budget policy before buying
- Never exceed `MaxMiningDrones`
- Do not block main loop when no ship is available; goals remain pending
- Keep handlers idempotent so repeated scans/ticks are safe

---

## Data & State Considerations

Track enough state to avoid duplicate churn:

- Active/pending mining goals by resource + destination market
- Assigned ship (if any)
- Last scan timestamp or freshness marker
- Goal status (`Pending`, `Assigned`, `Executing`, `Completed`, `Dormant`)

---

## Acceptance Criteria

- [ ] SCARCE mineral buy opportunities from scanned markets create mining goals
- [ ] Idle mining drones are assigned automatically
- [ ] If no idle drone exists, purchase is attempted when budget and cap allow
- [ ] `MaxMiningDrones` config exists and defaults to `20`
- [ ] If purchase cannot proceed, goal stays pending and retries later
- [ ] Existing mining command pipeline is reused via goal orchestration
- [ ] End-to-end flow is covered by unit tests for: trigger, assign, buy fallback, pending retry, cap enforcement

---

## Initial Implementation Status

### Planned
- [ ] Event-to-goal wiring for scanned market opportunities
- [ ] Goal dispatcher integration with mining drones
- [ ] Purchase fallback integration for missing idle drones
- [ ] Configuration support for `MaxMiningDrones`
- [ ] Automated tests for orchestration behavior

### Out of Scope (Initial)
- [ ] Multi-market optimization/ranking across many simultaneous SCARCE minerals
- [ ] Dynamic cap tuning by game phase
- [ ] Profit-per-minute route optimization beyond current command behavior

---

## Conclusion

This plan adds a lightweight orchestration layer: react to SCARCE mineral demand, create mining goals, assign available drones, optionally buy more up to a configurable cap, and defer safely when resources are constrained. The approach intentionally reuses existing mining commands and focuses only on event/goal/assignment stitching.
