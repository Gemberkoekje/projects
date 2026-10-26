# By Galactic Accord — code map

What is where, what calls what, and where money and goods move. Use it to find the file, then read the
file: the code is the truth. **Last checked against the code: 2026-10-26, after S4.**

**Keep it true.** A commit that adds, removes, renames or moves a file or a type, changes who calls a
public member, adds or removes an event, or changes a money or goods path updates this map in the same
commit. If you find the map wrong, fix it. Section 12 has the commands that regenerate the "used by" facts.

## 1. Orientation

- **What the build is:** S3's contract web of two kinds (a **Sale**, wanted or for sale, hands goods over at a
  place; a **Haul** carries a shipper's goods), with S4's **terms** on every one: deadline and grace, a late fee
  and its cap (the bond), cancellation for default and at will (a break fee), what a short delivery is, a quality
  warranty with a floor below which goods may be rejected, and a haul's cargo liability, in strict, standard and
  lenient packages. Goods belong to an actor and sit in its stock, in storage it rents from a station operator,
  or in a ship's hold. When a contract goes wrong its terms settle it; a decision they hand a party (cancel or
  wait, reject or accept) is made by an AI actor by utility and by the player at a hard pause. Every journal
  entry records its cause, so any outcome reads back as a chain. Every actor, the player too, acts through
  `Market`. Headless runs (soak harness, most tests) have no player; the client's `GameSession` generates the
  world with one, who can carry a haul, fill a wanted Sale with its ship or through a carrier, buy a lot, store
  goods, walk away from a contract, cancel one for default, raise its haul, and answer decisions.
- **Where it is going:** Slice 1 of the design, in the steps of [`plan.md`](plan.md) (S5, posting, responses and
  haggling, is next). Negotiation (S5), information (S6), ships (S7) and money's limits (S8) change shape next;
  plan §1 lists what is kept, reworked and retired.
- **Documents:** design [`../by_galactic_accord_gdd_v0.3.md`](../by_galactic_accord_gdd_v0.3.md) ·
  plan [`plan.md`](plan.md) · step logs [`log/`](log/README.md) · humans' overview [`README.md`](README.md) ·
  agent rules [`CLAUDE.md`](CLAUDE.md).
- **Section numbers in code comments:** a bare `§n` cites the **v0.2** design (deleted; it is in git history
  at `844c8307:by_galactic_accord_gdd_v0.2.md`). Comments written since S1 say "design §n" and cite v0.3.
  Section 11 translates.

## 2. Projects

| Project | Path | References | Role |
|---|---|---|---|
| Engine | `src/ByGalacticAccord.Engine/` | nothing (BCL only) | The whole simulation. No UI, no I/O, deterministic. `InternalsVisibleTo` the tests. |
| Presentation | `src/ByGalacticAccord.Presentation/` | Engine | What the interface shows, as data: the text templates, the view models, the game clock. No Razor, no browser. |
| Client | `src/ByGalacticAccord.Client/` | Presentation, Blazor WebAssembly | The browser client: renders the view models, runs the clock's frames. |
| Soak harness | `src/ByGalacticAccord.Cli/` | Engine | Console app: headless runs, telemetry, invariant checks, state fingerprints. |
| Tests | `tests/ByGalacticAccord.Tests/` | Engine (and its internals), Presentation, xUnit | Fast by default; `[Trait("Category", "Soak")]` for long runs. |
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
     adds 7 Locations, 9 Routes, 8 producers, 7 consumers, 1 government, 7 traders, the player if asked,
     and a station operator ("<Location> Docks") at every location; starting stock through Produce
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
| `ActorHeartbeat` | `Events/ControlEvents.cs` | `DecisionEngine.ChargeRunningCosts` for every actor, **the player too**; then `DecisionEngine.Run` unless it is the player | Itself, `HeartbeatInterval` (24) later |
| `ContractExpiryCheck` | `Events/ControlEvents.cs` | Still open at its deadline → `Market.Expire` (frees what the post set aside) | — |
| `GraceEnded` | `Events/ControlEvents.cs` | At `Contract.GraceEndsTick` (deadline + grace), scheduled when someone waits on a performer (`Market.Supply` unless handed over at once or collected; `TakeHaul`). Still `Active` → in default: records `ContractInDefaultEntry`; a player payer gets a `PlayerDecision` (the hard pause); an AI payer `DecisionEngine.ReviewDefault`s | `DefaultReview` (an AI that waits) |
| `DefaultReview` | `Events/ControlEvents.cs` | Still in default, AI payer → `ReviewDefault` again | Itself via `ReviewDefault`, a day later |
| `InflationCheck` | `Events/ControlEvents.cs` | `InflationMonitor.Evaluate` | Itself, every 400 |
| `ReputationDecay` | `Events/ControlEvents.cs` | `ReputationLedger.DecayTowardNeutral` | Itself, every 400 |
| `ShipDeparted` | `Events/TransitEvents.cs` | The ship reaches its job's pickup (`Ship.ReachPickup`) and `Settlement.Load`s; while the player inspects what it bought there, it reschedules itself and waits; a job with no destination (collecting a lot) ends here; nothing aboard → the Sale it serves fails; else `Ship.DepartLaden` | `TransitIncidentCheck` at mid-route; itself (waiting on an inspection) |
| `TransitIncidentCheck` | `Events/TransitEvents.cs` | `Rng.Chance(scale × danger × ship modifier)` | `TransitIncidentOccurred`, or `ShipArrived` |
| `TransitIncidentOccurred` | `Events/TransitEvents.cs` | Applies the `TransitIncident` to the job owner's lot (`Ship.Alter`: damage, partial or total loss through `Lose`), a credit cost (`ApplySink`), `Ship.DelayArrival`; records an `IncidentEntry`; raises it as a player alert if the ship is theirs | `ShipArrived` (plus any delay) |
| `ShipArrived` | `Events/TransitEvents.cs` | `Ship.Arrive`, docking fee, `Scout` the destination, `Settlement.Unload` (a haul cancelled on the way still sets the shipper's goods down), `Ship.FinishJob` | — |

Every transit event carries the `ShipJob` it belongs to and does nothing if the ship's job is no longer that
one (`ReferenceEquals`).

**A delivered Sale's life** (consumer → trader): `RunConsumer` → `PostWanted` (its package from personality,
relaxed by `Terms.Meetable` to what a standard hold can meet) → `Market.PostSale` (Wanted, receipt = the
consumer's stock at home; terms from `Terms.ForSale`; schedules `ContractExpiryCheck`; records
`ContractPostedEntry`; `DecisionEngine.AnswerAtOnce` lets an AI producer at the place answer it at once) → a
trader's `RunTrader` → `BestOption` (`Visibility`, `FillSources`, `ScoreFill`: arrival inside the grace, goods
the buyer won't reject, the terms' risk) → `Commit` → `Market.FillWithShip` (signs; buys the goods at the source
with `BuyAtSource`, a wanted Sale at the producer's location that it answers at once; `Dispatch`es the ship with a
`ShipJob`; schedules `GraceEnded`) → `ShipDeparted` (`Settlement.Load`: the purchase is handed over into the hold)
→ `TransitIncidentCheck` → (`TransitIncidentOccurred` →) `ShipArrived` → `Settlement.Unload` → `HandOverCargo`
(short under a strict term, or rejected on inspection: `Fail`, the goods stay aboard; else into the consumer's
stock, paid) or `Fail`. If the grace ends first, `GraceEnded`: the consumer waits while the goods are on their way
and worth more than its money back and the bond (`WouldCancel`), else cancels for default. A trader with no free
ship `Broker`s instead: `Market.FillViaHaul` buys into storage at the source and posts a Haul that serves the Sale;
the Haul's delivery hands the goods over. A producer selling delivered (`SellDelivered`) does the same from its
own stock (`FillViaHaul` with `FillSource.FromStock`). On later heartbeats `ArrangeTransport` reposts an untaken
Haul a quarter dearer (`RaiseHaulOffers`), arranges transport again for a signed Sale whose Haul expired, or, once
no carrier could make the grace, walks away (`WouldWalkAway`: the break fee costs it less than its bond).

**A lot's life:** `RunProducer` → `PostLot` → `Market.PostSale` (ForSale, the producer's stock set aside) → a
consumer's `TryBuyLot` → `Market.BuyLot` into storage at the place (handed over at once, `HandOverReserved`),
then `BringHome` posts a Haul home; or a ship collects it (`PickupSource.Purchase`), as the player's `Buy` does.

**The player's way in** is `PlayerActions` (`CheckHaul`/`TakeHaul`, `CheckFill`/`Fill`, `CheckBuy`/`Buy`,
`CheckBroker`/`Broker` (fill through a carrier, as `Broker` does), `CheckHaulAgain`/`HaulAgain` (raise or replace its
haul, as `RaiseHaulOffers` does), `CancelAtWill`, `CancelForDefault`, `Decide`, `Store`, `Retrieve`), the same
`Market` and `Settlement` commands an AI decision makes, called by `GameSession.Accept`, `Broker`, `HaulAgain`,
`CancelAtWill`, `CancelForDefault`, `Decide`, `Store` and `Retrieve`.

**A decision** (design §3.4, §8.3): `GraceEnded` (cancel for default or wait) or an inspection below a warranty's
floor (`Settlement.HandOverReserved`/`HandOverCargo`: accept with the refund, or reject). An AI decides at once by
utility (`DecisionEngine.ReviewDefault`, `WouldCancel`, `WouldReject`). For the player the engine raises a
`PlayerDecision` (`SimulationContext.RaisePlayerDecision`): goods under inspection wait set aside in the seller's
storage (or, when the player's ship collects, where they are, with the ship waiting), and `RunTo` returns
`RunStop.PlayerDecision` without moving until `PlayerActions.Decide` resolves it. A decision about a contract that
has ended some other way lapses.

**The journal** (`Simulation/EventJournal.cs`, a ring buffer of 5,000) records typed entries, not prose. Each is
stamped with its `Seq` when recorded and names its `Cause` (design §16.2): by default the previous entry about
the same contract, or about a haul delivering it (`SimulationContext.Record` keeps `Contract.LastEntry`, and a
haul's entries also become the Sale it serves' latest); a recorder names another cause where one applies (a
haul's failure failing its Sale, a cancellation withdrawing a haul, a haul posted for a Sale). `EventJournal.Find`
looks one up while the ring keeps it; `ContractView.Why` walks the chain.

| Entry | Recorded by |
|---|---|
| `ContractPostedEntry` (poster) | `Market.PostSale`, `PostHaul` |
| `ContractSignedEntry` (taker, price) | `Market.Sign` (every signing) |
| `GoodsHandedOverEntry` (seller, buyer, quantity, quality, ticks late, price, late fee, refund) | `Settlement.Complete` (via `HandOverReserved`, `HandOverCargo`) |
| `HaulDeliveredEntry` (hauler, quantity, quality kept in the hold, ticks late, fee, late fee, refund, cargo liability) | `Settlement.DeliverHaul` |
| `PaymentDefaultedEntry` (payer, payee) | `Settlement.PayerDefault` (outside the Accord) |
| `DeliveryFailedEntry` (performer, `FailureReason`: `CargoLost`, `NotDelivered`, `ShortDelivery`, `Rejected`; delivered, quality) | `Settlement.Fail` |
| `ContractInDefaultEntry` (performer, payer) | `GraceEnded` |
| `ContractCancelledEntry` (by, `CancelReason`: `ForDefault`, `AtWill`; paid: the bond taken or the break fee) | `Settlement.Cancel` |
| `ContractExpiredEntry` | `Market.Expire` |
| `ContractWithdrawnEntry` | `Market.Withdraw` |
| `StorageOpenedEntry` (renter), `StorageClosedEntry` (renter, rent paid) | `SimulationContext.PutInStorage`, `TakeFromStorage` |
| `IncidentEntry` (hauler, the `TransitIncident`) | `TransitIncidentOccurred`; also the player's alert |
| `FaucetAdjustedEntry` (drift, multiplier) | `InflationMonitor.Evaluate`; never shown in the client |

`Presentation/Text/JournalText.cs` words each one from `Templates.resx`, keyed by the entry type's name
(without "Entry") and the contract's kind (`Sale.Wanted`, `Sale.ForSale`, `Haul`), with a `.You` twin when the
player is the subject and a `.ToYou` twin when the player is the other party. Every entry about a contract
implements `IContractEntry`.

## 4. Where state lives: `SimulationContext`

`Simulation/SimulationContext.cs` (864 lines) is the world and the hub; nearly everything takes it.

- **Collections:** `Actors`, `Ships`, `Locations`, `Contracts` (dictionaries by typed id; contracts are never
  removed), `Routes` (list). Registered with the `Add(...)` overloads; `Actor(id)`, `Ship(id)`,
  `Location(id)`, `Contract(id)` look up.
- **Ordered views (use these where order matters):** `ActorsOrdered` (by id, cached), `OpenContracts` (open or
  negotiating, by id; a pruned list, because a contract never returns to Open), `LiveContractsOf(actor)` (the
  actor's open and active contracts in the order it joined them; pruned as they settle), `ProducersOf(good)`,
  `StationOperatorAt(location)`.
- **Graph:** `FindPath(from, to)` → `RoutePlan?` (Dijkstra in `SolvePath`, cached per pair; `Add(Location)` and
  `Add(Route)` clear the cache), `RouteBetween(a, b)`, `Adjacent(a, b)` (cached), `Neighbours(id)`.
- **Storage:** the active `StorageContract`s, by renter and location: `StorageOf(renter, location)`,
  `StoragesOf(renter)`, `StoragesAt(location)`. `PutInStorage` opens one at the location's station operator on
  first use; `TakeFromStorage` closes it when it empties.
- **Services it owns:** `Rng` (`DeterministicRandom`), `Queue` (`EventQueue`), `Journal` (`EventJournal`, a ring
  of 5,000 entries), `Reputation` (`ReputationLedger`), `Config` (`SimulationConfig`).
- **Bookkeeping the invariants read:** `InitialCredits`, `TotalFaucet`, `TotalSink`, `EscrowLockedTotal` /
  `ReleasedTotal` / `ForfeitedTotal`, `TotalEscrowHeld` (over the contracts that ever held escrow),
  `GovernmentRevenue`, the goods ledger per good (`GoodsProduced`, `GoodsConsumed`, `GoodsLost`, `GoodsHeld`:
  stock, storage and holds), counters `ContractsFulfilled` / `Breached` / `Defaulted` / `Cancelled`, `GoodsSold`,
  `IncidentsOccurred`, the failure-path tallies (`NoteFailure`, `Failures(FailureKind)`), and `FaucetMultiplier`
  (the inflation monitor's knob).
- **Other:** `Sign` (internal: `Market.Sign`'s record-keeping), scouting (`Scout`, `HasFreshScout`), player
  alerts (`RaisePlayerAlert(JournalEntry)`, `HasPlayerAlerts`, `PlayerAlerts`, `ClearPlayerAlerts`: an alert is
  the entry that needs the player), player decisions (`PlayerDecisions`, `HasPlayerDecisions`,
  `PlayerDecisionFor`; internal `RaisePlayerDecision`, `ResolvePlayerDecision`), `Record(JournalEntry)` (stamps
  the sequence and the default cause, returns the entry as kept).

## 5. Money and goods: the only paths

Every credit and unit moves through these `SimulationContext` primitives. Anything else is a bug.

**Money:**

| Primitive | Effect | Called from |
|---|---|---|
| `ApplyFaucet` | Creates credits | `RunConsumer` (income × `FaucetMultiplier`) |
| `ApplySink` | Destroys credits, **capped at the actor's balance** (nobody goes below ₵0) | `Produce` in `RunProducer` (production cost), `ChargeRunningCosts` (every ship, every heartbeat), `PayOut` (above working capital), `ShipArrived` (docking fee), `TransitIncidentOccurred` (credit-cost incident) |
| `TaxFromEscrow` | Destroys credits as tax from a contract's escrow, books `GovernmentRevenue` | `Settlement.Pay` (Accord space only: the frontier is untaxed) |
| `TryTransfer` | Moves credits between actors, all or nothing | `Settlement.Pay` outside the Accord (the payer pays the net at handover, or `PayerDefault`) |
| `TransferUpTo` | Moves at most what the payer has | `SettleRent` (renter → station operator); `Settlement.Pay` and `Fail` in the deep Accord (penalties and cargo liability beyond escrow, from the performer's account); `Settlement.Cancel` (a break fee beyond what escrow holds of the canceller's money) |
| `LockToEscrow` | Actor → a contract's escrow, **in whole cents** | `Market.Sign`: the payer locks price + tax, the performer a bond (≤ its credits, to the cent) |
| `ReleaseFromEscrow`, `ForfeitFromEscrow` | Escrow → an actor, in whole cents, capped at what the contract holds | `Settlement.Pay` (the payment and bond less penalties to the performer, the penalties and the rest to the payer), `Settlement.Fail` and `Cancel` for default (bond to the payer, escrow back), `Cancel` at will (the break fee from the canceller's share, the rest back) |

A station operator's rent above its float leaves through `PayOut`, as upkeep. `TaxDirect` has no callers since
S3.

**Goods** live in three places: an actor's stock (`ActorState`, with a reserved part), its storage at a
location (`StorageContract.Lots`), or a ship's hold (`Ship.Hold`, `Lot`s by owner). Perishables decay lazily:
a `Lot` remembers when it was last fresh-stamped and `At(tick, preservation)` computes its quality.

| Primitive | Effect | Called from |
|---|---|---|
| `Produce` | Creates goods in stock (ledger: produced) | `RunProducer`, `WorldGenerator` (starting stock) |
| `Consume` | Destroys goods from stock (ledger: consumed) | `RunConsumer` |
| `Lose` | Books goods destroyed in transit or written off (ledger: lost) | `TransitIncidentOccurred`, `SellOff` (a trader's stale stored goods) |
| `TakeFromStock`, `PutInStock`, `ReserveStock`, `UnreserveStock` | Stock out, in, set aside, freed | `Settlement` (handover, haul pickup, `Keep`, `Unreserve`); `Market.Reserve` |
| `PutInStorage`, `TakeFromStorage`, `ReserveStorage`, `UnreserveStorage` | Storage in (opens it), out (closes it when empty), set aside, freed | `Settlement`, `Market.Reserve`, `PlayerActions.Store`/`Retrieve`, `Stow` and `SellOff` in `RunTrader` |
| `SettleRent` | Charges rent due (rate × units × days) to the renter, up to its credits | `RunStation`; `PutInStorage` and `TakeFromStorage`, before what is held changes |
| `Load`, `Unload` | Into and out of a hold, by owner | `Settlement.Load`/`Unload`/`Receive`, `Market.FillWithShip` (goods already aboard), `PlayerActions`, `Stow` |

Goods move only at handovers (`Settlement.HandOverReserved`, `HandOverCargo`), a ship's pickup and arrival
(`Settlement.Load`, `Unload`, `DeliverHaul`), and storage commands; goods the player is inspecting wait in the
seller's storage (`HandOverCargo`, `PutInStorage` + `ReserveStorage`). Jurisdiction is where a contract is
performed (a Sale's place, a Haul's destination, a storage's location): Accord space (`Contract.UsesEscrow`)
locks the price and tax and the performer's bond at signing and settles from escrow; the deep Accord
(`Contract.Arbitrated`) also collects what escrow can't cover from the performer's account; outside the Accord
nothing is locked, the payer pays at handover or defaults (a seller then keeps its goods), remedies come only off
the payment, and no tax is due (`Settlement.Collectable` says how far each reaches).

## 6. File by file

### Engine: `Domain/` (value objects and aggregates)

| File | Contents | Used by |
|---|---|---|
| `Ids.cs` | `ActorId`, `ShipId`, `LocationId`, `ContractId`: long-backed record structs from counters in `SimulationContext` | everywhere |
| `Credits.cs` | `Credits(decimal)`: arithmetic, comparison, `ToString` "₡1,234" | everywhere money is |
| `Goods.cs` | `GoodType` (7), `GoodCategory`, `GoodInfo` (base value, `KeepsHours` in a standard hold, `Refrigeratable`; `Perishable`, `QualityDecayPerTick` from the keep-time), `Goods.Info` table (Food 7 days, Medicine 2: design §15.3), `Goods.StandardWarranty` (0.7) | `Cargo`, `Lot`, `PriceModel`, `Terms`, `DecisionEngine`, `Settlement`, the views |
| `Cargo.cs` | `Cargo(good, quantity, quality)`: `Fresh`, `Decayed(ticks, preservation)`, `LosingUnits`, `Damaged`, `Merged` (weighted quality) | `Lot`, `Ship`, `StorageContract`, `Settlement`, `TransitEvents` |
| `Lot.cs` | `Lot(owner, cargo, since)`: goods held somewhere, decaying lazily; `At(tick, preservation)` | `Ship.Hold`, `StorageContract.Lots`, the ledger |
| `Location.cs` | `AccordReach` (jurisdiction), `LocationType`, `MapPoint` (schematic map units), `Location` (region, reach, residents, `Map`) | `WorldGenerator`, `Visibility`, `DecisionEngine`, `MapView` |
| `Route.cs` | `Route` (two ends, hours, danger 0..1, law, `Band`), `RoutePlan` (distance, worst leg's danger, hops, path, `Band`), `DangerBand` | `SimulationContext.FindPath`, `DecisionEngine`, `TransitEvents`, `Market`, the views |
| `Contract.cs` | `ContractType` (Sale, Haul, Storage created; Build and the rest named), `ContractStatus` (Open, Negotiating (never set yet), Active, Fulfilled, PartlyFulfilled, Breached, Cancelled, Defaulted, Expired, Withdrawn), `SaleSide`, `HoldingKind`, `Holding` (stock, storage or aboard a ship, at a location); abstract `Contract` (poster, counterparty, offered and agreed price, hidden `Reservation`, tax, deadline, `Terms`, `GraceEndsTick`, `InDefault(now)`, status, `Cancellation`, `LastEntry`, jurisdiction, `UsesEscrow`, `Arbitrated`, escrow held, locked bond, `Payer`/`Performer`, `Places`, `LatePenalty`, `Warranty`, `PerformanceBond`; internal transitions); `SaleContract` (side, good, quantity, place, buyer/seller, receipt, `Collected`, reserved, delivered, `UnitPrice`); `HaulContract` (good, quantity, origin, destination, pickup holding, the Sale it `Serves`, shipper/carrier, assigned ship, `PickedUp`, `PickupQuality`, `InTransitQuality`); `StorageContract` (operator, renter, rate per unit-day, lots, reservations, rent charged; signs itself; `ContractTerms.None`) | `Market`, `Settlement`, `DecisionEngine`, events, `Visibility`, `Invariants`, the views |
| `ContractTerms.cs` | `TermPackage` (Strict, Standard, Lenient), `PartialDelivery` (ProRata, Failure), `CancelReason` (ForDefault, AtWill), `Cancellation`; `PenaltyOnLateDelivery` (per hour late, capped; `HoursToCap`); `QualityWarranty` (minimum, most refunded, `RejectBelow` floor; `RefundFraction`, `Rejectable`); `CargoLiability` (per unit, cap; `For(lost)`); `ContractTerms` (package, grace, late fee, break fee, partial delivery, warranty, liability; `Bond` = the late fee's cap; `None`) | `Contract`, `Terms`, `Settlement`, `DecisionEngine`, `DealEstimate`, `ContractView` |
| `Ship.cs` | `ShipStatus` (Docked, InTransit), `PickupSource` (Purchase, Haul, Storage, Aboard), `ShipCourse` (path, departed, arrival: what the client draws along), `ShipJob` (goods owner, good, quantity, pickup, source, destination, and the haul, purchase and delivery it serves), `Ship`: type, owner, location, capacity, `Hold` (lots by owner), `FreeSpace`, `Job`, `Course`, `Preservation`, `IsFree`, `LotOf`; `Reserve`, `ReachPickup`, `Load` (merges an owner's lot), `Unload`, `Alter`, `DepartLaden`, `DelayArrival`, `Arrive`, `FinishJob`; a text log | `Market`, `Settlement`, `TransitEvents`, `DecisionEngine`, `PlayerActions`, `Invariants`, the views |
| `ShipType.cs` | `ShipType` (5 hulls), `ShipClassInfo` (capacity, speed, running cost per tick, incident modifiers, preservation: refrigerated 0.8, so goods keep five times as long), `ShipClasses.Info` table | `Ship`, `PriceModel`, `IncidentResolver`, `WorkingCapital` (`MarketValue`) |
| `Personality.cs` | `Personality` (risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, concession rate) | `ActorState`, `DecisionEngine`, `PriceModel`, `WorldGenerator` |
| `Reputation.cs` | `ReputationContext` (Accord standing, region, good category, counterparty), `ReputationLedger` (scores −1..1, decay toward 0) | `Settlement` (writes), `Visibility` (reads region) |

### Engine: `Actors/`

| File | Contents | Used by |
|---|---|---|
| `ActorState.cs` | One model for every actor: credits, stock with a reserved part (`Stock`, `Reserved`, `Available`), ships, home, personality, `Producer`, `Station` and consumer `Demands` profiles, `IncomePerHeartbeat`, `IsPlayer`, `MovesCargo`, `KeepsStock`; per-good `Consumed`; its `PriceMemory`s (internal) | everywhere |
| `ActorRole.cs` | `ActorRole`: Producer, Consumer, ShippingCompany, IndependentPilot, Government, StationOperator in use; Factory and the rest named, never created | `ActorState`, `WorldGenerator`, the views |
| `Profiles.cs` | `ProducerProfile` (good, output, unit cost, cap, margin; `Fullness`), `StationProfile` (storage rate, float), `ConsumerDemand` (good, rate, target stock) | `ActorState`, `PriceModel`, `DecisionEngine`, `WorldGenerator` |
| `PriceMemory.cs` | `PriceKind` (Wanted, Lot, Haul), `PriceKey`, `PriceMemory`: a per-actor price factor that learns from each of its closed posts (taken within a day: lower; expired: higher, or the other way for a seller), clamped | `DecisionEngine` (consumers' wanted Sales, producers' lots, everyone's hauls) |
| `DecisionEngine.cs` | Partial class, the shared part: `Run` (by profile: station, producer, consumer, trader; then `PayOut` above `WorkingCapital`, except the government), `WorkingCapital`, `ChargeRunningCosts`, `AnswerAtOnce`, `HaulFeeEstimate`, `UsualHaulMarkup`/`UsualHaulOffer` (the player's haul offer), `RaisedHaulOffer`, `HaulDeadline`, `Ask` (a producer's unit ask); the terms' decisions by utility: `ReviewDefault`/`WouldCancel` (a payer whose contract is in default), `WouldReject` (a buyer handed goods below the floor), `WouldWalkAway` (a seller that can no longer deliver); and private `HaulTerms`, `PostPricedHaul`, `ArrangeTransport` (post a Haul for a signed Sale, send one's own ship, or walk away), `RaiseHaulOffers` (repost an untaken haul higher) | `ActorHeartbeat`; `Market.PostSale` (`AnswerAtOnce`); `GraceEnded`, `DefaultReview` (`ReviewDefault`); `Settlement` (`WouldReject`); `PlayerActions` (`FillSources`, the offers, `HaulDeadline`) |
| `DecisionEngine.Producers.cs` | `RunProducer`: `Produce` (paying cost), `TryAnswer` (local wanted Sales, as posted, at or above its ask), `SellDelivered` (a remote wanted Sale it can profit on, and whose perishables a standard hold gets there before the buyer could reject them: `Market.FillViaHaul` from its stock, which posts the Haul), `ArrangeTransport`, `PostLot` (when 75% full, priced by its `PriceMemory`, never below cost) | `DecisionEngine.Run`, `AnswerAtOnce` |
| `DecisionEngine.Consumers.cs` | `RunConsumer`: income (`ApplyFaucet`), `Consume`, `BringHome` (a Haul home for goods in storage elsewhere), then per demand below 60% of target, counting `Inbound`: `TryBuyLot` (a cheap lot plus a haul home) or `PostWanted` (delivered, priced from the cheapest source and its `PriceMemory`, on terms `Terms.Meetable` relaxes); `Willingness` (affordability-aware) | `DecisionEngine.Run` |
| `DecisionEngine.Traders.cs` | `RunTrader`: per free ship `BestOption` over visible Hauls (`ScoreHaul`: inside the grace, not a carriage the shipper would refuse, the warranty's refund) and wanted Sales by source (`FillSources`, `ScoreFill`: inside the grace, goods the buyer wouldn't reject) by `Utility` less `TermsRisk` (what the terms stand to cost if an incident strikes) over a `Threshold`, `Commit`; `Stow` (goods left aboard into storage); `Broker` (no free ship: `FillViaHaul`); `SellOff` (stored goods as lots, written off after 30 days); `RunStation` (rent) | `DecisionEngine.Run`; `FillSources` also `PlayerActions` |
| `Negotiation.cs` | `Negotiation.ResolveAi` (closed form), `NegotiationSession` (interactive rounds with tells): both **tests only** since S3 (AI actors take posts as posted until S5) | `NegotiationTests` |
| `PlayerActions.cs` | `DealBlock` (codes, worded by the templates; `TooLate` new), `HaulFeasibility`, `FillFeasibility`, `BuyFeasibility`, `BrokerFeasibility`, `HaulAgainFeasibility`; `ShipFor`, `CheckHaul`/`TakeHaul`, `CheckFill` (the cheapest source)/`Fill`, `CheckBuy`/`Buy`, `CheckBroker`/`Broker`, `CheckHaulAgain`/`HaulAgain`, `CancelAtWill`, `CancelForDefault`, `Decide`, `Store`, `Retrieve`: the player's commands through `Market` and `Settlement` | `GameSession`, `ContractView`, `BoardView`, `DealEstimate` |

### Engine: `Economy/`

| File | Contents | Used by |
|---|---|---|
| `PriceModel.cs` | `ProducerAsk` (cost × margin × fullness; with credits, a rich producer gives up its margin), `ConsumerWillingness` (base value × shortage; with credits and income, × affordability), `PilotMinimumFee`, `TravelTicks` (distance ÷ speed, rounded up) | `DecisionEngine`, `TransitEvents`, `Market`, `PlayerActions`, `DealEstimate` |
| `Visibility.cs` | `CanSee`: own contracts, the contract's region or any of its places in the home region, a place at home or adjacent, region reputation over 0.05, or a fresh scout; `OpenContractsVisibleTo` | `DecisionEngine`, `BoardView`, `LogView` |
| `InflationMonitor.cs` | `Evaluate`: drift of total credits beyond ±8% moves `FaucetMultiplier` by 7% | `InflationCheck` |

### Engine: `Events/`

| File | Contents | Used by |
|---|---|---|
| `ISimEvent.cs` | `ISimEvent` | events, `EventQueue`, `SimulationContext` |
| `EventQueue.cs` | Min-heap by tick, ties by insertion sequence | `SimulationContext` |
| `ControlEvents.cs` | `ActorHeartbeat`, `ContractExpiryCheck`, `GraceEnded`, `DefaultReview`, `InflationCheck`, `ReputationDecay` | scheduled by `WorldGenerator`, `Market` (expiry at posting; the grace's end at signing what someone waits on), `DecisionEngine.ReviewDefault` (reviews), and themselves |
| `TransitEvents.cs` | `ShipDeparted`, `TransitIncidentCheck`, `TransitIncidentOccurred`, `ShipArrived` | `ShipDeparted` scheduled by `Market.Dispatch`; the rest by each other |

### Engine: `Random/`, `Simulation/` and `Properties/`

| File | Contents | Used by |
|---|---|---|
| `Random/DeterministicRandom.cs` | xorshift128+ seeded by SplitMix64; `NextDecimal`, `NextLong`, `Chance`, `Pick`; counts `DrawCount` | `SimulationContext.Rng` → `WorldGenerator`, `TransitIncidentCheck`, `IncidentResolver` |
| `Simulation/SimulationContext.cs` | The world (section 4) | everything |
| `Simulation/Market.cs` | `Signing` (Signed, NotOpen, PayerCannotFund, SupplierDeclined), `FillSourceKind`, `FillSource`; `Market`: `PostSale` (a package, a lot in storage warranted as seen), `PostHaul` (a package, the goods' declared value, a cause), `Withdraw` (a cause), `Expire` (internal), `CancelForDefault`, `CancelAtWill`, `CanFund`, `CanFundHaul`, `Supply`, `BuyLot`, `TakeHaul`, `BuyAtSource`, `FillWithShip`, `FillViaHaul`, `UnderWay`, `ExpectedArrival`, `NextFree` (where and when a ship is next free), `TravelTo`, `Dispatch` (internal); private `AwaitPerformance` (schedules `GraceEnded`), `Sign` (locks escrow and bond), `Reserve`, `ReleaseOpen` | `DecisionEngine`, `PlayerActions`, `ControlEvents`, the views (`NextFree`, `ExpectedArrival`) |
| `Simulation/Settlement.cs` | `SettlementTerms` (gross, refund, late fee, liability, tax; `Payment`, `Penalties`, `Net`) and `Due` (pro rata; the late fee then the liability, limited by `Collectable`); `HandOver` (Done, Defaulted, Refused, Pending); `CanPay`; `HandOverReserved` and `HandOverCargo` (inspection: a floor, the AI's `WouldReject` or the player's decision; a short delivery under a strict term fails), `DeliverHaul` (the warranty on decay in the hold; rejection and strict shortness fail it; liability for goods lost); `Fail` (bond to the payer, escrow back, deep-Accord liability, set-aside goods freed, a lost haul fails the Sale it serves); `Cancel` (for default or at will, the break fee); `PayerDefault`; `Keep`; `Unreserve`; `Late`; `Load`, `Unload` (a ship's job at its pickup and destination; a cancelled haul's goods still set down); reputation rewards and blame | `Market`, `TransitEvents`, `ControlEvents`, `PlayerActions`, `DecisionEngine` (`Due`), `DealEstimate` (`Due`, `Collectable`), `DecisionView` (`Due`) |
| `Simulation/Terms.cs` | The three packages for Sales and Hauls (a table in its doc comment: grace, late fee and cap, break fee, short delivery, warranty and floor, liability), every figure from the posted price; `ForSale` (a lot in storage warranted as seen), `ForHaul` (liability at the declared unit value), `PackageFor` (by personality; the player standard), `Meetable` (what a buyer of perishables relaxes to), `TaxRate` | `Market`, `DecisionEngine`, `PlayerActions` |
| `Simulation/Decisions.cs` | `DecisionKind` (CancelForDefault, Inspection), `DecisionChoice` (Cancel, Wait, Accept, Reject), `PlayerDecision` (kind, contract, raised, the entry that raised it, goods inspected; `Choices`); `FailureKind` (the design §16.3 paths the soak counts) | `SimulationContext`, `GraceEnded`, `Settlement`, `PlayerActions`, `GameSession`, `DecisionView`, `Soak` |
| `Simulation/SimulationConfig.cs` | Tuning knobs: heartbeat 24, inflation check 400 / ±8% / 7%, reputation decay, tax 4%, docking ₵3, incident scale, reputation gain and loss, scout freshness, working capital (consumer 90 days' income, producer 4 warehouses at cost, trader 1.5 × fleet value) | `SimulationContext.Config` |
| `Simulation/WorldGenerator.cs` | The sector: locations (with map points), routes (hours and danger), producers, consumers, government, traders, optional player, station operators (₵0.10 a unit-day, ₵2,000 float); `WorldHandles` | `Soak`, `Fingerprint`, `GameSession`, `BenchPanel`, tests |
| `Simulation/IncidentResolver.cs` | `TransitIncidentKind`, `TransitIncident`, `Resolve` (roll → delay, damage, partial loss, credit cost, total loss; severity from the ship), `Share(kind)` (the table's odds per kind) | `TransitIncidentCheck`, `TransitIncidentTests`; `Share` by `ContractView` |
| `Simulation/Invariants.cs` | `Check`: credit conservation, escrow reconciliation, no negative escrow and nothing left in a settled contract's, goods conservation per good (produced = consumed + lost + held) with no negative or over-reserved stock or storage and no overfull hold, and every signed contract being performed (a haul has a ship flying it; a Sale has its goods set aside or under way); `InvariantViolation` | `Soak`, tests (`TestWorld.AssertSound`) |
| `Simulation/EventJournal.cs` | `JournalEntry` (`Seq`, `Cause`) and its fourteen kinds (section 3), `IContractEntry`, `FailureReason`, `EventJournal` (a ring of 5,000: `Record` stamps and returns, `Find`, `Entries`, `Tail`, `TotalRecorded`) | `SimulationContext.Record`; `LogView`, `JournalText`, `ContractView.Why` |
| `Simulation/StateFingerprint.cs` | `Of(context)`: tick, draws, money supply, settlement counters (fulfilled, breached, defaulted, cancelled, goods sold) and every actor's credits, as one string | `TestWorld.Signature`, the CLI's `fingerprint`, the client's `BenchPanel` |
| `Simulation/GameTime.cs` | One tick = one game hour. `Day`, `HourOfDay`, `At`, `Days`, `FormatDate` ("Day 4 · 14:00"), `FormatDuration` ("1d 6h"), `FormatRelative` ("in 1d 6h") | `Soak`, tests, `Format` (every date the client shows) |
| `Simulation/RunStop.cs` | Why `RunTo` returned: `ReachedTarget`, `Drained`, `PlayerAlert`, `PlayerDecision` | `SimulationContext.RunTo`, `GameSession.Advance` |
| `Properties/AssemblyInfo.cs` | `InternalsVisibleTo("ByGalacticAccord.Tests")` | the tests (internal `Sign`, `Dispatch`, stock reservations) |

### Soak harness: `src/ByGalacticAccord.Cli/`

| File | Contents |
|---|---|
| `Program.cs` | Commands `soak [ticks] [--seed N]` (default 200,000 ticks, seed 20260619), `fingerprint [ticks] [--seed N]` (default 20,000) and `help` |
| `Soak.cs` | Runs in 5,000-tick chunks, `Invariants.Check` after each (exit code 1 on a violation), then prints telemetry: money, settlement counters, every failure path's count, signed contracts and their failure rate by term package, the goods table, demand met per consumer, contracts by kind and status, trader solvency, wealth |
| `Fingerprint.cs` | A headless run's `StateFingerprint`, which the browser check compares with the client's |

### Presentation: `src/ByGalacticAccord.Presentation/`

Everything the interface says or draws, as plain records built from the session; the client only renders
them. A view never changes the world; commands go through `GameSession`.

| File | Contents | Used by |
|---|---|---|
| `Text/Templates.resx` | Every interface sentence and label (design §11.5): 432 templates with named `{fields}`, keyed by journal entry type and contract kind (`ContractSigned.Sale.Wanted`, `ContractCancelled.AtWill.Payer.Haul`, each with `.You` and `.ToYou` twins where the player can be either party) and fact type (`Good.Food`, `Package.Strict`, `Term.Default.Them.Frontier`, `Decision.Cancel.Note`, `Ledger.Storage`, …) | `Templates` |
| `Text/Templates.cs` | `Get`, `Fill(key, (name, value)…)` (throws on a missing key or field), words for the engine's enums (`Good`, `Jurisdiction`, `JurisdictionShort`, `JurisdictionLong`, `Danger`, `LocationKind`, `Role`, `ContractKind`, `Side`, `Package`, `ShipClass`), `KindLine` ("Sale · wanted") | every view, the client's components |
| `Text/Format.cs` | `Money` ("₵1,234", "−₵42"), `MoneyCents`, `Quantity`, `Percent`, `Points`, `Hours`, and `GameTime`'s dates, all invariant | every view |
| `Text/JournalText.cs` | `Describe(context, entry)` (the entry's sentence), `Subject`, `ContractOf` (any `IContractEntry`), `Kind(contract)` | `LogView`, `HeaderView` (alerts), `ContractView.Why` |
| `Session/GameSession.cs` | `ClockSpeed`, `DealOutcome` (a `DealBlock`, or the `Signing`), `GameSession` (from a seed, or over a world already made): the world with the player, the clock (`SetSpeed`, which won't start while a decision waits; `Advance(realSeconds)` capped at `MaxFrameSeconds`, stopping on an alert or a decision; `Clock` with its fraction, `Revision`), `Alerts`, `Decisions`, `PlayerShip`, `Accept` (haul, fill or buy by kind), `Broker`, `HaulAgain`, `CancelAtWill`, `CancelForDefault`, `Decide`, `Store`, `Retrieve`, `PlayerActiveContracts`, `PlayerOpenPosts` | `Shell`, the views, tests |
| `Views/DealEstimate.cs` | `DealKind` (Haul, Fill, Buy, Broker), `DealEstimate`: what a contract is worth to the player's ship by the engine's rules (income; goods bought and their tax; running costs for the empty and laden legs; docking; the certain warranty refund (a haul's on decay in the hold) and late fee, limited as `Settlement.Collectable` limits it; the bond; incident odds; the source; through a carrier, the haul fee and its tax), `TakeHome`, `Cost`, `PerDay`, from where and when the ship is next free; `ForBroker` | `BoardView`, `ContractView`, `MapView` |
| `Views/BoardView.cs` | `BoardRow` (with its `ContractType` and term package), `BoardViewModel`: open Sales and Hauls the player can see (`Visibility`), newest first, with take-home (or a lot's cost) and one note | `Shell` |
| `Views/ContractView.cs` | `MoneyLine`, `InfoLine`, `ContractCommand` (Broker, CancelAtWill, CancelForDefault, HaulAgain), `ContractAction`, `ContractViewModel`: the contract as a document per kind (breakdown, schedule, uncertain inputs, every term as a sentence from the player's side under "Terms · package", party, status (in default, partly fulfilled, cancelled and by whom), the accept label, `Actions`, and `Why`: its record read back through its causes, newest first, six at most, stopping where the journal no longer keeps it or the player can't see it); `Check`, `Head`, `BlockText`, `OutcomeText`, `BrokerText`, `CommandText`, `Why` | `Shell`, `BoardView`, `LedgerView` (`Head`) |
| `Views/DecisionView.cs` | `DecisionOption`, `DecisionPrompt`, `DecisionView.Build`: each pending decision as the prompt that stops the clock (what happened, what is on its way, each answer with what the terms make of it) | `Shell` |
| `Views/MapView.cs` | `MapNode`, `MapRoute`, `MapLeg`, `MapRegion`, `MapShip`, `MapViewModel`, `LabelAnchor`: the schematic map, the selected contract's overlay and title per kind, region frames, label placement; `Ships(session, clock)` and `Along` (a course's position by share of hours) | `Shell`, `SectorMap`, `MapLayers`, `ShipLayer` |
| `Views/LogView.cs` | `LogLine`, `LogViewModel`: the journal's visible entries, newest first, 40 at most; other actors' routine (lots that expired, withdrawals, storage) left out | `Shell` |
| `Views/HeaderView.cs` | `SpeedButton`, `HeaderViewModel`: speeds (and "waiting for your decision"), date, credits, escrow (the player's share), running cost, the fleet line (by the ship's job), alerts | `Shell`, `LedgerView` |
| `Views/LedgerView.cs` | `LedgerAction` (store here, or load aboard), `LedgerLine`, `LedgerViewModel`: money, what the player owns and where (holds and storage, with freshness and rent), its signed contracts with its role (carrying for, shipping with, selling to, buying from) and the figure, flagged in default, and its own open posts | `Shell` |
| `Views/Tone.cs` | `Tone`: what a value's colour encodes (gain, loss, warn, yours, muted, plain) | every view; CSS classes `tone-*` |

### Client: `src/ByGalacticAccord.Client/`

| File | Contents |
|---|---|
| `Program.cs` | The Blazor host; `Shell` is the root component |
| `Shell.razor` | Reads `?seed=` (default 20260619) and `?bench=`; builds the `GameSession` (started at Day 1 · 06:00); runs the frame loop; holds the selection and the last command's outcome; rebuilds the view models (decisions too) when `Revision` or the selection changes, and the ships every frame; `Accept`, the contract's `Command` (broker, walk away, cancel for default, raise the haul), `Decide`, and the ledger's `Act` (store or load) |
| `Components/TimeBar.razor` | Title, speed buttons (`data-speed`), date, money, theme picker |
| `Components/SectorMap.razor` | The map panel: `MapLayers` and `ShipLayer` in one SVG, the overlay card, the legend, the fleet line |
| `Components/MapLayers.razor` | Ground, regions, routes (`band-*` classes carry colour and dash), overlay legs, hours, locations; renders only when its model changes |
| `Components/ShipLayer.razor` | Ships at their interpolated positions; renders every frame |
| `Components/BoardPanel.razor`, `ContractPanel.razor`, `CaptainsLog.razor` | The board (each row's kind and term package), the contract document (its terms, its "why" chain whose lines select their contracts, the accept button and the other actions with what each does), the log (a line about a contract selects it) |
| `Components/DecisionPanel.razor` | A decision that stopped the clock: what happened (selects the contract), and a button per answer with its note |
| `Components/LedgerPanel.razor` | The ledger: money, possessions with their store or load button, contracts (each selects itself) |
| `Components/BenchPanel.razor` | `?bench=N`: runs a world with no player for N hours, shows the time and the `StateFingerprint` |
| `wwwroot/index.html`, `css/app.css`, `js/client.js`, `fonts/` | The page; colour tokens with dark (default) and light themes (`prefers-color-scheme`, or `data-theme` on `<html>`); the theme's storage, guarded; IBM Plex |

### Browser check: `tools/client-check/`

| File | Contents |
|---|---|
| `check.cjs` | With no arguments: `dotnet publish` the client to `artifacts/client-check/publish`, get the desktop fingerprint from the CLI, serve the site, then in Chromium: load (paused, Day 1 · 06:00, the board with each row's kind and terms, map, ledger), select a contract (its terms as sentences, its "why"), accept it (the log and the ledger record it), run 16× (resuming alerts, answering decisions), the goods aboard in the ledger, fill a wanted Sale through a carrier (the ledger lists the sale and the posted haul; the contract offers what can still be done), a decision prompt if the run raised one, light theme, phone width (no sideways scroll), the speed probe and the fingerprint; screenshots to `artifacts/client-check/screenshots`; fails on any console error, page error or failed request. `--site`, `--out`, `--fingerprint` override. |
| `package.json`, `package-lock.json` | Playwright 1.63.0 |

### Tests: `tests/ByGalacticAccord.Tests/`

| File | Covers | Soak variants |
|---|---|---|
| `TestWorld.cs` | Helpers: `RunHeadless`, `Quiet` (no heartbeats, incidents off unless asked: a test drives the market itself), `Signature`, `Actor`/`Place`/`Player`/`ShipOf` by name, `RunUntilSettled`, `PostWanted` (standard terms unless asked), `AssertSound` (every invariant), the `Soak` trait constants | — |
| `DomainTests.cs` | `Credits`, `PenaltyOnLateDelivery`, `QualityWarranty`, `Cargo` decay, `FindPath` (multi-hop, unreachable, cache), `OpenContracts`, the good and hull tables | — |
| `GoodsTests.cs` | Keep-times against design §15.3 and refrigeration (food reaches the frontier in a standard hold, medicine only refrigerated); merging parcels; lazy decay; conservation catches a unit that vanishes unbooked | — |
| `SaleTests.cs` | A Sale's lifecycle: answered at once at a producer's door; filled by buying and carrying; a lot bought whole and collected, or expiring; a default outside the Accord; the late fee from the bond; a warranty refund; a short delivery pro rata; in default with nothing coming, cancelled by the buyer for the bond; lost cargo failing a haul and its Sale; buying below the ask | — |
| `TermsTests.cs` | The packages' terms from the price; a haul's liability at the declared value; terms that give nothing away; a lot in storage warranted as seen; packages by personality; `Meetable`; grace and default | — |
| `FailurePathTests.cs` | Every design §16.3 path S4 has, for an AI actor and through the player's commands (its doc comment maps each path to its tests): late, in default (the AI waits, the player is asked, cancels or waits, the player defaults), short (strict failure, pro rata with liability; deep Accord vs edge), rejection (AI, and the player rejecting or accepting), walking away (AI and player), the player's payer default, the player's haul raised then expired | — |
| `HaulAndStorageTests.cs` | A producer's delivered Sale and its Haul; a lot bought into storage and hauled home; an untaken haul expiring; storage rent and spoilage; goods set aside in storage not used twice | — |
| `DecisionTests.cs` | Each role's rules in a quiet world: consumers post and learn their price, producers' lots and asks, traders fill and broker, the working-capital payout | — |
| `NegotiationTests.cs` | `Negotiation.ResolveAi`, `NegotiationSession` | — |
| `TransitIncidentTests.cs` | `IncidentResolver`: deterministic, armour milder than courier, danger raises credit costs | — |
| `GameTimeTests.cs` | `GameTime` | — |
| `DeterminismTests.cs` | Same seed → same `Signature`; stepped replay; seeds diverge | 150k, 120k |
| `InvariantTests.cs` | `Invariants.Check` at checkpoints; credits; escrow reconciling exactly; no negative balances | 1M, 250k |
| `EconomyTests.cs` | Economy comes alive (every kind settles, every consumer consumes every good); frontier reachable | 200k; every failure path over 1M; money supply over 1M; no monopoly over 500k |
| `JournalTests.cs` | Entries match how each contract ended; every lifecycle entry names an earlier cause; a lost cargo reads back to its incident; the ring buffer and `Find` | — |
| `ShipCourseTests.cs` | A course agrees with its ship and job every hour; a haul settles at its course's arrival, delays included | — |
| `PlayerActionsTests.cs` | Each player command through `Market` and each refusal (filling through a carrier among them); storing and retrieving; running costs every day; an alert stops `RunTo` | — |
| `TemplateTests.cs` | `Fill`; every template well formed; every enum word; every journal entry (in default and cancelled among them) worded for each kind, for an AI and for the player on either side | — |
| `FormatTests.cs` | Money, cents, percentages, quantities | — |
| `SessionTests.cs` | Speeds, the clock's fraction, the frame cap, the same world as one `RunTo`, the alert and the decision's hard pause, `Accept`, `Broker`, `HaulAgain`, `CancelAtWill`, `Store` and `Retrieve` | — |
| `DealEstimateTests.cs` | The estimate predicts the settlement of a clean haul, a late haul, a fill bought at the source and a lot, to the cent; empty legs; a busy ship | — |
| `ViewTests.cs` | Every view builds through a played game (deals of every kind, storing goods, filling through a carrier, answering decisions); the board's kinds and order; the ledger; the overlay's legs; interpolation along a path; the terms as sentences for every package and side; the decision prompts; the "why" chain; the contract's actions | — |

## 7. Determinism rules (design §20.1)

- Randomness comes only from `context.Rng`, and only inside events or world generation. Draws today:
  `WorldGenerator` (personalities, first heartbeats), `TransitIncidentCheck`, `IncidentResolver`. The client
  draws nothing: its clock decides only *when* `RunTo` is called, and runs in any increments give the same world.
- Anything that draws randomly or changes state in a loop iterates a stable order: `ActorsOrdered`,
  `OpenContracts`, `LiveContractsOf`, `ProducersOf`, a location's `Residents`, storages and holds (lists in
  insertion order). A dictionary's enumeration order is used only where order cannot change the result (sums,
  counts, the goods ledger, the invariant checks, reputation decay).
- `EventQueue` breaks tick ties by insertion order.
- Money is `decimal` throughout.
- Checked by `DeterminismTests` (fast and soak), `SessionTests.The_clock_runs_the_same_world_as_one_RunTo`, the
  soak harness's identical telemetry across runs, and the browser check, which compares the client's
  WebAssembly run with the desktop one (`StateFingerprint`, 20,000 hours, seed 42).

## 8. Known defects and where they live

From plan §1; the step named fixes each. S3 fixed the player's running costs, food's keep-time, the frontier's
goods cost, the same-place hauls and (early) the reservation leak through the terms. S4 replaced the producer's
failed Sale with the terms' remedies: a seller no carrier can save walks away for a break fee, or its buyer cancels
for its bond.

| Defect | Where | Step |
|---|---|---|
| Nobody can go below ₵0 | `SimulationContext.ApplySink` and `TransferUpTo` cap at the balance; `Market.Sign` bonds `min(bond, credits)` | S8 |
| AI actors pay out whatever they hold above a working capital (a sink standing in for spending, growth and failure) | `DecisionEngine.WorkingCapital`, `PayOut` | S7, S8 |
| A producer selling delivered still loses the Sale (now by walking away, or cancelled for default) when nobody takes its Haul in time: about as many succeed as in S3 | `DecisionEngine.HaulTerms`, `RaiseHaulOffers`; carriers take hauls as posted | S5 |
| An actor can't change its fleet, so a hull that doesn't pay (Dust Runner Kael's armoured freighter) bleeds its owner | `WorldGenerator`; `ChargeRunningCosts` | S7 |
| Visibility is nearly omniscient | `Visibility.CanSee`'s home-region rule (Helios is 5 of 7 locations) | S6 |
| Incident outcomes don't shift with danger | `IncidentResolver.Resolve`: the roll isn't weighted by danger, despite its comment | S8 |

## 9. No callers (left from the retired clients, or waiting for a step)

`Negotiation.ResolveAi` and `NegotiationSession` (tests only); `ISimEvent.Describe` (never
called); `ContractStatus.Negotiating` (never set); `Ship.History` (written, never read), `TransferOwnership`;
`ActorState.AllStock`; `EventJournal.Tail` (tests only); `SimulationContext.Neighbours`, `TaxDirect`;
`DeterministicRandom.Pick`; `Location.HasEscrow`, `HasArbitration` (contracts read their jurisdiction:
`Contract.UsesEscrow`, `Arbitrated`); `ShipClassInfo.Niche`, `RunsHot` (data only:
push is not implemented); `TransitIncident.Description` (only `Describe` and the ship's text log; the journal's
text comes from the templates); `ReputationLedger.Snapshot`; `SimulationConfig.NegotiationMaxRounds`. Delete or
wire them as the steps rework their areas.

## 10. The client's frame

```
Shell.OnInitialized → new GameSession(seed, startTick: Day 1 · 06:00), paused → Rebuild()
Shell.RunClock: PeriodicTimer, 33 ms
  └─ each frame while running: GameSession.Advance(real seconds since the last frame, at most 0.25)
       clock += seconds × speed → RunTo(floor(clock)) → Revision++ ; a PlayerAlert or PlayerDecision stops the clock
     → Rebuild(): if Revision or the selection changed, Header/Board/Map/Log/Ledger/Decision/ContractView.Build
                  always MapView.Ships(session, clock)  (positions along ShipCourse, by share of hours)
     → StateHasChanged: components whose model object is the same skip rendering; ShipLayer always renders
Player input → Shell.SetSpeed (refused while a decision waits) / Select / Accept (GameSession.Accept → PlayerActions → Market)
             / Command (GameSession.Broker, HaulAgain, CancelAtWill, CancelForDefault → PlayerActions → Market)
             / Decide (GameSession.Decide → PlayerActions.Decide → Market or Settlement)
             / Act (GameSession.Store or Retrieve → PlayerActions) → Rebuild
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
