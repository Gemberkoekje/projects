# Trading Automation Plan

## Overview

Add a reactive trading loop that turns market imbalances into trade-and-sell goals.

When a market scan indicates that a market **buys** a non-mineral good with **SCARCE** supply, and another market **sells** that same good with **ABUNDANT** supply, the system should create a trading goal to buy the good at the abundant market and sell it at the scarce market.

---

## Goal Definition

### Trigger Conditions

Create (or refresh) a trading goal when all conditions are true:

1. A market has been scanned
2. The scanned market **buys** a non-mineral resource
3. The resource supply at the scanned market is `SCARCE`
4. Another known market **sells** the same resource
5. The source market supply for that resource is `ABUNDANT`

### Goal Objective

- Buy the flagged non-mineral resource at the source market with `ABUNDANT` supply
- Transport it to the destination market with `SCARCE` supply
- Sell it at the destination market
- Keep the goal active until the opportunity is no longer valid or replaced by better rules

### Cancellation Rule

Cancel the trading goal when either of these conditions becomes true on market update:

- The destination sell market is no longer `SCARCE` for that resource
- The source buy market is no longer `ABUNDANT` for that resource

---

## Trade Ship Assignment & Capacity Rules

### Idle Trade Ship Assignment

- If an idle trade ship exists, assign the goal immediately
- Reuse existing navigation command flow for ship movement
- Only add the missing buy/sell orchestration and goal-to-command wiring

### Purchase Fallback

If no idle trade ship exists:

- Attempt to buy a trade ship only when:
  - credits are sufficient under budget policy, and
  - purchase rules allow expanding the trade fleet
- If purchase is not possible, keep the goal pending/idle and retry on future ticks/events

### Trade Ship Capacity Configuration

If a trade-fleet cap is introduced or already exists, respect it during purchase decisions. Otherwise, rely on existing budget and purchase policy rules for the initial implementation.

---

## Technical Stitching Scope

Existing ship navigation command support is assumed to be present.

Implementation should focus on wiring:

1. **Market scan events -> trading goal creation/update**
2. **Cross-market lookup -> source market selection for matching `ABUNDANT` supply**
3. **Trading goal -> trade ship assignment logic**
4. **Assigned goal -> buy, navigate, and sell command sequence**
5. **No trade ship available -> optional purchase path -> immediate assignment if purchase succeeds**
6. **Cannot purchase -> keep goal queued until ship/credits become available**
7. **Market updates -> goal cancellation when sell or buy supply conditions are invalidated**

No new low-level navigation command behavior is required. Buying and selling orchestration should be added only as needed to connect the goal flow.

---

## Suggested Flow

1. Market scan processed
2. Filter imports where `Type` is non-mineral and `Supply == SCARCE`
3. Find another market that sells the same resource with `Supply == ABUNDANT`
4. Upsert trading goal keyed by `(ResourceSymbol, BuyWaypointSymbol, SellWaypointSymbol)`
5. Try assign an idle trade ship
6. If none idle, evaluate purchase policy
7. If purchased, persist ship and assign goal
8. Execute buy at source market
9. Navigate to destination market using existing ship navigation support
10. Execute sell at destination market
11. On market update, cancel the goal if source supply is no longer `ABUNDANT` or destination supply is no longer `SCARCE`
12. On completion or re-evaluation, keep/requeue/close goal based on latest market data

---

## Budget & Safety Rules

- Always run ship purchase through budget policy before buying
- Do not block main loop when no ship is available; goals remain pending
- Keep handlers idempotent so repeated scans/ticks are safe
- Avoid duplicate goals for the same resource and route
- Prefer stable matching rules so repeated market scans do not churn assignments unnecessarily

---

## Data & State Considerations

Track enough state to avoid duplicate churn:

- Active/pending trading goals by resource + source market + destination market
- Assigned ship (if any)
- Last source and destination market freshness markers
- Goal status (`Pending`, `Assigned`, `Executing`, `Completed`, `Dormant`, `Cancelled`)
- Selected source market used for the route

---

## Acceptance Criteria

- [ ] SCARCE non-mineral buy opportunities from scanned markets create trading goals when an ABUNDANT source market exists
- [ ] Idle trade ships are assigned automatically
- [ ] If no idle trade ship exists, purchase is attempted when budget policy allows
- [ ] If purchase cannot proceed, the goal stays pending and retries later
- [ ] Existing navigation command pipeline is reused for movement
- [ ] Trading orchestration stitches buy, navigate, and sell behavior without introducing a second navigation flow
- [ ] Trading goals are cancelled when destination sell supply is updated away from `SCARCE`
- [ ] Trading goals are cancelled when source buy supply is updated away from `ABUNDANT`
- [ ] End-to-end flow is covered by unit tests for: trigger, route match, assign, buy fallback, pending retry, and cancellation

---

## Initial Implementation Status

### Implemented
- [x] Event-to-goal wiring for scanned market opportunities
- [x] Cross-market source lookup for `ABUNDANT` supply
- [x] Goal dispatcher integration with trade ships
- [x] Buy/navigate/sell goal orchestration using existing navigation support and direct trade port actions
- [x] Purchase fallback integration for missing idle trade ships
- [x] Goal cancellation on market updates that invalidate source or destination supply conditions
- [x] Retry hooks when ships become idle or credits change
- [x] Persist explicit queued/pending trading opportunities separate from ship-attached active goals
- [x] Automated tests for trigger, assign, buy fallback, pending defer, cancellation, queued persistence, retry transition behavior, executor behavior, and goal persistence query support

### Remaining
- [ ] Surface queued trading goals through read models or UI endpoints for observability
- [ ] Add profit/ranking logic when multiple `ABUNDANT` source markets are available for the same destination
- [ ] Evaluate whether trade-ship capacity or fleet-cap configuration should be formalized

### Out of Scope (Initial)
- [ ] Complex profit ranking across many possible source markets
- [ ] Multi-hop or speculative trade chains
- [ ] Dynamic fleet sizing strategy for trade ships
- [ ] Route optimization beyond existing navigation behavior

---

## Conclusion

This plan adds a lightweight trading orchestration layer: react to SCARCE non-mineral demand, locate an ABUNDANT source market, create trading goals, assign available trade ships, optionally buy more ships when policy allows, cancel goals when buy/sell market conditions are invalidated, and defer safely when ships or credits are constrained. The current implementation covers active-goal creation, executor wiring, purchase fallback, cancellation, and retry triggers, while richer route selection and explicit queued-goal persistence remain for later increments.
