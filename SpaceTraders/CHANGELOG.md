# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Code – Fixed (2026-10-07, B77)
- Startup sync no longer puts a ship back where it was before an arrival moved it (B77). The ship event scheduler starts first in the startup chain, so an arrival due during a restart is handled while startup sync fetches the ships' pages; the sync saved every ship as its page had it, and a ship that had jumped, flown on or sold meanwhile was stored as before. At the 05:27Z deploy on 2026-10-07 SPECTER-121 was put back at the gate it had jumped from, its next jump was refused and the explore plan left X1-FA16's gate alone for an hour; at the 11:04Z deploy SPECTER-35 was stored in transit for 10 minutes after it had arrived. Now a ship whose row was written after its page was asked for (`LastSyncedAt`) keeps where it is, its cooldown, fuel and cargo; its type, mounts and modules still come from the page.

### Docs – Changed (2026-10-07, B77)
- `PLAN.md`: B77. `docs/HOW_IT_WORKS.md`: startup sync and the arrivals handled while it runs.

### Code – Fixed (2026-10-07, B74)
- `ShipLeftIdle` no longer flags a probe parked at a shipyard (B74). Since slice 6.32 (D110, "Stays parked") a system with fewer probes than markets parks a probe at each shipyard, so a purchase there needs no probe called, and the plan gives it no due market; the rule still took every idle probe in a system with a due market for one the plan could send. On 2026-10-07 at 13:30Z all 52 open `ShipLeftIdle` anomalies were parked probes. The rule now leaves out the probe each shipyard market has in the probe plan's state.

### Docs – Changed (2026-10-07, B74)
- `PLAN.md`: B74, and where things stand after gembernodes#91 deployed slice 6.34, checked on the bot; 6.34's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the probe work `ShipLeftIdle` counts.

### Code – Changed (2026-10-07, slice 6.34)
- A cargo ship every half hour, before the probes (D116), as asked on 2026-10-07: "I would like to switch priorities between new trade ships and probes. So once every half hour, money permitting, a trade ship is bought, independent on whether probes still need to be bought.", and chosen: "Keep D88's check", "Save up, probes wait", "Keep the turns too". `PurchaseTier.TimedCargoShip` (new, 8) stands after every explorer and before the probes, which move from 8 to 9; the drones and cargo ships that take turns move from 9 to 10 and the far probes from 10 to 11. Beyond `Trade.ShipPurchases`, once a route has waited for a new cargo ship (D88), the trading plan says it needs the largest hold (D112) at `TimedCargoShip` when `Trade.ShipPurchaseIntervalMinutes` (new, 30; 0 or less for 30) have passed since it last bought a cargo ship, of the list or beyond it, or none is on record; till then at `Alternating`, in turn with the drones as before (`BeyondTheListTierAsync`). The credits are saved up for it as for anything in the order, so the probes wait until it is bought.
- The trading plan's state keeps when it last bought a cargo ship (`TradingAutomationPlanState.LastShipBoughtAt`, new), so a restart doesn't start the half hour again; it reads its state once a pass, before its purchase, and writes it when that changes too.

### Docs – Changed (2026-10-07, slice 6.34)
- `PLAN.md`: slice 6.34 with D116, and D43 marked as amended; where things stand after gembernodes#90 deployed 6.33; 6.33's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the trading plan's cargo ships and state, the order ships are bought in and its positions, the settings, and `spacetraders_purchase_need_credits`' positions.

### Code – Changed (2026-10-07, slice 6.33)
- The cargo ship bought beyond `Trade.ShipPurchases` (order 9) is the largest hold (D112), as asked on 2026-10-07: "when buying a new trade ship (purchasing order 9) it should pick whatever the known ship with the highest cargo capacity is, as long as it is not scarce", and chosen: "Within the trade reach", "Next largest". Of the ships that the shipyards within `Trade.MaxHaulDistance` jumps of home list with their hold, those that would be cargo ships (a hold and a tank, nothing to mine, siphon or survey with, no explorer) and that the shipyard doesn't have SCARCE, as cached: the largest hold, then the cheapest, then the nearest to home (`LargestHoldAsync`). A shipyard abroad counts only where a probe of ours is or is on its way, while the probe plan is on, to answer the purchase's call (D30). The purchase refuses a ship the shipyard, fetched again just before, lists SCARCE (`IShipPurchaseService.TryPurchaseUnlessScarceAsync`, new; `ShipPurchaseFailure.Scarce`). It used to be one more of the list's last type, at home. The list's own ships are bought as before.
- The turn between drones and cargo ships counts every ship but a drone, a probe, a surveyor or an explorer as a cargo ship (`PurchaseOrder.Turn`): counted by the list and the shuttles and haulers alone, a refining or bulk freighter bought beyond the list would have left the drones waiting for good. `ShipType.ShipBulkFreighter` (new), so the ledger books one under its type rather than `None`.
- Every explorer before the probes (D113), as asked on 2026-10-07: "I'd like Explorers (order 10) to go in front of probes (order 8)". `PurchaseTier.MoreExplorers` (10) is gone: every explorer the explore plan wants is an `Explorer` need (7), after the jump gate's loads; the probes keep 8 and the drones and cargo ships 9; `FarProbes` moves from 11 to 10. With no probe of ours in the shipyard's system, the command ship fetches each (D108).
- Exploring in rings (D114), as asked on 2026-10-07: "I'd like exploring done in concentric circles based on trade distance. So first the first 5 systems as is currently the case, then 6-10, then 11-15 etc.", and chosen: "Ring of their gate". Every exploring ship takes the systems 1 to 5 jumps from home (`Trade.MaxHaulDistance`) first, then 6 to 10, then 11 to 15, the nearest to it first within a ring (`ExploreAtlas.Ring`, new; `Next`, `NextByWays`). The jumps are those the systems dashboard shows (`JumpsFromHome`): a system behind a gate under construction counts one more than the system whose gate leads there; one found by a scan, with no gate known, comes after every ring. D103 put the first ring first and took the rest as one.
- A trade trip's requests first at the rate limit (D115), as asked on 2026-10-07: "I'd like trade ships to be prioritized in rate limiting. So if a trade ship docks/undocks/jumps/navigates/buys/sells it should not have to wait for a miner or a surveyor." A trade trip's goal steps (`ShipGoalExecutorService`) and its flights' departures and arrivals (`NavigateToWaypointHandler`, `NavigateToWaypointArrivedHandler`) mark their requests (`ApiPriority`, new, an `AsyncLocal`), the refreshes of the markets it trades at included. In `RateLimitingHandler` a marked request never gives way; another write gives way while one waits, for 10 seconds at most (`MaxWriteDelay`), and leaves the last 5 of the burst to them (`RequestBudget.TradeReserve`); a read gives way to them as to any write. `spacetraders_api_rate_limit_wait_seconds_total` gets the `kind` `trade`.

### Docs – Changed (2026-10-07, slice 6.33)
- `PLAN.md`: slice 6.33 with D112–D115, and D19, D21, D43, D102, D103 and D106 marked as amended; where things stand after gembernodes#89 deployed 6.32; 6.32's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the trading plan's cargo ships, the order ships are bought in and its positions, the explore plan's purchases and order, the probe plan's place in the order, the rate limiter, the settings and metrics tables.

### Code – Changed (2026-10-06, slice 6.32)
- The command ship fetches every explorer, unless a probe answers (D108), as asked on 2026-10-06: "Can we set up the command ship to go to that location if there isn't a probe there", the location being "where an explorer ship is supposed to be bought if it's in the purchase order and enough credits are available", and chosen: "Keep D102's order". When the order lets an explorer through and the credits allow it, and none of our ships is at the shipyard, a probe of ours in the shipyard's system or on its way there answers the purchase's call (`WaitingForAShipThere`, while the probe plan is on); with none, the command ship fetches it, the first (it used to set off even with a probe there) and every further one (before, a further one waited until one of our ships was in the shipyard's system, and nothing fetched it). Once on its way the command ship stays with the purchase while the credits are saved or the order holds it back; once the explorer is bought it comes home and is released.
- Probes at the shipyards first, those that sell explorers first of all (D109–D111), as asked on 2026-10-06: "and also have probes deployed to shipyards with priority, with shipyards with explorer ships being even higher priority than that?", and chosen: "Across systems", "Stays parked", "Probe tier". Within each tier, a system with fewer probes than shipyards that sell `SHIP_EXPLORER` (at the probe tier wherever the gates reach it), then than shipyards, then than markets gets the next probe or spare, before the systems nearer home (`Wants`); a probe that comes into a system flies to a shipyard without one first (`ProbePlanner.Entry`); in a system with fewer probes than markets a probe parks at each shipyard, and one at a shipyard that sells no explorer gives way to one that does, while the others roam (`ProbePlanner.Plan`).
- `ProbePlanner.Whereabouts` (new): where a probe counts, shared by the probe plan and the explore plan.
- Visibility: the probe plan's state gives each market's `Shipyard` (`None`, `Shipyard`, `Explorer`) and the next probe's `NextProbeFor`; the command ship's `PlanStarted` line says no probe of ours is there to answer the purchase's call.

### Docs – Changed (2026-10-06, slice 6.32)
- `PLAN.md`: slice 6.32 with D108–D111; where things stand after gembernodes#87 deployed 6.31; 6.31's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the explore plan's purchase, the probe plan's order and parking, the order ships are bought in, and the plans table.

### Code – Changed (2026-10-06, slice 6.31)
- Warping (D100, D101), as asked on 2026-10-06: "Research how the warp works exactly, then measure, then fuel-safe.", and D101, "One planner for every way", suggested as "Whether to jump or use a warp drive if the ship has one, based on distance and fuel". The research note (`Warps`, new; sources in `PLAN.md`): a warp burns a flight's fuel on the systems' distance (the distance in CRUISE, twice it in BURN) and takes round(round(distance) × 50 / speed + 15) seconds in CRUISE, 25 instead of 50 in BURN; the drive's range caps it. Chosen after it: "CRUISE/BURN only" (D104): BURN where the fuel pays for it, CRUISE otherwise, never a drift.
- One planner for every way (`SystemWays` on a `WayChart`, new): the fastest way from a ship to every system, by the seconds, through the gates and, for a ship with a warp drive, by warps from wherever it is. Fuel-safe (D100): a warp lands at a market or a fuel station, or, into a system with nowhere to refuel, keeps the fuel to warp back and goes no further; a system the API refused a warp into gets none for an hour (`WarpRefusals`). Every flight between systems of a ship with a warp drive takes the fastest way's first step (`GoalJumps.TowardsAsync`, `IGoalWarps`): a warp only where it is faster or the only way.
- The warp (`WarpGoal`, `WarpGoalExecutor`, `GoalWarps`, `IWarpSubCommand`, new; `POST my/ships/{ship}/warp`): the tank filled first at a market where a full tank warps where this one can't, or in BURN; a flight to the system's nearest market first when short of fuel elsewhere; the flight mode, the warp, the arrival scheduled for the goal. A BURN warp the API refuses for its fuel goes in CRUISE; any other refusal (`WarpRefusedException`, new) blocks the goal with `warp_refused`. Journalled `Warped` with the fuel and seconds it took against those reckoned, and a warning where they differ: the first warp measures the note.
- The explorer warps (`ExploreAtlas.NextByWays`, new): the systems within the trade reach of home through the gates first, then the nearest by the seconds, by jumps or warps (D106, "Reach first, then nearest"). Before a warp goes to a system only a warp reaches, the explore plan fetches its waypoints, one system a pass (`ExploreAtlas.WarpLooks`). With nothing left, an explorer with a sensor array scans from where it is, once a system (`POST my/ships/{ship}/scan/systems`, `ScanSystemsAsync`, new; D105, "Scan when none left"): every system found is cached with its position, those within its warps join the plan (`SystemsScanned`). In a system the gates don't reach from home, an explorer with nothing left warps back to the nearest one they do, and is released there to trade. Systems only a warp reaches don't count towards the explorers wanted (D107, "No, gates only").
- A bought ship is cached with its mounts, modules, frame, reactor and engine at once, as startup sync caches them (`PurchaseShipActionResult`, `ShipPurchaseService`): a second explorer warps, scans and has its speed before the next restart.
- Visibility: the fleet table says "warping to …"; the journal's `Warped` and `SystemsScanned` lines.

### Docs – Changed (2026-10-06, slice 6.31)
- `PLAN.md`: slice 6.31 built, with the research note, D104–D107 and D103 amended; where things stand after gembernodes#86 deployed 6.30; 6.30's details and B73 moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the explore plan's warps, ways and scans, the `Warp` goal and the warp command.

### Code – Changed (2026-10-06, slice 6.30)
- The explorers (D98, D102), as asked on 2026-10-06: "I'd like the new ship type, EXPLORER, to be bought to help with exploring", then "I'd like more explorers to be added when there are more systems to be discovered. Maybe 1 explorer for every 10 undiscovered systems?", and chosen: "Reachable, round up", "Cap as a setting", "First before probes, rest last". The explore plan wants one explorer for every `Explore.SystemsPerExplorer` (new, 10) systems it knows, hasn't explored and reaches from home through built gates, or part of that, at most `Explore.MaxExplorers` (new, 5; 0 for no cap; `ExploreAtlas.SystemsLeft`). It buys `SHIP_EXPLORER` at the cheapest shipyard the gates reach: the first at the new `PurchaseTier.Explorer`, after the gate's loads and before the probes, which the command ship fetches (D30: a `MoveToWaypointGoal` to the shipyard, which now flies across systems through the gates, and waits there while the credits are saved up); every further one at the new `PurchaseTier.MoreExplorers`, after the drones and cargo ships that take turns and before the far probes, only while one of our ships is at the shipyard or a probe of ours is in its system to answer the purchase's call.
- Who explores: the explorers, each taking a system no other exploring ship has taken, or the command ship while there are none; once there is one, the command ship finishes its step, jumps home and is released (D60). An explorer with no system left is released where it is and trades from there (D102: "Explorers can trade with 40 cargo space, so they can trade at the location they are at until a new unexplored location comes up."), and is taken back once its trip ends and a system turns up. `FleetRoles.IsExplorer` (new): trading is an explorer's only role, and it is never a siphon drone, a builder or a cargo ship of `Trade.ShipPurchases`, though it carries a gas siphon (a bought one, whose mounts startup sync hasn't recorded yet, looked like a cargo ship).
- Where first (D103), as asked on 2026-10-06: "first the systems within 5 jumps are explored before going further", and chosen: "Reach first, then nearest", "The trade reach". Every exploring ship takes a system within `Trade.MaxHaulDistance` (5) jumps of home before any beyond it, the nearest to it first in each (`ExploreAtlas.Next`). That day the command ship had explored a chain 16 jumps deep while five systems within 4 jumps of home waited.
- Charting (D99), chosen: "Every uncharted market or shipyard, I'm not sure if every single asteroid needs to be charted but I don't want my explorer to waste time on that." In each system it explores, the exploring ship charts every uncharted waypoint of a type that can hold a market or shipyard (every type but ASTEROID and GAS_GIANT), the gate first, then the nearest (`Charting`, new), and stores a market or shipyard the chart shows. The reward, the credits after the chart less those before it, is booked as the new ledger category `ChartReward` (`WaypointChartedEvent`), with a `Charted` journal line. `ISpaceTradersPort.CreateChartAsync` returns the waypoint and the agent's credits. Replaces D62.
- Visibility: the explore plan's state lists each explorer with what it does, the systems left, the explorers wanted and the next purchase; `spacetraders_explore_systems_left` and `spacetraders_explore_explorers_wanted` (new); the purchase order's positions move (probes 8, the turns 9, the far probes 11); the fleet table says "flying to …" for a move to a waypoint of another system.

### Code – Fixed (2026-10-06, B73)
- The explore plan no longer takes a free command ship home after each trade trip abroad once nothing is left to explore (B73): since slice 6.29 a ship whose role is trading takes routes across systems and stays where its last sale leaves it (D96); only a ship that explored is brought home (D60).

### Docs – Changed (2026-10-06, slice 6.30)
- `PLAN.md`: slice 6.30 built, with D102 and D103; where things stand after gembernodes#85 deployed 6.29 (verified); 6.29's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the explore plan with the explorers and charting, the order ships are bought in, the settings, events, journal kinds and metrics.

### Code – Changed (2026-10-06, slice 6.29)
- Trade routes reach the systems around (D96), as asked on 2026-10-06: "I'd like to expand the trade system so other systems actually get considered and used", and chosen: "Trade.MaxHaulDistance jumps (Recommended)". The trading plan reads the systems the built gates reach within twice `Trade.MaxHaulDistance` (5) jumps of each free trader's system as one map (`ITradeContextReader.ReadReachAsync`, new), with the ways between them (`TradeGates`, new: the fewest jumps through the explore plan's gates, D101, within the reach; each jump's antimatter, the price at the gate it leaves; its cooldown, estimated from the systems' distance as fitted to SPECTER-1's twelve jumps of the day, 17 s plus 0.311 a unit; the credit floor every jump leaves, D63). A route buys within the reach of the ship's system and sells within the reach of the buy market's; a flight to another system flies to the gate, jumps and flies on (`TradeRoutePlanner.TryPlanFlight`). A route's profit is after its antimatter (`TradeRoute.AntimatterCost`, `Jumps`), its time counts a jump's cooldown only where it holds the ship (`TripTime`), and a trip that jumps keeps the credit floor besides, so a ship with its cargo can always jump on. The rate chooses (D95): a far route goes first only when it earns more an hour. A market's prices count only while at most `Trade.MaxPriceAgeMinutes` (new, 30) old, at home too (`TradeMarketMap.StaleMarkets`).
- Who crosses systems: a ship whose role is trading (with the role board off, a cargo ship), not a shuttle kept for a collection point; drones, builders and the survey ship in its spare time trade in their own system, where their own work is (`TradeMarketMap.WithoutJumps`). A trader stays where its last sale leaves it, and its next route is ranked from there. Cargo a trader holds is sold, or jettisoned (D42), in its system. Cargo ships are bought at home; a route abroad counts as one that waits for a new one (D88).
- The trade executor flies through the gates (`GoalJumps`): to the gate, the jump once the cooldown and the floor allow it, and on. At the buy market of a trip that sells in another system the batches keep back the haul's fuel, antimatter and the floor (`TradeRoutePlanner.KeptBackFor`). A trip with nothing aboard that can't jump on is dropped (`TradeDropped`: `no_way`, `jump_refused`, `not_possible`); one with its cargo aboard keeps it and waits for the way, or at the gate for the credits. A sale moves only within its system.
- The role board values a ship abroad, or on a trade trip that sells abroad, for trading only (`RoleSettings.Available`, `RoleSettings.BusinessSystems`), and estimates trading across the systems in reach, a drone's in its own system.
- Visibility: `TradeStarted` gives a route's `Jumps` and `AntimatterCost`; `TripEnded` books a trip after its antimatter (`AntimatterCost`), and so do the trip profit metric and the trade earnings that cap the role board's estimates (D87); `Jumped` gives the jump's `CooldownSeconds`; `GET /status/trading-routes` gives each route's `buySystemSymbol`, `sellSystemSymbol` and `jumps`; a good counts as traded in "Goods not traded" wherever its route buys it.

### Docs – Changed (2026-10-06, slice 6.29)
- `PLAN.md`: slice 6.29 built, with its readings; where things stand after gembernodes#84 deployed 6.28 and B72 (both verified); B72's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: trading across systems, the executor's jumps, the role board abroad, `Trade.MaxPriceAgeMinutes`, the new journal fields.

### Code – Fixed (2026-10-06, B72)
- The probe plan waits for a probe on its way, instead of buying its system's next probes at home (B72). A shipyard sells only where one of our ships is (D30), so a system's own shipyard counted only once a probe of ours was there, and while its first probe flew there every pass bought the next one at home: at home's rising price and with up to five jumps' antimatter, against D97's "the shipyard where it costs least". Now the next probe goes by the cheapest shipyard, the antimatter counted: one that can sell now sells it; one in a system a probe of ours is on its way to waits for it (`ProbePurchaseStatus.WaitingForAProbeToArrive`, new); one in a system with neither waits for that system's first probe, which is bought now at the cheapest shipyard that can sell it and flies there. Found before 6.28's deploy.

### Docs – Changed (2026-10-06, B72)
- `PLAN.md`: B72, and slice 6.28 merged, its details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: where a probe is bought, and the wait.

### Code – Changed (2026-10-06, slice 6.28)
- Probes at the markets abroad (D97, D101), as asked on 2026-10-06: "I'd like probes to be at each new market", and chosen: "Trade reach as a priority, all explored markets when money allows - I have minimum required credit reserves for a reason. It should also check whether the probes are in SCARCE supply and not buy them if they are." The probe plan serves home first, then each explored system the built gates reach, the nearest first; a probe counts for the system it is in or flies to. A system with more probes than markets lends its spares to the first system short of one (`ProbePlanner.Surplus`) before a probe is bought for it. A probe is bought for the first system short of one at the shipyard where it costs least with the antimatter of the jumps from there counted, never at SCARCE supply (the purchase also refuses one the shipyard, fetched again just before, lists at SCARCE: `ShipPurchaseFailure.Scarce`); a shipyard abroad counts once a probe of ours is in its system (D30), and a probe bought for another system flies there at once, to the market a roaming probe would pick from the gate (`ProbePlanner.Entry`). Home's and the trade reach's probes (`Trade.MaxHaulDistance` jumps of home, 5) stay at the probe tier; the other systems' come last (`PurchaseTier.FarProbes`, after the drones and cargo ships that take turns).
- Every flight between systems jumps alike (D101, `GoalJumps`, new): the fewest jumps through the built gates the explore plan knows (`ExploreAtlas.TryFindJumps`, `ExploreAtlas.Reachable`, read through `IGateNetwork`), a leg to each system's gate as every flight flies (D84), and at the gate the jump goal's steps, moved out of `JumpGoalExecutor`: the cooldown, the credit floor (D63), the tank filled where the gate sells fuel, orbit, jump, booked and journalled (`Jumped`). A jump the API refuses is recorded for every way (`JumpRefusals`, in memory), which leaves that gate alone for an hour. `DeployProbeGoalExecutor` flies a probe to another system this way; no way known, or a jump short of the floor, ends its goal for the plan to choose again, and a refused jump blocks it.
- The plans do business at home alone (D60): `BusinessSystems.Of` is the headquarters' system. It was every system where a ship that doesn't explore is, so a probe abroad would have made its system one where the mining, siphon, trading and contract plans buy ships.
- The probe plan's state is per system (`ProbeDeploymentPlanState.Systems`), with the next probe's system and antimatter; `ShipLeftIdle` gives a system's due markets to that system's probes; the fleet view says "flying to X1-…" for a probe on its way abroad; `spacetraders_system_probes` (new) counts our probes per system. The seeded descriptions of `Trade.MaxHaulDistance` and the probe plan's switch say what they do now (for the next agent).

### Docs – Changed (2026-10-06, slice 6.28)
- `PLAN.md`: slice 6.28 built, and where things stand after gembernodes#82 deployed projects#186–#191; 6.27's details moved to `docs/archive/PLAN_HISTORY.md`. `docs/HOW_IT_WORKS.md`: the probe plan in every system, the jump every flight shares, business at home only, the `FarProbes` tier, `Trade.MaxHaulDistance`, `spacetraders_system_probes`.

### Docs – Changed (2026-10-06, the plan cleaned up)
- `PLAN.md` cleaned up, as asked on 2026-10-06 ("Please clean up the plans"): from 3,829 lines to about 400. It keeps the goal, the roles, a new "Where things stand" (what runs, what is merged but not deployed, what is still open, the cluster), the open bugs (B17, B24), every decision, a line per phase and per phase-6 slice, and the slices still to build (6.28–6.31, across systems, with 6.27's notes). The rest moved verbatim to the new `docs/archive/PLAN_HISTORY.md`: the dated status log to 2026-10-06, the fixed bugs B1–B71 with their evidence, every finished phase's and slice's details, and the gembernodes changes. A slice's or a bug's details move there once it is merged. `README.md`'s status, `CLAUDE.md`, `CONTRIBUTING.md` and the `st-investigate` skill point to both.

### Code – Changed (2026-10-06, slice 6.27)
- Trade routes rank by what they earn an hour (D95), as asked on 2026-10-06: "I want a "profit per time unit" so the system can choose between a short route that pays less or a long route that pays more", and chosen: "Whole trip, keep order. This should work in addition to Gate feeding first, Exchanges last and Products at half, not in spite of it." A route's rate is its profit after fuel over the whole trip from where the ship is (`TradeRoute.Seconds`, `TradeRoute.CreditsPerHour`): the flight to the buy market and the haul in CRUISE through their refuelling stops, as the API reckons them, and 10 seconds at each landing (`TripTime`, new, which the role board's estimates now share). `TradeRoutePlanner.RankingProfit` became `RankingRate`: routes that feed the jump gate's materials still come first (D89), those to an exchange last (D91), and an end product's rate counts at half (D85). The trip keeps its time (`TradeBetweenMarketsGoal.ExpectedSeconds`), `TradeStarted` gives `CreditsPerHour` and `TripMinutes`, the "Goods not traded" reason of a lucrative good its rate and minutes, and `GET /status/trading-routes` each route's `expectedMinutes` and `creditsPerHour`.

### Docs – Changed (2026-10-06, slices 6.27–6.31)
- `PLAN.md`: slices 6.27–6.31 planned, across systems, with decisions D94–D101, as asked on 2026-10-06 ("CHART unchartered systems and waypoints", trade with "a "profit per time unit"" that considers other systems, an EXPLORER, and "probes to be at each new market"): profit per hour, probes at the markets abroad, trade across systems, the explorer and charting, warping, in that order, one PR each; slice 6.27 built. Where things stand: slice 6.25 merged and deployed, the home gate finished and the systems explored since. `docs/HOW_IT_WORKS.md`: ranking by the rate, the trip's time, `TradeStarted`'s new fields.

### Code – Changed (2026-10-06, slice 6.26)
- Every ship that can build builds the jump gate (D93), as asked on 2026-10-06: "Let's remove the one gate ship limit, but have a "underway" counter of items so there aren't 3 ships gunning for the final 40 FAB MATS." `Construction.Ships` is now an optional cap, 0 (the new default) for no limit; with a cap, the largest holds build, as before (D65). The underway count was there already: a load takes only what no other trip carries or goes to buy (`MaterialNeed.OnTheWay`), and one trip buys a material at a market at a time (D80), so of three builders and the last 40 FAB_MATS one takes them and the others trade.

### Docs – Changed (2026-10-06, slice 6.26)
- `PLAN.md`: slice 6.26 and decision D93. `docs/HOW_IT_WORKS.md`: who builds the gate, and the setting.

### Code – Fixed (2026-10-06, B70)
- A ship whose arrival was never handled gets it again (`LostArrivals`): a goal step for a ship still stored in transit more than 5 minutes past its arrival time schedules its arrival again, for its active goal, at most once every 5 minutes, and logs a warning. Only the arrival's dock takes a ship out of transit in the cache, and an arrival whose dock failed four times was dropped: SPECTER-5's, at 00:42:53Z on 2026-10-06 in a 35-second outage of the game's API, left it waiting for hours, until a restart.

### Docs – Changed (2026-10-06, B70)
- `PLAN.md`: B70. `docs/HOW_IT_WORKS.md`: the arrival's dock, and a lost arrival.

### Code – Fixed (2026-10-06, B71)
- The mining, siphon and construction plans read every ship's goal before the ships (`FleetGoals`). A trip that ends in its arrival handler empties the hold first and ends its goal after; read the other way round, a pass could see the goal ended beside the ship as it was before, free with cargo it no longer held. The builder SPECTER-D, which had just supplied the gate's last 80 ADVANCED_CIRCUITRY, was passed over to the trading plan and spent 18 minutes on an ELECTRONICS trade while its next FAB_MATS load waited; a drone would have been sent to sell what it had just sold.

### Docs – Changed (2026-10-06, B71)
- `PLAN.md`: B71. `docs/HOW_IT_WORKS.md`: the gathering plans read the goals before the ships.

### Code – Fixed (2026-10-06, B69)
- Once there is a probe for every market, each market keeps one probe and only the spares fly: a free probe at a waypoint that is no market, or at a market that has another, goes to a market without a probe, due or not (`ProbePlanner`). Free probes used to fly only to due markets, scored by age minus twice the flight, so a market our other ships keep fresh never got its probe, and a probe next door left its own market for a due one: the eight probes bought at X1-FJ91-C46 sat there for a day while seven markets had none, and the probes in the A, D and H clusters hopped between neighbours 65 to 109 times an hour. With fewer probes than markets they still roam (D29).

### Docs – Changed (2026-10-06, B69)
- `PLAN.md`: B69; slice 6.25 merged and deployed, and the jump gate complete. `docs/HOW_IT_WORKS.md`: the probes' flights once there is a probe for every market.

### Code – Added (2026-10-05, slice 6.25)
- The jump gate's miners (D92), as asked on 2026-10-05: "extra miners to be bought for the ores that supply the build gate materials once every half hour (and those miners being dedicated to those ores) until each of the smelters have at least HIGH saturation". While the gate needs materials, a smelter is a market that imports an ore and exports a metal, made from ores alone, that goes into a material the gate still needs (`MiningPlanner.GateSmelters`; `TradeMarketMap.GoesIntoConstruction`): IRON_ORE into IRON for FAB_MATS, COPPER_ORE into COPPER for ADVANCED_CIRCUITRY. For each ore with a smelter below HIGH that a new drone can serve, the mining plan buys one mining drone every `Mining.GateMinerIntervalMinutes` (new, 30), the lowest supply first, within `Mining.MaxDrones` and the credit reserve. It stands at the gate's place in the order ships are bought in, after a load the markets sell now: the construction plan says when its load waits for SCARCE or LIMITED markets, or another buyer there (`PurchaseNeed.WaitsForMarkets`), and only then may the miners be bought. Each such drone mines only its ore, for its smelter with the lowest supply (reason `gate`), and parks at no collection point, until the gate needs nothing made from its ore; the mining plan's state lists them (`GateMiners`). Among the drones for scarce minerals (D48) a gate miner counts for its own ore only.

### Docs – Changed (2026-10-05, slice 6.25)
- `PLAN.md`: slice 6.25 and decision D92; slice 6.24 merged and deployed. `docs/HOW_IT_WORKS.md`: the jump gate's miners, the construction plan's need, the order ships are bought in, the new setting and the `gate` and `wealth` reasons of `MiningStarted`. `docs/GLOSSARY.md`: *Gate Miner*, *Smelter*.

### Code – Changed (2026-10-05, slice 6.24)
- Ore and gases go to the markets that make something from them, and EXCHANGE markets are wealth trades only (D91), as asked on 2026-10-05: "EXCHANGE nodes should be lowest priority and only considered as wealth trades, never as supply trades" and "first redirect the ore to a place that actually generates iron". A market supplies production when it imports the good and exports something made from it (`TradeMarketMap.MakesSomethingFrom`; without the production chains, any import). Miners and siphoners serve such markets first and share such a pair before mining or siphoning for a market that only pays (an exchange, or an import it makes nothing from, like D52's IRON_ORE), which never counts as scarce, buys no drone and is no opening; reason `wealth`. Their leftover cargo goes to such a market whenever that sale pays (`supplyFirst`). A trade route to an exchange never feeds production and ranks last (`TradeRoute.ToExchange`).

### Docs – Changed (2026-10-05, slice 6.24)
- `PLAN.md`: slice 6.24 and decision D91; slice 6.23 merged and deployed. `docs/HOW_IT_WORKS.md`: supply first, exchanges last, for the miners, the siphoners and the ranking of trade routes.

### Code – Changed (2026-10-05, slice 6.23)
- A trade route that feeds a material the jump gate still needs runs while its goods sell for at least what they cost, only its fuel lost (D90), as chosen on 2026-10-05: "Up to its fuel (Recommended)". IRON for the FAB_MATS markets D52 and F58, which paid 150 to 155 while H60 charged up to 157, earned less than the 5 a unit of D14. `TradeRoute.IsWorthIt`; the planner and the trade executor buy such a trip's units while each sells for what it costs, weigh it at no price gap, and sell at its market unless that would fetch less than the cargo cost and another market pays more. "Goods not traded" says "feeds the jump gate's …".

### Docs – Changed (2026-10-05, slice 6.23)
- `PLAN.md`: slice 6.23 and decision D90; slice 6.22 merged and deployed. `docs/HOW_IT_WORKS.md`: feeding the jump gate at cost.

### Code – Changed (2026-10-05, slice 6.22)
- Trade routes that feed the jump gate's materials come first (D89), as asked on 2026-10-05: "Please make sure the trade routes prioritize the feeding to the portal construction materials". While the gate needs a material and the construction plan is on, a lucrative route to a market that makes it from the good (exports the material, imports the good below ABUNDANT) goes before every other: IRON for the FAB_MATS markets D52 and F58, RESTRICTED while their IRON was SCARCE. `TradeMarketMap.ConstructionMaterials`, `ConstructionMaterialMadeFrom`; `TradeRoute.ConstructionMaterial`. `TradeStarted` says "which makes the jump gate's … from it (D89)".

### Docs – Changed (2026-10-05, slice 6.22)
- `PLAN.md`: slice 6.22 and decision D89; B68 merged and deployed. `docs/HOW_IT_WORKS.md`: feeding the jump gate first.

### Code – Fixed (2026-10-05, B68)
- A survey ship at a collection point's asteroid stays there and surveys, as D83 meant. With too little fuel to cruise to the point's market, where the parked drones count, the survey plan had moved it back to the market after every survey, a 25-minute drift: SPECTER-2C took one survey at B44 every 28 minutes, and the drones there extracted without one (138 unsurveyed extractions in the hour to 15:30Z).

### Docs – Changed (2026-10-05, B68)
- `PLAN.md`: B68. `docs/HOW_IT_WORKS.md`: the survey ship at a collection point.

### Code – Fixed (2026-10-05, B67)
- The role board credits a ship only with trade routes the trading plan could give it: none another ship's trip holds, nor a good another trip is on its way to buy at that market (D80). A trade job is the good at its buy market, so no two ships are credited with it, and each job offers its best trip. SPECTER-1 had changed role 120 times in 12 hours on estimates of 0.14 to 1.77 million an hour.

### Code – Changed (2026-10-05, slice 6.21)
- A ship's trade estimate is capped at what the trade trips that ended in the last two hours made per hour of their time (D87, "Cap at realized"; `TradeEarnings`, noted by the trip book).
- Beyond `Trade.ShipPurchases` the trading plan buys a cargo ship only once a route worth `Trade.ShipPurchaseMinRouteProfit` (10,000), held by no trader, has waited `Trade.ShipPurchaseWaitMinutes` (30) for a ship with every trader busy (D88, "Routes keep waiting"; `TradeShipDemand`). Two new settings.

### Docs – Changed (2026-10-05, slice 6.21)
- `PLAN.md`: B67, D87, D88, slice 6.21; slice 6.20 and B66 merged and deployed. `docs/HOW_IT_WORKS.md`: what a role earns, and the cargo ships beyond the list.

### Code – Changed (2026-10-05, slice 6.20)
- Trade routes rank by profit, an end product's (a good nothing is made from) counted at half (D85), as chosen on 2026-10-05 ("Half weight") after no trader took FOOD at about 75,000 a load while trips of 302 to 3,864 went first under D82's order. `TradeRoutePlanner.RankingProfit`; `Rank` and `CompareBestFirst` use it.
- A collection shuttle trades like any cargo ship until one of its point's drones is parked at the asteroid (D86, "Trade until parked"): the role board gives it the `Collect` role only then, from the end of its trip. SPECTER-2B had waited without work for about three hours.

### Docs – Changed (2026-10-05, slice 6.20)
- `PLAN.md`: slice 6.20, decisions D85 and D86; slice 6.19 merged and deployed. `docs/HOW_IT_WORKS.md`: the ranking of trade routes, the collecting role, and a collection point's shuttle.

### Code – Fixed (2026-10-05, B66)
- A good whose route waits for a free trader now says so in the trading plan's goods not traded (`waiting`: "It waits for a free trader: the free traders took routes that rank higher."), as asked on 2026-10-05: "it should at least give that reason for that good in the goods not traded tab". Such a good had no row, being listed, and once every trader was on a trip the waiting routes went and it was in neither list: ASSAULT_RIFLES and FOOD on 2026-10-05.

### Docs – Changed (2026-10-05, B66)
- `PLAN.md`: B66. `docs/HOW_IT_WORKS.md`: why a good isn't traded.

### Code – Changed (2026-10-05, slice 6.19)
- A flight out of CRUISE reach takes the fastest way (D84), as asked on 2026-10-05: "can we optimize the routing for a location where a combination of cruising and drifting is faster than just drifting?" It cruises as far as it can, refuelling at markets that sell fuel, and drifts the rest (`TradeRoutePlanner.TryPlanMixedFlight`, A* over the waypoints and the fuel aboard, about a millisecond in X1-FJ91). From H60 to B44 a drone cruises to F57 and drifts from there: 2.0 hours rather than 2.9 by B7. Drones for a collection point (D83) fly straight to their asteroid.
- Ships burn (D84), as asked on 2026-10-05: "I'd like a ship to burn if they can reach the destination with double fuel consumption, but cruise if they cannot. I accept the extra fuel costs this brings". A leg burns when the tank holds twice its CRUISE fuel and burning strands nothing: into a market that sells fuel, or elsewhere when what is left still takes the ship on as cruising would (`TradeRoutePlanner.TryPlanNextLeg`). A ship in orbit at a market that sells fuel docks to fill its tank first when a full tank would burn. The navigation flies a BURN the fuel no longer pays for in CRUISE before anything drifts.
- No leg lands a ship with an empty tank where no fuel is sold (asked on 2026-10-05: "A ship can technically land anywhere with 1 fuel and then drift to a fuel station").
- Every goal's flights and the contract's commands fly their leg's mode. `DriftStarted` is journaled on the leg that drifts, with `Leg` for where the drift ends. The fleet view and `spacetraders_ship_info` say "drifting to <asteroid>" for a drone on its way to a collection point.

### Docs – Changed (2026-10-05, slice 6.19)
- `PLAN.md`: slice 6.19 and decision D84; slice 6.18 merged and deployed. `docs/HOW_IT_WORKS.md`: how the executors fly, the far targets, the survey ship's move, the collection points' drones, the contract's flights, the navigation's fallback, the trade arithmetic's fuel, and `DriftStarted`. `st-investigate`: what a drift looks like now.

### Code – Added (2026-10-05, slice 6.18)
- Drones park at a far asteroid and hand their ore to a light shuttle that sells it (D83), as asked on 2026-10-05: "We park a light shuttle per ore type at the asteroid, and have the drones drop their ore into the light shuttle. When the light shuttle is full, it sells the ore at the market, then comes back." With the choices made then: one shuttle per asteroid that takes every ore, a survey ship parked there too, bought with the drones for scarce minerals, and only at asteroids no drone mines on a round trip of the market that buys their ores. In X1-FJ91 that is B44, 53 from B7, whose GOLD_ORE, SILVER_ORE and PLATINUM_ORE were SCARCE: a drone's 80-unit tank doesn't fly the 106 there and back, so no drone had ever mined there.
- `MiningPlanner.CollectionPoints` finds such asteroids. A mining drone with no uncovered ore of its own to serve takes a place there (`MineForShuttleGoal`): it drifts to the market first when that is out of its CRUISE reach (D45), flies on, and stays, mining with the best survey and handing its hold to the shuttle in orbit there (`POST my/ships/{ship}/transfer`, new in the API client), or waiting with a full hold. The shuttle's round (`CollectOreGoal`, trip activity `collecting`) waits in orbit until its hold is full, or no drone is left, then sells everything at the market and is booked as a trip. It starts once a drone is parked there.
- Purchases at the Coverage tier: a light shuttle for a point where a drone has a place, a second when a parked drone waits with a full hold while the first is away selling; each point's scarce ores count a drone each. The shuttle is designated for its point in the mining plan's state, and the role board keeps it in the new `Collect` role: it neither trades nor builds, its hold adds nothing to the credit reserve, and it doesn't count towards `Trade.ShipPurchases`.
- The survey plan gives a collection point's ores survey targets at its asteroid, counts the drones parked there in their market's area (D55), and lets a survey ship that reaches the asteroid survey there though it can't fly on (B58), as it stays parked.
- Journal: `CargoTransferred`, `CollectionStarted`, `MiningStarted` reason `collection`, `RoleChanged` role `Collect` reason `collection`. The fleet view and `spacetraders_ship_info` say "mining at … for the shuttle" and "collecting ore at …".

### Docs – Changed (2026-10-05, slice 6.18)
- `PLAN.md`: slice 6.18 and decision D83. `docs/HOW_IT_WORKS.md`: far asteroids with a shuttle in the mining plan, the collecting role, the survey ship that parks, the two goal kinds and their executors, and the journal.

### Code – Changed (2026-10-05, slice 6.17)
- Trade routes of goods something is made from come before those of end products, wherever they are sold (D82), as asked on 2026-10-05: "Ship parts do feed a factory, being the SHIP factory. So while I understand why things like food have a lower priority, this shouldn't be the case for ship parts." An end product is a good nothing is made from by the API's supply chain, ships included (`TradeMarketMap.IsEndProduct`): in X1-FJ91 exactly the 13 goods asked to come last, from ANTIMATTER to SUPERGRAINS. Before, a route came first only when its sell market made a pricier good from the cargo, by its exports (D15), so SHIP_PARTS and SHIP_PLATING, sold only at shipyards' markets, never did, nor did MACHINERY, ELECTRONICS or EQUIPMENT at their best markets: the traders took IRON or SILVER for about 2,000 a trip while SHIP_PARTS earned 3,800 a unit. Within each group the most profitable route still comes first; `FeedsTradeSymbol` still names what the sell market makes from the cargo.

### Docs – Changed (2026-10-05, slice 6.17)
- `PLAN.md`: slice 6.17 and decision D82; slices 6.15 and 6.16 merged and deployed. `docs/HOW_IT_WORKS.md`: the ranking of trade routes.

### Code – Fixed (2026-10-05, B65)
- The gate's next load stays in the order ships are bought in while the builder flies a load to the gate (D64). The construction plan judged a builder on its way from where it lands, with the fuel its flight leaves it but as if it couldn't refuel there, so from the far-out gate no market was in reach and the plan told the order it needed nothing: on 2026-10-05 a light hauler was bought for 345,915 at 08:40:32, during SPECTER-D's flight with the gate's first load, ahead of the next one, and raised the credit reserve by 80,000. A builder in transit is now judged docked where it lands, filling its tank where fuel is sold.

### Docs – Changed (2026-10-05, B65)
- `PLAN.md`: B65. `docs/HOW_IT_WORKS.md`: how the construction plan judges a builder on its way.

### Code – Changed (2026-10-05, slice 6.16)
- Trade trips carry as many units as each earn `Trade.MinProfitPerUnit` and trade them in batches (D79), as asked on 2026-10-05: "A ship should buy as much as is profitable per trip, and sell as much as is profitable per trip." A market's trade volume is the most one purchase or sale takes, not its stock, and each purchase raises the next quote and each sale lowers it. The trading plan sizes a trip unit by unit with the steps measured that morning (`PriceSteps`: 2%, 4% or 6% a batch bought for goods traded up to 6, up to 20 or more at a time, 2% a batch sold), up to the hold and the credits, and the trip holds back what they are expected to cost (D57). At the buy market the trade executor buys a batch at a time while the next units' expected sale still earns the minimum over the price quoted then; at the sell market it sells a batch at a time while that earns the minimum over what the cargo cost, then takes the rest where it fetches more, once per trip, or sells it anyway. It refreshes the market and stores the trip after every batch. Replaces D56's full hold in one purchase and one sale, and D74.
- One buyer of a good at a market at a time (D80): a trade or construction trip on its way to buy holds its good at its buy market, so the trading plan offers no route of it from there and the construction plan buys no load there (`market_busy`) until it has bought. Seven trade trips since the reset were dropped on arrival after another trader, sent for the same good at the same moment, had bought first.
- Why a good isn't traded (D76): the reason `not_full_hold` became `no_room`, and a route whose first unit earns too little says what a unit earns.

### Docs – Changed (2026-10-05, slice 6.16)
- `PLAN.md`: slice 6.16, with D79 and D80. `docs/HOW_IT_WORKS.md`: a trip's units, saving up for a trip, the credits a trip holds back, one buyer at a time, why a good isn't traded, the trade executor's buy and sell steps, `TradeDropped`, and the construction plan's `market_busy`.

### Code – Changed (2026-10-05, slice 6.15)
- The jump gate's loads are bought in batches of the market's trade volume (D81), as chosen on 2026-10-05 ("Full hold in batches") once a market's trade volume turned out to be the most one purchase takes, not its stock. Asked: "Also, I'm at 836k, construction should start at 624k. Why isn't construction started?" The gate had had no load since 10-04 18:09Z: every market sold FAB_MATS and ADVANCED_CIRCUITRY 20 at a time, and a load had to be one purchase of the builder's 80-unit hold (D67). A load is now the free hold, or what the gate still needs, at one market whose supply isn't SCARCE or LIMITED (D66); the construction executor buys it a batch at a time at the price quoted then, fetches the market again after each (D25), stores the trip after each, and stops when the supply falls to LIMITED or the next batch would dip into the credit reserve (D64), taking what it has to the gate. What a load costs is estimated batch by batch (`PriceSteps`: 2%, 4% or 6% a batch for goods traded up to 6, up to 20 or more at a time, rounded up from the medians measured on the bot's 222 purchases since the reset). `ConstructionDropped` no longer has `not_full_hold`, nor the plan's `Waiting` `trade_volume`.

### Docs – Changed (2026-10-05, slice 6.15)
- `PLAN.md`: slice 6.15, and decisions D79 to D81 (D79 and D80, trade trips in batches with one buyer per good and market, are for slice 6.16). `docs/HOW_IT_WORKS.md`: a load, the plan's `Waiting`, the construction executor's buy step, and why a construction trip ends. The `st-investigate` skill: the plan's waiting reasons, and a load in batches.

### Code – Changed (2026-10-05, slice 6.14)
- Drones mine and siphon until every mineral is ABUNDANT (D77), as asked on 2026-10-05: "I'd like the miners to only mine, even if there is more profit in trading. They can mine until every mineral is ABUNDANT." A market that has a mineral ABUNDANT is no longer a target of the mining and siphon plans (`MiningPlanner.IsAbundant`, in `MiningTargets` and `SiphonTargets`, which the role board's estimates read too); HIGH still is. A mining or siphon drone that finds no pair below ABUNDANT without a ship shares one (`MiningPlanner.SharedTargets`, `SiphonPlanner.SharedTargets`): the lowest supply first, a pair in CRUISE reach before one a drift away (D45), then the pair with the fewest ships on it; the journal says reason `shared`. Only with nothing below ABUNDANT left that it can reach and sell is a drone passed over to the trading plan (B63). Unchanged: drone purchases (a pair a drone would only share buys none), the command ship, which takes what pays it most (D38), the contract's miners and the cargo ships (D78).

### Docs – Changed (2026-10-05, slice 6.14)
- `PLAN.md`: slice 6.14, and decisions D77 (amends D28 and D58) and D78: asked whether trade ships are bought only for routes nobody serves, they are, at one pass (a lucrative route no trader holds while every trader has a trip, D21), and stay so. `docs/HOW_IT_WORKS.md`: no mining or siphon target at ABUNDANT, sharing, what that means for the role board's drones and the trading plan, and the `shared` reason in the journal.

### Code – Added (2026-10-05, slice 2.18)
- Why the other goods aren't traded (D76), as asked on 2026-10-05: "Can the new list also add why the other goods are not considered for trading?" Where the trading plan lists a free trader's lucrative routes, it puts every route with a price gap (a market sells the good for less than another pays for it) through the checks `TradeRoutePlanner.Rank` runs, in their order (`TradeRoutePlanner.Judge`; `Rank` runs the same `CheckRoutes`): the buy market in reach, the sell market in reach from it, a full hold in one purchase and one sale or an ABUNDANT seller (D56, D74), the credits, the profit after fuel (D14). Its state's `NotTraded` keeps, for each such good that no listed route carries, the check its route failed for the free trader that got furthest with it, in a sentence with the figures (`TradeRouteJudgement`), or that it ranks below the 20 waiting routes kept. While every trader in a system is on a trip, what the last pass with a free trader found there stays, with its time. Only a change is written, as before. `GET /status/trading-routes` serves it as `notTraded`, and the markets dashboard's trade routes table lists it after the routes (gembernodes, same branch). `Rank`'s routes are unchanged.

### Docs – Changed (2026-10-05, slice 2.18)
- `PLAN.md`: slice 2.18 and decision D76; slices 2.13 to 2.17, 6.12 and 6.13 merged and deployed. `docs/HOW_IT_WORKS.md`: why a good isn't traded, with the trading plan's state, and `notTraded` in the status endpoints.

### Code – Added (2026-10-04, slice 2.17)
- `GET /status/trading-routes` serves the trading plan's routes in the order it gives them out (D75), as asked on 2026-10-04: "Can you, in a new pr, add the exact logic to the market tree view that is used to determine which trade is done first?" It reads the plan's state as its last pass stored it: the routes traders hold, then the lucrative routes no trader holds, numbered from 1 (a route that feeds a pricier good first, D15, then the most profit after fuel), each with its units, profit after fuel and per unit, what it feeds and the traders that could take it. The markets dashboard shows it under the market tree (gembernodes, same branch).

### Docs – Changed (2026-10-04, slice 2.17)
- `PLAN.md`: slice 2.17 and decision D75, and slice 6.13 merged and deployed. `docs/HOW_IT_WORKS.md`: the endpoint, in the status endpoints and with the trading plan's state.

### Code – Added (2026-10-04, slice 2.16)
- Each ship's profit, as asked on 2026-10-04: "I'd like to see each ships total profit. So -purchase price-market buys+market sales-fuel (plus or minus any other relevant ship-specific credit changes)". `spacetraders_ship_ledger_credits{ship,category}` is each ship's ledger since it joined the fleet, summed by category: earnings positive, costs negative (its purchase, mounts and modules, cargo bought, fuel, a jump's antimatter, the jump gate's materials); summed by ship, what it has made. `PrometheusMetricsService` reads it every 10 seconds in one query grouping the ledger by ship and category, which also gives what each ship cost. The contract's payments are booked to the agent, no ship.
- In Grafana (gembernodes, same branch): a "Profit by ship" table under Roles, most profitable first, with what each profit is made of and the fleet's totals.

### Docs – Changed (2026-10-04, slice 2.16)
- `PLAN.md`: slice 2.16. `docs/HOW_IT_WORKS.md`: `spacetraders_ship_ledger_credits` and `spacetraders_ship_value_credits` in the metrics table, and who reads the ledger.

### Code – Changed (2026-10-04, slice 6.13)
- A trade trip may carry less than a full hold where the buy market's supply of the good is ABUNDANT (D74), as asked on 2026-10-04: "either a full hold needs to be obtained, or the supply of the seller needs to be ABUNDANT, in which case a full hold is not necessary. All other rules for profitability etc. Still stand." It carries what both markets trade at once, the smallest of the free hold and the two trade volumes, still in one purchase and one sale and paid for in full (`TradeRoutePlanner.UnitsAtOnce`, which replaces `TakesFullHold`). Anywhere else a trip is a full hold, as D56 has it. At the buy market the trip is worked out again with the supply its arrival fetched: it buys what the markets trade at once then, and a seller no longer ABUNDANT whose volume fills no hold drops it (`not_full_hold`). The minimum profit per unit (D14), the order of routes (D15) and the credits held back (D17, D24, D57) are unchanged.

### Docs – Changed (2026-10-04, slice 6.13)
- `PLAN.md`: slice 6.13 and decision D74, which amends D56. `docs/HOW_IT_WORKS.md`: a trip's units, saving up for a part hold, the trade executor's buy step and `not_full_hold`.

### Code – Added (2026-10-04, slice 2.15)
- A snapshot holds every market and shipyard the bot has cached, as last seen and with when, not only where a ship stands, as asked on 2026-10-04: "Can you expand the JSON export to include shipyard information, and can you make these jsons available through Grafana?", and "If the snapshot is taken and data is in memory but there's no ship at the shipyard right now, will the data from memory be in the snapshot or none at all?" (none at all, until now). A shipyard lists its ship types and, once one of our ships has been there, the ships for sale with price, supply, frame, reactor, engine, modules, mounts and crew. `GameStateSnapshots` builds it, moved out of `StartupSnapshotService`.
- A snapshot at every discovery, as asked the same day: "I'd like the snapshots to be made whenever a new discovery is made. So a shipyard with a new ship type or a market with a new good type, in addition to the times they are currently made." `DiscoverySnapshotService` looks every minute for a ship type or good the cache lists that no snapshot of the run held yet (D73: new to the run, not to a place), and takes one that says what was new and where (`Reason`, `Discoveries`), with a `Discovered` journal line. Retention is unchanged: the agent's first snapshot and the 10 newest.
- `startup_snapshots` gets `Reason` and `Discovered`, added to the cluster's table at the first start; `/status/startup-snapshots` lists them, and the download is named `startup-snapshot-…` or `discovery-snapshot-…`. The WebUI's Snapshots page shows why each was taken and what a discovery found.
- In Grafana (gembernodes, same branch): the Infinity data source reads the internal API with its key, for a snapshots dashboard with the list, a download of each as JSON and what the picked one holds.

### Code – Fixed (2026-10-04, B64)
- A shipyard answered without its ships for sale no longer wipes the cached listings, and with them the price a purchase reads (B28): the repository's one statement and startup sync leave a row that has them as it is, as B62 did for a market's prices.

### Docs – Changed (2026-10-04, slice 2.15)
- `PLAN.md`: slice 2.15, B64, D73, and what the WebUI shows that Grafana doesn't. `docs/HOW_IT_WORKS.md`: the snapshots and the discovery service, the shipyard cache, the `Discovered` journal line, the snapshot endpoints and the Snapshots page.

### Code – Added (2026-10-04, slice 2.14)
- Ships have names of the bot's own beside the game's symbols (D72), as asked on 2026-10-04: "Can we make custom names within the API which should be type-number … Bonus points if there's a list of relevant names for each of the types, one of which is picked per reset to call that type". Each type a shipyard sells has a list of names (`ShipNames.Lists`); each server reset picks one per type by its reset date, and the type's ships are numbered after it in the order they joined the fleet: in the run of 2026-10-04 the probes are MARINER-1, MARINER-2, …, the mining drones PICKAXE and the siphon drones HUMMINGBIRD, told apart by their mounts though the game calls both EXCAVATOR. A type with no list is named after its registration role (PATROL-1). The names aren't stored: they follow from the fleet and the reset date, so a restart keeps them.
- Where they show: `ShipName` on every log line about a ship (`ShipNameEnricher`), and the `ShipPurchased` line says the new ship's name; `spacetraders_ship_name_info{ship,name,type}`; `name` in the internal API's `/status/ships`, which the WebUI's fleet page shows in a column and searches, and the ship's page beside its symbol. The game's symbol stays what everything keys a ship by.

### Docs – Changed (2026-10-04, slice 2.14)
- `PLAN.md`: slice 2.14 and decision D72. `docs/HOW_IT_WORKS.md`: how ships are named and where the names show. `docs/GLOSSARY.md`: Ship Name. The `st-investigate` skill: a ship's name as its argument, and where to look it up.

### Code – Changed (2026-10-04, slice 6.12)
- A mining plan's trip keeps every other ore a market buys within one tank of the asteroid, and jettisons only the rest (D71), as asked on 2026-10-04: "only throw out minerals that they cannot sell within a single tank of fuel, instead of everything they're not specifically mining for". One tank is a full tank's CRUISE flight there without a refuelling stop (`MiningPlanner.IsSellableWithinOneTank`). The trip still extracts with the survey best for its own ore and sells that ore at its market; the mining plan sells the others after it, one good a trip, where each fetches most after fuel. As in the siphon plan, a full hold sells even where the sale doesn't pay for its fuel, and a full hold no market the miner can reach buys gets no trip. The contract's round trips keep only the contract's ore.

### Docs – Changed (2026-10-04, slice 6.12)
- `PLAN.md`: slice 6.12 and decision D71. `docs/HOW_IT_WORKS.md`: what a mining trip keeps, how the mining plan sells it, and that the contract's trips keep none.

### Code – Fixed (2026-10-04, B63)
- A drone whose trip ends between its plan's pass and the trading plan's, in a tick, no longer trades: the mining, siphon and construction plans record the free ships they had no work for (`PassedOverShips`), and the trading plan gives a ship of theirs a route only when its own plan, switched on, passed it over. On 2026-10-04 SPECTER-3's arrival sold its ore 0.3 s before tick 202's trading plan, which gave it a route although the mining plan would have given it a trip a tick later (D58).

### Docs – Changed (2026-10-04, B63)
- `PLAN.md`: B63. `docs/HOW_IT_WORKS.md`: what "had no trip for it" means for the trading plan.

### Code – Fixed (2026-10-04, B62)
- A market answered without prices no longer wipes the cached ones: the arrival stores a market through `MarketRefresher`, which leaves such an answer out, and startup sync keeps the cached prices. On 2026-10-04 an arrival fired at the pod's start stored X1-FJ91-C46 without prices; its fuel price gone, SPECTER-6 found no fuel stop it could reach and drifted 43 minutes after the API refused its flight with a 400.
- A market the cache holds without prices counts as never seen: the market watch fetches it as soon as a ship is there, and the probe plan sends a probe there first (`MarketFreshnessRecord.HasPrices`).
- A flight the fuel aboard can't pay for in the ship's flight mode isn't asked of the API (`FlightFuel`, from the cached positions); it goes straight to the fallback. The fallback logs a warning, for that and for the API's own refusal, so a run of them raises `RepeatingError`.

### Docs – Changed (2026-10-04, B62)
- `PLAN.md`: B62. `docs/HOW_IT_WORKS.md`: the arrival's market refresh, a market without prices, and the fuel check before a flight.

### Code – Added (2026-10-04, slice 2.13)
- Every `spacetraders_*` series carries `reset_date` and every log line `ResetDate`: the server reset the agent was registered under, such as `2026-10-04` (D70), as asked on 2026-10-04: "Can we key all the Grafana data off the agent ID (or something else that's different between resets) so data does not mix between different agents/different resets?" The bot registers the same symbol after every reset, so the reset date is what differs. `ResetDateLabel` reads it from the agent id; `PrometheusAutomationMetrics` defines every metric with the label and puts the value in front at every write (`RunGauge`, `ZeroFirstCounter`); `ResetDateEnricher` adds it to every log line once the agent is known. The next-reset gauge is set once the agent is known.

### Docs – Changed (2026-10-04, slice 2.13)
- `PLAN.md`: slice 2.13 and decision D70. `docs/HOW_IT_WORKS.md`: the label on every metric and the property on every log line.

### Code – Fixed (2026-10-04, B47)
- The scout plan's flights and the contract's, to the asteroid and to the delivery, go in CRUISE, through refuelling stops when the fuel aboard won't reach, as the trips' flights do since slice 6.10c. They asked for no flight mode and flew straight to their target, so a flight beyond one tank was left to the navigation's fallback, which drifts, and a ship it left in DRIFT flew on in DRIFT. After the reset of 2026-10-04 the scout plan left SPECTER-1 at J67, 747 from the contract's asteroid, EF5D, with a 400-unit tank: it drifted there in 87 minutes, where CRUISE through J66 and I65 takes about ten, and took the copper the 19 to H60 in DRIFT too; the first flight that switched it back was a survey trip at 18:09Z. A contract command flies one leg a tick, and at a stop, where the next tick finds the ship in orbit, it docks and refuels first. The fallback still drifts where no chain of fuel markets reaches, and the ship's next flight asks for CRUISE again.

### Docs – Changed (2026-10-04, B47)
- `PLAN.md`: B47 fixed. `docs/HOW_IT_WORKS.md`: how the contract's and the scout plan's flights go, and what is left to the fallback.

### Code – Fixed (2026-10-04, B61)
- When an arrival and the market watch store a market at the same moment, the later one updates the row the first one inserted: the market cache stores a row in one statement (`INSERT … ON CONFLICT … DO UPDATE`). Each looked for the row first, so for a market nobody had fetched before both inserted it, and the second insert failed on `PK_cached_markets` (23505): EF Core logged Errors, the watch a Warning, and the prices the watch fetched were lost. It happened twice on 2026-10-04, at X1-KR90-E18A at 12:46Z while exploring and at X1-FJ91-A2 at 13:07Z on the new agent's scout. The shipyard cache stores the same way now: two ships storing a shipyard never fetched at the same moment could collide there too.

### Docs – Changed (2026-10-04, B61)
- `PLAN.md`: B61. `docs/HOW_IT_WORKS.md`: an arrival and the market watch can fetch one market at the same moment; who writes the market and shipyard caches, and how.

### Code – Fixed (2026-10-04, B38)
- Startup recovery treats only a ship marked in transit as one. Startup sync stores the last route's arrival on every ship, and recovery took any ship whose arrival time had passed for one that had just arrived: it published a `ShipInTransitEvent`, which writes an "in transit" activity row, and logged that the ship "arrived at" its waypoint. On 2026-10-04 that happened to the new agent's two ships at 13:06:52Z, seconds after the reset registered them, and to all three ships at the restart of 18:09:17Z, though SPECTER-1 had been docked at H60 since 16:12Z. A docked or orbiting ship now gets its goal step and no event, as one without an arrival time did. The rows already written are pruned after 30 days.

### Docs – Changed (2026-10-04, B38)
- `PLAN.md`: B38 fixed. `docs/HOW_IT_WORKS.md`: what startup recovery does with a docked or orbiting ship.

### Docs – Changed (2026-10-04, phase 6 check)
- `PLAN.md`: where things stand after the server reset of 2026-10-04 (the new agent in X1-FJ91, its contract, its gate); slices 2.11, 2.12, 6.6 and 6.11 merged and deployed instead of "built on branch"; phase 6's checks on the run that ended (6.10b's and 6.10c's met, 6.11's first run); the gembernodes table's merged rows; 4.1's revoke done.

### Code – Fixed (2026-10-04, B59)
- The local request budget keeps to the API's rate limiter (B59, step 2), from the headers step 1 logged: 119 429s between 2026-10-03 19:51Z and 2026-10-04 13:00Z, all from the limiter's IP address count. Each window is 100 ms longer (`RequestBudget.JourneyMargin`), for a request's journey to the server: half the 429s came a few milliseconds early. A 429 from the limiter holds every request back until the reset it names (`RequestBudget.PauseUntil`), not only the one it refused: 68 of 118 came within 20 seconds of the one before. A new process starts with its burst spent (`RequestBudget.ForANewProcess`), as the server still counts what the process before it sent in the last minute: the 429s at 10:07:50Z on 2026-10-04 came during a start.

### Docs – Changed (2026-10-04, B59)
- `PLAN.md`: B59's row says what the headers showed and what step 2 changed. `docs/HOW_IT_WORKS.md`: the 429 handler's pause and the budget's margin and start.

### Code – Changed (2026-10-04, slice 2.12)
- Every plan is on by default (D69), as asked on 2026-10-04 after the server reset of 13:00Z registered a new agent with only the scout and contract plans on: "Can you ensure everything is on by default? And changes in config changes those defaults?" A setting nobody has set follows its default (`agent_settings."FollowsDefault"`): every start gives it the default as the running version has it, a `SettingChanged` journal line each. Of the settings stored before, the start that adds the column keeps one whose value isn't its default as set (a setting changed, or automation switched off, before the deploy) and lets the rest follow, with the plan switches that are off: so this build's first start switches on the plans the current agent has off. A setting set by you or by the bot keeps its value; the `Runtime.*` status flags never follow.

### Code – Added (2026-10-04, slice 2.12)
- Settings for the next run (D69), asked the same day: "a separate set of endpoints to only affect future runs. So I can have a setting for this run (e.g. 50% split between miners and traders) and change those settings for the next run to see if it gives an improvement." The next run is the agent the next server reset registers: it starts with the value chosen for each setting (`next_run_settings`, which belongs to no agent), else the default. `PUT /settings/{key}` and the kill switch set a setting now and for the next runs; the size guard's and the reset monitor's switch-offs don't carry over. `GET /settings/next-run` lists what the next run starts with, `PUT /settings/next-run/{key}` sets the next runs only, `DELETE /settings/next-run/{key}` gives them the default again; a key the seed doesn't hold or a status flag answers 404. `POST /settings/reset` also forgets the values chosen for the next runs. New journal kind `NextRunSettingChanged`; `spacetraders_setting_info` gets a `next_run` label, for a "next run" column in the dashboard's settings table (gembernodes, same branch). The cluster's database gets the new column and table at the first start (`SpaceTradersDatabaseInitializer.AddedSchema`, formerly `AddedColumns`).

### Docs – Changed (2026-10-04, slice 2.12)
- `PLAN.md`: slice 2.12 and decision D69. `docs/HOW_IT_WORKS.md`: how settings follow their defaults and carry over to the next run, the endpoints, the new table, column, journal kind and label. `docs/GLOSSARY.md`: next run, follows its default. `README.md`: the status.

### Code – Added (2026-10-04, slice 6.6)
- A construction plan and role for the home system's jump gate (slice 6.6, D64–D68), as asked on 2026-10-04: "Finishing this jump node should be top priority, as it opens up the rest of the game. Can you implement a special role that works on this jump gate?" Supplying a construction site pays nothing (the API answers with the site and the cargo, no credits), so a load of materials is judged like a ship purchase: it leaves the credit reserve and comes after the cargo ships in the order ships are bought in (`PurchaseTier.Construction`, 6; probes now 7, the turns 8), and a trip holds back its cargo from start to purchase, as a trade trip does (D64). The ship with the largest hold that isn't a drone or the surveyor builds (`FleetRole.Construct`, reason `construction`, setting `Construction.Ships`, 1), and trades while there is nothing it may buy (D65). A load is a full hold, or what the gate still needs, in one purchase at a market whose trade volume takes it at once (D67) and whose supply isn't SCARCE or LIMITED (D66), where it costs least with its fuel. Only the headquarters' system's gate is considered (D68). New switch `Automation.Plan.Construction.Enabled` (off); new journal kinds `ConstructionStarted`, `ConstructionSupplied` and `ConstructionDropped`; new ledger category `ConstructionBuy`; new metrics `spacetraders_construction_units_required` and `_fulfilled` for the dashboard's jump gate progress. The construction sites are fetched and stored (`cached_construction_sites`), a free ship holding what the gate needs supplies it first, the trading plan jettisons no such cargo, and `ShipLeftIdle` counts a load as work for its builder. The fleet table's and the shipyards table's "can do" list `Construct` for a ship with a hold and a tank that isn't a drone.

### Docs – Changed (2026-10-04, slice 6.6)
- `PLAN.md`: slice 6.6 and decisions D64–D68. `docs/HOW_IT_WORKS.md`: the construction plan, its role, trip, journal kinds, metrics, settings and purchase tier, and the goal kinds that run.

### Code – Added (2026-10-04, slice 2.11)
- The markets dashboard's shipyards table shows, for each ship for sale, its tank, its hold, what it could do in the fleet and its equipment, as asked on 2026-10-04: "For spacetraders, can we add some more information to the shipyard ships? I'd like to know fuel tank size, cargo size, and which special bits they have (e.g. mining laser)", then "Also which role they can fulfill within my fleet". From the shipyard listings it caches, the bot exports `spacetraders_shipyard_ship_fuel_capacity_units`, `spacetraders_shipyard_ship_cargo_capacity_units` and one series per ship type, `spacetraders_shipyard_ship_info{can,equipment}`: what it could do, judged as the fleet table's "can do" (`FleetRoles.PotentialRoles`; `Probe` for a probe), and its mounts and modules without the cargo holds and crew quarters. The table's new columns are in gembernodes, on the same branch.

### Code – Changed (2026-10-04, slice 2.11)
- `ShipyardShipDto` lists a ship for sale's mounts and modules; `ShipyardWaypointDto` lists its ship types and ships as read-only lists (two QW0012 warnings fewer).

### Docs – Changed (2026-10-04, slice 2.11)
- `PLAN.md`: slice 2.11; the gembernodes table's merged rows. `docs/HOW_IT_WORKS.md`: the new metrics.

### Code – Added (2026-10-04, slice 6.11)
- The explore plan (`ExplorePlanService`, off by default: `Automation.Plan.Explore.Enabled`), as asked on 2026-10-04: "if an active jump gate goes to a system that isn't explored yet, the COMMAND ship should go through that jump gate. If there are markets or shipyard there, the COMMAND ship should scout them, as it initially does for the home system, recursively." Once its trip ends (D61), the command ship jumps through built gates to the nearest system not explored yet, with no limit (D59), visits each market and shipyard there once (`ExploreSystemGoal`), and goes on; with nothing left it comes home, where the other plans give it work again (D60). A jump buys one ANTIMATTER at the gate's market (`JumpGoal`, booked as `AntimatterPurchase`) and goes only while the credits after it stay at or above `FleetExpansion.MinCreditReserve` (D63). Nothing is charted (D62). The plan learns the gates from the API one call a pass, and caches each new system's waypoints when the ship gets there. Journal kinds `Jumped` and `SystemExplored`.
- Eleven `spacetraders_system_*` gauges for a systems dashboard (gembernodes), asked the same day: "a systems grafana dashboard with a more wide view of which systems have been explored and what kind of mining, trading and shipyard opportunities it gives": each known system's state, jumps from home, connections, markets, shipyards, waypoints, what can be mined or siphoned there, the best price its markets pay for each ore and gas, and its best trades (`SystemOpportunities`).

### Code – Changed (2026-10-04, slice 6.11)
- Business stays home while the command ship explores (D60, `BusinessSystems`): the mining, siphon and trading plans buy ships, and the contract plan looks for its drone's shipyard, only in systems where a ship that doesn't explore is; the mining, siphon and survey plans plan no work in a system because the explorer is in it.
- A system the command ship has only explored keeps its markets' refresh times and its summary in the metrics, but not each good's price series, so Prometheus doesn't grow with every system explored.

### Code – Fixed (2026-10-04, B60)
- The jump call sends the destination gate's `waypointSymbol`, as API v2.3.0 asks; it sent the destination's `systemSymbol`. The port's jump-gate read keeps the connections as gates, so a jump can name one, and a jump the API refuses comes back as `JumpRefusedException`. Nothing jumped before slice 6.11.

### Docs – Changed (2026-10-04, slice 6.11)
- `PLAN.md`: slice 6.11, decisions D59–D63, B60; D57, D58 and slice 2.10 merged and deployed. `docs/HOW_IT_WORKS.md`: the explore plan, the jump and explore-system goals, business staying home, the journal kinds, the system metrics, the jump call. `docs/GLOSSARY.md`: jump gate, antimatter, explored system, business systems. The `st-investigate` skill: queries for exploring.

### Code – Added (2026-10-03, slice 2.10)
- The bot counts every request it initiates to the game API, once, as it starts: before the pause after a 502 and the local budget, and without the retries of a 429 (`spacetraders_api_requests_initiated_total{method,endpoint}`, from a new outermost handler, `ApiRequestInitiatedHandler`). It feeds the dashboard's new "API request rates" graph, asked on 2026-10-03: "For spacetraders, can we add a graph similar to this?" Next to the requests that went out, it shows requests waiting for the budget, and retries.

### Docs – Changed (2026-10-03, slice 2.10)
- `PLAN.md`: slice 2.10; `docs/HOW_IT_WORKS.md`: the new handler and metric; the `st-investigate` skill: a query for the request rates.

### Code – Changed (2026-10-03, D58)
- Drones gather first (D58), as asked on 2026-10-03: "Mining drones should be mining drones first, and traders second, and they should not leave gaps when trading in a way that results in endless drones being bought." The role board gives every mining drone the mining role and every siphon drone the siphon role (`gathers_first`), whatever trading would pay; a drone trades only when its plan has no trip for it. The board had moved drones between gathering and trading every 10 minutes, and the ores and gases they no longer gathered went short, so the coverage tier bought drones for them. The command ship still takes what pays it most.

### Docs – Changed (2026-10-03, D58)
- `PLAN.md`: D58 and its follow-up under 6.10c; D57 merged. `docs/HOW_IT_WORKS.md`: the role board's order, the reasons in `RoleChanged` and `spacetraders_ship_role_info`, and the board's say in drone purchases.

### Code – Added (2026-10-03, D57)
- Credits held back for a trade trip (D57), as asked on 2026-10-03: "Let's have these credits reserved as soon as a ship starts towards it, so that this cannot happen (waste of time and fuel)." A trip holds back what its cargo costs at the price it was chosen with (`TradeBetweenMarketsGoal.ReservedCredits`, `TripReservations`) from the moment it starts until its cargo is aboard. The trading plan gives other traders only the credits no trip holds back, the trip at its buy market spends its own and those no other trip holds back, and every ship purchase leaves them (`BudgetPolicy`, `spacetraders_credit_reserve`). A trader that sets off for the hold it saved up for (D56) saves up no more: the trip's hold takes its place. The goal store reads the fleet's trade trips at once (`GetActiveTradeGoalsAsync`). At 19:29Z SPECTER-8 had dropped its EQUIPMENT trip at K85 with nothing bought, because another trader spent the credits on the way.

### Docs – Changed (2026-10-03, D57)
- `PLAN.md`: D57 and its follow-up under 6.10c; B59's first step deployed. `docs/HOW_IT_WORKS.md`: credits held back for a trip, the saving that becomes the trip's hold, the credit reserve and its metric, the trade executor's buy step.

### Code – Changed (2026-10-03, B59)
- Every 429 warning carries the rate limiter's headers (`x-ratelimit-*` and `retry-after`, "none" without them), so the request budget can be held against what the server counted. From 10:30Z on 2026-10-03 the limiter answered 429 about four times an hour, each time while the budget was in full use; which window it counted wasn't logged.

### Docs – Changed (2026-10-03, B59)
- `PLAN.md`: B59; D56 and B58 deployed. `docs/HOW_IT_WORKS.md`: what a 429 warning holds.

### Code – Fixed (2026-10-03, B58)
- A surveyor takes a survey target only where it can get on, with the fuel left, to a market that sells fuel (`MiningPlanner.CanSurveyAt`), as a mining trip must get on to its market. A target it could reach only one way left it stranded: on 2026-10-03 SPECTER-F flew from B7 to B37 (68 of its 80 fuel) for gold, and the area rule drifted it back to B7 (32 minutes), where gold at B37 came round again, while B14, where the drones mine, got no survey. The survey plan's state lists only such surveyors for each target, so the `ShipLeftIdle` rule counts the same.

### Docs – Changed (2026-10-03, B58)
- `PLAN.md`: B58; D56 merged. `docs/HOW_IT_WORKS.md`: which targets a surveyor reaches.

### Code – Changed (2026-10-03, D56)
- Trades are full holds, in one purchase and one sale (D56), as asked on 2026-10-03: "So I'd suggest waiting for the market trade volume to be at max cargo capacity, and only then buy all of it at once. And especially mining drones can mine while this is not the case. The entire goal is to buy full holds in one go, because it makes no sense to buy more times than one." A route counts only when both markets' trade volumes are at least the ship's free hold and the credits, the trip's fuel kept back, pay for all of it. Each trade moves the price (a whole trade volume bought raised it 9% on 2026-10-03), so smaller loads, such as the drones' SHIP_PARTS 6 or 7 at a time, no longer count. At the buy market a trip whose markets no longer trade the full hold at once is dropped (`TradeDropped`, `Reason` `not_full_hold`).

### Code – Added (2026-10-03, D56)
- Saving up for a full hold (D56): "Full hold or nothing, when this occurs the credit floor should be temporarily expanded so any ship purchases wait for the full hold to be bought before new ships are bought." A free trader whose best route, credits aside, is a hold the credits don't pay for yet logs "saves up for a full hold … (D56)" and takes the best hold it can pay for meanwhile, or none; the credit reserve every ship purchase keeps (`BudgetPolicy`, `spacetraders_credit_reserve`) grows by the dearest such hold until it is bought (`FullHoldSavings`, in memory).

### Docs – Changed (2026-10-03, D56)
- `PLAN.md`: D56 and its follow-up under 6.10c; B57 deployed. `docs/HOW_IT_WORKS.md`: a trip's units, saving up for a full hold, the credit reserve, `not_full_hold`, and the credit-reserve metric.

### Code – Fixed (2026-10-03, B57)
- A sale's ledger row records the market it was sold to and its unit price, as a purchase's does. Every `TradeSell` row since the first, on 2026-10-02, had neither, though the sale event carried the market. The rows already written stay without them.

### Docs – Changed (2026-10-03, B57)
- `PLAN.md`: B57; B56 deployed. `docs/HOW_IT_WORKS.md`: what a ledger row of a sale holds.

### Code – Fixed (2026-10-03, B56)
- The survey ship's move to where most drones mine (D54) runs: `ShipGoalExecutorService` stepped only the goal types it listed, and the move wasn't one of them, so SPECTER-F, sent to B7 at 15:55:03Z, stayed at XB5C with a goal that never ended. A move belongs to the survey plan's switch, as its other goals do. The plan's "moves to" log line no longer renders its empty phrase as `""`.

### Docs – Changed (2026-10-03, B56)
- `PLAN.md`: B56; D54 and D55 deployed. `docs/HOW_IT_WORKS.md`: the goal kinds that are created and stepped.

### Code – Added (2026-10-03, D55)
- A survey ship per area with mining drones (D55), as asked on 2026-10-03: "Can we add that extra surveyor drones are bought to try and cover all areas with surveys? The second surveyor is lower priority than the first on the buy order." While a system has fewer ships that can only survey than areas with mining drones, the survey plan buys one more, in a new place in the order after the drones per scarce mineral and before the cargo ships (`SurveyorPerArea`; the cargo ships, probes and turns move one place down, and so does `position` in `spacetraders_purchase_need_credits`). Each area with drones gets a survey ship of its own: an area another one works in, or moves to, is taken, and of two in one area, one drifts to an area with drones that has none.

### Docs – Changed (2026-10-03, D55)
- `PLAN.md`: D55 and its follow-up under 6.10c. `docs/HOW_IT_WORKS.md`: a survey ship per area, the order ships are bought in, and the purchase-need metric's tiers.

### Code – Added (2026-10-03, D54)
- The survey ship works where most drones mine (D54), as asked on 2026-10-03: "Please add the option for the survey ship to get to the mining location without surveys." A ship that can only survey counts the mining drones by where they work (their trip's market, a drone drifting there included; between trips, where they are), and when an area out of its CRUISE reach has more of them than its own, it drifts there before it surveys again (`MoveToWaypointGoal` with `Drifting`, and its first executor); a tie keeps it where it is. On the cluster, four drones worked for B7, out of the survey ship's reach, against two in the middle, and B7's mined without surveys.

### Docs – Changed (2026-10-03, D54)
- `PLAN.md`: D54 and its follow-up under 6.10c; D53 deployed. `docs/HOW_IT_WORKS.md`: where the survey ship works, the move executor, `DriftStarted` for a move, and the fleet view's words.

### Code – Changed (2026-10-03, D53)
- Coverage per area (D53), as asked on 2026-10-03: "A drone covers a mineral only for the markets it can reach in CRUISE from where it works (the middle, or B7). The middle's scarce silicon gets a drone of its own; the coverage tier may buy a drone per scarce mineral per area (more drones)." A mining or siphon trip covers its mineral at the markets its ship reaches in CRUISE from the market it sells at, so a free drone takes a SCARCE or LIMITED mineral that no trip covers there, though a drone works on it for a far market (`CoveringTrip`). The coverage tier counts each such mineral once per area, the markets a drone flies between in CRUISE (`MiningPlanner.Areas`), and the role board keeps one drone per mineral and area. On the cluster, four of the five mining drones had drifted to B7 while the middle's silicon, SCARCE at H53, had none.

### Docs – Changed (2026-10-03, D53)
- `PLAN.md`: D53 and its follow-up under 6.10c, with what to expect; D52 deployed. `docs/HOW_IT_WORKS.md`: coverage per area in the mining and siphon plans, the role board and the order ships are bought in.

### Code – Changed (2026-10-03, D52)
- A ship that can only survey surveys on (D52), as asked on 2026-10-03: "A (single role) surveyor which is idle is allowed to keep surveying, starting with whichever ore is lowest." Once every ore it reaches has its stock of surveys (D27), it surveys the target it reaches with the fewest usable surveys, then the contract's, then the best paid, instead of waiting. The command ship, which can do more, still waits, or trades and mines in its spare time. `ShipLeftIdle` counts any target a ship that can only survey reaches as work for it.

### Docs – Changed (2026-10-03, D52)
- `PLAN.md`: D52, 6.10c's watch and follow-ups. `docs/HOW_IT_WORKS.md`: the survey plan's surveying on, and the survey work the idle rule counts.

### Code – Fixed (2026-10-03, B55)
- `ShipLeftIdle` counts a survey target as work only for the surveyors that can reach it, which the survey plan now lists per target (`CandidateShipSymbols`), as the mining, siphon and trading branches do. The designated surveyor, with an 80-unit tank in the middle of X1-DC53, waited by design while the targets that needed a survey were far out, for the command ship's mining, and the rule raised an anomaly on it.

### Docs – Changed (2026-10-03, B55)
- `PLAN.md`: B55. `docs/HOW_IT_WORKS.md`: the survey plan's state, and the survey work the idle rule counts.

### Code – Fixed (2026-10-03, B54)
- The survey plan surveys an asteroid for a market only where a miner could mine it for that market: a trip in CRUISE from where the miner is, to the asteroid and on to the market with the fuel left, as the mining plan reckons it. A drone still drifting to a far market (D45) counts once it is there. At the first drift on the cluster, the command ship left its trading to survey B37, which no drone can mine for B7 (136 there and back on an 80-unit tank), and would have surveyed B14 hours before the drone got there.

### Docs – Changed (2026-10-03, B54)
- `PLAN.md`: B54, and 6.10c merged and deployed. `docs/HOW_IT_WORKS.md`: where the survey plan surveys.

### Code – Added (2026-10-03, slice 6.10c: D45)
- Drones for minerals out of fuel range (D45), as asked on 2026-10-03: "I'd like a way to add mining/siphoning drones for the minerals outside of fuel range, e.g. by having a drone drift to the marketplace that buys the mineral first, then refueling and resuming normal behavior." A market out of a drone's CRUISE reach that sells fuel is a far target, gathered at the asteroid (or gas giant) nearest it within a CRUISE round trip of it; it ranks after every reachable target of its supply level (D28), and among the ores no drone works on (D48) after those in reach. Its trip (`Drifting`) drifts to the market first, 1 fuel whatever the distance and about ten times slower, logging `DriftStarted`; from there it mines or siphons in CRUISE. New and free drones both take far targets: an ore only a far market is short of counts for "a drone per scarce mineral" (D48), and the role board keeps a drone gathering it. The board values a far trip with its drift. The fleet view says `drifting to … to mine …`.
- A navigation can ask for a flight mode (`NavigateToWaypointCommand.FlightMode`, `FlightModeSubCommand`), set in orbit before it flies, with an API call only when the ship's mode differs: DRIFT for a drift, CRUISE for every flight of the mining, siphon, survey, spare-time and trade executors.

### Code – Changed (2026-10-03, slice 6.10c)
- A mining or siphon trip counts the fuel its ship has left where it fills its hold: it is offered only when the ship can carry the hold on to the market in CRUISE from there, as it can from XB5C, which sells fuel. The haul was planned with a full tank, so a drone at a far market could have been sent to an asteroid there and back beyond one tank, and would have drifted back (B47).

### Code – Fixed (2026-10-03, slice 6.10c, B47 in part)
- A ship left in DRIFT, by the navigation's fuel fallback or after a drift, flies its next mining, siphon, survey, spare-time or trade flight in CRUISE again; every one of its flights was ten times slower before. The fallback itself, and the scouting and contract flights, are unchanged.

### Docs – Changed (2026-10-03, slice 6.10c)
- `PLAN.md`: 6.10c built, with what was noticed; B47 partly fixed. `docs/HOW_IT_WORKS.md`: far targets and the drift, the flight mode a navigation asks for, `DriftStarted`, and the fleet view's words. The `st-investigate` skill: a drift takes hours, and isn't a stuck ship.

### Code – Added (2026-10-03, slice 6.10b: D43, D47, D48)
- The order ships are bought in (D43), as asked on 2026-10-03: "I'd like at least 1 drone per mineral that is scarce or limited, then save up for cargo ships, then a mix based on if the minerals aren't going above LIMITED", the mix being "Alternate drones and cargo ships, but probes first". Every plan that buys says on each pass what it would buy (`PurchaseNeed`), and buys only when nothing comes first (`IPurchaseOrder`): the contract's drone, a designated surveyor, a drone per scarce mineral, the cargo ships of `Trade.ShipPurchases` (saved up for), probes, then drones and cargo ships of the list's last type in turn. A need counts while its plan is on and can meet it; until every plan that is on has said what it needs lately (after a start, or a pause), nothing it could come before is bought. The turn goes to the kind not bought last, from the ledger and this process's purchases; a turn passes when the other kind has nothing to buy, and isn't made up later. Past the list, the trading plan buys one more of its last type at a time.
- A designated surveyor (D47): with the role board on, the survey plan buys a `SHIP_SURVEYOR` for each system with a mining drone and no ship that can only survey; the board gives it the survey role, which frees the command ship.
- One drone per scarce mineral (D48): the mining and siphon plans give a free drone a SCARCE or LIMITED ore or gas no drone works on first, the nearest first (journal reason `uncovered` when that came before D28's choice), and buy a drone, without asking the role board, while the system has fewer drones of the kind than such minerals a new drone could serve. The role board keeps one drone gathering per such mineral (reason `coverage`).
- Metrics: `spacetraders_purchase_need_credits{plan,tier,position,ship_type,shipyard}`, what each plan would buy, worth the ship's price; `spacetraders_credit_reserve`. The probe plan's state says `WaitingForAnotherPurchase` while something comes first.

### Code – Changed (2026-10-03, slice 6.10b, D51)
- The credit reserve every ship purchase keeps grows with what the ships that trade can carry: `FleetExpansion.MinCreditReserve` (now seeded at 60,000) plus `FleetExpansion.ReservePerTradingCargoUnit` (new, 1,000) a unit of hold on the cargo ships, the command ship and any ship the role board has trading. The command ship alone keeps 100,000, as before; a light shuttle makes it 140,000, a light hauler 220,000, a second 300,000. A stored `FleetExpansion.MinCreditReserve` keeps its value: set it to 60,000 after the deploy.

### Docs – Changed (2026-10-03, slice 6.10b)
- `PLAN.md`: D51; 6.10b built, with what to do after the deploy and what was noticed. `docs/HOW_IT_WORKS.md`: the order ships are bought in, the surveyor, coverage, the credit reserve, the new setting and metrics.

### Code – Added (2026-10-03, slice 6.10a)
- What each ship can do, whatever the plan switches: `spacetraders_ship_capabilities_info{ship,can}` (`Survey, Mine, Siphon, Trade`, or `none`), from its equipment. Mining and siphon drones are both cached as EXCAVATOR, the game's registration role; this tells them apart. The metrics sample maps each cached ship once (`ShipRepository.MapToModel`, now public).
- Units sold to and bought from each market, per good (`spacetraders_goods_sold_units_total`, `spacetraders_goods_bought_units_total`, labels `system`, `waypoint`, `good`), whoever traded them (D50): what we sell into a market can be set against what it makes. `ShipCargoSoldEvent` carries the market it was sold to.

- What each trip made after fuel (D46): a trade, mining, siphon or spare-time trip books its sales − purchases − fuel when it ends, whichever way it ends (sold, stopped early, interrupted by a survey or a trade, or blocked by the circuit breaker), with a `TripEnded` journal line and the counters `spacetraders_trips_total`, `spacetraders_trip_profit_credits_total` and `spacetraders_trip_loss_credits_total` by `activity`. The trip goals keep what they earned and spent (`TripGoal`); fuel is read from the ledger (`TripBook`). A contract's deposit and payout count as its profit, and each delivery's round-trip fuel as its loss.

### Code – Changed (2026-10-03, slice 6.10a, D49)
- The role board's production-chain share counts at most what a trip earns on a unit (`ChainValues.PerUnitAtMost`): a mined or siphoned unit's price at its market, a traded unit's margin, so feeding a factory at most doubles a trip. A siphon trip values its own gas at the market it sells it to, and each other gas it keeps where it counts most. On the first day the board valued a siphon drone at ~274,000 credits an hour that earned 7–10k, mining drones at 80–95k that earned 1–3k, and the command ship's trades at up to 4.7M an hour that earned ~13k: D39's second step counted a price difference such as PLASTICS → EQUIPMENT for every unit of gas. The setting's description says so.

### Docs – Changed (2026-10-03, slice 6.10)
- `PLAN.md`: slice 6.10 and your decisions D43–D50; 6.10a built, 6.10b and 6.10c planned with design notes; "Where things stand" brought up to date. `docs/HOW_IT_WORKS.md`: the new metrics and events.

### Code – Fixed (2026-10-03, B53)
- A flight logs two lines at Information: one when it leaves (`NavigateSubCommand`, now saying where from) and one when it lands (`ShipNavigationCompletedHandler`, with what its goal did next, or that it had none), besides its refuel. The ten or so steps between, each saying the ship had left or arrived, log at Debug. They were about 80% of the bot's lines, and with twelve ships the log budget would have been passed on normal running. Replaying the day's logs without them gives about 2,300 lines a ship a day instead of 6,000.

### Docs – Changed (2026-10-03, B53)
- `PLAN.md`: B53; `docs/HOW_IT_WORKS.md`: what a flight logs; the `st-investigate` skill: a flight is two lines.

### Code – Fixed (2026-10-03, B52)
- The credits, the database size and the next server reset no longer read 0 before the bot knows them. prometheus-net published every gauge without labels at 0 from the start, and Prometheus's first scrape of a new pod could come before the first sample: after a deploy the dashboard read 0 credits for a minute, which "Value gained per hour" showed as a loss of the whole fleet's value (−517,672 on 2026-10-03 at 07:28:30Z), and an hour later as the same gain. These gauges are listed from the start and have a series once they are set, so that minute is a gap now.

### Docs – Changed (2026-10-03, B52)
- `PLAN.md`: B52; `docs/HOW_IT_WORKS.md`: gauges without labels have no series until they are set.

### Code – Added (2026-10-02, slice 6.9: D38–D42)
- The role board, as asked on 2026-10-02: "Each ship should have a set of potential roles. … A ship should occasionally consider whether it's role is still the best thing it can do. This is not only based on it's own potential roles but also of other ships." A new plan (`Automation.Plan.Roles.Enabled`, off by default, bootstrapped after the scout plan and before the others) gives every ship a role, survey, mine, siphon or trade, from what it carries and what the other ships can do, and the plans give work by those roles. Off, the fixed rules of D20 and D34 hold as before.
- Surveys come first (D38): a ship that can only survey surveys, and where none can, the ship that can survey with the least to lose does, while another ship there can mine. While the contract wants ore, every other ship that can mine mines for it (D40, D23 kept). The rest share the work for the most credits per hour across the fleet (an assignment, the Hungarian method), no two on one trade route or mining or siphon opening; a ship's current role counts 20% more (`Roles.HeadStartPercent`, D41).
- Each role is valued per hour by the trips its plan would offer the ship: a trade route's profit after fuel; a mining or siphon trip's full hold at the ship's own rate (the yields and cooldowns its extractions showed, else the other ships', else 3 units every 70 seconds), less fuel; each with the production chains' share (D39, `Roles.ChainValueSharePercent`, 50): a good sold to a market that makes something pricier from it adds a share of the price difference, and that share again of the next step (iron ore → iron → machinery), fully at SCARCE and not at ABUNDANT. Flights take what the API reckons for CRUISE at the ship's engine speed, plus 10 seconds a landing.
- The board weighs the roles at every start, every `Roles.ReconsiderMinutes` (10), and at once when a ship joins, a plan is switched, the contract starts or stops wanting ore, or a ship with a choice of roles has had no work for a minute (D41). A new role takes effect when the ship's trip ends. Journal kind `RoleChanged`; the state (`plan_states`, `Roles`) lists each ship's role, why, what each role would earn it per hour, and the rates it used; metrics `spacetraders_ship_role_info{ship,role,reason}` and `spacetraders_ship_role_credits_per_hour{ship,role}`, exported while the board is on (switched off, the plans no longer read its roles).
- With the board on, the mining and siphon plans buy a drone only when the board would give it their role: a drone that would earn more trading would trade, and the plan would buy the next.
- Cargo nothing will sell or use (D42, asked the same day): a free trader that holds goods none of which pays for its sale after fuel jettisons them before it takes a route, but the contract's ore on a ship that mines for the contract; a surveyor with nothing to survey and no spare-time trip sells its hold where that pays, and jettisons the rest; a spare-time ship with a full hold no market it can reach buys jettisons it. Journal kind `CargoJettisoned` (`Reason` `no_buyer` or `not_worth_the_fuel`), counted in `spacetraders_jettisoned_units_total`. A jettison the API refuses waits 10 minutes before it is tried again.
- New settings, seeded: `Automation.Plan.Roles.Enabled` (false), `Roles.ReconsiderMinutes` (10), `Roles.HeadStartPercent` (20), `Roles.ChainValueSharePercent` (50).

### Code – Changed (2026-10-02, slice 6.9)
- The plans ask one place which ships they may give work (`FleetRoleBoard`), the board's roles or the fixed rules, instead of each asking `FleetRoles` with its own switches. The contract plan reads the plan states for it.
- The extraction, spare-time extraction and siphon commands record each yield and cooldown (`GatheringRates`, in memory).
- The probe plan reads an engine's speed through `FleetRoles.EngineSpeed`, which the board shares.

### Docs – Changed (2026-10-02, slice 6.9)
- `PLAN.md`: slice 6.9 and D38–D42 (D20 and D34 amended while the board is on; D36 amended by D42 for a hold nothing buys); `docs/HOW_IT_WORKS.md`: the role board, its settings, journal kinds and metrics, and what changes for every plan and the idle rule; `docs/GLOSSARY.md`: role, role board, chain value, jettison.

### Code – Added (2026-10-02, slice 2.9)
- The bot exports its settings for the dashboard's new settings table: one series per setting, with its value and what it does (`spacetraders_setting_info{setting,current,description}`, every 10 seconds). A value that may hold a secret shows `(hidden)`, as in `SettingChanged`; what a setting does is the running version's description, not the one stored when the agent was seeded.

### Docs – Changed (2026-10-02, slice 2.9)
- `PLAN.md`: slice 2.9, and 6.8 marked merged; `docs/HOW_IT_WORKS.md`: the new metric, and the settings table under "How settings work"; the `st-investigate` skill: a query for the settings.

### Code – Added (2026-10-02, slice 6.8: D34–D37)
- Spare time, as asked on 2026-10-02: "I'd like my command ship not to be idle." A new spare-time plan (`Automation.Plan.SpareTime.Enabled`, off by default, bootstrapped last) gives the command ship, when it has nothing to survey and no trade, one trip at a time (`GatherAndSellGoal`): it mines or siphons at the nearest asteroid or gas giant it can work that yields something a market buys (D35), without surveys, keeping whatever sells, until its hold is full; then it sells each good where it fetches most after fuel (D36), and the plans choose again.
- A survey that needs taking takes the ship off a trip that fills its hold at once, with the hold aboard (D37). With the spare-time plan on, the command ship trades when it has nothing to survey (D34, amending D20): the trading plan takes it, free or off a trip that fills its hold, for a route that waits for it once its hold is sold, after the other traders; it sells its hold first. A trip is only taken over when no goal step of the ship runs and the ship isn't in flight (B46, B17).
- New command `ExtractResourcesCommand` (one extraction without a survey, keeping every good a reachable market buys), journal kinds `GatheringStarted` and `GatheringInterrupted`. Spare-time yields count in `spacetraders_extracted_units_total`, not as extractions in the survey statistics. `ShipLeftIdle` counts spare-time work; the fleet view says "mining in its spare time", "siphoning in its spare time" or "selling …".

### Code – Changed (2026-10-02, slice 6.8)
- The trading plan sells held cargo by `TradeRoutePlanner.TryFindBestCargoSale`, now shared with the spare-time trip, and the siphon command keeps its gases by `MiningPlanner.IsSellableFrom`, now shared with the spare-time extraction. Neither changes what they do.
- `FleetStatusQueryService` reads the time from `TimeProvider` (warning S6354).

### Docs – Changed (2026-10-02, slice 6.8)
- `PLAN.md`: slice 6.8 and D34–D37 (D20 amended while the spare-time plan is on), and 6.3 and 6.7 marked merged; `docs/HOW_IT_WORKS.md`: the spare-time plan, its goal, command, journal kinds, metrics, setting and idle rule, and what changes for the survey and trading plans; `docs/GLOSSARY.md`: gather, spare time.

### Code – Added (2026-10-02, slice 6.7: D31–D33)
- Siphoning, as asked on 2026-10-02: "Functions practically the same as miners, including surveys, but for gassy materials." A new siphon plan (`Automation.Plan.Siphon.Enabled`, off by default) gives every siphoner (a ship with a gas siphon, a hold and a tank, and nothing to mine or survey with: a siphon drone) one trip at a time, by the miners' rules (D28): the market shortest of a gas first, siphoned at the gas giant nearest it and sold there. Surveys can't be used: the API's siphon call takes none, and a surveyor finds ores only.
- A trip keeps every gas it siphons that a market it can reach buys (D33), not only its own, and the plan sells the others after it, one good a trip, where each fetches most; the rest is jettisoned. A full hold only sells, so a siphoner can't take a trip that ends at once on every tick.
- The plan buys a `SHIP_SIPHON_DRONE` by the miners' rule (D32): one a tick, only when its first trip would serve a market short of a gas, within the credit reserve, up to `Siphon.MaxDrones` (new, 10). Gas contracts stay unsupported (D2, D31).
- New goal `SiphonAndSell`, command `SiphonResourcesCommand`, journal kinds `SiphonStarted` and `Siphoned`. Siphoned units count in `spacetraders_extracted_units_total`, so the dashboard's mined panels show gases, but not as extractions in the survey statistics. `ShipLeftIdle` counts a gas opening as work for the siphoners that can reach it; the fleet view says "siphoning for …".

### Docs – Changed (2026-10-02, slice 6.7)
- `PLAN.md`: slice 6.7 and D31–D33; `docs/HOW_IT_WORKS.md`: the siphon plan, its goal and command, the settings, the journal and metrics, and the idle rule; `docs/GLOSSARY.md`: siphon, siphoner, gas giant.

### Code – Changed (2026-10-02, slice 6.3: D29, D30)
- The probe plan works towards a probe at every market of the headquarters' system: while there are fewer probes than markets it buys a SHIP_PROBE where probes cost least, as long as the purchase leaves the credit reserve (`FleetExpansion.MinCreditReserve`, 100,000); its own 200,000 gate (D4) is gone. Until there are enough, the probes roam: each free probe flies, in CRUISE, to a market whose prices are older than `Market.RefreshMinutes` and that no probe is at or flying to, the oldest once twice the flight there counts against it.
- A purchase at a shipyard where none of our ships is makes no API call, which could only fail: it calls for a ship, the probe plan sends its nearest free probe, and the next attempt buys (D30). With a ship there, the shipyard's price is fetched again first, so the reserve is kept with the price it asks now. New journal kind `ProbeCalled`.
- The fleet view calls a probe's work "scouting", "called to a shipyard" or "watching its market", instead of "deploying" and "idle".

### Code – Fixed (2026-10-02, slice 6.3)
- The starting probe, cached with its role `SATELLITE`, is a probe (B25); a probe in flight counts, and keeps its market, so no second probe is bought or sent for it (B15).

### Code – Removed (2026-10-02, slice 6.3)
- `DeployProbeCommand` and the probe plan's credits handler, which only the old probe plan used.

### Docs – Changed (2026-10-02, slice 6.3)
- `PLAN.md`: slice 6.3, D29, D30, B15 and B25; `docs/HOW_IT_WORKS.md`: the probe plan, purchases, the idle rule and the journal.

### Code – Changed (2026-10-02, D28)
- Miners serve the markets that buy an ore by supply, shortest first: SCARCE, LIMITED, and once none is short, the lowest supply there is, even when it pays less; within a supply level, surveyed ores first. A trip for a market that isn't short logs reason `lowest_supply`.
- A drone is bought only when its first trip, by the miners' own ranking, would serve a market in low supply (SCARCE or LIMITED), and one a tick. It used to buy one for every low-supply opening at once, and a drone bought for a scarce market could then mine surveyed ore for a market that wasn't short, leaving the opening to pay for the next drone.

### Docs – Changed (2026-10-02, D28)
- `PLAN.md`: D28 and the fourth 6.4 follow-up; `docs/HOW_IT_WORKS.md`: the mining plan's ranking and its drone purchases.

### Code – Changed (2026-10-02, D27 refined)
- Survey targets are per ore and asteroid: every market that buys an ore gets the reachable asteroid nearest it, not only the market that pays most, so surveys lie close to wherever the ore is sold; each keeps its own stock of usable surveys.

### Code – Changed (2026-10-02, D27)
- The survey plan keeps a stock of usable surveys per ore instead of surveying the contract's ore without end: `Survey.StockPerOre` (new, 2). The contract's ore comes first only while it has fewer; then the ore with the fewest usable surveys, then the best paid. With the stock for every ore, the surveyor waits until one runs out, and `ShipLeftIdle` doesn't count that as idle.

### Docs – Changed (2026-10-02, D27)
- `PLAN.md`: D27, and the third 6.4 follow-up; `docs/HOW_IT_WORKS.md`: the survey stock and its setting.

### Code – Fixed (2026-10-02, B51)
- Extracting with a survey works: the survey's expiry goes back to the API as the API wrote it (`…Z`), not with an offset (`+00:00`), which the API answered with 422 "invalid payload" on every extraction.
- A survey the API can't read (422 without a game error code) is dropped with reason `rejected`, and the warning carries the API's response body; it was tried again on every step, five calls a tick with Wolverine's retries.

### Docs – Changed (2026-10-02, B51)
- `PLAN.md`: B51, and the second 6.4 follow-up with a note on Wolverine's retries; `docs/HOW_IT_WORKS.md`: the rejected survey.

### Code – Changed (2026-10-02, after slice 6.4's switch-on)
- A contract assignment lasts one round trip (D26, asked on 2026-10-02): the delivery closes it, after the fulfil call when one was due, and the plans assign the ship again on the next tick, in their order, so work that matters more comes first. A ship whose fulfil call fails keeps its assignment and makes the call again. The plan's first ship joins like every other free miner; it no longer gets its assignment back on every tick, or takes it back from other work.
- A restart reconsiders the contract's ships once (D26): with automation and the contract plan on, startup recovery releases every ship on the contract that isn't in flight, and the first tick assigns it again. A ship in flight keeps its assignment until its delivery.

### Code – Fixed (2026-10-02, after slice 6.4's switch-on)
- The command ship surveys once the survey plan is on, after its current contract trip or at the next restart (B50): it had joined the contract before the survey switch, and a contract assignment lasted until the contract was fulfilled.

### Docs – Changed (2026-10-02, after slice 6.4's switch-on)
- `PLAN.md`: 6.4 merged (projects#122, gembernodes#21 and #22) and switched on; B50 and D26; the follow-up's summary. `docs/HOW_IT_WORKS.md`: contract trips, the restart release.

### Code – Added (2026-10-02, slice 6.4)
- Surveying, as asked on 2026-10-02 (`PLAN.md` 6.4, decisions D20–D25). A new survey plan (`Automation.Plan.Survey.Enabled`, off by default) has every ship that can survey survey, and only that (D20): the contract's ore at the contract's asteroid first, otherwise ores the system's markets buy, at the asteroid nearest the market that pays most for them among those the miners can reach; ores without a usable survey first. One survey per goal; the goal ends after each.
- What an asteroid yields, from its traits (`AsteroidDeposits`), and which survey to extract with: the one where the ore makes up the largest share of the deposits, then the larger deposit, then the later expiry. Extractions use it, for the contract's miners too.
- The mining plan, rewritten: one trip per goal (mine until the hold is full, sell, choose again). A miner sells ore it holds first; otherwise it mines a surveyed ore first, then an ore in low supply (SCARCE or LIMITED, D22) at the asteroid nearest the market that is short of it, and sells it there. Trips fly with their goal through refuelling stops, never DRIFT. Drones are bought only for openings a drone could reach, and not while the contract takes the miners.
- Every free miner joins the active contract (D23); completion releases them all, and ore left over is sold by the mining plan.
- The trading plan buys its own cargo ships (D21, replacing D16): `Trade.ShipPurchases` (new; a light shuttle, then up to two light haulers), when every trader has a trip, a new ship would have a lucrative route, and the purchase keeps the credit reserve. With the survey plan on, a ship that can survey doesn't trade (D20); miners trade only when no mining work waits.
- `Trade.FuelReserveCredits` (new, 5,000): cargo purchases leave it untouched, so only fuel is bought below it (D24).
- After each purchase or sale, by a trader or a miner, the market is fetched again while the ship is still docked (D25, `MarketRefresher`, shared with the market watch).
- For a survey dashboard: journal kinds `Surveyed`, `SurveyEnded` (with how many extractions used the survey), `Extracted` and `MiningStarted`; metrics `spacetraders_surveys_taken_total`, `spacetraders_surveys_ended_total{reason,used}`, `spacetraders_surveys_active{used}` and `spacetraders_extractions_total{surveyed}`. `cached_surveys` gained `Extractions`; the initializer adds the column to an existing table.

### Code – Fixed (2026-10-02, slice 6.4)
- Waypoint traits are stored (B34): startup sync stores each waypoint's traits and modifiers, and fetches a system's waypoints again once when a cached one has none, so the cluster's waypoints get theirs at the next start.
- Every asteroid type can be mined (B48): `ASTEROID`, the type of 56 of X1-DC53's 57 asteroids, was refused.
- A survey the API refuses (exhausted, expired, not verified) is dropped instead of tried again on every step (B49).
- Survey goals end after their survey, and a miner no longer hands out survey goals or waits for one (B16's survey part); mining and survey trips navigate with their goal, so their arrivals wake them (B17 for those trips).
- A ship that delivers after another has fulfilled the contract no longer calls fulfil again.

### Code – Removed (2026-10-02, slice 6.4)
- The mining plan's event `Handle` methods (never wired, see `DiValidationTests`), its stale-goal cleanup, and `IShipGoalRepository.GetActiveMineAndSellTargetsAsync` and `GetActiveSurveyTargetsAsync`, which only the old mining code used.

### Docs – Changed (2026-10-02, slice 6.4)
- `PLAN.md`: slice 6.4 built, with its summary; D20–D25; B48 and B49 found and fixed; B16, B17 and B34 updated; 6.5 merged; 4.1's revoke done. `docs/HOW_IT_WORKS.md` describes the survey plan, the mining plan, the contract's miners, the cargo ship purchases, the fuel reserve, the refetch after trades, the new journal kinds, metrics and settings.

### Code – Added (2026-10-02, slice 6.5)
- Trading, as asked on 2026-10-02 (`PLAN.md` 6.5, decisions D14–D18). Any ship with a cargo hold and a fuel tank that has nothing else to do trades; after scouting that is the command ship. A trip's profit is what the sell market pays minus what the buy market charges, times the units, minus the fuel for the whole trip, the flight to the buy market included; it must earn `Trade.MinProfitPerUnit` per unit (D14), and routes whose sell market makes a pricier good from the cargo come first (D15). Two traders never share a route (D18), and the plan buys no ships (D16). Flights beyond one tank refuel at markets on the way instead of drifting. A trip checks its prices again where it lands: at the buy market it buys only while the trip is still lucrative (`TradeDropped` otherwise), and at the sell market it takes the cargo to a market that pays more, once (`TradeRerouted`). Sales above a market's trade volume go in several. Journal: `TradeStarted`, `TradeRerouted`, `TradeDropped`.
- The market watch: every market with one of our ships at its waypoint is fetched again once `Market.RefreshMinutes` (new, 5; 0 = off) have passed since its prices were last seen; one market a tick, the most overdue, as the tick's last step (D19).
- Writes before reads for the rate limit (D19): a GET gives way while a POST waits for the budget, leaves the last 10 of the 30-request burst to writes, and stops giving way after 10 seconds. `spacetraders_api_rate_limit_wait_seconds_total` has a `kind` label, `read` or `write`.
- The game's production chains are fetched once per process and shared by the trading plan and the markets dashboard (`SupplyChainCache`).

### Code – Fixed (2026-10-02, slice 6.5)
- One goal step at a time per ship (B46): the tick and an arrival could both step a ship as it docks, and a trade step would have bought twice. A step that finds its ship busy is skipped; the next tick takes it.

### Code – Removed (2026-10-02, slice 6.5)
- The old trading opportunities (a good scarce at one market and abundant at another, prices unread), the hauler purchases that went with them, and `IShipGoalRepository.GetActiveTradeRouteTargetsAsync`, which only they used.

### Docs – Changed (2026-10-02, slice 6.5)
- `PLAN.md`: slice 6.5 built, with its summary; D14–D19; B46 fixed and B47 (the navigation's fuel fallback leaves a ship in DRIFT) found. `docs/HOW_IT_WORKS.md` describes the market watch, the trading plan and the trip, and the one-step-per-ship guard; `docs/GLOSSARY.md` adds lucrative, market watch, trader and trade trip.

### Tools – Added (2026-10-02)
- `tools/investigate/st.py` (slice 5.1): reads the bot's data on the cluster from your PC, read-only, for the `st-investigate` skill: `check` (the pods, Prometheus, Loki and the database in turn), `prom`, `logs` (with `--group`, warnings and errors per statement) and `sql` (as `spacetraders_ro`, through psql or the `postgres:17` image, with the password from psql's password file). It starts and stops its own port-forwards and masks tokens and passwords. See its README.

### Tools – Added (2026-10-02, slice 5.2)
- `.claude/settings.json`: Claude may run `tools/investigate/st.py` and `kubectl -n spacetraders logs` without asking, and nothing else (your go-ahead of 2026-10-02).

### Docs – Changed (2026-10-02, phase 5)
- `.claude/skills/st-investigate/SKILL.md` is finished (slice 5.1) and was checked against the bot's first run on the cluster: it explained B42's anomaly and found B45. It reads every source through `tools/investigate/st.py`, leaves the internal API out (its key also unlocks settings and control), and records what the run taught. `PLAN.md`: phase 5 done, with a short summary; 4.1 done but for the revoke on `stored_credentials`; the gembernodes PRs of 2.7, 2.8 and B44 named. `README.md` reports phase 5.

### Code – Added (2026-10-02)
- Metrics for a markets dashboard per system (slice 2.8): every minute the cached markets (per good: prices, trade volume, supply, activity) and shipyards (ship types, prices, supply), with when each was last refreshed (`spacetraders_market_*`, `spacetraders_shipyard_*`, `PrometheusMarketMetricsService`); and once per start the game's production chains from `GET market/supply-chain`, one series per good with what it is made from and what is made from it (`spacetraders_good_supply_chain`).
- Metrics for the dashboard's fleet table (slice 2.7): where each ship is (the waypoint and its type, or `→` and where it goes), what the bot has it do (`mining COPPER_ORE`, `scouting`, `idle`…), when it arrives, and its hold per good (`spacetraders_ship_info`, `spacetraders_ship_arrival_timestamp_seconds`, `spacetraders_ship_cargo_units`, `spacetraders_ship_cargo_capacity_units`); and what drones extract and jettison, per ship and good (`spacetraders_extracted_units_total`, `spacetraders_jettisoned_units_total`).

### Code – Fixed (2026-10-02)
- The scout plan no longer skips a stop when a tick coincides with an arrival (B45). Both can run the ship's goal step as it docks; now only a visit of the plan's current stop moves the plan on, and the tick's resume check reads the assignment before the plan (advancing writes the plan first), so an advance under way no longer looks like a missing assignment. On the cluster the plan reported all 26 stops visited while the last, X1-DC53-J58, never was.
- A handler's first message no longer logs a warning for each dependency it resolves from the container (B42): Wolverine allows service location without one (`ServiceLocationPolicy.AlwaysAllowed`). On the cluster these warnings, all with one template, raised the `RepeatingError` anomaly in the bot's second minute, and their text ("…this is an error") tripped gembernodes' error-log alert.
- Every counter series reaches Prometheus at 0 before it counts (B43, `ZeroFirstCounter`): what a new series counts waits until a scrape has exported that 0, at most two scrape intervals. Prometheus's `increase()` and `rate()` never count the value a series has when it is first scraped, so the contract's deposit and the first drone, booked before the pod's first scrape, were missing from the ledger panels, and a single 429, failed call or breaker trip could never show.

### Docs – Changed (2026-10-02)
- `PLAN.md`: phase 4 merged and the first-run watch (4.3) started; B42–B44 found in its first minutes. `README.md` reports the bot back on the cluster, and `docs/HOW_IT_WORKS.md` says when Wolverine compiles a handler and how counters reach Prometheus.

### Docs – Added (2026-10-01)
- `PLAN.md`: the plan for getting the bot safely back on the cluster, with the known issues found by reading the code (B1–B25) and the decisions taken (D1–D11).
- `docs/HOW_IT_WORKS.md`: what the code does today, written from the code.
- `docs/archive/README.md`: an index of the archived documents.
- `.claude/skills/st-investigate/SKILL.md`: draft of the skill Claude uses to investigate and fix misbehaviour.

### Docs – Changed (2026-10-01)
- `PLAN.md`: B41, and the manifests are in gembernodes PR #11.
- `PLAN.md`: phase 4 ready to merge. The manifests (4.2) and the unpaused "bot is down" alert (4.3) are on a gembernodes branch, with a README for the steps by hand; the database logins (4.1) are prepared and tested against PostgreSQL 18; and B39 and B40 were found and fixed on the way. `README.md`, `spacetraders.md` and `docs/HOW_IT_WORKS.md` describe the deployment, and the `st-investigate` skill names the read-only login and the LAN address of the internal API.
- `PLAN.md`: phase 3 done (slices 3.1 and 3.2), with a short summary; D12 decided (the credit-drop alert is gone) and D13 added (the idle-ship and credits rules count only while work waits); the dashboard and alerts (2.4, 2.5) merged as gembernodes PR #10, with the Grafana restart still to do. `docs/HOW_IT_WORKS.md` has a section on the health rules (12), `docs/GLOSSARY.md` describes anomalies and health rules as they are, and the `st-investigate` skill says where each rule's anomaly points (and that production logs carry `@m` and `@i`, not `@mt`). `README.md` reports phase 3; `spacetraders.md` says where `/metrics` is served since slice 2.1.
- `PLAN.md`: phase 2 done in the code (slices 2.1, 2.2, 2.3, 2.6, and 0.5), with a short summary; the dashboard and alerts (2.4, 2.5) ready on a gembernodes branch; new known issues B37 (the credit-drop alert can't fire) and B38 (startup recovery reports docked ships as in transit), open decision D12, and notes for 4.2 (metrics port annotations, the Traefik migration) and 4.3 (unpause the "bot is down" alert).
- `PLAN.md`: the soak test's results (slice 1.14, the last of phase 1), the issues it found (B28–B36), a slice for its tidy-ups (0.5), and a short summary of phase 1.
- Moved the old plans, designs, progress logs, strategy notes and the `docs/implementation/` and `docs/operations/` documents to `docs/archive/`.
- `README.md`, `CONTRIBUTING.md`, `docs/GLOSSARY.md` and `spacetraders.md` now match the code: no `SpaceTraders.App`, no `k8s/` folder, no `/control/sync` or `/control/ships/{symbol}/reassign`, and the burst limit as the API guide defines it.
- `CLAUDE.md`: what Claude works on in this project, and that it considers fixing build warnings in the files it touches.

### Config – Changed (2026-10-01)
- The WebUI's nginx no longer logs requests to `/healthz`, which only Kubernetes' probes call (B40): 8 lines a minute, about 11,500 a day, which Grafana's log-volume panel and alert counted against the bot's budget of 50,000.
- `Metrics:Port` (9090) and `Metrics:Hostname` in `appsettings.json`: where `/metrics` is served. Development listens on `localhost` only; `Dockerfile.api` exposes 9090 next to 8080.
- `System.Net.Http` logs at Warning instead of Information (`appsettings.json`, `appsettings.Development.json`), removing about four log lines per outbound API call.

### Tools – Changed (2026-10-01)
- `tools/soak/` reads `/metrics` from the metrics port (`--metrics-url`, default `http://127.0.0.1:9090/metrics`) and counts API calls with `spacetraders_api_requests_total`; `launch.py` keeps the metrics on loopback.

### Tools – Added (2026-10-01)
- `tools/soak/`: runs the bot like the cluster against a local database, and samples every table, `/metrics` and the log every 15 minutes (slice 1.14). See its README.

### Build – Changed (2026-10-01)
- The Docker integration tests run on Windows without `DOCKER_HOST` (B36): they ask Testcontainers whether it can reach Docker, which finds Docker Desktop's named pipe, instead of looking for `/var/run/docker.sock` or `DOCKER_HOST` themselves. Three of the four test classes skipped silently there.
- Deleted the unused `packages.lock.json` in the project root (it belonged to `.net.csproj`, which no solution includes), and the UTF-8 BOM in three test `.csproj` files (Proj3000).

### Packages – Changed (2026-10-01)
- Every NuGet package is on its latest version, except `Microsoft.CodeAnalysis.CSharp` and `.Common` (kept at 5.0.0): they set the oldest compiler that can load the ST0001 analyzer, and 5.9 would switch it off on any SDK before 10.0.4xx. This clears both NuGet audit warnings (`Microsoft.OpenApi` through Swashbuckle, `SSH.NET` through Testcontainers).
- Majors: WolverineFx 6, NSubstitute 6, `xunit.runner.visualstudio` 4, `xunit.analyzers` 2. Wolverine 6 no longer compiles handler code without `WolverineFx.RuntimeCompilation` (now referenced, and registered with `UseRuntimeCompilation()` because discovery is `ManualOnly`), and refuses service location by default. Every handler that reaches the database needs it, because EF Core registers the DbContext's options through a factory: the host now resolves the DbContext from the scope (`AlwaysUseServiceLocationFor<SpaceTradersDbContext>()`), and `RestoreV5Defaults()` turns any other case into a warning instead of a failing handler. Found by running the published build; the unit tests replace those registrations.
- The newer analyzers bring new rules: about 220 more warnings, almost all style (Qowaiv's strongly typed identifiers and date literals, Sonar's redundant `!`, project-file rules), plus two licences to accept (`Proj0503`: FluentAssertions, SonarAnalyzer).

### Code – Added (2026-10-01)
- Health rules (phase 3): every minute `HealthMonitorService` checks the bot's own state against ten rules: the contract it works on (`ContractStalled`, `ContractLeftOpen`, `ContractDeadlineAtRisk`), its ships (`ShipStuck`, `ShipLeftIdle`, `CircuitBreakerTripped`), and `RepeatingError`, `CreditsUnchanged`, `ApiUnauthorized` and `ApiThrottled`. A subject that breaks one is an anomaly: `spacetraders_anomaly_active{rule,subject}`, and the journal's `AnomalyRaised` (with `Details`) and `AnomalyCleared`. Their thresholds are eight new `Health.*` settings. `docs/HOW_IT_WORKS.md` section 12 lists them.
- The journal (slice 2.3): one log line per meaningful thing with an `EventKind` property, so `{namespace="spacetraders"} | json | EventKind != ""` reads as a timeline: contracts accepted, delivered and fulfilled; ships and cargo bought and sold; plans started, completed and blocked with a reason; ships idle or blocked with a reason; every setting change (old and new value, secrets hidden); resets, API pauses, and anomalies raised and cleared. `JournalEvents` names them all.
- Metrics for the dashboard (slice 2.1): credits, credits earned and spent by ledger category, ships by role and state, each ship's state with its goal and blocked reason, contract units and deadline, API requests by route template and status, 429s by source, time waited for the request budget, handled messages by type, goal steps by kind, active anomalies (the database size limits for now) and the next server reset. `docs/HOW_IT_WORKS.md` section 11 lists them.
- Database size guard (D8): every 5 minutes the bot reads its database size and exports it as `spacetraders_db_size_bytes`. Above `Database.SoftLimitMegabytes` (1024) it logs a warning; above `Database.HardLimitMegabytes` (3072) it switches automation off.
- Every table has a retention policy, or the reason it can't grow without bound, in `DataRetention`; a test fails for a table without one (B3). New: `startup_snapshots` keeps the agent's first snapshot and the last 10, `runs` and `run_credit_highlights` keep 365 days, `ship_goal_history` and completed `fleet_goals` keep 30 days.
- Earlier agents are cleaned up: at startup, agent bootstrap deletes the rows of every agent but the active one, except their `runs` (B3). Each server reset left the previous agent's rows behind for good.
- One switch per plan: `Automation.Plan.{Scout,Contract,ProbeDeployment,Mining,Trading}.Enabled`. Scout and Contract are on by default, the other three off (D9). A plan that is off isn't bootstrapped, buys nothing, and its ships' goals wait.
- Per-ship circuit breaker: a ship that takes more than `Automation.CircuitBreaker.MaxGoalStepsPerMinute` goal steps in a minute (default 60) gets its goal blocked as `runaway`, with a warning and the `spacetraders_goal_breaker_trips_total` metric. Blocked goals are not stepped again.

### Code – Removed (2026-10-01)
- The credit-drop alert (D12, B37): credits only drop when the bot spends them, so it could only have reported the bot's own spending. It couldn't fire either: it compared each change with a field that Wolverine never kept.

### Code – Changed (2026-10-01)
- The host logs through its own Serilog logger instead of the process-wide `Log.Logger` (B41). Every host replaced the static logger when it started and closed it when it stopped, so tests running hosts side by side could log into another host's sinks, or none: CI failed on `main` after #114, in a health-rule scenario that had passed on the PR. One host per process in production, so nothing changes there.
- `/health/startup` answers 503 until the startup chain has completed (B39). It answered 200 while the chain ran, because the check reports that as Degraded, so the Kubernetes startup probe on it would have passed at once.
- Journal lines render their kind without quotes (`{EventKind:l}`): Serilog quotes string values in the rendered message, so a line read `"ShipIdle": ship …` in Loki.
- The settings seed holds only settings that code which runs reads (B18, D10): 26 are gone (`Navigation.*`, `Maintenance.*`, `Outfitting.*`, and the others nothing read), 30 remain, `Runtime.*` flags included. A database seeded earlier keeps its rows; the bot ignores them, as it always did.
- The size guard logs its limits as anomalies, `AnomalyRaised` and `AnomalyCleared` with rule `DbSizeSoftLimit` or `DbSizeHardLimit`, instead of `DbSizeSoftLimit`, `DbSizeHardLimit` and `DbSizeNormal` events.
- Rows are keyed on a short agent id instead of the agent token (B4): the agent's symbol and the server's reset date, such as `GEMBER@2026-09-27`. The token, a JWT of about 1 KB, is stored only in `stored_credentials`. **A database from before this change has to be dropped**: the schema initializer no longer upgrades old schemas, and the unused `scout_plan_states` table is gone.

### Code – Fixed (2026-10-01)
- Sales, cargo and ship purchases, and contract payments reach the ledger, the credit samples and the metrics (B7): each is published where it happens (`ShipCargoSoldEvent`, `CargoPurchasedEvent`, `NewShipPurchasedEvent`, `ContractAcceptedEvent`, `ContractFulfilledEvent`), and every credit change publishes `AgentCreditsChangedEvent`. Before, the ledger only held fuel purchases. The probe plan's credits handler now checks the automation and plan switches, since waking it can buy probes.
- Market refreshes record price history (B19): the trade goods are read case-insensitively, as they are stored.
- Prometheus can scrape `/metrics` (B11): it has a port of its own, `Metrics:Port` (9090), that needs no API key and that the Service and ingress don't route. The main port no longer serves it. The credits gauge shows the cached credits; it waited for an event nothing published (B7).
- The startup snapshot reads what startup sync has just cached instead of fetching it all again (B35, about 11 API calls on every start). It now also holds the ships' goals and the contracts, and it no longer switches `Automation.Enabled` off and back on around itself.
- Contract payments reach the cached credits (B33, found in the soak test): the accept and fulfil responses carry the agent's new credits, but nothing stored them, so purchases were budgeted from too little until the next restart (6,620 credits short in the soak test).
- `cached_ships` no longer grows without end (B32, found in the soak test): it is created with `fillfactor=50` and `autovacuum_vacuum_threshold=10`. Its few wide rows change every minute or so; pruning kept the dead-tuple count under the autovacuum trigger, so VACUUM never ran and updates that didn't fit their page kept extending the table (about 250 kB an hour with one busy ship). A database created before this keeps the old table: `ALTER TABLE cached_ships SET (fillfactor = 50, autovacuum_vacuum_threshold = 10)`.
- A restart keeps the contract's terms (B31, found in the soak test): startup sync stored contracts without their deadline and deliverables, so the contract plan couldn't read its deliverable until the next delivery.
- The contract plan completes once its contract is fulfilled, and releases the ship (B9): it waited for two events that nothing publishes, so after fulfilment the tick went on sending the ship to mine and deliver for a finished contract. The tick now advances the plan from the cached contract.
- A contract ship delivers whole trips (B8): what the contract still needs, at most a full hold. The tick sent it off as soon as one unit was aboard, so it shuttled one to three units a trip. A delivery also sends no more than the contract still needs (B30).
- The command ship is free after scouting (B10): the scout plan clears the ship's last goal when it completes. Before, every tick ran that finished goal again, rewriting a waypoint row each time.
- Startup sync caches a shipyard with its prices (B28, found in the soak test). It stored the priced ships where the ship types belong and left the prices empty, so a purchase at a shipyard where a ship sat at startup failed with "price unknown" until a ship arrived there again. Every restart did the same to each shipyard with a ship parked at it.
- Wolverine logs a handled message at Debug instead of Information (B29, found in the soak test). It logs that line under the message type's name, so the `Wolverine` level override didn't reach it: one line per message.
- A contract plan waiting for budget no longer calls the API on every tick (B27). Each 5 s retry fetched every contract (`GET my/contracts`, 12 calls a minute) and saved the plan again; a retry now tries only the ship again, and stores nothing while the plan keeps waiting.
- Log diet (B12): an idle bot no longer logs at Information on every tick. What a tick finds when nothing changed goes to Debug (the command ship after scouting wrote two lines every 5 s, a contract plan waiting for budget four). Production logs JSON with the rendered message (`RenderedCompactJsonFormatter`); ships, contracts and waypoints are always `ShipSymbol`, `ContractId` and `WaypointSymbol`; and every line logged during a tick carries `Tick` and its step's `Plan`, `ShipSymbol` or `ContractId`.
- Pruning starts right after database initialisation and prunes each table on its own (B3): it started only after every other startup step had succeeded, and one failing table stopped the tables after it. `DataRetentionService` has taken over from `ActivityLogPruningService`.
- Downsampling price and credit samples fits the 30 s command timeout: the `NOT IN` delete didn't finish within 3 minutes on 2.6 million samples; ranking the rows once takes 5 seconds.
- A newly registered agent gets its default settings right away (B26). They were stored under the previous agent, so after every server reset all settings read as missing, and automation stayed off until the pod restarted.
- The schema is created on a database that already holds other tables (B21): `EnsureCreated` skipped the app's tables as soon as any table existed, such as Wolverine's.
- Wolverine no longer stores messages in Postgres (B2): with its durability agent off it never deleted a handled message, so every published message stayed in the database. Messages now stay in memory; `WolverineFx.EntityFrameworkCore` and `WolverineFx.Postgresql` are no longer referenced.
- API limits and errors follow the API guide (B13): 2 requests per second plus a burst of 30 per 60 seconds, in a budget that no longer resets; a 429 waits for the rate limiter's reset or backs off exponentially; a 502 pauses all API calls for `Api.BadGatewayPauseMinutes` (default 3) instead of retrying after 1, 2 and 4 seconds. `spacetraders_api_throttled_total` now counts 429 responses only.
- A server reset during a run is noticed (B6): the first call that fails with the reset error switches automation off, logs `ResetDetected` and stops the host, so the restart registers a new agent.
- A restart no longer clears every ship's active goal (B20): startup sync updates the ship's game state and leaves its goal alone.
- One failing step no longer stops the whole tick (B14): each plan bootstrap, ship goal step and contract assignment runs in its own scope and try/catch.
- A failed startup chain stops the host and exits with code 1, so Kubernetes restarts the pod instead of leaving an idle one that looks healthy (B23).
- `Automation.Enabled` now stops the bot (B5): the tick runs no plans, goal steps or contract work, and no goal step runs from an arrival, the probe handler or startup recovery either.
- A ship that was already at its goal's target no longer loops (B1, the likely cause of the full database in May). In orbit at the target, the scout, probe and mining executors dock instead of navigating to where they are; docked at the target, the survey executor orbits. Navigating to the waypoint a ship is already at does nothing and no longer publishes `ShipNavigationCompletedEvent`.

### Docs – Changed
- Removed obsolete plan-related Markdown files.
- Renamed cleaned SpaceTraders.io reference content to `spacetraders.md`.
- Updated README, contribution guidance, glossary, and implementation docs to match the current code.
- Added `docs/SPACE_TRADERS_IMPLEMENTATION_PLAN.md` to map SpaceTraders.io reference capabilities to current implementation gaps and milestones.

---

## [0.2.0] – 2026-01-01 – Automation foundation

### Code – Added
- Typed `SpaceTradersApiClient` with implemented public/authenticated endpoint subset
- `SpaceTradersApiOptions`, DI registration, `SpaceTradersApiException`
- Startup services in API host:
  - `AgentBootstrapService`
  - `StartupSyncService`
- Persistence entities and `SpaceTradersDbContext` for cached agent/ships/contracts and stored credentials
- Razor Pages dashboard overview page showing cached agent + ships

### Docs – Added
- `docs/GLOSSARY.md`
- `CONTRIBUTING.md`
- `CHANGELOG.md`

### Docs – Changed
- `README.md` aligned with currently implemented foundation scope
- `CONTRIBUTING.md` aligned with current repository state

---

## [0.1.0] – 2025-01-01 – Infrastructure foundation

### Added
- Solution with six projects: Domain, Application, Infrastructure.SpaceTradersAPI, Infrastructure.Persistence, API, App
- Initial project structure

---

[Unreleased]: https://github.com/Gemberkoekje/projects/compare/v0.1.0...HEAD
[0.2.0]: https://github.com/Gemberkoekje/projects/releases/tag/v0.2.0
[0.1.0]: https://github.com/Gemberkoekje/projects/releases/tag/v0.1.0
