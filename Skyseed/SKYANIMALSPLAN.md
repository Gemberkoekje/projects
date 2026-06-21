# Skyseed — Sky Animals Content Plan

This document covers all vanilla overworld passive and neutral mobs, their assignment to existing island types as sprinkles, and the dedicated **Animal Skyseed** island types. It is a content plan — see [README.md](README.md) for architecture and `SKYISLANDSPLAN.md` for block/biome coverage.

---

## Status — partially implemented (v0.2.0, 2026-06-21)

The **mob-spawning capability** and the **sprinkle animals** on existing islands are built. A theme `mobs`
list (`{ "entity", "chance", "count" }`) — at theme, biome-override, and per-variant level — spawns
animals directly when an island finishes generating (any spot with solid ground and two non-blocking
blocks above, so flowers/grass don't prevent it). Applied across Forest, Meadow, Mushroom, Desert,
Frozen, Badlands, and Aquatic (and the Forest biome specials: taiga fox/wolf, jungle parrot, savanna
horse/llama/armadillo, mushroom-fields mooshroom, desert rabbit).

**Built (v0.10.0):** all five dedicated **Animal Islands** — Pasture, Poultry, Wool Farm, Stable, Aquarium.
Each is a jigsaw-placed fenced enclosure plus a guaranteed pack of animals rolled in via the new `animals`
theme field (weighted packs → N adults + N babies; sheep get random wool colours; aquarium life spawns
submerged in the glass tank; the Stable carries a `skyseed:chests/stable` loot chest). See the
[Animal Islands](#animal-islands) section below for per-island detail.

**Not yet built:** 1.21.5 biome mob variants (target is 1.21.1).

(As of v0.2.1, Meadow bee nests are populated with bees — they emerge to pollinate and return home.
As of v0.2.2, **water-spawned mobs** are in via a `pond.water_mobs` list: squid in Aquatic freshwater,
tropical fish on the warm reef, axolotls in Lush pools; **glow squid** need the deep pool of the new
**Large Lush** island — they beach themselves in a small pond.)

---

## Design principles

- **Sprinkle animals appear on generation.** Mobs are spawned server-side when the island germinates, not via the vanilla ambient spawn system. This means they appear regardless of light level or time of day, and the player gets the "catch it before it wanders off" moment on every relevant island.
- **Default Minecraft despawn/wander behavior is unchanged for now.** Passive mobs don't despawn in Java Edition, but they do wander. The void is permanent. This is intentional tension — revisit if playtesting shows it's frustrating rather than exciting.
- **Dedicated Animal Islands are generated terrain + curated structure on top.** The island shape generates normally (teardrop blob, appropriate palette). A small fenced enclosure is then placed on the flattest area of the resulting surface, with 2–4 animals spawned inside it. The fence placement adapts to the generated terrain rather than the terrain being flattened to fit the fence — this keeps island silhouettes interesting while still guaranteeing a functional pen.
- **Animal Island recipes use drops, not the live animal.** You shouldn't need to already have a farm to make the first farm island. Recipes use items the animal produces or drops (wool, egg, raw beef, feather, etc.), gated appropriately by tier.
- **Biome-variant animals follow the island's biome awareness.** Since 1.21.5 (Spring to Life), cows, pigs, and chickens have biome-based visual variants. Sprinkled animals should match the island's rolled biome — a forest island in a savanna biome spawns the savanna-variant cow, etc. This is mostly cosmetic but adds polish.
- **Not every animal needs a dedicated island.** Bats, squids, and tropical fish are atmosphere, not farms. Rabbits and foxes are niche. Dedicated islands are for animals with meaningful, repeatable farm value.

---

## Mob categories

### Tier A — Core farm animals (dedicated island + sprinkle)
Farm value is high enough to justify their own seed.

### Tier B — Valuable but niche (sprinkle only, on thematic islands)
Worth encountering, worth catching, but a dedicated island would feel like padding.

### Tier C — Atmosphere / impractical (sprinkle only, rare or skipped)
Little farm value, awkward to contain, or just ambience. May appear as rare flavor.

### Tier D — Skip entirely
Doesn't fit skyblock context, or is effectively impossible to make interesting as a sky island mob.

---

## Full mob list

---

### 🐄 Cow
**Category:** Tier A  
**Drops:** Raw Beef, Leather, Milk (bucket)  
**Vanilla biome:** Most grassy biomes. Biome variants added in 1.21.5.  
**Sprinkle on:** 🌿 Forest (any variant, 20% chance, 1–2 cows), ✨🌸 Meadow (30% chance, 1–2)  
**Dedicated island:** ✅ **Pasture Island** — see Animal Islands section  
**Notes:** The single most important farm animal for food + leather progression. Should be one of the first dedicated islands available.

---

### 🐖 Pig
**Category:** Tier A  
**Drops:** Raw Porkchop, (Saddle when saddled)  
**Vanilla biome:** Most grassy biomes. Biome variants in 1.21.5.  
**Sprinkle on:** 🌿 Forest (20% chance, 1–2), ✨🌸 Meadow (15% chance, 1–2)  
**Dedicated island:** ✅ **Pasture Island** (shares with Cow and Sheep — see Animal Islands)  
**Notes:** Pork is the most calorie-dense vanilla food. Included in the general Pasture island rather than its own seed.

---

### 🐑 Sheep
**Category:** Tier A  
**Drops:** Raw Mutton, Wool (shearing gives 1–3, killing gives 1)  
**Vanilla biome:** Most grassy biomes.  
**Sprinkle on:** 🌿 Forest (25% chance, 1–2), ✨🌸 Meadow (35% chance, 1–3 — meadow is the natural sheep island)  
**Dedicated island:** ✅ **Pasture Island**  
**Notes:** Wool is critical early — beds, banners, carpets, and the enchanting table all need it. The Meadow island's high sheep chance makes it the de facto early wool source even before a dedicated Pasture island.

---

### 🐔 Chicken
**Category:** Tier A  
**Drops:** Raw Chicken, Feather, Egg (passively over time)  
**Vanilla biome:** Most grassy biomes. Biome variants in 1.21.5.  
**Sprinkle on:** 🌿 Forest (30% chance, 1–3 — chickens are common and small, higher count feels right), ✨🌸 Meadow (20% chance, 1–2)  
**Dedicated island:** ✅ **Poultry Island** — see Animal Islands  
**Notes:** Eggs and feathers are both early progression items (arrows, cakes, pumpkin pie). Chickens are the smallest farm animal and wander less dangerously near edges than cows/pigs — the catch-it-before-it-falls tension is lower, which makes them a good first encounter for new players.

---

### 🐰 Rabbit
**Category:** Tier B  
**Drops:** Raw Rabbit, Rabbit Hide (4 → leather), Rabbit's Foot (rare, potion ingredient)  
**Vanilla biome:** Deserts, snowy biomes, meadows, taigas.  
**Sprinkle on:** 🏜️ Desert (15% chance, 1–2), ✨❄️ Frozen (15% chance, 1–2 — brown/white variants), ✨🌸 Meadow (20% chance, 1–2)  
**Dedicated island:** ❌ No dedicated island — Rabbit's Foot is niche enough not to justify one. Catching a sprinkle rabbit is the gameplay.  
**Notes:** Rabbit hide → leather is a useful early leather alternative on Desert islands before the Pasture island exists. The Rabbit's Foot → Jump Boost potion is a meaningful late-sprinkle reward.

---

### 🦊 Fox
**Category:** Tier B  
**Drops:** Nothing reliably (occasionally drops held item)  
**Vanilla biome:** Taiga, snowy taiga, grove.  
**Sprinkle on:** 🌿 Forest (spruce/taiga variant only, 10% chance, 1)  
**Dedicated island:** ❌ No dedicated island — not farmable in any meaningful sense.  
**Notes:** Foxes are flavor. Seeing one on a spruce island is a delight; trying to build a fox farm is a pointless exercise. Rare enough that catching one feels like a lucky bonus.

---

### 🐺 Wolf
**Category:** Tier B  
**Drops:** Nothing  
**Vanilla biome:** Forests, taigas, and several others in 1.21+. Multiple wolf variants by biome.  
**Sprinkle on:** 🌿 Forest (spruce/taiga and dark oak variants, 10% chance, 1)  
**Dedicated island:** ❌ No — wolves are companions, not farms. One sprinkle wolf per relevant island is the right scope.  
**Notes:** A tamed wolf is a meaningful companion in skyblock where hostile mobs can push you off islands. Getting one from a sprinkle is a memorable moment. Multiple wolf biome variants in 1.21+ mean the spruce island might give a different wolf variant than the dark oak island — nice flavor.

---

### 🐈 Cat
**Category:** Tier B  
**Drops:** String (rare)  
**Vanilla biome:** Villages, swamps.  
**Sprinkle on:** ✨🌊 Aquatic (swamp variant, 15% chance, 1)  
**Dedicated island:** ❌ No — cats are companions. One per swamp island is flavor.  
**Notes:** String → wool is an early alternative resource path. Cats also scare away phantoms and creepers, which has real skyblock utility. Sourcing from swamp flavor feels right — stray cats in a mossy swamp island.

---

### 🐴 Horse
**Category:** Tier B  
**Drops:** Leather (rare)  
**Vanilla biome:** Plains, savannas.  
**Sprinkle on:** ✨🌸 Meadow (10% chance, 1 — a horse on a meadow island is extremely satisfying visually), 🌿 Forest (acacia/savanna variant only, 8% chance, 1)  
**Dedicated island:** ❌ No — horses are transport, and transport in a skyblock of floating islands is of limited value. A horse on a small island is also more comedic than useful.  
**Notes:** The comedic image of a horse on a tiny island is probably worth the 10% chance alone. Leather drop makes them marginally useful to kill if taming isn't a priority.

---

### 🫏 Donkey
**Category:** Tier B  
**Drops:** Leather (rare)  
**Vanilla biome:** Plains, savannas.  
**Sprinkle on:** ✨🌸 Meadow (8% chance, 1)  
**Dedicated island:** ❌ No — chests on donkeys are useful but a skyblock context limits their movement value.  
**Notes:** Same logic as Horse. Donkeys are rarer in vanilla so a lower chance feels right.

---

### 🦙 Llama
**Category:** Tier B  
**Drops:** Leather, Wool (from carpet decoration)  
**Vanilla biome:** Windswept hills, savannas.  
**Sprinkle on:** 🌿 Forest (acacia/savanna variant, 8% chance, 1–2), ⛰️ Rocky (mountain variant, 10% chance, 1–2)  
**Dedicated island:** ❌ No dedicated island — Llamas spit, wander dangerously near edges, and are mostly useful for carpeted decoration.  
**Notes:** A llama on a rocky mountain-variant island is good visual flavor. Their leather drop gives mountain islands a minor bonus resource.

---

### 🐻‍❄️ Polar Bear
**Category:** Tier B (Neutral)  
**Drops:** Raw Cod, Raw Salmon  
**Vanilla biome:** Frozen biomes only.  
**Sprinkle on:** ✨❄️ Frozen (20% chance, 1 — always adult, no cubs to trigger aggro accidentally)  
**Dedicated island:** ❌ No — not breedable, not tameable.  
**Notes:** A polar bear on a frozen island is iconic. The fish drops make it a minor food source. Neutral behavior (only aggressive if you attack it or approach cubs) means a solo adult is safe to ignore.

---

### 🐸 Frog
**Category:** Tier B  
**Drops:** Nothing  
**Vanilla biome:** Swamps, mangrove swamps.  
**Sprinkle on:** ✨🌊 Aquatic (swamp + mangrove variants, 20% chance, 2–3)  
**Dedicated island:** ❌ No — froglight farming requires specific setup; a sprinkle frog is atmosphere and a potential froglight source if the player sets up the infrastructure.  
**Notes:** Three frog variants (temperate, cold, warm) match different aquatic island biome rolls nicely. Frogs on lily pads on a swamp island is peak Minecraft atmosphere.

---

### 🐝 Bee
**Category:** Tier B  
**Drops:** (via hive) Honey, Honeycomb  
**Vanilla biome:** Plains, sunflower plains, flower forests, meadows, cherry groves.  
**Sprinkle on:** ✨🌸 Meadow (bees appear via the bee nest sprinkle already planned — 40% chance of 1–2 nests with 3 bees each, not a direct mob sprinkle)  
**Dedicated island:** ❌ No — bees are already the central mechanic of the Meadow island via nest sprinkles.  
**Notes:** Already covered by SKYISLANDSPLAN.md. Bees don't need a separate animal island entry; the Meadow island *is* the bee island.

---

### 🦅 Parrot
**Category:** Tier C  
**Drops:** Feathers  
**Vanilla biome:** Jungles only.  
**Sprinkle on:** 🌿 Forest (jungle variant only, 5% chance, 1)  
**Dedicated island:** ❌ No — taming requires cookies (which are craftable, but scarce early) and the farming value is minimal.  
**Notes:** A parrot on a jungle island is a lovely rare encounter. 5% keeps it genuinely rare. Not worth building infrastructure around.

---

### 🦔 Armadillo
**Category:** Tier C  
**Drops:** Armadillo Scute (used for Wolf Armor)  
**Vanilla biome:** Savannas, badlands.  
**Sprinkle on:** 🌿 Forest (acacia/savanna variant, 8% chance, 1–2), ✨🌵 Badlands (10% chance, 1–2)  
**Dedicated island:** ❌ No dedicated island — wolf armor is niche; armadillo scutes are a bonus resource.  
**Notes:** Armadillos added in 1.21. Their appearance on a savanna or badlands island is thematically perfect. Scutes are useful if the player has a tamed wolf. Roll into their shell when spooked — good flavor on a high-altitude island.

---

### 🦎 Axolotl
**Category:** Tier C  
**Drops:** Nothing  
**Vanilla biome:** Lush caves (underground water).  
**Sprinkle on:** ✨🌿 Lush (10% chance, 1 — in any water pond on the island)  
**Dedicated island:** ❌ No — axolotls are combat companions for underwater fighting, which is limited in skyblock.  
**Notes:** An axolotl in a lush island pond is a charming detail and a genuinely rare find. Axolotls can be carried in buckets, so a caught one is a meaningful reward even without a dedicated farm context.

---

### 🐢 Turtle
**Category:** Tier C  
**Drops:** Seagrass (when killed as baby → Scute), Bowl  
**Vanilla biome:** Warm beaches.  
**Sprinkle on:** ✨🌊 Aquatic (warm ocean variant only, 10% chance, 1–2)  
**Dedicated island:** ❌ No — turtle scute farming requires breeding and baby turtles reaching adulthood, which is a long loop for a skyblock context.  
**Notes:** Turtles on a coral/warm-ocean island are good atmosphere. Scutes → Turtle Helmet is a meaningful reward for patient players.

---

### 🦑 Squid
**Category:** Tier C  
**Drops:** Ink Sac  
**Vanilla biome:** Any ocean or river (underwater).  
**Sprinkle on:** ✨🌊 Aquatic (freshwater variant, 15% chance, 1–2 — spawned in the water body)  
**Dedicated island:** ❌ No.  
**Notes:** Ink sacs → black dye → book and quill, signs, etc. A squid in the island's central water body is good atmosphere and a minor resource source.

---

### 🦑 Glow Squid
**Category:** Tier C  
**Drops:** Glow Ink Sac  
**Vanilla biome:** Underground water (dark areas).  
**Sprinkle on:** ✨🌿 Lush (10% chance, 1 — in the island water pond, dark underside context)  
**Dedicated island:** ❌ No.  
**Notes:** Glow ink sacs → glowing text on signs and frames. A glow squid in a lush island pond at night is visually striking.

---

### 🦇 Bat
**Category:** Tier D  
**Drops:** Nothing  
**Sprinkle on:** None  
**Notes:** No drops, no farm value, no meaningful interaction. Skipped entirely — they'd just fly off islands immediately anyway.

---

### 🐠 Tropical Fish / Cod / Salmon / Pufferfish
**Category:** Tier C/D  
**Drops:** Raw fish variants  
**Sprinkle on:** ✨🌊 Aquatic (in water body — ambient, not a deliberate sprinkle)  
**Notes:** Fish spawn naturally in water bodies in vanilla. The Aquatic island's central water pool should produce ambient fish just from existing as a water body in the right biome — no explicit sprinkle needed. Pufferfish are a meaningful ingredient (Water Breathing potion) but are sourced by fishing, not farming.

---

### 🐌 Snail / 🪲 Firefly *(if added post-1.21.1)*
**Category:** Depends on update  
**Notes:** Minecraft has teased further small creature additions. Slot them into Meadow (firefly) or Forest/Lush (snail) when confirmed.

---

## Animal Islands

Dedicated seeds producing a small island with a fenced enclosure and guaranteed animals inside.

---

### 🌾 Pasture Island ✨

**Character:** A gentle rolling grass island with a small wooden fence enclosure containing 2–3 farm animals. The most important early animal island — covers the three core grass-biome farm animals in one seed. The enclosure is placed on the flattest generated surface area; fence posts adapt to terrain slope rather than requiring a flat pad.

**Unlocked by:** Raw Beef (from a cow sprinkle on Forest/Meadow) OR Wool (from a sheep sprinkle).

**Recipe (two variants, same result):**
- Wool + Oak Planks + Dirt (shapeless)
- Raw Beef + Oak Planks + Dirt (shapeless)

*(Two unlock paths so whichever animal the player catches first opens this tier.)*

**Palette:** Grass Block surface, Dirt fill, Stone core. Identical to Forest palette — this is intentionally a plain, approachable island.

**Island shape:** Medium (radius 6–8), low rim noise, flat-ish top to give the fence room. No pond.

**Structure — the fence enclosure:**
- A roughly 5×5 to 7×7 fenced area placed on the highest flat cluster of blocks.
- Oak fence + oak fence gates (2 gates on opposite sides).
- One hay bale inside (decoration + breeding trigger item hint).
- One water trough (cauldron with water, or just a water source block in a 1-block pit).

**Animals spawned inside the enclosure:**

| Roll | Animals | Count |
|---|---|---|
| 40% | 2 Cows | 1 adult + 1 baby |
| 30% | 2 Sheep (random wool colors) | 1 adult + 1 baby |
| 20% | 2 Pigs | 1 adult + 1 baby |
| 10% | Mixed — 1 Cow + 1 Sheep | Both adult |

**Biome awareness:** Animal variants follow the island's germination biome (1.21.5 variants). A Pasture island grown in a savanna biome spawns savanna-variant cows.

**Decoration:**
- Grass, dandelion, poppy (sparse — this is a grazed field, not a wildflower meadow)
- Oak tree (1, near the edge — shade, not clutter)

**Ore table:** None.

---

### 🐔 Poultry Island ✨

**Character:** A smaller, scrappier island than the Pasture — more cobbled-together, like a farmyard corner. Features a chicken coop structure (a small enclosed area with a roof) rather than an open fence, since chickens can jump fences in vanilla.

**Unlocked by:** Feather (from a chicken sprinkle on Forest) + Egg.

**Recipe:** Feather + Egg + Dirt (shapeless)

**Palette:** Grass Block surface, Dirt fill, Stone core.

**Island shape:** Small-medium (radius 5–7), relatively flat top.

**Structure — the chicken coop:**
- 3×3 to 4×4 enclosed structure, oak planks walls, slab roof (half-height so it feels like a coop, not a house).
- Oak fence gate entrance.
- 1–2 hay bales inside.
- Composter inside (chickens + seeds → composting loop hint).

**Animals spawned inside:**
- Always: 3–4 Chickens (mix of adult and baby)

**Biome awareness:** Chicken variants follow germination biome (1.21.5).

**Decoration:** Scattered seeds/wheat on the surface nearby (decorative, using grass/fern placement).

**Ore table:** None.

---

### 🐑 Wool Farm Island ✨

**Character:** A mid-tier dedicated sheep island with a larger enclosure and multiple sheep in a range of wool colors. Primary purpose: renewable wool in all colors without needing dyes. The island is slightly larger and hillier than the Pasture — sheep on a rolling hillside.

**Unlocked by:** Multiple Wool colors OR Shears (iron, which comes from Rocky).

**Recipe:** 2× Wool (different colors) + Iron Ingot + Dirt

**Palette:** Grass Block, Dirt, Stone. Podzol patches (sheep graze the grass down in vanilla — the podzol mimics a well-used pasture).

**Island shape:** Medium-large (radius 7–9), moderate rim noise, gentle hills.

**Structure — the sheep pen:**
- Larger open pen, 8×8 to 10×10, oak fence.
- Split into 2 sections by an internal fence line (organization hint for color separation).
- Automatic grass-regrowing mechanic works naturally — sheep eat grass, grass grows back.

**Animals spawned:**
- 4–6 Sheep, wool colors weighted toward natural vanilla spawn rates but with a guaranteed color spread — at least 2 different colors, small chance of rare colors (light blue, pink, purple, lime).
- 1% chance of a naturally pink sheep (the rarest vanilla sheep).

**Decoration:** Slightly hillier surface, some bare dirt patches (grazed areas), sparse flowers.

**Ore table:** None.

---

### 🐴 Stable Island ✨

**Character:** A mid-late island. Plains aesthetic, slightly larger than Pasture. A wooden stable structure (3 stalls) with horses, donkeys, or a mule inside. Useful primarily as a source of leather and saddles (via chest loot), and for players who want mounted travel between large islands.

**Unlocked by:** Leather (from Cow or Horse kills) + Saddle is not craftable, so: Leather + Gold Ingot + Iron Ingot (a recipe that alludes to horse equipment without requiring a saddle you don't have yet).

**Recipe:** Leather + Gold Ingot + Oak Planks

**Palette:** Grass Block, Coarse Dirt (stable floor), Stone.

**Island shape:** Medium-large (radius 7–10), flatter than most.

**Structure — the stable:**
- 3-stall stable structure, oak planks + oak fence dividers, hay bale floors.
- Each stall has a fence gate.
- Small chest inside with a low chance of a Saddle or Horse Armor (this is the main reason to make this island beyond aesthetics).

**Animals spawned:**

| Roll | Animals |
|---|---|
| 50% | 2–3 Horses (random colors/markings) |
| 30% | 1 Horse + 1 Donkey |
| 15% | 2 Donkeys |
| 5% | 1 Mule (rare — mules require breeding a horse + donkey, so a natural mule is a treat) |

**Decoration:** Oak trees, coarse dirt path leading to stable entrance, hay bales outside.

**Ore table:** None. The chest inside is the only loot element.

---

### 🌊 Aquarium Island ✨

**Character:** A late-tier island that leans heavily into the Aquatic theme — essentially a large above-water aquarium with a glass viewing area. Turtles, axolotls, tropical fish, and squid in a well-lit water environment. More of an aesthetic/collector island than a farm island, though turtle scutes and ink sacs have practical value.

**Unlocked by:** Turtle Scute (rare, from Aquatic island sprinkle turtle babies reaching adulthood) OR Nautilus Shell (fishing).

**Recipe:** Turtle Scute + Prismarine Shard + Sand

**Palette:** Sand surface, Sandstone fill, Stone core. Water feature (large, central).

**Island shape:** Medium (radius 6–8), bowl-shaped top to hold water naturally.

**Structure:**
- Large central water basin (8–10 blocks across, 3 deep).
- Glass walls on 2 sides for viewing (the aquarium aesthetic).
- Sea lanterns for underwater lighting.
- Coral decoration inside the water.

**Animals spawned in the water:**
- 2 Turtles (surface)
- 1–2 Axolotls (underwater, random colors)
- Ambient tropical fish (via vanilla water spawning)
- 1 Squid or Glow Squid (50/50)

**Decoration:** Sea lanterns, coral fans, kelp, lily pads on water surface edges.

**Ore table:** None.

---

## Mob coverage summary

All overworld passive and neutral mobs accounted for:

| Mob | Tier | Sprinkle island(s) | Dedicated island |
|---|---|---|---|
| Cow | A | Forest, Meadow | Pasture |
| Pig | A | Forest, Meadow | Pasture |
| Sheep | A | Forest, Meadow | Pasture, Wool Farm |
| Chicken | A | Forest, Meadow | Poultry |
| Rabbit | B | Desert, Frozen, Meadow | — |
| Fox | B | Forest (spruce) | — |
| Wolf | B | Forest (spruce/dark oak) | — |
| Cat | B | Aquatic (swamp) | — |
| Horse | B | Meadow, Forest (acacia) | Stable |
| Donkey | B | Meadow | Stable |
| Llama | B | Forest (acacia), Rocky (mountain) | — |
| Polar Bear | B | Frozen | — |
| Frog | B | Aquatic (swamp/mangrove) | — |
| Bee | B | Meadow (via nest sprinkle) | — |
| Parrot | C | Forest (jungle) | — |
| Armadillo | C | Forest (acacia), Badlands | — |
| Axolotl | C | Lush | Aquarium |
| Turtle | C | Aquatic (warm) | Aquarium |
| Squid | C | Aquatic (freshwater) | Aquarium |
| Glow Squid | C | Lush | Aquarium |
| Tropical Fish | C | Aquatic (ambient) | Aquarium |
| Bat | D | — | — |
| Mule | — | — | Stable (rare roll) |

**Neutral mobs not sprinkled (hostile-adjacent):**
- **Enderman** — appears naturally in any biome at night; no need to sprinkle, and a skyblock void is already a dangerous enderman environment.
- **Spider** — spawns at low light; will appear on any unlit island at night without needing a sprinkle.
- **Bee** (neutral) — covered by Meadow nest mechanic.
- **Iron Golem** — player-constructed or village-spawned; not applicable.
- **Panda** — jungle only, very rare, not breedable without bamboo surplus; skip for now.
- **Goat** — mountain biome neutral mob; could be a Rocky (mountain variant) sprinkle but has limited farm value. Flag as a future addition.

---

## Progression sketch

```
Forest/Meadow sprinkle
    ↓ Chicken (feather + egg) → 🐔 Poultry Island
    ↓ Cow (raw beef) OR Sheep (wool) → 🌾 Pasture Island
    ↓ Multiple wools + iron → 🐑 Wool Farm Island
    ↓ Leather + gold → 🐴 Stable Island

Aquatic/Lush sprinkle
    ↓ Turtle Scute OR Nautilus Shell → 🌊 Aquarium Island
```

Animal islands are a mid-game branch, not a prerequisite for anything — they're quality-of-life upgrades (guaranteed farm starts) for players who caught a lucky sprinkle animal and want to industrialize. The sprinkle catch-it-before-it-falls moment remains the primary encounter; the dedicated island is the reward for players who build on that foundation.

---

## Implementation notes

- **Mob spawning on island generation:** Spawned via `ServerLevel.addFreshEntity()` at generation time, positioned inside the enclosure area for Animal Islands, or at a random surface position for sprinkles. Position must be validated (solid block below, air above × 2).
- **Fence enclosure placement:** Find the flattest N×N surface cluster on the generated island using a scan of the top surface layer, then place the fence structure centered on that cluster. No terrain flattening — the fence posts adapt by checking the surface block at each column and placing the fence post on top of it, allowing gentle slopes inside the pen.
- **The `mob_spawns` field** (proposed addition to the theme codec): `[{ "entity": "minecraft:cow", "chance": 0.20, "count": { "min": 1, "max": 2 } }]`. Processed after block placement, before the island is considered complete.
- **Animal Island structure placement** is a separate code path from the main generator — closer to the "curated start island" path than the procedural island path. The terrain generates procedurally; the structure is placed on top deterministically based on where the terrain came out.
- **Biome variants (1.21.5):** The Spring to Life variants for cows, pigs, and chickens are applied by checking `level.getBiome(spawnPos)` and selecting the appropriate variant entity type at spawn time.

---

*Mob list verified against Minecraft 1.21.1. Note: biome-based animal variants (cow, pig, chicken) were added in 1.21.5 — verify availability on your target version before implementing variant-specific spawning.*
