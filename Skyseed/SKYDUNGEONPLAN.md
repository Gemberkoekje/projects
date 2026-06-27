# SKYDUNGEONPLAN — Sprawling Dungeon + Abandoned Mineshaft

Two sprawling jigsaw structures for the **Huge Rocky** island, replacing its single-room `dungeon/lair` rare. Both
are rolled as rare structures at **2.5 %** each (so huge_rocky has a 5 % chance of *a* rare, then picks one):

| Rare on huge_rocky | Chance | What it is |
|---|---|---|
| **Sprawling Dungeon** (`dungeon_complex/`) | 2.5 % | a multi-room cobble/mossy dungeon complex sunk into the mountain |
| **Abandoned Mineshaft** (`mineshaft/`) | 2.5 % | a sprawling oak (or dark-oak mesa) mineshaft, with over-void wooden supports |

**Plus a dedicated, craftable Large Dungeon seed** (`dungeon_large`, Part D) — the sprawling dungeon as a *whole island*
in its own right, reusing the `dungeon_complex/` tileset as the island's main jigsaw. There is deliberately **no
dedicated mineshaft seed**: a dedicated big dungeon is the more interesting payoff; the mineshaft stays a huge_rocky
surprise only.

## ★ Reference the vanilla jigsaw pieces (do this for EVERY piece)

**Before authoring any piece, look at how the vanilla equivalent actually looks and is built, and mirror its block
placement.** We are inspired by vanilla, not reinventing it. Concretely:

- **Abandoned Mineshaft** → vanilla `Mineshaft` (the legacy `MineShaftPieces`: Corridor / Crossing / Room / Stairs).
  Copy the look: 3-wide plank floor with gaps, the **oak-fence-post + plank-beam support arch every ~4 blocks**, the
  centre `rail` line (partial), scattered cobwebs, wall torches, **chest minecarts** on the rails, **cave-spider
  spawners in cobweb nests**, the open Room with branch corridors at different heights, the `stairs` level-changers, and
  the **mesa (badlands) dark-oak variant** (dark-oak planks/fences + gold blocks / buried gold & emerald ore in walls).
- **Sprawling Dungeon** → vanilla has no jigsaw dungeon, so borrow **two** vanilla structures: the **Stronghold** for
  the sprawling underground *connectivity + room variety* (corridors, crossings, prison cells with iron bars, library,
  fountain room, spiral/straight stairwells), and the classic **monster dungeon** for the *palette + contents*
  (cobblestone + mossy cobblestone, mob spawner, `chests/simple_dungeon`).
- **Jigsaw mechanics / connector + pool pattern** → vanilla **village** tilesets
  (`data/minecraft/structure/village/...`) are the canonical jigsaw example, but Skyseed's own **Trade Post** jigsaw
  (`trade_post/`, `StructureParts.jig(...)`, `cap_count`/`depth`, `PathSurfacer`) already models them — follow it.

Where useful, inspect the real block names against the bundled client jar
(`~/.gradle/caches/.../minecraft_1.21.1_client.jar`) rather than guessing.

## Decisions (locked by the user)
1. **Sizing** — leave **huge_rocky as-is** (radius 24–30, dome 12–20). The "small dungeon island" was likely a low
   radius roll; the real fix is the dungeon *sprawling*, not a bigger island.
2. **Mineshaft delivery** — a **rare structure on huge_rocky**, one of **two** rares (the other = the dungeon), **both
   2.5 %**. (Each auto-gets a debug seed from `ThemeScanner`, so no manual debug wiring.)
3. **Authenticity** — yes to all: **chest-minecart entities** + **rails**, and the **dark-oak mesa variant** of the
   mineshaft. More variety is better.
4. **Dedicated Large Dungeon seed** (`dungeon_large`, Part D) — yes; a dedicated big dungeon is more interesting than a
   dedicated big mineshaft, so the dungeon gets a craftable island seed and the mineshaft does not.

## Infrastructure we reuse (already in the codebase)
- **Jigsaw sprawl**: a `jigsaw` config carries `pool`, `depth`, `pad`, `sink`, `reach`, `cap_count`/`cap_min`,
  `centerpiece`. Pieces connect via `StructureParts.jig(name, target, pool, finalState)`; `depth` + `cap` bound the
  recursion. Pools live in `data/skyseed/worldgen/template_pool/<name>/`; piece `.nbt` is code-authored in a
  `<Name>Templates.java` (`writeIfAbsent`) and wired into `DevStructureGenerator` (2-build dance for the `.nbt`).
- **Over-void handling** (`PathSurfacer`, run per jigsaw site with `reach>0` by `GenerationJob.placeStructures`):
  - `supportFloatingFloors()` — stilts a **dirt** foundation/stub under any solid floor left hanging over the void
    (the village's "lot ran off the edge" support). **This is the village logic the mineshaft supports build on.**
  - `resolve()` — turns connective `MARKER` (purple wool) tiles into a worn dirt path on ground / a self-railing oak
    plank bridge over void.

## Part A — Sprawling Dungeon (`dungeon_complex/`)
A jigsaw of small cobble/mossy `.nbt` pieces (mossed by the existing `hamlet_weathering` processor). Sunk into the
mountain (`sink`), with only the entrance stair as a surface tell.

**Tileset (each non-terminal piece carries `jig` connectors so the complex keeps branching):**
- `start` — entrance hub: the descent stair from the surface + 4 corridor connectors.
- `corridor`, `corridor_stairs` — 1–2-wide cobble passages; the stairs change level so it sprawls vertically.
- `cross` — 4-way intersection.
- Rooms (weighted): `spawner_room` (vanilla-style spawner — zombie/skeleton/spider variants — + 2 `chests/simple_dungeon`),
  `cell_block` (iron-bar prison cells, à la stronghold), `flooded_room` (water + a drowned spawner), `lava_room` (a
  magma/lava hazard + a chest), `fountain` (a stronghold-style water feature, flavour).
- `treasure_vault` — the capstone `centerpiece` (a better chest), placed once.
- `dead_end` — capped stub.

**Config (huge_rocky rare):** `chance 0.025`, jigsaw `{ pool: skyseed:dungeon_complex/start, target: minecraft:bottom,
depth ~5, pad ~10, sink ~8, cap_count ~10, cap_min ~6, centerpiece: …treasure_vault }`. Palette: cobblestone /
mossy_cobblestone / cobble stairs+slabs / iron_bars / torches / spawners / chests.

## Part B — Abandoned Mineshaft (`mineshaft/`, + dark-oak `mineshaft_mesa/` variant)
A jigsaw modeled piece-for-piece on the vanilla mineshaft.

**Tileset:**
- `start` — a crossing hub.
- `corridor` — 3-wide × 3-tall: oak-plank floor (with gaps), **support arch every ~4 blocks** (oak-fence posts each
  side + oak-plank beam across the top), centre `rail` line (partial), scattered cobwebs, wall torches; chance of a
  **cave-spider spawner in a cobweb nest** and a **chest minecart** (`chests/abandoned_mineshaft`).
- `cross` — intersection; `corridor_stairs` — sloped level-changer; `room` — large open chamber, branch corridors at
  varied heights; `dead_end`.
- **Mesa variant** `mineshaft_mesa/` — same pieces in dark-oak (dark-oak planks/fences) + gold blocks and buried
  gold/emerald ore in the walls, à la the vanilla badlands mineshaft.

**Config (huge_rocky 2nd rare):** `chance 0.025`, jigsaw `{ pool: skyseed:mineshaft/start, target: minecraft:bottom,
depth ~6, pad ~8, sink ~6, cap_count ~14, reach ~32, trestles: true }`. `reach` lets the corridors sprawl out (and
triggers the over-void pass). Mobs: cave_spider (+ the spawners).

**Chest-minecart entities** (authenticity #3): the `.nbt` format has an `entities` list, but `StructureWriter.write`
currently writes only blocks + block-entities. Sub-task: **extend `StructureWriter` to bake an entities map**
(`BlockPos → entity CompoundTag`) into the `.nbt`, so the jigsaw placement spawns the chest minecarts (with a
`LootTable`). Fallback if that's fiddly: bake a marker block and spawn the minecart at placement time, like `Traps`
swaps the pressure plate and `GenerationJob` spawns iron golems/villagers.

## Part C — Over-void wooden supports (the village-logic variant)
Corridors that sprawl off the island edge hang over the void. Add a **wood variant of `supportFloatingFloors`**:

- `PathSurfacer.supportTrestles(level, origin, reach)` — under each over-void mineshaft floor tile (a solid floor over
  air), drop an **oak-fence trestle leg** down to ground (within `SUPPORT_SEARCH`) or a short stub over pure void, with
  oak-plank cross-bracing at intervals — exactly like a vanilla mine support trestle.
- Gate it to the mineshaft (not the village's dirt foundation): add a `trestles` flag to `JigsawConfig`; in
  `placeStructures`, `trestles ? supportTrestles : supportFloatingFloors`. Connective rail/bridge gaps can also reuse
  the existing `resolve()` self-railing bridge.

## Part D — Dedicated Large Dungeon seed (`dungeon_large`)
A **craftable** seed that germinates the sprawling dungeon as a *whole island* — the `dungeon_complex/` tileset (Part A)
reused as the island's **main** jigsaw (not a rare). A bigger rocky island (stone/cobble palette, cobble/mossy surface
scatter, radius ≈ 14–18 like the Trial Chamber island) with the dungeon complex sunk into it; the entrance stair is the
surface tell.

- **Recipe** = *the normal dungeon seed + the "huge bits" + construction materials*: `CCC / EDP / CCC` — 6× **cobblestone**
  (C, the construction material) around the centre **`dungeon_skyseed`** (D), ringed between an **`ender_pearl`** (E) and
  **`blaze_powder`** (P). The ender pearl + blaze powder gate it to end-game exactly like the huge seeds.
- **Theme** `dungeon_large.json`: a rocky island whose `jigsaw` is `{ pool: skyseed:dungeon_complex/start, target:
  minecraft:bottom, depth ~5, pad ~10, sink ~8, cap_count ~10, cap_min ~6, centerpiece: …treasure_vault }` — the same
  complex as the huge_rocky rare, just as the island's own structure.
- **Full seed onboarding** (the 12-touch-point craftable-seed checklist): `SEED_THEMES += "dungeon_large"`, the recipe,
  the `#skyseeds` tag entry, a `craft_dungeon_large` advancement, lang, an item model + a PowerShell icon (a cobble/mossy
  dungeon mouth), a Patchouli island entry + a `recipes.json` crafting page. It's craftable, so it needs no debug seed
  (test it from the creative tab).

## Phases (each its own commit)
1. **Sprawling Dungeon (rare + dedicated seed) — DONE (v0.132.0).** `DungeonComplexTemplates` (start hub +
   corridor/corner/cross + zombie/skeleton/spider spawner rooms + cell_block + flooded_room + lava_room +
   treasure_vault + dead_end) + the self-linking `dungeon_complex/parts` pool (weighted + empty terminator, mossed by
   `hamlet_weathering`). Solid boxes with doorway connectors (jigsaw blocks → air), self-linking on `skyseed:dungeon`.
   Wired as the huge_rocky rare @ 2.5% (replacing `dungeon/lair`) AND as the dedicated `dungeon_large` seed
   (`CCC/EDP/CCC` recipe + full onboarding). `sprawlingDungeonAssembles` gametest proves the connectors align (cobble
   > 400 = sprawled past the hub). Depth 4, sink 6, pad 12. *(Follow-up ideas: corridor_stairs for level changes; a
   `centerpiece` to guarantee one treasure_vault.)*
2. **Mineshaft (oak)** — `MineshaftTemplates` + `mineshaft/` pool (corridor/cross/stairs/room/start/dead_end, with the
   arches/rails/cobwebs/spawner/chest), wire as huge_rocky 2nd rare @ 2.5%. **+ extend `StructureWriter` for entities**
   (chest minecarts). Gametest.
3. **Over-void trestles** — `supportTrestles()` + the `trestles` jigsaw flag, wired in `placeStructures`; gametest
   (a corridor forced over the void grows fence legs).
4. **Mesa dark-oak variant** — `mineshaft_mesa/` pool (+ a biome gate, or a parallel rare), gold/ore flavour.

## Verification
- Gametests: each structure assembles via `Jigsaw.placeCapped` (like `ancientCityPlaces`/`grandOceanMonumentPlaces`),
  asserting the key contents (spawners, chests, rails, arches, trestle legs). The two huge_rocky rares are config-checked
  via `rareIndex(...)` like the blaze-room/bastion tests.
- In-world (RCON/playtest can't fully cover jigsaw sprawl + minecarts): eyeball the sprawl, the minecart loot, the
  over-void trestles, and the mossing.
