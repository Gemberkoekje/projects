# Missing Blocks — Plan

Overworld-natural blocks that still **cannot be obtained in Skyseed** — neither generated nor craftable from what
you can get — with options for adding each, ordered by the intended progression.

**Out of scope (deferred per request):** the **End** (End Portal Frame, end stone, purpur, chorus, end rod, dragon
egg/head, …). We only start thinking about that once **both the overworld and the Nether are complete.**

**Excluded as "not a gap":** anything *derivable* from what you already get (wool/dyes; concrete; stained/tinted
glass; glazed terracotta; every stone/cobblestone/deepslate/sandstone/brick/tuff variant; almost all copper; all
wood sets; pumpkin/melon from loot seeds; candles & honey blocks; raw-ore blocks; redstone + rails; sponge; sea
lantern; decorated pots; amethyst; …). Also excluded: **spawner, budding amethyst, bedrock, reinforced deepslate,
infested blocks** — uncollectible in survival *in vanilla too*, so they're parity, not a regression.

---

## Done so far (for posterity)

**Implemented:** **lava** — veins + Y-banded lava lakes + ruined-portal lava (v0.33.0) · the **Ocean Monument** —
prismarine / dark prismarine / prismarine bricks (+ variants), sea lanterns, sponge, and a buried-treasure **Heart
of the Sea** for a conduit (also un-blocked the Aquarium recipe by making prismarine shards obtainable) (v0.34.0) ·
**bamboo** — bamboo forests over bamboo jungles + a sprinkle in jungle / sparse jungle (v0.34.3) · **glow lichen**
(Lush + Ancient undersides) and the remaining **coral fans / small coral plants** (Aquatic warm-ocean reefs) (v0.34.4).

---

## Progression context (so the suggestions fit)

Skyseed is a craft-and-throw loop: gather materials on an island → craft the next seed → throw → a new island with
new materials. New blocks should land in the tier whose materials/theme they match, and **every seed must be
craftable from earlier-tier materials** (no bootstrap loops). Rough tiers:

> Forest (start) → Rocky / Desert / Badlands / Frozen / Meadow → Lush / Aquatic / Ancient → Animal & Village islands →
> Structure islands → Grand structures (Trial Chamber, Mansion, Ocean Monument) → **[Nether]** → **[End]**

---

## Remaining gaps

| Gap | Vanilla source | Suggested approach | Tier | Effort |
|---|---|---|---|---|
| Copper bulb | trial chamber / blaze rod | place it in the **Trial Chamber** template (or defer to Nether) | trial / nether | small |
| Reinforced deepslate / infested blocks | Ancient City / Stronghold | encounter-only; skip (uncollectible in vanilla too) | — | — |

### Copper bulb
Missing; craftable only with a **blaze rod** (Nether). The rest of the copper set (cut / grate / chiseled / door /
trapdoor / waxed / oxidized / lightning rod) is already craftable.

**Options**
- **(a)** Place `copper_bulb` in the **Trial Chamber** template (it's a real trial-chamber block) → mine it.
  Collectible without the Nether, and the Trial Chamber is already a late-game target.
- **(b)** Defer to the Nether (blaze rods → craftable).

**Recommendation:** **(a)** place it in the Trial Chamber, unless you'd rather it wait for the Nether.

### Reinforced deepslate & infested blocks *(encounter-only)*
These have **no drops in survival even in vanilla** — you can't ever *hold* them outside creative. So the only thing
missing is the *encounter*. Add them as décor only if you build a future **Ancient City** (reinforced deepslate,
warden) or **Stronghold** (infested) — for the experience, not the block. Otherwise **skip** (parity with vanilla).

---

## Nether-gated (handle in the Nether plan — see `SKYNETHERPLAN.md`)

These naturally come with the Nether and shouldn't be forced into the overworld:
- **Wither rose** (needs a wither), **Froglight ×3** (frogs — which you have — eating tiny magma cubes), **Copper bulb**
  (blaze rod, unless placed in the Trial Chamber above), and the whole Nether block set (quartz family, soul sand/
  soil + soul light, the warped & crimson sets, nether gold/quartz ore, ancient debris/netherite, gilded blackstone,
  lodestone, respawn anchor, nether wart, shroomlight, weeping/twisting vines).
- **Already leaking through, so *not* gaps:** `netherrack`, `magma_block`, `basalt`, `blackstone` (→ its polished/brick
  variants craftable), `crying_obsidian`, `obsidian` (Ruined Portal + Ancient); `glowstone` (craft from witch
  glowstone-dust); nether brick (smelt netherrack). So a fair bit of nether *flavor* is reachable pre-Nether.

---

## Deferred — The End

Per request, not planned until the overworld and Nether are done: End Portal Frame, end stone (+ bricks), purpur ×4,
chorus plant/flower, end rod, dragon egg/head, end portal/gateway.

---

## Suggested sequencing

1. **Copper bulb** — a small Trial Chamber template edit (or leave it for the Nether).
2. **Nether tier (`SKYNETHERPLAN.md`):** wither rose, froglight, and the full Nether block set.
3. **Deferred:** the End.

With the items above done, the overworld is essentially **block-complete**, bar the encounter-only reinforced
deepslate / infested and the single copper bulb (which can ride in with the Nether's blaze rods anyway).
