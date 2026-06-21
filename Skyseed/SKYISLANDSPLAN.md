# Skyseed — Sky Islands Content Plan

This document covers all planned island types, their blocks, variants, Y-level ore distribution, and recipe sketches. It is a content plan, not a technical plan — see [README.md](README.md) for architecture, the data model, and project status.

---

## Implementation status (2026-06-21)

> This section reconciles the plan below with what is actually built. Everything after it is the **design target**; only a subset is live, and the build made one structural choice that reframes much of what follows.

**Seeds built:** Forest (`skyseed:forest`), Large Forest (`skyseed:forest_large` — the milestone-7 "bigger, pricier" size variant of Forest), and Rocky (`skyseed:rocky`). Everything else in this document (Desert, Mushroom, Frozen, Meadow, Aquatic, Ancient, Badlands, Lush) is **not yet built**.

**Engine features built:** datapack theme codec; biome-aware generation; Y-band overrides (`min_y`/`max_y`); contained ponds; off-rim waterfall cascades; hand-built mangroves (`skyseed:mangrove`); per-column `surface_scatter`; snow peaks; curated start island; void world preset with multi-noise biomes; structures disabled; tick-budgeted placement; overlap safety; charge-to-throw distance; Patchouli guide (Forest / Rocky / Large Forest entries, advancement-gated).

**Structural divergence — biome overrides, not random variants.** The biggest gap between this doc and the build: biome response is delivered by **`biome_overrides` keyed to the germination biome** (deterministic — a Forest seed thrown over savanna *always* rolls acacia, over jungle *always* jungle), not by the random "weighted variants + biome weight-nudge" model the per-island tables below describe. Weighted variants still exist, but **nested inside** an override (e.g. the `#is_forest` catch-all rolls oak/birch). Read the "Variants (weighted)" tables below as *intended within-biome variety*; in the build the **biome is the primary selector**.

Consequently, several island TYPES planned here as their own seeds currently exist only as **Forest biome-overrides**: Mushroom (`mushroom_fields` → mycelium + huge mushrooms), the Aquatic-ish water islands (`#is_ocean`/`#is_river` → lake + pond + waterfalls; `mangrove_swamp` → mud + mangroves), and snowy flavor (`grove`/snowy biomes → spruce + snow). Promoting these to dedicated seeds with richer palettes and decoration is future work.

**Theme structure as built.** A theme JSON has top-level keys: `shape`, `palette` (which holds `surface` / `fill` / `core` / `fill_depth` / `surface_scatter`), `ores`, `variants`, an optional `pond`, and an optional **`biome_overrides`** list. Each entry in `biome_overrides` is a `BiomeOverride` with these (all-optional) fields: `biomes`, `min_y`, `max_y`, `surface`, `fill`, `core`, `fill_depth`, `surface_scatter`, `shape`, `ores`, `variants`, `pond`, `waterfalls`. An override *replaces* the base theme's field where present, and *matches* when both its biome list (empty = any biome) and its `min_y`/`max_y` range pass — first match wins.

---

## Design principles

- **Every naturally generated overworld block has a home.** All vanilla overworld blocks appear on at least one island type. New island types were added where no existing type was a sensible fit.
- **Variants, not separate islands.** Different wood types, flower types, and biome flavors are *flavors* within a theme, not separate seeds. A Forest Skyseed can produce oak, birch, spruce, jungle, dark oak, or acacia — one seed, unbounded visual variety. *(As built this is driven primarily by the germination biome via `biome_overrides`, with weighted variants nested inside — see Implementation status.)*
- **Y level gates ore value.** On *mining* islands the same type thrown high versus low produces meaningfully different ore content. High islands are accessible early; low islands are a deliberate investment and risk. *Mining islands only (Rocky, planned Ancient) — see Y-level system for the deliberate scope.*
- **Biome awareness.** Where it makes sense, the island's surface/decoration responds to the biome the seed germinates over. A Forest Skyseed thrown over a savanna becomes acacia; over a jungle, jungle. Rocky mostly doesn't care — it's a constructed thing — except a snowy biome (or a very high throw) gives it a snow-capped peak.
- **Sprinkles unlock tiers.** Rare blocks appear as low-chance sprinkles in common islands. Getting enough snow from Rocky peaks unlocks the Frozen seed. Getting clay from Aquatic islands unlocks the next tier. No custom recipes needed.

---

## Y-level system

The `y_level` field in each ore/feature entry defines where on the island the block prefers to appear, mirroring vanilla depth logic — but more importantly, the ore *tables themselves* vary based on the **Y coordinate the island is thrown at**. High throw → island generates high → high-Y ore table. Low throw → low-Y ore table.

### Y bands (rough)

| Band | Y range | Character |
|---|---|---|
| Sky High | Y 150+ | Early game accessible. Coal, iron, copper. |
| Mid | Y 80–149 | Standard play. Gold, iron, copper, small redstone chance. |
| Deep | Y 20–79 | Requires investment to reach/throw low. Gold, redstone, lapis, small diamond. |
| Abyss | Y < 20 | Late game. Diamond, emerald, full redstone/lapis. High risk (fall damage, darkness). |

**Y-gating is deliberately not universal.** It carries real narrative weight only on the *mining* islands — **Rocky** and (planned) **Ancient** — so those get the full Y-banded ore tables. The nature islands (**Forest, Meadow, Lush**) get **no** Y-gating: a forest is a forest at any altitude, and bolting ore-depth logic onto them would be mechanics for their own sake. Other islands get at most a **minor cosmetic Y touch** where it genuinely reads — a snow cap on a high **Rocky** peak — and several (e.g. **Desert**) get nothing at all. This is intentional: Y matters where "how deep did you dig" is the fantasy, and nowhere else. Don't force it onto islands where it makes no narrative sense.

### Rocky Y-bands, as built

The live Rocky implementation uses germination-Y override bands rather than the four-band table above (which stays the fuller design target). Current bands — note the value gradient matches the intent (low = rare/valuable, high = common):

| Germination Y | Result |
|---|---|
| ≤ 8 | **Deepslate island** (deepslate / cobbled deepslate) — diamond, redstone, lapis, gold, iron in deepslate form; rares common |
| 9–69 | Stone — iron, copper, coal, gold, lapis |
| ≥ 70 | Stone — coal- and iron-heavy (+ rare emerald) |
| ≥ 130 *or* a snowy biome | **Snow-capped peak** (snow surface, taller/pointier silhouette; sparse coal/iron/emerald) |

The snow-cap trigger as built is **Y ≥ 130 or a snowy biome**, not the plan's original "radius > 8 AND Y > 100."

---

## Island types

---

### 🌿 Forest

**Character:** The starter island and most common theme. Grass surface, dirt fill, stone core. Heavily variant-driven — the same seed produces very different islands depending on biome and roll.

**Recipe:** Dirt + Oak Planks (checkerboard)
```
P D
D P
```

**Palette:**
- Surface: Grass Block (variant override: Podzol for mega spruce, Coarse Dirt for dark oak)
- Fill: Dirt
- Core: Stone

**Variants (weighted):**

| Weight | Name | Surface override | Trees | Extra decoration |
|---|---|---|---|---|
| 5 | Oak | — | Oak | Dandelion, poppy, grass |
| 3 | Birch | — | Birch | Dandelion, grass |
| 3 | Spruce/Taiga | Podzol patches | Spruce | Fern, sweet berry bush, small mushroom sprinkle |
| 2 | Dark Oak | Coarse Dirt patches | Dark Oak | Mushroom sprinkle, vine |
| 2 | Acacia | — | Acacia | Tall grass, biome-aware (savanna tag preferred) |
| 1 | Jungle | — | Jungle tree, jungle bush | Bamboo, melon, vine, cocoa |

**Biome awareness:** Acacia variant gets +3 weight when biome has `#minecraft:is_savanna` tag. Jungle variant gets +2 weight when biome has `#minecraft:is_jungle` tag.

**Sprinkles:**
- Small chance of a pumpkin patch (any variant)
- Tiny chance of a bee nest in oak/birch variants (links to Meadow progression)
- Snow on grove / snowy-biome variants — from the biome override, **not** Y (nature islands aren't Y-gated)
- **Azalea: low chance (~3–5%), any variant.** The canonical *first* azalea, encountered well before the player would think to craft a Lush seed. This is the locked-in fix for the Lush azalea bootstrap (azalea otherwise comes only from Lush). Thematically fine — azalea marks lush caves in vanilla, so a rare surface sprig reads naturally. *As built: 3% azalea on the temperate-forest variants (base oak, the `#is_forest` oak/birch catch-all, and plains) in `forest.json` + `forest_large.json` — kept off the snowy/desert/beach/water variants where it would look out of place.*
- **Red + Brown Mushroom: low chance on the dark-oak and spruce variants.** Mirrors vanilla (mushrooms spawn in dark forest and taiga). This is the locked-in unlock path for the Mushroom seed, which needs both mushrooms. *As built: `dark_forest` and the `#is_taiga` (spruce) override drop brown (4%) / red (2%) mushrooms in `forest.json` + `forest_large.json`; the snow-covered spruce variants (grove / snowy) deliberately skip them.*

**Ore table:** Minimal — this is not a mining island.
- Coal Ore: 20% chance, 1–2 pieces, near core surface

---

### ⛰️ Rocky

**Character:** The primary mining island. Stone surface, gravel patches, no trees. All overworld ores present, gated by Y level. A Rocky island thrown high (or over a snowy biome) generates a snow-capped peak — the first source of snow.

> **As built:** the Y-banded ore tables and the snow peak (Y ≥ 130 or snowy biome) are live. **Not yet built:** the granite/diorite/andesite surface variants, the Mountain variant (emerald + taller silhouette), rare lava pools, and gravel surface patches. Base Rocky currently has no decorative variants (`variants: []`).

**Recipe:** Stone + Cobblestone (checkerboard)
```
S C
C S
```

**Palette:**
- Surface: Stone (with granite/diorite/andesite patches, ~15% surface exposure each)
- Fill: Cobblestone
- Core: Stone

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 4 | Standard | Plain rocky island |
| 2 | Granite | Surface dominated by granite |
| 2 | Diorite | Surface dominated by diorite |
| 2 | Andesite | Surface dominated by andesite |
| 1 | Mountain | Taller silhouette, emerald ore in deep core, snow cap if large |

**Biome awareness:** Mountain variant gets +2 weight when biome has `#minecraft:is_mountain` tag.

**Sprinkles:**
- Snow-capped peak when Y ≥ 130 **or** a snowy biome (as built — the snow source that unlocks Frozen seeds)
- Small lava pool (rare, 5%) — with water contact produces obsidian *(not yet built)*

**Ore table by Y band:**

| Ore | Sky High (150+) | Mid (80–149) | Deep (20–79) | Abyss (<20) |
|---|---|---|---|---|
| Coal | 80%, 4–8 | 60%, 3–6 | 30%, 2–4 | 15%, 1–3 |
| Iron | 95%, 4–6 | 95%, 4–8 | 70%, 3–6 | 50%, 2–4 |
| Copper | 70%, 3–6 | 60%, 2–5 | 30%, 1–3 | 10%, 1–2 |
| Gold | 5%, 1–2 | 20%, 1–3 | 40%, 2–5 | 30%, 2–4 |
| Redstone | — | 10%, 1–3 | 50%, 3–6 | 70%, 4–8 |
| Lapis | — | 5%, 1–2 | 30%, 2–4 | 50%, 3–6 |
| Diamond | — | 1%, 1 | 5%, 1–2 | 15%, 1–3 |
| Emerald | — | — | 3% (mountain only), 1 | 8% (mountain only), 1–2 |

Vein size 1–3 for all unless noted. Deepslate variants replace stone equivalents below the island's midpoint.

---

### 🏜️ Desert

**Character:** Arid surface, sandstone fill. Primary source of sand, sandstone, glass materials, and cactus. Clay sprinkle avoids needing a separate early-game aquatic source.

**Recipe:** Sand + Sandstone (vertical)
```
A _
S _
```
*(A = Sand, S = Sandstone — or any 2-tall shaped recipe)*

**Palette:**
- Surface: Sand
- Fill: Sandstone
- Core: Smooth Sandstone

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 4 | Standard | Flat-ish, cactus, dead bush |
| 2 | Dune | More rounded silhouette, higher rim noise |
| 1 | Red | Red Sand surface, Red Sandstone fill — Badlands-adjacent feel without the full Badlands theme |

**Decoration:**
- Cactus (scattered, density 0.08)
- Dead Bush (density 0.12)
- Sugar cane along any water-adjacent edge (if water feature present)

**Sprinkles:**
- Clay: 40%, 2–4 pieces near core — early clay source before Aquatic island
- Fossil (bone block cluster): 5%, 3–8 bones — rare treat

**Ore table:** Minimal — not a mining island.
- Gold Ore: 10%, 1–2 (vanilla desert gold association)

---

### 🍄 Mushroom

**Character:** Mycelium surface, huge mushrooms, no hostile mobs spawn (vanilla mycelium rule). A safe island. Primary source of mushroom blocks, mycelium, and mooshroom spawning.

**Unlocked by:** Red + Brown Mushroom, both of which sprinkle onto the dark-oak and spruce **Forest** variants (see Forest → Sprinkles) — the same biomes mushrooms grow in vanilla. No Mushroom island is required to craft the first one. *Note: a Mushroom flavor also already exists in the build as a Forest biome-override (`mushroom_fields` → mycelium + huge mushrooms); this entry is the planned dedicated seed.*

**Recipe:** Dirt + Red Mushroom + Brown Mushroom (shapeless, or shaped with mushrooms flanking dirt)

**Palette:**
- Surface: Mycelium
- Fill: Dirt
- Core: Stone

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 3 | Red dominant | Mostly red huge mushrooms |
| 3 | Brown dominant | Mostly brown huge mushrooms |
| 2 | Mixed | Both types, denser |
| 1 | Giant | One very large brown mushroom, smaller island otherwise |

**Decoration:**
- Huge Red Mushroom (density 0.15)
- Huge Brown Mushroom (density 0.15)
- Small mushrooms (floor scatter, density 0.2)
- Mushroom Stem exposed on underside

**Sprinkles:** None — Mushroom island is a biome destination, not a resource sprinkler.

**Ore table:** None. The value here is mycelium spread, mob-free building space, and mushroom stew sustainability.

---

### ❄️ Frozen ✨

**Character:** Snow and ice surface. Cold, treacherous (powder snow pockets), but contains packed ice and rare blue ice — both high-value materials unavailable elsewhere.

**Unlocked by:** Collecting snow from Rocky island peaks.

**Recipe:** Snow Block + Ice (checkerboard)
```
N I
I N
```

**Palette:**
- Surface: Snow Block (with Snow Layer on top)
- Fill: Snow Block / Packed Ice bands
- Core: Stone (with ice intrusions)

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 3 | Snowy | Snow surface, ice pockets, spruce trees |
| 2 | Glacier | Packed ice dominant, very little decoration, stark |
| 1 | Deep Freeze | Blue ice surface patches, powder snow traps, no trees |

**Decoration:**
- Spruce trees (snowy variant only, density 0.12)
- Snow layers on everything
- Powder snow pockets (hidden traps — intentionally dangerous, density 0.08 in non-glacier variants)
- Ice Spike feature (rare, glacier variant)

**Sprinkles:**
- Packed Ice: always present, 6–12 blocks in fill band
- Blue Ice: 15%, 2–4 blocks, deep core only

**Ore table:**
- Iron Ore: 60%, 2–4 (same Y-band logic as Rocky, but reduced — this isn't a mining island primarily)
- Coal Ore: 40%, 2–4

---

### 🌸 Meadow ✨

**Character:** The flower and bee island. Dense flower coverage, cherry trees, high bee nest chance. Primary source of honey, honeycomb, wax, and all dye-producing flowers. Visually the most colorful island.

**Unlocked by:** Any flower (available from Forest island decoration).

**Recipe:** Dirt + any Flower (shapeless)

**Palette:**
- Surface: Grass Block
- Fill: Dirt
- Core: Stone

**Variants (weighted):**

| Weight | Name | Surface override | Trees | Flowers |
|---|---|---|---|---|
| 3 | Wildflower | — | Oak (sparse) | All temperate flowers, mixed |
| 2 | Sunflower | — | None | Sunflower dominant, tall flowers |
| 2 | Tulip | — | Oak (sparse) | Tulips (all 4 colors) |
| 1 | Cherry | Pink Petals patches | Cherry | Pink Petals, cherry blossoms |
| 1 | Cornflower | — | None | Cornflower dominant (blue dye source) |

**Decoration:**
- All temperate flowers at high density (0.35+) — this island should feel overwhelmingly floral
- Bee Nests: 40% chance, 1–2 per island (the reason to make this island)
- Tall flowers (lilac, rose bush, peony, sunflower) in sunflower variant

**Sprinkles:** None — value is the flowers and bees themselves.

**Ore table:** None.

---

### 🌊 Aquatic ✨

**Character:** A water-body island — part floating land, part floating lake. Primary source of clay, kelp, seagrass, coral, lily pads, and mangrove wood. Two distinct sub-identities: freshwater and warm ocean.

**Unlocked by:** Clay (sprinkle from Desert island) + Sand or Gravel.

**Recipe:** Clay + Sand (horizontal)
```
C S
_ _
```

**Palette:**
- Surface: varies by variant (grass, sand, or mud)
- Fill: Dirt / Clay bands
- Core: Stone
- Water: fills the central depression

**Variants (weighted):**

| Weight | Name | Surface | Trees | Water features |
|---|---|---|---|---|
| 3 | Freshwater | Grass + clay banks | — | Kelp, seagrass, lily pads, sugar cane edges |
| 2 | Mangrove | Mud surface | Mangrove | Mangrove roots in water, muddy mangrove roots |
| 2 | Swamp | Grass + mud patches | Oak (vine-draped) | Blue orchid, lily pads, seagrass |
| 1 | Warm Ocean | Sand surface | — | Coral (all types), sea pickle, warm water feel |

**Decoration:**
- Water body at center (always — this is the defining feature)
- Lily Pads (freshwater/swamp, density 0.2 on water surface)
- Kelp (freshwater/warm, underwater)
- Coral Blocks + Fans (warm variant, dense)
- Blue Orchid (swamp variant)
- Vines draping from oak canopy (swamp variant)

**Sprinkles:**
- Sponge: 3%, 1 block (warm variant only) — extremely rare, high value
- Obsidian: 5% if a lava source generates and contacts water

**Ore table:**
- Clay: always, 8–16 blocks in fill band (this is the point)
- Gravel: always, 4–8 in bed

---

### 🏛️ Ancient ✨

**Character:** The deep/cave island. Deepslate, tuff, moss, amethyst geode pockets, dripstone stalactites, sculk. Feels like a piece of the deep underground suspended in sky. High value ores in deepslate form, plus sculk components for redstone.

**Unlocked by:** Deepslate + Cobbled Deepslate — both from a deep-thrown Rocky island (Y ≤ 8). **No Lush dependency.** Ancient is the *gateway* that introduces moss into the progression: it generates a moss surface, and that moss is what later unlocks Lush. (This resolves the Lush ↔ Ancient moss deadlock — see Progression sketch.)

**Recipe:** Deepslate + Cobbled Deepslate *(parallels Rocky's stone+cobblestone; if Ancient should demand actually reaching the abyss, swap one ingredient for an abyss-Rocky drop such as Redstone)*

**Palette:**
- Surface: Moss Block / Deepslate mix
- Fill: Tuff / Deepslate
- Core: Deepslate

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 3 | Deep Cave | Moss surface, dripstone features, stalactites on underside |
| 2 | Geode | Amethyst block cluster, calcite band, budding amethyst inside |
| 1 | Deep Dark | Sculk surface, sculk veins, sculk features — dangerous |

**Decoration:**
- Moss Carpet (dense, surface)
- Pointed Dripstone stalactites hanging from underside (deep cave variant) — visually spectacular
- Dripstone Block clusters
- Glow Berries / Cave Vines on underside (rare, beautiful)
- Amethyst Clusters growing from Budding Amethyst (geode variant)
- Sculk Vein (deep dark variant surface)

**Sprinkles:**
- Sculk Catalyst: 20% (deep dark variant only) — XP farm potential
- Sculk Sensor: 15% (deep dark variant only) — redstone component
- Sculk Shrieker: 3% (deep dark variant only) — risk/reward; makes a big Ancient seed a meaningful decision

**Ore table by Y band** (deepslate variants throughout):

| Ore | Sky High (150+) | Mid (80–149) | Deep (20–79) | Abyss (<20) |
|---|---|---|---|---|
| Deepslate Coal | 40%, 2–4 | 30%, 1–3 | 20%, 1–2 | 10%, 1 |
| Deepslate Iron | 60%, 3–5 | 60%, 3–6 | 50%, 2–5 | 30%, 1–3 |
| Deepslate Copper | 50%, 2–4 | 40%, 2–4 | 20%, 1–2 | — |
| Deepslate Gold | 10%, 1–2 | 15%, 1–3 | 30%, 2–4 | 25%, 1–3 |
| Deepslate Redstone | — | 15%, 2–4 | 60%, 3–6 | 80%, 4–8 |
| Deepslate Lapis | — | 10%, 1–2 | 40%, 2–5 | 60%, 3–6 |
| Deepslate Diamond | — | 3%, 1 | 8%, 1–2 | 20%, 1–3 |

Ancient gives slightly better odds than Rocky at equivalent Y bands — reward for the harder unlock path.

---

### 🌵 Badlands ✨

**Character:** Red sand surface, banded terracotta layers in the fill — orange, yellow, white, red, brown cycling through the island's vertical cross-section, like a mesa cliff translated into a floating island. Visually the most striking island when viewed from the side.

**Unlocked by:** Red Sand (Desert red variant sprinkle) + any Terracotta.

**Recipe:** Red Sand + Terracotta

**Palette:**
- Surface: Red Sand / Terracotta (orange)
- Fill: Banded terracotta (cycling: Orange → Yellow → White → Light Gray → Red → Brown)
- Core: Stone / Red Sandstone

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 3 | Mesa | Classic banded terracotta, flat-ish top |
| 2 | Eroded | More jagged silhouette, exposed terracotta bands on surface |
| 1 | Wooded | Dark Oak trees on top (vanilla wooded badlands), coarse dirt surface patches |

**Decoration:**
- Dead Bush (density 0.1)
- Cactus (sparse, density 0.04)
- Dark Oak trees (wooded variant only)
- Exposed terracotta band cross-sections visible on island sides — this is the main visual feature, built into the layered fill algorithm

**Sprinkles:**
- Gold Ore: 25%, 2–4 (vanilla badlands have extra gold — preserve this)
- Mineshaft fragment: 5% (a few oak planks/fence/rail pieces embedded in the core) — flavour

**Ore table:**
- Gold: always, 2–5 (Badlands' signature)
- Redstone: 20%, 2–4 (mid-depth)

---

### 🌿 Lush ✨

**Character:** The cave garden island. Moss, azalea trees, dripleaves, spore blossoms on the underside, glow berries trailing off the bottom. The most visually unique island — it grows *downward* as much as upward. Primary source of all lush cave blocks and the two sniffer plants.

**Unlocked by:** Moss Block (from Ancient island — Ancient is the gateway; see its entry) + Azalea. ✅ **Azalea bootstrap resolved:** the first azalea comes from a low-chance **Forest** sprinkle (see Forest → Sprinkles), so Lush never depends on a prior Lush. (Villager trade remains a fallback.)

**Recipe:** Moss Block + Azalea

**Palette:**
- Surface: Moss Block
- Fill: Rooted Dirt / Dirt mix
- Core: Stone (with moss intrusions)

**Variants (weighted):**

| Weight | Name | Notes |
|---|---|---|
| 3 | Garden | Dense moss, dripleaves, azalea trees, hanging roots |
| 2 | Glow | Cave vines with glow berries covering the underside — magical at night |
| 1 | Ancient Garden | Moss + tuff mix, feels older, spore blossoms dominant |

**Decoration:**
- Azalea Tree (flowering + plain, density 0.15)
- Moss Carpet (dense, surface, density 0.5)
- Big Dripleaf (density 0.2)
- Small Dripleaf (density 0.15)
- Hanging Roots (dangling from soil)
- Spore Blossom (underside, density 0.1) — floats upward particles
- Cave Vines + Glow Berries (underside, glow variant density 0.3, others 0.05) — the signature visual
- Vine (sides)

**Sprinkles:**
- Torchflower: 10%, 1–2 — sniffer plant, high value
- Pitcher Plant: 8%, 1–2 — sniffer plant

**Ore table:** None. Lush island value is entirely in the surface blocks and decoration.

---

## Block coverage summary

All naturally generated overworld blocks accounted for:

| Category | Covered by |
|---|---|
| Stone, granite, diorite, andesite | Rocky |
| Deepslate, tuff, calcite | Ancient |
| Dirt, coarse dirt, grass block | Forest, Meadow, Lush |
| Rooted dirt | Lush |
| Podzol | Forest (spruce variant) |
| Mycelium | Mushroom |
| Mud, muddy mangrove roots | Aquatic (mangrove variant) |
| Sand, sandstone, smooth sandstone | Desert |
| Red sand, red sandstone | Badlands, Desert (red variant) |
| All terracotta variants | Badlands |
| Gravel | Rocky, Aquatic |
| Clay | Aquatic (primary), Desert (sprinkle) |
| All overworld ores (regular) | Rocky |
| All overworld ores (deepslate) | Ancient |
| All wood types | Forest (variants), Aquatic (mangrove), Meadow (cherry) |
| All leaves, saplings | As above |
| Bamboo | Forest (jungle variant) |
| Azalea + azalea leaves | Lush |
| Moss block, moss carpet | Ancient (first/gateway source), Lush |
| All temperate flowers | Meadow (primary), Forest (sprinkle) |
| Tropical/biome flowers (blue orchid) | Aquatic (swamp) |
| Pink petals | Meadow (cherry variant) |
| Torchflower, pitcher plant | Lush (sprinkle) |
| Tall flowers (sunflower, lilac etc.) | Meadow |
| Grass, tall grass, fern | Forest, Meadow |
| Dead bush | Desert, Badlands |
| Cactus | Desert, Badlands (sparse) |
| Sugar cane | Aquatic (edge), Desert (sprinkle) |
| Melon | Forest (jungle variant) |
| Pumpkin | Forest (sprinkle) |
| Sweet berry bush | Forest (spruce variant) |
| Mushrooms (small + huge) | Mushroom (primary), Forest (sprinkle) |
| Vine | Forest (dark oak/jungle), Lush, Aquatic (swamp) |
| Cave vines + glow berries | Lush (underside) |
| Hanging roots | Lush |
| Dripleaves (big + small) | Lush |
| Spore blossom | Lush (underside) |
| Seagrass, kelp | Aquatic |
| Lily pad | Aquatic |
| Coral (all types + fans) | Aquatic (warm variant) |
| Sea pickle | Aquatic (warm variant) |
| Sponge | Aquatic (very rare warm sprinkle) |
| Snow (layer + block) | Frozen (primary), Rocky (peak sprinkle) |
| Powder snow | Frozen |
| Ice, packed ice, blue ice | Frozen |
| Dripstone block, pointed dripstone | Ancient |
| Amethyst block, budding amethyst, clusters | Ancient (geode variant) |
| Sculk, sculk vein, catalyst, sensor, shrieker | Ancient (deep dark variant) |
| Obsidian | Rocky/Aquatic (lava+water contact, rare) |
| Lava | Rocky (rare surface pool) |
| Water | Aquatic (primary), Forest/Lush (pond sprinkle) |
| Bone block | Desert (fossil sprinkle) |

---

## Progression sketch

```
START → Forest Skyseed (dirt + planks)
             ↓ coal, wood, flowers
         Rocky Skyseed (stone + cobblestone)
             ↓ iron, copper — and snow peak sprinkle → Frozen
             ↓ low-Y rocky → gold, redstone, diamond
         Desert Skyseed (sand + sandstone)
             ↓ sand, glass, clay sprinkle → Aquatic
             ↓ red sand sprinkle → Badlands (with terracotta)
         Meadow Skyseed (dirt + flower)
             ↓ honey, wax, all dyes
         Aquatic Skyseed (clay + sand)
             ↓ clay, kelp, mangrove, coral
         Frozen Skyseed (snow + ice)
             ↓ packed ice, blue ice
         Badlands Skyseed (red sand + terracotta)
             ↓ extra gold, terracotta palette
         Ancient Skyseed (deepslate + cobbled deepslate)  ← deep Rocky ONLY; no Lush dependency
             ↓ all deepslate ores, sculk, amethyst
             ↓ moss surface → unlocks Lush          (Ancient is the moss gateway)
             ↓ deep dark variant → sculk shrieker risk/reward
         Lush Skyseed (moss + azalea)                     ← moss from Ancient; azalea: see Lush entry
             ↓ dripleaf, glow berries, sniffer plants
```

Ancient before Lush is deliberate: Ancient bootstraps from deep Rocky alone and is the only early moss source, so it must precede Lush. This breaks the original Lush ↔ Ancient moss deadlock (each previously required the other's moss).

**The column order is presentational, not a dependency chain.** The only *hard* gates are:

- **Forest** → everything (start: stone for Rocky, flowers for Meadow, sand for Desert via beach/desert islands, the azalea sprinkle for Lush).
- **Rocky** → **Frozen** (snow from peaks) and **Ancient** (deepslate from a deep throw).
- **Desert** → **Aquatic** (clay sprinkle) and **Badlands** (red sand + terracotta).
- **Ancient** → **Lush** (moss).

Everything else is free-order: a player holding Forest + Rocky can pursue Desert, Meadow, Frozen, and Ancient in any sequence. If FTB Quests is ever wired to this, the quest tree must model this branching DAG, not the sketch's single column.

Nether and End seeds are deferred — a long-term goal, not yet specified (see README.md → Status).

---

*Block list verified against Minecraft 1.21.1 vanilla. Re-verify any block ids before wiring into the theme codec.*
