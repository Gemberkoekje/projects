# By Galactic Accord — Slice 1 implementation plan

How we get from the current build to the playable **Slice 1** defined in Part II of
[`by_galactic_accord_gdd_v0.3.md`](../by_galactic_accord_gdd_v0.3.md) (the design, §12–§17). The design
says *what*; this file says *in which order*, and tracks status. Each step below has a goal, a
deliverable, a check, and one status line. When a step finishes, its log (what was built, checked and
measured) goes in `log/S<n>.md` and the status line links there.

**Status (2026-10-26):** S1, S2 and S3 done ([S1 log](log/S1.md), [S2 log](log/S2.md), [S3 log](log/S3.md)); S4
is next. The UI mockup
is at <https://claude.ai/artifact/VUfFWMFGd7p4ryYau2g7Pg> (private until shared): it is the visual target for
S2 and S9.

---

## 1. Where we start

The current build implements the v0.2 slice: a hauling loop with AI haulers, a WPF client and a console
client. It is about 5,700 lines and its 42 tests pass, though the suite takes over nine minutes. The
foundation is sound; most of the game logic on top of it changes shape.

| Keep | Rework | Retire |
|---|---|---|
| `EventQueue` (stable min-heap) | `Contract` → kinds (Sale, Haul, Build, Storage) and structured terms | WPF client (`ByGalacticAccord.Wpf`) |
| `DeterministicRandom` | `Settlement` → per kind, driven by terms | Console play mode (`Cli/Game.cs`) |
| `Credits`, typed ids | `DecisionEngine` → per-role actions: post, respond, invoke a term, buy or sell assets | The v0.2 README |
| `SimulationContext`'s conservation-safe primitives (faucet, sink, transfer, escrow) | `Negotiation` → replies over game time, multi-term, tells | |
| The invariant framework and soak harness | `Visibility` → presence, tiers, a per-actor knowledge store, the news | |
| Determinism tests | `Ship` / `ShipType` → parts, grades, computed stats, designs | |
| `WorldGenerator`'s map and cast (extended) | `ActorState` → possessions by location, debt | |
| `FindPath` (cached) | `ReputationLedger` → Accord standing plus counterparty records; the public record | |
| The transit-incident hook | `EventJournal` → structured entries with causal links | |

**Defects from the review, each fixed by the step named:**

| Defect | Fixed in |
|---|---|
| The board and the event log show the buyer's hidden reservation fee, so haggling is solved. *Since S2 neither prints it, but the posted late fee and its cap (the bond) are still scaled from it, so the terms give it away.* | S3 (fixed early: every term now scales from the posted price) |
| The player never pays running costs (heartbeats skip the player) | S3 (fixed) |
| Charges stop at ₵0, so nobody can go broke; a broke player still takes escrow jobs with a ₵0 bond | S8 |
| `AcceptHaul` doesn't check the contract is still open | S2 (fixed: the client calls it) |
| Food decays below its warranty on even the shortest run | S3 (fixed: the §15.3 keep-times) |
| Visibility is effectively omniscient (keyed on a fixed home; home region covers 5 of 7 locations) | S6 |
| Incident outcomes don't shift with danger, despite the comment saying they do | S8 |
| The test suite is slow: the board query walks and sorts every contract ever posted, and `FindPath` runs Dijkstra per contract, per ship, per heartbeat | S1 |
| *Found in S2:* outside the Accord the pilot pays the producer for the goods at pickup and is paid only the fee at handover, so nearly every frontier haul loses money (and AI haulers take them, since their scoring ignores the goods) | S3 (fixed: a delivered Sale pays for the goods) |
| *Found in S2:* consumers post hauls from a producer at their own location (Helios Prime), a one-hour trip to nowhere | S3 (fixed: a Sale handed over where it is posted) |
| *Found in S3:* AI actors pay out whatever they hold above a working capital, a sink standing in for spending, growth and failure; without it savers soak up the money supply | S7, S8 (growth and insolvency replace it) |
| *Found in S3:* a producer that sells delivered fails the Sale when nobody takes its Haul in time (about 3.5% of contracts over a million ticks) | S4 (cancellation, grace), S5 (raising the offer by negotiation) |
| *Found in S3:* Dust Runner Kael's armoured freighter costs more to run than it earns in this sector, and an actor can't change its fleet | S7 |

## 2. How we work

- **Playable at every step.** Each step ends with something you can run and click. The client arrives
  early (S2) so every engine change lands visibly.
- **Engine first within a step, then its interface.** A step isn't done until its feature is visible
  and explainable in the client.
- **Symmetric by construction.** Every player action is a command that goes through the same engine path
  an AI decision uses. No player-only code paths in the engine.
- **Text is data.** Every interface sentence is a template over event fields, computed values and explicit
  references (design §11.5). Each step that adds an event adds its templates; no free prose, no claims
  about intent or hidden state.
- **Every failure path gets a test,** for an AI actor and for the player.
- **Invariants on, always.** Fast tests check them on short runs; soak runs check them over a million ticks.
- **The code map stays true.** [`CODEMAP.md`](CODEMAP.md) changes in the same commit as the code it describes.
- **Checked in a browser.** The client is driven by Playwright scripts that screenshot key screens and
  fail on console errors, so interface work is checked like the rest of the code.
- **Lands directly on `main`, then work stops for review.** There is no PR gate (the user's choice,
  2026-10-26, as for the RayTracer's Vista slices): the user reviews each step on `main`. Its commits and
  its log say what was measured and with which commands.
- **Green before main.** Before pushing, from a clean working tree: the whole solution builds in Release
  with no warnings (`dotnet build -c Release`), and every test passes, fast and soak (`dotnet test -c Release`,
  then `dotnet test -c Release --filter Category=Soak`). Once the client exists (S2), its Playwright checks
  pass too. Fetch `main` just before pushing; the push must be a fast-forward. If `main` has moved, bring it
  in and run the checks again. CI (`.github/workflows/ci-bygalacticaccord.yml`, the same build and tests on
  Ubuntu) then confirms the push; a red run on `main` is fixed before anything else lands.

## 3. Steps

### S1 — Groundwork

- **Goal:** a lean, fast base to build on.
- **Deliverable:**
  - Remove the WPF client and the console play mode. The CLI becomes the soak harness only.
  - Cache all-pairs routes (they're static).
  - Split tests into fast (default) and soak (`[Trait("Category", "Soak")]`).
  - Add game-time formatting ("Day 4 · 14:00", "in 1d 6h").
  - Rewrite the README for the new direction.
- **Verify:** `dotnet test --filter "Category!=Soak"` finishes in under a minute; the soak category still
  passes; the solution builds on Linux.
- **Status (2026-10-25):** done. Fast tests run in about 2 s by default (they took 6 min 49 s as one suite),
  the soak category in 36 s, and a million-tick soak 8–13 times faster with identical results; see
  [`log/S1.md`](log/S1.md).

### S2 — The Blazor client, on the current engine

- **Goal:** the interface skeleton from §11, running the existing simulation, so later steps have
  somewhere to land.
- **Deliverable:**
  - A `ByGalacticAccord.Client` Blazor WebAssembly project: the schematic map in SVG (routes styled by
    danger with colour plus line style, ships interpolated along paths), the time bar (pause, 1×, 4×, 16×),
    the board, the contract panel, and the event log.
  - Colour tokens with light and dark themes.
  - The template layer for interface text (§11.5): resource files keyed by event and fact type.
  - A Playwright script that loads the client, runs time forward and screenshots it.
- **Verify:** the client runs locally; the script reports no console errors; screenshots match the
  mockup's layout and visual language.
- **Status (2026-10-26):** done. The client runs the engine in WebAssembly at about 21,800 game hours a second
  (16× needs 16) and reaches exactly the state desktop .NET does; the browser check passes with no console
  errors and screenshots the main screen in both themes and at phone width. The player can take a haul as
  posted. See [`log/S2.md`](log/S2.md).

### S3 — Possession, Sale and Haul

- **Goal:** split v0.2's bundled "buy and haul" into the contract web's first two kinds.
- **Deliverable:**
  - Goods live in an actor's own stock, in storage (Storage contracts at station operators), or in ship
    holds.
  - **Sale** (wanted or for sale, with a handover place) and **Haul** (the shipper owns the goods), each
    with a simple deadline and late fee for now.
  - AI rewired:
    - consumers post delivered Wanted Sales;
    - producers post For-sale Sales and answer Wanted ones;
    - traders fill delivered Sales by buying and carrying, and post Hauls when overcommitted.
  - The player pays running costs like everyone else.
  - Perishables tuned to the §15.3 keep-times.
  - Goods conservation covers stock, storage and holds.
  - Client: possessions in the ledger; kinds on the board.
- **Verify:** invariants hold over a million-tick soak; the economy stays stable (telemetry against
  S1's baseline); lifecycle tests for Sale, Haul and Storage pass.
- **Status (2026-10-26):** done. Goods are owned and somewhere (stock, storage, a hold) and conserved; Sales and
  Hauls replace the bundled contract; every role trades through one set of market commands, the player's too.
  Over a million ticks invariants hold, the money supply drifts 1–2%, and about 109,000 contracts settle where S1
  settled 5,600–25,000, with consumers using 51–98% of what they want. The client shows each contract's kind and
  a ledger of what the player owns and where. See [`log/S3.md`](log/S3.md).

### S4 — Terms and failure

- **Goal:** failure is content (§3.4, §16.3).
- **Deliverable:**
  - Structured terms: deadline and grace, a late fee with a cap, cancellation for default, cancellation at
    will with a break fee, partial delivery, a quality warranty with rejection, cargo liability, bonds.
  - The strict, standard and lenient packages.
  - Escrow and bonds per jurisdiction; payer default on the frontier.
  - Decisions: AI by utility. The player gets a prompt and a hard pause.
  - Causal links on every lifecycle event.
  - Client: terms as plain sentences, decision prompts, and the "why did this happen" chain.
- **Verify:**
  - A test per §16.3 path, both for an AI actor and through player commands.
  - Soak telemetry counts every failure kind above zero.
  - Escrow reconciles exactly.
- **Status:** not started.

### S5 — Posting, responses and haggling

- **Goal:** the player posts as well as takes (§3.6), which gives the first hour's second beat.
- **Deliverable:**
  - Anyone posts any Slice 1 kind.
  - Posts reach actors according to their presence.
  - Responses arrive over game time.
  - Haggling over price, deadline, late fee and package, with replies after 1–12 game hours. The tells
    are reply time and wording. AI-to-AI deals use the closed form.
  - The reservation leak is gone.
  - First bot players in the soak harness: a "board taker" and a "market reader".
  - Client: the post composer, "my posts" with responses, and negotiation threads.
- **Verify:**
  - Reply timing is deterministic per seed.
  - A food post at Verdant draws the producer's response.
  - Haggling cannot advance while paused.
  - The bots run a full soak without breaking invariants.
- **Status:** not started.

### S6 — Information and the news

- **Goal:** judging needs uncertain, ageing information (§7).
- **Deliverable:**
  - Presence: ship locations and destinations, plus storage.
  - The here, near and far tiers.
  - A per-actor knowledge store of last-seen prices and stocks, with timestamps. AI decides on its own
    knowledge and trusts what it last saw (D32).
  - The Accord public record.
  - **The Daily Accord:** a once-a-day digest drawing on yesterday's contracts, weighted by size, novelty
    and failure.
  - Client: stale information faded and labelled "as of …", signals, and the news panel.
- **Verify:**
  - Tier tests.
  - In a soak, the frontier still gets supplied (see risk R1).
  - The market reader beats the board taker.
- **Status:** not started.

### S7 — Ships from parts

- **Goal:** the signature decision (§6): stock, second-hand or custom.
- **Deliverable:**
  - Parts as graded goods; ship stats computed from parts; the five designs.
  - Part factories with inputs.
  - **Build** contracts:
    - yards bid from their own knowledge;
    - they post Wanted Sales for parts and assemble one ship at a time;
    - they build stock designs on spec when idle.
  - Substitution policies and the supply-failure term with its remedy proposals.
  - Second-hand Sales (AI sells idle ships; Edge Salvage); ship histories.
  - AI companies order ships when they want to grow.
  - Client: the ship sheet, the orders tree, the build composer, and the yard bids.
- **Verify:**
  - Goods conservation holds through parts into ships.
  - A test per build failure path.
  - In a soak, yards complete builds and AI fleets grow and shrink.
- **Status:** not started.

### S8 — Consequences

- **Goal:** risk has a downside (§5.4, §8.4).
- **Deliverable:**
  - Debt below ₵0, with interest.
  - Insolvency: fire-sale liquidation and contracts cancelled for default.
  - Player insolvency leads to the legacy screen.
  - The cruise or push throttle.
  - Incident outcomes that shift with danger and are modulated by toughness and reliability.
  - Docking fees.
- **Verify:**
  - Insolvency tests for an AI and for the player.
  - In a soak, some AI actors fail and their ships resell, but not en masse.
  - The trap contract from the first hour is visibly a trap.
- **Status:** not started.

### S9 — The interface, complete

- **Goal:** everything in §11 that earlier steps didn't already finish.
- **Deliverable:**
  - A hover explanation for every number (the engine exposes breakdowns; the client renders them).
  - The timeline in the time bar.
  - Ledger profit and loss per contract.
  - Player pause rules.
  - Onboarding polish.
- **Verify:**
  - Playwright walks the first hour's screens.
  - Every number on the board and in the contract panel has an explanation.
  - Definition of done item 6: the interface can answer "why did this contract make or lose money".
- **Status:** not started.

### S10 — The first hour, tuned

- **Goal:** meet the definition of done (§12).
- **Deliverable:**
  - The seeded start state that guarantees beats 1–3.
  - Bot players that measure beats 4–5 and the skill gap.
  - Tuning against the §17 pacing targets.
  - Playtests.
- **Verify:**
  - All six done criteria in §12.
  - Beats 4–5 occur in at least 70% of bot first hours.
  - The market reader earns at least 1.5× the board taker.
  - Three of four playtesters finish the first hour unaided.
- **Status:** not started.

## 4. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | With distant contracts invisible (D31), the frontier starves: few actors have presence at Edge Station. | News weights favour frontier contracts; traders based in the Marches; measure supply per location in soak and tune. |
| R2 | AI complexity explodes once every role posts and answers every kind. | Small action sets per role; one utility shape; AI-to-AI haggles in closed form; soak telemetry per action type. |
| R3 | Replies over game time make haggling feel slow. | At 1× a reply takes 1–12 seconds; tune the range; notifications rather than waiting screens. |
| R4 | The simulation is too heavy for WebAssembly in the browser. | Cached paths, trigger-driven AI, time-sliced processing per frame; profile in S2 with the current engine. |
| R5 | Slice 1 is much larger than v0.2's slice. | Every step is playable; S9's polish can shrink without losing the core; cut candidates are Storage and the cargo pod. |
| R6 | Determinism drifts between desktop .NET and WebAssembly. | Replay hashes compared across both in CI; decimals for all money and quantities. |

## 5. Answers from the 2026-10-25 review

Recorded in the design as D29–D37; summarised here with where each lands.

| Question | Answer | Slice 1 | Later |
|---|---|---|---|
| Distant markets: visible without presence? | Through news and data brokers eventually. | No far contracts, except via the daily news (D31) | Market news, data brokers (Slice 2) |
| Should AI discount stale information? | Aware without being unexploitable; each AI's risk profile decides how far it trusts old information. | Trusts what it last saw (D32) | Risk-tolerance-based trust (Slice 2) |
| Private offers to named actors? | Yes. | No | From Slice 2 (D33) |
| Signed contracts public? | Contracts can make the next day's news. Long term, a visibility term: public (publicity, reputation) or private (NDA). | Every contract public; a chance to make the news (D34) | Visibility term (Slice 2) |
| One build at a time per yard? | Yes for Slice 1. Long term, boutique yards build to spec and large yards build batches of near-identical standard ships. | One at a time (D35) | Yard kinds (Slice 6) |
| A "planned use" field on build orders, for text to compare against? | Yes, but maybe not in Slice 1. | No; text compares against fixed references (§11.5) | Tentatively Slice 2 (D37) |
| Storage in Slice 1? | Kept (it was in the doc and not struck). | Yes (D29) | — |
