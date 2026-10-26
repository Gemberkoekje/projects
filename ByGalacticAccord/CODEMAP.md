# By Galactic Accord — code map

What is where, what calls what, and where money and goods move. Use it to find the file, then read the
file: the code is the truth. **Last checked against the code: 2026-10-26, after S1.**

**Keep it true.** A commit that adds, removes, renames or moves a file or a type, changes who calls a
public member, adds or removes an event, or changes a money or goods path updates this map in the same
commit. If you find the map wrong, fix it. Section 11 has the commands that regenerate the "used by" facts.

## 1. Orientation

- **What the build is:** the v0.2 hauling slice, slimmed by S1. Consumers post Haul contracts, AI haulers
  take them, carry goods from producers and settle through escrow. No client and no player in any run
  today (`WorldGenerator.Generate(..., withPlayer: false)` everywhere).
- **Where it is going:** Slice 1 of the design, in the steps of [`plan.md`](plan.md) (S2, the Blazor client,
  is next). Most game logic changes shape in S3–S8; plan §1 lists what is kept, reworked and retired.
- **Documents:** design [`../by_galactic_accord_gdd_v0.3.md`](../by_galactic_accord_gdd_v0.3.md) ·
  plan [`plan.md`](plan.md) · step logs [`log/`](log/README.md) · humans' overview [`README.md`](README.md) ·
  agent rules [`CLAUDE.md`](CLAUDE.md).
- **Section numbers in code comments:** a bare `§n` cites the **v0.2** design (deleted; it is in git history
  at `844c8307:by_galactic_accord_gdd_v0.2.md`). Comments written since S1 say "design §n" and cite v0.3.
  Section 10 translates.

## 2. Projects

| Project | Path | References | Role |
|---|---|---|---|
| Engine | `src/ByGalacticAccord.Engine/` | nothing (BCL only) | The whole simulation. No UI, no I/O, deterministic. |
| Soak harness | `src/ByGalacticAccord.Cli/` | Engine | Console app: headless runs, telemetry, invariant checks. |
| Tests | `tests/ByGalacticAccord.Tests/` | Engine, xUnit | Fast by default; `[Trait("Category", "Soak")]` for long runs. |

- `ByGalacticAccord.slnx` lists the three. `Directory.Build.props`: net10.0, nullable on, **implicit usings off**
  (every file has explicit `using`s), **warnings are errors**.
- The test project's `VSTestTestCaseFilter` is `Category!=Soak` unless `--filter` is given.
- CI: [`../.github/workflows/ci-bygalacticaccord.yml`](../.github/workflows/ci-bygalacticaccord.yml) runs on every
  push and pull request that touches `ByGalacticAccord/` (or the workflow): restore, Release build, fast tests,
  soak tests, on Ubuntu.

## 3. How a run flows

```
WorldGenerator.Generate(seed, withPlayer, config)          Simulation/WorldGenerator.cs
  └─ new SimulationContext(seed, config)                    Simulation/SimulationContext.cs
     adds 7 Locations, 9 Routes, 8 producers, 7 consumers, 1 government, 7 haulers (+ the player if asked)
     schedules: ActorHeartbeat per actor (random first tick), InflationCheck, ReputationDecay
     sets FaucetMultiplier = 0.6, CaptureInitialCredits()

SimulationContext.RunTo(targetTick)
  └─ loop: EventQueue.Dequeue() → CurrentTick = event.Tick → event.Process(context)
     stops early with RunStop.PlayerAlert if a player alert was raised (never in headless runs)
```

**Events** (all implement `ISimEvent`: `Tick`, `Describe`, `Process`) and what each schedules:

| Event | File | Does | Schedules |
|---|---|---|---|
| `ActorHeartbeat` | `Events/ControlEvents.cs` | Non-player actors: `DecisionEngine.RunProducer` / `RunConsumer` / `RunHauler` by role. **Skips the player.** | Itself, `HeartbeatInterval` (24) later |
| `ContractExpiryCheck` | `Events/ControlEvents.cs` | Open or negotiating contract → `MarkExpired` | — |
| `InflationCheck` | `Events/ControlEvents.cs` | `InflationMonitor.Evaluate` | Itself, every 400 |
| `ReputationDecay` | `Events/ControlEvents.cs` | `ReputationLedger.DecayTowardNeutral` | Itself, every 400 |
| `ShipDeparted` | `Events/TransitEvents.cs` | `Settlement.Pickup` | `TransitIncidentCheck` at mid-route |
| `TransitIncidentCheck` | `Events/TransitEvents.cs` | `Rng.Chance(scale × danger × ship modifier)` | `TransitIncidentOccurred`, or `ShipArrived` |
| `TransitIncidentOccurred` | `Events/TransitEvents.cs` | Applies the `TransitIncident` (damage, loss, cost, delay); alerts the player if theirs | `ShipArrived` (plus any delay) |
| `ShipArrived` | `Events/TransitEvents.cs` | Perishable decay, `Ship.Arrive`, docking fee, `Scout` the destination | → `Settlement.Fulfill` or `Settlement.Breach` |

**A haul's life:** `RunConsumer` → `TryPostDemand` creates a `Contract` (Haul), `context.Add`, schedules
`ContractExpiryCheck` at the deadline → a hauler's `RunHauler` → `BestCandidate` (reads
`Visibility.OpenContractsVisibleTo`, `FindPath`, `Score`) → `Commit` (`Contract.Activate`, escrow and bond
locks, `Ship.Reserve`, schedules `ShipDeparted` after any repositioning) → `ShipDeparted` →
`TransitIncidentCheck` → (`TransitIncidentOccurred` →) `ShipArrived` → `Settlement.Fulfill` / `Breach`.

## 4. Where state lives: `SimulationContext`

`Simulation/SimulationContext.cs` (483 lines) is the world and the hub; nearly everything takes it.

- **Collections:** `Actors`, `Ships`, `Locations`, `Contracts` (dictionaries by typed id; contracts are never
  removed), `Routes` (list). Registered with the `Add(...)` overloads; `Actor(id)`, `Ship(id)`,
  `Location(id)`, `Contract(id)` look up.
- **Ordered views (use these where order matters):** `ActorsOrdered` (by id), `OpenContracts` (open or
  negotiating, by id; a pruned list, because a contract never returns to Open).
- **Graph:** `FindPath(from, to)` → `RoutePlan?` (Dijkstra in `SolvePath`, cached per pair; `Add(Location)` and
  `Add(Route)` clear the cache), `RouteBetween(a, b)`, `Neighbours(id)`.
- **Services it owns:** `Rng` (`DeterministicRandom`), `Queue` (`EventQueue`), `Journal` (`EventJournal`, capped
  at 5,000 entries), `Reputation` (`ReputationLedger`), `Config` (`SimulationConfig`).
- **Bookkeeping the invariants read:** `InitialCredits`, `TotalFaucet`, `TotalSink`, `EscrowLockedTotal` /
  `ReleasedTotal` / `ForfeitedTotal`, `GovernmentRevenue`, `CargoPickedUp` / `Delivered` / `Lost`, counters
  `ContractsFulfilled` / `Breached`, `IncidentsOccurred`, and `FaucetMultiplier` (the inflation monitor's knob).
- **Other:** `ProducerAt(location, good)` (first resident producer of that good), scouting
  (`Scout`, `HasFreshScout`), player alerts (`RaisePlayerAlert`, `HasPlayerAlerts`), `Record` (journal line).

## 5. Money and goods: the only paths

Every credit and unit moves through these `SimulationContext` primitives. Anything else is a bug.

| Primitive | Effect | Called from |
|---|---|---|
| `ApplyFaucet` | Creates credits | `DecisionEngine.RunConsumer` (income × `FaucetMultiplier`) |
| `ApplySink` | Destroys credits, **capped at the actor's balance** (nobody goes below ₵0) | `RunProducer` (production cost), `RunHauler` (running costs), `ShipArrived` (docking fee), `TransitIncidentOccurred` (credit-cost incident) |
| `TaxFromEscrow`, `TaxDirect` | Destroys credits as tax, books `GovernmentRevenue` | `Settlement.Fulfill` |
| `TryTransfer` | Moves credits between actors | `Settlement.Pickup` and `Fulfill` on the frontier |
| `LockToEscrow` | Actor → a contract's escrow | `DecisionEngine.Commit`, `PlayerActions.AcceptHaul`: consumer locks goods cost + fee + tax; hauler locks a bond (≤ its credits) |
| `ReleaseFromEscrow`, `ForfeitFromEscrow` | Escrow → an actor, capped at what the contract holds | `Settlement.Pickup` (goods cost to the producer), `Fulfill`, `Breach` |
| `NotePickup`, `NoteDelivered`, `NoteLost` | Goods bookkeeping | `Pickup`; `Fulfill`; `TransitIncidentOccurred` and frontier default in `Fulfill` |

Goods sit in `ActorState` stock (`AddStock`, `RemoveStock` floors at 0) or in a ship's `Manifest` (one `Cargo`).
They move producer stock → `Settlement.Pickup` → manifest → consumer stock in `Settlement.Fulfill`.
Jurisdiction decides the branch: `Contract.UsesEscrow` (deep Accord and Accord edge) or the frontier
(no escrow; the pilot fronts the goods cost and the consumer pays at handover, or defaults).

## 6. File by file

### Engine: `Domain/` (value objects and aggregates)

| File | Contents | Used by |
|---|---|---|
| `Ids.cs` | `ActorId`, `ShipId`, `LocationId`, `ContractId`: long-backed record structs from counters in `SimulationContext` | everywhere |
| `Credits.cs` | `Credits(decimal)`: arithmetic, comparison, `ToString` "₡1,234" | everywhere money is |
| `Goods.cs` | `GoodType` (7), `GoodCategory`, `GoodInfo`, `Goods.Info` table (base value, perishable, decay per tick) | `Cargo`, `PriceModel`, `DecisionEngine`, `Settlement`, `Reputation` |
| `Cargo.cs` | `Cargo(good, quantity, quality)`: `Fresh`, `Decayed(ticks, preservation)`, `LosingUnits`, `Damaged` | `Ship`, `TransitEvents`, `Settlement` |
| `Location.cs` | `AccordReach` (jurisdiction), `LocationType`, `Location` (region, reach, residents) | `WorldGenerator`, `Visibility`, `DecisionEngine` |
| `Route.cs` | `Route` (two ends, hours, danger 0..1, law), `RoutePlan` (distance, worst leg's danger, hops, path), `DangerBand` | `SimulationContext.FindPath`, `DecisionEngine`, `TransitEvents`, `PlayerActions` |
| `Contract.cs` | `ContractType` (only `Haul` is created), `ContractStatus`, `Contract`: parties, good, quantity, origin, destination, offered fee, the poster's hidden `BuyerReservationFee`, goods cost, tax rate, deadline, jurisdiction, clauses, escrow held, locked bond; transitions `BeginNegotiation`, `Activate`, `MarkFulfilled`/`Breached`/`Expired` | `DecisionEngine`, `Settlement`, events, `Visibility`, `Invariants` |
| `Clause.cs` | `ContractClause` base; `PenaltyOnLateDelivery` (per tick late, capped; the cap is the bond); `QualityWarranty` (refund fraction below a minimum quality) | `Contract`, `DecisionEngine.TryPostDemand`, `Settlement.Fulfill` |
| `Ship.cs` | `ShipStatus` (Docked, InTransit), `Ship`: type, owner, location, capacity, `ActiveContract`, `Manifest`, `DepartedTick`, a text log; `Reserve`, `Depart`, `Arrive`, `Unload` | `DecisionEngine`, `TransitEvents`, `Settlement`, `Invariants` |
| `ShipType.cs` | `ShipType` (5 hulls), `ShipClassInfo` (capacity, speed, running cost per tick, incident modifiers, preservation), `ShipClasses.Info` table | `Ship`, `PriceModel`, `IncidentResolver` |
| `Personality.cs` | `Personality` (risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, concession rate) | `ActorState`, `DecisionEngine`, `PriceModel`, `WorldGenerator` |
| `Reputation.cs` | `ReputationContext` (Accord standing, region, good category, counterparty), `ReputationLedger` (scores −1..1, decay toward 0) | `Settlement` (writes), `Visibility` (reads region) |

### Engine: `Actors/`

| File | Contents | Used by |
|---|---|---|
| `ActorState.cs` | One model for every actor: credits, stock, ships, home, personality, `Producer` profile, consumer `Demands`, `IncomePerHeartbeat`, `IsPlayer`, `MovesCargo` | everywhere |
| `ActorRole.cs` | `ActorRole`: Producer, Consumer, ShippingCompany, IndependentPilot, Government in use; Factory and the rest named, never created | `ActorState`, `WorldGenerator` |
| `Profiles.cs` | `ProducerProfile` (good, output, unit cost, cap, margin), `ConsumerDemand` (good, rate, target stock) | `ActorState`, `PriceModel`, `DecisionEngine`, `WorldGenerator` |
| `DecisionEngine.cs` | `RunProducer` (make goods up to cap, paying cost), `RunConsumer` (income, consume, post when below 60% of target), `TryPostDemand`, `RunHauler`, `BestCandidate`, `Score` (utility = profit × risk × growth − opportunity cost, over a liquidity threshold), `Commit` | `ActorHeartbeat` |
| `Negotiation.cs` | `Negotiation.ResolveAi` (closed form: deal if buyer max ≥ seller min, price split by concession rates); `NegotiationSession` (interactive rounds with tells) | `DecisionEngine.Score`; the session only by `NegotiationTests` |
| `PlayerActions.cs` | `CheckHaul`, `AcceptHaul`: the player's way to take a haul through the same steps as `Commit` | **no callers** since S1 (its clients are gone); S3 reworks it |

### Engine: `Economy/`

| File | Contents | Used by |
|---|---|---|
| `PriceModel.cs` | `ProducerAsk` (cost × margin × fullness), `ConsumerWillingness` (base value × shortage), `PilotMinimumFee`, `TravelTicks` (distance ÷ speed, rounded up) | `DecisionEngine`, `TransitEvents`, `PlayerActions` |
| `Visibility.cs` | `CanSee`: own contracts, home or adjacent location, home region, region reputation over 0.05, or a fresh scout; `OpenContractsVisibleTo` | `DecisionEngine.BestCandidate` |
| `InflationMonitor.cs` | `Evaluate`: drift of total credits beyond ±8% moves `FaucetMultiplier` by 7% | `InflationCheck` |

### Engine: `Events/`

| File | Contents | Used by |
|---|---|---|
| `ISimEvent.cs` | `ISimEvent`; `IPlayerPrompt` (**no implementations**) | events, `EventQueue`, `SimulationContext` |
| `EventQueue.cs` | Min-heap by tick, ties by insertion sequence | `SimulationContext` |
| `ControlEvents.cs` | `ActorHeartbeat`, `ContractExpiryCheck`, `InflationCheck`, `ReputationDecay` | scheduled by `WorldGenerator`, `DecisionEngine`, and themselves |
| `TransitEvents.cs` | `ShipDeparted`, `TransitIncidentCheck`, `TransitIncidentOccurred`, `ShipArrived` | `ShipDeparted` scheduled by `DecisionEngine.Commit`, `PlayerActions.AcceptHaul`; the rest by each other |

### Engine: `Random/` and `Simulation/`

| File | Contents | Used by |
|---|---|---|
| `Random/DeterministicRandom.cs` | xorshift128+ seeded by SplitMix64; `NextDecimal`, `NextLong`, `Chance`, `Pick`; counts `DrawCount` | `SimulationContext.Rng` → `WorldGenerator`, `TransitIncidentCheck`, `IncidentResolver` |
| `Simulation/SimulationContext.cs` | The world (section 4) | everything |
| `Simulation/SimulationConfig.cs` | Tuning knobs: heartbeat 24, inflation check 400 / ±8% / 7%, reputation decay, tax 4%, docking ₵3, incident scale, reputation gain and loss, scout freshness | `SimulationContext.Config` |
| `Simulation/WorldGenerator.cs` | The sector: locations, routes (hours and danger), producers, consumers, government, haulers, optional player; `WorldHandles` | `Soak`, tests |
| `Simulation/Settlement.cs` | `Pickup`, `Fulfill` (fee, warranty refund, late penalty from the bond, tax; frontier default), `Breach` (cargo lost: bond forfeited, consumer refunded); reputation changes | `TransitEvents` |
| `Simulation/IncidentResolver.cs` | `TransitIncidentKind`, `TransitIncident`, `Resolve` (roll → delay, damage, partial loss, credit cost, total loss; severity from the ship) | `TransitIncidentCheck`, `TransitIncidentTests` |
| `Simulation/Invariants.cs` | `Check`: credit conservation, escrow reconciliation and non-negative escrow, cargo conservation, no orphan active contracts; `InvariantViolation` | `Soak`, `InvariantTests` |
| `Simulation/EventJournal.cs` | `JournalEntry`, `EventJournal` (capped log, `TotalRecorded`) | `SimulationContext.Record` |
| `Simulation/GameTime.cs` | One tick = one game hour. `Day`, `HourOfDay`, `At`, `Days`, `FormatDate` ("Day 4 · 14:00"), `FormatDuration` ("1d 6h"), `FormatRelative` ("in 1d 6h") | `Soak`, `InvariantTests`, `GameTimeTests` |
| `Simulation/RunStop.cs` | Why `RunTo` returned: `ReachedTarget`, `Drained`, `PlayerAlert` | `SimulationContext.RunTo` |

### Soak harness: `src/ByGalacticAccord.Cli/`

| File | Contents |
|---|---|
| `Program.cs` | Commands `soak [ticks] [--seed N]` (default 200,000 ticks, seed 20260619) and `help` |
| `Soak.cs` | Runs in 5,000-tick chunks, `Invariants.Check` after each (exit code 1 on a violation), then prints telemetry |

### Tests: `tests/ByGalacticAccord.Tests/`

| File | Covers | Soak variants |
|---|---|---|
| `TestWorld.cs` | Helpers: `RunHeadless`, `Signature` (state fingerprint), `FastTicks` (20,000), the `Soak` trait constants | — |
| `DomainTests.cs` | `Credits`, `PenaltyOnLateDelivery`, `QualityWarranty`, `Cargo` decay, `FindPath` (multi-hop, unreachable, cache), `OpenContracts`, the good and hull tables | — |
| `NegotiationTests.cs` | `Negotiation.ResolveAi`, `NegotiationSession` | — |
| `TransitIncidentTests.cs` | `IncidentResolver`: deterministic, armour milder than courier, danger raises credit costs | — |
| `GameTimeTests.cs` | `GameTime` | — |
| `DeterminismTests.cs` | Same seed → same `Signature`; stepped replay; seeds diverge | 150k, 120k |
| `InvariantTests.cs` | `Invariants.Check` at checkpoints; credits, escrow, no negative balances | 1M, 250k |
| `EconomyTests.cs` | Economy comes alive; frontier reachable | 200k; money supply over 1M; no monopoly over 500k |

## 7. Determinism rules (design §20.1)

- Randomness comes only from `context.Rng`, and only inside events or world generation. Draws today:
  `WorldGenerator` (personalities, first heartbeats), `TransitIncidentCheck`, `IncidentResolver`.
- Anything that draws randomly or changes state in a loop iterates a stable order: `ActorsOrdered`,
  `OpenContracts`, lists in insertion order. A dictionary's enumeration order is used only where order
  cannot change the result (sums, counts, the invariant checks, reputation decay).
- `EventQueue` breaks tick ties by insertion order.
- Money is `decimal` throughout.
- Checked by `DeterminismTests` (fast and soak) and by the soak harness's identical telemetry across runs.

## 8. Known defects and where they live

From plan §1; the step named fixes each.

| Defect | Where | Step |
|---|---|---|
| The poster's hidden reservation fee leaks | `DecisionEngine.TryPostDemand`'s "Posted" journal line prints `≤{maxFee}` | S5 |
| The player never pays running costs | `ActorHeartbeat.Process` skips `IsPlayer`; running costs are charged in `RunHauler` | S3 |
| Nobody can go below ₵0 | `SimulationContext.ApplySink` caps at the balance; `Commit` bonds `min(bond, credits)` | S8 |
| `AcceptHaul` doesn't check the contract is still open | `PlayerActions.AcceptHaul` | S3 |
| Food decays below its warranty on any route | `Goods` (Food 0.012 a tick) against the 0.7 warranty in `TryPostDemand`: about 25 hours; the shortest route is 30 | S3 |
| Visibility is nearly omniscient | `Visibility.CanSee`'s home-region rule (Helios is 5 of 7 locations) | S6 |
| Incident outcomes don't shift with danger | `IncidentResolver.Resolve`: the roll isn't weighted by danger, despite its comment | S8 |

## 9. No callers (left from the retired clients, or waiting for a step)

`PlayerActions` (all of it); `NegotiationSession` (tests only); `IPlayerPrompt`; `ISimEvent.Describe` (never
called); `Ship.History`, `TransferOwnership`, `SetManifest`; `ActorState.RemoveShip`, `Owns`, `AllStock`;
`EventJournal.Entries`, `Tail`; `SimulationContext.Neighbours`, `PlayerAlerts`, `ClearPlayerAlerts`;
`ProducerProfile.IsIdle`; `DeterministicRandom.Pick`; `Location.HasEscrow`, `HasArbitration`; `Route.Band`,
`RoutePlan.Band`; `ShipClassInfo.Niche`, `MarketValue`, `RunsHot` (data only: push is not implemented);
`Contract.ExtendDeadline`; `ReputationLedger.Snapshot`; `SimulationConfig.NegotiationMaxRounds`;
`WorldGenerator.Generate(..., withPlayer: true)`. Delete or wire them as the steps rework their areas.

## 10. Design section numbers: v0.2 (old comments) → v0.3

| v0.2 | Topic | v0.3 |
|---|---|---|
| §1.1, §1.2 | Pillars; player experience | §1.1, §1.2 |
| §2.3 | The Galactic Accord | §2.1 |
| §3.3 | First session | §13 (and §11.4) |
| §3.4 | Time, speed, hard pause | §8.3 |
| §3.5 | Goals, failure, legacy | §10 |
| §3.6 | Ship progression, running costs | §6 (§6.5) |
| §4.1.1 | Contract data model | §3.1 |
| §4.1.2 | Contract lifecycle | §16.2 |
| §4.1.3 | Enforcement by jurisdiction | §3.5 |
| §4.1.5 | Clause schema | §3.4 |
| §4.1.6 | Negotiation | §3.6, §16.5 |
| §4.2.1, §4.2.2, §4.2.3 | Faucets and sinks; prices; production | §5.1, §5.2, §5.3 |
| §4.3 | Reputation | §9 |
| §4.4 | Bonding | later (Slice 2, §22) |
| §4.5, §4.5.1, §4.5.2, §4.5.3 | Actors; roles; personality; decisions | §4, §4.1, §4.2, §4.3 |
| §4.6 | Information and visibility | §7 |
| §4.7.1, §4.7.2, §4.7.3 | Locations; routes; the event engine | §8.1, §8.2, §19 |
| §5.4 | Simulation architecture | §19 |
| §5.5 | Determinism | §20.1 |
| §5.6 | Invariants and testing | §20.2, §20.3 |
| §5.7 | Interface | §11 |
| §5.8 | Inflation monitor | §5.5 |
| §6.1 | Slice scope | §14 |
| §6.3, §6.3.1 | Transit incidents; onboarding piracy | §8.4, §22.2 |
| §7.7 | Demand starvation | §5.6 |
| §7.8 | Bankruptcy | §5.4 |

## 11. Regenerating the "used by" facts

From `ByGalacticAccord/`:

```bash
# Every file that mentions a type or member (then read the hits: comments match too)
grep -rn --include=*.cs -w 'TypeName' src tests

# Members with no callers: no hits for ".Name" besides the definition
grep -rn --include=*.cs -E '\.MemberName\b' src tests

# Line counts, to judge what to read whole
find src tests -name '*.cs' -not -path '*/obj/*' | xargs wc -l
```
