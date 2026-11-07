# By Galactic Accord — code map

What is where, what calls what, and where money and goods move. Use it to find the file, then read the
file: the code is the truth. **Last checked against the code: 2026-11-05, after S8 (consequences); section 2 on 2026-11-07, for the hosting image.**

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
  home, its ships (where they are, or where they are heading) and its storage are **here** (live prices and stock, and every
  contract shown there); one jump from them is **near** (the morning **bulletin**'s asks); the rest is **far** (prices as
  last seen). A contract is shown at its own places at once and nowhere else unless its poster pays (S6b, D38,
  `Economy/Advertising.cs`): each further system's station takes a fee (0.05% of the price, at least ₵0.20, four times as
  much a jump further) and shows it there after a delay (4 hours a jump out, then a day, two, three). An AI pays a ring
  further each day a post sits unsold while the ring costs no more than 5% of its price; the player picks the systems in
  the composer or on its open post. Each actor keeps a **knowledge store**
  (`Economy/Knowledge.cs`: `ActorKnowledge` on `ActorState.Knowledge`) of its last reading of each producer's ask and
  stock, with timestamps, and decides on it, trusting what it last saw (D32): consumers price wanted Sales from the asks they
  know, traders and the player plan fills from them (`FillSources`), and an order at a producer's door pays the ask as it
  stands, up to a tenth over what the buyer expected. **The Daily Accord** (`Economy/News.cs`) reports three of yesterday's
  contracts each morning, by weighted chance (size, novelty, failure, the frontier); a post it reports open is known to
  everyone until its deadline, whether or not it has been taken since, and an actor finds out by trying. The **public
  record** (`Economy/TrackRecord.cs`) counts how every Accord contract ended, per actor, and each actor remembers its own
  dealings. The client fades stale information, labels it "as of …", shows signals and the news.
- **Ships from parts (S7, design §6, §15.4–§15.6):** every ship is a frame, a drive and a module or none (`Domain/ShipDesign.cs`:
  `ShipParts`), each a part kind at grade A, B or C (`Domain/ShipPart.cs`); its stats are computed from the parts (`ShipStats`, with
  `Terms` explaining each), and five combinations are the standard designs (`Designs`). Parts are goods (`GoodType.LightFrameA` …
  `CargoPodC`): they sit in stock, storage and holds, and are sold and hauled like any good. Three **factories**
  (`DecisionEngine.Factories.cs`) make them from ordinary goods, a grade lower when they make do without an input. A **Build**
  (`BuildContract`) orders a ship of named parts; yards bid on it (`DecisionEngine.Yards.cs`), take a 20% deposit, order the parts
  with wanted Sales (`Market.PostPartsOrder`), assemble one ship at a time and hand it over (`Settlement.HandOverBuild`), the Build's
  package setting its substitution policy (`Terms.Substitution`); a part that can't come in time has the yard propose remedies
  (`SupplyFailure`: more time, a substitute, or the refund), which the client chooses (`Market.ChooseRemedy`). Ships change hands by
  **ship Sales** (`ShipSaleContract`, handed over at signing, `Settlement.HandOverShip`): yards sell what they build on spec, traders
  sell idle ships and buy when their fleets are busy or order a Build (`DecisionEngine.Ships.cs`), and **Edge Salvage** buys cheap,
  ferries ships home, resells them and breaks up the unsellable. Each ship keeps a typed history (`ShipRecord`). The client has the
  ship sheet, a Build's orders tree, the build composer and the yards' bids as threads.
- **Consequences (S8, design §5.4, §8.4, §16.7, §16.8):** a balance may fall below ₵0. Running costs, interest, an incident's cost
  (`SimulationContext.Charge`), docking fees and rent (`TransferOwed`, to the station operator) and remedies the deep Accord enforces keep
  charging into debt; spending an actor chooses (production, labour, purchases, prices into escrow) still needs the money. Debt costs 0.2% a
  day (`Economy/Solvency.cs`: `ChargeInterest`). Every bond is locked in full: what the performer lacks the Accord clearing house lends, while
  its debt stays within half of what it owns (`Solvency.CanBond`). On each heartbeat `Solvency.Review` weighs the debt against what the actor
  owns (`Assets`: plant, ships, goods, its money in escrow): above half it is warned, above all of it, or above half for seven days, it is
  insolvent, and `Simulation/Liquidation.cs` ends its haggles, withdraws its posts, cancels its contracts (`CancelReason.Insolvency`: for
  default against an insolvent performer, a break fee from an insolvent payer), sells its ships and goods in fire sales shown in every
  system (`Contract.FireSale`), and dissolves it, writing off what it still owes (`DebtWrittenOff`). What an actor in debt sells is shown
  everywhere too. For the player, insolvency ends the game on the legacy screen (`Views/LegacyView.cs`, `LegacyPanel.razor`). A ship flies
  its laden leg at a **throttle** (`Domain/Throttle.cs`, `Simulation/Throttles.cs`): pushing is ×1.25 the speed and ×1.5 the incident
  chance, ×2 for a drive that runs hot; an AI pushes only to beat a late fee, the player chooses in the contract panel. A route's danger
  shifts the incident table toward its severe end (`IncidentResolver.Bound`).
- **Where it is going:** Slice 1 of the design, in the steps of [`plan.md`](plan.md) (S9, the interface complete, is next).
  Plan §1 lists what is kept, reworked and retired.
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
| Hosting image | `Dockerfile`, `nginx.conf`, `.dockerignore` | .NET SDK (build stage), nginx (`nginxinc/nginx-unprivileged`) | Publishes the client and serves its `wwwroot` as a static site; gembernodes (`apps/bygalacticaccord`) runs it at https://bygalacticaccord.gemberkoekje.nl. |

- `ByGalacticAccord.slnx` lists the five .NET projects. `Directory.Build.props`: net10.0, nullable on, **implicit usings off**
  (every file has explicit `using`s), **warnings are errors**.
- The test project's `VSTestTestCaseFilter` is `Category!=Soak` unless `--filter` is given.
- The client sets `InvariantGlobalization` (every number is formatted invariantly, so no ICU data ships) and
  pins its Blazor packages at 10.0.14. Its fonts (IBM Plex Sans and Mono, Latin, OFL) are vendored in
  `wwwroot/fonts/`, so nothing loads from outside the site.
- CI: [`../.github/workflows/ci-bygalacticaccord.yml`](../.github/workflows/ci-bygalacticaccord.yml) runs on every
  push and pull request that touches `ByGalacticAccord/` (or the workflow): restore, Release build, fast tests,
  soak tests, then the browser check (`npm ci`, `npx playwright install --with-deps chromium`, `node check.cjs`),
  on Ubuntu, and uploads the check's screenshots. On `main`, once that is green, its `docker` job builds the hosting image and
  pushes it to `ghcr.io/gemberkoekje/bygalacticaccord` as `:latest` and `:<commit sha>`; gembernodes deploys a sha.
- The hosting image: `nginx.conf` serves `/` (no fallback: the client is one page), the `.gz` files the publish writes, and
  `/healthz` for the pod's probes; `_framework/` is cached for a year (every name there carries its fingerprint), the rest
  revalidates on each load. The build stage drops the `.br` files (stock nginx has no Brotli).

## 3. How a run flows

```
WorldGenerator.Generate(seed, withPlayer, config)          Simulation/WorldGenerator.cs
  └─ new SimulationContext(seed, config)                    Simulation/SimulationContext.cs
     adds 7 Locations, 9 Routes, 8 producers, 7 consumers, 3 part factories, 2 shipyards, Edge Salvage (with a worn
     Refrigerated Transport), 1 government, 7 traders, the player if asked, and a station operator ("<Location> Docks") at every
     location; starting stock (a factory's: one of each part it makes and half its inputs) through Produce
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
| `ActorHeartbeat` | `Events/ControlEvents.cs` | Nothing for a dissolved actor (its heartbeat stops). `DecisionEngine.ChargeRunningCosts` for every actor, **the player too** (a ship laid up for sale costs a quarter; charged below ₵0); `Solvency.ChargeInterest` on debt; `Knowledge.Look` (it reads what it sees where it is present); `Solvency.Review` (warn, lift, or declare insolvent: `Liquidation.Begin`); then an insolvent actor's `Liquidation.Run`, else `DecisionEngine.Run` unless it is the player. Factories, yards and Edge Salvage act on it too: no event of their own | Itself, `HeartbeatInterval` (24) later, unless dissolved |
| `ContractExpiryCheck` | `Events/ControlEvents.cs` | Still open at its deadline → `Market.Expire` (frees what the post set aside) | — |
| `GraceEnded` | `Events/ControlEvents.cs` | At `Contract.GraceEndsTick` (deadline + grace), scheduled when someone waits on a performer (`Market.Supply` unless handed over at once or collected; `TakeHaul`). Still `Active` → in default: records `ContractInDefaultEntry`; a player payer gets a `PlayerDecision` (the hard pause); an AI payer `DecisionEngine.ReviewDefault`s | `DefaultReview` (an AI that waits) |
| `DefaultReview` | `Events/ControlEvents.cs` | Still in default, AI payer → `ReviewDefault` again | Itself via `ReviewDefault`, a day later |
| `InflationCheck` | `Events/ControlEvents.cs` | `InflationMonitor.Evaluate` | Itself, every 400 |
| `ReputationDecay` | `Events/ControlEvents.cs` | `ReputationLedger.DecayTowardNeutral` | Itself, every 400 |
| `ShipDeparted` | `Events/TransitEvents.cs` | The ship reaches its job's pickup (`Ship.ReachPickup`), its owner looks around there (`Knowledge.LookAt`), and `Settlement.Load`s; while the player inspects what it bought there, it reschedules itself and waits; a job with no destination (collecting a lot, or a ferry: `ShipJob.IsFerry`, a salvage yard's ship flying home empty) ends here; nothing aboard → the Sale it serves fails; else `Ship.DepartLaden`, the laden leg's hours from `Throttles.LadenTicks` at the job's `Throttle`, counted (`NoteLadenTrip`) | `TransitIncidentCheck` at mid-route; itself (waiting on an inspection) |
| `TransitIncidentCheck` | `Events/TransitEvents.cs` | `Rng.Chance(Throttles.IncidentOdds)`: scale × the route's worst danger × the ship's incident chance × the throttle's factor; an incident is `IncidentResolver.Resolve`d with the danger's shift | `TransitIncidentOccurred`, or `ShipArrived` |
| `TransitIncidentOccurred` | `Events/TransitEvents.cs` | Applies the `TransitIncident` to the job owner's lot (`Ship.Alter`: damage, partial or total loss through `Lose`), a credit cost (`Charge`, below ₵0 if it must), `Ship.DelayArrival`; records an `IncidentEntry` and a line of the ship's history (`ShipRecord`, `Incident`); raises it as a player alert if the ship is theirs | `ShipArrived` (plus any delay) |
| `ShipArrived` | `Events/TransitEvents.cs` | `Ship.Arrive`, `Dock` (the docking fee to the station operator there, `TransferOwed`), `Settlement.Unload` (a haul cancelled on the way still sets the shipper's goods down, and they go on to the Sale it served), `Ship.FinishJob`, its owner looks around there (`Knowledge.LookAt`) | — |
| `PostReached` | `Events/NegotiationEvents.cs` | The player's post reaches an AI actor that can see it, which answers: `DecisionEngine.Respond` takes it as posted (`TakeOn`), counters (`Market.Counter`), or lets it pass. Scheduled by `DecisionEngine.NoticePost` at posting, after the actor's reach (`Sight.ReachHours`: until the post is shown where it is present) and its reply time; again when the player pays to show it further (`Advertising.Show` → `NoticePost(newlyShown)`), for the actors present only there; by `NewsDesk.Publish` for the actors that learn of it from The Daily Accord | `ReplyDue` (through `Market.Counter`) |
| `ReplyDue` | `Events/NegotiationEvents.cs` | An AI's reply to the player's offer: `DecisionEngine.Reply` accepts (`Market.AcceptOffer`), counters, or ends it (`Market.EndNegotiation`); dropped if the haggle moved on or ended. Scheduled by `DecisionEngine.ExpectReply` 1–12 hours after the offer | — |
| `PricesPosted` | `Events/InformationEvents.cs` | The morning bulletin at 06:00 (design §7.1): `PriceBulletin.Post` records every producer's ask; `Knowledge.ReadBulletin`: every actor notes the asks of places one jump from its presence | Itself, a day later |
| `NewsPublished` | `Events/InformationEvents.cs` | The Daily Accord at 06:00, after the bulletin: `NewsDesk.Publish` gathers every contract with a journal entry yesterday, words each by where it stands, draws `NewsItemsPerDay` (3) by weighted chance from `Rng`, makes a producer's lot known (`Knowledge.ReadLot`) and sends the player's reported posts to the actors that couldn't see them (`DecisionEngine.NoticePost(fromNews: true)`). Prints nothing when `NewsItemsPerDay` is 0 | Itself, a day later |

Every transit event carries the `ShipJob` it belongs to and does nothing if the ship's job is no longer that
one (`ReferenceEquals`).

**A delivered Sale's life** (consumer → trader): `RunConsumer` → `PostWanted` (priced from the cheapest ask the consumer
knows, `Knowledge.ReadingOf`; its package from personality, relaxed by `Terms.Meetable` to what a standard hold can meet)
→ `Market.PostSale` (Wanted, receipt = the consumer's stock at home; terms from `Terms.ForSale`; schedules
`ContractExpiryCheck`; records `ContractPostedEntry`; `SimulationContext.Add` shows it at its places,
`Advertising.ShowOnPosting`; `DecisionEngine.AnswerAtOnce` lets an AI producer at the place answer it at once) →
unsold a day, the consumer's `Run` shows it a ring further (`Advertising.AdvertiseUnsold`) → a trader's `RunTrader` →
`BestOption` over what it believes open (`Known`: seen live where it is shown and the trader is present, or reported in
the news; tried twice if the first has been taken since) → `FillSources` (the asks it knows,
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

**Showing a post further** (design §7.1, D38): `Advertising.Options` lists the systems a post isn't shown in yet, nearest
first, with `FeeAt` (`AdFeeShare` of the price, at least `AdFeeFloor`, times `AdFeeGrowth` a jump past the first; jumps by
`SimulationContext.Jumps`, from the nearer of its places) and `DelayHours` (`AdDelayHours`). `Advertising.Show` checks the
whole `Quote` is affordable, pays each fee to that system's station operator (`TryTransfer`), shows the post there from now
plus the delay (`Contract.ShowAt`), and for the player's post calls `NoticePost(newlyShown)`. Callers: `AdvertiseUnsold`
(every AI actor's `Run`: a post open `AdWaitHours` since it was posted or last shown further gets the next ring, every
system at the smallest distance not yet shown, if that costs at most `AdBudgetShare` of its price; never storage or an
order at the door), `PlayerActions.Post` (the draft's `ShowAt`) and `PlayerActions.Advertise`. `Sight.CanSee` reads
`Contract.ShownFrom` at each place the actor is present; `Market.Sign` counts a signing whose taker saw it only through
an ad (`SignedViaAd`).

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

**A part's life** (design §15.4): `RunFactory` (`DecisionEngine.Factories.cs`) → `FinishWork` (a part done: `Produce` into stock, `NotePartMade`)
→ `StartWork`: `NextPart` (the oldest wanted Sale it knows for a part it makes and doesn't hold, at that grade; else the kind it holds least of, under
`PartRecipe.StockCap`, at its `FactoryProfile.Quality`) → inputs present: `Consume` them, `ApplySink` the `Labour`, `FactoryWork` for the recipe's
hours; short: it waits, unless the part is wanted or it has none of it and has been short `MakeDoAfterHours` (72), when it makes it without what
is missing, a grade lower for each (`NoteMadeDo`) → `BuyInputs` (`PostWanted` at its door, priced from known asks, worth `InputWorth`) → it sells
its parts as a producer sells goods: `TryAnswer` at its door, `SellDelivered` per part in stock (both through `ProfileFor`, `DecisionEngine.Ask(actor,
good)` → `PriceModel.PartAsk`), and traders buy them at its door to fill a yard's order (`FillSources` over `ProducersOf(part)`).

**A Build's life** (design §6.3, §16.4): a client posts it (`Market.PostBuild`: shown at the place it is posted, D38; jurisdiction provisional) → an
AI client's Build is taken by a yard that sees it on its heartbeat (`BidOnBuilds`: `BuildCost` ≤ the price as posted, else `ClosedForm`; `Ready` by
the deadline); the player's reaches the yards it is shown to (`NoticePost`, `Answers`: `actor.Yard`) and each bids with a counter (`BuilderQuote`:
its reservation from `BuildCost`, on its `YardProfile.Habit` package and a deadline it can make) → `Market.TakeBuild`: `PerformUnder` the yard's
jurisdiction, `Sign` (the deposit `TransferUpTo` the yard, the balance and tax `LockToEscrow`, the yard's bond), a `YardJob` queued, `GraceEnded`
scheduled → `RunYard`: jobs in order (`YardState.Jobs`), each taking parts in stock (`InStock`: the grade wanted, a better one, or a lower one
`Terms.Allows`; past its deadline any), ordering what is missing (`Order` → `Market.PostPartsOrder`, a wanted Sale of one part delivered to its
stock, `SaleContract.ForBuild`, posted with the Build's latest entry as its cause; the grade by `ChooseGrade`), the current job assembling once
every part is in (`ApplySink` labour, `YardJob.AssemblyDone`) → `Finish`: `SimulationContext.BuildShip` (the parts built in) → `ShipBuiltEntry` →
`Settlement.HandOverBuild`: `Terms.Substitution` against the spec (a discount within the policy; outside it the client inspects: an AI
`WouldRejectShip`, the player a `DecisionKind.Inspection`); accepted, escrow pays the yard the balance less discounts and the late fee, the tax is
collected, `TransferShip` to the client, `ShipHandedOverEntry`; rejected, `Fail` (escrow, bond and deposit back; the ship stays the yard's). An
order that ends without its part (expired, failed, cancelled) is a failure upstream: in time, the yard reorders; not, `ProposeSupplyFailure`
(`SupplyFailure` on the Build, `SupplyFailureEntry` caused by the order's last entry) → the client chooses (`ChooseRemedy` for an AI by utility;
the player a `DecisionKind.SupplyFailure`) → `Market.ChooseRemedy` (`Extend`: a later deadline and compensation; `Substitute`: the slot's grade
lowered, `AgreedDiscount`, a sooner deadline; `Cancel`: `Settlement.Cancel` with `CancelReason.SupplyFailure`) → `RemedyChosenEntry`. Late: the
late fee from the bond; in default an AI client waits while the yard has the job (`WouldCancel`), the player is asked. Cancelled at will by the
client, the yard keeps the deposit; by the yard, it gives it back and pays as much again (`Settlement.CancelBuild`).

**A ship's sale** (design §3.2, §6.3): `Market.PostShipSale` (the ship free and empty, `Ship.List`; laid up meanwhile) → a buyer signs
(`Market.BuyShip`, or `Market.SignAgreed` after a closed-form haggle) → `Settlement.HandOverShip` at once (escrow in Accord space, the tax collected;
on the frontier the buyer pays or defaults and the seller keeps its ship) → `TransferShip`, `ShipHandedOverEntry`; withdrawn or expired, the ship is
unlisted (`ReleaseOpen`). Sellers: a yard's lot (`SellLot`: ships it built on spec, its value plus half its margin, 5% less a fortnight, at least
half; a sale within `SpecSellsWithinHours` tells it to build another, `YardState.SpecSells`), a trader's idle ship (`ManageFleet`), Edge Salvage
(`RunSalvage`). Buyers: a trader that `WantsShip` (`Acquire`: its design at up to 95% of the value, another at 85%; else `PostBuild`), Edge Salvage
(at most `SalvageProfile.BuyShare`; it ferries its purchases home, `Market.Ferry`, and breaks up one unsold a year, `ScrapShip`), the player.

**A fleet** (`DecisionEngine.Ships.cs`): `RunTrader` ends with `ManageFleet`: a trader whose ships were busy (`FleetUtilization` over
`FleetWindowHours` ≥ `GrowAbove`, a pilot `PilotGrowAbove`, eased by ambition), under its cap (`CompanyFleetCap` 3, `PilotFleetCap` 2) and with no
Build up, `Acquire`s; its working capital keeps the ship's price meanwhile (`GrowthFund`, so growth spends what `PayOut` would sink). A fleet idler
than `ShrinkBelow` sells its idlest ship once it has sat free ten days, one up at a time, priced down a fortnight at a time; a pilot whose one dear ship
barely works and whose savings are worth less than it sells it to buy a cheaper one (`Uneconomic`).

**Debt and insolvency** (design §5.4, §16.8): charges below ₵0 (`Charge`: running costs, interest, an incident's cost; `TransferOwed`: rent,
docking, a remedy the deep Accord enforces, `Settlement.Collect`) → `ActorHeartbeat` → `Solvency.ChargeInterest` → `Solvency.Review`: debt above
half of `Assets` records a `DebtWarningEntry` (the player's an alert), at half or less a `DebtWarningLiftedEntry`; above all of it, or above half
for `WarnedHoursLimit` (seven days), `Liquidation.Begin`: `InsolventEntry` (the player's an alert: the game is over, `GameSession.IsOver`), its
haggles closed (`NegotiationEnd.CannotPerform`), its open posts withdrawn, every contract it signed cancelled with `CancelReason.Insolvency`
(`Settlement.SettleAs`: an insolvent performer for default, an insolvent payer at will, its break fee to the performer; the insolvent party is
the canceller) → on later heartbeats `Liquidation.Run`: each free ship has its owner's goods set down in storage and is listed in a fire sale
(`Market.PostShipSale`, 60% of its value, 10% less a week, at least 30%), its goods go up as fire-sale lots (`Market.PostSale`, 60% of base
value, likewise), each shown in every system at once, free (`ShowEverywhere`, `Contract.FireSale`); after `LongestHours` (60 days) what is
unsold is broken up (`ScrapShip`) or written off (`Lose`) → nothing left: `Dissolve` (`WriteOffDebt`, or what is left paid out), an
`ActorDissolvedEntry`, `SolvencyStatus.Dissolved`. An actor not `InBusiness` signs nothing (`Signing.Insolvent`), answers no post and replies to
no haggle, and an insolvent maker leaves `ProducersOf`. A trader in debt sells a spare ship (`ManageFleet`), a yard in debt marks its lot down to
what Edge Salvage pays (`SellLot`), and what any actor in debt sells is shown everywhere (`Advertising.ShowOnPosting`).

**The throttle** (design §8.4, §16.7): a `ShipJob` carries its `Throttle`. `Throttles` holds the rules every laden trip's hours and odds come
from. The AI's `HaulTake` and `FillTake` score both and keep cruise unless at cruise it would be late and pushing is worth more (`Better`); the
choice rides `Take`, `TraderOption` and `Plan` into `Market.TakeHaul` and `FillWithShip`. The player picks in the contract panel
(`ContractView.Push`, `GameSession.Accept(id, throttle)`), and `DealEstimate.Pushed` and `OnTimeOnlyPushed` say what pushing does: the
first hour's trap reads "on time only if you push" on the board.

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
| `ContractPostedEntry` (poster) | `Market.PostSale` (a parts order's caused by its Build), `PostHaul`, `PostBuild`, `PostShipSale` |
| `ContractSignedEntry` (taker, price) | `Market.Sign` (every signing) |
| `GoodsHandedOverEntry` (seller, buyer, quantity, quality, ticks late, price, late fee, refund) | `Settlement.Complete` (via `HandOverReserved`, `HandOverCargo`) |
| `HaulDeliveredEntry` (hauler, quantity, quality kept in the hold, ticks late, fee, late fee, refund, cargo liability) | `Settlement.DeliverHaul` |
| `PaymentDefaultedEntry` (payer, payee) | `Settlement.PayerDefault` (outside the Accord) |
| `DeliveryFailedEntry` (performer, `FailureReason`: `CargoLost`, `NotDelivered`, `ShortDelivery`, `Rejected`; delivered, quality) | `Settlement.Fail` (a ship rejected: through `HandOverBuild`) |
| `ContractInDefaultEntry` (performer, payer) | `GraceEnded` |
| `ContractCancelledEntry` (by, `CancelReason`: `ForDefault`, `AtWill`, `SupplyFailure`, `Insolvency` (by: the insolvent party); paid: the bond taken, the break fee, or a supply failure's compensation) | `Settlement.Cancel` (a Build's: `CancelBuild`) |
| `ContractExpiredEntry` | `Market.Expire` |
| `ContractWithdrawnEntry` | `Market.Withdraw` |
| `StorageOpenedEntry` (renter), `StorageClosedEntry` (renter, rent paid) | `SimulationContext.PutInStorage`, `TakeFromStorage` |
| `IncidentEntry` (hauler, the `TransitIncident`) | `TransitIncidentOccurred`; also the player's alert |
| `FaucetAdjustedEntry` (drift, multiplier) | `InflationMonitor.Evaluate`; never shown in the client |
| `OfferMadeEntry` (haggle, by, to, the `Offer`, round, reply hours and `Tell` for a reply) | `Market.Counter` |
| `OfferAcceptedEntry` (haggle, by, to, the offer, reply hours, tell) | `Market.AcceptOffer` |
| `NegotiationEndedEntry` (haggle, by, to, `NegotiationEnd`: walked away, rounds ran out, declined, could no longer perform) | `Market.EndNegotiation`, `AcceptOffer` |
| `ShipBuiltEntry` (Build, yard, ship) | `DecisionEngine.Finish` (a yard's job for a Build) |
| `StockShipBuiltEntry` (yard, ship; no contract) | `DecisionEngine.Finish` (a ship on spec) |
| `ShipHandedOverEntry` (contract, seller, buyer, ship, ticks late, price, late fee, discount) | `Settlement.HandOverBuild`, `HandOverShip` |
| `SupplyFailureEntry` (Build, yard, the failed order, the part's slot) | `DecisionEngine.ProposeSupplyFailure` |
| `RemedyChosenEntry` (Build, by, `Remedy`) | `Market.ChooseRemedy` |
| `ShipScrappedEntry` (owner, ship; no contract) | `DecisionEngine.RunSalvage`, `Liquidation.Run` (a fire-sale ship unsold in 60 days) |
| `DebtWarningEntry` (actor, debt, assets, insolvent by; `IActorEntry`, no contract) | `Solvency.Review` |
| `DebtWarningLiftedEntry` (actor, debt, assets) | `Solvency.Review` |
| `InsolventEntry` (actor, debt, assets, `InsolvencyReason`: `DebtAboveAssets`, `StayedAboveHalf`) | `Liquidation.Begin` |
| `ActorDissolvedEntry` (actor, raised, written off, left over) | `Liquidation.Run` (`Dissolve`) |

`Presentation/Text/JournalText.cs` words each one from `Templates.resx`, keyed by the entry type's name
(without "Entry") and the contract's kind (`Sale.Wanted`, `Sale.ForSale`, `Haul`, `Build`, `Ship` for a ship's Sale), with a `.You` twin when the
player is the subject and a `.ToYou` twin when the player is the other party (a cancellation on insolvency is keyed
`ContractCancelled.Insolvency.Performer` or `.Payer` by which side the insolvent party was). Every entry about a contract
implements `IContractEntry`; an entry about an actor's debts implements `IActorEntry` (the log shows the player its own, and anyone's
insolvency and dissolution); a haggle's entries implement `INegotiationEntry` and chain among themselves from the post
(`Record` keeps each haggle's latest), so a haggle that came to nothing leaves the contract's record alone, and only an
accepted offer becomes the contract's latest, the cause of its signing.

## 4. Where state lives: `SimulationContext`

`Simulation/SimulationContext.cs` (about 1,330 lines) is the world and the hub; nearly everything takes it.

- **Collections:** `Actors`, `Ships`, `Locations`, `Contracts`, `Negotiations` (dictionaries by typed id; contracts
  and haggles are never removed), `Routes` (list). Registered with the `Add(...)` overloads; `Actor(id)`, `Ship(id)`,
  `Location(id)`, `Contract(id)`, `Negotiation(id)` look up; `NegotiationsOn(contract)` (the open ones, pruned) and
  `NegotiationWith(contract, responder)`; internal `CloseNegotiationsOn` ends them when a post leaves the board.
- **Ordered views (use these where order matters):** `ActorsOrdered` (by id, cached), `OpenContracts` (open or
  negotiating, by id; a pruned list, because a contract never returns to Open), `LiveContractsOf(actor)` (the
  actor's open and active contracts in the order it joined them; pruned as they settle), `ProducersOf(good)` (an actor that
  `Makes` it: a producer's good, or every grade of a factory's part kinds), `StationOperatorAt(location)`.
- **Graph:** `FindPath(from, to)` → `RoutePlan?` (Dijkstra in `SolvePath`, cached per pair; `Add(Location)` and
  `Add(Route)` clear the cache), `RouteBetween(a, b)`, `Adjacent(a, b)` (cached), `Jumps(a, b)` (the fewest jumps, by
  breadth-first search, cached; `Advertising`'s distances), `Neighbours(id)`.
- **Storage:** the active `StorageContract`s, by renter and location: `StorageOf(renter, location)`,
  `StoragesOf(renter)`, `StoragesAt(location)`. `PutInStorage` opens one at the location's station operator on
  first use; `TakeFromStorage` closes it when it empties.
- **Services it owns:** `Rng` (`DeterministicRandom`), `Queue` (`EventQueue`), `Journal` (`EventJournal`, a ring
  of 5,000 entries), `Reputation` (`ReputationLedger`), `Config` (`SimulationConfig`), `Bulletin` (`PriceBulletin`: the
  morning's asks), `News` (`NewsDesk`: The Daily Accord's issues and the posts it made known), `Records` (`Records`: the
  public record and each actor's dealings). Each actor's knowledge is on the actor (`ActorState.Knowledge`).
- **Bookkeeping the invariants read:** `InitialCredits`, `TotalFaucet`, `TotalSink` (and by kind, `Sunk(SinkKind)`), `DebtWrittenOff`, `EscrowLockedTotal` /
  `ReleasedTotal` / `ForfeitedTotal`, `TotalEscrowHeld` (over the contracts that ever held escrow),
  `GovernmentRevenue`, the goods ledger per good (`GoodsProduced`, `GoodsConsumed`, `GoodsLost`, `GoodsHeld`:
  stock, storage and holds; `GoodsBuiltIn`: parts built into ships that still exist), counters `ContractsFulfilled` / `Breached` / `Defaulted` / `Cancelled`, `GoodsSold`,
  `DealsHaggled` (AI-to-AI, closed form), `NegotiationsOpened` / `NegotiationsAgreed` (with the player),
  `IncidentsOccurred`, the failure-path tallies (`NoteFailure`, `Failures(FailureKind)`), information telemetry
  (`DoorOrders`, `DoorRefusedForPrice`, `DoorRefusedForStock`, `SignedFromNews`, `FoundClosedFromNews`), advertising
  telemetry (`AdFeesPaid`, `AdsShown` by jumps, `SignedViaAd`), ship telemetry (`PartsMade` by grade, `PartsMadeDo`, `ShipsBuiltToOrder`,
  `ShipsBuiltOnSpec`, `BuildsSigned`, `RemediesChosen` by remedy, `ShipsSold`, `ShipsBoughtBySalvage`, `ShipsSoldBySalvage`, `ShipsScrapped`), debt
  telemetry (`Insolvencies`, `Dissolutions`, `FireSaleShipsSold`, `FireSaleShipsToSalvage`, `DebtWarnings`, `DeepestDebt`), transit telemetry
  (`IncidentsByKind`, `LadenTrips` by throttle), and `FaucetMultiplier` (the inflation monitor's knob).
- **Ships:** `BuildShip(yard, parts, owner, build, wanted)` (one of each part leaves the yard's stock, built in; the ship is commissioned at the
  yard), `TransferShip(ship, to, price, contract)` (the fleets and the ship's history follow), `ScrapShip(ship)` (out of its owner's fleet; parts
  built in written off as lost; the ship stays, `Scrapped`, for the record).
- **Other:** `Add(Contract)` shows a new contract at its places (`Advertising.ShowOnPosting`), `Sign` (internal: `Market.Sign`'s record-keeping), `OpenStorages` and `LocationIdLimit` (for `Visibility.Of`), player
  alerts (`RaisePlayerAlert(JournalEntry)`, `HasPlayerAlerts`, `PlayerAlerts`, `ClearPlayerAlerts`: an alert is
  the entry that needs the player), player decisions (`PlayerDecisions`, `HasPlayerDecisions`,
  `PlayerDecisionFor`; internal `RaisePlayerDecision`, `ResolvePlayerDecision`), `Record(JournalEntry)` (stamps
  the sequence and the default cause, returns the entry as kept; a haggle's entries chain within the haggle).

## 5. Money and goods: the only paths

Every credit and unit moves through these `SimulationContext` primitives. Anything else is a bug.

**Money:**

| Primitive | Effect | Called from |
|---|---|---|
| `ApplyFaucet` | Creates credits | `RunConsumer` (income × `FaucetMultiplier`); `GameSession.Grant` (the `?credits=` check aid) |
| `ApplySink` | Destroys credits an actor chooses to spend, **capped at its balance**, booked by `SinkKind` | `Produce` in `RunProducer` (`Production`), `StartWork` (`Labour`: a factory's on a part), `Progress` in `RunYard` (`Labour`: a yard's on an assembly), `PayOut` (above working capital), `Liquidation.Dissolve` (what a dissolved actor had left), `Advertising.Show` (an ad's fee where no station operator takes it: never in the generated sector) |
| `Charge` | Destroys credits **whether or not the actor has them** (design §16.8): what it lacks becomes debt | `ChargeRunningCosts` (`RunningCost`: every ship, every heartbeat; a quarter while laid up for sale), `Solvency.ChargeInterest` (`Interest`), `TransitIncidentOccurred` (`Incident`), `ShipArrived.Dock` (`Docking`, only where no station operator takes the fee: never in the generated sector); `GameSession.Grant` (a negative check aid) |
| `WriteOffDebt` | Brings a negative balance back to ₵0, booked as `DebtWrittenOff` (credits returning to the supply) | `Liquidation.Dissolve` |
| `TaxFromEscrow` | Destroys credits as tax from a contract's escrow, books `GovernmentRevenue` | `Settlement.Pay` (Accord space only: the frontier is untaxed) |
| `TransferOwed` | Moves credits **whether or not the payer has them**: what it lacks becomes its debt | `SettleRent` (renter → station operator); `ShipArrived.Dock` (owner → the station operator where it docks); `Settlement.Pay`, `Fail` and `HandOverBuild` in the deep Accord (penalties, cargo liability and a late fee beyond escrow, enforced); `Settlement.Collect` (a failed Build's deposit back, deep Accord) |
| `TryTransfer` | Moves credits between actors, all or nothing | `Settlement.Pay` outside the Accord (the payer pays the net at handover, or `PayerDefault`); `HandOverBuild` and `HandOverShip` outside the Accord (the balance, or a ship's price, at handover); `Advertising.Show` (an ad's fee, poster → that system's station operator, after the whole quote was checked affordable) |
| `TransferUpTo` | Moves at most what the payer has | `Settlement.Cancel` (a break fee beyond what escrow holds of the canceller's money); `Market.Sign` (a Build's deposit, client → yard); `Settlement.Collect` outside the deep Accord, and `CancelBuild` (a Build's deposit back, a yard's break fee and a supply failure's compensation, yard → client); `Market.ChooseRemedy` (an extension's compensation, yard → client) |
| `LockToEscrow` | Actor → a contract's escrow, **in whole cents**, only what it has | `Market.Sign`: the payer locks price + tax (a Build's balance, `Contract.EscrowedPart`, + tax), on the terms as agreed (`Market.SignAgreed` re-scales them from a haggled price first) |
| `LockBondToEscrow` | A performer's bond → escrow, **in full and in whole cents**, below ₵0 if it must (the clearing house lends it) | `Market.Sign`, once `Solvency.CanBond` says the debt stays within half of what the performer owns |
| `ReleaseFromEscrow`, `ForfeitFromEscrow` | Escrow → an actor, in whole cents, capped at what the contract holds | `Settlement.Pay` (the payment and bond less penalties to the performer, the penalties and the rest to the payer), `Settlement.Fail` and `Cancel` for default (bond to the payer, escrow back), `Cancel` at will (the break fee from the canceller's share, the rest back), `HandOverBuild` (the balance less discounts and bond less late fee to the yard, the rest to the client), `HandOverShip` (the price to the seller), `CancelBuild` (escrow back to the client; the bond to the yard, or to the client for default or a yard's walking away) |

A station operator's rent, docking fees and ad fees above its float leave through `PayOut`, as upkeep (S8 measured the alternative, plan S8 /
`log/S8.md`: the knobs `TradersPayOut` and `StationsPayOut`, the soak harness's `--keep`; and bonds in cash only, `BondsOnCredit`,
`--cash-bonds`). A trader's working capital counts the ship it wants
(`GrowthFund`), a factory's is `FactoryReserve`, a yard's and Edge Salvage's `YardReserve`. `TaxDirect` has no callers since
S3. An order at a producer's door (`SaleContract.AtTheDoor`, posted only by `Market.BuyAtSource`) is posted at the
buyer's limit (`DoorLimit`: 10% over the ask it knew) and signed at the producer's ask as it stands, so escrow locks
the ask, not the limit; a producer whose ask has risen past the limit turns it down and nothing moves.

**Goods** live in three places: an actor's stock (`ActorState`, with a reserved part), its storage at a
location (`StorageContract.Lots`), or a ship's hold (`Ship.Hold`, `Lot`s by owner). Perishables decay lazily:
a `Lot` remembers when it was last fresh-stamped and `At(tick, preservation)` computes its quality.

| Primitive | Effect | Called from |
|---|---|---|
| `Produce` | Creates goods in stock (ledger: produced) | `RunProducer`, `FinishWork` (a factory's part), `WorldGenerator` (starting stock) |
| `Consume` | Destroys goods from stock (ledger: consumed) | `RunConsumer`, `StartWork` (a factory's inputs) |
| `Lose` | Books goods destroyed in transit or written off (ledger: lost) | `TransitIncidentOccurred`, `SellOff` (stale stored goods: a trader's, and since S8 a producer's or factory's), `ScrapShip` (a broken-up ship's parts), `Liquidation.Run` (an insolvent actor's goods unsold in 60 days, or less than a unit) |
| `BuildShip` | Parts out of a yard's stock into a ship (ledger: built in) | `DecisionEngine.Finish` |
| `TakeFromStock`, `PutInStock`, `ReserveStock`, `UnreserveStock` | Stock out, in, set aside, freed | `Settlement` (handover, haul pickup, `Keep`, `Unreserve`); `Market.Reserve` |
| `PutInStorage`, `TakeFromStorage`, `ReserveStorage`, `UnreserveStorage` | Storage in (opens it), out (closes it when empty), set aside, freed | `Settlement`, `Market.Reserve`, `PlayerActions.Store`/`Retrieve`, `Stow` and `SellOff` in `RunTrader` |
| `SettleRent` | Charges rent due (rate × units × days) to the renter, below ₵0 if it must (`TransferOwed`) | `RunStation`; `PutInStorage` and `TakeFromStorage`, before what is held changes |
| `Load`, `Unload` | Into and out of a hold, by owner | `Settlement.Load`/`Unload`/`Receive`, `Market.FillWithShip` (goods already aboard), `PlayerActions`, `Stow` |

Goods conservation (design §20.2) is produced = consumed + held + lost + built in, per good; and the parts of every ship built in this
world, counted by good, are exactly what is built in (`Invariants`, `PartsInShips`). Goods move only at handovers (`Settlement.HandOverReserved`, `HandOverCargo`), a ship's pickup and arrival
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
| `Goods.cs` | `GoodType` (the 7 ordinary goods, then the 24 ship parts: each kind at grade A, B and C), `GoodCategory` (with `ShipPart`), `GoodInfo` (base value, `KeepsHours` in a standard hold, `Refrigeratable`; `Perishable`, `QualityDecayPerTick` from the keep-time), `Goods.Info` table (Food 7 days, Medicine 2: design §15.3; a part at its base price times its grade's `ValueShare`), `Goods.Trade` (the 7), `Goods.All` (all 31), `Goods.StandardWarranty` (0.7) | `Cargo`, `Lot`, `PriceModel`, `Terms`, `DecisionEngine`, `Settlement`, `Invariants`, the views |
| `Cargo.cs` | `Cargo(good, quantity, quality)`: `Fresh`, `Decayed(ticks, preservation)`, `LosingUnits`, `Damaged`, `Merged` (weighted quality) | `Lot`, `Ship`, `StorageContract`, `Settlement`, `TransitEvents` |
| `Lot.cs` | `Lot(owner, cargo, since)`: goods held somewhere, decaying lazily; `At(tick, preservation)` | `Ship.Hold`, `StorageContract.Lots`, the ledger |
| `Location.cs` | `AccordReach` (jurisdiction), `LocationType`, `MapPoint` (schematic map units), `Location` (region, reach, residents, `Map`) | `WorldGenerator`, `Visibility`, `DecisionEngine`, `MapView` |
| `Route.cs` | `Route` (two ends, hours, danger 0..1, law, `Band`), `RoutePlan` (distance, worst leg's danger, hops, path, `Band`), `DangerBand` | `SimulationContext.FindPath`, `DecisionEngine`, `TransitEvents`, `Market`, the views |
| `Contract.cs` | `ContractType` (Sale, Haul, Storage, Build created; the rest named), `ContractStatus` (Open (haggled over or not), Active, Fulfilled, PartlyFulfilled, Breached, Cancelled, Defaulted, Expired, Withdrawn), `SaleSide`, `HoldingKind`, `Holding` (stock, storage or aboard a ship, at a location); abstract `Contract` (poster, counterparty, offered and agreed price, hidden `Reservation` (the player's posts: its price), tax, deadline and `PostedDeadline`, `Terms`, `GraceEndsTick`, `InDefault(now)`, status, `Cancellation`, `LastEntry`, jurisdiction (internal `PerformUnder`: a Build's, from its yard), `UsesEscrow`, `Arbitrated`, escrow held, `EscrowedPart` (a Build's balance), locked bond, `Payer`/`Performer`, `Places`, `LatePenalty`, `Warranty`, `PerformanceBond`; where it is shown (`ShownFrom(location)`, `Shown`, `Advertised`, `LastAdvertised`, `AdFeesPaid`; internal `ShowAt` keeps the earliest, `NoteAdvertised`, `NoteAdFee`, design §7.1, D38); internal transitions, and `Amend` (an agreed offer's deadline and terms, before signing; a remedy's deadline after)); `SaleContract` (side, good, quantity, place, buyer/seller, receipt, `Collected`, reserved, delivered, `UnitPrice`, `SeenQuality` of a lot in storage, `AtTheDoor`: an order at a producer's door, never on a board or in the news; `ForBuild`: a yard's parts order); `HaulContract` (good, quantity, origin, destination, pickup holding, the Sale it `Serves`, `DeclaredUnitValue`, shipper/carrier, assigned ship, `PickedUp`, `PickupQuality`, `InTransitQuality`); `StorageContract` (operator, renter, rate per unit-day, lots, reservations, rent charged; signs itself; `ContractTerms.None`); `ShipSaleContract` (Type Sale: a ship for sale where it is docked, seller/buyer); `Remedy` (Extend, Substitute, Cancel), `SupplyFailure` (a yard's proposal: the slot, the failed order, each remedy's figures, its entry); `BuildContract` (`Spec` and `Ordered` parts, `Place` shown, `Yard` from signing, client/builder, `DepositShare` 20%, `DepositFor`, `DepositPaid`, `Ship` built, `AgreedDiscount`, `Proposal`, `Remedies`; internal `Substitute`, `Propose`) S8: `FireSale` (an insolvent actor's goods or ship, sold by the clearing house and shown in every system); `LockedBond` is the whole bond since S8. | `Market`, `Settlement`, `DecisionEngine`, events, `Visibility`, `Invariants`, the views |
| `ContractTerms.cs` | `TermPackage` (Strict, Standard, Lenient), `PartialDelivery` (ProRata, Failure), `CancelReason` (ForDefault, AtWill, SupplyFailure), `Cancellation`; `PenaltyOnLateDelivery` (per hour late, capped; `HoursToCap`); `QualityWarranty` (minimum, most refunded, `RejectBelow` floor; `RefundFraction`, `Rejectable`); `CargoLiability` (per unit, cap; `For(lost)`); `ContractTerms` (package, grace, late fee, break fee, partial delivery, warranty, liability, `LateFeeScale`; `Bond` = the late fee's cap; `None`) S8: `CancelReason.Insolvency`. | `Contract`, `Terms`, `Settlement`, `DecisionEngine`, `DealEstimate`, `ContractView` |
| `Offer.cs` | `Offer` (price, deadline, package, late-fee scale; `Of(contract)`, `SameTermsAs`, `LateFeeScales` the half, usual and double a counter may name) | `Market`, `DecisionEngine`, `PlayerActions`, `Terms.For`, the journal's haggle entries, the views |
| `Negotiation.cs` | `NegotiationStatus` (Open, Agreed, Ended), `NegotiationEnd` (WalkedAway, RoundsRanOut, Declined, PostClosed, CannotPerform), `Tell` (None, Readily, Reluctant, AtLimit), `Bid` (round, tick, by, offer, reply hours, tell), `Negotiation` (a haggle over one contract between its poster and a responder: bids, `Rounds`, `AwaitingFrom`, `ReplyDue`, `LatestBy`; internal `Add`, `AwaitReply`, `Agree`, `Close`) | `SimulationContext`, `Market.Haggling`, `DecisionEngine`, `PlayerActions`, `GameSession`, the views |
| `Ship.cs` | `ShipStatus` (Docked, InTransit), `PickupSource` (Purchase, Haul, Storage, Aboard), `ShipEventKind` (Commissioned, ChangedHands, Incident, Scrapped), `ShipRecord` (a line of its history, design §6.4: who, to whom, contract, price, where, the parts a build ordered, the incident), `ShipCourse` (path, departed, arrival: what the client draws along), `ShipJob` (goods owner, good, quantity, pickup, source, destination, and the haul, purchase and delivery it serves; `IsFerry`: none, a ship flying empty for its owner), `Ship`: built from `Parts` (from parts, or a `Design` at a grade), `Stats` computed from them, `Design`, `BuiltBy`/`BuiltAt`/`BuiltTick`, owner and `OwnedSince`, location, `CargoCapacity` and `Preservation` from its stats, `Hold` (lots by owner), `FreeSpace`, `Job`, `Course`, `ListedFor` (the ship Sale it is up under), `Scrapped`, `IsFree` (docked, no job, not for sale, not scrapped), `LotOf`, `History`, `ContractsFlown`/`RecentlyFlown`, `FreeSince`, `Utilization(now, window)` (from its busy spans); `Reserve`, `ReachPickup`, `Load` (merges an owner's lot), `Unload`, `Alter`, `DepartLaden`, `DelayArrival`, `Arrive`, `FinishJob`; internal `Record`, `List`, `TransferOwnership`, `Scrap` S8: `ShipJob.Throttle` (the laden leg's, design §16.7). | `Market`, `Settlement`, `TransitEvents`, `DecisionEngine`, `PlayerActions`, `Invariants`, the views, `Throttles`, `Liquidation` |
| `Throttle.cs` | `Throttle` (None: no laden leg; Cruise; Push), design §8.4, §16.7 | `ShipJob`, `Throttles`, `DecisionEngine` (traders, quotes), `Market`, `PlayerActions`, `GameSession`, `DealEstimate`, `ContractView`, the client |
| `ShipPart.cs` | `PartSlot` (Frame, Drive, Module), `PartKind` (the eight of design §15.4), `Grade` (A, B, C), `MainStat`, `PartInfo` (cargo, mass, toughness, power, reliability, preservation, runs hot, base price, main stat), `FittedPart` (kind and grade; its `Good`, `Slot`, `Info`), `ShipPart` (the part table; `StatScale` 1, 0.96, 0.92; `ValueShare` 1, 0.85, 0.70; `Kinds`, `KindsFor(slot)`, `Goods`, `GoodOf(kind, grade)`, `IsPart`, `Of(good)`, `PartOf(good)`, `StepsShort`) | `Goods`, `ShipDesign`, `Terms`, `DecisionEngine` (factories, yards), `SimulationContext.BuildShip`, the views |
| `ShipDesign.cs` | `Design` (the five, design §6.2), `ShipStat`, `StatTerm` (a part's contribution), `ShipParts` (frame, drive, module or none; `All`, `In(slot)`, `With`, `Design`, `AtGrade`, `Of`), `ShipStats` (cargo, mass, power, speed, toughness, reliability, preservation, runs hot, running cost per hour, value; `IncidentChance` 1/reliability, `IncidentSeverity` 1/toughness; `RunningShare` 0.003% of the parts' base prices an hour, `AssemblyValue` ₵2,000; `From(parts)`, `Terms(parts, stat)`), `Designs` (`Parts(design, grade)`, `Stats(design)`, `Match`) S8: `ShipStats.RunsHot` is read by `Throttles` (pushing a drive that runs hot doubles the incident chance). | `Ship`, `PriceModel`, `IncidentResolver`, `DecisionEngine`, `Terms`, `WorldGenerator`, the views |
| `Personality.cs` | `Personality` (risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, concession rate) | `ActorState`, `DecisionEngine`, `PriceModel`, `WorldGenerator` |
| `Reputation.cs` | `ReputationContext` (Accord standing, region, good category, counterparty), `ReputationLedger` (scores −1..1, decay toward 0) | `Settlement` (writes), `Visibility` (reads region) |

### Engine: `Actors/`

| File | Contents | Used by |
|---|---|---|
| `ActorState.cs` | One model for every actor: credits, stock with a reserved part (`Stock`, `Reserved`, `Available`), ships (`AddShip`, `RemoveShip`), home, personality, `Producer`, `Factory` (with `Work`, `ShortSince`), `Yard` (with `Works`: `YardState`), `Salvage`, `Station` and consumer `Demands` profiles; `Products`, `Makes(good)`, `ProfileFor(good)` (a producer's good, or a factory's part priced as a producer's); `IncomePerHeartbeat`, `IsPlayer`, `MovesCargo`, `KeepsStock`; per-good `Consumed`; its `PriceMemory`s (internal); its `Knowledge` (`ActorKnowledge`, design §7.2) S8: `Debt` (its balance below ₵0), `Solvency` (`SolvencyStatus`), `WarnedSince`, `InsolventSince`, `DebtAtInsolvency`, `SolvencyEntry` (the latest warning's or the insolvency's journal entry), `InBusiness` (neither liquidating nor dissolved). | everywhere |
| `ActorRole.cs` | `ActorRole`: Producer, Consumer, ShippingCompany, IndependentPilot, Government, StationOperator, Factory, Shipyard, Salvage in use; Pirate and the rest named, never created | `ActorState`, `WorldGenerator`, the views |
| `Profiles.cs` | `ProducerProfile` (good, output, unit cost, cap, margin; `Fullness`), `StationProfile` (storage rate, float), `ConsumerDemand` (good, rate, target stock); `RecipeInput`, `PartRecipe` (kind, inputs, hours, stock cap), `FactoryProfile` (quality, recipes, margin; `RecipeFor`, `Makes`, `Products`, `Inputs`, `InputTarget`, `UnitCost`, `Labour`, `ProfileFor`; `DeliveredInputMarkup`), `FactoryWork`; `YardProfile` (assembly hours, labour, margin, habit package, lot limit, spec designs, queue limit), `SalvageProfile` (buy, ask and floor shares, lot limit), `YardJob` (the Build or spec, parts wanted, assembly, fitting, orders by slot, failures), `YardState` (current job, queue of jobs, next spec design, `SpecSells`; `Has`, `Jobs`) | `ActorState`, `PriceModel`, `DecisionEngine`, `WorldGenerator`, `Market.TakeBuild`, `Invariants`, the views |
| `PriceMemory.cs` | `PriceKind` (Wanted, Lot, Haul), `PriceKey`, `PriceMemory`: a per-actor price factor that learns from each of its closed posts (haggled: toward the price agreed; taken within a day: lower; expired: higher, or the other way for a seller), clamped | `DecisionEngine` (consumers' wanted Sales, producers' lots, everyone's hauls) |
| `DecisionEngine.cs` | Partial class, the shared part: `Run` (by profile: station, producer, factory, yard, salvage, consumer, trader; then `Advertising.AdvertiseUnsold`; then `PayOut` above `WorkingCapital`, except the government), `WorkingCapital` (a trader's counts `GrowthFund`; factories `FactoryReserve`; yards and salvage `YardReserve`), `ChargeRunningCosts` and `RunningCost` (a quarter while laid up for sale, `LaidUpShare`), `AnswerAtOnce` (an AI's wanted Sale, or anyone's order at a producer's door, by any resident that `Makes` the good), `HaulFeeEstimate` (a standard Light Freighter's, `Designs.Stats`), `UsualHaulMarkup`/`UsualHaulOffer` (the player's haul offer), `RaisedHaulOffer`, `HaulDeadline`, `Ask(producer)` and `Ask(producer, good)` (a producer's unit ask; a factory's part through `PriceModel.PartAsk`); the terms' decisions by utility: `ReviewDefault`/`WouldCancel` (a payer whose contract is in default; a Build's client waits while its yard has the job), `WouldReject` (a buyer handed goods below the floor), `WouldWalkAway` (a seller that can no longer deliver); and private `HaulTerms`, `PostPricedHaul`, `ArrangeTransport` (post a Haul for a signed Sale, send one's own free ship if it can carry the goods, or walk away), `RaiseHaulOffers` (repost an untaken haul higher), `Known<T>` (others' contracts it believes open: `Knowledge.OpenContracts`) S8: `ChargeRunningCosts` charges below ₵0 (`Charge`; an overload takes the hours, for tests); `PaysOut` (the measurement knobs `TradersPayOut`, `StationsPayOut`); `AnswerAtOnce` asks only actors `InBusiness`. | `ActorHeartbeat`; `Market.PostSale` (`AnswerAtOnce`); `GraceEnded`, `DefaultReview` (`ReviewDefault`); `Settlement` (`WouldReject`); `PlayerActions` (`FillSources`, the offers, `HaulDeadline`); `Knowledge` and `PriceBulletin` (`Ask`); `HeaderView` (`RunningCost`) |
| `DecisionEngine.Producers.cs` | `RunProducer`: `Produce` (paying cost), `TryAnswer` (wanted Sales at its door for a good it `Makes`: as posted at or above its ask, or an AI's in closed form; an order at its door at its ask, up to the order's price), `SellDelivered` (per profile: a remote wanted Sale it can profit on, as posted or in closed form, and whose perishables a standard hold gets there before the buyer could reject them: `Market.FillViaHaul` from its stock, which posts the Haul), `ArrangeTransport`, `PostLot` (when 75% full, priced by its `PriceMemory`, never below cost) S8: `RunProducer` ends with `SellOff` (goods set down far from home when a sale fell through: their rent now charges into debt). | `DecisionEngine.Run`, `AnswerAtOnce`; `TryAnswer` and `SellDelivered` also `RunFactory` |
| `DecisionEngine.Factories.cs` | `RunFactory` (design §5.3, §15.4): `FinishWork`, `StartWork` (`NextPart`: wanted first, else the least stocked; inputs consumed, labour sunk; short of inputs it waits, or after `MakeDoAfterHours` makes it without, a grade lower for each), `BuyInputs` (`PostWanted` at `InputWorth`), then sells like a producer (`TryAnswer`, `SellDelivered` per part, `ArrangeTransport`); `PartsInStock` S8: `SellOff` too. | `DecisionEngine.Run`, tests |
| `DecisionEngine.Yards.cs` | `RunYard` (design §6.3, §15.5): drops jobs whose Build ended (finished on spec), puts a signed Build before a spec job, `SpecJob` (only with lot room, money to spare and `SpecSells` not false), `Progress` per job in line (`InStock` with the parts the jobs ahead took set aside, `Order` → `Market.PostPartsOrder` with `ChooseGrade` and a deadline from `PartSource`'s lead, `ProposeSupplyFailure` when a failed order can't be replaced in time; the current job assembles), `Finish` (`BuildShip`, then `HandOverBuild` or the lot), `BidOnBuilds` (the AI's Builds it sees: as posted or `ClosedForm`), `SellLot` (stock ships, marked down; `AwaitedByBuild` kept back), `NoteLotSale`; `PartSource` (the cheapest maker it knows, with delivery and lead), `BuildCost` (cost, reservation, ready), `QueueHours`, `Load`; `BuilderQuote` (its bid on the player's Build); `ChooseRemedy` (an AI client's utility), `WouldRejectShip` S8: a yard in debt asks `InDebtLotShare` (45%, what Edge Salvage pays) for its lot. | `DecisionEngine.Run`, `QuoteFor`, `Settlement` (`WouldRejectShip`, `NoteLotSale`), tests |
| `DecisionEngine.Ships.cs` | Fleets (design §4.2, §6.3, §6.5): `FleetUtilization`, `WantsShip` (busy, under the cap, nothing on order), `WantedDesign` (its busiest ship's), `ManageFleet` (from `RunTrader`: `Acquire` when it wants a ship; else sell its idlest idle ship when the fleet is idle, or its one dear ship that doesn't pay, `Uneconomic`), `Acquire` (buy what it sees worth the price, else `PostBuild`), `GrowthFund`; `RunSalvage` (ferry home, list at `AskShare` marked down to `FloorShare`, break up a ship a year unsold, buy at `BuyShare` with `SalvageCushion` in hand), `AfterBuyingShip`, `ShipBuyerQuote` (a buyer answering the player's ship for sale); constants `FleetWindowHours`, `GrowAbove`, `PilotGrowAbove`, `ShrinkBelow`, `CompanyFleetCap`, `PilotFleetCap`, `ScrapAfterHours`, `LaidUpShare` S8: `WantsShip` is false for a trader warned for its debts; a trader in debt sells its idlest free ship however busy the fleet (`ManageFleet`). | `RunTrader`, `DecisionEngine.Run`, `WorkingCapital`, `QuoteFor`, `TakeOn`, tests |
| `DecisionEngine.Consumers.cs` | `RunConsumer`: income (`ApplyFaucet`), `Consume`, `BringHome` (a Haul home for goods in storage elsewhere), then per demand below 60% of target, counting `Inbound`: `TryBuyLot` (a cheap lot plus a haul home, as posted or in closed form) or `PostWanted` (delivered, priced from the cheapest source it knows, `Knowledge.ReadingOf`, and its `PriceMemory`, up to a worth a unit the caller gives: a consumer's `Willingness`, a factory's `InputWorth`; on terms `Terms.Meetable` relaxes); `Willingness` (affordability-aware) | `DecisionEngine.Run`; `PostWanted` and `Inbound` also `BuyInputs` |
| `DecisionEngine.Traders.cs` | `RunTrader`: per free ship `BestOption` (twice, if the first was found taken) over the Hauls it believes open (`ScoreHaul` → `HaulTake` on an offer: inside the grace, not a carriage the shipper would refuse, the warranty's refund) and wanted Sales by source (`FillSources`: its own goods, or producers (a factory for a part) at the asks it knows, `Knowledge.KnownAsk`; an overload takes a `Sight`, `ScoreFill` → `FillTake`: inside the grace, goods the buyer wouldn't reject) by `Utility` less `TermsRisk` (what the terms stand to cost if an incident strikes) over a `Threshold`, priced by `Price` (as posted or closed form), `Commit` (through `Market.SignAgreed`); `Stow` (goods left aboard into storage); `Broker` (no free ship: `FillViaHaul`, also priced by `Price`); `SellOff` (stored goods as lots, written off after 30 days); `ManageFleet` last; `RunStation` (rent) S8: `HaulTake` and `FillTake` score cruise and push and keep cruise unless it would be late and pushing is worth more (`Better`); `TraderOption.Throttle`; `TermsRisk` takes the throttle and the danger's shifted table; a take needs the goods in cash and the bond within `Solvency.CanBond`; `Commit` passes the throttle; `SellOff` serves producers and factories too. | `DecisionEngine.Run`; `FillSources` also `PlayerActions` |
| `DecisionEngine.Haggling.cs` | `Take` (a take's utility, floor, arrival), `TermsOn`, `Price` (as posted, else `ClosedForm` with an AI poster), `ClosedForm` (`Haggling.Resolve`), `PosterReservation` (where an AI poster stands: ripening over two days, at once for a haul serving a signed Sale, within `Uncommitted` credits), `Uncommitted`, `LeastPrice`/`LeastPriceFrom` (a reservation by false position), `NoteHaggled`; with the player: `NoticePost` (schedules `PostReached` once the post is shown where an actor is, `Sight.ReachHours`; `fromNews` for the actors that couldn't see a post the news reported; `newlyShown` for the actors present only where the player just paid to show it, `NewlyReached`), `Answers` (who answers what: yards a Build, salvage and traders a ship for sale), `Respond`, `ExpectReply` (schedules `ReplyDue`), `Reply`, `Decide` (the haggling rule), `TakeOn` (an AI responder signs an agreed offer by its plan, `PlanKind.Build` and `BuyShip` among them); internal `Quote`/`Plan` and `QuoteFor`: `PosterQuote` (`TermsValue`, `DeadlineLimit`), `CarrierQuote`, `FillerQuote`, `ProducerQuote` (any good it `Makes`), `BuyerQuote`, `BuilderQuote`, `ShipBuyerQuote`, `Fit` (the nearest terms it can meet). Who pays follows `Market.PosterSells` S8: `Take.Throttle`, `Plan.Throttle` (the carrier's and filler's quotes carry it to `TakeOn`); `Price` returns the throttle; `Answers`, `Respond` and `Reply` skip an actor not `InBusiness`. | `Market` (`NoticePost`, `ExpectReply`, `TakeOn`), `Advertising.Show` (`NoticePost`), `PostReached`, `ReplyDue`, the other partials, tests |
| `Haggling.cs` | `NegotiationParty`, `NegotiationOutcome`, `Haggling`: the rules (design §3.6, §16.5): `Resolve` (an AI-to-AI haggle in closed form, the rounds played out), `Opening`, `Concede`, `Crosses`, `TooWide` (`WalkAwayGap` 35%), `TellFor`, `ReplyHours` (1–12 h by eagerness and closeness, with a deterministic hour of spread) | `DecisionEngine`, `NegotiationView`, `NegotiationTests` |
| `PlayerActions.cs` | `DealBlock` (codes, worded by the templates; `BadOffer`, `NotYourTurn`, `RoundsRanOut`), `PostKind` (Wanted, Lot, Haul, Build, Ship), `PostBlock` (with `NoShip`), `PostDraft` (with `ShowAt`, the further systems to pay for; `Spec` for a Build, `Ship` for a ship's sale; `Places`, `ShowFee`), `AdBlock` (NotYours, NothingChosen, CannotAfford), `HaulFeasibility`, `FillFeasibility`, `BuyFeasibility`, `BrokerFeasibility`, `HaulAgainFeasibility`; `LongestPost`, `LongestBuild`; `Believed` (whether the player believes a contract open: live where it sees it, else as the news left it; the checks use it), `ShipFor`, `Check`, `CheckHaul`/`TakeHaul`, `CheckFill` (the cheapest source)/`Fill`, `CheckBuy`/`Buy`, `CheckBuyShip`/`BuyShip`, `CheckBroker`/`Broker`, `CheckHaulAgain`/`HaulAgain`, `CheckPost`/`Post` (a wanted Sale into its storage at the place, a lot or a haul of its stored goods, a Build it can fund when a yard signs, one of its ships for sale; the fee to show it further counts against what the player has, and is paid as it goes up), `CheckAdvertise`/`Advertise` (show an open post in further systems, `Advertising.Show`), `CheckCounter`/`Counter`, `AcceptOffer`, `Decline`, `Withdraw`, `TakeOn` (the player signs an agreed offer by its own command; a ship among them), `CancelAtWill`, `CancelForDefault`, `Decide` (a Build's inspection through `HandOverBuild`, a supply failure's remedy through `Market.ChooseRemedy`), `Store`, `Retrieve`: the player's commands through `Market` and `Settlement` S8: `DealBlock.CannotBond`; `BondFor`; `CheckHaul` and `CheckFill` ask `Solvency.CanBond` (the goods in cash, the bond within credit); `TakeHaul` and `Fill` take a `Throttle`. | `GameSession`, `ContractView`, `ComposerView`, `BoardView`, `DealEstimate`, `Market.AcceptOffer` (`TakeOn`), the bots |

### Engine: `Economy/`

| File | Contents | Used by |
|---|---|---|
| `PriceModel.cs` | `ProducerAsk` (cost × margin × fullness; with credits, a rich producer gives up its margin), `PartAsk` (a factory's part: its cost with its margin, 10% over with none in stock, 10% under with its stock full), `ConsumerWillingness` (base value × shortage; with credits and income, × affordability), `PilotMinimumFee` (from a ship's `ShipStats`), `TravelTicks` (distance ÷ speed, rounded up) | `DecisionEngine`, `TransitEvents`, `Market`, `PlayerActions`, `DealEstimate` |
| `Visibility.cs` | `Tier` (Here, Near, Far: for prices), `Sight` (one actor's presence and the tier of every location from it: `TierOf` a location, `CanSee` a contract (its own or its counterparty's; or shown, by now, where the actor is present, `Contract.ShownFrom`), `ReachHours` (until it is shown where the actor is present; null if nowhere)), `Visibility`: `Of(context, actor)`, `Presence` (home, ships where they are or are heading, storage), `CanSee` (live, or known from the news) | `DecisionEngine`, `Knowledge`, `Market` (telemetry), `PlayerActions`, `Advertising`, `GameSession` (`Sight`), `BoardView`, `MapView`, `NewsView`, `ContractView`, `LogView`, the bots |
| `Advertising.cs` | Showing a post further (design §7.1, D38): `JumpsFrom` (the fewest jumps from a contract's places), `DelayHours`, `FeeAt` (a contract's, or a draft's places and price), `Options` (where it isn't shown yet, nearest first, with jumps and fee), internal `ShowOnPosting` (its places, and any free reach), `Quote`, `Show` (all or nothing: fees to each system's station operator, `Contract.NoteAdFee`, shown after the delay, the player's post noticed again), `AdvertiseUnsold` (the AI rule: a ring a day while all it has paid to show the post stays within `Budget`), `Budget` (`AdBudgetShare` of the price for goods and hauls, `AdAssetBudgetShare` for a Build or a ship for sale: S7), internal `SeenOnlyThroughAd` S8: `ShowOnPosting` shows what an actor in debt sells (goods for sale, a ship) in every system, free. | `SimulationContext.Add`, `DecisionEngine.Run`, `PlayerActions`, `Market.Sign`, `GameSession`, `AdView`, `ComposerView`, tests |
| `Solvency.cs` | `SolvencyStatus` (None, Warned, Liquidating, Dissolved); `Solvency` (design §5.4, §16.8): `WarnedHoursLimit` (seven days), `Assets` (plant, ships at value, goods at base value wherever they are, its money in escrow), `Plant` (a producer's, factory's, yard's, salvage yard's or station's working capital: a business that touches ₵0 with an empty warehouse isn't insolvent at once), `CanBond` (a bond lent by the clearing house while the debt stays within half of what the actor owns; cash spent first must be there), `ChargeInterest` (0.2% a day of debt, `DebtInterestPerDay`; none while liquidating), `Review` (warn, lift, or `Liquidation.Begin`) | `ActorHeartbeat`, `Market`, `DecisionEngine` (traders), `PlayerActions`, `HeaderView`, `LedgerView`, tests |
| `Knowledge.cs` | `ReadingSource` (Live, Seen, Daily, News, Start), `PriceReading` (a producer's ask for a good and when, its stock and when; `CurrentStock`: only while as new as the ask), `ActorKnowledge` (per actor, keyed by producer and good: `Stored(producer, good)`, `History(producer, good)` (a reading a day, 14 days), `LastHere`, `FoundClosed`, `RefusedAt`; internal notes), `PriceBulletin` (the morning's asks, `AskOf(producer, good)`), `Knowledge`: `Seed`, `Look`, `LookAt`, `ReadBulletin`, `ReadLot` (internal, by events; every good a producer `Products`); `ReadingOf` (live here, the bulletin near, remembered far; an overload for a producer's own good), `KnownAsk` (for a good) (what a door order is counted on at, unless short or refused in the last day), `OpenContracts` (what an actor believes open), `BelievesOpen`, `FromNews`; internal `NoteClosed`, `NoteRefused` | `ActorHeartbeat`, `ShipDeparted`, `ShipArrived`, `PricesPosted`, `NewsDesk`, `WorldGenerator`, `Market`, `DecisionEngine`, `PlayerActions`, the views, the bots |
| `News.cs` | `NewsKind` (Posted, Signed, Delivered, Short, Failed, Cancelled, Defaulted, Expired), `NewsItem` (a contract, where it stood at press time, the facts its sentence needs), `NewsIssue`, `NewsDesk` (the issues kept, `Latest`, `ReportedOpen`, `WasReportedOpen`, `ReportedOpenAt`, `AverageUnit` (the week's signed Sales), telemetry counts; internal `Publish`; `Weight`: size, novelty, outcome, frontier) | `NewsPublished`, `Knowledge`, `Market` (telemetry), `BoardView`, `ContractView`, `MapView`, `NewsView`, `NewsText`, `Soak` |
| `TrackRecord.cs` | `TrackRecord` (on time, late, short, failed, cancelled for default, walked away; paid, defaulted, walked away as payer), `Records` (`PublicRecordOf`: Accord contracts only; `DealingsWith`: each actor's own experience, frontier too; internal notes from `Settlement`) S8: `Records.WholeRecordOf` (an actor's every contract, as every counterparty remembers it: the legacy's record). | `Settlement`, `ContractView`, `InfoText`, `LegacyView` |
| `InflationMonitor.cs` | `Evaluate`: drift of total credits beyond ±8% moves `FaucetMultiplier` by 7% | `InflationCheck` |

### Engine: `Events/`

| File | Contents | Used by |
|---|---|---|
| `ISimEvent.cs` | `ISimEvent` | events, `EventQueue`, `SimulationContext` |
| `EventQueue.cs` | Min-heap by tick, ties by insertion sequence | `SimulationContext` |
| `ControlEvents.cs` | `ActorHeartbeat`, `ContractExpiryCheck`, `GraceEnded`, `DefaultReview`, `InflationCheck`, `ReputationDecay` S8: `ActorHeartbeat` charges interest, reviews solvency and runs a liquidation; a dissolved actor stops beating. | scheduled by `WorldGenerator`, `Market` (expiry at posting; the grace's end at signing what someone waits on), `DecisionEngine.ReviewDefault` (reviews), and themselves, `Solvency`, `Liquidation` |
| `TransitEvents.cs` | `ShipDeparted`, `TransitIncidentCheck`, `TransitIncidentOccurred`, `ShipArrived` S8: the laden leg hours and the incident odds at the throttle of the job (`Throttles`); `ShipArrived.Dock` (the docking fee to the station operator, owed below ₵0). | `ShipDeparted` scheduled by `Market.Dispatch`; the rest by each other |
| `NegotiationEvents.cs` | `PostReached`, `ReplyDue` | scheduled by `DecisionEngine.NoticePost`, `ExpectReply` |
| `InformationEvents.cs` | `PricesPosted`, `NewsPublished` | scheduled by `WorldGenerator`, and themselves |

### Engine: `Random/`, `Simulation/` and `Properties/`

| File | Contents | Used by |
|---|---|---|
| `Random/DeterministicRandom.cs` | xorshift128+ seeded by SplitMix64; `NextDecimal`, `NextLong`, `Chance`, `Pick`; counts `DrawCount` | `SimulationContext.Rng` → `WorldGenerator`, `TransitIncidentCheck`, `IncidentResolver` |
| `Simulation/SimulationContext.cs` | The world (section 4) | everything |
| `Simulation/Market.cs` | `Signing` (Signed, NotOpen, PayerCannotFund, SupplierDeclined, CannotPerform), `FillSourceKind`, `FillSource`; `Market`: `PostSale` (a package and late-fee scale, a lot in storage warranted as seen; an AI's wanted Sale or a door order is answered at once, the player's reaches others over game time), `HagglesInGameTime`, `PostHaul` (a package, the goods' declared value, a cause), `Withdraw` (a cause), `Expire` (internal), `CancelForDefault`, `CancelAtWill`, `CanFund`, `CanFundHaul`, `Supply`, `BuyLot`, `TakeHaul`, `DoorTolerance` (10%), `DoorLimit`, `BuyAtSource` (an order at the door: posted at the limit, signed at the producer's ask; telemetry), `PostPartsOrder` (a yard's wanted Sale of a part for a Build or on spec, `ForBuild`, caused by the Build's latest entry), `FillWithShip`, `FillViaHaul`, `UnderWay`, `ExpectedArrival` (both count a ship carrying a haul's goods for the Sale it serves, even once the haul is cancelled), `NextFree` (where and when a ship is next free), `TravelTo`, `Dispatch` (internal); internal `AwaitPerformance` (schedules `GraceEnded`), `Sign` (a Build's deposit to the yard first; locks escrow (`EscrowedPart`) and bond; a closed contract is noted in the taker's knowledge, `NotOpen`; counts a taker that saw it only through an ad, `SignedViaAd`); private `Declined` (a door order turned down: noted), `Reserve`, `ReleaseOpen` (a ship's sale unlists the ship) S8: `Signing.CannotBond`, `Signing.Insolvent`; `Sign` refuses a taker not `InBusiness` and locks the whole bond (`LockBondToEscrow`) once `Solvency.CanBond` allows it; `CanBond` (a fill's goods in cash, then the bond); `PostSale` takes a cause; `TakeHaul` and `FillWithShip` take a `Throttle`; `NextFree` counts the laden leg at the job's throttle; `HagglesInGameTime` is false for an insolvent player (the clearing house sells for it). | `DecisionEngine`, `PlayerActions`, `ControlEvents`, the views (`NextFree`, `ExpectedArrival`) |
| `Simulation/Market.Ships.cs` | `Market` (partial), ships (design §3.2, §6.3): `PosterSells` (a lot or a ship for sale: the poster is paid), `PostBuild`, `TakeBuild` (the yard's jurisdiction, `Sign`, a `YardJob` queued, a demand signal for spec ships), `ChooseRemedy` (Extend, Substitute, Cancel; `RemedyChosenEntry`), `PostShipSale`, `BuyShip` (signs, then `Settlement.HandOverShip`), `Ferry` (an empty trip home under no contract) S8: `PostShipSale` takes a cause. | `DecisionEngine` (yards, fleets, salvage, `TakeOn`), `PlayerActions`, tests, `Liquidation` |
| `Simulation/Market.Haggling.cs` | `Market` (partial): `Counter` (opens or continues a haggle; on a contract found closed, noted; an AI on the other side is told to reply), `AcceptOffer` (the responder takes the contract on the offer, or the haggle ends), `EndNegotiation`, internal `SignAgreed` (amend the deadline and terms to the agreed offer, sign, restore if it didn't) | `PlayerActions`, `DecisionEngine` |
| `Simulation/Settlement.cs` | `SettlementTerms` (gross, refund, late fee, liability, tax; `Payment`, `Penalties`, `Net`) and `Due` (pro rata; the late fee then the liability, limited by `Collectable`); `HandOver` (Done, Defaulted, Refused, Pending); `CanPay`; `HandOverReserved` and `HandOverCargo` (inspection: a floor, the AI's `WouldReject` or the player's decision; a short delivery under a strict term fails), `DeliverHaul` (the warranty on decay in the hold; rejection and strict shortness fail it; liability for goods lost); `Fail` (bond to the payer, escrow back, deep-Accord liability, set-aside goods freed, a lost haul fails the Sale it serves); `Cancel` (for default or at will, the break fee); `PayerDefault`; `Keep`; `Unreserve`; `Late`; `Load`, `Unload` (a ship's job at its pickup and destination; a cancelled haul's goods still set down); `HandOverBuild` (the substitution policy's discount, or the client's inspection outside it; escrow paid out, the ship transferred), `HandOverShip` (a ship's sale at signing; a frontier buyer that can't pay defaults), `CancelBuild` (private: for default, at will by either side, or on a supply failure); `Fail` gives a Build's deposit back; reputation rewards and blame (`ValueScale`: a Build or ship sale like a Sale); every ending noted in `Records` S8: `Collectable` in the deep Accord is everything the terms say (enforced into debt, `TransferOwed`); `Collect` (a remedy beyond escrow: enforced in the deep Accord, as far as the payer has elsewhere); `SettleAs` (an insolvency settles as for default against an insolvent performer, at will for an insolvent payer); `HandOverShip` counts a fire sale. | `Market`, `TransitEvents`, `ControlEvents`, `PlayerActions`, `DecisionEngine` (`Due`), `DealEstimate` (`Due`, `Collectable`), `DecisionView` (`Due`) |
| `Simulation/Terms.cs` | The three packages for Sales and Hauls (a table in its doc comment: grace, late fee and cap, break fee, short delivery, warranty and floor, liability), every figure from the price; `ForSale` (a lot in storage warranted as seen; a late-fee scale), `ForHaul` (liability at the declared unit value; a late-fee scale), `ForBuild` (a Build's packages, a table in its doc comment: grace in days, the late fee a day, the cap; the deposit is the break fee), `ForShipSale` (nothing late, nothing bonded), `For(contract, offer)` (its kind's terms rebuilt from an offer), `Substitution` (a Build's discount for parts below grade, or null outside its policy), `SubstituteDiscount`, `Allows`, `StandardSubstitutionDiscount` (20% of the part), `LenientDiscountPerStep` (15%), `PackageFor` (by personality; the player's default standard), `Meetable` (what a buyer of perishables relaxes to), `TaxRate` | `Market`, `DecisionEngine`, `PlayerActions`, `ComposerView` |
| `Simulation/Decisions.cs` | `DecisionKind` (CancelForDefault, Inspection (goods, or a ship built outside its Build's policy), SupplyFailure), `DecisionChoice` (Cancel, Wait, Accept, Reject, Extend, Substitute, TakeRefund), `PlayerDecision` (kind, contract, raised, the entry that raised it, goods inspected; `Choices`); `FailureKind` (the design §16.3 paths the soak counts; S7 adds SupplyFailure, Substituted, CancelledOnSupplyFailure) S8 adds `FailureKind.CancelledOnInsolvency` and `Insolvency`. | `SimulationContext`, `GraceEnded`, `Settlement`, `PlayerActions`, `GameSession`, `DecisionView`, `Soak` |
| `Simulation/SimulationConfig.cs` | Tuning knobs: heartbeat 24, inflation check 400 / ±8% / 7%, reputation decay, tax 4%, docking ₵3, `NegotiationMaxRounds` 6, `PostsDrawAnswers` (tests turn it off), incident scale, reputation gain and loss, `NewsItemsPerDay` 3, `NewsFrontierWeight` 3, advertising (design §7.1, D38: `AdFreeJumps` 0, `AdFeeShare` 0.05%, `AdFeeFloor` ₵0.20, `AdFeeGrowth` 4, `AdDelayHours` 0/4/24/48, `AdWaitHours` 24, `AdBudgetShare` 5% and `AdAssetBudgetShare` 1%, each a total per post since S7), working capital (consumer 90 days' income, producer 4 warehouses at cost, trader 1.5 × fleet value, `FactoryReserve` ₵30,000, `YardReserve` ₵60,000) S8: `DebtInterestPerDay` 0.2%, `PushSpeedFactor` 1.25, `PushIncidentFactor` 1.5, `PushHotIncidentFactor` 2, `IncidentDangerShift` 2, and the measurement knobs `TradersPayOut`, `StationsPayOut` and `BondsOnCredit`. | `SimulationContext.Config` |
| `Simulation/WorldGenerator.cs` | The sector: locations (with map points), routes (hours and danger), producers, consumers, part factories (`AddFactory`: design §15.4's recipes at grade A, `FactoryMargin` 25%), shipyards (`AddYard`: Tycho Yards cheap, quick and lenient; Helios Orbital Yards dear, slower and strict), Edge Salvage with a worn Refrigerated Transport, government, traders (ships from `Design`s), optional player, station operators (₵0.10 a unit-day, ₵2,000 float); the knowledge everyone starts with and the daily bulletin and news; `WorldHandles` | `Soak`, `Fingerprint`, `GameSession`, `BenchPanel`, tests |
| `Simulation/IncidentResolver.cs` | `TransitIncidentKind`, `TransitIncident`, `Resolve` (roll → delay, damage, partial loss, credit cost, total loss; severity from the ship's `ShipStats.IncidentSeverity`), `Share(kind)` (the table's odds per kind) S8: the danger shifts the table (`Bound`: each bound b moves down by k·b·(1−b), k = `IncidentDangerShift` × danger, at most 1); `Share(kind, danger, shift)`; `Resolve` takes the shift. | `TransitIncidentCheck`, `TransitIncidentTests`; `Share` by `ContractView` |
| `Simulation/Liquidation.cs` | `Liquidation` (design §5.4, §16.3): `Begin` (`InsolventEntry`, the player told; haggles closed; open posts withdrawn; every contract it signed cancelled, `CancelReason.Insolvency`), `Run` (each free ship listed in a fire sale, its owner's goods aboard set down; goods as fire-sale lots; `ShowEverywhere`; marked down 10% a week; after `LongestHours` unsold ships broken up and goods written off; `Dissolve` when nothing is left: `WriteOffDebt`, `ActorDissolvedEntry`); constants `ShipAskShare` 60%, `ShipFloorShare` 30%, `GoodsAskShare` 60%, `GoodsFloorShare` 20%, `MarkdownShare` 10%, `PostHours` 4 days | `ActorHeartbeat`, `Solvency.Review`, tests |
| `Simulation/Throttles.cs` | `Throttles` (design §16.7): `SpeedFactor`, `IncidentFactor` (×1.5, ×2 for a drive that runs hot), `LadenTicks`, `IncidentOdds` | `TransitEvents`, `Market.NextFree`, `DecisionEngine` (traders), `DealEstimate`, tests |
| `Simulation/Sinks.cs` | `SinkKind` (Production, Labour, RunningCost, Docking, Incident, Interest, PayOut, AdFee, Tax): what each destroyed credit went on | `SimulationContext.ApplySink`, `Charge`, the callers in section 5, `Soak` |
| `Simulation/Invariants.cs` | `Check`: credit conservation, escrow reconciliation, no negative escrow and nothing left in a settled contract's, goods conservation per good (produced = consumed + lost + held + built in) with no negative or over-reserved stock or storage and no overfull hold, every part built in found in a ship (`PartsInShips`), every ship in its owner's fleet and no other (`ShipOwner`), and every signed contract being performed (a haul has a ship flying it; a Sale has its goods set aside or under way; a Build is its yard's or built; no ship's sale stays signed); `InvariantViolation` S8: credit conservation counts `DebtWrittenOff`; a dissolved actor holds nothing (`CheckDissolved`). | `Soak`, tests (`TestWorld.AssertSound`) |
| `Simulation/EventJournal.cs` | `JournalEntry` (`Seq`, `Cause`) and its twenty-three kinds (section 3), `IContractEntry`, `INegotiationEntry`, `FailureReason`, `EventJournal` (a ring of 5,000: `Record` stamps and returns, `Find`, `Entries`, `Tail`, `TotalRecorded`) S8: `IActorEntry`; `DebtWarningEntry`, `DebtWarningLiftedEntry`, `InsolventEntry` (`InsolvencyReason`), `ActorDissolvedEntry`. | `SimulationContext.Record`; `LogView`, `JournalText`, `ContractView.Why` |
| `Simulation/StateFingerprint.cs` | `Of(context)`: tick, draws, money supply, settlement counters (fulfilled, breached, defaulted, cancelled, goods sold) and every actor's credits, as one string | `TestWorld.Signature`, the CLI's `fingerprint`, the client's `BenchPanel` |
| `Simulation/GameTime.cs` | One tick = one game hour. `Day`, `HourOfDay`, `At`, `Days`, `FormatDate` ("Day 4 · 14:00"), `FormatDuration` ("1d 6h"), `FormatRelative` ("in 1d 6h") | `Soak`, tests, `Format` (every date the client shows) |
| `Simulation/RunStop.cs` | Why `RunTo` returned: `ReachedTarget`, `Drained`, `PlayerAlert`, `PlayerDecision` | `SimulationContext.RunTo`, `GameSession.Advance` |
| `Properties/AssemblyInfo.cs` | `InternalsVisibleTo("ByGalacticAccord.Tests")` | the tests (internal `Sign`, `Dispatch`, stock reservations) |

### Soak harness: `src/ByGalacticAccord.Cli/`

| File | Contents |
|---|---|
| `Program.cs` | Commands `soak [ticks] [--seed N]` (default 200,000 ticks, seed 20260619), `bots [ticks] [--seed N]` (default 200,000), `fingerprint [ticks] [--seed N]` (default 20,000) and `help` | S8: `soak … --keep traders|stations|both` (those actors keep their surplus: the payout measurement) and `--cash-bonds` (no bond lent).
| `Soak.cs` | Runs in 5,000-tick chunks, `Invariants.Check` after each (exit code 1 on a violation), then prints telemetry: money, settlement counters, every failure path's count, signed contracts and their failure rate by term package, signed contracts by kind and how many were haggled in closed form (orders at the door left out), information (door orders and refusals, the news, contracts signed from it, ad fees paid and systems shown by jumps, contracts signed only through an ad), ships (parts made by grade, Builds by status, ships built to order and on spec, supply failures and remedies, ships sold, ships by holder, fleets), the goods table (parts by kind, with what is built in), demand met per consumer, contracts by kind and status, trader solvency, wealth | S8: sinks by kind, debt written off, what station operators hold; incidents by kind and laden trips by throttle; a debt and insolvency block (warnings, the deepest debt, who went insolvent and when, fire-sale ships sold); each trader's solvency status.
| `Bots.cs` | The same world played once by each bot (`BotRun`), invariants checked; prints each one's credits, earnings a day, deals, posts, counters, haggles and decisions (exit code 1 on a violation) | S8: says when a bot went under.
| `Fingerprint.cs` | A headless run's `StateFingerprint`, which the browser check compares with the client's |

### Presentation: `src/ByGalacticAccord.Presentation/`

Everything the interface says or draws, as plain records built from the session; the client only renders
them. A view never changes the world; commands go through `GameSession`.

| File | Contents | Used by |
|---|---|---|
| `Text/Templates.resx` | Every interface sentence and label (design §11.5): about 930 templates with named `{fields}`, keyed by journal entry type and contract kind (`ContractSigned.Sale.Wanted`, `ContractCancelled.AtWill.Payer.Haul`, each with `.You` and `.ToYou` twins where the player can be either party) and fact type (`Good.Food`, `Package.Strict`, `Term.Default.Them.Frontier`, `Decision.Cancel.Note`, `Ledger.Storage`, …) S8 adds the debt and insolvency entries, the cancellation on insolvency, the throttle (`Throttle.*`, `Action.Push`, `Board.Note.PushOnly`), debt in the header and ledger, `Block.CannotBond`, a fire sale on the board, and the legacy (`Legacy.*`). | `Templates` |
| `Text/Templates.cs` | `Get`, `Fill(key, (name, value)…)` (throws on a missing key or field), words for the engine's enums (`Good` (a part with its grade), `Jurisdiction`, `JurisdictionShort`, `JurisdictionLong`, `Danger`, `LocationKind`, `Role`, `ContractKind`, `Side`, `Package`, `Design`, `ShipName` (a design's name, or a custom ship by its part kinds), `Part` (a kind, or a part with its grade), `Grade`, `Slot`, `Tell`, `LateFee`), `KindLine` ("Sale · wanted"), `Offer` ("₵460, due Day 4 · 06:00, standard") | every view, the client's components |
| `Text/Format.cs` | `Money` ("₵1,234", "−₵42"), `MoneyCents`, `Quantity`, `Percent`, `Points`, `Hours`, and `GameTime`'s dates, all invariant | every view |
| `Text/JournalText.cs` | `Describe(context, entry)` (the entry's sentence; a haggle's reply with its reply time and tell; a ship built, handed over (late, at a discount), a supply failure, a remedy, a ship broken up), `Subject`, `Addressee` (the side an offer is addressed to), `ContractOf` (any `IContractEntry`), `Kind(contract)` (`Build`, and `Ship` for a ship's sale), `Head(contract)`, `ShipLine` ("Light Freighter S12") S8: the debt warning, its lifting, insolvency (by reason) and dissolution (written off, or left over); a cancellation on insolvency by the insolvent side; `Subject` of an `IActorEntry`. | `LogView`, `HeaderView` (alerts), `ContractView` |
| `Text/InfoText.cs` | `Age` (Live, Today, Days, Old: information age as opacity, design §11.1), `InfoText`: `AgeOf`, `ReadingAge` ("daily bulletin, Day 4 · 06:00"), `PublicRecord`, `Dealings`, `Range` (of a reading history) | `BoardView`, `MapView`, `NewsView`, `ContractView` |
| `Text/NewsText.cs` | `Describe(context, item)`: a news item as a headline, keyed `News.<Kind>.<contract kind>` (a Build and a ship's sale among them), with the price against the week's average | `NewsView` |
| `Session/GameSession.cs` | `ClockSpeed`, `DealOutcome` (a `DealBlock`, or the `Signing`), `PostOutcome` (a `PostBlock`, or the post), `GameSession` (from a seed, or over a world already made): the world with the player, the clock (`SetSpeed`, which won't start while a decision waits; `Advance(realSeconds)` capped at `MaxFrameSeconds`, stopping on an alert or a decision; `Clock` with its fraction, `Revision`), `Alerts`, `Decisions`, `PlayerShip`, `Accept` (haul, fill, buy a lot or a ship, by kind), `Broker`, `HaulAgain`, `Post`, `Counter`, `AcceptOffer`, `Decline`, `Withdraw`, `Advertise` (show an open post further: the block and the fee), `CancelAtWill`, `CancelForDefault`, `Decide`, `Grant` (the client's `?credits=` check aid: income through the faucet), `Store`, `Retrieve`, `PlayerActiveContracts`, `PlayerOpenPosts`, `PlayerNegotiations`, `Sight` (the player's now), `KnownOpen()` (what it believes open) S8: `IsOver` (the player is insolvent: the clock won't run, `Advance` does nothing); `Grant` charges a negative amount; `Accept(id, throttle)`. | `Shell`, the views, the bots, tests |
| `Views/DealEstimate.cs` | `DealKind` (Haul, Fill, Buy, Broker), `DealEstimate`: what a contract is worth to the player's ship by the engine's rules (income; goods bought and their tax; running costs for the empty and laden legs; docking; the certain warranty refund (a haul's on decay in the hold) and late fee, limited as `Settlement.Collectable` limits it; the bond; incident odds; the source; through a carrier, the haul fee and its tax), `TakeHome`, `Cost`, `PerDay`, from where and when the ship is next free; `ForBroker` S8: an estimate at a `Throttle` (`For`, `ForHaul`, `ForFill` take one; the laden leg and odds from `Throttles`), `Pushed` (the same deal pushed), `OnTimeOnlyPushed` (design §13's trap); the bond is the whole bond. | `BoardView`, `ContractView`, `MapView` |
| `Views/BoardView.cs` | `BoardRow` (with its `ContractType` and term package; `Live`: seen where it is shown, not only from the news), `BoardViewModel`: the Sales, Hauls, ships for sale (their cost with the tax, and worth) and Builds (for the yards: blocked) the player believes open (`GameSession.KnownOpen`), newest first, each with how it knows of it (`Seen`: from the news, with that issue's `Age`; or "Advertised at …" when it is seen only where its poster paid to show it), with take-home (or a lot's cost) and one note (why it can't be taken, goods the buyer may reject, lateness, a refund, the goods bought) S8: a row late at cruise and on time only pushed says so ("on time only if you push", with the odds); a fire sale says whose. | `Shell` |
| `Views/ContractView.cs` | `MoneyLine`, `InfoLine`, `ContractCommand` (Broker, CancelAtWill, CancelForDefault, HaulAgain, Withdraw), `ContractAction`, `ContractViewModel`: the contract as a document per kind (breakdown, schedule, uncertain inputs, every term as a sentence from the player's side under "Terms · package", party, status (in default, partly fulfilled, cancelled and by whom, the player's own post), the accept label, `Actions`, `Record`: the party's public record and the player's dealings with it; `Known`: from the news; `Shown`: where an open contract is shown and from when; `Advertise`: for the player's own open post, the further systems it could pay for (`AdView.ForPost`); a producer's ask the player doesn't see live and "still open?" among the uncertain inputs; `Why`: its record read back through its causes, newest first, six at most, stopping where the journal no longer keeps it or the player can't see it; `Threads`: its haggles with the player, and `Counter`: the offer form while the player may counter someone's post); `Check`, `Head`, `BlockText`, `OutcomeText`, `BrokerText`, `CommandText`, `Why`, `TermsOf` (the terms from the player's side: performer, payer, or seller of its own lot; a Build's and a ship sale's from `ShipView`); `Orders` (a Build's orders tree, `ShipView.Orders`) and `Ship` (the ship it concerns: for sale, or built) with its label; a ship for sale's price and tax as the lines, "Buy the ship"; another's Build blocked (only a yard builds) with its bids as threads S8: `PushChoice` (the deal pushed: arrival, on time or late, the odds against cruise's, take-home, and the trap's sentence when only pushing makes the deadline) as `Push`; the incident line's table shifted by the route's danger, warned on a red route; `Block.CannotBond`. | `Shell`, `BoardView`, `LedgerView` (`Head`), `ComposerView` (`TermsOf`), `PostsView` |
| `Views/NegotiationView.cs` | `Choice<T>`, `OfferFormModel` (an offer as a form, with the deadlines, packages and late fees it may name), `BidLine`, `ThreadViewModel` (a haggle as a thread: offers with their tells, status, whose turn, accept, decline and counter); `Thread`, `Form`, `Packages`, `LateFees`, `CounterText`, `AcceptText`, `DeclineText` | `ContractView`, `ComposerView`, `PostsView`, `Shell` |
| `Views/ComposerView.cs` | `ComposerDraft` (the composer's fields, kept by the client, with `ShowAt`: the further systems ticked; `Spec` for a Build, `Ship` for a ship's sale), `ComposerViewModel` (choices, what posting it means, reference figures from the player's board, the usual fee or a ship's parts and assembly, its terms as sentences, `Showing` (`AdView.ForDraft`), what it commits (the fee to show it further among it), why it can't be posted; for a Build: `IsBuild`, the designs and custom, frames, drives and modules, `Design`, `SpecStats`); `Durations`, `BuildDurations`, `Default` (a first draft of a kind: a Build is a Refrigerated Transport at home, four weeks, shown where the yards are), `ReferencePrice`, `ToPost` (keeps only the ticks still to pay for), `Build`, `SpecStats`, `WithSpec` (a new spec re-prices a draft still at the old one's value), `BlockText`, `OutcomeText` | `Shell`, the bots' tests |
| `Views/AdView.cs` | `ShowOption` (a system: name, jumps, fee and delay, ticked or not), `ShowingModel` (title, note, options, `Fee`, `Total`, `Choose(ticks)`), `AdView`: `ForDraft`, `ForPost` (the player's own open post only; "shown at" names where it is shown already), `ShownLine` ("Shown at Verdant; Meridian Station from …"), `OutcomeText` (design §7.1, D38) | `ComposerView`, `ContractView`, `Shell`, tests |
| `Views/PostsView.cs` | `PostLine`, `PostsViewModel`: "your posts" with how many answered and how many wait for the player, and its haggles over others' posts (open, and the last three ended) | `Shell` |
| `Views/DecisionView.cs` | `DecisionOption`, `DecisionPrompt`, `DecisionView.Build`: each pending decision as the prompt that stops the clock (what happened, what is on its way, each answer with what the terms make of it; a Build's supply failure and ship inspection from `ShipView.BuildDecision`) |
| `Views/ShipView.cs` | `StatLine`, `HistoryLine`, `ShipSheetModel`, `OrderLine`; `ShipView`: `Build` (the ship sheet: what and where, parts with grades, every stat with its parts, running cost and value, built by whom and where, substitutions, history, contracts flown, its sale), `Stat` (a stat and how its parts make it), `Orders` (a Build's orders tree: each part, its orders, who took them, the hauls carrying them, the assembly; a parts order shows its Build above), `BuildTerms`, `ShipSaleTerms`, `BuildStatus`, `BuildDecision` (a supply failure's remedies with their figures; a ship built outside the policy) | `ContractView`, `ComposerView`, `DecisionView`, `Shell`, tests | `Shell` |
| `Views/MapView.cs` | `MapNode` (with the asks the player knows there, their age, the tier, a signal's badge), `MapRoute`, `MapLeg`, `MapRegion`, `MapShip`, `MapViewModel`, `LabelAnchor`: the schematic map, the selected contract's overlay and title per kind, region frames, label placement; `Ships(session, clock)` and `Along` (a course's position by share of hours) | `Shell`, `SectorMap`, `MapLayers`, `ShipLayer` |
| `Views/LogView.cs` | `LogLine`, `LogViewModel`: the journal's visible entries, newest first, 40 at most; other actors' routine (lots that expired, withdrawals, storage) left out S8: the player's own debt entries, and anyone's insolvency and dissolution. | `Shell` |
| `Views/LegacyView.cs` | `LegacyShip`, `LegacyViewModel`, `LegacyView.Build` (design §10): how it ended (the insolvency's sentence), the days in business, contracts honoured and broken (`Records.WholeRecordOf`), the failures in the month before (`Cascade`), the markets moved (goods delivered by place), the companies outlasted (`Dissolutions`), and every ship flown with its history (`ShipView.Build`) | `Shell`, tests |
| `Views/HeaderView.cs` | `SpeedButton`, `HeaderViewModel`: speeds (and "waiting for your decision"), date, credits, escrow (the player's share), running cost (`DecisionEngine.RunningCost`: a quarter for a ship laid up for sale), the fleet line (by the ship's job), alerts S8: debt instead of credits below ₵0, with its interest and where insolvency starts in the tip (`CreditsTone`), and the warning's deadline (`Warning`). | `Shell`, `LedgerView` |
| `Views/LedgerView.cs` | `LedgerAction` (store here, or load aboard), `LedgerLine` (with the contract or `Ship` it opens), `LedgerViewModel`: money, what the player owns and where (its ships first, each opening its sheet; holds and storage, with freshness and rent), its signed contracts with its role (carrying for, shipping with, selling to, buying from, ordering from) and the figure, flagged in default, and its own open posts S8: in debt, the interest a day and what the player owns against it. | `Shell` |
| `Views/NewsView.cs` | `NewsLine`, `SignalKind` (Idle, Short, PriceLow, PriceHigh), `SignalLine`, `NewsViewModel`: the latest issue of The Daily Accord and the signals in what the player knows (`Signals`: idle producers and consumers running short where it is present, asks 15% or more off its readings anywhere) | `Shell`, `MapView` (badges) |
| `Bots/Bot.cs` | `Bot`: a scripted player (design §20.3) that plays through the session; `Deals`, `Posts`, `Counters`; `Answer` (decisions by the AI's rule), `Takeable` (the deals its board offers that pay, skipping deals past the grace or goods the buyer could reject), `BestDeal` (the most take-home a day), `ReadDeal` (judged: take-home less `Risk`, and the best next deal from where it ends at `NextLegWeight`), `Take`, `ClearHold` | `BoardTaker`, `MarketReader`, `BotRun` |
| `Bots/BoardTaker.cs` | Takes the board's best deal as posted whenever its ship is free | `BotRun`, the CLI, `BotTests` |
| `Bots/MarketReader.cs` | Takes the deal it judges best (`ReadDeal`), countering a board deal whose post has waited (`Haggles`); with `PlansPurchases` (off unless asked: it loses money) it also posts a wanted Sale at a producer's door below the ask it knows when a delivered Sale pays well over the goods and the trip, haggles the answers, then fills the Sale from storage (keeping leftovers for the next fitting Sale, then selling them) | `BotRun`, the CLI, `BotTests` |
| `Bots/BotRun.cs` | `BotReport`, `BotRun.Run`: a world with the bot as its player, the clock four hours a glance, decisions answered, alerts acknowledged, invariants checked every 5,000 hours S8: a bot that goes insolvent ends its run there (`BotReport.WentUnder`). | the CLI's `Bots`, `BotTests` |
| `Views/Tone.cs` | `Tone`: what a value's colour encodes (gain, loss, warn, yours, muted, plain) | every view; CSS classes `tone-*` |

### Client: `src/ByGalacticAccord.Client/`

| File | Contents |
|---|---|
| `Program.cs` | The Blazor host; `Shell` is the root component |
| `Shell.razor` | Reads `?seed=` (default 20260619), `?bench=` and `?credits=` (a check aid: `GameSession.Grant`); builds the `GameSession` (started at Day 1 · 06:00); holds the open ship sheet (`SelectShip`, `CloseShip`; `ShipView.Build` when the revision or the ship changes); runs the frame loop; holds the selection, the last command's outcome (cleared when the clock starts) and the composer's draft; rebuilds the view models (decisions, posts, the composer and the news too) when `Revision`, the selection or the draft changes, and the ships every frame; `Accept`, the contract's `Command` (broker, walk away, cancel for default, raise the haul, withdraw), `Advertise` (show the player's post further), `Decide`, the ledger's `Act` (store or load), and posting and haggling (`NewPost`, `ChangeDraft`, `Post`, `CloseComposer`, `Counter`, `AcceptOffer`, `Decline`) | S8: when the game is over (`GameSession.IsOver`) only the legacy shows (`LegacyPanel`, "start again" reloads); `?credits=` may be negative (a check aid: debt, the warning, insolvency); `Accept(throttle)`.
| `Components/TimeBar.razor` | Title, speed buttons (`data-speed`), date, money, theme picker | S8: credits in the loss tone in debt, and the warning while warned (`data-testid="debt-warning"`).
| `Components/SectorMap.razor` | The map panel: `MapLayers` and `ShipLayer` in one SVG, the overlay card, the legend, the fleet line |
| `Components/MapLayers.razor` | Ground, regions, routes (`band-*` classes carry colour and dash), overlay legs, hours, locations (a badge, name, kind, the asks known there and their age; `tier-*` and `age-*` classes); renders only when its model changes |
| `Components/ShipLayer.razor` | Ships at their interpolated positions; renders every frame |
| `Components/BoardPanel.razor`, `ContractPanel.razor`, `CaptainsLog.razor` | The board (each row's kind and term package; `data-seen` is `live`, `ad` (seen only where it was advertised, and says so) or `news` (says so and fades with age)), the contract document (how the player knows of it, where it is shown, a button to the ship it concerns, a Build's orders tree whose lines select their contracts, the party's public record, its terms, its "why" chain whose lines select their contracts, its haggles as threads, the accept button, the counter-offer form, the other actions with what each does, and on the player's own open post a `ShowingBlock` to pay for further systems, its ticks kept until it pays), the log (a line about a contract selects it) | S8: the contract panel draws the push choice (`data-testid="push"`, the trap's sentence) and a second button to take it pushed (`data-testid="accept-push"`).
| `Components/DecisionPanel.razor` | A decision that stopped the clock: what happened (selects the contract), and a button per answer with its note |
| `Components/PostsPanel.razor`, `PostRow.razor` | "Your posts and haggles": each of the player's posts with its answers and each haggle over others' posts (selects its contract), and the button that opens the composer |
| `Components/ComposerPanel.razor` | The post composer: kind, goods, quantity (for a Build: design or custom, frame, drive, module, and the spec's stats with the parts behind each), place (and destination), price, how long it runs, package, late fee; the reference figures, the terms as sentences, a `ShowingBlock` whose ticks go into the draft, what it commits, and Post |
| `Components/ShowingBlock.razor` | "Shown at": a tick box a further system with its jumps, fee and delay, the total, and (on an open post) the button to pay |
| `Components/ThreadView.razor`, `OfferForm.razor` | A haggle as a thread in the contract panel (offers with their tells, its status; accept, decline, counter on the player's turn); the offer form for a counter (keeps what was typed while the clock runs) |
| `Components/LedgerPanel.razor` | The ledger: money, possessions with their store or load button (a ship's line opens its sheet), contracts (each selects itself) |
| `Components/ShipPanel.razor` | The ship sheet beside the map: parts, stats with the parts that make them, built and substituted, history (a line about a contract selects it), its sale |
| `Components/LegacyPanel.razor` | The legacy screen (design §10): how it ended, the cascade, the record, the markets, the rivals, the ships with their histories, and "start again" |
| `Components/NewsPanel.razor` | The Daily Accord (each item selects its contract) and the signals with their ages, beside the log |
| `Components/BenchPanel.razor` | `?bench=N`: runs a world with no player for N hours, shows the time and the `StateFingerprint` |
| `wwwroot/index.html`, `css/app.css`, `js/client.js`, `fonts/` | The page; colour tokens with dark (default) and light themes (`prefers-color-scheme`, or `data-theme` on `<html>`); the theme's storage, guarded; IBM Plex | S8: the legacy's and the push block's styles.

### Browser check: `tools/client-check/`

| File | Contents |
|---|---|
| `check.cjs` | With no arguments: `dotnet publish` the client to `artifacts/client-check/publish`, get the desktop fingerprint from the CLI, serve the site, then in Chromium: load (paused, Day 1 · 06:00, the board with each row's kind and terms, map, ledger; the tiers from Meridian, the age of what the player knows of each place, The Daily Accord), a contract from the news (says so, "still open?"), run at 4× until a takeable row is on the board (at the start there is none: a post a jump away shows only once advertised), select a contract (its terms as sentences, its "why", the counter-offer form, the party's public record), accept it (the log and the ledger record it), fill a wanted Sale through a carrier (the ledger lists the sale and the posted haul; the contract offers what can still be done), post a wanted Sale of 20 food at Verdant below the ask the map shows (its terms and reference figures; tick Meridian Station in "Shown at" and read the fee; "your posts" lists it), open it (where it is shown) and pay to show it one system further, run 16× (resuming alerts, answering decisions), read the answers as threads with their tells and counter one, check no reply comes while paused and one comes while running (the log words the haggle), the player's ship in the ledger (and goods aboard, if any), a decision prompt if the run raised one; then, in a fresh session with `?credits=40000`: the ship sheet from the ledger, the build composer (a design, then a custom build with a Fast drive, its stats), posting the Build, the yards' bids as threads, accepting one and the orders tree; light theme, phone width (no sideways scroll), the speed probe and the fingerprint; screenshots to `artifacts/client-check/screenshots`; fails on any console error, page error or failed request. `--site`, `--out`, `--fingerprint` override. | S8's step: the push choice on the contract taken (`19-push.png`), debt in the header and ledger (`?credits=-6000`, `20-debt.png`), the warning that stops the clock (`?credits=-8000`, `21-warning.png`), and insolvency ending on the legacy screen (`?credits=-12000`, `22-legacy.png`).
| `package.json`, `package-lock.json` | Playwright 1.63.0 |

### Tests: `tests/ByGalacticAccord.Tests/`

| File | Covers | Soak variants |
|---|---|---|
| `TestWorld.cs` | Helpers: `RunHeadless`, `Quiet` (no heartbeats, incidents off and the player's posts unanswered unless asked: a test drives the market itself), `Signature`, `Actor`/`Place`/`Player`/`ShipOf` by name, `RunUntilSettled`, `PostWanted` (standard terms unless asked), `PresentAt` (a unit in storage: a presence there), `ShowAJumpFurther` (its poster pays for the next ring, and the clock runs to when it shows), `AssertSound` (every invariant), the `Soak` trait constants | — |
| `DomainTests.cs` | `Credits`, `PenaltyOnLateDelivery`, `QualityWarranty`, `Cargo` decay, `FindPath` (multi-hop, unreachable, cache), `OpenContracts`, the goods, part and design tables; a design's stats from its parts (design §15.6); grades and a custom build | — |
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
| `TransitIncidentTests.cs` | `IncidentResolver`: deterministic, armour milder than courier, danger raises credit costs S8: danger shifting the outcomes toward the severe end (the table's shares at 0 and 0.42; the same draws never milder on a red run); pushing ×1.25 the speed and ×1.5 the odds, ×2 for a drive that runs hot. | — |
| `GameTimeTests.cs` | `GameTime` | — |
| `DeterminismTests.cs` | Same seed → same `Signature`; stepped replay; seeds diverge | 150k, 120k |
| `InvariantTests.cs` | `Invariants.Check` at checkpoints; credits; escrow reconciling exactly; no negative balances S8: only actors in business owe and a dissolved actor holds nothing (replacing "no actor goes below ₵0"); conservation counts debt written off. | 1M, 250k |
| `EconomyTests.cs` | Economy comes alive (every kind settles, every consumer consumes every good); frontier reachable | 200k; every failure path over 1M (a Build's own left to `BuildTests`); yards complete builds and fleets grow and shrink over 1M (plan S7); money supply over 1M; no monopoly over 500k; the frontier still supplied over 1M (every Marches demand 30% met or more; plan risk R1), and in the same runs posts shown two and three jumps out, contracts signed only through an ad, and Helios Prime getting 75% of its machinery or more (plan S6b) | S8: some AI actors fail and their ships resell, but not en masse (seeds 2 and 6, 1M); every failure path leaves out the two insolvency kinds, rare by design.
| `JournalTests.cs` | Entries match how each contract ended; every lifecycle entry names an earlier cause; a lost cargo reads back to its incident; the ring buffer and `Find` | — |
| `ShipCourseTests.cs` | A course agrees with its ship and job every hour; a haul settles at its course's arrival, delays included | — |
| `BuildTests.cs` | Plan S7's engine checks: a Build's whole life through the market (the deposit and escrow; the frame next door at once; the drive maker selling delivered and a carrier hauling it; assembly; the handover and the ship's history); goods conservation through parts into ships in a world left to run; a standard Build's substitute at the discount; a strict Build's none, and a late yard's ship rejected by an AI client; the player accepting or rejecting a ship outside the policy; a supply failure chosen by an AI client and each remedy chosen by the player; a late handover's fee; an AI client waiting in default and the player cancelling for default; cancelling at will either side; the player's Build bid on by both yards on their habits; a spec ship sold from a yard's lot; a frontier ship sale and its payer default; Edge Salvage buying, ferrying and breaking up; fleets growing and shrinking; a factory making parts and making do | — |
| `ConsequenceTests.cs` | Plan S8's engine and view checks: running costs and interest below ₵0; docking fees and rent to the station operator, into debt; every bond locked in full on the clearing house's credit up to half of what the performer owns (the S8 defect); the deep Accord collecting a carrier's liability into debt, the edge only from escrow; the warning, its lifting, and insolvency after seven days above half; insolvency at once above everything; an insolvent trader's contracts cancelled, its ship fire-sold and bought, its dissolution; goods fire-sold and written off unsold; a carrier whose shipper goes insolvent getting the break fee; the player's insolvency ending the game, its legacy; an AI carrier pushing to beat a late fee and not otherwise; the first hour's trap read as a trap on the board and in the contract, taken pushed; every new entry worded for an AI and the player | — |
| `ShipViewTests.cs` | The ship sheet (parts, stats from the parts, built, substituted, history), the orders tree, the build composer (a design, a custom build re-priced, posted), the yards' bids as threads, the supply-failure prompt, ships for sale and Builds on the board and buying one, every Build and ship-sale sentence for an AI and for the player | — |
| `PlayerActionsTests.cs` | Each player command through `Market` and each refusal (filling through a carrier among them); storing and retrieving; running costs every day; an alert stops `RunTo` | — |
| `TemplateTests.cs` | `Fill`; every template well formed; every enum word (tells, late fees, post kinds among them); an offer in a line; every journal entry (in default, cancelled and every haggle entry among them) worded for each kind, for an AI and for the player on either side | — |
| `FormatTests.cs` | Money, cents, percentages, quantities | — |
| `SessionTests.cs` | Speeds, the clock's fraction, the frame cap, the same world as one `RunTo`, the alert and the decision's hard pause, `Accept`, `Broker`, `HaulAgain`, `CancelAtWill`, `Store` and `Retrieve` | — |
| `DealEstimateTests.cs` | The estimate predicts the settlement of a clean haul, a late haul, a fill bought at the source and a lot, to the cent; empty legs; a busy ship | — |
| `ViewTests.cs` | Every view builds through a played game (the news, signals and the map's knowledge among them; deals of every kind, storing goods, filling through a carrier, posting wanted Sales and answering their haggles, the composer of every kind, answering decisions); the board's kinds and order; the ledger; the overlay's legs; interpolation along a path; the terms as sentences for every package and side; the decision prompts; the "why" chain; the contract's actions | — |
| `InformationTests.cs` | Plan S6's engine checks: the tiers from the player's presence, a ship's destination and storage; contracts here at once, a jump away only once their poster pays to show them where the player is, far only from the news; here live, near the bulletin, far as last seen, a newer ask superseding an older stock; an AI trader trusting a stale ask and paying the ask as it stands at the door; a refusal leaving a producer out for a day; the news reporting yesterday's contracts as they stand; a far post known everywhere until found closed; the player's far post reaching actors through the news; the news weights; the same news for a seed; the public record and own dealings | — |
| `InformationViewTests.cs` | Every kind of news item as a headline for an AI and for the player; the map's tiers and ages; signals (idle, running short, a low price) with their badges; a post from the news on the board and in the contract; a party's public record and the goods line's age | — |
| `AdvertisingTests.cs` | Plan S6b's engine checks: a post shown only at its places; each further system's fee and delay (the floor, a haul's two places); `Show` paying each station operator and showing it after the delay, never twice, never someone else's; the AI's ring a day while all it paid stays within 5% of the price (a total since S7); the player's post reaching the farm a jump away only once shown there; the player's refusals (not yours, nothing chosen, can't afford, with a post's own fee); a contract signed by someone who saw it only through an ad | — |
| `AdvertisingViewTests.cs` | The composer's "Shown at" options with fees and delays, ticked and totalled, and paid when posting; the player's open post: where it is shown, what else it could pay for, the outcome; a board row seen only where it was advertised | — |

## 7. Determinism rules (design §20.1)

- Randomness comes only from `context.Rng`, and only inside events or world generation. Draws today:
  `WorldGenerator` (personalities, first heartbeats), `TransitIncidentCheck`, `IncidentResolver`, and `NewsPublished`
  (`NewsDesk.Publish`: The Daily Accord's weighted draw, one per item, over yesterday's contracts in id order). The client
  draws nothing: its clock decides only *when* `RunTo` is called, and runs in any increments give the same world.
  Haggling draws nothing either: a reply's hours come from the actor's eagerness, its closeness to its limit and a
  spread computed from the haggle's and contract's ids (`Haggling.ReplyHours`), so the player's commands, made between
  events, never draw.
- Factories, yards, Edge Salvage and fleets draw nothing: they act on heartbeats, in a stable order (a factory's recipes, a yard's
  jobs in line and slots in order, an actor's ships in its fleet's order, contracts by id).
- Debt, insolvency and the throttle draw nothing either: interest and the solvency review run on the actor's heartbeat, a liquidation
  walks an actor's ships, goods and storages in their order and its contracts in `LiveContractsOf` order, and the danger's shift of the
  incident table (`IncidentResolver.Bound`) is decimal arithmetic, so desktop and WebAssembly reach the same outcome from the same draw.
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
for its bond. S6 replaced the near-omniscient visibility with tiers by presence and a knowledge store per actor. S7's
growing fleets cut the producers' walking away by about 95%, and let an actor change its fleet. S8 let balances go below ₵0 (debt,
interest, insolvency), made every bond whole, and made incident outcomes shift with danger. Its measurements kept the payout: without it,
savers hoard and trade halves or collapses (`log/S8.md`).

| Defect | Where | Step |
|---|---|---|
| AI actors pay out whatever they hold above a working capital (a sink standing in for spending, growth and failure). S7 keeps a wanted ship's price back (`GrowthFund`). S8 measured debt and insolvency in its place and found they don't replace it: with traders keeping their surplus trade halves, with station operators keeping their fees it collapses | `DecisionEngine.WorkingCapital`, `PayOut`, the knobs `TradersPayOut`, `StationsPayOut` | Open: spending (station upkeep as demand, growth beyond the fleet caps) would be the replacement |
| A producer selling delivered still sometimes loses the Sale when no carrier takes its Haul in time: since S7's fleets grew, a few hundred times a million ticks, down from three to five thousand | `DecisionEngine.SellDelivered`, `ArrangeTransport`, `RaiseHaulOffers` | S10 (tuning) |
| A producer with a full warehouse asks exactly its cost, so it banks nothing; idle with a small debt, interest takes it under in decades (Halcyon Fuelworks, two seeds of ten, years 50–80) | `PriceModel.ProducerAsk` (the supply factor's floor) | Open (S8's question) |
| The bots keep leftover goods in storage and pay their rent idle; in debt they can't climb out, and the market reader went under on seed 5 | `Bot.ClearHold`, `MarketReader` | S10 (the bots) |
| Ships are seldom ordered once the fleets settle, so a long run may meet no supply failure, substitution or cancellation on one; `BuildTests` drives them | `DecisionEngine.WantsShip`, `ProposeSupplyFailure` | S10 (the first hour's beat 4) |
| The board's estimate counts a late fee but not a cancellation for default past the grace; a deal it shows positive can end in default | `DealEstimate.Late` | S9 (an explanation for every number) |

## 9. No callers (left from the retired clients, or waiting for a step)

`ISimEvent.Describe` (never called); `ActorState.AllStock`; `EventJournal.Tail` (tests only); `SimulationContext.Neighbours`,
`TaxDirect`; `DeterministicRandom.Pick`; `Location.HasEscrow`, `HasArbitration` (contracts read their jurisdiction: `Contract.UsesEscrow`,
`Arbitrated`); `TransitIncident.Description` (only
`Describe`; the journal's text comes from the templates); `ReputationLedger.Snapshot`; `DecisionEngine.PartsInStock` (tests);
`PlayerActions.CheckPost`/`Post` of a ship for sale (the composer drafts it; the client offers no button yet). S7 retired `ShipClassInfo`
and the ship's text log: its history is typed now, and the ship sheet reads it. S8 wired `ShipStats.RunsHot` (the throttle). Delete or wire them as the steps rework their areas.

## 10. The client's frame

```
Shell.OnInitialized → new GameSession(seed, startTick: Day 1 · 06:00), paused; ?credits=N grants (or, negative, charges) → Rebuild()
Shell.RunClock: PeriodicTimer, 33 ms
  └─ each frame while running: GameSession.Advance(real seconds since the last frame, at most 0.25)
       clock += seconds × speed → RunTo(floor(clock)) → Revision++ ; a PlayerAlert or PlayerDecision stops the clock
     → Rebuild(): once the game is over (GameSession.IsOver: the player insolvent), LegacyView.Build once and nothing else: only the legacy shows;
                  else, if Revision, the selection or the open ship changed, Header/Board/Map/Log/Ledger/Decision/ContractView/ShipView.Build
                  always MapView.Ships(session, clock)  (positions along ShipCourse, by share of hours)
     → StateHasChanged: components whose model object is the same skip rendering; ShipLayer always renders
Player input → Shell.SetSpeed (refused while a decision waits, or the game is over) / Select / SelectShip, CloseShip (the ship sheet)
             / Accept (cruise) or take it pushed (GameSession.Accept(id, throttle) → PlayerActions → Market) / NewGame (the legacy: reload)
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
