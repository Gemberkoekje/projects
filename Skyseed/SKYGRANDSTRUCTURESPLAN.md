# Skyseed — Grand Structures Plan: Woodland Mansion & Trial Chamber

A design exploration for turning the **Woodland Mansion** and **Trial Chamber** into big, imposing,
**jigsaw-assembled** sky islands — replacing the modest sketches in
[SKYSTRUCTURESPLAN.md](SKYSTRUCTURESPLAN.md) (Mansion = "one enclosed room island"; Trial Chamber = "a chain of
small single-section islands"). This doc supersedes those two sections **once the open decisions at the bottom
are locked**. Status: **Tier-1 gating cells built (v0.27.0); grand Trial Chamber built (v0.28.0, single-template v1); Woodland Mansion still to do.**

---

## The shift in direction

| | Old plan (SKYSTRUCTURESPLAN) | New direction (this doc) |
|---|---|---|
| Mansion | One enclosed room on a small island | A **grand, multi-room mansion** assembled by the jigsaw system |
| Trial Chamber | A multi-island "dungeon chain" | A **single grand Trial Chamber** island, jigsaw-assembled |
| Gating items | Locked *behind* the structure (bootstrap problem) | Moved *out* into small random-chance chambers |

Both old versions put the gated reward (totem; trial-key→vault loop) **inside** the structure, which creates
the circular bootstrap problems the old plan flags. The new direction separates the two concerns.

---

## The two-tier model (the core idea)

| Tier | What it is | How you get it | Its job |
|---|---|---|---|
| **1 — Gating chambers** | Small rare chambers: a **Vault Cell**, an **Evoker Cell** | Random chance on themed islands, exactly like today's igloo / buried-dungeon rares (no recipe) | The *accessible, repeatable* source of **trial keys / ominous keys / a first evoker → totem** — solves the bootstrap |
| **2 — Grand islands** | The big **Woodland Mansion** + **Trial Chamber** | Their own crafted seeds (a known destination) | The *spectacle and the headline payoff* (totems, vaults, the mace + heavy core) — where you spend the keys |

This decouples **"can I keep progressing?"** (Tier 1, always reachable by throwing seeds) from **"the epic
build"** (Tier 2, the reward you work toward). Your idea — "smaller chambers with a random chance that can have
trial keys and such" — is Tier 1.

---

## Problems & solutions

Each problem: the issue → options → a recommendation. The recommendations are *defaults to react to*, not
decisions — the real decisions are collected at the bottom.

### P1 — Scale: vanilla is far too big for a sky island
A vanilla mansion is ~60×60 over three floors; a trial chamber sprawls underground across an even larger area.
A Large island is radius ~11–17 (≈ 22–34 blocks across). We can't reproduce them 1:1.

**Options**
- **A) Go vertical.** Stack rooms/floors with up-facing jigsaw connectors — a mansion *spire* or a trial
  *shaft*. Small footprint, dramatic height/depth, fits the sky-island form. (Precedent: the Outpost tower.)
- **B) Go wide on a new "grand" island tier.** A bigger island (radius ~20–26) hosting a bounded horizontal
  cluster — a mansion *wing* / a trial *complex*. More authentic sprawl, but costs a bigger island, careful
  bounding, and more generation budget.
- **C) Compact hybrid.** A 2–3 floor block with a few rooms per floor and internal stairs — a "condensed full
  structure." Medium footprint, medium height.

**Recommendation:** Mansion → **C (compact hybrid, leaning vertical)** — reads as a real mansion without 60×60.
Trial Chamber → **buried complex** (it's *meant* to be underground; use `sink`). *Your call.*

### P2 — Bounding the jigsaw so it stays on the island
The jigsaw grows outward from the start piece through connectors; left unbounded it walks off the island edge
(pieces floating or clipped). Vanilla mansions use a bespoke grid algorithm we don't have — we use plain
`JigsawPlacement` with a `depth` cap (villages, our Trade Post).

**Solution (combine all of these)**
- **Curated pool + low depth.** A deliberate connector graph: a START piece whose connectors lead only to a
  fixed, small set of room/stair pieces, ending in **terminal pieces** (no further connectors). Depth ~3–5.
- **Designed, not infinite** — think Trade Post (a plaza that branches a *known* number of shops), not "endless
  village."
- **Fit-test the worst case.** Size `pad` + island radius for the *maximum* assembled footprint, not the
  average.
- **`minecraft:empty` fallback** — an unfilled connector resolves to its `final_state`, so a short assembly
  degrades into a wall rather than a hole.

**Risk:** up-facing (vertical) connectors are standard jigsaw but **unproven in our setup** — needs a spike.

### P3 — Good news: the reward mechanics are block-entities
The 1.21 trial mechanics are **block-entity-driven**, which is exactly how we already author structures:
- **Trial spawner** (`minecraft:trial_spawner`) — its BE config holds the mob, the loot-on-clear, and the key
  reward; it runs the wave fight and drops a Trial Key natively. We just place the block + BE-NBT (exactly like
  the Dungeon's `mob_spawner`).
- **Vault** (`minecraft:vault`) — BE config holds the loot table + the required key item; an *ominous* vault is
  the ominous config. Place + BE-NBT.

So the **entire Trial Chamber loop is native** once the blocks are placed — no custom wave logic to write.

**Risk:** the exact `trial_spawner` / `vault` NBT schema (config sub-tags, key item ids) must be verified before
authoring — a quick spike.

### P4 — Spawning hostiles across many rooms
Today's mob mechanisms are **centre-anchored**: the `mobs` pack spawns at the island centre ±1, and
`iron_golems` spawns at the centre. Fine for a one-room structure; wrong for a multi-room one (everything clumps
in the middle).

**Options**
- **A) Trial spawners do it for free** (Trial Chamber) — each room's trial spawner spawns its own waves where it
  sits. No extension needed. ✅
- **B) Per-room marker spawn** (Mansion) — extend the proven bed-scan idea (the Trade Post spawns a villager at
  every bed): scan the assembled structure for a "mob marker" block and spawn that room's hostile there. One new
  scan, reusing an existing pattern.
- **C) Vindicator/evoker `mob_spawner`s** — works mechanically but is non-vanilla (these don't normally spawn
  from spawners) and keeps respawning.

**Recommendation:** Trial Chamber = **A**; Mansion = **B** (one evoker marker in the evoker room → totem,
vindicator markers elsewhere).

### P5 — The gating bootstrap (your idea, fleshed out)
Circular dependencies in vanilla: totems come only from evokers (mansion); trial keys come only from trial
spawners (trial chamber); and the old plan wanted those very items as the structure's recipe/entry gate.

**Solution — small rare "gating chambers" (Tier 1):**
- **Vault Cell** — a tiny tuff/copper room with **1–2 trial spawners** (→ Trial Keys) and maybe a single vault.
  A reliable, repeatable Trial-Key source. A `rare_structures` feature, buried (`sink`).
- **Evoker Cell** — a small dark-oak cell holding **one evoker** (→ a Totem of Undying on the kill) — the
  bootstrap totem. A `rare_structures` feature.
- Both need **no recipe** (random finds, like the igloo / buried dungeon), so the grand seeds become *optional
  power-ups* rather than mandatory gates.

**Decided:** the grand Trial Chamber is **self-contained** — its own entry spawners drop the keys for its own
vaults. Vault Cells are a *separate*, accessible source (a bootstrap / a quick key without the full chamber).

### P6 — Generation budget & performance
Big structures are thousands of blocks. Placement is already **tick-budgeted** (no single-tick stalls) and the
jigsaw assembly is one-shot, so this is fine **if bounded** (P1/P2). Keep the piece count modest (a spire of
~5 floors, or a complex of ~6–9 rooms) and prefer fewer larger pieces over many tiny ones.

### P7 — Player access & navigation
A multi-floor/room structure needs an obvious **entrance**, internal **stairs/ladders** (authored into the stair
pieces), and — for a buried trial chamber — a surface descent (the Dungeon's stairwell, scaled up). Because
vaults gate the *good* loot, the layout can stay roughly linear: fight in → earn keys → open vaults → out.

---

## Woodland Mansion — proposed design
- **Form:** *(decided)* a **multi-floor mansion** — a few rooms per floor + internal stairs, going **up** as well
  as out — on the **new larger "grand island" tier.** Wider than a pure spire, taller than a single wing, kept a
  bit smaller than a full vanilla sprawl.
- **Palette:** dark oak + birch, cobblestone accents, red carpet, the mansion's signature look.
- **Jigsaw pool:** an **entrance hall** (start) → connectors → a handful of iconic rooms (vindicator room,
  fake-end-portal room, checkerboard/secret room, library, an arena hall) + **stair pieces** + the **evoker
  room** (rare in the pool, or guaranteed once — decision).
- **Mobs:** vindicators via per-room markers (P4-B); the evoker room (**guaranteed**) gives an evoker → **Totem
  of Undying** — so the mansion is a *reliable* totem destination.
- **Loot:** room chests on the vanilla mansion tables + the totem.
- **Seed:** a craftable destination — e.g. **Dark Oak + Diamond + Evoker banner** (the banner is dropped by the
  bootstrap **Evoker Cell**'s evoker, so the cell gates the mansion seed; thereafter the mansion farms totems).

## Trial Chamber — **✅ Built** (v0.28.0)
Shipped as `TrialChamberTemplates` → `data/skyseed/structure/trial_chamber/chamber.nbt`, on its own
`trial_chamber` theme (a larger, thicker rocky island, radius 14–18) with a craftable **Trial Chamber Skyseed**
(tuff bricks + copper blocks + a diamond). **v1 is a single rotated template, not a multi-piece jigsaw** — one
buried 11×11 tuff/copper arena (`sink` 7) holds a centre **breeze** trial spawner + an **ominous vault**, four
more trial spawners (zombie/skeleton/spider/breeze) each near one of three regular vaults, nine hanging lanterns,
and a corner ladder shaft punched up to the surface as the only tell. The spawner helper sets both `normal_config`
and `ominous_config`, so Bad Omen drives the ominous loop (ominous keys → the centre ominous vault → heavy core).
*Follow-up:* split the arena into a modular jigsaw pool (entrance → corridors → intersections → boss) for layout
variety, per the design below.

### Original design (the jigsaw target for a future pass)
- **Form:** *(decided)* a **buried complex** carved down into the island via `sink` — a surface entrance
  descends into the multi-room chamber; the grand island is built *thicker* so there's body to carve into.
- **Palette:** tuff + copper family (cut/chiseled copper, copper bulbs, tuff bricks).
- **Jigsaw pool:** an **entrance/descent** → **corridors** (trial spawners → keys) → **intersections** (vaults)
  → a **boss chamber** (a Breeze trial spawner + an ominous vault for the mace + heavy core).
- **Mechanics:** native trial spawners + vaults (P3). **Self-contained:** the entry spawners drop the regular
  keys for the chamber's regular vaults; the boss spawner drops the ominous key for the ominous vault.
- **Loot:** vault loot, copper bulbs, **mace components + heavy core** from the ominous vault — the top
  overworld payoff.
- **Seed:** Copper + Tuff — **no trial key in the recipe** (the chamber is self-contained). *(Decided.)*

## Small gating chambers (Tier 1) — proposed design
- **Vault Cell** *(decided host: Ancient)* — a `rare_structures` feature on the **Ancient** island: a tiny
  **buried** (`sink`) tuff/copper room with 1–2 trial spawners (→ Trial Keys) and maybe a single vault. The
  reliable, repeatable Trial-Key source; you dig into it like the buried dungeon.
- **Evoker Cell** *(decided host: Forest in `minecraft:dark_forest`)* — a `rare_structures` feature, biome-gated
  to a dark-oak Forest (like the Trail Ruins' taiga gate): a small dark-oak cell with one evoker → a **Totem of
  Undying + evoker banner** (the banner gates the Mansion seed). The bootstrap totem.
- **Chances:** 5% each (in line with the other rares; tune later).

**Status: ✅ Built** (v0.27.0). Both are `RareStructureTemplates` cells, generated to `.nbt`. **Evoker Cell**
(`skyseed:evoker_cell/cell`) — a 7×7 sealed dark-oak room (red carpet, bookshelves, `chests/woodland_mansion`),
the evoker from the rare entry's `mobs` pack; 5% on `forest` gated to `minecraft:dark_forest`. **Vault Cell**
(`skyseed:vault_cell/cell`) — a 7×7 tuff/copper room (`tuffMix`: tuff_bricks + cut copper + chiseled tuff), two
configured `trial_spawner`s (zombie + skeleton) + a default `vault`, buried via `sink: 5`; 5% on `ancient`.
**Spike result:** a `trial_spawner` is configured with `spawn_data: {entity:{id}}` + `normal_config:
{spawn_potentials:[{weight,data:{entity:{id}}}], total_mobs, simultaneous_mobs}` (floats); the **default vault
needs no config** (its default `key_item` is already `trial_key` with the reward loot table). Verified the blocks
place + configure; the clear→key→vault loop is native (needs a hands-on playtest).

> **Correction for the Mansion build:** evokers drop a **Totem of Undying**, *not* an "evoker banner" (the
> illager/ominous banner comes from raid captains). The Evoker Cell therefore yields a totem; the Mansion-seed
> recipe gate should use the **totem** (or another item), not an evoker banner.

---

## Feasibility scorecard (proven vs needs a spike)
- ✅ Multi-piece jigsaw assembly (Trade Post, depth 2).
- ✅ BE-NBT survives placement + rotation (spawners, loot chests, brushable blocks).
- ✅ `sink` burying; `rare_structures` + `biomes` gating; tick-budgeted placement.
- ⚠️ **Vertical (up-facing) jigsaw connectors** — standard jigsaw, unproven here. **Spike.**
- ⚠️ **`trial_spawner` / `vault` NBT schema** — verify the config tags before authoring. **Spike.**
- ⚠️ **Per-room marker spawning** — a small extension (model: the Trade Post bed-scan).
- ⚠️ **Bounding the assembly to the island** — needs fit-testing for the worst-case footprint.

---

## Decisions

### Locked (round 1)
1. **Mansion form** → a **multi-floor mansion with a real footprint** on a **new, larger "grand island" tier** —
   a combination of the "tall spire" and "wide wing" options, kept a bit smaller than a full vanilla sprawl. The
   island can be physically bigger than a Large island, and the mansion goes *up* several floors as well as out.
2. **Trial Chamber form** → a **buried complex** carved down into the island via `sink`.
3. **Totem access** → the grand Mansion **guarantees an evoker room** (a reliable totem destination); rare
   **Evoker Cells** give an early bootstrap totem before you can craft the mansion seed.
4. **Trial-key loop** → the grand Trial Chamber is **self-contained** (its own trial spawners drop the keys for
   its own vaults); **Vault Cells** are a separate, accessible bootstrap / extra key source.

> **Implication:** a **bigger-than-Large island** carries each grand structure — and, for the buried trial
> complex, a *thicker* body to carve down into. Whether this is its own size or simply the future **"Huge" tier**
> (see below) is a build-time call.

### Locked (round 2)
5. **Gating-chamber hosts** → the **Vault Cell** is a rare structure on the **Ancient** island (its deep
   tuff/deepslate body suits a buried key-cell); the **Evoker Cell** is a rare structure on a **Forest grown in
   a `minecraft:dark_forest` biome** (biome-gated, vanilla-faithful, like the Trail Ruins). Chances TBD (a few %).
6. **Reaching the grand structures** → **their own crafted seeds, for now** (Mansion: dark oak + diamond + evoker
   banner; Trial Chamber: copper + tuff). No rare-variant placement yet — see the future ambition.
7. **Scale** → **decide during the build.** Start moderate (grand island ~radius 18, mansion ~3 floors, trial
   complex ~6 rooms), generate it, and scale up if it reads too small in-game.

### Future ambition (deferred — leans on the Nether)
A planned **"Huge" island tier** — huge versions of terrain islands, gated behind **rare Nether ingredients** —
onto which the grand structures would appear as a **rare occurrence**: the **Mansion on a Huge dark-forest
Forest**, the **Trial Chamber on a Huge Ancient**. That's the "rare variant" path, and it waits on the Huge tier
plus the Nether dimension (see the CurseForge roadmap). Building the grand structures as **their own seeds first**
keeps them ready to drop onto Huge islands later — the jigsaw pool and `rare_structures` plumbing are the same.
