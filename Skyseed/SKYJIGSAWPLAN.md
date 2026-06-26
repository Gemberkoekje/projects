# SKYJIGSAWPLAN — Structural diversity through deeper jigsaw use

> Goal: make full use of the jigsaw system so villages look like *actual villages*, mansions have
> *different shapes*, and Nether fortresses *sprawl over the void* the way they do in vanilla. Today every
> village / mansion / fortress is nearly identical because each structure has one shallow pool that always
> lands the same way. This plan turns that into varied, organic, over-the-void layouts.

Status: **the villages are shipped; mansion / fortress / structure-corridor diversity remain.** Companion to
`SKYNETHERPLAN.md`; the README is the living index. Per-release detail in [CHANGELOG.md](CHANGELOG.md).

## Shipped (one line each)

- **Marker path/bridge surfacing + bbox-scaled post-assembly scans** (§3a, §5.1) — `PathSurfacer` resolves a connective
  piece's markers into terrain-aware dirt paths on the island and self-railing wooden-slab bridges out over the void,
  and the villager / connection-link / path passes follow the structure's `reach` instead of a fixed box — **v0.68.0**.
- **Trade Post → a real street village** (§4a) — a `streets` pool (straights / corners / tees / crosses + empty
  terminators) at `depth`, `lot` connectors pulling shops / fields / gardens / over-void piers, a `shop_` cap holding
  it to a tidy 2–4 shops, biome-styled (desert / savanna / snowy / taiga) — **v0.69.0–0.70.0**.
- **Hamlet → a green + a short lane**, reusing the trade post's `lots` pool, biome-aware — **v0.74.0**.
- **Village Center → a bigger Trade Post**, reshaped into a 3-island cluster around a void centre with an anvil
  capstone and a guaranteed 4–6 shops (§4b) — **v0.89.0–0.93.0**.

What's left is the **non-village** diversity: Woodland Mansion footprints (§4c), Nether Fortress sprawl over the void
(§4d, reusing `PathSurfacer` for the bridges), and the cheap corridor-pool reuse for Trial Chamber / Ocean Monument /
Bastion (§4e). The diagnosis + technique (§1–3) and support notes (§5–7) below stand as the reference those reuse; the
shipped sections are kept only for that context.

---

## 1. Diagnosis — why everything looks the same

The engine is not the problem. `GenerationJob.placeStructures()` calls `Jigsaw.place(...)` →
`JigsawPlacement.generateJigsaw(level, pool, target, depth, origin, useExpansionHack=false)`. That is the
**full vanilla recursive jigsaw assembler** — the same code villages and bastions use. It already supports
deep recursion, random rotation, weighted pools, and per-piece structure processors.

What we fed it was shallow and singular. The **original** state below (the **Hamlet / Trade Post / Village Center** rows
are now fixed — see *Shipped*; the **Mansion / Fortress / Trial Chamber** rows still stand) (`data/skyseed/skyseed/theme/*.json` →
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
- **Buildings carry their own floor course**, so a shop or house that lands past the island's edge brings its
  planks with it. **Paths don't bake a floor at all** — they use the marker-surfacing pass in §3a below, so a
  street that recurses out over the void becomes a boardwalk and over grass becomes a worn dirt path. Either
  way the "village making a little pier over the void" the brief asks for comes essentially for free.
- The island only needs to support the **start** piece (that is all `pad` levels). Everything the path reaches
  beyond the pad is free to hang in the air.

### 3a. Marker-driven path surfacing — terrain-aware paths *and* free bridges (preferred)

> **✅ Implemented as `PathSurfacer` (v0.68.0).** The design below stands as the reference the Nether Fortress (§4d)
> reuses for its over-void bridges; the code is now the source of truth for the details.

Rather than bake a fixed plank floor into every path piece (wrong-looking on grass, oddly floating over void),
the **connective path pieces place only a sentinel marker** — a reserved block (e.g. `purple_wool`) one block
**above** the intended path tile — and a post-assembly pass resolves each marker by looking at what sits
directly **under** it:

- under = **dirt / grass / sand / terrain** → set that tile to a **path surface** (`dirt_path`, with a little
  `gravel` / `cobblestone` mixed in for texture) — the path *becomes* the island's ground, re-textured;
- under = **air (void)** → build a **bridge**, not just a plank: lay a **wooden slab** for the deck, and for
  each horizontal neighbour that is **not** itself a path tile *and* is also over void (an open edge), add a
  **fence railing** with a **full wood block** beneath it as the edge beam. That one neighbour rule yields a
  free, barebones-but-complete bridge — a slab deck, railings down both exposed sides, and a railed cap
  wherever a run dead-ends over the void — and it **scales with path width automatically**: a 1-wide path rails
  both sides, a 3-wide path rails only its two outer columns, and where the deck meets island ground or
  continues as more path that side *has* a neighbour, so it gets no railing and the path stays walkable;
- finally delete the markers (two-phase — see below).

Why this is the right tool:

- **Terrain-aware for free.** One path adapts to grass, sand, snow *or* void — so it also matches the **biome**
  of whatever island it grows on, not just the void case. That's a bonus the baked-floor approach can't give.
- **The order already supports it.** `tick()` places terrain + scatter first and only then runs
  `placeStructures()` (≈ line 117), so when the pass reads "the block below," the island ground is already in
  the world and the void is genuinely air. The pass slots in beside the existing post-jigsaw passes
  (`linkConnections`, `Traps.applyAfterJigsaw`) and **reuses the same structure-bbox scan** as the §5.1 fix.
- **Marker one block *above* the tile, on purpose** — so the tile we need to read (terrain or air) is left
  untouched and unambiguous; a marker placed *on* the tile would erase the very information we read.
- **Buildings keep their solid baked floors.** Markers are only for the path/street network — exactly the part
  that sprawls over void.

Details to mind:

- **Two-phase, snapshot first.** The bridge rule reads each marker's **neighbours** *and* writes to non-marker
  side columns, so the pass must (1) scan the bbox and snapshot the full marker set, (2) resolve decks + edges
  reading from that snapshot, then (3) clear the markers **last**. Removing markers as you go would make an
  already-resolved neighbour look like "not a path" and rail the wrong edges.
- **Rail only an actual drop.** Count a side as exposed only when the neighbour column is itself over void; a
  neighbour that is solid ground means the bridge is meeting the island, so leave it open (no fence post
  stranded on the grass).
- **Height step / cross-section.** `dirt_path` is ~0.94 tall and a bottom slab is 0.5, so a boardwalk-meets-
  path seam shows a half-step, and a full-block edge beam stands ~0.5 proud of the slab deck (a curb under the
  railing — fine for a bridge). Use a **top slab** / full plank for the deck if you want the tops flush; tune
  in the spike.
- **Reserved marker.** `purple_wool` works as a sentinel *if* we never use it decoratively in structures (we
  don't); document it as reserved, or pick any block we'll never place for real.
- **Optional stilts.** Over deep void the pass can occasionally drop a fence/post under the slab so long piers
  read as supported (cheap; no engine support needed).
- **Vanilla processors can't quite do this.** A `RuleProcessor` can swap dirt → `dirt_path` (its
  `location_predicate` tests the world block at the *same* position), but it has no neighbour predicate to test
  the block **below**, so it can't choose slab-over-void. The custom post-pass is the justified tool.
- **Generalises later.** Other marker colours could drive other context rules (a "wall-base" marker that
  becomes a stilt over void, say). Keep v1 to the path case.

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

### 4a. Trade Post · 4b. Village Center — ✅ shipped (see *Shipped* above)
The Trade Post became the street village (streets / lots / fields / gardens / piers, a `shop_` cap, biome variants); the
Village Center became *a bigger Trade Post* — a 3-island cluster around a void centre with an anvil capstone and 4–6
shops, **not** the multi-hall, all-13-professions build §4b first imagined. Versions in *Shipped*.

### 4c. Woodland Mansion → different shapes
- **2–3 core start variants** (different hall footprints / entrance sides) instead of the one `core`.
- Turn `wings` into a small **room graph**: wings expose their *own* onward connectors into a `rooms` pool
  (corridor, library, checker hall, prison, balcony, stair-up to a partial second storey), `depth: 4–5`, with
  an empty terminator so the manor ends raggedly — a different silhouette each throw.
- Optional **second-storey** pieces via a vertical (`up`) connector, reusing the proven vertical-jigsaw trick
  (memory: connector must sit at the `.nbt` bbox **edge**, not flush on an inset wall, or it's rejected).
- Keep the guaranteed evoker (the Totem source) on the core so the reward is unconditional regardless of shape.

### 4d. Nether Fortress → sprawl over the void
> **✅ DONE (v0.94.0–0.95.1).** The monolith is a bounded jigsaw network — `keep` (start, blaze spawner), self-connecting
> `span_bridge`, a 4-way `span_crossing` (branching), a garden `span_balcony`, a `span_stair_down` (descends a level
> over the lava sea), and a wart-garden `end` terminator. `depth: 5` plus the **`span_` cap** (≤ 8 span pieces, surplus
> re-stamped as ends) bound the total across all branches (modelled on vanilla's `NetherFortressPieces` but ~5 piece
> types vs 13, depth 5 vs MAX_DEPTH 30). The over-void spans carry their own arcade, so no `PathSurfacer` needed.

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
- **Hamlet**: ✅ shipped (v0.74.0) — a green + a short lane reusing the trade post's `lots` pool.

---

## 5. Engine / support work (the real gotchas found in the code)

These are concrete and must be handled or sprawling structures will half-break:

1. **Bounded post-assembly scans don't follow the sprawl.** **✅ Fixed (v0.68.0): a `reach` knob** — the villager-bed,
   connection-link and path passes now scan the structure's declared `reach` instead of a fixed box (and skip unloaded
   chunks so a wide reach never force-loads the void), so a sprawling structure's beds, railings and trap blocks are all
   covered. (The original problem, for context: those passes scanned a fixed box around the origin —
   `linkConnections` at `LINK_RADIUS = 16`, the villager scan at `pad` — so a structure reaching 40+ blocks out left
   beds with no villager and unlinked railings beyond the box.)

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

- **Phases 0–2 — ✅ shipped (the spike, the scan-bounds/`reach` fix, the Trade Post street village, and the Village
  Center).** See *Shipped* up top for versions.
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
- A **marker-resolution** test (§3a): assemble a path that runs off the island edge in the gametest region,
  then assert **no sentinel markers remain**, on-terrain tiles became a path block, over-void tiles became a
  slab deck, and an exposed over-void edge gained a railing + edge beam (while a deck tile that meets ground
  did **not**).
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
