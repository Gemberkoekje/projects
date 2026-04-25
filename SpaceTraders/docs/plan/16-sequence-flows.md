# 16 – Sequence Flows

Key runtime interaction sequences illustrated as Mermaid diagrams.

_See [`../GLOSSARY.md`](../GLOSSARY.md) for term definitions._

---

## 16.1 Agent Bootstrap

The flow that runs on cold start when no agent token is present in the database.

```mermaid
sequenceDiagram
    participant App as SpaceTraders.API<br/>(startup)
    participant ABS as AgentBootstrapService
    participant DB as PostgreSQL
    participant ATP as IAgentTokenProvider
    participant ST as SpaceTraders.io API

    App->>ABS: StartAsync()
    ABS->>DB: SELECT stored_credentials WHERE key = 'AgentToken'
    DB-->>ABS: (empty)

    ABS->>ST: POST /register { symbol, faction, token: AccountToken }
    ST-->>ABS: 201 { agentToken, agent, ships, contract }

    ABS->>DB: INSERT stored_credentials (AgentToken)
    ABS->>DB: UPSERT cached agent / ships / contract
    ABS->>ATP: Set(agentToken)

    Note over ABS: AccountToken is consumed from configuration.<br/>AgentToken is now in memory and DB.

    ABS->>App: Bootstrap complete
```

**Notes:**
- If a token _is_ found in the DB, `AgentBootstrapService` skips `POST /register` and calls
  `IAgentTokenProvider.Set(...)` directly from the persisted value.
- The `AccountToken` is never written to the DB — it passes through memory only.
- See [`14-security.md §14.5`](14-security.md) for token exposure prevention details.

---

## 16.2 Trade Cycle

A single ship completing one full buy-navigate-sell loop and receiving its next assignment.

```mermaid
sequenceDiagram
    participant GLS as GameLoopService
    participant SWS as ShipWorkerService
    participant MB as Wolverine IMessageBus
    participant AL as Application Layer
    participant RC as Rate-Limited ST API Client
    participant DB as PostgreSQL
    participant ST as SpaceTraders.io API

    GLS->>MB: Publish AssignShipCommand(shipId)
    MB->>AL: AssignShipHandler
    AL->>DB: SELECT best TradeOpportunity for ship
    DB-->>AL: opportunity (buyWaypoint, sellWaypoint, tradeGood)
    AL->>DB: INSERT ShipAssignmentRecord (Trade, step=0)
    AL->>MB: Publish ShipAssignedEvent

    MB->>SWS: ShipAssignedHandler → begin Trade state machine

    Note over SWS: Step 1 – Navigate to buy waypoint
    SWS->>RC: POST /my/ships/{id}/navigate { waypointSymbol: buyWaypoint }
    RC->>ST: POST /navigate
    ST-->>RC: 200 { nav: { arrival, status: IN_TRANSIT } }
    RC-->>SWS: response
    SWS->>DB: UPDATE ship (status=IN_TRANSIT, arrivesAt=arrival)
    SWS->>DB: UPDATE ShipAssignmentRecord (step=1)

    Note over GLS: Dead-reckoning tick detects arrival
    GLS->>MB: Publish ShipArrivedAtWaypointEvent(shipId)
    MB->>SWS: resume Trade state machine at step 1

    Note over SWS: Step 2 – Dock & buy cargo
    SWS->>RC: POST /my/ships/{id}/dock
    RC->>ST: POST /dock
    ST-->>RC: 200 { nav: { status: DOCKED } }
    SWS->>RC: POST /my/ships/{id}/purchase { symbol, units }
    RC->>ST: POST /purchase
    ST-->>RC: 200 { agent, cargo, transaction }
    SWS->>DB: UPDATE agent (credits), UPDATE ship cargo
    SWS->>DB: UPDATE ShipAssignmentRecord (step=2)

    Note over SWS: Step 3 – Navigate to sell waypoint (same pattern as step 1)

    Note over SWS: Step 4 – Dock & sell cargo
    SWS->>RC: POST /my/ships/{id}/sell { symbol, units }
    RC->>ST: POST /sell
    ST-->>RC: 200 { agent, cargo, transaction }
    SWS->>DB: UPDATE agent (credits), UPDATE ship cargo
    SWS->>MB: Publish CargoPurchasedEvent / CargoSoldEvent

    MB->>AL: AssignShipAfterSaleHandler
    AL->>DB: SELECT next best TradeOpportunity
    AL->>MB: Publish AssignShipCommand(shipId)

    Note over SWS,GLS: Loop restarts from top
```

**Notes:**
- Every `POST` response is applied directly to the cache — no follow-up GET is ever issued
  (see [ADR-003](15-adr.md#adr-003-no-get-after-post-caching-rule)).
- `ShipAssignmentRecord.StepIndex` is updated after each step so a pod restart can resume
  from the exact correct point (see [`10-error-handling.md §10.3`](10-error-handling.md)).
- The rate-limited client transparently queues requests so all ships share the 2 req/s budget.

---

## 16.3 Pod Restart Recovery

What happens when the Kubernetes pod is killed mid-cycle and restarted.

```mermaid
sequenceDiagram
    participant App as SpaceTraders.API<br/>(restart)
    participant ABS as AgentBootstrapService
    participant GLS as GameLoopService
    participant DB as PostgreSQL
    participant MB as Wolverine IMessageBus

    App->>ABS: StartAsync() — token found in DB, skip registration
    ABS->>DB: LOAD IAgentTokenProvider from stored_credentials

    App->>GLS: StartAsync()
    GLS->>DB: SELECT all ShipAssignmentRecords WHERE active = true
    GLS->>DB: SELECT all ships WHERE status = IN_TRANSIT AND arrivesAt <= NOW()

    loop each arrived ship
        GLS->>MB: Publish ShipArrivedAtWaypointEvent(shipId)
    end

    loop each active assignment
        GLS->>MB: Publish ResumeAssignmentCommand(shipId, stepIndex)
    end

    Note over GLS: Normal operation resumes.<br/>No extra GETs needed — arrival<br/>was persisted before the crash.
```

**Notes:**
- Ships that are still in transit have their `arrivesAt` persisted, so the dead-reckoning tick
  will detect their arrival correctly after restart — no API call needed.
- Ships that arrived while the pod was down are detected on the first tick after restart.
- See [`10-error-handling.md §10.3`](10-error-handling.md) for the full stranded-ship edge case.

---

## See Also

- [`00-overview.md`](00-overview.md) – High-level architecture overview
- [`15-adr.md`](15-adr.md) – Rationale behind the design choices shown above
- [`05-automation-engine.md`](05-automation-engine.md) – Automation engine state machine detail
- [`10-error-handling.md`](10-error-handling.md) – Error and recovery scenarios
