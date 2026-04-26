# 13 – Performance & Optimisation

## Goals
- Keep the automation game loop responsive at scale (20+ ships, 100+ market rows).
- Minimise database round-trips; prefer in-memory reads where data is already cached.
- Provide guidance on tuning thresholds when fleet size grows.

---

## 13.1 Database Indexing Strategy

Apply these indexes in EF Core entity configuration or as explicit migration SQL.

| Table | Index | Rationale |
|-------|-------|-----------|
| `CachedShip` | `(NavStatus, ArrivesAt)` | `GameLoopService` filters by `IN_TRANSIT` + `ArrivesAt <= now` every 5 s |
| `CachedShip` | `(Symbol)` PK (already indexed) | – |
| `ShipPlanRecord` | `(ShipSymbol, Status)` | Startup recovery loads all active ship plans |
| `ShipPlanRecord` | `(Role, CargoSymbol, PlannedWaypoint)` | Price-change re-planning finds affected ships quickly |
| `CachedMarket` | `(WaypointSymbol, LastObservedAt)` | TTL check before dispatching `RefreshMarketDataCommand` |
| `TradeOpportunity` | `(ProfitPerJump DESC, ComputedAt)` | `GetBestTradeRouteQuery` sorts descending |
| `ActivityLog` | `(Timestamp DESC, ShipSymbol)` | Dashboard paging and ship-filter queries |
| `ActivityLog` | Partial on `Timestamp > NOW() - INTERVAL '30 days'` | Only recent rows are regularly queried |

```csharp
// Example: ActivityLog in SpaceTradersDbContext
modelBuilder.Entity<ActivityLog>()
    .HasIndex(e => new { e.Timestamp, e.ShipSymbol });
```

---

## 13.2 Query Optimisation Patterns

**Use `AsNoTracking()` for all read-only queries** (dashboard, status endpoints, query handlers):
```csharp
var ships = await _db.Ships.AsNoTracking().ToListAsync(ct);
```

**Avoid N+1 loads** – when loading ships with assignments, use a single join:
```csharp
var ships = await _db.Ships
    .AsNoTracking()
    .Include(s => s.Assignment)
    .ToListAsync(ct);
```

**Paginate the ActivityLog** – never load the full table:
```csharp
var logs = await _db.ActivityLog
    .AsNoTracking()
    .Where(l => shipFilter == null || l.ShipSymbol == shipFilter)
    .OrderByDescending(l => l.Timestamp)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync(ct);
```

---

## 13.3 Dead-Reckoning Tick Performance

The `GameLoopService` tight loop (every 5 s) runs:
```csharp
var transitShips = await _db.Ships
    .Where(s => s.ArrivesAt != null && s.ArrivesAt <= DateTimeOffset.UtcNow)
    .ToListAsync(ct);
```

With the `(NavStatus, ArrivesAt)` index this query is O(arrived ships), not O(all ships).
Expected execution time: < 5 ms for fleets up to 50 ships.

---

## 13.4 TradeOpportunity Recomputation and Re-planning

`TradeOpportunityRecomputeHandler` runs after every `MarketDataRefreshedEvent`. At scale this can
be triggered frequently. Guard against redundant recomputes:

- Use a `Debounce`: if a recompute was triggered within the last 30 s, skip and schedule one
  30 s from now instead.
- Recompute is pure in-memory (reads from cache, writes to `TradeOpportunity` table) – no external
  HTTP calls, so the compute itself is cheap.
- Emit `MarketPricesChangedEvent` only for actionable price, supply, activity, or trade-volume changes.
- Query affected `ShipPlanRecord` rows by role, planned waypoint, and cargo symbol before inspecting JSON details.
- Re-plan in memory first, then persist only plans that materially change.

---

## 13.5 Credit History Sparkline (In-Memory Buffer)

The dashboard credit sparkline is stored in a singleton circular buffer – never written to the DB:

```csharp
public sealed class CreditHistoryBuffer
{
    private readonly long[] _credits  = new long[360];  // 1 h at 10 s intervals
    private readonly long[] _timestamps = new long[360];
    private int _head = 0;

    public void Record(long credits) { /* overwrite oldest */ }
    public IReadOnlyList<(DateTimeOffset, long)> GetHistory() { /* snapshot */ }
}
```

This avoids writing 6 rows/minute to the database for purely presentational data.

---

## 13.6 Fleet Size Scaling Expectations

| Fleet size | DB rows (Ships + ShipPlans + ActivityLog/day) | Expected game loop tick time |
|-----------|--------------------------------------------------|------------------------------|
| 5 ships   | ~5 + ~5 + ~720 | < 2 ms |
| 20 ships  | ~20 + ~20 + ~2 880 | < 5 ms |
| 50 ships  | ~50 + ~50 + ~7 200 | < 15 ms |

ActivityLog grows at roughly 1–2 rows per ship per assignment cycle. At 20 ships cycling every
~10 min that is ~144 rows/hour. The 30-day retention policy keeps the table under ~100 k rows.

---

## 13.7 Rate Limiter Tuning

If the `PriorityChannel.DroppedLowPriorityCount` counter is non-zero, the system is producing
work faster than the 2 req/s limit can drain it. Options:

1. **Reduce fleet size** – fewer ships → fewer commands.
2. **Increase `Trade.MaxHaulDistance`** – longer routes → ships spend more time in transit, fewer commands.
3. **Reduce `Scout.MarketRefreshIntervalMinutes`** minimum – scout less often.
4. **Prioritise** – ensure Critical requests (refuel, contract) always make it through; Low priority
   (scout, cache refresh) can be dropped without breaking automation.

---

## 13.8 Related Documents

- `02-rate-limiter.md` – token bucket implementation and queue sizing
- `03-persistence.md` – entity model and activity log retention
- `09-milestones.md` – Phase 6 adds Prometheus metrics for measuring these values at runtime
