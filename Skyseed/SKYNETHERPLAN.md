# Skyseed — Nether Chapter Plan

This document covers the Nether dimension chapter of Skyseed. See `README.md` for general architecture and the
built Overworld chapter, and `description.md` for the published mod description.

> **Design pivot (this revision):** the Nether no longer has a separate "dedicated Nether seed" track that competed
> with the overworld seeds. Instead there is **one rule** — every overworld terrain seed either *adapts* to the
> Nether (same item, dimension + biome aware) or *fizzles* — plus a **Tier-2 upgrade** path where the old Nether
> seeds (Nether Rocky, Crimson, Warped, Soul, Basalt) return as Nether-material crafts — joined by a new Lava seed —
> that grow the *enhanced* version. This kills the earlier self-contradictions (the "works vs fizzle" table, Forest both reused and fizzled,
> Y both gating and not gating value) and lets far more overworld seeds carry into the Nether.

---

## World design

### The void

> **Built (v0.35.0).** `the_nether` in the `skyseed:skyblock` preset now uses the `skyseed:void_nether` noise settings: no terrain (`final_density` 0), a **lava sea** below Y 32 (`default_fluid` lava + `sea_level` 32), and the five Nether biomes via the `minecraft:nether` multi-noise source. Applies to newly created worlds. Seeds and the Nether structures are still to come; there's deliberately **no curated start platform** (see *Arrival* below).

The Nether is **completely empty** except for what the player generates — no natural terrain, no bedrock floor.
Below every island is a **lava sea** at a fixed low Y: it lights the dimension from beneath, gives the hellish
ambience, and is a lethal hazard. Fall off an island and you're dead, no recovery.

The biome map still generates normally with the full multi-noise Nether biomes, distributed as in a natural Nether.
Seeds respond to the biome at their germination point exactly as overworld seeds respond to overworld biomes. This
is the core loop: **you get the right Nether island by throwing the right seed into the right part of the sky**,
which means exploring the void to find biomes first. Xaero's biome overlay earns its keep here.

### Arrival — no curated start

**Decision: there is no curated Nether start.** When the player first steps through a portal, the game's own portal
placement drops them onto the little obsidian platform it builds in the void over the lava sea — a small spot to
stand on, and that's all they need. From there they do what they do everywhere in Skyseed: start throwing seeds (the
first Nether seed *is* the first real platform). Keeping arrival uncurated is simpler and skyblock-pure.

The early Nether is genuinely lethal — one stumble into the lava sea is fatal — but that's the player's problem to
solve with the gear and potions they carry **through** the portal, not something handed to them on arrival.

### No ceiling — and how value is gated

There's no Y=128 ceiling; islands float at any Y under the open Nether fog. Value is gated on **two axes**:

- **Biome -> island *type*.** Which of the five biomes you throw into decides the island's character (mining,
  fungal, soul, basalt, lava).
- **Lava proximity (low Y) -> ore *richness*.** Islands grown low, near the lava sea, are hotter, more dangerous,
  and carry the Ancient Debris that vanilla puts at low Y; high islands are safe but lean. This deliberately
  **rewards risky low throws** — that risk/reward tension is the point, and it maps directly onto vanilla
  (debris peaks ~Y15). *(Earlier drafts claimed altitude doesn't gate value — it does, via lava proximity. Resolved.)*

### Ruined Portal twins — linked across dimensions

A cross-dimension detail, and the one structure we can **pre-place** (because *we* choose where the ruined portal
goes, unlike the player-placed arrival portal). **Whenever a ruined portal is created on either side** — the
dedicated Ruined Portal seed *or* the random `rare_structures` roll on a big island — a matching ruined portal is
generated on the **other** side at the **vanilla-linked coordinate** (the standard 8:1 map: `nether = overworld / 8`,
`overworld = nether * 8`).

- **The twin is a small, dimension-appropriate island:** a little **dirt** island in the Overworld, a little
  **netherrack** island in the Nether, each carrying its own ruined frame.
- **Placement stays close on purpose.** It runs the normal germination free-space check (is every block clear? if
  not, step to the nearest free spot) — but the step must be *small*: a Nether portal too far from the linked
  coordinate **won't link**. So unlike a normal seed (which can be shoved any distance to fit), the twin prefers the
  exact linked spot and moves only the shortest distance that frees it.
- **The payoff is free.** Sitting at the linked coordinate, if the player repairs and lights *both* frames, vanilla's
  own portal search connects them into a real working pair — no linking code needed. (This is why the frame was
  fixed to a real, repairable 4×5 shape in v0.35.6.)
- **No recursion:** a twin doesn't spawn a twin of its own.

---

## The adaptation model

> **Built (v0.36.0 / v0.37.0).** The dimension key is implemented: a `biome_override` takes an optional `dimension`
> (e.g. `minecraft:the_nether`), matched alongside biome + Y, so a seed's Nether form is a dimension-gated override —
> no new item. A theme also declares its base `dimensions` (every current seed is explicitly `[minecraft:overworld]`).
> **Adapt-or-fizzle is live (v0.37.0):** a seed thrown into a dimension it implements neither in `dimensions` nor via
> a dimension-keyed override **fizzles** instead of growing the foreign base form — so a future Nether-only seed gets
> the overworld fizzle for free. **Rocky** is the first adapted seed (overworld base + Nether overrides). **As of
> v0.45.0 a dimension override never inherits Overworld content** — an unset field is neutral/empty (a
> netherrack/end-stone body, no ores/decoration/mobs), not the overworld base, so a dimension form can't leak grass,
> coal or terracotta across the portal. Still to do: adapt Lush (Meadow + Frozen fizzle by design).

**One rule:** every overworld terrain seed either **adapts** or **fizzles** in the Nether.

- **Adapts** — the throw entity already checks dimension (the fizzle rule). An overworld seed thrown in the Nether
  grows its Nether form, shaped by the biome it lands in, via the existing `biome_overrides` system extended with a
  dimension key. No new item.
- **Fizzles** — seeds with no Nether analogue puff out (a smoke burst, a hiss, the seed is consumed). A light
  punishment; experimentation stays cheap. The Patchouli guide flags which seeds are dimension-specific.

**Two tiers (the hybrid):**

| Tier | What you throw | What you get | Gate |
|---|---|---|---|
| **1 — adapt (free)** | the overworld seed (e.g. Rocky) | a **tiny** Nether island (~7×7×4) — a foothold and some resources, not a base | just reaching + surviving the Nether |
| **2 — upgrade (craft)** | a **Nether seed** crafted from the overworld seed + a signature Nether block | the **full-size**, *enhanced* island: richer ore, guaranteed mob packs, the biome's rare block | Nether materials (the crafting sink) |

> **★ Design decision (v0.39.0): Tier-1 adapted islands are deliberately TINY (~7×7×4).** Reusing an overworld seed
> in the Nether gives you a *foothold* — enough netherrack / soul sand and a little ore to bootstrap — but it is
> small on purpose. The substantial Nether islands come from the Tier-2 **Nether-specific seeds**: full-size and far
> richer. Reuse is a convenience, not a shortcut; the incentive is firmly to craft the Nether seeds. *(A deliberate
> deviation from the earlier "Tier-1 = the basic island" framing — putting the weight on Nether seeds is more fun.)*

So the old dedicated seeds come back as Tier-2 upgrades, not a parallel system:

| Tier-2 seed | Recipe (sketch) | Adds over Tier 1 |
|---|---|---|
| **Nether Rocky** | Rocky seed + Blackstone | deeper, gilded blackstone, debris-rich core |
| **Crimson** | Forest seed + Nether Wart Block | dense canopy, guaranteed Hoglin pack, wart floor |
| **Warped** | Forest seed + Warped Wart Block | dense canopy, guaranteed Enderman population |
| **Soul** | Desert seed + Bone Block | large fossil, soul fire field, more skeletons |
| **Basalt** | Badlands seed + Gilded Blackstone | taller chaotic columns, gilded core |
| **Lava** | Aquatic seed + Magma Block | bigger lava lagoon, guaranteed Strider pack |

**Large vs Tier-2 — they never blur.** Overworld **Large** seeds adapt too, as *larger Tier-1* islands: bigger,
same content. **Tier-2 Nether seeds are defined by exclusive *content*, not size** — gilded blackstone, ancient-debris
nodes, guaranteed mob packs, the biome's signature rare block. So Large = bigger; Tier-2 = richer.

---

## Seed mapping

The ten overworld terrain seeds (and Ruined Portal), in the Nether:

| Overworld seed | Nether (Tier 1, free) | Biome focus | Tier-2 upgrade |
|---|---|---|---|
| **Rocky** | netherrack **mining** island (quartz/gold/debris) | Nether Wastes | Nether Rocky |
| **Forest** | dense **huge-fungi forest** (wood, canopy, mobs) | Crimson / Warped | Crimson / Warped |
| **Mushroom** | the **mushroom island** (giant red/brown mushrooms, Mooshrooms — a safe larder) | any (a transplanted safe pocket) | — |
| **Desert** | **Soul Sand Valley** (soul sand, bone fossils) | Soul Sand Valley | Soul |
| **Badlands** | **Basalt Deltas** (banded basalt/blackstone) | Basalt Deltas | Basalt |
| **Aquatic** | **Lava Lagoon** (contained lava pool, striders) | any (lava is everywhere) | Lava |
| **Lush** | **vine grotto** (vines + shroomlight glow) | Crimson / Warped | — |
| **Ancient** | **haunted deep** (blackstone, soul veins, fossils) | Soul Sand Valley / any | — |
| **Frozen** | **fizzles** — no cold analogue | — | — |
| **Meadow** | **fizzles** — flowers/grass have no Nether analogue | — | — |
| **Ruined Portal** | **works** — Nether-scene version (blackstone surround) | any | — |

That's eight of ten terrain seeds carrying into the Nether (vs two before), filling all five biomes plus the
mushroom, lava-lagoon and grotto extras — which is what makes the dimension feel fuller without inventing biomes that
don't exist.

> **Forest vs Mushroom (resolved):** they're now clearly distinct. **Forest** stays *foresty* — the dense
> huge-fungus **forest** (crimson/warped stems as "trees," canopy, vines, Hoglins/Endermen). **Mushroom** is the
> dedicated **mushroom island** — giant red & brown mushrooms (which thrive in the Nether's dark) on mycelium, with
> Mooshrooms: a calm safe haven and a rare *food* source. Nether fungi vs actual mushrooms — no overlap.

---

## The five biomes (reference)

What each biome *is* in vanilla, so the adaptations above have a grounding. **Structures don't generate naturally**
in the empty Skyseed Nether — Fortresses, Bastions and Trading Posts are seed-grown (below).

| Biome | Character | Signature blocks | Native mobs |
|---|---|---|---|
| 🔥 **Nether Wastes** | open netherrack plains, lava pools | netherrack, quartz ore, gold ore, gravel | Zombified Piglin, Ghast, Piglin, Magma Cube |
| 🌲 **Crimson Forest** | dense red fungal forest | crimson nylium/stem, weeping vines, shroomlight, wart block | Hoglin, Piglin, Zombified Piglin |
| 🌀 **Warped Forest** | the "safe" teal fungal forest | warped nylium/stem, twisting vines, nether sprouts | Enderman (almost exclusively) |
| 💀 **Soul Sand Valley** | haunting, lethal, foggy | soul sand/soil, blue soul fire, bone block fossils, basalt | Skeleton (high), Ghast, Enderman |
| 🌋 **Basalt Deltas** | volcanic chaos | basalt, blackstone, magma block, gilded blackstone | Magma Cube (high), Ghast |

---

## Adapted islands (detail)

### 🪨 Rocky → Nether mining *(Nether Wastes)*
> **Built (v0.36.0; tiny since v0.39.0).** Tier-1 Rocky: a **tiny ~7×7×4** netherrack island with a blackstone core,
> a little Nether Quartz + Nether Gold, and Ancient Debris that ramps up low (a `max_y: 50` band), with zombified
> piglin / magma cube / piglin sprinkles. Still to come: Tier-2 *Nether Rocky* (the full-size, debris-rich version),
> and the biome-specific surface patches below (crimson/warped/soul/basalt).

- **Palette:** netherrack surface/fill, basalt/blackstone core.
- **Biome overrides:** crimson -> crimson nylium patches; warped -> warped patches; soul -> soul sand patches + blue
  fire; basalt -> basalt/blackstone + magma block patches.
- **Ore (lava proximity — see below):** quartz + gold high; Ancient Debris emerges low.
- **Mobs:** Zombified Piglin (30%), Magma Cube small (20%), Piglin (15%, triggers bartering if you wear gold).
- **Tier 2 — Nether Rocky:** taller blackstone/basalt mountain, gilded blackstone surface, best debris odds. The
  Nether's deep-mining island.

### 🌲 Forest → huge-fungi forest *(Crimson / Warped)*
> **Built (v0.44.0).** A tiny ~7×7×4 fungal patch: **crimson nylium** (crimson roots/fungi, Hoglin + Piglin) by
> default, or **warped nylium** (warped roots, nether sprouts, twisting vines, Endermen) in a `warped_forest` biome;
> shroomlight floor glow, weeping vines hanging underneath, a little nether quartz. *Small* fungal decoration only —
> the dense huge-fungi forest is the Tier-2 Crimson/Warped payoff.

- Stays *foresty* — a dense fungal **forest**, not a grove. Tall huge crimson/warped fungus are the "trees."
- **Palette:** crimson/warped nylium surface (by biome), netherrack fill.
- **Decoration:** huge crimson/warped fungus (density ~0.2), weeping vines (crimson, hang off the underside),
  twisting vines (warped, climb up), shroomlights in the canopy, roots/sprouts on the floor.
- **Mobs:** crimson -> Hoglin (25%, the Nether's food animal) + Piglin; warped -> Enderman (40%), no Hoglin/Piglin.
- **Yields:** crimson stem (wood), wart blocks, shroomlights; Hoglin meat/leather (crimson) or ender pearls (warped).
- **Tier 2 — Crimson / Warped:** denser canopy, guaranteed mob pack, nether wart growing on the floor.

### 🍄 Mushroom → mushroom island *(a transplanted safe haven)*
> **Built (v0.43.0).** A tiny ~7×7×4 calm mycelium island over netherrack, mushroom-cap surface patches + small
> red/brown mushrooms, grazed by mooshrooms (the Nether's food/leather). **Caveat:** the overworld "no hostile spawns"
> is a property of the *mushroom_fields biome*, not the mycelium block, so it does **not** translate to the Nether —
> the value here is the mooshrooms, not safety. Still to come: Large Mushroom (the bigger haven with giant mushrooms).

- Mushrooms thrive in the Nether's permanent darkness, so a literal mushroom island belongs here — and it doubles as
  the dimension's rare **safe pocket** and **food source** (Nether food is scarce). The actual-mushroom counterpart
  to Forest's fungal trees.
- **Palette:** mycelium surface, netherrack fill/core.
- **Decoration:** giant **red & brown mushrooms** (density ~0.15), red/brown mushroom ground scatter, mushrooms on
  the underside. No hostile decoration — it's the calm island.
- **Mobs:** **Mooshroom** (the draw — milk, mushroom stew, beef + leather); nothing hostile spawns on it.
- **Yields:** mushroom blocks (building), stew/food, brown mushroom (-> fermented spider eye -> weakness/harming).
- **Biome behavior:** ignores the surrounding biome's hostility — the same calm island wherever thrown (a deliberate
  oddity, like a mooshroom island floating in hell). **Large variant:** a bigger haven, a guaranteed Mooshroom herd.
  *(No Tier-2 — Mushroom has no signature-block upgrade; its only "more" is the bigger Tier-1 island.)*

### 💀 Desert → Soul Sand Valley
> **Built (v0.38.0; tiny since v0.39.0).** A **tiny ~7×7×4** soul-sand island over soul soil + a basalt core,
> soul-fire ground scatter (eternal on soul sand), bone-block fossils buried in the basalt (a `bone_block` ore), a
> little nether quartz/gold, and skeleton + enderman sprinkles. Still to come: a standalone bone fossil + basalt
> columns, and Tier-2 *Soul* (the full-size version).

- **Palette:** soul sand surface, soul soil fill, basalt core.
- **Decoration:** blue **soul fire** (ambient, hurts the player), basalt columns at the rim, one **bone-block
  fossil** rising from the island, blue flame particles.
- **Mobs:** Skeleton (50%, multiple on spawn), Ghast (10%, spawns off-island).
- **Yields:** soul sand (Wither, Soul Speed), soul soil, bone meal. **Tier 2 — Soul:** larger fossil, deeper soul
  sand, soul fire everywhere, more skeletons.

### 🌋 Badlands → Basalt Deltas
> **Built (v0.40.0).** A tiny ~7×7×4 Basalt Deltas fragment: blackstone surface/core over a basalt fill (the overworld
> terracotta strata dropped via a `fill_bands` override), magma-block + basalt ground scatter, gilded blackstone +
> nether gold, and magma-cube sprinkles. Still to come: jagged basalt columns, lava wells, and Tier-2 *Basalt*.

- **Palette:** banded basalt/blackstone surface (echoing terracotta layers), blackstone fill, basalt core.
- **Decoration:** jagged **basalt columns** (the defining feature), magma-block surface patches (the hazard — fire
  damage on contact), shallow lava wells between columns, blackstone boulders.
- **Mobs:** Magma Cube large (30%) / medium (40%), Ghast (10%).
- **Yields:** blackstone (the Nether's stone), magma cream. **Tier 2 — Basalt:** taller chaotic columns, gilded
  blackstone embedded in the core (gold drops). Genuinely dangerous to navigate.

### 🫧 Aquatic → Lava Lagoon *(any biome)*
> **Built (v0.41.0).** A tiny ~7×7×4 basalt island whose pond is overridden to a contained **lava** basin
> (magma-block shores, a Strider as the pond "water mob", a magma cube, a little nether gold). Still to come: an
> islet, a guaranteed Strider pack, and Tier-2 *Lava* (the full-size lagoon).

- The clever swap: water evaporates, so the pond becomes a **contained lava basin** (walled like the Ocean Ruin's
  pool so it can't overflow off the island).
- **Decoration:** magma-block shore, basalt rim, an occasional islet; **Striders** ride the lava (the Nether's
  rideable lava mob — finally used), Magma Cubes around, the odd Ghast overhead.
- **Yields:** striders (lava travel + breeding with warped fungus on a stick), magma cream. **Tier 2 — Lava:** a
  bigger lagoon, guaranteed Strider pack, a gilded-blackstone or fossil islet. A standout new island.

### 🌿 Lush → vine grotto *(Crimson / Warped)*
- The "glowing hanging garden" translated: **twisting + weeping vines** drape the underside and walls,
  **shroomlights** provide the glow (in place of glow lichen/glow berries), nether sprouts and roots carpet the
  floor, the odd warped fungus. Calm and pretty — the Nether's prettiest island. Endermen if warped.
- **Large variant:** Large Lush — denser vine curtains, more shroomlights. *(No Tier-2 upgrade.)*

### ⬛ Ancient → haunted deep *(Soul Sand Valley / any)*
> **Built (v0.42.0).** A tiny ~7×7×4 dark blackstone island over a basalt band, soul-soil flecks + soul-lantern glow,
> shot through with soul-sand veins, bone fossils and the odd Ancient Debris (a reliable if small debris source), with
> skeleton + enderman sprinkles. Still to come: a larger fossil network, and Large Ancient (top debris odds).

- The deep-dark vibe without sculk (no Nether analogue): a **blackstone/basalt deep** shot through with **soul-sand
  veins**, **buried bone fossils**, blue **soul fire**, thick fog, basalt spires. A mining-capable island with an
  eerie atmosphere — and, grown low, an Ancient-Debris island (deep = debris ties straight into lava proximity).
- **Mobs:** Skeleton, Enderman. **Large variant:** Large Ancient — bigger fossil network, more soul fire, top debris odds. *(No Tier-2 upgrade.)*
- *(Distinct from Desert->Soul: Desert is open soul-sand surface dunes + farming; Ancient is dark blackstone depths +
  debris mining.)*

---

## Ore & value — lava proximity (the one ore model)

Mining-capable islands (Rocky, Ancient, and any island grown low) use a **single** ore model keyed to height above
the lava sea. There is no second, competing system.

| Height above the lava sea | Ore content |
|---|---|
| **High** | Nether Quartz (common), Nether Gold (common) |
| **Mid** | Quartz, Gold (richer), a small Ancient Debris chance |
| **Low** | Ancient Debris (notable), Gold (rich), Quartz |
| **Abyss** (just above the lava) | Ancient Debris (best), Gold, Gilded Blackstone (rare) |

**Ancient Debris** is the only source of Netherite — and it generates low in vanilla, so "throw low, near the lava,
for the best ore" is both thematically right and mechanically faithful. The danger of a low throw *is* the gate.

---

## Nether structures (new seeds — no overworld parallel)

These have no overworld-craftable parallel, so they're genuinely new seeds — built with the existing jigsaw +
mob-pack machinery (as the overworld structure islands are).

### 🏰 Nether Fortress Island
- **Recipe:** Nether Brick **only** (smeltable from netherrack). *This deliberately drops the old Blaze-Rod
  requirement, which was circular — you needed a Fortress to get the Blaze Rod that the Fortress seed required.*
- **Structure:** a jigsaw Fortress section — corridors, a **blaze spawner room**, a nether-wart garden, a chest room.
- **Spawns:** Blaze (permanent spawner), Wither Skeleton (corridors), Magma Cube.
- **Loot:** `minecraft:chests/nether_bridge`. **Why it matters:** Blaze Rods -> brewing + Eyes of Ender (the End
  gate); nether wart -> all potions; wither skeleton skulls -> the Wither.

### 🏯 Bastion Remnant Island
- **Recipe:** Blackstone + Gold Ingot + Crying Obsidian.
- **Fizzles over Basalt Deltas specifically** (vanilla rule) with its own message: *"The seed finds no purchase in
  the volcanic rock."*
- **Variants (weighted):** Bridge (3) — brutes, hoglin stables; Housing (2) — piglins, central loot; Treasure (1) —
  brutes, gilded blackstone, magma-cube spawner, the best loot.
- **Loot:** `minecraft:chests/bastion_treasure` / `bastion_other`. Lodestone, Pigstep, top Nether loot.

### 🐷 Piglin Trading Post Island *(the Nether's "village")*
- **Recipe:** Gold Ingot + Blackstone + Netherrack.
- **Structure:** a blackstone hall with gold accents, soul-campfire "thrones," **3–5 gold-armoured Piglins** (neutral
  so they won't attack). The player drops gold to barter — no job blocks; barter is fixed by vanilla.
- **Barter table covers:** Fire Resistance Potion, gravel/flint, leather, nether brick, small obsidian + soul sand,
  string, Ender Pearl (rare), Soul Speed boots (rare). The economy hub, gold instead of emeralds.

### ☠️ Wither Arena Island
- **Recipe:** Obsidian + Nether Brick + Soul Sand — a sturdy, **blast-resistant** arena so the Wither's explosions
  can't blow you into the void or lava. The seed itself consumes **no skulls** — you bring those to build the boss.
- **Structure:** a large enclosed obsidian/nether-brick **bowl** with a soul-sand floor (where you build the Wither),
  a safe perimeter ledge, shroomlight/glowstone lighting, and a charged **Respawn Anchor** in the corner.
- **Why:** the Wither becomes craftable mid-Nether (soul sand from Desert/Soul + skulls from the Fortress), but
  fighting it on a small island over a lava sea is suicide. This is the dedicated, survivable venue.
- **Reward:** Nether Star -> Beacon (the Nether chapter's capstone craft).

---

## Progression sketch

```
Overworld → build a Nether Portal → step through (vanilla drops you on a small obsidian platform; no curated start)
    ↓ Bring your own gear through the portal: fire-resistance potion, blocks, food (the early Nether is lethal)
    ↓ Explore the void to find biomes (Xaero's biome overlay)

Throw overworld seeds straight into the Nether (Tier 1, free):
  Rocky    → netherrack mining (quartz, gold)            → low throws → Ancient Debris → Netherite
  Forest   → fungal forest (crimson: Hoglin food/wood;  warped: Enderman pearls → End gate)
  Mushroom → mushroom island (Mooshroom food — the safe larder)
  Desert   → Soul Sand Valley (soul sand → Wither, Soul Speed)
  Badlands → Basalt Deltas (blackstone, gilded blackstone)
  Aquatic  → Lava Lagoon (Striders, magma cream)
  Lush     → vine grotto;   Ancient → haunted deep (debris)

Craft Tier-2 Nether seeds (Nether materials → enhanced islands):
  Nether Rocky / Crimson / Warped / Soul / Basalt / Lava

Structures (new seeds):
  🏰 Nether Fortress (nether brick) → Blaze Rods, nether wart, wither skeleton skulls
  🏯 Bastion Remnant (blackstone + gold + crying obsidian) → lodestone, top loot
  🐷 Piglin Trading Post (gold + blackstone + netherrack) → fire res, obsidian, ender pearls
  ☠️ Wither Arena (obsidian + nether brick + soul sand) → safe Wither fight → Nether Star → Beacon

Warped Enderman pearls + Fortress Blaze Rods → Eyes of Ender → The End chapter
```

---

## Open questions & implementation notes

Resolved this pass: **Wither Arena** (added as a seed), **Forest vs Mushroom** (Forest = fungal forest, Mushroom =
the mushroom island), **Meadow** (fizzles), **Large vs Tier-2** (Large = bigger Tier-1, Tier-2 = exclusive content).

- **Respawn in the Nether** — Respawn Anchors charge with Glowstone (from Rocky/Wastes). "Charge an anchor before you
  explore" is Nether 101 and doubly true when every island is separated by void — give it a prominent guide entry.
- **Ruined Portal twins (impl)** — whenever a ruined portal is created on either side (seed *or* `rare_structures`
  roll), generate a matching small ruined-portal island on the other side at the vanilla-linked coord
  (`nether = overworld / 8`), nudging only the shortest free distance so it stays in portal-linking range; a twin
  must not spawn its own twin. See *Ruined Portal twins* under World design. (Also still worth confirming the seed
  renders a Nether-appropriate scene — blackstone/basalt surround — when thrown in the Nether.)
- **Mooshrooms in the Nether (impl)** — spawned via the `animals` pack and marked persistent; they won't naturally
  breed-spawn there, so the seed places a starter herd. Verify they don't despawn.

---

*Nether biome list verified against Minecraft 1.21.1 — exactly five biomes (Nether Wastes, Crimson Forest, Warped
Forest, Soul Sand Valley, Basalt Deltas); all five are covered above. Re-verify mob spawn tables and structure loot
table IDs before implementation.*
