# Changelog

All notable changes to Skyseed are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and this project uses [SemVer](https://semver.org/).

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
