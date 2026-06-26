# Skyseed — End Chapter Plan

The third and final dimension. The overworld and Nether chapters are feature-complete; this is the capstone chapter.
The End is **already pre-voided** (v0.35.1 — terrain emptied, the standard End biome source kept), so growing it out
forces **no re-save**. The Nether chapter already hands off to here: *Warped enderman pearls + Fortress blaze rods →
Eyes of Ender → the End* (see `SKYNETHERPLAN.md` → "Then → the End"). The whole arc ends, as it should, with a dragon.

This is a **first draft to decide against**, not a contract. Phase 1 (how you get in) is now decided in shape — a
capstone collect-a-thon; the rest is the shape of the chapter that follows.

---

## Phase 1 — Getting to the End  ✅ *shape decided (tuning open)*

**The problem.** In vanilla you reach the End through a Stronghold's portal room — 12 End Portal Frames you fill with
Eyes of Ender. Skyseed is skyblock: there is nothing to explore to, and End Portal Frames aren't craftable. So the
chapter's gate is "how do you obtain a working End portal," and that gate must sit *behind the Nether* (Eyes of Ender
already require blaze rods + enderman pearls — a clean, earned prerequisite).

**Decided design — the End is the mod's capstone collect-a-thon.** You don't *find* the portal, you *assemble its seed*
from one ingredient out of nearly every corner of Skyseed — so reaching the End means you've engaged with most of the
mod. Then you plant the seed and **the whole portal grows** (option 2 — no movable-frame mechanic).

**The recipe tree (math is exact — 4 edges × (1 shard + 2 materials) = 4 shards + 8 materials):**

- **4× Portal Frame Shard** — the base, a random **Dungeon** roll (+ a thin **Ancient** chance). The early-game mystery:
  *"what the hell is this Portal Frame Shard I just found?"*
- **8× structure/biome materials** — a *unique rare drop*, one from each of eight major Skyseed structures/biomes (one
  in the **Woodland Mansion**, one in the **Ocean Monument**, …). The late-game checklist: *"where do I find the last
  one I'm missing?"*
- **4 Edges** — four distinct items, each = **1 Portal Frame Shard + 2 of the structure materials** (paired by domain).
- **1 End Portal Seed** = the four Edges combined.
- **Plant it → the full 12-frame portal chamber island grows**; fill with **12 Eyes of Ender** (Nether — blaze rods +
  enderman pearls) → activate → the End.

**The eight sources (proposal — one rare material each, spanning the mod; tune the exact set):** Woodland Mansion +
Ocean Monument (the grand structures) · Desert Temple + Jungle Temple (the temples) · Trial Chamber + Pillager Outpost
(the hostile camps) · Nether Fortress + Bastion Remnant (the Nether). Candidates to swap in: Witch Hut, Wither Arena, or
a biome relic (Mushroom / Badlands). Make them **rare but farmable** — you can re-grow that seed and re-loot, so it's a
hunt, never a permanent RNG wall.

**The four edges (proposal — each pairs one domain):** *Grand* = shard + Mansion + Monument · *Temple* = shard + Desert
+ Jungle · *Camp* = shard + Trial Chamber + Outpost · *Nether* = shard + Fortress + Bastion. The four → the End Portal
Seed.

**Why this is the gate:** the End becomes a true *finale* — you can't shortcut it, you must have looted the breadth of
Skyseed *and* done the Nether (for the eyes). The shard is the early "what is this?"; the eight materials are the long
tail that pulls you back through the whole mod.

**Open to tune:** the exact eight sources + their drop rarity; whether one or two are *guaranteed* (so a completionist
is never RNG-stranded on the very last item); the edge pairings; the shard's dungeon-roll chance; and whether the End
Portal Seed island is the bare portal or carries a little stronghold flavour (silverfish, library).

**Deliverables:** the **Portal Frame Shard** (dungeon drop) + **8 structure-material** items + their loot-table hooks;
the **4 Edge** recipes + the **End Portal Seed** recipe (+ `skyseeds` tag/guide); the End Portal Seed island-gen (grows
the 12-frame portal chamber on the island-gen machinery); the guide thread (early "mystery shard" → late "collection
checklist → assemble → plant"); and gametests for each loot drop, the recipe tree, and the grown portal.

---

## Phase 2 — Being in the End (the world design)

- **The void.** Like the Nether: an empty End over the void, `noise_settings: skyseed:void_end` (⚠ baked into level.dat
  by string — **never rename**). You arrive via the portal onto the **standard obsidian arrival platform** at the central
  island's edge and grow islands out from there. No curated start — bring gear; endermen are everywhere and the central
  island is the dragon's.
- **No ore model.** The End has no ores — so unlike the Nether's lava-proximity gradient, End seeds yield *blocks +
  access*, not tiered ore. Value here is **chorus, purpur, shulkers, elytra, and the dragon drops**, gated by structure
  and biome, not depth.
- **Respawn.** No beds, no respawn anchors in the End. Every island sits over void; dying sends you to the overworld
  spawn. The guide must make "the End has no respawn — stage from the overworld" a prominent warning (mirrors the
  Nether's anchor warning).
- **Five biomes** (verify against 1.21.1, as the Nether list was): `the_end` (central, the dragon), `end_highlands`,
  `end_midlands`, `end_barrens`, `small_end_islands`. The kept biome source means a seed thrown in the End lands in
  whichever biome that spot is — Phase 5 leans on this.

---

## Phase 3 — End-native seeds (new content, jigsaw + mob-pack machinery)

Genuinely new seeds with no overworld parallel, the way the Nether got Bastion / Trading Post / Wither Arena:

- **End Stone island** — the base End seed: an end-stone foothold (the Tier-N "adapt me" body other End seeds build on).
- **Chorus Forest** — chorus plants + flowers, purpur, an enderman mob pack; the "End Highlands" look. Chorus fruit →
  the random-teleport food + (cooked) popped chorus → purpur, a small renewable economy.
- **End City** — the chapter's flagship structure seed, on the jigsaw machinery: purpur towers + a ship, **shulkers**
  (shulker shells → shulker boxes) and the **elytra** reward on the ship. The natural parallel to the Mansion / Bastion
  as the "grand loot dungeon." Likely **End-biome-gated** (highlands/midlands only — see Phase 5).
- **Open question:** an **Ender Pearl / enderman farm** seed (a dark end-stone platform mob-packed with endermen) — handy
  but maybe redundant with Chorus Forest's pack. Decide whether endermen are a dedicated seed or just a pack rider.

---

## Phase 4 — Interactions with current seeds

- **The ladder shaft already works in the End** (`ladder_small`/`ladder_large` have an `end` override → an end-stone
  foothold + the drop). Free transit, already shipped — just confirm it reads well over the End void.
- **Adaptation.** An adapted overworld/utility seed thrown in the End should take **End form** via `biome_overrides`
  (like the Nether adaptations): end-stone body, no grass/water. Decide the *output* — most overworld seeds have nothing
  to give in the End (no ore, no crops); they may just become barren end-stone footholds, or be **disallowed** in the
  End entirely (cleaner). *Recommend: a small allowlist of seeds that make sense in the End; the rest refuse to plant
  there* — and that refusal is itself a teaching moment.
- **End materials feeding overworld seeds.** Chorus fruit / purpur / shulker boxes are the new craft inputs; check
  whether any existing recipe wants them, and whether a "chorus farm" belongs as an overworld-grown seed fed by End
  chorus (probably End-only — keep it there).
- **Standing rule:** every real seed gets a recipe + `skyseeds` tag + guide entry; debug seeds get none. End seeds
  inherit this.

---

## Phase 5 — Interactions with the End biomes

The kept End biome source is the lever. A seed thrown in the End takes its form from the biome it lands in, via
`biome_overrides` keyed on the End biomes (exactly the Nether's per-biome pattern):

- **`the_end`** (central) — reserved for the dragon (Phase 6); ordinary seeds probably *can't* plant on the central
  island, or get overridden to the arena setup.
- **`end_highlands`** — the lush End: chorus forests, the prime **End City** territory.
- **`end_midlands`** — transitional; End Cities also spawn here in vanilla.
- **`end_barrens`** — bare cliffs; the lean variant (sparse, no chorus) — the End's "low-value" surface.
- **`small_end_islands`** — the scattered outer voids; thematically the "you need transit" zone (ties to the ladder /
  End gateways).

**Decision:** do End seeds *gate* on biome (End City only in highlands/midlands, like the overworld structure seeds gate
on biome), and does an "adapt me" End seed *re-skin* per biome (chorus in highlands, bare in barrens)? *Recommend yes to
both* — it reuses proven machinery and makes the End feel mapped, not uniform.

---

## Phase 6 — …is that a dragon?!

**Yes.** The Ender Dragon is the End capstone — the parallel to the Wither Arena (Nether Star → Beacon). The whole mod's
finale.

- **The arena.** A dedicated capstone seed that grows the **central-island dragon setup**: the end-stone island + the
  obsidian **pillars topped with End Crystals** + the central **exit-portal fountain** (bedrock + the portal frame the
  dragon's death lights). This is the hardest piece — the vanilla dragon fight is tightly coupled to these features and
  to `EndDragonFight`; growing it from a seed in a *voided* End needs care (the fight expects the central-island
  features at the world origin). **Biggest open risk; prototype early.**
- **Respawn loop.** Four End Crystals on the portal re-summon the dragon — so the seed should make crystals/obsidian
  obtainable (a crystal recipe, or a "pillars" seed) for a repeatable fight, not a one-shot.
- **The rewards close the chapter:** the **dragon egg** (trophy), **dragon's breath** (lingering potions), the **End
  Gateway** opening to the outer islands → **End Cities → elytra + shulkers** (Phase 3). Flight is the literal and
  thematic top of the progression.

---

## Notes & standing rules

- **Prerequisite chain:** Nether (blaze rods + enderman pearls → Eyes of Ender) **then** Phase-1 frames → portal → End.
  Don't let the End be reachable before the Nether is done.
- **`skyseed:void_end`** noise-settings id is baked into level.dat by string — never rename it (same rule as the Nether).
- **⚠ Before 1.0:** the `/emptyend` rescue command + the `PlayerEvents` conversion offer are a pre-void-world stopgap on
  the experimental-features path — remove them for 1.0 (tracked in the README roadmap alongside `/emptynether`).
- **Verify against 1.21.1** as each piece lands: the End biome list (should be exactly the five above), End structure
  loot-table IDs (end_city, end_city_treasure), and mob spawn tables (endermen, shulkers) — the same discipline the
  Nether chapter used.
- **Last nether-gated blocks** (wither rose, froglight, copper bulb) were meant to ride in with the End handoff — confirm
  against `MISSINGBLOCKSPLAN.md` so nothing's stranded.
- **Build discipline (unchanged):** dev-generated `.nbt` via the 2-build dance; structure seeds on the jigsaw + mob-pack
  machinery; every feature gets gametests; commit per feature.
```
