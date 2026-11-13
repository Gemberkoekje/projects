# Tick bench

Times one tick's route work and plan passes on a copy of the day's world (`PLAN.md` slice 6.60, B88, D146): the measure the
route work of slice 6.61 is held to.

Two parts:
- `copy_world.py` copies the cluster's database into a throwaway local Postgres, read-only on the cluster (the
  `spacetraders_ro` login of `tools/investigate`, with the session read-only too). It never copies the agent token:
  `stored_credentials` is left out entirely. The rows of `activity_logs`, `startup_snapshots` and `api_endpoint_usages`,
  which no plan reads, are left out too. It also saves the game's production chains (public, no token) to a file.
- `TickBench` (`tests/SpaceTraders.API.Tests/Bench/`) starts the API host on that copy with nothing reaching the game (a
  stub port that only serves the chains) and no background service (no game loop). It times:
  - reading each trader's reach map (`ITradeContextReader.ReadReachAsync`), and on it every trader's
    `TradeRoutePlanner.Rank` and `Judge`, which write nothing;
  - the trade list (slice 6.63, D148), built once as the background worker builds it, when the copy's home gate is built;
  - each plan's pass, in the game loop's order and each in its own scope, as `GameLoopService` runs them. These write to
    the copy as they would to the cluster's database, so copy it again for a run that starts from the same world. With a
    trade list built, the trading plan and the role board work out their routes over it.
- `TradeListBench` (same folder, slice 6.63) writes nothing. It ranks each trader's routes over every trade, then over trade
  lists of several sizes. For each size it gives how often the trader's best route stays the same, how much of its rate the
  list's pick loses, and how long ranking and judging take on maps read afresh.

## Run

From the `SpaceTraders` folder, with Docker running and the password file of `tools/investigate/README.md`:

```bash
python tools/bench/copy_world.py --out ../../bench
```

It prints the two settings the bench reads. Then:

```bash
SPACETRADERS_BENCH_DB="Host=127.0.0.1;Port=55433;Database=spacetraders;Username=postgres" \
SPACETRADERS_BENCH_SUPPLY_CHAIN="../../bench/supply-chain.json" \
dotnet test tests/SpaceTraders.API.Tests --filter "FullyQualifiedName~TickBench" --logger "console;verbosity=detailed"
```

`FullyQualifiedName~TradeListBench` runs the list bench instead; it writes nothing, so it needs no fresh copy.

Without `SPACETRADERS_BENCH_DB` the bench is skipped, as it is in CI.

The copy is the container `spacetraders-bench-pg`, on loopback port 55433 (the soak test's is 55432). Every run of
`copy_world.py` replaces it; remove it with `docker rm -f spacetraders-bench-pg`.
