# Changelog

All notable changes to Skyseed are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and this project uses [SemVer](https://semver.org/).

## [0.8.0] - 2026-06-21

### Added
- **Trade Post Island** (`skyseed:trade_post`) — the mid-tier village island. A lantern-lit cobblestone
  plaza with shops branching off its four sides, assembled by the jigsaw system: a central plaza piece
  carries outward connectors that pull buildings from a pool — real vanilla-village-style, multi-piece
  assembly (the payoff of the v0.7.0 jigsaw pivot). Each shop holds a job-site block and a bed, so its
  villager moves in and takes up that trade on its own — farmer, librarian, fisherman, fletcher, or
  toolsmith — and no two trade posts share a layout. Crafted from planks + cobblestone + 3 emeralds;
  ships with theme, recipe, advancement, guide entry, and icon.

### Changed
- **Villagers are now placed by scanning the assembled structure for beds** (one villager per bed), so the
  same code serves the Hamlet's single cottage and the Trade Post's plaza of shops. They arrive unemployed
  and claim the nearby job sites themselves, exactly as in a natural village.

## [0.7.0] - 2026-06-21

### Changed
- **Buildings now use the vanilla jigsaw system.** Placement moved from "load one `.nbt` and stamp it" to
  real jigsaw assembly: a theme's `jigsaw` config points at a `worldgen/template_pool`, and the generator
  levels a pad and runs `JigsawPlacement.generateJigsaw` at the island centre — the same machinery vanilla
  villages use. This brings, for free:
  - **Random rotation** — cottages now face any direction.
  - **Structure processors** for organic variation — the Hamlet's `hamlet_weathering` processor mosses
    cobblestone and strips the odd oak log, so even same-style cottages differ.
  - A **weighted pool** of the three cottages, now a standard template pool, and the foundation for
    multi-building islands (Trade Post, Village Center) that branch pieces off jigsaw connectors.
  Cottage `.nbt` carry a "bottom" anchor jigsaw (emitted by the dev-time writer); drop in your own
  structure-block-authored `.nbt` and add it to the pool to extend it.

## [0.6.0] - 2026-06-21

### Added
- **NBT structure templating.** Buildings are now placed from `.nbt` structure templates (the standard
  structure-block format) loaded at generation time and stamped onto a levelled pad. A theme carries a
  weighted `structures` pool, so one of several buildings is chosen per island. This is the reusable
  foundation for the larger village islands (Trade Post, Village Center) and future structure islands —
  and you can drop in a structure-block-authored `.nbt` and add it to the pool.

### Changed
- **The Hamlet now varies.** Its cottage is picked from **three templates** (oak, spruce, and a small
  cabin), so no two hamlets look quite alike — replacing the single hand-built cottage. The villager,
  bed, recipe, and raid handling are unchanged.

## [0.5.2] - 2026-06-21

### Changed
- The Almanac's **Recipes** chapter is pinned to the front of the book (a priority entry) so it's easy
  to find instead of buried among the island entries. Added a note that a small island is a 2x2 of four
  blocks and a large one a 3x3.

## [0.5.1] - 2026-06-21

### Changed
- **Consistent seed recipes.** Every small island now costs exactly **4 blocks** in a 2×2 "stack" — the
  surface/feature on the top row, the base on the bottom: Desert = 2 sand over 2 sandstone, Rocky = 2
  stone over 2 cobblestone, Forest = 2 planks over 2 dirt, Meadow = 2 flowers over 2 dirt, Frozen = 2
  snow over 2 ice, Badlands = 2 red sand over 2 terracotta, Ancient = 2 deepslate over 2 cobbled
  deepslate, Lush = 2 azalea over 2 moss, Aquatic = 2 sand over 2 clay, Mushroom = red + brown mushroom
  over 2 dirt. (Previously these ranged from 2 to 4 blocks in mixed shapes.) Hamlet keeps its emerald
  gate as 2 planks over cobblestone + emerald. Large variants are unchanged (3×3, nine blocks).

## [0.5.0] - 2026-06-21

### Added
- **Villager progression — the Hamlet Island.** The first village island: a grassy isle with a
  hand-built oak cottage (bed, crafting table, torch, windows, door) and one resident **villager**. The
  villager arrives **unemployed** — place a job-site block (composter, lectern, cauldron, …) to give it a
  trade — and is dressed for the biome it grew over; its bed gives it a home to claim, restock and breed
  from. Crafted from planks + cobblestone + an **emerald** (mine a Rocky mountain for emeralds). Full
  pattern: theme, recipe, advancement, guide entry, icon.
- **Curated structures.** A theme can name a `structure` to stamp onto the island surface (the Hamlet's
  cottage is the first); a new plan step spawns villagers once the island lands.
- **Raids disabled on Skyseed worlds** (`disableRaids` gamerule) — illagers would only path into the
  void. Villager islands still trade, breed and spawn iron golems normally.

## [0.4.1] - 2026-06-21

### Changed
- The Almanac's **Recipes** entry now gives a sourcing hint for every seed — where to find what it takes,
  not just the ingredients. E.g. Ancient = deepslate from a Rocky island thrown deep; Lush = moss from an
  Ancient island plus azalea from a Forest; Aquatic = clay and sand from a Desert island; Frozen = snow
  from a snowy island or a Rocky snow-cap.

## [0.4.0] - 2026-06-21

### Added
- **Rivers.** A pond can now have `"style": "river"` — instead of a central pool it cuts a meandering
  channel across the island (`radius` = half-width). River biomes use it: a Forest Skyseed over a river
  grows a wood with a river running through it; an Aquatic Skyseed cuts a salmon-stocked channel.

### Changed
- **Water carving has variety.** Ponds are no longer perfect circles — their edge is wobbled with a few
  angular harmonics, so each pool is a unique blob.
- **Water features are properly contained.** Every water column is only carved where the island body
  reaches below the pool floor, so water can never hang off the rim or take a slab of the island with it
  (fixes aquatic islands losing a side). Large Aquatic's lake radius trimmed 8→6 for extra margin.
- **Small Aquatic ocean reef toned down.** A small island no longer gets the full ten-piece reef (which
  looked silly at that scale) — just a couple of corals, sea pickles, seagrass, and a turtle. The Large
  Aquatic island keeps the full reef.

## [0.3.7] - 2026-06-21

### Changed
- **Large Frozen no longer always has a frozen lake — it's biome-driven.** The ice lake used to be an
  unconditional feature. Now it only forms over frozen biomes (frozen ocean/river, frozen & jagged
  peaks, ice spikes, snowy slopes), where the island also turns properly icy — packed/blue-ice surface
  and more ice spikes. Over snowy biomes (snowy plains/taiga/beach, grove) it's deep powder snow and
  spruce with no lake; elsewhere a snowy-leaning mix. Guide text updated.

## [0.3.6] - 2026-06-21

### Changed
- **Forest & Large Forest stay a forest in every biome.** The biome overrides used to *replace* the
  forest — beaches and deserts became treeless sand/dirt patches, oceans a near-empty lake, mushroom
  fields a mushroom island. They now keep grass and trees everywhere and only add the biome accent:
  a pond in watery biomes (ocean / river / swamp / mangrove), sandy ground on beaches and in deserts,
  mycelium patches with a couple of giant mushrooms over mushroom fields, and the local tree species
  elsewhere. Plains is a lighter wood rather than near-empty grass. Guide text updated to match.

## [0.3.5] - 2026-06-21

### Fixed
- **Ponds now sit flush with the island surface instead of recessed in a pit.** The water level used the
  island's un-domed base height, so on domed islands — the large variants especially — the pool sat 2–3
  blocks below the surrounding ground. Water now fills to the island top at the pond's rim. Large pond
  depths trimmed to suit (Large Aquatic 7→5, Large Lush 5→4). Bonus: sugar-cane banks survive better now
  that the shore sits at the water's edge.

## [0.3.4] - 2026-06-21

### Fixed
- **Death respawn now returns you to the start island.** First-join set the world spawn but never the
  player's own respawn point, so dying ran vanilla's area-search around the world spawn and could drop
  you onto a different, nearby island you'd built. Players without a bed are now pinned (forced
  respawn) to the start island on login; sleeping in a bed still takes over as normal. Existing
  characters are fixed on their next login (anyone who hasn't slept in a bed).

## [0.3.3] - 2026-06-21

### Fixed (guidebook)
- **Forest / Large Forest** entries no longer imply they always grow an oak forest. Both now lead with
  the biome dependence — a Forest Skyseed thrown over a desert is a treeless dirt-and-sand patch, not a
  grove. Large Forest also dropped the stale "costs oak logs" line (the recipe takes any log as of 0.3.1).

## [0.3.2] - 2026-06-21

### Fixed (guidebook)
- Corrected Skyfarer's Almanac text to match actual generation, and added throw-height hints:
  - **Rocky** — dropped "iron is guaranteed and plentiful" (really ~80%, small veins) and the "rare
    diamond in the core" claim (a normally-thrown Rocky has *no* diamond). Added a **Height is
    everything** page: ore depends on the altitude the island forms at — high → coal/iron + a chance of
    emerald (snow-capped); mid → the broad iron/copper/coal/lapis/gold spread; deep (y ≤ 8) → deepslate
    rich in diamond/redstone/lapis/gold.
  - **Ancient** — noted the same height effect (grow it deep for thicker diamond/emerald).
  - **Large Rocky** — added the deep-throw deepslate/diamond case alongside the snow cap.
  - **Mushroom** — removed the false "hostile mobs won't spawn on mycelium" claim (islands take their
    germination biome); now describes mycelium's any-light mushroom farming.
  - **Lush / Aquatic** — mention the pool axolotls and the squid / tropical fish added in 0.2.2.
  - **Introduction** — added a note that throw height matters for some islands.

## [0.3.1] - 2026-06-21

### Changed
- Skyseed recipes now accept generic material groups where it makes sense, so you aren't forced to use
  one specific variant:
  - **Forest** — any planks (was oak planks).
  - **Large Forest** — any logs, i.e. any tree (was oak logs).
  - **Frozen** / **Large Frozen** — any ice (ice / packed ice / blue ice).
  - **Lush** / **Large Lush** — azalea *or* flowering azalea.
  Recipes that are intentionally variant-specific are unchanged (Rocky, Desert, Mushroom, Badlands,
  Ancient, Aquatic) — e.g. red sand stays the Badlands signature and the two mushrooms stay required.

## [0.3.0] - 2026-06-21

### Added
- **A Large variant of every island type.** Joining Large Forest and Large Lush, there are now Large
  seeds for **Rocky, Desert, Mushroom, Frozen, Meadow, Badlands, Ancient, and Aquatic** — each a
  bigger, pricier island (crafted from a 3×3 of its theme blocks) with its own advancement-gated guide
  entry, creative-tab example, and disk icon. Each has a thematic twist beyond just scale:
  - **Large Rocky** — a tall, peaked **mountain** that rises well above its rim, carrying **emerald
    ore** and goats; snow-capped when grown high or over a snowy biome.
  - **Large Aquatic** — a **proper deep lake** (a wide, deep pool) with squid, salmon, and **glow
    squid**; a deep coral reef with tropical fish/cod/pufferfish over warm oceans.
  - **Large Desert** — sweeping dunes with a small **oasis** pool, and larger buried bone-block fossils.
  - **Large Frozen** — a glacier with a **frozen lake** of solid ice, plus foxes.
  - **Large Mushroom** — a dense giant-mushroom grove.
  - **Large Meadow** — a vast flower field with many more bee nests.
  - **Large Badlands** — a towering, thickly-banded **mesa**.
  - **Large Ancient** — a deep slab with the full deepslate ore suite, larger geodes and dripstone.

## [0.2.2] - 2026-06-21

### Added
- **Water-spawned mobs** — a `water_mobs` list on a pond spawns animals submerged inside the pool
  (placed below the surface so they don't beach). Aquatic freshwater pools get **squid**; the warm
  reef gets **tropical fish**; Lush pools get **axolotls**.
- **Large Lush island** (`skyseed:lush_large`) — a bigger, pricier Lush isle built around a deep
  central pool, home to **glow squid** (which won't settle in a small Lush pond) as well as axolotls.
  Full pattern: recipe (moss + azalea), advancement, guide entry, creative-tab example, disk icon.

## [0.2.1] - 2026-06-21

### Added
- **Bees** — Meadow bee nests now come **populated with bees** (3 per nest); they emerge to pollinate
  the flowers and return home, so the bee island finally has its bees.
- **Sugar cane** on Aquatic pond banks — grows at the water's edge (freshwater, swamp, mangrove pools).
- **Ice spikes** on Frozen islands — hand-built packed-ice spires on the glacier and deep-freeze variants.

### Fixed
- Mob sprinkles now spawn reliably on decorated islands. The 0.2.0 spawn check required the block
  above the surface to be air, so flowers/grass/mushrooms blocked most spawns; it now allows any
  non-motion-blocking block there.

### Added (branding)
- `icon.png` (CurseForge mod icon) and `description.md` (store description).

## [0.2.0] - 2026-06-21

### Added
- **Mob spawning on island generation.** Themes can now sprinkle animals onto islands via a `mobs`
  list (`{ "entity", "chance", "count" }`), settable at the theme level, per biome-override, and per
  variant (variant mobs add to the theme/override set; an override's `mobs` replaces the theme's).
  Mobs are spawned directly when the island finishes generating — independent of light or time of day.
- **Animal sprinkles on the existing islands:**
  - Forest / Large Forest: cows, pigs, sheep, chickens — plus foxes & wolves on taiga, a parrot in
    the jungle, and horses/llamas/armadillos on savanna. Mooshrooms over mushroom fields; rabbits over
    desert/badlands.
  - Meadow: sheep, cows, pigs, chickens, rabbits, and the occasional horse or donkey.
  - Mushroom: mooshrooms. Desert: rabbits. Frozen: rabbits and a polar bear. Badlands: armadillos.
  - Aquatic: frogs (swamp/mangrove), a swamp cat, and a warm-reef turtle.

### Notes
- Dedicated **Animal Islands** (Pasture, Poultry, Wool Farm, Stable, Aquarium) are still planned, not
  yet built — see `SKYANIMALSPLAN.md`.
- **Water mobs** (squid, axolotl, glow squid) are deferred — they need in-pond spawn positioning.
- Sprinkle animals use vanilla wander AI; on small islands they can roam toward the edge (per design).

## [0.1.0] - 2026-06-21

The first complete version of the mod.

### Added
- **The core loop:** a throwable, charge-to-launch **Skyseed** that arms ~2 s then germinates a
  procedurally generated floating island (irregular teardrop silhouette, layered fill, clustered ore
  veins, tick-budgeted "grow-in", overlap safety).
- **Two throw modes:** Classic (charged physics arc) and Precise (direct placement along the look
  vector), toggled by a keybind (default V) and persisted in client config.
- **All 11 island types**, each its own crafted seed with an advancement-gated guide entry and icon:
  Forest, Large Forest, Rocky, Desert, Mushroom, Frozen, Meadow, Badlands, Ancient, Lush, Aquatic.
- **Data-driven themes** (`skyseed:theme` datapack registry): shape, palette, ore tables, weighted
  variants, biome overrides, ponds, and more — new islands are pure JSON.
- **Biome-aware generation** (islands take after the biome they're thrown over) and **altitude-aware**
  ore tables on the mining islands (Rocky, Ancient).
- **Generation features:** banded vertical fill (badlands strata), underside-hanging decoration
  (dripstone, cave vines, spore blossoms, roots), contained ponds with water plants, off-rim
  waterfalls, two-tall plants, per-column surface scatter, snow peaks, and hand-built trees
  (mangrove, azalea) for vanilla features that won't place on floating islands.
- **World:** a void Skyblock world preset with a full multi-noise biome map (structures off), a
  curated soft-lock-proof start island, and a safe first-join spawn.
- **The Skyfarer's Almanac** — an in-game Patchouli guidebook whose island entries unlock as you craft.
