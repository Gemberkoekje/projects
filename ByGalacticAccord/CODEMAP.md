# By Galactic Accord — code map

What is where, what calls what, and where money and goods move. Use it to find the file, then read the
file: the code is the truth. **Last checked against the code: 2026-10-26, after S2.**

**Keep it true.** A commit that adds, removes, renames or moves a file or a type, changes who calls a
public member, adds or removes an event, or changes a money or goods path updates this map in the same
commit. If you find the map wrong, fix it. Section 12 has the commands that regenerate the "used by" facts.

## 1. Orientation

- **What the build is:** the v0.2 hauling slice, slimmed by S1, with the browser client of S2 on top.
  Consumers post Haul contracts, AI haulers take them, carry goods from producers and settle through
  escrow. Headless runs (soak harness, most tests) have no player; the client's `GameSession` generates
  the world with the player (`withPlayer: true`), who can take a haul as posted.
- **Where it is going:** Slice 1 of the design, in the steps of [`plan.md`](plan.md) (S3, possession, Sale
  and Haul, is next). Most game logic changes shape in S3–S8; plan §1 lists what is kept, reworked and retired.
- **Documents:** design [`../by_galactic_accord_gdd_v0.3.md`](../by_galactic_accord_gdd_v0.3.md) ·
  plan [`plan.md`](plan.md) · step logs [`log/`](log/README.md) · humans' overview [`README.md`](README.md) ·
  agent rules [`CLAUDE.md`](CLAUDE.md).
- **Section numbers in code comments:** a bare `§n` cites the **v0.2** design (deleted; it is in git history
  at `844c8307:by_galactic_accord_gdd_v0.2.md`). Comments written since S1 say "design §n" and cite v0.3.
  Section 11 translates.

## 2. Projects

| Project | Path | References | Role |
|---|---|---|---|
| Engine | `src/ByGalacticAccord.Engine/` | nothing (BCL only) | The whole simulation. No UI, no I/O, deterministic. |
| Presentation | `src/ByGalacticAccord.Presentation/` | Engine | What the interface shows, as data: the text templates, the view models, the game clock. No Razor, no browser. |
| Client | `src/ByGalacticAccord.Client/` | Presentation, Blazor WebAssembly | The browser client: renders the view models, runs the clock's frames. |
| Soak harness | `src/ByGalacticAccord.Cli/` | Engine | Console app: headless runs, telemetry, invariant checks, state fingerprints. |
| Tests | `tests/ByGalacticAccord.Tests/` | Engine, Presentation, xUnit | Fast by default; `[Trait("Category", "Soak")]` for long runs. |
| Browser check | `tools/client-check/` | Node, Playwright (`package.json`) | Publishes the client, drives it in Chromium, screenshots it, compares WebAssembly with desktop. |

- `ByGalacticAccord.slnx` lists the five .NET projects. `Directory.Build.props`: net10.0, nullable on, **implicit usings off**
  (every file has explicit `using`s), **warnings are errors**.
- The test project's `VSTestTestCaseFilter` is `Category!=Soak` unless `--filter` is given.
- The client sets `InvariantGlobalization` (every number is formatted invariantly, so no ICU data ships) and
  pins its Blazor packages at 10.0.14. Its fonts (IBM Plex Sans and Mono, Latin, OFL) are vendored in
  `wwwroot/fonts/`, so nothing loads from outside the site.
- CI: [`../.github/workflows/ci-bygalacticaccord.yml`](../.github/workflows/ci-bygalacticaccord.yml) runs on every
  push and pull request that touches `ByGalacticAccord/` (or the workflow): restore, Release build, fast tests,
  soak tests, then the browser check (`npm ci`, `npx playwright install --with-deps chromium`, `node check.cjs`),
  on Ubuntu, and uploads the check's screenshots.

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

In the client (section 10 has the frame loop), `GameSession.Advance` calls `RunTo` with whole ticks as real
time passes, and stops the clock on `RunStop.PlayerAlert`.

**Events** (all implement `ISimEvent`: `Tick`, `Describe`, `Process`) and what each schedules:

| Event | File | Does | Schedules |
|---|---|---|---|
| `ActorHeartbeat` | `Events/ControlEvents.cs` | Non-player actors: `DecisionEngine.RunProducer` / `RunConsumer` / `RunHauler` by role. **Skips the player.** | Itself, `HeartbeatInterval` (24) later |
| `ContractExpiryCheck` | `Events/ControlEvents.cs` | Open or negotiating contract → `MarkExpired` | — |
| `InflationCheck` | `Events/ControlEvents.cs` | `InflationMonitor.Evaluate` | Itself, every 400 |
| `ReputationDecay` | `Events/ControlEvents.cs` | `ReputationLedger.DecayTowardNeutral` | Itself, every 400 |
| `ShipDeparted` | `Events/TransitEvents.cs` | `Settlement.Pickup` (the ship's `Location` becomes the pickup), then `Ship.SetCourse` for the laden leg | `TransitIncidentCheck` at mid-route |
| `TransitIncidentCheck` | `Events/TransitEvents.cs` | `Rng.Chance(scale × danger × ship modifier)` | `TransitIncidentOccurred`, or `ShipArrived` |
| `TransitIncidentOccurred` | `Events/TransitEvents.cs` | Applies the `TransitIncident` (damage, loss, cost, delay), `Ship.DelayArrival`, records an `IncidentEntry`; raises it as a player alert if the ship is theirs | `ShipArrived` (plus any delay) |
| `ShipArrived` | `Events/TransitEvents.cs` | Perishable decay, `Ship.Arrive` (clears the course), docking fee, `Scout` the destination | → `Settlement.Fulfill` or `Settlement.Breach` |

**A haul's life:** `RunConsumer` → `TryPostDemand` creates a `Contract` (Haul), `context.Add`, schedules
`ContractExpiryCheck` at the deadline, records a `ContractPostedEntry` → a hauler's `RunHauler` →
`BestCandidate` (reads `Visibility.OpenContractsVisibleTo`, `FindPath`, `Score`) → `Commit` (`Contract.Activate`,
escrow and bond locks, `Ship.Reserve` with the repositioning course from `RepositioningCourse`, schedules
`ShipDeparted` after any repositioning, records a `HaulTakenEntry`) → `ShipDeparted` → `TransitIncidentCheck` →
(`TransitIncidentOccurred` →) `ShipArrived` → `Settlement.Fulfill` / `Breach`. The player's way in is
`PlayerActions.CheckHaul` and `AcceptHaul`, the same steps as `Commit`, called by `GameSession.TakeHaul`.

**The journal** (`Simulation/EventJournal.cs`) records typed entries, not prose. Who records what:

| Entry | Recorded by |
|---|---|
| `ContractPostedEntry` (poster, supplier) | `DecisionEngine.TryPostDemand` |
| `HaulTakenEntry` (hauler, fee) | `DecisionEngine.Commit`, `PlayerActions.AcceptHaul` |
| `HaulDeliveredEntry` (quantity, quality, ticks late, fee, late fee, refund) | `Settlement.Fulfill` |
| `PaymentDefaultedEntry` (payer, hauler) | `Settlement.Fulfill` (frontier default) |
| `DeliveryFailedEntry` (hauler) | `Settlement.Breach` |
| `ContractExpiredEntry` | `ContractExpiryCheck` |
| `IncidentEntry` (hauler, the `TransitIncident`) | `TransitIncidentOccurred`; also the player's alert |
| `FaucetAdjustedEntry` (drift, multiplier) | `InflationMonitor.Evaluate`; never shown in the client |

`Presentation/Text/JournalText.cs` words each one from `Templates.resx`, keyed by the entry type's name
(without "Entry"), with a `.You` twin for the player's own events.

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
  (`Scout`, `HasFreshScout`), player alerts (`RaisePlayerAlert(JournalEntry)`, `HasPlayerAlerts`, `PlayerAlerts`,
  `ClearPlayerAlerts`: an alert is the entry that needs the player), `Record(JournalEntry)`.

## 5. Money and goods: the only paths

Every credit and unit moves through these `SimulationContext` primitives. Anything else is a bug.

| Primitive | Effect | Called from |
|---|---|---|
| `ApplyFaucet` | Creates credits | `DecisionEngine.RunConsumer` (income × `FaucetMultiplier`) |
| `ApplySink` | Destroys credits, **capped at the actor's balance** (nobody goes below ₵0) | `RunProducer` (production cost), `RunHauler` (running costs), `ShipArrived` (docking fee), `TransitIncidentOccurred` (credit-cost incident) |
| `TaxFromEscrow`, `TaxDirect` | Destroys credits as tax, books `GovernmentRevenue` | `Settlement.Fulfill` |
| `TryTransfer` | Moves credits between actors | `Settlement.Pickup` and `Fulfill` on the frontier |
| `LockToEscrow` | Actor → a contract's escrow | `DecisionEngine.Commit`, `PlayerActions.AcceptHaul` (from `GameSession.TakeHaul`): consumer locks goods cost + fee + tax; hauler locks a bond (≤ its credits) |
| `ReleaseFromEscrow`, `ForfeitFromEscrow` | Escrow → an actor, capped at what the contract holds | `Settlement.Pickup` (goods cost to the producer), `Fulfill`, `Breach` |
| `NotePickup`, `NoteDelivered`, `NoteLost` | Goods bookkeeping | `Pickup`; `Fulfill`; `TransitIncidentOccurred` and frontier default in `Fulfill` |

Goods sit in `ActorState` stock (`AddStock`, `RemoveStock` floors at 0) or in a ship's `Manifest` (one `Cargo`).
They move producer stock → `Settlement.Pickup` → manifest → consumer stock in `Settlement.Fulfill`.
Jurisdiction decides the branch: `Contract.UsesEscrow` (deep Accord and Accord edge) or the frontier
(no escrow; the pilot pays the producer the goods cost at pickup and the consumer pays only the fee at
handover, or defaults: the pilot is never paid back for the goods, see section 8).

## 6. File by file

### Engine: `Domain/` (value objects and aggregates)

| File | Contents | Used by |
|---|---|---|
| `Ids.cs` | `ActorId`, `ShipId`, `LocationId`, `ContractId`: long-backed record structs from counters in `SimulationContext` | everywhere |
| `Credits.cs` | `Credits(decimal)`: arithmetic, comparison, `ToString` "₡1,234" | everywhere money is |
| `Goods.cs` | `GoodType` (7), `GoodCategory`, `GoodInfo`, `Goods.Info` table (base value, perishable, decay per tick) | `Cargo`, `PriceModel`, `DecisionEngine`, `Settlement`, `Reputation` |
| `Cargo.cs` | `Cargo(good, quantity, quality)`: `Fresh`, `Decayed(ticks, preservation)`, `LosingUnits`, `Damaged` | `Ship`, `TransitEvents`, `Settlement` |
| `Location.cs` | `AccordReach` (jurisdiction), `LocationType`, `MapPoint` (schematic map units), `Location` (region, reach, residents, `Map`) | `WorldGenerator`, `Visibility`, `DecisionEngine`, `MapView` |
| `Route.cs` | `Route` (two ends, hours, danger 0..1, law, `Band`), `RoutePlan` (distance, worst leg's danger, hops, path, `Band`), `DangerBand` | `SimulationContext.FindPath`, `DecisionEngine`, `TransitEvents`, `PlayerActions`, the views |
| `Contract.cs` | `ContractType` (only `Haul` is created), `ContractStatus`, `Contract`: parties, good, quantity, origin, destination, offered fee, the poster's hidden `BuyerReservationFee`, goods cost, tax rate, deadline, jurisdiction, clauses, escrow held, locked bond; transitions `BeginNegotiation`, `Activate`, `MarkFulfilled`/`Breached`/`Expired` | `DecisionEngine`, `Settlement`, events, `Visibility`, `Invariants` |
| `Clause.cs` | `ContractClause` base; `PenaltyOnLateDelivery` (per tick late, capped; the cap is the bond); `QualityWarranty` (refund fraction below a minimum quality) | `Contract`, `DecisionEngine.TryPostDemand`, `Settlement.Fulfill` |
| `Ship.cs` | `ShipStatus` (Docked, InTransit), `ShipCourse` (path, departed, arrival: the leg the client draws the ship along), `Ship`: type, owner, location, capacity, `ActiveContract`, `Manifest`, `DepartedTick`, `Course`, a text log; `Reserve` (with the repositioning course), `Depart` (at the pickup), `SetCourse`, `DelayArrival`, `Arrive`, `Unload` | `DecisionEngine`, `PlayerActions`, `TransitEvents`, `Settlement`, `Invariants`, `HaulEstimate`, `MapView` |
| `ShipType.cs` | `ShipType` (5 hulls), `ShipClassInfo` (capacity, speed, running cost per tick, incident modifiers, preservation), `ShipClasses.Info` table | `Ship`, `PriceModel`, `IncidentResolver` |
| `Personality.cs` | `Personality` (risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, concession rate) | `ActorState`, `DecisionEngine`, `PriceModel`, `WorldGenerator` |
| `Reputation.cs` | `ReputationContext` (Accord standing, region, good category, counterparty), `ReputationLedger` (scores −1..1, decay toward 0) | `Settlement` (writes), `Visibility` (reads region) |

### Engine: `Actors/`

| File | Contents | Used by |
|---|---|---|
| `ActorState.cs` | One model for every actor: credits, stock, ships, home, personality, `Producer` profile, consumer `Demands`, `IncomePerHeartbeat`, `IsPlayer`, `MovesCargo` | everywhere |
| `ActorRole.cs` | `ActorRole`: Producer, Consumer, ShippingCompany, IndependentPilot, Government in use; Factory and the rest named, never created | `ActorState`, `WorldGenerator` |
| `Profiles.cs` | `ProducerProfile` (good, output, unit cost, cap, margin), `ConsumerDemand` (good, rate, target stock) | `ActorState`, `PriceModel`, `DecisionEngine`, `WorldGenerator` |
| `DecisionEngine.cs` | `RunProducer` (make goods up to cap, paying cost), `RunConsumer` (income, consume, post when below 60% of target), `TryPostDemand`, `RunHauler`, `BestCandidate`, `Score` (utility = profit × risk × growth − opportunity cost, over a liquidity threshold), `Commit`, `RepositioningCourse` (internal: the empty leg to a pickup) | `ActorHeartbeat`; `RepositioningCourse` also `PlayerActions` |
| `Negotiation.cs` | `Negotiation.ResolveAi` (closed form: deal if buyer max ≥ seller min, price split by concession rates); `NegotiationSession` (interactive rounds with tells) | `DecisionEngine.Score`; the session only by `NegotiationTests` |
| `PlayerActions.cs` | `HaulBlock`, `HaulAcceptance` (codes, worded by the templates), `HaulFeasibility`; `CheckHaul`, `AcceptHaul` (refuses a contract that is no longer open): the player's way to take a haul through the same steps as `Commit` | `GameSession.TakeHaul`, `BoardView`, `ContractView`; S3 reworks it |

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
| `Simulation/WorldGenerator.cs` | The sector: locations (with map points), routes (hours and danger), producers, consumers, government, haulers, optional player; `WorldHandles` | `Soak`, `Fingerprint`, `GameSession`, `BenchPanel`, tests |
| `Simulation/Settlement.cs` | `Pickup`, `Fulfill` (fee, warranty refund, late penalty from the bond, tax; frontier default), `Breach` (cargo lost: bond forfeited, consumer refunded); reputation changes | `TransitEvents` |
| `Simulation/IncidentResolver.cs` | `TransitIncidentKind`, `TransitIncident`, `Resolve` (roll → delay, damage, partial loss, credit cost, total loss; severity from the ship), `Share(kind)` (the table's odds per kind) | `TransitIncidentCheck`, `TransitIncidentTests`; `Share` by `ContractView` |
| `Simulation/Invariants.cs` | `Check`: credit conservation, escrow reconciliation and non-negative escrow, cargo conservation, no orphan active contracts; `InvariantViolation` | `Soak`, `InvariantTests` |
| `Simulation/EventJournal.cs` | `JournalEntry` and its eight kinds (section 3), `EventJournal` (capped at 5,000, `Entries`, `Tail`, `TotalRecorded`) | `SimulationContext.Record`; `LogView`, `JournalText` |
| `Simulation/StateFingerprint.cs` | `Of(context)`: tick, draws, money supply, settlement counters and every actor's credits, as one string | `TestWorld.Signature`, the CLI's `fingerprint`, the client's `BenchPanel` |
| `Simulation/GameTime.cs` | One tick = one game hour. `Day`, `HourOfDay`, `At`, `Days`, `FormatDate` ("Day 4 · 14:00"), `FormatDuration` ("1d 6h"), `FormatRelative` ("in 1d 6h") | `Soak`, `InvariantTests`, `GameTimeTests`, `Format` (every date the client shows) |
| `Simulation/RunStop.cs` | Why `RunTo` returned: `ReachedTarget`, `Drained`, `PlayerAlert` | `SimulationContext.RunTo` |

### Soak harness: `src/ByGalacticAccord.Cli/`

| File | Contents |
|---|---|
| `Program.cs` | Commands `soak [ticks] [--seed N]` (default 200,000 ticks, seed 20260619), `fingerprint [ticks] [--seed N]` (default 20,000) and `help` |
| `Soak.cs` | Runs in 5,000-tick chunks, `Invariants.Check` after each (exit code 1 on a violation), then prints telemetry |
| `Fingerprint.cs` | A headless run's `StateFingerprint`, which the browser check compares with the client's |

### Presentation: `src/ByGalacticAccord.Presentation/`

Everything the interface says or draws, as plain records built from the session; the client only renders
them. A view never changes the world; commands go through `GameSession`.

| File | Contents | Used by |
|---|---|---|
| `Text/Templates.resx` | Every interface sentence and label (design §11.5): ~190 templates with named `{fields}`, keyed by journal entry type (`HaulTaken`, `Incident.CargoDamage`, each with a `.You` twin) and fact type (`Good.Food`, `Jurisdiction.AccordEdge.Long`, `Line.Running.Note`, …) | `Templates` |
| `Text/Templates.cs` | `Get`, `Fill(key, (name, value)…)` (throws on a missing key or field), words for the engine's enums (`Good`, `Jurisdiction`, `JurisdictionShort`, `JurisdictionLong`, `Danger`, `LocationKind`, `Role`, `ContractKind`, `ShipClass`) | every view, the client's components |
| `Text/Format.cs` | `Money` ("₵1,234", "−₵42"), `MoneyCents`, `Quantity`, `Percent`, `Points`, `Hours`, and `GameTime`'s dates, all invariant | every view |
| `Text/JournalText.cs` | `Describe(context, entry)` (the entry's sentence), `Subject`, `ContractOf` | `LogView`, `HeaderView` (alerts) |
| `Session/GameSession.cs` | `ClockSpeed`, `HaulOutcome`, `GameSession`: the world with the player, the clock (`SetSpeed`, `Advance(realSeconds)` capped at `MaxFrameSeconds`, `Clock` with its fraction, `Revision`), `Alerts`, `PlayerShip`, `TakeHaul` | `Shell`, the views, tests |
| `Views/HaulEstimate.cs` | What a haul is worth to the player's ship by the engine's rules: fee, running costs (empty and laden legs), docking, goods paid at pickup (frontier), the certain warranty refund and late fee, the bond, incident odds; `TakeHome`, `PerDay`, `NextFree` (where and when a busy ship is free) | `BoardView`, `ContractView`, `MapView` |
| `Views/BoardView.cs` | `BoardRow`, `BoardViewModel`: open hauls the player can see (`Visibility`), newest first, with take-home and one note | `Shell` |
| `Views/ContractView.cs` | `MoneyLine`, `InfoLine`, `ContractViewModel`: the contract as a document (breakdown, schedule, uncertain inputs, terms as sentences, party, status); `BlockText`, `OutcomeText` | `Shell`, `BoardView` (`BlockText`) |
| `Views/MapView.cs` | `MapNode`, `MapRoute`, `MapLeg`, `MapRegion`, `MapShip`, `MapViewModel`, `LabelAnchor`: the schematic map, the selected contract's overlay, region frames, label placement; `Ships(session, clock)` and `Along` (a course's position by share of hours) | `Shell`, `SectorMap`, `MapLayers`, `ShipLayer` |
| `Views/LogView.cs` | `LogLine`, `LogViewModel`: the journal's visible entries, newest first, 40 at most | `Shell` |
| `Views/HeaderView.cs` | `SpeedButton`, `HeaderViewModel`: speeds, date, credits, escrow, running cost, the fleet line, alerts | `Shell` |
| `Views/Tone.cs` | `Tone`: what a value's colour encodes (gain, loss, warn, yours, muted, plain) | every view; CSS classes `tone-*` |

### Client: `src/ByGalacticAccord.Client/`

| File | Contents |
|---|---|
| `Program.cs` | The Blazor host; `Shell` is the root component |
| `Shell.razor` | Reads `?seed=` (default 20260619) and `?bench=`; builds the `GameSession` (started at Day 1 · 06:00); runs the frame loop; holds the selection and the last command's outcome; rebuilds the view models when `Revision` or the selection changes, and the ships every frame |
| `Components/TimeBar.razor` | Title, speed buttons (`data-speed`), date, money, theme picker |
| `Components/SectorMap.razor` | The map panel: `MapLayers` and `ShipLayer` in one SVG, the overlay card, the legend, the fleet line |
| `Components/MapLayers.razor` | Ground, regions, routes (`band-*` classes carry colour and dash), overlay legs, hours, locations; renders only when its model changes |
| `Components/ShipLayer.razor` | Ships at their interpolated positions; renders every frame |
| `Components/BoardPanel.razor`, `ContractPanel.razor`, `CaptainsLog.razor` | The board, the contract document with "Accept as posted", the log (a line about a contract selects it) |
| `Components/BenchPanel.razor` | `?bench=N`: runs a world with no player for N hours, shows the time and the `StateFingerprint` |
| `wwwroot/index.html`, `css/app.css`, `js/client.js`, `fonts/` | The page; colour tokens with dark (default) and light themes (`prefers-color-scheme`, or `data-theme` on `<html>`); the theme's storage, guarded; IBM Plex |

### Browser check: `tools/client-check/`

| File | Contents |
|---|---|
| `check.cjs` | With no arguments: `dotnet publish` the client to `artifacts/client-check/publish`, get the desktop fingerprint from the CLI, serve the site, then in Chromium: load (paused, Day 1 · 06:00, board, map), select a contract, accept it, run 16× (resuming alerts), light theme, phone width (no sideways scroll), the speed probe and the fingerprint; screenshots to `artifacts/client-check/screenshots`; fails on any console error, page error or failed request. `--site`, `--out`, `--fingerprint` override. |
| `package.json`, `package-lock.json` | Playwright 1.63.0 |

### Tests: `tests/ByGalacticAccord.Tests/`

| File | Covers | Soak variants |
|---|---|---|
| `TestWorld.cs` | Helpers: `RunHeadless`, `Signature` (`StateFingerprint.Of`), `FastTicks` (20,000), the `Soak` trait constants | — |
| `DomainTests.cs` | `Credits`, `PenaltyOnLateDelivery`, `QualityWarranty`, `Cargo` decay, `FindPath` (multi-hop, unreachable, cache), `OpenContracts`, the good and hull tables | — |
| `NegotiationTests.cs` | `Negotiation.ResolveAi`, `NegotiationSession` | — |
| `TransitIncidentTests.cs` | `IncidentResolver`: deterministic, armour milder than courier, danger raises credit costs | — |
| `GameTimeTests.cs` | `GameTime` | — |
| `DeterminismTests.cs` | Same seed → same `Signature`; stepped replay; seeds diverge | 150k, 120k |
| `InvariantTests.cs` | `Invariants.Check` at checkpoints; credits, escrow, no negative balances | 1M, 250k |
| `EconomyTests.cs` | Economy comes alive; frontier reachable | 200k; money supply over 1M; no monopoly over 500k |
| `JournalTests.cs` | Entries match the contracts they describe; the cap | — |
| `ShipCourseTests.cs` | A course agrees with its ship and contract every hour; a haul settles at its course's arrival, delays included | — |
| `PlayerActionsTests.cs` | Signing through the engine path; each refusal (not open, busy, too large, origin short, can't front the goods); an alert stops `RunTo` | — |
| `TemplateTests.cs` | `Fill`; every template well formed; every enum word; every journal entry worded for an AI and for the player | — |
| `FormatTests.cs` | Money, cents, percentages, quantities | — |
| `SessionTests.cs` | Speeds, the clock's fraction, the frame cap, the same world as one `RunTo`, the hard pause, `TakeHaul` | — |
| `HaulEstimateTests.cs` | The estimate predicts a clean run's settlement to the cent; frontier goods; empty legs; a busy ship; food's warranty refund | — |
| `ViewTests.cs` | Every view builds through a played game; the board's order; the overlay's legs; interpolation along a path | — |

## 7. Determinism rules (design §20.1)

- Randomness comes only from `context.Rng`, and only inside events or world generation. Draws today:
  `WorldGenerator` (personalities, first heartbeats), `TransitIncidentCheck`, `IncidentResolver`. The client
  draws nothing: its clock decides only *when* `RunTo` is called, and runs in any increments give the same world.
- Anything that draws randomly or changes state in a loop iterates a stable order: `ActorsOrdered`,
  `OpenContracts`, lists in insertion order. A dictionary's enumeration order is used only where order
  cannot change the result (sums, counts, the invariant checks, reputation decay).
- `EventQueue` breaks tick ties by insertion order.
- Money is `decimal` throughout.
- Checked by `DeterminismTests` (fast and soak), `SessionTests.The_clock_runs_the_same_world_as_one_RunTo`, the
  soak harness's identical telemetry across runs, and the browser check, which compares the client's
  WebAssembly run with the desktop one (`StateFingerprint`, 20,000 hours, seed 42).

## 8. Known defects and where they live

From plan §1 (the frontier-goods and same-place rows were found in S2); the step named fixes each.

| Defect | Where | Step |
|---|---|---|
| The poster's hidden reservation fee leaks through the terms | `TryPostDemand` scales the late fee (1% an hour) and its cap, which is the bond (30%), from `maxFee`; the client shows both, as it must. (The journal line that printed `≤{maxFee}` is gone since S2.) | S5 |
| The player never pays running costs | `ActorHeartbeat.Process` skips `IsPlayer`; running costs are charged in `RunHauler`. The client's take-home counts them anyway, as the design defines it | S3 |
| Outside the Accord the pilot pays for the goods and is paid only the fee | `Settlement.Pickup` transfers `GoodsCost` pilot → producer; `Fulfill` transfers only the fee back. Nearly every frontier haul's take-home is negative; AI haulers take them because `Score` ignores the goods cost | S3 (Sale and Haul replace the bundled contract) |
| Hauls with the same origin and destination | `TryPostDemand` sources from the cheapest producer anywhere, including one at the consumer's own location (Helios Prime's Luxury and Medicine): a one-hour "haul" | S3 |
| Nobody can go below ₵0 | `SimulationContext.ApplySink` caps at the balance; `Commit` bonds `min(bond, credits)` | S8 |
| Food decays below its warranty on any route | `Goods` (Food 0.012 a tick) against the 0.7 warranty in `TryPostDemand`: about 25 hours; the shortest route is 30 | S3 |
| Visibility is nearly omniscient | `Visibility.CanSee`'s home-region rule (Helios is 5 of 7 locations) | S6 |
| Incident outcomes don't shift with danger | `IncidentResolver.Resolve`: the roll isn't weighted by danger, despite its comment | S8 |

## 9. No callers (left from the retired clients, or waiting for a step)

`NegotiationSession` (tests only); `IPlayerPrompt`; `ISimEvent.Describe` (never called); `Ship.History`,
`TransferOwnership`, `SetManifest`; `ActorState.RemoveShip`, `Owns`, `AllStock`; `EventJournal.Tail` (tests
only); `SimulationContext.Neighbours`; `ProducerProfile.IsIdle`; `DeterministicRandom.Pick`;
`Location.HasEscrow`, `HasArbitration`; `ShipClassInfo.Niche`, `MarketValue`, `RunsHot` (data only: push is not
implemented); `TransitIncident.Description` (only the ship's text log; the journal's text comes from the
templates); `Contract.ExtendDeadline`; `ReputationLedger.Snapshot`; `SimulationConfig.NegotiationMaxRounds`.
Delete or wire them as the steps rework their areas.

## 10. The client's frame

```
Shell.OnInitialized → new GameSession(seed, startTick: Day 1 · 06:00), paused → Rebuild()
Shell.RunClock: PeriodicTimer, 33 ms
  └─ each frame while running: GameSession.Advance(real seconds since the last frame, at most 0.25)
       clock += seconds × speed → RunTo(floor(clock)) → Revision++ ; a PlayerAlert stops the clock
     → Rebuild(): if Revision or the selection changed, Header/Board/Map/Log/ContractView.Build
                  always MapView.Ships(session, clock)  (positions along ShipCourse, by share of hours)
     → StateHasChanged: components whose model object is the same skip rendering; ShipLayer always renders
Player input → Shell.SetSpeed / Select / Accept (GameSession.TakeHaul → PlayerActions) → Rebuild
```

## 11. Design section numbers: v0.2 (old comments) → v0.3

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

## 12. Regenerating the "used by" facts

From `ByGalacticAccord/`:

```bash
# Every file that mentions a type or member (then read the hits: comments match too)
grep -rn --include=*.cs --include=*.razor -w 'TypeName' src tests

# Members with no callers: no hits for ".Name" besides the definition
grep -rn --include=*.cs --include=*.razor -E '\.MemberName\b' src tests

# Line counts, to judge what to read whole
find src tests \( -name '*.cs' -o -name '*.razor' \) -not -path '*/obj/*' | xargs wc -l
```
