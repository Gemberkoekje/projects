# BY GALACTIC ACCORD

**Game Design Document**
Working Title · Sandbox Edition · **v0.2** (revision of v0.1)

> *A contract-based space trading simulation in a living, player-agnostic universe.*

---

## Revision Notes (v0.1 → v0.2)

This revision keeps the v0.1 design intact wherever it was a deliberate choice (the player-as-cog stance, decoupled production/consumption, no continuous time). It does three things: resolves contradictions, specifies systems that were doing heavy lifting while undefined, and adds new design directions for your evaluation.

**Resolved (genuine contradictions or holes in v0.1):**

- **Danger ratings were inert.** v0.1 put "route danger ratings" *in* the vertical slice as "required for meaningful risk/reward" but deferred the only mechanic that consumed them (interdiction). Danger ratings did nothing in the slice. Fixed with the **Transit Incident** mechanic (§6.3) — abstract, actor-free route risk that gives danger ratings teeth in hour one and generalises cleanly when the piracy slice lands.
- **Frontier contracts had no enforcement.** Frontier = "reputation only," but reputation-as-deterrent depends on *retaliation*, which depends on pirate-like actors, which are deferred. So frontier contracts in the slice had zero fraud protection. Addressed by the **Bonding Agent** institution (§4.4) and by making reputation actually *gate* contract access (§4.3).
- **The time model was never decided.** "Pauses until the player responds" implies the sim runs otherwise — but at what cadence? This is the single most consequential unresolved question (it determines the entire feel of the game). Decided in §3.4: **continuous simulation with player-controlled speed and hard auto-pause on decision events.**

**Specified (present in v0.1 but undefined, and load-bearing):**

- **`ContractClause` schema** (§4.1.5) — clauses drive cascades, escape hatches, warranties, insurance, and bonding, but had no model. Now a typed Trigger/Effect structure.
- **Negotiation mechanics** (§4.1.6) — "offer / counteroffer / accept" is the *central verb* of a contract-first game. Now a concrete reservation-price model with readable tells.
- **The actor decision algorithm** (§4.5.3) — "the world feels alive" lives or dies here. Now a concrete scheduling + greedy-utility model.
- **Reputation data model** (§4.3) — the sole frontier enforcement mechanism and the trust-threshold gate. Now contextual and bounded with decay.
- **Determinism / seeded RNG** (§5.5) and **simulation invariants + test strategy** (§5.6) — an event-sourced sim with hidden RNG is not actually replayable, and an emergent economy is untunable without headless soak tests and invariant checks. Both were absent and both are essential.
- **Information & visibility** (§4.6) — "a player who reads the signals has an advantage" was a hope, not a mechanic. Now an actual visibility model.
- **UI / information architecture** (§5.7) — for a sim this information-dense, the UI *is* the game; v0.1 had "TBD."

**Added (new design directions — flagged inline with *▸ New in v0.2* so you can accept or reject each):**

- **The Galactic Accord** (§2.3) — the title currently has no in-world referent. Proposed as the treaty body that defines the standard contract, escrow protocol, and arbitration — giving the enforcement-context table a single diegetic spine and the title a meaning.
- **Information as a tradeable good** (§4.6) — market reports and danger histories as a good a data-broker actor sells. Perfectly consistent with contract-as-primitive.
- **Standing / recurring contracts** (§4.1.7) — logistics as infrastructure; cuts micro and enables passive businesses.
- **Demand starvation** (§7.7) — unmet demand has consequences beyond price: colonies wither or bloom based on whether you supply them. Stakes without piracy.
- **Bankruptcy & asset liquidation** (§7.8) — organic money-supply regulation and opportunity, reducing reliance on the inflation god-hand.
- **Player goals, failure states & legacy** (§3.5) — a sandbox still needs jeopardy and aspiration.
- A **sliced roadmap** (§8) replacing the vague "milestone 2," and an **idea bank** (Appendix B).

---

## 1. Vision & Design Philosophy

By Galactic Accord is a space trading sandbox in which **contracts are the universal language of economic interaction**. Every transaction, every negotiation, every act of piracy or industry is mediated through a contract system that treats all actors — player, corporation, government, pirate — as fundamentally equivalent participants in a living economy.

The universe does not exist for the player. It runs continuously, driven by actors with goals, personalities, and constraints. The player is a cog in that machine, with the freedom to decide what kind of cog they want to be.

### 1.1 Core Pillars

- **Contracts as the universal primitive.** Not just cargo hauling — ship construction, insurance, arbitration, piracy fencing, bonding, and law enforcement are all contracts with parties, terms, and enforcement mechanisms.
- **Simulated cause, not scripted outcome.** Traffic patterns, price shifts, piracy hotspots, and trade-lane development emerge from actors pursuing goals. Nothing is spawned because it is 16:00.
- **Every object tells a story.** A ship with 115 cubic units of cargo space instead of 120 has support beams from a repair job. The stat and the story are the same thing.
- **Meaningful destruction drives meaningful creation.** Ships wear out, get interdicted, get destroyed. This is not a bug. It is the demand engine that makes supply non-trivial.
- **Player and actor are the same type.** The player has no special privileges in the simulation. Whatever decision-making framework an AI actor uses is available to the player, and whatever the player can do, an AI actor could theoretically do.
- **Legible by construction.** *(Promoted to a pillar in v0.2.)* Dwarf Fortress earns its emergent depth and pays for it in opacity. We take the depth and refuse the tax: every number the player sees is explainable from a story, every signal the player needs to act on is *surfaceable*, and the first hour teaches the loop without a wiki. Legibility is a design constraint on every system, not a tutorial bolted on at the end (see §3.3).

### 1.2 Player Experience Goals

*(New framing in v0.2. v0.1 was system-first and never described the player's felt experience; these are the experiential targets the systems must hit.)*

- **The world is visibly indifferent to you, and that's the appeal.** Prices move, companies grow and fail, lanes open and silt up — whether or not you log in. The player should feel like a small operator who has *found an edge*, not a chosen one handed a destiny.
- **Reading the economy is the skill.** Mastery is noticing that idle producers mean a price spike is coming, that a refinery fire just made fuel hauls lucrative and dangerous, that a rival is overextended and about to break. The fun is being *right* about the simulation.
- **Risk is a dial the player chooses.** Safe core hauls for thin steady margin, or the frontier exotic run that pays for a month and might cost the ship. The game never forces the dial; it makes both honest.
- **Consequence is real and survivable.** You can go broke. You can also claw back. A cascade of breaches can ruin a quarter; it should sting, and it should be recoverable.

### 1.3 Inspirations & Points of Departure

| Game | What we take | What we do differently |
|---|---|---|
| EVE Online | Destruction-driven economy, player-created contracts | AI-populated universe that works without players |
| X-Series (X4) | Living economy, player as one actor among many; real-time-with-pause | Contract system as primary interface, not just trading |
| Dwarf Fortress | Emergent complexity from simple rules | Playable and **legible** from the start (§1.1) |
| Elite Dangerous | Scale, lone-pilot feel, faction politics | Economy driven by simulation, not hand-tuning |
| *Capitalism / transport sims* | Supply chains as systems, not set dressing | Everything routed through one contract primitive |

---

## 2. Setting

Humanity has recently achieved faster-than-light travel. The technology is new, imperfect, and expensive — its exotic fuel components must be sourced from the fringes of explored space, near phenomena that remain poorly understood and genuinely dangerous.

The stars were not empty. Several alien civilisations exist, ranging from post-scarcity ancients to curious but isolationist neighbours. None of them are hostile. None of them are particularly interested in humanity either. Their indifference is the setting's slow-burn tension: as human expansion encroaches on territory they consider theirs in some untranslatable sense, that indifference may curdle into something more complicated.

The game begins in the early expansion era. FTL lanes are mapped, not fully understood. Colonies range from thriving industrial hubs to precarious outposts. Law is a geographic variable — core systems have courts and police forces, frontier docks have reputation and consequences.

### 2.1 FTL as an Economic Driver

- FTL drives require exotic materials harvested from dangerous frontier space (black-hole margins, unstable stellar phenomena).
- FTL-lane cartography is a profession. First-mover advantage on a newly mapped stable route has enormous economic value.
- FTL itself carries risk. A jump that goes wrong is a contract cascade waiting to happen.
- Insurance for FTL cargo is its own industry, priced by route danger ratings that update based on incident history.

### 2.2 The Alien Indifference (Future Layer)

Not a vertical-slice concern. In the full vision, alien factions are actors in the simulation with their own contract-equivalent systems. As human expansion reaches certain thresholds, alien behaviour shifts — more interference events, resource-access restrictions, diplomatic contract equivalents. This can drive emergent political storylines without scripted plot. (See §7.5 and §2.3 — the aliens are *non-signatories* to the Accord, which is what makes their thresholds politically charged.)

### 2.3 The Galactic Accord  *▸ New in v0.2*

The title needs a referent, and the enforcement-context table (§4.1.4) needs a *reason* — why do core systems have automatic escrow while the frontier has nothing? The answer is the **Galactic Accord**: the multilateral human treaty (with a handful of pragmatic alien co-signatories) that defines three things every player touches constantly:

1. **The standard contract format** — the schema in §4.1.1 *is* the Accord standard. "By Galactic Accord" is the legal phrase stamped on every compliant contract.
2. **The escrow protocol** — Accord clearing houses hold escrow and release it on confirmed fulfilment.
3. **The arbitration courts** — where disputes go, inside Accord jurisdiction.

**Accord reach is geography.** Core and mid-tier systems are inside the Accord (escrow + arbitration). Frontier space is *outside* it (reputation only). This single idea explains the whole enforcement-context table diegetically, instead of it being four disconnected rules.

**Accord standing is a reputation axis** (§4.3). Honour your standard contracts and the Accord trusts you; breach them flagrantly inside Accord space and you can be **sanctioned** — locked out of Accord contracts and pushed toward the lawless frontier, where the bonding agents (§4.4) and the pirates (§7.2) live. The frontier is not just "the edge of the map"; it is *life outside the Accord*, with all the freedom and exposure that implies.

This also arms the alien layer (§7.5): the aliens never signed the Accord, so as humanity expands, the open question — can the Accord be *extended* to them, *imposed* on them, or does it simply *end* at their border? — becomes the emergent political pressure, expressed through the same contract and reputation systems the player already uses.

---

## 3. Core Loop

The core loop in three sentences:

> *You take contracts to earn credits. Credits let you upgrade your ship and take better contracts. The universe shifts around you based on forces you can influence but not control.*

Everything else — ship variance, piracy, cascading contract failures, alien interference — is texture layered onto this loop. **A feature belongs in the vertical slice only if it directly serves the loop.** Everything else is a later slice.

### 3.1 The Contract as Primary Interface

The player does not shop, craft, or quest in the traditional sense. They interact with the world through contracts:

- A consumer posts a buy contract. A producer posts a sell contract. The player (or an AI pilot) brokers the connection by fulfilling a haul contract.
- Need a new ship? Post a procurement contract with your specifications. Shipyards, independent builders, and second-hand sellers respond with counteroffers.
- Want to expand? Post a service contract for a pilot, a factory operator, a security escort.

Three participation modes, all built on the same data model:

- **Demand contract:** *I want X delivered to me, I will pay Y.*
- **Supply contract:** *I have X at location A, I will pay Y to have it moved.*
- **Brokered contract:** Buyer and seller have agreed terms; the pilot is paid to move the goods between them.

### 3.2 The Player's Verbs

*(New in v0.2. v0.1 stated the loop abstractly but never enumerated what the player actually does. These are the concrete interactions; everything else is reading state to inform them.)*

1. **Browse** the contract board(s) visible to you (§4.6 governs what's visible).
2. **Plot** a route across the location graph, weighing distance, danger, and law (§4.7).
3. **Negotiate** — accept, counter, or walk away from an offer (§4.1.6).
4. **Commit** a ship to an active contract (locks escrow, schedules departure).
5. **Respond** to interruptions mid-route — a transit incident, an interdiction demand, a better offer (§4.7.3).
6. **Acquire** — post procurement contracts to buy ships, upgrades, or (later) facilities (§3.6).
7. **Set policy** — standing contracts, alerts, and automation rules so the business runs by exception (§4.1.7).

### 3.3 Onboarding & Legibility (First-Session Walkthrough)

*(New in v0.2. The legibility pillar (§1.1) was asserted but never mechanised. This walkthrough is both the onboarding design and a smoke-test for the slice — if any of these beats can't be expressed with the systems above, the systems are wrong.)*

- **Minutes 0–5.** Player starts as an Independent Pilot, one beat-up Light Freighter, docked at a mid-tier colony. The local board shows three or four haul contracts. One is highlighted: a short, safe food run to an adjacent agricultural world. The player accepts it (a guided first negotiation: the counterparty accepts immediately). Departure, a clean arrival, escrow releases, first credits, a tick of reputation. The loop, demonstrated once, end to end.
- **Minutes 5–20.** The board now shows a choice with a *trade-off* the player can read: a safe low-margin parts haul vs. a higher-paying run down a route flagged amber for danger. Taking the dangerous one introduces the Transit Incident (§6.3) as a *survivable* scare — a delay or minor cargo damage, not a death. The player learns danger ratings mean something.
- **Minutes 20–40.** A perishable food contract with a tight deadline and a penalty clause. The player must pick the right ship behaviour (push speed and run hot, or play it safe and risk lateness). First exposure to clauses (§4.1.5) and to consequence.
- **Minutes 40–60.** The player notices — or the UI gently surfaces — that two producers at the agricultural world have gone idle. Prices for their good are about to move. The player who acts on that signal makes their first *informed-edge* profit, and the game's actual skill clicks. By the end of the hour they have enough to start a procurement contract for a second ship.

Legibility mechanisms that make the above work, applied across all systems: every stat carries its causal story on inspection; every signal the player needs is surfaceable (idle-producer indicator, route danger colour, deadline countdown); soft tells in negotiation (§4.1.6); and an event log the player can read like a captain's log.

### 3.4 Time & Pacing Model — **Decision**  *▸ Resolved in v0.2*

This is the most important open question in v0.1 and it changes everything downstream, so it is decided here rather than left "TBD."

**Options considered:**

- **Turn-based (clock advances only when the player acts).** Fully deterministic and pressure-free; trivial to reason about. But it badly undercuts the "living universe" pillar — the world only moves on *your* turn — and time-pressure mechanics (deadlines, interdiction) become artificial.
- **Continuous real-time with variable speed + hard pause.** The universe runs at a configurable tick rate. The player controls speed (Pause · 1× · Fast-forward). The simulation **hard-pauses automatically** on any `ActorResponseRequired` event addressed to the player, and on any player-set alert (e.g. "pause when an Exotic Materials contract appears within two jumps"). The player can browse, plot, and negotiate while paused *or* while running.

**Decision: continuous real-time with variable speed and hard auto-pause.**

Rationale: it makes "the world moves whether or not you act" *literally true*, gives deadlines and interdictions genuine weight, and matches the cited inspirations (X4, Elite). Crucially, the discrete-event engine already supports it for free: "running" means dequeuing events as wall-clock time advances at the chosen rate; "paused" means stop dequeuing. The player never experiences continuous time — they experience a world that advances at a speed they control and *stops for them when a real decision lands.* (Glossary "Tick" updated accordingly: ticks-per-second is configurable; default 1 tick ≈ 1 real second at 1×.)

### 3.5 Goals, Failure & Legacy  *▸ New in v0.2*

The game has **no win condition** by design — but a sandbox without jeopardy is a spreadsheet, and one without aspiration is a chore. Both are provided without scripting:

- **Failure state — bankruptcy.** If your obligations exceed your assets and your credits hit zero, you're insolvent (§7.8). Lose your last ship and you're done — or, diegetically, you restart as a Corporate Lackey, indentured to whoever bought your debt. The path there is the cascade: a string of breached contracts dragging your reputation and escrow down together. Real, and survivable until it isn't.
- **Aspiration — soft, player-chosen ledgers, never quests.** Build a fleet. Found and grow a company. Corner a good's market in a region. Control a chokepoint route. Bankrupt a specific rival. Single-handedly keep a dying frontier colony alive (§7.7). The simulation *recognises* these from the player's own event log and marks them; it does not hand them out as objectives.
- **Legacy — retirement.** The player can retire at any time. The game then renders their legacy from their event log: markets they moved, companies they founded or broke, colonies they saved or starved, and the specific battered Light Freighter with the support-beam patch they flew for four hundred ticks before selling it. This gives the open-ended sandbox an *arc*, and a reason to play *well* rather than merely *long*.

### 3.6 Ship Progression  *(specified in v0.2)*

The "upgrade your ship" half of the loop needs a shape, even before exact tuning:

- **Acquisition is a contract.** You buy ships by posting a procurement contract (§3.1); shipyards and second-hand sellers counteroffer. There is no shop.
- **The ladder is niche, not strictly "better."** Progression is not a linear power curve from Light Freighter to Bulk Hauler — each ship is the *right tool for a niche* (§4.4.1). "Upgrading" means *acquiring the ship that unlocks a market you couldn't profitably serve*: a Bulk Hauler to take the ore contracts the frontier mining colony posts, a Refrigerated Transport to chase the fat perishable-medical margins, an Armed Freighter to survive the exotic runs.
- **Running cost gates fleet size.** Every ship drains credits per tick whether or not it's earning (§4.4.1). This is the sink that stops the player (or an AI company) from hoarding hulls — an idle fleet bleeds you. Fleet growth is therefore self-limiting and tied to throughput, not just savings.
- **Costing is tuning, not design.** Exact credit values live in a balance table tuned via headless soak runs (§5.6), not hard-coded here.

---

## 4. Systems

### 4.1 The Contract System

#### 4.1.1 Contract Data Model (the Accord Standard)

A contract is an aggregate with the following core fields:

| Field | Type | Notes |
|---|---|---|
| ContractId | GUID / Qowaiv typed ID | |
| ContractType | Enum: `Haul`, `Procure`, `Service`, `Insurance`, `Brokered`, `Bond` | `Bond` added in v0.2 (§4.4) |
| PostedBy | ActorId | The actor who created the offer |
| CounterpartyId | ActorId? | Null until accepted |
| Good | GoodType | What is being moved or procured |
| Quantity | decimal | In standard units per good type |
| OriginLocationId | LocationId | |
| DestinationLocationId | LocationId | |
| OfferedPayment | Credits | What the poster is offering |
| AgreedPayment | Credits? | Null until negotiation concludes |
| DeadlineTick | long | Game tick by which delivery must occur |
| Status | Enum: `Open`, `Negotiating`, `Active`, `Fulfilled`, `Breached`, `Expired`, `Disputed` | |
| Clauses | List\<ContractClause\> | Warranties, penalties, inspection rights, insurance/bond riders (§4.1.5) |
| EscrowHeld | Credits | Amount locked pending fulfilment |
| AccordCompliant | bool | Was this signed under the Accord standard / in Accord jurisdiction (§2.3)? |
| Jurisdiction | LocationId / RegionId | Determines enforcement context (§4.1.4) |

`AccordCompliant` and `Jurisdiction` are added in v0.2 so the enforcement-context lookup (§4.1.4) is a property of the contract, not an out-of-band rule.

#### 4.1.2 Contract Lifecycle

- **Posted** — actor creates an open offer visible to eligible counterparties (eligibility: §4.6).
- **Negotiating** — a counterparty responds with a counteroffer; the poster can accept, counter again, or withdraw (mechanics: §4.1.6).
- **Active** — both parties agreed; escrow locked; the fulfilling ship scheduled.
- **Fulfilled** — delivery confirmed; escrow released; reputation updated for both parties.
- **Breached** — one party failed to perform; escrow disposition determined by enforcement context.
- **Disputed** — one party contests the outcome; referred to arbitration if available in the jurisdiction.

#### 4.1.3 Enforcement Context

Enforcement is a geographic and social variable, not a game rule. The same breach has different consequences depending on where the contract was signed (its `Jurisdiction`) and who the parties are. The pattern below is simply **the reach of the Accord** (§2.3) made concrete:

| Context | Enforcement mechanism | Breach consequence |
|---|---|---|
| Core system (deep Accord) | Automatic escrow + Accord arbitration | Legal penalty, credit loss, Accord-standing hit |
| Mid-tier colony (Accord edge) | Escrow only, no arbitration | Credit loss, reputation hit |
| Frontier outpost (outside Accord) | Reputation only, no escrow — *unless bonded* (§4.4) | Reputation damage, possible hired retaliation |
| Black market | None formal | Danger-rating increase, fence relationships affected |

#### 4.1.4 Cascading Contracts

Contracts interact. A breach or delay in one contract can trigger clauses in dependent contracts. A shipyard whose thruster shipment was interdicted may invoke an escape-hatch clause, offering the client a refund plus compensation or an extended timeline. The client then has a decision that affects their own contract graph.

This is not a designed event chain. It emerges from contracts having clauses (§4.1.5) and actors responding to changed circumstances. **The simulation does not need to know that contracts are related. The actors do** — and the clauses do, because a clause's Effect can post or alter other contracts, which the engine then processes normally.

#### 4.1.5 Contract Clause Schema  *(specified in v0.2)*

In v0.1, `Clauses` did enormous work — escape hatches, warranties, penalties, inspection, insurance, and (now) bonding all ride on it — but had no model, which makes cascades unbuildable. A clause is a **typed, evaluable term** with a Trigger and an Effect:

| Field | Type | Notes |
|---|---|---|
| ClauseId | typed ID | |
| ClauseType | Enum (below) | Determines default Trigger/Effect shape |
| Trigger | Predicate over contract + sim state | When the clause fires |
| Effect | Action that schedules events / posts contracts | What happens when it fires |
| Parameters | Clause-specific payload | Penalty amount, quality threshold, insurer/bonder ActorId, etc. |

**Clause types (vertical slice in bold):**

- **`PenaltyOnLateDelivery`** — *Trigger:* delivery tick > `DeadlineTick`. *Effect:* transfer a parameterised penalty from the fulfiller's escrow to the poster.
- **`QualityWarranty`** — *Trigger:* delivered quality < threshold (perishables, §4.3). *Effect:* reduce payment / partial refund.
- **`EscapeHatch`** — *Trigger:* `Status → Breached` due to an upstream cascade. *Effect:* offer the counterparty refund + compensation **or** an extended `DeadlineTick`; the choice is itself an `ActorResponseRequired`.
- `InspectionRight` — *Trigger:* on arrival. *Effect:* counterparty may reject sub-threshold goods before escrow release.
- `InsuranceRider` (later slice, §7.x) — *Trigger:* a `TransitIncident`/`Interdiction` event on this haul. *Effect:* post a `ClaimContract` against the named insurer.
- `BondRequirement` (§4.4) — *Trigger:* contract acceptance in unbonded jurisdiction. *Effect:* require a third-party `Bond` contract before the haul can go `Active`.
- `StandingRenewal` (§4.1.7) — *Trigger:* `Fulfilled` or `Expired`. *Effect:* re-post the contract from a template at prevailing price.

Clauses are evaluated at lifecycle transitions and at `CascadeCheck` (§4.7.2). **This is the mechanism that makes "everything is a contract" true rather than slogan:** insurance, bonding, and cascades are not special-cased code paths — they are clauses whose Effects post ordinary contracts.

#### 4.1.6 Negotiation Mechanics  *(specified in v0.2)*

"Offer / counteroffer / accept" is the **central verb** of a contract-first game and v0.1 left its logic undefined. Model:

- **Reservation price (private).** Each party computes a hidden reservation price from its utility function: production/running cost + a desired margin, adjusted by personality (§4.5.2) — high *Liquidity Preference* widens the demanded margin; high *Growth Ambition* or deadline urgency narrows it; high *Risk Tolerance* accepts thinner margins on dangerous routes.
- **Opening offer.** Reservation price ± a markup the poster hopes to capture.
- **Concession.** Each counteroffer moves a fraction of the remaining gap toward the conceder's reservation price. The fraction is a personality trait (eager parties concede more per round). A party **accepts** when the standing offer crosses its reservation price, and **walks** when the offer is worse than reservation or after `maxRounds`.
- **The player side.** The UI shows the current offer and **soft tells** derived from how close the counterparty is to its reservation price — *"responds instantly,"* *"reluctant,"* *"appears to be at its limit."* The player counters, accepts, or walks against this. The hidden numbers stay hidden; the *reading* of the counterparty is the skill — which is exactly the §1.2 fantasy applied to a single deal.

This makes negotiation a genuine, readable micro-mechanic, identical for AI and player (a human counteroffer is structurally indistinguishable from an AI one — which is what makes §7.4 multiplayer cheap).

#### 4.1.7 Standing & Recurring Contracts  *▸ New in v0.2*

Real logistics is not a series of one-offs; it is *infrastructure*. Allow actors (and the player) to post **standing contracts** via a `StandingRenewal` clause on a template: *"buy 50 food every 100 ticks at prevailing price ± band,"* *"this haul route, repeating until cancelled."*

Why it matters: it cuts busywork dramatically (the player manages by exception, not by clicking every haul), it makes the economy *feel* like infrastructure with persistent trade lanes rather than a flickering job board, and it gives the player a path to **passive income / a logistics business** they grow and supervise. It's also pure upside for emergence — standing contracts are what make a "trade lane" a real, observable thing that can then be *disrupted* (a chokepoint blockade, §B), which creates strategy.

### 4.2 The Economy

#### 4.2.1 Faucets and Sinks

The economy is a closed credit system with designed inflows and outflows:

- **Faucet:** Consumer actors have an abstract income source that funds their buy contracts. In the slice this is not modelled in detail — consumers simply have a credit regeneration rate.
- **Sink:** Producer actors pay production costs. Ship maintenance, **running cost per tick** (§3.6), and docking fees drain credits. Government actors tax transactions and the credits disappear into abstraction.
- **Monitor:** A global inflation observer watches aggregate credit supply and nudges faucet/sink rates if the economy drifts significantly from baseline.

**Initial money supply** *(specified in v0.2):* the total credit supply at world creation is a seeded parameter; thereafter total credits = initial + Σ faucet − Σ sink, an invariant the test suite enforces (§5.6). The monitor regulates the *rate*, not the conservation.

**Design note on the monitor** *(v0.2):* the inflation monitor is a pragmatic safety valve but it is a designer's thumb on a simulation that otherwise claims to be emergent. We keep it for the slice (it must not break in the first hour), but the long-term aim is to lean on **organic regulators** — bankruptcy and asset redistribution (§7.8), actor savings behaviour absorbing variance, and new actors entering when margins are fat — and to shrink the monitor's role to a last-resort circuit breaker. Flagged so the tension is explicit and not forgotten.

#### 4.2.2 Price Signals

Contract pricing reflects supply and demand pressure:

- Scarcity of a good increases the value of haul contracts for that good.
- Oversupply depresses contract values until producers reduce output.
- Actors with savings behaviour absorb some variance before the global monitor needs to act.
- A player who can **read** these signals has genuine economic advantage — but only if they can *see* them. v0.1 assumed the player could; v0.2 makes visibility a mechanic (§4.6). Idle producers are a leading indicator of a price spike — *if you have the information to know they're idle.*

#### 4.2.3 Vertical-Slice Economy

Deliberately minimal:

- **Conjurers** (producers) post sell contracts at a rate set by production cost and current price signals.
- **Eaters** (consumers) post buy contracts at a rate set by abstract income and current prices.
- A **government** actor skims a flat percentage of all fulfilled contracts.
- The **global monitor** adjusts faucet/sink multipliers if credit supply drifts beyond threshold.

### 4.3 Reputation  *(specified in v0.2)*

Reputation is the **sole enforcement mechanism on the frontier** and the gate for trust thresholds — load-bearing, and undefined in v0.1.

- **Contextual, not scalar.** *▸ New in v0.2:* a single reputation number is too crude. Reputation is keyed by `(RatedActor, Context)`, where Context is a faction/region, a good-category, or a specific counterparty. **A pilot trusted by miners may be a stranger to the luxury cartels.** This makes reputation a *map* of where you can operate, not a single bar.
- **Earned and lost on outcomes.** `ContractFulfilled` raises the relevant reputations; `ContractBreached` lowers them. Magnitude scales with contract value and lateness — breaching a large, late, high-trust contract hurts far more than fumbling a small one.
- **Accord standing** (§2.3) is a distinguished, game-wide reputation context: your record under the Accord standard, governing access to Accord (core/mid) contracts.
- **Gates access.** Low reputation in a context limits which contracts are *visible/eligible* to you there (§4.6) — drift low enough in Accord standing and you're effectively pushed to frontier work. High-`Trust-Threshold` counterparties refuse low-reputation actors outright.
- **Decays toward neutral.** Slow regression over ticks, so old sins fade and a wronged reputation can be rebuilt — keeping the world dynamic and the player redeemable.

### 4.4 Bonding & Frontier Trust  *▸ New in v0.2*

Frontier contracts have *no formal escrow* — which leaves a fraud hole: what stops an actor accepting a frontier contract, taking the goods, and vanishing? v0.1's implicit answer was "retaliation," which depends on deferred pirate-like actors. The emergent, in-fiction fix:

A **Bonding Agent** is a third-party actor who, for a fee, offers to **hold escrow and arbitrate** a frontier deal — a `Bond` contract (§4.1.1) layered on the haul via a `BondRequirement` clause (§4.1.5). Two parties who don't trust *each other* can both trust the *bond*. This is exactly the institution that grows in real frontier economies, and it costs us no new system: it is a new actor niche and two new uses of the existing contract primitive. It is also a beautiful bridge between the lawless and lawful zones — and a target for piracy later (rob the bonded shipment and you've stolen from the bond, not the pilot, which changes who comes after you).

### 4.5 Actors

#### 4.5.1 Actor Taxonomy

All actors share a base model. Specialisation is a matter of which contracts they post, which they fulfil, and what personality weights they carry.

| Actor Type | Posts | Fulfils | Owns | Notes |
|---|---|---|---|---|
| Producer (Conjurer) | Sell contracts | Supply contracts for raw materials | Production facility | Credit siphon via production costs |
| Consumer (Eater) | Buy contracts | Nothing | Nothing | Credit faucet via abstract income |
| Shipping Company | — initially | Haul contracts | Fleet of ships | Accumulates capital, expands fleet |
| Independent Pilot | — initially | Haul contracts | One ship | Player archetype, scrappier than companies |
| Factory | Sell (processed goods) | Buy (raw materials) | Production facility | Transforms goods, adds value (later slice) |
| Government | Law-enforcement contracts | Tax collection | Police ships | Credit sink, enforces in jurisdiction |
| Pirate | — formal | Interdiction "contracts" | Fast ship(s) | Black-market sell contracts for seized goods (later slice) |
| Bonding Agent *(new)* | Bond contracts | Arbitration | — | Frontier escrow-as-a-service (§4.4) |
| Data Broker *(new)* | Sells Information | — | — | Market reports, danger histories (§4.6) |
| Multinational | Multiple types | Multiple types | Multiple facilities and ships | Contains subsidiary actors with their own personalities |

#### 4.5.2 Actor Personality

Each actor carries a personality vector that weights its decision-making. These are not complex AI — they are multipliers on decision thresholds.

| Parameter | Low-value behaviour | High-value behaviour |
|---|---|---|
| Risk Tolerance | Only safe routes, low-margin safe contracts | Dangerous routes, thin margins for high upside |
| Liquidity Preference | Spends freely above a minimum threshold | Maintains a large credit buffer, slow to commit |
| Quality Bias | Cheap ships, own repair facility | Pays premium for reliable equipment, outsources maintenance |
| Growth Ambition | Consolidates, defends position | Expands aggressively, accepts short-term loss for share |
| Trust Threshold | Deals with anyone, high breach tolerance | Only high-reputation counterparties |
| Danger Rating | N/A for most actors | Pirates/enforcement — how much others fear crossing them |

A multinational has a parent personality that sets boundary conditions, within which subsidiaries have their own personalities. Internal incoherence (the shipping division takes risks the insurance division hates) is a feature.

#### 4.5.3 The Actor Decision Algorithm  *(specified in v0.2)*

"The world feels alive" lives or dies here, and v0.1 left it a black box. The design is deliberately simple — emergent complexity comes from *many* simple agents interacting (the DF-inspired pillar), not from clever individual AI.

- **Scheduling (hybrid, event-driven first).** Actors do **not** all "think" every tick — that doesn't scale and isn't necessary. An actor re-evaluates when a *relevant trigger* fires: a new contract posted in its region or for its good, one of its ships freed up, escrow released, a deadline approaching. On top of that, a low-frequency **heartbeat** (every N ticks, staggered across actors) catches slow drifts — a producer deciding to raise output, a company deciding to expand. This keeps the simulation cheap (no global per-tick scan) while staying responsive.
- **Decision (greedy utility with personality bias).** When an actor evaluates, it scores a candidate action set — *post contract, accept an open contract, counter an offer, idle, expand* — with a utility function:

  `utility(action) = expected_credit_delta × risk_adjustment(RiskTolerance) × personality_weights − opportunity_cost`

  The actor takes the `argmax` *if* it clears a commit threshold gated by `Liquidity Preference` (a cautious actor needs a clearly good deal to part with cash). No planning, no search, no lookahead — greedy, legible, and debuggable. This is intentional: a transparent decision rule is one a developer can reason about and a player can *learn to predict*, which is the whole point of §1.2.

### 4.6 Information & Visibility  *(specified + extended in v0.2)*

v0.1 said "a player who reads the signals has an advantage" but never said how the player *sees* the signals — and "contracts visible to eligible counterparties" never defined eligibility. Both are now mechanics, and information becomes a *good*.

**Default visibility.** An actor sees:
- Contracts posted **at its current location**.
- Contracts on **routes/regions where it has reputation** (§4.3) — your reach is your reputation.
- **Not** global market prices or producer idle-states. The economy is *legible but not omniscient*; you have to *work* to know.

**Acquiring information** (four mechanisms, increasing cost/effort):
1. **Be there** — the local board, free.
2. **Scout** — visit a location or fly a route and its current state is revealed to you for a while. Exploration has informational value even with the map already drawn.
3. **Buy reports** *▸ New in v0.2* — a **Data Broker** actor sells **Information** as a good: price snapshots, route danger histories, idle-producer indices. Information is tradeable, perishable (stale data is worth less), and routed through the *same contract primitive as cargo*. This is the cleanest possible expression of the thesis — and it gives the "read the signals" fantasy a literal supply chain. It also creates a non-combat, non-hauling career: the analyst who never moves cargo but sells what's about to happen.
4. **Reputation network** — high reputation in a region passively surfaces more local intel there. Belonging pays.

This turns price-reading from a hope into the game's core skill loop, and makes information asymmetry a *resource the player can buy, earn, or sell.*

### 4.7 Locations, Routes & the Simulation

#### 4.7.1 Locations

A location is a node in the route graph with a list of resident actors. **Production and consumption at a location are decoupled** — a farming planet that receives luxury goods does not produce more food as a result. Each actor at a location operates independently. *(Deliberate v0.1 choice, retained. Note: §7.7 adds a consequence for chronic **under**-supply — colonies can wither or bloom — without recoupling production to throughput.)*

| Location Type | Typical residents | Economic character |
|---|---|---|
| Core World | Consumers, luxury producers, government, multinationals | Deep Accord, high-value contracts, competitive |
| Industrial World | Parts manufacturers, ore consumers, shipping companies | Mid Accord, bulk goods, steady volume |
| Agricultural World | Food producers, basic consumers | Accord edge, perishable-goods pressure |
| Mining Colony | Ore producers, fuel producers, independent pilots | Low law, dangerous routes nearby, high variance |
| Frontier Outpost | Independent pilots, pirates, bonding agents, fences | Outside Accord, reputation/bond only, exotic-material access |
| Refinery Station | Fuel producers, FTL-component manufacturers | Mid law, strategic chokepoint potential |

#### 4.7.2 Routes

Routes are weighted edges in the location graph. Each route has:
- **Distance in ticks** (travel time at standard speed, adjusted by ship-speed multiplier).
- **Danger rating** (drives Transit Incidents §6.3, insurance pricing, the Armed-Freighter premium).
- **Law coverage** (the enforcement context — i.e. Accord reach — for contracts signed or breached on this route).

Routes need not be fully connected. Some locations are reachable only via specific paths. **Controlling or disrupting a chokepoint route has emergent strategic value** (§B, blockade).

#### 4.7.3 The Discrete Event Engine

The game runs as a discrete event simulation. There is no continuous time, no position tracking, no physics. The world is a priority queue of future events ordered by game tick. The simulation processes the next event, which may schedule further events, and advances the clock. **Nothing happens between events.** A ship in transit has no position until something forces a calculation. Between `ShipDeparted` and `ShipArrived`, the ship simply does not exist in space — it is a scheduled arrival.

**Core event types:**

| Event | Scheduled by | May schedule |
|---|---|---|
| `ContractPosted` | Actor decision loop | `NegotiationOpened` |
| `ContractAccepted` | Negotiation resolution | `ShipDeparted`, `EscrowLocked` |
| `ShipDeparted` | `ContractAccepted` | `TransitIncidentCheck` (§6.3) / `InterdictionCheck` (later), `ShipArrived` |
| `TransitIncidentCheck` | `ShipDeparted` (probabilistic, danger-weighted) | `TransitIncidentOccurred` or nothing |
| `TransitIncidentOccurred` | the check | delay / cargo damage / loss / cost; `ActorResponseRequired` if a choice arises |
| `InterdictionCheck` *(later slice)* | `ShipDeparted` | `InterdictionOccurred` or nothing |
| `InterdictionOccurred` *(later)* | the check | `CargoSeized`, `CourseChanged`, `ActorResponseRequired` |
| `CourseChanged` | Actor interruption (incl. player) | new `ShipDeparted` from interpolated position |
| `ShipArrived` | `ShipDeparted` / `CourseChanged` | `ContractFulfilled` or `ContractBreached` |
| `ContractFulfilled` | `ShipArrived` | `EscrowReleased`, `ReputationUpdated`, `CascadeCheck` |
| `ContractBreached` | Deadline exceeded or actor default | `EscrowDisputed`/`EscrowForfeited`, `ReputationUpdated`, `CascadeCheck` |
| `CascadeCheck` | `ContractFulfilled`/`Breached` | dependent-contract clause triggers (§4.1.5) |
| `InflationCheck` | Scheduled interval | `FaucetAdjusted`, `SinkAdjusted` |

**Player as actor.** The player is an actor whose decisions come from a human rather than a personality vector. The simulation does not distinguish them. When the player must decide, the engine surfaces the `ActorResponseRequired` event as a UI prompt and **hard-pauses** (§3.4) until the player responds; the response schedules further events exactly as an AI response would. A player can interrupt a ship mid-route: this fires `CourseChanged` at the current tick, and **position is interpolated on demand** — departed tick 1000, arrives tick 1200, interrupted at 1100 ⇒ halfway; the interpolated point becomes the new departure. Position is *only ever* calculated at interruption moments.

**Piracy as events** *(later slice; see the Transit Incident bridge, §6.3).* A pirate actor scans routes for ships in transit and schedules an interdiction. If the target has a scanner, a `DetectionCheck` may fire first, granting a `CourseChanged` opportunity. Outcomes resolve through the event system — no chase, no real-time combat, no position tracking. **Piracy is just an actor with a different contract model:** its income is seized cargo sold through black-market supply contracts; its personality vector sets how aggressive, targeted, and retaliatory it is.

---

## 5. Technical Architecture

### 5.1 Stack

| Layer | Technology | Notes |
|---|---|---|
| Language | C# (.NET) | |
| Event sourcing / aggregates | Qowaiv + Marten | Typed IDs, value objects |
| Database | PostgreSQL | Via Marten |
| Simulation loop | Custom discrete-event queue | Min-heap ordered by game tick |
| Determinism | Seeded PRNG, draws journaled (§5.5) | Required for replay |
| UI | Character / ASCII-forward (§5.7) | Must work without art dependencies |

### 5.2 Core Aggregates

- **Ship** — long-running, semantic snapshots.
- **Contract** — short-lived, live aggregation (no snapshot needed).
- **Actor** — long-running, semantic snapshots.
- **Location** — slow-changing, interval snapshots.

### 5.3 Snapshotting Strategy

Full replay on every load is not viable for long-running aggregates; Marten supports snapshotting natively. The strategy is **semantic** — snapshot at meaningful chapter boundaries rather than on a fixed event count or interval.

| Aggregate | Snapshot trigger | Rationale |
|---|---|---|
| Ship | `ContractCompleted`, `RepairPerformed`, `OwnershipTransferred` | Natural chapter boundaries in a ship's life |
| Actor | Session end, major capital event (company founded, bankruptcy) | State changes meaningfully at these moments |
| Location | Interval (every N ticks) | Locations change slowly |
| Contract | None — live aggregation | Short-lived, few events |

Marten allows conditional projection logic to snapshot on event type; Ship and Actor use this for the semantic triggers above. Between snapshots, only events since the last snapshot are replayed on load.

### 5.4 Simulation Architecture

The event queue is a min-heap ordered by game tick. Each event implements `Process(SimulationContext)`, which may enqueue further events.

```csharp
while (queue.HasEvents && running)
{
    var next = queue.Dequeue();
    currentTick = next.Tick;
    next.Process(context);     // may enqueue further events
}
```

"Running" advances by dequeuing as wall-clock time elapses at the player's chosen speed (§3.4). When an `ActorResponseRequired` event addressed to the player is dequeued, the loop **hard-pauses** and hands control to the UI; all other events process without interruption.

**Future — parallelism.** Events with no shared state (ships on different routes, contracts in different jurisdictions) could process concurrently; the event-sourcing model makes this tractable since each aggregate's stream is independent. Note the tension with the pause boundary (§3.4) — parallel workers must quiesce on a player hard-pause. Deferred.

### 5.5 Determinism & Seeded RNG  *(specified in v0.2)*

An event-sourced simulation with *hidden* randomness is not actually replayable — and replay is the thing event sourcing exists to give you. Every stochastic draw (`TransitIncidentCheck`, `InterdictionCheck`, ship-variance rolls, negotiation jitter) draws from a **seeded PRNG** whose seed is fixed at world creation and whose draw **results are journaled into the event stream** (or stored with enough state to re-derive). Consequences:

- **Deterministic replay** — replaying the log from the seed reproduces *identical* state. This is the only sane way to debug an emergent sim, and the basis of the replay invariant (§5.6).
- **Save/load correctness** — a save is the log + snapshots; load is replay; they must converge.
- **Multiplayer-ready** — a server-authoritative sim replays identically for all clients (§7.4).

### 5.6 Testing & Simulation Invariants  *(new in v0.2)*

You can't develop or tune an emergent economy by playing it; you develop it by **asserting invariants** and **running it headless at scale.** Given your unit-testing discipline, this is the most important addition to the technical plan.

**Invariants** (checked continuously in debug builds; asserted in property-based tests):
- **Credit conservation** — `total_credits == initial + Σfaucet − Σsink` at every tick. No credits are ever conjured or destroyed except via faucet/sink events.
- **Escrow reconciliation** — `Σ escrow_held == Σ locked − Σ released − Σ forfeited`; escrow is never negative.
- **No orphan contracts** — every `Active` contract has either a scheduled `ShipArrived` or a breach path; nothing can get stuck.
- **Cargo conservation** — goods seized == goods removed from the origin owner == goods available to the fence.
- **Deterministic replay** — replaying the log yields a projection whose hash matches the live state.

**Test strategy:**
- **Property-based tests** (FsCheck) generate random actor/contract populations and assert the invariants hold over N-tick runs.
- **Soak runs** — run the sim **headless for millions of ticks with no player** and assert the economy neither collapses (mass bankruptcy, deflationary spiral) nor runs away (hyperinflation, monopoly lock-in). **This is also the primary balance-tuning instrument** — prices, faucet/sink rates, ship costs, and danger weights are all tuned by reading soak telemetry, not by hand. The economy is a thing you *observe and adjust*, like a real one.

### 5.7 UI & Information Architecture  *(specified in v0.2)*

For a sim this information-dense, **the UI is the game** — and v0.1 had "TBD." A character/ASCII-forward presentation is the right aesthetic and removes art-pipeline risk, but the *information architecture* still has to be designed. The core screens:

- **The Board** — the contract list visible to you (§4.6), filterable/sortable by good, value, danger, deadline, jurisdiction. The primary screen. Each row carries its enforcement context and a danger flag at a glance.
- **The Map** — the location graph as a node/edge view: routes labelled by distance, annotated with law coverage and known prices. Route-plotting happens here. **Danger is two channels, not one** *(v0.2 correction — see §6.3.1)*: a static **geographic** danger rating (the baseline hazard the Transit Incident table reads, slow-changing, the thing colour-coding the route is *for*) and, once Slice 3 lands, a separate **predation** overlay (live, decays and spikes, reflects actor activity) shown as a distinct indicator on the same route rather than folded into one number. The two must stay visually distinguishable indefinitely, not just during onboarding — a single merged "danger colour" silently re-teaches the static read every time the player glances at the map, regardless of what any tutorial beat established.
- **The Ship Sheet** — a ship's stats *and its event-log history* in one view: "Cargo 115 (↓ from 120 — hull-brace repair, tick 8420)." The §1.1 pillar made visible. Players learn to read histories before buying.
- **The Ledger** — your credits, escrow, active contracts, reputations-by-context (§4.3), and standing contracts (§4.1.7). Your business, by exception.
- **The Negotiation View** — the current offer, the soft tells (§4.1.6), and accept/counter/walk.
- **Alerts & speed control** — the time-control bar (§3.4) and the player-set pause triggers ("pause when…").

A captain's-log reading of the player's own event stream ties §3.5 (legacy) and §1.1 (every object tells a story) together: your history is *literally* the log.

### 5.8 Economy Monitor

A scheduled `InflationCheck` fires every N ticks, reads aggregate credit supply from a Marten projection, and compares to baseline. If drift exceeds threshold, it fires `FaucetAdjusted`/`SinkAdjusted` events that nudge multipliers on consumer income and production costs. These are themselves events in the log — the economy's interventions are **auditable.** (See the §4.2.1 note on shrinking the monitor's role over time in favour of organic regulators.)

---

## 6. Vertical-Slice Scope

The vertical slice is the smallest version of the game worth playing for an hour. It must run, feel alive, and have *real stakes* — so v0.2 corrects the one place v0.1's scope made the slice toothless (danger ratings).

### 6.1 In Scope

| Feature | Why it's in scope |
|---|---|
| Contract system (haul only) + clause schema | The core primitive; clauses needed for penalties/deadlines even in the slice |
| Negotiation (offer/counter/accept) with tells (§4.1.6) | The central verb — minimal but real |
| 6–7 goods (no exotics), perishability via quality decay | Enough variety for routing decisions |
| 5 ship types (canonical stats, no variance) | Covers all niches; special abilities now *mean* something via §6.3 |
| 3–5 location types, one sector | Enough for meaningful routing |
| Producers + consumers with faucet/sink | The economy must work or nothing else does |
| AI shipping companies + independent pilots | The world must feel alive without the player |
| Actor decision algorithm (§4.5.3) | The mechanism that makes "alive" true |
| Route graph with danger ratings | Required for risk/reward — **now consumed by §6.3** |
| **Transit Incident risk (§6.3)** | **NEW: gives danger ratings teeth without full piracy** |
| Discrete event simulation loop + seeded RNG (§5.5) | The whole game runs on this; RNG must be replayable |
| Contextual reputation system (§4.3) | Minimum frontier enforcement + visibility gate |
| Information visibility model (§4.6, scout + local board) | So price-reading is an actual skill in the slice |
| Inflation monitor (simple) | Prevents the economy breaking in the first hour |
| Invariant checks + soak harness (§5.6) | The only way to tune and trust the economy |

*(Data-broker reports, bonding agents, and standing contracts are **architecturally enabled** by the above but their actors/UX can land in Slice 1 — see §8 — if slice budget is tight.)*

### 6.2 Explicitly Deferred

| Feature | Why it's deferred |
|---|---|
| Ship component variance | Architecture supports it; generation logic is Slice 2 |
| Ship degradation & maintenance contracts | Requires the component system first |
| Full piracy actors | The Transit-Incident abstraction (§6.3) covers risk for the slice |
| FTL travel & exotic materials | Setting flavour now, mechanic later |
| Alien factions | Full-vision content |
| Factory actors (good transformation) | Eaters and conjurers suffice for now |
| Government beyond tax sink | Flat transaction tax is enough for the slice |
| Multiplayer | Single-player until the simulation is proven |
| Insurance contracts | Clause system exists; insurance-as-product is Slice 2/3 |
| Cartography as profession | Requires the FTL layer |
| Named actor personalities with story arcs | Personality vectors yes, scripted arcs no — for now |

### 6.3 The Transit Incident — resolving the danger-rating contradiction  *▸ Resolved in v0.2*

**The problem:** v0.1 placed danger ratings *in* the slice ("required for meaningful risk/reward") but deferred interdiction, the only mechanic consuming them. Danger ratings did nothing. A risk dial with no downside isn't a dial.

**The fix:** a route's danger rating drives a probabilistic **`TransitIncidentCheck`** scheduled on `ShipDeparted` — *the very same hook `InterdictionCheck` will use later.* When it fires, `TransitIncidentOccurred` resolves abstractly against a small table — *minor delay · cargo damage · partial cargo loss · credit cost* — weighted by danger rating and **modulated by ship type**, which finally gives the v0.1 "special" stats meaning in the slice:

- Armed Freighter → interdiction-resistance reduces severity.
- Fast Courier → fragile hull worsens damage outcomes (speed for safety).
- Light Freighter → "runs hot above 80% speed" becomes a *real choice*: push speed to beat a deadline and raise incident odds, or play safe and risk the penalty clause.

No pirate actor, no chase, no position tracking — fully consistent with the discrete-event model. **And it generalises perfectly:** when the piracy slice lands (§8, Slice 3), pirate actors become *one source* of incidents and the abstract table is replaced by actor-driven interdiction with counterplay (detection, rerouting, escorts). The deferred system becomes an *elaboration* of a thing the player already understands, not a new mechanic bolted on. This is the single most important scope fix in v0.2.

#### 6.3.1 Why the generalisation isn't free, and the Slice 3 onboarding it requires  *▸ New in v0.2*

The transfer from Transit Incident to interdiction is mostly clean, but not uniformly. Split what the player learns into two claims. **Spatial:** some routes are physically hazardous (black-hole margins, unstable phenomena) — this stays true under predation and transfers without friction. **Temporal:** the hazard rate is a *stable property* of the route — this is what predation violates, because a pirate's target selection (§4.5.3) specifically looks for low-traffic, historically-quiet lanes. The exact heuristic Slice 0 trains — *quiet lane, safe lane* — is the one Slice 3's adversary is built to punish. That's mild negative transfer on the dimension the player was most confident in, not a neutral gap.

Two design consequences, both binding on Slice 3 onboarding (the counterpart to §3.3, currently missing from the doc):

- **Two sequenced beats, in this order.** An *existence* beat first — an interdiction visibly attributable to a named pirate actor's decision (their personality vector, their target selection), so the player registers "an agent did this" rather than "the table rolled badly again." This can land on an already-amber route at low stakes; its only job is to make the adversary visible. Then, separately and later, a *falsification* beat — an interdiction on a route the player's own history says is quiet, attributed to that pirate having been displaced onto a softer lane by enforcement pressure elsewhere. This is what actually overwrites the static-danger heuristic, because an amber-route hit is consistent with the old model and teaches nothing; a green-route ambush is the only event that forces revision. Do not collapse these into one event — stacking "this is what interdiction is" and "and your danger model is wrong" into a single moment reads as unfair rather than instructive.
- **The map must keep the two channels visually separate after the beats land, not just during them** (§5.7) — otherwise the interface re-teaches the static read at far higher frequency than either beat corrected it, and players keep treating Slice 3 incidents as re-rolls of the old table.

The bottom rung to plant deliberately: static danger → an adversary exists → the adversary relocates under enforcement pressure → the player can predict, avoid, or induce that relocation (e.g. by funding police contracts to push pirates off a lane they rely on). Plant the first two beats on purpose; leave the predictive/manipulative layer emergent — that's where the §1.1 "player and actor are the same type" pillar wants it.

---

## 7. Future Considerations

Designed extensions to the systems above, deferred only because the slice must be proven first.

### 7.1 Ship Variance

Every ship instance rolls stats at manufacture time within a range for its type. A `RepairPerformed` event may alter stats with a reason. The support-beam hauler is just a ship whose event log contains a repair that reduced cargo capacity. The UI surfaces this history (§5.7). Players learn to read ship histories before buying.

### 7.2 Piracy Ecosystem

Pirate actors with scanners. Interdiction as a scheduled event with counterplay (detection, rerouting, escorts). Black-market fence actors. Piracy pressure responds to enforcement — if police contracts become lucrative, more enforcement actors appear and pirates move to softer targets. No scripting. (Generalises the §6.3 Transit Incident — but see §6.3.1: this generalisation needs a deliberate two-beat onboarding sequence and a permanent dual-channel map representation, not just new mechanics dropped in.)

### 7.3 Full Factory Chain

Factories consume raw goods and produce processed goods via internal transformation contracts. Input quality affects output quality — high-grade ore yields higher-quality parts. **This is where component variance originates**, and where the goods list gains supply-chain *depth* (ore → parts → manufactured goods → luxury), which the slice deliberately lacks.

### 7.4 Multiplayer

The simulation runs identically (guaranteed by §5.5). Human players are actors whose response events arrive from the network rather than an AI loop. The negotiation system is already designed for this — a human counteroffer is structurally identical to an AI one (§4.1.6). The main engineering question is latency tolerance in the event queue and reconciling the §3.4 hard-pause across players (likely: no global pause in MP; the world runs, and a non-responding player's actor defaults or auto-declines).

### 7.5 Alien Factions

Alien factions are actors with personality vectors but no contract system. Their equivalent of contracts is **threshold events**: when human expansion into a region crosses a trigger point, the faction's behaviour shifts. Emergent political pressure without scripted storyline. Because the aliens are **non-signatories to the Accord** (§2.3), the pressure expresses itself through systems the player already uses — rerouting around restricted regions, re-pricing insurance, lobbying government actors, deciding whether the Accord should expand to meet them.

### 7.6 Cartography

FTL-lane discovery is a profession. An explorer who maps a new stable route owns that knowledge and can sell it, keep it, or post a route-access contract. Unknown routes exist in the graph but are not traversable until mapped. First-mover advantage is significant and **temporary** — once a route is known, competitors can use it. (Note the natural synergy with the Data Broker §4.6: a mapped route is *information*, and information is already a good.)

### 7.7 Demand Starvation — consequences for unmet demand  *▸ New in v0.2*

In v0.1, unmet demand only moves *prices.* That makes the world strangely consequence-free: you can ignore the frontier forever and nothing happens but a number rising. Optional layer (Slice 1-adjacent):

A location whose consumers go **chronically under-supplied** past a threshold suffers a *visible, escalating* consequence — prices spike first, then consumer attrition (eaters reduce consumption or leave), then in extremis a **downgrade** (a thriving outpost slides to precarious). Conversely, reliably well-supplied locations can **grow** (more residents, bigger market). This keeps production decoupled from throughput (per the §4.7.1 design) but couples *survival* to supply — which gives the player's routing genuine **moral and economic stakes**: neglect a frontier colony and watch it wither; keep it alive single-handed and watch it bloom into a better market that remembers you (reputation, §4.3). Pure emergence, zero scripting, and it directly serves the §1.2 goal of a world that visibly *reacts* to the player without being *about* them.

### 7.8 Bankruptcy & Asset Liquidation  *▸ New in v0.2*

When an actor's credits hit zero and obligations exceed assets, a **`Bankruptcy`** event fires: the actor's ships and facilities are posted as **fire-sale** procurement/sale contracts (cheap), debts are written down, and the actor either dissolves or restructures. Three payoffs:

1. **Organic money-supply regulation** — assets are *redistributed*, not conjured, which lets the inflation monitor (§4.2.1, §5.8) shrink toward a last-resort circuit breaker. The economy self-corrects through *failure*, like a real one.
2. **Opportunity** — the player (or a rival) buys a dead company's Bulk Hauler at a discount. Other actors' failure is your supply of cheap capital.
3. **Consequence with weight** — a competitor really *can* go under, and so can *you* (§3.5). The §1.1 "meaningful destruction → meaningful creation" pillar, applied to companies instead of ships.

### 7.9 Insurance as the Clause System's Showcase  *(expanded in v0.2)*

When insurance lands (Slice 2/3), it is the thesis made flesh — and it requires **no special-case code**:

1. A haul contract carries an **`InsuranceRider`** clause (§4.1.5) naming an insurer actor; the insurer holds a premium in escrow.
2. On a `TransitIncident`/`Interdiction` event affecting that haul, the rider's Trigger fires and its Effect **posts a `ClaimContract`** against the insurer.
3. The insurer **pays out** from escrow, then **re-prices** future premiums by reading the route's danger history from a Marten projection.

Premiums, claims, payouts, and re-pricing are *all contracts and events.* This is the single best proof that "everything is a contract" is architecture, not slogan — and it's why the clause schema (§4.1.5) was worth defining now even though the product ships later.

---

## 8. Sliced Roadmap  *▸ New in v0.2*

Replacing v0.1's vague "milestone 2." Each entry is a **vertical slice** — a thin end-to-end cut through data, simulation, and UI that is shippable and *playable* on its own — in keeping with vertical-slice discipline. Order is by dependency and by how much each adds to the core fantasy per unit of risk.

| Slice | Theme | Cuts in | Depends on | The new fantasy it unlocks |
|---|---|---|---|---|
| **0** | **Core loop** | Haul contracts, 5 ships, one sector, faucet/sink, AI pilots/companies, reputation, Transit Incidents, negotiation, seeded RNG, invariants | — | *"I found an edge in a living economy."* |
| **1** | **Institutions & information** | Data Broker + Information good, Bonding Agents, Standing contracts, Demand starvation | 0 | *"I read the market, run a logistics business, and the world reacts to me."* |
| **2** | **Wear & variance** | Ship variance rolls, degradation, maintenance contracts, second-hand market depth, Insurance product | 0–1 | *"Every ship has a history I can read and price."* |
| **3** | **Piracy & enforcement** | Pirate actors generalising Transit Incidents (existence + falsification onboarding beats, §6.3.1), fences, escorts, police contracts, blockades/chokepoint control | 1–2 | *"The frontier is dangerous and I choose how to face it."* |
| **4** | **Production depth** | Factory actors, transformation contracts, multi-tier supply chains, quality grades | 2 | *"I'm a link in a real supply chain, not just a courier."* |
| **5** | **FTL frontier** | Exotic materials, FTL routes & risk, cartography-as-profession, route-access contracts | 3–4 | *"I open the map and own what I discover."* |
| **6** | **The Accord & the aliens** | Alien threshold actors, Accord sanction/extension politics, diplomatic contract-equivalents | 5 | *"My economy has consequences at the scale of civilisations."* |
| **7** | **Multiplayer** | Network actors, server-authoritative replay, MP pause model | 0 (architecturally), realistically 2+ | *"The other operators are people."* |

Bankruptcy & asset liquidation (§7.8) is small and high-value; fold it into whichever of Slice 0/1 has budget — it pays for itself immediately in economic robustness.

---

## Appendix A: Glossary

| Term | Definition |
|---|---|
| Accord (Galactic) | *(new)* The multilateral treaty body defining the standard contract format, escrow protocol, and arbitration courts. Its geographic reach *is* the enforcement-context table. The title's referent. |
| Accord Standing | *(new)* A game-wide reputation context: your record under the Accord standard, gating access to core/mid (Accord) contracts. |
| Actor | Any entity in the simulation that can post or fulfil contracts — players, AI companies, governments, pirates, bonding agents, data brokers. |
| Aggregate | An event-sourced domain object whose state is the projection of its event history. |
| Bonding Agent | *(new)* A third-party actor who, for a fee, holds escrow and arbitrates a frontier deal — escrow-as-a-service outside the Accord. |
| Cascade | A chain of contract events triggered by a breach or delay in a dependent contract, propagated by clauses. |
| Conjurer | Informal term for a producer actor — generates goods for sale. |
| Contract Clause | A typed, evaluable term (Trigger + Effect) attached to a contract: warranty, penalty, escape hatch, insurance rider, bond requirement, standing renewal. |
| Data Broker | *(new)* An actor who sells Information (price snapshots, danger histories, idle-producer indices) as a tradeable, perishable good. |
| Demand Starvation | *(new)* The escalating consequence of chronic under-supply at a location: price spike → consumer attrition → downgrade. |
| Discrete Event Simulation | A model in which state changes occur only at discrete points in time, driven by an event queue. |
| Eater | Informal term for a consumer actor — purchases and consumes goods. |
| Enforcement Context | The combination of jurisdiction (Accord reach) and counterparty reputation that determines how a breach is handled. |
| Faucet | A credit injection mechanism. In the slice, consumer income is the primary faucet. |
| Information | *(new)* Market and route data treated as a good — buyable, sellable, and perishable (stale data is worth less). |
| Interdiction | An event in which a pirate actor interrupts a ship in transit and demands goods, payment, or both. (Later slice; generalises the Transit Incident.) |
| Legacy | *(new)* The narrative the game renders from the player's own event log on retirement — markets moved, companies made or broken, colonies saved or starved. |
| Personality Vector | Numeric weights biasing an actor's decisions: risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, danger rating. |
| Reservation Price | *(new)* An actor's hidden walk-away price in a negotiation, derived from its utility function and personality. |
| Reputation (contextual) | A bounded score keyed by `(actor, context)` — region, good-category, or counterparty — that gates contract visibility and trust. Decays toward neutral. |
| Semantic Snapshot | An aggregate snapshot triggered by a meaningful domain event rather than a fixed interval or count. |
| Sink | A credit destruction mechanism — production costs, running costs, docking fees, taxes. |
| Soak Run | *(new)* A headless, player-free multi-million-tick simulation used to assert economic stability and tune balance. |
| Standing Contract | *(new)* A recurring contract that re-posts from a template (via a `StandingRenewal` clause) until cancelled. |
| Tick | The unit of game time. All scheduling is in ticks. Ticks-per-second is configurable (default ≈ 1 tick/second at 1× speed); the player controls speed and the sim hard-pauses on decisions (§3.4). |
| Transit Incident | *(new)* An abstract, danger-weighted route risk event (delay / damage / loss / cost) that gives danger ratings teeth in the slice and generalises into full interdiction later. |

---

## Appendix B: Idea Bank  *▸ New in v0.2 — unscheduled proposals for evaluation*

Loose ideas consistent with the design's logic, not yet placed in a slice. Keep, cut, or promote as you like.

- **Chokepoint control / blockade.** An actor (or the player) stations escorts or pirates on a route and effectively taxes or denies passage — turning the §4.7.2 "controlling a chokepoint has strategic value" line into a named mechanic. A standing-contract trade lane (§4.1.7) is precisely the thing worth blockading. Natural fit for Slice 3.
- **Contract syndication / sub-contracting.** A shipping company that wins a haul it can't fulfil in time *sub-contracts* part of it to the player — a *positive* cascade. The player's first taste of being trusted by an institution. Already expressible with the existing primitive; just needs surfacing.
- **Reputation as an introduction graph.** High reputation with Actor A passively *introduces* you to A's trusted partners (their contracts become visible to you). Belonging compounds. Ties §4.3 to §4.6.
- **Goods quality grades pre-factory.** Even before the factory chain, goods could carry a grade affecting value and consumer preference — seeding the variance theme on the *goods* side, and giving the Data Broker something extra to sell (where the high-grade ore is).
- **Refinery fire / facility loss as a rare emergent event.** A `Damaged`/`Destroyed` event on a *facility* (not just a ship) causes a regional shortage that ripples through prices and contracts — a "news event" that is fully consistent with the sim rather than scripted. Pairs beautifully with insurance (§7.9) and information (§4.6): the broker who knew first profits.
- **The analyst career.** A player who *never moves cargo* — buys cheap information, sells it dear, brokers deals, and bankrolls bonded shipments. The §4.6 information layer plus §4.4 bonding makes this a viable, distinct way to play. Worth protecting as a design goal: the game should be *playable without ever hauling.*
- **Diegetic difficulty via starting archetype.** The four archetypes (§3.x of v0.1) double as difficulty settings without a difficulty menu: Independent Pilot is the default; Corporate Lackey is "easy mode with a leash" (steady internal contracts, low margins, low risk); Newbie Pirate is "hard mode" (no Accord standing, frontier-only, high risk/reward); Factory Owner is "economic mode" (managing both sides of the ledger from turn one).
