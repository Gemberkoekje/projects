# SKYJIGSAWPLAN — Structural diversity through deeper jigsaw use

> Goal: make full use of the jigsaw system so villages look like *actual villages*, mansions have
> *different shapes*, and Nether fortresses *sprawl over the void* the way they do in vanilla. Today every
> village / mansion / fortress is nearly identical because each structure has one shallow pool that always
> lands the same way. This plan turns that into varied, organic, over-the-void layouts.

Status: **proposed** (not started). Companion to `SKYNETHERPLAN.md`; the README is the living index.

---

## 1. Diagnosis — why everything looks the same

The engine is not the problem. `GenerationJob.placeStructures()` calls `Jigsaw.place(...)` →
`JigsawPlacement.generateJigsaw(level, pool, target, depth, origin, useExpansionHack=false)`. That is the
**full vanilla recursive jigsaw assembler** — the same code villages and bastions use. It already supports
deep recursion, random rotation, weighted pools, and per-piece structure processors.

What we feed it is shallow and singular. Current state (`data/skyseed/skyseed/theme/*.json` →
`data/skyseed/worldgen/template_pool/*`):

| Structure | `depth` | Start pool | Onward pools | Result |
|---|---|---|---|---|
| **Hamlet** | **1** | `hamlet/cottages` (3 variants) | — | One cottage. Depth 1 = *start piece only*. 3 looks total. |
| **Trade Post** | 2 | `trade_post/start` = **1 plaza** | `buildings` (5 shops) | Fixed plaza, 4 shops at the **same four edge midpoints**. Only shop *identity* varies; the silhouette never does. |
| **Village Center** | 2 | `village_center/start` = **1 plaza** | 4× `hall_*` (**1 element each**) | **Byte-identical every time.** Zero variety. |
| **Woodland Mansion** | 2 | `woodland_mansion/start` = **1 core** | `wings` (3 variants) | Fixed core, wings at fixed connectors. Footprint never changes. |
| **Nether Fortress** | **1** | `nether_fortress/fortress` = **1 piece** | — | One monolithic 12-long bridge+keep. Identical every throw. |
| **Trial Chamber** | 2 | `trial_chamber/start` = 1 hub | `rooms` (5) | Hub fixed; room *identities* vary at fixed connectors. |

Three root causes, in order of impact:

1. **Shallow `depth`.** With `depth: 1` nothing branches at all (Hamlet, Fortress). With `depth: 2` the start
   piece's connectors fire **once** and the pieces they pull are terminal — no second hop, no wandering.
2. **Single-element start pools.** Every assembly begins from the *same* piece. Random rotation flips
   orientation, but the skeleton (connector positions, piece sizes) is constant, so the shape is constant.
3. **No connective tissue.** Buildings attach **directly** to the start piece at its fixed connector slots
   (e.g. `TradePostTemplates.plaza()` puts four connectors at the four edge midpoints, and each shop's onward
   jigsaw points at `minecraft:empty` — a dead end). Vanilla villages get their organic sprawl from **street
   pieces** that chain into each other and hang houses off the side. We have none.

---

## 2. The core technique: streets + depth + terminators

Vanilla `plains` villages are not a big template — they are a tiny **street pool** (straights, corners, T- and
cross-junctions) that recursively connects to itself, with houses hanging off side connectors, and a weighted
**empty terminator** so a run of street randomly ends. Deep `depth` (vanilla villages use 6–7) lets the street
wander; rotation + which-piece-rolls + where-a-run-terminates makes every village unique from the same handful
of pieces.

We adopt the same shape. The pattern, concretely:

- **A connective pool** (`streets`, `corridors`, `bridges`, …) whose pieces each expose:
  - one or more **onward** connectors (`name: street`, `target: street`, `pool: <self>`) so the path continues, and
  - **side** connectors (`name: lot`, `target: door`, `pool: <buildings>`) so buildings attach.
- Pieces of several **shapes** in that pool, weighted: straight, corner-left, corner-right, T, cross, plus a
  short **dead-end cap**. Include `minecraft:empty` as a weighted fallback element so a connector can simply
  stop — that is what makes runs different lengths.
- **Bump `depth`** to ~6–7 so the path actually recurses.
- **Buildings keep a single door connector** (`pool: minecraft:empty`) so they don't sprout further — variety
  comes from the streets, not from buildings exploding.
- **Multiple start variants** (2–4 plazas / wells / cores) so the seed of the whole thing differs too.

The handshake is the existing `StructureParts.jig(name, target, pool, finalState)` convention already used in
`TradePostTemplates` (`plaza_edge` ↔ `building_door`). We just add a `street ↔ street` self-handshake and a
`lot ↔ door` building handshake. No engine change to make this work — it is purely new `.nbt` pieces + pool
JSON + a `depth` bump.

---

## 3. The skyblock superpower: build over the void

On a skyblock island this technique gets *more* interesting than vanilla, because of how rigid jigsaw placement
behaves:

- Every element is `"projection": "rigid"` → placed at the **exact** connector-aligned position, with **no
  heightmap snapping and no ground-support check**. Vanilla rejects an overlapping piece; it does **not** reject
  a piece that floats over air.
- Each of our templates **carries its own floor course**. So a street/bridge piece that recurses past the
  island's edge simply lays its own planks **out over the void** — an instant pier or walkway, exactly the
  "village making a little pier over the void" the brief asks for.
- The island only needs to support the **start** piece (that is all `pad` levels). Everything the path reaches
  beyond the pad is free to hang in the air.

To make overhangs read as *intentional* rather than glitchy:

- Give over-void pieces an optional **under-support**: a few stilt columns / fence hangers / stair brackets
  baked into the bottom of the piece (cosmetic, cheap, no engine support needed).
- Author a dedicated **`pier`/`balcony`/`bridge_span`** sub-pool so the over-void runs *look* like piers
  (railings, lampposts, mooring posts) instead of a street that wandered off a cliff.
- For the Nether Fortress this is the whole point: vanilla fortresses are mostly air-spanning bridges. Our
  fortress should become a **bridge-span pool** that marches out over the lava sea and the void, with towers,
  balconies and stair-downs hung off it.

**One thing to validate first (Phase 0 spike):** confirm that `generateJigsaw` via `Jigsaw.place` really does
place rigid pieces freely over void here, and find the effective **`maxDistanceFromCenter`** cap of that
convenience overload (it is the `/place jigsaw`-style call). If the cap is too small for the sprawl we want,
add a sibling `Jigsaw.place(... maxDistance)` overload in `compat/Jigsaw.java` that calls the
full-parameter `generateJigsaw` with an explicit radius. This is the only *likely* engine touch.

---

## 4. Per-structure deliverables

### 4a. Trade Post → a real little village
- Replace the single plaza with a **`village/streets` pool**: `street_straight`, `street_corner`,
  `street_tee`, `street_cross`, `street_end`, weighted, each a 1-wide-ish dirt/gravel path with side `lot`
  connectors and its own ground.
- Start pool `village/well` with 2–3 variants (a well, a market cross, a campfire green).
- **Lot/building pool**: the existing shops + new **non-shop** lots for texture: a **wheat field** (tilled
  soil + water + crops, fenced — the "little wheat fields" ask), a **small house**, an **animal pen stub**, a
  **lamppost/garden** filler. Fields and gardens have no villager, so they read as connective scenery.
- `depth: 6`. Add a weighted `minecraft:empty` to the streets pool so paths fork and stop unevenly → twists
  and turns.
- A **`pier` street variant** that the path can roll when heading off the island edge (railings + a mooring
  post + a bench) so the walkway-over-void shows up naturally.

### 4b. Village Center → the grand version of the same
- Same street system, richer **start** (a bell + green), and the four trading halls become a **`halls` pool**
  with **2–3 shape variants each** (currently 1 each — the worst offender). Keep all 13 professions reachable
  by ensuring the hall set still covers them across whatever rolls.
- Keep `iron_golems: 1` at the centre; raise `pad` only enough to seat the start (sprawl handles the rest).

### 4c. Woodland Mansion → different shapes
- **2–3 core start variants** (different hall footprints / entrance sides) instead of the one `core`.
- Turn `wings` into a small **room graph**: wings expose their *own* onward connectors into a `rooms` pool
  (corridor, library, checker hall, prison, balcony, stair-up to a partial second storey), `depth: 4–5`, with
  an empty terminator so the manor ends raggedly — a different silhouette each throw.
- Optional **second-storey** pieces via a vertical (`up`) connector, reusing the proven vertical-jigsaw trick
  (memory: connector must sit at the `.nbt` bbox **edge**, not flush on an inset wall, or it's rejected).
- Keep the guaranteed evoker (the Totem source) on the core so the reward is unconditional regardless of shape.

### 4d. Nether Fortress → sprawl over the void
- Break the monolith into a **`nether_fortress` pool**: `bridge_span` (4–6 long, self-connecting), `crossing`
  (4-way bridge junction), `tower_keep` (the current keep, with the blaze spawner), `balcony` (wart garden +
  loot, hung off a span), `stair_down` (descends a level so the fortress steps down over the lava), and a
  `span_end` cap.
- `depth: 6–7`, generous `maxDistance`, so a fortress becomes a branching network of walkways striding out
  over the lava sea and the void — like vanilla. Each span carries its own magma channel + arches, so it
  self-supports visually.
- Re-balance loot/spawners so a sprawling fortress doesn't stack five blaze spawners (cap via separate
  `keep` pool used once near the start, with `bridge`/`balcony` pieces being the repeatable ones).

### 4e. Reuse for the rest (cheap follow-on)
- **Trial Chamber / Ocean Monument / Bastion**: once the connective-pool pattern exists, give each a small
  `corridor`/`gallery` connective pool + an empty terminator + a `depth` bump for free layout variety.
- **Hamlet**: smallest touch — `depth: 2` and a one-path `streets` micro-pool so a hamlet is a cottage **plus**
  a short lane with a garden or a single field, still tiny but no longer a lone box.

---

## 5. Engine / support work (the real gotchas found in the code)

These are concrete and must be handled or sprawling structures will half-break:

1. **Bounded post-assembly scans don't follow the sprawl.** After `Jigsaw.place`, `GenerationJob` runs three
   passes that scan a **fixed box around the origin**:
   - `linkConnections(level, origin)` with `LINK_RADIUS = 16`, `LINK_DOWN = 2` — links fences/panes/walls.
   - `Traps.applyAfterJigsaw(level, origin)` — re-adds plates/tripwire.
   - `spawnVillagersAtBeds(origin, pad)` — spawns a villager at every bed, scanned by **`pad`**.

   A structure that now reaches 40+ blocks out will have **beds with no villager**, **unlinked railings** and
   **dropped trap blocks** beyond the scan box. **Fix:** derive the scan bounds from the **actual placed
   structure bounding box** (capture the assembled `StructurePiecesBuilder`/`BoundingBox` from the jigsaw call
   and pass it to the three passes), or at minimum scale `LINK_RADIUS` / the villager scan with the structure's
   `maxDistance`. This is the single most important support change.

2. **`maxDistanceFromCenter` cap** of the convenience `generateJigsaw` overload (§3). Add an explicit-radius
   overload in `compat/Jigsaw.java` if the default is too tight for fortress/village sprawl.

3. **Overlap rejection silently drops branches.** Rigid placement rejects a piece whose bbox intersects an
   already-placed piece. This is *desirable* (it ends runs and varies shape) but means authors must (a) keep
   piece bounding boxes tight and (b) accept that a loop-back lane just stops. Author defensively; don't rely
   on any given connector always firing.

4. **The roofed-connector-at-bbox-edge rule** (already learned, memory + Trial/Mansion work): a connector on a
   piece with a roof/overhang must sit at the piece's `.nbt` bounding-box **edge**, never flush against an
   inset wall, or the attached piece is rejected for overlap. Bake this into the authoring checklist.

5. **Frame spike.** `placeStructures()` already notes that `generateJigsaw` runs un-budgeted in one tick.
   Deeper recursion = many more pieces = a bigger spike. Mitigation options, in order: keep piece counts
   sane (terminators!), then **defer big structures to a later tick** than blocks/trees (the code already
   suggests this), then as a last resort chunk the assembly.

6. **`pad` vs sprawl.** `pad` only levels the **start** foundation; that is fine and intended — the sprawl is
   *meant* to leave the pad and go over the island edge / void. Do **not** grow `pad` to cover the whole
   structure (that would flatten the island and defeat the over-void look). Grow `pad` only enough to seat the
   start piece cleanly.

7. **Determinism / golden master.** `islandOutputIsStable` fingerprints the **plan** (the jigsaw *site* count,
   not the assembled blocks — assembly happens in `GenerationJob`, outside the plan). Changing pools/`depth`
   does **not** change the plan fingerprint, so the golden master stays green. The structure gametests
   (`*IsNetherNativeWithItsJigsaw`, `villageCenter…`) assert the **start pool path** is present in the plan —
   still true. New pieces/pools are additive. We should *add* assertions (below), not fear breaking these.

---

## 6. New data knobs (small `JigsawConfig` additions, optional)

`JigsawConfig` today: `pool, target, depth(1), pad(6), ironGolems(0), sink(0)`. Candidate additions:

- **`max_distance`** (int, default = vanilla) — let a theme widen the sprawl cap for fortresses/villages.
- **`defer_ticks`** (int, default 0) — place this structure N ticks after blocks/trees, to spread the spike.
- Possibly a **`support`** flag later if we want the engine (not the templates) to drop stilts under
  over-void pieces; prefer baking supports into the pieces first and only adding engine support if authoring
  proves too tedious.

Keep these optional with backward-compatible defaults so every existing theme is unchanged.

---

## 7. Authoring workflow (unchanged tooling, new conventions)

- Pieces stay **code-authored** in `*Templates.java` → `.nbt` via `DevStructureGenerator` (dev-only, write-if-
  absent). Pools are hand-written `worldgen/template_pool/*.json`.
- **Connector naming convention** to adopt project-wide: `street`/`street` (path self-link), `lot`/`door`
  (building attach), `up`/`down` (vertical), `span`/`span` (fortress bridge). Document in `StructureParts`.
- Always place the start anchor (`name: minecraft:bottom`) **last** (existing rule in `StructureParts.anchor`).
- Respect the bbox-edge connector rule (§5.4).

---

## 8. Phasing

- **Phase 0 — Spike (½ day).** A throwaway `streets` pool with 3 pieces + a debug seed at `depth: 6`. Confirm:
  (a) rigid pieces place over void, (b) the real `maxDistance` cap, (c) which post-assembly scans miss the
  sprawl. Decide the §5.1 scan-bounds fix here.
- **Phase 1 — Scan-bounds + Trade Post village.** Implement §5.1 (structure-bbox-driven scans), then build the
  Trade Post street/lot system incl. the **wheat field** and a **pier** variant. First visible payoff.
- **Phase 2 — Village Center.** Multi-variant halls + the street system; keep all 13 professions + golem.
- **Phase 3 — Woodland Mansion footprint variety.** Multiple cores + room graph (+ optional 2nd storey).
- **Phase 4 — Nether Fortress sprawl.** Span/crossing/tower/stair-down pool over the void + loot/spawner cap.
- **Phase 5 — Reuse & polish.** Hamlet lane, Trial Chamber / Ocean Monument / Bastion corridor pools,
  under-support polish on over-void pieces.

Each phase ships independently and is committed on its own (one feature per commit, per project convention).

---

## 9. Tests

- Extend the structure gametests: for each reworked theme, assert the **connective pool exists** and contains
  `> N` weighted elements (catches "someone shipped a 1-element pool again").
- A **villager-coverage** test for the village: assemble in the gametest region and assert a villager spawned
  for **every** bed in the placed bounding box (guards the §5.1 scan-bounds fix).
- A **fence-link** spot check across a multi-piece span (guards §5.1 for `linkConnections`).
- Keep the golden master as-is (it stays green; §5.7).

---

## 10. Open questions

- **How big is too big?** Pick a `maxDistance` that feels like a hamlet/town/fortress without lagging or
  colliding with neighbouring islands (placement overlap-fit already nudges islands apart — a sprawling
  structure may need a bigger keep-out radius around its island).
- **Should over-void pieces auto-stilt** (engine) or carry baked supports (templates)? Start with templates.
- **Villager counts** on a big village — cap so a sprawling Village Center doesn't spawn 30 villagers (e.g.
  cap beds, or only some lots carry beds).
- **Profession coverage** when halls are randomised — guarantee the 13 are reachable (a "must-include" subset
  in the hall pool, or a deterministic first-ring).
