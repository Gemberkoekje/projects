# BY GALACTIC ACCORD

**Game Design Document · v0.3** · working title · 2026-10-25

> *A space economy where everything is a contract, and the player is just another party to them.*

## How to read this document

- **Part I — The game** describes the full vision. It should change slowly.
- **Part II — Slice 1** is the spec for the first playable build. It stands on its own: if you read one part before writing code, read this one.
- **Part III — Technical design** covers the engine, the client, determinism and testing.
- **Part IV — Roadmap** orders the slices after Slice 1.
- **Appendix A — Decisions** records what was decided, when and why, including everything carried over from v0.1 and v0.2, plus the questions still open. The body states decisions plainly; the appendix holds the history.
- Appendices B and C hold the glossary and the idea bank.

Numbers are first-pass and get tuned with headless soak runs (§20). Where a number is a *design target* rather than a tuning value, it says so.

---

# Part I — The game

## 1. Vision

By Galactic Accord is a space economy sandbox in which **every economic interaction is a contract**. Buying food, moving it, storing it, building a ship, hiring a pilot, insuring a cargo: each is a contract with parties, terms, and a defined outcome for when it goes wrong.

The universe does not exist for the player. It runs whether you act or not, driven by actors pursuing their own goals through the same contracts you use. The player is one operator among many, and can become any kind of operator the world contains.

### 1.1 Pillars

1. **Everything is a contract.** There is no shop, no quest giver, no crafting menu. If something changes hands, moves, or gets made, a contract says who, what, where, when and for how much, and what happens if it doesn't.
2. **The player and the AI are the same kind of actor.** Anything an AI actor can do, the player can do, and the other way round. AI actors post contracts, haggle, cancel, order ships and go bankrupt; so can you. No player-only shortcuts, no AI-only cheats. Not every capability ships in the first slice, but none is ever designed as player-only or AI-only.
3. **Failure is content.** Contracts fail: deliveries run late, arrive short or damaged, parts get substituted, buyers default, suppliers fall through. Every contract states its remedies (late fees, cancellation rights, substitution rules, warranties), and those terms decide what happens. This applies to player and AI alike, and a failure upstream ripples through every contract that depended on it.
4. **Things are the sum of their parts.** A ship is its frame, drive and module, and each part is only as good as what its maker could get hold of, again through contracts. A ship with 115 cargo instead of 120 has a substituted frame from a supply shortage, and its history says so. The stat and the story are the same thing.
5. **Simulated cause, not scripted outcome.** Prices, shortages, delays and booms come from actors pursuing goals. Nothing happens because a timer said so.
6. **Legible by construction.** Emergent depth without the opacity tax: every number can explain itself, every outcome can be traced back through the contracts that caused it, and the first hour teaches the game without a wiki.
7. **Meaningful destruction drives creation.** Ships get lost, contracts break, companies fail. That is the demand engine that keeps supply interesting.

### 1.2 Player experience goals

- **The skill is judging contracts.** Is this haul worth running? Is this shipyard's cheap bid a bargain or a trap? Should I post my own contract instead of taking what's on the board? Mastery is being right about a contract before signing it.
- **Off the shelf or built to order.** A stock or second-hand ship is cheaper and available now, but never quite what you need. A custom build fits exactly, and costs more, takes longer, and is only as good as the parts the contractor can source. This is the game's signature decision, and it generalises to everything you acquire.
- **You found an edge.** The world is indifferent to you. Prices move and companies grow and fail whether or not you act. You are a small operator who noticed something first.
- **Risk is a dial you choose.** Thin, safe margins in the Accord core, or the frontier run that pays for a month and might cost you the ship. The game never forces the dial; it makes both honest.
- **Consequence is real and survivable.** A cascade of failures can ruin a quarter. It should sting, and it should be recoverable, until it isn't.

### 1.3 Inspirations

| Game | What we take | What we do differently |
|---|---|---|
| EVE Online | Destruction-driven economy, player-made contracts | An AI-populated universe that works without other players |
| X4 | Living economy, player as one actor among many, real time with pause | Contracts as the interface, not trade menus |
| Dwarf Fortress | Emergent complexity from simple rules | Legible from the first minute |
| Transport and tycoon sims | Supply chains as systems, not set dressing | One contract primitive for everything |
| Mini Metro, DEFCON | Schematic, data-drawn presentation | Applied to a dense economic interface (§11) |

---

## 2. Setting

Humanity has recently achieved faster-than-light travel. The technology is new, imperfect and expensive: its exotic fuel components come from the fringes of explored space, near phenomena that are poorly understood and genuinely dangerous.

The stars were not empty. Several alien civilisations exist, from post-scarcity ancients to curious but isolationist neighbours. None is hostile; none is particularly interested in humanity. That indifference is the setting's slow-burn tension: as human expansion reaches territory they consider theirs in some untranslatable sense, it may curdle into something more complicated.

The game begins early in the expansion era. FTL lanes are mapped but not understood. Colonies range from thriving industrial hubs to precarious outposts. Law is a geographic variable: core systems have courts, frontier docks have reputations.

### 2.1 The Galactic Accord

The **Galactic Accord** is the multilateral human treaty, with a handful of pragmatic alien co-signatories, that defines four things every player touches constantly:

1. **The standard contract.** The structure in §3.1 *is* the Accord standard. "By Galactic Accord" is the phrase stamped on every compliant contract.
2. **Escrow.** Accord clearing houses hold payments and bonds and release them according to the contract's terms.
3. **Arbitration.** Accord courts settle disputes in core space.
4. **The public record.** Outcomes of contracts signed in Accord space are public: anyone can look up an actor's history of delivering, delaying and defaulting.

**Accord reach is geography.** Core and mid-tier systems are inside the Accord; the frontier is outside it. That single idea explains the whole enforcement table (§3.5).

**Accord standing is reputation** (§9). Honour Accord contracts and the Accord trusts you. Breach them flagrantly inside Accord space and you can be sanctioned: locked out of Accord contracts and pushed toward the frontier. The frontier is not just the edge of the map; it is life outside the Accord, with all the freedom and exposure that implies.

The aliens never signed. As humanity expands, whether the Accord can be extended to them, imposed on them, or simply ends at their border becomes the long-term political pressure, expressed through the contract and reputation systems the player already uses (Slice 8, §22).

### 2.2 Future layers: FTL and the aliens

Neither is in Slice 1; both shape the long-term design.

- **FTL as an economic driver.** Drives need exotic materials from dangerous space. Lane cartography is a profession, and a newly mapped stable route is valuable, temporarily, until competitors use it too. A jump that goes wrong is a cascade waiting to happen, and FTL cargo insurance is an industry priced by route history.
- **The alien indifference.** Alien factions are actors with personalities but no contracts. Their equivalent is threshold behaviour: when human expansion into a region crosses a trigger point, they shift: more interference, restricted access, diplomatic equivalents of contracts. The result is emergent political pressure without a scripted storyline.

---

## 3. Contracts

### 3.1 The Accord standard contract

Every contract, of every kind, has the same skeleton:

| Element | Meaning |
|---|---|
| Parties | Who posted it, and who took it (empty while open). |
| Obligation | What must happen: goods handed over, goods moved, a thing built, space provided. Depends on the kind (§3.2). |
| Price | What the paying party pays, and when: on signing, on delivery, or as a deposit plus balance. |
| Schedule | The deadline, and any intermediate dates (pickup window, milestones). |
| Terms | The remedies: late fees, cancellation rights, substitution rules, warranties, bonds (§3.4). |
| Security | Escrow and bonds, where the jurisdiction provides them (§3.5). |
| Jurisdiction | Where it was signed. Decides enforcement (§3.5). |
| Record | The contract's history: posted, haggled, signed, performed, failed, settled, each with its cause. |

### 3.2 Contract kinds

| Kind | Obligation | Usually posted by | Example |
|---|---|---|---|
| **Sale** | Goods or an asset change hands at a named place. | The buyer ("wanted") or the seller ("for sale") | "Wanted: 110 Food delivered to Meridian Habs by Day 9." "For sale: 200 Ore at Rust Belt." "For sale: used Light Freighter at Edge Salvage." |
| **Haul** | Move goods the carrier doesn't own. | Whoever owns the goods | "Move my 60 Food from Verdant to Rust Belt by Day 6." |
| **Build** | Make something to a specification. | Whoever wants the thing | "Light frame, standard drive, refrigeration unit, cargo at least 110, delivered at Tycho Yards by Day 30." |
| **Storage** | Provide space for goods at a location, for a period. | Station operators offer it; anyone takes it | "40 units at Meridian Station, ₵2 per unit per week." |
| Service *(later)* | Do work: fly a ship, run a facility, escort. | Anyone | "Pilot wanted for a Bulk Hauler, 30 days." |
| Insurance *(later)* | Pay out if a named risk occurs. | Insurers | "Cover this haul against total loss." |
| Bond *(later)* | Hold escrow and arbitrate where the Accord doesn't reach. | Bonding agents | "We'll hold payment for your Edge Station deal." |
| Information *(later)* | Deliver a market or route report. | Data brokers | "Prices across the Marches, as of today." |

A Sale names where the goods change hands. If that's the seller's location, the buyer arranges transport; if it's the buyer's location ("delivered"), the seller does. Either way, moving the goods is the responsible party's problem: they can carry it themselves or post a Haul.

Ships are assets and are sold through ordinary Sale contracts. A refit or repair (later) is a Build contract on an existing ship.

### 3.3 Contracts depend on contracts

Most contracts can only be performed by signing others. A shipyard that takes a Build posts Sales to buy parts. A parts maker posts Sales to buy its inputs and Hauls to deliver its output. A trader who takes a delivered Sale buys the goods elsewhere and carries them.

The simulation does not need to know these contracts are related. The actors do, and the terms do. When an upstream contract fails, the actor depending on it must decide what to do about its own downstream obligations: absorb the delay, pay the late fee, substitute, or invoke a remedy. That decision can fail the next contract down. That is a **cascade**: not a scripted event chain, just actors honouring or breaking terms.

The player can sit anywhere in the web: at the top (ordering a ship), in the middle (a trader filling a delivered Sale) or at the bottom (hauling a yard's parts), and sometimes in several places in the same chain, such as hauling the drive for their own ship.

### 3.4 Terms: what happens when it goes wrong

Every contract carries terms that define failure and remedy. They are part of the contract, visible before signing, and negotiable.

| Term | Applies to | What it says |
|---|---|---|
| Deadline and grace | All | When performance is due, and how long after that the contract is late but not yet in default. |
| Late fee | All with a deadline | Paid by the late party per day late, up to a cap (for example 15% of the price). |
| Cancellation for default | All | Once the grace period has passed, the wronged party may cancel: escrow refunded to the payer, the defaulting party's bond forfeited to the wronged party. Until they cancel, the contract continues and the late fee keeps accruing up to its cap. |
| Cancellation at will | All | Either party may walk away by paying a break fee (often the deposit). |
| Supply failure | Build, Sale | If something upstream fails, the performer may propose a remedy: more time plus compensation, a substitute at a discount, or cancellation with a refund plus compensation. The other party chooses. |
| Substitution policy | Build, and Sales of graded goods | **Strict:** no substitutes. **Standard:** one grade lower allowed, at a set discount. **Lenient:** any substitute, priced down pro rata. |
| Quality or spec warranty | Sale, Haul, Build | A minimum quality, grade or spec. Below it, a refund per shortfall; below a floor, the receiver may reject on inspection. |
| Cargo liability | Haul | What the carrier owes for lost or damaged goods, up to a cap. |
| Performance bond | All, in Accord space | A sum the performer locks in escrow, drawn on for late fees and forfeited on default. |
| Partial delivery | Sale, Haul | Accept pro rata, or treat as a failure. |
| Visibility *(later)* | All | **Public:** the contract may make the news, giving both parties free publicity and a reputation boost. **Private (NDA):** nobody else learns of it, so nobody can act on it or undercut it. Until this term exists, every contract is public and may make the next day's news (§7.4). |

Terms come in three ready-made packages, **strict**, **standard** and **lenient**, so posting a contract takes seconds. Stricter terms protect the payer and cost more; lenient terms are cheaper and riskier. Packages are a starting point: a haggle (§3.6) can move individual terms.

**Both sides can fail.** The performer can be late, short, damaged or substituted. The payer can default: on the frontier, with no escrow, a buyer can simply not pay. Every failure produces the same events whether it is the player's or an AI's, and when the player must decide something, the game pauses for it (§8.3).

### 3.5 Enforcement and the reach of the Accord

| Jurisdiction | Security | When a contract breaks |
|---|---|---|
| Core system (deep Accord) | Escrow, bonds, Accord arbitration | Remedies enforced automatically; Accord standing hit; public record |
| Mid-tier colony (Accord edge) | Escrow and bonds, no arbitration | Remedies enforced from escrow; public record |
| Frontier (outside the Accord) | None, unless bonded *(later)* | Only the counterparty's memory of you; no public record |
| Black market *(later)* | None | Danger rises; fence relationships suffer |

On the frontier, terms still exist; nobody enforces them. A frontier buyer who defaults owes you the money exactly as much as an Accord buyer would. The difference is that nobody makes them pay.

### 3.6 Posting, responding and haggling

**Posting.** Anyone can post a contract of any kind. A post is seen by whoever can see it (§7): post at a busy core station and many actors see it quickly; post at a remote outpost and few do. Responses arrive over time, each either an acceptance of the posted terms or a counter-offer. The poster can accept a response, counter it, or withdraw the post.

**Responding.** Anyone who can see an open contract can accept it as posted, which is immediate, or counter.

**Private offers** *(later)*. Instead of posting publicly, a contract can be offered to named actors only, for example asking two yards for a quote on the same build.

**Haggling.** A counter can change the price and a small set of terms: the deadline, the late fee, the term package. Each side has a hidden reservation point derived from its own costs and personality (§4.3). Each round, the side that concedes moves part of the way toward its reservation point. A side accepts when the standing offer crosses that point, and walks away when the gap is too wide or the rounds run out.

**Haggling takes game time.** A counter-offer gets its reply after a few game hours, and the contract stays open to others meanwhile. You cannot haggle while paused, and pushing too hard risks losing the contract to someone who simply accepted. The choice between taking a contract now and haggling for more is part of judging it.

**Tells.** The reply's timing is itself a tell: an eager counterparty answers within the hour; one near its limit takes a day. The wording tells too: "accepts readily", "reluctant", "appears to be at its limit". The hidden numbers stay hidden; reading the counterparty is the skill. A human counter-offer is structurally identical to an AI one, which keeps multiplayer cheap later.

AI-to-AI haggles resolve with a closed-form version of the same rules, so the world stays cheap to simulate.

---

## 4. Actors

### 4.1 Roles are habits, not types

All actors share one model: credits, possessions, reputation, a personality, and the contracts they're party to. A "role" describes which contracts an actor tends to post and take and what it owns. It is not a different kind of entity, and the player can take on any of them.

| Role | Typically posts | Typically takes | Owns |
|---|---|---|---|
| Producer | For-sale Sales; Hauls when it sold delivered | Wanted Sales for its good | A production facility |
| Factory | Wanted Sales for inputs; For-sale Sales for outputs | Wanted Sales for its outputs | A facility that turns inputs into outputs |
| Consumer | Wanted Sales, usually delivered | — | An income source (the faucet, §5.1) |
| Shipyard | Wanted Sales for parts; For-sale Sales for stock ships | Build contracts | A yard |
| Trader / hauler | Hauls when overcommitted; Sales for cargo it bought | Hauls; delivered Sales it can source | Ships |
| Station operator | Storage offers | — | Storage space; docking |
| Government | *(later: enforcement contracts)* | Taxes on Accord contracts | — |
| Pirate *(later)* | Black-market Sales | — (interdiction) | Fast ships |
| Bonding agent *(later)* | Bond contracts | — | — |
| Data broker *(later)* | Information contracts | — | — |

The player starts as an independent pilot with one ship. Long term, nothing stops a player from owning a factory and filling sell orders from it, running a yard, or never flying at all and living off contracts alone.

### 4.2 Personality

Each actor carries a personality: multipliers on decision thresholds, not complex AI.

| Trait | Low | High |
|---|---|---|
| Risk tolerance | Safe routes, strict terms | Dangerous routes, thin margins for upside |
| Liquidity preference | Spends freely above a floor | Keeps a large buffer; slow to commit |
| Quality bias | Cheap parts, lenient terms, accepts substitutes | Pays for grade, insists on strict terms |
| Growth ambition | Consolidates and defends | Expands; accepts short-term loss for share |
| Trust threshold | Deals with anyone | Deals only with actors whose record is good |
| Eagerness | Concedes slowly, replies slowly | Concedes fast, replies fast |
| Danger rating *(later)* | — | How much others fear crossing them (pirates, enforcers) |

A multinational (later) has a parent personality that bounds its subsidiaries'. Internal incoherence, such as a shipping division taking risks its insurance division hates, is a feature.

### 4.3 How actors decide

The design is deliberately simple. Emergent complexity comes from many simple actors interacting, not from clever individuals.

**Scheduling.** Actors don't think every tick. An actor re-evaluates when something relevant happens: a contract it can see appears, one of its ships comes free, a response arrives, a deadline approaches, a contract it depends on fails. A slow, staggered heartbeat catches gradual drifts on top of that: raising output, expanding, selling an idle ship.

**Choosing.** When it evaluates, an actor scores its candidate actions: post a contract, respond to one (accept or counter), invoke a term (cancel, claim, propose a remedy), buy or sell an asset, or do nothing. The score is

`utility(action) = expected credit change × risk adjustment × personality weights − opportunity cost`

and the actor takes the best action if it clears a commit threshold set by its liquidity preference. There is no planning and no lookahead: the rule is greedy, legible and debuggable. The same utility sets the actor's reservation point when it haggles.

**Same information, same rules.** Actors decide only on what they can see (§7), exactly like the player, so an AI trader far from a market reads it as late as you do. The interface shows the player the same inputs the AI weighs (§11.2). A player who understands the rule can predict the AI, and that is the point.

**Old information.** Actors should be aware of how old their information is without being unexploitable. In Slice 1 they act on what they last saw as if it were current, which a sharp player can exploit: a yard bidding on part prices it hasn't seen for a week will misjudge. Later, an actor's risk tolerance decides how far it trusts old information: a risk-tolerant actor bids on week-old prices, a cautious one discounts them or waits for fresher news.

---

## 5. Economy

### 5.1 Money supply

The economy is a closed credit system with designed inflows and outflows.

- **Faucet.** Consumers have an abstract income (wages, colony budgets) that funds their purchases.
- **Sinks.** Production costs, running costs, docking fees, interest on debt, station upkeep, and the Accord tax on contracts signed in its space.

The money supply at world creation is a seeded parameter. After that, total credits always equal initial credits plus faucet minus sinks, an invariant the tests enforce (§20.2).

### 5.2 Prices

There is no price list. A good's price at a location is what contracts there are being signed and asked at: recent deals plus open posts. Behind every contract are reservation points. A producer's comes from its cost, its margin and how full its warehouse is; a consumer's from how short it is and what it can afford. A producer with a full warehouse asks less; a consumer running short pays more. Those are the signals a market reader acts on, if they can see them (§7).

### 5.3 Production

**Producers** make goods from cost alone (a sink) at a steady rate, up to a warehouse cap. When the warehouse is full they idle, which is the classic leading indicator of a falling price.

**Factories** turn inputs into outputs. They post Wanted Sales for inputs and stop when inputs run out. Each facility has a quality level, and output grade equals that level, lowered when the factory works from lower-grade inputs or makes do without one.

**Production is decoupled from throughput.** A farming world that receives luxuries does not grow more food; each actor at a location runs independently.

Slice 1 has factories only for ship parts (§15.4). Long term any good can have a processing chain, such as raw food to food, and any actor, the player included, can own the factory.

### 5.4 Debt and bankruptcy

An actor's credits can go negative: running costs, fees and interest keep charging. Debt carries interest from the Accord clearing house. When debt grows too large relative to the actor's assets, it is **insolvent**: its assets are sold in fire-sale Sale contracts, its open contracts are cancelled for default (with the usual remedies against it), and it dissolves.

For AI actors this redistributes capital organically: a dead company's Bulk Hauler becomes someone's cheap second-hand ship. For the player, insolvency ends the game, and the legacy screen tells the story (§10).

### 5.5 The inflation monitor

A scheduled check compares total credit supply to its baseline and nudges the faucet if it drifts too far. The nudges are events in the log, so the economy's interventions are auditable.

The monitor is a designer's thumb on a simulation that claims to be emergent. It stays as a safety valve, but the aim is to lean on organic regulators instead (bankruptcy redistributing assets, actors' savings absorbing variance, new actors entering when margins are fat) and shrink the monitor to a last-resort circuit breaker.

### 5.6 Demand starvation *(later)*

Unmet demand should have consequences beyond price. A location whose consumers stay under-supplied past a threshold suffers a visible, escalating decline: prices spike, then consumers cut back or leave, then the location downgrades. Reliably supplied locations grow. Production stays decoupled from throughput, but survival becomes coupled to supply, which gives routing moral as well as economic stakes: neglect a frontier colony and watch it wither, or keep it alive and watch it bloom into a market that remembers you.

---

## 6. Ships and parts

### 6.1 Parts

Every ship is built from three parts:

| Part | Determines | Variants (Slice 1) |
|---|---|---|
| Frame | Cargo space, mass, toughness (how badly incidents hurt) | Light, Bulk, Courier |
| Drive | Power (speed = power ÷ mass), reliability (how often incidents happen), whether it runs hot when pushed | Standard, Fast |
| Module | One specialty | Refrigeration unit, Armour plating, Cargo pod, or empty |

Each part has a **grade**: A (as designed), B (a step down) or C (barely to spec). Grade scales the part's main stat; for example, a grade-B Light frame holds 115 instead of 120. A ship's stats are always computed from its parts, never stored separately, so every stat can explain itself.

### 6.2 Designs

A design is a standard combination of parts. The five classic hulls are designs:

| Design | Frame | Drive | Module | Niche |
|---|---|---|---|---|
| Light Freighter | Light | Standard | — | Jack of all trades; runs hot when pushed |
| Bulk Hauler | Bulk | Standard | — | Huge and slow; ore and bulk runs |
| Fast Courier | Courier | Fast | — | Beats deadlines; its thin hull suffers in incidents |
| Refrigerated Transport | Light | Standard | Refrigeration | Keeps perishables fresh |
| Armoured Freighter | Light | Standard | Armour | Shrugs off incidents; for dangerous runs |

Anything else, such as a refrigerated courier or an armoured bulk hauler, is a custom build.

### 6.3 Getting a ship

| Route | Price | Availability | Fit | Risk |
|---|---|---|---|---|
| Stock | The yard's asking price, haggled | Now, if one is on the lot | A standard design, at the grades the yard had | Low |
| Second-hand | The seller's ask, haggled | Now | Whatever exists, with its history | What the history doesn't say; frontier sales have no escrow |
| Custom build | Bid by yards | Weeks | Exactly the spec, if the parts come through | Late, substituted or cancelled, per the terms |

A custom build is a Build contract (§3.2). Yards respond with bids: price, delivery date and terms. A yard's bid comes from what it can see of part prices and availability, its queue and its personality. A cheap bid may be cheap because the yard is efficient, because its information is stale, or because it plans to substitute. Judging that bid is the skill.

Once a build is signed, the yard posts Wanted Sales for the parts. You can watch them on the board, and haul them yourself if you like.

### 6.4 Histories

Every ship carries its history: where it was built, from which parts at which grades, what was substituted and why, its owners, incidents, and the contracts it flew. A second-hand buyer reads it before buying (§11.3).

### 6.5 Running costs

Every ship costs credits per day whether or not it earns. An idle fleet bleeds, which caps fleet size by throughput rather than savings, for AI companies and the player alike.

### 6.6 Later: wear, maintenance and refits

Parts wear with use and incidents. Repair is a Build contract on an existing ship, with all the usual terms, substitution included; this is where the support-beam patch that cost you five units of cargo comes from. A refit swaps one part for another. Insurance can cover a build or a ship.

### 6.7 Later: kinds of shipyard

Slice 1 has two yards that each build one ship at a time. Long term, yards differ in kind as well as character:

- **Boutique yards** build to your specification, a few at a time, like custom yachts for billionaires: high grades, close attention, slow and expensive.
- **Large yards** build a dozen of a standard design at once. Every ship in the batch is very similar but not exactly the same, because each one got whatever parts were on hand that week. Cheap, quick, and a reason to read a ship's history even when it's new.

---

## 7. Information

### 7.1 Tiers by distance

You know what your presence lets you see. How much you know about a place depends on how close it is to that presence: the locations of your ships (for a ship in transit, where it's heading), and anywhere you rent storage or own a facility.

| Tier | Where | You see |
|---|---|---|
| **Here** | A location where you have a presence | Every contract the moment it's posted; live prices and stock levels; who is idle and who is running short; full details on every actor there |
| **Near** | One jump from such a location | Contracts a few hours after posting; prices updated daily; no internal state |
| **Far** | Anywhere else | No contracts, except those that make the news (§7.4); prices as of the last time you had a presence there |

The same rules apply to every actor. A company with five ships knows more than a pilot with one; that is what scale buys. Your own posts reach others by the same rules (§3.6).

### 7.2 Information age

Everything you know has an age, and the interface shows it (§11.1). A price seen three days ago is drawn faded and labelled "as of Day 4". Stale information is the main source of uncertainty when judging a contract, and the main thing presence buys.

### 7.3 The public record

Outcomes of Accord contracts are public: on-time rate, cancellations and defaults, per actor. Anyone can look them up from anywhere, which is how you judge a yard you've never dealt with. Frontier deals leave no record beyond each party's own experience.

### 7.4 The news

Once a game day, **The Daily Accord** reports a handful of the previous day's contracts, sector-wide: newly posted, signed, fulfilled or failed. For example: "Rust Belt Camp pays a premium for food", "Coreward Logistics orders a Bulk Hauler from Tycho Yards", "Juno Okafor two days late on a Verdant food run". Which contracts make the news is a weighted chance: large, unusual and failed contracts are likelier. The news is how distant markets reach you, a day late and incomplete, and it can tip you off to a queue at a yard or a shortage before you see it yourself.

### 7.5 Later

- **Market news:** "Yesterday the price of Fuel at Halcyon skyrocketed."
- **Visibility terms:** public contracts may make the news, private ones never do (§3.4).
- **Scouting** as a deliberate act: fly somewhere to learn it.
- **Information as a contract kind:** data brokers sell price snapshots, route danger histories and idle-producer indices. Information is perishable, and it moves through the same contract primitive as cargo.
- **Reputation networks:** standing in a region surfaces more intel there; trusted partners introduce you to their partners.

---

## 8. Space and time

### 8.1 Locations

A location is a node in the route graph with resident actors.

| Type | Typical residents | Character |
|---|---|---|
| Core world | Consumers, high-value producers, premium shipyards, government | Deep Accord; high-value, competitive |
| Industrial world | Parts and machinery makers, part factories, shipyards | Accord edge; steady volume |
| Agricultural world | Food producers, consumers | Accord edge; perishable pressure |
| Refinery station | Fuel producers, module factories | Accord edge; strategic chokepoint potential |
| Mining colony | Ore and fuel producers, independent pilots | Outside the Accord; dangerous routes nearby |
| Frontier outpost | Consumers, salvage yards, *(later)* pirates, bonding agents | Outside the Accord; reputation only |

Every location has a station operator who charges docking fees and rents storage.

### 8.2 Routes and danger

Routes are weighted edges: a distance in hours at standard speed, a danger rating, and a law coverage (the Accord reach along it). Routes need not form a complete graph; some places are reachable only through others, which gives chokepoints strategic value.

**Danger is two channels.** The geographic channel is the static hazard of the route itself (black-hole margins, unstable phenomena). It is slow to change and drives transit incidents (§8.4). The predation channel arrives with pirates (Slice 5): live, spiking and decaying with actor activity. The map shows the two separately, permanently. A single merged danger colour would keep re-teaching the static reading every time the player glanced at the map (see §22.2).

### 8.3 Time

**One tick is one game hour.** The interface shows dates and durations ("Day 4 · 14:00", "due in 1d 6h"), never raw ticks.

The world runs in real time at a speed the player picks: pause, 1×, 4× or 16×, where 1× is one game hour per second. It **stops itself when a decision lands on the player**: a counterparty proposing a remedy, a cancellation right becoming available, an incident that needs a call. Everything else (responses, arrivals, new posts) arrives as a notification without stopping the clock. Players can add their own pause rules, for example "pause when a Wanted Sale for Medicine appears near my ships".

You can browse, plan, post and accept while paused. Haggling needs the clock to run (§3.6).

Under the hood the simulation is discrete-event (§19): "running" means processing events as the clock advances, and "paused" means stopping.

### 8.4 Transit incidents

A route's danger gives every journey a chance of a **transit incident**: a delay, cargo damage, partial loss, an unplanned cost or, rarely, total loss. Higher danger makes incidents both more likely and worse. Parts modulate it: armour reduces severity, a courier frame worsens it, a less reliable drive raises the odds.

At departure the pilot picks a **throttle**: cruise or push. Pushing is faster and raises the odds of an incident, more so for a drive that runs hot. It is the classic choice between beating the late fee and risking the cargo.

Incidents are abstract and actor-free, fully consistent with the discrete-event model, and use exactly the hook that pirate interdiction will use later. When pirates arrive they become one source of incidents among others, with counterplay, rather than a new mechanic bolted on.

---

## 9. Reputation

Reputation is how actors decide whom to trust. There are two layers in Slice 1:

- **Accord standing.** Game-wide, earned and lost on Accord contracts. Low standing means larger bonds demanded, fewer actors willing to deal (their trust thresholds), and eventually sanction.
- **Counterparty record.** What each actor remembers of its own dealings with you, plus the public record for Accord contracts (§7.3).

Reputation rises when contracts are performed and falls when they fail, scaled by the contract's value and by how badly it went. It decays slowly toward neutral, so old sins fade and a damaged name can be rebuilt.

Later: reputation keyed by region and by trade (a pilot trusted by miners may be a stranger to the luxury cartels), and introductions through trusted partners.

---

## 10. Goals, failure and legacy

There is **no win condition**. But a sandbox without jeopardy is a spreadsheet, and one without aspiration is a chore.

- **Failure.** Insolvency (§5.4) ends the game. The road there is usually a cascade: failed contracts dragging reputation and credit down together. It is real, and survivable until it isn't.
- **Aspiration.** Player-chosen, never handed out: build a fleet, corner a market, keep a frontier colony alive, own a factory, bankrupt a specific rival. The game recognises these from your record and marks them; it does not assign them.
- **Legacy.** Retire at any time, or be forced to by insolvency, and the game renders your story from your record: markets you moved, contracts honoured and broken, companies you outlasted, and the specific ships you flew, with their histories.

---

## 11. Interface

For a simulation this dense, the interface *is* the game.

### 11.1 Visual language

**Schematic, drawn from data.** Everything on screen is vector shapes and type generated from the simulation: no illustrations, no sprites, no art pipeline. Think Mini Metro's map or DEFCON's displays, applied to an economy.

- **Colour only ever encodes data.** Danger is green, amber or red; money is gain or loss; yours is one accent colour; information age is opacity. Nothing is coloured for decoration.
- **Danger is never colour alone.** Line style (solid, dashed, dotted) carries it too.
- **Numbers line up.** Figures are set in a monospace face with tabular digits.
- **Motion means something happened.** Ships move along routes, credits tick up and down, new posts slide in.
- **Light and dark themes** come from the same set of colour tokens.

### 11.2 Organised around the player's questions

| Question | Where it's answered |
|---|---|
| What is this contract worth to me? | The board and the contract view show your take-home: the price minus the costs the game knows for certain (running cost for the trip and any repositioning, fees, tax). Beside it are the uncertain inputs as ranges: incident odds, the counterparty's record, prices at the destination with their age. The game does the bookkeeping; you do the judging. It never collapses a contract into one "expected value". |
| Where does it go, and how dangerous is it? | Selecting a contract draws it on the map: the empty leg to the pickup dashed, the laden leg solid, each leg coloured by danger, and the arrival time against the deadline. |
| Why is this number what it is? | Every number explains itself on hover, for example "₵8/unit = cost 8 × margin 1.25 × full warehouse 0.8". |
| Why did this happen? | Every outcome links to its cause. A late ship links to the yard's late parts Sale, which links to the Haul that hit an incident. You can walk any cascade back to where it started. |
| What's about to change? | Signals on the map and in a list (idle producers, consumers running short, yards with long queues), each with its age. |
| What's happening to my stuff? | A timeline in the time bar: arrivals, deadlines, expected replies, build milestones. |
| How am I doing? | The ledger: credits, debt, escrow, and profit and loss per contract. |

### 11.3 Surfaces

- **Map** (the primary surface): locations, routes, ships in motion, signals, your presence highlighted. Selecting a contract overlays its path.
- **Board:** open contracts you can see, filterable by kind, good, place, danger, deadline and take-home. Each row shows kind, jurisdiction and danger at a glance. Your own posts appear here with their responses.
- **Contract view:** the contract as a document, with parties, obligation, schedule, and terms in plain language ("If late: ₵40 per day, up to ₵600. After 2 days' grace you may cancel and keep their bond."). It includes the take-home breakdown and the counterparty's record, and it is where you accept, counter or post.
- **Negotiations:** open haggles as threads showing your offer, their reply, its tell, and when to expect the next answer.
- **Orders:** your Build contracts with their dependency tree: which parts are sourced, in transit or late. This is the cascade made visible.
- **Fleet and ship sheet:** each ship's parts and grades, its computed stats with their explanations, and its history.
- **Ledger:** credits, debt, escrow, profit and loss per contract, and reputation.
- **Time bar:** speed controls, the date, the timeline, alerts and pause rules.

Contract and negotiation views are panels beside the map, not modal dialogs, so the world stays visible while you decide.

### 11.4 Teaching the game

The interface teaches by being legible, not with tutorial text. Every stat carries its cause on inspection; every signal the player needs can be surfaced; terms read as plain sentences; and the event log reads like a captain's log. The first hour (§13) is designed so each new idea arrives when the player needs it.


### 11.5 How text is made

Every sentence the interface shows is a template filled from structured data. Nothing is written free-hand at runtime, and no sentence states anything the simulation doesn't know. A template may draw on three sources only:

1. **The event's own fields.** A supply-failure proposal carries its kind, the part, the grades, the price change and the new date: "Grade B instead of A. On time. ₵600 off."
2. **Values computed from what the player's actor knows,** using the same formulas as the engine: the stat changes between two sets of parts, keep-times, travel times, take-home, price history from the player's own readings. "Medicine keeps 8.6 days instead of 10.0."
3. **Explicit references:** the contract's own terms, the player's own contracts and posts, and fixed facts about the world such as the longest trip in the sector. "Longest trip in the sector at this ship's speed: 6.9 days (Verdant → Edge Station)."

Text never states intent ("still enough for the run you want"), never another actor's hidden state, and never a prediction the game can't compute. The information rules of §7 apply to text exactly as they apply to numbers: a producer's warehouse is internal state, so from one jump away the player reads "asking ₵8.00, 23% below your readings", not "warehouse full".

Templates live in resource files keyed by event type and fact type, which also makes them translatable.

**A language model, later, may only restyle.** It can turn a fact list into livelier prose, but its input is that fact list and nothing else, its output never feeds back into the simulation, and the template text stays canonical in the log and on hover. Contracts, prices and outcomes are always deterministic.

---

# Part II — Slice 1

## 12. Goal and definition of done

Slice 1 is the smallest version of the game worth playing for an hour. It has to show the contract web, not just a hauling loop: posting as well as taking, failure with consequences, and a ship built from parts that were sourced through contracts.

Slice 1 is **done** when:

1. A first-time player gets through the first hour (§13) without outside help: in playtests, three out of four testers.
2. The start state guarantees the first three beats of §13. The last two (a supply-failure decision, and a custom ship arriving with a story) happen in at least 70% of first hours, measured with scripted bot players in the soak harness (§20.3).
3. Every failure path in §16.3 is reachable for both player and AI, settles through contract terms, and is covered by tests.
4. Skill matters: a scripted "market reader" bot earns at least 1.5× the credits per hour of a "board taker" bot.
5. The economy holds over a million-tick soak: invariants intact, no mass bankruptcy, no runaway inflation, no class of actor stuck permanently idle.
6. For any contract, the player can find out from the interface why it made or lost money.

## 13. The first hour

The player starts at **Meridian Station**, an Accord-edge industrial colony, as an independent pilot with ₵3,000 and a worn Light Freighter.

| Minutes | What happens | What it teaches |
|---|---|---|
| 0–10 | The local board offers a few Hauls and delivered Sales. One is a trap: a fat fee on a red route, a harsh late fee, and a deadline the freighter can only meet by pushing. The player runs two or three contracts. | Reading a contract: price, route, deadline, terms |
| 10–20 | Verdant's food producer, its warehouse full, is posting food cheap: visible from Meridian, one jump away. The Daily Accord reports that the Rust Belt camp is paying a premium for delivered food. The player posts their own Wanted Sale at Verdant, haggles the price down, carries the food, and fills the camp's contract. | Posting, haggling and acting on a signal: the first edge you found yourself |
| 20–35 | The best money nearby is medicine to Edge Station, which needs refrigeration and crosses a red route. The freighter is wrong for it. Three options: a second-hand refrigerated ship at Edge Salvage (cheap, unarmoured, a frontier sale with no escrow, and a history worth reading), a stock Refrigerated Transport at Tycho Yards, or a custom build that is no standard design: a Light frame with a Fast drive and refrigeration, quick enough to beat both the spoilage and the deadlines. | Off the shelf vs built to order |
| 35–50 | If the player commissions, the yards bid, and the player picks one and its terms. The yard's parts contracts appear on the board, and the player can haul one. Upstream, a parts haul hits an incident, and the yard invokes its supply-failure term: wait with compensation, take a grade-B substitute at a discount, or cancel with a refund. | Contracts depend on contracts; failure is settled by terms |
| 50–60 | The ship arrives, its stats telling the story of its parts. With two ships the player can't fly every run, so they post a Haul for an AI pilot to carry what they can't. | The loop closes: the player is a client now, too |

If the player buys off the shelf instead, the 35–50 beat arrives from the other side: a contract they are performing fails upstream (the producer behind one of their delivered Sales falls short), and the remedy decision is theirs.

The start state is seeded to guarantee the first three beats. The last two emerge from the simulation and are measured, not scripted (§12).

## 14. Scope

**In Slice 1:**

| Feature | Why |
|---|---|
| Sale, Haul, Build and Storage contracts, posted and taken by anyone | The contract web, in both directions |
| Terms: deadline and grace, late fees, cancellation, supply failure, substitution, warranties, cargo liability, bonds; the three packages | Failure is content |
| Haggling with replies that take game time, and tells | The central verb |
| Escrow and bonds in Accord space; none on the frontier | Enforcement by jurisdiction |
| Seven goods; food and medicine perishable | Routing variety |
| Ship parts (frame, drive, module) with grades; five designs | Things are the sum of their parts |
| Three part factories, two shipyards, a salvage yard; stock, second-hand and custom ships | The signature decision |
| Producers, consumers, traders and haulers (companies and independents), station operators, a government tax | A living economy |
| AI that posts and takes every contract kind, including ordering ships | Symmetry |
| One sector: seven locations, danger-rated routes, multi-hop paths | Meaningful routing |
| Transit incidents and the throttle choice | Danger with teeth |
| Information tiers, information age, the Accord public record | Judging needs uncertain, ageing information |
| The Daily Accord: a daily digest of some of yesterday's contracts | How distant markets reach you |
| Accord standing and counterparty records | Trust |
| Debt, insolvency, liquidation; game over and legacy | Real consequences |
| Discrete-event engine, seeded RNG, invariants, soak harness with bot players | A trustworthy, tunable economy |
| The browser client, with the interface of §11 | The interface is the game |

**Not in Slice 1:**

| Feature | Lands in (§22) |
|---|---|
| Standing contracts, bonding agents, data brokers, market news, private offers, visibility terms, scouting, demand starvation, reputation by region and trade, AI that weighs information age by risk tolerance | Slice 2 |
| Player-owned facilities, service contracts (hiring pilots), the player as contractor | Slice 3 |
| Wear, maintenance, refits, insurance | Slice 4 |
| Pirates, enforcement, blockades | Slice 5 |
| Processing chains for ordinary goods (raw food to food, and so on); boutique and large shipyards | Slice 6 |
| FTL, exotics, cartography | Slice 7 |
| Aliens and Accord politics | Slice 8 |
| Multiplayer | Slice 9 |

## 15. Content

All values are first-pass.

### 15.1 Locations

| Location | Type | Jurisdiction | Region | Residents |
|---|---|---|---|---|
| Meridian Station | Industrial | Accord edge | Helios | Meridian Fabworks (Parts), Meridian Drive Works (drives), Meridian Habs (consumer). **Player start.** |
| Verdant | Agricultural | Accord edge | Helios | Verdant Agricombine (Food), Verdant Township (consumer) |
| Helios Prime | Core world | Deep Accord | Helios | Prime Couturiers (Luxury), Helios Biolabs (Medicine), Helios Orbital Yards, Helios Prime Metropole (consumer), Helios Accord Authority (tax) |
| Tycho Foundry | Industrial | Accord edge | Helios | Tycho Heavy Industries (Machinery), Tycho Frameworks (frames), Tycho Yards, Tycho Workforce (consumer) |
| Halcyon Refinery | Refinery | Accord edge | Helios | Halcyon Fuelworks (Fuel), Halcyon Modules (modules), Halcyon Crew (consumer) |
| Rust Belt | Mining colony | Outside Accord | Marches | Rust Belt Extraction (Ore), Marches Volatiles (Fuel), Rust Belt Camp (consumer) |
| Edge Station | Frontier outpost | Outside Accord | Marches | Edge Station Holdfast (consumer), Edge Salvage (second-hand ships) |

Every location also has a station operator (docking, storage).

### 15.2 Routes

| Route | Hours | Danger |
|---|---|---|
| Meridian — Verdant | 30 | green |
| Meridian — Helios Prime | 50 | green |
| Meridian — Tycho | 40 | green |
| Verdant — Helios Prime | 45 | green |
| Helios Prime — Halcyon | 40 | green |
| Tycho — Halcyon | 35 | green |
| Tycho — Rust Belt | 70 | amber |
| Halcyon — Rust Belt | 60 | amber |
| Rust Belt — Edge | 80 | red |

The Rust Belt — Edge route is the only way to the frontier outpost: a natural chokepoint.

### 15.3 Goods

| Good | Category | Made at | Keeps, standard hold | Keeps, refrigerated |
|---|---|---|---|---|
| Food | Staple | Verdant | ~7 days | ~35 days |
| Ore | Raw | Rust Belt | — | — |
| Fuel | Raw | Halcyon, Rust Belt | — | — |
| Parts | Industrial | Meridian | — | — |
| Machinery | Industrial | Tycho | — | — |
| Medicine | High value | Helios Prime | ~2 days | ~10 days |
| Luxury | High value | Helios Prime | — | — |

"Keeps" is the time until quality drops below the standard warranty. It is a design target: food survives the trip from Verdant to Rust Belt in a standard hold, but medicine only reaches the frontier refrigerated.

### 15.4 Parts and factories

| Part | Made by | Inputs |
|---|---|---|
| Frames (Light, Bulk, Courier) | Tycho Frameworks, Tycho Foundry | Ore, Parts |
| Drives (Standard, Fast) | Meridian Drive Works, Meridian Station | Machinery, Fuel |
| Modules (Refrigeration, Armour, Cargo pod) | Halcyon Modules, Halcyon Refinery | Parts, Machinery; Ore for armour |

Each factory has a quality level and drops a grade when it substitutes for a missing input.

| Part | Cargo | Mass | Toughness | Power | Reliability | Other | Base price |
|---|---|---|---|---|---|---|---|
| Light frame | 120 | 1.0 | 1.0 | | | | ₵3,000 |
| Bulk frame | 500 | 1.7 | 0.85 | | | | ₵12,000 |
| Courier frame | 45 | 0.78 | 0.7 | | | | ₵3,500 |
| Standard drive | | | | 1.0 | 1.0 | Runs hot when pushed | ₵3,000 |
| Fast drive | | | | 1.4 | 0.9 | | ₵6,000 |
| Refrigeration unit | −10 | +0.05 | | | | Perishable decay ×0.2 | ₵5,000 |
| Armour plating | −15 | +0.1 | ×1.6 | | | | ₵8,000 |
| Cargo pod | +40 | +0.15 | | | | | ₵2,500 |

Grade B scales a part's main stat by 0.96 and grade C by 0.92.

### 15.5 Shipyards

| Yard | Location | Character |
|---|---|---|
| Tycho Yards | Tycho Foundry | Cheap and quick; lenient terms by habit; next door to the frame maker |
| Helios Orbital Yards | Helios Prime | Expensive and slower; strict terms by habit; deep Accord, so disputes go to arbitration |
| Edge Salvage | Edge Station | Doesn't build. Buys wrecks and retired ships and sells them second-hand with their histories. Frontier: no escrow. |

Each yard builds one ship at a time and, when idle, builds a standard design on spec for its lot.

### 15.6 Designs

Stats follow from §15.4:

| Design | Cargo | Speed | Toughness | Rough price |
|---|---|---|---|---|
| Light Freighter | 120 | 1.0 | 1.0 | ₵8,000 |
| Bulk Hauler | 500 | 0.59 | 0.85 | ₵18,000 |
| Fast Courier | 45 | 1.8 | 0.7 | ₵12,000 |
| Refrigerated Transport | 110 | 0.95 | 1.0 | ₵13,000 |
| Armoured Freighter | 105 | 0.91 | 1.6 | ₵16,000 |

### 15.7 Other actors

- **Haulers:** two shipping companies (Coreward Logistics, Foundry Freightways) and five independent pilots, spread across the sector.
- **Consumers:** one per location (see §15.1), each with a few goods it consumes and an income.
- **Government:** the Helios Accord Authority, which collects the Accord tax.

## 16. Rules

### 16.1 Goods and possession

Goods always sit somewhere:

- in an actor's own stock at its location (a producer's warehouse, a consumer's larder);
- in storage at a station, under a Storage contract;
- in a ship's hold.

Moving goods between locations takes a ship. Handing goods over under a Sale needs the goods at the named place and somewhere for the receiver to put them there: a docked ship, storage, or (for a consumer) its own stock.

### 16.2 Contract lifecycle

**Open** (posted, collecting responses) → **Signed** → **Performing** → one of:

- **Fulfilled**, on time or late;
- **Partly fulfilled**;
- **Cancelled**: at will, for default, or by agreement after a supply-failure proposal;
- **Defaulted**: the payer didn't pay;
- **Expired**: never signed before its deadline.

Every transition is an event that records its cause, including the event or contract that triggered it. That is what makes outcomes traceable in the interface (§11.2) and readable in the legacy (§10).

### 16.3 Failure paths

All of these must be reachable, for player and AI, and covered by tests:

| Failure | Example | Resolution |
|---|---|---|
| Late delivery | A Haul arrives after the deadline | Late fee per day, from the performer's bond (Accord) or owed directly (frontier), up to the cap |
| Default by the performer | The grace period passes with nothing delivered | The payer may cancel (escrow refunded, bond forfeited) or keep waiting |
| Short delivery | Partial cargo loss in an incident | Per the partial-delivery term: pro rata, or treated as a failure |
| Quality or spec shortfall | Medicine arrives below warranty quality; a ship is built below spec | Refund per shortfall; rejection below the floor |
| Substitution | The yard can't get a grade-A frame | Per the substitution policy; outside it, a supply-failure proposal |
| Supply failure | A parts haul is lost and the yard can't finish on time | The performer proposes an extension with compensation, a discounted substitute, or cancellation with a refund and compensation; the payer chooses |
| Cancellation at will | The player cancels a build order | The break fee is paid |
| Payer default | A frontier buyer doesn't pay at handover | The seller keeps the goods; the buyer's record with the seller suffers; nobody enforces anything |
| Insolvency | An actor can't cover its debts | Assets liquidated; its contracts cancelled for default |
| Expiry | Nobody takes a post before its deadline | Withdrawn, without consequences |

### 16.4 Escrow, bonds and deposits

- **In Accord space**, on signing, the payer locks the price and the Accord tax in escrow and the performer locks a bond (by default, the late-fee cap). Settlement pays out of escrow according to the terms.
- **Build contracts** take a deposit on signing (20% by default, paid to the yard so it can buy parts) and lock the balance in escrow. The deposit doubles as the client's break fee.
- **On the frontier** nothing is locked. Payment changes hands at handover, and each side trusts the other or doesn't.

### 16.5 Haggling

- Up to six rounds.
- Replies take 1–12 game hours, faster for eager actors.
- A counter can change the price, the deadline, the late fee and the term package.
- A counterparty walks away if the gap to its reservation point is too wide, or when the rounds run out.

### 16.6 Posts and responses

An actor that can see a post evaluates it on its next trigger and may respond with an acceptance or a counter. A post closes when the poster accepts a response, withdraws it, or its deadline passes.

### 16.7 Throttle and incidents

- **Cruise:** standard speed.
- **Push:** speed ×1.25, incident chance ×1.5, or ×2 for a drive that runs hot.

Incident chance scales with the route's danger and the drive's reliability. Danger also shifts the outcome table toward the severe end; toughness scales the damage.

### 16.8 Debt and insolvency

- Running costs, fees and interest keep charging below ₵0.
- Debt costs interest daily.
- An actor is warned when its debt exceeds half its asset value, and becomes insolvent when debt exceeds its asset value or stays above half for seven days.

## 17. Pacing targets

These are design targets; the soak harness tunes the numbers toward them.

| Target | Value |
|---|---|
| 1× speed | One game hour per second |
| A core haul | 1–2 game days |
| A frontier haul | 3–6 game days |
| A haggle reply | 1–12 game hours |
| A custom build, start to finish | 2–4 game weeks |
| Starting position | ₵3,000 and a Light Freighter |
| Second ship affordable | 35–45 minutes for a market reader; 60+ minutes for a board taker |
| A day idle | Costs a Light Freighter roughly a tenth of a typical core haul's take-home |
| The player's first failure | Within the first 20 minutes, and survivable |

---

# Part III — Technical design

## 18. Architecture

| Component | What it is |
|---|---|
| **Engine** | A C# (.NET 10) class library containing the whole simulation. No UI, no I/O, deterministic. |
| **Client** | A Blazor WebAssembly app. It references the engine directly and runs the simulation in the browser; the map is SVG. Implements §11. |
| **Soak harness** | A console app: headless runs, telemetry, invariant checks and bot players. The balance instrument. |
| **Tests** | xUnit. Fast by default; soak-length runs live in a separate category. |

**Why Blazor.** Most of this interface is documents, tables, tooltips and timelines, which is what HTML and CSS do best, and an SVG map with CSS motion covers the schematic style. The C# engine plugs in without a rewrite or interop layer. The client can be driven and screenshotted by browser automation, so the interface can be tested like the rest of the code. It runs in any browser and can be wrapped as a desktop app later.

The earlier WPF client and the console play mode are retired.

## 19. The simulation engine

The game runs as a **discrete-event simulation**. The world is a priority queue of future events ordered by tick, with stable tie-breaking. Each event's `Process` may schedule further events. Nothing happens between events: a ship in transit has no position, only a scheduled arrival. The client interpolates positions for display, and the engine interpolates only when something forces it.

```csharp
while (queue.HasEvents && queue.NextTick <= targetTick)
{
    var next = queue.Dequeue();
    context.AdvanceTo(next.Tick);
    next.Process(context);     // may schedule further events
    if (context.PlayerDecisionPending) break;   // hard pause (§8.3)
}
```

**Core events:**

| Event | May schedule |
|---|---|
| `ContractPosted` | Actor evaluations (via visibility), `ContractExpired` |
| `ResponseReceived` (accept or counter) | Further responses, `ContractSigned` |
| `ContractSigned` | Escrow and bond locks, `ProductionRun`, `ShipDeparted`, sub-contract posts |
| `ShipDeparted` | `TransitIncidentCheck`, `ShipArrived` |
| `TransitIncidentCheck` | `TransitIncidentOccurred` or nothing |
| `TransitIncidentOccurred` | Delay, damage, loss or cost; a decision if one arises |
| `ShipArrived` | `GoodsHandedOver`, settlement |
| `GoodsHandedOver` | Settlement of the Sale or Haul |
| `DeadlinePassed`, `GraceEnded` | Late fees; cancellation rights; decisions |
| `RemedyProposed` | A decision for the other party |
| `ContractSettled` | Escrow release, reputation, public record, causal links |
| `ProductionRun` | Stock changes |
| `ShipAssembled` | A Build's delivery |
| `ActorHeartbeat` | Evaluations |
| `InsolvencyCheck` | Liquidation |
| `InflationCheck` | Faucet adjustment |

**The player as actor.** The player's decisions arrive from the interface as commands that schedule exactly the events an AI decision would. When an event requires a player decision, the loop hard-pauses and the interface prompts; the answer schedules further events as usual.

**Causal links.** Every event records the event or contract that caused it. That one field powers traceability in the interface (§11.2), the legacy screen (§10) and debugging.

## 20. Determinism, invariants and testing

### 20.1 Determinism

Every random draw comes from a seeded PRNG, every iteration that consumes randomness runs over a stably ordered collection, and all randomness happens inside events. A seed plus the player's command log reproduces a run exactly. That gives exact replay for debugging, saves that are just a seed plus a command log (§21), and a server-authoritative model for multiplayer later.

### 20.2 Invariants

These are checked continuously in debug builds and asserted in tests:

- **Credit conservation:** total credits = initial + Σ faucet − Σ sinks, exact to the cent.
- **Escrow reconciliation:** Σ held = Σ locked − Σ released − Σ forfeited; no contract's escrow is ever negative.
- **Goods conservation:** goods produced = consumed + held anywhere (stock, storage, holds) + used in parts and ships + lost.
- **No orphan contracts:** every signed contract has a scheduled next event or a pending decision.
- **No negative stock**, anywhere.
- **Deterministic replay:** replaying a seed and command log yields the same state hash.

### 20.3 Testing

- **Unit and property tests** run fast and by default. Property tests generate random populations and assert the invariants over short runs.
- **Soak runs** run the world headless for millions of ticks and check that it neither collapses (mass bankruptcy, deflationary spiral) nor runs away (hyperinflation, monopoly lock-in). They are also the primary balance-tuning instrument: prices, faucet and sink rates, ship costs and danger weights are tuned by reading soak telemetry, not by hand.
- **Bot players** are scripted player strategies in the soak harness, such as a "board taker" and a "market reader". They measure the definition-of-done targets: how often the first-hour beats occur, and whether skill pays (§12).
- **Interface tests** drive the client in a browser and check key flows and screenshots.

## 21. Persistence

In Slice 1, state is in memory. A save is the seed, the configuration and the player's command log; loading replays it, and determinism makes that exact. Snapshots get added if replay grows slow.

Later, for long campaigns or multiplayer, a persistent event store fits the event-sourced model naturally. Marten on PostgreSQL was the original plan. Snapshots would be semantic: a ship at build, sale or repair; an actor at major capital events; a location at intervals.

---

# Part IV — Roadmap

## 22. Slices

Each slice is a thin, end-to-end cut through data, simulation and interface that is playable on its own. They are ordered by dependency and by how much each adds to the core fantasy per unit of risk.

### 22.1 The slices

| Slice | Theme | Adds | Fantasy it unlocks |
|---|---|---|---|
| **1** | **The contract web** | Part II of this document | *"I judge contracts, post my own, and live with the consequences."* |
| 2 | Institutions and information | Standing contracts, bonding agents, data brokers and information contracts, market news, private offers, visibility terms (public or NDA), scouting, demand starvation, reputation by region and trade, AI that weighs information age by risk tolerance | *"I read the market, run a logistics business, and the world reacts to me."* |
| 3 | Ownership and services | Facilities as assets (factories, warehouses, yards) bought or built through contracts; service contracts (hire pilots and operators); the player as contractor; multinationals | *"I own a piece of the supply chain."* |
| 4 | Wear and insurance | Part wear, maintenance and repair contracts, refits, insurance | *"Every ship has a history I can read and price."* |
| 5 | Piracy and enforcement | Pirate actors as a source of incidents, fences, escorts, police contracts, blockades and chokepoint control | *"The frontier is dangerous and I choose how to face it."* |
| 6 | Production depth | Processing chains for any good, multi-tier supply chains, quality flowing through them, boutique and large shipyards | *"I'm a link in a real supply chain, not just a courier."* |
| 7 | The FTL frontier | Exotic materials, FTL routes and risk, cartography, route-access contracts | *"I open the map and own what I discover."* |
| 8 | The Accord and the aliens | Alien threshold actors, Accord sanction and extension politics | *"My economy has consequences at the scale of civilisations."* |
| 9 | Multiplayer | Network actors, server-authoritative replay, a multiplayer pause model | *"The other operators are people."* |

### 22.2 Onboarding piracy (binding on Slice 5)

Transit incidents teach two things. **Spatial:** some routes are physically hazardous. That stays true when pirates arrive. **Temporal:** a route's hazard is stable. Predation breaks that, because pirates look specifically for quiet, historically safe lanes. The heuristic Slice 1 trains, *a quiet lane is a safe lane*, is exactly what the Slice 5 adversary punishes. That is mild negative transfer on the dimension the player is most confident about.

Two consequences:

- **Two beats, in this order.** First an *existence* beat: an interdiction visibly attributable to a named pirate's decision, on an already-amber route at low stakes, so the player registers "an agent did this" rather than "the table rolled badly again". Later and separately, a *falsification* beat: an interdiction on a route the player's own history says is quiet, attributed to that pirate being displaced by enforcement elsewhere. Only the second overturns the static-danger heuristic, because an amber-route hit is consistent with the old model and teaches nothing. Don't stack both into one moment; that reads as unfair rather than instructive.
- **The map keeps the two danger channels separate permanently** (§8.2), or the interface re-teaches the static reading far more often than the beats corrected it.

Plant the first two rungs on purpose (static danger, then "an adversary exists"). Leave the rest, predicting, avoiding or inducing pirate relocation (for example by funding police contracts on a lane pirates rely on), to emerge. That is where the symmetry pillar wants it.

---

# Appendix A — Decisions

## A.1 Decision log

| # | Decision | Version | Why | Replaces |
|---|---|---|---|---|
| D1 | Contracts are the universal primitive. | v0.1 | The thesis of the game. | — |
| D2 | The player is a cog in a world that doesn't exist for them. | v0.1 | Living, indifferent universe. | — |
| D3 | Production is decoupled from throughput. | v0.1 | Each actor runs independently; consequences come through supply (§5.6), not coupling. | — |
| D4 | Discrete-event simulation; no continuous position. | v0.1 | Cheap, deterministic, scales. | — |
| D5 | Real time with player-controlled speed and a hard pause on decisions. | v0.2 | Makes "the world moves without you" literally true and gives deadlines weight. Turn-based was considered and rejected. | — |
| D6 | Transit incidents give danger ratings teeth before pirates exist. | v0.2 | Danger with no downside isn't a dial; the hook generalises to interdiction. | — |
| D7 | The Galactic Accord is the enforcement spine and the title's referent. | v0.2 | Explains escrow, arbitration and the frontier diegetically. | — |
| D8 | Greedy-utility actors with personality multipliers, re-evaluating on triggers plus a heartbeat. | v0.2 | Legible, predictable, cheap. | — |
| D9 | Seeded, replayable randomness; invariants; soak testing. | v0.2 | An emergent economy is untunable and undebuggable otherwise. | — |
| D10 | Danger is two channels, geographic and predation, shown separately for good. | v0.2 | §22.2. | — |
| D11 | The game is the contract web. Hauling is a supporting activity, not the core. | v0.3 | The interesting layer is contracts depending on contracts, most clearly in ship building. | The hauling-centred Slice 0 of v0.2 |
| D12 | Symmetry: anything an AI can do, the player can do, and vice versa. | v0.3 | Core to the thesis. | "Player and actor are the same type" (v0.2), strengthened |
| D13 | Players post contracts of every kind, not only take them. | v0.3 | If nothing on the board fits, post your own. This also gives market signals a verb. | — |
| D14 | Contract kinds are Sale, Haul, Build and Storage in Slice 1; Service, Insurance, Bond and Information later. | v0.3 | v0.2's bundled "buy and haul" contract hid the web. | v0.2's Haul / Procure / Brokered split |
| D15 | Ships are built from parts with grades. Custom builds sub-contract their parts; stock and second-hand ships exist. | v0.3 | Off the shelf vs built to order is the signature decision. | Ship variance (v0.2 Slice 2) and factories (v0.2 Slice 4), partly pulled into Slice 1 |
| D16 | Failure is content. Every contract states deadline, grace, late fee, cancellation, substitution and warranty terms, and both sides can fail. | v0.3 | Failure states are a large part of the game, for player and AI alike. | v0.2's clause schema (expanded) |
| D17 | The core skill is judging whether a contract is worth signing. The game computes certain costs and shows uncertain inputs as ranges, never a single expected value. | v0.3 | Keeps the judgment with the player while keeping the books legible. | — |
| D18 | Information falls off with distance from your presence, and every piece of information shows its age. | v0.3 | Nearby markets known in detail, distant ones roughly; it makes presence valuable. | v0.2's visibility model |
| D19 | Haggling covers price and a few terms. Replies take game time, and reply time is a tell. | v0.3 | Gives haggling a cost and makes it readable. | v0.2's instant price-only haggle |
| D20 | Posts collect responses over time from actors who can see them. | v0.3 | Where you post matters. | — |
| D21 | One tick is one game hour; the interface shows dates and durations. | v0.3 | Ticks had no meaning, so pacing couldn't be designed. | "1 tick ≈ 1 second" |
| D22 | The interface is schematic, not ASCII, organised around the player's questions, and every outcome is traceable to its cause. | v0.3 | ASCII stood in for "no art pipeline"; schematic keeps that and gains layout, motion and hover. | "Character / ASCII-forward" UI |
| D23 | The client is Blazor WebAssembly. The WPF client and the console play mode are retired; the soak harness stays. | v0.3 | Suits a document-heavy interface, reuses the engine directly, and can be tested in a browser. | WPF client, console client |
| D24 | Persistence in Slice 1 is in memory, with saves as seed plus command log. A persistent event store comes later. | v0.3 | Zero infrastructure; determinism makes replay exact. | Marten + PostgreSQL from the start |
| D25 | Slice 1 reputation is Accord standing plus counterparty records, and Accord contracts are public record. | v0.3 | Enough to judge counterparties; region and trade contexts wait for Slice 2. | v0.2's fully contextual reputation from the first slice |
| D26 | Debt and insolvency are in Slice 1. Player insolvency ends the game with a legacy screen. | v0.3 | Risk needs a downside. | Bankruptcy as optional (v0.2) |
| D27 | A new first hour; the start state guarantees its first three beats. | v0.3 | Built around posting and the ship decision. | v0.2's §3.3 walkthrough |
| D28 | Player-owned facilities and the player as contractor are designed for, but land in Slice 3. | v0.3 | Keeps Slice 1 shippable without closing the door. | — |
| D29 | Storage contracts are in Slice 1. | v0.3 | Lets goods wait for a buyer and lets sell orders be filled from storage. | — |
| D30 | Slices are numbered from 1: the first playable slice is Slice 1. | v0.3 | Matches how the slice is talked about. | v0.2's "Slice 0" |
| D31 | Without a presence you see no distant contracts. In Slice 1 the news reports some of them a day late; market news and data brokers come later. | v0.3 | Distant markets should be known roughly and late, and knowing them better should cost something. | Far contracts visible after a delay |
| D32 | In Slice 1, actors trust what they last saw. Later, risk tolerance decides how far each actor trusts old information. | v0.3 | Aware without being unexploitable, eventually; simple and exploitable first. | — |
| D33 | Private offers to named actors exist, from Slice 2. | v0.3 | Natural for builds; symmetric. Not needed for the first hour. | — |
| D34 | In Slice 1, every contract is public and may make the next day's news. From Slice 2, a visibility term chooses public (publicity, reputation) or private (an NDA: nobody can act on it or undercut it). | v0.3 | Gives the news something to report now and makes secrecy a negotiable term later. | — |
| D35 | Slice 1 yards build one ship at a time. Later, boutique yards build custom ships a few at a time, and large yards build batches of near-identical standard ships. | v0.3 | Enough for queues now; yard kinds add a real market for ships later. | — |
| D36 | Interface text is templates filled from event fields, computed values and explicit references; never intent, hidden state or prediction. A language model may later restyle the fact list, nothing more. | v0.3 | Determinism and legibility: text that claims more than the simulation knows misleads the player. | Free prose in the v0.3 mockup |

## A.2 Open questions

- **Grade steps and substitution discounts:** tuning, but they decide how much substitution stings.
- **What makes the news:** the weights for size, novelty and failure, and how many items a day. Too few and the frontier starves for lack of information; too many and presence stops mattering.
- **Which board does a post go on?** The poster's location, or the place the contract is performed? The mockup assumes the handover place for Sales and Hauls (a Wanted post for food at Verdant is seen from Verdant outward), and the poster's location for Builds. This decides whether you can post somewhere you have no presence.
- **Can an actor pay to be in the news?** An advertising contract with the paper would be symmetric and useful for a poster at a remote outpost; it may belong with the visibility terms in Slice 2.

---

# Appendix B — Glossary

| Term | Definition |
|---|---|
| Accord (Galactic) | The treaty defining the standard contract, escrow, arbitration and the public record. Its geographic reach decides enforcement. |
| Accord standing | Game-wide reputation earned on Accord contracts; gates access to them. |
| Actor | Anything that can be party to a contract: the player, AI companies, pilots, producers, factories, yards, station operators, governments, and later pirates, bonding agents and data brokers. |
| Bond (performance) | A sum the performer locks in escrow, drawn on for late fees and forfeited on default. |
| Break fee | What a party pays to cancel at will. |
| Build contract | A contract to make something to a specification. |
| Cascade | A chain of contract failures propagated by actors honouring or breaking dependent contracts. |
| Contract web | The graph of contracts that depend on each other; the actual game. |
| Deposit | The part of a Build price paid on signing; doubles as the client's break fee. |
| Design | A standard combination of ship parts (Light Freighter, Bulk Hauler, and so on). |
| Discrete-event simulation | A model in which state changes only at discrete events from a queue. |
| Escrow | Money held by an Accord clearing house and paid out according to a contract's terms. |
| Faucet | Where credits enter the economy: consumer income. |
| Grace period | Time after a deadline during which a contract is late but not yet in default. |
| Grade | A part's quality: A, B or C. Scales its main stat. |
| Haul contract | A contract to move goods the carrier doesn't own. |
| Information age | How old a piece of information is; shown in the interface. |
| Information tier | Here, near or far: how much you know about a place, by distance from your presence. |
| Insolvency | Debt too large for an actor's assets; leads to liquidation. |
| Late fee | Paid per day late, up to a cap. |
| Legacy | The story the game renders from your record when you retire or go under. |
| News (The Daily Accord) | A daily digest reporting some of the previous day's contracts, sector-wide; how distant markets reach you. |
| Part | Frame, drive or module; ships are built from them. |
| Personality | Multipliers biasing an actor's decisions: risk tolerance, liquidity preference, quality bias, growth ambition, trust threshold, eagerness. |
| Presence | Where you can see from: your ships' locations (or destinations), and anywhere you rent storage or own a facility. |
| Private offer | A contract offered to named actors only, instead of posted publicly *(later)*. |
| Public record | The published outcomes of Accord contracts. |
| Reservation point | An actor's hidden walk-away price or terms in a haggle. |
| Sale contract | A contract for goods or an asset to change hands at a named place. |
| Sink | Where credits leave the economy: production, running costs, fees, interest, upkeep, tax. |
| Soak run | A long headless run used to check stability and tune balance. |
| Storage contract | A contract for space to keep goods at a location. |
| Substitution policy | Strict, standard or lenient: what a performer may substitute, and at what discount. |
| Supply failure | A term letting a performer propose remedies when something upstream fails. |
| Term package | Strict, standard or lenient presets for a contract's terms. |
| Tell | A readable hint of how close a counterparty is to its limit: reply time and wording. |
| Throttle | Cruise or push, chosen at departure. |
| Tick | One game hour. |
| Transit incident | An abstract, danger-weighted mishap in transit: delay, damage, loss or cost. |
| Visibility term | Public (may make the news) or private (an NDA) *(later)*. |

---

# Appendix C — Idea bank

Unscheduled ideas consistent with the design. Keep, cut or promote.

- **Chokepoint control and blockades.** An actor stations escorts or pirates on a route and taxes or denies passage. A standing-contract trade lane is precisely the thing worth blockading. The Rust Belt — Edge route is the obvious first candidate. Natural fit for Slice 5.
- **Sub-contracting as a positive cascade.** A company that wins more than it can fly sub-contracts part of it to the player: the player's first taste of being trusted by an institution. Already expressible with Haul contracts; it just needs surfacing.
- **Reputation as an introduction graph.** High standing with an actor makes its trusted partners' contracts visible to you. Belonging compounds.
- **Grades for all goods.** Extend part grades to ordinary goods, so where the high-grade ore is becomes information worth selling.
- **Facility loss as a rare event.** A refinery fire causes a regional shortage that ripples through prices and contracts: a news event fully consistent with the simulation. Pairs with insurance and information brokers: the broker who knew first profits.
- **The analyst career.** A player who never moves cargo: buys information cheap, sells it dear, brokers deals, bankrolls bonded shipments. Symmetry makes it possible; it is worth protecting as a design goal that the game can be played without ever hauling.
- **Starting archetypes as difficulty.** *Independent pilot* (the default: one ship, some savings). *Corporate lackey* (indentured to a company: steady internal contracts, thin margins, little risk; also the diegetic restart after insolvency). *Newbie pirate* (no Accord standing; frontier only; high risk and reward). *Factory owner* (starts with a facility and both sides of the ledger). No difficulty menu needed.
