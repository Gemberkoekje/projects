# Skyseed — End Chapter Plan

The third and final dimension. The overworld and Nether chapters are feature-complete; this is the capstone chapter.
The End is **already pre-voided** (v0.35.1 — terrain emptied, the standard End biome source kept), so growing it out
forces **no re-save**. The Nether chapter already hands off to here: *Warped enderman pearls + Fortress blaze rods →
Eyes of Ender → the End* (see `SKYNETHERPLAN.md` → "Then → the End"). The whole arc ends, as it should, with a dragon.

This is a **first draft to decide against**, not a contract. Phase 1 (how you get in) is now decided in shape — a
capstone collect-a-thon; the rest is the shape of the chapter that follows.

---

## Phase 1 — Getting to the End  ✅ *built (v0.101.0–0.104.0; balance tuning open)*

> **Build progress — Phase 1 is functionally complete.** The crafting items — **Portal Frame Shard**, the 8 structure
> relics, the 4 portal edges (PowerShell-painted icons) — and the shapeless **edge recipes** (v0.101.0); the **loot-table
> drops** (v0.102.0): a `skyseed:add_drop` global loot modifier seeds the shard into dungeon loot (~40%) and each relic
> into its structure's table (~25%) — re-grow a structure to re-roll; the **portal-chamber structure + `end_portal`
> theme** (v0.103.0, rebuilt as a diorite/quartz/blackstone peristyle temple with corner spires + a glowing cupola in
> v0.107.0): the vanilla 12-frame ring (frames empty) grows on a stone island; and the
> **End Portal Seed** (v0.104.0): the craftable seed (4 edges in a cross → seed) that plants that chamber, with full
> survival onboarding (recipe, unique icon, `skyseeds` tag, gated field-notes guide entry, reveal/gathered/craft
> advancements). The loop is closed: structure drops → 4 edges → seed → grown portal → fill with 12 Eyes of Ender → the
> End. **Open:** drop-rate / recipe balance (playtesting), and the End Gateway void-landing safeguard (flagged for Phase 6).

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

**The eight sources (working set — exact picks + rarities are perpetual playtest balance):** Woodland Mansion + Ocean
Monument (the grand structures) · Desert Temple + Jungle Temple (the temples) · Trial Chamber + Pillager Outpost (the
hostile camps) · Nether Fortress + Bastion Remnant (the Nether). Swappable: Witch Hut, Wither Arena, or a biome relic
(Mushroom / Badlands). Make them **rare but farmable** — re-grow that seed and re-loot, so it's a hunt, never a
permanent RNG wall (worth making one or two *guaranteed* so a completionist can't be stranded on the very last item).

**The four edges (working set — each pairs one domain):** *Grand* = shard + Mansion + Monument · *Temple* = shard +
Desert + Jungle · *Camp* = shard + Trial Chamber + Outpost · *Nether* = shard + Fortress + Bastion. The four → the End
Portal Seed.

**It's the gate — but it's opt-in.** For a player driving at the End it's a true *finale*: you can't shortcut it, you
must have looted the breadth of Skyseed *and* done the Nether (for the eyes); the shard is the early "what is this?", the
eight materials the long tail that pulls you back through the whole mod. For **everyone else it simply doesn't matter** —
the materials are passive rare drops you might idly find and never need. Someone just building a beautiful sky base never
has to think about it; nothing here gates non-End play, it only rewards the player who *wants* the End. That's also why
the exact balance can stay loose: it's low-stakes, perpetual playtest tuning, not a load-bearing number.

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
- **Respawn — warned (v0.112.0).** No beds, no respawn anchors in the End; dying sends you to the overworld spawn. The
  End Portal guide entry now carries this prominently ("no respawn there… stage from home"), mirroring the Nether's
  anchor warning. **Phase 2 is complete** (void End + central island + arrival + no-ore design + this warning).
- **Five biomes** (verify against 1.21.1, as the Nether list was): `the_end` (central, the dragon), `end_highlands`,
  `end_midlands`, `end_barrens`, `small_end_islands`. The kept biome source means a seed thrown in the End lands in
  whichever biome that spot is — Phase 5 leans on this.

---

## Phase 3 — End-native seeds (new content, jigsaw + mob-pack machinery)

Genuinely new seeds with no overworld parallel, the way the Nether got Bastion / Trading Post / Wither Arena:

- **End Stone island — dropped (superseded by v0.111.0).** The 10 overworld biome seeds already grow same-size, pure
  end-stone islands in the End, so a dedicated end-stone base seed adds nothing; skipped.
- **Chorus Forest — built (v0.112.0).** The `chorus_forest` seed: an end-stone island with chorus plants (the vanilla
  `chorus_plant` feature), purpur surface-scatter, and an enderman pack — the End-highlands look + a renewable
  chorus / purpur / ender-pearl economy. Crafted from chorus fruit (bootstrapped off the outer islands) ringing end
  stone; full onboarding. Also added a dedicated **"The End" guide category** (End Portal / Return Portal / Chorus
  Forest moved into it).
- **End City — built (v0.113.0).** The `end_city` seed: a 7×7 purpur tower (corner pillars, doorway, windows, an
  internal ladder, end-rod lighting, a ground-floor `end_city_treasure` chest) with a cantilevered **End ship** off the
  top — a tapering hull, a masted deck, a bow dragon head, and the reward chest (`skyseed:chests/end_ship`) holding a
  **guaranteed elytra**. **Shulkers** spawn on the island (theme mobs) for shells → shulker boxes. Crafted from purpur
  framing shulker shells around end stone (bootstrapped off the outer islands' vanilla End Cities); full onboarding +
  a structure-assembly gametest. The chapter's "grand loot dungeon," the Mansion / Bastion parallel.
- **Enderman farm — resolved: a pack rider, not its own seed.** The Chorus Forest already carries an enderman pack
  (ender pearls) and the End City a shulker pack, so a dedicated enderman/pearl-farm seed is redundant; dropped.
- **Deferred to Phase 5:** End-biome gating (e.g. End City only in highlands/midlands). Today every End seed grows
  wherever it's thrown; biome interactions are the Phase-5 job. **With these, the Phase-3 seed roster is complete.**

---

## Phase 4 — Interactions with current seeds

- **The ladder shaft already works in the End** (`ladder_small`/`ladder_large` have an `end` override → an end-stone
  foothold + the drop). Free transit, already shipped — just confirm it reads well over the End void.
- **Adaptation — built (v0.111.0).** The 10 overworld biome seeds and their large variants take **End form** via a
  `the_end` `biome_override` (like the Nether adaptations): a **same-size, pure end-stone island**, no ore / crops /
  trees / decoration — a barren foothold, not a content seed. The silhouette still varies per theme (each override
  carries its own shape, so a forest dome still reads differently from a rocky crag), and Aquatic's pond becomes an
  empty carved basin — an "empty lake" — for a touch of character. (End stone is the End's neutral default block, so the
  override only needs to carry the shape.) Other utility/structure seeds still refuse the End (the cleaner default).
- **The chorus bootstrap — built (v0.114.0).** **Forest and Lush** (+ their large variants) are the exception to the
  bare-end-stone rule: their `the_end` form grows **chorus plants** (chorus fruit) and carries a **small shulker chance**
  (shells). This is the whole End economy's bootstrap — a fresh void End grows *no* outer islands (`final_density 0`)
  and *no* End Cities (`generate-structures false`), so there is no natural chorus or shulker anywhere; the player
  brings a Forest/Lush seed from the overworld and throws it in the End to start the chain. Progression:
  **overworld Forest/Lush seed → End chorus island (chorus + rare shulker) → Chorus Forest seed** (renewable chorus →
  purpur, plus its own rare shulker) **→ purpur + shells → End City** (the reliable shulker source + elytra). Recipes
  stay thematic (chorus fruit / shulker shells); the guide entries + the End-Portal arrival hint point the player at it.
- **Standing rule:** every real seed gets a recipe + `skyseeds` tag + guide entry; debug seeds get none. End seeds
  inherit this. **Phase 4 is complete** (ladder transit + bare-end adaptation + the chorus bootstrap).

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

**Built (v0.115.0) — gating done; re-skin deferred.** The **End City** now gates to `end_highlands` / `end_midlands`
via a top-level `fizzle` rule (biomes `the_end` + `end_barrens` + `small_end_islands`, with a hinted message): it grows
in its native biomes and fizzles non-destructively elsewhere — reach the outer End through the dragon's gateway. Gated
through the existing `FizzleRule` / `IslandGenerator.formValidFor` machinery (no theme restructure needed since
`formValidFor` checks `fizzlesIn` before the base-dimension allow); guarded by `endCityGatesToHighlandsAndMidlands`.
The per-biome **re-skin is deferred**: in the void End the player rarely visits the outer biomes (everything is grown
near the `the_end` arrival), so re-skinning barrens/highlands is low-value and largely unseen — revisit if the End ever
gets natural terrain. **Phase 5's substantive item (gating) is done.**

---

## Phase 6 — …is that a dragon?!

**Yes.** The Ender Dragon is the End capstone — the parallel to the Wither Arena (Nether Star → Beacon). The whole mod's
finale.

- **The arena — the dragon fight is fixed; we don't touch its location.** *Playtest finding (revised plan):* entering
  the void End spawns the vanilla dragon fight automatically at the world origin, and it **works** — egg drops, a real
  fight, just harder without creative flight. `EndDragonFight` owns the central island, the pillars/crystals and the
  dragon, and we **cannot** relocate it — so the original "a dedicated arena seed that *grows* the central dragon setup"
  idea is **out** (a setup grown elsewhere would just sit empty while the dragon spawns at origin regardless). Lean into
  it: the fight is free and already functional. What we *can* still grow are **set pieces** that don't touch the fight or
  `EndDragonFight` — decorative end-stone islands, an approach causeway, a trophy room for the egg, a beacon plinth.
- **Dragon Trophy — built (v0.116.0).** The `dragon_trophy` seed grows a **Dragon Monument**: a stepped
  end-stone-brick dais, a purpur-capped pedestal for the egg (left **empty** — the egg is unique, so the player sets
  their own rather than the structure handing out a second), and four obsidian obelisks crowned with inward-facing
  **dragon heads**, end-rod lit. A pure set-piece (never touches the fight / `EndDragonFight`). Crafted from **dragon's
  breath** (bottled from the fight — post-dragon by construction) ringed in obsidian + end stone; grows anywhere in the
  End. The other suggested set-pieces (causeway, beacon plinth) are optional flavor, not built.
- **The central island — built (v0.109.0).** The root cause of the broken End was that the void End is `final_density:0`
  (no terrain), so the dragon's arena and the exit fountain's footing were pure void — only the bare bedrock egg-spike
  generated, which is why the fountain came out frameless and the fight happened over nothing. A worldgen feature
  (`skyseed:central_end_island`, added to `the_end` at raw_generation) now grows a domed end-stone disk (radius 42,
  surface y63) back at the origin, so the vanilla fountain, the four-crystal respawn, and the way-home portal all have a
  floor again. New worlds get it automatically; an existing End must be regenerated (`/emptyend force`) to grow it.
- **Respawn loop.** Re-summoning is a vanilla mechanic at the origin portal (four End Crystals on its frame) — nothing to
  relocate. The job is just making **End Crystals / obsidian obtainable** (a crystal recipe, or a "pillars" set-piece
  seed) for a repeatable fight. With the central island back (above), the bedrock exit-portal frame should now generate
  on it — *verify in-game* that the crystal-placement spots are there.
- **The rewards close the chapter:** the **dragon egg** (trophy), **dragon's breath** (lingering potions), the **End
  Gateway** opening to the outer islands → **End Cities → elytra + shulkers** (Phase 3). Flight is the literal and
  thematic top of the progression.
- **End Gateways over the void — must solve here.** Vanilla gateways open when the dragon dies and fling you ~1000 blocks
  out to the **outer islands** — which, in a void End, **don't exist**, so you'd arrive in empty sky and fall. Two fixes:
  **(a)** make the gateway land you safe — drop you onto a generated End island, or auto-place a small landing platform at
  the destination (the robust fix; hook the gateway block-entity's exit placement); **(b)** a guide warning to throw an
  End seed at the far side first (fragile — you can't easily seed a spot you haven't reached). *Recommend (a).* The
  pressure is low, though: because **End Cities are themselves a grown seed** (Phase 3), the gateway no longer *gates*
  content — it just needs to not kill you, so a safe landing platform is enough and the real outer-End loot you grow.
- **The way home — built (v0.106.0).** Playtesting confirmed the void-End gap above: defeating the dragon dropped the
  **egg** but lit **no central exit portal** — only an End Gateway out to the (pregenerated, kept) edge islands —
  because the bedrock exit fountain never generated at the origin. The **Return Portal Seed** (End-only, *end stone +
  ender pearls* → an end-stone shrine around an `end_portal` exit block) is the stopgap way out, independent of the
  dragon: plant it, step through, land at your overworld spawn. Since the dragon's own exit fountain doesn't generate in
  the void End (and per the revised arena note above we won't try to grow one the fight uses), this **is** the way home,
  not a stopgap.

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
