# Skyseed — 26.1.2 GameTest Harness Plan (SKYGAMETESTPLAN)

**Goal.** Stand up a **fresh, independent gametest suite for the 26.1.2 node** using the new (1.21.5+)
`GameTestInstance` framework, so 26.1.2 has its own behavioural regression coverage. **Aim 80–90% line coverage**
of the mod classes (multi-session is fine). Use the existing 1.21.1 suite (`SkyseedGameTests`, 128 tests) as
*inspiration* — port bodies, re-derive coverage — not as a file to mutate.

**Hard constraint — leave 1.21.1 alone.** The 1.21.1 suite stays exactly as-is (the golden-master witness:
`islandOutputIsStable` + 126 passing tests). It must keep compiling + passing on the `:1.21.1:` node unchanged, so
it remains a stable regression check that does **not** drift as the 26.1.2 port evolves. The two suites are fully
isolated (different packages, per-version compile exclude — see Architecture).

---

## Why a new suite at all (the API shift)

26.1.2 **removed the annotation-based registration** the 1.21.1 suite uses:
- `@GameTestHolder` / `@GameTest` / `@PrefixGameTestTemplate` (`net.neoforged.neoforge.gametest.*`) — **gone**.
- Tests are now `GameTestInstance` objects registered into the `minecraft:test_instance` registry, with the
  environment in `minecraft:test_environment` and (optionally) the body in `minecraft:test_function`.

**What survived (so bodies port near-verbatim):** `net.minecraft.gametest.framework.GameTestHelper` is intact —
`assertTrue(boolean,String)`, `assertFalse`, `assertValueEqual`, `succeed()`, `getLevel()`, `absolutePos(BlockPos)`,
`getBlockEntity(...)`, etc. all present. So a 1.21.1 test body copies across with only the production-side facade
swaps already done in Stage 2b (`Ids`/`Lookup` instead of raw `ResourceLocation`/`registryOrThrow`).

**The registration replacement (decompile-confirmed, 26.1.2):**
- `net.neoforged.neoforge.event.RegisterGameTestsEvent` — a **mod-bus** event. Registers **in code, no datapack
  JSON required**:
  - `Holder<TestEnvironmentDefinition<?>> registerEnvironment(Identifier name, TestEnvironmentDefinition<?>...)`
  - `void registerTest(Identifier name, GameTestInstance test)`
- `GameTestInstance` (abstract): `run(GameTestHelper)` + `MapCodec<? extends GameTestInstance> codec()`; constructed
  with a `TestData<Holder<TestEnvironmentDefinition<?>>>`.
- `TestData` record: `(environment, Identifier structure, int maxTicks, int setupTicks, boolean required,
  Rotation rotation, boolean manualOnly, int maxAttempts, int requiredSuccesses, boolean skyAccess, int padding)`.
- `FunctionGameTestInstance(ResourceKey<Consumer<GameTestHelper>> function, TestData)` exists, but the function must
  be pre-registered into `Registries.TEST_FUNCTION` (a separate step `RegisterGameTestsEvent` does **not** expose).
  → we **skip it** in favour of a tiny custom `GameTestInstance` that wraps the `Consumer` directly (one-step, all
  inside the event). See Approach.

---

## Architecture

### Source layout (isolation without a second Gradle source set)
- New package: **`dev.gemberkoekje.skyseed.gametest26`** (name is an open decision — see below) under the existing
  `src/main`. Stonecutter copies `src/main` to every node; **build.gradle excludes the wrong suite per node**:
  ```groovy
  if (mcv == "1.21.1") {
      sourceSets.main.java { exclude("dev/gemberkoekje/skyseed/gametest26/**") }   // new suite: 26.1.2+ only
  } else {
      sourceSets.main.java { exclude("dev/gemberkoekje/skyseed/gametest/**") }     // old suite: 1.21.1 only
  }
  ```
  Result: `gametest/**` compiles only on 1.21.1; `gametest26/**` compiles only on 26.1.2. Zero cross-talk, no new
  source set / ModDevGradle wiring. (A true separate `src/gametest26` source set is the alternative if we ever want
  them co-compiled, but that needs extra ModDevGradle run-config plumbing under Stonecutter — not worth it here.)

### Registration mechanism (code-only)
- `SkyseedTests26` — a `@EventBusSubscriber(bus = MOD)` (or `Skyseed`-wired) handler with one
  `@SubscribeEvent static void onRegisterGameTests(RegisterGameTestsEvent e)` method.
- Register one shared environment once, then one line per test via a small helper:
  ```java
  static Holder<TestEnvironmentDefinition<?>> ENV;
  static void onRegisterGameTests(RegisterGameTestsEvent e) {
      ENV = e.registerEnvironment(Ids.mod("default"));            // AllOf() — no special batch setup
      reg(e, "island_generates_blocks", REGION, Tests::islandGeneratesBlocks);
      reg(e, "rocky_adapts_in_the_nether", REGION, Tests::rockyAdaptsInTheNether);
      ... // ~one line per ported test
  }
  static void reg(RegisterGameTestsEvent e, String name, Identifier structure, Consumer<GameTestHelper> body) {
      TestData<Holder<TestEnvironmentDefinition<?>>> data = new TestData<>(
          ENV, structure, /*maxTicks*/100, /*setupTicks*/0, /*required*/true,
          Rotation.NONE, /*manualOnly*/false, /*maxAttempts*/1, /*requiredSuccesses*/1,
          /*skyAccess*/false, /*padding*/0);
      e.registerTest(Ids.mod(name), new SkyseedTest(data, body));
  }
  ```
- `SkyseedTest extends GameTestInstance` — the wrapper:
  ```java
  final class SkyseedTest extends GameTestInstance {
      private final Consumer<GameTestHelper> body;
      SkyseedTest(TestData<...> data, Consumer<GameTestHelper> body) { super(data); this.body = body; }
      @Override public void run(GameTestHelper h) { body.accept(h); }
      @Override public MapCodec<? extends GameTestInstance> codec() { return DUMMY; }
      // never serialized (RegistrationInfo.BUILT_IN); unit codec satisfies the abstract contract.
      static final MapCodec<SkyseedTest> DUMMY = MapCodec.unit(() ->
          { throw new UnsupportedOperationException("Skyseed gametests are code-registered, not serialized"); });
  }
  ```
- Test bodies: keep them as **`static void name(GameTestHelper)`** methods (mirrors the 1.21.1 file), grouped in
  the new package, referenced by method-ref in `reg(...)`.

### Structure templates (already shipped)
- The suites use `skyseed:gametest/region` (16×24×16) and `skyseed:gametest/big_region`. Both `.nbt` live in
  `src/main/resources/data/skyseed/structure/gametest/` and are **version-independent committed resources** → they
  ship on the 26.1.2 jar unchanged. `DevStructureGenerator` being disabled on 26.1.2 (Stage 2b) only stops
  *regeneration*; the committed `.nbt` still loads. ✅ No action needed.

---

## Phase 0 — the spike (de-risk, ~1 short session)
1. Add the build.gradle per-version exclude (above) + the new package with **`SkyseedTest`** + `SkyseedTests26`
   wiring **one** ported test (`islandGeneratesBlocks` — the simplest).
2. `:1.21.1:runGameTestServer` must stay green (126) — proves the exclude isolates correctly.
3. `:26.1.2:runGameTestServer` must run **1 test, pass**. This validates the open risks in one shot:
   - the `codec()` dummy doesn't trip registry validation at startup,
   - `RegisterGameTestsEvent` fires + the test is discovered (`neoforge.enabledGameTestNamespaces=skyseed`),
   - the committed `gametest/region.nbt` loads on 26.1.2,
   - the GameTestServer run config works on the 26.1.2 node at all.
4. Commit the spike. **If the codec/registration risk bites**, fall back to the `FunctionGameTestInstance` +
   `TestFunctionLoader.registerLoader` two-step (documented above) — same end state, slightly more plumbing.

## Phases 1–4 — rollout by category (port bodies, re-derive coverage)
Port in coverage-value order, tracking `gameTestCoverage` after each. The 1.21.1 taxonomy (counts are *inspiration*,
not a quota — port what earns coverage toward 80–90%):

| Phase | Category (1.21.1 section)                         | #   | Notes |
|------:|---------------------------------------------------|----:|-------|
| 1 | generation invariants (IslandGenerator split)        | 60  | Bulk of the coverage; pure `planIsland` + `IslandPlan` asserts, fast, no block placement. Highest ROI. |
| 2 | world-apply pipeline (throw→germinate→GenerationJob) | 7   | The only ones that place blocks (BIG_REGION); cover `IslandSeedEntity`/`GenerationJob` end-to-end. |
| 3 | structure templates (template de-dup guards)         | 11  | Assert spawners/cages/vaults survive; cover the structure builders. |
| 4 | book/icon coverage helpers                           | 48  | Guide/lang/model completeness; some touch client-ish code — port what counts toward the target, skip the 26.1.2-stubbed model hook. |

After each phase: `:26.1.2:runGameTestServer` green + coverage delta recorded here. Stop when ≥80–90%.

---

## Coverage measurement
- Existing JaCoCo task **`gameTestCoverage`** (build.gradle ~L228–253): `dependsOn runGameTestServer`,
  `sourceSets main`, report → `build/reports/jacoco/gameTestCoverage/html/index.html`. It wires the JaCoCo agent into
  the ModDevGradle GameTestServer run manually (the run is not a JavaExec/Test task).
- **TODO (Phase 0/1):** confirm `:26.1.2:gameTestCoverage` works on the 26.1.2 node (the agent-wiring is per-node);
  if the task isn't node-aware, add a 26.1.2 analog. Target **80–90% of the mod classes** (exclude generated/datagen).
- The 1.21.1 coverage number is the reference bar — match or beat it on 26.1.2.

---

## Risks / open decisions
- **`codec()` dummy** — primary spike risk. If registry validation calls `codec()`/encodes built-in instances, switch
  to `FunctionGameTestInstance` (functions via `TestFunctionLoader.registerLoader`). Resolved in Phase 0.
- **GameTestServer on 26.1.2** — the `runGameTestServer` run config (build.gradle L97–112) is assumed node-portable;
  Phase 0 proves it.
- **Open decision — package name.** `gametest26` vs `gametestng` (next-gen) vs `gametest_modern`. Default proposed:
  `gametest26`. Trivial to rename before Phase 1.
- **Open decision — environment.** Start with a single empty `AllOf()` environment; split per-batch later only if a
  test needs special setup (none obviously do — the 1.21.1 suite uses one implicit batch).

---

## Status log
- **★ Phase 0 DONE (commit `df20812`, 2026-06-28) — the harness is validated end-to-end on 26.1.2.**
  `:26.1.2:runGameTestServer` BUILD SUCCESSFUL (the ported `island_generates_blocks` runs + passes); `:1.21.1:`
  stays 126 green. Confirmed facts (save re-deriving):
  - **`GameTestInstance` has THREE abstract methods, not two:** `run(GameTestHelper)`, `codec()`, **and
    `protected MutableComponent typeDescription()`** (display label — return any `Component.literal(...)`). The first
    spike compile failed only on the missing `typeDescription`.
  - **The `codec()` dummy is fine** — `MapCodec.<SkyseedTest>unit(() -> { throw … })` satisfies the contract and is
    never invoked for `RegistrationInfo.BUILT_IN` tests (the run never serializes them). No `TEST_INSTANCE_TYPE`
    registration needed. (Fallback path documented above is unnecessary.)
  - **`@EventBusSubscriber(modid = Skyseed.MODID)` auto-routes `RegisterGameTestsEvent` to the mod bus** (no `bus=`).
  - **The 26.1.2 GameTestServer run needs `JAVA_HOME = jdk-26.0.1`** (the node toolchain is `java_26.1.2=25`; JDK 26
    satisfies it). The 1.21.1 run uses JDK 21.
  - **A NeoForge/vanilla built-in test runs alongside ours** — the run reported "All **2** required tests passed"
    with one test registered. Harmless; just don't read the raw count as the Skyseed test count. (Watch when Phase 1
    lands the bulk — the delta from the registered count should stay +1, not ×2.)
  - **Two runtime data issues the compile/build milestone couldn't catch** (they need a live server — `:build` never
    loads registries):
    1. **FIXED — void noise_settings.** `void`/`void_nether`/`void_end` failed registry load: the 26.1.2
       `noise_router` codec added a **required** `preliminary_surface_level` density function. Added `0.0` (a void
       has no surface) as the first `noise_router` field in all three. 1.21.1's lenient codec ignores the extra key
       → **one shared JSON loads on both** (verified). This is the [[skyseed-refactor]] "void noise-settings standing
       rule" / "re-verify void_* load" risk, now closed.
    2. **NOT fixed (separate production-data scope, non-fatal):** the seed crafting recipes log ~45 parse errors on
       26.1.2 — the **1.21.5 ingredient format change** (`{"item":"x"}`/`{"tag":"x"}` → plain `"x"`/`"#x"`). Unlike
       noise_settings, the formats are **mutually exclusive** between versions, so they can't share one JSON — needs
       per-version recipe resources or datagen. Doesn't block tests (recipes aren't exercised by the gen suite).
  - **JaCoCo 0.8.12 can't instrument Java 25 (class major 69)** → the `gameTestServer` agent is now guarded to the
    1.21.1 node. **26.1.2 coverage tooling is the open Phase-1 question** (a Java-25-capable JaCoCo if/when one ships,
    or an alternative). Until then, 26.1.2 coverage can't be numerically measured — port by category/inspection and
    revisit the tool.
- **★ Phase 1 COMPLETE — 57 generation-invariant tests ported + green** (`:26.1.2:runGameTestServer` = "All 58
  passed" incl. the +1 built-in; commits `144a915`/`6ab0294`/`881c6b7`/`f7b7bca`/`771f1d5`, 5 batches, 2026-06-28).
  The 1.21.1→26.1.2 port is purely mechanical — only three idioms differ, funnelled through helpers in `SkyseedTests`:
  `biome(level, Biomes.X)` and `biome(level, "ns:path")` (was
  `registryAccess().registryOrThrow(BIOME).getHolderOrThrow(...)`), `Lookup.dimensionId(Level.X)` (was
  `Level.X.location().toString()`), and `Lookup.blockId(block)` (was `BuiltInRegistries.BLOCK.getKey(...).getPath()`).
  Plus one block rename to watch: `Blocks.CHAIN` → `Blocks.IRON_CHAIN` (copper update; the committed 1.21.1-era
  structure `.nbt` is datafixer-upgraded on load, so the pier test passes).
  **Three generation tests deliberately deferred** (noted in-code at the registration block):
  `everySeedRecipeAndBookEntryMatchesSeedKind` + `everyCraftableSeedHasUniqueIcon` need the book/icon resource helpers
  (Phase 4), and the recipe half blocks on [[skyseed-refactor]]/SKYRECIPEGENPLAN; `legacyDimensionResetRewrites-
  GeneratorSettings` exercises the level.dat `/emptynether` reset that's a no-op on 26.1.2 (and slated for removal).
- **★ Phases 2 + 3 COMPLETE — +17 tests (75 ported total; "All 76 passed" incl. the built-in)** (commits `63ead1d`
  world-apply +6, `8d79916` structure +11, 2026-06-28). **Phase 2 (world-apply, 6 of 7):** the thrown-seed
  germinate→GenerationJob pipeline (entity ticking via `succeedWhen` + `maxTicks` overload; mob spawns through the
  26.1.2 subpackages `animal.cow.Cow`/`animal.golem.IronGolem`/`npc.villager.Villager`); `seedStateRoundTripsThroughNbt`
  deferred (drives `addAdditionalSaveData(CompoundTag)` directly — 26.1.2 reworked it to ValueOutput/ValueInput).
  **Phase 3 (structure templates, all 11):** template placement via `level.getServer().getStructureManager().get(id)`
  + `placeInWorld`; jigsaw-NBT `filterBlocks` via `CompoundTag.getStringOr` (getString → `Optional` on 26.1.2); the
  golem via the `Entities` compat helper; biome overrides via `biome()`. Also added a Modonomicon coverage test
  (`modonomicon_guide_book_is_complete_and_degrades`) when SKYMODONOMICONPLAN Phase 2 landed.
- _Next: Phase 4 — book/icon (48 tests, the big one) + the 3 deferred tests
  (`everySeedRecipeAndBookEntryMatchesSeedKind`, `everyCraftableSeedHasUniqueIcon`, `seedStateRoundTripsThroughNbt`).
  It needs the book/icon resource helpers (1.21.1 source lines ~1918–3097) and pairs with SKYRECIPEGENPLAN (recipes
  now load) + SKYMODONOMICONPLAN (validate BOTH guide backends stay complete)._

## Resolved decisions
- **Package = `gametest_26_1_2`** (Java can't have dots/digit-led segments, so the literal "26.1.2" renders with
  underscores; per-version packages let more nodes get isolated suites later).
- **Environment = one empty `AllOf()`** (`Ids.mod("default")`) — sufficient; no per-batch setup needed.
