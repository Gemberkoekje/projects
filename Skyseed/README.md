# Skyseed

A **terraforming skyblock** mod for **Minecraft 1.21.1 / NeoForge**. Craft a *Skyseed*, throw it into open air, and ~2 seconds later a procedurally generated, themed sky island germinates where it comes to rest. Progression is driven by **exploration + crafting**, not block-condensing.

> This README is the consolidated project plan (architecture, data model, decisions, status). For the island-by-island *content* roadmap — every planned island type, its blocks, variants, and recipes — see **[SKYISLANDSPLAN.md](SKYISLANDSPLAN.md)**.

---

## Concept

You start on a small, hand-authored sky island. Crafting produces a **Skyseed**, a throwable item. Thrown into the void, it arms briefly, then *germinates*: particles, a sound, and a new island grows in over the next ticks. The loop:

> craft a Skyseed → throw it → a new themed island appears → harvest new resources → craft the next, pricier Skyseed.

Different recipes produce Skyseeds of different **themes** (forest, rocky, …) and sizes. Exploration *and* crafting are both progression currency.

### Design pillars

1. **Themed, never identical.** Every island is rolled within its theme — irregular silhouette, varied decoration, clustered ores. No pasted blobs.
2. **Curated start, procedural everything-after.** The first island is authored block-by-block so a player can never soft-lock on turn one. Everything after is generated.
3. **Adding content is data, not code.** A new theme, recipe, ore, or biome flavor is JSON. Zero Java changes to extend the game.

---

## Status

**Version 0.19.0** — see [CHANGELOG.md](CHANGELOG.md). All planned engine milestones (0–9) are complete, plus several post-plan features. What exists today:

| Area | Built |
|---|---|
| Core loop | Throwable charge-to-launch seed → arm timer → germinate → tick-budgeted grow-in |
| Themes (seeds) | All 11 planned island types — **Forest**, **Rocky**, **Desert**, **Mushroom**, **Frozen**, **Meadow**, **Badlands**, **Ancient**, **Lush**, **Aquatic** — each with a **Large variant** (`*_large`): a bigger, pricier island with a thematic twist (Rocky → emerald mountain, Aquatic → deep lake, Desert → oasis, Frozen → frozen lake, Badlands → towering mesa, …) (`skyseed:*`) |
| Banded fill | `fill_bands` palette option: a Y-cycled body palette for badlands-style strata |
| Underside decor | per-variant `underside` list: hanging dripstone, cave vines, spore blossoms, roots from the island's bottom |
| Two-tall plants | ground entries that are double plants (dripleaves, pitcher plant, tall flowers) place both halves |
| Hand-built trees | `skyseed:mangrove`, `skyseed:azalea` — trees whose vanilla features won't place on floating islands |
| Pond water plants | `pond.plants` list: lily pads on the surface, kelp / seagrass / coral / sea pickle / wet sponge on the floor |
| Throw modes | Classic (charged physics arc) + Precise (direct placement along the look vector); toggle keybind (default **V**), persisted in client config |
| Mob sprinkles | `mobs` list (theme / override / variant): animals spawned directly when an island finishes generating |
| Water mobs | `pond.water_mobs`: animals spawned submerged in the pool (squid, axolotls, fish, glow squid) |
| Villager islands | **Hamlet** (`skyseed:hamlet`): a cottage + an unemployed villager. **Trade Post** (`skyseed:trade_post`): a plaza ringed by shops, each villager takes up a trade. **Village Center** (`skyseed:village_center`): a bell plaza + four trading halls with **all 13 professions** + an iron golem. See `SKYVILLAGESPLAN.md`. Raids disabled on Skyseed worlds |
| Jigsaw buildings | A theme's `jigsaw` config assembles buildings from a vanilla `worldgen/template_pool` via `JigsawPlacement` — random rotation + structure-processor variation, exactly like vanilla villages. Single-piece (the Hamlet's three weathered cottages) or **multi-piece** (the Trade Post's plaza branches shops off jigsaw connectors). A villager spawns at every bed; drop in structure-block-authored `.nbt` to extend a pool |
| Animal islands | **Pasture / Poultry / Wool Farm / Stable / Aquarium** — a fenced enclosure + a guaranteed pack of animals via the `animals` theme field (weighted packs, babies, random sheep colours, submerged aquarium life; the Stable carries a loot chest). See `SKYANIMALSPLAN.md` |
| Structure islands | **Dungeon** (spawner cube + `simple_dungeon` chests), **Ruined Portal** (broken frame + crying obsidian + `ruined_portal` chest), **Desert Temple** (4 `desert_pyramid` chests + buried TNT), **Jungle Temple** (2 `jungle_temple` chests + dispenser), **Witch Hut** (witch + cat + cauldron). Built on the jigsaw system — loot chests, spawners and vanilla loot tables are block-entity NBT in the structure `.nbt`. See `SKYSTRUCTURESPLAN.md` |
| Rare features | `rare_structures`: a chance-gated structure that germinates in place of the ordinary island. **Igloo** (5% on Frozen — a sealed snow dome with a trapped zombie villager to cure), **Abandoned cottage** (10% on Hamlet — a cobwebbed, bed-less ruin haunted by a zombie villager), **Ocean ruin** (8% on Aquatic — a flooded stone-brick basin with suspicious sand and a sunken chest, replacing the pond). The surprise's own `mobs` pack spawns its inhabitants |
| Datapack themes | Full `IslandTheme` codec (the keystone); themes are pure JSON |
| Biome response | `biome_overrides` keyed to the germination biome (Forest rolls acacia over savanna, jungle over jungle, lake over ocean, …) |
| Y-band overrides | `min_y` / `max_y` gating — drives Rocky's deepslate ↔ coal/iron gradient and snow peaks |
| Water | Contained ponds (a containment ring walls the rim up to the water before decoration), sand/clay/gravel beds and shores; off-rim static waterfall cascades; hand-built mangroves (`skyseed:mangrove`) |
| Surface | Per-column `surface_scatter` (block mixes); snow-capped peaks (taller shape + snow) |
| World | Void world preset with multi-noise overworld biomes; structures disabled |
| Start | Curated start island; safe spawn (valid biome, on island not in a tree); first-join guide book |
| Guide | The Skyfarer's Almanac — **Patchouli optional**: the rich illustrated book when Patchouli is installed, a plain vanilla written book otherwise. Crafted from any one Skyseed (`#skyseed:skyseeds`); advancement-gated entries |
| Safety | Tick-budget placement (no single-tick stalls); overlap nudge + fizzle-and-drop |

**All 11 island types in [SKYISLANDSPLAN.md](SKYISLANDSPLAN.md) are now built.** Remaining polish is small and mostly placement-shaped: water-edge sugar cane, side vines, sculk veins, Frozen ice spikes, and bees inside Meadow nests (needs entity spawning). Content beyond the island set lives in the sibling plans `SKYANIMALSPLAN.md` (mobs) and `THROWMODEPLAN.md` (throw modes). Nether/End skyblock dimensions are a long-term goal.

---

## The Skyseed item

**One item per theme, `skyseed:<theme>_skyseed`** (e.g. `skyseed:forest_skyseed`). Each is an `IslandSeedItem` instance carrying a fixed `theme()` id; all are registered from `ModItems.SEED_THEMES` and named in the `lang` file ("Forest Skyseed"). Distinct items mean each shows up individually in JEI/REI, and add-on mods can register their own seed item (pointed at their own theme) and add it to the `#skyseed:skyseeds` item tag. Every seed's icon is its own client model under `models/item/<theme>_skyseed.json`.

- **The `#skyseed:skyseeds` tag** holds every seed item — the guide recipe (any one skyseed → the Almanac) keys off it, and it's the integration point for add-on seeds.
- **Throwing.** Hold right-click to wind up, release to throw. Two modes, toggled by a keybind (default **V**, persisted in client config — see `THROWMODEPLAN.md`): **Classic** lobs a charged physics arc (a tap lands close, a full ~1.25 s charge flies far) and germinates where it lands; **Precise** places the island directly along the look vector at a charge-scaled distance (5–40 blocks) and germinates exactly there. On release the client sends a packet; the server reads the held seed's `theme()`, then validates and spawns the entity. Throw height/distance are how the player *chooses* an island's germination Y (and thus, on Rocky, its ore band).

A new Skyseed is one recipe JSON + one theme JSON + a one-line entry in `ModItems.SEED_THEMES` (plus an icon/model and a lang name). The theme content itself stays fully data-driven.

---

## Configuration

The dividing line: **config owns the *what & how much*** (recipes, palettes, ore tables, variants, shape, overrides); **code owns the *how*** (the generation algorithm).

### Recipe (data)

A normal datapack shaped-crafting recipe whose `result` is the theme's seed item. The Forest seed — planks + dirt checkerboard:

```json
{
  "type": "minecraft:crafting_shaped",
  "pattern": ["PD", "DP"],
  "key": { "P": { "item": "minecraft:oak_planks" }, "D": { "item": "minecraft:dirt" } },
  "result": { "id": "skyseed:forest_skyseed" }
}
```

### Theme (data)

One JSON per theme under `data/<namespace>/skyseed/theme/<id>.json` (the `skyseed:theme` datapack registry). Block/feature ids resolve at gen time; a **missing id is skipped with a warning**, so a theme can optionally reference modded content and still load cleanly without that mod.

**Top-level keys**

| Key | Type | Default | Meaning |
|---|---|---|---|
| `shape` | Shape | — (required) | Silhouette + vertical profile |
| `palette` | Palette | — (required) | The layered blocks |
| `ores` | OreEntry[] | `[]` | Base ore table |
| `variants` | Variant[] | `[]` | Weighted decoration looks (one rolled per island) |
| `biome_overrides` | BiomeOverride[] | `[]` | Conditional tweaks by biome and/or Y |
| `pond` | Pond | — | Optional contained pool |

**Shape** — `radius` `{min,max}` (required) · `rim_noise` float (0.40) · `underside` (`teardrop`) · `top_dome` `{min,max}` (`{1,2}`; raise for peaks).

**Palette** — `surface`, `fill`, `core` block ids (required) · `fill_depth` int (3) · `surface_scatter` GroundEntry[] (`[]`, mixes blocks into the surface per column) · `fill_bands` block-id[] (`[]`; when set, the body (fill + core) becomes a Y-cycled list of strata — badlands cliffs; core still seeds ores) · `band_thickness` int (2; blocks per band).

**OreEntry** — `block` (required) · `chance` float presence-roll (required) · `count` `{min,max}` veins (required) · `vein_size` `{min,max}` (required) · `depth` (`core` | `deep_core`; deep = lower ~40% of the core). Veins grow favouring face-adjacent steps over diagonals, so patches come out as solid clusters. The Rocky/Ancient tables use `biome_overrides` with `min_y`/`max_y` bands to approximate the vanilla ore-by-depth curve (deep = diamond/redstone/gold/lapis, mid = iron/copper, high = coal/iron).

**Variant** — `weight` int (1) · `name` string · `surface_override` block id · `decoration` { `trees`: TreeEntry[], `ground`: GroundEntry[], `underside`: GroundEntry[] }. `underside` entries hang from each column's bottom face — `pointed_dripstone` and `cave_vines` build multi-block strands; others (spore blossom, hanging roots, …) hang one block. A `ground` entry that resolves to a two-tall plant (dripleaf, pitcher plant, tall flower) places both halves.

**TreeEntry** — `feature` id (required; a vanilla configured feature, or a built-in hand-built tree: **`skyseed:mangrove`**, **`skyseed:azalea`**) · `tries` int (3) · `spacing` int (3).

**GroundEntry** — `block` id (required) · `chance` float per-column (required).

**Pond** — `block` fluid id (`minecraft:water`) · `radius` int (3) · `depth` int (2) · `plants` GroundEntry[] (`[]`; per-column water plants — `lily_pad` floats on the surface, `kelp` fills a column, `tall_seagrass` places both halves, and anything else roots on the floor, waterlogged if it can be). Kept within ≈0.62× the island radius, then a **containment ring** walls every land column touching the water up to the surface (the island's fill/surface block), and the bed/shore are dressed with sand, clay and gravel — all before decoration, so it stays still and never spills off the rim (where the very edge can't be walled, a small waterfall is left as variety).

### Biome overrides

Each entry in `biome_overrides` is conditionally applied on top of the base theme. It **matches** when *both* its biome list and its Y range pass (first match wins); each present field then **replaces** the base theme's field for that island.

| Field | Type | Role |
|---|---|---|
| `biomes` | string[] | Biome ids or `#tags`; **empty = any biome** |
| `min_y` / `max_y` | int | Germination-Y gate (each optional) |
| `surface` / `fill` / `core` | block id | Palette overrides |
| `fill_depth` | int | Palette override |
| `surface_scatter` | GroundEntry[] | Surface mix override |
| `shape` | Shape | Silhouette override (e.g. taller snow peaks) |
| `ores` | OreEntry[] | Ore-table override |
| `variants` | Variant[] | Decoration override (`[]` = bare) |
| `pond` | Pond | Add/override a pond |
| `waterfalls` | int | Number of static cascades off the rim |

Examples in the shipped themes: Forest over `#minecraft:is_ocean` → lake island + pond + waterfalls; over `mangrove_swamp` → mud + hand-built mangroves; Rocky with `max_y: 8` → deepslate island with diamonds; Rocky with `min_y: 130` *or* a snowy biome → snow-capped peak.

---

## Generation algorithm

A near-pure function `planIsland(ServerLevel, BlockPos center, IslandTheme, Holder<Biome>, RandomSource) → IslandPlan`, then a scheduler applies the plan over several ticks.

1. **Match** the first applicable `biome_override` (biome + germination Y), producing the effective shape/palette/ores/variants/pond/waterfalls.
2. **Silhouette.** A 2D rim-radius **noise field** (never a clean sphere) extruded with a **teardrop** profile — flat-ish domed top, tapering underside.
3. **Layered fill.** surface → fill band (`fill_depth`) → core, with jitter.
4. **Ores.** Per entry: roll `chance`, then place `count` clustered **veins** of `vein_size` within the core, weighted by `depth`. The random-walk strongly prefers orthogonal (face) steps so veins are compact, not diagonal specks.
5. **Pond / waterfalls / decoration.** Carve the pond; pick weighted variant; place trees (vanilla features queued, or `skyseed:mangrove` hand-built into the plan) and per-column ground cover; add rim cascades.
6. **RNG.** `RandomSource.create(worldSeed ^ center.asLong())` — unique per island, reproducible, decorrelated from neighbours.

**Two guards:** *tick-budget placement* drains ~512 blocks + 2 trees per tick (no stutter; doubles as a grow-in animation, using block flags that avoid cascading neighbour/light updates); *overlap safety* nudges germination up through `{0, 8, 16, 24}` and, if nothing is clear, fizzles and drops the seed back rather than carving into existing terrain.

---

## World & progression setup

- **World preset `skyseed:skyblock`** — a void `noise_settings` (final density 0, air blocks, `sea_level: -64` to dodge the hardcoded lava floor) with a **multi-noise overworld biome source**, so F3 shows real, varied biomes over empty sky. Selectable as a world type and defaulted on the create-world screen, with structure generation turned off.
- **Start island** — a curated build placed at world creation (guarded by a new-world check), with spawn forced onto a valid land biome and onto the island surface (not into a tree).
- **First join** — the player is teleported to the start island and given the guide (the Patchouli book if installed, otherwise a vanilla written book). Tracked per-UUID in the world `SavedData`, so it's granted only once.

---

## Architecture

`dev.gemberkoekje.skyseed`:

- **`Skyseed`** — `@Mod` entry point; registers everything below. `MODID = "skyseed"`.
- **`registry/`** — `DeferredRegister`s for the per-theme seed items (`ModItems.SEED_THEMES`), throwable entity type, creative tab; and `SkyseedRegistries` (the `skyseed:theme` datapack registry, via `DataPackRegistryEvent`).
- **`item/`** — `IslandSeedItem` (charge-to-throw, one per theme) and `SkyseedGuide` (builds the guide book: Patchouli if present via the isolated `compat/PatchouliCompat`, else a vanilla written book).
- **`entity/IslandSeedEntity`** — `ThrowableItemProjectile` carrying the theme; arms `ARM_DURATION = 40` ticks (~2 s), rests on block-hit, then `germinate()`s (overlap loop → enqueue plan).
- **`worldgen/`** — `IslandGenerator` (the algorithm) → `IslandPlan`; `GenerationJob` + `IslandGrowth` (the tick-budget scheduler on `ServerTickEvent.Post`); `StartIsland`; `SkyseedWorldData` (`SavedData`); `WorldSetupEvents`; `event/PlayerEvents`.
- **`worldgen/theme/`** — the codec records: `IslandTheme`, `Shape`, `Palette`, `OreEntry`, `Variant`, `Decoration`, `TreeEntry`, `GroundEntry`, `Pond`, `BiomeOverride`, plus `IntRange`, `OreDepth`, `Underside`.
- **`client/SkyseedClientEvents`** — renderers and the world-type default hook.

---

## Design decisions (resolved)

The plan's former open questions, settled by the build:

- **Naming.** "Skyseed" is the locked name for the mod and the thrown item; per-theme display names ("Forest Skyseed", …) come from the `item_name` component.
- **Germination point.** The seed germinates at its **rest position** after a fixed ~2 s arm (tuned down from the original 3 s) — wherever it stopped, or mid-air if still flying. Throw power therefore controls the island's Y.
- **Bridging.** v1 is **free-floating only**; the overlap nudge keeps islands from merging. Growing onto existing islands is deferred.
- **Theme codec.** Finalized and extended well past the original sketch (`biome_overrides`, `min_y`/`max_y`, `surface_scatter`, `pond`, `waterfalls`, `shape.top_dome`).
- **RNG seeding.** `worldSeed ^ center` only; the planned `throwCount` term was dropped as redundant — overlap safety already guarantees distinct centers.
- **Structure templates / jigsaw.** Remain deferred; pure procedural generation is sufficient.

**Still open:** multiplayer client-sync hasn't been explicitly verified (generation is server-side and placed with client-updating block flags, so it *should* be fine).

---

## Modpack roadmap

Loader is **NeoForge** because the intended pack is content-heavy (most non-Create tech mods worth a pack's identity live there, several NeoForge-exclusive). The Skyseed mechanic is the pack's signature, so it doesn't need Create.

- **Tech:** Immersive Engineering, Mekanism, Applied Energistics 2, Powah.
- **Quests:** FTB Quests + Library/Teams — each island theme is a natural quest gate (model the actual branching DAG, not a single column).
- **Beauty:** Supplementaries, Macaw's suite, Immersive Furniture, Chipped/Rechiseled.
- **Performance:** Sodium + modern stack.

*(Mod-availability landscape last checked June 2026; re-verify 1.21 support before locking the pack.)*

---

## Building & running

Requires **JDK 21** (NeoForge 1.21.1's required Java). The build is pinned to a local JDK 21 via `org.gradle.java.home` in `gradle.properties` — adjust that path if your JDK lives elsewhere.

```sh
./gradlew build          # compile + package the mod jar
./gradlew runClient      # launch a dev Minecraft client with the mod loaded
./gradlew runServer      # launch a dev dedicated server
```

The first invocation downloads Gradle, NeoForge, and Minecraft, so it takes a while.

## Repository layout

- `src/main/java/dev/gemberkoekje/skyseed/` — mod sources (`Skyseed.java` is the `@Mod` entry point).
- `src/main/resources/data/skyseed/skyseed/theme/` — the theme JSONs (`forest`, `forest_large`, `rocky`).
- `src/main/resources/` — assets, recipes, advancements, the Patchouli book, and the world preset.
- `src/main/templates/` — `META-INF/neoforge.mods.toml` source (Patchouli is an *optional* dep).
- `gradle.properties` — mod id/version and Minecraft/NeoForge versions.

Scaffolded from the [NeoForge ModDevGradle MDK](https://github.com/NeoForgeMDKs/MDK-1.21.1-ModDevGradle).
