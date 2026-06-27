# SKYHUGEPLAN — Huge overworld islands

A tier **above `*_large`**: sprawling overworld islands with **internal cave systems** and a **rare** chance of a
**large structure** inside (a jigsaw dungeon, the large woodland mansion). The good news from a code audit: almost every
piece already exists — `Shape.cluster_offsets` (the archipelago used by the Village Center), `Shape.max_under_depth`
(the plateau cap), `RareStructure` (chance-gated jigsaw that replaces the normal one), and the underside-decoration
palette + `PondCarver`/`DecorationPlanner` as templates. The only genuinely new engine is the **cave carver**.

Scope: **overworld biome seeds only** (forest, rocky, desert, mushroom, frozen, meadow, badlands, ancient, lush,
aquatic). The structure/village/animal seeds are already sized to their builds and stay out.

---

## 1. Sizing — single huge island vs cluster (per seed type)

Two shapes, chosen per seed (both already supported by `Shape`):

- **(a) Single huge island.** Bump `radius` well past large (large ≈ 13–17 → **huge ≈ 24–32**), and **set
  `max_under_depth`** (≈ 16–20) so the teardrop underside is capped — otherwise a wide island dangles a ~radius-deep
  cone (the Shape doc warns of exactly this). Result: a broad plateau/landmass with a thick (but not bottomless) body —
  which is also where the **caves** live. Terrain themes lean on `top_dome` for a mountain/mesa profile.
- **(b) Cluster (archipelago).** `cluster_offsets` stamps the same shape at each offset — a ring of medium islands over
  a void centre, exactly the Village Center pattern (`[[0,0,19],[-16,0,-10],[16,0,-10]]`). Best where "several islands"
  reads better than "one slab."

**Starting per-seed allocation** (tunable — it's one theme field):

| Huge seed | Shape | Why |
|---|---|---|
| `huge_aquatic` | **cluster** (4–5 isles) | an ocean archipelago — the natural cluster case, ponds/reefs per isle |
| `huge_mushroom` | **cluster** (3 isles) | a scatter of mycelium isles + giant mushrooms reads better than one slab |
| `huge_forest` | single | a sprawling forest landmass |
| `huge_rocky` | single (tall `top_dome`) | a true mountain — caves shine here |
| `huge_badlands` | single (tall `top_dome`, banded) | a towering mesa |
| `huge_frozen` | single | a glacier / ice sheet |
| `huge_meadow` | single | a vast flower-meadow plateau |
| `huge_ancient` | single | a deep deepslate slab — the premier cave host |
| `huge_lush` | single | a lush-cave island (caves are the whole point) |
| `huge_desert` | single (or cluster) | a vast dune sea; cluster it for a dune chain if preferred |

Recipe/onboarding (standard "add a seed" checklist): a **huge_* seed per chosen theme**. **Recipe — the middle row is
`ender pearl / <theme>_large_skyseed / blaze powder`** (`[ "TTT", "ELP", "TTT" ]`, where `T` is the theme's bulk
block): the `*_large` seed gated between an **End** farm drop (ender pearl) and a **Nether** farm drop (blaze powder),
so a huge island demands you've farmed *both* late chapters — *you want the huge island, you farm for it* — wrapped in
six of the theme's bulk block (the body). Same uniform middle for every huge seed; only the six `T` blocks change per
theme. Unique icon (the large icon, bigger / with a cave mouth), lang, `skyseeds` tag, guide entry, advancements.

---

## 2. Cave systems (the one new engine piece)

Huge islands have a thick body (capped teardrop / mountain), so there's room to hollow it out.

- **`CaveCarver`** (model on `PondCarver`: carve + contain). After pass 1 (body stamped) and before decoration, carve a
  small **3-D cave system** in the *interior* fill/core volume: a short random-walk of carving spheres (or 3-D noise
  threshold) producing a few connected chambers + tunnels. **Containment:** never carve the top `surface`+`fill_depth`
  skin (keep the surface intact) and never carve through the underside skin (no holes to the void) — leave a solid
  margin, the same "only where solid all around" discipline `PondCarver.containPond` already uses. Track carved cells
  (a `Set<Long>`) and the cave **floor / ceiling / wall** cells for decoration.
- **Cave decoration = the underside palette, reused.** `DecorationPlanner.hangUnder` already builds
  `pointed_dripstone` stalactites, `cave_vines` strands, `glow_lichen` (lit), `spore_blossom`, `hanging_roots`. Apply it
  to **cave ceilings** (hang down) and add the mirror for **floors** (stalagmites — dripstone pointing up) and **walls**
  (glow lichen for light). Source the block list from the theme's existing `decoration.underside` (so **lush** caves get
  cave-vines/dripleaf/moss and **ancient** caves get dripstone/deepslate for free); themes with no underside list get a
  small default (a little dripstone + glow lichen). Optionally a dedicated `decoration.caves` list if we want caves to
  differ from undersides.
- **Ores for free:** the theme's `ores` already seed the fill/core; caves simply *expose* them (rocky/ancient caves are
  worth mining). No new ore work.
- **Reachability — varied per island (the carver rolls one of three styles):**
  1. **Hidden / caved-in** — the cave is fully enclosed; you mine in (no breach). The quiet "is there even a cave?"
     island.
  2. **An obvious entrance** — a sinkhole shaft punched straight down through the surface skin, or a cave-mouth opened
     on the rim, leading in. Clearly findable.
  3. **A gash** — a long, deep **ravine-like cut** slicing through the surface into the cave volume, dramatically
     exposing the interior and letting daylight down into it. The showpiece.

  Roll a style per huge island on a tunable weight split (start ~40 / 35 / 25 hidden / entrance / gash). A rolled rare
  structure punches its own entrance regardless of style. Containment still holds — a breach is a *deliberate* carve
  through the skin (shaft / mouth / gash), everything else stays solid so the island never accidentally opens to the void.
- Gate caves to huge islands via a `caves` block on the shape/theme (e.g. `"caves": { "rooms": {min,max}, "decoration":
  "underside" }`); absent = no caves (every existing island stays solid, unchanged).

---

## 3. Rarely, a large structure (reuse `RareStructure`)

`RareStructure` already does exactly this: a `chance`-gated `jigsaw` that **replaces** the theme's normal jigsaw
(or becomes the only one), biome/dimension-gated, with its own mobs/twin/suppress_pond. Huge biome themes have no normal
jigsaw, so a rolled rare structure is the whole event. Add a `rare_structures` list per huge theme with **low chances**:

- **Jigsaw dungeon (in the caves).** A multi-room dungeon — the current `dungeon` is a single `dungeon/lair`
  (depth 1); the huge version is a deeper jigsaw (connected rooms via street/connector pieces, spawners + loot chests),
  placed **sunk** into the island so it sits in the cave layer (`jigsaw.sink`, as `dungeon` already uses `sink: 5`).
  Chance ≈ 0.08.
- **Large woodland mansion (on the surface).** The mansion is already a depth-2 jigsaw (`woodland_mansion/start`,
  pad 12); the "large" version runs deeper / adds wing pools for a bigger manor. Chance ≈ 0.05, gated to forest-y biomes
  (it already adapts by biome). Pad must fit the huge island's radius (huge radius ≫ pad 12, so fine).
- Both are authored the standard way (`*Templates.java` → `.nbt` via `DevStructureGenerator`, the **2-build dance**, a
  template pool, a placement gametest). The jigsaw dungeon can largely reuse/extend the existing dungeon pieces.

Because a rare structure is *rare*, most huge islands are just big terrain + caves; the structure is the jackpot.

---

## 4. Phases (build order)

1. **Huge tier (sizing).** Add the engine-free part first: `huge_*` themes/seeds using `radius` + `max_under_depth`
   (single) and `cluster_offsets` (cluster). Ship two as proof — **`huge_forest`** (single) and **`huge_aquatic`**
   (cluster) — with full onboarding. **Verify performance here** (see Open questions) before rolling out more.
2. **Cave systems.** `CaveCarver` + cave decoration (reuse underside palette), gated by a `caves` config. Make
   **`huge_lush`** / **`huge_ancient`** the showcases.
3. **Rare large structures.** Author the jigsaw dungeon + large mansion; wire them as `rare_structures` on the huge
   themes. 2-build dance + gametests.
4. **Roll out** huge variants for the remaining overworld biomes; tune sizes, cave density, chances, recipes.

---

## Open questions / decisions

- **Performance — verify in Phase 1.** A radius-28 island is tens of thousands of blocks; the `GenerationJob` streams
  at 512 blocks/tick (a long grow-in — acceptable as animation), but check (a) the in-memory `IslandPlan` size, (b) the
  germinate **overlap scan** over a huge planned volume, (c) whether huge islands want a higher per-tick budget. If
  huge islands are too heavy, clusters (smaller bodies) are the pressure valve — another reason to allow per-seed choice.
- **Cluster spacing/count** — Village Center uses 3 isles ~16–19 apart; huge clusters may want 4–5 and wider. The
  centre-void gap matters (you bridge it yourself, or a rare structure spans it).
- **Cave reachability — DECIDED: varied.** Per huge island the carver rolls **hidden / obvious-entrance / gash**
  (see §2); tune the weight split + the gash dimensions in playtest.
- **Cave decoration source** — reuse `decoration.underside` (recommended, free theming) vs a dedicated
  `decoration.caves` list (more control, more JSON).
- **Recipe cost / gate — DECIDED.** Middle row `ender pearl / <theme>_large_skyseed / blaze powder`, surrounded by six
  of the theme's bulk block. The large seed gates behind large; the **ender pearl + blaze powder** make huge a
  post-Nether *and* post-End farm gate (supersedes the README's older "rare Nether ingredient" idea). Both flank items
  are renewable farm drops, so it's a sink, not a one-shot.
- **Which seeds get huge** — all 10 overworld biomes (recommended) vs a curated subset first.

## Notes & standing rules

- **Reuse, don't reinvent:** `cluster_offsets`, `max_under_depth`, `RareStructure`, and the underside-decoration code
  already exist — the cave carver is the only substantial new engine. Keep the `caves` config opt-in so every current
  island is byte-identical until a theme asks for caves.
- **Add-a-seed checklist** applies to each `huge_*` seed (recipe + `skyseeds` tag + unique icon + guide entry +
  reveal/gathered/craft advancements); debug seeds get none.
- **2-build dance + the .nbt staging trap** apply to every new structure `.nbt` (dungeon, large mansion).
- Gate behind the chapter where it belongs: this is **overworld** end-game content (huge = a late resource sink), above
  `*_large` in the catalogue order.
