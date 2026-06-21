# Skyseed — Villages & Villagers Plan

This document covers how villages and villagers fit into the Skyseed skyblock context. See `README.md` for general architecture, `SKYISLANDSPLAN.md` for island types, and `SKYANIMALSPLAN.md` for animal islands.

---

## The core problem

Villages in vanilla Minecraft generate as large multi-building complexes spread across hundreds of blocks. In Skyseed, there are no hundreds of blocks — there are floating islands, each at most 10–20 blocks across. A vanilla village cannot exist in this context.

But villagers and their trading system are genuinely important: the cleric is the only villager to reliably sell Ender pearls, librarians sell enchanted books, leatherworkers sell saddles, and the trading economy as a whole gives the player a way to convert renewable resources into otherwise hard-to-obtain items. Cutting them out entirely would leave a significant hole in the late-game progression.

The solution is to break the village into its constituent parts and spread them across dedicated islands — one profession per island, or a small cluster of related professions on a slightly larger island. The player builds their own trading hub by crafting and placing Villager Islands rather than finding a pre-generated village.

---

## Village Island types

### 🏡 Hamlet Island ✨ (base villager island)

**Character:** The starting point for the villager system. A small cobblestone-and-wood cottage — one building, one bed, one villager. The villager spawns as **Unemployed**, ready to take any profession the player provides via a job site block. No iron golem. No raids (see Raids section).

**Recipe:** Oak Planks + Cobblestone + Emerald  
*(Emerald gates this behind Rocky progression — you need to mine before you can trade)*

**Palette:** Oak planks, cobblestone, glass pane windows, oak door. Vanilla plains-village aesthetic.

**Structure:** A hand-authored small cottage (~5×5 interior), one bed, one crafting table, a small chest with a handful of starter food. The emerald in the recipe hints at the trading system the island enables.

**Spawns:** 1 Unemployed Villager (adult)

**Biome awareness:** The villager's visual variant matches the germination biome — a Hamlet Island thrown in a desert generates a desert-dressed villager, etc. Purely cosmetic; trades are identical regardless of variant.

**Progression note:** The player places their chosen job site block near the villager to assign a profession. This is vanilla behavior and requires no special handling — it's the same mechanic as in a normal village. The island's value is the villager and the bed; the profession is the player's choice.

---

### 🏪 Trade Post Island ✨ (multi-profession, mid-tier)

**Character:** A slightly larger island with two or three buildings and 2–3 villagers, each with their own job site block pre-placed. The "small commercial district" version — saves the player from crafting many individual Hamlet Islands.

**Recipe:** Oak Planks + Cobblestone + 3× Emerald  
*(More expensive, proportionally better value)*

**Structure:** 2–3 small buildings arranged around a central path, each containing one pre-placed job site block and one bed per villager. Job site blocks are chosen from a weighted random selection (see variant table below).

**Variants (weighted):**

| Weight | Buildings included |
|---|---|
| 3 | Farmer (composter) + Shepherd (loom) + Fletcher (fletching table) |
| 3 | Armorer (blast furnace) + Weaponsmith (grindstone) + Toolsmith (smithing table) |
| 2 | Librarian (lectern) + Cartographer (cartography table) |
| 2 | Butcher (smoker) + Fisherman (barrel) + Leatherworker (cauldron) |
| 1 | Cleric (brewing stand) + Mason (stonecutter) |

**Biome awareness:** Same cosmetic variant behavior as Hamlet Island.

---

### ⛪ Village Center Island ✨ (late-tier, all professions)

**Character:** The premium village island. A proper village center: a well, a notice board, 5–6 buildings, one iron golem, and a villager of each of the 13 professions. This is the "full trading hall" island — expensive, but it gives the player the complete trading economy in one throw.

**Recipe:** 5× Emerald + Iron Ingot + Oak Planks + Cobblestone (shaped — a 3×3 recipe)

**Structure:** Authored as a compact village center — buildings arranged around a central well/square, one bed and one job site block per villager, all 13 professions represented. Iron golem patrols the perimeter.

**Spawns:** 13 Villagers (one per profession) + 1 Iron Golem

**Notes:** This is a late-game island. The 5 emerald cost means the player needs an established Rocky/Ancient mining operation before this is reachable. The all-in-one nature makes it feel like a reward rather than a grind.

---

## The 13 professions and their skyblock value

All professions available in vanilla 1.21, with their skyblock-specific value noted. This informs which Trade Post variant is worth crafting first.

| Profession | Job Site Block | Key skyblock value |
|---|---|---|
| Armorer | Blast Furnace | Chainmail armor (unobtainable otherwise), enchanted diamond armor late |
| Butcher | Smoker | Cooked meat for emeralds — converts farm output to currency |
| Cartographer | Cartography Table | Explorer maps — *(limited value in skyblock void, but Globe banner pattern is exclusive)* |
| Cleric | Brewing Stand | **Ender Pearls** — the most important late-game trade; also Glowstone, Redstone |
| Farmer | Composter | Buys crops for emeralds — converts farm surplus to currency; sells golden carrots |
| Fisherman | Barrel | Buys fish; sells campfire, enchanted fishing rod |
| Fletcher | Fletching Table | Buys sticks (trivially renewable) for emeralds — best early emerald farm; sells arrows |
| Leatherworker | Cauldron | **Saddle** at master level — only craftable source in skyblock |
| Librarian | Lectern | **Enchanted books** — biome-gated in 1.21.5; key late-game upgrade path |
| Mason | Stonecutter | Buys stone for emeralds; sells terracotta, quartz — good for Badlands island output |
| Shepherd | Loom | Buys wool for emeralds — converts Wool Farm Island output to currency; sells dyes |
| Toolsmith | Smithing Table | Sells enchanted diamond tools at master |
| Weaponsmith | Grindstone | Sells enchanted diamond sword/axe at master |

**Most critical professions in skyblock context, in rough priority:**
1. **Fletcher** — sticks → emeralds is the easiest early economy engine
2. **Cleric** — Ender Pearls are otherwise nearly unavailable in a void world
3. **Leatherworker** — Saddle (not craftable in vanilla)
4. **Librarian** — enchanted books (best enchantment path)
5. **Armorer/Toolsmith/Weaponsmith** — late-game gear without needing to craft everything

---

## Emerald sourcing

Emeralds are the currency for the entire trading system, so their availability in a skyblock context matters.

**Primary sources (sustainable):**
- **Rocky Island (mountain variant)** — emerald ore, the only direct mining source
- **Fletcher** — 32 sticks → 1 emerald. Sticks from tree farms are the most renewable emerald source in the game
- **Shepherd** — wool → emeralds. Wool Farm Island output converts directly
- **Farmer** — crop surplus → emeralds
- **Butcher** — meat → emeralds
- **Mason** — stone → emeralds. Rocky Island output converts directly

**First emerald problem:** the Hamlet Island recipe requires an emerald, but the player may not have one yet. Two resolutions:
- Make the first emerald available as a rare sprinkle on Rocky islands (a single emerald ore at low chance regardless of Y band) — thematically consistent with vanilla (emeralds generate in mountains)
- Or lower the Hamlet Island recipe to not require an emerald, reserving the emerald cost for Trade Post and Village Center

This is a balance decision to make before implementing recipes. The first option is recommended — it keeps the "mine to unlock trading" narrative intact.

> **Resolved (v0.3.0):** the **Large Rocky** seed (`skyseed:rocky_large`) now exists — it is crafted from just **stone + cobblestone** (no emerald gate) and reliably carries **emerald ore** (~0.40/island, verified). That gives a clean first-emerald path without a chicken-and-egg, so the chosen approach is to **gate the Hamlet recipe behind 1 emerald**. Base Rocky still has no emerald; emeralds come from the (cheaply craftable) mountain.

---

## Raids and iron golems

Raids are triggered when a player with Bad Omen (obtained from Ominous Bottles in Trial Chambers) enters a village. In vanilla this is a meaningful risk-reward mechanic.

**In Skyseed skyblock, raids are a significant design problem:**
- A raid spawns a wave of illagers that will path around a small floating island and immediately fall into the void. The raid "completes" trivially or bugs out.
- Alternatively, all illagers concentrate on the tiny island and trivially overwhelm the player.
- Neither is the intended experience.

**Recommended handling:** Disable raid triggers for Skyseed village islands entirely, at least for v1. This can be done by not registering the island's villagers as part of a vanilla "village" POI cluster (they live on the island, but the game doesn't recognize it as a raid-eligible village). This is a consequence of not using vanilla village generation — the POI/village detection system won't see these as a real village unless you explicitly set that up, which you're not doing.

> **⚠ Verify before designing around this.** The above is an assumption, and a risky one: beds and workstations placed in the world **are** POIs, and a villager near claimed beds + a workstation is exactly how vanilla forms a village (and enables raids / iron-golem spawns) — no explicit registration required. Spike this first: place beds + a workstation + a villager on a generated island, give a nearby player Bad Omen, and confirm whether a raid starts. If it does, we need an explicit suppression (e.g. `doPatrolSpawning` is unrelated; the lever is the `point_of_interest_type` / village detection, or keeping villagers/beds/workstations out of POI range of each other). This is the single biggest unknown in this plan.

**Iron Golems** on the Village Center Island are cosmetic/protective against natural hostile mob spawns, not raid defense. They work normally.

**Future option:** A "Raid Island" could be a separate, deliberately enclosed combat arena island crafted from Ominous Bottles — an opt-in raid experience with proper containment. Deferred.

---

## Wandering Trader

The Wandering Trader spawns naturally in vanilla and wanders near the player. In a void world, he spawns on whatever island the player is on. This works without any changes — he just appears, offers his rotating trades, and wanders off (probably into the void, which is a mildly tragic image).

As of 1.21.5, wandering trader changes were fully implemented including lowered prices and additional trades. No changes needed for skyblock — let him spawn naturally.

**One consideration:** the Wandering Trader's two llamas spawn with him. On a small island two llamas immediately wandering toward the edge is chaotic. This is either charming or annoying depending on temperament. Not a blocker, but worth noting.

---

## Zombie Villagers

Zombie villagers spawn naturally at night on any unlit island. The zombie villager → cure mechanic (Splash Weakness Potion + Golden Apple) gives permanent trade discounts in Java Edition. This works without changes in skyblock, and is if anything *more* valuable here since the player has fewer villagers to work with. The Igloo structure (see SKYSTRUCTURESPLAN.md) in vanilla contains a zombie villager specifically for this mechanic — consider including one as a rare room inside the Hamlet Island structure.

---

## Breeding

Villager breeding requires:
- Unclaimed beds (one per desired new villager)
- Food (bread, carrots, potatoes, beetroot)
- Willingness (triggered by having surplus food)

All of this works normally in skyblock. The player can breed villagers on their Hamlet or Trade Post islands by placing additional beds and ensuring the farm islands supply enough food. No changes needed.

---

## Progression sketch

```
Rocky Island (mountain variant)
    ↓ first emerald (rare ore sprinkle)

🏡 Hamlet Island (planks + cobblestone + emerald)
    ↓ player places job site block → any profession
    ↓ Fletcher trade: sticks → emeralds (the economy engine)

🏪 Trade Post Island (planks + cobblestone + 3× emerald)
    ↓ 2–3 pre-assigned professions
    ↓ Cleric → Ender Pearls (End progression gate)
    ↓ Leatherworker → Saddle

⛪ Village Center Island (5× emerald + iron + planks + cobblestone)
    ↓ all 13 professions
    ↓ Master Librarian → enchanted books (best enchantments)
    ↓ Master Armorer/Toolsmith/Weaponsmith → enchanted diamond gear
```

---

## Implementation notes

- **Village islands are curated structures**, not procedurally generated. The terrain generates normally (grass/cobblestone palette), and the buildings are placed on top using the same "curated structure on generated terrain" path as Animal Islands.
- **Structure placement uses NBT `StructureTemplate` files** (decided 2026-06-21), authored in-game with structure blocks and stamped onto the island at generation. This is a new engine capability — the codebase currently has **no** structure-placement system (the start island and trees are hand-coded `setBlock` builders). Building the template loader is the first task here and is shared with Animal Islands and Structure Islands. Buildings also need a **flat pad / flattest-region** step, since the generated terrain has rim noise and a top dome.
- **Villagers are spawned inside their buildings** at generation time, already assigned to their job site blocks (for Trade Post and Village Center). The Hamlet Island spawns an Unemployed Villager.
- **No vanilla village POI registration** — don't register these as vanilla villages. This avoids raids, avoids iron golem overgeneration, and avoids the vanilla village-detection system making assumptions about the island layout.
- **Bed placement is inside each building** — villagers path to beds normally. Ensure beds are accessible (no blocks in the way) so the villager links to their bed correctly and restocks function.
- **The `mobs` sprinkle field** (the implemented theme mob list) is not used here — villagers are placed as part of the structure placement step, not as random sprinkles.

---

*Verified against Minecraft 1.21.1 villager trading system. Note: librarian biome-gated enchantments and wandering trader price changes arrived in 1.21.5 — verify target version before implementing biome-specific librarian behavior.*
