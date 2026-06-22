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
| A1 | **High** | Architecture | `IslandGenerator` is a 1,096-line god class; split it |
| A2 | **High** | Architecture | Structure-template duplication (`Built` ×14, `writeIfAbsent` ×8, `anchor`/`lootChest`/`jig`/`spawner` re-implemented) |
| B1 | ✅ done | Tooling | ~~No compiler lint~~ — `-Xlint:all` added (not `-Werror`), suppressions removed + warnings fixed |
| B3 | ✅ done | Tooling | ~~No automated tests~~ — GameTest suite added as the refactoring guard |
| B2 | Medium | Tooling | `.gitattributes` doesn't normalize source line endings (every commit warns) |
| D1 | Medium | Docs | Stale `IslandSeedEntity` class Javadoc ("placeholder", "milestone 4") |
| C1 | Medium | Quality | Null-as-sentinel returns; prefer `Optional` / document |
| C2–C5 | Low | Quality | FQN-over-import, brace/format inconsistencies, magic numbers, import order |
| P1 | Low | Perf | Jigsaw assembly placed un-budgeted in one tick |

---

## A. Architecture & refactors (highest value)

### A1 — `IslandGenerator` is a god class — **High**
`worldgen/IslandGenerator.java` is **1,096 lines** in one class, with a **~200-line `planIsland`** method
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

### A2 — Structure-template duplication — **High**
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
it chokes instrumenting Minecraft's huge `Blocks`/`Items`). Current run: **~73% line, 77% method, 84% class**.

| Package / class | Line cov. | |
|---|---|---|
| `worldgen/theme` (codecs) | **98%** | the `everyThemePlansWithoutError` test hits every record |
| `registry`, root, `gametest` | 93–100% | |
| `GenerationJob` | **77%** | `generationJobBuildsStructureIsland` drains a job (blocks, jigsaw, villager, golem, animals) + the germination tests |
| `IslandSeedEntity` | **71%** | `seedGerminatesIntoIsland` / `preciseSeedGerminatesAtTarget` (throw→germinate) + an NBT save/load round-trip |
| `worldgen/structure` | 69% | builders run at boot + the two placement tests |
| `worldgen` (`IslandGenerator`) | 64% | the `planIsland` tests |
| `client` / `event` / `network` | 0–21% | client + event paths a server-side gametest can't reach |

The honest read: the **generation core and the world-apply pipeline are now both well covered**. Remaining
gaps are the harder/edge paths — the pond water-mob spawn (no aquatic theme in the suite), the
overlap-fizzle branch, the projectile collision handlers, and the client/event code a server-side gametest
can't reach. Two tiny `skyseed:gametest/*` themes + the empty region template are test-only assets that
currently ship in the main jar (inert — no seed item references them); they'd move with the gametest source
set in the follow-up above.

### B2 — Line-ending normalization — **Medium**
`.gitattributes` only pins `src/generated/**` to LF. Source files aren't covered, so every commit prints
`warning: … LF will be replaced by CRLF` and one `.java` file is already CRLF while the rest are LF. Add:
```gitattributes
*.java   text eol=lf
*.json   text eol=lf
*.gradle text eol=lf
```
(or a blanket `* text=auto eol=lf`). Satisfies `instructions.md`'s "consistent line endings".

### B4 — Dead template clutter — **Low**
`build.gradle` still carries the NeoForge MDK's example comments (JEI, `coolmod`, flat-dir repo,
`localRuntime` essay). Harmless, but trimming the unused examples reduces noise.

---

## C. Code quality & conventions

### C1 — Null as sentinel — **Medium**
Several methods return `null` to mean "none/skip": `IslandGenerator.resolveBlock`/`resolveEntity`/
`pondBed`/`pondShore`/`matchOverride`/`pickVariant`, `IslandSeedEntity.getTheme`/`resolveTheme`. `null`
is idiomatic in MC modding, so this is a judgement call rather than a bug — but to align with
`instructions.md`'s null-avoidance intent, prefer `Optional<T>` for the resolver/picker helpers (or at
least add `@return … or {@code null}` Javadoc so callers know). The callers already null-check correctly,
so this is about clarity, not correctness.

### C2 — Fully-qualified names where an import exists — **Low**
- `entity/IslandSeedEntity.java:41` — `extends net.minecraft.…ThrowableItemProjectile` (not imported).
- `worldgen/IslandGenerator.java:265` — `java.util.Set` though `Set` is imported (`:51`).
- `worldgen/IslandPlan.java:49` — `net.minecraft.resources.ResourceLocation` ×2 inline.
Import them for consistency with the rest of the codebase.

### C3 — Brace / one-statement-per-line inconsistency — **Low**
`IslandGenerator.pondBed`/`pondShore` (`:933–936`, `:942–944`) use braceless single-line `if`s, and
`pondColumns`/`riverColumns` (`:957–958`, `:965`) pack multiple statements per line — both against the
house style used everywhere else (braces, one statement per line).

### C4 — Magic numbers & a duplicated noise routine — **Low**
Gameplay-tuning constants are inline: bulge exponent `0.85` (`:138`), deep-core fraction `0.4` (`:474`),
vein face-grow chance `0.80` (`:523`), `16` seed tries (`:497`), pad clear-height `10` (`:313`), pond
extent `0.5·radius` (`:952`). Many are comment-explained; promoting the *tunable* ones to named
constants would aid balancing. The **rim-harmonic routine** (`freq{2,3,5}` + amp/phase normalization) is
**duplicated** at `:99–110` and `pondColumns:953–958` — extract a `RimNoise.sample(angle, …)` helper.

### C5 — Import ordering — **Low**
`GenerationJob.java` interleaves `net.minecraft.core.*` (`:20–22`) among `net.minecraft.world.*` imports
and puts `DyeColor` (`:15`) between two `npc` imports. `instructions.md` ("organize imports") — let the
IDE re-sort; otherwise low impact.

---

## D. Documentation

### D1 — Stale class Javadoc — **Medium**
`entity/IslandSeedEntity.java:35–39` still says germination produces *"a placeholder stone platform. The
real procedural island replaces the placeholder in milestone 4"* — long superseded by the full theme
system. Rewrite to describe current behaviour (arm timer → `IslandGenerator.planIsland` →
`GenerationJob`). Worth a quick grep for other "milestone N"/"placeholder" comments that may have aged.

---

## E. Performance (minor)

- **P1** — `GenerationJob.placeStructures()` (`:78`) runs `JigsawPlacement.generateJigsaw` for the whole
  structure in a **single tick**, un-budgeted — a potential frame spike for the big assemblies
  (Woodland Mansion + wings, Village Center) that the per-block budget otherwise avoids. It's one vanilla
  call so hard to chunk; worth a comment acknowledging the trade-off, or deferring large structures a
  tick.
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
