# Changelog

All notable changes to Skyseed are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and this project uses [SemVer](https://semver.org/).

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
