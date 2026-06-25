# Changelog

All notable changes to Skyseed are recorded here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and this project uses [SemVer](https://semver.org/).

## [0.75.0] - 2026-06-25

### Added
- **Bigger buildings (experiment): the blacksmith is now a 5×7 L-shaped forge.** It replaces the 5×5 toolsmith cabin
  with a larger L-shaped building — a stone-faced forge room with a furnace + chimney, and an open cobblestone patio
  in the notch with an anvil and a fence railing. It stays 5 wide (so it fits between lots ~6 apart on a street) but
  runs 7 deep into the open space beside the lane, proving the jigsaw can place larger footprints. A gametest confirms
  it attaches and places (its anvil appears). Being bigger it's more often overlap-rejected in tight spots, so it
  shows up less than the 5×5 shops — the "jeopardy" of bigger pieces; wider buildings would need lots spaced further
  apart (a follow-up). Applies across every biome palette.

## [0.74.1] - 2026-06-25

### Added
- **Snow layer on snowy villages.** The snowy trade post and hamlet now sit on snow-covered grass (a `minecraft:snow`
  ground decoration) instead of a solid snow-block surface, so the snow reads as a thin layer like a real snow biome.

### Fixed
- **Over-void foundations no longer pillar down into nothing.** The foundation pass dropped a fixed 6 blocks even over
  pure void; it now searches a short way down for ground and connects its foundation to it, or — finding none — drops
  just a 2-block stub.

## [0.74.0] - 2026-06-25

### Changed
- **The hamlet is now biome-aware and reuses the trade post's buildings.** It was a single generic cottage; now it
  starts from a small village green whose lot connectors pull the trade post's `lots` pool — so a hamlet places 1–2
  of the same diverse profession shops (forge / library / fletcher / farmer / fisherman), capped, in the biome's
  materials, reusing the over-void foundation pass and villager spawning. Biome overrides mirror the trade post
  (desert sand/sandstone, savanna acacia, taiga/snowy spruce). The only new piece is a tiny per-biome hub — no new
  building designs were authored. (The old cottages remain as gametest fixtures.)

## [0.73.0] - 2026-06-25

### Added
- **Per-profession trade-post buildings** (first slice). The five shops were identical 5×5 cabins differing only by
  their job-site block; now each has a distinct design — a roof shape (gable cottage / flat / stepped pyramid) plus a
  profession feature. The blacksmith stands well apart: a stone-fronted forge with a furnace and a chimney up through
  a flat roof. Farmer and fisherman keep gable cottages (composter / barrel), the librarian gets a stepped roof and
  bookshelves, the fletcher a flat roof and a hay store. Applies across every biome style (built from each palette).

## [0.72.1] - 2026-06-25

### Fixed
- **Over-void roads no longer grow a sizable dirt floor.** The foundation pass ran *after* the lanes were bridged and
  treated the bridge's plank edge-beams as floor to support, stilting dirt under lanes that are meant to float. It now
  runs *before* the bridges are laid (a lane is still a marker over an empty deck, so it's skipped) and is
  material-agnostic — so only buildings/fields/gardens over the void get a foundation (in any biome's materials), and
  the lanes stay floating bridges.

## [0.72.0] - 2026-06-25

### Added
- **All five vanilla village styles for the trade post.** Following the desert slice, added savanna (acacia) and a
  shared spruce set used by both taiga and snowy — which diverge by island surface for now (taiga on grass with
  ferns, snowy on snow) and can split into separate sets later for snow caps. So a trade post germinating in a
  desert/savanna/taiga/snowy biome builds from biome-matched materials on a biome-matched island; plains stays the
  oak default. Added debug seeds for savanna/taiga/snowy in the Skyseed Debug tab to spawn each on demand.

## [0.71.1] - 2026-06-25

### Added
- **Forced-biome debug seeds** (creative-only, in the "Skyseed Debug" tab). A seed can now carry an optional forced
  biome: the debug seed germinates an existing theme *as if* it were planted in that biome, so the real biome
  overrides fire — no duplicated theme files. Lets you inspect any biome-adaptive island on demand without flying to
  the biome. Added: trade post (plains / desert), forest (taiga / dark forest), desert (badlands), rocky (snowy),
  aquatic (ocean / swamp), frozen (ice spikes), ladder (desert). Like the other debug seeds: no recipe/tag/guide.

## [0.71.0] - 2026-06-25

### Added
- **Biome-styled trade posts — first slice: desert.** A trade post that germinates in a desert now builds from its
  own sand/sandstone piece set on a sand island, instead of always grass + oak buildings. The trade-post generator is
  now palette-driven, so each biome gets a *distinct* piece set rather than a recolour — the shapes are near-copies
  for now, but this is the structure that lets building details (flat roofs, snow caps, …) diverge per biome later.
  `BiomeOverride` gained an optional `jigsaw`, so a biome can swap the whole jigsaw build (here: the desert pool +
  a sand surface + dead-bush ground). Working toward vanilla's five styles (desert / plains / savanna / snowy / taiga).

## [0.70.2] - 2026-06-25

### Changed
- **A trade post now reliably lands its rolled 2–4 shops**, instead of sometimes ending up with 0–1. A cap can only
  trim a surplus — it can't conjure shops when the random assembly happens to roll few — so the lot pool is now made
  entirely of shops and the assembled piece list is normalised before stamping: the rolled number of shops nearest
  the centre are kept, and every surplus lot is re-stamped in place from a new `fillers` pool (wheat fields / gardens).
  So the planned house count is guaranteed whenever that many lots placed (a tiny island may still place fewer, which
  is acceptable), and the remaining lots become the fields. New `JigsawConfig.cap_filler` names the replacement pool.

## [0.70.1] - 2026-06-25

### Changed
- **A trade post now rolls a target of 2–4 shops up front**, instead of always landing exactly 4. The target is
  rolled from the island RNG when the plan is built — so it's reproducible per seed but varies between trade posts —
  and the shop cap then trims to it. Most trade posts land 2–4 shops (occasionally fewer if a lot can't attach).
  New `JigsawConfig.cap_min`: when set in `[1, cap_count)`, the generator rolls the cap in `[cap_min, cap_count]`;
  left unset it keeps the fixed `cap_count` behaviour.

## [0.70.0] - 2026-06-25

### Added
- **Per-element caps on jigsaw assembly** (`JigsawConfig.cap_prefix` / `cap_count`). Vanilla's jigsaw has no native
  limit on how many of an element it places, so a pool allowed to run long places as many shops as fit. The compat
  layer now mirrors `generateJigsaw` but, after assembling the full piece list, drops any piece whose element name
  contains `cap_prefix` beyond the `cap_count` nearest the centre — keeping the central ones — before stamping. The
  spare lots simply don't get their (capped) building.

### Changed
- **The Trade Post is now a proper 2–4 building trade post.** Its streets run long and branch (so plenty of lots,
  fields, and over-void piers form), but a `shop_` cap of 4 holds the shops to a tidy handful while the remaining
  lots fall to wheat fields and gardens. Rebalanced the lot pool toward fields (fields were too rare before — three
  villages in a row could have none).

### Fixed
- **Buildings and piers no longer float over the void.** A new post-assembly pass (`PathSurfacer.supportFloatingFloors`)
  drops a short dirt foundation under any building or bridge floor left hanging over an open drop, so a lot or pier
  that ran off the island edge reads as anchored rather than floating in mid-air (e.g. the garden-on-a-lantern that
  hung in the sky).

## [0.69.2] - 2026-06-25

### Fixed
- **Trade Post shops now appear reliably**, and dev-generated structure templates reach the run without a manual
  wipe.
  - The street runs terminated too early, so a village often grew only one or two building lots (frequently no
    shop). Lengthened the runs (far fewer empty terminators) so a village reliably grows several lots — most
    villages now show multiple shops alongside the fields/gardens.
  - **Build:** new `syncDevStructures` Gradle task refreshes this Stonecutter version node's copy of the
    code-generated `.nbt` from the repo-root src before `processResources`, so editing a `*Templates.java` (or
    pulling new structures) takes effect on the next `runClient` — no more deleting `versions/<v>/src` by hand.
    (This was the real cause of the earlier shop-less villages: a stale node copy shadowed the current template.)
  - The `tradePostVillagePlacesShops` gametest now assembles five villages and asserts shops appear in aggregate,
    so it can't flake on an unlucky single roll.

## [0.69.1] - 2026-06-25

### Fixed
- **Trade Post villages now reliably place shops.** The streets branched into a dense grid (a 4‑way *cross*
  piece), and a 5×5 building lot hung off a 3‑wide street collided with neighbouring streets/lots — the jigsaw
  overlap check then rejected it, so most lots (and often *every* shop) silently dropped, leaving a square with a
  couple of fields and no houses. Dropped the cross piece — streets are now straights + corners, so lots have
  open space along the sides and shops/fields/gardens place reliably. A new gametest assembles a village on a
  flat platform and asserts shops actually appear (guards the regression). The underlying jigsaw treats shops and
  fields identically; the fix is purely about giving lots room.

## [0.69.0] - 2026-06-25

### Changed
- **The Trade Post is now a real street village (SKYJIGSAWPLAN Phase 1).** Its fixed plaza‑plus‑four‑shops gave
  way to a jigsaw village: a cobblestone **square** radiates a `streets` pool (straight / corner / cross + an
  empty terminator) that branches and twists at **depth 6**, with buildings hung off side `lot` connectors from a
  `lots` pool — the five trade shops plus non‑shop scenery (a fenced, watered, grown **wheat field** and a flower
  **garden**) and a weighted empty terminator so the village breathes. The lanes are marker‑surfaced by
  `PathSurfacer`, so they read as terrain‑aware **dirt paths** on the island and **self‑railing wooden bridges**
  where a lane runs out over the void — little piers off the village edge, for free.
- The villager bed scan and the connection‑link pass now follow the structure's `reach` instead of a fixed
  16‑block box (SKYJIGSAWPLAN §5.1), so every shop across the sprawl still gets its villager and its fences
  linked — and both skip unloaded chunks so a wide reach never force‑loads the void.

## [0.68.2] - 2026-06-25

### Fixed
- Path tiles surface as a **uniform worn `dirt_path`** instead of a dirt/gravel mix. The old scatter formula
  degenerated — `x*7 + z*5 mod 5` reduces to depend only on `x` — so gravel came up in regular stripes ("weird
  lines"). Dropped the gravel; the on-island paths now read like a natural village trail.

## [0.68.1] - 2026-06-25

### Changed (dev)
- **The `debug_streets` spike now lays path markers instead of baked cobblestone**, driving the new
  `PathSurfacer` end to end in-world: the street pieces bake no floor — each lays a `purple_wool` marker above its
  deck and clears its connector tile — so the surfacing pass turns the run into a terrain-aware dirt path (with a
  little gravel) on the island and a self-railing wooden-slab bridge out over the void. The seed declares
  `reach: 96` so the pass covers the full sprawl, and `PathSurfacer` now skips unloaded chunks so a wide reach
  never force-loads terrain. Throw it to see the paths + bridges.

## [0.68.0] - 2026-06-25

### Added
- **Terrain-aware path / over-void bridge surfacing — SKYJIGSAWPLAN Phase 1 foundation.** A new `PathSurfacer`
  pass turns the markers a connective jigsaw piece leaves behind into a context-matched surface: a `dirt_path`
  (with a little gravel) where the deck sits on ground, and a self-railing wooden-slab **bridge** — edge beams +
  fence railings on every open-drop side, capped dead-ends, scaling with path width — where it runs out over the
  void. Two-phase (snapshot the markers, resolve decks + edges, clear the markers last) and decides
  void-vs-ground from the block *under* the deck, so it's robust to a connector clearing the deck tile.
  Unit-tested.

### Changed
- Jigsaw structures gained a `reach` config knob — the half-extent the post-assembly passes scan around the
  origin. The connection-link pass now scans `reach` (so railings link across a *sprawling* structure, not just
  a fixed 16-block box), and the path-surfacing pass runs only when `reach > 0`. Default `0` preserves every
  existing structure's behaviour. Wired into `GenerationJob`, dormant until the street-based structures start
  laying markers (next).

## [0.67.1] - 2026-06-25

### Added (dev)
- **Jigsaw diversity spike — SKYJIGSAWPLAN Phase 0.** A throwaway creative-only `debug_streets` seed that grows
  a self-connecting cobblestone **street network**: a plaza start piece + a `straight` / `corner` / `cross` /
  `end` pool with a weighted empty terminator, recursing at `depth: 6`. It exists to prove the jigsaw really
  sprawls — branching, twisting, and (on a real island) running straight out **over the void** from rigid
  pieces that need no ground support. Throw it to eyeball the effective sprawl reach (the
  `maxDistanceFromCenter` cap). Plain decks for now; the terrain-aware path / self-railing-bridge surfacing
  (SKYJIGSAWPLAN §3a) is Phase 1. To be deleted when the real village system lands.

## [0.67.0] - 2026-06-25

### Added
- **Bastion remnant rare structure.** A ruined bastion — crumbling cracked polished-blackstone-brick ramparts on a
  polished-blackstone floor, weeping crying-obsidian corner buttresses and a wept central shard, gilded/gold accents,
  a caged magma-cube spawner and a bastion loot chest, garrisoned by two piglins and a brute — now has a **5% chance**
  of germinating on the three bastion-*biome* Large Nether seeds: Large Nether Rocky (Nether wastes), Large Nether
  Forest (crimson/warped) and Large Nether Soul (soul sand valley). It deliberately does **not** roll on the Basalt or
  Lava larges, matching vanilla's rule that bastions shun the basalt deltas. It lives in its own
  `skyseed:bastion/remnant` pool, separate from the dedicated Bastion seed's treasure/bridge/housing variants, and a
  creative-only `debug_bastion_remnant` seed germinates it on demand.

## [0.66.5] - 2026-06-24

### Changed
- The Nether chapter is **visible again** (its seeds shown as locked "?"), not hidden entirely — only the early
  *unlocking* was the bug. The `changed_dimension` reveal gate from 0.66.4 stays, so Nether seeds still don't unlock
  until you've entered the Nether; the chapter just isn't `secret` anymore.

## [0.66.4] - 2026-06-24

### Fixed
- **The Nether chapter is now actually hidden until you enter the Nether.** Patchouli categories don't honour an
  `advancement` lock (the one on the chapter was a no‑op), so the Nether seeds were showing early. The chapter is now a
  `secret` category, and each Nether seed's reveal additionally requires having entered the Nether (a
  `changed_dimension` to `the_nether`) — so the whole chapter, and its seeds, stay out of the book until you step
  through a portal.

## [0.66.3] - 2026-06-24

### Added
- **A "Rare Catch" guide** in Getting Started — always readable, it spells out how to unlock the islands that hide
  behind a rare find: the Woodland Mansion's totem (from the 1‑in‑20 Evoker Cell), the Ocean Monument's sponge, the
  Aquarium's scute/prismarine, the deep‑thrown Trial Chamber, and the badlands/jungle biome seeds. A green
  "[x] … found it!" line appears under each once you've gathered its makings — as close to a live checklist as
  Patchouli allows.

## [0.66.2] - 2026-06-24

### Changed
- **Farm, animal and structure seeds now reveal on finding their signature item**, not on crafting a prerequisite —
  Poultry on an egg or feather, Woodland Mansion on a totem, Ocean Monument on a sponge, Aquarium on a scute or
  prismarine shard, Dungeon on rotten flesh, and so on (14 seeds). The biome islands, Larges and the village chain keep
  the craft‑the‑prior‑tier unfold, so the main path stays guided while the rare islands become finds. The intro now
  tells you the rarer seeds unlock when you find the item they call for.

## [0.66.1] - 2026-06-24

### Changed
- **A blocked seed now fizzles back to you instead of stacking the island high.** Placement still slides horizontally
  to find room and tries a small, contained up/down nudge, but the old extreme vertical lifts (up to +32) are gone —
  if the island can't fit within those margins, the seed fizzles and returns to the thrower.

## [0.66.0] - 2026-06-24

### Changed
- **The Almanac now unfolds as a tech tree.** Each seed's entry is hidden until you reach its tier: crafting its
  prerequisite reveals it (Forest → Rocky → Ancient, Desert → Badlands, …), with an override so holding all its
  ingredients at least once reveals it anyway — for a lucky rare‑roll find. **Forest** is the always‑visible root, and
  the **Nether** chapter stays hidden until you set foot in the Nether.
- **Large seed recipes now take the small seed in the centre.** Each Large is built from its small (Large Forest = a
  Forest seed + logs + dirt, etc.; the Nether larges take the small Nether seed rather than the overworld large) — so a
  Large is both gated behind and crafted from its small.

## [0.65.6] - 2026-06-24

### Changed
- **Snow now caps the highest block of each column** rather than the ground. In a snowy Forest the snow settles on the
  spruce canopies and on open ground, leaving bare ground *under* the trees — the look you'd expect, instead of snow
  pooled around the trunks.

## [0.65.5] - 2026-06-24

### Fixed
- **A Forest seed on snowy plains (and grove/snowy biomes) came up bare.** Ground cover (a 90% snow layer) was placed
  before the deferred tree features, and a snow layer fails the vanilla tree's valid‑position check — so the spruces
  never formed. Generation now follows the natural order: **terrain → trees → ground cover** (placed onto the bare
  surface, skipping wherever a tree stands). A Forest island is also guaranteed at least one tree — a last‑resort
  fallback plants one if every site somehow fails.

## [0.65.4] - 2026-06-24

### Changed
- Reverted the deep‑ocean sponge drop from 0.65.3. It duplicated what the **Large Aquatic** island already does (3%
  wet sponge on its ocean pool bed, plus a 5% Ocean Monument roll) and wasn't fixing a circular dependency — there
  wasn't one. Sponge stays a Large Aquatic reward; the Ocean Monument hint and the Large Aquatic field notes now
  point there.

## [0.65.3] - 2026-06-24

### Added
- **Sponge now comes from a deep‑ocean Aquatic island.** Thrown over a `#minecraft:is_deep_ocean` biome, an Aquatic
  seed's pool comes up dark and sandy with **wet sponge on the bed** (smelt it dry) — closing the bootstrap gap where
  the Ocean Monument seed needed sponge that, until now, only a monument could give. The Ocean Monument hint and the
  Aquatic field notes point there.

## [0.65.2] - 2026-06-24

### Fixed
- The Woodland Mansion hint now points at the right Totem of Undying source — the rare **Evoker Cell** that grows on a
  Forest island thrown into a **dark forest** — instead of a village raid.

## [0.65.1] - 2026-06-24

### Changed
- **Seed hints now point you toward the rarer materials.** Each seed's hint page gained a "Where to look" line for
  its non‑obvious ingredients — which island bears them, and any biome or height that matters: gold off a Badlands
  isle, diamonds from a deep throw, the Nether materials from the foothold the matching overworld seed leaves below.
  Obvious materials (wood, dirt, cobble) are left unsaid. 30 entries enriched.

## [0.65.0] - 2026-06-24

### Changed
- **The Skyfarer's Almanac is rebuilt around progressive disclosure.** The single ~47‑page "Recipes" list and the
  grid of locked "?" entries are gone. The book now has **five chapters** — Getting Started, Overworld Islands,
  Villages & Farms, Ruins & Landmarks, and The Nether (hidden until you first set foot in the Nether). Every seed has
  one always‑visible page that reveals in stages:
  - a **hint** naming the materials to gather (always shown);
  - the **crafting recipe**, once you're holding all the ingredients (a per‑seed `gathered_<seed>` advancement);
  - the full **field notes** on what it grows, unlocked when you craft it.
  A new **Where to Begin** progression guide rounds out Getting Started.

## [0.64.1] - 2026-06-24

### Added
- **Piglin Trading Post easter egg.** Thrown in the overworld, the Nether‑native trading post no longer fizzles — it
  grows a grass island carrying the **abandoned cottage** (the Hamlet's 10% rare structure), zombie villager and all.
  Built on a small reusable mechanism: a `rare_structures` entry can now name a `dimension` so it rolls only there (an
  overworld easter egg on a Nether seed), paired with an overworld `biome_override` so the seed takes root topside.

## [0.64.0] - 2026-06-24

### Added
- **Wither Arena Island** — the Nether chapter's capstone. A new Nether‑native seed (obsidian + nether bricks + soul
  sand) that grows a blackstone island carrying an enclosed, blast‑resistant **obsidian arena**: the Wither's skull
  explosions can't break it or blow you into the void. A soul‑sand summoning patch, glowstone lighting, nether‑brick
  trim, a narrow entrance the boss can't fit through, and a sheltered corner holding a **charged respawn anchor** (set
  spawn so a death drops you back here, not home) and a `bastion_treasure` reward chest behind an arrow slit. Bring
  your own boss — the survivable venue to win the Nether Star → Beacon. **The Nether chapter is complete.**

## [0.63.0] - 2026-06-24

### Added
- **Piglin Trading Post Island** — the Nether's "village". A new Nether‑native seed (gold ingot + blackstone +
  netherrack) that grows a blackstone island carrying a trading‑post hall: an open colonnade with rafter beams and
  hanging lanterns around a central gold trade pillar, gilded‑blackstone accents and two loot chests. Three to five
  neutral piglins keep it — wear gold and drop a gold ingot to barter (vanilla). The gold‑economy hub. Unlike the
  bastion it has no fizzle rule, so it grows anywhere in the Nether.

## [0.62.0] - 2026-06-24

### Added
- **The Bastion Remnant Island now grows all three variants.** Alongside the Treasure room, a throw can come up as a
  **Bridge** (a raised blackstone bridge over a lava channel, with a stable end and loot) or a **Housing** block (a
  walled, partitioned unit around a central loot chamber) — one weighted‑random per island. Hoglins now roam the
  bastion too.
- **A hard biome‑exclusion fizzle (`fizzle` theme rule).** A theme can list `biomes` it refuses to grow in even in a
  dimension it implements, with an optional message. The Bastion uses it to **fizzle in the basalt deltas** (the
  vanilla rule), showing the thrower its own action‑bar message.

## [0.61.1] - 2026-06-24

### Changed
- **Ladder Island shafts now face a random direction.** The shaft and its cobblestone backing pick one of the four
  directions per island, instead of every ladder facing the same way.

### Added
- **Debug seeds for the ladder waterfall** (creative-only, in the "Skyseed Debug" tab): *Debug: Small Waterfall* and
  *Debug: Large Waterfall* force the 5% waterfall variant of the small / large Ladder Island, for testing.

## [0.61.0] - 2026-06-24

### Added
- **Bastion Remnant Island — a new Nether seed.** A blackstone island carrying a bastion ruin: gold and gilded
  blackstone around a **lodestone treasure plinth**, a `bastion_treasure` and a `bastion_other` chest, and a caged
  **magma-cube spawner** — with piglins, a brute and magma cubes drifting across it. Nether-native (fizzles
  elsewhere). Crafted from blackstone, gold and crying obsidian. New item, recipe, advancement, guide entry and a
  dark-gold icon. Guarded by the `bastionIsNetherNativeWithBastionJigsaw` gametest. *(This is the first,
  treasure-flavoured variant; the Bridge / Housing variants and the "fizzles over Basalt Deltas" rule are next.)*

## [0.60.0] - 2026-06-24

### Changed
- **Version strings now carry the Minecraft version** — `<mc>_<mod>`, e.g. the jar is `skyseed-1.21.1_0.60.0.jar`
  and the in-game mod version matches. Once Skyseed targets more than one Minecraft version the builds are
  distinguishable at a glance, and it tracks the per-version Stonecutter node automatically.
- **The Ladder Island waterfall drains through a capped centre.** The landing's centre is left open so the water
  sinks in instead of flooding the landing — but with a cobblestone block one below it, so the water stays contained
  *and* a player riding the waterfall down lands on the cap rather than dropping through into the void.
- **The Desert Temple no longer blows the island's bottom out.** The TNT trap is cut from ten charges (a buried 3×3
  cache plus the trigger) down to the single trigger, over a hidden obsidian floor — so the blast still stings (lost
  loot, a crater) but leaves the island's underside intact for your dropped items to land on.

## [0.59.0] - 2026-06-24

### Added
- **Large Ladder Island.** A bigger sibling of the Ladder Island, punching its shaft a full **30 blocks** down (up
  from 20) to the cobblestone landing — for when mining level is a long way below. A slightly larger island, same
  biome/dimension-aware body and 5% waterfall easter egg. Crafted **3×3** (a row of dirt over a ladder ringed by
  cobblestone). Guarded by the `largeLadderIslandPunchesDeeper` gametest.

### Changed
- **The standalone blaze spawner room is roomier.** Widened from 7×7 to **9×9** and the pitched roof removed — it was
  cramped to fight a blaze in — leaving an open-top arena.
- **The Ladder Island guide entry and recipe note no longer mention the waterfall**, so the 5% surprise stays a
  surprise.

## [0.58.0] - 2026-06-24

### Added
- **Ladder Island — a home-grown way down to mining level.** A small, biome- and dimension-aware island whose centre
  is punched through with a **ladder shaft**: ladders backed by a cobblestone wall, hanging ~20 blocks below the
  island to a **5×5 cobblestone landing** — so you can get down to mining level without bridging out over the void.
  It takes the look of wherever it lands (grass / sand / snow) and grows in every dimension (a netherrack nub in the
  Nether, end stone in the End — the shaft comes too). **5% easter egg:** the ladders come up as a **waterfall**
  instead — a single water source on the surface that water physics carries straight down the open shaft. Crafted
  from dirt, cobblestone and a ladder. New theme field `ladder_shaft` driving a new `ShaftPlanner`; new item, recipe,
  advancement, guide entry and icon. Guarded by the `ladderIslandPunchesAShaftToALanding` gametest.

## [0.57.1] - 2026-06-24

### Fixed
- **A fizzling seed is returned to the thrower instead of dropped into the void.** When a seed can't germinate where
  it's thrown (an overworld seed in the Nether, say), it used to drop itself at the failure point — which, since
  you're almost always throwing over empty sky, meant watching the seed you just crafted tumble out of reach. It now
  goes back into the thrower's inventory (or at their feet if it's full), with the fizzle smoke and hiss still playing
  at the failure point. A seed thrown by a dispenser (no player) still drops where it fizzled.

## [0.57.0] - 2026-06-23

### Added
- **A surprise blaze spawner room on the Large Nether seeds.** Each of the 5 Large Nether seeds now has a **5% chance**
  to grow with a **caged blaze spawner room** tucked on it — a standalone non-boxy nether-brick room (pitched roof,
  fence-grate windows, a doorway, soul-sand/wart braziers, a bridge-loot chest) with a permanent **blaze spawner**: a
  renewable blaze-rod source if you get lucky. Plus a creative-only **Debug: Blaze Spawner** seed (in the "Skyseed
  Debug" tab) that germinates the room on demand. Guarded by the `blazeRoomRollsOnLargeNetherSeedsAndDebugSeed`
  gametest.

## [0.56.0] - 2026-06-23

### Added
- **Large Nether seeds — a Large variant of each of the 5 Tier-2 Nether-native seeds.** Same biome content as their
  Tier-2 counterparts, much bigger (radius 11-17, mirroring the overworld Large jump) with ore counts scaled to
  match: **Large Nether Rocky, Lava, Forest, Soul and Basalt.** Recipe: **3×3 with the matching Large overworld seed
  in the centre** + 6 bulk nether blocks + 2 signature items (e.g. a *Large Rocky Skyseed* ringed by netherrack and
  nether quartz). Overworld easter eggs scale up to match — except **Large Nether Lava**, which (like Nether Lava)
  grows a full-size **Large lava island** topside, the overworld's only one. New items, recipes, advancements, guide
  entries and nether-tinted icons. Guarded by the `largeNetherSeedsAreFullSizeNetherNative` gametest. *(The 5% Blaze
  Spawner Room roll planned for these is deferred — to be added next.)*

## [0.55.0] - 2026-06-23

### Added
- **Nether Fortress Island — a new seed.** A piece of nether fortress set adrift, and built with a *form*, not a box:
  an **arched nether-brick walkway** over a glowing **magma channel**, running straight out of a square **keep** with
  a pitched roof, fence-grate windows and a caged **blaze spawner** — a renewable blaze-rod source with no fortress to
  hunt for — ending in a **nether-wart garden** and a bridge-loot chest. Wither skeletons patrol it; blazes and magma
  cubes drift past. Nether-native (fizzles in the overworld). Crafted from nether bricks, nether brick fence and a
  nether wart block. New item, recipe, craft advancement, guide entry and a nether-brick-tinted icon. Guarded by the
  `netherFortressIsNetherNativeWithFortressJigsaw` gametest.

## [0.54.1] - 2026-06-23

### Added
- **Ruined Portals that surface on big islands now get a twin too.** The cross-dimension portal pairing (v0.54.0)
  previously only fired for the dedicated Ruined Portal seed; now the rare 1%-chance ruined portal that can roll on
  *any* large island also grows its linked twin in the other dimension. (Internally the `twin` flag now names the
  theme to pair, and lives on both the theme and the rare-structure config, routed through the island plan — so any
  rolled structure can opt into a twin.)

## [0.54.0] - 2026-06-23

### Added
- **Ruined Portals now come in pairs, linked across dimensions.** Thrown in the Nether, a Ruined Portal Skyseed now
  grows a small netherrack island carrying an unfinished frame (instead of fizzling). And whenever a Ruined Portal is
  thrown on *either* side, a matching one is grown at the vanilla **8:1 linked coordinate** in the other dimension
  (overworld ÷ 8, nether × 8), placed as close to the link as possible — so repairing and lighting **both** frames
  connects them into a real working portal pair, with no special linking code (vanilla's own portal search does it).
  The split is deliberate: the **overworld** portal always carries the goodies (the ruined-portal loot chest, gold
  blocks); the **nether** portal is just the bare, repairable frame. A twin never spawns a twin of its own.
- Internally, a general convention: a structure can ship a Nether variant by providing a `<pool>_nether` template
  pool, which is preferred automatically in the Nether (the Ruined Portal uses it for its no-goodies frame).

## [0.53.0] - 2026-06-23

### Added
- **Tier-2 Basalt — a full-size Basalt Deltas (tiny badlands topside), completing the Tier-2 Nether seeds.** The
  fifth and final Tier-2 Nether-native seed, and the most dangerous island in the mod: a full-size jumble of
  **basalt** over blackstone, bristling with jagged **basalt columns**, scarred with magma-block patches and shallow
  lava wells, with **Magma Cubes** between the spires and the odd **Ghast**. Its core is salted with **gilded
  blackstone** (gold when mined). Crafted (2×2) from a **Badlands Skyseed** + basalt + a lump of gilded blackstone.
  Thrown topside it makes a tiny badlands butte (red sand, terracotta, a glint of gold). With this, **all five
  sketched Tier-2 Nether seeds are done** (Rocky, Lava, Crimson/Warped, Soul, Basalt). Guarded by the
  `netherBasaltIsFullSizeWithTinyBadlandsOverworld` gametest.

## [0.52.1] - 2026-06-23

### Changed
- **Tier-2 Nether seed recipes are now compact 2×2 crafts built on their overworld Skyseed.** Instead of a full 3×3
  of Nether blocks, each Tier-2 Nether seed is now its matching overworld seed infused with a couple of Nether
  blocks and a signature ingredient — cheaper, and a clear "upgrade the overworld seed into its Nether form"
  progression (the parent seed is consumed):
  - **Nether Soul** = 2 soul sand + a **Desert Skyseed** + a bone block.
  - **Nether Rocky** = 2 netherrack + a **Rocky Skyseed** + nether quartz.
  - **Nether Lava** = 2 basalt + an **Aquatic Skyseed** + a bucket of lava (returned empty).
  - **Nether Forest** = crimson + warped stems + a **Forest Skyseed** + a shroomlight.

## [0.52.0] - 2026-06-23

### Added
- **Tier-2 Soul — a full-size Soul Sand Valley (tiny desert topside).** The fourth Tier-2 Nether-native seed: a
  full-size island of **soul sand** and soul soil, riddled with **bone fossils** and lit by ghostly **soul fire**,
  haunted by a pack of **Skeletons** (with Endermen and the odd Ghast). Crafted from a ring of soul sand around a
  single **bone block** — the fossil at its heart (soul sand from the Nether, a bone block from an overworld fossil
  or crafted from bones). Thrown in the overworld it makes a tiny desert island instead. New item, recipe, craft
  advancement, guide entry and a soul-sand-tinted icon. Guarded by the `netherSoulIsFullSizeWithTinyDesertOverworld`
  gametest.

## [0.51.1] - 2026-06-23

### Changed
- **The default seed icon is now an unmistakable placeholder.** The Forest Skyseed got its own dedicated icon (the
  green orb that used to be the generic fallback), and the generic `island_seed` texture is now a plain **white ball
  with a question mark**. So any future seed that ships without its own icon shows a clear "?" placeholder instead of
  masquerading as a Forest seed. (The creative-only debug seeds use this placeholder by design.)

## [0.51.0] - 2026-06-23

### Added
- **Tier-2 Crimson/Warped — a full-size fungal forest (and a tiny grass island topside).** The third Tier-2
  Nether-native seed: a full-size island thick with **huge fungi**. **Crimson** by default (crimson nylium, towering
  crimson fungi, weeping vines, shroomlight, a pack of Hoglins and Piglins); thrown in a **Warped Forest** biome the
  whole isle turns teal — warped nylium, huge warped fungi, nether sprouts, Endermen. Crafted from crimson + warped
  stems, shroomlight and a nether wart block. Thrown in the overworld it shrugs into a tiny grass island (a seed's a
  seed). New item, recipe, craft advancement, guide entry and a crimson-tinted icon. Guarded by the
  `netherForestIsCrimsonWarpedWithTinyOverworld` gametest.

## [0.50.0] - 2026-06-23

### Added
- **Tier-2 Lava — a full-size lava lagoon, and the overworld's first lava island.** The molten counterpart to the
  Aquatic isle: a full-size **basalt** island built around a contained **lava lake**, with magma-block shores, a
  wandering pack of **Striders** and a Magma Cube or two. Crafted from blackstone, magma blocks and a bucket of lava
  (you keep the bucket). Because the overworld has no lava island of its own, this is the one Tier-2 Nether seed that
  *also* grows full-size when thrown topside — a stone-bodied volcanic isle with the same lava lake (Striders and
  all), rather than the tiny easter egg Nether Rocky makes. New item, recipe, craft advancement, guide entry and a
  lava-tinted icon. Guarded by the `netherLavaIsFullSizeInBothDimensions` gametest.

## [0.49.0] - 2026-06-23

### Added
- **Easter egg: throw a Nether Rocky seed in the overworld and it makes a tiny rocky island.** The mirror of the
  Tier-1 adaptations — instead of fizzling, a Nether-native seed thrown topside grows a deliberately tiny (~7×7×4)
  plain **stone** island with sparse iron and gold. Throw it low enough (Y ≤ 8) and it comes out **deepslate**
  instead. A "yeah, I figured that'd happen" sort of thing; the real, full-size Nether Rocky island is still only
  down in the Nether.

## [0.48.0] - 2026-06-23

### Added
- **The first Tier-2 Nether-native Skyseed: Nether Rocky.** A **full-size** Nether mining island — the size of a
  normal overworld seed's island, *not* the tiny foothold an overworld seed makes in the Nether. A netherrack body
  over a **blackstone** core, packed with **nether quartz** and **nether gold**, with **gilded blackstone** and
  **ancient debris** for the patient; throw it low near the lava sea and the deepest isles run richest. Crafted from
  netherrack, blackstone and quartz — all Nether-mined — it's the payoff for committing to the Nether and the first
  proof of the Tier-2 design. New item, recipe, craft advancement, guide entry and a netherrack-tinted icon. Fizzles
  in the overworld. Guarded by the `netherRockyIsNetherNativeAndFullSize` gametest.

## [0.47.0] - 2026-06-23

### Added
- **Large terrain seeds adapt in the Nether too — same form, ~1.5× bigger.** All eight Large terrain Skyseeds
  (Rocky, Desert, Badlands, Aquatic, Ancient, Mushroom, Forest, Lush) now grow their normal seed's Nether island
  when thrown in the Nether — just about 1.5× the size of a normal seed's Nether island (shape radius 3–4 vs 2–3),
  with the same blocks, ores, mobs and decoration. Still a fraction of their lavish Overworld selves: yes, you *can*
  toss a Large overworld seed down here, but it remains emphatically *not* the efficient way to build in the Nether.
  Guarded by the `largeSeedsAdaptInTheNether` gametest.

## [0.46.0] - 2026-06-23

### Added
- **Lush adapts in the Nether — a vine grotto.** Throw a Lush Skyseed in the Nether and it grows the prettiest little
  island down there: **warped nylium** lit by **shroomlights**, **weeping vines** hanging beneath, warped roots and
  nether sprouts underfoot, and an Enderman or two — calm and dry, no pond. This **completes the Tier-1 Nether
  adaptations**: all eight viable terrain seeds (Rocky, Desert, Badlands, Aquatic, Ancient, Mushroom, Forest, Lush)
  now have a Nether form. (Meadow and Frozen fizzle by design.)

## [0.45.0] - 2026-06-23

### Changed
- **A Nether/End override never inherits Overworld content.** A dimension-keyed `biome_override` is now a complete
  spec for that dimension: any field it doesn't set defaults to neutral/empty (a netherrack/end-stone body, no ores,
  no decoration, no mobs, no pond, a small shape) instead of falling back to the theme's Overworld base. So overworld
  ore, plants, mobs or terracotta strata can't leak across the portal when an override omits a field. (Overworld
  generation is unchanged — the base config only applies where it's valid for the dimension.)

## [0.44.0] - 2026-06-23

### Added
- **Forest adapts in the Nether — a fungal forest.** Throw a Forest Skyseed in the Nether and it grows a little fungal
  patch instead of a grove: **crimson nylium** with crimson roots and fungi, **Hoglins** (the Nether's food animal)
  and Piglins by default, or **warped nylium** with warped roots, nether sprouts and twisting vines and **Endermen**
  when thrown into a warped forest. Shroomlights glow on the floor and weeping vines hang underneath. (The dense
  huge-fungi forest is reserved for the future Tier-2 Crimson/Warped seeds.)

## [0.43.0] - 2026-06-23

### Added
- **Mushroom adapts in the Nether — a mushroom island.** Throw a Mushroom Skyseed in the Nether and it grows a calm
  little mycelium island over netherrack, dotted with mushroom-cap patches and small mushrooms and grazed by
  **mooshrooms** — the Nether's scarce source of food and leather. A gentle change of pace among the harsh islands.
  (The overworld mushroom field's "nothing hostile spawns" is a biome property, so it doesn't carry into the Nether —
  the value here is the mooshrooms, not safety.)

## [0.42.0] - 2026-06-23

### Added
- **Ancient adapts in the Nether — a haunted deep.** Throw an Ancient Skyseed in the Nether and it grows a dark
  blackstone island shot through with **soul-sand veins**, buried **bone fossils** and the odd **Ancient Debris**,
  flecked with soul soil and lit by soul lanterns, with skeletons and the odd Enderman. The eerie debris-mining island.

### Changed
- A theme's `lava` veins/lakes are now home-dimension only (like rare structures), so an overworld lava field tuned
  for a big island doesn't swamp a tiny adapted Nether one — the Nether adaptation supplies its own lava (e.g.
  Aquatic's lava lagoon).

## [0.41.0] - 2026-06-23

### Added
- **Aquatic adapts in the Nether — a Lava Lagoon island.** Throw an Aquatic Skyseed in the Nether and its pond comes
  up as a contained pool of **lava** instead of water (the clever bit — water can't survive down here): a tiny basalt
  island with magma-block shores, a **Strider** riding the lava, and a magma cube or two, with a little Nether Gold.

### Changed
- Rare structures (igloo, ocean ruin, vault cell, …) are now gated to a theme's home dimension, so an adapted Nether
  island never rolls an out-of-place overworld ruin.

## [0.40.0] - 2026-06-23

### Added
- **Badlands adapts in the Nether — a Basalt Deltas island.** Throw a Badlands Skyseed in the Nether and it grows a
  tiny Basalt Deltas fragment instead of the mesa: blackstone over a basalt body, scattered with glowing **magma
  blocks** (the deltas' stand-here-and-burn hazard), a little **gilded blackstone** and Nether Gold, and magma cubes
  bouncing around. The overworld terracotta strata are dropped.

### Changed
- A `biome_override` can now replace or clear a theme's `fill_bands` (banded body), so an adaptation can drop strata
  that shouldn't carry across dimensions — Badlands' terracotta bands don't follow it into the Nether.

## [0.39.0] - 2026-06-23

### Changed
- **Nether islands grown from overworld seeds are now deliberately tiny (~7×7×4).** A reused overworld seed in the
  Nether gives you a *foothold* — some netherrack / soul sand and a little ore to bootstrap — but it's small on
  purpose. The substantial, richer Nether islands are meant to come from the (upcoming) **Nether-specific seeds**, so
  reusing an overworld seed is a convenience, not a shortcut. Rocky and Desert (the two adapted so far) are shrunk
  accordingly, with their ore scaled down to match the smaller volume. A deliberate shift of weight onto the Nether
  seeds — it should make crafting them the main event. (See `SKYNETHERPLAN.md`.)

## [0.38.1] - 2026-06-23

### Fixed
- **Structure fences, panes and walls now connect.** Jigsaw (structure) placement copies blockstates verbatim, so a
  fence or glass pane lands as an unconnected post. After pasting *any* structure, Skyseed now re-derives the
  connection state of every fence / pane / wall in its footprint with the game's own shape update — so the Evoker
  Cell's windows, and any structure's railings and bars, link up to their neighbours (and the surrounding terrain).
- **Evoker Cell front:** the dark-oak door is now framed by wood, with the red windows moved out to the edges next
  to the cobblestone pillars instead of crowding the door.

## [0.38.0] - 2026-06-23

### Added
- **Desert adapts in the Nether — a Soul Sand Valley island.** Throw a Desert Skyseed in the Nether and it grows a
  Soul Sand Valley instead of the sand island: soul sand over soul soil and a basalt deep, scattered with the eternal
  blue of **soul fire**, with **bone-block fossils** buried in the basalt and a little Nether Quartz and Gold.
  Skeletons (and the odd Enderman) wander it. Soul sand is the gate to the **Wither** and **Soul Speed**.

## [0.37.1] - 2026-06-23

### Changed
- **The Igloo looks like an igloo.** It was a sealed 5×5×5 snow cube; it's now a rounded snow dome (octagonal
  footprint, two wall courses, an inset shoulder and a cap) with a doorway you can actually walk through, furnished
  with a hearth, a workbench, the cleric's brewing stand + cauldron, an igloo-loot chest, a redstone-torch glow and
  carpet. (The shoulder arches over the doorway, so the interior still can't see the sky and the zombie villager
  inside doesn't burn.)
- **The Evoker Cell looks like a mini woodland mansion.** It was a sealed dark-oak box with no way in; it's now a
  mansion fragment — cobblestone corner pillars and a foundation course, dark-oak walls with white-framed glass
  windows (and the illagers' red windows flanking a **dark-oak front door**), and a pitched dark-oak stair roof.

## [0.37.0] - 2026-06-23

### Added
- **Seeds now fizzle in dimensions they don't implement.** This completes the adapt-or-fizzle model: a Skyseed only
  grows where it has an implementation — its declared base `dimensions` (now spelled out on every seed, all
  `["minecraft:overworld"]`) or a dimension-keyed `biome_override`. Thrown anywhere it doesn't implement — an
  overworld seed into the Nether or End — it puffs out and drops back instead of growing the wrong, foreign island.
  A future Nether-only or End-only seed therefore gets the overworld fizzle for free (it simply won't declare
  overworld). Rocky still adapts in the Nether (it declares overworld *and* carries Nether overrides).

### Changed
- Non-dimensioned `biome_overrides` are scoped to the theme's base dimension(s), so an overworld biome/height tweak
  can't leak onto a seed grown in another dimension.

## [0.36.0] - 2026-06-23

### Added
- **Rocky adapts in the Nether — the first Nether island.** Throw a Rocky Skyseed in the Nether and, instead of the
  overworld stone island, it grows a **Nether mining island**: a netherrack body over a blackstone core, seeded with
  Nether Quartz and Nether Gold, and — the lower you throw it, near the lava sea — **Ancient Debris** (the only route
  to Netherite). Zombified Piglins, a Magma Cube and the odd Piglin wander it. This rides a new general mechanism:
  `biome_overrides` now take an optional **`dimension`** key, so a seed's Nether (or any other-dimension) form is just
  a dimension-gated override — no new item, no parallel system. The other terrain seeds don't adapt yet (they still
  grow their overworld form in the Nether); they follow one at a time.

## [0.35.6] - 2026-06-23

### Fixed
- **The Ruined Portal frame was one row too short to ever be a portal.** It built a 4×4 frame (2×2 inner opening),
  which can't be lit even if fully repaired — a working Nether portal needs a 2×3 inner (4×5 frame). It's now a
  proper 4×5 ruined frame with a decayed top-right corner: it reads like a real ruined portal *and* can be repaired
  into a working one by filling the two missing top blocks. Regenerated `portal.nbt`; applies to the Ruined Portal
  seed and the 1%-on-big-islands version.

## [0.35.5] - 2026-06-23

### Changed
- **The legacy-world warning now presents the in-place conversion as a genuine, equal option.** Now that the reset
  is interruption-hardened, the login message offers `/emptynether` and `/emptyend` as a normal, safe way to fix an
  older world — on equal footing with starting fresh — instead of a scary caveat-laden last resort.

## [0.35.4] - 2026-06-23

### Changed
- **Hardened the `/emptynether` / `/emptyend` reset against interruption.** Three changes so an ill-timed crash or
  power loss can't corrupt or half-convert a save: the `level.dat` rewrite now happens *last* and *atomically* (it's
  written to a temp file and atomically moved into place, so the real file is never seen half-written); the old
  chunks are deleted *before* that flip, so any interruption leaves `level.dat` still pointing at the original
  generator — the dimension just regenerates consistently and the command can be re-run, instead of leaving a
  half-void/half-vanilla mix; and the original `level.dat` is copied to `level.dat_skyseed_backup` first as a
  recovery point. The blast radius stays tiny: only `level.dat` and the target dimension's `region`/`entities`/`poi`
  folders are ever touched — the overworld, player data and everything else are left alone.

## [0.35.3] - 2026-06-23

### Added
- **`/emptynether` and `/emptyend` — convert a legacy world in place.** A rescue path for the older-world warning:
  instead of starting fresh, an operator (or single-player with cheats) can wipe and regenerate just the Nether or
  End with the new void generation. `/emptynether` first explains it will *permanently destroy* that dimension and
  asks you to confirm with `/emptynether force`. Because a dimension's generator is baked into the save and can't be
  swapped live without risking corruption, the reset is applied safely at shutdown: it rewrites the single generator
  `settings` entry in `level.dat` and deletes that dimension's chunk folders, so the empty version regenerates the
  next time the world loads. Anyone standing in the doomed dimension is moved to spawn first, and the legacy-world
  login warning now points at these commands. (The End's entry-triggered dragon caveat from v0.35.1 still applies to
  a converted world.)

## [0.35.2] - 2026-06-23

### Added
- **Older-world warning for the Nether/End.** A world's dimensions are baked in when it's created, so a save
  started before v0.35.x keeps the vanilla Nether and End — there's no way to retrofit the empty Skyseed ones
  without a fresh world. The mod now checks the live Nether/End generators on load and, if they're still the
  vanilla terrain, shows the player a one-line heads-up at login recommending a new world (the overworld is
  unaffected). New worlds are also stamped with the Skyseed version they were created on, and the active mod
  version + the world's creation version are logged on every load. *Why now:* with the overworld, Nether and End
  all emptied as of v0.35.1, this is the last expected world-breaking change — so anyone starting fresh today
  shouldn't need to restart again for the dimension work.

## [0.35.1] - 2026-06-23

### Added
- **Pre-emptied the End** — the same treatment as the Nether, done early on purpose: so that whenever the End
  chapter actually gets built, players won't have to start a new save to see it. `the_end` in the `skyseed:skyblock`
  preset now uses a new `skyseed:void_end` void noise settings (no terrain), while keeping the standard
  `minecraft:the_end` biome source so all the End biomes stay available for later. Applies to newly created worlds.
  Two differences from the Nether worth knowing: you arrive on the game's hardcoded obsidian platform (no lava to
  fall into), and the ender dragon fight is triggered by *entering* the dimension rather than by terrain — so for
  now a dragon still spawns over the void on first entry. Deciding what happens to it is part of the (still
  unwritten) End chapter.

## [0.35.0] - 2026-06-23

### Added
- **Emptied the Nether** — the first step of the Nether chapter (and, as is tradition, the way the overworld
  started). The `skyseed:skyblock` world preset now generates `the_nether` as void (no natural terrain), the same
  trick that voids the overworld, but with a **lava sea** filling everything below Y 32 instead of empty air — a
  hellish floor and light source you'd fall into rather than open sky. The five Nether biomes still generate
  (multi-noise), so you explore the void to find them, and the dimension keeps its own feel (ultrawarm, red fog).
  New `skyseed:void_nether` noise settings; applies to newly created worlds. A portal-arrival platform, the
  Nether Skyseeds, and the structures follow.

## [0.34.9] - 2026-06-23

### Fixed
- **Guide book: missing seed recipes, and the Ocean Monument seed's icon.** The recipe almanac was missing the
  **Outpost, Ocean Monument, Trial Chamber and Woodland Mansion** crafting pages — added, so the book now lists
  every craftable seed. The **Ocean Monument Skyseed** also reused the generic seed texture; it now has its own
  prismarine icon, plus a field-notes entry and its craft advancement, so it's fully documented like every other
  seed.

## [0.34.8] - 2026-06-23

### Added
- **Varied, gentler river/pond banks.** A sheer carved channel can now slope down to the waterline instead of always
  dropping straight to it. Each island rolls a bank style — **steep** (sheer, as before), **sloped** (banks step
  down to a flush shore over 2–3 rings), or a coherent **mix** of the two — so islands differ from one another. The
  flush inner ring this creates is also where sugar cane grows, so gentle-banked rivers get their greenery back.

## [0.34.7] - 2026-06-23

### Fixed
- **Sugarcane on river/pond banks no longer instantly pops.** Bank placement only checked that an adjacent
  *column* held carved water (no Y), so on a steep bank the cane could land several blocks above the water surface —
  where no water sits beside its supporting block — and broke on the first tick. Sugarcane is now placed only where
  its supporting block is dirt/sand-type ground with water horizontally beside it at the same level (so it's always
  next to, and one up from, a water block). Other bank plants are unaffected.

## [0.34.6] - 2026-06-23

### Changed
- **Reworked the placement fix so new islands can sit right next to existing ones — and you can hop onto them.**
  v0.34.5's distance check was too strict: a precise throw a short way off the start island got shoved far *upward*
  instead of placing an island beside you. Reverted to a **block-level overlap** test (islands may sit flush —
  touching is fine, only real interpenetration is rejected), and on a collision the island now nudges
  **horizontally** off whatever it would grow into, out to a decent distance, falling back to up/down lifts only
  when there's genuinely no horizontal room. Engulfment and player-burial are still rejected, but islands are no
  longer held apart, so close placement (especially in precise mode) works.

## [0.34.5] - 2026-06-23

### Fixed
- **Islands could germinate inside an existing island.** The old check tolerated up to 5% of the *new* island's
  blocks overlapping solid, so a large island could swallow a small one (its blocks were under that 5%), and it kept
  no gap at all. Replaced it with a proper **distance check**: every island now records its footprint, and a new
  throw is rejected if it comes within a clearance of any recorded island. The keep-out is an oval — **wider than
  deep** (islands are far wider than tall) — and the required gap is the sum of both islands' radii plus a margin, so
  a big island reserves a bigger berth than a small one.
- **A throw can no longer bury you.** Germination is rejected (nudged up, then fizzled if nothing's clear) when it
  would grow over a player, so you can't end up immersed inside a freshly grown island.

## [0.34.4] - 2026-06-23

### Added
- **Glow lichen** now hangs under **Lush** and **Ancient** islands (added to their underside decoration). It's a
  multiface block, so it's placed with its UP face set, clinging to the island's bottom and glowing. Shears it off.
- **Coral fans + small coral plants** — the warm-ocean reefs on **Aquatic** and **Large Aquatic** now grow the full
  set: the three coral fans that were missing (brain / bubble / horn) and all five small coral plants, alongside the
  coral blocks already there. Every coral variant is now obtainable. Both close their `MISSINGBLOCKSPLAN.md` gaps.

## [0.34.3] - 2026-06-23

### Added
- **Bamboo.** A Forest / Large Forest seed thrown over a **bamboo jungle** now comes up as a **bamboo forest** —
  a dense bamboo grove with scattered jungle trees and podzol. Thrown over a regular **jungle** or **sparse
  jungle** (the other biomes where bamboo grows naturally) it gets a light sprinkle of bamboo among the jungle
  trees. (Bamboo plants in one block tall and grows up over time, like the real thing.) The new
  `bamboo_jungle` override sits before the general `#is_jungle` one so the tag doesn't swallow it. Closes the
  bamboo gap from `MISSINGBLOCKSPLAN.md` — every bamboo block is now craftable.

## [0.34.2] - 2026-06-23

### Added
- **Bonus chest.** The vanilla **"Generate Bonus Chest"** world-creation checkbox now does something on a
  Skyseed world: when it's ticked, a starter chest is placed on the grass right beside the spawn, holding a
  wooden tool set (sword / pickaxe / axe / shovel / hoe), 16 torches, apples + bread, a few oak logs, and some
  coal. Read from the world's `WorldOptions.generateBonusChest()` as the starter island is built; off unless you
  tick the box.

## [0.34.1] - 2026-06-23

### Added
- **Hidden debug seeds** for the rare structures that otherwise only appear by chance — **Igloo, Abandoned
  Cottage, Ocean Ruin, Evoker Cell, Vault Cell, Trail Ruins**. Each germinates that structure as a dedicated
  island (reusing the existing rare-structure jigsaw pools — no new templates), so it can be spawned on demand
  instead of throwing dozens of seeds to roll it. They live in a separate **"Skyseed Debug"** creative tab and
  are **creative-only**: no recipe, kept out of the `#skyseed:skyseeds` tag, and no guide entry. (Vault Cell and
  Trail Ruins germinate buried, exactly like the real rolls — fly or dig down to inspect.)

## [0.34.0] - 2026-06-23

### Added
- **Ocean Monument** — a new structure island, *and* a 5% rare roll on a Large Aquatic island thrown over an
  ocean biome (both share one `ocean_monument/monument` template + jigsaw pool). A 13×13 prismarine basin
  (prismarine / bricks / dark prismarine, lit by sea lanterns) holds a contained, open-top pool with
  dark-prismarine corner towers; an **elder guardian + four guardians** spawn submerged in it (guardians added
  to the aquatic-spawn set so they don't beach), guarding a gold-block cache and a `chests/buried_treasure`
  chest — a **Heart of the Sea** + nautilus shells for a **conduit**. A wet-sponge niche sits in the west wall.
  This closes the biggest overworld gap in `MISSINGBLOCKSPLAN.md`: mining the build yields prismarine, sea
  lanterns and sponges, and the guardians drop **prismarine shards** — which also un-blocks the **Aquarium** seed
  recipe (it required shards, previously unobtainable). New `ocean_monument` seed (gold blocks + sponge +
  diamond). Also fixed: the three newest structure seeds (Outpost, Trial Chamber, Woodland Mansion) were missing
  from the `#skyseed:skyseeds` tag — added.

## [0.33.0] - 2026-06-23

### Added
- **Lava.** A new optional `lava` theme field (orthogonal to the biome-override palette bands, so it doesn't
  duplicate them) adds two things: a lava **vein** — a 4–8 block lava cluster grown into the core like an ore,
  rolled at `vein_chance` — and Y-banded lava **lakes** — a contained lava pool carved like a pond, where the
  first matching throw-height band rolls and a hit suppresses the theme's water pond. Wired up:
  - **Rocky / Large Rocky** — a 5% lava vein, plus a lava lake whose odds rise with depth: 1% at normal height,
    5% low, 20% sub-zero (Y < 0).
  - **Ancient / Large Ancient** — a 5% lava vein, plus a 20% lava lake at any height.
  - **Aquatic / Large Aquatic** thrown sub-zero (Y < 0) — comes up as a stone/deepslate island with a guaranteed
    lava lake instead of its water one.
  - **Ruined Portal** (the dedicated seed and the rare-encounter, same template) — a few lava blocks at the foot
    of the frame, sunk flush into the levelled pad so the surrounding ground walls them in.

  Lava lakes reuse the pond carving + containment, so they sit still and don't sheet off the island. Guarded by
  a new `aquaticSubZeroHasLavaLake` gametest; the generation golden master is unchanged (no test theme uses lava).

## [0.32.9] - 2026-06-22

### Fixed
- **Pillager Outpost**: the watch-platform lantern no longer floats in mid-air — it hangs on a chain dropped
  from the roof ridge. The golem cage's fences already link (the v0.32.8 all-fence ring + `linkFences`); the
  `outpostHasSpawnerAndCage` gametest now also asserts a cage-edge fence is connected, to keep it that way.

### Docs
- `description.md`: fleshed out the **Large**-variant twist for all ten terrain islands (Forest, Mushroom,
  Meadow and Ancient were the four still missing one), and added a line making clear each Large variant is its
  own separate, pricier seed — so the ten terrain types are twenty islands in all.

## [0.32.8] - 2026-06-22

### Fixed
- **Pillager Outpost — golem suffocation**: the iron golem now spawns on the cage floor (it was spawned one
  block up, jamming its ~2.7-tall head into the floor above), and the cage is an all-fence ring instead of having
  corner logs (a full block there sat inside the ~1.4-wide golem's eye-box and suffocated it). Spawn point is
  also chosen by a clearance search now. Guarded by a new `outpostGolemFitsInCage` gametest.
- **Pillager Outpost — unlinked fences**: the watch-platform railing and the cage now connect up. Jigsaw
  placement copies stored blockstates verbatim with no neighbour update, so fences are pre-linked to their
  neighbours when the template is generated, via a new `StructureParts.linkFences`.
- **Woodland Mansion — wing gap**: the side wings now butt flush against the central hall instead of leaving an
  open slot beside each doorway. The box-edge connector plane is walled across the wing's footprint (a jamb), so
  the wing meets solid wall — without moving the connector and re-triggering the jigsaw overlap rejection.
- **Woodland Mansion — staircase**: removed the lantern that hung in the open stairwell; moved the stairs one
  column inward so the corner log pillar no longer blocks the climb's exit onto the upper floor; and stopped
  carving the stairwell's near row so the upper-hall carpet no longer floats over the opening.

## [0.32.7] - 2026-06-22

### Internal
- **Code-review cleanup** (B4, C1–C5, D1, P1): trimmed the dead NeoForge-MDK example comments from
  `build.gradle`; documented the nullable-sentinel returns (`@return … or {@code null}`); replaced
  fully-qualified names with imports (`ThrowableItemProjectile`, `ResourceLocation`); braced and reflowed
  `PondCarver`'s pond-bed/shore/column helpers; named the tunable magic numbers (depth-bulge exponent,
  deep-core fraction, vein face-grow chance, seed tries, pond extent / rim wobble, pad clear-height);
  re-sorted `GenerationJob`'s imports; rewrote the stale `IslandSeedEntity` class Javadoc (no more
  "milestone 4 placeholder"); and documented the un-budgeted single-tick jigsaw trade-off.
- Extracted the duplicated rim-harmonic routine into a shared `RimNoise` helper (used by both `ShapeBuilder`
  and `PondCarver`). Behaviour-preserving: the `islandOutputIsStable` golden master stays byte-identical and
  all 17 gametests pass; compiles warning-free under `-Xlint:all`.

## [0.32.6] - 2026-06-22

### Internal
- **Split the `IslandGenerator` god class** (codereview A1): 1096 lines → a ~290-line orchestrator plus six
  focused, package-private collaborators — `ShapeBuilder` (terrain pass), `OrePlanner`, `PondCarver`,
  `DecorationPlanner` (+ `CustomTrees` for the hand-built mangrove/azalea/ice-spike), `MobPlanner` — and a
  shared `Scatter` record. `planIsland` now reads as the generation pipeline, threading one `RandomSource`
  through the passes in the original order; each pass is independently testable.
- Guarded by a new **golden-master test** (`islandOutputIsStable`) fingerprinting the exact generation output
  (centre-relative block checksum + entity/structure counts) of 5 themes covering all six planner paths. The
  refactor is behaviour-preserving: every fingerprint is byte-identical and all 17 gametests pass. Coverage
  held — orchestrator 99.3%, new classes 96.8–100% (ShapeBuilder/CustomTrees/Scatter 100%).

## [0.32.5] - 2026-06-22

### Internal
- **De-duplicated the structure templates** (codereview A2). A package-level `Built` record replaces 14 nested
  copies, and `StructureParts` now owns the shared `writeIfAbsent`/`jig`/`mobSpawner` helpers (plus the existing
  `anchor`/`lootChest`); the per-file re-implementations are gone. ~160 lines of copy-paste removed across 15
  files. Behaviour-preserving: regenerating all 46 structure `.nbt` produced zero diffs, and the gametests pass.
- Also normalized line endings (`.gitattributes` `* text=auto eol=lf`), stopping the LF/CRLF commit warnings.

## [0.32.4] - 2026-06-22

### Internal
- Raised `IslandGenerator` (the largest upcoming refactor) test coverage from **64% to ~99%** with targeted
  tests + tiny themes that exercise the previously-untested paths: river-style ponds (`riverColumns`),
  hand-built mangroves (`buildMangrove`), biome-override waterfalls (`placeWaterfalls`), and an all-bogus-ids
  theme that drives the warn/fallback branches (`resolveBlock`/`resolveScatter`/`resolveBands`/`resolveEntity`
  and unknown features). The remaining few lines are unreachable defensive branches. Overall ~76% line.

## [0.32.3] - 2026-06-22

### Internal
- Extended the GameTest suite to cover the **world-apply pipeline** that was at 0%: `IslandSeedEntity`
  (throw → arm → germinate, precise-mode targeting, and an NBT save/load round-trip) and `GenerationJob`
  (draining a structure island — block stream, jigsaw cottage, villager-at-bed, iron golem, animal pack).
  Coverage of those two classes went **0% → 71% / 77%**; overall ~73% line. Adds two tiny test-only themes
  (`skyseed:gametest/island`, `skyseed:gametest/structure`).

## [0.32.2] - 2026-06-22

### Internal
- The build now compiles with **`-Xlint:all`** (deliberately *not* `-Werror`, so warnings stay visible
  instead of pressuring suppression). Removed all three `@SuppressWarnings` and **fixed** the warnings they
  hid: `Mob.finalizeSpawn` → `EventHooks.finalizeMobSpawn` (worldgen mob spawns now fire the NeoForge
  FinalizeSpawn event) and `BlockStateBase.blocksMotion()` → a position-aware collision-shape check. The
  tree compiles warning-free.
- Added **test-coverage measurement**: `./gradlew gameTestCoverage` attaches JaCoCo to the gameTest run →
  `build/reports/jacoco/`. The GameTest suite covers ~65% of the mod's own lines (generation core well
  covered; the entity germination loop and `GenerationJob` world-apply are the main gaps).

## [0.32.1] - 2026-06-22

### Internal
- Added a **NeoForge GameTest suite** (`gametest/SkyseedGameTests.java`, run with `./gradlew
  runGameTestServer`) covering island-generation and structure invariants — every theme plans without
  error, generation is deterministic and bottom-up sorted, rocky carries ore, structure themes record a
  jigsaw site, the mansion plans its evoker garrison, and the outpost/trial-chamber pieces keep their key
  blocks. This is the safety net for the refactors tracked in the new `codereview.md`.

## [0.32.0] - 2026-06-22

### Changed
- **Pillager Outpost rebuilt — wider, with a camp.** The cramped 5×5 box became a **7×7 cobblestone-and-dark-oak
  watchtower** on a larger island, fixing both old problems:
  - The iron-golem cage sits in a **semi-open arched base** with room to walk around it to the corner ladder (you
    no longer have to break the cage to get past it).
  - The pillager spawner moved to an **enclosed middle room** (walls, floor and ceiling), so spawned pillagers
    can't fall off the island or get stuck in a tree — you fight them as you climb. Above it is an open watch
    platform under a pitched roof.
  - The base is ringed by a small **camp**: two canvas tents, an archery target, a campfire with log seats, a
    banner on a pole, and hay-bale supplies. The outpost island (and the 5%-on-a-Trade-Post variant) grew to fit.

## [0.31.0] - 2026-06-22

### Changed
- **Woodland Mansion is now a modular jigsaw structure** (the second half of the modular follow-up — both grand
  structures are now modular). The two-storey dark-oak core is the start piece, and it draws up to **three
  single-storey wings** from a pool — a storeroom (barrels), a library (bookshelves) and a checkerboard secret
  room (wool) — attached to the west, east and back walls, each with its own `chests/woodland_mansion` chest, so
  the manor sprawls a little differently every time (up to six chests now). Same guaranteed evoker → totem +
  vindicator garrison in the hall; the island is larger to hold the wings. Vertical floor-stacking via jigsaw
  was spiked and confirmed working, but the internal staircase makes horizontal wings the cleaner split.

## [0.30.0] - 2026-06-22

### Changed
- **Trial Chamber is now a modular jigsaw complex** (the flagged follow-up to v0.28.0). Instead of one fixed
  11×11 arena, the chamber is assembled from a central **hub** (the breeze boss spawner, the ominous vault and
  the ladder entrance) plus up to **four room pieces** drawn from a pool — zombie / skeleton / spider / breeze
  spawner rooms (each with a vault) and a twin-vault treasure room — so the layout varies every time you grow
  one. Still buried via `sink`, still the same self-contained spawner→key→vault loop and Bad-Omen ominous path;
  the island is a touch larger to hold the spread-out complex. (Proves buried multi-piece jigsaw assembly.)

## [0.29.0] - 2026-06-22

### Added
- **Woodland Mansion Skyseed** — the second grand structure from `SKYGRANDSTRUCTURESPLAN.md`, and the last of
  the planned grand pair. A two-storey **dark-oak manor** (13×13, a tall gabled roof) raised on a larger grassy
  island ringed with dark oaks: a red-carpet entrance hall, a staircase up to loot rooms and a small library,
  glass windows, hanging lanterns.
  - **Guaranteed totem** — an **evoker** and a pack of **vindicators** garrison the hall (spawned via the
    theme's `animals` pack). Kill the evoker for a **Totem of Undying**; throw another seed whenever you want
    more, so the mansion is a *reliable* totem destination.
  - **Loot** — three chests on the vanilla `chests/woodland_mansion` table.
  - Crafted from **dark oak + diamonds around a Totem of Undying** — your first totem comes from an Evoker Cell
    (rarely on a dark-forest Forest island), which bootstraps the mansion. New `woodland_mansion` theme + guide
    entry + advancement. *(With this, both planned grand structures are built.)*

## [0.28.0] - 2026-06-22

### Added
- **Trial Chamber Skyseed** — the first grand structure from `SKYGRANDSTRUCTURESPLAN.md`. A copper-and-tuff
  **trial chamber sunk deep into a rocky island**, entered by a single ladder shaft punched up to the surface.
  Drop into a lantern-lit arena with a **breeze trial spawner** dead centre and **four more trial spawners**
  (zombie, skeleton, spider, breeze) around it.
  - **Self-contained loop** — clear a spawner's waves for a **trial key**, spend keys on the **three vaults**
    for loot. Nothing respawns.
  - **Ominous path** — bring an **ominous bottle** (raid one from an Outpost island): the spawners turn
    ominous and feed the centre **ominous vault**, the one holding the **heavy core** for a **mace**.
  - Crafted from **tuff bricks + copper blocks around a diamond**; the island carries a little copper and iron
    ore of its own. New `trial_chamber` theme (a larger, thicker rocky island) + guide entry + advancement.

## [0.27.0] - 2026-06-22

### Added
- **Tier-1 gating chambers** — the first build toward the grand Woodland Mansion & Trial Chamber (see
  `SKYGRANDSTRUCTURESPLAN.md`). Small rare chambers that hand you the progression-gating items directly, so the
  future grand structures can be spectacle rather than gatekeepers:
  - **Evoker Cell** — a sealed dark-oak room (a mansion fragment) with an evoker inside and a woodland-mansion
    chest, **5% on a Forest grown in a `dark_forest` biome**. Break in, kill the evoker, claim a bootstrap
    **Totem of Undying**.
  - **Vault Cell** — a buried tuff/copper room with **two trial spawners and a vault**, **5% on an Ancient
    island**. Dig in, clear the spawners for **trial keys**, open the vault for the reward — a self-contained
    mini trial-chamber. The trial mechanics are native 1.21 block-entities (spawner/vault NBT schema verified
    in-game).

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
