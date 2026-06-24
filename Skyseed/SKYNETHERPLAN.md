# Skyseed — Nether Chapter Plan

The Nether is **built out end to end** as of v0.57.0 — every overworld seed adapts or fizzles, all five Nether
biomes have a full-size native seed (+ a Large variant), and the first Nether structures (the Fortress + blaze room, the Bastion Remnant, the Piglin Trading Post)
are in. What's left: the Wither Arena, then the handoff to the End. Per-release detail is in [CHANGELOG.md](CHANGELOG.md).

---

## Shipped (one line each)

**The dimension**
- Nether emptied to a **void + a lava sea below Y 32** + the 5 Nether biomes — new worlds only (v0.35.0); the End
  pre-voided too with its biome source kept (v0.35.1); legacy-world login warning + `/emptynether` `/emptyend`
  in-place rescue commands (v0.35.2–0.35.3).
- **Adapt-or-fizzle:** a theme declares its `dimensions`; thrown into a dimension it doesn't implement it fizzles
  rather than grow the wrong base form (v0.37.0). A Nether/End `biome_override` never inherits overworld content
  (v0.45.0).

**Tier-1 — overworld seeds adapt (deliberately TINY ~7×7×4 footholds, v0.39.0)**
- All 8 viable terrain seeds adapt: Rocky → mining (v0.36.0) · Desert → Soul Sand Valley (v0.38.0) · Badlands →
  Basalt Deltas (v0.40.0) · Aquatic → Lava Lagoon (v0.41.0) · Mushroom (v0.43.0) · Forest → crimson/warped fungal
  (v0.44.0) · Ancient → haunted deep · Lush → vine grotto (v0.46.0). Meadow + Frozen fizzle by design.
- The 8 **Large** overworld variants also adapt — same form, ~1.5× the tiny size (v0.47.0).

**Tier-2 — full-size Nether-native seeds (the payoff; radius 6-9)**
- `nether_rocky` (v0.48.0) · `nether_lava` (v0.50.0 — also grows a full-size lava island in the overworld) ·
  `nether_forest` (v0.51.0, crimson/warped) · `nether_soul` (v0.52.0) · `nether_basalt` (v0.53.0). Each has a tiny
  overworld easter-egg island. Recipes are 2×2 = the matching overworld seed + 2 nether blocks + 1 signature (v0.52.1).
- **Large Nether seeds** — a Large variant of each (radius 11-17), recipe 3×3 with the matching Large overworld seed
  in the centre (v0.56.0); a 5% **blaze spawner room** rolls on them + a `debug_blaze_spawner` debug seed (v0.57.0).

**Structures & cross-dimension**
- **Nether Fortress Island** (`nether_fortress`, v0.55.0): a hand-built arcaded nether-brick bridge running out of a
  keep with a caged blaze spawner. The standalone blaze-spawner room is the 5% roll on the Large Nether seeds (v0.57.0).
- **Ruined Portal twins:** the ruined portal grows in both dimensions (overworld = goodies, Nether = bare frame) and
  spawns a linked twin at the vanilla 8:1 coordinate so repaired+lit frames link for free — for the dedicated seed
  *and* the rare-structure roll (v0.54.0–0.54.1).

---

## World design (reference)

- **The void.** Like the overworld void, the Nether is empty + a lava sea below Y 32; you arrive via a portal onto
  its obsidian platform and grow islands out from there. No curated start — bring your own gear; the early Nether is
  lethal.
- **No ceiling — value gated by lava proximity.** The Nether has no "depth," so *lava proximity* does the job height
  does in the overworld: throw a mining seed low (near the lava sea) for the richest ore (Ancient Debris, gold),
  high for the lean version. One ore model across the Nether seeds.
- **Five biomes.** Nether Wastes, Crimson Forest, Warped Forest, Soul Sand Valley, Basalt Deltas — each has a native
  Tier-2 seed; an adapted overworld seed takes its form from the biome it lands in.

---

## Still to build

Genuinely new seeds with no overworld parallel, on the existing jigsaw + mob-pack machinery:

_Shipped: the **Bastion Remnant** (3 weighted variants + the basalt-deltas fizzle, v0.62.0) and the **Piglin Trading
Post** (v0.63.0) — see the [CHANGELOG](CHANGELOG.md)._

### ☠️ Wither Arena Island
- Recipe: Obsidian + Nether Brick + Soul Sand — a **blast-resistant** arena so the Wither can't blow you into the
  void. An enclosed obsidian/nether-brick bowl with a soul-sand floor, a safe ledge, a charged Respawn Anchor.
- Reward: Nether Star → Beacon (the Nether chapter's capstone). The Wither is craftable mid-Nether (soul sand from
  Soul islands + skulls from the Fortress); this is the survivable venue to fight it.

### Then → the End
Warped Enderman pearls + Fortress blaze rods → Eyes of Ender → the End chapter (already pre-voided, biome source
kept, so no re-save needed). The last nether-gated blocks (wither rose, froglight, copper bulb) ride in with this
work — see `MISSINGBLOCKSPLAN.md`.

---

## Notes
- **Respawn in the Nether:** Respawn Anchors charge with glowstone (Rocky / Wastes) — "charge an anchor before you
  explore" deserves a prominent guide entry, given every island sits over void.
- **Standing rule:** the void noise_settings IDs (`skyseed:void` / `void_nether` / `void_end`) are baked into
  level.dat by string — never rename them.
- **⚠ Before 1.0:** remove the `/emptynether` / `/emptyend` rescue commands (and the conversion offer in
  `PlayerEvents`) — a pre-void-world stopgap that leans on the "experimental features" path, fine now but not for a
  1.0 release. See the README Roadmap.
- Nether biome list verified against 1.21.1 (exactly five biomes); re-verify mob spawn tables + structure loot-table
  IDs before building the remaining structures.
