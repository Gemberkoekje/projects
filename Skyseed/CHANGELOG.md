# Changelog

All notable changes to Skyseed are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and this project uses [SemVer](https://semver.org/).

## [0.26.0] - 2026-06-22

### Added
- **Trail Ruins** — a buried archaeology site: a mud-brick floor under a gravel layer salted with **suspicious
  gravel** to brush for pottery sherds (the vanilla `trail_ruins` loot tables), low broken walls, and a few
  fragments poking up through the surface as the only tell. It's sunk a few blocks, so you spot the fragments,
  dig in, and brush. Appears at **10% on a Large Ancient island** (its natural home — the deep, ancient isle)
  and **5% on a Forest grown in a taiga biome** (the vanilla home for Trail Ruins). No new seed — reuses the
  buried-structure (`sink`) and brushable-block mechanisms.

## [0.25.0] - 2026-06-22

### Added
- **Every big island (except Aquatic) now has a 1% chance to grow a Ruined Portal** — the broken Nether-portal
  frame with crying obsidian and a `ruined_portal` chest, sitting on the surface. A rare find across Large
  Forest / Rocky / Desert / Mushroom / Frozen / Meadow / Badlands / Ancient / Lush. The island keeps its own
  look (verified: a Large Frozen with a portal is still snow). Datapack-only — a 1% `rare_structures` entry on
  each large theme.

## [0.24.1] - 2026-06-22

### Changed
- **Drew the Outpost seed icon** — a dark-oak watchtower with a flag, arrow slits and a grass-and-dirt base,
  in the iconographic style of the other structure seeds (it had shipped with a placeholder copied from the
  Dungeon seed).

## [0.24.0] - 2026-06-22

### Added
- **Pillager Outpost — a new island, and a Trade Post surprise.** A dark-oak watchtower: climb the inside
  ladder past arrow slits to a **pillager spawner and a `pillager_outpost` loot chest** up top, while an
  **iron golem sits caged behind dark-oak fences at the base** — break the cage and it turns on the pillagers.
  - **🗼 Outpost Island** (`skyseed:outpost`) — its own seed (dark oak planks + crossbow + iron ingot), with an
    Almanac entry.
  - **A Trade Post has a 5% chance of coming up as a Pillager Outpost instead** of a village — no villagers, a
    hostile takeover. Reuses the `rare_structures` mechanism; the caged golem spawns via the jigsaw
    `iron_golems` field (lands at the centre, inside the cage).
  - Pillager **patrol spawning is now disabled** on Skyseed worlds (alongside raids), so pillagers are an
    Outpost encounter, not random wanderers.

## [0.23.0] - 2026-06-22

### Changed
- **The Dungeon is roomier and reworked.** The cramped 3×3 cell is now a **5×5×3 cobble room** — more space to
  move and fight, especially against spiders. On the dedicated **Dungeon island** the room is now **sunk into
  the ground** (like the Desert Temple) with a **stepped stairwell down to a dark-oak door**, and the (larger)
  island is strewn with **broken ruin stubs and ruined stonework**. Spawner + two `simple_dungeon` chests as
  before; the cobble is mossed by the weathering processor.

### Added
- **A Dungeon has a 5% chance on a Large Rocky or Large Ancient island** — buried and fully sealed (no stairs),
  so the only way in is to dig down onto it: a nasty surprise and a reminder not to dig straight down. The
  spawner still rolls zombie / skeleton / spider. (New `dungeon/buried` vs `dungeon/lair` pools; the burial
  reuses the jigsaw `sink` field.)

## [0.22.0] - 2026-06-22

### Added
- **A Witch Hut can now turn up on big islands grown in the right biome** — 5% on a **Large Forest** in a
  swamp, mangrove swamp or dark-oak (`dark_forest`) biome, and 5% on a **Large Aquatic** island in a swamp or
  mangrove swamp. The witch and her cat come with it. It stands on dry swampy ground (the island's pond is
  suppressed on a hut roll, the same way the ocean ruin replaces a pond). Reuses the `biomes` rare-structure
  filter from 0.21.0 — datapack only, no new code.
  - Verified: hut + witch appear in swamp and dark-oak forests and on a swamp aquatic island (lake gone); a
    jungle forest still grows the jungle temple instead (the two forest entries coexist); plains grows neither.

## [0.21.0] - 2026-06-22

### Changed
- **The Jungle Temple is rebuilt to look the part.** Instead of the old flat box it's now a tiered
  cobblestone-and-moss ziggurat — a 9×9 base stepping up through smaller tiers, with corner columns rising
  over it and vines trailing down the walls. The trapped inner chamber is intact: two loot chests and an arrow
  dispenser wired to a tripwire (verified live after assembly), reached through a front doorway.

### Added
- **A Jungle Temple has a 5% chance on a Large Forest island grown in a jungle** — any `#minecraft:is_jungle`
  biome, so bamboo and sparse jungles count too. It stands among the jungle trees (not buried — `sink` 0).
  - New `biomes` filter on a rare structure gates the roll to matching germination biomes (same id/`#tag`
    syntax as biome overrides). Verified end-to-end: in a real jungle the forest island grows jungle trees
    *and* the temple; in plains it grows neither.

## [0.20.0] - 2026-06-22

### Added
- **Structure islands can now also appear, buried, on big islands.** First one: a **Desert Temple has a 5%
  chance on a Large Desert island**, sunk a block beneath the sand so its sandstone roof is hidden and the only
  tell is a suspicious hole in the dunes (verified: zero sandstone shows at the surface; the island's own sand
  stays intact right up to the hole). Drop in and the central pressure-plate-over-TNT trap is waiting, exactly
  like the dedicated Desert Temple island.
  - New `sink` field on a theme's jigsaw config buries the structure N blocks below the levelled surface, so
    the island's own surface covers it; the temple's shaft punches up through that surface for the hole. The
    dedicated Desert Temple island is unchanged (`sink` 0). The oasis is suppressed on a temple roll so the two
    don't fight over the island centre.

## [0.19.11] - 2026-06-22

### Changed
- **Two small roof tweaks.** Dropped the extra upside-down rake stair at each eave corner (it stuck out on the
  side), and added a full block under the ridge slab at the gable-overhang peak (a touch nicer than slab-over-air).

## [0.19.10] - 2026-06-22

### Changed
- **Gable rake upside-down stairs, corrected.** Moved them out to the overhang plane (tucked under the
  overhanging rake stair, instead of replacing the gable wall), flipped their facing to downhill, and extended
  them down to the bottom eave step. 16 per cottage (8 per gable).

## [0.19.9] - 2026-06-22

### Changed
- **Precise throw mode is now the default** (most playtesters preferred it). New installs start in Precise
  (direct placement along your look vector); press **V** to switch to Classic (the charged arc). Existing
  players keep whatever they last had — delete `config/skyseed-client.toml` or press V once to pick up the new
  default. The in-game guide and store text were updated to match.

## [0.19.8] - 2026-06-22

### Changed
- **Smoothed the gable rake with upside-down stairs.** The diagonal gable edge now has upside-down stairs
  filling the step-notches (the topmost gable-fill block under each sloped course), so the rake reads as a
  solid diagonal rather than a blocky staircase. This is what the earlier "upside-down stairs at the overhang"
  request actually meant — they belong on the gable rake, not the slope-side eaves. 8 per cottage gable.

## [0.19.7] - 2026-06-22

### Changed
- **Cleaner roof eaves.** Removed the upside-down "soffit" stairs from under the overhanging eaves (added in
  v0.19.4) — they cluttered the slope sides. The overhang is now an open stepped eave, matching the intended
  look. Affects the cottages and the witch hut (the overhanging-roof structures).

## [0.19.6] - 2026-06-22

### Changed
- **The Desert Temple is buried now, like the original.** Reworked it to anchor on its **roof** so it sits
  flush with the (all-sandstone) island surface with the chamber hanging below — the only thing on top is a
  hole in the roof centre that invites a drop-in. A single pressure plate sits dead-centre on the floor over
  TNT and the buried cache, directly under the hole: drop in carelessly and you land on it (verified — a mob
  dropped through the hole detonates the chamber). Replaces the four corner plates from v0.19.5. The buried
  interior is carved with explicit air (which the jigsaw placement does honour).

## [0.19.5] - 2026-06-22

### Added
- **The temple traps are back.** A new post-placement pass (`Traps`) re-adds the support-dependent blocks that
  the jigsaw assembler drops, so the trapped structures have their jeopardy again:
  - **Desert Temple** — a pressure plate over hidden TNT at each floor corner, sitting over the buried 3×3
    cache. Step on one and the chamber goes off (verified: a mob stepping on a plate detonates it).
  - **Jungle Temple** — a tripwire strung across the dispenser's line of fire; tripping it powers the hook
    mounted on the dispenser, which looses arrows.
  - How: the fragile blocks are baked into the templates as **wool markers** (full blocks survive the jigsaw
    path and rotate with the piece); after assembly, `Traps` swaps each marker for its real block with full
    block updates, deriving a tripwire hook's facing from the adjacent string.

## [0.19.4] - 2026-06-22

### Changed
- **Roof + structure polish (from playtest feedback):**
  - Roof ridges are now capped with a **slab** instead of a full block — a slimmer, cleaner peak.
  - The overhang roofs (cottage, witch hut) get **upside-down soffit stairs** underneath, so the eaves are
    boxed in rather than open.
  - Cottages now have an accessible **loft**: a ladder climbs the back wall and punches through the ceiling
    into the attic (lit by the gable's loft window).
  - The **chicken coop** gained a gabled roof to match the rest of the buildings.

### Fixed
- **Pasture / Wool Farm / Stable fences render as a connected ring** instead of loose posts — the fence
  connection states are now baked into the template (structure placement does no neighbour updates, so the
  connections have to be stored, like the cottage doors).
- **Enclosure mobs spawn in a clear spot** rather than inside the furniture — the Witch Hut witch no longer
  materialises sitting in her own cauldron.

## [0.19.3] - 2026-06-22

### Changed
- **Gable roofs on the rest of the buildings.** Extended the pitched stair roof from the cottages to the
  **Trade Post shops**, the **Village Center halls**, and the **Witch Hut** — no more flat lids. The roof
  geometry is now a shared `StructureParts.gableRoof` helper: the single-piece Witch Hut gets the full
  one-block overhang (spruce), while the connector-based shops/halls use flush eaves so their jigsaw
  connections and the tight plaza packing stay intact. Verified the villages still assemble — 4 shop beds, all
  13 professions + iron golem, and the witch + cat.

## [0.19.2] - 2026-06-22

### Changed
- **Hamlet cottages got a proper roof.** Replaced the flat plank lid with a pitched, overhanging **gable roof**
  built from stairs rising to a ridge beam — far nicer in silhouette. Cottages also gained log corner posts,
  glass windows on three sides, and a glass loft window in the front gable. The three variants now use distinct
  woods — **oak, spruce, birch** — instead of all sharing oak. (Built with a one-block border so the roof
  overhang stays within the template's bounds. The Trade Post shops and Village Center halls still have flat
  roofs — the same treatment can be extended to them.)

## [0.19.1] - 2026-06-22

### Fixed
- **Structures no longer sit a block too deep.** `JigsawPlacement` seats a structure's anchor block at
  `origin.y − 1`, so every island building (cottages, plazas, igloo, ruin, animal pens, dungeons, …) sank one
  block into its levelled pad and left a grass lip around it. The jigsaw origin is now passed as `gy + 1` so
  floors land flush on the pad. Uniform across all structures — the Desert Temple's TNT still buries one below.

### Changed
- **Trade Post and Village Center are now enterable without breaking blocks.** Their buildings ringed the plaza
  flush on all four sides and met at the corners, sealing it in. The plazas are now two blocks wider than their
  buildings (Trade Post 5→7, Village Center 7→9), so each building leaves the plaza's corner columns open as
  walkable sneak-gaps to the bell and the traders. The Village Center island grew slightly to fit (radius
  15–18 → 16–19, pad 11 → 12).

## [0.19.0] - 2026-06-22

### Added
- **Rare structures on ordinary islands.** A new `rare_structures` theme field rolls a chance-gated structure
  that germinates in place of the usual island (the first whose chance hits wins, at most one per island). When
  rolled, its jigsaw replaces the theme's normal one (or becomes the only one) and its own `mobs` pack spawns
  the inhabitants; `suppress_pond` lets a flooded ruin stand in for the pond. Three surprises ship with it:
  - **Igloo** — 5% on Frozen islands. A sealed snow dome (kept dark so it survives daylight) with a brewing
    stand, a water cauldron, the vanilla `igloo_chest` (golden apple!), and a trapped **zombie villager** to
    cure. Simplified from vanilla: a surface dome rather than a hidden basement, and no separate cleric.
  - **Abandoned cottage** — 10% on Hamlet islands. A cobwebbed, gap-punched oak ruin with a `village_plains_house`
    chest and **no bed** — so no normal villager spawns, only the haunting **zombie villager**.
  - **Ocean ruin** — 8% on Aquatic islands. A weathered stone-brick basin (it replaces the central pond) holding
    a contained pool, **suspicious sand** (warm — the Sniffer-egg source) and gravel, and a sunken
    `underwater_ruin_big` chest.
- Brief field notes for each surprise added to the Frozen / Hamlet / Aquatic guide entries.

## [0.18.2] - 2026-06-22

### Changed
- **Made Large Rocky a proper mining mountain.** A 10-island measurement showed it was actually *leaner* than
  the small island — ~0.75% ore by volume at y≈100 (vs the small island's ~2.75%), because its vein counts
  didn't scale with its ~6× larger volume. Raised the bulk-ore (coal / iron / copper) counts on its mid /
  high / peak bands so density now lands ~1.8–2%, and total ore rose from ~54 to ~132 per island — clearly
  richer than the small island in total, while staying in the vanilla per-block range. The emerald gateway
  and the deep diamond band are unchanged.

## [0.18.1] - 2026-06-22

### Changed
- **Toned down Rocky ore density.** A 10-island measurement at the y≈100 start height put small Rocky islands
  at ~4.3% ore by volume (up to ~10% on the smallest) — about 1.5× vanilla's richest layers. Vein *counts* on
  the mid / high / peak bands were trimmed (vein size stays 4–8) to bring it to ~2.75–3%, and the
  smallest-island spike from ~10% to ~5.75%. The deep band and Large Rocky (the dedicated mining islands) keep
  their richer tables.

## [0.18.0] - 2026-06-22

### Fixed
- **The Frozen and Large Frozen recipes were uncraftable.** They keyed the ice ingredient off
  `#minecraft:ice`, which isn't a real item tag, so the slot resolved to nothing — it showed as a broken
  red cross in the guide and couldn't be filled. Added a `#skyseed:ice` tag (ice / packed ice / blue ice)
  and pointed both recipes at it.

### Changed
- **The animal and structure island recipes now use the 2×2 shaped layout** like the other small islands,
  instead of shapeless: Pasture (both wool and beef), Poultry, Wool Farm, Stable, Aquarium, Ruined Portal,
  Desert Temple, Jungle Temple and Witch Hut are each a four-ingredient 2×2 (e.g. Ruined Portal = obsidian
  over gold, Desert Temple = sandstone + TNT over sand).

## [0.17.0] - 2026-06-21

### Changed
- **Vein sizes moved from a generator multiplier into the theme JSONs.** The v0.16.0 in-code vein scaling was
  reverted; the Rocky/Ancient ore tables now set sizes directly — **iron, coal and copper are veins of 4–8**
  at normal throw heights. (The orthogonal vein growth from v0.16.0 stays.)
- **Rocky & Ancient ore tables reworked to track the vanilla ore-by-depth curve** (a simplified per-Y-band
  heuristic): thrown **deep** → deepslate diamond / redstone / gold / lapis (the richest haul); **mid-air** →
  an iron & copper peak with coal; **high / peak** → coal- and iron-heavy with none of the deep treasures —
  so a high island gives less than one thrown low. Copper is reliably common at mid heights, and deepslate
  copper now appears in the deep bands. Verified via RCON across deep/mid/high/peak throws.

### Fixed
- **The guide book shows its custom cover again.** Making Patchouli optional had dropped the custom item
  texture; the book now points its Patchouli `model` at the restored `skyseed:guide` texture.

### Guide
- Rocky and Ancient entries (and their Large variants) now name **copper** explicitly and spell out that
  **deeper throws are richer** — high islands yield coal and iron but not the deep treasures.

## [0.16.0] - 2026-06-21

### Changed
- **Ore veins are bigger.** The generator scales each sampled vein up (≈1.4–1.9×), with the common ores
  (higher presence chance) growing the most — so patches read as proper deposits.
- **Veins grow compactly.** Vein growth now strongly favours face-adjacent steps (up/down/N/S/E/W) over
  diagonals (~80/20), so ore looks like solid clusters rather than scattered diagonal specks. Diagonal steps
  still happen, just rarely.
- **Copper is now a common ore** on Rocky and Ancient islands (and their Large variants): presence chance and
  vein count bumped to roughly iron-level (≈0.7–0.85 per island), so it shows up reliably. Copper has existed
  since MC 1.17 and was already present, but only at ~50% per island.

## [0.15.0] - 2026-06-21

### Fixed
- **Ponds no longer overflow off the island.** A carved pool now gets a **containment ring** — every land
  column touching the water is walled up to the surface with the island's own fill/surface block (the "ring
  of dirt"), placed *before* decorations — so the water can't sheet over a low edge. Pools are also kept a
  little smaller (extent ≈ 0.62× the island radius, down from ≈0.87×) so the rim always has solid ground to
  wall against; where the pool genuinely sits against the very edge, a small waterfall is left as variety.
  (Verified: pond water now spans only its own depth, ~3–5 blocks, instead of cascading down the island.)

### Added
- **Water features carry sand, clay and gravel.** Every pond/river/lake bed is dressed with sandy / gravelly
  / clay patches, and the shore gets sandy and gravelly edges — the materials you'd expect in and around
  water.

## [0.14.0] - 2026-06-21

### Added
- **Four more structure islands** (see `SKYSTRUCTURESPLAN.md`), all built on the jigsaw system:
  - **Ruined Portal** (`skyseed:ruined_portal`) — a scorched basalt scene with a broken obsidian /
    crying-obsidian frame and a `minecraft:chests/ruined_portal` chest (crying obsidian is otherwise
    unobtainable up here). Obsidian + gold ingot.
  - **Desert Temple** (`skyseed:desert_temple`) — a sealed sandstone chamber, four
    `minecraft:chests/desert_pyramid` chests over a buried 3×3 cache of TNT. Sand + sandstone + TNT.
  - **Jungle Temple** (`skyseed:jungle_temple`) — a mossy-cobblestone room with two
    `minecraft:chests/jungle_temple` chests and a lootable arrow dispenser. Jungle planks + mossy
    cobblestone + tripwire hook.
  - **Witch Hut** (`skyseed:witch_hut`) — a spruce hut with a witch and a cat (via the `animals` pack) and a
    water cauldron; no chest, the witch's drops are the reward. Oak planks + brown mushroom + cauldron.
  - Each ships theme, recipe, advancement, guide entry, icon, and item + `#skyseed:skyseeds` tag entry.

### Changed
- Mobs spawned via the `animals` pack are now `setPersistenceRequired` — harmless for farm animals, but it
  keeps structure-island mobs (the Witch Hut's witch) from despawning. A shared `StructureParts` helper now
  holds the jigsaw anchor + loot-chest block-entity NBT used by the structure templates.

### Known limitation
- Support-dependent "trap trigger" blocks (a desert temple's pressure plate, a jungle temple's tripwire) do
  not survive the jigsaw structure-placement path, so they're omitted for now: the desert's TNT is reframed as
  a minable cache and the jungle's dispenser is left lootable. Restoring live traps would need a
  post-placement fragile-block pass.

## [0.13.0] - 2026-06-21

### Added
- **Dungeon Island** (`skyseed:dungeon`) — the first structure island (see `SKYSTRUCTURESPLAN.md`). A small
  mossy cobblestone island carrying a sealed 5×5×5 cube: a vanilla mob spawner (zombie / skeleton / spider,
  the luck of the throw) and two loot chests on the vanilla `minecraft:chests/simple_dungeon` table. Sealed
  and dark so the spawner keeps running — break in, clear it, loot it (the music discs Cat and 13 are
  dungeon-only). Crafted from cobblestone around a piece of rotten flesh. Ships theme, recipe, advancement,
  guide entry and icon.
  - Structure islands reuse the existing **jigsaw** system — no new engine field needed. The spawner and the
    loot chests are block-entity NBT baked into the structure `.nbt` (the spawner's mob via three weighted
    pool variants), and the loot reuses the vanilla table by id.

## [0.12.0] - 2026-06-21

### Changed
- **Patchouli is now optional.** With Patchouli installed, the Skyfarer's Almanac is the rich illustrated book
  as before; without it, the guide is a plain vanilla written book carrying a short text edition. The mod
  loads and plays fully without Patchouli (verified by booting with it removed). The single Patchouli API
  call is isolated in a `PatchouliCompat` class that only loads when Patchouli is present; the dependency is
  `optional` in the mod metadata and compile-only in the build.
- **The guide is granted and crafted via one helper, `SkyseedGuide.book()`** (Patchouli book or written
  book), so the first-join grant and the craft recipe always match. Crafting is now a small code recipe
  (`skyseed:guide`) that turns any one `#skyseed:skyseeds` item into the Almanac.

### Removed
- The custom `skyseed:guide` item — the guide is now a real written book (or the Patchouli book), not a
  bespoke item.

## [0.11.0] - 2026-06-21

### Changed
- **Each Skyseed is now its own item** (`skyseed:<theme>_skyseed`, e.g. `skyseed:forest_skyseed`) instead of a
  single `skyseed:island_seed` carrying a `skyseed:theme` data component. 28 distinct items, registered from
  `ModItems.SEED_THEMES`, each with its own model and lang name — so every seed shows up individually in
  JEI/REI, and add-on mods can register their own seed (pointed at their own theme). All seeds share the new
  `#skyseed:skyseeds` item tag; the thrown seed reads its item's fixed theme. The `skyseed:theme` data
  component and the generic item are removed.
- **The guide book recipe is now "any single Skyseed → the Almanac"** (shapeless, keyed off
  `#skyseed:skyseeds`), replacing the vanilla-book + seed recipe — a vanilla book was awkward to obtain in skyblock.

### Fixed
- **The guide book is granted only on first join**, not every login. The "guide given" and "start-island
  placed" flags now live in the world `SavedData` keyed by player UUID, so they survive relogs reliably (the
  previous player-persistent-data flag did not).

### Migration
- Breaking: old `skyseed:island_seed` items (and the `skyseed:theme` component) no longer exist. Generated
  islands are unaffected; any uncrafted seeds sitting in inventories/chests are lost. Re-craft from the
  (unchanged) recipes — each now yields its own distinct item.

## [0.10.0] - 2026-06-21

### Added
- **Five Animal Islands** — dedicated farm islands, each a fenced enclosure (jigsaw-placed) with a guaranteed
  pack of animals rolled inside:
  - **Pasture** (`skyseed:pasture`) — a fenced field with a cow / sheep / pig breeding pair (weighted, sometimes
    mixed), a hay bale and a water trough. Two unlock paths: wool OR raw beef, + planks + dirt.
  - **Poultry** (`skyseed:poultry`) — a walled coop of four chickens with a composter. Feather + egg + dirt.
  - **Wool Farm** (`skyseed:wool_farm`) — a roomy pen of five assorted-colour sheep. 2 wool + iron + dirt.
  - **Stable** (`skyseed:stable`) — a three-stall stable of horses / donkeys (rare mule) plus a loot chest
    with a chance of a saddle or horse armour. Leather + gold + planks.
  - **Aquarium** (`skyseed:aquarium`) — a glass tank of turtles, axolotls, a squid and tropical fish over a
    coral-and-sea-lantern floor. Turtle scute + prismarine shard + sand.
  Each ships theme, recipe, advancement, guide entry and icon. Verified via RCON: correct packs, babies,
  4-colour sheep spreads, stable loot chest, and submerged aquarium life.
- **`animals` theme field** — a weighted list of packs; exactly one is rolled into the enclosure centre at
  generation time. Each entry spawns N adults + N babies, sheep are given a random wool colour, and aquatic
  animals are spawned submerged. The enclosure itself is the theme's `jigsaw` structure.

### Changed
- `levelStructurePad` now lays the foundation in the theme's own surface/fill blocks (so the sand Aquarium
  gets a sand pad rather than a grass ring) instead of always grass over dirt.

## [0.9.0] - 2026-06-21

### Added
- **Village Center Island** (`skyseed:village_center`) — the premium, late-game village island, completing
  the villager progression (Hamlet → Trade Post → Village Center). A bell-topped cobblestone plaza with four
  themed trading halls branching off it (farm / smith / scholar / craft) and an **iron golem** on guard.
  Built by the jigsaw system with each plaza connector pointing at its *own* hall pool, so the layout is
  deterministic and **all 13 vanilla professions are guaranteed** — farmer, shepherd, fletcher, butcher,
  armorer, weaponsmith, toolsmith, librarian, cartographer, cleric, mason, leatherworker, fisherman. Crafted
  from 5 emeralds + an iron ingot + planks + cobblestone. Ships theme, recipe, advancement, guide, icon.
  Verified via RCON: 13 villagers, **all 13 distinct professions claimed within ~10 s**, 1 iron golem.

### Changed
- `JigsawConfig` gained an optional `iron_golems` count (golems spawned at the structure centre once assembled).
- **Structure foundation pads are now a disc, not a square.** The village footprints are plus-shaped (corners
  empty), so a round pad covers them while staying inside a round island's rim — square corners could float
  past the edge on the larger pads. Also gives the Hamlet and Trade Post a tidier round clearing.

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
