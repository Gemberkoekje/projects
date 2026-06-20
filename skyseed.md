# Skyseed — Project Plan

*Working title. The mod and the thrown item ("Skyseed") still want better names eventually — see §9.*

---

## 1. Concept

A skyblock where progression is driven by **terraforming**, not condensing. You start on a small, fully hand-authored sky island. Crafting produces a **Skyseed** — a throwable item. Thrown into open air, it arms for ~3 seconds, then *germinates*: particles, a sound, and a new procedurally generated island appears at that point in the sky.

Different recipes produce Skyseeds of different **themes** (forest, rocky/ore, …) and sizes. The loop is:

> craft a Skyseed → throw it → a new themed island appears → harvest new resources → craft the next, more expensive Skyseed.

Exploration *and* crafting are both the progression currency, instead of crafting alone.

### Design pillars

1. **Themed, never identical.** Every generated island must feel hand-rolled within its theme. No pasting the same blob each time. (§5)
2. **Curated start, procedural everything-after.** The first island is authored block-by-block so the player can never soft-lock on turn one. Everything after is generated.
3. **Adding content is data, not code.** A new island theme, a new recipe, a new ore — all addable via config, with zero Java changes. (§4)

---

## 2. Platform

- **Loader: NeoForge.** The modpack is content-heavy (quests, tech, beauty mods, deliberately skipping Create), which is NeoForge's strength — most non-Create tech mods worth building a pack's identity around (Immersive Engineering, Mekanism) live there, several NeoForge-exclusive. Quests and beauty/decoration mods are available on both loaders, so they didn't factor into the decision.
- **Minecraft 1.21.1.** Best mod availability across the 1.21 line.
- **JDK 21**, NeoForge MDK for 1.21.1, Gradle.

---

## 3. The Skyseed item

**One item, `skyseed:island_seed`.** It is not one item per theme — different Skyseeds are the same item carrying a different `skyseed:theme` data component, set by whichever recipe crafted it. A `minecraft:item_name` component on the recipe result gives each one its display name (e.g. "Rocky Skyseed").

- `use()` on the item spawns the throwable entity, copying the `theme` component onto it.
- A unique **icon** per theme is the one part that isn't pure data — it needs a companion resource pack (`custom_model_data` → model → texture). Optional; shared icon + distinct name is a fine fallback.
- Recipe *inputs* can't match on item components (only recipe *results* can), so a Skyseed can't be crafted from another Skyseed. Crafting from ordinary items — including modded ones — is unaffected.

This is what makes §4 possible: a new Skyseed recipe is just a new recipe JSON pointing at a new theme id. No new item, no Java.

---

## 4. Configuration

Each Skyseed **recipe** and each **theme** is config. The dividing line:

- **Config owns the *what and how much*:** recipes (inputs → theme + name), block palettes, ore tables (block, chance, count, vein size, depth), weighted tree/feature variants, size range, shape parameters, optional structures.
- **Code owns the *how*:** the generation algorithm that consumes those parameters — blob geometry, layering, vein clustering, placement, RNG seeding.

Shape stays parametric rather than fully data-driven (radius, rim noise, underside profile as numbers) — enough flexibility without inventing a geometry DSL.

### Recipe (data)

A normal datapack shaped-crafting recipe. The example below is the basic Skyseed — dirt and planks in a checkerboard — producing a forest-themed seed:

```
planks | dirt
-------------
dirt   | planks
```

```json
{
  "type": "minecraft:crafting_shaped",
  "pattern": [
    "PD",
    "DP"
  ],
  "key": {
    "P": { "item": "minecraft:oak_planks" },
    "D": { "item": "minecraft:dirt" }
  },
  "result": {
    "id": "skyseed:island_seed",
    "components": {
      "skyseed:theme": "skyseed:forest",
      "minecraft:item_name": "Forest Skyseed"
    }
  }
}
```

The pattern/key/ingredients are exactly what make this *a recipe* — the `result` block is what makes it *a Skyseed recipe*. A rocky theme might instead use `stone` + `cobblestone` in the same shape, with `"skyseed:theme": "skyseed:rocky"` in the result; that's the only thing that changes.

### Theme (data)

One JSON per theme, identified by the id the recipe references.

```json
{
  "shape":   { "radius": { "min": 6, "max": 9 }, "rim_noise": 0.35, "underside": "teardrop" },
  "palette": { "surface": "minecraft:stone", "fill": "minecraft:cobblestone", "core": "minecraft:stone" },
  "ores": [
    { "block": "minecraft:iron_ore",    "chance": 0.95, "count": { "min": 4, "max": 6 }, "vein_size": { "min": 1, "max": 3 }, "depth": "core" },
    { "block": "minecraft:gold_ore",    "chance": 0.20, "count": { "min": 1, "max": 3 }, "vein_size": { "min": 1, "max": 2 }, "depth": "core" },
    { "block": "minecraft:diamond_ore", "chance": 0.01, "count": { "min": 1, "max": 1 }, "vein_size": { "min": 1, "max": 1 }, "depth": "deep_core" }
  ],
  "variants": [
    { "weight": 1, "decoration": { "features": ["minecraft:patch_grass_normal"], "density": 0.1 } }
  ]
}
```

`chance` is the per-island presence roll ("does this island have iron at all"); `count` is how many if it does.

Forest theme, showing weighted **variants** (oak / birch / jungle — one chosen per island):

```json
{
  "shape":   { "radius": { "min": 7, "max": 10 }, "rim_noise": 0.45, "underside": "teardrop" },
  "palette": { "surface": "minecraft:grass_block", "fill": "minecraft:dirt", "core": "minecraft:stone" },
  "ores": [
    { "block": "minecraft:coal_ore", "chance": 0.6, "count": { "min": 1, "max": 4 }, "vein_size": { "min": 1, "max": 3 }, "depth": "core" }
  ],
  "variants": [
    { "weight": 5, "name": "oak",    "decoration": { "features": ["minecraft:oak"],   "density": 0.18 } },
    { "weight": 3, "name": "birch",  "decoration": { "features": ["minecraft:birch"], "density": 0.18 } },
    { "weight": 1, "name": "jungle", "decoration": { "features": ["minecraft:jungle_tree", "minecraft:jungle_bush"], "density": 0.25 } }
  ]
}
```

Adding a new mod's content is then purely additive — e.g. a magic-tree mod becomes one more variant:

```json
{ "weight": 1, "name": "magic", "surface_override": "magicmod:mana_grass",
  "decoration": { "features": ["magicmod:mana_tree"], "density": 0.2 } }
```

...or an entirely new Skyseed: one theme JSON (possibly referencing the new mod's trees, ores, or structures) + one recipe JSON crafting it from that mod's items. No code changes either way.

### Modded references

Block/feature/structure ids in a theme resolve at load. A missing id should be **skipped with a warning**, not a hard failure — so a theme can optionally reference modded content (`somemod:tin_ore`) and still load cleanly in a pack without that mod.

### Loading

Recipes load via the vanilla recipe manager, as always. Themes load via a custom datapack registry backed by a `Codec` — exact NeoForge 1.21.1 API to confirm at scaffold time. **The codec is the keystone**: it, the `skyseed:theme` component, the generator, and every recipe all key off the same theme-id namespace. Lock its shape before building on top of it.

---

## 5. Island generation algorithm

Hard requirement: islands feel **random within a theme**, never stamped. A single pasted blob per theme is explicitly rejected.

**Procedural voxel generation, parameterised per theme:**

1. **Silhouette.** An *irregular* blob, never a clean sphere — e.g. a 2D rim-radius noise field extruded with a **teardrop** vertical profile (flat-ish top, tapering underside). Reads instantly as "floating island," different every throw.
2. **Layered fill.** Surface block → soft fill band → core, thicknesses from theme params with small jitter.
3. **Ores.** Walk the core; per ore-table entry, roll presence (`chance`) then place a `count` of small **veins** (`vein_size`), weighted by `depth`. Clustered, not salt-and-pepper.
4. **Decoration.** Trees/grass/flowers from the rolled variant, scattered (random or Poisson-disc) at the variant's density.
5. **RNG.** Derive the `RandomSource` from `worldSeed ^ hash(center) ^ throwCount` — unique per island, reproducible, decorrelated from neighbours.

**Two implementation guards:**
- **Tick-budget placement.** Don't place thousands of blocks in one tick (visible stutter). Queue placement across N ticks with a per-tick budget; this doubles as a "grows in" animation. Use `setBlock` flags that avoid cascading neighbour/light updates per block.
- **Overlap safety.** Reject or nudge germination if too close to existing solid blocks (the start island or another generated one), so islands never grow into each other.

**Deferred option:** vanilla structure templates + jigsaw/processors (block-swap, rotation/mirror) as an alternative to pure procedural, if procedural ever feels too noisy for a given theme. Not needed for v1.

---

## 6. Architecture

- **Registration** — `DeferredRegister` for the `island_seed` item, the throwable entity type, the `theme` data component type, creative tab, sounds, particles. Registered once; everything past this point is data (§4).
- **Throwable + timer** — `IslandSeedEntity extends ThrowableItemProjectile`, carrying the theme id. `tick()` counts to **60 (3 s)**, then calls `germinate()` server-side only. On germinate: particles + sound, then hand off `(ServerLevel, BlockPos center, IslandTheme)` to the generator.
- **Generator** — a near-pure function: `(ServerLevel, BlockPos, IslandTheme, RandomSource) → places blocks`. Independent of items/entities, reusable (the curated start island can share palette logic), testable on its own.
- **Starting island** — authored as a structure (structure blocks or hand-written NBT), placed at world creation. Not procedural. Soft-lock-proof by construction.

---

## 7. Build & dev loop

- `runClient` / `runServer` Gradle tasks launch a dev Minecraft with the mod loaded.
- Loop: edit → build → read stack trace → fix → repeat. An agent that runs its own build and reads its own errors closes this loop without manual copy-pasting — the main reason to drive this from Claude Code rather than chat.
- Git from commit zero; worldgen is exactly the kind of thing worth bisecting.

---

## 8. Milestones

Each step compiles, runs, and proves one thing before the next.

0. **Toolchain.** Scaffold the NeoForge 1.21.1 MDK; `runClient` launches an empty mod.
1. **Item + one recipe.** The generic item appears and crafts. Proves registration.
2. **Throwable.** Flies and despawns like a snowball. No generation yet.
3. **Timer + placeholder.** At 60 ticks: particles + sound + a flat stone platform. *Proves the full core loop end to end.*
4. **First real island.** Teardrop blob, one theme, layered fill.
5. **Decoration + ore.** Trees/grass on top, veins in the core. Now it feels like an island.
6. **Datapack themes.** Theme moves to JSON; add theme #2 (rocky) + its recipe. Theme #3 should now be JSON-only.
7. **Sizes.** A pricier recipe → larger radius. Parameter change only.
8. **Starting island.** Curated structure, placed on world creation.
9. **Polish.** Tick-budgeted placement, overlap safety, recipe balancing, per-theme sound/particles.

---

## 9. Modpack roadmap

Loader: NeoForge (§2). The Skyseed mechanic is the pack's signature, so it doesn't need Create to feel distinctive.

- **Tech:** Immersive Engineering (retro multiblock, NeoForge-only), Mekanism, Applied Energistics 2, Powah.
- **Quests:** FTB Quests + FTB Library/Teams. Each island theme is a natural quest gate.
- **Beauty:** Supplementaries, Macaw's suite, Immersive Furniture, Chipped/Rechiseled.
- **Performance:** Sodium + modern stack (OptiFine is dead on 1.21 regardless of loader).

---

## 10. Open questions

- **Naming.** Mod name, and whether "Skyseed" is the final name for the thrown item.
- **Germination point.** At landing, or peak-of-flight, or a fixed offset from the throw? Affects feel and overlap-safety.
- **Bridging.** Free-floating islands only for v1, or allow growing onto/adjacent to an existing island later?
- **Theme codec shape.** Finalize before milestone 6 — recipes, the generator, and every theme key off it.
- **Multiplayer sync.** Generation is server-side already; confirm client sync behaves before relying on it.

---

*Mod-availability and loader landscape checked June 2026; re-verify specific mods' 1.21 support before locking the pack.*
