# Skyseed — End Chapter Plan

The third and final dimension. The overworld and Nether chapters are feature-complete; this is the capstone chapter.
The End is **already pre-voided** (v0.35.1 — terrain emptied, the standard End biome source kept), so growing it out
forces **no re-save**. The Nether chapter already hands off to here: *Warped enderman pearls + Fortress blaze rods →
Eyes of Ender → the End* (see `SKYNETHERPLAN.md` → "Then → the End"). The whole arc ends, as it should, with a dragon.

This is a **first draft to decide against**, not a contract. Phase 1 is an open decision; the rest is the shape of the
chapter once we've decided how players get in.

---

## Phase 1 — Getting to the End  ⟵ *decide this first*

**The problem.** In vanilla you reach the End through a Stronghold's portal room — 12 End Portal Frames you fill with
Eyes of Ender. Skyseed is skyblock: there is nothing to explore to, and End Portal Frames aren't craftable. So the
chapter's gate is "how do you obtain a working End portal," and that gate must sit *behind the Nether* (Eyes of Ender
already require blaze rods + enderman pearls — a clean, earned prerequisite).

**Decided design — shards → edges → a Portal Frame *Seed*.** Peak on-theme: in Skyseed everything is a seed, so the End
Portal Frame is too — you don't get it as a drop, you *grow* it. You build it up a crafting ladder, then plant it:

- **The ladder.** Find/farm **12 Portal Frame Shards** → combine into **4 Portal Frame Edges** (3 shards each) →
  combine into **1 Portal Frame Seed** (4 edges). The shards are the scarce, farmable currency; the 12→4→1 ladder is
  the satisfying multi-tier endgame craft.
- **The seed grows the frame.** A **Portal Frame Seed**, planted, grows a **small portal-chamber island bearing an End
  Portal Frame** — you *find* it on a grown island, the Skyseed echo of stumbling on the portal room in a vanilla
  stronghold (and consistent with how every other structure here arrives).
- **Bootstrap (the only RNG):** the **Dungeon** seed (the main source, a solid chance) + a **thin chance on Ancient**
  (the deep, old places) roll a **stronghold fragment** alongside their normal output — mossy stone-brick corridors,
  silverfish, loot — that drops the **shards**. This is the only way to get started; the ladder (and an optional shard
  farm) does the rest. (No debug seed gets it — standing rule: debug seeds gate nothing.)
- **The ring → the End.** You need **12 frames**, so the loop runs ~12 times; assemble the 12-frame ring, fill with
  **12 Eyes of Ender** (from the Nether — blaze rods + enderman pearls) → activate → the End.

**The open fork (your call) — how 12 frames become one activatable ring:**

1. **One frame per island** (the literal version): each Portal Frame Seed grows an island with a single, *collectable*
   frame (vanilla's frame can't be picked up, so Skyseed mints a movable one); harvest 12, lay the ring. Keeps the neat
   12→12 symmetry and makes every frame earned — but it's the heaviest grind (up to ~144 shards for a portal).
2. **The whole portal per seed**: 12 shards → 1 Portal Frame Seed → an island holding the **full 12-frame ring**; one
   grow, fill with eyes, done. No movable-frame mechanic, much lighter. Reads as "grow the portal room," not "grow a
   frame." *Leaning this way for sanity* unless the heavier farm is the point.

**Open numbers to tune:** the bootstrap chances (Dungeon vs Ancient) and shards per fragment; the ladder ratios (12→4→1
as given, or cheaper); under option 1, frames per island (1 vs 2–3) and total grind; and whether the grown island is a
bare frame or a small loot-bearing chamber.

**Deliverables:** the **Portal Frame Shard** + **Portal Frame Edge** items and the two combine recipes; the **Portal
Frame Seed** (grows the portal-chamber island on the island-gen machinery) + its `skyseeds` tag/guide; under option 1, a
movable **End Portal Frame** item; the `stronghold` bootstrap fragment (jigsaw set + the shard drop) hooked onto Dungeon
+ Ancient; and gametests for the roll chance, the ladder recipes, and the grown portal/frame.

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
