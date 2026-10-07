# SpaceTraders — Plan history

> Moved out of `PLAN.md` on 2026-10-06 (asked: "Please clean up the plans"): the dated log of where things stood, the
> fixed bugs with their evidence, every phase's and slice's details (what was asked, found, done and tested), and the
> changes made in gembernodes. `PLAN.md` keeps the goal, the open bugs, every decision, the list of slices and the slices
> still to build. The text
> is as it stood in `PLAN.md`, so "below", "above" and "the table" in it point within that file as it was.

## Where things stood, 2026-10-01 to 2026-10-07

- The bot was taken off the cluster on 2026-05-26 (gembernodes `906fd99`), and its manifests were
  removed on 2026-09-26 (`3f9f785`). The 1Password item `spacetraders-secrets` still exists.
- The old database is gone, so the next run starts empty. That makes this the cheapest moment
  for schema changes: there is nothing to migrate.
- Nothing can confirm what filled Postgres in May: the data is lost, and Loki only started
  working on 2026-09-26, so no logs from any earlier run exist. The suspects in the table below
  come from reading the code.
- **Cluster facts that shape this plan (from gembernodes):**
  - **Postgres** is one Bitnami instance shared by every app: `postgresql.flux-system`, LAN
    address 192.168.1.232.
    - Its 32Gi volume sits on the QNAP NFS share that every PVC uses (Loki, Prometheus, Grafana,
      the backups, the file server).
    - The NFS CSI driver most likely doesn't enforce the 32Gi, so a runaway table can fill the
      whole share unless the QNAP share has a quota. Slice 1.6 exists for this reason.
  - **Logs:** Promtail ships every container log to Loki with namespace, pod, container and app
    labels. Loki keeps 31 days on a 10Gi volume.
  - **Metrics:** Prometheus (chart 25.x) scrapes pods annotated `prometheus.io/scrape`. The old
    deployment had no such annotation, and `/metrics` needs the API key anyway (B11), so the
    bot's metrics were never collected.
  - **Grafana** runs at http://192.168.1.230/grafana (LAN only). Its datasources are Prometheus
    and Loki. Dashboards and alert rules are provisioned from
    `gembernodes/infrastructure/monitoring/`, and alerts go out by email.
- `SpaceTradersV3/` was deleted on 2026-10-01 (D7). It was an unbuilt copy of this project's API
  client and interfaces; git history still has it.
- Phase 1 is done (2026-10-01): the soak test (1.14) ran the bot for four hours against the live
  API from an empty local database.
- Phase 2 is done in the code (2026-10-01): metrics, the ledger and the journal. Its Grafana
  dashboard and alerts (2.4, 2.5) were merged in gembernodes (PR #10) and applied by Flux on
  2026-10-01, and Grafana has read the alert rules since its restart on 2026-10-02.
- Phase 3 is done in the code (2026-10-01): ten health rules check the bot's own state every
  minute, and each broken one is an anomaly (a metric and journal lines).
- Phase 4 is merged (2026-10-02): gembernodes PR #11 put the bot back on the cluster at 08:50Z, at
  main `3373ca7` (with B39–B41), and it registered a new agent. Grafana restarted at 09:09Z, so its
  alerts, "bot is down" included, are live. The first-run watch (4.3) is under way; its first
  minutes found B42–B44.
- Phase 5 is done (2026-10-02): the `st-investigate` skill is finished and was checked against the
  first run (5.1), where it explained B42's anomaly and found B45; the helper and the bot's pod logs
  run without asking (5.2).
- Slice 6.5 (trading) was merged as projects#121 on 2026-10-02 (deployed by gembernodes#20), with
  your decisions D14–D19; it found B46 (fixed with it) and B47 (open). The trading plan stays off
  until you switch it on (D9).
- Slice 6.4 (surveying and mining, and cargo ships for trading) was merged as projects#122 on
  2026-10-02 (its dashboard section as gembernodes#21), with your decisions D20–D25, and deployed by
  gembernodes#22 at 14:13Z. It fixed B34 first, at your request, then the survey part of B16, B17
  for the mining and survey trips, B48 and B49. Trading was switched on at 13:08Z, surveying and
  mining at 14:14Z. Its first watch found B50, fixed with your decision D26 (projects#123, deployed
  by gembernodes#23 at 14:47Z). That deploy's first surveys found B51, fixed by projects#125
  (deployed by gembernodes#25 at 15:08Z). Your decision D27, a stock of surveys per ore instead of
  surveying the contract's ore without end, is live (projects#126 and #127, deployed by gembernodes#26
  and #27). The contract was fulfilled at 15:49Z (23,076 credits). Your decision D28, one rule for
  what miners mine and when a drone is bought, was merged as projects#128.
- Slice 6.3 (probes) was merged as projects#129 on 2026-10-02 (deployed by gembernodes#28), with your
  decisions D29 (a probe for every market, bought while the credits stay at 100,000; until then they
  roam) and D30 (a purchase where none of our ships is fetches a probe first). It fixes B15 and B25. The
  probe plan stays off until you switch it on (D9).
- Slice 6.7 (siphoning) was merged as projects#130 on 2026-10-02 (deployed by gembernodes#29), with your
  decisions D31–D33: siphon drones siphon gases at gas giants by the miners' rules, without surveys (the
  API's siphon call takes none). The siphon plan stays off until you switch it on (D9).
- Slice 6.8 (spare time) was merged as projects#131 on 2026-10-02 (deployed by gembernodes#30), with your
  decisions D34–D37, asked that day: "I'd like my command ship not to be idle." With nothing to survey the
  command ship trades, and with no trade either it mines or siphons whatever sells at the nearest place it
  can, and sells it; a survey or a trade interrupts that. You switched on the siphon, probe and spare-time
  plans at 21:40–21:42Z; the first minutes' journal matched the plan.
- Slice 2.9 (a settings table on the dashboard) was merged as projects#132 and deployed by gembernodes#31 on
  2026-10-02: which settings exist, and which are on.
- Slice 6.9 (roles by what pays most, and cargo nothing will sell) was merged as projects#133 with your decisions
  D38–D42, and deployed by gembernodes#32 on 2026-10-03 at 07:27Z; the role board is on.
- The health check of 2026-10-03, after the bot's first day on the cluster, found B52 (projects#134, deployed by
  gembernodes#33) and B53 (projects#135). gembernodes#34 makes the log budget a number per ship and stays open until
  slice 6.10a is merged, so it deploys both.
- Slice 6.10 (the fleet's shape, asked on 2026-10-03, with your decisions D43–D51) is split in three: 6.10a (visibility and
  the role board's rates) is merged and deployed (projects#136, gembernodes#34); 6.10b (the order ships are bought in, a designated
  surveyor, one drone per scarce mineral, and a credit reserve that grows with the trading holds, D51) is merged and deployed
  (projects#137, its turn fix projects#138, gembernodes#35, #36 and #37); 6.10c (drones drifting to minerals out of fuel range)
  is merged and deployed (projects#139, gembernodes#37, 12:13Z on 2026-10-03). Its first drift (SPECTER-4 to B7, 12:35Z)
  found B54 (projects#140, gembernodes#38), and the designated surveyor's first wait found B55 (projects#141, gembernodes#39)
  and your decision D52, a ship that can only survey surveys on (projects#142, gembernodes#40: the cluster runs `6c9f8cc`
  since 14:00Z). Your decision D53, coverage per area, is merged and deployed (projects#143, gembernodes#41: the cluster runs
  `489720e` since 14:40Z; its first coverage drone, SPECTER-11, mines the middle's silicon). Your decision D54, the survey
  ship works where most drones mine, and D55, a survey ship per area with drones, are merged and deployed (projects#144,
  gembernodes#42). Their first move found B56: the move never ran (projects#145, gembernodes#43: the cluster runs
  `827785b` since 16:07Z). B57, sales booked without their market and unit price, is merged and deployed (projects#146,
  gembernodes#44: the cluster runs `2cfc071` since 17:53Z). Your decision D56, trade only full holds, in one purchase and
  one sale, and keep ship purchases back while a trader saves up for one, is merged and deployed (projects#147,
  gembernodes#45: the cluster runs `ef1cdb7` since 19:00Z), and so is B58, a survey ship flying to an asteroid it can't
  get away from (projects#148, gembernodes#46: the cluster runs `e17a765` since 19:03Z; SPECTER-F surveys B14 since
  19:17Z). B59, 429s from the API's rate limiter while the bot keeps to its budget: its first step, logging the
  limiter's headers with each 429, is merged and deployed (projects#149, gembernodes#47: the cluster runs `5ef42bd`
  since 19:51Z). Your decision D57, credits held back for a trade trip from the moment it starts until it buys, is merged
  and deployed (projects#150, gembernodes#48), and so is your decision D58, drones gather first (projects#151,
  gembernodes#49).
- Slice 2.10 (the API's request rates on the dashboard, and the graphs' legends as tables, asked on 2026-10-03) is merged
  and deployed (projects#152, gembernodes#50: the cluster runs `df677b9`).
- Slice 6.11 (exploring through the jump gates, and a systems dashboard, asked on 2026-10-04, with your decisions
  D59–D63) is merged and deployed (projects#153, its dashboard gembernodes#51, deployed by gembernodes#52). It found and
  fixed B60. You switched the explore plan on at 11:54Z: the command ship explored X1-KR90 and had jumped on to X1-CV66
  when the reset ended the run.
- Slice 2.11 (each ship for sale's tank, hold, what it could do in the fleet and its equipment, on the markets dashboard's
  shipyards table, asked on 2026-10-04) is merged and deployed (projects#154, gembernodes#52).
- Slice 6.6 (the jump gate, asked on 2026-10-04, with your decisions D64–D68) is merged and deployed (projects#155, its
  dashboard gembernodes#53, deployed by gembernodes#52). Supplying a construction site pays nothing. X1-DC53's own gate was
  complete before our ships came; the new home system's gate is not (below).
- The server reset on 2026-10-04 at 13:00Z. The bot found it at 13:00:33Z, restarted until the API was back (it answered
  503, "The universe is being reset", for about six minutes), and registered a new agent at 13:06Z, SPECTER again, in
  X1-FJ91. The scout plan visited all 28 of its markets (so B45 is fixed), and the contract was fulfilled at 16:12Z. Its
  jump gate, X1-FJ91-I64, needs 1,600 FAB_MATS and 400 ADVANCED_CIRCUITRY, which the construction plan (6.6) now works on;
  until it is built the explore plan has no gate to jump through.
- Slice 2.12 (asked on 2026-10-04, after that reset left the new agent with only the scout and contract plans on, with your
  decision D69) is merged and deployed (projects#156, gembernodes#55: the cluster runs `52a73fd` since 18:09Z): every plan
  is on by default, the settings you set carry over to the next run (the agent the next reset registers), and
  `/settings/next-run` sets the next run alone. Its first start switched on, at 18:09Z, the plans the agent of 13:00Z had
  had off. With it, "stays off until you switch it on (D9)" in the bullets above no longer holds.
- Slice 2.13 (Grafana data per server reset, asked on 2026-10-04, with your decision D70) is merged and deployed
  (projects#162, its dashboards gembernodes#56, deployed by gembernodes#57): every metric and log line carries the reset
  date, and the three SpaceTraders dashboards show one reset's run at a time.
- Slices 6.12 (mining drones keep the ores they can sell within one tank, D71) and 2.14 (the bot's own ship names beside
  the game's symbols, D72), both asked on 2026-10-04, are merged and deployed (projects#165, gembernodes#60).
- Slice 2.15 (snapshots of every cached shipyard and market, taken at every discovery too, and in Grafana, asked on
  2026-10-04, with your decision D73) is merged and deployed (projects#166, gembernodes#62). It found and fixed B64.
- Slice 6.13 (a trade trip may carry less than a full hold where the seller's supply is ABUNDANT, asked on 2026-10-04,
  with your decision D74) is merged and deployed (projects#167, gembernodes#63), and so are the market tree's trade
  volumes, asked just before it (gembernodes#61), with D74 in the panel's description.
- Slice 2.16 (each ship's profit, asked on 2026-10-04) is merged and deployed (projects#169, gembernodes#64): each ship's
  ledger by category as a metric, and a "Profit by ship" table on the SpaceTraders dashboard.
- Slice 2.17 (the trading plan's order of routes under the market tree, asked on 2026-10-04, with your decision D75) is
  merged and deployed (projects#168, gembernodes#65).
- Slice 2.18 (why the other goods with a price gap aren't traded, in that list, asked on 2026-10-05, with your decision
  D76) is built on branch `ccr-f3fba810-ie3vm6` in projects and gembernodes.
- Slice 6.14 (drones mine and siphon until every mineral is ABUNDANT, sharing a pair rather than trading, asked on 2026-10-05, with your
  decisions D77 and D78; the cargo ships' purchases stay as they are) is built on branch `claude/eager-allen-5q9biz`.
- Slice 6.15 (the jump gate's loads bought in batches of the market's trade volume, asked on 2026-10-05, with your decision
  D81) is merged and deployed (projects#172, gembernodes#68). The gate had had no load since 10-04 18:09Z; its first load
  since, 80 ADVANCED_CIRCUITRY, was supplied at 08:54Z.
- Slice 6.16 (trade trips in batches, sized with measured price steps and checked live, with one buyer of a good at a market
  at a time, asked on 2026-10-05, with your decisions D79 and D80) is merged and deployed (projects#173, gembernodes#68,
  image `6b1481d`, live since 2026-10-05 08:24Z). Still to do from D79: learning the price steps per good and market from
  the bot's own trades.
- Slice 6.17 (end products last: the routes of goods something is made from come first, wherever they are sold, asked on
  2026-10-05, with your decision D82) and B65 are merged and deployed (projects#175, #174, gembernodes#69, image
  `0da3f31`, live since 2026-10-05 09:56Z).
- Slice 6.18 (drones parked at a far asteroid hand their ore to a light shuttle that sells it, asked on 2026-10-05, with
  your decision D83) is merged and deployed (projects#176, gembernodes#70, image `788799b`, live since 2026-10-05
  10:52Z).
- Slice 6.19 (the fastest way out of CRUISE reach, cruising as far as it can and drifting the rest, and BURN wherever the
  fuel allows it, asked on 2026-10-05, with your decision D84) is merged and deployed (projects#177, gembernodes#72, image
  `5d31082`, live since 2026-10-05 12:47Z).
- Slice 6.20 (end products rank at half their profit, and a collection shuttle trades until a drone is parked at its
  asteroid, asked on 2026-10-05, with your decisions D85 and D86) and B66 are merged and deployed (projects#178, #179,
  gembernodes#74, image `5daf615`, live since 2026-10-05 13:30Z).
- Slice 6.21 (the role board's trade estimates count only what the trading plan could give, capped at what trading
  earned lately, and cargo ships beyond the list only while routes wait for one; B67, with your decisions D87 and D88)
  is merged and deployed (projects#180, gembernodes#75, image `0d60234`, live since 2026-10-05 14:25Z).
- B68 (the collection point's survey ship drifted back to the market after every survey) is merged and deployed
  (projects#181, gembernodes#76, image `89731ac`, live since 2026-10-05 15:52Z).
- Slice 6.22 (trade routes that feed the jump gate's materials come first, asked on 2026-10-05, with your decision D89)
  is merged and deployed (projects#182, gembernodes#77, image `30e43cf`, live since 2026-10-05 16:12Z).
- Slice 6.23 (a trade route that feeds the jump gate runs while its goods sell for what they cost, asked on 2026-10-05,
  with your decision D90) is merged and deployed (projects#183, gembernodes#78, image `9cf8226`, live since 2026-10-05
  16:46Z).
- Slice 6.24 (ore and gases go to the markets that make something from them, and EXCHANGE markets are wealth trades only,
  asked on 2026-10-05, with your decision D91) is merged and deployed (projects#184, gembernodes#79, image `78d238f`).
- Slice 6.25 (the jump gate's miners: a mining drone per ore every half hour for the smelters that make the gate's metals,
  dedicated to its ore, until each smelter has its ore at HIGH; D91's phase 2, asked on 2026-10-05, with your decision D92)
  is merged and deployed (projects#185, gembernodes#80, image `f2068e5`, live since 2026-10-05 21:31Z).
- The home gate, X1-FJ91-I64, was complete at 2026-10-06 03:17Z, when its last FAB_MATS were supplied. The explore
  plan (6.11) saw it built at 04:12Z, on its hourly look, and took the command ship out at once: by 07:23Z SPECTER-1
  had jumped seven times (5,024 to 5,560 antimatter a jump), explored X1-HN44, X1-NF46, X1-AD37, X1-GT9, X1-TA92 and
  X1-AA31, and was scouting X1-PX46.
- B69 (with a probe for every market, seven spare probes sat at the shipyard where they were bought while the others
  hopped between neighbouring markets) is built on branch `claude/spacetraders-idle-probes`.
- B70 (an arrival whose dock failed for good, in a network outage, left SPECTER-5 stored in transit for hours, every
  step of its goal waiting for it) is built on branch `claude/spacetraders-b70-lost-arrival`.
- B71 (a trip that ended while a pass read the goals left its ship looking free with the cargo it had just sold or
  supplied: the builder SPECTER-D was traded away from a waiting gate load) is built on branch
  `claude/spacetraders-b71-builder-snapshot`.
- Slice 6.26 (every ship that can build builds the jump gate, `Construction.Ships` 0 for no limit, the underway count
  keeping them off each other's units; asked on 2026-10-06, with your decision D93) is built on branch
  `claude/spacetraders-every-ship-builds`.
- Across systems (asked on 2026-10-06, with your decisions D94–D101): profit per hour, probes at the markets abroad, trade
  across systems, an explorer that charts, and warping, planned as slices 6.27–6.31 and built in that order, one PR each,
  with a stop after each for your check. Slice 6.27 (profit per hour) is built on branch
  `claude/spacetraders-profit-per-hour`.
- B69, B71, B70, slices 6.26 and 6.27 and the plan cleanup are merged (projects#186–#191, main `6a0bcf2`) and deployed by
  gembernodes#82 (image `6a0bcf2`, live since 2026-10-06 09:05Z); 6.27's routes table went in as gembernodes#81.
- Slice 6.28 (probes at the markets abroad, through the gates; with your decisions D97 and D101) is merged (projects#192,
  main `806e9f1`, 2026-10-06 10:25Z). Tracing its first passes for the deploy PR (gembernodes#84) found B72; the deploy
  waits for that fix.
- B72's fix is merged (projects#193, main `edb83c8`), and gembernodes#84 deployed 6.28 with it (image `edb83c8`, live since
  2026-10-06 10:55Z). Checked: no anomalies; X1-NF46's first probe was bought at home (SPECTER-43, 29,885, 10:56Z) and was in
  X1-NF46 by 11:21Z after two jumps, while the probe plan waited for it (`WaitingForAProbeToArrive`) before buying
  X1-HN44's at X1-NF46's shipyard.
- Slice 6.29 (trade across systems, through the gates; with your decisions D95 and D96) is merged (projects#194, main
  `7555d61`, 2026-10-06 11:53Z) and deployed by gembernodes#85, which also gave the routes table its jumps column (image
  `7555d61`, live since 12:01Z). Checked: no anomalies, and by 12:14Z three traders had taken routes abroad: SPECTER-E 80
  LAB_INSTRUMENTS from X1-NF46 to home (4 jumps, about 55,309 after 21,404 for antimatter), SPECTER-D ELECTRONICS from home
  to X1-NF46, and SPECTER-C FIREARMS from X1-NF46 to home.
- Slice 6.30 (the explorers and charting; with your decisions D98, D99, D102 and D103, and B73's fix) is merged
  (projects#195, main `8c37dd7e`, 2026-10-06 13:13Z) and deployed by gembernodes#86, which also gave the systems dashboard the
  systems left, the explorers wanted and bought, the chart rewards and the `Charted` lines (image `8c37dd7e`, live since
  13:19Z). Checked: the explore plan wanted 2 explorers for the 19 systems left, and at 13:25Z the command ship set off from
  X1-QT24 to X1-GT9-AE7B to buy the first.
- Slice 6.31 (warping; with your decisions D100, D101 and D104–D107) is merged (projects#196, main `60f3922a`, 2026-10-06
  14:38Z) and deployed by gembernodes#87, which also gave the systems dashboard's exploring journal the `Warped` and
  `SystemsScanned` lines (image `60f3922a`, live since 14:45Z). Just before, at 14:44Z, the command ship had bought the first
  explorer, SPECTER-5C, at X1-GT9-AE7B; it was home at 15:05Z. gembernodes#88 raised the API's CPU and memory limits
  (restart at 15:37Z). By 16:07Z the explorer hadn't warped yet: the systems within the trade reach come first (D103,
  D106), and at 15:54Z it found a second shipyard that sells explorers, X1-GY77-A2.
- Slice 6.32 (shipyards first; with your decisions D108–D111) is merged (projects#197, main `4b4cbcad`, 2026-10-06
  17:45Z) and deployed by gembernodes#89, with the dashboards' descriptions of the slice (merged 17:54Z).
- Slice 6.33 (the largest hold, every explorer before the probes, exploring in rings, trade trips first at the rate limit;
  with your decisions D112–D115) is merged (projects#198, main `27050f8b`, 2026-10-07 05:19Z) and deployed by
  gembernodes#90, with the dashboard's descriptions of the slice (merged 05:24Z).
- Slice 6.34 (a cargo ship every half hour, before the probes; with your decision D116) is merged (projects#199, main
  `07cf58f3`, 2026-10-07 10:49Z) and deployed by gembernodes#91, with the dashboard's description of the slice (merged
  11:02Z, pod up 11:04Z).
- Phase 6's checks, on the run that ended at the reset (on the cluster since 2026-10-02 08:50Z, so the last 2.2 days of
  its period): 6.10b's and 6.10c's are met. The other loops ran without anomalies of their own, but none has had a full
  period yet; the first is the one that began at 13:00Z, with every plan on since 18:09Z. The only anomalies left open
  were B59's 429s.

## Fixed bugs

Found by reading the code on 2026-10-01, unless a row says where it was found. The soak test (1.14)
saw B8, B9, B10 and B27 at runtime and found B28–B36. Each fix starts with a test that reproduces
the misbehaviour.

| # | Issue | Evidence | Slice |
|---|---|---|---|
| B1 | **"Already at destination" loop.** Navigating to the waypoint a ship is already at publishes `ShipNavigationCompletedEvent` without docking or orbiting. Its handler re-runs the goal executor, which asks to navigate there again. No API call is involved, so the rate limiter doesn't slow it, and the 5 s tick starts another chain every time. The survey executor hits this in its normal state right after any arrival (docked at the target). Most likely what filled the database. | `NavigateToWaypointCommand.cs:82-96`, `ShipNavigationCompletedHandler.cs:26`, `SurveyWaypointGoalExecutor.cs:52`, `ScoutWaypointGoalExecutor.cs:41`, `DeployProbeGoalExecutor.cs:48`, `MineAndSellGoalExecutor.cs:106` | 1.1 (done) |
| B2 | **The Wolverine inbox is never cleaned.** Every published message is stored in Postgres. Turning the durability agent off also turns off the deletion of handled messages: confirmed against Postgres in 1.3, where 50 handled messages were all still there a minute later (with the agent on they were gone). | `Program.cs:180`, `Program.cs:189-191` | 1.3 (done) |
| B3 | **Retention gaps.** Pruning only starts if every earlier startup step succeeded, and one failing table stops the tables after it. `startup_snapshots` (full JSON on every pod start), `runs` and `wolverine.*` are never pruned. All pruning is scoped to the current agent, so each server reset leaves the previous agent's rows behind for good (fixed in 1.4: agent bootstrap deletes them). | `DeferredStartupHostedService.cs:57-76`, `DataRetentionService.cs:55-64` | 1.4 (done: earlier agents), 1.5 (done) |
| B4 | **The agent JWT (about 1 KB) is part of every table's key**, and of its indexes. | `SpaceTradersDbContext.cs` (`AgentToken`, `HasMaxLength(1024)`) | 1.4 (done) |
| B5 | **The automation kill switch doesn't stop the game loop.** Nothing in `Application/Automation/` reads `Automation.Enabled`. | `GameLoopService.cs:69-76` | 1.7 (done) |
| B6 | **A server reset during a run isn't detected.** Every call fails with 401 until the pod restarts, and the liveness check keeps passing. | `AgentBootstrapService` (runs only at startup) | 1.8 (done) |
| B7 | **Domain events are raised but never dispatched.** Nothing outside the domain reads `AggregateRoot.DomainEvents`. As a result:<br>• the ledger only holds fuel purchases;<br>• the credits gauge and the credit samples stay empty;<br>• below 200k credits the probe plan waits forever for an `AgentCreditsChanged` that never comes. | `Domain/Common/AggregateRoot.cs`, `ProbeDeploymentPlanService.cs:71` | 2.2 (done) |
| B8 | **The contract miner leaves with a partial load.** The tick sends it to deliver as soon as any contract cargo is aboard, so the "fill up to required units or a full hold" logic never gets to run. | `GameLoopService.cs:118-139` vs `MineResourceVolumeCommand.cs:145-149` | 1.14 (done) |
| B9 | **The contract plan never completes.** It only advances on `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`, and nothing publishes either. After the contract is fulfilled, the plan stays Active and the assignment stays open. | `ContractPlanService.cs:227-266` | 1.14 (done) |
| B10 | **The command ship idles after scouting.** When the scout plan completes, the ship's last `ScoutWaypointGoal` stays active, so mining and trading treat the ship as busy. It also writes two log lines every tick (Debug since 1.9), and every tick it marks its last waypoint visited again: in 1.14, 178 updates of `cached_waypoints` in 15 minutes. | `ScoutAllMarketplacesPlanService.cs:162`, `MiningAutomationService.cs:388-405` | 1.14 (done) |
| B11 | **Prometheus can't scrape `/metrics`.** Only `/health` is exempt from the API key. (`spacetraders_api_throttled_total` also counted every 25 ms local wait as a throttle; since 1.10 it counts 429 responses only.) | `ApiKeyMiddleware.cs:17`, `RateLimitingHandler.cs` | 2.1 (done) |
| B12 | **Log noise.** Information-level logs on every 5 s tick, `System.Net.Http` at Information (about 4 lines per API call), no correlation properties, and the ship symbol logged under three names (`ShipSymbol`, `Symbol`, `Ship`). The production JSON has no rendered message. | `Program.cs:60-74`, tick services | 1.9 (done) |
| B13 | **API limits and errors don't follow the official guide** (https://spacetraders.io/api-guide/rate-limits; per D3 that makes them bugs).<br>• **Limit:** the guide allows 2 requests per second with a burst of 30 requests per 60 seconds, per IP and per account. The code makes every request take a token from both a 2/s bucket and a 30-per-60 s bucket, which caps the bot at 30 requests a minute: a quarter of the sustained rate. This reads "burst" as extra capacity on top of 2/s, the only reading in which a burst is faster than the normal rate; the guide doesn't spell out how the two combine, so the 429 counter must confirm it after the fix.<br>• **502:** the guide says to wait a few minutes. The code retries after 1, 2 and 4 seconds, then the tick keeps calling every 5 s, because nothing reads `IsAvailable`.<br>• **429 without `x-ratelimit-*` headers** (from the cloud infrastructure, not the rate limiter): the guide recommends exponential backoff. The code retries once after 1 second.<br>• **The buckets probably reset.** The limiter is registered as a transient handler, so the HttpClient factory recreates its buckets whenever it rebuilds the handler chain (every 2 minutes by default). | `RateLimitingHandler.cs:13-32`, `RateLimitResponseHandler.cs`, `RetryHandler.cs` | 1.10 (done) |
| B14 | **One failing step stops the whole tick.** The tick has a single try/catch, so an exception in any plan skips every later plan, all ship steps and the contract commands, again every 5 s while it keeps failing. Example: until the scout plan has saved its state, scout ship selection throws whenever there isn't exactly one ship with fuel. | `GameLoopService.cs:33-44`, `ScoutShipSelectionService.cs:21-42` | 1.11 (done) |
| B15 | **The probe plan buys a probe every tick for a target whose probe is still travelling.** Each pass starts with an empty in-flight set, and a travelling probe doesn't count as available, so the target looks unserved. Only the credit reserve stops the purchases. | `ProbeDeploymentPlanService.cs:220-245, 336-345, 427-438` | 6.3 (done: the plan buys by count, probes in flight included, and a probe in flight keeps its market) |
| B16 | **Goal status never changes, and scout and survey goals are never cleared.** `UpdateGoalStatusAsync` has no production caller, so every goal stays `Assigned` and the Completed/Blocked checks in mining and trading never match.<br>• A finished scout goal keeps the command ship "busy" (B10).<br>• When the mining executor replaces a miner's goal with a survey goal, that miner keeps surveying and never returns to mining. | `ShipGoalRepository.cs:70-81`, `MineAndSellGoalExecutor.cs:194-213`, `MiningAutomationService.cs:397` | scout part: 1.14 (done); survey part: 6.4 (done) |
| B18 | **Most settings do nothing.** Of the 47 seeded settings, only `Automation.Enabled` (partly, see B5), `FleetExpansion.MinCreditReserve`, `Mining.MaxDrones`, `ActivityLog.RetentionDays` and `Alerts.WebhookUrl` change what the bot does.<br>• `Navigation.*` and `Maintenance.*` are read only by services that never run.<br>• `Trade.*` is read only by the market views.<br>• 21 keys are read by nothing at all.<br>• The `Runtime.*` keys are status flags, not settings to tune.<br>The settings table in `docs/HOW_IT_WORKS.md` lists each one. | `DefaultSettingsSeed.cs` | 2.6 (done) |
| B19 | **Price history is never recorded.** Market trade goods are stored as camelCase JSON, but `MarketPriceSampleRepository` reads them back case-sensitively into PascalCase properties. Every good is skipped, so `market_price_samples` stays empty and the price endpoints return nothing. `MarketRepository` reads the same JSON case-insensitively, so mining and trading are unaffected. | `MarketPriceSampleRepository.cs:15, 119-127`, `SpaceTradersPortAdapter.cs:202` | 2.2 (done) |
| B20 | **A restart clears every ship's active goal.** Startup sync overwrites each existing ship row with `SetValues(new CachedShip { … })`, and that object doesn't carry the goal columns, so they become null.<br>• A scout ship whose assignment already matches the current route step doesn't get its goal back.<br>• Arrival wake-ups scheduled before the restart no longer match any goal (B17). | `StartupSyncService.cs:106-130` | 1.12 (done) |
| B21 | **The app tables may never be created** (confirmed in 1.13). `EnsureCreatedAsync` does nothing when the database already holds any table. Wolverine creates its `wolverine` tables when the host starts, before the deferred initializer runs, so the initializer's `ALTER TABLE` statements would then fail. The cluster's database will be empty on redeploy. | `SpaceTradersDatabaseInitializer.cs:12`, `Program.cs:75-78`, `DeferredStartupHostedService.cs:22-30` | 1.13 (done) |
| B22 | **The dashboard publishes the internal API key.** The WebUI container writes the key into `config.js`, which anyone who can open the dashboard can read. The old ingress served both the dashboard and the API on the public `gemberkoekje.nl`, so anyone could call `PUT /settings/*` and `POST /control/*`. | `SpaceTraders.WebUI/docker-entrypoint.sh:11-29`, `SpaceTraders.WebUI/index.html:17`; gembernodes `3f9f785^:ingress/spacetraders-ingress.yaml` | 4.2 (done: LAN only, not merged) |
| B23 | **A failed startup leaves an idle pod that looks healthy.** One try/catch wraps the startup chain. If database init, agent bootstrap, the run lifecycle, startup sync or recovery throws, the later services (the tick and pruning among them) never start, and nothing retries. `/health/live` runs no checks, and the old deployment used it for the startup and liveness probes, so Kubernetes never restarts the pod. | `DeferredStartupHostedService.cs:57-89`, `Program.cs:146` | 1.11 (done) |
| B25 | **The starting probe is probably not recognised as a probe.** Startup sync stores a ship's registration role as its type (`SATELLITE` for the starting probe), but the probe plan only accepts type `SHIP_PROBE` or a symbol containing `PROBE` or `SATELLITE`, and ship symbols look like `AGENT-2`. The plan then buys a probe instead of using the free one. | `StartupSyncService.cs:64`, `ProbeDeploymentPlanService.cs:495-498` | 6.3 (done: `FleetRoles.IsProbe` reads the frame and both cached types) |
| B26 | **A newly registered agent had no settings until the pod restarted** (found and confirmed in 1.4). Registration wrote the new agent's rows and default settings through a DbContext that was created before the new agent was set: resolving the API client creates it, for the endpoint-usage counter. So all of it was stored under the previous agent. With every setting missing, `Automation.Enabled` read as off, and after a server reset the bot sat idle until its next restart. | `AgentBootstrapService.cs` (`RegisterNewAgentAsync`), `ApiEndpointUsageRecorder.cs` | 1.4 (done) |
| B27 | **A contract plan waiting for budget calls the API on every tick** (found in 1.9, seen at runtime in 1.14: 12 calls a minute). It is retried every 5 s, and each retry fetches every contract (`GET my/contracts`) and saves a new plan: 12 calls a minute while it waits, and the waiting can last as long as the credits stay short. Its log lines went to Debug in 1.9; the calls are still there. | `ContractPlanService.cs` (`EnsureBootstrappedAsync`, `RefreshContractsCacheOnceAsync`) | 1.14 (done) |
| B28 | **Startup sync caches a shipyard without its prices** (found in 1.14). It stored the priced ships where the ship types belong and left the prices empty, and purchases read the price from the latter. A purchase at a shipyard where a ship sat at startup then failed with "price unknown" until a ship arrived there again, and every restart did the same to each shipyard with a ship parked at it; a parked probe never leaves. In the soak test the contract plan waited 9 minutes for a drone it could afford, until the scout docked at that shipyard. | `StartupSyncService.cs` (`EnsureFacilitiesForShipsAreCachedAsync`) vs `SpaceTradersPortAdapter.GetShipyardAsync`; `ShipyardRepository.MapToDto`, `ShipPurchaseService.ResolveShipPurchasePrice` | 1.14 (done) |
| B29 | **Wolverine logs every handled message at Information** (found in 1.14). It logs "Successfully processed message …" under the message type's name, so the `"Wolverine": "Warning"` override doesn't reach it: one line per message. | `DependencyInjection.cs` (`AddWolverine`); Wolverine's `MessageSuccessLogLevel` defaults to Information | 1.14 (done) |
| B30 | **A contract delivery sends every unit aboard** (found in 1.14). A trip's last extraction can bring more aboard than the contract still needs; the surplus earns nothing, and whether the API refuses such a delivery outright is unconfirmed (its docs don't say). If it does, the final delivery fails on every tick. | `FulfillContractDeliveryCommand.cs` | 1.14 (done) |
| B31 | **A restart blanks the contract's terms** (found in 1.14). Startup sync stored contracts without their deadline and deliverables, so after every restart the contract plan couldn't read its deliverable until the next delivery response wrote it back: it couldn't restore a lost assignment, and a ship that had delivered everything couldn't fulfil. | `StartupSyncService.cs` (contracts) | 1.14 (done) |
| B32 | **The ship table grows without end** (found in 1.14). `cached_ships` holds a few wide rows (about 2 kB of ship JSON each) that change every minute or so. Postgres prunes their old versions in place, which kept the table's dead-tuple count under the autovacuum trigger (50), so VACUUM never ran and every update that didn't fit its page extended the table: about 250 kB an hour with one busy ship, and in phase 6 that scales with the fleet. | soak samples: 32 pages for 3 rows, 0 autovacuums in 2 hours | 1.14 (done) |
| B33 | **Contract payments never reach the cached credits** (found in 1.14). The accept and fulfil responses carry the agent's new credits, but only refuels, sales and purchases wrote them to the cached agent. Purchases are budgeted from the cache, so after a contract the bot thinks it has less than it does until the next restart: in the soak test 6,620 less (130,564 cached, 137,184 in the game). | `FulfillContractDeliveryCommand.cs`, `ContractPlanService.cs` (accept) | 1.14 (done) |
| B34 | **Waypoint traits are never stored** (found in 1.14). Startup sync stores only a waypoint's market and shipyard flags, and nothing calls `WaypointRepository.UpsertRangeAsync`, the one method that writes traits (and modifiers, orbitals and charts). What reads them finds nothing:<br>• the mining plan means to pick the nearest asteroid whose deposits can yield the mineral, but always falls back to the nearest asteroid of any kind, so a drone can be sent where its mineral never comes up;<br>• the contract plan's trait score is always 0. It only breaks ties between equally near asteroids (nearest first is by design); whether a farther asteroid with the right deposits should win is a strategy question;<br>• the dashboard shows no traits. | `StartupSyncService.cs` (`EnsureSystemsForShipsAreCachedAsync`), `MiningAutomationService.cs` (`MatchesTradeSymbolAvailability`), `ContractPlanService.cs` (`ScoreTradeSymbolMatch`), `FleetStatusMapper.cs` | 6.4 (done) |
| B35 | **The startup snapshot repeats startup sync's API calls** (found in 1.14). Right after startup sync it fetches the agent, the ships, the system, every page of its waypoints, and the markets and shipyards where ships are, all of which sync has just fetched or found cached: about 11 calls on every start. | `StartupSnapshotService.cs` vs `StartupSyncService.cs` | 0.5 (done) |
| B36 | **The Docker integration tests skip silently on Windows** (found in 1.14). Four test classes decide whether Docker runs by looking for `/var/run/docker.sock` or `DOCKER_HOST`. Docker Desktop on Windows has neither, so the tests report "skipped" while Docker is running. Until it's fixed, the README says to set `DOCKER_HOST=npipe://./pipe/docker_engine`. | `MessageStorageIntegrationTests.cs`, `AgentCleanupIntegrationTests.cs`, `DatabaseInitializerTests.cs`, `IntegrationTestBase.cs` | 0.5 (done) |
| B37 | **The credit-drop alert can't fire** (found in 2.2). `AlertHandler` compares each credit change with the credits it remembers in a field from the previous one, but Wolverine creates the handler anew for every message, so the field is always empty. Until 2.2 nothing published the event anyway. The event carries the old credits, so the fix is small, but it would then warn, and post to `Alerts.WebhookUrl`, on every purchase that costs more than 10% of the credits, a ship included. | `AlertHandler.cs` (`_previousCredits`) | D12: removed |
| B38 | **Startup recovery reports docked ships as in transit** (found in a local run during phase 2, through `spacetraders_messages_handled_total`). Its first branch takes any ship whose cached arrival time has passed for a ship that has just arrived, but that time stays on a ship after it docks: startup sync stores the last route's arrival. So every docked ship that ever travelled gets a `ShipInTransitEvent` (an "in transit" activity row) on every start: three in a run with an idle fleet. `docs/HOW_IT_WORKS.md` describes the branch as "still marked in transit", which the code doesn't check; a ship that really arrived while the bot was down comes back from `GetAllAsync` already in orbit, so the branch never sees one. | `StartupRecoveryService.cs` (`RecoverShipAsync`), `StartupSyncService.cs` (`ArrivesAt`) | 6.4 (fixed: only a ship marked in transit is recovered as one, so a docked or orbiting ship gets its goal step and no `ShipInTransitEvent`; the "in transit" rows already written are pruned after 30 days) |
| B39 | **`/health/startup` answers 200 while the startup chain runs** (found in 4.2). The check reports "still running" as Degraded, and ASP.NET Core answers Degraded with 200 unless told otherwise. The startup probe that 4.2 adds for B23 would pass as soon as the HTTP server is up, like the old probe on `/health/live`. | `Program.cs` (`MapHealthChecks("/health/startup")`), `StartupInitializationHealthCheck.cs` | 4.2 (done) |
| B40 | **The dashboard's probes fill the bot's log budget** (found in 4.2). nginx logs every request, and the WebUI's liveness and readiness probes call `/healthz` 8 times a minute: about 11,500 lines a day in `{namespace="spacetraders"}`, which the dashboard's log-volume panel and the 50,000-a-day alert (2.4, 2.5) count as the bot's. An idle bot logs nothing, so the panel would have shown only the probes. | `SpaceTraders.WebUI/nginx.conf` (`location = /healthz`) | 4.2 (done) |
| B41 | **Hosts in one process share a logger** (found when CI failed on main at `41a7c22`, after #114 had passed). By default the Serilog hosting package sends a host's lines to the process-wide `Log.Logger`, which every host replaces when it starts and closes when it stops. Production runs one host per process; the tests run several side by side, so a host's warnings could reach another host's error log, or none. `HealthRuleScenarioTests`' RepeatingError scenario failed that way, which kept CI from building the images. | `Program.cs` (`UseSerilog`), Serilog.Extensions.Hosting (`preserveStaticLogger`) | 4.2 (done) |
| B42 | **A handler's first message raises `RepeatingError`** (found in 4.3, on the cluster). Wolverine compiles a handler when its first message comes, and under 5.x's `AllowedButWarn` (`RestoreV5Defaults()`) it logged a warning for each dependency it resolves from the container ("Utilizing service location for …"). They all share one template: 11 in the bot's first minute, over the rule's limit of 5, so the anomaly was raised on every start, and could be again whenever a handler first ran later. Their text ("…this is an error") also tripped the error-log alert (B44). | `DependencyInjection.cs` (`RestoreV5Defaults()`), `RepeatingErrorRule.cs` | 4.3 (done) |
| B43 | **A counter's first value never reaches the dashboard** (found in 4.3). Prometheus's `increase()` and `rate()` count what a series gains between two scrapes, never the value it has when first scraped, and prometheus-net creates a labelled series on its first increment. On the cluster the contract's deposit (4,267) and the first drone (46,885) were booked about 25 seconds before Prometheus first scraped the pod, so the ledger panels showed neither; a single 429, failed call or breaker trip could never show at all. | `PrometheusAutomationMetrics.cs`; the dashboard's ledger, 429 and breaker panels | 4.3 (done) |
| B44 | **The error-log alert fires on the bot's ordinary lines** (found in 4.3). Gembernodes' "Error logs detected" rule matches `(?i)error` anywhere in a line, and 2.5 added `spacetraders` to it. The bot's JSON lines contain the word without being errors: the startup settings dump (`Health.Errors.MaxRepeatsIn10Minutes`), Wolverine's "…this is an error" (B42) and a `RepeatingError` anomaly's own lines. It fired five minutes after the first start. | gembernodes `infrastructure/monitoring/grafana-alerting-provisioning.yaml` (`loki-error-logs`) | 4.3 (done: gembernodes PR #13) |
| B45 | **The scout plan can skip a stop** (found in 5.1, on the cluster). When the ship docks at a stop, the tick and the arrival can both run its goal step. On 2026-10-02 at 09:18:58 the arrival's step moved the plan from stop 25 to stop 26, X1-DC53-J58. In the same second the tick's resume check read the plan from before that advance and the assignment from after it, took the assignment for missing and set the ship's goal back to stop 25; and the visit to stop 25 completed a second time, in the tick's goal step, which moved the plan past stop 26. The plan logged "all 26 waypoints visited", but J58's market was never fetched, and markets aren't scouted again. Any stop can be skipped this way, whenever a tick coincides with an arrival. | `ScoutAllMarketplacesPlanService.cs` (`ResumeIfAssignmentMissingAsync`, `AdvanceAsync`), `ShipGoalExecutorService.cs`; Loki, 09:18:58Z (`Tick` 326) | 5.1 (done) |
| B46 | **Two goal steps can run for one ship at once** (found in 6.5, from the code; B45 was the scout plan's case). The tick steps every ship every 5 s, and an arrival steps the ship it docks, on a thread of its own. Both read the ship before either acts, so a trade step would buy twice, or try to sell cargo that is already sold. | `GameLoopService.cs` (goal steps), `ShipNavigationCompletedHandler.cs`, `ShipGoalExecutorService.cs` | 6.5 (done) |
| B47 | **The navigation's fuel fallback leaves a ship in DRIFT** (found in 6.5, from the code). When a flight needs more fuel than the ship has, `NavigateSubCommand` switches it to DRIFT, which burns 1 fuel whatever the distance, and flies there. Nothing switches it back, so every later flight of that ship is DRIFT, about ten times slower than CRUISE. Trade trips plan refuelling stops and never need the fallback (6.5); scouting and contract flights still can. A probe has no tank, so no flight of its runs short of fuel, and the probe executor switches a probe it finds in DRIFT back to CRUISE (6.3). | `INavigateSubCommand.cs` (`TrySwitchToDriftForFuelEfficiencyAsync`); `MineResourceVolumeCommand.cs`, `FulfillContractDeliveryCommand.cs` and `ScoutWaypointGoalExecutor.cs`, whose flights asked for no mode; Loki, 2026-10-04 13:35–16:12Z (`ShipSymbol="SPECTER-1"`) | 6.10c in part, then 2026-10-04 (fixed: the scout plan's and the contract's flights go in CRUISE through refuelling stops too, so a ship left in DRIFT flies its next flight in CRUISE; the fallback still drifts, as a last resort, where no chain of fuel markets reaches) |
| B48 | **Only two of three asteroid types can be mined** (found in 6.4, from the code and the live waypoints). `MineResourceVolumeCommand` accepted only `ASTEROID_FIELD` and `ENGINEERED_ASTEROID`; 56 of X1-DC53's 57 asteroids are of type `ASTEROID`, so a drone sent to any of them got a state mismatch instead of ore, on every step. | `MineResourceVolumeCommand.cs` (`IsValidExtractionWaypoint`) | 6.4 (done) |
| B49 | **A used-up survey is tried again and again** (found in 6.4, from the code). An extraction with a survey that is exhausted, expired or doesn't verify fails with 4224, 4221 or 4220, and nothing removed the survey from the cache, so the miner picked it again on every step; only its expiry ended that. | `MineAndSellGoalExecutor.cs` (`GetBestActiveSurveyAsync`), `SurveyRepository.cs` | 6.4 (done) |
| B50 | **The command ship stays on the contract after the survey plan is switched on** (found in 6.4's first watch, on the cluster). The contract plan gave SPECTER-1 a contract assignment at 14:14:29Z, 23 s before the survey switch, and a contract assignment lasted until the contract was fulfilled. The survey plan takes only free ships, so SPECTER-1 went on mining copper (7 units in its first 22 minutes) instead of surveying (D20). The plan also gave its first ship its assignment back on every tick, whatever that ship did or had become. | `ContractPlanService.cs` (`EnsureActivePlanAssignmentAsync`), `FulfillContractDeliveryCommand.cs` | 6.4 (fixed with D26) |
| B51 | **The API can't read the surveys the bot sends back** (found on the cluster on 2026-10-02, with the first surveys, right after the D26 deploy). Every extraction with a survey was answered 422 "invalid payload": the bot sent the survey's expiry as .NET writes a `DateTimeOffset` (`2026-10-02T15:44:51.937+00:00`), not as the API gave it out (`…51.937Z`). The refusal isn't one of B49's codes, so the step failed, Wolverine tried it 4 more times, and the next tick did it all again: 364 failed calls in the first 12 minutes, and SPECTER-3 extracted nothing. | `SpaceTradersApiClient.cs` (`ExtractWithSurveyAsync`), `SpaceTradersPortAdapter.cs` | 6.4 (fixed) |
| B52 | **After a restart the dashboard could read 0 credits** (found in a health check on 2026-10-03, on the cluster). prometheus-net publishes a gauge without labels at 0 as soon as it is defined, and the bot sets the credits only once the startup chain knows the agent. When Prometheus scraped the new pod in between, it stored the 0: after the deploy of 2026-10-03 the credits read 0 from 07:27:51Z to 07:28:36Z, and with the old pod gone at 07:28:30 the dashboard's "Value gained per hour" fell from 56,896 to −517,672, to show the same amount as a gain an hour later. After the deploy at 15:45Z the day before, the credits and the database size both read 0: −153,292, then +228,819 at 16:45:30Z. Three of the run's 16 starts stored a 0 (15:34Z and 15:45Z on 2026-10-02, 07:27Z on 2026-10-03); whether one does depends on when the first scrape comes. | `PrometheusAutomationMetrics.cs` (`spacetraders_agent_credits`, `spacetraders_db_size_bytes`, `spacetraders_server_next_reset_timestamp_seconds`), `PrometheusMetricsService.cs` (the first sample) | health check (done) |
| B53 | **Every flight logs about a dozen lines** (found in the same health check). Handler after handler said at Information that the ship had left or arrived: the navigation command, the orbit, "in transit", the scheduler's wake-up, "arrived; dispatching", "arrived at", the market and shipyard refresh, the dock, "docked; navigation complete", "navigation complete; resuming goal" and the goal's outcome, about 12 lines a hop. With twelve ships, flights were about 80% of the bot's 2,850 lines an hour (about 6,000 a ship a day), and the log budget of 50,000 a day (2.5) would have been passed that night on normal running. Now a flight logs one line when it leaves and one when it lands, besides its refuel. Replaying the day's logs without the others gives about 2,300 lines a ship a day, as the soak test measured for a mining drone (1.9); the budget itself becomes a number per ship in gembernodes. | `NavigateToWaypointCommand.cs`, `SubCommands/` (orbit, dock, navigate), `ShipArrivedEventHandler.cs`, `ShipNavigationCompletedHandler.cs`, `ShipEventScheduler.cs`; Loki, 2026-10-03 00:27–07:27Z (`--group`) | health check (done) |
| B54 | **The survey plan surveys where no miner will mine** (found in 6.10c's first watch, on the cluster). An asteroid counted for the surveys when one of the miners could reach it, one way. A ship in transit counts as at its destination, so when SPECTER-4 set off at 12:35:20Z on 2026-10-03 on its drift of 2 hours 25 to B7 (D45), the survey plan at once wanted surveys at B14 for B7's five ores, and at B37 for B7's gold, platinum and silver: B37 is 68 from B7, 136 there and back, more than a drone's tank, so no drone can mine it for B7. The command ship left its trading for B37 (12:35:51Z) and took two surveys there at 12:40:11Z that no drone could use, expiring 13:13Z and 13:26Z; B14's would have expired long before the drone got there. Before 6.10c no miner was ever at a far market, so reaching was enough. | `MiningPlanner.cs` (`SurveyTargets`), `SurveyPlanService.cs`; Loki, 12:35–12:41Z (`ShipSymbol="SPECTER-1"`); the survey plan's state at 12:35:49Z | 6.10c follow-up (fixed: an asteroid counts where a miner could mine it for the market, the mining plan's trip in CRUISE; a drone still drifting counts once it is there) |
| B55 | **`ShipLeftIdle` counts survey targets a surveyor can't reach** (found in 6.10c's first watch, on the cluster). The rule took every target that needed a survey for work waiting for any surveyor, but the plan gives a surveyor only targets it can reach. With 6.10b's designated surveyor (SPECTER-F, bought 12:50:29Z on 2026-10-03, an 80-unit tank) in the middle and the command ship mining far out with its 400-unit tank, the targets that needed a survey were at B14, B37, B8 and J72: SPECTER-F waited at XB5C from 13:05:29Z, by design, and at 13:15:33Z the rule raised `ShipLeftIdle` on it ("9 targets to survey"). The mining, siphon and trading branches of the rule read the ships each plan lists as able; the survey branch didn't. | `ShipLeftIdleRule.cs`, `SurveyPlanService.cs`, `SurveyPlanState.cs`; Loki, `EventKind="AnomalyRaised"` at 13:15:33Z; the survey plan's state at 13:16:44Z | 6.10c follow-up (fixed: the plan lists the surveyors that can reach each target, and the rule counts a target only for those) |
| B56 | **The survey ship's move never runs** (found in D54's first minutes, on the cluster). At 15:55:03Z on 2026-10-03, the survey plan gave SPECTER-F its move to B7 (D54: "moves to X1-DC53-B7, where 4 mining drones work and no other survey ship, against 3 in its own area"), and no step of it ran: `ShipGoalExecutorService` steps only the goal types it lists, and `MoveToWaypointGoal`, which had no executor before D54, wasn't one of them. The unit tests ran the new executor directly. SPECTER-F stayed at XB5C with a goal that never ended, so it wasn't free and the survey plan gave it nothing else either. | `ShipGoalExecutorService.cs`, `AutomationSwitches.cs`; Loki, `ShipSymbol="SPECTER-F"` from 15:55Z: the "moves to" line and nothing after it | 6.10c follow-up (fixed: the move is stepped, and belongs to the survey plan's switch; the plan's log line no longer renders its empty phrase as `""`) |
| B57 | **Sales are booked without their market and unit price** (found on 2026-10-03, while measuring how our trades move prices). All 1,336 `TradeSell` rows of the ledger, from the first on 2026-10-02 at 13:20Z, have no `WaypointSymbol` and no `UnitPrice`, while all 281 `TradeBuy` rows have both. `ShipCargoSoldEvent` carries the market (slice 6.10a, D50), and `LedgerEntryHandler` passed it to `spacetraders_goods_sold_units_total` but not to the row; its test asserted the row without them. | `LedgerEntryHandler.cs`; `ledger_entries` grouped by category and empty columns, 2026-10-03 17:33Z | 6.10c follow-up (fixed: a sale is booked with its market and unit price, as a purchase is; the rows already written stay without them) |
| B58 | **A survey ship flies to an asteroid it can't get away from, and back** (found on 2026-10-03, watching D54 on the cluster). A surveyor took any target it could reach one way (`MiningPlanner.CanReach`). The command ship's 400-unit tank makes B37's gold, platinum and silver targets for B7 (68 away; no drone's 80-unit tank gets there and back). SPECTER-F reached B7 at 18:40:10Z after its drift from the middle (D54), refuelled, and at once flew to B37 for gold, the best paid ore without a survey (as all were): it got there at 18:43:38Z with 12 of 80 fuel, and the nearest market that sells fuel is B7, 68 away. Its survey (no gold in it) done, the area rule found no drones within its CRUISE reach and drifted it back to B7 (18:43:49Z, arriving 19:15:33Z), where gold at B37 would again be the best paid ore without a survey: a loop of about 36 minutes, 32 of them drifting, while B14, where the drones mine for B7, gets no survey. D54 has the survey ship drift once, and survey from where the drones are. | `SurveyPlanService.cs`, `MiningPlanner.cs` (`CanReach`); Loki, `ShipSymbol="SPECTER-F"` from 18:40Z; the survey plan's state at 18:43:56Z (every target at B37 and B14 with 0 usable surveys, SPECTER-F the one candidate) | 6.10c follow-up (fixed: a surveyor takes a target only where it can get on, with the fuel left, to a market that sells fuel, `MiningPlanner.CanSurveyAt`; the state lists only those surveyors) |
| B59 | **The API's rate limiter answers 429 while the bot keeps to its budget** (found on 2026-10-03, on the cluster). `ApiThrottled` expects none (slice 1.10), and B13 left its reading of the guide's burst (30 more requests per 60 seconds on top of 2 a second) for the 429 counter to confirm. From about 10:30Z on 2026-10-03, as the fleet grew to 21 ships and its requests to about 0.6 a second, the limiter answered 429 7 to 13 times every three hours (about 1 in 500 requests), in bursts: four in 4 seconds at 19:07Z, six in 16 seconds at 19:14Z. Each came while the bot's own budget was in full use (writes waited 3.1 and 9.1 seconds in the two minutes around them), and each went through on its first retry, most after about 35 ms. `RepeatingError` was raised at 19:15Z, `ApiThrottled` at 19:27Z (11 in an hour). What the server counted isn't known: the warning gave the endpoint and the wait, not the limiter's headers. Step 1's headers (2026-10-03 19:51Z to 2026-10-04 13:00Z, 119 429s, more as the fleet grew: 4, 16, 29, 37 and 53 in successive 6 hours) all said `X-Ratelimit-Type=IP Address`, `Remaining=0`, a burst of 30 and 2 a second. Half named a reset a few to some tens of milliseconds away, and their retry after 35 to 80 ms went through: requests that reached the server a little early. 68 of 118 came within 20 seconds of the one before: a 429 made only its own request wait, and the requests after it went out into the empty budget. The four at 10:07:50Z on 2026-10-04 came during a start, whose budget began full while the server still counted the process before. | `RateLimitResponseHandler.cs`, `RequestBudget.cs`; Loki, `|= "429 from"` from 18:55Z; `spacetraders_api_throttled_total`, `spacetraders_api_rate_limit_wait_seconds_total`, `spacetraders_api_requests_total` | 6.10c follow-up (step 1, asked on 2026-10-03, "log headers first": each 429's warning carries the limiter's headers; step 2, fixed on 2026-10-04 from what they showed: each window is 100 ms longer for a request's journey, a 429 holds every request back until its reset, and a new process starts with its burst spent) |
| B60 | **The jump call sends the destination's system, not its gate** (found on 2026-10-04, building slice 6.11, from the API's spec). `POST my/ships/{ship}/jump` takes the jump gate to jump to, `{"waypointSymbol": …}` (API v2.3.0), and buys one ANTIMATTER at the gate's market; `SpaceTradersApiClient.JumpShipAsync` sent `{"systemSymbol": …}`. The port's jump-gate read turned each connection into its system, so nothing could have named the gate either. No plan jumped before 6.11, so neither ever ran. | `SpaceTradersApiClient.cs`, `SpaceTradersPortAdapter.cs` (`JumpShipAsync`, `GetJumpGateConnectionsAsync`); the API's spec, `jump-ship` and `get-jump-gate` | 6.11 (fixed: the jump sends the gate's `waypointSymbol`, which `JumpRequestTests` checks in the request's body; the connections stay gates; the result carries the antimatter's price and the credits after it, and a refused jump is a `JumpRefusedException`) |
| B61 | **A market seen for the first time can be stored twice at once** (found on 2026-10-04, on the cluster). An arrival at a market fetches it and stores it, and so does the market watch, each in a scope of its own. The watch counts a ship as there as soon as its arrival time has passed, while the arrival is still fetching, and a market never fetched is due at once. Each looked for the market's row first: for a market the bot had never fetched, both found none and both inserted one, and the second insert failed on `PK_cached_markets` (23505). That logged EF Core's Errors, which Grafana's error-log alert counts, and the watch's Warning; the watch's call was wasted and its prices dropped. It happened twice to SPECTER-1: at 12:46:05Z at X1-KR90-E18A, the last market of the system it had just jumped into (6.11), and at 13:07:23Z at X1-FJ91-A2, one of the first markets of the new agent's scout plan after the server reset. The first agent's scout ran before the market watch existed (6.5). Shipyards were stored the same way; two ships storing a shipyard never fetched at the same moment would have collided too. | `MarketRepository.cs`, `ShipyardRepository.cs` (`UpsertAsync`); `MarketWatchService.cs`, `NavigateToWaypointCommand.cs` (the arrival); Loki, the lines naming `PK_cached_markets` or `PK_cached_shipyards` over 30 days: 12:46:05Z and 13:07:23Z only | 6.11 follow-up (fixed: the market and shipyard caches store a row in one statement, `INSERT … ON CONFLICT … DO UPDATE`, so the later of two writers updates the row the first one inserted) |
| B62 | **A market answered without prices loses the prices the cache has** (found on 2026-10-04, on the cluster, the first minutes after the deploy of `150700a`). When the pod started at 19:28Z, the scheduler fired SPECTER-5's arrival at X1-FJ91-C46, due since 19:27:37Z, while no pod ran. The market came back without prices, and the arrival stored it as it came: C46, the station at the gas giant C45, lost its fuel price (the dashboard showed 28 markets, 27 with prices). At 19:29:59Z SPECTER-6, at C45 with 37 of 80 fuel, found no fuel stop it could reach (C47 is 43 away; its siphon trips refuelled at C46 first), flew straight at G59, 93 away, and the API refused the flight with a 400, which the fallback took for its cue: a 43-minute drift. The 400 showed only as a status, as the fallback logged it at Information. `MarketRefresher` already said why not to store such an answer: "it would wipe the prices the cache has"; the arrival and startup sync stored it anyway. And a market without prices looked fresh to the probes and the watch, its last fetch being recent. | `NavigateToWaypointCommand.cs` (`NavigateToWaypointArrivedHandler`), `StartupSyncService.cs`, `MarketRepository.cs`, `INavigateSubCommand.cs`; `ProbeDeploymentPlanService.cs` | 6.5 follow-up (fixed: the arrival stores a market through `MarketRefresher` and startup sync keeps the cached prices, so an answer without prices never wipes them; a market without prices counts as never seen, for the watch and the probes; a flight the fuel aboard can't pay for isn't asked of the API, and the fallback warns) |
| B63 | **A drone whose trip ends mid-tick trades instead of gathering** (found on 2026-10-04, on the cluster, asked about SPECTER-3). SPECTER-3, a mining drone with the mining role (`coverage`), sold its ore at H60 at 19:50:06.749Z in its arrival's goal step, which runs outside the tick, and tick 202's trading plan gave it a POLYNUCLEOTIDES route 0.3 s later; the same at 19:10:41Z (tick 576), 2 of its last 8 trip ends. The trading plan takes a ship with the mining, siphon or construction role that is free when it runs as one its own plan, earlier in the tick, had no work for; but that plan had run while the trip was still on, and at the next tick it would have given a trip, as it did after the drone's other trips (D58: drones gather first). | `TradingAutomationService.cs` (its free traders), `FleetRoleBoard.IsTrader`; `MiningAutomationService.cs`, `SiphonAutomationService.cs`, `ConstructionPlanService.cs` | 6.9 follow-up (fixed: the mining, siphon and construction plans record the free ships they had no work for at each pass, `PassedOverShips`, and the trading plan gives a ship of theirs a route only when its plan, switched on, passed it over) |
| B64 | **A shipyard answered without its ships for sale loses the listings the cache has** (found on 2026-10-04 by reading the code, asked whether a snapshot holds a shipyard where no ship is). The API lists a shipyard's ships for sale, with their price, frame, modules and mounts, only while one of our ships is there; another answer has only the ship types. Startup sync, an arrival and the exploring command ship stored a shipyard as it came, so such an answer wiped the cached listings, and with them the price a purchase reads (B28: "every purchase at that shipyard failed until a ship arrived there again"), as an answer without prices did to a market's (B62). A purchase already left such an answer out (`ShipPurchaseService.QuoteAsync`), and `ShipyardRepositoryTests` asserted the wipe as part of B61's "the next fetch replaces it". Whether it happened on the cluster is not known: the cache keeps no history. | `ShipyardRepository.cs` (`UpsertAsync`), `StartupSyncService.cs` | 2.15 (fixed: an answer without the ships for sale leaves a row that has them as it is, in the repository's one statement and in startup sync; B61's test now replaces listings with listings) |
| B65 | **The gate's next load drops out of the purchase order while the builder flies a load to the gate** (found on 2026-10-05, on the cluster, asked why construction hadn't started). The construction plan tells the order ships are bought in what construction would buy next (D64), judging its builder as if its hold were empty where it is going (`AsIfEmptyWhereItGoes`). A ship in transit was judged there with the fuel its flight leaves it, but not as docked, so it couldn't fill its tank where fuel is sold (`TradeRoutePlanner.FuelAtDeparture`); from the gate, far out, no market was in reach with that, and the plan told the order it needed nothing. SPECTER-D left D49 for the gate at 08:39:48 with 73 of its 600 fuel left after the flight; from the 08:40:19 scrape until it landed at 08:54:41, `spacetraders_purchase_need_credits` had no construction need, and the trading plan's fourth light hauler (`Alternating`, after the gate) was bought at 08:40:32 for 345,915, ahead of the gate's next load (80 FAB_MATS, about 92,400). The hauler also raised the credit reserve by 80,000 (D51), so the load then waited for credits. Every delivery flight to the gate opened the same gap, about 15 minutes each, and the builder flying one was left out of `ReadyShipSymbols`. | `ConstructionPlanService.cs` (`AsIfEmptyWhereItGoes`, read by `NextLoad` and for `ReadyShipSymbols`), `TradeRoutePlanner.cs` (`FuelAtDeparture`) | 6.6 follow-up (fixed: a builder in transit is judged docked where it lands, with the fuel its flight leaves it, so where fuel is sold it leaves with a full tank, as `MiningPlanner.CanReach` judges a ship in transit; the hauler stays bought, and with it its 80,000 of the reserve) |
| B66 | **A good whose route waits for a free trader says nowhere why it isn't traded** (found on 2026-10-05, asked: "Assault Rifles should make a tidy profit at 2191, yet it doesn't even show up in the Goods not traded and why tab. I'm all for pacifism, but it should at least give that reason for that good in the goods not traded tab."). D76 gives each good with a price gap a row, but left out a good a listed route carries, and a waiting route counted as listed; at the next pass with every trader on a trip the waiting routes went (they wait for no one), while the rows of the last pass with a free trader stayed, so such a good was in neither list. | At 12:59Z the trading plan's state listed 4 routes, none waiting, and 14 goods not traded: neither ASSAULT_RIFLES (E54 at 2,360 to J67 at 4,556) nor FOOD (K94 at 1,502 to A1 at 2,488), both lucrative and waiting behind D82's order. `TradingAutomationService.cs` (`SaveStateAsync`, `NotTradedIn`) | 2.18 follow-up (fixed: a good is left out only when a trader's route carries it; one whose route waits says `waiting`, "It waits for a free trader: the free traders took routes that rank higher.", and its row stays while every trader is on a trip, as the others do; the markets dashboard's table needs the new reason's words, in gembernodes) |
| B67 | **The role board credits ships with trade routes the trading plan wouldn't give them** (found on 2026-10-05, asked to investigate as a bug, after SPECTER-1 changed its role 120 times in 12 hours, a median of 3.5 minutes apart). The board's trade options counted routes other ships' trips held and goods they were on their way to buy at a market (D80), and gave each route its own job, so several ships were credited with the same good bought at one market. | At 13:30:39 SPECTER-1 (MEDICINE D48 to D50, "about 1766946 credits an hour") and SPECTER-2B (MEDICINE D48 to D52, 1065319) were both credited with MEDICINE bought at D48, which SPECTER-2A then took (13:31:04); SPECTER-2B took DRUGS for 97,306. From 10:27 to 13:30 every one of SPECTER-1's nine switches to Trade cited MEDICINE D48 to A1, 0.14 to 1.37 million an hour, a route the trading plan never gave it (D82 put it last), and it switched back about ten minutes later. `RoleEstimator.cs` (`TradeOptions`, `Options`) | 6.9 follow-up (fixed in slice 6.21: a ship's trade options leave out what other ships' trips hold, as the trading plan does; a trade job is the good at its buy market, and each job offers its best route; the one-trip rate is capped at what trading earned lately, D87) |
| B68 | **The survey ship of a collection point drifts back to the market after every survey** (found on 2026-10-05, asked: "Extractions without surveys is up, can you check?"). D54 counts a survey ship's own area as what it reaches in CRUISE from where it is. At a collection point's asteroid, with too little fuel to cruise to the point's market, where the parked drones count (D83), its own area had none, so the survey plan moved it to the market after each survey, a drift; from the market it was sent to survey the asteroid again. | SPECTER-2C, from 13:49 on, every 28 minutes: from B7 it cruised the 53 to B44 (3 minutes), took one survey ("Surveyed: … X1-FJ91-B44 for GOLD_ORE"), was moved ("moves to X1-FJ91-B7, where 3 mining drones work and no other survey ship, against 0 in its own area (D54, D55)") and drifted back with 26 fuel (25 minutes). Unsurveyed extractions, all by the three drones parked at B44 (SPECTER-7, -8, -B): 0 at 10h, 10 at 13h, 35 at 14h, 138 at 15h; the one survey there at 15:25 had none of their GOLD, PLATINUM or SILVER. `SurveyPlanService.cs` (the D54 move), `MiningPlanner.TryFindBusierArea` | 6.18 follow-up (fixed: a ship that can only survey at a collection point's asteroid, not in flight, isn't moved; it surveys there, the point's ores first, as D83 meant: "a survey ship that reaches one parks there, as the drones do") |
| B69 | **With a probe for every market, the spare probes never leave the shipyard, and the others hop between neighbours** (found on 2026-10-06, asked: "Could you also have a look at those 7 drones? Only 1 drone is necessary to make a purchase, so that sounds like a bug."). A free probe flew only to a market that was due, its prices older than 5 minutes, and each pair of probe and market was scored by the market's age minus twice the flight. A market our other ships keep fresh is never due, and when a market without a probe fell due, a probe next door, the only one at its own market, beat any spare further away and left its own market, which fell due five minutes later. So with as many probes as markets, D29's probe at every market was never reached. | 28 probes for 28 markets since 2026-10-05 05:23Z: 18 bought between 05:20:41 and 05:23:02, at C46 and A2 in turn. The eight bought at C46 (SPECTER-1A, -1B, -1E, -20, -22, -24, -27, -29) were still there on 10-06 at 07:33Z, docked and without a goal; the plan's state had SPECTER-1A as C46's probe, none at A1, A4, D48, D52, H60, H61 and EF5D, and no open calls. Meanwhile the probes in the A, D and H clusters hopped between neighbours (SPECTER-1F D50→D49→D48→D51→D50 every one to two minutes, SPECTER-16 H60→H61→H63, SPECTER-26 A1→A2→A3): 65 to 109 probe flights an hour. `Probes/ProbePlanner.cs` (`Plan`) | 6.3 follow-up (fixed: once there is a probe for every market, each market keeps one, and only the spares fly, to the markets without a probe, due or not; with fewer probes than markets they still roam) |
| B70 | **An arrival whose handling fails for good leaves its ship stored in transit, every goal step waiting for it** (found on 2026-10-06, asked: "Please have a look at what's keeping SPECTER-5."; slice 1.10 had noticed it for a 502 pause). Only the arrival's dock takes a ship out of transit in the cache. The scheduler deletes a timer when it fires, and Wolverine drops a message after its last retry (250 ms, 500 ms, 1 s, then discard), so an arrival whose dock fails four times is gone. Every step of the ship's goal reads it as stored, in transit, and waits for an arrival; nothing scheduled it again until a restart's sync stored the ship as the API has it. | SPECTER-5 left C47 at 00:40:32Z for gas giant C45, due at 00:42:53Z. From 00:42:49 to 00:43:24Z every call to the game's API failed ("HttpRequestException: Resource temporarily unavailable (api.spacetraders.io:443)"): its `NavigateToWaypointArrivedCommand` failed at the dock at 00:42:59.9, 00:43:07.9, 00:43:15.9 and 00:43:23.9Z, a second before the API answered again (SPECTER-1's arrival at J67, in the same outage, went through on its third try at 00:43:24.9Z). At 07:50Z `cached_ships` still had it `IN_TRANSIT`, `ArrivesAt` 00:42:53Z, its `SiphonAndSell` goal active, and no row in `scheduled_ship_events`; `ShipStuck` had been raised at 01:12:30Z. `ShipGoalExecutorService` (`FindAsync` without dead-reckoning), the `*GoalExecutor`s ("in transit": waiting for the arrival), `NavigateToWaypointArrivedHandler` (the dock), `ShipEventScheduler.FireAndDeleteAsync`, the Wolverine policy in `DependencyInjection.cs` | B17's territory (fixed: a goal step for a ship stored in transit more than 5 minutes past its arrival time schedules its arrival again for its active goal, at most once every 5 minutes per ship (`LostArrivals`); the arrival runs again, its dock included) |
| B71 | **A trip that ends while a plan's pass reads the goals leaves its ship looking free with the cargo it just sold or supplied** (found on 2026-10-06, asked: "Please have a look at SPECTER-D's behavior."). The mining, siphon and construction plans read the ships first and then each ship's goal. A trip ends in its arrival handler, outside the tick: the sale or the supply empties the cached hold first, and the goal ends after. Between the two reads, a pass saw the goal ended next to the ship as it was before: free, with cargo it no longer held. The construction plan leaves a free builder with other cargo to the trading plan (B63), which, reading the ship empty a moment later, gave it a trade; the mining and siphon plans send such a drone to sell what it holds. | SPECTER-D, the one builder: "ConstructionSupplied: … 80 ADVANCED_CIRCUITRY … 400 of 400" at 01:19:25.597Z and "TripEnded" at 01:19:25.630Z, in the arrival handler (no `Tick`); "TradeStarted: … 80 ELECTRONICS from X1-FJ91-D52 … to X1-FJ91-D48" at 01:19:26.294Z (`Tick` 1424, `Plan` Trading), while the construction plan's next load, 80 FAB_MATS at F58, waited (`spacetraders_purchase_need_credits` 01:17–01:37Z; nothing earlier in the order, no other buyer of FAB_MATS, about 0.9M credits above the reserve). The trade took 18 minutes; the FAB_MATS trip started at 01:37:23Z. On 10-05 at 23:00 the same supply, ended between two ticks, was followed by the next load 14 s later. Mining and siphon read the same way; no such sale of goods just sold showed in the 24 hours to 10-06 08:00Z (4,483 sales, 1,204 `held_cargo` trips). `ConstructionPlanService`, `MiningAutomationService`, `SiphonAutomationService` (`EnsureBootstrappedAsync`), `SupplyConstructionGoalExecutor` (the hold before the goal) | 6.6/6.4/6.7 follow-up (fixed: the three plans read every ship's goal first and the ships after, `FleetGoals`: a goal read as ended comes with the ship as its trip left it, and one read as running keeps its ship busy for the pass) |
| B72 | **The probe plan bought a system's probes at home while its first probe was still on its way** (slice 6.28, found on 2026-10-06 before its deploy).<br>• A shipyard sells only where one of our ships is (D30), so a system's own shipyard counted for it only once a probe of ours was there. Meanwhile every pass (5 s) bought its next probe where one could be bought, at home: at home's price, rising with each purchase, and with up to five jumps' antimatter. D97 says "the shipyard where it costs least", and 6.28 itself "the rest where it arrived".<br>• Found before the deploy, tracing the first passes with the prices of 2026-10-06: X1-HN44's 8 probes, X1-NF46's 16 and X1-GT9's 16 bought at home for about 35,000 to 50,000 each with the antimatter, against 26,000 to 32,000 at the shipyards near them, until home's two shipyards turned SCARCE. | `BuyProbeAsync` in `Automation/ProbeDeploymentPlanService.cs`; `ProbeDeploymentPlanServiceTests` `WhileAProbeIsOnItsWay_TheSystemsOtherProbesWaitToBeBoughtWhereItArrives` and `ASystemsFirstProbe_GoesWhereItsProbesCostLeast_ThoughThatIsAnotherSystem` (both failed before the fix) | 6.28 follow-up (fixed in projects#193: the next probe goes by the cheapest shipyard, the antimatter counted; one in a system a probe of ours is on its way to waits for it (`WaitingForAProbeToArrive`), and one in a system with neither waits for that system's first probe, bought at the cheapest shipyard that can sell it; deployed with 6.28 by gembernodes#84, and seen working at 10:56Z) |
| B73 | **The explore plan took a free command ship home after each trade trip abroad, once nothing was left to explore** (found on 2026-10-06 while slice 6.30 was built, never seen live).<br>• Since slice 6.29 a ship whose role is trading takes routes across systems, and "a trader stays where its last sale leaves it" (D96); the command ship trades once it is released.<br>• With nothing left to explore, the plan's next step for a free command ship away from home was the jump home (D60), so it took the ship, flew it home and released it there, after every trip that ended abroad. D60 brings home a ship that explored. | `DecideAsync` in `Exploring/ExplorePlanService.cs`; `ExplorersTests.ACommandShipThatTradesAbroad_IsNotTakenHome_WithNothingLeftToExplore` (failed before the fix) | 6.30 (fixed: only a ship the plan has, one that explored, is brought home; merged as projects#195) |

## Phases

### Phase 0: Housekeeping

**0.1 Claude's role in `CLAUDE.md`** (done)

**0.2 Archive the old plans** (done)
- Moved the old plans, designs, progress logs, the strategy notes, `docs/implementation/*` and
  `docs/operations/LOCAL_DEVELOPMENT.md` into `docs/archive/`. Its README says what each file is,
  and that none of them describes the current code.
- The current docs are `README.md`, `PLAN.md`, `docs/HOW_IT_WORKS.md`, `docs/GLOSSARY.md`,
  `spacetraders.md` (solution overview: stack, configuration, conventions), `CONTRIBUTING.md` and
  `CHANGELOG.md`.

**0.3 `docs/HOW_IT_WORKS.md`** (done; kept current by every PR that changes behaviour)
- Do: describe the current runtime from the code:
  - the startup chain;
  - the 5 s tick and the plans it bootstraps;
  - goal executors, events and handlers;
  - every table and its retention.
- Also fix the stale parts of `README.md`: the `SpaceTraders.App` project, the `k8s/` folder,
  and `/control/sync` and `/reassign`, none of which exist anymore.
- Done when: every later PR that changes behaviour updates this file.

**0.4 Remove `SpaceTradersV3/`** (done)

**0.5 Tidy-ups found by the soak test** (done)
- Do:
  - B35: build the startup snapshot from what startup sync has just cached, instead of fetching it
    again;
  - B36: let the four Docker checks find Docker on Windows too (Docker Desktop's named pipe, or
    leave it to Testcontainers), so the integration tests run there instead of skipping;
  - delete `SpaceTraders/packages.lock.json`: no project uses it (it came in with a "wip" commit);
  - remove the UTF-8 BOM from the `.csproj` files of `SpaceTraders.API.Tests`,
    `SpaceTraders.Application.Tests` and `SpaceTraders.Integration.Test`, which the project-file
    analyzer flags (Proj3000) since the NuGet update.
- Done when: the snapshot makes no API calls of its own; `dotnet test --filter Category=Integration`
  runs the Docker tests on Windows without `DOCKER_HOST`; and the build has no Proj3000 warning.
- Done:
  - B35: the snapshot reads what startup sync has just cached: the agent, the ships with their
    goals, the contracts, every waypoint in the ships' systems, and the market and shipyard where
    each ship is. `StartupSnapshotServiceTests` runs it against a substitute API client, which
    receives no call (about 11 on every start before). The cache holds less than the API returns
    (no crew, no mount names or descriptions, and no waypoint traits until B34); it does hold the
    goals and the contracts, which the old snapshot lacked.
  - The snapshot no longer switches `Automation.Enabled` off and back on around itself. That kept
    the fleet still during its API calls; without them it has nothing to wait for, and switching
    automation back on could undo a switch-off made in the meantime.
  - B36: the four checks ask Testcontainers whether it found Docker
    (`TestcontainersSettings.OS.DockerEndpointAuthConfig`), the same discovery it starts the
    container with: `DOCKER_HOST`, the Unix socket, or Docker Desktop's named pipe. On Windows,
    without `DOCKER_HOST`, all 74 integration tests now run; 6 used to skip (`IntegrationTestBase`
    already tried the named pipe itself, the other three classes didn't).
  - The lock file belonged to `.net.csproj`, the project-file analyzer's SDK project, which came in
    with the same "wip" commit. No solution includes it, so nothing restores it.
  - Noticed: where `npm ci` has run in `SpaceTraders.WebUI`, the build still reports 6 Proj3000
    warnings, for BOMs in files under `node_modules`. The analyzer walks the project folder itself
    and skips only `bin`, `obj`, `.vs`, `.git` and `.nuget`, so an MSBuild exclude doesn't reach it.
    CI's .NET job has no `node_modules`, so it sees none.

### Phase 1: Safe to run

**1.1 Fix the "already at destination" loop (B1)** (done)
- Goal: navigating to the waypoint a ship is already at ends in the state the caller needs, and
  never re-triggers the executor without progress.
- Do: either have `NavigateToWaypointHandler`'s step 1 dock before emitting the completed event,
  or have the executors handle "at target" themselves without calling navigate. Pick whichever
  keeps a single obvious path.
- Done when: for every executor, tests cover both "docked at target" and "in orbit at target",
  and each case finishes in one step without publishing `ShipNavigationCompletedEvent` again.
- Done:
  - The executors handle "at target" themselves. Docking first would not have fixed the survey
    executor, which needs orbit. In orbit at the target, the scout, probe and mining executors
    now dock, like the trade executor already did; docked at the target, the survey executor
    orbits. That is the whole step; the next one does the work.
  - Navigating to the waypoint a ship is already at does nothing and logs a warning. It no
    longer publishes `ShipNavigationCompletedEvent`, so that event only follows a real arrival.
  - `AlreadyAtDestinationLoopTests` reproduces the loop through the real navigate and
    navigation-completed handlers: before the fix, each of the four cases ran until the test's
    limit of 10 steps.

**1.2 Per-ship circuit breaker** (done)
- Goal: any future loop is contained to one ship, and it shows up.
- Do: count goal steps per ship in a sliding window. Above N per minute (a setting), the ship's
  goal becomes Blocked with the reason `runaway`. Until phase 3 exists, this means a Warning log
  plus a metric; after that, an anomaly.
- Done when: a test with a looping fake executor trips the breaker.
- Done:
  - `ShipGoalExecutorService` counts every goal step per ship over the last minute, whatever
    triggered it. Above `Automation.CircuitBreaker.MaxGoalStepsPerMinute` it blocks the goal
    (`StatusReason` = `runaway`), logs a warning and counts
    `spacetraders_goal_breaker_trips_total{ship}`. Blocked goals are not stepped again.
  - The default of 60 is five times what the tick takes (12 a minute); a loop like B1 takes
    thousands. It's a setting, so it's yours to tune.
  - A blocked goal stays blocked until a plan replaces it. The scout plan never does, so a
    tripped scout ship waits for someone to look at it.

**1.3 Stop storing in-process messages (B2)** (done)
- Background: the durable queues were added on purpose, as an outbox. A message is stored in the
  same transaction as the database change, so a crash can't lose it (phase 2 of
  `docs/archive/RACE_CONDITION_PREVENTION_IMPLEMENTATION.md`). The same document names startup
  recovery as the second line of defence.
- Do: choose one of these and record the choice in `HOW_IT_WORKS.md`:
  - **Turn the durability agent back on**, which keeps the outbox and should clean up handled
    messages.
  - **Remove `UseDurableLocalQueues()`** and rely on `StartupRecoveryService` and
    `scheduled_ship_events` to resume ships after a restart. If nothing else needs Wolverine's
    Postgres storage, drop `PersistMessagesWithPostgresql` too.
- Done when: the soak test (1.14) shows no `wolverine` tables, or flat ones.
- Done:
  - Chosen: no Postgres message storage at all. `UseDurableLocalQueues()`,
    `PersistMessagesWithPostgresql`, the unused EF Core transaction middleware (no handler used
    it, so messages were never stored in the same transaction as the data) and
    `UseResourceSetupOnStartup` are gone, and so are the two Wolverine packages they needed.
  - Why not the durability agent: it would clean up, but every published message would still be
    written to Postgres and deleted again, and nothing here needs it. A crash loses the messages
    in flight; startup sync, startup recovery and `scheduled_ship_events` already cover that.
  - `MessageStorageIntegrationTests` starts the host as on the cluster against an empty Postgres:
    it created 8 `wolverine` tables before, none now. The soak test (1.14) showed it in a real
    run: no `wolverine` schema or table in four hours.
  - `OutboxReplayIntegrationTests` is gone: it tested Wolverine's durable scheduling, which the
    app doesn't use.

**1.4 Short agent identity, old agents cleaned up (B4, part of B3)** (done)
- Do: key rows on a short agent id (agent symbol plus reset date, or a small surrogate key)
  instead of the JWT; the token itself is stored only in the credentials table. When a new agent
  is registered after a reset, delete the previous agents' rows and keep only their run summary
  (`runs`).
- Done when: no table other than credentials stores the token, and the cleanup has a test.
- Done:
  - Rows are keyed on `AgentId`: the agent's symbol and the server's reset date, such as
    `GEMBER@2026-09-27`. Bootstrap reads the reset date from `GET /` before it tries any token, so
    a token the server accepts was registered in that reset. A token keeps the id it was first
    stored under. The token sits only in `stored_credentials`; before, a test found it in four
    more tables after one registration.
  - At every start, once it has picked the agent, bootstrap deletes every other agent's rows,
    table by table, except `runs`. At every start rather than only after a registration, so a
    table that fails (a timeout on a big table, say) is retried at the next start, and an agent
    switched to through configuration is covered too. Against Postgres, the old agent's rows
    stayed in seven tables before.
  - Registration refuses when an agent with the new id is stored already. That only happens when
    the server resets between reading its reset date and registering; the restart reads it again.
  - Found on the way, and fixed: B26, a newly registered agent had no settings until the pod
    restarted.
  - The schema initializer only creates the model's tables now. Its upgrades for old schemas
    would have added a token column back to every table, and the database starts empty anyway. A
    database from before this slice has to be dropped. The unused `scout_plan_states` table is
    gone.

**1.5 Retention for every table that grows (B3)** (done)
- Do:
  - give each table a policy: by age, by row count, or explicitly bounded;
  - add `startup_snapshots` (keep the last N) and `runs`;
  - start pruning independently of the startup chain;
  - run each table's prune separately, so a failure logs and moves on;
  - rewrite the `NOT IN` downsampling delete so it fits the 30 s command timeout.
- Done when: a test fails for any `DbSet` without a retention policy or an explicit "bounded"
  mark, so a new table can't slip through.
- Done:
  - `DataRetention` lists every table: 10 with a policy that prunes them, 18 bounded, each with
    the reason. `DataRetentionTests` fails for a table that isn't listed; before, 23 tables had
    no policy, `startup_snapshots` and `runs` among them.
  - New policies: `startup_snapshots` keeps the agent's first snapshot and the last 10; `runs`
    and `run_credit_highlights` keep 365 days (runs of every agent, since 1.4 keeps those);
    `ship_goal_history` and completed `fleet_goals` keep 30 days. Claude picked these numbers;
    they're yours to change. The existing ones (7 and 90 days for samples, 30 for the ledger and
    ship tasks, `ActivityLog.RetentionDays` for the activity log) are as they were.
  - `DataRetentionService` prunes each table in its own scope and try/catch, and has taken over
    from `ActivityLogPruningService`. It starts right after database initialisation, before any
    step that can fail, and prunes at once, then daily. Pruning covers every agent's rows, so it
    doesn't need agent bootstrap; the activity log takes the longest `ActivityLog.RetentionDays`
    any stored agent has.
  - The downsampling deletes rank the rows once (`row_number()`). On 2.6 million samples (30
    markets, 20 goods, 90 days), the old `NOT IN` was cancelled after 3 minutes, still running:
    Postgres compared every row with a list too big to hash in `work_mem`. The new one took 5 s.

**1.6 Database size guard** (done)
- Goal: the bot can't fill the shared Postgres volume or the NAS share.
- Do: every few minutes, read `pg_database_size(current_database())` and export it as
  `spacetraders_db_size_bytes`. Above the soft limit, raise an anomaly; above the hard limit,
  pause automation (this needs 1.7). The limits are settings, starting at 1 GB and 3 GB (D8).
- Done when: tests with a fake size source cover both limits.
- Done:
  - `DatabaseSizeGuardService` reads the size every 5 minutes and exports it. The limits are
    `Database.SoftLimitMegabytes` (1024) and `Database.HardLimitMegabytes` (3072).
  - Until phase 3, the "anomaly" for the soft limit is a `DbSizeSoftLimit` warning, logged once
    per crossing. Above the hard limit it switches `Automation.Enabled` off and logs
    `DbSizeHardLimit`; while the database stays above it, every check switches automation off
    again.
  - Its first check runs before the rest of startup goes on, so a database already above the
    hard limit has automation off before the tick starts (and before the startup snapshot reads
    the switch, which would otherwise switch it back on).

**1.7 A kill switch that works (B5), and one switch per plan (D9)** (done)
- Do:
  - make the tick, the plan bootstraps and scheduler-triggered goal steps all respect
    `Automation.Enabled`;
  - add one setting per plan (`Automation.Plan.Scout.Enabled`, `.Contract`,
    `.ProbeDeployment`, `.Mining`, `.Trading`). The tick skips a disabled plan's bootstrap.
    Per D9, Scout and Contract default to on, the other three to off.
- Done when: tests show that a tick with automation disabled issues no ship commands, and that a
  disabled plan neither bootstraps nor buys anything.
- Done:
  - The tick checks `Automation.Enabled` first. So does `ShipGoalExecutorService`, which every
    goal step goes through (the tick, arrivals, the probe handler, startup recovery).
  - A plan that is off isn't bootstrapped, which is where all buying happens. A test checks that
    Wolverine doesn't wire the plan services' `Handle` methods, so no event can run a plan either.
  - One step further than the "Do": a plan that is off also doesn't move its ships. Their goals
    are skipped (they wait, they aren't cleared), and the tick's contract work is skipped when the
    contract plan is off. Otherwise switching mining off mid-run would leave its drones mining.
  - An arriving ship still docks and refreshes its market while automation is off: that finishes
    a command issued before. Only the goal step after it is skipped.

**1.8 Server reset during a run (B6)** (done)
- Do: a 401 with the reset-date error pauses automation, logs `ResetDetected`, and stops the
  host. Kubernetes restarts the pod, and startup already registers the new agent through the
  account token. Simpler than re-bootstrapping in-process.
- Done when: a test with a fake port returning the reset error stops the host.
- Done:
  - The API client notices it, not the callers: every failed call goes through one method, so
    no caller can swallow the error first (the contract plan, for one, ignores API errors). The
    test therefore fakes the server's HTTP response rather than the port.
  - `ServerResetMonitor` switches `Automation.Enabled` off (for the old agent), logs
    `ResetDetected` at Critical and stops the host, once.
  - It ignores reports until startup has completed, because agent bootstrap tries old tokens on
    purpose. A reset in the middle of startup makes that step fail, which stops the host (1.11).
  - The new agent starts with the default settings, because settings are stored per agent.

**1.9 Log diet (B12)** (done)
- Do:
  - move per-tick messages to Debug, or log them only when something changes;
  - set `System.Net.Http` to Warning in the Serilog section (done in `appsettings*.json`; the old
    ConfigMap's `Logging__LogLevel__*` variables probably never reached Serilog);
  - use `RenderedCompactJsonFormatter` in Production;
  - use one property name per concept (`ShipSymbol`, `ContractId`, `WaypointSymbol`);
  - push tick and plan context through `LogContext`.
- Done when: an idle bot writes less than one line a minute, and a normal day stays within a
  budget of 50k lines. Loki's 31 days on 10Gi are shared with every app.
- Done:
  - What a tick finds when nothing changed goes to Debug: a plan already complete or still
    waiting, no idle ship, no budget, a purchase denied, the mining drone cap. Every action stays
    at Information (docked, sold, bought, assigned), and so does the contract plan starting to
    wait for budget, once. Ship commands log their result; the "starting" line goes to Debug.
  - Two idle states the first run can meet now write nothing at Information: the command ship
    after scouting (two lines every tick, 24 a minute, from B10) and a contract plan waiting for
    budget (four every tick, 48 a minute). Tests replay a minute of ticks of each.
  - `RenderedCompactJsonFormatter` in Production. The ship a line is about is always
    `ShipSymbol` (no more `Symbol` or `Ship`), a waypoint `WaypointSymbol` unless the message
    names its role (`Destination`, `SellWaypoint`), a contract `ContractId`, a goal kind
    `GoalKind`. Every line logged during a tick carries `Tick`, and its step's `Plan`,
    `ShipSymbol` or `ContractId`, through `ILogger.BeginScope`, which Serilog turns into
    properties (checked against Serilog itself).
  - The soak test (1.14) measured the budget: about 2,000 lines a day while a drone mines, and
    none while the fleet is idle. Probes, mining and trading are still to measure, once phase 6
    switches them on. A ship that can't find a route logs
    a warning on every tick; phase 3's health rules are the place for that.
  - Found on the way: B27 (the waiting contract plan's API calls).

**1.10 Follow the API guide for limits and errors (B13)** (done, apart from the check after the redeploy)
- Do: make the client do what https://spacetraders.io/api-guide/rate-limits says:
  - **Limit:** 2 requests per second sustained. When that's used up, a request may draw from a
    burst pool of 30 that refills over 60 seconds. Only when both are empty does it wait.
  - **429 with `x-ratelimit-type`** (the rate limiter): wait until `x-ratelimit-reset`, or
    `retry-after` if that's missing, then retry.
  - **429 without those headers** (the cloud infrastructure): retry with exponential backoff,
    up to a cap.
  - **502** (DDoS protection): stop all outbound calls for a few minutes (a setting, default 3),
    then try one call. The tick and the plan bootstraps skip while paused. Log
    `ApiUnavailable` and `ApiAvailable` as journal events.
  - Correct the "Burst Limit" entry in `docs/GLOSSARY.md` (done).
- The limit is per IP and per account. Never run two instances against the same account at the
  same time (for example, the soak test while the bot runs on the cluster).
- Done when:
  - tests cover each of the four behaviours above;
  - after redeploy, the count of real 429s stays at zero, which confirms the burst reading (the
    429 rule in 3.2 watches this).
- Done:
  - `RequestBudget` (a singleton, so the buckets no longer reset) counts in sliding windows: 2
    requests in any second, then up to 30 more in any 60 seconds. A sliding window is at least
    as strict as any fixed window the server might count in.
  - 429: with the headers it waits until the reset (at most a minute), without them it backs off
    1, 2, 4, 8 and 16 s. It gives up after five retries. Every 429 is logged at Warning and
    counted in `spacetraders_api_throttled_total`, which no longer counts local waits.
  - 502: `OutagePauseHandler` pauses all calls for `Api.BadGatewayPauseMinutes` (default 3).
    Calls during the pause fail at once (`ApiPausedException`); the tick skips its work and
    logs `ApiUnavailable`, then `ApiAvailable` after the first successful call.
  - Still to check after the redeploy (phase 4): the count of 429s stays at zero. It did in the
    soak test (1.14): no 429 in four hours.
  - Noticed: an arrival that falls in a 502 pause can't dock, and its message is dropped after
    Wolverine's three retries. The ship then looks in transit until the next restart; that is
    B17's territory (6.4).

**1.11 Failures don't silently stop work (B14, B23)** (done)
- Do:
  - run each tick step (each plan bootstrap, each ship's goal step, each contract assignment)
    in its own try/catch that logs the step and the ship;
  - make a failing startup chain stop the host, so Kubernetes restarts the pod with back-off;
  - use `/health/startup` for the startup probe (4.2).
- Done when: tests show that a throwing plan doesn't stop the other plans or the ship steps, and
  that a throwing startup step stops the host.
- Done:
  - Each tick step also gets its own DI scope, so a step that fails halfway can't leave a broken
    DbContext (with unsaved changes) to every step after it.
  - A failed startup chain logs at Critical, stops the host and exits with code 1.
  - The startup probe came with the manifests (4.2), and needed B39 fixed.

**1.12 Restarts keep ship goals (B20)** (done)
- Do: startup sync updates a ship's game state without touching its goal columns.
- Done when: a test syncs a ship that has an active goal, and the goal is still there afterwards.
- Done: sync sets the game-state columns one by one instead of replacing the whole row. It now
  also sets `LocalStatus` from the nav status, which the old code reset to `None`.

**1.13 An empty database gets every table (B21)** (done)
- Do: check against an empty Postgres. If B21 is confirmed, create the app tables explicitly, or
  initialise them before Wolverine sets up its storage.
- Done when: starting against an empty database creates every app table, and an integration test
  covers it.
- Done:
  - Confirmed: with any other table in the database, `EnsureCreated` skipped the app's tables and
    the initializer failed on `relation "stored_credentials" does not exist`. Wolverine's tables
    did exactly that until 1.3.
  - The initializer now creates the database and the model's tables itself when none of them
    exist, whatever else the database holds.
  - `DatabaseInitializerTests` (Postgres): an empty database, a database with an unrelated table,
    and a second run on its own schema all end with every model table.

**1.14 Soak test** (done)
- Do: start from an empty local database (Postgres in Docker), and run against the live API for a
  few hours with all of phase 1 in, while the cluster bot is off. Every 15 minutes, record table
  sizes and message counts.
- Done when: tables grow only with real game activity (ledger, activity log), nothing in
  `wolverine` grows, and the breaker never trips.
- Done:
  - Ran on 2026-10-01 from 07:09 to 11:10 UTC against the live API, from an empty Postgres 16 in
    Docker, with no SpaceTraders deployment on the cluster. Configured like the cluster:
    Production, JSON logs, only the scout and contract plans on (D9). The bot registered
    `SPECTER-DEBUG2`, scouted all 26 markets in 28 minutes, bought a mining drone and fulfilled
    its IRON_ORE contract (42 units) at 10:45; after that the fleet was idle. `tools/soak/`
    recorded every table, `/metrics` and the log every 15 minutes: 17 samples.
  - The criteria hold. The database went from 9.25 MB to 9.76 MB and from 202 to 324 rows, all
    game activity: the activity log (one row per navigation), the fuel ledger, the scouted
    markets, and a startup snapshot per start. Every other table kept its size once B32 was
    fixed. No `wolverine` schema or table appeared. The breaker never tripped: a busy ship takes
    12 goal steps a minute.
  - Also measured: no 429s, no errors, no exceptions, no token in the log. 625 API calls, 106 of
    them B27's. Log lines: about 2,000 a day while a drone mines, and none at all while the fleet
    is idle (0 lines and 0 API calls from 10:46 to 11:10), so 1.9's budget holds. Memory stayed at
    220–330 MB. Messages handled, before B29 took their lines away: 168 in the first 70 minutes,
    every one from a ship moving.
  - The bot was restarted four times on fixed builds, killed like a pod each time: the stored
    token was reused, the run resumed, ship goals were kept, and the leader lease was taken over
    after its 30 s.
  - Found and fixed on the way, each with a test that failed first: B28–B33, and B8, B9, B10 and
    B27, pulled forward from phase 6 because the run showed them. B8, B9, B10, B28, B29, B31 and
    B32 were also seen working in the run.
  - Noticed, not fixed, and now in the plan: waypoint traits are never stored (B34, which matters
    most for mining, 6.4); the startup snapshot repeats startup sync's API calls (B35); the Docker
    integration tests skip on Windows unless `DOCKER_HOST` is set (B36); and a stray lock file and
    three `.csproj` files with a BOM. Slice 0.5 collects the small ones.

**Phase 1 in short** (done 2026-10-01)
- The bot can no longer fill the shared Postgres or Loki: messages stay in memory (1.3), every
  table has a retention policy (1.5), a size guard watches the database (1.6), the logs are on a
  diet (1.9, B29), and the ship table no longer bloats (B32). It doesn't loop a ship (1.1, 1.2),
  its kill switches work (1.7), it follows the API's rate rules (1.10), a failing step doesn't
  stop the tick (1.11), and restarts and server resets keep its state (1.8, 1.12, 1.13, B31).
- Traps it took: startup sync rebuilt rows from one API call and dropped what the other paths
  store, three times (B20, B28, B31); Wolverine logs under the message type's name (B29); a small,
  wide, often-updated table outgrows the autovacuum trigger's reach (B32); and Wolverine 6 needs
  runtime compilation and service location for the DbContext (see `CHANGELOG.md`).
- To understand this phase, start with `docs/HOW_IT_WORKS.md` (sections 1, 2 and 6), then
  `GameLoopService.cs`, `ShipGoalExecutorService.cs`, `DeferredStartupHostedService.cs` and
  `DataRetention.cs`.

### Phase 2: Visibility

Prometheus holds the numbers, Loki holds the events, and Grafana shows both. There is no
Grafana-to-Postgres datasource and no new journal table: Loki already keeps 31 days and handles
its own retention, so the bot's database stays small.

**2.1 Metrics Prometheus can scrape (B11)** (done)
- Do: serve `/metrics` on a separate port that the Service and ingress don't route. It then needs
  no API key and isn't public (the API itself is on the public ingress).
  - Fix the credits gauge.
  - Replace the throttle counter with a wait-time counter and a counter for real 429s.
  - Label API calls by route template, not by raw path.
  - Add these metrics, all low-cardinality:
    - credits;
    - credits earned by source and spent by category;
    - ships by role and state;
    - per-ship status (ship, role, state, goal, reason);
    - contract units required and fulfilled;
    - API requests by endpoint and status;
    - rate-limit wait time;
    - handled messages by type;
    - goal steps by kind;
    - breaker trips;
    - active anomalies;
    - database size;
    - next reset time.
- Done when: a local scrape returns all of them without an API key.
- Done:
  - `/metrics` is served by a second, minimal Kestrel server on `Metrics:Port` (9090), which
    needs no API key; the main port serves no `/metrics` any more. `MetricsEndpointTests` scrapes
    it without a key and finds every metric below (it fails with the metrics server switched off);
    the deployment's Prometheus annotations point at 9090 (4.2).
  - `PrometheusAutomationMetrics` defines every metric when the host starts, so a scrape lists
    them all, also before they have a value. The names are in `docs/HOW_IT_WORKS.md` section 11:
    - credits (`spacetraders_agent_credits`): read from the cached agent every 10 s, so it shows
      the credits from the first sample on. It was fed by an event nothing publishes (B7);
    - credits earned by source and spent by category: counted with each ledger row, by ledger
      category. Until 2.2 only fuel purchases reach the ledger;
    - ships by role and state, and one series per ship with its role, state, goal and blocked
      reason as labels and, as value, when it entered that combination: Grafana's "time in
      state" is `time()` minus it;
    - contract units required and fulfilled, and the deadline, per accepted contract;
    - API requests by method, route template (`my/ships/{shipSymbol}/navigate`) and status,
      counted by a new innermost handler, so every attempt counts, retries included;
    - 429s by source (`rate_limiter` or `infrastructure`), in `spacetraders_api_throttled_total`,
      which since 1.10 counts real 429s only; and the time requests waited for the local budget;
    - handled messages by type (Wolverine middleware), goal steps by kind, breaker trips,
      active anomalies, the database size, and when the server resets next (from `GET /` at
      bootstrap).
  - "Active anomalies" has two rules until phase 3: the size guard's soft and hard limits
    (`spacetraders_anomaly_active{rule="DbSizeSoftLimit"|"DbSizeHardLimit"}`), which 1.6 already
    called anomalies.
  - Ship roles are the cached ship types: the registration role after a start, the shipyard type
    for a ship bought since (B25), so one kind of ship can show under two roles until the next
    restart.
  - `tools/soak/` reads the metrics from the new port and the new request counter.

**2.2 Record what happens (B7, B19)** (done)
- Do:
  - dispatch aggregate domain events, or publish where the change happens, so that credit
    changes, sales, purchases, contract payments and ship purchases reach the ledger and the
    metrics;
  - read the trade-goods JSON case-insensitively in `MarketPriceSampleRepository`, like
    `MarketRepository` does, so price history gets recorded.
- Done when: tests show each of those four producing a ledger row and moving its counter, and a
  market refresh writing price samples.
- Done:
  - Published where the change happens: production code doesn't use the aggregates, so there
    was nothing to dispatch from. The mining and trade executors publish `ShipCargoSoldEvent`
    and `CargoPurchasedEvent`, `ShipPurchaseService` `NewShipPurchasedEvent`, the contract plan
    `ContractAcceptedEvent` and the delivery command `ContractFulfilledEvent`. The contract
    payments come from the contract's terms in the accept and fulfil responses (the port didn't
    map them), and the acceptance payment is a ledger row too (`ContractDeposit`, which nothing
    used).
  - Every credit change also publishes `AgentCreditsChangedEvent` (`AgentCreditsUpdates`, one
    helper for all the places that stored credits), so the credit samples fill and the probe
    plan stops waiting for credits that already came (B7's third bullet).
  - `LedgerEntryHandler` writes the row and moves `spacetraders_credits_earned_total` or
    `_spent_total` by ledger category. `LedgerWiringTests` sends each of the four through the
    host's own Wolverine and finds the row and the counter; the executor and service tests show
    who publishes, and failed before.
  - B19: `MarketPriceSampleRepository` reads the trade goods case-insensitively, with `long`
    prices like `MarketRepository`. Its test stores a market's goods the way an arrival does and
    finds a sample per good (none before).
  - Found on the way: the probe plan's credits handler would have run the plan, and bought
    probes, while it or automation was off; nothing published its event until now. It checks
    both switches now (1.7, D9). Below 200,000 credits every change would also have woken the
    plan only to send it back to waiting (two plan writes and a log line per sale or refuel); it
    now stays asleep until the credits are there. And B37 (the credit-drop alert can't fire), with
    D12.
  - `NewShipPurchasedEvent` takes the ship type as the `ShipType` enum, which has no
    `SHIP_MINING_DRONE`: the service maps the API's name (`ShipMiningDrone`), so the ledger row
    reads that.
  - Noticed:
    - `ShipCargoSoldEvent` doesn't say whether a sale was mined or traded, so both are
      `TradeSell`; `MiningSell` stays unused. Telling them apart is up to you.
    - Wolverine runs all handlers of an event as one chain and retries the whole chain when
      one fails, so a failing handler can probably write the others' rows twice (ledger, credit
      samples, activity log). Not seen; `MultipleHandlerBehavior.Separated` would isolate them.

**2.3 Journal events** (done)
- Do: write one Information event per meaningful thing, each with an `EventKind` and the standard
  properties:
  - contracts: ContractAccepted, ContractDelivered, ContractFulfilled;
  - money: ShipPurchased, CargoBought, CargoSold;
  - plans: PlanStarted, PlanCompleted, and PlanBlocked with a reason;
  - ships: ShipIdle and ShipBlocked, each with a reason;
  - settings: SettingChanged, with key, old value and new value;
  - ResetDetected, AnomalyRaised and AnomalyCleared.
- Done when: `{namespace="spacetraders"} | json | EventKind != ""` in Grafana reads as a timeline
  of the run.
- Done:
  - `JournalEvents` names every kind, and `docs/HOW_IT_WORKS.md` section 11 lists who logs each
    and with which properties. Every journal line starts with its kind (`CargoSold: ship …`), so
    the rendered message reads as a timeline too.
  - Where a line already said what happened, that line became the journal entry, so the log
    doesn't grow: the contract delivery and fulfilment, the purchases and sales, the plans
    starting, completing and waiting, the circuit breaker (`ShipBlocked`), the reset and the API
    pauses. New lines: `ContractAccepted`, `SettingChanged` and `ShipIdle`.
  - `SettingChanged` comes from `SettingsRepository`, so it covers every change, whoever makes
    it (the endpoints, the control switches, the size guard, the reset monitor), and only real
    changes. A key that may hold a secret (`Alerts.WebhookUrl`) shows `(hidden)`.
  - `ShipIdle` comes from the 10 s metrics sample (`ShipStateJournal`): once for each idle ship
    after a start, and once when a ship's goal or assignment ends, so it covers every way a ship
    turns idle without a line in each.
  - The size guard's limits log `AnomalyRaised` and `AnomalyCleared`, with `Rule` and `Subject`,
    in place of `DbSizeSoftLimit`, `DbSizeHardLimit` and `DbSizeNormal`.
  - Mining and trading have no plan to start or complete (they are opportunity queues), so they
    write none of the plan kinds.
  - Tests check the kind and properties of each new line and of the reworded ones (sales,
    purchases, contract payments, the contract plan, the breaker, settings, idle ships, the
    anomalies). The Loki query itself needs the bot deployed (phase 4); a local run shows the
    same lines in the JSON log.
  - `.claude/skills/st-investigate/SKILL.md`: the journal and metrics rows and queries now name
    what exists; the stale B2 and B12 queries are gone.

**2.4 Grafana dashboard** (done: gembernodes PR #10, merged 2026-10-01)
- Location: `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`, added to the
  monitoring `configMapGenerator`.
- Panels:
  - credits, and credits per hour;
  - earned and spent by source;
  - fleet table: ship, role, state, goal, time in state, reason;
  - contracts: required vs. fulfilled, and the deadline;
  - API calls and 429s;
  - messages per minute by type;
  - database size;
  - active anomalies;
  - the journal (from Loki).
- `SettingChanged` and `ResetDetected` appear as annotations, so you can see what changed when.
- Done, on gembernodes branch `claude/spacetraders-dashboard` (commit `3df654a`), not merged: it
  deploys through Flux once it is on `main`. Merged as PR Gemberkoekje/gembernodes#10 on
  2026-10-01, and applied by Flux (`2cd1e32`).
  - Every panel above, plus a row of numbers (credits, credits in the last hour, next reset,
    database size, active anomalies, whether Prometheus reaches the bot), goal steps and breaker
    trips, and log lines per hour against the 50,000-a-day budget. Series are aggregated with
    `max` or `sum`, so a pod restart doesn't split a line in two.
  - Checked without the cluster: every PromQL expression passed `promtool check rules`, and every
    LogQL query (journal, annotations, log volume) ran against a local Loki fed with the bot's own
    JSON log from a local run, and returned the journal lines and the annotation fields.
  - It can't be checked against real data until the bot runs on the cluster (phase 4).

**2.5 Grafana alerts** (done: gembernodes PR #10, merged 2026-10-01; the Grafana restart is pending)
- Rules:
  - the bot is down (no scrape for 10 minutes);
  - an anomaly has been active for more than 15 minutes;
  - the database is over the soft limit;
  - log volume is over budget;
  - add `spacetraders` to the existing error-log rule.
- Grafana only reads this file at startup, so it needs a rollout restart.
- Done, in the same gembernodes commit, not merged:
  - Group `spacetraders` (every minute): the bot is down (`max_over_time(up[10m])` under 1, or no
    series at all), an anomaly active for 15 minutes (the size limits excepted), and the database
    over its soft limit for 5 minutes (from `spacetraders_anomaly_active`, so it follows the
    setting). Group `spacetraders-logs` (every 10 minutes): more than 50,000 lines in 24 hours.
    `spacetraders` joined the error-log rule's namespaces.
  - "Bot is down" starts paused (`isPaused: true`): with the bot off the cluster it would fire at
    once. Unpause it in 4.3. The other rules can't fire without the bot's data.
  - After the merge: a Grafana rollout restart, so it reads the rules.

**2.6 Only settings that do something (B18, D10)** (done)
- Do: remove from the seed every setting that nothing reads at runtime:
  - the 21 keys that no code reads;
  - the `Navigation.*` and `Maintenance.*` keys, which only code that never runs reads.

  Then update the settings table in `docs/HOW_IT_WORKS.md`. Keep the `Runtime.*` status flags
  for now; moving them out of the settings is a separate cleanup. The database starts empty, so
  no old rows need removing.
- Done when: apart from the `Runtime.*` flags, every seeded setting is read by code that runs.
- Done:
  - 26 settings left the seed, after checking each key against the code again: the 18 non-runtime
    keys that nothing read (`Maintenance.LongRouteJumpThreshold` among them), and the four
    `Navigation.*` and four other `Maintenance.*` keys that only code that never runs reads. 30
    remain: the 14 that work, 4 that are read without changing what the bot does (the run's
    strategy label and the market views), and the 12 `Runtime.*` flags, three of which nothing
    reads.
  - `DefaultSettingsSeedTests` lists the non-runtime settings with who reads each; it failed with
    the 26 before. A new setting has to be added there, with its reader.
  - `NavigationPlanningService` and `FleetMaintenancePlanner` still read their settings; they
    don't run, and per D10 a feature that needs a setting adds it back.
  - `RunLifecycleService` still treats a change under `Navigation.`, `Maintenance.`,
    `Outfitting.` and so on as a strategy change that starts a new run. Those prefixes are now
    only reachable through `PUT /settings/{key}`, which accepts any key.

**2.7 Where each ship is, what it does, what it carries** (done; asked 2026-10-02, during 4.3)
- Asked: the fleet table showed each ship's role, state, goal and time in state. The contract's
  drone was in orbit at its asteroid and mining, but nothing showed that, nor how much it had mined.
- Done:
  - Every 10 seconds, per ship: where it is (the waypoint and its type; in transit, `→` and where
    it goes), what the bot has it do (its goal in a few words; else its contract work, `mining` at
    the contract's source, `delivering` at its destination and `on the way to …` between them; else
    `idle`, or `blocked (…)`), when it arrives while in transit, and its hold per good and its
    capacity: `spacetraders_ship_info`, `_arrival_timestamp_seconds`, `_cargo_units` and
    `_cargo_capacity_units`.
  - What the drones extract and jettison is counted per ship and good:
    `spacetraders_extracted_units_total`, `spacetraders_jettisoned_units_total`.
  - The dashboard's side is gembernodes PR #15 (merged): the fleet table's new columns, and panels
    for the holds and for what was mined. Gembernodes PR #16 deploys the bot with them.
  - Noticed: a goal other than scouting reads as its purpose, not its current step (a `MineAndSell`
    drone reads "mining and selling …" while it sells too); those goals only run from phase 6.

**2.8 A markets dashboard per system** (done; asked 2026-10-02, during 4.3)
- Asked: a second dashboard with a dropdown for the system to watch, that system's market and
  shipyard data, and the market tree: which goods are made from which.
- Done:
  - Every minute the bot exports the markets and shipyards it has cached
    (`PrometheusMarketMetricsService`): per market and good its prices, trade volume, supply (1 to
    5) and activity (0 to 3) as last seen; per shipyard its ship types and, once a ship has been
    there, their prices and supply; and when each was last refreshed. A market that no ship has
    visited yet shows only that it exists.
  - The market tree is the game's own: `GET market/supply-chain`, once per start (retried an hour
    after a failure, so a game API that is down can't raise `RepeatingError`), one series per good
    with what it is made from and what is made from it (`spacetraders_good_supply_chain`).
  - The dashboard itself is gembernodes PR #17 (merged; uid `spacetraders-markets`), and PR #18
    deploys the bot with these metrics (image `fd9e3d3`, since 10:29Z).
  - Noticed: the game lists `MACHINERY` as what raw goods (`ICE_WATER`, `AMMONIA_ICE` and others)
    are made from; the tree shows the game's map as it is.

**2.9 Which settings exist, and which are on** (built 2026-10-02 on branch `ccr-212dac2b-p2ent0`, in projects
and gembernodes; asked that day)
- Asked: "Can you work on an extra panel in grafana with which configuration items exist, and which of
  them are turned on?"
- Done:
  - Every 10 seconds the bot exports its settings, one series per setting, always 1:
    `spacetraders_setting_info{setting,current,description}`, the value as stored and what the setting
    does. Grafana reads only Prometheus and Loki (phase 2), so the table needs a metric.
  - A value that may hold a secret shows `(hidden)`, by the rule `SettingChanged` follows
    (`SettingsRepository.Shown`, now shared). Today that is only `Alerts.WebhookUrl`, which is empty.
  - What a setting does comes from the running version's seed (`DefaultSettingsSeed.DescriptionOf`): a
    stored setting keeps the description it was seeded with, and the cluster's agent was registered
    before 6.3 to 6.5 rewrote the probe, mining and trading plans' descriptions. A key the seed doesn't
    hold shows its stored description (empty for one only `PUT /settings/{key}` wrote).
  - The dashboard's side is gembernodes (branch `ccr-212dac2b-p2ent0`): a **Settings** table after the
    database size and the anomalies. The switches come first (every setting whose value is `true` or
    `false`), on in green, off in plain text; then the other settings by name, with their values and
    what they do. The `Runtime.*` status flags are left out (status flags, not settings to tune, B18);
    the metric has them. The blue "Setting changes" annotations already say when each one changed.
  - Tests: `PrometheusMetricsServiceTests` (every setting, the secret hidden, the running version's
    description, only the agent's own), `PrometheusAutomationMetricsTests` (one series per setting as
    its value changes; gone with the setting) and `MetricsEndpointTests` (the scrape lists it). The
    table was checked in a browser, in Grafana 11.6.1 (what the chart's newest 8.x ships) against a
    local Prometheus fed the metric as the bot writes it: 34 rows, the 9 switches first, the empty
    webhook URL as `(empty)`, no `Runtime.*` row.
- Noticed (not changed):
  - The startup settings dump (`SettingsSnapshotLogger`: "Setting {Key} = {Value} …") logs every value as
    stored, `Alerts.WebhookUrl` included, where `SettingChanged` hides it. The URL is empty, so nothing
    has leaked; hiding it there too would take one line.
  - A change reaches the table within about a minute: the 10-second sample, then Prometheus's scrape.

**2.10 The API's request rates, and legends as tables** (built 2026-10-03 on branch `ccr-ff72b415-uqqj4q`, in projects
and gembernodes; asked that day)
- Asked: "For spacetraders, can we add a graph similar to this?", with a screenshot of a graph of requests initiated,
  executed and completed, and rate limited, per second, with each one's minimum, maximum, mean and last in a table under
  it. Then: "In addition, can we change the legends to table view where appropriate".
- Done:
  - A new counter, `spacetraders_api_requests_initiated_total{method,endpoint}`, from a new outermost handler
    (`ApiRequestInitiatedHandler`): every request the bot initiates, once, as it starts, before the pause after a 502
    and the local budget. Nothing counted a request before it went out, so "initiated" needed it; the other three lines
    come from the counters the bot already had.
  - The dashboard's side is gembernodes (same branch): an **API request rates** graph, full width, above the other API
    panels: initiated; executed, each request that went out, retries included; completed, each that got an answer; and
    rate limited, the 429s. Per second over 5 minutes, in the screenshot's colours, with a table of min, max, mean and
    last. Initiated above executed: requests wait for the budget, or the pause refuses them; executed above initiated:
    retries.
  - Every graph with a list legend on both SpaceTraders dashboards has a table under it: mean, max and last for rates
    and counts per minute or hour; min, max and last for levels (credits, ships, usable surveys, the database size,
    prices, supply); total, mean and max for the hourly bars of what we sold into a market. Graphs with many series sort
    by mean (levels by last), so the biggest come first. The graphs with more series grew by two rows, so their tables
    show three or four lines before they scroll; the panels below moved down.
  - Tests: `ApiRequestInitiatedHandlerTests` (counted by method and template before the request goes on),
    `ApiRequestPipelineMetricsTests` (through the client's own handlers: a request retried after a 429 is initiated
    once and executed twice; one the pause refuses is initiated and never executed), `PrometheusAutomationMetricsTests`
    and `MetricsEndpointTests` (the scrape lists it, and its first increment reaches Prometheus). The graph's four
    queries with `promtool test rules` against synthetic series, and both dashboards in Grafana 11.6.1 against a local
    Prometheus holding a day of synthetic data, at desktop and phone width.
- Noticed (not changed):
  - On a phone, a table wider than its graph scrolls sideways: long names, such as the endpoints, push the numbers off
    to the right, as in the screenshot the graph was asked from.
  - "429s, failed calls and rate-limit waits" draws a straight line across hours where a series is missing: a new pod
    has no `status 429` series until its first 429, and the panel joins the gap (`spanNulls`). In the screenshot of
    2026-10-03 23:11 (local time) it climbs from 0 to about 4 between about 18:10 and 21:20 local time, the hours
    between the deploy at 16:07Z and the 429s at 19:07Z. The new graph joins gaps of up to 10 minutes only (a restart),
    and reads 0 for rate limited while the bot runs without a 429 series.

**2.11 What each ship for sale holds, could do and carries** (done: merged 2026-10-04 as projects#154 and gembernodes#52,
which deployed it; built on branch `ccr-1e461fef-n6hydk`; asked that day)
- Asked: "For spacetraders, can we add some more information to the shipyard ships? I'd like to know fuel tank size,
  cargo size, and which special bits they have (e.g. mining laser)", in Grafana, then "Also which role they can fulfill
  within my fleet".
- Done:
  - With the markets and shipyards, every minute (`PrometheusMarketMetricsService`), the bot exports for each ship type
    a shipyard lists in full, once a ship has been there: its tank, the frame's `fuelCapacity`
    (`spacetraders_shipyard_ship_fuel_capacity_units`); its hold, what its cargo holds take together
    (`spacetraders_shipyard_ship_cargo_capacity_units`); and one series with what it could do in the fleet and its
    equipment (`spacetraders_shipyard_ship_info{system,waypoint,ship_type,can,equipment}`). The cached listings hold the
    frame, modules and mounts; slice 6.4 read the tank and hold from them for purchases, and the mounts and modules are
    read now too (`ShipyardShipDto.Mounts`, `.Modules`).
  - `can` is judged as the fleet table's "can do" judges a ship (`FleetRoles.PotentialRoles`), on the ship as the
    plans see one they would buy: by its type, mounts, hold and tank, whichever plans are on. `Survey`, `Mine`, `Siphon`
    and `Trade` in that order, such as `Mine, Trade` for a mining drone; `none` for a ship that can do none of them. A
    probe says `Probe`, where the fleet table says `none`: the probe plan buys and flies it.
  - `equipment` is its mounts, then its modules, each in symbol order and without its `MOUNT_` or `MODULE_` prefix,
    such as `MINING_LASER_I, MINERAL_PROCESSOR_I`. The cargo holds are left out, as the cargo column has them, and so
    are the crew quarters, which house the crew (the command frigate has two); `none` without any.
  - The dashboard's side is gembernodes (same branch): the markets dashboard's **Shipyards** table gets the columns
    "can do", "fuel", "cargo" and "equipment", and is full width under "Places", which is too; the panels below moved
    down.
  - `ShipyardWaypointDto` lists its ship types and ships as read-only lists, which removes the two QW0012 warnings in
    `ApplicationDtos.cs`. The file's QW0028 and QW0029 warnings (strongly typed identifiers) stay, as across the code.
  - Tests: `ShipyardShipCapacityTests` (a listing's mounts and modules), `PrometheusMarketMetricsServiceTests` (tank,
    hold, can do and equipment of a mining drone, siphon drone, surveyor, light hauler, probe and command frigate as
    listed), `PrometheusAutomationMetricsTests` (the series; one info series per ship type that follows the listing,
    gone with the details or the type) and `MetricsEndpointTests` (the scrape lists them). The panel's queries with
    `promtool test rules` against synthetic series in two systems, and the dashboard in Grafana 11.6.1 against a local
    Prometheus scraping the series as the bot writes them, at desktop and phone width.
- Noticed (not changed):
  - Slice 6.7 noted that whether a siphon drone has a gas processor "isn't known here (the shipyard listing the bot
    caches has no modules)". The cached listings do hold the modules (A2's light hauler, with its two holds and crew
    quarters, on 2026-10-02), so the shipyards table's equipment column answers it whenever the cache holds C39's
    listing in full.

**2.12 The next run's settings, and every plan on by default** (done: merged 2026-10-04 as projects#156 and gembernodes#55,
which deployed it at 18:09Z; built on branch `ccr-856636cc-qj1te0`; asked that day, D69)
- Asked, after the server reset of 2026-10-04 13:00Z registered a new agent with the seeded settings (only the scout and
  contract plans on, D9): "After the restart most config items were turned off. Can you ensure everything is on by
  default? And changes in config changes those defaults?" Then: "In addition, id like to have a separate set of endpoints
  to only affect future runs. So I can have a setting for this run (e.g. 50% split between miners and traders) and change
  those settings for the next run to see if it gives an improvement."
- A run here is one agent, from one server reset to the next. `RunLifecycleService`'s runs also start when a strategy
  setting changes; a value chosen for the next run only reaches the agent the next reset registers.
- Done:
  - Every plan switch defaults to on (`DefaultSettingsSeed`).
  - A setting follows its default while nobody has set it (`agent_settings."FollowsDefault"`): every start gives it the
    default as the running version has it, a `SettingChanged` journal line each. Set through `SettingsRepository`, by you
    or by the bot (the size guard, the reset monitor), a setting keeps its value. The `Runtime.*` status flags never
    follow.
  - The settings stored before are judged once, by the start that adds the column, in one transaction with it: one whose
    value isn't its default was set and keeps it (a setting you change, or automation you switch off, before the deploy);
    the rest follow, with the plan switches that are off, whose defaults D69 switched on. So the first start of this build
    switches on the plans the agent of 13:00Z has off, and keeps what was changed before it.
  - The values the next runs start with live in `next_run_settings`, which has no agent, so the agent cleanup leaves it.
    `PUT /settings/{key}` and the kill switch set them too; `PUT /settings/next-run/{key}` sets one alone, `DELETE` forgets
    it, `GET /settings/next-run` lists what the next run starts with, and a key the seed doesn't hold or a status flag is
    not found (404). A new agent is seeded from them, else from the defaults. `POST /settings/reset` forgets them. Each
    change is a `NextRunSettingChanged` journal line.
  - The cluster's database gets the column and the table at the first start: `SpaceTradersDatabaseInitializer.AddedSchema`,
    renamed from `AddedColumns` as it now adds a table too.
  - `spacetraders_setting_info` gets the label `next_run`. The dashboard's side is gembernodes (same branch): the Settings
    table gets a "next run" column with the same on and off as "value", both 180 px wide so they fit side by side on a
    phone, and its description the endpoints; it no longer says a setting can be changed on the bot's own dashboard, whose
    Settings page only shows them.
  - `ControlEndpoints.cs` lost its SA1507 warning (two blank lines). `ApplicationDtos.cs` keeps its QW0028 and QW0029
    warnings (strongly typed identifiers), as across the code, and `SpaceTradersDbContext.cs` its QW0029.
  - Tests: `AutomationSwitchesTests` (every plan on), `DefaultSettingsSeedTests` (a new agent starts with the chosen
    values; a setting that follows its default gets the new one, journaled; a setting someone set and a status flag keep
    theirs; a restart doesn't take the next run's values), `NextRunSettingsTests` (the run that runs now is left alone;
    status flags and unknown keys refused; forget, journal with secrets hidden, reset), `ApiIntegrationTests` (the
    endpoints, and that `PUT /settings/{key}` and the kill switch set the next runs too), `AgentBootstrapServiceTests` (a
    new agent after a reset), `AgentDataCleanupTests` and `DataRetentionTests` (the new table), `PrometheusMetricsTests`
    (the label), and against PostgreSQL 16: `DatabaseInitializerTests` (a database from before gets the column and table
    as the model creates them; of its settings, a plan that is off comes on while a changed setting and automation
    switched off keep their values; the settings are judged only when the column is added) and
    `AgentCleanupIntegrationTests` (the chosen values outlive the old agent and reach the new one). The Settings table in Grafana 11.6.1 against a local Prometheus
    scraping the series as the bot writes them, at desktop and phone width.
- Noticed (not changed):
  - `SettingsRepository.SetAsync` stores the type of what it was given, so `PUT /settings/{key}` turns a `bool` setting's
    type into `string`. Only the bot's own Settings page and the settings snapshot log show it; nothing acts on it.

**2.13 Grafana data per server reset** (done: merged 2026-10-04 as projects#162 and gembernodes#56, deployed by
gembernodes#57; asked that day, D70)
- Asked, after the server reset of 2026-10-04: "Can we key all the Grafana data off the agent ID (or something else that's
  different between resets) so data does not mix between different agents/different resets?"
- The bot registers the same symbol after every reset (SPECTER), so the agent's and its ships' symbols repeat: a counter of
  the new run looked like the old one restarted, a graph ran on from one agent to the next, and an `increase()`, an `offset
  1h` or a table over the dashboard's range added both runs up (the fleet's "mined in range", "Value gained in the last
  hour" for an hour after the reset, the 24-hour survey stats). What differs is the server's reset date, the part of the
  agent id after `@` (`SPECTER@2026-10-04`, B4).
- Done:
  - **Every metric** (`spacetraders_*`) carries `reset_date` as its first label. `PrometheusAutomationMetrics` defines every
    metric with it, and a wrapper (`RunGauge`, and `ZeroFirstCounter`) puts the value in front of the others at every
    write, so the hundred places that write a metric stay as they were. The value comes from the agent id at every write
    (`ResetDateLabel`); a reset ends the process, so one process has one value once bootstrap has picked the agent. What is
    written before that (bootstrap's own API calls) carries an empty value, which Prometheus stores as no label.
    `spacetraders_server_next_reset_timestamp_seconds` is set once the agent is known, so it carries it too. A counter of
    a new run starts at 0 as a series of its own (B43's zero first still holds), and an unlabelled gauge of before (the
    credits, the reserve, the database size, the next reset) has no series until it is set (B52 still holds).
  - **Every log line** carries `ResetDate`, the same value, once bootstrap has picked the agent (`ResetDateEnricher`, in
    `Program.cs`'s Serilog setup).
  - **The dashboards** (gembernodes, same branch): the SpaceTraders, markets and systems dashboards each get a "Reset"
    picker, the dashboard's first variable: the reset dates of `spacetraders_agent_credits` in the time range, newest first,
    so a dashboard opens on the run that runs now; tick several to compare runs. Every Prometheus query filters on
    `reset_date`, the markets and systems pickers too, so their lists hold only that run's systems, markets and goods.
    The journal panels (the journal, the survey journal, the exploring journal) and the setting-change markers keep the
    chosen run's lines. "Errors" keeps them and the lines that carry no run (not the bot's JSON, or logged before the
    agent was known), and "Log lines per hour" counts those too, as the log-volume alert does. The links between the
    three dashboards pass the picker on. Left unfiltered: the "Bot" stat (`up`, which is Prometheus's, not the bot's), and
    the "Server resets" markers, the line between two runs.
  - Checked against the cluster's Prometheus and Loki before the deploy: with the picker matching anything, every changed
    query answers what it answered before (119 identical; the rest differ only by the seconds between the two queries).
  - The alert rules stay as they are: they look at what the bot reports now, and add over the label.
- Noticed (not changed):
  - Data from before the deploy has no reset date, so the picker can't show it: the run of 2026-10-04 shows from the
    deploy on, and the run before it not at all. Prometheus keeps 15 days and Loki 31, so it ages out.
  - `RunLifecycleService` has runs of its own, which also start when a strategy setting changes (see 2.12); the label is
    the reset date, not that run.
- Tests: `PrometheusMetricsTests` (every expected series carries `reset_date="2026-09-27"`; every series of a scrape carries
  the agent's reset date; before the agent is known a series has an empty one), `ResetDateLabelTests` (the reset date of an
  agent id; a log line carries it once the agent is known), `AgentBootstrapServiceTests` (the next-reset gauge is set once
  the agent is known). App 1005, Domain 72, API 188 (and 4 skipped), Integration 1.
- To understand this, start with `SpaceTraders.API/Services/ResetDateLabel.cs`, then `RunGauge` at the end of
  `PrometheusAutomationMetrics.cs` and `ZeroFirstCounter.cs`; in gembernodes, the `reset_date` variable of
  `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`.

**2.14 Ship names** (done: merged 2026-10-04 as projects#165 and gembernodes#60; asked that day, D72)
- Asked: "SHIPS are now named by the game in ascending order. Can we make custom names within the API which should be
  type-number, so COMMAND-1, SATTELITE-1, EXCAVATOR-1. Bonus points if there's a list of relevant names for each of the
  types, one of which is picked per reset to call that type, e.g. all sattelites being called SPUTNIK-1, SPUTNIK-2 etc.
  There should be a list of potential names for each ShipType SHIPYARD enum value. This does mean that while SIPHON DRONE
  and MINING DRONE are both EXCAVATORs, they should get different names."
- The game names an agent's ships after it, in the order they join the fleet, in hexadecimal (SPECTER-1 … SPECTER-F,
  SPECTER-10), and the API can't rename a ship. So the name is the bot's own, beside the game's symbol (D72), which stays
  what every API call, table, metric label and log line keys a ship by.
- Done:
  - **The names** (`Naming/ShipNames.cs`, no I/O): each type a shipyard sells has a list of names (13 lists, one for each
    of the API's ship types, 8 to 10 names each, no name in two lists): probes after probes and telescopes, mining drones
    after what digs, siphon drones after what sips, surveyors after their instruments, the command frigate after
    flagships, shuttles after small birds, haulers after pack animals, and so on. Each server reset picks one name of each
    list, by its reset date (FNV-1a, the same on every start of the reset), and the type's ships are numbered after it in
    the order they joined the fleet, the game's own numbers read as hexadecimal. A type with no list is named after its
    registration role: the plain version asked for. In the run of 2026-10-04 the command frigate is INTREPID-1, the
    starting probe MARINER-1, the mining drones PICKAXE, the siphon drones HUMMINGBIRD, the survey ships ASTROLABE, the light
    shuttle ROBIN and the light haulers PONY.
  - **A ship's type** (`ShipNames.TypeOf`): the one it was bought as, while the cache has it (until the next start's sync
    caches its registration role, B25); then what its frame makes it, a drone by its mount (a laser, a gas siphon or a
    surveyor) and a heavy freighter by a refinery. A test checks for every type that both give the same.
  - **Not stored:** a name follows from the fleet and the reset date alone, and ships only join the fleet, so a name never
    changes during a reset and a restart gives every ship the name it had. A test pins the run of 2026-10-04's names:
    reordering a list, or changing the pick, would rename the fleet of the run under way at the next deploy.
  - **The name book** (`Naming/ShipNameBook.cs`, in memory) keeps them for the log lines, which can't read the fleet:
    startup recovery, a purchase, the internal API's ship list and the metrics every 10 seconds tell it the fleet. It
    needs the reset date, which the Application layer reads through `IActiveReset` (Persistence's `ActiveReset`, from the
    agent id, as slice 2.13's label does).
  - **Where they show** (D72): `ShipName` on every log line about a ship, one with a `ShipSymbol` from its message or from
    the tick's scope (`ShipNameEnricher`, as slice 2.13's `ResetDateEnricher`), and the `ShipPurchased` line says the new
    ship's name ("the bot calls it PICKAXE-3"); `spacetraders_ship_name_info{ship,name,type}`, one series per ship; `name`
    on the internal API's `/status/ships`, which the WebUI's fleet page shows in a column and searches, and the ship's
    page shows beside its symbol. In gembernodes (same branch): a "name" column in the dashboard's Fleet and Roles tables,
    and the name in brackets in front of a journal line about a ship, in the journal, the survey journal and the exploring
    journal. The `st-investigate` skill takes a ship's name too.
- Noticed (not changed):
  - The Fleet table's "type" column still shows the type as cached: the registration role after a start's sync, EXCAVATOR
    for both kinds of drone. The new series' `type` label has the shipyard type, if you'd rather show that.
  - A type the game adds later, with a frame no type has yet, is named after the type it was bought as until the next
    start, and after its registration role from then on.
- Tests: `ShipNamesTests` (numbered by type in the order the ships joined, hexadecimal; a mining and a siphon drone told
  apart; one name per reset, kept for it; every bought type keeps its name through a start's sync; a type with no list
  named after its role; a list for every shipyard type, no name in two; the run of 2026-10-04's names),
  `ShipNameBookTests`, `ShipListNamesTests`, and additions to `ShipPurchaseServiceTests` (Application);
  `ShipNameEnricherTests` (a line naming a ship, a line in a ship's goal step through Microsoft's logging, a line about
  no ship), `StartupRecoveryServiceTests`, `PrometheusMetricsServiceTests` and `PrometheusAutomationMetricsTests` (API);
  the fleet page's name column and search, and the ship's page (WebUI, 112). App 1059, Domain 72, API 195 (and 4
  skipped), Integration 1. In gembernodes, `scripts/validate.py`, the PromQL queries with `promtool test rules` and the
  LogQL queries with `logcli --stdin` (NOTES.md).
- To understand this, start with `SpaceTraders.Application/Naming/ShipNames.cs`, then `ShipNameBook.cs` and
  `SpaceTraders.API/Services/ShipNameEnricher.cs`; in gembernodes, the Fleet table's query H in
  `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`.

**2.15 Snapshots of every shipyard and market, at every discovery, and in Grafana** (done: merged 2026-10-04 as
projects#166 and gembernodes#62; asked that day, D73)
- Asked: "Can you expand the JSON export to include shipyard information, and can you make these jsons available through
  Grafana? If we do that, is everything from the webUI covered in Grafana?" Then: "I'd like the snapshots to be made
  whenever a new discovery is made. So a shipyard with a new ship type or a market with a new good type, in addition to
  the times they are currently made." And: "If the snapshot is taken and data is in memory but there's no ship at the
  shipyard right now, will the data from memory be in the snapshot or none at all?" None at all: the snapshot held the
  market and shipyard only where a ship stood, though the cache had the others.
- Done:
  - **Every market and shipyard the cache holds** is in the snapshot now, as last seen, each with when
    (`LastObservedAt`): the systems the ships are in first, then every other system the cache holds a market or
    shipyard in, each with its cached waypoints. A shipyard lists its ship types, and the ships for sale (price,
    supply, frame, reactor, engine, modules, mounts, crew) once one of our ships has been there; a market its imports,
    exports and exchange, and its prices once a ship has been there. The snapshot is built by `GameStateSnapshots`, moved
    out of `StartupSnapshotService`; it still calls no API (B35).
  - **A snapshot at every discovery** (`DiscoverySnapshotService`, every minute, started right after the startup
    snapshot): when the cached shipyards list a ship type, or the cached markets a good (imports, exports, exchange or
    prices), that no snapshot of the run held yet, it takes one. New to the run, not to a place (D73): a second shipyard
    selling a known type discovers nothing. It reads the cache rather than following its writers (arrivals, the market
    watch, the exploring command ship, startup sync, purchases), so every writer counts, and what was stored within the
    same minute shares one snapshot. One process serves one agent, so what the snapshots held is kept in memory, from the
    startup snapshot on; when that failed, from what the cache lists at the first look, which takes no snapshot.
  - **Why a snapshot was taken:** `Reason` (`Startup` or `Discovery`) at the top of its JSON and in a new column, and for
    a discovery `Discoveries` (each ship type and good with the shipyards or markets that list it) and a `Discovered`
    column with the same in a line: `Ship types: SHIP_LIGHT_HAULER (X1-FJ91-A2). Goods: FAB_MATS (X1-FJ91-H59).` The
    cluster's table gets the columns at the first start (`AddedSchema`); its rows were all startups'. A discovery is a
    `Discovered` journal line too, with the snapshot's id, so Loki keeps them after the snapshots are pruned.
  - **Retention as it is** (D73): the agent's first snapshot and the 10 newest, whatever the reason, pruned at every
    start and daily. With new types bounded (about 20 ship types and 150 goods), so are a run's discovery snapshots; most
    come in its first hours.
  - **The internal API:** `/status/startup-snapshots` lists `reason` and `discovered` with each snapshot; the download is
    named `startup-snapshot-…` or `discovery-snapshot-…`. The WebUI's Snapshots page shows why each was taken and what a
    discovery found, and names its downloads the same way.
  - **B64** (found answering the question above): a shipyard answered without its ships for sale no longer wipes the
    cached listings.
  - **Grafana** (gembernodes, same branch): the Infinity data source (3.11.1, pinned: 4.x needs Grafana 11.6.11, the
    cluster runs 11.6.1), installed by Grafana's own background preinstall, reads the internal API with the API key
    (the 1Password item `spacetraders-secrets`, copied into the monitoring namespace). A **SpaceTraders snapshots**
    dashboard (uid `spacetraders-snapshots`) lists the run's snapshots (captured, why, what a discovery found), downloads
    each as the JSON file the bot saved, through Grafana's data source proxy, so the browser needs no key, and shows the
    picked one: its summary, what it found, its ships, its shipyards (one row per ship type: price, supply, activity,
    tank, hold, mounts, modules, when seen) and its markets (one row per good). A Discoveries panel shows the journal's
    `Discovered` lines. The three other SpaceTraders dashboards link to it.
- **Is everything from the WebUI covered in Grafana?** No (from reading the code and the dashboards; no live comparison).
  Grafana covers the live state, mostly with more detail, history and alerts: credits, each ship's state, location,
  arrival, activity and hold, contracts, market and shipyard prices and their age, settings, the API's request rates and
  429s; with this slice the snapshots too. Not in Grafana:
  - per ship: fuel, flight mode, cooldown, mounts, and its assignment's contract, source and destination;
  - per waypoint: coordinates, its market, shipyard and construction flags, and extractable resources;
  - the rate limiter's state (remaining, limit, reset, type, the bot's burst budget), whether the pod holds the leader
    lease, and per-endpoint call counts over the agent's life with when each was last called;
  - the mining plan's queue of openings;
  - a ship for sale's name, description and activity; per-good prices in systems the command ship has only explored
    (left out of Prometheus on purpose, slice 6.11);
  - the settings' types and the `Runtime.*` flags (the metric has the flags; the Settings table filters them out);
  - the WebUI's search, filters and per-ship page.

  Most of these could be metrics (a series per ship, endpoint or setting) or a Loki panel (trip history, from
  `TripEnded`); descriptions and the explored systems' prices could come through the Infinity data source, as the
  snapshots do. Whether to retire the WebUI is still D5.
- Noticed (not changed):
  - The Infinity data source has the API's full key, so a Grafana user can also switch automation off or change a
    setting, through the data source proxy (which forwards POST, PUT and DELETE) or an Infinity query (which allows POST).
    Anyone who opens the bot's dashboard can already (B22), and Grafana needs a login; a key that only reads, for Grafana,
    would close it.
  - The WebUI shows several things nothing feeds: the Overview's API, Cache and Contract deadline badges and the server
    reset pill read settings nothing writes; Plans' "Recent goals" reads `ship_goal_history`, which nothing writes;
    a surveying ship and a probe on a deploy trip show as Idle there (`FleetStatusQueryService` has no case for their
    goals); the ship page's "Last synced" is the time of the request; the Current Status badges compare against
    `IN_TRANSIT` and `DOCKED` while the API sends `InTransit` and `Docked`.
- Tests: `DiscoverySnapshotServiceTests` (a new ship type and a new good each take a snapshot that says what and where,
  with the journal line; a new place with known types takes none; one snapshot per discovery; discoveries between two
  looks share one; without a startup snapshot, the first look counts what the cache lists as known; a list that doesn't
  parse discovers nothing), `StartupSnapshotServiceTests` (a shipyard and a market where no ship is, in another system
  too, with their listings and when they were seen), `ShipyardRepositoryTests` and `StartupSyncServiceTests` (B64),
  `DatabaseInitializerTests` (a table from before gets the columns as the model creates them, its rows a startup's) and
  `SnapshotEndpointsIntegrationTests` (the list's reason and discovered, newest first; the download's name), against
  PostgreSQL 16; the Snapshots page (WebUI, 113). App 1059, Domain 72, API 209 (and 4 skipped), Infrastructure 87,
  Integration 1. In gembernodes, `scripts/validate.py`, and the dashboard in Grafana 11.6.1 with Infinity 3.11.1 against
  a stand-in for the bot's API serving snapshots the bot's code wrote (NOTES.md).
- To understand this, start with `SpaceTraders.API/Services/GameStateSnapshots.cs`, then `KnownTypes.cs` and
  `DiscoverySnapshotService.cs`; in gembernodes, `infrastructure/monitoring/dashboards/spacetraders-snapshots-dashboard.json`
  and the SpaceTraders API data source in `infrastructure/monitoring/grafana-release.yaml`.

**2.16 Each ship's profit** (done: merged 2026-10-04 as projects#169 and gembernodes#64; asked that day)
- Asked: "For spacetraders, I'd like to see each ships total profit. So -purchase price-market buys+market sales-fuel (plus
  or minus any other relevant ship-specific credit changes)"
- The ledger already books every credit change to the ship it is about (`LedgerEntryHandler`): its purchase, each cargo
  purchase and sale, each refuel, each jump's antimatter, the jump gate's materials. Only the contract's deposit and payout
  go to `AGENT`. The metrics read the ledger only for what each ship cost (`spacetraders_ship_value_credits`, the "Total
  value" graph), and "Profit per hour by activity" adds trips up by activity, not by ship.
- Done:
  - **The metric:** `spacetraders_ship_ledger_credits{ship,category}`, each ship's ledger summed by category since it joined
    the fleet: earnings positive, costs negative, one series per category the ship has rows of; summed by ship, what it has
    made. `PrometheusMetricsService` reads it every 10 seconds in one query that groups the ledger by ship and category; it
    replaces the query for what each ship cost, which now comes from the same sums. A category whose rows age out of the
    ledger (30 days, longer than a reset lasts) loses its series, and a ship that is gone loses them all.
  - **Grafana** (gembernodes, same branch): a "Profit by ship" table under Roles, most profitable first: ship, name, profit,
    then what it is made of: purchase (the ship and its mounts and modules), market buys, market sales, fuel, and other
    (antimatter for jumps, the jump gate's materials, repairs). Costs are negative, a ship with nothing in a column shows 0,
    profit is green or red, and the bottom row sums the fleet. Ship, name and profit fit a phone's width.
- Noticed (not changed):
  - The contract's payments are the agent's (`AGENT`), so a ship that mines and delivers for the contract shows only its
    fuel, and a contract's deposit and payout count for no ship. Splitting them over the ships that delivered would need
    each delivery booked with its ship and units, and a rule for the deposit; asked whether you want that.
  - Profit counts credits as they move: a trader between its purchase and its sale shows the purchase until it sells, and
    cargo aboard counts for nothing (the "Total value" graph values it).
  - The builder's profit is mostly the jump gate's materials, which supplying never pays back (slice 6.6).
  - `MetricsEndpointTests.ExpectedMetrics`, the metrics the dashboard and the alerts read, missed
    `spacetraders_ship_value_credits` and the two construction gauges; they are listed now.
- Tests: `PrometheusMetricsServiceTests` (each ship's ledger summed by category; a ship without rows has an empty ledger;
  the agent's contract payments are no ship's; the value is still the ship and its equipment), `PrometheusAutomationMetricsTests`
  (one series per category; a category that ages out and a ship that is gone lose theirs), `PrometheusMetricsIntegrationTests`
  (the grouping against PostgreSQL 16; the agent of the reset before, with the same ship symbols, counts for none) and
  `MetricsEndpointTests` (the metric is listed). App 1074, Domain 72, API 206 (and 4 skipped), Integration 1; the API's
  integration tests 6. In gembernodes, the panel's seven queries with `promtool test rules`, the table in Grafana 11.6.1
  against a local Prometheus at 1600 and 390 pixels wide, and `scripts/validate.py` (NOTES.md).
- To understand this, start with the ledger query in `SpaceTraders.API/Services/PrometheusMetricsService.cs`, then the end of
  `Details` in `PrometheusAutomationMetrics.cs`; in gembernodes, the "Profit by ship" panel of
  `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`.

**2.17 The trading plan's order under the market tree** (done: merged as projects#168 on 2026-10-04 and gembernodes#65
on 2026-10-05; asked on 2026-10-04, D75)
- Asked: "Can you, in a new pr, add the exact logic to the market tree view that is used to determine which trade is
  done first?" The market tree shows, per good, where it is cheapest and where it sells best; the trading plan ranks
  routes per trader, with what Grafana doesn't have: the trader's hold, fuel and position, and the credits.
- Done:
  - **`GET /status/trading-routes`** (`StatusEndpoints`): the trading plan's state as its last pass stored it
    (`plan_states`, `TradingAutomation`). The routes traders hold come first (`Assigned`, with the ship), then the
    lucrative routes no trader holds (`Pending`), numbered from 1 (`position`) in the order the plan gives them out:
    one that feeds a pricier good first (D15), then the most profit after fuel (`TradeRoutePlanner.Rank`). Each with
    its units (D56, D74), profit after fuel and per unit, what it feeds, and the free traders that could take it.
    `updatedAt` says when the plan last changed it. Behind the API key, like every `/status` endpoint.
  - **In gembernodes:** a table under the market tree, "Trade routes, in the order traders take them", reads it through
    the SpaceTraders API data source (slice 2.15).
- What it shows, and doesn't: the routes as the plan saw them at its last pass with a free trader. Waiting routes are
  listed only when a trader was free, at most 20 (`MaxPendingRoutes`), each in the version of the trader it suits
  best; a route a trader holds keeps the figures it was chosen with. The plan writes its state only when it changes.
- Tests: `ApiIntegrationTests` (the held routes, then the waiting ones numbered in the plan's order, with their figures;
  none before the plan's first pass; the API key). The two that read the state failed before the endpoint existed.
  App 1074, Domain 72, API 209 (and 4 skipped), Integration 1, with slice 2.16 merged in.
- To understand this, start with `TradingRoute` and `/trading-routes` in `SpaceTraders.API/Endpoints/StatusEndpoints.cs`,
  then `SaveStateAsync` in `Automation/TradingAutomationService.cs`; in gembernodes,
  `infrastructure/monitoring/dashboards/spacetraders-markets-dashboard.json`.

**2.18 Why the other goods aren't traded** (built 2026-10-05 on branch `ccr-f3fba810-ie3vm6`, in projects and gembernodes;
asked that day, D76)
- Asked: "Can the new list also add why the other goods are not considered for trading?" The list is slice 2.17's: the
  routes traders hold and the lucrative ones that wait. Why a good isn't in it depends on the trader, so you chose the
  free traders of the plan's last pass, one row per good, for the trader that got furthest, and only the goods with a
  price gap (D76).
- Done:
  - **`TradeRoutePlanner.Judge`**: every route with a price gap that no other trader holds, through `Rank`'s own checks in
    the order they run (`CheckRoutes`, which `Rank` runs too, so the two can't differ): the buy market in reach, the
    sell market in reach from it, a full hold in one purchase and one sale or an ABUNDANT seller (D56, D74), the credits
    (D56), the profit after fuel (D14). Each route says the first check it fails, with its figures so far; the lucrative
    ones are `Rank`'s routes. `TradeRouteJudgement.Why` puts the check in a sentence with those figures, and
    `FurthestPerGood` keeps, per good, the route and trader that got furthest, then the largest price gap.
  - **The trading plan** judges each free trader's routes where it listed their lucrative ones (D13), and its state gets
    `NotTraded`: for each good with a price gap that no listed route carries, by system, the check that failed, the
    trader, the route, the sentence and when it was found. A good whose furthest route is lucrative ranks below the 20
    waiting routes the state keeps (`MaxPendingRoutes`). A system without a free trader at a pass keeps what was found
    there before, less the goods now listed. Only a change is written, as before: when it was found doesn't count as one.
  - **`GET /status/trading-routes`** serves them as `notTraded`.
  - **In gembernodes:** the trade routes table lists them after the routes, as "not traded", with a "why not" column.
- What it shows, and doesn't: the reasons as the checks found them for the traders that were free. Ships that gather in
  their spare time (D34) aren't judged. A route another trader holds isn't either, and its good is in the list.
- Tests: `TradeRoutePlannerTests` (Judge's lucrative routes are Rank's; each check; the figures; held routes left out),
  `TradeRouteJudgementTests` (each sentence; the furthest per good), `TradingAutomationServiceTests` (the reasons in the
  state; for the trader that got furthest; kept with their time while no trader is free; dropped once the good is
  listed; below the listed routes; written once while a trader waits) and `ApiIntegrationTests` (`notTraded`). They
  failed before `Judge` existed. App 1095, Domain 72, API 210 (and 4 skipped), Integration 1.
- Noticed (not changed): a ship that gathers in its spare time and takes a route that waited for a trader (D34) holds it,
  but the route stays among the waiting ones too (`TradeInsteadOfGatheringAsync` doesn't take it out of `pending`), so
  the state and the table list it twice: held, and waiting.
- To understand this, start with `CheckRoutes` and `TryEvaluateFrom` in `Trading/TradeRoutePlanner.cs`, then
  `Trading/TradeRouteJudgement.cs`, then `SaveStateAsync` in `Automation/TradingAutomationService.cs`; in gembernodes,
  the trade routes panel of `infrastructure/monitoring/dashboards/spacetraders-markets-dashboard.json`.

**Phase 2 in short** (done 2026-10-01; the dashboard and alerts merged in gembernodes PR #10, the Grafana restart pending)
- Prometheus can scrape the bot (port 9090, no key), and every number the dashboard needs is a
  metric: credits, the ledger by category, each ship's state, contracts, the API by endpoint,
  messages, goal steps, anomalies, the database and the next reset (2.1). Sales, purchases and
  contract payments reach the ledger and the credit samples, and price history is recorded (2.2).
  One journal line per meaningful thing makes the log read as a timeline (2.3). Only settings
  that do something are seeded (2.6). Slice 0.5 cleaned up after the soak test.
- Verified with a local run against the live API (2026-10-01, the debug agent, an idle fleet):
  the scrape on 9090 without a key, no `/metrics` on the main port, API requests by route
  template, no API call from the startup snapshot (B35), and the journal's `ShipIdle` and
  `SettingChanged` lines in the JSON log. Then the dashboard's and alerts' queries, against
  `promtool` and a local Loki.
- Traps it took: the domain events had handlers but no publisher, so publishing them woke
  handlers that had never run (the probe plan's, which ignored the plan switches; the credit-drop
  alert, B37); Serilog quotes strings in the rendered message, hence `{EventKind:l}`; and the
  project-file analyzer walks folders itself, so an MSBuild exclude can't keep `node_modules` from
  it.
- Found and recorded: B37 (with D12, open) and B38. Noticed: mined and traded sales share
  `TradeSell`; Wolverine retries every handler of an event when one fails.
- To understand this phase, start with `docs/HOW_IT_WORKS.md` section 11 (metrics and the
  journal), then `PrometheusAutomationMetrics.cs`, `PrometheusMetricsService.cs`,
  `JournalEvents.cs` and `AgentCreditsUpdates.cs`; in gembernodes,
  `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`.

### Phase 3: Health rules (the bot checks itself)

**3.1 Rule mechanism** (done)
- Do: evaluate the rules every minute against the bot's own state.
  - Each violation is an anomaly: rule, subject, since when, details.
  - It's exposed as `spacetraders_anomaly_active{rule,subject}` and as AnomalyRaised and
    AnomalyCleared events.
  - Thresholds are settings.
- Done when: there's a unit test per rule.
- Done:
  - `HealthMonitorService`, the last step of the startup chain, evaluates every `IHealthRule` at
    start and then every minute, each in the scope of one evaluation. A subject that breaks a rule
    is an anomaly: the journal logs `AnomalyRaised` at Warning, with `Rule`, `Subject` and `Details`
    (what the rule saw, its limit and the setting that holds it), and the metric turns 1. Once the
    rule holds again: `AnomalyCleared`, with `ActiveMinutes`, and 0. "Since when" is the raised
    line's time.
  - Anomalies live in memory: after a restart the first evaluation raises the ones still there
    again. A rule that throws is logged at Error and keeps its anomalies; the others carry on.
  - The rules count time no earlier than the monitor saw the bot able to work
    (`HealthCheckContext.WorkingSince`: automation and the plan on, API calls not paused after a
    502), so switching automation off and on, or a restart, doesn't look like a stall. Conditions
    without a timestamp of their own get one from the monitor's memory (`HeldSince`).
  - Thresholds are eight `Health.*` settings. Claude picked the defaults (3.2), from the soak test
    where it had data; they're yours to change. `Health.` isn't a strategy prefix, so a change
    doesn't start a new run.
  - The size guard's two limits stay in the guard, where 1.6 made them the first anomalies: its hard
    limit has to act before the tick starts.
  - `HealthMonitorServiceTests` covers raising, clearing, one anomaly per subject, a failing rule
    and the clocks. Every rule has its own tests (`tests/SpaceTraders.Application.Tests/Health`).

**3.2 First set of rules** (done). Each one states an intended behaviour:
- **Contracts:**
  - an accepted contract makes progress within N hours;
  - a fulfilled contract has no active plan or assignment;
  - a deadline within N hours comes with at least X% delivered.
- **Ships:**
  - a ship with a goal changes state within N minutes, unless it's in transit;
  - a ship without a goal is idle for at most N minutes;
  - the circuit breaker hasn't tripped.
- **Errors:** the same error repeats at most N times in 10 minutes.
- **Credits:** they change at least once in 24 hours while automation is enabled.
- **API:** no 401 or reset errors, and at most N 429s an hour.
- **Database:** under the soft size limit.
- Done when: a deliberately broken scenario in a test host raises each rule.
- Done:
  - Ten rules, plus the size guard's: `ContractStalled`, `ContractLeftOpen`,
    `ContractDeadlineAtRisk`, `ShipStuck`, `ShipLeftIdle`, `CircuitBreakerTripped`,
    `RepeatingError`, `CreditsUnchanged`, `ApiUnauthorized`, `ApiThrottled`, and `DbSizeSoftLimit`.
    `docs/HOW_IT_WORKS.md` section 12 has what breaks each, and its setting.
    `HealthRuleScenarioTests` breaks one thing per rule in the real host and sees its anomaly
    raised, through the real log pipeline, API client handlers and circuit breaker where a rule
    reads them.
  - D13: the idle-ship and credits rules count only while work waits. A ship is "left idle" only
    while a plan that is on has work it could give it; the credits must change daily only while
    ships have work. Taken literally, both would have stayed active for the rest of every first-run
    reset.
  - "Changes state" (`ShipStuck`) means the bot updates the ship (its `LastSyncedAt`: nav, cargo,
    fuel, cooldown), not its nav state: in the soak test a contract drone stayed in orbit with the
    same assignment for up to 69 minutes while it extracted every 71 seconds, so the metrics' time
    in state (2.1) would flag a working miner. The clock starts at the arrival at the earliest, and
    the ship has to look stuck at two evaluations in a row: the fleet is loaded with arrivals
    dead-reckoned, which drops the arrival time for the seconds until the arrival handler docks it.
  - The contract rules judge the contract the bot works on: the contract plan's, accepted and
    unfulfilled, while the plan is active, waits for a ship or budget, or found no asteroid for its
    mineral. A non-mineral contract is parked by decision (D2) and left out. A contract that missed
    its deadline is an anomaly whatever was delivered.
  - The circuit breaker rule counts a blocked goal, or a trip in the last hour: mining and trading
    replace a blocked goal at once, so the breaker now remembers each ship's last trip.
  - Warnings count as errors: this codebase logs "something is wrong, carrying on" at Warning, like
    B31's missing deliverable on every tick in the soak test, or 1.9's ship that can't find a route.
    Journal lines don't count (their own rules watch them), nor does what startup logged. A Serilog
    sink hands every warning and error to the rule.
  - The API rules count the 401s and 429s that the client's innermost handler sees, over the last
    hour. A 401 during startup is agent bootstrap trying old tokens, and a reset stops the host
    (1.8), so `ApiUnauthorized` means a token the server rejects for another reason.
  - Defaults: a contract may go 4 hours without a delivery (the soak test's took 23 to 69 minutes);
    24 hours before the deadline, half delivered; a working ship 30 minutes without an update; an
    idle ship 10 minutes while work waits; one statement 5 warnings or errors in 10 minutes (once a
    minute is 10); credits unchanged 24 hours (the rule's own number); 10 429s an hour (none are
    expected, and a wrong reading of the burst limit would give dozens).
  - Noticed:
    - Once phase 6 switches plans on, expect `ShipStuck` for B17's drones and `ShipLeftIdle` for the
      starting probe (B25): the rules show known issues, as they should.
    - A burst of 429s raises `RepeatingError` next to `ApiThrottled`: the 429 handler logs a warning
      per retry.
    - Grafana repeats the anomaly email every 12 hours (gembernodes' notification policy) for as
      long as an anomaly lasts, such as a contract that missed its deadline.
    - `/status/anomalies` is an older credit-growth heuristic, unrelated to these anomalies.
      `docs/HOW_IT_WORKS.md` listed it among endpoints that read tables nothing writes, which
      stopped being true with 2.2; corrected.

**Phase 3 in short** (done 2026-10-01)
- The bot checks itself: every minute ten health rules, and the size guard every 5 minutes, compare
  its state with what it is meant to do, and each broken rule is an anomaly with a subject, details,
  a metric and journal lines, which the "anomaly active" alert (2.5) turns into an email after 15
  minutes. D12 removed the credit-drop alert; D13 keeps idle ships and unchanged credits quiet while
  no work waits.
- Traps it took: what "a ship changes state" means (a mining drone works for an hour in one nav
  state); the fleet's dead-reckoned arrivals, which hide the arrival time for a moment; and two
  rules that, read literally, the first run's own decisions would have broken.
- To understand this phase, start with `docs/HOW_IT_WORKS.md` section 12, then
  `HealthMonitorService.cs`, `HealthCheckContext.cs` and one rule such as `ShipStuckRule.cs` (all in
  `SpaceTraders.Application/Health`), and `HealthRuleScenarioTests.cs`.

### Phase 4: Back on the cluster (gembernodes)

**4.1 Database logins** (done by hand; one step left, see below)
- Do: check the connection string in the 1Password item `spacetraders-secrets`.
  - It should use a dedicated, non-superuser login that owns only the `spacetraders` database;
    create both if they're missing.
  - Add a read-only login (for example `spacetraders_ro`) for Claude on your PC.
- Note: a separate database on the shared server doesn't cap disk usage. That's the job of 1.6
  and the alert in 2.5.
- Prepared: `apps/spacetraders/README.md` in gembernodes (4.2's PR) has the steps and the SQL:
  - the login `spacetraders`: not a superuser, can't create databases or roles, and owns the
    `spacetraders` database, which only it and the read-only login may connect to;
  - the read-only login `spacetraders_ro`: read-only transactions, and reading rights on every
    table the bot creates (default privileges), except `stored_credentials` after the first start,
    because it holds the agent token;
  - the four fields of the 1Password item, the connection string pointing at
    `postgresql.flux-system.svc.cluster.local` like every other app's since 2026-09-26.
- Tested against PostgreSQL 18 (the cluster runs 18.4), with the published bot:
  - Started as `spacetraders` against the empty database, the bot created its 28 tables, with
    `cached_ships`'s storage parameters (B32), and its second start used them. It needs no more
    than that: it creates the database only when it's missing, and `pg_database_size` (1.6)
    works for the owner.
  - `spacetraders_ro` could read the tables the bot created after the grants, but not write, even
    with read-only switched off; after the revoke it couldn't read `stored_credentials`.
  - Another app's login couldn't connect, and `spacetraders` couldn't create a database.
- Done by hand before 4.2 was merged: both logins exist. Checked on 2026-10-02 with
  `tools/investigate/st.py check` (5.1): `spacetraders_ro` connects, isn't a superuser and is
  read-only. Its password is in psql's password file on your PC.
- Step 3 of the gembernodes README, `REVOKE SELECT ON stored_credentials FROM spacetraders_ro;`, was
  done by you on 2026-10-02: `st.py check` reports `can_read_agent_token = f`. `st.py` still refuses
  any query that names the table.

**4.2 Manifests** (done: gembernodes PR #11, merged 2026-10-02)
- Do: restore `apps/spacetraders/`, the namespace and the ingress (from `3f9f785^`), with these
  changes:
  - Prometheus annotations for the metrics port;
  - a ConfigMap with Serilog overrides;
  - no route to the old `spacetraders-app-service`, because that project no longer exists;
  - the WebUI deployment comes back too (D5);
  - a LAN-only ingress (D11, B22), modelled on `grafana-internal-ingress.yaml`:
    - no public hosts;
    - `nginx.ingress.kubernetes.io/whitelist-source-range: "192.168.0.0/16,10.0.0.0/8"`;
    - `/spacetraders/api` and `/spacetraders/dashboard` on http://192.168.1.230;
    - no certificate needed;
  - `/health/startup` as the startup probe (B23).
- The Prometheus annotations are `prometheus.io/scrape: "true"`, `prometheus.io/port: "9090"` and
  `prometheus.io/path: /metrics` (2.1). Don't add 9090 to the Service.
- Noticed (2026-10-01): gembernodes is replacing ingress-nginx with Traefik
  (`docs/ingress-nginx-migration-plan.md`; phases 0 to 3 done). Traefik serves the existing
  Ingress objects, nginx annotations included, so the above still works; but once ingress-nginx
  is gone (its phase 4) the LAN address may be `192.168.1.231` rather than `.230`. Model the
  ingress on `grafana-internal-ingress.yaml` as it is by then.
- Done, in gembernodes PR #11; Flux deploys it once that's merged, which waits for 4.1.
  - `apps/spacetraders/` (the API and the WebUI, their Services, the 1Password item, the API's
    environment, `secret.yaml.template` and a README with the steps by hand),
    `namespaces/spacetraders-namespace.yaml` and `ingress/spacetraders-ingress.yaml`, each in its
    kustomization. Both images are main `12443acb`, the last build, with phases 1–3.
  - The API runs one pod and never two (`strategy: Recreate`): the rate limit is per account, and
    startup sync and recovery call the API before the leader lease decides which pod runs the
    tick.
  - Probes: startup `/health/startup`, every 10 s for up to 10 minutes; liveness `/health/live`;
    readiness `/health/ready`, which checks the database. The startup probe only works with B39
    fixed: with the current image it passes as soon as the HTTP server is up.
  - The Prometheus annotations point at 9090, which the Service doesn't route. The Prometheus
    chart's `kubernetes-pods` job (checked in 25.30.2) adds the `namespace` label that the
    dashboard and alerts select on.
  - The ConfigMap is `api.env`, through a `configMapGenerator`, so a change rolls the pod:
    Production, port 8080, `Metrics__Port` 9090 next to the annotation that points at it, and
    Serilog's default level, with how to override one. Checked with the published bot:
    `Logging__LogLevel__Default=Warning` (the old ConfigMap's style) left Information lines in the
    log, and `Serilog__MinimumLevel__Default=Warning` removed them.
  - Each container reads the secret fields it needs by name, instead of the whole item: the API
    four, the WebUI only the API key. A missing field keeps the pod in
    `CreateContainerConfigError`, which the existing "Container crash-looping or failing to start"
    alert reports.
  - A wait-for-postgres init container, like armabotcs and curatool, so a node reboot doesn't
    crash-loop the bot; and 60 s to stop, the app's own shutdown timeout.
  - The ingress follows `grafana-internal-ingress.yaml`: no host, no certificate, the LAN
    allowlist, `/spacetraders/api` and `/spacetraders/dashboard`. Traefik serves it on
    192.168.1.231, where the router points since the migration's phase 3, and ingress-nginx on
    .230 until it's removed.
  - Checked with `kustomize build` and `kubeconform -strict`.
  - Found on the way, and fixed, each reproduced first: B39 (`StartupProbeTests` got 200 while
    the chain ran) and B40 (nginx with the image's configuration logged 10 lines for 10 probes,
    and none after the fix). Both reach the cluster with the next image: both image tags in
    gembernodes move to the first `main` commit after them whose build pushes images.
  - B41 kept that from being `41a7c22` (#114's merge): its build failed in a test that had passed
    on the PR, because test hosts shared Serilog's static logger. `HostLoggingTests` reproduces it:
    after another host has started and stopped, a host's warning reached no error log at all.
  - Noticed:
    - Each poll of the startup probe while the chain runs logs one Warning ("Health check
      startup with status Degraded"): a few lines per start, before the health monitor starts,
      so `RepeatingError` doesn't count them.
    - Only the tick checks the leader lease: startup sync, recovery and the arrival timers
      (`ShipEventScheduler`) run in every pod. One replica and `Recreate` make that moot; more
      replicas would need more than the lease, although its comment says it covers them.

**4.3 First-run watch**
- Unpause the "SpaceTraders bot is down" alert (2.5). Done in 4.2's PR: it's live once that's
  merged and Grafana has restarted (the restart 2.5 still needs). Restart Grafana once Prometheus
  has scraped the bot, or the alert fires for its first minute without a series.
- Only the scout and contract plans are on (D9).
- First hour: messages per minute, database size, log lines per minute, anomalies. Then check
  again after 24 hours, then after a full reset period.
- Also check that the count of real 429s stays at zero (1.10).
- Phase 6 starts after a clean reset period.
- Started 2026-10-02:
  - Gembernodes PR #11 was merged at 08:48Z; the bot started at 08:50Z on main `3373ca7`, registered
    agent SPECTER, accepted a COPPER_ORE contract and bought a drone for it. Grafana was restarted
    at 09:09Z: the "bot is down" rule is no longer paused.
  - B39 and B40 hold on the cluster: the startup probe got 503 until the chain had completed, and
    the WebUI logs no probes.
  - The first six minutes: database 9.5 MB, 86 API calls and no 429s, about 28 messages and
    29 log lines a minute (about 42,000 lines a day while the scout works, under the budget of
    50,000).
  - Found: B42 (`RepeatingError` raised at 08:51:46 and cleared at 09:01:48), B43 (the ledger
    panels missed the deposit and the drone) and B44 (the error-log email at 08:55:50). B42 and B43
    are fixed in this repository; B44 in gembernodes PR #13.
  - Noticed: "Credits in the last hour" and "Credits per hour" show no data in the bot's first hour
    on the cluster. They subtract the credits of an hour ago (`offset 1h`), which didn't exist yet;
    across restarts `max()` keeps them working.
  - At 10:50Z, two hours in, with 5.1's helper: four starts, one per deploy (08:50, 09:57, 10:02,
    10:29), and no other restart; about 310 log lines an hour, now that the scout has finished (some
    7,500 a day); database 9.5 MB; no 429s and no failed API call in the last hour; no anomaly
    active. 5.1 found B45 in this run.
  - Still to do: the 24-hour check, and a full reset period.

### Phase 5: Claude as mechanic (on your PC)

**5.1 The `/st-investigate` skill** (done: `SpaceTraders/.claude/skills/st-investigate/SKILL.md`)
- The draft marks which data sources arrive with phases 2–4. Finish it once they exist, and
  check it against a real run.
- Input: an anomaly (rule plus subject), or `week`.
- Reads:
  - **Prometheus and Loki:** anomalies, metrics, journal events and errors. Access through
    Grafana's datasource proxy at http://192.168.1.230/grafana, or through
    `kubectl port-forward` to `grafana-loki:3100` and `prometheus-server`.
  - **Postgres:** current state at 192.168.1.232, with the read-only login.
  - **Pod status:** mcp-k8s, which is read-only.
- Does:
  - finds the code path;
  - writes a failing test that reproduces the misbehaviour, then fixes it;
  - opens a PR with the evidence (queries and log lines).
- Never: changes settings, or tunes thresholds, budgets or priorities. Observations about
  strategy go under "Noticed".
- Done (2026-10-02):
  - `tools/investigate/st.py` reads every source from your PC: `check` (the pods, Prometheus, Loki
    and the database in turn), `prom`, `logs` (with `--group`: warnings and errors per statement)
    and `sql`. This PC has no psql, jq or logcli, so it is Python with the standard library only. It
    starts and stops the port-forwards itself, runs psql from the `postgres:17` Docker image, reads
    the password from psql's password file, and masks tokens and passwords. Its README says how.
  - Grafana's datasource proxy needs a Grafana login, so the helper goes to Prometheus and Loki
    through `kubectl port-forward` instead. The internal API is left out: its key also unlocks
    `PUT /settings/*` and `POST /control/*`, and the database and the metrics hold the same state.
  - The skill: the helper's commands, the queries the run proved useful, and what the run taught
    (`Tick` marks a tick's lines and not a handler's, so two paths acting on one ship show
    interleaved; the startup probe's expected warnings; quoted PascalCase columns; plan state as
    JSON; a metric with no series as evidence). Its procedure now checks first whether a symptom is
    a known B-number and whether the running image has the fix, and lines code up with logs to the
    millisecond when two paths may race.
  - Checked against the first run, with the procedure:
    - **B42's anomaly**, the run's only one, explained: `RepeatingError` was raised at 08:51:46
      and cleared at 09:01:48, on Wolverine's "Utilizing service location" warning (`@i`
      `bd1538c5`). All 15 occurrences came from the first pod (image `3373ca7`), the last four
      at 09:54:38, when the contract's first delivery compiled `FulfillContractDeliveryCommand`'s
      handler: a handler's first message, as B42 says. The fix (`5d72e4e`, projects PR #116) runs
      since 09:57:50 (gembernodes PR #14), and none came in the three starts since.
      Reproduced: with the fix reverted, `HandlerCodegenTests` fails ("18x Utilizing service
      location …"); with it, it passes.
    - **B45, found:** grouping the run's warnings by statement showed one "re-created missing
      assignment" warning from the scout plan, at 09:18:58. The ship's lines, the plan's state in
      the database and the market metrics showed that the plan had skipped its last stop, and the
      lines' `Tick` property and millisecond times showed which two paths raced. Fixed with a test
      that interleaves them, in its own PR.

**5.2 Permissions** (done; your go-ahead of 2026-10-02)
- Allowed without asking: `kubectl get`, `kubectl logs`, `kubectl port-forward`, `psql` with the
  read-only login, and reads from Grafana. Everything else asks.
- Waits for 4.1, because the read-only login doesn't exist yet. These rules widen what Claude may
  do on your cluster without asking, so they go in only with your explicit go-ahead.
- Done when: the skill reproduces and explains one real anomaly from the first run. Done: B42 (5.1).
- Done: `SpaceTraders/.claude/settings.json` allows `Bash(python tools/investigate/st.py *)` and
  `Bash(kubectl -n spacetraders logs *)`, and nothing else. A session started in the `SpaceTraders`
  folder reads them.
  - The helper covers the port-forwards, the pods and their events, Prometheus, Loki and psql with
    the read-only login. Reads from Grafana aren't needed.
  - Not `kubectl get` as such: it reads Secrets too (`kubectl get secret -o yaml` shows the agent
    token, the API key and the database password), and a rule that matches the start of a command
    can't keep them out (`kubectl get pods,secrets`).
  - The helper is only as read-only as its code, which is in this repository; the database login
    is read-only on the server, whatever the code does.

**Phase 5 in short** (done 2026-10-02)
- Claude can investigate the running bot from your PC, read-only: `tools/investigate/st.py` reads
  the pods, Prometheus, Loki and the database (as `spacetraders_ro`), and the `st-investigate` skill
  turns a symptom into evidence, a test that reproduces it and a fix (5.1). Two commands run without
  asking: the helper and the bot's pod logs (5.2).
- Checked against the first run: the procedure explained B42's anomaly and reproduced it with its
  test, and found B45, the scout plan skipping a stop when a tick and an arrival race.
- Traps it took: this PC has no psql, jq or logcli; Grafana's API needs a login; plain `kubectl get`
  reads Secrets; the read-only login could read the agent token until 4.1's revoke (done the same day); Wolverine's
  warnings run to dozens of lines; and `sed -i` in Git Bash turns CRLF files into LF.
- To understand this phase, start with `.claude/skills/st-investigate/SKILL.md`, then
  `tools/investigate/README.md` and `st.py`'s `check`; for an example of the procedure, B45 in the
  known issues and `ScoutStopSkippedTests`.

### Phase 6: Make money, one loop at a time

A loop counts as done after a full reset period with no open anomalies for it on the dashboard.
How credits are split stays your call; Claude only fixes deviations from intended behaviour.

Checked after the reset of 2026-10-04 13:00Z, on the run that ended then (on the cluster since 2026-10-02 08:50Z, the last
2.2 days of its period): 6.10b's and 6.10c's checks are met, and 6.11's plan explored a system. The contract ran end to end
twice, the second time under the new agent (accepted at 13:06Z, fulfilled at 16:12Z), and the command ship went straight on
from scouting to the contract (6.2). No loop has had its full period yet; the first began at 13:00Z, with every plan on
since 18:09Z. The run's anomalies came from bugs since fixed (B42, B51, B55), a short outage of the shared Postgres, and
B59's 429s. The checks found three
more bugs: B38 is still open, B47's DRIFT fallback still flew the contract's trip, and a new one, B61, stores a market twice
when it is seen for the first time.

- **6.1 The first contract, end to end:** B8 and B9 (both fixed in 1.14; what's left is a clean
  reset period). Per D1 and D2 the bot takes one mineral contract per reset; taking the next
  contract is a later addition.
- **6.2 The command ship after scouting:** B10, and the scout part of B16 (both fixed in 1.14;
  what's left is a clean reset period). The ship moves on to its next job instead of holding on to
  the finished scout goal.
- **6.3 Probes** (merged 2026-10-02 as projects#129, deployed by gembernodes#28; built on branch `claude/spacetraders-more-scouting`, with your decisions
  D29 and D30; fixes B15 and B25). Asked that day: "I'd like more scouting to be done": a probe at
  every market in the long run, bought while the credits stay at 100,000; while there are fewer
  probes than markets, they drift between nearby markets, those not updated for a while first.
  - Done:
    - **Which ships are probes** (`FleetRoles.IsProbe`): a probe frame, or the type a probe is cached
      with, `SHIP_PROBE` when bought and its role `SATELLITE` after startup sync. The starting probe
      is one (B25). No other plan uses a probe: it has no hold, no tank and no mounts.
    - **Buying** (D29): while the headquarters' system has fewer probes than markets (26 in X1-DC53),
      the plan buys a SHIP_PROBE at the shipyard that sells it for the least (A2, 81,645 on
      2026-10-02), at most one a tick, as long as the purchase leaves the credit reserve
      (`FleetExpansion.MinCreditReserve`, 100,000): the first from 181,645 credits. Probes in flight
      count (B15). The 200,000 gate (D4), and the credits handler that woke the old plan, are gone.
    - **Roaming** (`Probes/ProbePlanner.cs`, no I/O): each tick every free probe (no flight, not in
      transit) gets a market that is due, its prices older than `Market.RefreshMinutes` (5), with no
      probe at it or on its way. Each pair of free probe and due market is scored by the market's age
      minus twice the flight there (CRUISE, as the API reckons it), and the best pair goes first, so
      a market goes to the probe nearest it: a market 10 minutes away must be 20 minutes staler than
      one next door. A market never seen is the oldest (J58, which B45 skipped). With a probe at every
      market nothing due is left without one, and the probes stay; the market watch keeps their
      markets fresh. Simulated on X1-DC53's 26 markets over a day, the prices' average age is about an
      hour with one probe, 36 minutes with two, 22 with three, 13 with five and 7 with ten; of the
      weights 1 to 6, 2 kept them youngest without leaving the far markets much older.
    - **The flight** (`DeployProbeGoalExecutor`): one goal per flight, in CRUISE. A probe has no tank,
      so no flight costs it fuel, and DRIFT (where the old plan parked probes) would make it ten times
      slower: a probe found in DRIFT is switched to CRUISE first. The arrival fetches the market and
      the shipyard, as every arrival does; then the goal ends and the plan chooses again.
    - **A purchase fetches a probe** (D30, `Services/ShipyardCalls.cs`): the API sells a ship only
      where one of ours is. `ShipPurchaseService` checks that first: without a ship there it makes no
      API call and records a call at the shipyard. The probe plan sends the nearest free probe, which
      stays while the call is open (2 minutes after the last attempt), and the next attempt buys.
      Every plan buys this way: the mining plan's drones at H52, the trading plan's shuttle at A2
      (which, with none of our ships at A2, would have failed at the API) and the probes. New journal
      kind `ProbeCalled`.
    - **The price at the moment of purchase:** with a ship at the shipyard, the purchase fetches the
      shipyard again and keeps the reserve with the price it asks now. A cached price can be hours
      old, and every purchase moves it (SPECTER-3 cost 46,885, SPECTER-4 48,328).
    - **The plan's state** lists every market with the probe at it or on its way, or another ship of
      ours at it, or when it is due; the next probe's shipyard and price and why it isn't bought
      (`Purchase`: `WaitingForCredits`, `WaitingForAShipAtTheShipyard`, ...); and the open calls.
      Written only when it changes. Journal: `PlanStarted` once, `PlanBlocked`
      (`waiting_for_credits`) when it starts waiting, `ProbeCalled`, and `ShipPurchased`.
    - `ShipLeftIdle` (D13): a due market that no probe or ship watches is work for any probe; a probe
      parked at its market while every market is watched is not idle.
    - The fleet view: a probe flying to a market is "scouting", one fetched to a shipyard "called to a
      shipyard", one without a flight "watching its market" (they said "deploying" and "idle").
  - Noticed (not changed):
    - Purchases share the credits above the reserve, plan by plan in the tick's order (probes, then
      mining, then trading), and as the credits grow a cheaper ship's bar comes first: a drone (48,328)
      from 148,328 credits, a probe from 181,645. While the mining plan wants drones (D28), probes may
      wait. How the credits are split is yours.
    - A roaming probe has no goal for up to a tick after each arrival, and the 10-second sampler can
      catch that: a `ShipIdle` (`goal_ended`) journal line for about half its flights, as for a miner
      after each trip.
    - The flight weight (2) and a call's 2 minutes are constants, not settings.
    - A probe at A2 is SCARCE, so its price rises as probes are bought: 26 probes cost well over 2
      million credits.
  - To switch it on: `PUT /settings/Automation.Plan.ProbeDeployment.Enabled` with `{"value": "true"}`.
    The setting's description in the cluster's database still describes the old plan: a description
    is seeded once per agent, so the new one comes with the next reset.
  - Done when: a full reset period with the probe plan on and no open anomaly for it.
- **6.3 in short** (merged 2026-10-02 as projects#129): every market of the headquarters' system gets a probe in the
  long run, bought while the credits stay at the reserve; until then the probes roam, each to the
  market whose prices are oldest once the flight there counts against it; and a purchase where none
  of our ships is fetches a probe first. To understand this, start with
  `SpaceTraders.Application/Probes/ProbePlanner.cs`, then `Automation/ProbeDeploymentPlanService.cs`,
  and `Services/ShipPurchaseService.cs` with `Services/ShipyardCalls.cs`;
  `tests/SpaceTraders.Application.Tests/Probes/ProbePlannerTests.cs` holds X1-DC53's positions.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Probes/ProbePlanner.cs`, `Services/ShipyardCalls.cs`;
    - rewritten: `Automation/ProbeDeploymentPlanService.cs`, `ProbeDeploymentPlanState.cs`,
      `Goals/Executors/DeployProbeGoalExecutor.cs`;
    - changed: `Services/ShipPurchaseService.cs` and `IShipPurchaseService.cs` (a ship there, the
      calls, the price again, `ShipPurchaseFailure`), `Commands/Fleet/PurchaseShipCommand.cs`,
      `Automation/FleetRoles.cs` (`IsProbe`), `Health/ShipLeftIdleRule.cs`, `JournalEvents.cs`
      (`ProbeCalled`), `DependencyInjection.cs`; `DeployProbeGoal` (Domain: `ForPurchase`);
      `PrometheusMetricsService` (API: the fleet view's words); `DefaultSettingsSeed` (Persistence:
      the probe switch's description, for the next agent);
    - removed: `EventHandlers/ProbeDeploymentCreditsChangedHandler.cs` and
      `Commands/Ships/DeployProbeCommand.cs`, which only the old plan used;
    - tests: `Probes/ProbePlannerTests` (new), `Automation/ProbeDeploymentPlanServiceTests` and
      `Goals/DeployProbeGoalExecutorTests` (rewritten), `Services/ShipPurchaseServiceTests` (a ship
      there, the calls, the price; `ShipyardCallsTests`), `PurchaseShipHandlerTests`,
      `Health/ShipRuleTests` (the probe cases), `AlreadyAtDestinationLoopTests`,
      `PrometheusMetricsTests` (API), and the credits handler's tests removed from
      `LedgerEntryHandlerTests`.
- **6.4 Surveying and mining** (merged 2026-10-02 as projects#122, its dashboard as gembernodes#21,
  deployed by gembernodes#22; it took in the old 6.4, mining drones mine and sell, with the survey
  part of B16, B17 and B34). Asked that day:
  1. a ship that can survey surveys before it trades: the contract's ore first, otherwise ores the
     system's markets buy, at asteroids near the market that buys them;
  2. a miner mines surveyed ores before unsurveyed asteroids;
  3. several ships may mine for a contract; over-mining is fine, the rest is sold;
  4. ships mine ores that are in low supply at a market, and sell them there;
  5. a ship that can mine mines before it trades, so the command ship stops trading, and the trading
     plan buys its own cargo ships;

  and during the work: fix B34 first; a survey dashboard, to see whether we survey too much or too
  little; a credit bar under which only fuel is bought (D24); and the market fetched again after each
  trade (D25). Your choices: D20–D25.
  - Done:
    - **B34, first:** startup sync stores each waypoint's traits and modifiers (and orbitals, parent,
      chart), and fetches a system's waypoints again, once, when a cached one has no traits: the
      cluster's 85 waypoints get theirs at the first start after the deploy, keeping when each was
      last observed. **What an asteroid yields** (`AsteroidDeposits`) follows from its traits, by the
      table community bots use (the game publishes none): XB5C's common metal deposits yielded the six
      ores it lists, about equally, on 2026-10-02.
    - **The survey plan** (`SurveyPlanService`, new switch `Automation.Plan.Survey.Enabled`, off): a
      ship that can survey surveys, and only that (D20). One survey per goal: the contract's ore at
      the contract's asteroid while the contract plan mines it; otherwise an ore a market buys, at the
      asteroid nearest the market that pays most, among those the miners can reach (a drone's 80-unit
      tank keeps it in the middle of X1-DC53, so that is XB5C); ores without a usable survey there
      first, then the best paid. The goal ends after each survey (B16's survey part: survey goals
      never ended), and the plan gives the next.
    - **Extraction** (`MineResourceVolumeCommand`) picks the best usable survey of the asteroid for its
      ore: the largest share of the deposits, then the larger deposit, then the later expiry. A survey
      the API refuses (4224 exhausted, 4221 expired, 4220 not verified) is dropped (B49). Any asteroid
      type can be mined (B48).
    - **The mining plan** (rewritten): one trip per goal, chosen again after each sale. A miner that
      holds ore sells it first, where it fetches most (the contract's leftovers); otherwise a surveyed
      ore first, sold where it fetches most; then an ore in low supply (SCARCE or LIMITED, D22), mined
      at the asteroid nearest that market and sold there. One miner per sell market and ore. The trip
      flies with its goal (B17), through refuelling stops, never DRIFT. It buys drones only for
      low-supply openings a drone could reach, and not while the contract takes the miners (D23).
    - **The contract** (D23): every free miner joins the active contract with an assignment like the
      first ship's; completion releases them all, and a ship that arrives after another fulfilled the
      contract doesn't call fulfil again. With the survey plan on, the command ship surveys the
      contract's ore, and the contract's miners extract with those surveys.
    - **Who does what** (`FleetRoles`): the survey plan is bootstrapped before mining and trading;
      with it on, a ship that can survey neither mines nor trades (D20); a miner trades only when
      neither the contract nor the mining plan has work for it.
    - **Cargo ships** (D21): when every trader has a trip, the trading plan buys the next ship in
      `Trade.ShipPurchases` (`SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER`), when it would
      have a lucrative route from the shipyard, within the credit reserve: the shuttle (117,273) from
      about 217,000 credits, a hauler (354,210) from about 454,000.
    - **D24:** cargo leaves `Trade.FuelReserveCredits` (5,000) untouched, on top of the trip's fuel.
      **D25:** after each purchase and sale, by a trader or a miner, the market is fetched again while
      the ship is still docked (`MarketRefresher`, which the market watch uses too).
    - **The survey dashboard's data:** each survey's life is counted and journaled: `Surveyed` (one per
      survey: deposits, size, expiry), `Extracted` (per extraction, with the survey's signature or
      none), `SurveyEnded` (expired, exhausted or not verified, with how many extractions used it);
      metrics `spacetraders_surveys_taken_total`, `_surveys_ended_total{reason,used}`,
      `_surveys_active{used}` and `spacetraders_extractions_total{surveyed}`. Surveys expiring unused
      mean too many; extractions without a survey while a surveyor works mean too few.
      `cached_surveys` gained `Extractions`; there are no migrations, so the initializer adds the
      column to the cluster's table (`ADD COLUMN IF NOT EXISTS`). The dashboard's survey section is a
      gembernodes change (see the table at the end); its PromQL passed `promtool check rules`.
    - `ShipLeftIdle` (D13) counts survey work for surveyors, contract work for every free miner, and a
      low-supply opening only for the miners that can reach it.
  - Noticed (not changed):
    - `HasMiningEquipment` counts a surveyor mount as mining equipment, and a test asserts it, against
      the comment that says "mining mount or miner-type frame". No ship we have or can buy has a
      surveyor and a hold without a laser, so it changes nothing today. Yours to call.
    - Extraction still jettisons every ore but the one the trip mines, as before. At XB5C without a
      survey that is five extractions in six; keeping what a nearby market buys would fill holds
      faster, at the price of more sell stops.
    - The contract plan picks the asteroid nearest the ship it starts with (by design, B34); with the
      traits stored, its tie-break by deposits now works. Whether an asteroid that yields the ore
      should beat a nearer one that doesn't is still your call.
    - Asteroid modifiers (STRIPPED, UNSTABLE, ...) are stored but not used.
    - The survey request sends the cached survey back to the API; whether the API accepts the expiry
      as it comes back from the database is only known once it runs: a rejection shows as
      `SurveyEnded` with reason `not_verified`, and the miner carries on without surveys.
  - Follow-up after the switch-on (2026-10-02, your decision D26, fixes B50; built on branch
    `claude/spacetraders-trip-release`). To understand it, start with `EndTripAsync` in
    `FulfillContractDeliveryCommand.cs`, then `AddFreeMinersAsync` and `ReleaseShipsAsync` in
    `Automation/ContractPlanService.cs`.
    - **A contract trip ends at its delivery:** the delivery closes the ship's contract assignment,
      after the fulfil call when one was due, and the plans assign the ship again on the next tick,
      in their order. A miner rejoins the contract (`MiningStarted`, reason `contract`, now once per
      trip); with the survey plan on, the command ship surveys. A ship whose fulfil call fails keeps
      its assignment and makes the call again: no ship would get a trip with no units left.
    - **The plan's first ship is no longer special:** it got its assignment back on every tick,
      whatever it was doing, and even had another assignment replaced; now it joins like every free
      miner. The two tests of that restore are replaced by tests of the new rule.
    - **A restart reconsiders once** (`StartupRecoveryService`, with automation and the contract plan
      on): every ship on the contract that isn't in flight is released, and the first tick assigns
      it again. A ship in flight keeps its assignment until its delivery: without one, nothing would
      record its arrival, and it would never be free again.
    - Noticed (not changed): a released ship keeps its cargo. A miner delivers it with its next trip,
      but the command ship, once it surveys, carries what it mined for the contract (7 copper on
      SPECTER-1) for good: nothing sells a surveyor's cargo. Surveying needs no hold, so it costs only
      the ore's value. Yours to call.
    - Files: `FulfillContractDeliveryCommand.cs`, `Automation/ContractPlanService.cs`
      (`EnsureActivePlanAssignmentAsync` removed, `ReleaseShipsAsync` added),
      `StartupRecoveryService.cs` (API); tests: `ContractMinersTests` (four new),
      `FulfillContractDeliveryHandlerTests` (two new, one extended), `StartupRecoveryServiceTests`
      (new, API), and the two restore tests removed from `ContractPlanServiceTests`.
  - Second follow-up (2026-10-02, fixes B51; built on branch `claude/spacetraders-b51`). To understand
    it, start with `ExtractWithSurveyAsync` in `SpaceTradersPortAdapter.cs`.
    - **A survey goes back as the API gave it out:** its expiry is written as the API writes it (UTC,
      milliseconds, `Z`, `ExtractWithSurveyRequest.FormatExpiration`).
    - **A survey the API can't read is dropped** (a 422 without a game error code, reason
      `rejected`): one call per survey instead of five a tick for as long as the survey lasts. The
      warning carries the API's response body, whose `data` names what it couldn't read. A 422 with a
      game error code (a full hold, say) stays the API's.
    - Noticed (not changed): Wolverine retries every failed command 4 times (`RetryWithCooldown` in
      `DependencyInjection.cs`), whatever the error, so any API call that fails for good costs five
      calls a tick. Retrying makes sense for 429s and 5xx, not for a 4xx that will fail the same way.
    - Files: `SpaceTradersPortAdapter.cs`, `SpaceTradersApiClient.cs`, `Phase1ActionModels.cs`
      (Infrastructure.SpaceTradersAPI), `Ports/SurveyRefusedException.cs`,
      `Commands/Ships/MineResourceVolumeCommand.cs`; tests: `SurveyRequestTests` (new),
      `SurveyRefusalTests` (two new), `MineResourceVolumeHandlerTests` (one new).
  - Third follow-up (2026-10-02, your decision D27; built on branch
    `claude/spacetraders-survey-stock`). To understand it, start with `SurveyTargets` in
    `Mining/MiningPlanner.cs`, then `EnsureBootstrappedAsync` in `Automation/SurveyPlanService.cs`.
    - **A stock of surveys per ore:** a target needs a survey while fewer usable surveys hold its ore
      than `Survey.StockPerOre` (new setting, 2). The contract's ore comes first only while it needs
      one; then the ore with the fewest usable surveys, then the best paid. With the stock for every
      ore, the surveyor waits until a survey expires or is used up.
    - The plan's state shows each target's usable surveys and whether it needs one; `ShipLeftIdle`
      counts only those that need one, so a surveyor that waits is not an anomaly.
    - **Deployed** by gembernodes#26 at 15:34Z: every ore at XB5C had 17 to 24 usable surveys left over,
      so SPECTER-1 waits until they expire.
    - **Refined the same day** ("Stock per ore per asteroid. I'd like the surveys to be close to
      wherever the mineral can be sold"; branch `claude/spacetraders-survey-per-market`): every market
      that buys an ore gets the reachable asteroid nearest it, not only the market that pays most; one
      target per ore and asteroid, each with its own stock. With only drones as miners nothing changes
      yet: the one asteroid they reach is XB5C, nearest every buyer they can reach. It shows once
      miners reach further.
    - Files: `Mining/MiningPlanner.cs` (`SurveyTargets`, `SurveyTarget`), `Mining/SurveySelection.cs`
      (`CountUsable`), `Automation/SurveyPlanService.cs`, `SurveyPlanState.cs`,
      `Health/ShipLeftIdleRule.cs`, `DefaultSettingsSeed.cs` (Persistence); tests:
      `MiningPlannerTests` (the contract-first test replaced, two new; two more for the refinement),
      `SurveyPlanServiceTests` (three new), `ShipRuleTests` (one new), `DefaultSettingsSeedTests`.
  - Fourth follow-up (2026-10-02, your decision D28; built on branch `claude/spacetraders-mining-rules`).
    To understand it, start with `MiningTargets` and `CompareBestFirst` in `Mining/MiningPlanner.cs`,
    then `BuyDroneAsync` in `Automation/MiningAutomationService.cs`.
    - **What a miner mines:** every market that buys an ore is a target, mined at an asteroid with a
      usable survey holding it or else the asteroid nearest the market. They rank by the market's
      supply first: SCARCE, LIMITED, MODERATE, HIGH, ABUNDANT; within a level, surveyed first, then
      the most an extraction is expected to fetch. So miners serve the scarce markets first, and once
      none is short, keep mining the lowest supply there is, even when it pays less. A trip for a
      market that isn't short logs reason `lowest_supply`.
    - **When a drone is bought:** with every miner working, the plan asks the same ranking what a new
      drone would mine (the trips under way held), and buys it only if that is in low supply (SCARCE or
      LIMITED, D22). One a tick: it used to buy one for every opening at once, and a drone could then
      go to a market that wasn't short, leaving its opening to pay for the next.
    - Files: `Mining/MiningPlanner.cs` (`MiningTargets`, `CompareBestFirst`, `SupplyRank`,
      `MiningTarget.Supply`), `Automation/MiningAutomationService.cs` (`BuyDroneAsync`),
      `JournalEvents.cs`; tests: `MiningPlannerTests` and `MiningAutomationServiceTests` (the
      expectations that assumed surveyed-first rewritten, four new).
  - To switch it on: `PUT /settings/Automation.Plan.Survey.Enabled` and
    `.../Automation.Plan.Mining.Enabled` with `{"value": "true"}` (trading as in 6.5).
  - Done when: a full reset period with these plans on and no open anomaly for them.
- **6.4 in short** (merged 2026-10-02 as projects#122): the command ship surveys (the contract's ore
  first), every free miner works the contract and then mines surveyed or scarce ores for the market,
  one trip at a time; the trading plan buys a shuttle and then haulers, keeps 5,000 credits for
  fuel, and fetches a market again after each trade; asteroids' traits are stored, so the bot knows
  what each yields. To understand this, start with
  `SpaceTraders.Application/Mining/MiningPlanner.cs` and `AsteroidDeposits.cs`, then
  `Automation/SurveyPlanService.cs`, `MiningAutomationService.cs` and `FleetRoles.cs`;
  `tests/SpaceTraders.Application.Tests/Mining/MiningFixture.cs` holds X1-DC53's middle as the tests
  use it.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Mining/AsteroidDeposits.cs`, `SurveySelection.cs`, `MiningPlanner.cs`, `MiningContext.cs`,
      `SurveyKeeper.cs`; `Automation/SurveyPlanService.cs`, `SurveyPlanState.cs`, `FleetRoles.cs`;
      `Goals/Executors/GoalFlight.cs`; `Services/MarketRefresher.cs`; `Ports/SurveyRefusedException.cs`;
    - rewritten: `Automation/MiningAutomationService.cs`, `Goals/Executors/MineAndSellGoalExecutor.cs`,
      `SurveyWaypointGoalExecutor.cs`; `ISurveyRepository` and `SurveyRepository` (Persistence);
    - changed: `ContractPlanService` (D23), `TradingAutomationService` (D20, D21), `TradeContextReader`
      (D24), `TradeBetweenMarketsGoalExecutor` (D25), `MarketWatchService`, `MineResourceVolumeCommand`
      (B48, B49), `FulfillContractDeliveryCommand`, `ShipLeftIdleRule`, `AutomationSwitches` (the
      Survey plan), `GameLoopService`, `JournalEvents`, `IAutomationMetrics`, `TradeMarketMap`,
      `TradeRoutePlanner`, `MineAndSellGoal` (Domain: `Selling`), `ShipyardShipDto`;
      `StartupSyncService` (API, B34), `PrometheusAutomationMetrics`, `PrometheusMetricsService`;
      `SpaceTradersPortAdapter` (the refusals); `ShipyardRepository`, `CachedSurvey`,
      `SpaceTradersDatabaseInitializer`, `DefaultSettingsSeed` (Persistence); two unused goal queries
      removed from `IShipGoalRepository`;
    - tests: `Mining/*`, `Automation/SurveyPlanServiceTests`, `MiningAutomationServiceTests`,
      `ContractMinersTests`, the executors', `SurveyRepositoryTests`, `SurveyRefusalTests`,
      `MarketRefresherTests`, `ShipyardShipCapacityTests`; B34 in `StartupSyncServiceTests`, the
      column in `DatabaseInitializerTests`, and additions to the trading, contract delivery, idle rule,
      game loop and metrics tests.
- **6.5 Trading** (merged 2026-10-02 as projects#121; deployed by gembernodes#20). Asked for that day:
  1. a trader can be any ship with fuel and a cargo bay;
  2. a trip's profit is what the sell market pays minus what the buy market charges, minus the fuel,
     the fuel to get to the first market to begin with included (your note, the same day);
  3. two traders on one route are discouraged, to keep it simple (D18);
  4. when new prices come in, from a ship getting there or from the regular check of the ships at
     markets, the ship reconsiders whether its trade is still lucrative, and if not, goes elsewhere;
  5. goods in the market tree that let pricier goods become available come first (D15);

  and, added during the work: every market with one of our ships at its waypoint refreshes once every
  x minutes. That check didn't exist: markets only refreshed when a ship arrived. Then: a move or a
  trade goes before a market refresh, or any GET, for the rate limit. Your choices: D14–D19.
  - Done:
    - **The trade arithmetic** (`TradeRoutePlanner`, no I/O): profit = (sell price − buy price) ×
      units − the fuel for the whole trip: from where the ship is to the buy market, then on to the
      sell market. A trip is one purchase, as many units as the free hold, both markets' trade
      volumes and the credits allow (D17). Fuel is CRUISE, the distance rounded, paid in whole FUEL
      units (100 each) at the market each leg ends at; the logs of the first run confirm that (a
      222-long leg cost 3 × 79). A flight longer than one tank refuels at markets on the way, the
      fewest stops first: from J57, where scouting ended, one tank reaches only I56, and without stops
      the command ship found no route at all. Never DRIFT (B47).
    - **The trading plan** (`TradingAutomationService`) gives every free trader a trip each tick: a
      ship with a cargo hold and a fuel tank, no goal (or a blocked one), no assignment, not in
      transit. Cargo it holds is sold first, where it fetches most after fuel, when that earns
      anything. The best route goes first, to the trader it is best for; a held route isn't offered
      again (D18). It buys no ships (D16). Its state lists the held routes and the open ones, with the
      ships that could take them (`ShipLeftIdle` reads those, D13), and is written only when it changes.
    - **The trip** (`TradeBetweenMarketsGoalExecutor`) reconsiders where it lands, because a ship
      can't change course in flight, and changing its goal in flight would lose its arrival (B17): at
      the buy market, with the prices its arrival just fetched, it buys while the trip is still
      lucrative and otherwise drops it (`TradeDropped`), and the plan chooses again from there. The
      flight there is spent by then, so that check counts only the fuel still ahead. At the
      sell market, when selling there no longer pays and another market pays more after fuel, it takes
      the cargo there, once per trip (`TradeRerouted`). Sales above the market's trade volume go in
      several. New journal kinds: `TradeStarted`, `TradeRerouted`, `TradeDropped`.
    - **The market watch** (`MarketWatchService`), the last step of every tick, one market a tick
      (D19): each market with one of our ships at it is fetched again once `Market.RefreshMinutes` (5,
      new; 0 = off) have passed since it was last seen, the one that has waited longest first. A market
      that fails, or comes back without prices, waits an interval too. A probe parked at a market keeps
      it current; until the probe plan runs (6.3), that is the starting probe at H52 and wherever the
      other ships are.
    - **Writes before reads** (D19, `RateLimitingHandler`): a GET gives way while a POST waits for the
      budget (before, it also gave way while a POST was in flight, and could use the whole burst),
      leaves the last 10 of the 30-request burst to writes, and stops giving way after 10 seconds.
      The wait metric has a `kind` label, `read` or `write`, which shows whether a write ever waits.
      On 2026-10-02 the bot made 67 GETs and 425 POSTs in 6 hours and waited 0 seconds: this is for
      when probes watch every market.
    - **B46**: one goal step at a time per ship (`ShipGoalStepGuard`); a step that finds its ship busy
      is skipped, and the next tick takes it.
    - The production chains are fetched once per process and shared with the markets dashboard
      (`SupplyChainCache`): still one call per start.
    - A dry run of the planner on the live prices of 2026-10-02 (the command ship at J57, 252 fuel,
      129,451 credits): two routes clear 200 a unit, both feeding nothing. The first trip would be
      MEDICINE from D41 to A1, about 6,982 after 738 of fuel: 648 to get from J57 to D41 by way of
      I56, and 90 on to A1. EQUIPMENT from K85 to D41, which feeds SHIP_PARTS, earns 198 a unit from
      there, the 530 to get to K85 included.
  - Noticed (not changed):
    - Travel time is not in the profit (your formula): a trip across the system counts the same as
      one next door.
    - Prices are as last seen. Most markets were last seen during scouting, hours old; each arrival
      and the watch refresh them, and the trip reconsiders on arrival.
    - The watch adds price samples: 3 markets with ships today is about 4,000 rows a day; with probes
      at all 25 markets about 40,000 (7 days raw, then hourly; the size guard watches).
  - To switch it on: `PUT /settings/Automation.Plan.Trading.Enabled` with `{"value": "true"}`.
  - Done when: a full reset period with the trading plan on and no open anomaly for it.
- **6.5 in short** (built 2026-10-02): idle ships with a hold trade between markets for the most
  profit after fuel, routes that grow a pricier good's production first; each trip checks its prices
  again at both markets, and the markets where ships are refresh every 5 minutes, after the moves and
  trades, which go first for the rate limit too (D19). To understand this,
  start with `SpaceTraders.Application/Trading/TradeRoutePlanner.cs`, then
  `Automation/TradingAutomationService.cs` and `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`;
  `tests/SpaceTraders.Application.Tests/Trading/TradeFixture.cs` holds the live prices the tests use.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Trading/TradeRoutePlanner.cs` (the arithmetic and refuelling flights), `TradeMarketMap.cs`,
      `TradeContextReader.cs`; `Automation/MarketWatchService.cs`; `Goals/ShipGoalStepGuard.cs` (B46);
      `Services/SupplyChainCache.cs`;
    - rewritten: `Automation/TradingAutomationService.cs`, `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`;
    - changed: `TradeBetweenMarketsGoal` (`SpaceTraders.Domain/Goals/ShipGoal.cs`: the trip's plan and
      progress), `GameLoopService` (the watch first), `ShipGoalExecutorService` (the guard),
      `ShipLeftIdleRule` (candidates), `MarketAutomationPlanState`, `JournalEvents`,
      `DependencyInjection`; `RequestBudget` and `RateLimitingHandler` (Infrastructure.SpaceTradersAPI,
      D19); `PrometheusMarketMetricsService` (SpaceTraders.API, the shared cache) and
      `PrometheusAutomationMetrics` (the wait metric's `kind`);
      `DefaultSettingsSeed` (Persistence: `Market.RefreshMinutes`); `GetActiveTradeRouteTargetsAsync`
      removed from the goal repository;
    - tests: `Trading/TradeRoutePlannerTests`, `TradingAutomationServiceTests`,
      `TradeBetweenMarketsGoalExecutorTests`, `MarketWatchServiceTests`, `SupplyChainCacheTests`, B46 in
      `ShipGoalExecutorServiceTests`, the trip's round trip in `ShipGoalRepositoryTests`, D19 in
      `RateLimitHandlerTests`.
- **6.6 Jump gate construction** (merged 2026-10-04 as projects#155, its dashboard as gembernodes#53, deployed by
  gembernodes#52; built on branch `ccr-914173a3-6coo89`, with your decisions D64–D68). Asked that day: "Spacetraders has unfinished buildings at waypoints, specifically an unbuilt jump
  node. Finishing this jump node should be top priority, as it opens up the rest of the game. Can you implement a special
  role that works on this jump gate? Also please check whether the construction command gives any money, if so the new
  role can be set up like a trader, if not it probably needs a different money cap."
  - **Supplying pays nothing:** the API's supply call (`POST systems/{system}/waypoints/{waypoint}/construction/supply`,
    OpenAPI spec v2.3.0) answers with the construction site and the ship's cargo only, without credits or the agent. So
    the role isn't set up like a trader: a load is judged like a ship purchase (D64). A refused supply answers 4800 (the
    site doesn't need the material), 4801 (it has all of it) or 4802 (the ship isn't at the site).
  - **X1-DC53** (public API, 2026-10-04): its jump gate, X1-DC53-I55, was complete before our ships came (FAB_MATS
    1600/1600, ADVANCED_CIRCUITRY 400/400, QUANTUM_STABILIZERS 1/1). Its neighbours X1-HZ59-I59 and X1-BG54-I54 were
    under construction, needing the same, but D68 leaves them out. F49 exports FAB_MATS and D42 ADVANCED_CIRCUITRY. So
    the plan finds nothing to build until the server reset (2026-10-04 13:00Z) gives a new home system.
  - **X1-FJ91**, the home system after that reset: its gate, X1-FJ91-I64, needs 1,600 FAB_MATS and 400 ADVANCED_CIRCUITRY
    (QUANTUM_STABILIZERS 1/1). The plan started at 18:09Z, when slice 2.12 switched it on, with SPECTER-1 as its builder;
    the builder trades while the ships ahead of the gate's loads in the purchase order are bought (D64).
  - Done:
    - **The role** (`FleetRole.Construct`, `FleetRoles.CanConstruct`): a hold and a tank, and no drone or probe. While the
      home gate needs materials, the role board gives it (reason `construction`) to the `Construction.Ships` (new, 1)
      largest holds that are left once the drones and the surveyor are decided, before the trips by profit (D65); of
      equal holds the one that builds now, then the one that can do least else. The gate starting or stopping to need
      materials weighs the roles at once. A builder trades when the construction plan has nothing it may buy. With the
      board off, the plan picks its builders by the same rule.
    - **The site** (`Construction/ConstructionSites.cs`): only the home system's jump gate (D68), fetched when the
      waypoint cache lists it under construction and it isn't cached, then every 10 minutes while it needs materials
      (other agents supply it too), and stored from every supply's answer: `cached_construction_sites`, which nothing
      wrote before. Journal: `PlanStarted` with its materials, `PlanCompleted`.
    - **The plan** (`Automation/ConstructionPlanService.cs`, new switch `Automation.Plan.Construction.Enabled`, off;
      bootstrapped after siphon and before trading): a free ship that holds what the gate needs supplies it first,
      whatever its role (`held_cargo`); then it tells the order ships are bought in what it would buy next, and when
      that lets it, each free builder with an empty hold takes the first load the credits pay for above the credit
      reserve (`purchase`). The state (`plan_states`, `Construction`): each material with what is on its way, the
      builders, those a load waits for, and why none was bought (`purchase_order`, `waiting_for_credits`, `low_supply`,
      `trade_volume`, `no_market`).
    - **A load** (`Construction/ConstructionPlanner.cs`, no I/O): one material in one purchase, a full hold or what the
      gate still needs, at a market whose trade volume takes it at once (D67) and whose supply isn't SCARCE or LIMITED
      (D66); the material with the smallest share supplied or on its way first; the market where the load costs least
      with its fuel, there and on to the gate, in CRUISE through refuelling stops.
    - **The trip** (`SupplyConstructionGoal`, now a `TripGoal` with the load, what it holds back and what it paid;
      `Goals/Executors/SupplyConstructionGoalExecutor.cs`): to the market, where it checks the load again with the
      prices its arrival fetched (dropped as `not_needed`, `not_sold_here`, `low_supply`, `not_full_hold` or
      `over_budget`), buys it in one purchase, flies to the gate and supplies it. A supply the API refuses
      (`ConstructionRefusedException`, from the port adapter) ends the trip at Warning with the cargo aboard, fetches
      the site again, and the ship isn't offered the material again for 10 minutes (`ConstructionRetries`). Each trip
      books its loss (`TripEnded`, activity `construction`, D46).
    - **Money** (D64): a load leaves the credit reserve and waits for tiers 1 to 5 of the order ships are bought in
      (`PurchaseTier.Construction`, 6); the probes (now 7) and the turns of drones and cargo ships (now 8) wait while
      the gate has a load to buy. A trip holds back its cargo from start to purchase (`ReservedCredits`, as D57): the
      trading plan, the trade executor and `BudgetPolicy` (so `spacetraders_credit_reserve`) leave it. The purchases
      have a ledger category of their own, `ConstructionBuy` (`CargoPurchasedEvent.ForConstruction`).
    - **Cargo the gate needs** isn't jettisoned by the trading plan while the construction plan is on.
    - **Visibility:** the journal kinds `ConstructionStarted`, `ConstructionSupplied` and `ConstructionDropped`; the
      metrics `spacetraders_construction_units_required` and `_fulfilled` (`site`, `trade_symbol`); the fleet view's
      "buying … at … for …" and "supplying … to …"; `ShipLeftIdle` counts a load as work for the builders a load waits
      for; `ConstructionSuppliedEvent` is published (an `activity_logs` row).
    - **The dashboard** (gembernodes, same branch): "Jump gate progress" (percent), "Jump gate: materials still needed"
      (a table of what is left, supplied and required per material) and "Jump gate materials" (each material's share),
      under Contracts; the Roles, Purchase order, Spent per hour and Profit per hour descriptions name the new reason,
      tier, ledger category and activity.
  - Noticed (not changed):
    - **The scale:** X1-DC53's neighbours needed 1,600 FAB_MATS and 400 ADVANCED_CIRCUITRY each. If the next home gate is
      like them, an 80-unit hold takes 25 loads, each waiting for a market at MODERATE or better whose trade volume
      takes the hold (D66, D67), and probes and further ships wait meanwhile (D64). The state's `Waiting` and the
      dashboard's Purchase order table show what holds it up.
    - **A trade volume under the hold** holds that material up for as long as it lasts (D67): if no market trades a
      whole hold of it at once (say 40 at a time for an 80-unit hauler), the builder never buys it, the state says
      `trade_volume`, and the probes and further ships keep waiting behind the load (D64). Whether to change that is
      your call; nothing here does.
    - **A gate others finish:** the plan sees it at the next fetch, within 10 minutes. A trip on its way to buy is then
      dropped `not_needed` at its market; one that has bought is refused at the gate (`not_needed`) and keeps its cargo,
      which the trading plan sells where it fetches most, likely below what it cost.
    - **Settings after a reset:** a new agent's settings start at their defaults, so the plan (and the role board) must
      be switched on again for the new home gate.
    - **And exploring** (slice 6.11, merged meanwhile): a jump needs both gates built, so the explore plan can't take
      the command ship while the home gate needs materials; they take turns. The explore plan looks at a gate under
      construction again hourly, so it may set off up to an hour after this plan has seen the gate complete; it could
      read the construction cache instead, which is your call.
    - **The shipyards table** (slice 2.11) judges what a ship for sale could do as the fleet table does, so a cargo ship
      now reads `Trade, Construct` there too; its gembernodes description (on its own branch) doesn't name `Construct`.
    - The dashboard's Purchase order description had no SurveyorPerArea (D55) and the Roles description no
      `gathers_first` (D58); both are fixed with this slice. HOW_IT_WORKS said "8 of the 13 goal kinds" never run; with
      `SupplyConstruction` running it is 6 of 15.
    - Left as they were: the Qowaiv warnings (QW0028, QW0029) in `ShipGoal.cs` and `DomainEvents.cs`, which ask for
      strongly typed identifiers across the domain. The five S8969 warnings in `ShipGoalRepositoryTests.cs` are fixed.
  - To switch it on: `PUT /settings/Automation.Plan.Construction.Enabled` with `{"value": "true"}`, with the role board on
    (`Automation.Plan.Roles.Enabled`) so the role shows on the dashboard; after the reset, on the new agent.
  - Done when: the home gate is complete with the plan on, and no open anomaly for it.
  - **To understand this,** start with D64–D68, then `Construction/ConstructionPlanner.cs` and `ConstructionSites.cs`,
    `Automation/ConstructionPlanService.cs` and `Goals/Executors/SupplyConstructionGoalExecutor.cs`;
    `tests/SpaceTraders.Application.Tests/Construction/ConstructionFixture.cs` holds X1-DC53's gate side (positions from
    the API, prices made up).
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Construction/ConstructionPlanner.cs`, `Construction/ConstructionSites.cs` (with `ConstructionSiteWatch`),
      `Construction/ConstructionRetries.cs`, `Automation/ConstructionPlanService.cs`, `Automation/ConstructionPlanState.cs`,
      `Goals/Executors/SupplyConstructionGoalExecutor.cs`, `Ports/ConstructionRefusedException.cs`;
    - changed: `Automation/FleetRoles.cs`, `AutomationSwitches.cs`, `GameLoopService.cs`, `RolePlanService.cs`,
      `TradingAutomationService.cs`; `Roles/FleetRole.cs`, `FleetRoleBoard.cs`, `RolePlanner.cs`, `RoleSettings.cs`,
      `RoleAdvisor.cs`, `RoleEstimator.cs`; `Services/PurchaseOrder.cs`, `TripBook.cs`; `Trading/TripReservations.cs`;
      `Orchestration/BudgetPolicy.cs`; `Goals/ShipGoalExecutorService.cs`,
      `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`; `EventHandlers/LedgerEntryHandler.cs`;
      `Health/ShipLeftIdleRule.cs`; `Interfaces/IAutomationMetrics.cs`, `Interfaces/Repositories/IShipGoalRepository.cs`;
      `JournalEvents.cs`; `DependencyInjection.cs`; `SupplyConstructionGoal`, `LedgerCategory` and
      `CargoPurchasedEvent` (Domain); `ShipGoalRepository` and `DefaultSettingsSeed` (Persistence);
      `SpaceTradersPortAdapter` (Infrastructure.SpaceTradersAPI); `PrometheusMetricsService` and
      `PrometheusAutomationMetrics` (API);
    - tests: new `Construction/ConstructionPlannerTests`, `ConstructionSitesTests`, `Roles/RolePlannerConstructionTests`,
      `Roles/ConstructionRoleTests`, `Automation/ConstructionPlanServiceTests`, `Goals/SupplyConstructionGoalExecutorTests`;
      more in `RolePlanServiceTests`, `TradingAutomationServiceTests`, `TradeBetweenMarketsGoalExecutorTests`,
      `BudgetPolicyTests`, `LedgerEntryHandlerTests`, `PurchaseOrderTests`, `ShipGoalExecutorServiceTests`,
      `ShipRuleTests`, `DefaultSettingsSeedTests`, `AutomationSwitchesTests`, `ShipGoalSerializationTests` (Domain),
      `PrometheusMetricsTests` and `DiValidationTests` (API), and `ShipGoalRepositoryTests` (Postgres).
  - Tests: App 940 (76 new), Domain 72, API 167 (2 new, and 4 skipped); against Postgres, Infrastructure 75 (1 new, in
    `ShipGoalRepositoryTests`) and API 2. Merged with slices 6.11 and 2.11 (whose decisions took D59–D63, so this
    slice's are D64–D68): App 981, Domain 72, API 171 (and 4 skipped), Infrastructure 76 and API 2 against Postgres;
    2.11's shipyards test now expects `Construct` in what a cargo ship for sale can do.
- **6.7 Siphoning** (merged 2026-10-02 as projects#130, deployed by gembernodes#29; built on branch `ccr-0969c532-x5ricm`, with your decisions D31–D33).
  Asked that day: "Can you work on implementing syphons. Functions practically the same as minors,
  including surveys, but for gassy materials."
  - **No surveys, by the game's rules:** the API's siphon call (`POST my/ships/{ship}/siphon`) takes no
    survey (the OpenAPI spec v2.3.0 gives it no body), the docs use surveys with the extract call only,
    and a surveyor's deposits are ores only (the command ship's Surveyor II lists 13 ores). Everything
    else follows the miners.
  - **X1-DC53** (public API, 2026-10-02): one gas giant, C38 (-57,-143), with STRONG_MAGNETOSPHERE as its
    only trait. The orbital station C39 at the same spot sells `SHIP_SIPHON_DRONE` (and probes), and
    exchanges the three gases; G50 imports all three, E47 and F48 the two liquids. C40, a fuel station
    39 from C38, is the refuelling stop towards G50 and E47 for an 80-unit tank.
  - Done:
    - **Who siphons** (`FleetRoles.IsSiphoner`): a ship with a gas siphon, a hold and a tank, and nothing
      to mine or survey with, whichever plans are on: a siphon drone. The command ship has a
      MOUNT_GAS_SIPHON_II too, but it mines or surveys, as before. A siphoner trades only when the siphon
      plan has no trip for it, as a miner.
    - **What a gas giant yields** (`Siphoning/GasGiants.cs`): the game publishes no table, and C38's
      traits name no gas, so every gas giant counts as yielding HYDROCARBON, LIQUID_HYDROGEN and
      LIQUID_NITROGEN, about equally (the gases C39 exchanges). Only GAS_GIANT waypoints are siphoned.
    - **The siphon plan** (`SiphonAutomationService`, new switch `Automation.Plan.Siphon.Enabled`, off;
      bootstrapped after mining and before trading): one trip per goal (`SiphonAndSellGoal`), chosen again
      after each sale. A siphoner that holds goods a market buys sells them first, one good a trip, where
      each fetches most after fuel (a full hold only sells, even at a loss on the fuel: a siphon trip would
      end at once without its gas aboard, on every tick); otherwise the best of
      `SiphonPlanner.SiphonTargets`: every market
      that buys a gas, siphoned at the gas giant nearest it and sold there, the market shortest of its
      gas first (D28), then the most a siphon is expected to fetch, then the nearest gas giant. One
      siphoner per sell market and gas. Journal: `SiphonStarted`, reason `low_supply`, `lowest_supply`
      or `held_cargo`.
    - **The trip** (`SiphonAndSellGoalExecutor`, `SiphonResourcesCommand`): flies with its goal through
      refuelling stops, never DRIFT (`GoalFlight`), orbits, siphons once per cooldown and keeps every
      good a market it can reach from the gas giant buys (D33), jettisoning the rest, which would fill the
      hold for good. A full hold, of any gases, turns it to selling: its own gas at its market, in
      batches of the trade volume, then the market fetched again (D25). Journal: `Siphoned` per siphon.
    - **Drones** (D32): when no siphoner is free, a `SHIP_SIPHON_DRONE` is bought, one a tick, only when
      its first trip by the same ranking would serve a market short of its gas, at the shipyard that
      sells it for the least in a system where our ships are, up to `Siphon.MaxDrones` (new, 10), within
      the credit reserve. In X1-DC53 that is C39.
    - **Contracts** stay mineral-only (D2, D31).
    - **Visibility:** a siphon's yield counts in `spacetraders_extracted_units_total`, so the dashboard's
      fleet table and "Mined and jettisoned per hour" show gases with no gembernodes change; it isn't an
      extraction in `spacetraders_extractions_total`, which the survey statistics read. The Journal
      panel shows the new kinds. The fleet view says "siphoning for …" and "selling …". The plan's state
      (`plan_states`, `SiphonAutomation`) lists the gas openings of every system where our ships are,
      before the first drone too; `ShipLeftIdle` (D13) counts a gas opening as work for the siphoners
      the plan lists as able to reach its gas giant.
  - Noticed (not changed):
    - **The first drone needs a ship at C39:** the API sells a ship only where one of ours is, and no
      ship of ours stays at C39. The purchase calls for a probe (D30), which only the probe plan answers:
      with it off, no siphon drone is bought until a ship happens to be there.
    - **The gas processor:** the docs say a siphon needs a gas siphon and a gas processor module. The
      command ship has a MODULE_GAS_PROCESSOR_I; whether a siphon drone does isn't known here (the
      shipyard listing the bot caches has no modules), and the plan doesn't ask. If the API refused a
      drone's siphons, each attempt would drop its trip with a warning, and `RepeatingError` would show it.
    - **Unconfirmed yields:** the three gases at about a third each are an assumption until the first
      `Siphoned` lines.
    - **Log volume:** a `Siphoned` line per siphon, as `Extracted` per extraction: at a cooldown of about
      70 seconds (an extraction's, in the soak test) that is some 1,200 lines a day per drone, 12,000 for
      ten, against the 50,000-a-day log alert that mining's lines count towards too.
    - A trip sells only its own gas at its market, as you chose; the other gases follow one good a trip,
      each where it fetches most after fuel, which is often the same market without a flight.
    - The mining planner's tie-break by the nearest asteroid never applies: its comparer ends with the
      opening's key, so ties go by key. The siphon planner's does apply.
  - To switch it on: `PUT /settings/Automation.Plan.Siphon.Enabled` with `{"value": "true"}` (and the
    probe plan, for the first purchase at C39).
  - Done when: a full reset period with the siphon plan on and no open anomaly for it.
  - **To understand this,** start with `SpaceTraders.Application/Siphoning/SiphonPlanner.cs` and
    `GasGiants.cs`, then `Automation/SiphonAutomationService.cs` and
    `Goals/Executors/SiphonAndSellGoalExecutor.cs`;
    `tests/SpaceTraders.Application.Tests/Siphoning/SiphonFixture.cs` holds X1-DC53's gas side (positions
    from the API, prices made up).
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Siphoning/GasGiants.cs`, `Siphoning/SiphonPlanner.cs`, `Automation/SiphonAutomationService.cs`,
      `Goals/Executors/SiphonAndSellGoalExecutor.cs`, `Commands/Ships/SiphonResourcesCommand.cs`;
    - changed: `Automation/FleetRoles.cs` (`IsSiphoner`), `AutomationSwitches.cs` (the Siphon plan),
      `GameLoopService.cs`, `MarketAutomationPlanState.cs` (`PlanTypes.SiphonAutomation`),
      `Goals/ShipGoalExecutorService.cs`, `Health/ShipLeftIdleRule.cs`, `Mining/MiningPlanner.cs`
      (`IsDemanded` and `CanSellFrom` shared), `JournalEvents.cs`, `IAutomationMetrics.cs`,
      `Services/FleetStatusQueryService.cs`, `DependencyInjection.cs`; `SiphonAndSellGoal` and
      `ShipGoalKind` (Domain); `PrometheusMetricsService`, `PrometheusAutomationMetrics` (API);
      `DefaultSettingsSeed` (Persistence: the switch and `Siphon.MaxDrones`);
    - tests: `Siphoning/SiphonPlannerTests` and `SiphonFixture`, `Automation/SiphonAutomationServiceTests`,
      `Goals/SiphonAndSellGoalExecutorTests`, `Commands/SiphonResourcesHandlerTests` (new); additions to
      `AutomationSwitchesTests`, `GameLoopServiceTests`, `DefaultSettingsSeedTests`, `ShipRuleTests`,
      `FleetStatusQueryServiceTests`, `ShipGoalSerializationTests` (Domain) and `PrometheusMetricsTests`
      (API).
- **6.8 Spare time** (merged 2026-10-02 as projects#131, deployed by gembernodes#30; built on branch `ccr-3f080253-4o4wfu`, with your decisions D34–D37).
  Asked that day: "I'd like my command ship not to be idle. So can we add a interuptable mining/siphoning
  task that just fills up the cargo with whatever and sells it where it's relevant. If a more important job
  comes up such as trading or surveying it should stop mining, sell it's inventory and start on the new
  job." With the survey plan on, the command ship surveys and nothing else (D20); with a stock of surveys
  for every ore (D27), it waited.
  - Done:
    - **The order** (D34): survey, then trade, then mine or siphon. A new plan, spare time
      (`SpareTimePlanService`, switch `Automation.Plan.SpareTime.Enabled`, off), is bootstrapped last, so it
      gets the command ship only when the survey and trading plans left it free. It gives a trip to every
      ship that gathers in its spare time (`FleetRoles.GathersInSpareTime`): a surveyor, with the survey plan
      on, with a mining laser or a gas siphon, a hold and a tank. That is the command ship; with the survey
      plan off it is a miner, as before.
    - **The trip** (`GatherAndSellGoal`, `GatherAndSellGoalExecutor`): at the nearest asteroid or gas giant it
      can work and reach that yields a good a market it can carry it to buys (D35, `GatherPlanner`): XB5C,
      where it surveys, in practice. It mines (`ExtractResourcesCommand`, new) or siphons
      (`SiphonResourcesCommand`) once per cooldown, without surveys, which stay for the drones, keeping every
      good a reachable market buys (D33's rule) until the hold is full. Then it sells one good at a time,
      each where it fetches most after fuel (D36): it records the sale in its goal, flies there through
      refuelling stops, docks, sells in batches and fetches the market again (D25). A full hold sells even
      where the sale doesn't pay for its fuel; after that, what doesn't pay stays aboard for the next trip.
      The goal ends, and the plans choose again.
    - **A survey interrupts it** (D37): the survey plan treats a spare-time trip that fills its hold as free
      and replaces it with the survey; the hold stays aboard, and the next trip fills it on.
    - **A trade interrupts it** (D34): with the spare-time plan on, the trading plan takes the command ship,
      free or on a trip that fills its hold, for a route that waits for it once its hold is sold, after the
      other traders have chosen. It judges the route from where selling the hold leaves the ship
      (`GatherPlanner.AfterSellingHold`), so the route is still lucrative once the hold is sold and the ship
      doesn't turn back to gathering on the way. The ship sells its hold first, one good a trip, by the rule
      held cargo is sold by (`TradeRoutePlanner.TryFindBestCargoSale`, now shared), then takes its route.
      Without such a route the trading plan leaves the ship and its hold to the spare-time plan (D37).
    - **Safe interruptions** (`SpareTimeInterruption`): only while the trip fills its hold, only when no
      goal step of the ship runs (B46: a step that turns the trip to selling would write it back over the
      new goal), and only when the ship as stored isn't in flight, so its arrival still matches the goal
      that flew it (B17). Otherwise a later tick tries again.
    - **Visibility:** journal kinds `GatheringStarted` (`Method` `mines` or `siphons`) and
      `GatheringInterrupted` (`Reason` `survey` or `trade`, `Units` aboard); extractions and siphons log
      `Extracted` and `Siphoned` with `Target` `whatever sells`. The yield counts in
      `spacetraders_extracted_units_total`, so the dashboard's mined panels show it with no gembernodes
      change, but a spare-time extraction isn't counted in `spacetraders_extractions_total`: the survey
      statistics would read an extraction without a survey, which it is by design, as a sign of too few
      surveys. The fleet view says "mining in its spare time", "siphoning in its spare time" or "selling …".
      The plan's state (`plan_states`, `SpareTime`) lists each such ship, what it does and its trip's source;
      `ShipLeftIdle` (D13) counts a ship it lists with a source as having work.
    - **The switch brings it all** (D9): with the spare-time plan off, the command ship surveys and waits as
      before, and doesn't trade (D20).
    - Warnings fixed on the way, in files this slice touches: `FleetStatusQueryService` reads the time from
      `TimeProvider` (S6354), and `FleetStatusQueryServiceTests` lost 13 QW0021 and 2 S8969 warnings. Left:
      QW0028 on `SpareTimePlanState.PlanId`, a `Guid` like every plan state's.
  - Noticed (not changed):
    - **A survey may wait one cooldown:** mining and surveying share the ship's cooldown, so a survey that
      comes up right after an extraction waits for it (about 70 seconds in the soak test); before, the
      ship was idle and surveyed at once.
    - **Spare-time ore is sold, never delivered:** with the survey plan on the command ship isn't a miner,
      so the contract never takes it (D20, D23), even when the contract wants the ore it holds.
    - **Shared markets:** it sells at the markets the drones sell at (H51, F49 near XB5C), which moves their
      prices for the drones' trips too.
    - **Travel time isn't in the sale** (as in 6.5): with a 400-unit tank fuel is cheap, so a good may go to
      a far market for a few credits more per unit.
    - **Cargo ships:** the command ship takes routes only after the other traders, and doesn't count
      towards "every trader has a trip" (D21), so it neither delays nor triggers a cargo ship purchase.
    - **Selling the hold for a trade goes good by good** through the trading plan, so a survey that comes
      up between two sales comes first (survey > trade).
    - **Log volume:** an `Extracted` or `Siphoned` line per extraction, about 1,200 a day, as for a drone.
  - To switch it on: `PUT /settings/Automation.Plan.SpareTime.Enabled` with `{"value": "true"}`. The survey
    plan must be on for the command ship to count as gathering in its spare time, and the trading plan for
    trades to come first; both are.
  - Done when: a full reset period with the spare-time plan on and no open anomaly for it.
  - **To understand this,** start with `SpaceTraders.Application/SpareTime/GatherPlanner.cs`, then
    `Automation/SpareTimePlanService.cs`, `Goals/Executors/GatherAndSellGoalExecutor.cs` and
    `Automation/SpareTimeInterruption.cs`, with `TradeInsteadOfGatheringAsync` in
    `Automation/TradingAutomationService.cs`; `tests/SpaceTraders.Application.Tests/SpareTime/SpareTimeFixture.cs`
    holds X1-DC53's middle and its gas side.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `SpareTime/GatherPlanner.cs`, `Automation/SpareTimePlanService.cs`, `SpareTimePlanState.cs`,
      `SpareTimeInterruption.cs`, `Goals/Executors/GatherAndSellGoalExecutor.cs`,
      `Commands/Ships/ExtractResourcesCommand.cs`;
    - changed: `Automation/FleetRoles.cs` (`GathersInSpareTime`, `HasMiningLaser`), `AutomationSwitches.cs`
      (the SpareTime plan), `GameLoopService.cs`, `SurveyPlanService.cs` (D37), `TradingAutomationService.cs`
      (D34), `Trading/TradeRoutePlanner.cs` (`TryFindBestCargoSale`), `Mining/MiningPlanner.cs`
      (`IsSellableFrom`, shared with `Commands/Ships/SiphonResourcesCommand.cs`),
      `Goals/ShipGoalExecutorService.cs`, `Health/ShipLeftIdleRule.cs`, `JournalEvents.cs`,
      `Interfaces/IAutomationMetrics.cs`, `Services/FleetStatusQueryService.cs`, `DependencyInjection.cs`;
      `GatherAndSellGoal` and `ShipGoalKind` (Domain); `PrometheusMetricsService` (API); `DefaultSettingsSeed`
      (Persistence: the switch);
    - tests: `SpareTime/GatherPlannerTests` and `SpareTimeFixture`, `Automation/SpareTimePlanServiceTests`,
      `Goals/GatherAndSellGoalExecutorTests`, `Commands/ExtractResourcesHandlerTests` (new); additions to
      `SurveyPlanServiceTests`, `TradingAutomationServiceTests`, `TradeRoutePlannerTests`,
      `AutomationSwitchesTests`, `GameLoopServiceTests`, `DefaultSettingsSeedTests`, `ShipRuleTests`,
      `FleetStatusQueryServiceTests`, `ShipGoalSerializationTests` (Domain), `DiValidationTests` and
      `PrometheusMetricsTests` (API).

- **6.9 Roles** (built 2026-10-02 on branch `claude/ship-role-profitability-ghjzlb`, in projects and gembernodes, with your
  decisions D38–D42). Asked that day: "Please help me do some refinement on the ship's behavior. Each ship should have a
  set of potential roles. … A ship should occasionally consider whether it's role is still the best thing it can do. …
  Goal is to have every ship be the most profitable it can be by comparing each role it has with the potential profits it
  can make." And, during the work, D42: cargo nothing will sell is sold where that pays, or jettisoned.
  - Done:
    - **Potential roles** (`FleetRoles.PotentialRoles`): survey with a surveyor (or a bought `SHIP_SURVEYOR`, whose mounts
      startup sync records later); mine with a mining laser, a hold and a tank; siphon with a gas siphon, a hold and a tank;
      trade with a hold and a tank. A probe has none. A role counts only while its plan is on; mining also while the
      contract wants ore.
    - **The role board** (`RolePlanService`, new switch `Automation.Plan.Roles.Enabled`, off; bootstrapped after the scout
      plan and before the others) gives every ship one role (`RolePlanner`, no I/O):
      1. a ship with one role takes it (`only_role`);
      2. surveys first (D38): in a system with a ship that can only survey, it surveys and the others don't; otherwise the
         ship that can survey with the least to lose (its best other trip earns least per hour) surveys, while another
         ship there can mine, since surveys are for miners (`survey_first`). It keeps the role unless another would lose
         less by more than the head start. With the spare-time plan on it trades or gathers when it has nothing to survey
         (D34), as the command ship does now;
      3. while the contract wants ore, every other ship that can mine mines (`contract`, D40);
      4. the rest share the work for the most credits per hour across the fleet: each takes one trip (or none), no two the
         same trade route (D18) or the same mining or siphon opening (`most_profitable`). It is the assignment problem,
         solved exactly (`Assignment`, the Hungarian method), so the work goes where it earns the fleet most, not to the
         first ship that wants it. A ship's current role counts `Roles.HeadStartPercent` (20) more (D41). A ship without a
         trip keeps its role (`no_work`).
    - **The estimates** (`RoleEstimator`, no I/O): for each role a ship could take but surveying, the trips its plan would
      offer, best per hour first: every lucrative route (D14), its profit after fuel; every mining target (D28's), a full
      hold of the target ore, filled at the ship's rate times the ore's share of the extractions (its survey's, or one of
      the asteroid's ores), less fuel; every siphon target, a full hold of the gases a market buys (D33), the trip's own gas
      at its market and each other gas where it counts most (D49). Each adds the production chains' share (D39, `ChainValues`): a good sold to a market that imports it
      and exports a pricier good made from it counts `Roles.ChainValueSharePercent` (50%) of the price difference (what the
      market charges for each, as D15 compares them), and that share again of the next step at the market in the system that
      makes the most of it; each step times how short its market is of the input (SCARCE 1, LIMITED ¾, MODERATE ½, HIGH ¼,
      ABUNDANT 0); at most what the trip earns on a unit, its price for a mined or siphoned good, its margin for a traded one
      (D49). Time: the flights in CRUISE as the API reckons them (15 seconds plus the distance times 25 over the
      engine's speed, 9 when it isn't cached), 10 seconds a landing, and the cooldowns to fill the hold, half a tick after
      each.
    - **How fast a ship fills its hold** (`GatheringRates`): every extraction and siphon records its yield and cooldown; a
      ship's last 10 give its rate, else the average of the other ships of the kind, else 3 units every 70 seconds (the soak
      test's drone extracted every 71 seconds; the command ship's Mining Laser II has strength 5). In memory; the plan state
      keeps them, and the first evaluation after a restart takes them back.
    - **When** (D41): at the first pass after a start, every `Roles.ReconsiderMinutes` (10), and at once when a ship joins,
      a plan is switched, the contract starts or stops wanting ore, or a ship with a choice of roles (not the one that
      surveys, which waits for surveys by design) has had no work for a minute, at most once a minute. A new role takes
      effect when the ship's trip ends: the plans only give work to free ships.
    - **The plans** read one board (`FleetRoleBoard`): with the board on, the survey plan gives surveys to the survey role,
      the mining plan trips to the mining role, the siphon plan to the siphon role, and the trading plan routes to the trade
      role and to a miner or siphoner its plan had no trip for, as before; the spare-time plan takes the survey role's ship.
      The contract takes every ship that can mine but the one that surveys (D40), whatever its role, so it never waits for
      the next evaluation. A ship bought this tick waits one tick for its role. With the board off, the fixed rules of D20
      and D34 hold, unchanged.
    - **Purchases:** with the board on, the mining and siphon plans buy a drone only when the board would give it their
      role (`RoleAdvisor`: its best trip in the role earns at least as much per hour as in any other, from the shipyard). A
      drone that would earn more trading would trade, and the plan, finding no free miner, would buy the next, and the next.
    - **Dead cargo** (D42, `HeldCargo`, `CargoJettison`): a free trader whose hold has nothing that pays for its sale after
      fuel jettisons it before it takes a route (it used to stay aboard for good), but for the contract's ore on a ship that
      mines for the contract. A surveyor with nothing to survey and no spare-time trip to fill its hold on sells its hold
      where that pays (a held-cargo trip), and jettisons the rest: the 7 copper SPECTER-1 carried since its contract work
      (6.4's Noticed). A spare-time ship with a full hold that no market it can reach buys jettisons it, and gathers again.
      Journal `CargoJettisoned` (`Reason` `no_buyer` or `not_worth_the_fuel`); counted in
      `spacetraders_jettisoned_units_total`. A jettison the API refuses is a warning, and waits 10 minutes before the next
      try (`JettisonRetries`).
    - **Visibility:** journal kind `RoleChanged` (`OldRole`, `NewRole`, `Reason`, and for a role chosen by profit
      `CreditsPerHour` and the `Job` that decided it). The state (`plan_states`, `Roles`) lists every ship's role, why,
      since when, what each role it could take would earn it per hour with the trip, and the rates it used. Metrics
      `spacetraders_ship_role_info{ship,role,reason}` and `spacetraders_ship_role_credits_per_hour{ship,role}`, exported
      while the board is on (switched off, the plans no longer read its roles); the dashboard's new "Roles" table shows
      them (gembernodes).
  - Noticed (not changed):
    - **The estimates are estimates:** prices as last seen; a full hold sold at one price, while each sale lowers it; a
      ship's first evaluations use the default rate until it extracts. The state and the dashboard show every estimate, so
      a wrong one shows: on the first day they were 30 to 300 times what ships earned, fixed for now by D49.
    - **Within a role the plans still choose by their own rules** (D15: a route to a market that makes a pricier good
      first; D28: the market shortest of its ore first), so the trip a ship gets may not be the one its role was valued by. The chain value only weighs roles; ranking the plans' own trips
      by it would change D15 and D28. Yours to call.
    - **One ship surveys per system** (besides every ship that can only survey), and only while another ship there can
      mine. With many drones far apart, one surveyor may not keep up; the board doesn't measure that.
    - **Spare time:** what doesn't pay for its fuel after a spare-time sale stays aboard for the next trip, as D36 has it;
      D42 jettisons a spare-time hold only when it is full and no market it can reach buys any of it.
    - **With the trading plan off,** nothing sells or jettisons a free ship's cargo: D42 runs in the trading plan.
    - **Jettisoning while docked:** the API's documentation names no state for it, and the archived ship planner allowed it
      anywhere but in flight; the bot never did it docked before. A refusal would show as a warning, once every 10 minutes.
    - No plan buys a `SHIP_SURVEYOR`; one bought by hand reaches the bot at its next startup sync.
    - The board writes its state at each evaluation: every 10 minutes, and at most once a minute while a ship with a choice
      of roles has no work.
  - To switch it on: `PUT /settings/Automation.Plan.Roles.Enabled` with `{"value": "true"}`. The first evaluation follows
    at the next tick, and the "Roles" table and the journal's `RoleChanged` lines show what it decided.
  - Done when: a full reset period with the role board on and no open anomaly for it.
  - **To understand this,** start with `SpaceTraders.Application/Roles/RolePlanner.cs`, then `RoleEstimator.cs` and
    `ChainValues.cs`, `Automation/RolePlanService.cs` and `Roles/FleetRoleBoard.cs`; for D42, `Trading/HeldCargo.cs` and
    `TradeInsteadOfGatheringAsync`'s neighbours in `Automation/TradingAutomationService.cs`.
    `tests/SpaceTraders.Application.Tests/Roles/RolePlannerTests.cs` has your two examples.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Roles/FleetRole.cs`, `FleetRoleBoard.cs`, `RolePlanner.cs`, `RoleEstimator.cs`, `ChainValues.cs`,
      `Assignment.cs`, `GatheringRates.cs`, `RoleSettings.cs`, `RoleAdvisor.cs`; `Automation/RolePlanService.cs` (with
      `RoleBoardMemory`), `RolePlanState.cs`; `Trading/HeldCargo.cs`; `Services/CargoJettison.cs` (with
      `JettisonRetries`);
    - changed: `Automation/FleetRoles.cs` (`PotentialRoles`, `CanSurvey`, `CanMine`, `CanSiphon`, `EngineSpeed`),
      `AutomationSwitches.cs` (the Roles plan), `GameLoopService.cs`, `ContractPlanService.cs`, `SurveyPlanService.cs`,
      `MiningAutomationService.cs`, `SiphonAutomationService.cs`, `TradingAutomationService.cs`, `SpareTimePlanService.cs`,
      `ProbeDeploymentPlanService.cs`, `Health/ShipLeftIdleRule.cs`, `Commands/Ships/MineResourceVolumeCommand.cs`,
      `ExtractResourcesCommand.cs`, `SiphonResourcesCommand.cs`, `JournalEvents.cs`, `Interfaces/IAutomationMetrics.cs`,
      `DependencyInjection.cs`; `PrometheusAutomationMetrics`, `PrometheusMetricsService` (API); `DefaultSettingsSeed`
      (Persistence);
    - tests: `Roles/RolePlannerTests`, `RoleEstimatorTests`, `ChainValuesTests`, `AssignmentTests`, `GatheringRatesTests`,
      `FleetRoleBoardTests`, `RoleAdvisorTests`, `RoleBoardTestSupport`, `Automation/RolePlanServiceTests`,
      `Trading/HeldCargoTests`, `Services/CargoJettisonTests` (new); additions to `TradingAutomationServiceTests` (one
      rewritten: cargo no market buys is jettisoned now), `SpareTimePlanServiceTests` (one rewritten),
      `MiningAutomationServiceTests`, `SiphonAutomationServiceTests`, `SurveyPlanServiceTests`, `ContractMinersTests`,
      `ShipRuleTests`, `ExtractResourcesHandlerTests`, `SiphonResourcesHandlerTests`, `AutomationSwitchesTests`,
      `DefaultSettingsSeedTests`; `PrometheusMetricsTests` and `MetricsEndpointTests` (API).

- **6.10 The fleet's shape** (asked 2026-10-03, after the bot's first day on the cluster, with your decisions D43–D50).
  Asked: too many siphon drones and too few other ships; more trades; drones for minerals out of fuel range; the actual
  profit of each source; the price gap per good in the market tree; what each ship can do next to its role; a designated
  surveyor; and what selling an input does to what a market makes from it. Split in three, so each part stays reviewable.
  - **What the data said** (2026-10-03, the last 12 to 24 hours):
    - Net credits an hour after fuel (ledger): the command ship trading about 13,000; siphon drones 2,000–4,000;
      mining drones 1,000–3,000. A few drones made one-off trades of goods worth 50,000 with a 15-unit hold; some lost.
    - Each plan buys on its own, and every purchase keeps the 100,000 reserve: a drone (49,011–51,411) is affordable at
      about 150,000 credits, a probe (77,117) at 177,000, a light shuttle (114,225) at 214,000, a light hauler (354,210) at
      454,000. The credits peaked at 150,000–166,000 before each drone purchase, so 5 siphon drones and 2 mining drones
      were bought overnight, and no probe or cargo ship.
    - The role board's estimates were 30 to 300 times what the ships earned (D49).
    - 176 trades in 24 hours, 159 of them by the command ship; 102 taken because they feed production (D15), at a median
      538 credits against 3,386 (D44 keeps D15). Price gaps are thin: most goods under 50 a unit, SHIP_PLATING, SHIP_PARTS
      and MEDICINE excepted.
    - SCARCE or LIMITED minerals, and what a drone could do about them: near, COPPER_ORE (H51), SILICON_CRYSTALS (A3,
      H53) and QUARTZ_SAND (F49), all from XB5C; far, ALUMINUM, COPPER, IRON, QUARTZ and SILICON at B7, from asteroid B14
      (25 from B7); out of any drone's round trip, AMMONIA_ICE, GOLD_ORE, SILVER_ORE, PLATINUM_ORE and PRECIOUS_STONES
      (their nearest asteroids are 68 to 476 from their markets); DIAMONDS, which no asteroid yields; and the three gases,
      all from C38. The drones worked on four of the ores.
  - **6.10a Visibility and the role board's rates** (built 2026-10-03; dashboards in gembernodes#34):
    - What each ship can do, whatever the switches: `spacetraders_ship_capabilities_info{ship,can}` (`Survey, Mine,
      Siphon, Trade`, or `none`); the fleet and roles tables show it next to the role (gembernodes#34), as drones of both
      kinds report the registration role EXCAVATOR.
    - Profit per trip (D46): journal kind `TripEnded`; counters `spacetraders_trip_profit_credits_total`,
      `spacetraders_trip_loss_credits_total` and `spacetraders_trips_total` by `activity`; sales and purchases from the
      trip's own figures (`TripGoal.Earned`, `Spent`: a ledger row lands after the trip has ended), fuel from the ledger
      (`TripBook`). Every end books: sold, each early stop, `interrupted` (spare time taken over) and `runaway` (the
      circuit breaker). Contracts: deposit and payout as profit, each delivery's round-trip fuel as a loss. Not booked:
      contract round trips released without a delivery, goals an agent reset wipes, the earlier batches of a sale that
      fails partway; a trade bought before the deploy books its whole sale once; a trade that sells a held spare-time
      hold books it under `trade`. Dashboard: profit an hour by activity, stacked.
    - Units sold and bought per market and good (D50); dashboard: what we sell into a market an hour against the supply,
      trade volume and price of what it makes from it.
    - The role board's rates (D49).
    - The market tree's price gap per good, where to buy, where to sell (dashboard only, in gembernodes#34).
  - **6.10b The order ships are bought in** (built 2026-10-03 on branch `claude/spacetraders-purchase-order`; D43, D47, D48,
    D51). Asked: "I feel there are too many siphoning drones and not enough other ship types"; "I'd like at least 1 drone per
    mineral that is scarce or limited, then save up for cargo ships, then a mix based on if the minerals aren't going above
    LIMITED", the mix being "Alternate drones and cargo ships, but probes first"; a designated surveyor first (D47); and,
    during the work, a reserve that grows with the trading holds (D51).
    - Done:
      - **The order** (`PurchaseOrder`, `PurchaseNeeds`, D43): every plan that buys says on each pass what it would buy
        (`PurchaseNeed`: a tier, the ship, the shipyard, the price) and buys only when nothing comes first: the contract's
        drone, a surveyor, a drone per scarce mineral, the cargo ships of `Trade.ShipPurchases`, probes, then drones and
        cargo ships of the list's last type in turn. A need counts while its plan is on, and only while it can be met (its
        cap not reached, a known shipyard with a price for it). Until each plan that is on and could need something earlier
        has said what it needs within the last 2 minutes, nothing after it is bought: after a start, or a pause in which no
        plan ran (a 502 pauses them for 3 minutes), the probe plan, which runs before the survey, mining, siphon and trading
        plans, waits a tick; a plan that fails before it says holds the purchases after it. The order says who may buy; the
        reserve stays the purchase's own check.
      - **The turn** between drones and cargo ships goes to the kind not bought last: after the list's last cargo ship a
        drone, then a cargo ship, and so on. It reads the ledger's `ShipPurchase` rows (once a start) and the purchases of
        this process, which `ShipPurchaseService` records at once, as the ledger's row comes a moment later and the siphon
        plan, later in the same tick, would otherwise buy a second drone. Any drone counts, the contract's and a scarce
        mineral's too; a cargo ship is a type of the list or one of the game's freighters. A turn passes when the other
        kind has nothing to buy: a drone's first trip wouldn't serve a market short of its mineral, or a miner is free; a
        new cargo ship would have no lucrative route (judged with any credits, so a hauler that isn't affordable yet keeps
        its turn), or a trader has no trip. A turn that passed isn't made up later. Probes and surveyors don't take
        turns. (The design notes counted the purchases since the list's last cargo ship; a review found that an edited
        list, or a lost ledger row, then left it the drones' turn for good, and that passed turns came back as a run of
        one kind.)
      - **A cargo ship of the list is saved up for** whatever the routes (D43, "then save up for cargo ships"); it is
        bought, as before (D21), when every trader has a trip and the new ship would have a lucrative route with the
        credits left after it. Past the list, one more of its last type at a time, in turn with the drones: D21's "up to 2
        light haulers" became that.
      - **The surveyor** (D47): with the role board on, the survey plan buys a `SHIP_SURVEYOR` (33,905 at H52) for each
        system with a mining drone and no ship that can only survey. The board gives it the survey role (D38), which
        frees the command ship. The survey executor makes no check of its own, so a surveyor bought since the last restart,
        whose mount startup sync hasn't recorded, surveys.
      - **Coverage** (D48): the mining and siphon plans give a free drone a SCARCE or LIMITED mineral no drone's trip works
        on first, near before far, then D28's order; the journal's `MiningStarted` and `SiphonStarted` say `uncovered` when
        that came before D28's choice. They buy a drone (tier `Coverage`, without asking `RoleAdvisor`) while the system
        has fewer drones of the kind than SCARCE or LIMITED minerals a new drone could serve (`MiningPlanner.ScarceOres`,
        `SiphonPlanner.ScarceGases`): a count, so a drone between trips doesn't buy another. The role board keeps one
        drone gathering per such mineral after the contract (`coverage`): the drone whose trip works on it, else one that
        has the role, else the one with the least to lose. A drone beyond one per mineral is bought by D28's and D32's
        rules, in turn with the cargo ships, and asks `RoleAdvisor` as before.
      - **The reserve** (D51, `CreditReserve`): `BudgetPolicy` keeps the floor plus 1,000 a unit of hold on the ships that
        trade, read from the cached fleet and the role board at every evaluation. New setting
        `FleetExpansion.ReservePerTradingCargoUnit` (1,000); `FleetExpansion.MinCreditReserve` is seeded at 60,000.
      - **Visibility:** `spacetraders_purchase_need_credits{plan,tier,position,ship_type,shipyard}`, one series per plan
        that needs something, worth the ship's price (the lowest position is what the credits are saved for);
        `spacetraders_credit_reserve`; the probe plan's state says `WaitingForAnotherPurchase`; the role board's
        `coverage` reason.
    - After the deploy (yours): **set `FleetExpansion.MinCreditReserve` to 60,000.** The seed adds missing keys at every
      start, so `FleetExpansion.ReservePerTradingCargoUnit` arrives by itself, but a stored value is never overwritten: until
      then the reserve is 100,000 plus the command ship's 40,000, 140,000.
    - Noticed (not changed):
      - **A long saving phase:** after the surveyor (33,905), the list is 114,225 + 2 × 354,210 = 822,645, with the reserve
        rising to 140,000, 220,000 and then 300,000; then 25 probes for X1-DC53's 26 markets (77,117 at A2, more as each
        purchase moves the price) above a 300,000 reserve, before any drone beyond one per scarce mineral. Today one per
        scarce mineral is already there (four mining drones for COPPER, SILICON and QUARTZ near the middle; seven siphon
        drones for three gases), so the next purchases are the surveyor, then the shuttle.
      - **A trader without a trip holds the list back:** a cargo ship is bought only when every trader has a trip (D21),
        and nothing after it while it is to buy ("save up", D43). Freed by the surveyor, the command ship trades: while it
        finds no lucrative route, the shuttle waits, and so do probes and drones. With the role board on, a drone that its
        mining or siphon plan had no trip for, and that finds no route, counts as such a trader too.
      - **During a contract** the mining plan buys nothing (D23), so a drone for a scarce ore waits for the contract's end,
        while cargo ships and probes, after it in the order, may be bought.
      - **Coverage counts drones, not trips:** a drone kept for a mineral may be on another trip; the trip-level rule moves
        drones to uncovered minerals as their trips end, so with as many drones as minerals they rotate rather than stay.
      - **One system:** needs are per plan, not per system; with ships in several systems a plan reports its first need.
      - `ContractPlanService` holds a `LegacyContractShipPurchaseService` that nothing constructs (dead code).
    - Done when: the surveyor and the shuttle are bought in that order, the probes after the list, and no drone beyond one
      per scarce mineral before the probes; `spacetraders_purchase_need_credits` shows what waits.
    - **Met** (checked on 2026-10-04): after the deploy (11:37Z on 2026-10-03) the purchases were the surveyor SPECTER-F
      (12:50Z), five drones the role board kept for `coverage` (SPECTER-10, -11, -12, -14, -15), the second survey ship
      SPECTER-13 (16:32Z, D55), the shuttle SPECTER-16 (20:33Z) and the hauler SPECTER-17 (04:51Z on 2026-10-04); no probe and
      no further drone, and none at all after D58 (20:26Z), as the order says.
    - **To understand this,** start with `SpaceTraders.Application/Services/PurchaseOrder.cs`, then each plan's need:
      `DroneNeedAsync` in `Automation/MiningAutomationService.cs` and `SiphonAutomationService.cs`, `BuyCargoShipAsync` in
      `TradingAutomationService.cs`, `SurveyorNeedAsync` in `SurveyPlanService.cs`; for coverage, `UncoveredFirst` in
      `Mining/MiningPlanner.cs` and `Keepers` in `Roles/RolePlanner.cs`; for D51, `Orchestration/CreditReserve.cs`.
    - Files, in `SpaceTraders.Application` unless named:
      - new: `Services/PurchaseOrder.cs` (`IPurchaseOrder`, `PurchaseOrder`, `PurchaseNeeds`, `PurchaseTier`,
        `PurchaseKind`, `PurchaseNeed`, `ReportedNeed`, `PurchaseRecord`), `Orchestration/CreditReserve.cs`;
      - changed: `Automation/ContractPlanService.cs`, `ProbeDeploymentPlanService.cs`, `ProbeDeploymentPlanState.cs`
        (`WaitingForAnotherPurchase`), `SurveyPlanService.cs`, `MiningAutomationService.cs`, `SiphonAutomationService.cs`,
        `TradingAutomationService.cs`, `RolePlanService.cs`, `FleetRoles.cs` (`IsMiningDrone`); `Mining/MiningPlanner.cs`,
        `Siphoning/SiphonPlanner.cs` (`UncoveredFirst`, `ScarceOres`, `ScarceGases`); `Roles/RolePlanner.cs`
        (`MineralCoverage`, `coverage`); `Orchestration/BudgetPolicy.cs`; `Services/ShipPurchaseService.cs`;
        `Commands/Fleet/PurchaseShipCommand.cs`; `Interfaces/IAutomationMetrics.cs`; `DependencyInjection.cs`;
        `PrometheusAutomationMetrics`, `PrometheusMetricsService` (API); `DefaultSettingsSeed` (Persistence);
      - tests: `Services/PurchaseOrderTests` and `OpenPurchaseOrder` (new); additions to `ContractPlanServiceTests`,
        `ProbeDeploymentPlanServiceTests`, `SurveyPlanServiceTests`, `MiningAutomationServiceTests` (two rewritten: the D28
        purchase now needs a drone per scarce ore first), `SiphonAutomationServiceTests` (two rewritten, the same),
        `TradingAutomationServiceTests` (one rewritten: past the list, one more hauler in turn), `RolePlanServiceTests`
        (one rewritten: the only drone is kept for a scarce ore), `RolePlannerTests`, `MiningPlannerTests`,
        `SiphonPlannerTests`, `ShipPurchaseServiceTests`, `BudgetPolicyTests`, `DefaultSettingsSeedTests`;
        `PrometheusMetricsTests`, `MetricsEndpointTests` (API).
  - **6.10c Drift to minerals out of fuel range** (built 2026-10-03 on branch `claude/spacetraders-drift`; D45). Asked: "I'd
    like a way to add mining/siphoning drones for the minerals outside of fuel range, e.g. by having a drone drift to the
    marketplace that buys the mineral first, then refueling and resuming normal behavior."
    - Done:
      - **Far targets** (`MiningPlanner.MiningTargets`, `SiphonPlanner.SiphonTargets`): a market out of the ship's CRUISE
        reach (no chain of fuel markets gets it there) that sells fuel counts, gathered at the asteroid or gas giant
        nearest it within a CRUISE round trip of it (`IsWithinRoundTrip`: out with a full tank, back with what is left, or
        a full tank where the source sells fuel), marked `Far`. It ranks after every reachable target of its supply level
        (D28), and among the minerals no drone works on (D48) after those in reach. In `MiningFixture` B7's gold and copper,
        from B14, now come after F49's SCARCE silicon and quartz and before H51's LIMITED copper; iron at B7, whose nearest
        asteroid (B13) is 48 away, 96 there and back, doesn't count.
      - **The trip** (`MineAndSellGoal.Drifting`, `SiphonAndSellGoal.Drifting`, `DriftStepAsync`, `GoalFlight.DriftAsync`):
        it navigates to the market asking for DRIFT (1 fuel, about ten times slower) and journals `DriftStarted`; the
        arrival docks it there and refreshes the market; the next step records that the drift has ended, and the trip goes
        on from there. Its next flight refuels (the orbit fills the tank at a fuel market) and asks for CRUISE. Never to
        the asteroid in DRIFT: the fallback would have left the drone there, in DRIFT (B47).
      - **The flight mode a navigation asks for** (`NavigateToWaypointCommand.FlightMode`, `FlightModeSubCommand`): set in
        orbit, after the orbit's refuel, just before the flight, so it never depends on whether the API lets a docked ship
        change mode; the API is called only when the cached mode differs. `GoalFlight` (mining, siphon, survey, spare time)
        and the trade executor ask for CRUISE, so a ship left in DRIFT, after a drift or by the fallback (B47, in part),
        flies in CRUISE again. The API's spec lists no restriction on the call; its response is `{nav, fuel, events}`, as
        the client reads it.
      - **A trip counts the fuel left at its source** (`MiningPlanner.Arrival`): the haul was checked with a full tank at
        the asteroid (`CanSellFrom`), true from XB5C, which sells fuel, but not from a far market's asteroids: from B7 a
        drone could have been sent to an asteroid 48 away, with 32 left for the 48 back, and drifted back. A trip now counts
        only when the ship can carry its hold to the market in CRUISE with the fuel it has there, as D45's "mines from
        that market in CRUISE" needs. In X1-DC53's middle nothing changes.
      - **New and free drones** (D45): `ScarceOres` and `ScarceGases` count the far minerals, so the coverage tier (D48)
        buys a drone for an ore only a far market is short of, and the role board keeps one gathering it (`coverage`). The
        plans' openings list a free drone that would drift to them (`MiningPlanner.CanTake`, read by `ShipLeftIdle`).
      - **The role board's estimate** (`RoleEstimator`): a far trip adds its drift (`DriftSeconds`: 15 seconds plus the
        distance times 250 over the engine's speed) and the unit of fuel bought back where it lands, so the board neither
        skips it nor counts it as near. Its job reads `…, drifting there first`. From the middle to B7 that is about 2.5
        hours, so a far trip rarely wins on its rate; the drone kept for a far mineral takes it anyway.
      - **Visibility:** the fleet view says `drifting to X1-DC53-B7 to mine GOLD_ORE`, as does the WebUI's fleet status;
        the journal has `DriftStarted`, and `FlightModeSubCommand` logs each change of mode.
    - What to expect, from the data of 2026-10-03 (not checked against the live markets): B7 was SCARCE or LIMITED in
      aluminum, copper, iron, quartz and silicon, all from B14. Aluminum and iron weren't short near the middle, so the
      ores a drone could serve go from three to five: with four mining drones, the coverage tier buys one more, ahead of
      the cargo ships (D43), and one drone for each of the two ends up at B7 after a drift of about 2.5 hours. Siphon drones
      don't drift in X1-DC53: C38 has every buyer in reach.
    - Deployed by gembernodes#37 at 12:13Z on 2026-10-03, with projects#138. On the cluster the whole cycle ran by 13:30Z:
      the command ship, left with 14 fuel at B14 by B54's surveys, drifted to B7 (12:51Z), switched back to CRUISE there
      (12:54:29Z), mined iron at B14 and sold it at B7 (1,616 after fuel). SPECTER-4 (12:35Z), SPECTER-10, the coverage
      drone bought at 13:09Z, and SPECTER-A (13:36Z) drifted to B7.
    - Noticed (not changed):
      - **Drift back and forth:** supply comes first (D28), so a drone at B7 whose ores there have risen to MODERATE drifts
        back to a SCARCE market in the middle, and a free drone in the middle drifts to a SCARCE one at B7, 2.5 hours each
        way. That is the rule as decided; the coverage keepers keep one drone at each far mineral.
      - **Surveys before the drone gets there:** a ship in transit counts as at its destination, so while a drone drifts to
        B7 the survey plan may survey B14 for it, if the surveyor reaches B14 in CRUISE; those surveys can expire first.
        It happened at the first drift, worse than noted: the command ship also surveyed B37, where no drone can mine.
        That is B54, fixed in the follow-up below.
      - **Coverage counts a mineral, not a market** (D48): with SPECTER-10 bound for B7's silicon, silicon counted as
        covered, and SPECTER-A left the middle's silicon markets for B7's iron. Your call was D53, coverage per area: see the
        follow-up below.
      - **The contract plan takes any free miner** without checking that it can reach the contract's asteroid in CRUISE,
        and the contract's commands fly with the fallback (B47). Under D1 (one contract per reset) and D23 (no drone is
        bought while the contract mines) no drone has drifted while a contract wants ore; a slice that takes the next
        contract should check.
      - **B47 remains** for scouting and the contract's flights; the fallback itself still switches to DRIFT.
      - **Cost:** the far-target search adds about 20 ms to each ranking of mining targets on an 85-waypoint system (timed
        on a synthetic one, Release build; the ranking before it took about 30 ms), a few rankings a tick. The bot waits
        on the API's rate limit, not its CPU.
      - The analyzers ask for strongly typed ids on `ShipGoal.GoalId`, `DeliverCargoGoal.ContractId` and
        `NavigateToWaypointArrivedCommand.GoalId` (QW0028, QW0029): a change across the goal model, left.
    - Done when: a drone drifts to B7, mines there and sells there in CRUISE, and its later trips leave B7 in CRUISE.
    - **Met** (checked on 2026-10-04): SPECTER-4 drifted from H53 at 12:35Z on 2026-10-03, reached B7 at 15:00:45Z and
      switched to CRUISE at 15:00:52Z; from then on it mined at B14 and sold at B7, about 90 fuel a round trip, as did
      SPECTER-10 and SPECTER-A.
    - Tests: App 817, Domain 71, API 163.
    - **To understand this,** start with `MiningTargets` in `Mining/MiningPlanner.cs` (the far targets, `CanDriftTo` and
      `IsWithinRoundTrip`), then `DriftStepAsync` in `Goals/Executors/MineAndSellGoalExecutor.cs` with `GoalFlight.cs`,
      then the flight mode in `Commands/Ships/NavigateToWaypointCommand.cs` and `SubCommands/IFlightModeSubCommand.cs`.
    - Files, in `SpaceTraders.Application` unless named:
      - new: `Commands/Ships/SubCommands/IFlightModeSubCommand.cs`;
      - changed: `Mining/MiningPlanner.cs`, `Siphoning/SiphonPlanner.cs`, `Goals/Executors/GoalFlight.cs`,
        `MineAndSellGoalExecutor.cs`, `SiphonAndSellGoalExecutor.cs`, `TradeBetweenMarketsGoalExecutor.cs`,
        `Commands/Ships/NavigateToWaypointCommand.cs`, `Automation/MiningAutomationService.cs`,
        `SiphonAutomationService.cs`, `Roles/RoleEstimator.cs`, `Services/FleetStatusQueryService.cs`, `JournalEvents.cs`
        (`DriftStarted`), `DependencyInjection.cs`; `ShipGoal.cs` (Domain: `Drifting`); `PrometheusMetricsService` (API);
      - tests: `Commands/FlightModeSubCommandTests` (new); additions to `MiningPlannerTests` (three expectations rewritten:
        B7's ores count now, a drift away), `SiphonPlannerTests` and `SiphonFixture` (`MapWithAGasGiantNearF48`),
        `MiningAutomationServiceTests` (four rewritten: B7's gold and copper get drones too, so each keeps its point),
        `SiphonAutomationServiceTests`, `RolePlanServiceTests` (two rewritten: a sixth drone), `RoleEstimatorTests`,
        `MineAndSellGoalExecutorTests`, `SiphonAndSellGoalExecutorTests`, `TradeBetweenMarketsGoalExecutorTests`,
        `NavigateToWaypointHandlerTests`, `FlightLogLinesTests`, `AlreadyAtDestinationLoopTests`,
        `FleetStatusQueryServiceTests`, `ShipRuleTests` (a comment); `ShipGoalSerializationTests` (Domain);
        `PrometheusMetricsTests` (API); docs: `docs/HOW_IT_WORKS.md`, the `st-investigate` skill (a drift takes hours).
    - **Follow-up, B54** (projects#140, deployed by gembernodes#38): the survey plan counts an asteroid where a miner could
      mine it for the market, as the mining plan reckons the trip (in CRUISE, there and on to the market with the fuel
      left), not where a miner merely reaches it; and a drone still drifting to a far market counts once it is there. To
      understand it, start with `SurveyTargets` in `Mining/MiningPlanner.cs`, then `MinersAsync` in
      `Automation/SurveyPlanService.cs`. Tests: `MiningPlannerTests` and `SurveyPlanServiceTests` (one new each).
    - **Follow-up, B55** (projects#141, deployed by gembernodes#39): the survey plan's state lists the surveyors that can
      reach each target (`CandidateShipSymbols`), and `ShipLeftIdle` counts a target as work only for those. Tests:
      `ShipRuleTests`, `SurveyPlanServiceTests` (one new each).
    - **Follow-up, D52** (projects#142, deployed by gembernodes#40 at 14:00Z): a ship that can only survey
      (`FleetRoles.CanOnlySurvey`), with every ore it reaches at its stock, surveys on: the target it reaches with the
      fewest usable surveys first. `ShipLeftIdle` counts any target it reaches as work for it. Start with the surveyor
      loop in `Automation/SurveyPlanService.cs`. Tests: `SurveyPlanServiceTests`, `ShipRuleTests` (one new each; B55's
      test keeps its point without a target the surveyor reaches).
    - **Follow-up, D53** (branch `claude/spacetraders-coverage-per-area`): coverage per area.
      - **A trip covers its mineral near the market it sells at** (`CoveringTrip`, `MiningPlanner.Covers`): at the markets
        its ship reaches in CRUISE from there, through refuelling stops, leaving with a full tank. The mining and siphon
        plans list the trips under way as `CoveringTrip`s (the mineral, the market, the ship's tank), and `UncoveredFirst`
        puts first the SCARCE or LIMITED targets no trip covers. A drone at B7 doesn't cover the middle, nor one in the
        middle B7; the command ship's 400-unit tank reaches both, so its trip covers both.
      - **The coverage tier counts areas** (`ScarceOres`, `ScarceGases`, `MiningPlanner.Areas`): each SCARCE or LIMITED
        mineral once per area, an area being the markets short of it that a new drone flies between in CRUISE (two markets
        share one when either reaches the other). So it buys a drone per scarce mineral and area, ahead of the cargo ships
        (D43), as before without asking the role board.
      - **The role board keeps one drone per mineral and area** (`MineralCoverage.MarketSymbols`): the areas are those of
        the system's drones of the kind (the smallest tank among them); the drone kept for one is the one whose trip covers
        it, else as before.
      - **What to expect,** from the markets as the bot last saw them at 14:30Z (B7's at 13:30Z): the middle is short of
        aluminum (H51) and silicon (A3, F49, H53), and B7 of aluminum, copper, iron, quartz and silicon, all from B14 (B7's
        gold, silver and platinum come from B37 or B10, beyond a drone's round trip; nothing in a drone's reach yields the
        ammonia ice, diamonds or precious stones that H53, J58 and H54 want). That is seven ores and areas for five mining
        drones, against five ores before, so the coverage tier wants two more drones; a free drone in the middle, or a new
        one, takes whichever of the middle's two ores has no drone, and the drones at B7 keep to B7's ores, as uncovered ores
        in reach come before those a drift away. Siphon drones reach every gas market in X1-DC53 from C38, one area, so nothing
        changes for them.
      - To understand it, start with `UncoveredFirst` and `Areas` in `Mining/MiningPlanner.cs` (`CoveringTrip` and
        `MineralArea` at the end of the file), then `Minerals` in `Automation/RolePlanService.cs`.
      - Tests: App 832 (9 new: `MiningPlannerTests` 3, `SiphonPlannerTests` 2, `MiningAutomationServiceTests` 2,
        `SiphonAutomationServiceTests` 1, `RolePlannerTests` 1; rewritten to keep their point with the extra area:
        `MiningAutomationServiceTests` 3, `RolePlanServiceTests` 2; the scarce ores and gases are listed per area), Domain
        71, API 161 (and 4 skipped, as on main).
        On the old code the new tests of the plans and the board fail as the cluster did: the free drone drifts to B7's
        gold instead of mining the middle's copper, and the purchase comes as `Alternating`, not `Coverage`.
      - Deployed by gembernodes#41 (merged before the CI run had pushed the images: the API, which is replaced rather than
        rolled, was down 14:38:55–14:40:33Z). The coverage tier bought SPECTER-11 at 15:16:59Z, which the board kept for
        coverage and which took the middle's silicon (XB5C for H53).
    - **Follow-up, D54** (branch `claude/spacetraders-surveyor-follows-drones`): the survey ship works where most drones mine.
      - **Where it works** (`MiningPlanner.TryFindBusierArea`, `SurveyorMove`): the survey plan counts the mining drones by
        where they work, the market their trip sells at (a drone still drifting there included), else where they are;
        its own area is what it reaches in CRUISE, and the drones beyond that group into areas as it would fly between
        them (`Areas`' grouping). An area with more drones than its own gets it: the market there, among those that sell
        fuel, where the most drones work. A tie keeps it where it is. Only a ship that can only survey moves.
      - **The move** (`MoveToWaypointGoal.Drifting`, new `MoveToWaypointGoalExecutor`): the goal existed with no executor.
        One flight, in DRIFT out of CRUISE reach (`DriftStarted`), else in CRUISE; at the target it ends, and the survey
        plan gives the ship its surveys there (D27, D52). The fleet view says `drifting to X1-DC53-B7`; the plan logs
        "moves to … (D54)" with both counts.
      - **What to expect:** at 15:30Z B7 had four mining drones (SPECTER-4 mining, -10, -A and -9 drifting there) against
        two in the middle (SPECTER-3, SPECTER-11), so SPECTER-F drifts to B7 at its first free tick after the deploy, about
        2.5 hours, and surveys B14 from there. The middle's drones then mine
        without surveys, by design; the command ship doesn't survey (D38).
      - To understand it, start with `TryFindBusierArea` in `Mining/MiningPlanner.cs`, then the surveyor loop in
        `Automation/SurveyPlanService.cs` and `Goals/Executors/MoveToWaypointGoalExecutor.cs`.
      - Tests: App 842 (10 new: `MiningPlannerTests` 4, `SurveyPlanServiceTests` 2, `MoveToWaypointGoalExecutorTests` 4),
        Domain 72 (1 new: `ShipGoalSerializationTests`), API 161 (and 4 skipped; `PrometheusMetricsTests` and
        `FleetStatusQueryServiceTests` cover the drifting survey ship's words).
    - **Follow-up, D55** (branch `claude/spacetraders-surveyor-per-area`, on top of D54's): a survey ship per area.
      - **The order** (`PurchaseTier.SurveyorPerArea`, 4): one more surveyor, after the drones per scarce mineral and area
        and before the cargo ships; the cargo ships, probes and turns moved one place down (5, 6, 7), as does `position` in
        `spacetraders_purchase_need_credits`. The dashboard reads the labels as they come.
      - **The need** (`SurveyorNeedAsync`, `MiningPlanner.CountAreas`): a system with no ship that can only survey needs
        its first (`Surveyor`, D47); one with fewer of them than areas with mining drones (the drones where they work,
        grouped with the survey ships' smallest tank) needs one more (`SurveyorPerArea`), at the same shipyard.
      - **Where each works** (`TryFindBusierArea` with the other survey ships): the plan notes where each ship that can only
        survey works, where it is or where it is moving to; an area with one of them is taken. A survey ship alone in its
        area moves only to an area that isn't taken and has more drones than its own (D54); one that shares its area moves
        to the busiest area with drones that isn't taken. The log says "moves to …, where N mining drones work and no
        other survey ship, against M in its own area[, which another survey ship works in] (D54, D55)".
      - **What to expect** after D54 and D55 are deployed, from 15:45Z: SPECTER-F drifts to B7 (four drones against three in
        the middle), and the survey plan wants a second survey ship (33,905 at H52) for the middle, bought once the
        coverage drones are (the third, for B7's aluminum, may still be wanted) and the credits allow; it surveys XB5C
        where it is bought. If SPECTER-F is still in the middle when the second arrives, one of the two drifts to B7.
      - To understand it, start with `TryFindBusierArea` and `CountAreas` in `Mining/MiningPlanner.cs`, then
        `SurveyorNeedAsync` and the surveyor loop in `Automation/SurveyPlanService.cs`, then `PurchaseTier` in
        `Services/PurchaseOrder.cs`.
      - Tests: App 849 (7 new: `MiningPlannerTests` 3, `SurveyPlanServiceTests` 3, `PurchaseOrderTests` 1; D54's planner tests
        pass no other survey ship), Domain 72, API 161 (and 4 skipped; the positions in `PrometheusMetricsTests` follow the
        new order).
    - **Follow-up, D56** (branch `claude/spacetraders-full-holds`): full holds only, in one purchase and one sale.
      - **A route** (`TradeRoutePlanner.TakesFullHold`, `TryEvaluateFrom`): its units are the ship's free hold; it counts only
        when the buy market's and the sell market's trade volumes are both at least that, and the credits for cargo, the
        trip's fuel kept back, pay for all of it. The held-cargo sale (D42) is unchanged: it sells what is aboard.
      - **At the buy market** (`TradeBetweenMarketsGoalExecutor`): the trip is worked out again with the prices just
        fetched; a market that no longer trades the full hold at once drops it (`TradeDropped`, `Reason` `not_full_hold`),
        as do too few credits (`not_possible`). The purchase is the whole hold.
      - **Saving up** (`FullHoldSavings`, `TradingAutomationService.NoteSaving`): a free trader whose best route, credits
        aside, is a hold the credits don't pay for logs "saves up for a full hold of … (D56)" and takes the best hold it
        can pay for meanwhile, or none. `BudgetPolicy` adds the dearest saving to the credit reserve, and so does
        `spacetraders_credit_reserve`. The saving ends when that hold is bought, when the trader's best route is one it
        can pay for, or when the ship no longer trades; a restart forgets it until the plan's next pass.
      - **What to expect** after the deploy: at 18:25Z on 2026-10-03, 84 of the 124 goods the markets of X1-DC53 listed,
        fuel aside, traded at least 40 at once, and 26 at least 80 (158 pairs of markets for a 40-unit hold, 18 for an
        80-unit one). The command ship (40) trades only those routes; EQUIPMENT from K85 (43 at once) has no buyer that
        takes 40 (20 each), and SHIP_PARTS (15 at D41, 6 to 13 where it is sold) fills no hold, not even a drone's 15. Its
        purchases are all 40 units; with about 120,000 credits a 40-unit hold of a good dearer than about 3,000 a unit is
        saved up for, and ship purchases wait meanwhile (the reserve shows it). At 18:25Z's prices the best 40-unit routes
        were FABRICS from D43 (2,622 a unit, 104,880 the hold) to D41 and K85, 70 and 48 a unit more, then IRON from H51
        (22) and PRECIOUS_STONES from J58 (18), against `Trade.MinProfitPerUnit` 5. Drones trade only 15-unit holds.
      - To understand it, start with `TakesFullHold` and `TryEvaluateFrom` in `Trading/TradeRoutePlanner.cs`, then
        `NoteSaving` in `Automation/TradingAutomationService.cs`, `Trading/FullHoldSavings.cs` and
        `Orchestration/BudgetPolicy.cs`.
      - Tests: App 857 (6 new, 3 rewritten: `TradeRoutePlannerTests`, `TradeBetweenMarketsGoalExecutorTests`,
        `TradingAutomationServiceTests`, `BudgetPolicyTests`; the trading fixture's EQUIPMENT and MEDICINE trade 40 at once,
        and its credits are 250,000), Domain 72, API 162 (and 4 skipped; the credit-reserve theory has a case with a
        saving).
    - **Follow-up, D57** (branch `claude/spacetraders-reserve-trip-credits`): credits held back for a trip.
      - **What a trip holds back** (`TradeBetweenMarketsGoal.ReservedCredits`, `Trading/TripReservations.cs`): its units at
        the buy price it was chosen with, from the moment the trading plan starts it until its cargo is aboard; nothing
        once bought, blocked or done. It is stored with the goal, so a restart keeps it, and a trip stored before D57
        holds back nothing.
      - **Who leaves it:** the trading plan gives free traders only the credits no trip on its way to buy holds back;
        the trade executor at the buy market spends its own and those no other trip holds back (the goal store reads the
        fleet's trips at once, `GetActiveTradeGoalsAsync`); `BudgetPolicy` adds the holds to the credits every ship
        purchase must leave, and `spacetraders_credit_reserve` shows them. A trader that sets off for the hold it saved
        up for (D56) saves up no more: the trip's hold takes its place.
      - **What to expect:** no trade trip dropped `not_possible` at its buy market because another trader spent the
        credits; while the command ship flies to buy a dear hold, the reserve shows it and ship purchases wait. A price
        that rose since the trip was chosen (COPPER at H51: chosen at 270, bought at 286 on 2026-10-03) is paid from
        the credits no trip holds back.
      - To understand it, start with `Trading/TripReservations.cs`, then `AssignRoutesAsync` and `RouteGoal` in
        `Automation/TradingAutomationService.cs`, the buy step of `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs` and
        `Orchestration/BudgetPolicy.cs`.
      - Tests: App 863 (4 new: `TradingAutomationServiceTests` 1, `TradeBetweenMarketsGoalExecutorTests` 2,
        `BudgetPolicyTests` 1; D56's saving test now ends in the trip's hold), Domain 72, API 164 (and 4 skipped; two
        credit-reserve cases with a trip's hold), and `ShipGoalRepositoryTests` 13 against Postgres (1 new).
    - **Follow-up, D58** (branch `claude/spacetraders-drones-gather-first`): drones gather first.
      - **The board** (`RolePlanner`, reason `gathers_first`): after the coverage keepers (D48, D53), every other drone
        takes its gathering role, Mine for a mining drone and Siphon for a siphon drone, whatever trading would pay. A
        drone is a ship whose roles include mining or siphoning and that has no surveyor (`FleetRoles.CanSurvey`), so
        the command ship still shares the work by profit. Nothing changes in the plans: a drone with the mining or
        siphon role already trades when its plan has no trip for it.
      - **Purchases:** the coverage tier counts as before; the last tier's check that the board would have a new drone
        gather (`RoleAdvisor`) now always passes, so such a drone is bought when its first trip would serve a market
        short of its ore or gas, and no miner (or siphoner) is free.
      - **What to expect:** no more `RoleChanged` lines moving a drone to Trade "most_profitable"; drones that traded
        (SPECTER-3, and at times all seven siphon drones) gather at the next evaluation, within 10 minutes
        of the deploy, as their trips end (D41). With the minerals they mine supplied, fewer go short, so the coverage
        tier should stop buying drones for them.
      - To understand it, start with `Decide` and `GathersFirst` in `Roles/RolePlanner.cs`.
      - Tests: App 864 (1 new, `RolePlannerTests.ADrone_GathersFirst_ThoughTradingWouldPayItMore`; the tests of the
        fleet-wide assignment, the head start and "no work" now use ships that can survey, and the coverage tests expect
        the other drones to gather), Domain 72, API 164 (and 4 skipped).
  - **To understand this,** start with the decisions D43–D58, then this slice's notes; 6.10b and 6.10c each have their own
    entry.

- **6.11 Exploring through the jump gates, and a systems dashboard** (asked 2026-10-04, with your decisions D59–D63; merged
  as projects#153, its dashboard as gembernodes#51, deployed by gembernodes#52; built on branch `ccr-c7061120-est7c1`; fixes
  B60). Asked: "if an active jump gate goes to a system
  that isn't explored yet, the COMMAND ship should go through that jump gate. If there are markets or shipyard there, the
  COMMAND ship should scout them, as it initially does for the home system, recursively." And during the work: "I'd
  probably want a systems grafana dashboard with a more wide view of which systems have been explored and what kind of
  mining, trading and shipyard opportunities it gives."
  - **What the API said** (2026-10-04, public endpoints and the spec, v2.3.0):
    - X1-DC53's gate, I55, is built; its market exchanges ANTIMATTER and FUEL. It connects to X1-KR90 (18 waypoints, 7
      markets, no shipyard) and X1-MT49 (31 waypoints, 11 markets, two gas giants, no shipyard), both built, and to X1-HZ59
      and X1-BG54 (each about 90 waypoints, 26 to 28 markets and 3 shipyards), whose gates are under construction. A walk
      through the connections passed 60 systems within 7 jumps; farther out, systems such as X1-VR15 and X1-CV66 have a
      shipyard each.
    - A jump (`POST my/ships/{ship}/jump`) takes the destination gate's `waypointSymbol`, needs the ship in orbit at a gate
      and both gates built, buys one ANTIMATTER at the gate's market, and starts a cooldown (B60: the bot's call sent the
      destination's system).
    - An uncharted waypoint shows the trait `UNCHARTED` instead of its own. X1-DC53 and its four neighbours have none;
      some systems farther out do (X1-UF58: 13 of 34).
    - The server resets weekly; the next is on 2026-10-04 at 13:00Z, so this runs under a new agent, maybe in another
      system: nothing in it is tied to X1-DC53.
  - Done:
    - **The explore plan** (`Exploring/ExplorePlanService.cs`, switch `Automation.Plan.Explore.Enabled`, off by
      default): bootstrapped after the scout plan and before the role board. It knows every system it has seen
      (`ExplorePlanState`, `plan_states` row `Explore`): the gate, whether it is built, the gates it connects to, when a
      jump there was refused and when it was explored. Home counts as explored (the scout plan's). It learns one thing
      from the API a pass (D19): home's gate, then each explored system's connections, then each connected gate; a gate
      under construction, or one a jump was refused at, is looked at again hourly. A system the ship has just jumped into
      has its system and waypoints fetched and cached at once, as startup sync does.
    - **The command ship** is taken when its trip ends (D61): free, with a system left to explore; an `Explore`
      assignment keeps the other plans off it. It goes to the nearest system not explored yet by jumps through built
      gates, no limit (D59, `ExploreAtlas`), jumps home when none is left (D60) and is released there.
    - **A jump** (`JumpGoal`, `JumpGoalExecutor`): fly to the gate, fill the tank when docked at a gate that sells fuel,
      orbit, wait out the cooldown, jump. Only while the credits after the antimatter stay at or above
      `FleetExpansion.MinCreditReserve` (D63); until then the plan holds it (`PlanBlocked`, `waiting_for_credits`, once),
      and a free command ship keeps its other work. The antimatter is booked (`AntimatterPurchase`). A jump the API
      refuses blocks the goal (`jump_refused`); the plan leaves that gate an hour and chooses again.
    - **Scouting a system** (`ExploreSystemGoal`, `ExploreSystemGoalExecutor`): each market and shipyard not cached yet,
      nearest first from the gate, once; the arrival stores them, and the gate it jumped to is fetched there. Nothing is
      charted (D62). Journal `SystemExplored` when it is done; a system without either is explored at once.
    - **Business stays home** (D60, `Automation/BusinessSystems.cs`): the mining, siphon and trading plans buy ships, and
      the contract plan looks for its drone's shipyard, only in systems where a ship that doesn't explore is
      (`IShipyardRepository.FindShipyardForTypeAsync` takes those systems); the mining, siphon and survey plans plan no
      work for the system the explorer is in.
    - **Visibility:** journal kinds `Jumped` and `SystemExplored`, and the plan's `PlanStarted`, `PlanCompleted` and
      `PlanBlocked`; the ship's activity reads "jumping to …" or "exploring …". Eleven `spacetraders_system_*` gauges
      (`SystemOpportunities`, every minute): each known system's state, jumps from home, when it was explored, its
      connections, markets, shipyards and uncharted waypoints, waypoints by type, gathering sites per good (asteroids by
      their deposits, gas giants), the best price its markets pay for each ore and gas with the lowest supply, and its
      five best trades with their volume. A system only explored keeps its markets' refresh times but not each good's
      price series, so Prometheus doesn't grow with every system explored.
    - **The systems dashboard** (gembernodes, uid `spacetraders-systems`): counts of systems known, explored, to explore
      and behind gates under construction, and the antimatter bought; the command ship; a table per system; mining and
      siphoning; trades; shipyards; gates; systems over time; the exploring journal. The two other SpaceTraders
      dashboards link to it.
  - **What to expect** once switched on: within a minute (one call a 5-second pass) the plan has looked at home's gate,
    what it connects to and each gate there; at the end of the command ship's trip, `PlanStarted` ("explores X1-…, 1 jumps
    away"), the flight to the gate, `Jumped`, then the new system's markets and shipyards one by one, `SystemExplored`,
    the next jump. Each jump costs one ANTIMATTER at the gate's price; while the credits after it would be under the
    floor, `PlanBlocked` says so once and the ship keeps working. With nothing left it jumps home, `PlanCompleted`, and
    the other plans give it work again. The systems dashboard fills as it goes. While it is away it neither surveys nor
    trades at home: a ship that can only survey (D47) surveys on, and without one the drones mine without surveys.
  - Noticed (not changed):
    - **A gate the API won't describe:** the connections of a gate that is uncharted may be refused, or come back
      empty; the plan treats either as no connections and asks again hourly. Charting it (D62) would settle that.
    - **The role board still evaluates the command ship while it explores,** in the system it is in; the plans give it
      no work there (it is assigned), so only the board's state and `RoleChanged` lines show it.
    - **The credit reserve still counts the command ship's 40-unit hold** while it explores (D51), 40,000 credits that
      no trade of it will spend meanwhile. Yours to call.
    - **`JumpGateCacheService`** is registered but nothing calls it (it was already unused); the explore plan keeps the
      gates in its own state.
    - The explore plan is off by default, as every new plan (D9), and the reset of 13:00Z registers a new agent with the
      seeded settings: switch it on after the reset.
  - To switch it on: `PUT /settings/Automation.Plan.Explore.Enabled` with `{"value": "true"}`.
  - Done when: the command ship has explored every system the built gates reach and is back home, with no open anomaly.
  - **The first run** (switched on at 11:54Z on 2026-10-04): the plan started at 12:24Z, SPECTER-1 jumped to X1-KR90 at
    12:31Z (antimatter 7,723), explored its 7 markets by 12:46Z and jumped on to X1-CV66 at 12:57Z (4,862), when the reset
    ended the run. In X1-FJ91 the home gate is still to be built (6.6), so the plan has nothing to jump through until then;
    its state says done, and it looks at the gate again every hour.
  - **To understand this,** start with `SpaceTraders.Application/Exploring/ExplorePlanService.cs` and `ExploreAtlas.cs`,
    then the two executors (`Goals/Executors/JumpGoalExecutor.cs`, `ExploreSystemGoalExecutor.cs`) and
    `Automation/BusinessSystems.cs`; for the dashboard, `Exploring/SystemOpportunities.cs`.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Exploring/ExplorePlanService.cs`, `ExplorePlanState.cs`, `ExploreAtlas.cs`, `SystemOpportunities.cs`;
      `Goals/Executors/JumpGoalExecutor.cs`, `ExploreSystemGoalExecutor.cs`; `Automation/BusinessSystems.cs`;
      `Ports/WaypointSymbols.cs`, `JumpRefusedException.cs`;
    - changed: `Ports/ISpaceTradersPort.cs` (`GetWaypointAsync`, the jump's gate), `ApiPortModels.cs`;
      `Automation/AutomationSwitches.cs`, `GameLoopService.cs`, `ScoutAllMarketplacesPlanState.cs` (`PlanTypes.Explore`),
      `MiningAutomationService.cs`, `SiphonAutomationService.cs`, `TradingAutomationService.cs`, `SurveyPlanService.cs`,
      `ContractPlanService.cs`; `Goals/ShipGoalExecutorService.cs`; `EventHandlers/LedgerEntryHandler.cs`;
      `JournalEvents.cs`; `Services/FleetStatusQueryService.cs`; `Interfaces/IAutomationMetrics.cs`,
      `Repositories/IShipyardRepository.cs`; `DependencyInjection.cs`; `ShipGoal.cs`, `ShipGoalKind.cs`,
      `LedgerCategory.cs`, `DomainEvents.cs` (Domain); `SpaceTradersApiClient.cs`, `ISpaceTradersApiClient.cs`,
      `Phase1ActionModels.cs`, `SpaceTradersPortAdapter.cs` (SpaceTradersAPI); `ShipyardRepository.cs`,
      `DefaultSettingsSeed.cs` (Persistence); `PrometheusAutomationMetrics.cs`, `PrometheusMarketMetricsService.cs`,
      `PrometheusMetricsService.cs` (API);
    - tests: `Exploring/ExplorePlanServiceTests`, `ExploreAtlasTests`, `JumpGoalExecutorTests`,
      `ExploreSystemGoalExecutorTests`, `SystemOpportunitiesTests`, `Ports/JumpRequestTests` (new); additions to
      `MiningAutomationServiceTests`, `SiphonAutomationServiceTests`, `SurveyPlanServiceTests`,
      `TradingAutomationServiceTests`, `ContractPlanServiceTests`, `AutomationSwitchesTests`,
      `ShipGoalExecutorServiceTests`, `LedgerEntryHandlerTests`, `DefaultSettingsSeedTests`; `ShipyardRepositoryTests`
      (Infrastructure); `PrometheusMetricsTests`, `PrometheusMarketMetricsServiceTests`, `MetricsEndpointTests` (API).
  - Tests: App 907 (new: `ExplorePlanServiceTests` 8, `JumpGoalExecutorTests` 6, `ExploreSystemGoalExecutorTests` 6,
    `ExploreAtlasTests` 4, `SystemOpportunitiesTests` 4, `JumpRequestTests` 3; one each for the mining, siphon, survey,
    trading and contract plans, which failed before the change; the antimatter's ledger row; the two new goals in
    `ShipGoalExecutorServiceTests`), Domain 72, API 167 (and 6 skipped: the sandbox, and Postgres without Docker),
    Integration 1. `ShipyardRepositoryTests` (Infrastructure) need Docker and didn't run here.

- **6.12 Mining drones keep what they can sell within one tank** (done: merged 2026-10-04 as projects#165, deployed by
  gembernodes#60; asked that day, D71). Asked: "I'd like to have the configuration for mining drones only
  to throw out minerals that they cannot sell within a single tank of fuel, instead of everything they're not specifically
  mining for. They still should use the survey with the highest chance of getting the minerals they want, just added with
  some extra trips to sell other ores as well."
  - Done:
    - **What a trip keeps** (`MineResourceVolumeCommand.KeepOtherOres`, set only by the mining plan's trips): after each
      extraction, the trip's own ore, and every other ore a market buys within one tank of the asteroid
      (`MiningPlanner.IsSellableWithinOneTank`: a full tank's CRUISE flight there without a refuelling stop). The rest is
      jettisoned, as before. The survey it extracts with is still the best for its own ore (`SurveySelection`). This holds
      for whichever ship has the trip: a drone, or the command ship with the mining role, whose 400-unit tank keeps nearly
      every ore a market buys.
    - **Selling them:** the trip turns to selling when the hold is full, of whatever ores, and sells its own ore at its
      market. The mining plan then sells the others, one good a trip, where each fetches most after fuel (`held_cargo`),
      by the rule traders and siphoners sell held cargo by (`TradeRoutePlanner.TryFindBestCargoSale`), which replaces the
      plan's own copy of it. As in the siphon plan, a full hold sells even where the sale doesn't pay for its fuel, and a
      full hold no market it can reach buys gets no trip, so the trading plan jettisons it (D42): a mining trip would
      otherwise turn to selling at once and end without its ore aboard, on every tick.
    - **The contract's round trips** keep only the contract's ore (D71): the tick's contract work sends the command
      without the flag.
  - Noticed (not changed):
    - The role board values a mining trip by a full hold of its own ore (`RoleEstimator`), so the ores it keeps beside it
      count for nothing there: mining earns a little more than the board says. Since D58 a drone gathers whatever the
      estimate, so it weighs only where the command ship's roles are compared.
    - An ore kept whose sale doesn't pay for the fuel to its market stays aboard until it pays, or until a full hold sells
      it at a loss, as a siphon's gas does. A drone's flight within one tank costs at most one unit of FUEL at the market
      (72 to 97 credits in X1-DC53's markets on 2026-10-02), less than a few units of almost any ore.
  - Tests: `MineResourceVolumeHandlerTests` (a mining trip keeps the ore a market buys within one tank, and jettisons the
    one only a refuelling stop away and the one nobody buys; the contract's keeps neither; a full hold keeps what it keeps),
    `MineAndSellGoalExecutorTests` (the trip asks to keep other ores; a full hold of mixed ores turns it to selling),
    `MiningAutomationServiceTests` (a full hold sells at a loss; a full hold nobody buys gets no trip). Each failed before
    the change, but the contract's, which shows it unchanged. App 1027, Domain 72, API 189 (and 4 skipped), Integration 1.
  - To understand this, start with `JettisonWhatTheTripDoesNotKeepAsync` in
    `SpaceTraders.Application/Commands/Ships/MineResourceVolumeCommand.cs`, then `MiningPlanner.IsSellableWithinOneTank`
    and `GiveTripAsync` in `Automation/MiningAutomationService.cs`.

- **6.13 Less than a full hold where the seller's supply is ABUNDANT** (done: merged 2026-10-04 as projects#167, deployed
  by gembernodes#63; asked that day, D74). SHIP_PARTS went untraded in X1-FJ91 at a difference of 5,008 a unit: they were ABUNDANT with a
  trade volume of 6, and D56 wants a full hold. Asked: "either a full hold needs to be obtained, or the supply
  of the seller needs to be ABUNDANT, in which case a full hold is not necessary. All other rules for profitability etc.
  Still stand."
  - Done:
    - **A route's units** (`TradeRoutePlanner.UnitsAtOnce`, which replaces `TakesFullHold`): the free hold when both
      markets' trade volumes take it at once (D56); otherwise, where the buy market's supply of the good is ABUNDANT, the
      smallest of the free hold and the two trade volumes; otherwise no route. Still one purchase and one sale. The
      credits pay for all of it, and the trip holds them back from the start (D57).
    - **At the buy market** (`TradeBetweenMarketsGoalExecutor`): the trip is worked out again with what its arrival
      fetched, the supply included. It buys what the markets trade at once then; a seller no longer ABUNDANT whose
      volume fills no hold drops the trip (`not_full_hold`), as before.
    - **Unchanged:** the minimum profit per unit (D14), the order of routes (D15), the fuel and its reserve (D24),
      saving up for a hold the credits don't pay for (D56, now for a part hold too; its log line still says "full
      hold"), and the construction plan's loads (D67).
  - Noticed (not changed):
    - **D15 with small trips:** a route that feeds a pricier good's production still comes before any that doesn't,
      whatever the profit. A part hold that feeds production can now win over a full hold that earns far more, and with
      `Trade.MinProfitPerUnit` low it takes little for a part hold to count as lucrative.
    - **Cargo ships:** a new cargo ship counts as having work when a part hold would wait for it (D21, D43), so its
      purchase no longer needs markets that trade its whole hold at once.
    - **The role board** (`RoleEstimator`) values trading by the same routes, part holds included.
  - Tests: `TradeRoutePlannerTests` (the units at an ABUNDANT seller; no exception at any other supply, or for the
    buyer's; the credits and the minimum profit per unit still apply), `TradeBetweenMarketsGoalExecutorTests` (the part
    hold bought in one purchase; the trip dropped once the seller is no longer ABUNDANT) and
    `TradingAutomationServiceTests` (a trader takes the part hold and holds back what it costs). Each failed before the
    change, but those that show D56 unchanged. App 1074, Domain 72, API 204 (and 4 skipped), Integration 1.
  - To understand this, start with `UnitsAtOnce` and `TryEvaluateFrom` in `Trading/TradeRoutePlanner.cs`, then the buy
    step of `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`.

- **6.14 Drones gather until every mineral is ABUNDANT** (built on branch `claude/eager-allen-5q9biz`, asked on 2026-10-05, D77;
  D78 for the cargo ships). Asked: "I'd like the miners to only mine, even if there is more profit in trading. They can mine
  until every mineral is ABUNDANT." And: "Can you check if trade ships are only bought if there are unserved routes for them? I
  now have 2 trade ships that are mostly idling."
  - Found, by reading the code (the cluster isn't reachable from the cloud session that built this):
    - Profit didn't move the drones: since D58 the role board gives every drone its gathering role whatever trading pays. A drone
      traded when its plan had no trip for it: the mining plan puts one miner on each sell market and ore (D28), so a drone whose
      every pair in reach was taken was passed over to the trading plan (B63). A market that had an ore ABUNDANT was still mined for,
      as the lowest supply left (D28).
    - A cargo ship is bought only for a lucrative route no trader holds, at one pass (D21); see D78.
  - Done:
    - **No target at ABUNDANT** (`MiningPlanner.IsAbundant`): `MiningPlanner.MiningTargets` and `SiphonPlanner.SiphonTargets`
      leave out a market that has the mineral ABUNDANT; HIGH still counts. The role board's estimates (`RoleEstimator`) read the
      same targets, so a trip for an ABUNDANT market counts for nothing there either.
    - **Sharing** (`MiningPlanner.SharedTargets`, `SiphonPlanner.SharedTargets`): a mining drone (`FleetRoles.IsMiningDrone`) or
      siphon drone (`FleetRoles.IsSiphoner`) that finds no pair below ABUNDANT without a ship takes one that has one: the lowest
      supply first, a pair in CRUISE reach before one a drift away (D45), then the pair with the fewest ships on it (the trips
      under way, and those given out earlier in the same pass), then the plan's own order (surveyed, what a single extraction or
      siphon is expected to fetch, the nearest source). Journal: `MiningStarted` or `SiphonStarted`, reason `shared`.
    - **Trading:** a drone is passed over to the trading plan (B63) only when nothing below ABUNDANT is left that it can reach and
      sell, or with a full hold nobody it can reach buys (D42), as before. The trading plan itself is unchanged.
    - **Unchanged:** drone purchases (D28, D32, D48, D53): a new drone's first trip is still judged with the pairs under way held,
      so a pair it would only share buys none. The command ship shares nothing (D38): with every pair taken it is passed over, as
      before. The contract's miners (D23, D40) and the spare-time plan (D34) are untouched, and so are the cargo ships (D78).
  - Noticed (not changed):
    - **Surveys:** the survey plan's targets (`MiningPlanner.SurveyTargets`) still count every market that buys an ore, an
      ABUNDANT one too, so a survey can be taken for an ore no miner will mine near there.
    - **A far pair to share:** the lowest supply comes first, so a drone may drift to a SCARCE pair another drone already works,
      about 2.5 hours from the middle of X1-DC53 to B7, rather than share a LIMITED one in reach (D28 with D45, as for a pair
      nobody works).
    - **The state:** an opening several drones share names one of them as its ship (`AssignedShipSymbol`).
    - **Cargo ships (D78):** while `Trade.ShipPurchases` has a ship left to buy, it is saved up for whatever the routes, and the
      jump gate's loads (D64), the probes and further drones wait behind it (D43). With its traders idle, that ship may not be
      bought for a long time.
  - Tests: `MiningPlannerTests` (no target at ABUNDANT, one at HIGH; the order a drone shares in; no shared pair at ABUNDANT),
    `MiningAutomationServiceTests` (a drone shares rather than be passed over, and two share in the same pass; a drone shares
    rather than mine for an ABUNDANT market; with every ore ABUNDANT it gets no trip and may trade; the command ship shares
    nothing; no drone is bought for a pair it would only share), and the same for gases in `SiphonPlannerTests` and
    `SiphonAutomationServiceTests`. Each failed before the change (against a `SharedTargets` that found nothing), but those
    that show HIGH, the command ship and the purchases unchanged, and the two that share no pair at ABUNDANT, which nothing
    passes too. App 1114, Domain 72, API 210 (and 4 skipped), Integration 1.
  - To understand this, start with `SharedTargets` and `MiningTargets` in `SpaceTraders.Application/Mining/MiningPlanner.cs`,
    then `GiveTripAsync` in `Automation/MiningAutomationService.cs`; the siphon plan mirrors them.

- **6.15 The jump gate's loads in batches** (built on branch `claude/spacetraders-construction-batches`, asked on 2026-10-05,
  D81). Asked: "Also, I'm at 836k, construction should start at 624k. Why isn't construction started?"
  - Found, on the cluster (image `486f581`): the construction plan had started at 10-04 18:09Z and bought nothing since: no
    `ConstructionStarted` at all. The money wasn't it (838,786 credits against a reserve of 314,080). Its builder, SPECTER-D
    with the construction role since 00:38Z (the command ship before it), has an 80-unit hold, and every market in X1-FJ91
    sold the gate's materials 20 at a time: FAB_MATS at F58 (1,084) and D52 (1,159), ADVANCED_CIRCUITRY at D49 (3,578), all
    ABUNDANT. D67 wanted a load in one purchase, so `ConstructionPlanner.Loads` found none, and the builder traded meanwhile.
    The state's `Waiting` stayed empty, as the plan names a reason only for a free builder with an empty hold, and SPECTER-D
    was never both.
  - Found while asking what to do: a market's trade volume is the most one purchase takes, not its stock, and the API gives
    no stock (D79). Across the 170 goods of X1-FJ91 the trade volume was one of 6, 18, 20, 60 or 180, by kind of good, and
    none moved in the run's first 18 hours; in X1-DC53, a week into its reset, 20 of 150 moved over two days, in steps of
    about 10%, up to three times where they started.
  - Done:
    - **A load** (`ConstructionPlanner.Loads`): the builder's free hold, or what the gate still needs, at one market whose
      supply isn't SCARCE or LIMITED (D66), whatever its trade volume. Its cost (`ConstructionLoad.CargoCost`, what the trip
      holds back, D57) is estimated batch by batch (`PriceSteps.CostInBatches`: each batch 2%, 4% or 6% dearer for a good
      traded up to 6, up to 20 or more at a time, the measured medians rounded up), so the credit check (D64) and the
      choice of market count the rise. `WhyNoLoad` names `no_market` or `low_supply`; `trade_volume` is gone.
    - **The purchase** (`SupplyConstructionGoalExecutor`): one batch of the trade volume at a time, each at the price quoted
      then, with `CargoBought` and the market fetched again after each (D25). Before each batch it checks the market as the
      last refresh fetched it: a supply fallen to SCARCE or LIMITED, or a batch that would dip into the reserve, stops the
      buying. Before the first batch that drops the trip, as before; after it, the ship takes what it has to the gate
      (logged at Information). The trip is stored after each batch with what it spent, and holds back only what is left to
      buy, so a restart goes on from what is aboard.
    - **Unchanged:** the order of materials (the smallest share supplied first, then by name) and markets (the cheapest with
      fuel), the purchase order (D64), the reserve, and the trading plan: trade trips still follow D56 and D74 until slice
      6.16 (D79, D80).
  - Noticed (not changed):
    - **The first load:** with D42-style markets now loadable, ADVANCED_CIRCUITRY comes first on an unbuilt gate, by name at
      an equal share of nothing supplied; in X1-FJ91 that is 80 at D49 for about 304,000.
    - **The state:** `Waiting` says nothing while the builder trades. `ReadyShipSymbols` shows whether a load waits for it,
      but a builder that never comes free with an empty hold would hide the reason again.
  - Tests: `ConstructionPlanServiceTests` (an 80-unit builder at markets that sell 20 at a time takes a full hold; failed
    before the change, with no load), `SupplyConstructionGoalExecutorTests` (four batches at rising prices; the trip stored
    between batches; a restart buys what is left; LIMITED, or the reserve, part way stops the buying and the ship goes on
    with what it has), `ConstructionPlannerTests` (a full hold where the market sells less at once, its cost batch by batch)
    and `PriceStepsTests`. App 1126, Domain 72, API 210 (and 4 skipped), Integration 1.
  - To understand this, start with the buy step of `Goals/Executors/SupplyConstructionGoalExecutor.cs`, then `TryLoad` in
    `Construction/ConstructionPlanner.cs` and `Trading/PriceSteps.cs`.

- **6.16 Trade trips in batches, one buyer at a time** (built on branch `claude/spacetraders-trade-batches`, on top of 6.15,
  asked on 2026-10-05, D79 and D80). Asked: "Can you have a look at ships starting to do something, then cancelling without
  finishing? e.g. traders that cancelled trades, and why?", then, once a market's trade volume turned out to be the most one
  purchase takes: "A ship should buy as much as is profitable per trip, and sell as much as is profitable per trip. The
  problem is, with trade volumes of 6, it's very hard to determine whether something is actually profitable if you trade 80
  of them. I'm open to suggestions on this one."
  - Found, on the cluster since the reset (2026-10-04 13:00Z to 10-05 07:00Z): of 250 trade trips 240 sold, 7 were dropped
    on arrival (`not_full_hold`) and 1 as `not_lucrative` (its prices had moved). All 7 had the same cause: the trading plan
    sent two traders, at the same moment, for the same good at the same market with different sell markets (D18 keeps only
    the whole route to one trader). Their haulers' 80-unit holds took the trip only through D74 (20 at an ABUNDANT seller);
    the first purchase ended the ABUNDANT supply, and the second trader dropped its trip on arrival: about 2,000 credits of
    fuel and 47 minutes of hauler time. Of 377 siphon trips 100 ended `nothing_aboard`, and 9 of 522 mining trips: by design
    (D33), see Noticed.
  - Measured for the estimate: the price paid is the price quoted, a full batch bought raises the next quote by a median of
    1.8%, 3.6% or 5.7% (6, 20, 40 of 60 at a time), a sale lowers it about 2%, and a raised price recovers in about an hour.
    The bot had never bought or sold more than one batch at a market, so no batch-after-batch figure exists yet.
  - Done:
    - **A trip's units** (`TradeRoutePlanner.TryEvaluate`): unit by unit, each priced by its batch (`PriceSteps`: bought 2%,
      4% or 6% a batch dearer, sold 2% a batch cheaper), while the next earns `Trade.MinProfitPerUnit`, up to the free hold
      and what the credits pay for once the trip's fuel is kept back; the trip after fuel still earns it a unit (D14). The
      route's `CargoCost` is what the trip holds back (D57). `UnitsAtOnce` and its supply rule are gone.
    - **At the buy market** (`TradeBetweenMarketsGoalExecutor`): a trip that has bought nothing is weighed again with the
      arrival's prices, as before; then it buys a batch at a time while the next units' expected sale still earns the minimum
      over the price quoted now (`TradeRoutePlanner.UnitsWorthBuying`), refreshing the market after each (D25) and storing
      the trip, which holds back only what is left to buy. A restart goes on from what is aboard.
    - **At the sell market:** a batch at a time while the quote earns the minimum over what the cargo cost; when it no
      longer does, the rest goes where it fetches more after fuel, once per trip, or is sold there all the same.
    - **One buyer at a time** (`HeldBuys`, D80): a trade or construction trip on its way to buy holds its good at its buy
      market; the trading plan offers no route of that good from there, and the construction plan buys no load there
      (`market_busy`), until it has bought. Routes given out earlier in the same pass count.
    - **Why a good isn't traded** (D76): `not_full_hold` became `no_room` (no room in the hold, or a market without a price
      or trade volume); a route whose first unit earns too little says what a unit earns.
    - **Unchanged:** D14, D15, D17, D18, D24, D56's saving (now for the trip's units: a trader that sets off with fewer
      units on that route ends it), the cargo ships (D21, D43) and the role board, which values trading by the same routes.
  - Not yet (D79): the price steps are the measured medians for every good and market; learning them per good and market
    from the bot's own trades comes next.
  - Noticed (not changed):
    - **Siphon and mining `nothing_aboard`** (D33): a 15-unit drone's first siphon or extraction brings 11 to 14 units of
      whatever comes up, so the trip's target is luck; the next trip (`held_cargo`) sells the hold, mostly next door. The
      books show a loss of about 130 credits on one trip and the gain on the next.
    - **Liveness probes:** the API pods' liveness probe timed out once at 06:36Z and once at 07:55Z, minutes after a
      start; neither restarted the pod.
    - **More API calls a trip:** each batch is a purchase or sale and a market refresh, about 16 calls for 80 units at a
      trade volume of 20 and 56 at 6.
  - Tests: `TradingAutomationServiceTests` (two traders are never sent for one good at one market; a trip on its way holds
    it until it has bought; fewer units where the credits don't pay for all; failed before the change: both traders went),
    `TradeBetweenMarketsGoalExecutorTests` (batches at rising quotes; it stops when the next would earn too little; the trip
    stored between batches; a restart buys what is left; the rest of a sale taken elsewhere, or sold anyway),
    `TradeRoutePlannerTests` (units by batch, bought and sold; fewer units for fewer credits; the first unit too little; the
    trip after fuel; `UnitsWorthBuying`; one buyer at a time, construction trips too), `TradeRouteJudgementTests`,
    `ConstructionPlannerTests` and `ConstructionPlanServiceTests` (a material another trip is on its way to buy).
    App 1133, Domain 72, API 210 (and 4 skipped), Integration 1.
  - To understand this, start with `TryEvaluateFrom` and `UnitsWorthBuying` in `Trading/TradeRoutePlanner.cs`, then the buy
    and sell steps of `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`, and `Trading/HeldBuys.cs`.

- **6.17 End products last** (built on branch `claude/spacetraders-end-products-last`, asked on 2026-10-05, D82). Asked:
  "Why is iron prioritized over ship parts, although the profit would be a lot higher?", then "Ship parts do feed a
  factory, being the SHIP factory. So while I understand why things like food have a lower priority, this shouldn't be the
  case for ship parts." and "According to the market tree, this should be the list of lower-priority goods: Antimatter,
  Assault Rifles, Clothing, Drugs, Fab Mats, Firearms, Food, Fuel, Ice Water, Jewelry, Medicine, Relic Tech and
  Supergrains."
  - Found, at 09:00Z: the four traders were on trips expected to earn 912 to 2,057 (SILVER, LAB_INSTRUMENTS,
    PRECIOUS_STONES, POLYNUCLEOTIDES), each to a market that makes a pricier good from it (D15), while 14 lucrative routes
    worth up to 95,209 for a 40-unit trader waited behind "the 20 waiting routes listed". D15 counted only a sell market's
    exports: SHIP_PARTS (3,920 at D48, 7,738 at the shipyards' markets) and SHIP_PLATING sell only at A2, C46 and H61, which
    make ships from them and export nothing made from them; MACHINERY, MICROPROCESSORS, ELECTRONICS, EQUIPMENT, FABRICS and
    AMMUNITION failed it at their best markets too. What passed were raw and refined goods with margins of 2 to 42 a unit.
  - The 13 goods asked to come last are exactly those traded in X1-FJ91 that the API's supply chain makes nothing from.
  - Done: `TradeMarketMap.IsEndProduct` says whether nothing in the production chains is made from a good, ships included;
    `TradeRoute.FeedsProduction` is now whether the good is no end product, wherever it is sold, and `Rank` and
    `CompareBestFirst` put those routes first, then the end products' ones, each by profit. Without the chains every good
    is an end product and the routes go by profit alone, as before.
  - Unchanged: `FeedsTradeSymbol`, the pricier good the sell market makes from the cargo, in `TradeStarted` ("which makes …
    from it") and in the routes view; the role board's chain values (D39, D49), which read what a market makes; D14, D18, D79,
    D80.
  - Noticed (not changed):
    - **The routes view** shows nothing in its "feeds" column for a SHIP_PARTS route, which now ranks first: whether a good
      is an end product isn't in the view. Adding it would be a dashboard change in gembernodes.
    - **The role board** values trading by the best route `Rank` gives, which is now often SHIP_PARTS or SHIP_PLATING.
  - Tests: `TradeRoutePlannerTests` (SHIP_PARTS before IRON that feeds MACHINERY, with DRUGS last though it earns most;
    EQUIPMENT's routes before MEDICINE's wherever it is sold; what an end product is; the first two failed before the
    change), `TradingAutomationServiceTests` (the command ship's best route is now EQUIPMENT for A1; a trip to a market that
    makes a pricier good from its cargo still says which), and the API's routes list. App 1136, Domain 72, API 210 (and 4
    skipped), Integration 1.
  - To understand this, start with `IsEndProduct` in `Trading/TradeMarketMap.cs`, then `Rank` and the end of
    `TryEvaluateFrom` in `Trading/TradeRoutePlanner.cs`.

- **6.18 Ore shuttles at far asteroids** (built on branch `claude/spacetraders-ore-shuttle`, asked on 2026-10-05, D83).
  Asked: "Are there no asteroids in the system that yield silver or gold ore? Or are they too far away?", then "Advanced
  strategy, hear me out here. We park a light shuttle per ore type at the asteroid, and have the drones drop their ore into
  the light shuttle. When the light shuttle is full, it sells the ore at the market, then comes back."
  - Found: B44 (PRECIOUS_METAL_DEPOSITS) is 53 from B7, the one market that buys GOLD_ORE, SILVER_ORE and PLATINUM_ORE,
    all three SCARCE there, and B7's GOLD, SILVER and PLATINUM production RESTRICTED. B44 sells no fuel: a drone's 80-unit
    tank doesn't fly the 106 there and back, and D45 counts a far asteroid only within a CRUISE round trip of its market,
    so the mining plan had listed the three openings since 10-04 18:09Z with no drone able to take them, and bought none
    for them (D48: "A mineral no drone can reach ... doesn't count"). The survey ship couldn't survey there either (B58).
    Only the command ship, with its 400-unit tank, mined there once, unsurveyed: 5 SILVER_ORE in 17 extractions. B7 buys
    all eight of B44's ores, the other five as exchange goods. The API moves cargo between two of our ships at one
    waypoint, both docked or both in orbit (`POST my/ships/{ship}/transfer`, v2.3.0), which the bot didn't use.
  - Done:
    - **Collection points** (`MiningPlanner.CollectionPoints`): for a market that sells fuel and buys an ore below
      ABUNDANT (D77) that no drone mines on a round trip of it (D45), the asteroid nearest it that yields the ore, when a
      drone gets there from the market on a full tank and a light shuttle flies there and back. A drone per SCARCE or
      LIMITED ore there (D48).
    - **Parked drones** (`MineForShuttleGoal`): a mining drone whose best target isn't an uncovered ore of its own takes a
      place at a point that wants more drones, drifting to its market first (D45), and stays there: it mines with the
      best survey, hands its hold to the shuttle in orbit there, one transfer per good, and with its hold full and no
      shuttle there waits without an API call. A drone at the asteroid always stays.
    - **The shuttle's rounds** (`CollectOreGoal`, trip activity `collecting`): once a drone is parked there, it waits in
      orbit until its hold is full, or no drone is left, then sells everything at the market (its hold fetched from the
      API first, as the drones' transfers wrote the cache), and the round is booked.
    - **Purchases** (Coverage, as chosen): a light shuttle for a point where a drone has a place; then a point's scarce
      ores count a drone each; a second shuttle when a parked drone waits with a full hold while the first is away
      selling. The shuttle is designated for its point in the mining plan's state; the role board keeps it in the new
      `Collect` role (reason `collection`), out of trading, building, the credit reserve's trading holds (D51) and the
      count towards `Trade.ShipPurchases`. A point that is gone releases its shuttles.
    - **The survey ship** parks too: the survey plan gives each point's ores targets at its asteroid, counts parked
      drones in their market's area (so D55 buys the area a survey ship, which D54 moves there), and lets a ship that
      can only survey survey at a point's asteroid it reaches without flying on (B58).
    - **The API client** transfers cargo (`ISpaceTradersPort.TransferCargoAsync`).
  - Choices (2026-10-05): "Per asteroid, any ore (Recommended)" rather than one shuttle per ore type; "Yes, park one there
    (Recommended)" for the survey ship; "With scarce-mineral drones" for the shuttle and the drones (the survey ship
    comes by D55's per-area rule, the tier right after them); "Only out of drones' reach (Recommended)".
  - Unchanged: every other mining trip, D45's far targets, sharing (D77), the siphon plan.
  - Noticed (not changed):
    - **A collector shuttle still counts as a cargo ship in the purchase order's turns** between drones and cargo ships
      (`PurchaseOrder.Turn`), which goes by ship type.
    - **The shuttle's first round** may wait hours: the drones drift to B7 first, about 2.5 hours from the middle, and
      the shuttle is bought once the first one sets off.
    - **The command ship's role flips between Trade and Siphon** every one to three minutes at times (105 changes since
      10-04 18:09Z; 8 between 03:44 and 03:57Z, Trade estimated at 330,000 to 500,000 an hour against 20,000 to 32,000):
      not the close calls D41's head start is for. Not looked into.
  - Tests: `MiningPlannerTests` (what a collection point is, ABUNDANT, no shuttle, round trips), `MiningAutomationServiceTests`
    (a drone with nothing uncovered joins, drifting first; an uncovered ore of its own first; a drone at the asteroid stays;
    the shuttle collects once a drone is parked, not while they drift; the shuttle bought and designated; the coverage
    count; a second shuttle when a drone waits, none while the first collects), `MineForShuttleGoalExecutorTests`,
    `CollectOreGoalExecutorTests`, `CollectionRoleTests`, `SurveyPlanServiceTests` (a survey ship parks at the point's
    asteroid), and the goal's storage, plan switch, trip book and dispatch. App 1172, Domain 75, API 210 (and 4 skipped),
    Integration 1.
  - To understand this, start with `CollectionPoints` in `Mining/MiningPlanner.cs`, then `GiveTripAsync`,
    `TryJoinPointAsync` and `ShuttleNeed` in `Automation/MiningAutomationService.cs`, and the two executors
    `Goals/Executors/MineForShuttleGoalExecutor.cs` and `CollectOreGoalExecutor.cs`.

- **6.19 The fastest way: cruise, drift and burn** (built on branch `claude/spacetraders-fastest-flights`, asked on
  2026-10-05, D84). Asked: "The excavators are now drifting for 2 hours. Did they start drifting from the location they
  were built, or did they first go to the closest location they can reach with their fuel and then start drifting? More
  generally, can we optimize the routing for a location where a combination of cruising and drifting is faster than just
  drifting?", then "While adding that, can you also add burning to the options? I'd like a ship to burn if they can reach
  the destination with double fuel consumption, but cruise if they cannot. I accept the extra fuel costs this brings, I
  think it is worth it." And, while it was being built: "A ship can technically land anywhere with 1 fuel and then drift to
  a fuel station, so it can cruise to an asteroid with 2 fuel left, drift to the correct asteroid with 1 fuel left, then
  drift back to a fuel station with 0 fuel and refuel."
  - Found:
    - The drones bought for B44's collection point drifted straight from where they were to B7, the point's market: D52 to
      B7, 368, about 2 hours 52 minutes. Every flight out of CRUISE reach drifted all the way (D45's drift step), where
      cruising to a market that sells fuel nearer the target and drifting from there is faster: from H60 to B44, cruising
      to F57 by way of A1 and drifting the 244 from there takes 2.0 hours, against 2.9 by B7, and lands with 79 fuel
      instead of 26.
    - Every flight asked for CRUISE, or DRIFT for a drift; nothing burned.
  - Done:
    - **The next leg** (`TradeRoutePlanner.TryPlanNextLeg`): within reach of a chain of markets that sell fuel, the next
      stop of that flight, as before (B47); out of that reach, the first leg of the fastest way (`TryPlanMixedFlight`):
      A* over the waypoints and the fuel aboard, by the API's flight times at one engine speed (25 per unit of distance
      in CRUISE, 250 in DRIFT, 15 seconds a flight, 10 to refuel at a stop), guided by the straight distance still to go
      in CRUISE. On X1-FJ91's 94 waypoints a way takes about a millisecond (1,504 ways: worst 7 ms; a first version
      without the guide took up to 2 seconds for a 600-unit tank), and none was slower than the first version's.
    - **BURN** (`Burns`): a leg burns when the tank holds twice its CRUISE fuel and burning strands nothing: a leg into a
      market that sells fuel, where the tank fills; elsewhere only when what is left still takes the ship on as cruising
      would, to the trip's market with no more refuelling stops (`onward`), or, without one, to a market that sells fuel
      in CRUISE (B58). In a way out of reach, a cruise leg into a market that sells fuel burns when the tank allows.
    - **No empty tank where no fuel is sold** (`Strands`): no leg lands a ship with nothing left where it can't refuel;
      with 1 left it can always drift on to fuel. A flight in reach whose last leg would empty the tank at an asteroid
      takes the fastest way that doesn't.
    - **Every flight flies its leg's mode**: the goals' flights (`GoalFlight`) and the contract's commands
      (`CommandFlight`). In orbit at a market that sells fuel, a ship docks to fill its tank first when a full tank would
      fly the leg differently (further, or in BURN). The mining and siphon trips' way to a far market, the survey ship's
      move and the parked drones' flight (which now goes straight to the asteroid) take the fastest way and journal
      `DriftStarted` on the leg that drifts, with the property `Leg` for where the drift ends.
    - **The navigation's fallback** (`NavigateSubCommand`): a leg planned in BURN that the fuel aboard no longer pays for
      flies in CRUISE before anything drifts.
  - Choices: as asked; nothing was offered.
  - Unchanged: the planners count CRUISE for reach, time and fuel (D45's far targets, the collection points, the trade
    arithmetic), so a trip out of reach is estimated as a drift all the way, which errs long.
  - Noticed (not changed):
    - **The trade arithmetic counts CRUISE fuel**; a burnt leg costs twice that: on a 300-unit leg about 216 credits more
      (fuel at 72 a market unit), against trips worth 30,000 to 107,000 on 2026-10-05. Accepted ("I accept the extra fuel
      costs this brings").
  - Tests: `FlightLegTests` (new: BURN into a market and into an asteroid only when what is left cruises on, no extra
    refuelling stop, B58 without one; the fastest mix, a straight drift when nothing is faster, a cruise leg into a fuel
    market burns; no empty tank where no fuel is sold, failing without the rule; the example of 2026-10-05),
    `NavigationFuelGuardTests` (BURN falls back to CRUISE), the executors' flights (each leg's mode; the far trips'
    first legs and their `DriftStarted`), the contract's flights (`FlyingShip` burns twice the fuel, and falls back as
    the navigation does) and docking to fill the tank before a burn. App 1189, Domain 75, API 210 (and 4 skipped),
    Integration 1.
  - To understand this, start with `TryPlanNextLeg`, `Burns`, `Strands` and `TryPlanMixedFlight` in
    `Trading/TradeRoutePlanner.cs`, then `Goals/Executors/GoalFlight.cs` and `Commands/Ships/CommandFlight.cs`.

- **6.20 FOOD and the waiting shuttle** (built on branch `claude/spacetraders-food-and-waiting-shuttle`, asked on
  2026-10-05, D85, D86). Asked: "I think one of the traders is idle, while there is profitable food to be sold."
  - Found:
    - **No trader took FOOD.** K94 sold it at 1,502 and A1 and J67 paid about 2,490, SCARCE: about 986 a unit, about
      75,000 for a full 80-unit hold after the price steps (D79), with 796,254 credits on hand. The four traders were on
      EQUIPMENT (3,864), COPPER_ORE (1,125), IRON_ORE (775) and PLASTICS (302): D82 put every route of a good something is
      made from before every end product's, whatever each earned, and with four traders one of those was always left.
      ASSAULT_RIFLES (E54 at 2,360 to J67 at 4,556) waited the same way.
    - **The idle ship was no trader.** SPECTER-2B, the light shuttle the mining plan bought at A2 at 10:57Z for B44's
      collection point (D83), kept the `Collect` role and waited without work for a drone to park there: the point's
      three drones drifted to B7 first (they had set off before slice 6.19), due there 13:50 to 13:54Z.
  - Done:
    - **Half weight** (D85, `TradeRoutePlanner.RankingProfit`): `Rank` and `CompareBestFirst` order routes by profit, an
      end product's counted at half, a good something is made from first on a tie. FOOD at ~75,000 ranks as ~37,500, before
      EQUIPMENT's 3,864.
    - **Trade until parked** (D86): the role board counts a designated shuttle as a collector only once one of its
      point's drones is parked at the asteroid (there and not in flight). Until then it is a cargo ship like any other,
      which the board gives the trade role; the change of collectors weighs the roles again at once, and the new role takes
      effect when its trip ends, as every role change does. The mining plan's rounds, purchases and second shuttle are
      unchanged: a round starts only for a collector, and a second shuttle only while the first is away selling ore.
  - Choices (2026-10-05): "Half weight (Recommended)" rather than profit only or keeping end products last; "Trade until
    parked (Recommended)" rather than waiting.
  - Unchanged: D14 (lucrative from `Trade.MinProfitPerUnit`), D79, D80, the role board's estimates (which weigh every
    route, not only the first), the mining plan's own target order (`MiningPlanner.CompareBestFirst`).
  - Tests: `TradeRoutePlannerTests` (FOOD before IRON at today's prices, failing under D82's order; MEDICINE, earning less
    than twice EQUIPMENT, still after it; DRUGS now between SHIP_PARTS and IRON), `RolePlanServiceTests` (a designated
    shuttle trades while its drone drifts and collects once it is parked, failing under the old rule).
  - To understand this, start with `RankingProfit` and `CompareBestFirst` in `Trading/TradeRoutePlanner.cs`, then
    `Collectors` in `Automation/RolePlanService.cs`.

- **6.34 A cargo ship every half hour, before the probes** (built on branch `ccr-caa38096-yjumz1` in projects and
  gembernodes, asked on 2026-10-07, D116; merged as projects#199, main `07cf58f3`, its dashboard's description and the
  deploy as gembernodes#91, merged 2026-10-07 11:02Z). Asked: "I would like to switch priorities between new trade ships
  and probes. So once every half hour, money permitting, a trade ship is bought, independent on whether probes still need
  to be bought."
  - Found (in the code; nothing was read from the bot, which this session can't reach):
    - The order's positions (`PurchaseTier`): 7 every explorer, 8 the probes of home and the trade reach, 9 the drones and
      cargo ships that take turns, 10 the far probes. Beyond `Trade.ShipPurchases` the trading plan said what it needed
      only at 9 (`BuyCargoShipAsync`), so no cargo ship was bought while a probe within the trade reach was still to buy:
      on 2026-10-06 the trade reach had about 66 markets (D108).
    - Beyond the list a cargo ship is a need only once a route worth `Trade.ShipPurchaseMinRouteProfit` has waited
      `Trade.ShipPurchaseWaitMinutes` for a ship with every trader busy (D88, `TradeShipDemand`).
    - The gate's miners' "once every half hour" (D92) counts from the last one bought, which the mining plan's state keeps;
      nothing kept when the trading plan last bought a ship.
  - Asked on 2026-10-07, all as recommended: "Keep D88's check", "Save up, probes wait", "Keep the turns too" (D116).
  - Done:
    - **The cargo ship on the clock** (D116; `BeyondTheListTierAsync` in `Automation/TradingAutomationService.cs`,
      `PurchaseTier.TimedCargoShip`): beyond the list, once D88's route has waited, the trading plan says it needs the
      largest hold (D112) at `TimedCargoShip` (8) when `Trade.ShipPurchaseIntervalMinutes` (new, 30; 0 or less means 30)
      have passed since it last bought a cargo ship, or none is on record, and at `Alternating` otherwise, as before. The
      probes are 9, the turns 10 and the far probes 11.
    - **When the plan last bought one** (`TradingAutomationPlanState.LastShipBoughtAt`, new): every cargo ship the trading
      plan buys, the list's too, sets it; the plan reads its state once a pass, before its purchase, and writes it when the
      routes, the goods not traded or this change.
    - gembernodes: the purchase order's description on the SpaceTraders dashboard gives the new positions.
  - Readings in the build (yours to confirm or change):
    - **"Once every half hour"** counts from the trading plan's last cargo ship, of the list or beyond it, as the gate's
      miners count from their last (D92), not by the clock's half hours; a shuttle the mining plan buys for a collection
      point (D83) doesn't count. With no purchase on record, as after the deploy, the next goes before the probes at once.
    - **Money permitting** is the credit reserve every purchase keeps (D51); the credits are saved up for the cargo ship
      as you chose, and a purchase that fails leaves the half hour where it was.
    - **Before the probes only**: every explorer (7, D113) and the jump gate's loads (6, D64) stay before it.
    - **`Trade.ShipPurchaseIntervalMinutes` at 0 or less means half an hour**, as `Mining.GateMinerIntervalMinutes` does;
      a large value puts the cargo ships back after the probes.
  - Expect, once deployed:
    - while a route has waited for a ship (D88), the Trading plan's row on the dashboard's purchase order at position 8
      (`TimedCargoShip`), the probes' at 9 waiting behind it, until the cargo ship is bought;
    - then probes for half an hour, the Trading plan's row at 10 (`Alternating`) while a route waits;
    - a cargo ship about every half hour while the credits keep up and routes keep waiting.
  - Checked on 2026-10-07 at 13:30Z: heavy freighters bought at 11:45, 12:15 and 12:45Z (`TimedCargoShip`, 8, for a minute
    or two each), probes in between; at 13:17Z a bulk freighter at X1-HB56-C18D took position 8 and waited for a probe to
    reach that shipyard (D30), the probes behind it.
  - Tests: `PurchaseOrderTests` (the cargo ship on the clock after every explorer and before the probes, the turns and the
    far probes; it doesn't wait for the drones' turn, which comes first between; the positions 0 to 11),
    `TradingAutomationServiceTests` (31 minutes after the plan's last purchase the largest hold goes before the probes and
    the state keeps the new purchase; at 29 minutes it takes its turn; the setting at 60, and unset; a purchase that fails
    doesn't start the half hour; the time stays in the state as the routes change; the list's ship starts the half hour;
    with none on record the largest hold goes before the probes; D88 still holds it back), `DefaultSettingsSeedTests` (the
    new setting), `PrometheusMetricsTests` (the probes at 9).
  - To understand this, start with `BeyondTheListTierAsync` and `BuyCargoShipAsync` in
    `Automation/TradingAutomationService.cs`, then `PurchaseTier` in `Services/PurchaseOrder.cs`.
  - Done when: while probes wait to be bought, a cargo ship is bought about every half hour that a route waits for one,
    and the probes in between.

- **6.33 The largest hold, explorers before probes, rings, and trade trips first** (built on branch `ccr-1ca8b8f2-r8kgby`
  in projects and gembernodes, asked on 2026-10-07, D112–D115; merged as projects#198, main `27050f8b`, its dashboard's
  descriptions and the deploy as gembernodes#90, merged 2026-10-07 05:24Z).
  Asked: "For spacetraders: - when buying a new trade ship (purchasing order 9) it should pick whatever the known ship with
  the highest cargo capacity is, as long as it is not scarce. - I'd like Explorers (order 10) to go in front of probes
  (order 8) - I'd like exploring done in concentric circles based on trade distance. So first the first 5 systems as is
  currently the case, then 6-10, then 11-15 etc.", and during the work: "I'd like trade ships to be prioritized in rate
  limiting. So if a trade ship docks/undocks/jumps/navigates/buys/sells it should not have to wait for a miner or a
  surveyor."
  - Found (in the code; nothing was read from the bot, which this session can't reach):
    - The order's numbers are the dashboard's positions (`PurchaseTier`): 8 the probes, 9 the drones and cargo ships
      that take turns, 10 the further explorers (`MoreExplorers`), 11 the far probes. Since D108 only their place told
      the first and the further explorers apart.
    - At 9, beyond `Trade.ShipPurchases`, the trading plan bought one more of the list's last type at the cheapest
      shipyard at home (`BuyCargoShipAsync`). The turn counted as cargo ships only the list's types and the shuttles and
      haulers (`PurchaseOrder.Turn`): a refining or bulk freighter bought there would have left the cargo ships' turn
      standing, and the drones waiting for good. `SHIP_BULK_FREIGHTER` wasn't in the domain's `ShipType`, so a purchase
      of one would have gone into the ledger as `None`.
    - The explorers took the systems within the trade reach first and the rest as one (D103, D106).
    - The rate limiter knew writes and reads (D19, `RateLimitingHandler`), every write alike. A flight's arrival, with
      the market refresh and the dock, runs from the scheduler's wake-up, apart from the goal's steps.
  - Asked on 2026-10-07, all as recommended: "Within the trade reach" and "Next largest" (D112), "Ring of their gate"
    (D114).
  - Done:
    - **The largest hold** (D112; `LargestHoldAsync`, `AnsweredAsync` and `AsBought` in
      `Automation/TradingAutomationService.cs`): beyond the list, the shipyards within the trade reach of home through
      the built gates (`ExploreAtlas.Reachable`), home and every system a probe of ours counts for
      (`ProbePlanner.Whereabouts`, from the goals the pass reads anyway) while the probe plan is on; their ships with a
      price and a hold that would be cargo ships (`FleetRoles.IsCargoShip` on the ship as bought) and aren't SCARCE
      there as cached; the largest hold, then the cheapest, then the nearest to home. The purchase goes through
      `TryPurchaseUnlessScarceAsync` (new, in `Services/ShipPurchaseService.cs`), which refuses a ship the shipyard,
      fetched again, lists SCARCE (`Scarce`). The list's ships are bought as before (`ListedAsync`).
      `PurchaseOrder.Turn` counts every ship but a drone, a probe, a surveyor or an explorer as a cargo ship;
      `ShipType.ShipBulkFreighter` (new).
    - **Every explorer before the probes** (D113; `PurchaseTier`, `BuyAsync` in `Exploring/ExplorePlanService.cs`):
      `MoreExplorers` is gone; every explorer is an `Explorer` need (7); `FarProbes` is 10.
    - **Rings** (D114; `Ring`, `Next` and `NextByWays` in `Exploring/ExploreAtlas.cs`): a system's ring is its jumps
      from home (`JumpsFromHome`) over the trade reach, rounded up; every exploring ship takes the nearest ring first,
      and within it the system nearest to it, by jumps, or by the seconds for an explorer that warps; a system no known
      gate leads to comes after every ring.
    - **Trade trips first** (D115; `ApiPriority` (new, `Ports/ApiPriority.cs`), `RateLimitingHandler`,
      `RequestBudget.TradeReserve` and `TradeRequestsWaiting`): a trade trip's steps (`ShipGoalExecutorService`) and its
      flights' departures and arrivals (`NavigateToWaypointHandler`, `NavigateToWaypointArrivedHandler`) mark their
      requests with an `AsyncLocal`, which flows into the handler. A marked request never gives way; another write gives
      way while one waits, for 10 seconds at most (`MaxWriteDelay`), and leaves the last 5 of the burst to them; a read
      gives way to them as to any write. Their waits are counted as `kind` `trade` in
      `spacetraders_api_rate_limit_wait_seconds_total`.
    - gembernodes: the purchase order's description on the SpaceTraders dashboard gives the new positions, and its rate
      limit graph shows the seconds waited per minute by kind.
  - Readings in the build (yours to confirm or change):
    - **"Known ship"** is a ship a shipyard lists with its hold. A shipyard lists its ships' details only while a ship
      of ours is there, and the probes park at the shipyards within the trade reach (D110), so it is mostly every
      shipyard there.
    - **Abroad, only where a probe answers**: a purchase at a shipyard with none of our ships calls for one (D30), which
      only a probe in that system answers; elsewhere it would wait for good and hold the drones' turn.
    - **A trade ship is a cargo ship**: a ship with a mining laser, a siphon or a surveyor would get those roles (D58,
      D38) whatever its hold, and an explorer explores, so neither is bought here.
    - **SCARCE, as cached, then fetched**: the choice goes by the cached supply; the purchase fetches the shipyard again
      and refuses a ship it lists SCARCE then, and the next pass takes the next largest. A shipyard is fetched when one
      of our ships arrives there and before a purchase, so a SCARCE in the cache can be old: it keeps the ship out until
      then.
    - **Order 10 to 7, not between 7 and 8**: every explorer takes the first's place, so the probes keep 8 and the cargo
      ships 9 as you know them; the far probes move from 11 to 10.
    - **A trade ship for the rate limit is a ship on a trade trip**, whatever its type (the command ship or a drone on
      one included); the refresh of the market it trades at goes first with its writes, as it decides the next batch; a
      jettison, or anything else outside the trip's steps and flights, isn't marked.
    - **Another write stops giving way after 10 seconds**, as a read does (D19), so a busy trading fleet can't starve
      the miners and the surveyors.
  - Expect, once deployed:
    - beyond the list, the next cargo ship is the largest hold the shipyards within the trade reach list (the markets
      dashboard's shipyards table shows each ship's cargo), bought where a probe of ours is;
    - while the explore plan wants another explorer, it comes before the probes (position 7 on the dashboard), and the
      command ship fetches it where no probe of ours is in the shipyard's system;
    - the explorers finish the systems 1 to 5 jumps from home, then go to those 6 to 10 away, before 11 to 15;
    - `spacetraders_api_rate_limit_wait_seconds_total{kind="trade"}` stays small next to `write`.
  - Tests: `TradingAutomationServiceTests` (beyond the list the largest hold within the trade reach where a probe of
    ours answers, not where none does; a SCARCE one left out for the next largest; a ship with a mining laser and an
    explorer left out; a shipyard beyond the reach left out), `ShipPurchaseServiceTests` (a cargo ship the shipyard
    lists SCARCE when fetched again isn't bought; one not SCARCE is; `SHIP_BULK_FREIGHTER` read), `PurchaseOrderTests`
    (after a heavy, bulk or refining freighter the drones' turn; an explorer doesn't take turns; a further explorer
    before the probes and the turns; the positions 0 to 10), `ExplorersTests` (a further explorer's need is an
    `Explorer` one), `ExploreAtlasTests` (the rings in turn, the nearest to the ship first in each; a ring's width),
    `WarpExplorersTests` (a system behind a gate under construction in the ring of its gate; one found by a scan after
    every ring), `RateLimitHandlerTests` (another write gives way to a trade trip's request, at most its limit; a trade
    trip's sale and market refresh give way to none; a read gives way to one; its wait counted as `trade`; the trade
    trips' reserve), `ShipGoalExecutorServiceTests` (a trade trip's step is marked, a mining trip's isn't),
    `TradeFlightPriorityTests` (a trade trip's refuel, orbit, flight, arrival refresh and dock are marked, with no mark
    around the handlers; a mining trip's aren't).
  - To understand this, start with `LargestHoldAsync` in `Automation/TradingAutomationService.cs`, then `PurchaseTier`
    and `Turn` in `Services/PurchaseOrder.cs`, then `Ring` in `Exploring/ExploreAtlas.cs`, then `ApiPriority` and
    `WaitForBudgetAsync` in `Infrastructure.SpaceTradersAPI/RateLimiting/RateLimitingHandler.cs`.
  - Done when: a cargo ship beyond the list is the largest hold within the trade reach that isn't SCARCE, the explorers
    wanted are bought before the probes, the explorers finish each ring before the next, and a trade ship's requests
    wait less than the others'.

- **6.32 Shipyards first** (built on branch `claude/spacetraders-shipyard-probes`, asked on 2026-10-06, D108–D111; merged as
  projects#197, main `4b4cbcad`, its dashboards' descriptions and the deploy as gembernodes#89, merged 2026-10-06 17:54Z).
  Asked after 6.31's deploy: "Can we set up the command ship to go to that location if there isn't a probe there, and also
  have probes deployed to shipyards with priority, with shipyards with explorer ships being even higher priority than
  that?", the location being "The location where an explorer ship is supposed to be bought if it's in the purchase order
  and enough credits are available".
  - Found (read-only, 2026-10-06 between 15:45Z and 16:10Z, image `60f3922a`):
    - The command ship bought the first explorer, SPECTER-5C, at X1-GT9-AE7B at 14:44Z, after setting off at 13:25Z, and
      was home at 15:05Z. The explore plan wanted 2 explorers for the 20 systems left.
    - The second (718,106 at X1-GT9-AE7B, `MoreExplorers`) waited for the order, not for a ship: credits 1,615,691, the
      credit reserve 811,653. Ahead of it came the probe for X1-BC61 (`Probes`, waiting for a probe at X1-BC61-EE6B), the
      rest of the probes within the trade reach (X1-BC61 8, X1-GT9 16, X1-TA92 15, X1-GY77 27 once explored) and the drones
      and cargo ships that take turns. A further explorer also waited until one of our ships was in X1-GT9; SPECTER-60,
      passing through on its way to another system, briefly was.
    - 40 shipyards were known, each at a waypoint with the SHIPYARD trait and a market. Two sell SHIP_EXPLORER: X1-GT9-AE7B
      (718,106, HIGH) and X1-GY77-A2 (865,362, MODERATE), which SPECTER-5C found at 15:54Z. No probe was at either; within
      the trade reach X1-GT9-AE7B, X1-BC61-EE6B, X1-TA92-A18X, X1-TA92-X20X and X1-GY77's three had none.
  - Asked on 2026-10-06, all as recommended: "Keep D102's order" (D108), "Across systems" (D109), "Stays parked" (D110),
    "Probe tier" (D111).
  - Done:
    - **The command ship fetches every explorer, unless a probe answers** (D108; `BuyAsync`, `ProbeAnswersAsync`, `Fetches`
      and `DecideAsync` in `Exploring/ExplorePlanService.cs`): a purchase that finds none of our ships at the shipyard is
      `WaitingForAShipThere` while a probe of ours counts for the shipyard's system, there or on its way there
      (`ProbePlanner.Whereabouts`, as the probe plan counts it: one passing through counts for where it goes), and the probe
      plan is on; otherwise `CommandShipFetchesIt`, for the first explorer and every further one. A further explorer reports
      its need to the order even with none of our ships in the shipyard's system. The fetch comes before the command ship's
      way home once explorers explore; once on its way it stays with the purchase while it waits for the credits, the order
      or a probe that came meanwhile.
    - **Which system gets the next probe** (D109, D111; `Wants` in `Automation/ProbeDeploymentPlanService.cs`): fewer
      probes than shipyards that sell SHIP_EXPLORER, wherever the gates reach it, at the probe tier; then, within the trade
      reach, fewer probes than shipyards, then than markets; then, beyond it, the same at the far-probe tier. Each step goes
      home first, then the nearest. For the probes bought and the spares lent.
    - **Where a probe parks** (D110; `Park`, `Parked` and `Nearest` in `Probes/ProbePlanner.cs`): after the calls (D30), a
      probe parks at each shipyard without one, those that sell explorers first, the nearest pair first. One at a shipyard
      stays; one parked at a shipyard that sells no explorer gives way to one that does. The others roam (D29), or, with a
      probe for every market, settle (B69), the shipyards first. A probe that comes into a system flies to a shipyard
      without one first (`Entry`).
    - A market is a shipyard by its waypoint's SHIPYARD trait, and one that sells SHIP_EXPLORER by the cached shipyard (its
      types or its ships).
    - Visibility: the probe plan's state gives each market's `Shipyard` and the next probe's `NextProbeFor`; the command
      ship's `PlanStarted` line says no probe of ours is there to answer the purchase's call.
  - Readings in the build (yours to confirm or change):
    - **"A probe there"** is a probe of ours in the shipyard's system or on its way there, not only one at the shipyard: it
      answers the purchase's call within minutes (D30), while the command ship is several jumps away.
    - **Once on its way the command ship stays with the purchase** while the order holds the explorer back, as it already
      did for the credits (D98): a drone's or a cargo ship's turn can come while it flies, and flying home and back would
      cost the trip twice. It waits at the shipyard, where the purchase needs it.
    - **A probe parked at a shipyard still answers a call at another** shipyard of its system (D30): the purchase that waits
      comes first, and the shipyard it leaves gets the next free probe.
    - **The counts decide which system is next**: a system with as many probes as shipyards counts as having them all, as
      its probes park at its shipyards before they roam.
    - **Home too**: home has a probe at each of its 28 markets now, so nothing changes there in this reset. After the next
      reset the starting probe parks at a home shipyard (X1-FJ91 had three), and home's markets are roamed only by the probes
      bought after the shipyards have theirs; the scout plan's first round and the ships at markets keep prices meanwhile.
  - Expect, once deployed: the next probe is for X1-GT9 (`NextProbeFor` `Explorer`) and parks at X1-GT9-AE7B; X1-GY77-A2's
    follows once X1-GY77 is explored, then the shipyards of X1-TA92 and X1-GY77, then the markets; X1-BC61's probe parks at
    X1-BC61-EE6B. The second explorer still waits for the order (D102); by its turn a probe is at X1-GT9-AE7B, and it is
    bought without the command ship.
  - Noticed (not changed): the command ship's flight to the shipyard is a `MoveToWaypointGoal`, which
    `AutomationSwitches.PlanFor` gives the survey plan (the survey ship's moves, D54), so with the survey plan switched off
    the flight would wait. Every plan is on.
  - Tests: `ExplorersTests` (a further explorer stays last, the command ship fetches it and comes home; a probe in the
    shipyard's system answers the first one's call while the command ship explores on; a probe on its way there answers,
    one passing through doesn't; with the probe plan off the command ship fetches; once on its way it stays while the order
    holds the explorer back; until the order lets a further one through, the command ship keeps its work),
    `ProbePlannerTests` (a probe at a shipyard stays parked while the others roam; a shipyard without one before a due
    market; an explorer shipyard before every other, however far; a parked probe gives way to one; settling spares take a
    shipyard first; a probe coming in takes an explorer shipyard, then the shipyard nearest the gate; `Whereabouts`),
    `ProbeDeploymentPlanServiceTests` (a probe parks while another roams; an explorer shipyard gets the next probe before a
    nearer system's markets; a shipyard before a nearer system's markets; beyond the trade reach the probe tier only for an
    explorer shipyard; a probe for X1-KR90 flies to its shipyard K2).
  - To understand this, start with `Plan`, `Park` and `Entry` in `Probes/ProbePlanner.cs`, then `Wants` and `BuyProbeAsync`
    in `Automation/ProbeDeploymentPlanService.cs`, then `BuyAsync`, `ProbeAnswersAsync` and `Fetches` in
    `Exploring/ExplorePlanService.cs`.
  - Done when: an explorer is bought without the command ship wherever a probe is in its shipyard's system, the command
    ship fetches one only where none is, and every shipyard within the trade reach has a probe, those that sell explorers
    first, before the other markets get theirs.

- **6.31 Warping** (built on branch `claude/spacetraders-warp`, asked on 2026-10-06, D100, D101, D104–D107; merged as
  projects#196, main `60f3922a`, its dashboard and the deploy as gembernodes#87, live since 2026-10-06 14:45Z): the explorer
  warps to the systems the gates don't reach, fuel-safe, and scans for more.
  - Research (2026-10-06, D100: "Research how the warp works exactly"). The sources: the API's docs, "Ship Navigation",
    read in a browser (a warp "behaves very similar to normal waypoint travel in that it takes time and consumes normal
    fuel"); the OpenAPI spec 2.3.0 in `SpaceTradersAPI/api-docs` (`POST my/ships/{ship}/warp` to a waypoint of another
    system, from orbit, with a warp drive installed, answered with the nav and the fuel; the docs' guide shows a
    `systemSymbol`, the spec and the client a `waypointSymbol`); the docs' error codes (4235 `warpInsideSystemError`, 4241
    `shipMissingWarpDriveError`, 4203 `navigateInsufficientFuelError`, none for a warp out of range); and the api-docs
    wiki's "Travel Fuel and Time", which the players compiled:
    - The distance is the straight one between the two systems' positions, rounded.
    - The fuel is a flight's: the distance in CRUISE and STEALTH, twice that in BURN, 1 in DRIFT; at least 1.
    - The seconds are round(round(distance) × multiplier / engine speed + 15). A warp's multiplier is 50 in CRUISE, twice
      a flight's 25; the wiki marks DRIFT's 300, BURN's 25 and STEALTH's 60 as not confirmed since API 2.1.
    - The drive's range: "Warp-drives have a maximum range given, although you are generally limited by the amount of
      fuel rather than the warp range."
    - To compare, a jump: no time, then a cooldown of 17 seconds plus 0.311 a unit of the systems' distance before the
      next jump (`TradeGates`, fitted on SPECTER-1's jumps), and one ANTIMATTER, 5,024 to 5,654 that day. A jump's cooldown
      holds back no warp.
    - Refuelling: a market unit of FUEL fills 100 of the tank, and every market seen sold FUEL (248 of 248, the gates' and
      the fuel stations' among them).
  - Found (read-only, 2026-10-06 between 13:30Z and 14:25Z, image `8c37dd7e`):
    - The explorer X1-GT9-AE7B sells: an Ion Drive II of speed 36, an 800-unit tank, a Warp Drive I of range 2,000, a
      Sensor Array II, a gas siphon and a 40-unit hold. So a CRUISE warp reaches 800 at most, 1.39 seconds a unit (800 in
      19 minutes), BURN 400 at half that; a DRIFT warp would cost 1 fuel at 8.3 seconds a unit.
    - The explore plan knew 41 systems: 19 left behind built gates, and 5 behind gates under construction, which only a
      warp reaches: X1-ZZ69 (531 from X1-GT9), X1-JU15 (540 from X1-AD37), X1-YG40 (441 from X1-TA92), X1-JX83 (459 from
      X1-AA31) and X1-XJ90 (955 from home, 1,497 or more from every other system known). Other agents had charted all
      five; each has 25 to 27 markets, fuel stations among them.
    - Only the 17 systems explored had a cached position.
    - The command ship set off from X1-QT24 to X1-GT9-AE7B for the first explorer at 13:25Z; at 14:23Z it was at X1-PX46.
  - Asked on 2026-10-06, after the research, all as recommended: "CRUISE/BURN only" (D104), "Scan when none left" (D105),
    "Reach first, then nearest" (D106) and "No, gates only" (D107).
  - Done:
    - **The note** (`Exploring/Warps.cs`): a warp's fuel and seconds as above; the drive and its range from the ship's
      cached modules (`Warps.Range`); BURN where the fuel pays for it, CRUISE otherwise, never a drift (D104).
    - **One planner for every way** (D101, `Exploring/SystemWays.cs` on `Exploring/WayChart.cs`): the fastest way from a
      ship to every system it can get to, Dijkstra over the systems and where the ship is in each, by the seconds: jumps
      through the usable gates (the flight to the gate, and the cooldown a jump waits out after the one before it), and,
      for a ship with a warp drive, warps from wherever it is, within the drive's range. Fuel-safe (D100): a warp lands
      where the ship can refuel (a market, or a FUEL_STATION, whose type shows even where it is uncharted), or, into a
      system with nowhere to refuel, keeps the fuel to warp back and goes no further. A ship fills its tank where it
      leaves a market, or flies to its system's nearest market first. A system the API refused a warp into gets none for
      an hour (`WarpRefusals`). The positions come from `cached_systems`; the waypoints from the cache.
    - **Every executor flies through it** (D101, `GoalJumps.TowardsAsync`): a ship with a warp drive takes the first step of
      the fastest way, a warp (`IGoalWarps`) or a jump, so it warps only where that is faster or the only way; a ship
      without one jumps as before.
    - **The warp** (`Goals/Executors/GoalWarps.cs`, `IWarpSubCommand`, `WarpGoal` and `WarpGoalExecutor`): it fills its tank
      first where a full tank warps where this one can't, or in BURN (in orbit it docks, docked it refuels and orbits); then
      the flight mode, the warp, the nav and fuel cached and the arrival scheduled for the goal (B17). A BURN warp the API
      refuses for its fuel goes in CRUISE; any other refusal (`WarpRefusedException`) blocks the goal, and the plan
      chooses again.
    - **The measurement** (D100, "then measure"): every warp is journalled (`Warped`) with the fuel and the seconds it took
      against those the note reckons, and a warp that differs logs a warning, for the note to take the API's numbers.
    - **The explorer warps** (`ExploreAtlas.NextByWays`, `DecideExplorerAsync` in `Exploring/ExplorePlanService.cs`): the
      systems within the trade reach of home through the gates first (D103), then the nearest by the seconds its way
      takes, by jumps or warps (D106). Before a warp goes to a system only a warp reaches, the plan fetches its waypoints
      (`LookForWarpsAsync`, `ExploreAtlas.WarpLooks`: one system a pass, after the gates' looks), and the explorer waits for
      that. It lands at the market nearest the system's gate, and explores and charts there as anywhere (D99).
    - **Scanning** (D105, `ScanAsync`; `POST my/ships/{ship}/scan/systems`): with nothing left within its ways, an
      explorer with a sensor array scans from where it is, once a system, after its cooldown, and the plan keeps it
      meanwhile. Every system found is cached with its position; those within its warps (800) join the plan's systems.
      Journalled `SystemsScanned`.
    - **And back**: with nothing left in a system the gates don't reach from home, the explorer warps to the nearest one
      they do (`ExploreStepKind.Rejoin`, status `Returning`), and is released there to trade (D102).
    - **D107**: a system only a warp reaches doesn't count towards the explorers wanted.
    - A bought ship is cached with its mounts, modules and engine at once, as startup sync caches them
      (`PurchaseShipActionResult`, `ShipPurchaseService`): a second explorer warps, scans and has its speed before the next
      restart.
    - Visibility: the fleet table says "warping to X1-ZZ69"; gembernodes: the systems dashboard's exploring journal shows
      the `Warped` and `SystemsScanned` lines.
  - Readings in the build (yours to confirm or change):
    - **The drive's range is a cap**: the API names no error for a warp beyond it; with CRUISE/BURN only (D104) the tank
      (800) limits the explorer first anyway.
    - **Every market refuels**: a warp may land at any market, or at a FUEL_STATION, as a place to refuel.
    - **"Nearest" is by the seconds** for an explorer with a warp drive, within the trade reach too (D103 counted jumps).
    - **The scan's reach**: the systems within the explorer's warps (800) join the plan; the others are only cached with
      their position, and join when a scan from nearer finds them.
    - **The trading plan's estimates count jumps**, for every ship: the explorer's trades warp where that is faster, so a
      trip can be quicker than its estimate.
    - **Measured, not held back**: the first warp measures the note, without a stop for your check: it lands where the
      ship can refuel, so a wrong note costs time, not a ship, and says so in a warning.
  - Expect, once deployed: the restart caches the explorer's modules and engine (startup sync). The plan fetches the five
    systems behind gates under construction, one a pass. The explorer explores what is left within the trade reach first
    (X1-QA35, X1-QR21, X1-VY81, X1-BC61, X1-GY77), then the nearest by the seconds, which should soon be a warp: X1-ZZ69
    from X1-GT9 (531 in CRUISE, 753 seconds), X1-JU15 from X1-AD37, X1-YG40 from X1-TA92, X1-JX83 from X1-AA31. Its first
    `Warped` line is the measurement: 531 fuel and 753 seconds for X1-ZZ69 from X1-GT9. X1-XJ90 stays out of reach (955,
    against an 800-unit tank). No chart reward is likely in those five: others charted them.
  - Noticed (yours to call): a scan finds systems only within its sensor's range, which isn't documented; the first
    `SystemsScanned` line will show how far it reaches. A `RepeatingError` for 429s was raised at 13:32Z and cleared at
    13:43Z, after 6.30's deploy (the bug session's).
  - Tests: `WarpsTests` (new: the fuel and seconds of the note, the range from the cached modules, BURN, CRUISE and never a
    drift), `SystemWaysTests` (new: a system behind an unbuilt gate by a jump and a warp; none beyond the tank; a jump where
    it is faster, a warp where it is; into a system with nowhere to refuel only with the fuel to warp back, and no further;
    a refused system; a ship without a drive; a warp from the nearest market; the landing), `WarpExplorersTests` (new: the
    waypoints fetched before the warp, once even where there are none; D107; D106 both ways; the scan, once, and a warp to what it found; a scan that finds
    nothing in reach; a scan after the cooldown; back where the gates reach and released; a refused warp for an hour),
    `WarpGoalExecutorTests` (new: the warp and its journal line; a mismatch warns; dock, refuel, a flight to a market first;
    a tank the refuel didn't fill asks for no warp; BURN refused goes in CRUISE; a refusal blocks; the fuel to warp back;
    the destination's system), `WarpFlightTests` (new: a move to another system warps where the way does, jumps where it
    does, and a ship without a drive jumps), `WarpRequestTests` (new: the request, a refusal and the rate limiter's 429,
    the scan, a bought ship's modules), `ShipPurchaseServiceTests` (a bought ship's mounts, modules and engine),
    `PrometheusMetricsTests` (the fleet table's "warping to").
  - To understand this, start with `Exploring/Warps.cs` and `Exploring/SystemWays.cs`, then `NextByWays` and `WarpLooks` in
    `Exploring/ExploreAtlas.cs` and `DecideExplorerAsync` and `ScanAsync` in `Exploring/ExplorePlanService.cs`, then
    `GoalWarps.WarpAsync`.
  - Done when: the explorer warps to such systems and back without being stranded, the planner picks a warp only where
    it is the faster way or the only one, and the explorer charts what it finds.

- **6.30 The explorers, and charting** (built on branch `claude/spacetraders-explorer-charting`, asked on 2026-10-06, D98,
  D99, D102, D103; merged as projects#195 with B73's fix, main `8c37dd7e`, its systems dashboard and the deploy as
  gembernodes#86, live since 2026-10-06 13:19Z): the explorers are bought and explore, and the uncharted markets and
  shipyards are charted.
  - Found (read-only, 2026-10-06 about 12:05Z, image `7555d61`):
    - The explore plan knew 35 systems and had explored 14; of the 21 left, 16 lay behind built gates and 5 behind gates
      under construction (X1-XJ90, X1-JU15, X1-ZZ69, X1-YG40, X1-JX83). By D102 that is 2 explorers.
    - X1-GT9-AE7B, an orbital station with a market, 4 jumps from home, is the one shipyard seen that sells SHIP_EXPLORER:
      702,315 at 05:55Z, supply HIGH. It sells nothing else. An explorer has an 800-unit tank, a 40-unit hold, a warp
      drive, a sensor array and a gas siphon: by what it carries the plans would take it for a siphon drone.
    - Every waypoint cached in the 15 systems seen is charted, by other agents. The API's chart call answers with the
      waypoint, its traits shown, and the agent's credits; the reward isn't given apart.
    - Credits 1.91M, the credit reserve 632,325.
  - Found too (2026-10-06 12:42Z, while it was built): each exploring ship took the system nearest to it (D59), so the
    command ship had explored a chain 16 jumps deep, one system a jump, and was on its way to X1-MN30, 16 jumps out, while
    X1-QA35 and X1-QR21 (1 jump from home), X1-VY81 (2), X1-BC61 (3) and X1-GY77 (4) waited: D103. 17 systems were left.
  - Done:
    - **How many** (D102, `CountAsync` in `Exploring/ExplorePlanService.cs`): one explorer for every
      `Explore.SystemsPerExplorer` (new, 10) systems left, or part of that, at most `Explore.MaxExplorers` (new, 5; 0 for no
      cap); `Explore.SystemsPerExplorer` 0 buys none. The systems left (`ExploreAtlas.SystemsLeft`, new) are those the plan
      knows, hasn't explored and reaches from home through usable gates: one behind a gate under construction, or one
      refused within the hour, counts once a ship can jump there.
    - **The purchase** (D98, D102, `BuyAsync`): the explore plan is a buying plan now, at the cheapest shipyard the gates
      reach that sells SHIP_EXPLORER, within the credit reserve (`IShipPurchaseService`). The first at the new
      `PurchaseTier.Explorer` (7), after the gate's loads and before the probes; every further one at the new
      `PurchaseTier.MoreExplorers` (10), after the drones and cargo ships that take turns and before the far probes (11),
      and only while one of our ships is at the shipyard or a probe of ours is in its system, which answers the
      purchase's call (D30): a need nothing could meet would hold back the far probes for good. The state says where the
      next purchase stands (`ExplorerPurchaseStatus`).
    - **The fetch** (D98, D30, `FetchAsync`): when the purchase waits only for a ship at the shipyard
      (`CommandShipFetchesIt`), the command ship flies there once its current step ends (`FetchingExplorer`), through the
      gates: a `MoveToWaypointGoal` now flies across systems (`MoveToWaypointGoalExecutor`, `GoalJumps`, D101). There it
      waits while the credits are saved up, and the purchase is made. It goes only while a way there through usable
      gates is known (the jumps refused lately left out), and explores on meanwhile: a flight that found no way would end
      at once, on every pass.
    - **Who explores, and where first** (D103): the explorers, or the command ship while there are none. Once there is one,
      the command ship finishes its step, jumps home and is released (`HomeAsync`, `ExploreAtlas.HomeFrom`, D60). Each
      exploring ship takes a system no other has taken (`Taken`), within the trade reach of home (`Trade.MaxHaulDistance`,
      5) before any beyond it, the nearest to it first in each (`ExploreAtlas.Next`). The state lists each explorer with
      what it does (`ExploringShip`).
    - **B73** (`DecideAsync`): a free command ship away from home, trading there since 6.29, is no longer taken home
      with nothing left to explore: only a ship that explored is brought home (D60).
    - **In between, trade** (D102, `DecideExplorerAsync`): an explorer with no system left is released where it is
      (`nothing_to_explore`, journalled once as `PlanCompleted`) and trades from there through the trading plan, across the
      systems in reach (D96). `FleetRoles.IsExplorer` (new): trading is an explorer's only role
      (`FleetRoles.PotentialRoles`), and it is never a siphon drone, a builder or a cargo ship of `Trade.ShipPurchases`
      (`IsSiphoner`, `CanConstruct`, `IsCargoShip`), though it carries a gas siphon; with the role board off it trades
      across systems as a cargo ship does. The plan takes it back once its trip ends and a system turns up.
    - **Charting** (D99, `Exploring/Charting.cs`, `ChartAsync` in `Goals/Executors/ExploreSystemGoalExecutor.cs`): the
      scouting stops include every uncharted waypoint (trait `UNCHARTED`) of a type that can hold a market or shipyard,
      every type but ASTEROID and GAS_GIANT, an uncharted gate first, then the nearest. At such a stop the ship charts it
      (`POST my/ships/{ship}/chart`; `ChartActionResult` now holds the waypoint and the agent's credits), the cache keeps
      the waypoint the chart shows, and a market or shipyard on it is stored as at any stop. The reward, the credits after
      the chart less those cached before it, is booked as `ChartReward` (`WaypointChartedEvent`, `LedgerEntryHandler`) and
      journalled (`Charted`). A chart that fails fetches the waypoint instead, and the ship moves on.
    - **Visibility**: `spacetraders_explore_systems_left` and `spacetraders_explore_explorers_wanted` (new), from the
      plan's state; a chart's reward in `spacetraders_credits_earned_total{source="ChartReward"}` and the ships' ledger
      metric; the purchase order's positions moved (probes 8, the turns 9, the far probes 11); the fleet table says
      "flying to X1-GT9-AE7B" for the command ship on its way to the shipyard.
    - gembernodes: the systems dashboard shows the systems left, the explorers wanted and bought, the chart rewards and
      the `Charted` lines; the purchase order table sorts its positions as numbers (10 and 11 sorted before 2), and its
      description lists the new tiers.
  - Readings in the build (yours to confirm or change):
    - **The command ship finishes what it is doing first**: a system it has just jumped into is scouted before it flies to
      the shipyard, and once on its way it stays with the purchase while the credits are saved up for it; if something
      earlier in the order comes first meanwhile, it explores on until the explorer may be bought again.
    - **The chart's reward is measured from the credits**: another ship's trade that lands between the chart and the
      credits read before it would count in the reward.
    - **An explorer counts towards the credit reserve as a trader** (D51): with the role board on, trading is its only role,
      so its 40 units count while it explores too, as the command ship's do.
  - Expect, once deployed: 17 systems left, so 2 explorers wanted. The first explorer's need at position 7, and, while
    nothing earlier waits, the command ship turning back to X1-GT9-AE7B from the chain, about 12 jumps (some 60,000 in
    antimatter, an hour or two of cooldowns); the probes abroad wait for that purchase (D98). Then `ShipPurchased` for the
    explorer, the command ship home and released, and the explorer jumping towards the systems within the trade reach
    first (X1-GY77, X1-BC61, X1-VY81, X1-QA35, X1-QR21). The second once a probe of ours is in X1-GT9, after the drones
    and cargo ships that take turns. No chart until a system with uncharted waypoints is reached: every waypoint of the
    16 systems explored so far was charted by other agents.
  - Noticed (yours to call): D97's SCARCE rule is for probes, so an explorer is bought at any supply.
  - Tests: `ExplorersTests` (new: one explorer for every 10 systems left, rounded up, at most the cap, and none at 0; the
    first before the probes, fetched by the command ship, which explores on while no way to the shipyard is known; bought there, the command ship home and released, the explorer
    on its way; short of credits the command ship explores on, but once on its way it waits at the shipyard; a further
    one only while a probe of ours is in the shipyard's system; each explorer its own system; a command ship that trades abroad isn't taken home (B73, which failed before); an explorer with nothing
    left trades and is taken back after its trip; the trade reach first; the stops chart the gate first, and no asteroid
    or gas giant), `ExploreAtlasTests` (the systems left; a taken system left to its ship; the way home; the trade reach
    first), `ExplorerRolesTests` (new: an explorer only trades, bought or synced, which failed for a bought one as a cargo
    ship; a siphon drone still siphons), `ExploreSystemGoalExecutorTests` (a chart, its reward and the market it shows;
    a failed chart fetches the waypoint; no chart for a charted stop or an asteroid), `FetchFlightTests` (new: a move to
    another system through the gates, no way, a refused jump), `PurchaseOrderTests` (the two tiers),
    `LedgerEntryHandlerTests` (`ChartReward`), `DefaultSettingsSeedTests` (the two settings), `PrometheusMetricsTests`
    (the two gauges, the probes at 8, the command ship's flight to the shipyard on the fleet table).
  - To understand this, start with `EnsureBootstrappedAsync`, `BuyAsync` and `DecideExplorerAsync` in
    `Exploring/ExplorePlanService.cs`, then `Next` and `SystemsLeft` in `Exploring/ExploreAtlas.cs`, `Exploring/Charting.cs`
    with `ChartAsync` in `Goals/Executors/ExploreSystemGoalExecutor.cs`, and `FleetRoles.IsExplorer`.
  - Done when: the explorers wanted are bought and explore, each its own system, and trade when none is left; the command
    ship works at home; and the uncharted markets and shipyards are charted as they are found, with their rewards in the
    ledger.

- **6.29 Trade across systems** (built on branch `claude/spacetraders-trade-across-systems`, asked on 2026-10-06, D95,
  D96; merged as projects#194, main `7555d61`, its routes table's jumps column and the deploy as gembernodes#85, live
  since 2026-10-06 12:01Z): the trading plan's routes reach the systems around.
  - Found (read-only, 2026-10-06 between 10:55Z and 11:45Z, image `edb83c8`):
    - Prices at most 30 minutes old abroad: X1-NF46 4 of its 16 markets, X1-HN44 1 of 8, X1-FH63 1, and X1-HU81's 20,
      where the command ship was exploring; home all 28. The probes abroad (6.28) had just begun: SPECTER-43, the first,
      reached X1-NF46 at 11:21Z.
    - A jump's cooldown grows with the distance between the two systems (`cached_systems`): over SPECTER-1's twelve
      jumps of the day, 400 to 2,221 apart, each next flight came 17 seconds plus 0.311 a unit after the jump, within six
      seconds (X1-AA31 to X1-PX46, 1,891 apart: 603 seconds). The explore plan waits out the cooldown before that flight.
      SPECTER-43 jumped on from X1-HN44 274 seconds after jumping in, where SPECTER-1 had waited 283 for the same jump:
      a cooldown may differ by ship, by a few seconds. A flight doesn't wait for a cooldown; only the next jump does.
    - Antimatter 5,024 to 5,560 a jump.
  - Done:
    - **The map of the systems in reach** (`ITradeContextReader.ReadReachAsync`, new): the systems the built gates reach
      within twice `Trade.MaxHaulDistance` (5) jumps of a trader's system, as the explore plan knows them with the jumps
      refused lately (`IGateNetwork`), their cached waypoints and markets in one `TradeMarketMap`, which measures distances
      within a system only. With them the ways between the systems (`TradeGates`, new): the fewest jumps
      (`ExploreAtlas.TryFindJumps`, D101), no more than the reach; each jump's antimatter, the price at the gate it leaves
      (unknown: the average of those seen); its cooldown, estimated from the systems' distance as above; and the credit
      floor every jump leaves (`FleetExpansion.MinCreditReserve`, D63). A market whose prices are older than
      `Trade.MaxPriceAgeMinutes` (new, 30), or that was never seen with prices, is stale, at home too
      (`TradeMarketMap.StaleMarkets`): it chooses no route, but its fuel still counts.
    - **Routes across systems** (`TradeRoutePlanner`): a flight to another system flies to its system's gate as every
      flight does (D84's planner within each system), jumps, and flies on from the last gate, filling the tank where that
      gate's market sells fuel (`TryPlanFlight`). A route buys within the reach of the ship's system and sells within
      the reach of the buy market's. Its profit is after its antimatter (`TradeRoute.AntimatterCost`, `Jumps`), and a trip
      that jumps keeps the credit floor besides, so it buys fewer units where the credits are short and can always jump
      on with them. Its time counts a cooldown only where it holds the ship: at a gate between two jumps, or when the
      haul's jump comes before the approach's cooldown is over (`TripTime`; the ship's own cooldown left too). The rate
      chooses (D95): in the tests, EQUIPMENT one jump away at 6,000 against A1's 3,499 earns about 860,000 an hour against
      107,000; at 3,700 it still earns more a trip, but less an hour, and goes after A1. The flights planned on a map are
      kept with it, as a pass weighs the same haul for every good and trader.
    - **Who crosses systems** (`TradingAutomationService.CrossesSystems`): a ship whose role is trading (the trade role on
      the board; with the board off, a cargo ship), not a shuttle kept for a collection point. Every other trader takes
      routes in its own system (`TradeMarketMap.WithoutJumps`). A trader stays where its last sale leaves it, and its next
      route is ranked across the reach of that system. Cargo a trader holds is sold where it is, or jettisoned (D42).
      Cargo ships are still bought at home; a route abroad counts as one that waits for a new one (D88).
    - **The trade executor** flies through the gates (`GoalJumps`, as the probes do): to the gate, the jump once the
      cooldown and the floor allow it, on from the gate it jumped to. At the buy market of a trip that sells abroad, its
      batches keep back the haul's fuel, antimatter and the floor (`TradeRoutePlanner.KeptBackFor`). A trip with nothing
      aboard that can't jump on is dropped (`TradeDropped`: `no_way`, `jump_refused`, `not_possible`); one with its cargo
      aboard keeps it and waits (below). A sale moves only within the system it sells in.
    - **The role board** values a ship abroad, or on a trade trip that sells abroad, for trading only
      (`RoleSettings.Available`, `RoleSettings.BusinessSystems`); a new role takes effect where the trip ends. Its trade
      estimates come from the systems in reach, a drone's from its own system.
    - **Visibility:** `TradeStarted` gives a route's `Jumps` and `AntimatterCost`; `TripEnded` books a trip after its
      antimatter (`AntimatterCost`), and so do the trip profit metric and the trade earnings that cap the board's estimates
      (D87); `Jumped` gives the jump's `CooldownSeconds`, to hold the estimate against; `GET /status/trading-routes` gives
      each route's `buySystemSymbol`, `sellSystemSymbol` and `jumps`. "Goods not traded" counts a good as traded, or
      waiting, wherever its route buys it.
    - gembernodes: the markets dashboard's routes table gets a **jumps** column, and its profit column says it is after
      antimatter too.
  - Readings in the build (yours to confirm or change):
    - **Who trades abroad**: only ships whose role is trading. D58 has drones gather first, D65 a builder trade only while
      the gate has no load for it, D86 a collection shuttle collect once a drone is parked, and the mining plan plans trips
      in the system a drone is in: a drone or shuttle that traded abroad would leave its own work undone, or start it
      abroad. The survey ship's spare-time trades (D34) stay in its system for the same reason.
    - **Cargo aboard that can't jump on waits.** Selling it in the system it is in, often the market it was bought at, or
      jettisoning it (D42), would give its value away, while a refused gate comes back after an hour. A wait longer than
      30 minutes shows as `ShipStuck`.
    - **A trip that jumps keeps the credit floor** (D63 read for trade trips): cargo may use the credit reserve (D17), but
      a ship that spent the floor on cargo could not jump on with it.
    - **Fresh prices choose routes**: a trip under way finishes on the newest prices it has, however old.
  - Expect, once deployed: routes abroad as the probes reach the markets abroad, the trade reach's first (6.28's order),
    as only a market seen within 30 minutes counts; until then mostly X1-NF46's and the markets the command ship passes
    through. A trader that takes one jumps with `Jumped`, and its `TripEnded` line names the antimatter.
  - Unchanged: one buyer at a time (D80), across systems too; D14's minimum a unit, now after antimatter; D57's credits
    held back; mining, siphoning, surveys, contracts, construction and the other plans' purchases stay home (D60, D68).
  - Noticed (not changed): a trip with its cargo that waits at a refused gate holds its ship for the hour; a trip's
    reroute at its sell market (`TradeRerouted`) looks only within that system.
  - Tests: `TradeAcrossSystemsTests` (new: distances within a system only; the flight to another system through the
    gate; a route abroad first only when it earns more an hour; stale prices; the reach counted from the ship, then from
    the buy market; the credit floor kept; a second jump waiting out the first's cooldown, a jump waiting for the ship's
    own; a held cargo sale after antimatter; the gates' ways, cooldowns and antimatter), `TradingAutomationServiceTests`
    (a trader takes a route abroad; a ship with the mining role, and a shuttle kept for a collection point, stay in their
    system; a trader abroad takes its next route from there), `TradeBetweenMarketsGoalExecutorTests` (to the gate first, the
    jump, no way with nothing aboard dropped and with cargo kept, too few credits to jump, the batches keeping back the
    antimatter and the floor), `TradeContextReaderTests` (the systems within twice the reach, stale markets, the gates),
    `TripBookTests` (a trip booked after its antimatter), `AbroadRoleTests` and `RolePlanServiceTests` (abroad, trade
    only), `ApiIntegrationTests` (the routes' systems and jumps), `DefaultSettingsSeedTests` (`Trade.MaxPriceAgeMinutes`).
  - To understand this, start with `Trading/TradeGates.cs` and `TryPlanFlight` in `Trading/TradeRoutePlanner.cs`, then
    `ReadReachAsync` in `Trading/TradeContextReader.cs`, `CrossesSystems` in `Automation/TradingAutomationService.cs`,
    and `AbroadAsync` in `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`.
  - Done when: traders take routes abroad whenever those earn more an hour, and finish them: no ship stranded, no jump
    refused, each trip's profit after antimatter in the ledger.

- **6.28 Probes at the markets abroad** (built on branch `claude/spacetraders-probes-abroad`, asked on 2026-10-06, D97,
  D101, with D96's reach; merged as projects#192, main `806e9f1`, its dashboard as gembernodes#83; B72 fixed before the
  deploy): every market within the trade reach gets a probe, then every other explored market as the credits allow.
  - Found (read-only, 2026-10-06 about 09:20Z, image `6a0bcf2`):
    - Home had 28 probes for its 28 markets, and the probe plan reported `EveryMarketHasOne`: it counted home's markets
      only. The explored systems had 124 markets more, none watched: within 5 jumps X1-HN44 8, X1-NF46 16, X1-AD37 4,
      X1-GT9 16 and X1-TA92 16 (60); beyond, X1-AA31 26, X1-PX46 6, X1-JQ80 10, X1-DR50 3 and X1-FC19 19 (64, more as
      exploring goes on).
    - Shipyards selling SHIP_PROBE, none SCARCE: home's A2 and C46 29,885; X1-HN44 25,737; X1-NF46 26,830 and 33,794;
      X1-TA92 23,443 and 25,307; X1-AA31 21,385 and 31,012; X1-JQ80 24,736. Antimatter about 5,000 a jump.
    - Credits 2,332,178, the credit reserve 406,959.
    - `BusinessSystems` counted every system where a ship that doesn't explore is: a probe abroad would have made its
      system one where the mining, siphon, trading and contract plans buy ships.
    - Nothing in the API's spec says a cooldown holds a navigation up, and the drones fly off to sell while their
      extraction cooldown runs; only a jump waits for one.
  - Done:
    - **The jump, every flight's** (D101, `Goals/Executors/GoalJumps.cs`, new): the jump goal's steps moved out of
      `JumpGoalExecutor` into a helper every flight between systems uses. The way is the fewest jumps through the built
      gates the explore plan knows (`ExploreAtlas.TryFindJumps`, new, read through `IGateNetwork`); in each system a leg
      to its gate as every flight flies (`GoalFlight`, D84); at the gate the cooldown waited out, the credit floor kept
      (D63), the tank filled where the gate sells fuel, orbit, jump, booked (`ShipJumpedEvent`) and journalled
      (`Jumped`). A jump the API refuses is recorded (`JumpRefusals`, new, in memory), and no way goes through that gate
      for an hour. `JumpGoalExecutor` keeps its goal's handling. `DeployProbeGoalExecutor` flies a probe to a market of
      another system this way: no way known any more, or a jump short of the floor, ends the goal for the plan to choose
      again; a refused jump blocks it (`jump_refused`). A probe has no tank, so its flights cost only the antimatter.
    - **The probe plan in every system** (`ProbeDeploymentPlanService`): home first, then each explored system the
      built gates reach from home, the nearest first (`ExploreAtlas.Reachable`, new), then any other system a probe is
      in. A probe counts for the system it is in or flies to, holding its market there (B15). Each system's free probes
      fly as before (`ProbePlanner`); a probe sent to another system flies to the market a roaming probe would pick from
      that system's gate (`ProbePlanner.Entry`, new).
    - **Spares first** (B69): a system with more probes than markets lends those it would settle at no market of its own
      (`ProbePlanner.Surplus`, new) to the first system short of one they can get to, before a probe is bought for it.
    - **Where a probe is bought** (D97): for the first system short of one, at the shipyard where it costs least with
      the antimatter of the jumps from there counted, as last seen at each gate, never at SCARCE supply. A shipyard
      sells only where one of our ships is (D30): at home, and abroad once a probe of ours is in its system. As merged,
      a system's next probes were bought where one could be bought while its first was still on its way (B72, fixed
      before the deploy): now one in a system a probe is on its way to waits for it, and one in a system with no probe
      there or coming waits for that system's first probe, bought at the cheapest shipyard that can sell it. A probe
      bought for another system flies there at once. The purchase refuses a probe the shipyard, fetched again just
      before, lists at SCARCE (`ShipPurchaseFailure.Scarce`, new): the cache is a purchase behind.
    - **The order** (D97): the probes of home and the trade reach (`Trade.MaxHaulDistance` jumps of home, 5) at
      `PurchaseTier.Probes`; the other systems' at `PurchaseTier.FarProbes` (new, 9), after the drones and cargo ships
      that take turns.
    - **Business stays home** (D60): `BusinessSystems.Of` is the headquarters' system alone; the mining, siphon and
      trading plans read the agent for it.
    - **Visibility:** the plan's state per system (`ProbeDeploymentPlanState.Systems`: jumps, trade reach, probes,
      markets; the next probe's system, shipyard, price and antimatter; `ShipyardsScarce`); an Information line for each
      probe sent to another system and `Jumped` for each jump; the fleet view's "flying to X1-…" for a probe on its way
      abroad; `spacetraders_system_probes` (new) for the systems dashboard. `ShipLeftIdle` counts a system's due markets
      as work for that system's probes. The descriptions of `Trade.MaxHaulDistance` and the probe plan's switch, for the
      next agent.
    - gembernodes (branch `claude/spacetraders-probes-abroad` there too): the systems dashboard's table gets **probes**
      and **oldest prices** (the age of the oldest market prices in each system, from
      `spacetraders_market_observed_timestamp_seconds`), a stat of the probes abroad, and the main dashboard's purchase
      order description `FarProbes`.
  - Choices made in the build (yours to change): a probe bought for another system flies straight there, whatever the
    system it was bought in lacks; `Trade.MaxHaulDistance` 0 or less means 5, as the market views read it; a system no
    way reaches now, or whose jumps the credits don't allow, gets no probe until that changes; a probe stays where it is
    when its way closes, and its system keeps it working.
  - How D101 came out: the ways between systems are found on their own, the fewest jumps through built gates, and D84's
    planner flies the legs within each system. A jump burns no fuel, so there is nothing for D84's fuel search to weigh
    between gates; 6.31's warps, which do, are where the two searches meet. The executors whose goals leave the system
    jump through `GoalJumps`: the probes' and the explore plan's so far; 6.29's trade executor is next.
  - Expect, once deployed (the gate is built in this reset; with B72's fix): a probe a pass at most, as the order and the
    reserve allow. X1-NF46's shipyard (26,830) sells probes for least for X1-HN44 and X1-AD37 too, so X1-NF46's first
    probe is bought at home (29,885 and two jumps) and the others wait for it; from there it goes outwards, each system's
    probes bought at its own shipyard once its first has arrived, or at the nearest that sells for less: about 60 probes
    and 1.6M credits for the trade reach, then the 64 beyond it after the drones and cargo ships. Each jump journals `Jumped`; X1-AD37 and X1-GT9 have no shipyard
    that sells probes, so theirs come from the nearest that does. The market watch fetches one market a tick, so with
    about 90 markets watched each is fetched every 7 or 8 minutes rather than 5, inside D96's 30.
  - Unchanged: the probes' flights within a system (B69's settling included); the explore plan's own jumps and its
    `JumpRefusedAt`; what a jump costs (one ANTIMATTER at the gate's market).
  - Noticed (not changed): a jump's cooldown isn't counted in a probe's choice of shipyard, only the antimatter (D97's
    words); routes with more jumps wait out more cooldowns.
  - Tests: `ExploreAtlasTests` (the fewest jumps through built gates, reach only through explored systems, a refused
    gate left alone for an hour), `ProbePlannerTests` (the spares, the market a probe coming in takes),
    `ProbeDeploymentPlanServiceTests` (a probe bought at home for the nearest system abroad and sent there, the rest
    bought where the first arrived, a system beyond the reach last, none at SCARCE at home either, a spare sent before a
    purchase, none sent where the jump would break the floor, a probe on its way abroad counted there),
    `DeployProbeGoalExecutorTests` (to the gate first, the jump and on, the cooldown between jumps, no way, short of the
    floor, a refused jump), `JumpGoalExecutorTests` (the refusal recorded for every way), `ShipPurchaseServiceTests` (a
    probe at SCARCE when fetched again isn't bought; a drone is), `PurchaseOrderTests` (`FarProbes` after the turns),
    `MiningAutomationServiceTests`, `TradingAutomationServiceTests` and `ContractPlanServiceTests` (no purchase where only
    a probe is; these three and the executor's first failed before the change), `ShipRuleTests` (a probe abroad is no
    probe for a due market at home), `PrometheusMetricsTests` and `MetricsEndpointTests` (the fleet view's "flying to",
    `spacetraders_system_probes`). App 1,298, Domain 75, API 216 (4 skipped), Infrastructure 87, Integration 1.
  - Warnings left in the files touched: QW0028 on `ProbeDeploymentPlanState.PlanId` ("use a strongly typed
    identifier"), from before, as on every plan state.
  - Done when: every market within the trade reach has a probe, or one on its way, as far as the credits allow; no probe
    is bought where SHIP_PROBE is SCARCE; no other plan buys or works abroad.
  - Files, in `SpaceTraders.Application` unless named: new `Goals/Executors/GoalJumps.cs`, `Exploring/GateNetwork.cs`,
    `Exploring/JumpRefusals.cs`; changed `Automation/ProbeDeploymentPlanService.cs`, `ProbeDeploymentPlanState.cs`,
    `BusinessSystems.cs`, `MiningAutomationService.cs`, `SiphonAutomationService.cs`, `TradingAutomationService.cs`,
    `ContractPlanService.cs`; `Exploring/ExploreAtlas.cs`, `SystemOpportunities.cs`; `Goals/Executors/JumpGoalExecutor.cs`,
    `DeployProbeGoalExecutor.cs`; `Probes/ProbePlanner.cs`; `Services/PurchaseOrder.cs`, `ShipPurchaseService.cs`,
    `IShipPurchaseService.cs`; `Trading/TradeContextReader.cs`; `Health/ShipLeftIdleRule.cs`; `DependencyInjection.cs`;
    API `PrometheusMetricsService.cs`, `PrometheusMarketMetricsService.cs`, `PrometheusAutomationMetrics.cs`; Persistence
    `DefaultSettingsSeed.cs`; docs `HOW_IT_WORKS.md`, `GLOSSARY.md`.
  - To understand this, start with `Goals/Executors/GoalJumps.cs` and `TryFindJumps` in `Exploring/ExploreAtlas.cs`,
    then `ServeAsync`, `LendSpares` and `BuyProbeAsync` in `Automation/ProbeDeploymentPlanService.cs`.

- **6.27 Profit per hour** (built on branch `claude/spacetraders-profit-per-hour`, asked on 2026-10-06, D95; merged as
  projects#190, its routes table as gembernodes#81, deployed by gembernodes#82, image `6a0bcf2`, live since 2026-10-06
  09:05Z): the trading plan ranks routes by what they earn an hour.
  - Done:
    - `Trading/TripTime.cs` (new): the timing the trading plan and the role board share, so they agree. A flight in CRUISE,
      leg by leg through its refuelling stops, as the API reckons it (15 seconds plus the distance times 25 over the
      engine's speed, 9 for an engine not cached), and 10 seconds at each landing (dock, trade or refuel, the market's
      refresh), a ship already at its buy market stopping there too. The batches aren't timed: the ledger can't tell them
      apart (a trade's rows share their second), and a flight takes minutes.
    - `TradeRoute.Seconds` (the whole trip from where the ship is: the flight to the buy market and the haul) and
      `TradeRoute.CreditsPerHour`, worked out with the route (`TradeRoutePlanner.TryEvaluate`, `Rank`, `Judge`).
    - `TradeRoutePlanner.RankingProfit` became `RankingRate`: the rate, an end product's at half (D85). `Rank` and
      `CompareBestFirst` keep their order around it: the routes that feed the gate's materials first (D89, D90), the routes
      to an exchange last (D91), then the rate, then a good something is made from, then the key. The trading plan hands
      out its routes trader by trader, the best first, so of two traders the nearer one now gets a route both could fly.
    - The role board's trade options take the route's own seconds (`RoleEstimator.TradeOptions`); its `FlightSeconds`,
      `StopSeconds` and `DefaultEngineSpeed` are `TripTime`'s.
    - The trip keeps its time (`TradeBetweenMarketsGoal.ExpectedSeconds`, whole seconds), and so do the trading plan's
      state (`TradingAutomationOpportunityState.ExpectedSeconds`) and `GET /status/trading-routes`, which gives
      `expectedMinutes` and `creditsPerHour` for each route. `TradeStarted` gives `CreditsPerHour` and `TripMinutes`
      ("… an hour over about … minutes"), and a lucrative good's "Goods not traded" reason its rate and minutes ("…; 60,130
      an hour, the trip taking about 9 minutes (D95).").
  - gembernodes (branch `claude/spacetraders-profit-per-hour` there too): the routes table under the market tree gets
    **minutes** and **per hour**, "—" for a route without a time, and its description the plan's order as it is now (it
    still said full holds and the most profit after fuel). It goes in with the image bump, once this is merged.
  - Unchanged: D14's 5 a unit, which sizes a trip (D79); one buyer at a time (D80); the credits a trip holds (D57); a
    cargo ship beyond the list still waits for a route worth `Trade.ShipPurchaseMinRouteProfit` in credits (D88); the
    role board's cap on trade estimates (D87).
  - Noticed (not changed): a leg the executor burns (D84) takes half the time counted, so a short trip whose legs burn
    ends sooner than its rate assumed. The role board's times were already counted this way.
  - Watch after the deploy: shorter trips make more calls an hour; `ApiThrottled` should stay quiet (1.12 requests a
    second of 2 on 2026-10-06).
  - Done when: the trading plan's list orders the lucrative routes by credits an hour within D89 and D91, and a short
    route that earns more an hour goes before a long one that earns more a trip.
  - Tests: `TradeRoutePlannerTests` (a route's time, at the stand-in speed and at 36; a short route that earns more an hour
    before a long one that earns more a trip, and without the chains EQUIPMENT for A1 before MEDICINE, both failing
    before the change; the time of the flight to the buy market; the rate within D89's and D91's order; an end product's
    rate at half), `TradeRouteJudgementTests` (the reason's rate), `TradingAutomationServiceTests` (the trip's and the
    state's time, the journal's rate and minutes; the D15 trip's scenario made the best per hour too: D41 pays 3,700),
    `ApiIntegrationTests` (`expectedMinutes`, `creditsPerHour`). App 1,261, Domain 75, API 216 (4 skipped), Infrastructure
    87, Integration 1.
  - Warnings left in the files touched: QW0028 and QW0029 ("use a strongly typed identifier") on the plan states' and
    goals' `Guid` ids (`MarketAutomationPlanState.cs`, `ShipGoal.cs`), from before; typed ids would reach every plan and
    goal.
  - To understand this, start with `Trading/TripTime.cs`, then `RankingRate` and `TryEvaluateFrom` in
    `Trading/TradeRoutePlanner.cs`, and `TradeOptions` in `Roles/RoleEstimator.cs`.

- **6.26 Every ship builds the gate** (built on branch `claude/spacetraders-every-ship-builds`, asked on 2026-10-06, D93).
  Asked: "Let's remove the one gate ship limit, but have a "underway" counter of items so there aren't 3 ships gunning for
  the final 40 FAB MATS."
  - Found (read-only, 2026-10-06): the gate X1-FJ91-I64 was complete at 03:17Z. From 21:05Z the one builder, SPECTER-D,
    started each trip within seconds of ending the last (23 minutes via F58, 30 via D52 and D49), every load a full 80 but
    one: FAB_MATS reached the gate at about 135 an hour, against about 100 while the loads were partial (17:15–21:05Z).
  - Done:
    - `RoleSettings.DefaultConstructionShips` 0, and `Construction.Ships` 0 in the seed: 0 means every ship that can build;
      a value above 0 caps them, the largest holds first. `RolePlanner.Builders` and `ConstructionPlanner.PickBuilders`
      take them all at 0. The setting follows its default, so the deploy lifts the limit.
    - The underway count was there already: `ConstructionPlanner.Needs` counts what every construction trip carries or
      goes to buy (`OnTheWay`), `MaterialNeed.Remaining` leaves it out, and the pass adds each trip it starts
      (`Pass.Started`) before it weighs the next builder; D80 keeps a second buyer of a material away from a market
      another trip is on its way to.
  - Tests: `RolePlannerConstructionTests` (without a limit every ship that can build builds; without a setting, no limit),
    `ConstructionPlannerTests` (`PickBuilders` at 0), `ConstructionPlanServiceTests` (three free builders and the last 40
    FAB_MATS: one takes them, and the others may trade).
  - Noticed (not changed): before 21:05Z the FAB_MATS markets' output set the pace (partial loads at LIMITED), and may
    again with more builders. Each load is judged as a ship purchase (D64), so more builders hold back more credits at once.
  - To understand this, start with `Builders` in `Roles/RolePlanner.cs`, then `Needs` and `Loads` in
    `Construction/ConstructionPlanner.cs`.

- **6.25 The jump gate's miners** (built on branch `ccr-ca2bf7e9-hweozt` in projects and gembernodes, asked on 2026-10-05,
  D92). Asked: "For spacetraders, I'd like, as part of the jump gate build phase, extra miners to be bought for the ores that
  supply the build gate materials once every half hour (and those miners being dedicated to those ores) until each of the
  smelters have at least HIGH saturation." It is D91's phase 2: "if that's not enough to push it up, we add more miners".
  - Found (reading the code; the cluster's data wasn't read from this session):
    - While the gate needs materials the construction plan reports its next load on every pass, also while every market
      that sells it is SCARCE or LIMITED (D66), and everything after it in the order waits (D64): the probes and D28's
      drones. Before it come only the drones for scarce minerals (D48) and the collection points' shuttles (D83), bought for a
      SCARCE or LIMITED ore alone: no rule bought a drone for H60's IRON_ORE at MODERATE.
    - D91 sends the drones' ore to the smelters first; nothing kept a drone on a smelter's ore.
  - Choices (2026-10-05): "Smelters only (Recommended)"; "One per ore"; "Same tier as gate loads, capped. If the gate can be
    built, it should be built, otherwise extra miners can be built."; "Until the gate is done (Recommended)".
  - Done:
    - `TradeMarketMap.InputsOf` and `GoesIntoConstruction`: a material the gate still needs, and every good it is made
      from through the production chains.
    - `MiningPlanner.GateSmelters` (a market that imports an ore and exports a metal, made from ores alone, that goes into a
      material the gate still needs), `GateOresShort` (the ores of the smelters below HIGH that a drone from the shipyard can
      serve, the lowest supply first), `GateTargets` (a gate miner's pairs, sharing allowed), `HasEnough` (HIGH or ABUNDANT;
      an unknown supply counts as enough) and the `GateSmelter` record.
    - The mining plan, after the drones for scarce minerals and before D28's: a drone for the first ore short whose last gate
      miner was bought `Mining.GateMinerIntervalMinutes` (new, 30) ago or longer, at `PurchaseTier.Construction`, within
      `Mining.MaxDrones` and the credit reserve; noted in its state (`MiningAutomationPlanState.GateMiners`: ship, ore, when
      bought; a ship that left the fleet is dropped). A free gate miner sells what it holds first, then mines its ore for the
      smelter of it with the lowest supply (reason `gate`, `MiningStarted`); it doesn't park at a collection point while its
      ore goes into a material the gate still needs, and follows the usual rules while it has no smelter to serve (ABUNDANT,
      or out of reach) and once the gate needs nothing made from its ore. In the count of drones for scarce minerals (D48) a
      gate miner and its ore's areas are left out.
    - The order ships are bought in: `PurchaseNeed.WaitsForMarkets`, set by the construction plan when its next load waits
      for SCARCE or LIMITED markets (D66) or another buyer there (D80). At the gate's place, its load comes before another
      plan's need unless it waits so, and so does the construction plan while it hasn't said what it needs (after a start the
      mining plan runs first). The gate's miners never hold the load back; the probes and D28's drones wait for both.
  - Unchanged: the contract takes every free miner, a gate miner too, and no drone is bought while it mines (D23); a gate
    miner's trip keeps the other ores it can sell within one tank and sells them on the trips after (D71); QUARTZ_SAND and
    SILICON_CRYSTALS, which the FAB_MATS, ELECTRONICS and MICROPROCESSORS factories take directly, buy no gate miner.
  - Noticed (not changed):
    - `Mining.MaxDrones` (20) counts every ship that can mine, the command ship included: once it is reached no gate miner is
      bought, whatever the smelters' supply. While the smelters stay below HIGH, a drone per ore is bought every half hour
      until then.
    - A smelter out of a drone's CRUISE reach that sells fuel counts, as D45's far markets do: its drone drifts there first.
  - Tests: `GateSmelterTests` (the chains; smelters, not factories; only what the gate still needs; none without the chains;
    the ores short by supply until HIGH; a smelter no drone can serve; a gate miner's targets, sharing, and none at
    ABUNDANT), `GateMinerTests` (a drone at the gate's place for the ore shortest; half an hour per ore; one per ore; none at
    HIGH, none while the gate needs nothing, none at the cap; the drones for scarce minerals first; a gate miner counts for
    its own ore only and parks nowhere, both failing without the change; its trip before a scarce mineral nobody mines;
    sharing; held cargo first; ordinary once the gate needs nothing from its ore; forgotten once gone; kept in the plan's
    state's JSON, and none in a state stored before them), `PurchaseOrderTests`
    (the load first unless it waits for its markets; the miners never hold it back; an unheard construction plan holds them,
    failing without the change), `ConstructionPlanServiceTests` (the need says when it waits for its markets),
    `DefaultSettingsSeedTests` (the new setting).
  - To understand this, start with `GateSmelters` in `Mining/MiningPlanner.cs`, then `DroneNeedAsync` and `GiveTripAsync` in
    `Automation/MiningAutomationService.cs`, and `IsGateLoadBeforeMiners` in `Services/PurchaseOrder.cs`.

- **6.24 Ore to the markets that make something from it** (built on branch `claude/spacetraders-ore-to-makers`, asked on
  2026-10-05, D91). Asked: "I am still wondering whether just throwing more miners on iron ore would increase the iron
  production, therefor speeding up everything even further"; then "Yes, EXCHANGE nodes should be lowest priority and only
  considered as wealth trades, never as supply trades." and "Let's do this in 2 phases, first redirect the ore to a place
  that actually generates iron, if that's not enough to push it up, we add more miners."
  - Found (read-only, 2026-10-05, the last 24 hours):
    - H60 was never RESTRICTED for IRON, and reached STRONG, the top production level, at least as often with IRON_ORE
      LIMITED (47% of the time) as MODERATE (29%). Its production followed demand: WEAK while its IRON stock was HIGH or
      ABUNDANT, up to STRONG while traders drew it down. IRON_ORE never went above MODERATE there, so what more ore does
      is untested.
    - Our traders bought about 100 IRON an hour at H60, 300 at most. D52, GROWING after 80 IRON at 16:24Z, was SCARCE
      again 17 minutes later. With D89 and D90 sending H60's IRON to the factories, H60's output sets the pace of FAB_MATS.
    - Of the IRON_ORE the drones sold, 998 went to H60 and 742 to D52, which makes nothing from it. B7's and H62's exchanges
      took 368 SILICON_CRYSTALS, 197 QUARTZ_SAND, 116 COPPER_ORE, 91 ALUMINUM_ORE and 797 ICE_WATER, which only exchanges
      buy; C46's exchange took 395 LIQUID_HYDROGEN and 96 LIQUID_NITROGEN, which G59, F57 and D51 make FUEL, PLASTICS,
      FERTILIZERS, EXPLOSIVES and AMMUNITION from.
  - Done:
    - `TradeMarketMap.MakesSomethingFrom` (the market imports the good and exports something made from it; without the
      production chains, any import) and `TradeMarketMap.Exchanges`.
    - Mining and siphoning: `MiningTarget.FeedsProduction` and `SiphonTarget.FeedsProduction`. Such targets come first
      (`CompareBestFirst`, `SiphonTargets`, `SharedTargets`). The others never count as uncovered (D48), as scarce
      (`ScarceOres`, `ScarceGases`) or as openings (`LowSupplyOpportunities`), so they buy no drone and wait for no ship.
      A drone with no free pair whose market makes something from its good shares one (D77) before it takes one of
      them, reason `wealth`.
    - Held cargo: `TradeRoutePlanner.TryFindBestSale` and `TryFindBestCargoSale` with `supplyFirst`: a sale that pays, at a
      market that makes something from the good, then at one that imports it, then at an exchange; the drones and the
      siphoners sell their leftovers so.
    - Trading: `TradeRoute.ToExchange`. Such a route never feeds production and ranks after every other (`Rank`,
      `CompareBestFirst`).
  - Unchanged:
    - The spare-time trips and the trading plan's held cargo sell where each good fetches most (D36), so the trading plan
      still foresees where a sale leaves a ship.
    - The collection point's shuttle sells everything at its market (D83): B44's by-products (silicon, quartz, aluminum
      and copper ore, ice) still go to B7's exchange.
    - The trading plan's end products (D85): a good something is made from counts wherever it is sold, an exchange apart.
  - Tests: `MiningPlannerTests` (H51, which makes IRON, before F49, which makes nothing from IRON_ORE, and XB5C, which
    exchanges it, though they are shorter and pay more; no scarce ore, opening or first share for them; the rule without
    chains), `MiningAutomationServiceTests` (a drone shares H51's pair; with no maker it mines for the exchange, reason
    `wealth`; no drone is bought for them; held IRON_ORE goes to H51), `SiphonPlannerTests` and
    `SiphonAutomationServiceTests` (the same for HYDROCARBON), `TradeRoutePlannerTests` (a route to an exchange ranks last;
    the supply-first sale). The fixtures' exchanges (B7's COPPER_ORE, C39's gases) are imports now, so the tests of D28,
    D45, D48, D53 and D77 keep their subject.
  - Phase 2 (more miners) follows if, a few hours after the deploy, H60's IRON_ORE doesn't reach HIGH or its IRON output
    (IRON bought an hour, how fast its price falls back) doesn't rise.
  - To understand this, start with `MakesSomethingFrom` in `Trading/TradeMarketMap.cs`, then `CompareBestFirst` and
    `MiningTargets` in `Mining/MiningPlanner.cs`, and `GiveTripAsync` in `Automation/MiningAutomationService.cs`.

- **6.23 Feed the gate at cost** (built on branch `claude/spacetraders-feed-at-cost`, asked on 2026-10-05, D90). Asked:
  "Should trade routes that feed the jump gate's factories run even when they earn less than the 5-a-unit minimum?";
  chosen: "Up to its fuel (Recommended)".
  - Found (read-only, 2026-10-05 15:30 to 16:35Z):
    - H60 sold IRON for 105 to 157 within an hour, each purchase raising the price; D52 paid 151 to 155 for it and F58
      150. A trip earned D14's 5 a unit to D52 only while H60 charged at most 146 to 150, and to F58 at most 145.
    - Once D89 was live (16:12Z), SPECTER-C took IRON from H60 to D52 twice, at 133 (16:19Z) and 144 (16:22Z). D52's IRON
      went from SCARCE to LIMITED and its FAB_MATS from RESTRICTED to GROWING. F58's IRON stayed SCARCE and its FAB_MATS
      RESTRICTED. The two purchases raised H60's price to 157, above what either pays.
  - Done:
    - `TradeRoute.IsWorthIt`: a route that feeds a material the gate still needs is worth taking while its goods sell for
      at least what they cost (its profit plus its fuel at least 0); any other route when it is lucrative (D14). `Rank`,
      `Judge` and the trade executor use it.
    - The planner buys such a route's units while each sells for at least what it costs (`Earns`, `UnitsWorthBuying`), and
      weighs such a route when its sell market pays exactly what the buy market charges; other routes still need a price
      gap.
    - At the buy market such a trip buys on those terms, and is dropped only when its goods would sell for less than they
      cost (`TradeDropped` then gives a minimum of 0). At the sell market it sells unless that would fetch less than the
      cargo cost and another market pays more (`TradeRerouted`, once per trip, as before).
    - "Goods not traded" reads "feeds the jump gate's FAB_MATS, 40 units for -145 after fuel (D89, D90)" for such a route
      rather than "lucrative".
  - Unchanged: D14 for every other route; D80 (one trip at a time buys a good at a market, so one trader at a time carries
    H60's IRON); D85's order among the routes that feed the gate (D52 before F58 while it pays more); D88 (only a route
    worth 10,000 asks for another cargo ship); the role board, which weighs only trips that earn something.
  - Noticed (not changed): such trips count in `TradeEarnings` (D87) at their loss of a few hundred credits, which lowers
    the cap on the role board's trade estimates a little.
  - Tests: `TradeRoutePlannerTests` (IRON for a FAB_MATS maker at cost, and at 4 a unit, runs while the gate needs FAB_MATS;
    not without the need, nor at a loss), `TradeRouteJudgementTests` (the wording; no judgement at a loss),
    `TradeBetweenMarketsGoalExecutorTests` (a trip at cost buys only when it feeds the gate; one at a loss is dropped with a
    minimum of 0; it sells at the factory under the minimum, and takes the cargo elsewhere when it would sell below cost),
    `TradingAutomationServiceTests` (the trading plan starts a trip at cost to the gate's factory before MEDICINE at 386 a
    unit).
  - To understand this, start with `IsWorthIt` and `Earns` in `Trading/TradeRoutePlanner.cs`, then `Pays` in
    `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`.

- **6.22 Feed the gate first** (built on branch `claude/spacetraders-feed-the-gate`, asked on 2026-10-05, D89). Asked:
  "Could the construction of the gate go faster?", then "Please make sure the trade routes prioritize the feeding to the
  portal construction materials, yes."
  - Found (read-only analysis, 2026-10-05 15:00 to 15:50Z):
    - The gate had FAB_MATS 340 of 1,600 and ADVANCED_CIRCUITRY 220 of 400. After the markets' overnight stock ran out
      (FAB_MATS 11:08, ADVANCED_CIRCUITRY 12:05), FAB_MATS came at about 8.5 an hour: finished between 10-09 04:00Z and
      10-11 20:00Z, against the reset at 10-11 13:00Z.
    - Each 20-unit batch turned a market from MODERATE to LIMITED (D66 then stops the load); a market took 83 to 211
      minutes back to MODERATE. Production, not the builder, its hold or the credits (2.23M), set the pace.
    - Both FAB_MATS markets were RESTRICTED: in all 10 activity changes seen, an export was RESTRICTED exactly while an
      input was SCARCE. IRON was SCARCE at F58 74% of the run and at D52 54%; of the 1,920 IRON the traders carried, F58
      got none and D52 220 (prices at the six IRON buyers within 0 to 5 a unit). While GROWING, the markets made 4 to 8
      times as much. ADVANCED_CIRCUITRY at D49 left RESTRICTED for about 2 hours after each 40 ELECTRONICS sold there.
    - Asked how much buying at LIMITED or SCARCE would push the price: each batch below MODERATE costs about 9 to 12%
      more than the one before; down to SCARCE gives 120 to 160 FAB_MATS once (8 to 11 hours) for about 1M more on the
      rest. D66 stays (2026-10-05: "Keep MODERATE").
  - Done:
    - `TradeMarketMap.ConstructionMaterials` (the materials the gate still needs, read by `TradeContextReader` from the
      construction cache while the construction plan is on) and `ConstructionMaterialMadeFrom`: the material a market makes
      from a good delivered there, when it exports the material, imports the good below ABUNDANT, and the production chains
      make the one from the other.
    - `TradeRoute.ConstructionMaterial`; `Rank` and `CompareBestFirst` put the routes that feed the gate first, then D85's
      order. D80 still lets one trip at a time buy a good at a market, so one trader at a time carries H60's IRON.
    - `TradeStarted` for such a route says "which makes the jump gate's FAB_MATS from it (D89)".
  - Choices (2026-10-05): the trade routes, rather than the builder hauling IRON while it waits.
  - Unchanged: D66 (the construction plan buys at MODERATE or better), D14 (a route must still earn the minimum), the
    mining plan (QUARTZ_SAND for D52 and F58 stays a mining target as before).
  - Noticed (not changed): the construction plan keeps only its current waiting reason and doesn't log it; its credit
    check is costed for a full 80-unit hold even when D66 stops the load after 20.
  - Tests: `TradeRoutePlannerTests` (IRON for a FAB_MATS maker before SHIP_PARTS that earn more than ten times as much,
    while the gate needs FAB_MATS; not without the need, nor with the IRON ABUNDANT), `TradeContextReaderTests` (the needed
    materials of the system's gate, none with the construction plan off), `TradingAutomationServiceTests` (the route is
    taken first, and the journal says so).
  - To understand this, start with `ConstructionMaterialMadeFrom` in `Trading/TradeMarketMap.cs`, then `ReadAsync` in
    `Trading/TradeContextReader.cs` and `Rank` in `Trading/TradeRoutePlanner.cs`.

- **6.21 Honest trade estimates, and cargo ships only while routes wait** (built on branch
  `claude/spacetraders-b67-trade-estimates`, asked on 2026-10-05, B67, D87, D88). Asked: "yes, investigate the role board
  estimates as a bug, and can you add a limitation on buying more trade ships unless a trade ship actually adds value? If
  the market is stable, we have too many trade ships right now."
  - Found (B67, PLAN's bug table has the evidence):
    - The role board's trade options counted routes other ships' trips held and goods they were on their way to buy at a
      market (D80), and gave each route its own job: at 13:30:39 SPECTER-1 and SPECTER-2B were both credited with MEDICINE
      bought at D48, which SPECTER-2A took.
    - Under D82 the board counted end products the trading plan never gave: from 10:27 to 13:30 SPECTER-1's nine
      switches to Trade all cited MEDICINE D48 to A1.
    - And a trip's rate isn't repeatable: just after D85 went live, CLOTHING at K94 went from 3,108 to 3,620 to 4,146 in
      five minutes as three traders bought it, and such gaps take hours to come back. The board compared one trip's
      profit over its flights, about a million an hour, with mining's steady 12,000 to 20,000; SPECTER-1's trading made
      about 83,000 an hour. It changed role 120 times in 12 hours.
    - Purchases: beyond the list, D43 bought another cargo ship whenever every trader was busy and a new ship would have
      a route earning 5 a unit, nearly always; the purchase order had a light hauler for 358,193 next.
  - Done:
    - **B67**: `RoleEstimator.TradeOptions` leaves out the routes other ships' trips hold and the goods they are on
      their way to buy at a market (the trading plan's `HeldBuys`), from the role board's trips under way; a ship's own
      trip holds nothing from it. A trade job is the good at its buy market (`TradeJobKey`), and `Options` keeps each
      job's best trip, so duplicates don't crowd out other jobs among the 20.
    - **D87**: the trip book notes every trade trip that ends (`TradeEarnings`, in memory); the role board caps each trade
      option at what the trade trips that ended in the last two hours made per hour of their time (a trip of no time
      counts a minute), and says so in its job: "at most what trading earned lately (D87)".
    - **D88**: beyond `Trade.ShipPurchases` the trading plan notes the routes worth `Trade.ShipPurchaseMinRouteProfit`
      (10,000) that a new ship would have from the shipyard and no trader holds (`TradeShipDemand`), and buys only once
      one has waited `Trade.ShipPurchaseWaitMinutes` (30) with every trader busy. Both are new settings; the list's own
      ships are bought as before.
  - Choices (2026-10-05): "Cap at realized (Recommended)" rather than the bug fixes only; "Routes keep waiting
    (Recommended)" rather than a payback rule or no ships beyond the list.
  - Unchanged: the mining and siphon estimates (the analysis of 2026-10-05 found them 2 to 6 times what drones make, from
    side trips and fuel; not asked about), the head start (D41), the trading plan's own routes.
  - Tests: `RoleEstimatorTests` (what other trips hold is left out, failing before the fix; the cap), `TradeEarningsTests`,
    `TradeShipDemandTests`, `TripBookTests` (a trade trip is noted, a mining trip isn't), `TradingAutomationServiceTests`
    (beyond the list a ship is bought only once a route worth the minimum has waited; none while no route is worth it).
  - To understand this, start with `TradeOptions` in `Roles/RoleEstimator.cs` and `Trading/TradeEarnings.cs`, then
    `BuyCargoShipAsync` in `Automation/TradingAutomationService.cs` and `Trading/TradeShipDemand.cs`.

## Changes in gembernodes

Changes to files are made on a branch there, and Flux deploys them once merged. The rest needs
your PC, 1Password or kubectl:

| Slice | Change |
|---|---|
| 2.4 | `infrastructure/monitoring/dashboards/spacetraders-dashboard.json` plus a `configMapGenerator` entry (merged: PR #10) |
| 2.5 | Rules in `infrastructure/monitoring/grafana-alerting-provisioning.yaml`, then a Grafana rollout restart (merged: PR #10; Grafana restarted 2026-10-02) |
| 2.7 | The fleet table's new columns, and panels for the holds and for what was mined (merged: PR #15) |
| 2.8 | A markets dashboard per system, uid `spacetraders-markets` (merged: PR #17) |
| 6.4 | A survey section on the SpaceTraders dashboard: surveys taken, the share that ended unused, the share of extractions with a survey, usable surveys per asteroid, and the survey journal (merged: PR #21) |
| 4.1 | Database login and read-only login (Postgres and 1Password): by hand, with the steps in `apps/spacetraders/README.md` (done; the revoke on `stored_credentials`, step 3, done on 2026-10-02) |
| 4.2 | `apps/spacetraders/`, `namespaces/spacetraders-namespace.yaml`, `ingress/spacetraders-ingress.yaml`, plus the kustomization entries (merged: PR #11) |
| 4.3 | "SpaceTraders bot is down" unpaused (merged: PR #11), then the Grafana rollout restart (done 2026-10-02 09:09Z) |
| 4.3 | B44: the bot's error lines get a rule of their own, by log level, instead of the shared rule's word match (merged: PR #13); then a Grafana rollout restart (done 2026-10-02 09:44Z) |
| 2.9 | The settings table on the SpaceTraders dashboard (merged: PR #31) |
| 6.9 | A "Roles" table under the SpaceTraders dashboard's fleet table: each ship's role, why, and what mining, siphoning and trading would earn it per hour (merged: PR #32). It shows data while the role board is switched on |
| 2.10 | An "API request rates" graph on the SpaceTraders dashboard (initiated, executed, completed, rate limited), and table legends on the graphs of both SpaceTraders dashboards (merged: PR #50) |
| 6.11 | A systems dashboard, uid `spacetraders-systems` (`dashboards/spacetraders-systems-dashboard.json` plus its `configMapGenerator` entry), and links to it from the two other SpaceTraders dashboards (merged: PR #51). It shows the systems beyond home once the explore plan has jumped |
| 2.11 | The markets dashboard's shipyards table: "can do", fuel, cargo and equipment for each ship for sale, and the table at full width (merged: PR #52, which deployed the build) |
| 2.12 | The Settings table's "next run" column, "value" and "next run" 180 px wide, and its description (merged: PR #55, which deployed the build) |
| 2.13 | A "Reset" picker on the SpaceTraders, markets and systems dashboards, and every query filtered on it, the logs' too (merged: PR #56; PR #57 deployed the build) |
| 2.14 | A "name" column in the SpaceTraders dashboard's Fleet and Roles tables, and the ship's name in brackets in front of a journal line about it, in the journal, the survey journal and the exploring journal (merged: PR #60, which deployed the build) |
| 2.15 | The Infinity data source (Grafana's background preinstall, pinned to 3.11.1), the SpaceTraders API data source with the bot's API key (`spacetraders-secrets.yaml`, the 1Password item copied into the monitoring namespace), a snapshots dashboard, uid `spacetraders-snapshots`, and links to it from the three other SpaceTraders dashboards (merged: PR #62, which deployed the build) |
| 6.13 | The markets dashboard's market tree: the trade volume where each good is cheapest and where it sells best ("buy volume", "sell volume"), and a description that says when such a pair is traded (merged: PR #61); D74 in that description (merged: PR #63, which deployed the build) |
| 6.6 | "Jump gate progress", "Jump gate: materials still needed" and "Jump gate materials" on the SpaceTraders dashboard, and the Roles, Purchase order, Spent per hour and Profit per hour descriptions brought up to date (merged: PR #53). They show data while the home gate is under construction, as X1-FJ91's is |
| 2.16 | A "Profit by ship" table under the SpaceTraders dashboard's Roles table: each ship's profit, its purchase, market buys, market sales, fuel and other, and the fleet's totals; the panels below moved down by its height (merged: PR #64, which deployed the build) |
| 2.17 | "Trade routes, in the order traders take them" under the market tree on the markets dashboard, from `/status/trading-routes` through the SpaceTraders API data source (merged: PR #65, which deployed the build) |
| 2.18 | The trade routes table under the market tree: after the routes, a row for each other good with a price gap, "not traded", with why not in a new "why not" column, and the description that says so (branch `ccr-f3fba810-ie3vm6`, not merged). Its rows show once the bot runs a build with slice 2.18: deploy that build with it |
