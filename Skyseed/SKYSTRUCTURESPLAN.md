# Skyseed — Structures & Loot Plan

This document covers all vanilla overworld structures, their translation into Skyseed sky islands, and the loot/encounter system. See `README.md` for general architecture, `SKYISLANDSPLAN.md` for terrain islands, and `SKYVILLAGESPLAN.md` for villages.

---

## Design philosophy

Vanilla structures serve two purposes: **exploration rewards** (loot chests with rare items) and **encounter spaces** (hostile mobs in a defined arena). Both translate to skyblock, but the delivery changes — instead of stumbling upon a dungeon while mining, the player *crafts a seed that generates a structure island*. The moment of discovery shifts from exploration to a deliberate (but still uncertain) throw.

**Two tiers of structure island:**
- **Loot islands** — a small structure fragment with a chest, no special mob spawner. The reward is the loot, grabbed quickly before mobs push the player off the island.
- **Dungeon islands** — a larger enclosed structure with a mob spawner or trial spawner, designed to be cleared rather than looted-and-run. The reward is harder to reach and proportionally better.

**Structures that don't work in skyblock** are translated into their most interesting *fragment* — a ruined wall, a single room, a chest on a platform — rather than omitted entirely. The goal is coverage: every vanilla structure has a home somewhere in the system.

---

## Complete vanilla overworld structure list

Sourced from vanilla 1.21.1. Structures are listed with their skyblock translation.

---

### 🧟 Dungeon (Monster Room)

> **Built (v0.13.0).** `skyseed:dungeon` — a small mossy cobblestone island carrying a sealed 5×5×5 cube:
> a vanilla mob spawner (zombie/skeleton/spider via three weighted jigsaw variants) and two chests on the
> vanilla `minecraft:chests/simple_dungeon` table. Sealed/dark so the spawner runs day and night. Built on
> the existing jigsaw system (no new engine code) — spawner + chest are block-entity NBT in the `.nbt`.
> Recipe: cobblestone ring around rotten flesh. **Deviation from "open top" below:** the cube is sealed so
> the spawner stays dark and functional (an open-top spawner won't spawn in daylight); you break in instead.

**Vanilla:** Underground cobblestone room with a mob spawner and 1–2 chests. Spawns zombies, skeletons, or spiders.

**Skyblock translation:** A direct fit. A small cobblestone cube island with a spawner at its center and a chest on each side. The open-air context makes it more dangerous than vanilla (no walls to hide behind, mobs push you off) but the island is deliberately small to keep fights contained.

**Island type:** New — **⚔️ Dungeon Island**  
**Recipe:** Rotten Flesh + Cobblestone (shapeless) — rewards killing early zombie sprinkles on Forest/Rocky islands  
**Loot:** Vanilla dungeon loot table (saddle, iron ingot, bread, wheat, redstone, string, cocoa bean, enchanted book, music disc — Cat and 13 specifically)  
**Spawner:** 1 spawner (zombie/skeleton/spider weighted equally)  
**Structure:** ~5×5 cobblestone cube, open top, spawner in center, 2 chests on opposite walls  
**Notes:** Music disc Cat and 13 are dungeon-exclusive in vanilla — this is the only way to obtain them without another player.

---

### 🏜️ Desert Temple

> **Built (v0.14.0; reworked v0.19.5–v0.19.6).** `skyseed:desert_temple` — now **buried like the original**:
> anchored on its roof so it sits flush with the all-sandstone island surface, chamber hanging below, a hole in
> the roof centre inviting a drop-in. Four `minecraft:chests/desert_pyramid` chests line the walls; a single
> pressure plate sits dead-centre over TNT and a buried 3×3 cache, directly under the hole — drop in carelessly
> and you land on it. Fragile/support-dependent blocks don't survive the jigsaw path, so the plate is baked as
> a wool marker and swapped in by the `Traps` post-pass; the interior is carved with explicit air (verified: a
> mob dropped through the hole detonates the chamber). Archaeology not yet included.

**Vanilla:** Sandstone pyramid with 4 treasure chests below a pressure-plate TNT trap. Archaeology added dig sites in 1.20.

**Skyblock translation:** The buried chamber only — the pyramid above is more architecture than gameplay. A sandstone platform island with a sealed-floor TNT trap chamber underneath, accessible by digging through the floor. Full vanilla loot tables including archaeology.

**Island type:** New — **🏺 Desert Temple Island**  
**Recipe:** Sand + Sandstone + TNT — the TNT requirement hints at what's inside  
**Loot:** Vanilla desert temple loot (diamonds, iron ingot, gold ingot, emerald, bones, rotten flesh, enchanted book, golden apple, horse armor)  
**Archaeology:** Suspicious sand pots with desert-temple-appropriate sherds  
**Structure:** Sandstone platform surface, sealed stone floor with the TNT chamber below. Pressure plate trap intact — the player must recognize and disarm it or trigger it (and lose the loot in the blast, as vanilla intended).  
**Biome awareness:** Only generates on Desert Skyseeds or when thrown over a desert biome — thematically it shouldn't appear on a forest island.

---

### 🌿 Jungle Temple

> **Built (v0.14.0; tripwire restored v0.19.5).** `skyseed:jungle_temple` — a sealed mossy-cobblestone room,
> two `minecraft:chests/jungle_temple` chests and a `minecraft:chests/jungle_temple_dispenser`. Jungle planks +
> mossy cobblestone + tripwire hook. **The tripwire trap is back**: a string across the dispenser's line of
> fire, with a hook mounted on the dispenser, so tripping it fires arrows. The fragile blocks are baked as wool
> markers and swapped in by the `Traps` post-pass after assembly. (The lever puzzle is still omitted.)

**Vanilla:** Mossy cobblestone structure with tripwire traps, a puzzle dispenser, and 2 chests.

**Skyblock translation:** The puzzle room only — one room, two chests, the tripwire trap intact. Solving the lever puzzle (vanilla mechanic) is more rewarding in skyblock where loot scarcity makes every chest matter.

**Island type:** New — **🕹️ Jungle Temple Island**  
**Recipe:** Jungle Planks + Mossy Cobblestone + Tripwire Hook  
**Loot:** Vanilla jungle temple loot (diamonds, emerald, iron ingot, gold ingot, bones, rotten flesh, bamboo, enchanted book) + dispenser with arrows  
**Structure:** Mossy cobblestone 5×7 room, tripwire trap on entrance, lever puzzle on back wall, 2 chests  
**Biome awareness:** Forest Skyseed jungle variant or jungle biome germination preferred.

---

### 🧙 Witch Hut (Swamp Hut)

> **Built (v0.14.0).** `skyseed:witch_hut` — a spruce hut on a muddy island with a water cauldron, a crafting
> table and a potted mushroom; a witch and a cat spawn inside via the theme's `animals` pack (now
> `setPersistenceRequired`, so the witch stays). No chest — the witch's drops are the reward. Oak planks +
> brown mushroom + cauldron. (The cat is a random variant, not specifically black; the witch doesn't respawn.)

**Vanilla:** Small oak platform in swamps with a witch and a black cat.

**Skyblock translation:** The hut itself is small enough to place directly on an island. A mossy-oak platform with the hut on top, a witch spawning inside, a black cat, and a cauldron. No structural changes needed — the vanilla hut is already island-sized.

**Island type:** New — **🧙 Witch Hut Island**  
**Recipe:** Oak Planks + Mushroom (brown) + Cauldron (references the witch's brewing aesthetic)  
**Loot:** No chest — the witch drops potions and brewing ingredients when killed. The cauldron is the "reward" (already filled with a random potion).  
**Spawns:** 1 Witch (permanent spawner behavior — respawns in the hut), 1 black Cat  
**Structure:** Vanilla witch hut exactly, on a small moss/oak platform island  
**Notes:** Witch drops (redstone, glowstone dust, spider eye, glass bottles, sticks, sugar, gunpowder) are all useful mid-game brewing ingredients. The cat is a free tamed-cat opportunity.

---

### 🏔️ Pillager Outpost

**Vanilla:** Dark oak watchtower with pillagers, caged iron golem, sometimes allays, tents/targets around the base.

**Skyblock translation:** The watchtower only — 3–4 floors, pillager spawners at the top, a cage with an iron golem (or allay) at the base. Fighting up a tower on a floating island is an interesting encounter.

**Island type:** New — **🗼 Outpost Island**  
**Recipe:** Dark Oak Planks + Crossbow + Iron Ingot  
**Loot:** Vanilla outpost loot (crossbow, tripwire hook, dark oak log, iron ingot, enchanted book, bottle o' enchanting, ominous bottle)  
**Spawns:** Pillager spawner at the top floor, 2–4 Pillagers on generation  
**Caged rewards:** Iron Golem (break the cage, it helps fight pillagers) OR 1–3 Allays (rare, high value — allays are otherwise unobtainable in skyblock)  
**Structure:** 4-floor dark oak tower, ladder inside, chest on top floor, cage at base  
**Notes:** Ominous Bottles from outpost loot are what trigger raids — but as noted in SKYVILLAGESPLAN.md, raids are disabled on Skyseed village islands, so ominous bottles are effectively a currency/collector item rather than a raid trigger here. Worth flagging for the player in the Patchouli guide.

**Status: ✅ Built** (v0.24.0). `skyseed:outpost` — a dark-oak watchtower (`skyseed:outpost/tower`): a 5×5 tower, three floors up a corner ladder past arrow slits, a `minecraft:pillager` spawner + `chests/pillager_outpost` chest on the top floor, and an iron golem caged behind dark-oak fences at the base (spawned at the jigsaw centre via the theme's `iron_golems: 1`, so it lands inside the cage). Also a **`rare_structures` entry on the Trade Post (5%)** that swaps the village jigsaw for the tower (no villagers). Pillager `doPatrolSpawning` is disabled with raids in `WorldSetupEvents`. Allays-instead-of-golem (the rare high-value cage reward) is deferred; the banner/tents/targets are omitted for now.

---

### 🏚️ Abandoned Village (Zombie Village)

**Vanilla:** A village where all villagers have been replaced by zombie villagers and the buildings are partially ruined and cobwebbed.

**Skyblock translation:** A single ruined building — one zombie villager inside (the cure mechanic payoff), cobwebs, cracked wood, a chest with modest loot. Essentially a Hamlet Island that went wrong.

**Island type:** Not a dedicated seed — an **alternate variant of the Hamlet Island** (see SKYVILLAGESPLAN.md), with a small chance (~10%) of generating as an abandoned variant. The recipe is identical; the player doesn't know they're getting a zombie village until the island pops.

**Loot:** Small chest with some food, string (from cobwebs), rotten flesh  
**Spawns:** 1 Zombie Villager  
**Notes:** The zombie villager is the payoff — curing it gives the player a permanently discounted villager (Java Edition). The cure requires a Splash Weakness Potion and a Golden Apple, both of which require some progression to obtain, making this a mid-game bonus rather than an early shortcut.

**Status: ✅ Built** (v0.19.0). A `rare_structures` entry on the Hamlet theme (10%) swaps the cottage jigsaw for `skyseed:abandoned/cottage` — a cobwebbed, gap-punched oak ruin with a `village_plains_house` chest and **no bed** (so the bed-scan spawns no normal villager). The zombie villager comes from the rare structure's own `mobs` pack. Uses the vanilla cure path; the chest gives string/food.

---

### 🏠 Igloo

**Vanilla:** Snow-surface structure with a carpet floor hiding a ladder to a basement — the basement contains a zombie villager in a cage and a cleric's brewing setup for curing.

**Skyblock translation:** The igloo translates well. A snow-block dome on a Frozen island surface, with the hidden basement intact. The basement contains the zombie villager cage and the curing ingredients.

**Island type:** Not a dedicated seed — a **rare structure feature on Frozen Islands** (5% chance, same as the lava pool on Rocky). The Frozen Skyseed can generate with an igloo on the surface.

**Loot:** Basement chest (golden apple, emerald, coal, wheat, stone axe) — the golden apple is the igloo's signature reward and directly enables zombie villager curing  
**Spawns:** 1 Zombie Villager (in cage), 1 Cleric Villager (in the basement brewing area)  
**Notes:** The igloo is the intended introduction to the zombie villager curing mechanic. Including it as a Frozen island feature rather than its own seed keeps it as a discovery rather than a crafted destination.

**Status: ✅ Built** (v0.19.0). A `rare_structures` entry on the Frozen theme (5%) places `skyseed:igloo/igloo` — a **sealed** snow dome (kept dark so the zombie villager survives daylight) holding a brewing stand, a water cauldron and the vanilla `igloo_chest`. Simplified from vanilla: one surface dome rather than a hidden basement, and the cleric is dropped (a loose zombie villager + cleric would fight) — the player cures the trapped zombie villager with the chest's golden apple, and the cured villager can then claim the brewing stand as a cleric.

---

### 🪨 Ruined Portal

> **Built (v0.14.0).** `skyseed:ruined_portal` — a basalt/blackstone island with a broken obsidian +
> crying-obsidian frame, a gold block, magma and netherrack accents, and a `minecraft:chests/ruined_portal`
> chest. Obsidian + gold ingot. Crying obsidian is the signature reward.

**Vanilla:** A broken Nether portal (obsidian frame, partially missing) with a chest containing gold items and sometimes crying obsidian.

**Skyblock translation:** A small basalt/blackstone platform with the ruined portal frame on it. The chest contains the vanilla ruined portal loot. The broken obsidian frame hints at the Nether and provides crying obsidian — unobtainable otherwise in skyblock without this structure.

**Island type:** New — **🪨 Ruined Portal Island**  
**Recipe:** Obsidian + Gold Ingot (both hint at Nether-adjacent materials)  
**Loot:** Vanilla ruined portal loot (golden items, fire charge, flint and steel, obsidian, crying obsidian)  
**Structure:** Small basalt platform, ruined portal frame (7–10 obsidian blocks placed, rest missing), chest  
**Notes:** Crying Obsidian → Respawn Anchor (Nether respawn mechanic) is otherwise unobtainable. The gold cost of the recipe creates a mild progression gate.

---

### 🏰 Woodland Mansion

**Vanilla:** Enormous dark-oak structure with dozens of rooms, evokers, vindicators, and vexes. Contains totem of undying.

**Skyblock translation:** A full mansion doesn't fit on a sky island in any reasonable sense. Instead: a **single Mansion Room Island** — one room from the mansion's room pool, fully enclosed, with whatever hostile mobs that room type spawns. Multiple different room types means the loot and encounter vary. The totem of undying (evoker-exclusive drop) makes this a high-priority late-game island.

**Island type:** New — **🏰 Mansion Room Island**  
**Recipe:** Dark Oak Planks + Totem of Undying (you need one to make more — or an alternative: Emerald + Diamond + Dark Oak Planks, making it a pure-crafting gate)  
**Recommended recipe:** Dark Oak Planks + Diamond + Evoker Banner (banner from killing an evoker, which requires… see the circular problem below)

**The bootstrapping problem:** Evokers only spawn in Woodland Mansions, and reaching the first evoker is the core challenge. Resolution options:
- The first Mansion Room Island has a fixed cheap recipe (just dark oak + emerald), and rolling an evoker room is the rare chance outcome — rare roll unlocks further evoker-dependent recipes
- Or include evokers as a rare spawn on Pillager Outpost Islands (not vanilla behavior, but a reasonable escalation)  
*This needs a decision before implementation.*

**Variants (room type, weighted):**

| Weight | Room type | Spawns | Notable loot |
|---|---|---|---|
| 4 | Vindicator room | 2–3 Vindicators | Iron axe, emerald |
| 3 | Fake End Portal room | 1–2 Vindicators | Cobblestone, lapis |
| 2 | Checkerboard room | 2 Vindicators | Enchanted book |
| 1 | Evoker room | 1 Evoker | **Totem of Undying** — the whole point |

**Structure:** Fully enclosed room (no sky access), dark oak + birch wood, chests matching the vanilla mansion room loot tables  

---

### 🗿 Pillager Camp / Patrol

**Vanilla:** Pillager patrols spawn naturally; no fixed structure.

**Skyblock translation:** No structure exists, but pillager patrols will spawn naturally in the void world if the game's patrol system runs. This likely produces pillagers appearing on whatever island the player is on — which is either a nuisance or an intended encounter. **Recommendation:** disable patrol spawning in the void world (it's controlled by the game rule `doPatrolSpawning`) and let pillagers only appear on the Outpost Island. This keeps the pillager encounter intentional.

---

### 🏺 Trail Ruins

**Vanilla:** Underground buried structure, primarily an archaeology site. Suspicious gravel yields pottery sherds and other items.

**Skyblock translation:** A small buried platform island — mostly gravel and dirt on the surface, with suspicious gravel blocks hidden in the fill layer. The player digs down through the island to find the archaeology content.

**Island type:** New — **🏺 Ruins Island**  
**Recipe:** Gravel + Brick + Pottery Sherd (any) — rewards finding the first sherds from another source  
**Loot:** Suspicious gravel with trail ruins loot table (pottery sherds, emerald, gold nugget, bricks, coal, wheat, string, lead, blue dye, dead bush)  
**Structure:** Gravel/dirt surface island with suspicious gravel embedded in fill layer, some terracotta and brick fragments poking through  
**Notes:** Pottery sherds are a collector/aesthetic reward. The island appeals to the completionist player more than the efficiency player. Brush tool required — this island teaches the archaeology mechanic if the player hasn't encountered it.

**Status: ✅ Built** (v0.26.0). Not a dedicated seed — a `rare_structures` feature. `skyseed:trail_ruins/ruins` (`TrailRuinsTemplates`): a small buried 5×5 mud-brick/packed-mud/brick/terracotta floor under a gravel layer holding 7 `suspicious_gravel` (6 × `archaeology/trail_ruins_common` + 1 × `_rare`), low broken walls, and a few fragments poking up through the surface as the tell. Buried via the jigsaw `sink: 3`. Placed at **10% on `ancient_large`** (its deep/ancient home, no biome gate) and **5% on the regular `forest` theme gated to `#minecraft:is_taiga`** (the vanilla Trail Ruins biome — the forest grows its podzol/spruce taiga look there). Reuses the brushable-block (`StructureParts.suspicious`) + buried-structure mechanisms; no new seed.

---

### 🌊 Ocean Ruins

**Vanilla:** Underwater stone or sandstone ruins, hot or cold variant, with suspicious blocks for archaeology and some modest loot.

**Skyblock translation:** A partially submerged ruin on the Aquatic Island or a standalone flooded ruin island. Stone bricks or sandstone fragments rising from a shallow water body, suspicious blocks, a buried chest.

**Island type:** Not a dedicated seed — a **rare structural feature on Aquatic Islands** (similar to the igloo on Frozen islands). ~8% chance an Aquatic Island generates with an ocean ruin fragment emerging from its water body.

**Loot:** Vanilla ocean ruins loot (suspicious sand/gravel with archaeological finds, small chest with emerald, coal, gold nugget, enchanted fishing rod)  
**Notes:** The Sniffer egg is obtainable from warm ocean ruin suspicious sand — if the Aquatic Island's warm variant can generate this, it's a meaningful rare reward that feeds into the Lush Island's Torchflower/Pitcher Plant payoff.

**Status: ✅ Built** (v0.19.0). A `rare_structures` entry on the Aquatic theme (8%, `suppress_pond: true`) replaces the central pond with `skyseed:ocean_ruin/ruin` — a weathered (plain/mossy/cracked) stone-brick basin holding a contained 2-deep pool, three `suspicious_sand` (→ `archaeology/ocean_ruin_warm`, the Sniffer-egg source) and one `suspicious_gravel` (→ cold) sunk in the floor, a submerged `underwater_ruin_big` chest, and broken upper walls + a pillar. The basin is raised (walls hold the water) rather than truly sunk — a future refinement.

---

### ⚔️ Trial Chamber

**Vanilla:** Large underground structure of copper and tuff, featuring trial spawners (scales to player count), vaults (require trial keys to open), and the Breeze mob. Contains the mace and heavy core.

**Skyblock translation:** The most complex vanilla structure — too large for one island, but also too important to omit (the mace, the wind charge, the breeze are major 1.21 content). Translate as a **multi-room sequence** — one Trial Chamber Island leads to a vault that may contain a Trial Key (consumed) pointing to another Trial Chamber Island. A deliberate multi-island dungeon chain.

**Island type:** New — **⚔️ Trial Chamber Island**  
**Recipe:** Copper Ingot + Tuff + Trial Key (the Trial Key requirement creates a gating problem — see below)

**The Trial Key bootstrapping problem:** Trial Keys drop from trial spawners. Trial spawners are inside Trial Chambers. Resolution: Trial Keys have a small chance to appear in the Outpost Island's chest (vanilla outpost loot does include trial keys as of 1.21), giving the player a path to their first Trial Chamber Island.

**Variants (chamber section, weighted):**

| Weight | Section | Spawns | Notable loot |
|---|---|---|---|
| 3 | Corridor (entry) | 2 trial spawners (zombie/skeleton) | Trial Key (from spawner), basic loot |
| 2 | Intersection | 2 trial spawners (spider/slime) | Vault (requires key), copper bulbs |
| 1 | Chamber (boss) | Breeze + 2 trial spawners | Ominous Vault (requires Ominous Key), mace components |

**Structure:** Enclosed tuff/copper structure, trial spawners (scale with player count as vanilla), vault blocks requiring keys  
**Notes:** The Breeze (1.21 mob) only spawns in Trial Chambers. Wind charges are significant for the mace. The Heavy Core (mace crafting ingredient) comes from ominous vaults — this is the hardest loot in the overworld progression.

---

## Structure coverage summary

| Vanilla structure | Skyblock equivalent | How delivered |
|---|---|---|
| Dungeon (Monster Room) | ⚔️ Dungeon Island | Dedicated seed |
| Desert Temple | 🏺 Desert Temple Island | Dedicated seed |
| Jungle Temple | 🕹️ Jungle Temple Island | Dedicated seed |
| Witch Hut | 🧙 Witch Hut Island | Dedicated seed |
| Pillager Outpost | 🗼 Outpost Island | Dedicated seed |
| Village | 🏡🏪⛪ Village Islands | See SKYVILLAGESPLAN.md |
| Abandoned Village | Hamlet Island variant | ✅ 10% chance on Hamlet |
| Igloo | Frozen Island feature | ✅ 5% rare feature on Frozen |
| Ruined Portal | 🪨 Ruined Portal Island | Dedicated seed |
| Woodland Mansion | 🏰 Mansion Room Island | Dedicated seed (single room) |
| Trail Ruins | 🏺 Ruins Island | Dedicated seed |
| Ocean Ruins | Aquatic Island feature | ✅ 8% rare feature on Aquatic |
| Trial Chamber | ⚔️ Trial Chamber Island | Dedicated seed (multi-room) |
| Pillager Patrol | Disabled | Use `doPatrolSpawning` gamerule |
| Stronghold | Deferred | Nether/End progression scope |
| Mineshaft | Deferred | Could be an Ancient Island feature |
| Ancient City | Deferred | Could be an Ancient Island deep variant |
| Buried Treasure | Deferred | Could be an Aquatic Island rare feature |
| Shipwreck | Deferred | Could be an Aquatic Island feature |

---

## Deferred structures (notes)

**Stronghold:** Required to reach the End. Deferred to the End/Nether progression scope — the End portal room could be a dedicated island that appears in the Void dimension, or it could be reached via a crafted portal frame. Complex enough to deserve its own plan.

**Mineshaft:** A multi-corridor underground structure. Could be implemented as a special **Ancient Island variant** — a deep-throw Ancient Island has a chance of generating with mineshaft corridors hanging off its underside (visually spectacular — oak planks and rails dangling from deepslate). Deferred because it requires procedural corridor generation.

**Ancient City:** The deepslate city with sculk and the Warden. Too large for a single island; too important for a single room approach. Could be a dedicated Very Large island type at extreme cost, or a multi-island "city cluster" concept. Deferred — the Ancient Island's deep dark variant handles the sculk encounter for now.

**Buried Treasure / Shipwreck:** Both Aquatic-adjacent. Shipwreck could be an Aquatic Island structural feature (a sunken wreck rising from the water body). Buried Treasure is already a feature of any sandy island in vanilla and requires a map — could be a rare Aquatic Island chest. Both deferred.

---

## Progression sketch

```
Forest/Rocky sprinkle kills (rotten flesh, crossbow drops)
    ↓ Rotten Flesh + Cobblestone → ⚔️ Dungeon Island (mob farm + music discs)

Desert Island (sand + sandstone)
    ↓ TNT from Dungeon Island chest
    ↓ Sand + Sandstone + TNT → 🏺 Desert Temple Island (diamonds, golden apple)

Forest Island jungle variant
    ↓ Mossy Cobblestone + Jungle Planks + Tripwire Hook → 🕹️ Jungle Temple Island

Aquatic Island (swamp variant)
    ↓ Oak Planks + Mushroom + Cauldron → 🧙 Witch Hut Island (brewing ingredients, cat)

Rocky Island (gold from mid-Y band)
    ↓ Obsidian + Gold Ingot → 🪨 Ruined Portal Island (crying obsidian, fire charge)

Ruined Portal Island / Rocky mining
    ↓ Dark Oak Planks + Crossbow + Iron Ingot → 🗼 Outpost Island (allays, trial keys)

Outpost Island (first trial key)
    ↓ Copper + Tuff + Trial Key → ⚔️ Trial Chamber Island (mace, wind charges)

Outpost Island (ominous bottle → but raids disabled)
    ↓ [Ominous Bottle currently a collector item — revisit if Raid Island is built]

Dark Oak from Forest variant + Dungeon/Rocky progression
    ↓ Dark Oak + Emerald → 🏰 Mansion Room Island
    ↓ Evoker room (rare) → Totem of Undying
```

---

## Implementation notes

- **Structure islands use the same "generated terrain + curated structure on top" path as Animal Islands** — the island terrain generates procedurally, and the structure is placed on the flattest surface region.
- **Fully enclosed structures** (Dungeon, Jungle Temple, Mansion Room, Trial Chamber) are placed with their own floor — the generated terrain is effectively just the foundation beneath an enclosed structure. The interior is what matters.
- **Loot tables** should reuse vanilla loot table IDs where possible rather than reimplementing them — `minecraft:chests/simple_dungeon`, `minecraft:chests/desert_pyramid`, etc. are already defined and balanced.
- **Trial spawners** scale to player count via vanilla mechanics — no custom implementation needed, just place the trial spawner block via structure placement.
- **Mob spawner type** for Dungeon Island uses the vanilla dungeon spawner mechanic (random mob type on placement) — no need to hardcode a specific mob.
- **Structure placement reuses the existing `jigsaw` theme field** (added for the village/animal islands) — no separate `structures` field was needed. A structure island is a theme whose `jigsaw` pool *is* the structure: the generator levels a disc pad and assembles the `.nbt` at the centre. Loot chests and mob spawners are just block-entity NBT baked into the `.nbt` (the dungeon's spawner mob is chosen via three weighted pool variants; the Stable proved loot chests). Initial-mob spawns, if a structure wants them, can reuse the `animals` pack field. So the "first engine task" is already done — new structure islands are pure data + a code-authored `.nbt`.

---

*Structure list verified against Minecraft 1.21.1. Trial Chambers and the Breeze are 1.21 additions. Re-verify loot table IDs before implementation as these have changed across versions.*
