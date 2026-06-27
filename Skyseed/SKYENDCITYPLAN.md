# SKYENDCITYPLAN — the End City as a full vanilla-style jigsaw

Goal: replace the single-box `end_city/city.nbt` with a recursing **jigsaw tileset** modelled on Mojang's
`minecraft:end_city` structure, so each spawn is a stepped, overhanging, sprawling purpur city — never "another box".

★ **Standing instruction:** author every piece by referencing the **vanilla `end_city` template pool** pieces
(`data/minecraft/structure/end_city/*` + `data/minecraft/worldgen/template_pool/end_city/*`) for block placement,
material palette, overhang/corbel detailing, and the jigsaw graph. We simplify the geometry, but the silhouette,
palette, and connection topology should read as a vanilla End City.

## Vanilla reference → Skyseed piece map

| Vanilla piece(s) | Role | Skyseed piece | Pool |
| --- | --- | --- | --- |
| `base_floor` | the ground tower section (start) | `start` | `end_city/start` |
| `second_floor_1/2`, `third_floor_1/2` | stacked overhanging tiers (variants) | `floor_a`, `floor_b` | `end_city/floor` |
| `third_roof` | the terraced roof cap | `roof` | `end_city/floor` |
| `tower_base/piece/top` | thin end-rod-lit spires off a tier | `tower_base`, `tower_piece`, `tower_top` | `end_city/tower` |
| `bridge_end/piece/gentle_stairs/steep_stairs` | horizontal connectors (with stairs) to a new section | `bridge`, `bridge_stairs`, `bridge_end` | `end_city/bridge` |
| `fat_tower_base/middle/top` | the wide tower that carries the ship | `fat_tower` | `end_city/ship` |
| `ship` | the End ship — the **guaranteed-elytra** reward | `ship` | `end_city/ship` |

### Palette (vanilla)
`purpur_block`, `purpur_pillar`, `purpur_stairs` (corbels/overhang under-bevel + roof terracing), `purpur_slab`,
`end_stone_bricks` (bridges + accent courses), `magenta_stained_glass` (windows), `end_rod` (lighting). Loot:
`minecraft:chests/end_city_treasure` in tiers, `skyseed:chests/end_ship` (elytra) on the ship. Shulkers already spawn
as theme mobs.

### Jigsaw topology
Vertical stacking uses **up/down** jigsaw connectors (like vanilla): a tier's `UP` connector
(`target = skyseed:ec_tier`) mates a floor/roof's `DOWN` connector (`name = skyseed:ec_tier`). Side connectors on the
tiers branch out to `end_city/tower`, `end_city/bridge`, and `end_city/ship`. The `start`'s up connector roots the
stack; the `roof` has no up connector, so it caps. (Mirrors the `dungeon_complex` self-link pattern, but vertical.)

## Phases

- [x] **Phase 1 — Vertical tiered core (de-box).** ✅ `start` + `floor_a`/`floor_b` (overhanging tiers) + `roof`, stacked
      via up/down connectors; `end_city/start` + `end_city/floor` pools; theme `jigsaw` → `end_city/start`, depth 5,
      pad 7. The interim ship + treasure ride the `start`. Gotcha solved: a vertical-jigsaw child is rejected if a
      parent block sits in the child's destination cell — the base's old top-corner rods (y9) collided with the tier
      floor (y9), so no tier stacked; removing them fixed it (the joint/orientation were fine all along).
- [x] **Phase 2 — Thin towers.** ✅ `tower_base`/`tower_piece`/`tower_top` (slender 3×3 spires, windowed + end-rod-lit)
      sprout from the tiers' lip-edge side connectors and cantilever over the void; pools `end_city/tower` +
      `end_city/tower_piece`, ~43% per branch, depth 6. Side branches put their destination cell clear of the lip so the
      tower doesn't collide with the tier (the seam-collision lesson from Phase 1). Guarded by `endCitySproutsTowers`.
- [x] **Phase 3 — Bridges + sprawl.** ✅ A tier's side branch yields a tower, a `bridge_end`, or nothing; a bridge
      (`bridge_piece` + a stepped `bridge_stairs`) spans end-stone-brick rails out over the void to a `wing` — a second
      7×7 section (own treasure) that stacks its own tiers + may sprout more spires/spans. Pools `end_city/branch`
      (replaces `tower`) + `end_city/bridge`. **No over-void support by design** — the spans/wings float, the End City
      aesthetic (no `trestles`). Guarded by `endCityBridgesAcrossVoid`; the tower test hardened to place the spire
      pieces in isolation (the full assembly is position-seeded AND the gametest origin varies per run → flaky).
- [ ] **Phase 4 — Fat tower + dedicated ship.** `fat_tower` + a proper `ship` piece (`end_city/ship`); move the elytra
      reward to the ship and drop the interim start-ship.
- [ ] **Phase 5 — Detailing pass.** magenta-glass windows, end-stone-brick accent courses, purpur-stair corbels under
      every overhang, roof terracing + lighting polish — to match the vanilla read.

Tests: assembly (multi-seed, position-seeded jigsaw → robust thresholds), vertical span, the elytra-chest piece, and
over-void bridge support. The `.nbt` 2-build dance applies to every new/changed piece.
