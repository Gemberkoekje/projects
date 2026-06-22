# Skyseed — Code Review

A full pass over `src/main/java` (~5,800 lines, ~60 files) plus build/config. Findings are grouped by
category and tagged by priority. File references are `path:line`.

## Note on `instructions.md`

`projects/instructions.md` is a **C# best-practices guide** (var, async/await, LINQ, `IDisposable`,
file-scoped namespaces, nullable reference types, `GlobalUsings.cs`, `.globalconfig`). **Skyseed is a
Java / NeoForge 1.21.1 mod**, so the C#-specific rules don't apply. The *general* principles that do
translate are applied below: least-privilege access, null avoidance, specific exceptions, one type per
file, import hygiene, no trailing whitespace, consistent line endings, resolve warnings, validate input,
automated tests. Two rules don't translate cleanly and are called out where relevant:
- *"All enums should have an empty value"* — Skyseed's enums (`OreDepth`, `Underside`) are datapack
  `StringRepresentable` codec enums; an EMPTY value would need a serialized name and isn't idiomatic.
- *"Do not use nullables"* — Java has no `?` annotations to remove; the analogue is "prefer `Optional` /
  document nullable returns", covered in C1.

## What's already good (keep doing it)

- **Plan → apply architecture** (`IslandGenerator.planIsland` returns a pure `IslandPlan`; `GenerationJob`
  drains it under a per-tick budget). Clean separation of "compute" from "mutate the world".
- **Graceful degradation**: unknown blocks/entities/features are logged and skipped, never crash gen
  (`resolveBlock`, `resolveEntity`, `planDecoration`, …).
- **Server-authoritative networking with input validation** (`SkyseedNetwork.handleThrow`): re-checks the
  held item, clamps a malicious precise-target to max range. This is exemplary — use it as the model.
- **Deterministic RNG** seeded from `worldSeed ^ center` for reproducible islands.
- Hygiene: **no** trailing whitespace, **no** `TODO/FIXME/HACK`, **no** `System.out`/`printStackTrace`,
  only **one** broad `catch` (dev-only, justified), and **no** `@SuppressWarnings` (the 3 it had were removed
  and the underlying warnings fixed — see B1). Most files carry accurate Javadoc.

---

## Priority summary

| # | Priority | Area | Item |
|---|----------|------|------|
| A1 | ✅ done | Architecture | ~~`IslandGenerator` is a 1,096-line god class~~ — split into a ~290-line orchestrator + 6 planners (`ShapeBuilder`/`OrePlanner`/`PondCarver`/`DecorationPlanner`/`CustomTrees`/`MobPlanner`) |
| A2 | ✅ done | Architecture | ~~Structure-template duplication~~ — consolidated into `StructureParts` + a shared `Built` record (~160 lines removed) |
| B1 | ✅ done | Tooling | ~~No compiler lint~~ — `-Xlint:all` added (not `-Werror`), suppressions removed + warnings fixed |
| B3 | ✅ done | Tooling | ~~No automated tests~~ — GameTest suite added as the refactoring guard |
| B2 | ✅ done | Tooling | ~~`.gitattributes` doesn't normalize line endings~~ — `* text=auto eol=lf` + binary rules added |
| B4 | ✅ done | Tooling | ~~Dead MDK example clutter in `build.gradle`~~ — JEI/coolmod/flat-dir/sister-project examples trimmed |
| D1 | ✅ done | Docs | ~~Stale `IslandSeedEntity` Javadoc~~ — rewritten to the current germinate → plan → `GenerationJob` flow |
| C1 | ✅ done | Quality | ~~Null-as-sentinel returns~~ — documented with `@return … or {@code null}` Javadoc (null is idiomatic in MC) |
| C2–C5 | ✅ done | Quality | ~~FQN-over-import, brace/format, magic numbers, import order~~ — fixed; rim-harmonic routine extracted to `RimNoise` |
| P1 | ✅ done | Perf | ~~Un-budgeted jigsaw assembly~~ — documented the trade-off + the defer-a-tick escape hatch |

---

## A. Architecture & refactors (highest value)

### A1 — `IslandGenerator` is a god class — **✅ done**
Split into a ~290-line orchestrator (`planIsland` + the config-resolution helpers + `levelStructurePad`) and six
package-private collaborators in `worldgen/`: `ShapeBuilder` (pass-1 terrain, returns the radius/dome/core-Y),
`OrePlanner`, `PondCarver` (takes the resolved water `BlockState`, so `resolveBlock` stays in the orchestrator),
`DecorationPlanner` (delegating the hand-built trees to `CustomTrees`), and `MobPlanner` — plus a shared `Scatter`
record. `planIsland` threads one `RandomSource` through them in the original order, so the RNG sequence (and thus
every island) is unchanged. **Verified by the `islandOutputIsStable` golden master**: all fingerprints byte-identical,
17/17 gametests pass, coverage held (orchestrator 99.3%; new classes 96.8–100%). The original analysis is kept below.

### A1 (original) — `IslandGenerator` is a god class
`worldgen/IslandGenerator.java` was **1,096 lines** in one class, with a **~200-line `planIsland`** method
(`:64–262`) orchestrating config resolution, shape, ores, rare structures, ponds, jigsaw, decoration,
waterfalls, and mobs. The private statics already cluster into cohesive responsibilities — extract them:

- `ShapeBuilder` — pass-1 solid island + rim noise (`:99–165`).
- `OrePlanner` — `planOres`/`pickSeed`/`growVein` (`:468–541`).
- `PondCarver` — `carvePond`/`containPond`/`pondColumns`/`riverColumns`/plants/banks (`:816–1086`).
- `DecorationPlanner` — `planDecoration`/`placeGround`/`planUnderside`/`hangUnder` (`:543–682`).
- `CustomTrees` — `buildMangrove`/`buildAzalea`/`buildIceSpike` (the "skyseed:* feature" hand-builds,
  `:684–771`) — these are genuinely a separate concern (vanilla-feature substitutes).
- `MobPlanner` — `rollAnimals`/`planMobs`/`planPondMobs` (`:269–402`).

`planIsland` then reads as a short pipeline. This is the single biggest readability/maintainability win.

### A2 — Structure-template duplication — **✅ done**
Consolidated: a package-level `Built` record replaces the 14 nested copies, and `StructureParts` now owns
`writeIfAbsent`, `jig`, `mobSpawner` (alongside the pre-existing `anchor`/`lootChest`/`gableRoof`/`suspicious`),
which the templates call via a static import. The re-implemented `anchor`/`lootChest` copies in
Dungeon/Outpost/Animal are gone (they call the shared ones, passing their own `final_state`/loot-table). Net
**~160 lines** removed across 15 files. **Verified behaviour-preserving:** regenerating all 46 `.nbt` produced
**zero diffs** (byte-identical), and the 16 gametests pass. (`trialSpawner` was deliberately *not* merged — the
Vault Cell's is normal-config-only while the Trial Chamber's adds an ominous config, so they genuinely differ.)
The original analysis below is kept for context.

### A2 (original) — Structure-template duplication
The 14 `*Templates` classes copy the same scaffolding instead of sharing it:

- `private record Built(Map<BlockPos,BlockState>, Map<BlockPos,CompoundTag>) {}` — **re-declared in 14
  files**.
- `private static void writeIfAbsent(Path, Built)` — **8 files**.
- `anchor(...)` and `lootChest(...)` — **re-implemented** in `DungeonTemplates`, `OutpostTemplates`,
  `AnimalTemplates` **even though `StructureParts` already provides both**. (`StructureParts.lootChest`
  exists; `OutpostTemplates:152` and `DungeonTemplates` declare their own copies.)
- `jig(name,target,pool,finalState)` connector BE — **4 files** (`TradePost`, `TrialChamber`,
  `VillageCenter`, `WoodlandMansion`).
- `spawner(mobId)` mob/trial-spawner BE — **4 files**; the **`trialSpawner` helper is duplicated**
  between `RareStructureTemplates` and `TrialChamberTemplates` (already noted in project memory).

**Refactor:** promote the shared pieces into `StructureParts` (it's already the "shared building blocks"
home) or a small `TemplateBuilder` base:
- a shared `Built`/`Template` type + `writeIfAbsent`,
- `StructureParts.jig(...)`, `StructureParts.mobSpawner(mobId)`, `StructureParts.trialSpawner(mob)`,
- delete the per-file `anchor`/`lootChest` copies and call the existing `StructureParts` ones.

Net: removes well over 100 lines of copy-paste and gives one place to fix a BE-NBT schema (which has
already bitten this project — the trial-spawner schema needed a fix in two places).

---

## B. Build & tooling

### B1 — Compiler lint — **✅ done**
`build.gradle` now compiles with **`-Xlint:all` + `options.deprecation = true`**, **deliberately not
`-Werror`** (warnings stay visible as reminders rather than becoming failures that pressure people into
suppressing them). All **three `@SuppressWarnings` were removed** and the four real warnings they hid were
**fixed properly, not re-suppressed**: `Mob.finalizeSpawn` → the NeoForge `EventHooks.finalizeMobSpawn`
(fires the spawn event, the non-deprecated path), and `BlockStateBase.blocksMotion()` → a position-aware
`getCollisionShape(level, pos).isEmpty()` check (agrees with the old check for every block that occurs at a
spawn column). The tree now compiles **warning-free** under `-Xlint:all`; any new warning will show in the
build log. (Spawn behaviour re-verified in-game: pasture animals + meadow mobs still spawn.)

### B3 — Automated tests — **✅ started (the refactoring guard)**
Previously there were **no tests** (the whole pipeline was hand-verified over RCON). A **NeoForge GameTest
suite** now lives at `gametest/SkyseedGameTests.java` (9 tests), run with **`./gradlew runGameTestServer`**
(exits 0 on pass; also via `/test runall` in dev). It asserts *invariants*, not byte output, so it
survives the planned refactors while still catching a real regression:
- **Generation** (guards the `IslandGenerator` split): non-empty output, determinism (same seed → same
  plan), bottom-up sort, ore presence, jigsaw site recorded, mansion garrison planned, and
  `everyThemePlansWithoutError` — plans **every** registered theme and requires non-empty output.
- **Structures** (guards the template de-duplication): the outpost keeps its spawner/cage/chest; the trial
  hub keeps its breeze boss + ominous vault.

Still worth adding later: pond-containment ("no water hangs off the rim" — needs to exclude intentional
rim waterfalls), island overlap rejection (`isTooCrowded`), and codec round-trips for the theme records.
*(The GameTest sources currently ship in the main source set, gated by namespace; moving them to a
dedicated `gametest`/test source set so they don't ship in the jar is a low-priority follow-up.)*

**Coverage** is measured with **`./gradlew gameTestCoverage`** → `build/reports/jacoco/gameTestCoverage/`.
JaCoCo can't use its task extension here (the gameTestServer is a ModDevGradle run, not a `JavaExec`/`Test`
task), so the agent is attached as a JVM argument, scoped to `includes=dev.gemberkoekje.skyseed.*` (otherwise
it chokes instrumenting Minecraft's huge `Blocks`/`Items`). Current run: **~76% line, 78% method, 84% class**.

| Package / class | Line cov. | |
|---|---|---|
| **`IslandGenerator`** (the A1 refactor) | **99%** | the `planIsland` tests + targeted themes for river ponds, mangroves, waterfalls and bad ids |
| `worldgen/theme` (codecs) | **98%** | the `everyThemePlansWithoutError` test hits every record |
| `registry`, root, `gametest` | 93–100% | |
| `GenerationJob` | **77%** | `generationJobBuildsStructureIsland` drains a job (blocks, jigsaw, villager, golem, animals) + the germination tests |
| `IslandSeedEntity` | **71%** | `seedGerminatesIntoIsland` / `preciseSeedGerminatesAtTarget` (throw→germinate) + an NBT save/load round-trip |
| `worldgen/structure` | 69% | builders run at boot + the two placement tests |
| `client` / `event` / `network` | 0–21% | client + event paths a server-side gametest can't reach |

The honest read: **`IslandGenerator` — the biggest refactor (A1) — is now ~99% covered** (the last 8 lines are
unreachable defensive branches: null returns, exact-centre edge cases). The world-apply pipeline is covered too.
Remaining package gaps are the projectile collision handlers, the overlap-fizzle branch, and the client/event
code a server-side gametest can't reach. The five tiny `skyseed:gametest/*` themes + the empty region template
are test-only assets that currently ship in the main jar (inert — no seed item references them); they'd move
with the gametest source set in the follow-up above.

### B2 — Line-ending normalization — **✅ done**
Added `* text=auto eol=lf` + binary rules (`.nbt`/`.png`/`.ogg`/`.jar`); the per-commit CRLF warning is gone.

### B2 (original) — Line-ending normalization
`.gitattributes` only pins `src/generated/**` to LF. Source files aren't covered, so every commit prints
`warning: … LF will be replaced by CRLF` and one `.java` file is already CRLF while the rest are LF. Add:
```gitattributes
*.java   text eol=lf
*.json   text eol=lf
*.gradle text eol=lf
```
(or a blanket `* text=auto eol=lf`). Satisfies `instructions.md`'s "consistent line endings".

### B4 — Dead template clutter — **✅ done**
Trimmed the unused MDK examples from `build.gradle` (the JEI / `coolmod` flat-dir / file / sister-project
dependency comments + the stray gradle-doc links and the `data` working-directory example). Kept the
`localRuntime` config + comment (it's real — Patchouli rides on it). The lint/coverage blocks are unchanged.

---

## C. Code quality & conventions

### C1 — Null as sentinel — **✅ done**
Took the documented-null route (the codereview's lighter option; `null` is idiomatic in MC modding, and the
callers already null-check correctly): added `@return … or {@code null}` Javadoc to `MobPlanner.resolveEntity`,
`IslandGenerator.matchOverride`/`pickVariant`, `PondCarver.pondBed`/`pondShore`, and
`IslandSeedEntity.getTheme`/`resolveTheme`. (`resolveBlock` was *not* in scope — it always returns a non-null
fallback.) Left as-is rather than churning every helper to `Optional`.

### C1 (original) — Null as sentinel
Several methods return `null` to mean "none/skip": `IslandGenerator.resolveBlock`/`resolveEntity`/
`pondBed`/`pondShore`/`matchOverride`/`pickVariant`, `IslandSeedEntity.getTheme`/`resolveTheme`. `null`
is idiomatic in MC modding, so this is a judgement call rather than a bug — but to align with
`instructions.md`'s null-avoidance intent, prefer `Optional<T>` for the resolver/picker helpers (or at
least add `@return … or {@code null}` Javadoc so callers know). The callers already null-check correctly,
so this is about clarity, not correctness.

### C2 — Fully-qualified names where an import exists — **✅ done**
- `IslandSeedEntity` — imports `ThrowableItemProjectile` and `extends` the simple name.
- `IslandPlan.JigsawSite` — imports `ResourceLocation`, uses it for both fields.
- The `java.util.Set` FQN in the old `IslandGenerator` was resolved by the A1 split (the `AQUATIC` set moved to
  `MobPlanner`, which imports `Set`).

### C3 — Brace / one-statement-per-line inconsistency — **✅ done**
`PondCarver.pondBed`/`pondShore` now brace every `if`, and `pondColumns`/`riverColumns` are one statement per
line (the packed harmonic loop in `pondColumns` went away entirely with the `RimNoise` extraction — see C4).

### C4 — Magic numbers & a duplicated noise routine — **✅ done**
The duplicated **rim-harmonic routine** is now a shared `RimNoise` class (`sample(random, strength)` rolls the
six doubles in the original order; `rim(base, angle)` evaluates the wobbled radius), called by both
`ShapeBuilder` and `PondCarver.pondColumns` — behaviour byte-identical (the golden master confirms it). Tunable
constants were named: `ShapeBuilder.DEPTH_BULGE_EXP` (0.85), `OrePlanner.DEEP_CORE_FRACTION` (0.4) /
`FACE_GROW_CHANCE` (0.80) / `SEED_TRIES` (16), `PondCarver.POND_EXTENT_FRACTION` (0.5) / `POND_RIM_WOBBLE`
(0.28), `IslandGenerator.PAD_CLEAR_HEIGHT` (10). The pond bed/shore probability thresholds were left inline
(comment-clear, not balance knobs).

### C5 — Import ordering — **✅ done**
`GenerationJob` imports re-sorted alphabetically — the `core`/`resources` imports no longer interrupt the
`world.*` block, and `DyeColor` no longer sits between the two `npc` imports.

---

## D. Documentation

### D1 — Stale class Javadoc — **✅ done**
`IslandSeedEntity`'s class Javadoc now describes the real flow (arm timer → resolve theme →
`IslandGenerator.planIsland` → `GenerationJob`), drops the broken `{@link #germinate()}` link (the method takes
a `ServerLevel` now), and corrects "~3 s" → "~2 s". A grep confirmed no other "milestone N"/"placeholder"
comments remain in `src/main/java`.

---

## E. Performance (minor)

- **P1** — ✅ **done (comment).** `GenerationJob.placeStructures()` runs `JigsawPlacement.generateJigsaw` for
  the whole structure in a **single tick**, un-budgeted — a potential frame spike for the big assemblies
  (Woodland Mansion + wings, Village Center) that the per-block budget otherwise avoids. It's one vanilla call
  so it can't be chunked without reimplementing jigsaw placement; a Javadoc note now records the trade-off and
  the escape hatch (defer large structures to a later tick than the blocks/trees) if it ever bites.
- **P2** — `IslandGenerator` keys its working map on `BlockPos` objects; a packed-`long` map would cut
  allocations on big islands. Micro-optimization; current cost is acceptable.
- **P3** — `IslandSeedEntity.isTooCrowded` (`:177`) does up to a few thousand `getBlockState` × 4 nudge
  steps at germination; fine because it's rare and early-exits at the threshold.

---

## Suggested order of work

0. **B3** (tests) — ✅ **done first**, on purpose: the GameTest suite is the safety net for everything below.
1. **B2** (line endings) and **D1** (stale Javadoc) — minutes each, stop the commit noise / doc drift.
2. **A2** (template de-duplication) — mechanical, high payoff, low risk; do before adding more structures.
   Guarded by `outpostHasSpawnerAndCage` / `trialHubHasBossAndOminousVault`.
3. **B1** (lint) → triage warnings → consider `-Werror`.
4. **A1** (`IslandGenerator` split) — larger; do once A2 establishes the shared-helpers pattern. Guarded by the
   generation tests (run `runGameTestServer` before and after — block counts/determinism must match).
5. **C1–C5** as cleanup alongside the above.
6. Extend the test suite (pond containment, overlap rejection, codec round-trips).
