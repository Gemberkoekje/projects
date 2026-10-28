# By Galactic Accord — code map

What is where, what calls what, and where money and goods move. Use it to find the file, then read the
file: the code is the truth. **Last checked against the code: 2026-10-28, after S6.**

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
  entry records its cause, so any outcome reads back as a chain. S5 adds **haggling** (design §3.6, §16.5): an
  `Offer` (price, deadline, package, late fee) can be countered; two AI actors settle a haggle at once in closed form
  (`Haggling.Resolve`, the same rules played out), while the player's posts reach the AI actors that can see them and
  draw acceptances or counters over game time, and the player's counters get their replies after 1–12 game hours, each
  with its tells (reply time and wording). An agreed offer re-scales the contract's terms. Every actor, the player too,
  acts through `Market`. Headless runs (soak harness, most tests) have no player; the client's `GameSession`
  generates the world with one, who can post any Sale or Haul, counter, accept or decline offers, withdraw its posts,
  carry a haul, fill a wanted Sale with its ship or through a carrier, buy a lot, store goods, walk away from a
  contract, cancel one for default, raise its haul, and answer decisions. Bot players (`Presentation/Bots`) play it
  headless the same way.
- **Information (S6, design §7):** every actor knows what its **presence** lets it see (`Economy/Visibility.cs`): its
  home, its ships (where they are, or where they are heading) and its storage are **here** (every contract at once, live
  prices and stock); one jump from them is **near** (contracts four hours after posting, the morning **bulletin**'s asks);
  the rest is **far** (no contracts but the news, prices as last seen). Each actor keeps a **knowledge store**
  (`Economy/Knowledge.cs`: `ActorKnowledge` on `ActorState.Knowledge`) of its last reading of each producer's ask and
  stock, with timestamps, and decides on it, trusting what it last saw (D32): consumers price wanted Sales from the asks they
  know, traders and the player plan fills from them (`FillSources`), and an order at a producer's door pays the ask as it
  stands, up to a tenth over what the buyer expected. **The Daily Accord** (`Economy/News.cs`) reports three of yesterday's
  contracts each morning, by weighted chance (size, novelty, failure, the frontier); a post it reports open is known to
  everyone until its deadline, whether or not it has been taken since, and an actor finds out by trying. The **public
  record** (`Economy/TrackRecord.cs`) counts how every Accord contract ended, per actor, and each actor remembers its own
  dealings. The client fades stale information, labels it "as of …", shows signals and the news.
- **Where it is going:** Slice 1 of the design, in the steps of [`plan.md`](plan.md) (S7, ships from parts, is next).
  Ships (S7) and money's limits (S8) change shape next; plan §1 lists what is kept, reworked and retired.
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
| Presentation | `src/ByGalacticAccord.Presentation/` | Engine | What the interface shows, as data: the text templates, the view models, the game clock; and the bot players, which play through it. No Razor, no browser. |
| Client | `src/ByGalacticAccord.Client/` | Presentation, Blazor WebAssembly | The browser client: renders the view models, runs the clock's frames. |
| Soak harness | `src/ByGalacticAccord.Cli/` | Engine, Presentation | Console app: headless runs, telemetry, invariant checks, state fingerprints, bot players. |
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
     Knowledge.Seed (everyone knows the sector as it starts); PricesPosted and NewsPublished, daily from Day 0 · 06:00
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
| `ActorHeartbeat` | `Events/ControlEvents.cs` | `DecisionEngine.ChargeRunningCosts` for every actor, **the player too**; `Knowledge.Look` (it reads what it sees where it is present); then `DecisionEngine.Run` unless it is the player | Itself, `HeartbeatInterval` (24) later |
| `ContractExpiryCheck` | `Events/ControlEvents.cs` | Still open at its deadline → `Market.Expire` (frees what the post set aside) | — |
| `GraceEnded` | `Events/ControlEvents.cs` | At `Contract.GraceEndsTick` (deadline + grace), scheduled when someone waits on a performer (`Market.Supply` unless handed over at once or collected; `TakeHaul`). Still `Active` → in default: records `ContractInDefaultEntry`; a player payer gets a `PlayerDecision` (the hard pause); an AI payer `DecisionEngine.ReviewDefault`s | `DefaultReview` (an AI that waits) |
| `DefaultReview` | `Events/ControlEvents.cs` | Still in default, AI payer → `ReviewDefault` again | Itself via `ReviewDefault`, a day later |
| `InflationCheck` | `Events/ControlEvents.cs` | `InflationMonitor.Evaluate` | Itself, every 400 |
| `ReputationDecay` | `Events/ControlEvents.cs` | `ReputationLedger.DecayTowardNeutral` | Itself, every 400 |
| `ShipDeparted` | `Events/TransitEvents.cs` | The ship reaches its job's pickup (`Ship.ReachPickup`), its owner looks around there (`Knowledge.LookAt`), and `Settlement.Load`s; while the player inspects what it bought there, it reschedules itself and waits; a job with no destination (collecting a lot) ends here; nothing aboard → the Sale it serves fails; else `Ship.DepartLaden` | `TransitIncidentCheck` at mid-route; itself (waiting on an inspection) |
| `TransitIncidentCheck` | `Events/TransitEvents.cs` | `Rng.Chance(scale × danger × ship modifier)` | `TransitIncidentOccurred`, or `ShipArrived` |
| `TransitIncidentOccurred` | `Events/TransitEvents.cs` | Applies the `TransitIncident` to the job owner's lot (`Ship.Alter`: damage, partial or total loss through `Lose`), a credit cost (`ApplySink`), `Ship.DelayArrival`; records an `IncidentEntry`; raises it as a player alert if the ship is theirs | `ShipArrived` (plus any delay) |
| `ShipArrived` | `Events/TransitEvents.cs` | `Ship.Arrive`, docking fee, `Settlement.Unload` (a haul cancelled on the way still sets the shipper's goods down, and they go on to the Sale it served), `Ship.FinishJob`, its owner looks around there (`Knowledge.LookAt`) | — |
| `PostReached` | `Events/NegotiationEvents.cs` | The player's post reaches an AI actor that can see it, which answers: `DecisionEngine.Respond` takes it as posted (`TakeOn`), counters (`Market.Counter`), or lets it pass. Scheduled by `DecisionEngine.NoticePost` at posting, after the actor's reach (`Sight.ReachHours`) and its reply time; by `NewsDesk.Publish` for the actors that learn of it from The Daily Accord | `ReplyDue` (through `Market.Counter`) |
| `ReplyDue` | `Events/NegotiationEvents.cs` | An AI's reply to the player's offer: `DecisionEngine.Reply` accepts (`Market.AcceptOffer`), counters, or ends it (`Market.EndNegotiation`); dropped if the haggle moved on or ended. Scheduled by `DecisionEngine.ExpectReply` 1–12 hours after the offer | — |
| `PricesPosted` | `Events/InformationEvents.cs` | The morning bulletin at 06:00 (design §7.1): `PriceBulletin.Post` records every producer's ask; `Knowledge.ReadBulletin`: every actor notes the asks of places one jump from its presence | Itself, a day later |
| `NewsPublished` | `Events/InformationEvents.cs` | The Daily Accord at 06:00, after the bulletin: `NewsDesk.Publish` gathers every contract with a journal entry yesterday, words each by where it stands, draws `NewsItemsPerDay` (3) by weighted chance from `Rng`, makes a producer's lot known (`Knowledge.ReadLot`) and sends the player's reported posts to the actors that couldn't see them (`DecisionEngine.NoticePost(fromNews: true)`). Prints nothing when `NewsItemsPerDay` is 0 | Itself, a day later |

Every transit event carries the `ShipJob` it belongs to and does nothing if the ship's job is no longer that
one (`ReferenceEquals`).

**A delivered Sale's life** (consumer → trader): `RunConsumer` → `PostWanted` (priced from the cheapest ask the consumer
knows, `Knowledge.ReadingOf`; its package from personality, relaxed by `Terms.Meetable` to what a standard hold can meet)
→ `Market.PostSale` (Wanted, receipt = the consumer's stock at home; terms from `Terms.ForSale`; schedules
`ContractExpiryCheck`; records `ContractPostedEntry`; `DecisionEngine.AnswerAtOnce` lets an AI producer at the place
answer it at once) → a trader's `RunTrader` → `BestOption` over what it believes open (`Known`: seen live by its tiers,
or reported in the news; tried twice if the first has been taken since) → `FillSources` (the asks it knows,
`Knowledge.KnownAsk`), `ScoreFill` (arrival inside the grace, goods the buyer won't reject, the terms' risk) → `Commit` →
`Market.FillWithShip` (signs; buys the goods at the source with `BuyAtSource`, an order at the producer's door at the ask
as it stands, up to `DoorLimit` of what the trader expected; turned down, the trader leaves that producer out for a day;
`Dispatch`es the ship with a
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

**Haggling between AI actors** (design §3.6, closed form): a trader scoring a post (`ScoreHaul`, `ScoreFill`, `Broker`),
a producer answering or selling delivered (`TryAnswer`, `SellDelivered`) and a consumer buying a lot (`TryBuyLot`) take it
as posted if it clears their floor; if not, and the AI poster would give more (`PosterReservation`: nothing on a fresh
post, up to a personality's share of the way to its hidden reservation once it has waited two days, at once for a haul
serving a Sale already signed; never more than it can pay), `Haggling.Resolve` plays the rounds out at once and the deal
signs at the offer accepted, through `Market.SignAgreed`, which re-scales the terms from the agreed price
(`Terms.For`). The posters' `PriceMemory` learns from a haggled price. `SimulationContext.DealsHaggled` counts these.

**A haggle with the player** (design §3.6, §16.5, §16.6): the player's `PlayerActions.Post` → `Market.PostSale`/`PostHaul`
→ `DecisionEngine.NoticePost` schedules a `PostReached` for each AI actor that can see it and could answer (carriers,
producers of the good, consumers of a lot's good) → `Respond`: as posted it signs (`TakeOn`); else `Market.Counter` opens a
`Negotiation` whose first bid is the post → the player answers (`PlayerActions.AcceptOffer`, `Counter`, `Decline`, or
`Withdraw` the post). On an AI's post the player counters first (`PlayerActions.Counter` → `Market.Counter`). Each offer to
an AI schedules its reply (`ExpectReply` → `ReplyDue` → `Reply`), decided by `Decide` on the AI's `Quote` (its reservation
on the offer's terms and the nearest terms it can meet): accept if it crosses, walk away if too wide or after six rounds,
else counter (`Haggling.Opening`, `Concede`), with its tell (`Haggling.TellFor`, `ReplyHours`). Accepting signs it by the
responder's own command (`DecisionEngine.TakeOn` or `PlayerActions.TakeOn`) on the agreed terms (`Market.SignAgreed`); one
that can no longer perform ends the haggle. Any signing, withdrawal or expiry ends the other haggles over the contract
(`SimulationContext.CloseNegotiationsOn`). The contract stays open to all meanwhile.

**A lot's life:** `RunProducer` → `PostLot` → `Market.PostSale` (ForSale, the producer's stock set aside) → a
consumer's `TryBuyLot` → `Market.BuyLot` into storage at the place (handed over at once, `HandOverReserved`),
then `BringHome` posts a Haul home; or a ship collects it (`PickupSource.Purchase`), as the player's `Buy` does.

**The player's way in** is `PlayerActions` (`CheckHaul`/`TakeHaul`, `CheckFill`/`Fill`, `CheckBuy`/`Buy`,
`CheckBroker`/`Broker` (fill through a carrier, as `Broker` does), `CheckHaulAgain`/`HaulAgain` (raise or replace its
haul, as `RaiseHaulOffers` does), `CheckPost`/`Post`, `CheckCounter`/`Counter`, `AcceptOffer`, `Decline`, `Withdraw`,
`TakeOn`, `CancelAtWill`, `CancelForDefault`, `Decide`, `Store`, `Retrieve`), the same `Market` and `Settlement` commands
an AI decision makes, called by `GameSession.Accept`, `Broker`, `HaulAgain`, `Post`, `Counter`, `AcceptOffer`, `Decline`,
`Withdraw`, `CancelAtWill`, `CancelForDefault`, `Decide`, `Store` and `Retrieve`.

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
| `OfferMadeEntry` (haggle, by, to, the `Offer`, round, reply hours and `Tell` for a reply) | `Market.Counter` |
| `OfferAcceptedEntry` (haggle, by, to, the offer, reply hours, tell) | `Market.AcceptOffer` |
| `NegotiationEndedEntry` (haggle, by, to, `NegotiationEnd`: walked away, rounds ran out, declined, could no longer perform) | `Market.EndNegotiation`, `AcceptOffer` |

`Presentation/Text/JournalText.cs` words each one from `Templates.resx`, keyed by the entry type's name
(without "Entry") and the contract's kind (`Sale.Wanted`, `Sale.ForSale`, `Haul`), with a `.You` twin when the
player is the subject and a `.ToYou` twin when the player is the other party. Every entry about a contract
implements `IContractEntry`; a haggle's entries implement `INegotiationEntry` and chain among themselves from the post
(`Record` keeps each haggle's latest), so a haggle that came to nothing leaves the contract's record alone, and only an
accepted offer becomes the contract's latest, the cause of its signing.

## 4. Where state lives: `SimulationContext`

`Simulation/SimulationContext.cs` (864 lines) is the world and the hub; nearly everything takes it.

- **Collections:** `Actors`, `Ships`, `Locations`, `Contracts`, `Negotiations` (dictionaries by typed id; contracts
  and haggles are never removed), `Routes` (list). Registered with the `Add(...)` overloads; `Actor(id)`, `Ship(id)`,
  `Location(id)`, `Contract(id)`, `Negotiation(id)` look up; `NegotiationsOn(contract)` (the open ones, pruned) and
  `NegotiationWith(contract, responder)`; internal `CloseNegotiationsOn` ends them when a post leaves the board.
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
  of 5,000 entries), `Reputation` (`ReputationLedger`), `Config` (`SimulationConfig`), `Bulletin` (`PriceBulletin`: the
  morning's asks), `News` (`NewsDesk`: The Daily Accord's issues and the posts it made known), `Records` (`Records`: the
  public record and each actor's dealings). Each actor's knowledge is on the actor (`ActorState.Knowledge`).
- **Bookkeeping the invariants read:** `InitialCredits`, `TotalFaucet`, `TotalSink`, `EscrowLockedTotal` /
  `ReleasedTotal` / `ForfeitedTotal`, `TotalEscrowHeld` (over the contracts that ever held escrow),
  `GovernmentRevenue`, the goods ledger per good (`GoodsProduced`, `GoodsConsumed`, `GoodsLost`, `GoodsHeld`:
  stock, storage and holds), counters `ContractsFulfilled` / `Breached` / `Defaulted` / `Cancelled`, `GoodsSold`,
  `DealsHaggled` (AI-to-AI, closed form), `NegotiationsOpened` / `NegotiationsAgreed` (with the player),
  `IncidentsOccurred`, the failure-path tallies (`NoteFailure`, `Failures(FailureKind)`), information telemetry
  (`DoorOrders`, `DoorRefusedForPrice`, `DoorRefusedForStock`, `SignedFromNews`, `FoundClosedFromNews`), and
  `FaucetMultiplier` (the inflation monitor's knob).
- **Other:** `Sign` (internal: `Market.Sign`'s record-keeping), `OpenStorages` and `LocationIdLimit` (for `Visibility.Of`), player
  alerts (`RaisePlayerAlert(JournalEntry)`, `HasPlayerAlerts`, `PlayerAlerts`, `ClearPlayerAlerts`: an alert is
  the entry that needs the player), player decisions (`PlayerDecisions`, `HasPlayerDecisions`,
  `PlayerDecisionFor`; internal `RaisePlayerDecision`, `ResolvePlayerDecision`), `Record(JournalEntry)` (stamps
  the sequence and the default cause, returns the entry as kept; a haggle's entries chain within the haggle).

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
| `LockToEscrow` | Actor → a contract's escrow, **in whole cents** | `Market.Sign`: the payer locks price + tax, the performer a bond (≤ its credits, to the cent), on the terms as agreed (`Market.SignAgreed` re-scales them from a haggled price first) |
| `ReleaseFromEscrow`, `ForfeitFromEscrow` | Escrow → an actor, in whole cents, capped at what the contract holds | `Settlement.Pay` (the payment and bond less penalties to the performer, the penalties and the rest to the payer), `Settlement.Fail` and `Cancel` for default (bond to the payer, escrow back), `Cancel` at will (the break fee from the canceller's share, the rest back) |

A station operator's rent above its float leaves through `PayOut`, as upkeep. `TaxDirect` has no callers since
S3. An order at a producer's door (`SaleContract.AtTheDoor`, posted only by `Market.BuyAtSource`) is posted at the
buyer's limit (`DoorLimit`: 10% over the ask it knew) and signed at the producer's ask as it stands, so escrow locks
the ask, not the limit; a producer whose ask has risen past the limit turns it down and nothing moves.

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
| `Ids.cs` | `ActorId`, `ShipId`, `LocationId`, `ContractId`, `NegotiationId`: long-backed record structs from counters in `SimulationContext` | everywhere |
| `Credits.cs` | `Credits(decimal)`: arithmetic, comparison, `ToString` "₡1,234" | everywhere money is |
| `Goods.cs` | `GoodType` (7), `GoodCategory`, `GoodInfo` (base value, `KeepsHours` in a standard hold, `Refrigeratable`; `Perishable`, `QualityDecayPerTick` from the keep-time), `Goods.Info` table (Food 7 days, Medicine 2: design §15.3), `Goods.StandardWarranty` (0.7) | `Cargo`, `Lot`, `PriceModel`, `Terms`, `DecisionEngine`, `Settlement`, the views |
| `Cargo.cs` | `Cargo(good, quantity, quality)`: `Fresh`, `Decayed(ticks, preservation)`, `LosingUnits`, `Damaged`, `Merged` (weighted quality) | `Lot`, `Ship`, `StorageContract`, `Settlement`, `TransitEvents` |
| `Lot.cs` | `Lot(owner, cargo, since)`: goods held somewhere, decaying lazily; `At(tick, preservation)` | `Ship.Hold`, `StorageContract.Lots`, the ledger |
| `Location.cs` | `AccordReach` (jurisdiction), `LocationType`, `MapPoint` (schematic map units), `Location` (region, reach, residents, `Map`) | `WorldGenerator`, `Visibility`, `DecisionEngine`, `MapView` |
| `Route.cs` | `Route` (two ends, hours, danger 0..1, law, `Band`), `RoutePlan` (distance, worst leg's danger, hops, path, `Band`), `DangerBand` | `SimulationContext.FindPath`, `DecisionEngine`, `TransitEvents`, `Market`, the views |
| `Contract.cs` | `ContractType` (Sale, Haul, Storage created; Build and the rest named), `ContractStatus` (Open (haggled over or not), Active, Fulfilled, PartlyFulfilled, Breached, Cancelled, Defaulted, Expired, Withdrawn), `SaleSide`, `HoldingKind`, `Holding` (stock, storage or aboard a ship, at a location); abstract `Contract` (poster, counterparty, offered and agreed price, hidden `Reservation` (the player's posts: its price), tax, deadline and `PostedDeadline`, `Terms`, `GraceEndsTick`, `InDefault(now)`, status, `Cancellation`, `LastEntry`, jurisdiction, `UsesEscrow`, `Arbitrated`, escrow held, locked bond, `Payer`/`Performer`, `Places`, `LatePenalty`, `Warranty`, `PerformanceBond`; internal transitions, and `Amend` (an agreed offer's deadline and terms, before signing)); `SaleContract` (side, good, quantity, place, buyer/seller, receipt, `Collected`, reserved, delivered, `UnitPrice`, `SeenQuality` of a lot in storage, `AtTheDoor`: an order at a producer's door, never on a board or in the news); `HaulContract` (good, quantity, origin, destination, pickup holding, the Sale it `Serves`, `DeclaredUnitValue`, shipper/carrier, assigned ship, `PickedUp`, `PickupQuality`, `InTransitQuality`); `StorageContract` (operator, renter, rate per unit-day, lots, reservations, rent charged; signs itself; `ContractTerms.None`) | `Market`, `Settlement`, `DecisionEngine`, events, `Visibility`, `Invariants`, the views |
| `ContractTerms.cs` | `TermPackage` (Strict, Standard, Lenient), `PartialDelivery` (ProRata, Failure), `CancelReason` (ForDefault, AtWill), `Cancellation`; `PenaltyOnLateDelivery` (per hour late, capped; `HoursToCap`); `QualityWarranty` (minimum, most refunded, `RejectBelow` floor; `RefundFraction`, `Rejectable`); `CargoLiability` (per unit, cap; `For(lost)`); `ContractTerms` (package, grace, late fee, break fee, partial delivery, warranty, liability, `LateFeeScale`; `Bond` = the late fee's cap; `None`) | `Contract`, `Terms`, `Settlement`, `DecisionEngine`, `DealEstimate`, `ContractView` |
| `Offer.cs` | `Offer` (price, deadline, package, late-fee scale; `Of(contract)`, `SameTermsAs`, `LateFeeScales` the half, usual and double a counter may name) | `Market`, `DecisionEngine`, `PlayerActions`, `Terms.For`, the journal's haggle entries, the views |
| `Negotiation.cs` | `NegotiationStatus` (Open, Agreed, Ended), `NegotiationEnd` (WalkedAway, RoundsRanOut, Declined, PostClosed, CannotPerform), `Tell` (None, Readily, Reluctant, AtLimit), `Bid` (round, tick, by, offer, reply hours, tell), `Negotiation` (a haggle over one contract between its poster and a responder: bids, `Rounds`, `AwaitingFrom`, `ReplyDue`, `LatestBy`; internal `Add`, `AwaitReply`, `Agree`, `Close`) | `SimulationContext`, `Market.Haggling`, `DecisionEngine`, `PlayerActions`, `GameSession`, the views |
| `Ship.cs` | `ShipStatus` (Docked, InTransit), `PickupSource` (Purchase, Haul, Storage, Aboard), `ShipCourse` (path, departed, arrival: what the client draws along), `ShipJob` (goods owner, good, quantity, pickup, source, destination, and the haul, purchase and delivery it serves), `Ship`: type, owner, location, capacity, `Hold` (lots by owner), `FreeSpace`, `Job`, `Course`, `Preservation`, `IsFree`, `LotOf`; `Reserve`, `ReachPickup`, `Load` (merges an owner's lot), `Unload`, `Alter`, `DepartLaden`, `DelayArrival`, `Arrive`, `FinishJob`; a text log | `Market`, `Settlement`, `TransitEvents`, `DecisionEngine`, `PlayerActions`, `Invariants`, the views |
| `ShipType.cs` | `ShipType` (5 hulls), `ShipClassInfo` (capacity, speed, running cost per tick, incident modifiers, preservation: refrigerated 0.8, so goods keep five times as long), `ShipClasses.Info` table | `Ship`, `PriceModel`, `IncidentResolver`, `WorkingCapital` (`MarketValue`) |
| `Personality.cs` | `Personality` (risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, concession rate) | `ActorState`, `DecisionEngine`, `PriceModel`, `WorldGenerator` |
| `Reputation.cs` | `ReputationContext` (Accord standing, region, good category, counterparty), `ReputationLedger` (scores −1..1, decay toward 0) | `Settlement` (writes), `Visibility` (reads region) |

### Engine: `Actors/`

| File | Contents | Used by |
|---|---|---|
| `ActorState.cs` | One model for every actor: credits, stock with a reserved part (`Stock`, `Reserved`, `Available`), ships, home, personality, `Producer`, `Station` and consumer `Demands` profiles, `IncomePerHeartbeat`, `IsPlayer`, `MovesCargo`, `KeepsStock`; per-good `Consumed`; its `PriceMemory`s (internal); its `Knowledge` (`ActorKnowledge`, design §7.2) | everywhere |
| `ActorRole.cs` | `ActorRole`: Producer, Consumer, ShippingCompany, IndependentPilot, Government, StationOperator in use; Factory and the rest named, never created | `ActorState`, `WorldGenerator`, the views |
| `Profiles.cs` | `ProducerProfile` (good, output, unit cost, cap, margin; `Fullness`), `StationProfile` (storage rate, float), `ConsumerDemand` (good, rate, target stock) | `ActorState`, `PriceModel`, `DecisionEngine`, `WorldGenerator` |
| `PriceMemory.cs` | `PriceKind` (Wanted, Lot, Haul), `PriceKey`, `PriceMemory`: a per-actor price factor that learns from each of its closed posts (haggled: toward the price agreed; taken within a day: lower; expired: higher, or the other way for a seller), clamped | `DecisionEngine` (consumers' wanted Sales, producers' lots, everyone's hauls) |
| `DecisionEngine.cs` | Partial class, the shared part: `Run` (by profile: station, producer, consumer, trader; then `PayOut` above `WorkingCapital`, except the government), `WorkingCapital`, `ChargeRunningCosts`, `AnswerAtOnce` (an AI's wanted Sale, or anyone's order at a producer's door), `HaulFeeEstimate`, `UsualHaulMarkup`/`UsualHaulOffer` (the player's haul offer), `RaisedHaulOffer`, `HaulDeadline`, `Ask` (a producer's unit ask); the terms' decisions by utility: `ReviewDefault`/`WouldCancel` (a payer whose contract is in default), `WouldReject` (a buyer handed goods below the floor), `WouldWalkAway` (a seller that can no longer deliver); and private `HaulTerms`, `PostPricedHaul`, `ArrangeTransport` (post a Haul for a signed Sale, send one's own ship, or walk away), `RaiseHaulOffers` (repost an untaken haul higher), `Known<T>` (others' contracts it believes open: `Knowledge.OpenContracts`) | `ActorHeartbeat`; `Market.PostSale` (`AnswerAtOnce`); `GraceEnded`, `DefaultReview` (`ReviewDefault`); `Settlement` (`WouldReject`); `PlayerActions` (`FillSources`, the offers, `HaulDeadline`) |
| `DecisionEngine.Producers.cs` | `RunProducer`: `Produce` (paying cost), `TryAnswer` (wanted Sales at its door: as posted at or above its ask, or an AI's in closed form; an order at its door at its ask, up to the order's price), `SellDelivered` (a remote wanted Sale it can profit on, as posted or in closed form, and whose perishables a standard hold gets there before the buyer could reject them: `Market.FillViaHaul` from its stock, which posts the Haul), `ArrangeTransport`, `PostLot` (when 75% full, priced by its `PriceMemory`, never below cost) | `DecisionEngine.Run`, `AnswerAtOnce` |
| `DecisionEngine.Consumers.cs` | `RunConsumer`: income (`ApplyFaucet`), `Consume`, `BringHome` (a Haul home for goods in storage elsewhere), then per demand below 60% of target, counting `Inbound`: `TryBuyLot` (a cheap lot plus a haul home, as posted or in closed form) or `PostWanted` (delivered, priced from the cheapest source it knows, `Knowledge.ReadingOf`, and its `PriceMemory`, on terms `Terms.Meetable` relaxes); `Willingness` (affordability-aware) | `DecisionEngine.Run` |
| `DecisionEngine.Traders.cs` | `RunTrader`: per free ship `BestOption` (twice, if the first was found taken) over the Hauls it believes open (`ScoreHaul` → `HaulTake` on an offer: inside the grace, not a carriage the shipper would refuse, the warranty's refund) and wanted Sales by source (`FillSources`: its own goods, or producers at the asks it knows, `Knowledge.KnownAsk`; an overload takes a `Sight`, `ScoreFill` → `FillTake`: inside the grace, goods the buyer wouldn't reject) by `Utility` less `TermsRisk` (what the terms stand to cost if an incident strikes) over a `Threshold`, priced by `Price` (as posted or closed form), `Commit` (through `Market.SignAgreed`); `Stow` (goods left aboard into storage); `Broker` (no free ship: `FillViaHaul`, also priced by `Price`); `SellOff` (stored goods as lots, written off after 30 days); `RunStation` (rent) | `DecisionEngine.Run`; `FillSources` also `PlayerActions` |
| `DecisionEngine.Haggling.cs` | `Take` (a take's utility, floor, arrival), `TermsOn`, `Price` (as posted, else `ClosedForm` with an AI poster), `ClosedForm` (`Haggling.Resolve`), `PosterReservation` (where an AI poster stands: ripening over two days, at once for a haul serving a signed Sale, within `Uncommitted` credits), `Uncommitted`, `LeastPrice`/`LeastPriceFrom` (a reservation by false position), `NoteHaggled`; with the player: `NoticePost` (schedules `PostReached` by tier; `fromNews` for the actors that couldn't see a post the news reported), `Answers` (who answers what), `Respond`, `ExpectReply` (schedules `ReplyDue`), `Reply`, `Decide` (the haggling rule), `TakeOn` (an AI responder signs an agreed offer by its plan); internal `Quote`/`Plan` and `QuoteFor`: `PosterQuote` (`TermsValue`, `DeadlineLimit`), `CarrierQuote`, `FillerQuote`, `ProducerQuote`, `BuyerQuote`, `Fit` (the nearest terms it can meet) | `Market` (`NoticePost`, `ExpectReply`, `TakeOn`), `PostReached`, `ReplyDue`, the other partials, tests |
| `Haggling.cs` | `NegotiationParty`, `NegotiationOutcome`, `Haggling`: the rules (design §3.6, §16.5): `Resolve` (an AI-to-AI haggle in closed form, the rounds played out), `Opening`, `Concede`, `Crosses`, `TooWide` (`WalkAwayGap` 35%), `TellFor`, `ReplyHours` (1–12 h by eagerness and closeness, with a deterministic hour of spread) | `DecisionEngine`, `NegotiationView`, `NegotiationTests` |
| `PlayerActions.cs` | `DealBlock` (codes, worded by the templates; `BadOffer`, `NotYourTurn`, `RoundsRanOut` new), `PostKind`, `PostBlock`, `PostDraft`, `HaulFeasibility`, `FillFeasibility`, `BuyFeasibility`, `BrokerFeasibility`, `HaulAgainFeasibility`; `Believed` (whether the player believes a contract open: live where it sees it, else as the news left it; the checks use it), `ShipFor`, `Check`, `CheckHaul`/`TakeHaul`, `CheckFill` (the cheapest source)/`Fill`, `CheckBuy`/`Buy`, `CheckBroker`/`Broker`, `CheckHaulAgain`/`HaulAgain`, `CheckPost`/`Post` (a wanted Sale into its storage at the place, a lot or a haul of its stored goods), `CheckCounter`/`Counter`, `AcceptOffer`, `Decline`, `Withdraw`, `TakeOn` (the player signs an agreed offer by its own command), `CancelAtWill`, `CancelForDefault`, `Decide`, `Store`, `Retrieve`: the player's commands through `Market` and `Settlement` | `GameSession`, `ContractView`, `ComposerView`, `BoardView`, `DealEstimate`, `Market.AcceptOffer` (`TakeOn`), the bots |

### Engine: `Economy/`

| File | Contents | Used by |
|---|---|---|
| `PriceModel.cs` | `ProducerAsk` (cost × margin × fullness; with credits, a rich producer gives up its margin), `ConsumerWillingness` (base value × shortage; with credits and income, × affordability), `PilotMinimumFee`, `TravelTicks` (distance ÷ speed, rounded up) | `DecisionEngine`, `TransitEvents`, `Market`, `PlayerActions`, `DealEstimate` |
| `Visibility.cs` | `Tier` (Here, Near, Far), `Sight` (one actor's presence and the tier of every location from it: `TierOf` a location or a contract, `CanSee` (own; here at once; near after `ReachNear` hours; never far), `ReachHours`), `Visibility`: `Of(context, actor)`, `Presence` (home, ships where they are or are heading, storage), `CanSee` (live, or known from the news), `ReachHere` 0, `ReachNear` 4 | `DecisionEngine`, `Knowledge`, `Market` (telemetry), `PlayerActions`, `GameSession` (`Sight`), `BoardView`, `MapView`, `NewsView`, `ContractView`, `LogView`, the bots |
| `Knowledge.cs` | `ReadingSource` (Live, Seen, Daily, News, Start), `PriceReading` (a producer's ask and when, its stock and when; `CurrentStock`: only while as new as the ask), `ActorKnowledge` (per actor: `Stored`, `History` (a reading a day, 14 days), `LastHere`, `FoundClosed`, `RefusedAt`; internal notes), `PriceBulletin` (the morning's asks), `Knowledge`: `Seed`, `Look`, `LookAt`, `ReadBulletin`, `ReadLot` (internal, by events); `ReadingOf` (live here, the bulletin near, remembered far), `KnownAsk` (what a door order is counted on at, unless short or refused in the last day), `OpenContracts` (what an actor believes open), `BelievesOpen`, `FromNews`; internal `NoteClosed`, `NoteRefused` | `ActorHeartbeat`, `ShipDeparted`, `ShipArrived`, `PricesPosted`, `NewsDesk`, `WorldGenerator`, `Market`, `DecisionEngine`, `PlayerActions`, the views, the bots |
| `News.cs` | `NewsKind` (Posted, Signed, Delivered, Short, Failed, Cancelled, Defaulted, Expired), `NewsItem` (a contract, where it stood at press time, the facts its sentence needs), `NewsIssue`, `NewsDesk` (the issues kept, `Latest`, `ReportedOpen`, `WasReportedOpen`, `ReportedOpenAt`, `AverageUnit` (the week's signed Sales), telemetry counts; internal `Publish`; `Weight`: size, novelty, outcome, frontier) | `NewsPublished`, `Knowledge`, `Market` (telemetry), `BoardView`, `ContractView`, `MapView`, `NewsView`, `NewsText`, `Soak` |
| `TrackRecord.cs` | `TrackRecord` (on time, late, short, failed, cancelled for default, walked away; paid, defaulted, walked away as payer), `Records` (`PublicRecordOf`: Accord contracts only; `DealingsWith`: each actor's own experience, frontier too; internal notes from `Settlement`) | `Settlement`, `ContractView`, `InfoText` |
| `InflationMonitor.cs` | `Evaluate`: drift of total credits beyond ±8% moves `FaucetMultiplier` by 7% | `InflationCheck` |

### Engine: `Events/`

| File | Contents | Used by |
|---|---|---|
| `ISimEvent.cs` | `ISimEvent` | events, `EventQueue`, `SimulationContext` |
| `EventQueue.cs` | Min-heap by tick, ties by insertion sequence | `SimulationContext` |
| `ControlEvents.cs` | `ActorHeartbeat`, `ContractExpiryCheck`, `GraceEnded`, `DefaultReview`, `InflationCheck`, `ReputationDecay` | scheduled by `WorldGenerator`, `Market` (expiry at posting; the grace's end at signing what someone waits on), `DecisionEngine.ReviewDefault` (reviews), and themselves |
| `TransitEvents.cs` | `ShipDeparted`, `TransitIncidentCheck`, `TransitIncidentOccurred`, `ShipArrived` | `ShipDeparted` scheduled by `Market.Dispatch`; the rest by each other |
| `NegotiationEvents.cs` | `PostReached`, `ReplyDue` | scheduled by `DecisionEngine.NoticePost`, `ExpectReply` |
| `InformationEvents.cs` | `PricesPosted`, `NewsPublished` | scheduled by `WorldGenerator`, and themselves |

### Engine: `Random/`, `Simulation/` and `Properties/`

| File | Contents | Used by |
|---|---|---|
| `Random/DeterministicRandom.cs` | xorshift128+ seeded by SplitMix64; `NextDecimal`, `NextLong`, `Chance`, `Pick`; counts `DrawCount` | `SimulationContext.Rng` → `WorldGenerator`, `TransitIncidentCheck`, `IncidentResolver` |
| `Simulation/SimulationContext.cs` | The world (section 4) | everything |
| `Simulation/Market.cs` | `Signing` (Signed, NotOpen, PayerCannotFund, SupplierDeclined, CannotPerform), `FillSourceKind`, `FillSource`; `Market`: `PostSale` (a package and late-fee scale, a lot in storage warranted as seen; an AI's wanted Sale or a door order is answered at once, the player's reaches others over game time), `HagglesInGameTime`, `PostHaul` (a package, the goods' declared value, a cause), `Withdraw` (a cause), `Expire` (internal), `CancelForDefault`, `CancelAtWill`, `CanFund`, `CanFundHaul`, `Supply`, `BuyLot`, `TakeHaul`, `DoorTolerance` (10%), `DoorLimit`, `BuyAtSource` (an order at the door: posted at the limit, signed at the producer's ask; telemetry), `FillWithShip`, `FillViaHaul`, `UnderWay`, `ExpectedArrival` (both count a ship carrying a haul's goods for the Sale it serves, even once the haul is cancelled), `NextFree` (where and when a ship is next free), `TravelTo`, `Dispatch` (internal); private `AwaitPerformance` (schedules `GraceEnded`), `Sign` (locks escrow and bond; a closed contract is noted in the taker's knowledge, `NotOpen`), `Declined` (a door order turned down: noted), `Reserve`, `ReleaseOpen` | `DecisionEngine`, `PlayerActions`, `ControlEvents`, the views (`NextFree`, `ExpectedArrival`) |
| `Simulation/Market.Haggling.cs` | `Market` (partial): `Counter` (opens or continues a haggle; on a contract found closed, noted; an AI on the other side is told to reply), `AcceptOffer` (the responder takes the contract on the offer, or the haggle ends), `EndNegotiation`, internal `SignAgreed` (amend the deadline and terms to the agreed offer, sign, restore if it didn't) | `PlayerActions`, `DecisionEngine` |
| `Simulation/Settlement.cs` | `SettlementTerms` (gross, refund, late fee, liability, tax; `Payment`, `Penalties`, `Net`) and `Due` (pro rata; the late fee then the liability, limited by `Collectable`); `HandOver` (Done, Defaulted, Refused, Pending); `CanPay`; `HandOverReserved` and `HandOverCargo` (inspection: a floor, the AI's `WouldReject` or the player's decision; a short delivery under a strict term fails), `DeliverHaul` (the warranty on decay in the hold; rejection and strict shortness fail it; liability for goods lost); `Fail` (bond to the payer, escrow back, deep-Accord liability, set-aside goods freed, a lost haul fails the Sale it serves); `Cancel` (for default or at will, the break fee); `PayerDefault`; `Keep`; `Unreserve`; `Late`; `Load`, `Unload` (a ship's job at its pickup and destination; a cancelled haul's goods still set down); reputation rewards and blame; every ending noted in `Records` | `Market`, `TransitEvents`, `ControlEvents`, `PlayerActions`, `DecisionEngine` (`Due`), `DealEstimate` (`Due`, `Collectable`), `DecisionView` (`Due`) |
| `Simulation/Terms.cs` | The three packages for Sales and Hauls (a table in its doc comment: grace, late fee and cap, break fee, short delivery, warranty and floor, liability), every figure from the price; `ForSale` (a lot in storage warranted as seen; a late-fee scale), `ForHaul` (liability at the declared unit value; a late-fee scale), `For(contract, offer)` (its kind's terms rebuilt from an offer), `PackageFor` (by personality; the player's default standard), `Meetable` (what a buyer of perishables relaxes to), `TaxRate` | `Market`, `DecisionEngine`, `PlayerActions`, `ComposerView` |
| `Simulation/Decisions.cs` | `DecisionKind` (CancelForDefault, Inspection), `DecisionChoice` (Cancel, Wait, Accept, Reject), `PlayerDecision` (kind, contract, raised, the entry that raised it, goods inspected; `Choices`); `FailureKind` (the design §16.3 paths the soak counts) | `SimulationContext`, `GraceEnded`, `Settlement`, `PlayerActions`, `GameSession`, `DecisionView`, `Soak` |
| `Simulation/SimulationConfig.cs` | Tuning knobs: heartbeat 24, inflation check 400 / ±8% / 7%, reputation decay, tax 4%, docking ₵3, `NegotiationMaxRounds` 6, `PostsDrawAnswers` (tests turn it off), incident scale, reputation gain and loss, `NewsItemsPerDay` 3, `NewsFrontierWeight` 3, working capital (consumer 90 days' income, producer 4 warehouses at cost, trader 1.5 × fleet value) | `SimulationContext.Config` |
| `Simulation/WorldGenerator.cs` | The sector: locations (with map points), routes (hours and danger), producers, consumers, government, traders, optional player, station operators (₵0.10 a unit-day, ₵2,000 float); the knowledge everyone starts with and the daily bulletin and news; `WorldHandles` | `Soak`, `Fingerprint`, `GameSession`, `BenchPanel`, tests |
| `Simulation/IncidentResolver.cs` | `TransitIncidentKind`, `TransitIncident`, `Resolve` (roll → delay, damage, partial loss, credit cost, total loss; severity from the ship), `Share(kind)` (the table's odds per kind) | `TransitIncidentCheck`, `TransitIncidentTests`; `Share` by `ContractView` |
| `Simulation/Invariants.cs` | `Check`: credit conservation, escrow reconciliation, no negative escrow and nothing left in a settled contract's, goods conservation per good (produced = consumed + lost + held) with no negative or over-reserved stock or storage and no overfull hold, and every signed contract being performed (a haul has a ship flying it; a Sale has its goods set aside or under way); `InvariantViolation` | `Soak`, tests (`TestWorld.AssertSound`) |
| `Simulation/EventJournal.cs` | `JournalEntry` (`Seq`, `Cause`) and its seventeen kinds (section 3), `IContractEntry`, `INegotiationEntry`, `FailureReason`, `EventJournal` (a ring of 5,000: `Record` stamps and returns, `Find`, `Entries`, `Tail`, `TotalRecorded`) | `SimulationContext.Record`; `LogView`, `JournalText`, `ContractView.Why` |
| `Simulation/StateFingerprint.cs` | `Of(context)`: tick, draws, money supply, settlement counters (fulfilled, breached, defaulted, cancelled, goods sold) and every actor's credits, as one string | `TestWorld.Signature`, the CLI's `fingerprint`, the client's `BenchPanel` |
| `Simulation/GameTime.cs` | One tick = one game hour. `Day`, `HourOfDay`, `At`, `Days`, `FormatDate` ("Day 4 · 14:00"), `FormatDuration` ("1d 6h"), `FormatRelative` ("in 1d 6h") | `Soak`, tests, `Format` (every date the client shows) |
| `Simulation/RunStop.cs` | Why `RunTo` returned: `ReachedTarget`, `Drained`, `PlayerAlert`, `PlayerDecision` | `SimulationContext.RunTo`, `GameSession.Advance` |
| `Properties/AssemblyInfo.cs` | `InternalsVisibleTo("ByGalacticAccord.Tests")` | the tests (internal `Sign`, `Dispatch`, stock reservations) |

### Soak harness: `src/ByGalacticAccord.Cli/`

| File | Contents |
|---|---|
| `Program.cs` | Commands `soak [ticks] [--seed N]` (default 200,000 ticks, seed 20260619), `bots [ticks] [--seed N]` (default 200,000), `fingerprint [ticks] [--seed N]` (default 20,000) and `help` |
| `Soak.cs` | Runs in 5,000-tick chunks, `Invariants.Check` after each (exit code 1 on a violation), then prints telemetry: money, settlement counters, every failure path's count, signed contracts and their failure rate by term package, signed contracts by kind and how many were haggled in closed form (orders at the door left out), information (door orders and refusals, the news, contracts signed from it), the goods table, demand met per consumer, contracts by kind and status, trader solvency, wealth |
| `Bots.cs` | The same world played once by each bot (`BotRun`), invariants checked; prints each one's credits, earnings a day, deals, posts, counters, haggles and decisions (exit code 1 on a violation) |
| `Fingerprint.cs` | A headless run's `StateFingerprint`, which the browser check compares with the client's |

### Presentation: `src/ByGalacticAccord.Presentation/`

Everything the interface says or draws, as plain records built from the session; the client only renders
them. A view never changes the world; commands go through `GameSession`.

| File | Contents | Used by |
|---|---|---|
| `Text/Templates.resx` | Every interface sentence and label (design §11.5): 575 templates with named `{fields}`, keyed by journal entry type and contract kind (`ContractSigned.Sale.Wanted`, `ContractCancelled.AtWill.Payer.Haul`, each with `.You` and `.ToYou` twins where the player can be either party) and fact type (`Good.Food`, `Package.Strict`, `Term.Default.Them.Frontier`, `Decision.Cancel.Note`, `Ledger.Storage`, …) | `Templates` |
| `Text/Templates.cs` | `Get`, `Fill(key, (name, value)…)` (throws on a missing key or field), words for the engine's enums (`Good`, `Jurisdiction`, `JurisdictionShort`, `JurisdictionLong`, `Danger`, `LocationKind`, `Role`, `ContractKind`, `Side`, `Package`, `ShipClass`, `Tell`, `LateFee`), `KindLine` ("Sale · wanted"), `Offer` ("₵460, due Day 4 · 06:00, standard") | every view, the client's components |
| `Text/Format.cs` | `Money` ("₵1,234", "−₵42"), `MoneyCents`, `Quantity`, `Percent`, `Points`, `Hours`, and `GameTime`'s dates, all invariant | every view |
| `Text/JournalText.cs` | `Describe(context, entry)` (the entry's sentence; a haggle's reply with its reply time and tell), `Subject`, `Addressee` (the side an offer is addressed to), `ContractOf` (any `IContractEntry`), `Kind(contract)`, `Head(contract)` | `LogView`, `HeaderView` (alerts), `ContractView` |
| `Text/InfoText.cs` | `Age` (Live, Today, Days, Old: information age as opacity, design §11.1), `InfoText`: `AgeOf`, `ReadingAge` ("daily bulletin, Day 4 · 06:00"), `PublicRecord`, `Dealings`, `Range` (of a reading history) | `BoardView`, `MapView`, `NewsView`, `ContractView` |
| `Text/NewsText.cs` | `Describe(context, item)`: a news item as a headline, keyed `News.<Kind>.<contract kind>`, with the price against the week's average | `NewsView` |
| `Session/GameSession.cs` | `ClockSpeed`, `DealOutcome` (a `DealBlock`, or the `Signing`), `PostOutcome` (a `PostBlock`, or the post), `GameSession` (from a seed, or over a world already made): the world with the player, the clock (`SetSpeed`, which won't start while a decision waits; `Advance(realSeconds)` capped at `MaxFrameSeconds`, stopping on an alert or a decision; `Clock` with its fraction, `Revision`), `Alerts`, `Decisions`, `PlayerShip`, `Accept` (haul, fill or buy by kind), `Broker`, `HaulAgain`, `Post`, `Counter`, `AcceptOffer`, `Decline`, `Withdraw`, `CancelAtWill`, `CancelForDefault`, `Decide`, `Store`, `Retrieve`, `PlayerActiveContracts`, `PlayerOpenPosts`, `PlayerNegotiations`, `Sight` (the player's now), `KnownOpen()` (what it believes open) | `Shell`, the views, the bots, tests |
| `Views/DealEstimate.cs` | `DealKind` (Haul, Fill, Buy, Broker), `DealEstimate`: what a contract is worth to the player's ship by the engine's rules (income; goods bought and their tax; running costs for the empty and laden legs; docking; the certain warranty refund (a haul's on decay in the hold) and late fee, limited as `Settlement.Collectable` limits it; the bond; incident odds; the source; through a carrier, the haul fee and its tax), `TakeHome`, `Cost`, `PerDay`, from where and when the ship is next free; `ForBroker` | `BoardView`, `ContractView`, `MapView` |
| `Views/BoardView.cs` | `BoardRow` (with its `ContractType` and term package), `BoardViewModel`: the Sales and Hauls the player believes open (`GameSession.KnownOpen`), newest first, each with how it knows of it (`Seen`: from the news, with that issue's `Age`), with take-home (or a lot's cost) and one note (why it can't be taken, goods the buyer may reject, lateness, a refund, the goods bought) | `Shell` |
| `Views/ContractView.cs` | `MoneyLine`, `InfoLine`, `ContractCommand` (Broker, CancelAtWill, CancelForDefault, HaulAgain, Withdraw), `ContractAction`, `ContractViewModel`: the contract as a document per kind (breakdown, schedule, uncertain inputs, every term as a sentence from the player's side under "Terms · package", party, status (in default, partly fulfilled, cancelled and by whom, the player's own post), the accept label, `Actions`, `Record`: the party's public record and the player's dealings with it; `Known`: from the news; a producer's ask the player doesn't see live and "still open?" among the uncertain inputs; `Why`: its record read back through its causes, newest first, six at most, stopping where the journal no longer keeps it or the player can't see it; `Threads`: its haggles with the player, and `Counter`: the offer form while the player may counter someone's post); `Check`, `Head`, `BlockText`, `OutcomeText`, `BrokerText`, `CommandText`, `Why`, `TermsOf` (the terms from the player's side: performer, payer, or seller of its own lot) | `Shell`, `BoardView`, `LedgerView` (`Head`), `ComposerView` (`TermsOf`), `PostsView` |
| `Views/NegotiationView.cs` | `Choice<T>`, `OfferFormModel` (an offer as a form, with the deadlines, packages and late fees it may name), `BidLine`, `ThreadViewModel` (a haggle as a thread: offers with their tells, status, whose turn, accept, decline and counter); `Thread`, `Form`, `Packages`, `LateFees`, `CounterText`, `AcceptText`, `DeclineText` | `ContractView`, `ComposerView`, `PostsView`, `Shell` |
| `Views/ComposerView.cs` | `ComposerDraft` (the composer's fields, kept by the client), `ComposerViewModel` (choices, what posting it means, reference figures from the player's board or the usual fee, its terms as sentences, what it commits, why it can't be posted); `Default` (a first draft of a kind), `ReferencePrice`, `ToPost`, `Build`, `BlockText`, `OutcomeText` | `Shell`, the bots' tests |
| `Views/PostsView.cs` | `PostLine`, `PostsViewModel`: "your posts" with how many answered and how many wait for the player, and its haggles over others' posts (open, and the last three ended) | `Shell` |
| `Views/DecisionView.cs` | `DecisionOption`, `DecisionPrompt`, `DecisionView.Build`: each pending decision as the prompt that stops the clock (what happened, what is on its way, each answer with what the terms make of it) | `Shell` |
| `Views/MapView.cs` | `MapNode` (with the asks the player knows there, their age, the tier, a signal's badge), `MapRoute`, `MapLeg`, `MapRegion`, `MapShip`, `MapViewModel`, `LabelAnchor`: the schematic map, the selected contract's overlay and title per kind, region frames, label placement; `Ships(session, clock)` and `Along` (a course's position by share of hours) | `Shell`, `SectorMap`, `MapLayers`, `ShipLayer` |
| `Views/LogView.cs` | `LogLine`, `LogViewModel`: the journal's visible entries, newest first, 40 at most; other actors' routine (lots that expired, withdrawals, storage) left out | `Shell` |
| `Views/HeaderView.cs` | `SpeedButton`, `HeaderViewModel`: speeds (and "waiting for your decision"), date, credits, escrow (the player's share), running cost, the fleet line (by the ship's job), alerts | `Shell`, `LedgerView` |
| `Views/LedgerView.cs` | `LedgerAction` (store here, or load aboard), `LedgerLine`, `LedgerViewModel`: money, what the player owns and where (holds and storage, with freshness and rent), its signed contracts with its role (carrying for, shipping with, selling to, buying from) and the figure, flagged in default, and its own open posts | `Shell` |
| `Views/NewsView.cs` | `NewsLine`, `SignalKind` (Idle, Short, PriceLow, PriceHigh), `SignalLine`, `NewsViewModel`: the latest issue of The Daily Accord and the signals in what the player knows (`Signals`: idle producers and consumers running short where it is present, asks 15% or more off its readings anywhere) | `Shell`, `MapView` (badges) |
| `Bots/Bot.cs` | `Bot`: a scripted player (design §20.3) that plays through the session; `Deals`, `Posts`, `Counters`; `Answer` (decisions by the AI's rule), `Takeable` (the deals its board offers that pay, skipping deals past the grace or goods the buyer could reject), `BestDeal` (the most take-home a day), `ReadDeal` (judged: take-home less `Risk`, and the best next deal from where it ends at `NextLegWeight`), `Take`, `ClearHold` | `BoardTaker`, `MarketReader`, `BotRun` |
| `Bots/BoardTaker.cs` | Takes the board's best deal as posted whenever its ship is free | `BotRun`, the CLI, `BotTests` |
| `Bots/MarketReader.cs` | Takes the deal it judges best (`ReadDeal`), countering a board deal whose post has waited (`Haggles`); with `PlansPurchases` (off unless asked: it loses money) it also posts a wanted Sale at a producer's door below the ask it knows when a delivered Sale pays well over the goods and the trip, haggles the answers, then fills the Sale from storage (keeping leftovers for the next fitting Sale, then selling them) | `BotRun`, the CLI, `BotTests` |
| `Bots/BotRun.cs` | `BotReport`, `BotRun.Run`: a world with the bot as its player, the clock four hours a glance, decisions answered, alerts acknowledged, invariants checked every 5,000 hours | the CLI's `Bots`, `BotTests` |
| `Views/Tone.cs` | `Tone`: what a value's colour encodes (gain, loss, warn, yours, muted, plain) | every view; CSS classes `tone-*` |

### Client: `src/ByGalacticAccord.Client/`

| File | Contents |
|---|---|
| `Program.cs` | The Blazor host; `Shell` is the root component |
| `Shell.razor` | Reads `?seed=` (default 20260619) and `?bench=`; builds the `GameSession` (started at Day 1 · 06:00); runs the frame loop; holds the selection, the last command's outcome (cleared when the clock starts) and the composer's draft; rebuilds the view models (decisions, posts, the composer and the news too) when `Revision`, the selection or the draft changes, and the ships every frame; `Accept`, the contract's `Command` (broker, walk away, cancel for default, raise the haul, withdraw), `Decide`, the ledger's `Act` (store or load), and posting and haggling (`NewPost`, `ChangeDraft`, `Post`, `CloseComposer`, `Counter`, `AcceptOffer`, `Decline`) |
| `Components/TimeBar.razor` | Title, speed buttons (`data-speed`), date, money, theme picker |
| `Components/SectorMap.razor` | The map panel: `MapLayers` and `ShipLayer` in one SVG, the overlay card, the legend, the fleet line |
| `Components/MapLayers.razor` | Ground, regions, routes (`band-*` classes carry colour and dash), overlay legs, hours, locations (a badge, name, kind, the asks known there and their age; `tier-*` and `age-*` classes); renders only when its model changes |
| `Components/ShipLayer.razor` | Ships at their interpolated positions; renders every frame |
| `Components/BoardPanel.razor`, `ContractPanel.razor`, `CaptainsLog.razor` | The board (each row's kind and term package; a row from the news says so and fades with age), the contract document (how the player knows of it, the party's public record, its terms, its "why" chain whose lines select their contracts, its haggles as threads, the accept button, the counter-offer form and the other actions with what each does), the log (a line about a contract selects it) |
| `Components/DecisionPanel.razor` | A decision that stopped the clock: what happened (selects the contract), and a button per answer with its note |
| `Components/PostsPanel.razor`, `PostRow.razor` | "Your posts and haggles": each of the player's posts with its answers and each haggle over others' posts (selects its contract), and the button that opens the composer |
| `Components/ComposerPanel.razor` | The post composer: kind, goods, quantity, place (and destination), price, how long it runs, package, late fee; the reference figures, the terms as sentences, what it commits, and Post |
| `Components/ThreadView.razor`, `OfferForm.razor` | A haggle as a thread in the contract panel (offers with their tells, its status; accept, decline, counter on the player's turn); the offer form for a counter (keeps what was typed while the clock runs) |
| `Components/LedgerPanel.razor` | The ledger: money, possessions with their store or load button, contracts (each selects itself) |
| `Components/NewsPanel.razor` | The Daily Accord (each item selects its contract) and the signals with their ages, beside the log |
| `Components/BenchPanel.razor` | `?bench=N`: runs a world with no player for N hours, shows the time and the `StateFingerprint` |
| `wwwroot/index.html`, `css/app.css`, `js/client.js`, `fonts/` | The page; colour tokens with dark (default) and light themes (`prefers-color-scheme`, or `data-theme` on `<html>`); the theme's storage, guarded; IBM Plex |

### Browser check: `tools/client-check/`

| File | Contents |
|---|---|
| `check.cjs` | With no arguments: `dotnet publish` the client to `artifacts/client-check/publish`, get the desktop fingerprint from the CLI, serve the site, then in Chromium: load (paused, Day 1 · 06:00, the board with each row's kind and terms, map, ledger; the tiers from Meridian, the age of what the player knows of each place, The Daily Accord), a contract from the news (says so, "still open?"), select a contract (its terms as sentences, its "why", the counter-offer form, the party's public record), accept it (the log and the ledger record it), fill a wanted Sale through a carrier (the ledger lists the sale and the posted haul; the contract offers what can still be done), post a wanted Sale of 20 food at Verdant below the ask the map shows (its terms and reference figures; "your posts" lists it), run 16× (resuming alerts, answering decisions), read the answers as threads with their tells and counter one, check no reply comes while paused and one comes while running (the log words the haggle), the goods aboard in the ledger, a decision prompt if the run raised one, light theme, phone width (no sideways scroll), the speed probe and the fingerprint; screenshots to `artifacts/client-check/screenshots`; fails on any console error, page error or failed request. `--site`, `--out`, `--fingerprint` override. |
| `package.json`, `package-lock.json` | Playwright 1.63.0 |

### Tests: `tests/ByGalacticAccord.Tests/`

| File | Covers | Soak variants |
|---|---|---|
| `TestWorld.cs` | Helpers: `RunHeadless`, `Quiet` (no heartbeats, incidents off and the player's posts unanswered unless asked: a test drives the market itself), `Signature`, `Actor`/`Place`/`Player`/`ShipOf` by name, `RunUntilSettled`, `PostWanted` (standard terms unless asked), `PresentAt` (a unit in storage: a presence there), `AssertSound` (every invariant), the `Soak` trait constants | — |
| `DomainTests.cs` | `Credits`, `PenaltyOnLateDelivery`, `QualityWarranty`, `Cargo` decay, `FindPath` (multi-hop, unreachable, cache), `OpenContracts`, the good and hull tables | — |
| `GoodsTests.cs` | Keep-times against design §15.3 and refrigeration (food reaches the frontier in a standard hold, medicine only refrigerated); merging parcels; lazy decay; conservation catches a unit that vanishes unbooked | — |
| `SaleTests.cs` | A Sale's lifecycle: answered at once at a producer's door; filled by buying and carrying; a lot bought whole and collected, or expiring; a default outside the Accord; the late fee from the bond; a warranty refund; a short delivery pro rata; in default with nothing coming, cancelled by the buyer for the bond; lost cargo failing a haul and its Sale; buying below the ask | — |
| `TermsTests.cs` | The packages' terms from the price; a haul's liability at the declared value; terms that give nothing away; a lot in storage warranted as seen; packages by personality; `Meetable`; grace and default | — |
| `FailurePathTests.cs` | Every design §16.3 path S4 has, for an AI actor and through the player's commands (its doc comment maps each path to its tests): late, in default (the AI waits, the player is asked, cancels or waits, the player defaults), short (strict failure, pro rata with liability; deep Accord vs edge), rejection (AI, and the player rejecting or accepting), walking away (AI and player), the player's payer default, the player's haul raised then expired | — |
| `HaulAndStorageTests.cs` | A producer's delivered Sale and its Haul; a lot bought into storage and hauled home; an untaken haul expiring; storage rent and spoilage; goods set aside in storage not used twice | — |
| `DecisionTests.cs` | Each role's rules in a quiet world: consumers post and learn their price, producers' lots and asks, traders fill and broker, the working-capital payout | — |
| `NegotiationTests.cs` | The haggling rules: as posted, a deal inside both reservations or none, too wide, a slow conceder holds out, rounds run out, concession, tells, reply hours | — |
| `HagglingTests.cs` | Plan S5's checks: a food post at Verdant draws the producer's counter and accepting it hands the food over; a post taken as posted after its reply time; reply timing deterministic per seed; no haggling while paused; an AI poster holding to a fresh post and settling a ripe one in closed form, the terms following the agreed price; a counter moving package, late fee and deadline; round-by-round concession; walking away; six rounds; posting a lot and a haul and every refusal; withdrawing ends haggles, another taker ends the rest; the player as a buyer asked when its seller defaults | — |
| `PostingViewTests.cs` | The composer for each kind (reference, terms from the poster's side, what it commits, refusals); a post's answers as threads with tells, the player's turn and the counter form; a counter on someone's post read back after signing; **no view gives away a poster's hidden reservation** | — |
| `BotTests.cs` | Both bots trade and keep every invariant over a short run, and the reader with its purchase plans | both bots over 1M (seed 42); the market reader beats the board taker (ten seeds, 200k: at least 7 wins and 1.05×) |
| `TransitIncidentTests.cs` | `IncidentResolver`: deterministic, armour milder than courier, danger raises credit costs | — |
| `GameTimeTests.cs` | `GameTime` | — |
| `DeterminismTests.cs` | Same seed → same `Signature`; stepped replay; seeds diverge | 150k, 120k |
| `InvariantTests.cs` | `Invariants.Check` at checkpoints; credits; escrow reconciling exactly; no negative balances | 1M, 250k |
| `EconomyTests.cs` | Economy comes alive (every kind settles, every consumer consumes every good); frontier reachable | 200k; every failure path over 1M; money supply over 1M; no monopoly over 500k; the frontier still supplied over 1M (every Marches demand 30% met or more; plan risk R1) |
| `JournalTests.cs` | Entries match how each contract ended; every lifecycle entry names an earlier cause; a lost cargo reads back to its incident; the ring buffer and `Find` | — |
| `ShipCourseTests.cs` | A course agrees with its ship and job every hour; a haul settles at its course's arrival, delays included | — |
| `PlayerActionsTests.cs` | Each player command through `Market` and each refusal (filling through a carrier among them); storing and retrieving; running costs every day; an alert stops `RunTo` | — |
| `TemplateTests.cs` | `Fill`; every template well formed; every enum word (tells, late fees, post kinds among them); an offer in a line; every journal entry (in default, cancelled and every haggle entry among them) worded for each kind, for an AI and for the player on either side | — |
| `FormatTests.cs` | Money, cents, percentages, quantities | — |
| `SessionTests.cs` | Speeds, the clock's fraction, the frame cap, the same world as one `RunTo`, the alert and the decision's hard pause, `Accept`, `Broker`, `HaulAgain`, `CancelAtWill`, `Store` and `Retrieve` | — |
| `DealEstimateTests.cs` | The estimate predicts the settlement of a clean haul, a late haul, a fill bought at the source and a lot, to the cent; empty legs; a busy ship | — |
| `ViewTests.cs` | Every view builds through a played game (the news, signals and the map's knowledge among them; deals of every kind, storing goods, filling through a carrier, posting wanted Sales and answering their haggles, the composer of every kind, answering decisions); the board's kinds and order; the ledger; the overlay's legs; interpolation along a path; the terms as sentences for every package and side; the decision prompts; the "why" chain; the contract's actions | — |
| `InformationTests.cs` | Plan S6's engine checks: the tiers from the player's presence, a ship's destination and storage; contracts here at once, near after four hours, never far; here live, near the bulletin, far as last seen, a newer ask superseding an older stock; an AI trader trusting a stale ask and paying the ask as it stands at the door; a refusal leaving a producer out for a day; the news reporting yesterday's contracts as they stand; a far post known everywhere until found closed; the player's far post reaching actors through the news; the news weights; the same news for a seed; the public record and own dealings | — |
| `InformationViewTests.cs` | Every kind of news item as a headline for an AI and for the player; the map's tiers and ages; signals (idle, running short, a low price) with their badges; a post from the news on the board and in the contract; a party's public record and the goods line's age | — |

## 7. Determinism rules (design §20.1)

- Randomness comes only from `context.Rng`, and only inside events or world generation. Draws today:
  `WorldGenerator` (personalities, first heartbeats), `TransitIncidentCheck`, `IncidentResolver`, and `NewsPublished`
  (`NewsDesk.Publish`: The Daily Accord's weighted draw, one per item, over yesterday's contracts in id order). The client
  draws nothing: its clock decides only *when* `RunTo` is called, and runs in any increments give the same world.
  Haggling draws nothing either: a reply's hours come from the actor's eagerness, its closeness to its limit and a
  spread computed from the haggle's and contract's ids (`Haggling.ReplyHours`), so the player's commands, made between
  events, never draw.
- What an actor knows (`ActorKnowledge`) changes only in events (heartbeats, a ship's departure and arrival, the
  bulletin, the news) and in a signing or refusal, which the actor's own command or an event makes. An AI reads another
  producer's ask and stock only through `Knowledge` (`ReadingOf`, `KnownAsk`), and others' contracts only through
  `Knowledge.OpenContracts`; its own stock and ask it reads directly.
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
for its bond. S6 replaced the near-omniscient visibility with tiers by presence and a knowledge store per actor.

| Defect | Where | Step |
|---|---|---|
| Nobody can go below ₵0 | `SimulationContext.ApplySink` and `TransferUpTo` cap at the balance; `Market.Sign` bonds `min(bond, credits)` | S8 |
| AI actors pay out whatever they hold above a working capital (a sink standing in for spending, growth and failure) | `DecisionEngine.WorkingCapital`, `PayOut` | S7, S8 |
| A producer selling delivered still loses the Sale (by walking away, or cancelled for default) when no carrier takes its Haul in time. Haggling (S5) didn't fix it: carriers' time near the goods binds, not the fee. Limited sight (S6) lowered it a little (walk-aways 6,179 → 5,185 and 6,458 → 4,678 a million ticks) but carriers' hours still bind | `DecisionEngine.SellDelivered`, `ArrangeTransport`, `RaiseHaulOffers` | S7 (fleets that grow) |
| The board's estimate counts a late fee but not a cancellation for default past the grace; a deal it shows positive can end in default | `DealEstimate.Late` | S9 (an explanation for every number) |
| An actor can't change its fleet, so a hull that doesn't pay (Dust Runner Kael's armoured freighter) bleeds its owner | `WorldGenerator`; `ChargeRunningCosts` | S7 |
| Incident outcomes don't shift with danger | `IncidentResolver.Resolve`: the roll isn't weighted by danger, despite its comment | S8 |

## 9. No callers (left from the retired clients, or waiting for a step)

`ISimEvent.Describe` (never
called); `Ship.History` (written, never read), `TransferOwnership`;
`ActorState.AllStock`; `EventJournal.Tail` (tests only); `SimulationContext.Neighbours`, `TaxDirect`;
`DeterministicRandom.Pick`; `Location.HasEscrow`, `HasArbitration` (contracts read their jurisdiction:
`Contract.UsesEscrow`, `Arbitrated`); `ShipClassInfo.Niche`, `RunsHot` (data only:
push is not implemented); `TransitIncident.Description` (only `Describe` and the ship's text log; the journal's
text comes from the templates); `ReputationLedger.Snapshot`. Delete or
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
             / Command (GameSession.Broker, HaulAgain, CancelAtWill, CancelForDefault, Withdraw → PlayerActions → Market)
             / Decide (GameSession.Decide → PlayerActions.Decide → Market or Settlement)
             / Act (GameSession.Store or Retrieve → PlayerActions)
             / NewPost, ChangeDraft (ComposerView.Build) / Post (GameSession.Post → PlayerActions.Post → Market)
             / Counter, AcceptOffer, Decline (GameSession → PlayerActions → Market.Haggling) → Rebuild
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
