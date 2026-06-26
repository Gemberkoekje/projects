# Skyseed

A **terraforming skyblock** mod for **Minecraft 1.21.1 / NeoForge**. Craft a *Skyseed*, throw it into open air, and ~2 seconds later a procedurally generated, themed sky island germinates where it comes to rest. Progression is driven by **exploration + crafting**, not block-condensing.

> This README is the consolidated project plan: architecture, data model, decisions, and current status. The full version history is in **[CHANGELOG.md](CHANGELOG.md)**; the Nether chapter (complete as of v0.57.0) is summarised in `SKYNETHERPLAN.md`, and the deeper-jigsaw structure work in `SKYJIGSAWPLAN.md`.

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

**Version 0.93.5** — see [CHANGELOG.md](CHANGELOG.md). The **overworld chapter** is built end to end (every island type and its Large variant, villages, animal farms, the loot/encounter structure islands, both grand structures, the rare surprises) and the **Nether chapter** is complete (overworld seeds adapt or fizzle across the portal, all five Nether biomes have a full-size native seed + a Large variant, all four Nether structures — the Fortress + blaze room, the Bastion Remnant, the Piglin Trading Post, the Wither Arena — and ruined-portal twins linked across dimensions). The villages now assemble as varied **street villages** (Hamlet / Trade Post / Village Center) through deep jigsaw use — see `SKYJIGSAWPLAN.md`. What exists today:

| Area | Built |
|---|---|
| Core loop | Throwable charge-to-launch seed → arm timer → germinate → tick-budgeted grow-in |
| Themes (seeds) | All 10 planned island types — **Forest**, **Rocky**, **Desert**, **Mushroom**, **Frozen**, **Meadow**, **Badlands**, **Ancient**, **Lush**, **Aquatic** — each with a **Large variant** (`*_large`): a bigger, pricier island with a thematic twist (Rocky → emerald mountain, Aquatic → deep lake, Desert → oasis, Frozen → frozen lake, Badlands → towering mesa, …) (`skyseed:*`) |
| Banded fill | `fill_bands` palette option: a Y-cycled body palette for badlands-style strata |
| Underside decor | per-variant `underside` list: hanging dripstone, cave vines, spore blossoms, roots from the island's bottom |
| Two-tall plants | ground entries that are double plants (dripleaves, pitcher plant, tall flowers) place both halves |
| Hand-built trees | `skyseed:mangrove`, `skyseed:azalea` — trees whose vanilla features won't place on floating islands |
| Pond water plants | `pond.plants` list: lily pads on the surface, kelp / seagrass / coral / sea pickle / wet sponge on the floor |
| Throw modes | **Precise** (default — direct placement along the look vector) + Classic (charged physics arc); toggle keybind (default **V**), persisted in client config |
| Mob sprinkles | `mobs` list (theme / override / variant): animals spawned directly when an island finishes generating |
| Water mobs | `pond.water_mobs`: animals spawned submerged in the pool (squid, axolotls, fish, glow squid) |
| Villager islands | **Hamlet** (`skyseed:hamlet`): a small green with 1–2 profession cottages. **Trade Post** (`skyseed:trade_post`): a street village — a square radiating lanes lined with shops (2–4 trades), fields and gardens, plus larger landmarks (a blacksmith's forge, a meeting hall) and over-void bridges; biome-styled. **Village Center** (`skyseed:village_center`): the same village scaled up — a huge (depth-capped) island, deeper streets, 4–6 shops + an iron golem. Raids disabled on Skyseed worlds |
| Jigsaw buildings | A theme's `jigsaw` config assembles buildings from a vanilla `worldgen/template_pool` via `JigsawPlacement` — random rotation + structure-processor variation, exactly like vanilla villages. Single-piece or **multi-piece** (the Trade Post's central square branches streets and hangs shops/fields off jigsaw connectors; a per-element cap holds the shop count and re-stamps the surplus as fields/gardens). A villager spawns at every bed; drop in structure-block-authored `.nbt` to extend a pool |
| Animal islands | **Pasture / Poultry / Wool Farm / Stable / Aquarium** — a fenced enclosure + a guaranteed pack of animals via the `animals` theme field (weighted packs, babies, random sheep colours, submerged aquarium life; the Stable carries a loot chest) |
| Structure islands | **Dungeon** (a 5×5 spawner room + two `simple_dungeon` chests, sunk underground with a stepped stairwell to a dark-oak door and ruins strewn across the island), **Ruined Portal** (broken frame + crying obsidian + `ruined_portal` chest), **Desert Temple** (4 `desert_pyramid` chests + buried TNT), **Jungle Temple** (a tiered cobble-and-moss ziggurat with corner columns and vines, over a trapped chamber of 2 `jungle_temple` chests + a tripwire dispenser), **Witch Hut** (witch + cat + cauldron), **Pillager Outpost** (a wide cobblestone-and-dark-oak watchtower with a camp — a semi-open arched base holds the `iron_golems`-spawned golem cage with room to walk past it, an enclosed middle room carries the pillager spawner + `pillager_outpost` chest so spawns can't fall off the island, an open watch platform sits under a pitched roof, and the apron has tents, a target, a campfire and a banner), **Trial Chamber** (the first *grand* structure — a copper-and-tuff complex sunk deep into a larger rocky island via the `sink` field, entered by one ladder shaft; a modular jigsaw hub holds the breeze boss spawner + a centre ominous vault for the heavy core/mace and draws up to four spawner/vault rooms from a pool, so the layout varies each throw), **Woodland Mansion** (the second grand structure — a two-storey dark-oak manor with a gabled roof on a larger grassy island; an evoker + vindicator garrison spawns in the hall via the theme's `animals` pack, so the guaranteed evoker makes it a reliable Totem-of-Undying source. A modular core start-piece draws up to three single-storey wings — storeroom/library/checkerboard room — from a pool, so the manor sprawls differently each throw, with up to six `woodland_mansion` chests), and **Ocean Monument** (`skyseed:ocean_monument` — a prismarine temple on a deep-ocean sand island: guardians + an elder guardian via the `animals` pack, a wet-sponge room, sea lanterns, and a buried-treasure chest with a Heart of the Sea for a conduit; also a 5% `rare_structures` roll on Large Aquatic). Built on the jigsaw system — loot chests, spawners, trial spawners, vaults and vanilla loot tables are block-entity NBT in the structure `.nbt` |
| Rare features | `rare_structures`: a chance-gated structure that germinates in place of the ordinary island. **Igloo** (5% on Frozen — a sealed snow dome with a trapped zombie villager to cure), **Abandoned cottage** (10% on Hamlet — a cobwebbed, bed-less ruin haunted by a zombie villager), **Ocean ruin** (8% on Aquatic — a flooded stone-brick basin with suspicious sand and a sunken chest, replacing the pond), **Desert Temple** (5% on Large Desert — sunk a block under the sand so only a suspicious hole gives it away, via the jigsaw `sink` field), **Jungle Temple** (5% on Large Forest grown in a `#minecraft:is_jungle` biome — a `biomes` filter on the rare structure gates the roll), **Witch Hut** (5% on Large Forest in a swamp/mangrove/`dark_forest` biome, or Large Aquatic in a swamp/mangrove — witch + cat included, pond suppressed for dry footing), **Dungeon** (5% on Large Rocky or Large Ancient — buried and sealed via the `sink` field, no stairs, so you only find it by digging onto it), **Pillager Outpost** (5% on a Trade Post — the watchtower replaces the village, villagers and all), **Ruined Portal** (1% on any big island except Aquatic — the broken frame + chest on the surface, the island keeps its own terrain), **Trail Ruins** (10% on Large Ancient, 5% on a Forest grown in a `#minecraft:is_taiga` biome — a buried mud-brick site with suspicious gravel to brush for pottery sherds, fragments poking up as the tell), **Evoker Cell** (5% on a Forest in a `dark_forest` biome — a sealed dark-oak room with an evoker for a bootstrap totem), **Vault Cell** (5% on Ancient — a buried tuff/copper room with two trial spawners + a vault, a self-contained mini trial-chamber and trial-key source). The surprise's own `mobs` pack spawns its inhabitants |
| Nether chapter | Overworld seeds **adapt or fizzle** across the portal (deliberately tiny ~7×7×4 Tier-1 footholds; Meadow/Frozen fizzle); five full-size **Nether-native** Tier-2 seeds — `nether_rocky` / `nether_lava` / `nether_forest` / `nether_soul` / `nether_basalt` — plus a **Large variant** of each, gated by a theme `dimensions` field; a **Nether Fortress** island (hand-built arcaded bridge + keep + caged blaze spawner) and a 5% **blaze spawner room** on the Large Nether seeds; **ruined-portal twins** that grow a linked frame in the other dimension at the vanilla 8:1 coordinate. The Nether/End are pre-voided (void + a lava sea below Y 32, biome sources kept) |
| Datapack themes | Full `IslandTheme` codec (the keystone); themes are pure JSON |
| Biome response | `biome_overrides` keyed to the germination biome (Forest rolls acacia over savanna, jungle over jungle, lake over ocean, …) |
| Y-band overrides | `min_y` / `max_y` gating — drives Rocky's deepslate ↔ coal/iron gradient and snow peaks |
| Water | Contained ponds and rivers (a containment ring walls the rim up to the water before decoration), sand/clay/gravel beds and shores; off-rim waterfall cascades; hand-built mangroves (`skyseed:mangrove`); per-island **bank styles** (steep / sloped / mixed) that step a river's banks down toward the waterline; shore plants (sugar cane only where water sits beside its support) |
| Surface | Per-column `surface_scatter` (block mixes); snow-capped peaks (taller shape + snow) |
| Lava | A `lava` theme field: a low-chance lava vein in the core, Y-banded contained lava lakes (Rocky/Ancient — likelier the deeper you throw; Aquatic below Y 0 comes up a bare stone/deepslate isle around a lava lake), and a pool at the foot of a Ruined Portal |
| World | Void world preset with multi-noise overworld biomes; structures disabled |
| Start | Curated start island; safe spawn (valid biome, on island not in a tree); first-join guide book; honours the vanilla *Generate Bonus Chest* world option with a starter kit chest |
| Guide | The Skyfarer's Almanac — **Patchouli optional**: the rich illustrated book when Patchouli is installed, a plain vanilla written book otherwise. Crafted from any one Skyseed (`#skyseed:skyseeds`); advancement-gated entries |
| Safety | Tick-budget placement (no single-tick stalls); block-overlap fit + horizontal nudge-off (islands sit flush), player-aware, fizzle-and-drop |

**The overworld and Nether chapters are both feature-complete** — all the overworld islands/structures, plus the full Nether chapter (adaptations, the five Tier-2 native seeds + their Large variants, all four Nether structures, ruined-portal twins). What remains is the non-village structural diversity (mansion footprints, fortress sprawl over the void) and then the End; see **Roadmap** below.

---

## Roadmap

The overworld and Nether chapters are built; what's planned next:

- **More varied structures** — the **villages** now differ each throw (street villages with fields, gardens, and piers/bridges out over the void, by using the jigsaw system *deeply* — street/connective pools + real recursion depth; `SKYJIGSAWPLAN.md`, **shipped v0.68–0.93**). Still to do, same approach: different **mansion** footprints and **Nether-fortress** sprawl over the void.
- **The End** — after the Nether's structures. Already pre-voided as of v0.35.1 (terrain emptied, the standard End biome source kept), so growing it out later won't force a new save.
- **A "Huge" island tier** — huge versions of the terrain islands, gated behind rare Nether ingredients.
- **A few more structures** that sit better later: a Stronghold (End-adjacent), a Mineshaft or Ancient City as deep-Ancient variants, and Buried Treasure / Shipwreck as Aquatic features.
- **Remaining vanilla blocks** — now down to essentially the copper bulb (the Nether-gated set became reachable when the Nether chapter completed), tracked in `MISSINGBLOCKSPLAN.md`.
- **Multi-version support** — building against multiple Minecraft / NeoForge versions from one codebase (`REFACTORPLAN.md`): the **Stonecutter skeleton + the `compat` facade are done** (Stages 0–1); adding the second version is deferred until 1.21.1 is feature-complete (NeoForge-only; no new runtime dependency; Fabric is a separate future concern).

> **⚠ Before 1.0 (cleanup):** **remove the `/emptynether` and `/emptyend` rescue commands** (`SkyseedCommands.java`, offered by the legacy-world warning in `PlayerEvents`). They're a one-time stopgap for worlds created *before* the void dimensions landed (v0.35.x) — by 1.0 there should be no pre-void world left to rescue — and the in-place conversion leans on Minecraft's **experimental-features** path, which is acceptable as a rescue route now but should **not** ship in a 1.0 release.

---

## The Skyseed item

**One item per theme, `skyseed:<theme>_skyseed`** (e.g. `skyseed:forest_skyseed`). Each is an `IslandSeedItem` instance carrying a fixed `theme()` id; all are registered from `ModItems.SEED_THEMES` and named in the `lang` file ("Forest Skyseed"). Distinct items mean each shows up individually in JEI/REI, and add-on mods can register their own seed item (pointed at their own theme) and add it to the `#skyseed:skyseeds` item tag. Every seed's icon is its own client model under `models/item/<theme>_skyseed.json`.

- **The `#skyseed:skyseeds` tag** holds every seed item — the guide recipe (any one skyseed → the Almanac) keys off it, and it's the integration point for add-on seeds.
- **Throwing.** Hold right-click to wind up, release to throw. Two modes, toggled by a keybind (default **V**, persisted in client config): **Precise** (the default) places the island directly along the look vector at a charge-scaled distance (5–40 blocks) and germinates exactly there; **Classic** lobs a charged physics arc (a tap lands close, a full ~1.25 s charge flies far) and germinates where it lands. On release the client sends a packet; the server reads the held seed's `theme()`, then validates and spawns the entity. Throw height/distance are how the player *chooses* an island's germination Y (and thus, on Rocky, its ore band).

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
| `biome_overrides` | BiomeOverride[] | `[]` | Conditional tweaks by biome and/or Y (and/or dimension) |
| `pond` | Pond | — | Optional contained pool |
| `mobs` | MobEntry[] | `[]` | Mobs/animals spawned when the island finishes generating |
| `jigsaw` | JigsawConfig | — | Assemble a structure on the island from a vanilla template pool |
| `animals` | AnimalPack[] | `[]` | Weighted farm-animal packs (Animal Islands; rare-structure mobs) |
| `rare_structures` | RareStructure[] | `[]` | Chance-gated structures that roll in place of / onto the island |
| `lava` | Lava | — | Lava veins + Y-banded contained lava lakes |
| `dimensions` | string[] | `["minecraft:overworld"]` | Which dimensions the base config implements; a seed thrown into one it doesn't implement (and no dimension-keyed override covers) **fizzles** |
| `twin` | id | — | Grow this theme at the vanilla 8:1 linked coordinate in the other dimension (the Ruined Portal's cross-dimension twin) |

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
| `dimension` | id | Gate the override to one dimension — the Nether/End form of a seed. An override for a *foreign* dimension is a **complete** spec: any field it doesn't set goes neutral/empty, never the overworld base |
| `fill_bands` | block-id[] | Banded body override (`[]` clears the base's bands) |

Examples in the shipped themes: Forest over `#minecraft:is_ocean` → lake island + pond + waterfalls; over `mangrove_swamp` → mud + hand-built mangroves; Rocky with `max_y: 8` → deepslate island with diamonds; Rocky with `min_y: 130` *or* a snowy biome → snow-capped peak; Rocky with `dimension: "minecraft:the_nether"` → a tiny netherrack mining island instead of fizzling.

---

## Generation algorithm

A near-pure function `planIsland(ServerLevel, BlockPos center, IslandTheme, Holder<Biome>, RandomSource) → IslandPlan`, then a scheduler applies the plan over several ticks.

1. **Match** the first applicable `biome_override` (biome + germination Y), producing the effective shape/palette/ores/variants/pond/waterfalls.
2. **Silhouette.** A 2D rim-radius **noise field** (never a clean sphere) extruded with a **teardrop** profile — flat-ish domed top, tapering underside.
3. **Layered fill.** surface → fill band (`fill_depth`) → core, with jitter.
4. **Ores.** Per entry: roll `chance`, then place `count` clustered **veins** of `vein_size` within the core, weighted by `depth`. The random-walk strongly prefers orthogonal (face) steps so veins are compact, not diagonal specks.
5. **Pond / waterfalls / decoration.** Carve the pond; pick weighted variant; place trees (vanilla features queued, or `skyseed:mangrove` hand-built into the plan) and per-column ground cover; add rim cascades.
6. **RNG.** `RandomSource.create(worldSeed ^ center.asLong())` — unique per island, reproducible, decorrelated from neighbours.

**Two guards:** *tick-budget placement* drains ~512 blocks + 2 trees per tick (no stutter; doubles as a grow-in animation, using block flags that avoid cascading neighbour/light updates); *placement safety* grows the island where it lands if clear, else nudges it **horizontally** off whatever it would interpenetrate — a block-level overlap test, so islands may sit flush but never merge — out to a "decent" distance, falling back to up/down lifts only when boxed in, and won't drop a block onto a player; if nothing's clear it fizzles and drops the seed back (see `IslandPlacement.check` + `IslandSeedEntity.findClearSpot`).

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
- **`worldgen/theme/`** — the codec records: `IslandTheme`, `Shape`, `Palette`, `OreEntry`, `Variant`, `Decoration`, `TreeEntry`, `GroundEntry`, `Pond`, `BiomeOverride`, `MobEntry`, `JigsawConfig`, `AnimalPack`, `RareStructure`, `Lava`, plus `IntRange`, `OreDepth`, `Underside`.
- **`worldgen/structure/`** — code-authored structure templates (`*Templates.java`, one per structure — hamlet, dungeon, ruined portal, nether fortress, …) written to `.nbt` at dev time via `DevStructureGenerator`, plus the shared `StructureParts` helpers (jigsaw anchors, loot chests, mob spawners, pitched `gableRoof`s, fence-linking).
- **`client/SkyseedClientEvents`** — renderers and the world-type default hook.

---

## Design decisions (resolved)

The plan's former open questions, settled by the build:

- **Naming.** "Skyseed" is the locked name for the mod and the thrown item; per-theme display names ("Forest Skyseed", …) come from the `item_name` component.
- **Germination point.** The seed germinates at its **rest position** after a fixed ~2 s arm (tuned down from the original 3 s) — wherever it stopped, or mid-air if still flying. Throw power therefore controls the island's Y.
- **Bridging.** v1 is **free-floating only**; the overlap nudge keeps islands from merging. Growing onto existing islands is deferred.
- **Theme codec.** Finalized and extended well past the original sketch (`biome_overrides`, `min_y`/`max_y`, `surface_scatter`, `pond`, `waterfalls`, `shape.top_dome`).
- **RNG seeding.** `worldSeed ^ center` only; the planned `throwCount` term was dropped as redundant — overlap safety already guarantees distinct centers.
- **Structure templates / jigsaw.** Adopted after all: villages, animal pens, the structure islands and both grand structures assemble from code-authored `.nbt` via vanilla `JigsawPlacement`. (The original "pure procedural is enough" call was reversed once structures landed.)

**Still open:** multiplayer client-sync hasn't been explicitly verified (generation is server-side and placed with client-updating block flags, so it *should* be fine).

---

## Modpack

Two CurseForge packs are planned, both on NeoForge 1.21.1:

- **Vanilla-like** — Skyseed plus a curated quality-of-life set, **no content mods** (nothing beyond vanilla blocks). Scaffolded in `modpack-vanilla/`.
- **Full** (later) — layers in content mods (Quark, the Delights, storage, tech, …), and each one is also wired into island generation as a new island tier, so its blocks have somewhere to grow.

Skyseed itself only optionally integrates with Patchouli; nothing else is referenced, so it drops cleanly into either pack.

---

## Building & running

Requires **JDK 21** (NeoForge 1.21.1's required Java). The build is pinned to a local JDK 21 via `org.gradle.java.home` in `gradle.properties` — adjust that path if your JDK lives elsewhere.

```sh
./gradlew build              # compile + package the mod jar
./gradlew runClient          # launch a dev Minecraft client with the mod loaded
./gradlew runServer          # launch a dev dedicated server
./gradlew runGameTestServer  # run the GameTest suite (exits 0 on pass); also /test runall in dev
```

The first invocation downloads Gradle, NeoForge, and Minecraft, so it takes a while.

### Tests

`gametest/SkyseedGameTests.java` holds a NeoForge GameTest suite that asserts generation/structure
invariants (every theme plans without error, generation is deterministic, structures keep their key
blocks). Run it with `./gradlew runGameTestServer` — it's the safety net to run before and after the
refactors in the codebase. For test **coverage**, run `./gradlew gameTestCoverage` (attaches the
JaCoCo agent to the run) → `build/reports/jacoco/gameTestCoverage/html/index.html`.

The build compiles with `-Xlint:all` (warnings stay visible in the log; the build is *not* `-Werror`).

## Repository layout

- `src/main/java/dev/gemberkoekje/skyseed/` — mod sources (`Skyseed.java` is the `@Mod` entry point).
- `src/main/resources/data/skyseed/skyseed/theme/` — the theme JSONs (`forest`, `forest_large`, `rocky`).
- `src/main/resources/` — assets, recipes, advancements, the Patchouli book, and the world preset.
- `src/main/templates/` — `META-INF/neoforge.mods.toml` source (Patchouli is an *optional* dep).
- `gradle.properties` — mod id/version and Minecraft/NeoForge versions.

Scaffolded from the [NeoForge ModDevGradle MDK](https://github.com/NeoForgeMDKs/MDK-1.21.1-ModDevGradle).
