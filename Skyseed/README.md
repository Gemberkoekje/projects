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

**All planned engine milestones (0–9) are complete**, plus several post-plan features. What exists today:

| Area | Built |
|---|---|
| Core loop | Throwable charge-to-launch seed → arm timer → germinate → tick-budgeted grow-in |
| Themes (seeds) | **Forest** (`skyseed:forest`), **Large Forest** (`skyseed:forest_large`), **Rocky** (`skyseed:rocky`), **Desert** (`skyseed:desert`) |
| Datapack themes | Full `IslandTheme` codec (the keystone); themes are pure JSON |
| Biome response | `biome_overrides` keyed to the germination biome (Forest rolls acacia over savanna, jungle over jungle, lake over ocean, …) |
| Y-band overrides | `min_y` / `max_y` gating — drives Rocky's deepslate ↔ coal/iron gradient and snow peaks |
| Water | Contained ponds; off-rim static waterfall cascades; hand-built mangroves (`skyseed:mangrove`) |
| Surface | Per-column `surface_scatter` (block mixes); snow-capped peaks (taller shape + snow) |
| World | Void world preset with multi-noise overworld biomes; structures disabled |
| Start | Curated start island; safe spawn (valid biome, on island not in a tree); first-join guide book |
| Guide | Patchouli book (Forest / Rocky / Large Forest entries, advancement-gated to "crafted it once") |
| Safety | Tick-budget placement (no single-tick stalls); overlap nudge + fizzle-and-drop |

**Not built:** the other 7 island types in [SKYISLANDSPLAN.md](SKYISLANDSPLAN.md) (Mushroom, Frozen, Meadow, Aquatic, Ancient, Badlands, Lush) — several currently exist as *Forest biome-overrides* rather than dedicated seeds. Nether/End skyblock dimensions are a long-term goal.

---

## The Skyseed item

**One item, `skyseed:island_seed`.** It is *not* one item per theme — every Skyseed is the same item carrying a different `skyseed:theme` data component (a `ResourceLocation`), set by the recipe that crafted it. A `minecraft:item_name` component gives each its display name ("Forest Skyseed"). An optional `minecraft:custom_model_data` int selects a distinct per-theme icon via a client model (e.g. Rocky); without it the default Skyseed model is used.

- **Throwing.** Hold right-click to wind up, release to throw — a tap lobs it in close (`MIN_VELOCITY`), a full ~1.25 s charge launches it far (`MAX_VELOCITY`). Throw height and distance are how the player *chooses* an island's germination Y (and thus, on Rocky, its ore band).
- Recipe *inputs* can't match on item components, so a Skyseed can't be crafted from another Skyseed; crafting from ordinary/modded items is unaffected.

This is what makes the data-driven model possible: a new Skyseed is one recipe JSON pointing at one theme JSON. No new item, no Java.

---

## Configuration

The dividing line: **config owns the *what & how much*** (recipes, palettes, ore tables, variants, shape, overrides); **code owns the *how*** (the generation algorithm).

### Recipe (data)

A normal datapack shaped-crafting recipe whose `result` is the seed item plus components. The Forest seed — planks + dirt checkerboard:

```json
{
  "type": "minecraft:crafting_shaped",
  "pattern": ["PD", "DP"],
  "key": { "P": { "item": "minecraft:oak_planks" }, "D": { "item": "minecraft:dirt" } },
  "result": {
    "id": "skyseed:island_seed",
    "components": {
      "skyseed:theme": "skyseed:forest",
      "minecraft:item_name": "\"Forest Skyseed\""
    }
  }
}
```

> **Gotcha:** `item_name` in a recipe result uses the legacy string-component serializer — the value must be a **JSON-escaped string** (`"\"Forest Skyseed\""`), not a bare string.

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

**Palette** — `surface`, `fill`, `core` block ids (required) · `fill_depth` int (3) · `surface_scatter` GroundEntry[] (`[]`, mixes blocks into the surface per column).

**OreEntry** — `block` (required) · `chance` float presence-roll (required) · `count` `{min,max}` veins (required) · `vein_size` `{min,max}` (required) · `depth` (`core` | `deep_core`; deep = lower ~40% of the core).

**Variant** — `weight` int (1) · `name` string · `surface_override` block id · `decoration` { `trees`: TreeEntry[], `ground`: GroundEntry[] }.

**TreeEntry** — `feature` id (required; a vanilla configured feature, or **`skyseed:mangrove`** for the hand-built mangrove) · `tries` int (3) · `spacing` int (3).

**GroundEntry** — `block` id (required) · `chance` float per-column (required).

**Pond** — `block` fluid id (`minecraft:water`) · `radius` int (3) · `depth` int (2). Walled by the domed rim and placed without block updates, so it stays still and never spills into the void.

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
4. **Ores.** Per entry: roll `chance`, then place `count` clustered **veins** (random-walk of `vein_size`) within the core, weighted by `depth`.
5. **Pond / waterfalls / decoration.** Carve the pond; pick weighted variant; place trees (vanilla features queued, or `skyseed:mangrove` hand-built into the plan) and per-column ground cover; add rim cascades.
6. **RNG.** `RandomSource.create(worldSeed ^ center.asLong())` — unique per island, reproducible, decorrelated from neighbours.

**Two guards:** *tick-budget placement* drains ~512 blocks + 2 trees per tick (no stutter; doubles as a grow-in animation, using block flags that avoid cascading neighbour/light updates); *overlap safety* nudges germination up through `{0, 8, 16, 24}` and, if nothing is clear, fizzles and drops the seed back rather than carving into existing terrain.

---

## World & progression setup

- **World preset `skyseed:skyblock`** — a void `noise_settings` (final density 0, air blocks, `sea_level: -64` to dodge the hardcoded lava floor) with a **multi-noise overworld biome source**, so F3 shows real, varied biomes over empty sky. Selectable as a world type and defaulted on the create-world screen, with structure generation turned off.
- **Start island** — a curated build placed at world creation (guarded by a new-world check), with spawn forced onto a valid land biome and onto the island surface (not into a tree).
- **First join** — the player is teleported to the start island and given the Patchouli guide.

---

## Architecture

`dev.gemberkoekje.skyseed`:

- **`Skyseed`** — `@Mod` entry point; registers everything below. `MODID = "skyseed"`.
- **`registry/`** — `DeferredRegister`s for the item, throwable entity type, the `skyseed:theme` data component, creative tab; and `SkyseedRegistries` (the `skyseed:theme` datapack registry, via `DataPackRegistryEvent`).
- **`item/`** — `IslandSeedItem` (charge-to-throw) and `GuideItem` (opens the Patchouli book).
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
- `src/main/templates/` — `META-INF/neoforge.mods.toml` source (Patchouli is a required dep).
- `gradle.properties` — mod id/version and Minecraft/NeoForge versions.

Scaffolded from the [NeoForge ModDevGradle MDK](https://github.com/NeoForgeMDKs/MDK-1.21.1-ModDevGradle).
