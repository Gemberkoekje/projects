# Changelog — Minecraft 26.1.2 build

Notable changes to the **26.1.2** Skyseed build. Skyseed is one codebase built for multiple Minecraft versions (see
`REFACTORPLAN.md`); the **1.21.1** build's history is in [CHANGELOG_1.21.1.md](CHANGELOG_1.21.1.md). Both builds share
the version-number sequence, so a version can appear in one changelog and not the other.

> **Status: Stage 2 COMPLETE — the 26.1.2 build compiles, builds, and passes its gametests.** Both version nodes are
> green (`./gradlew chiseledBuild` / per-node `:1.21.1:` / `:26.1.2:` tasks; CI builds both). The remaining work is
> Stage 3 (generalize/document) in `REFACTORPLAN.md`. The per-feature build plans (the gametest harness, the recipe
> generator, and the Modonomicon guide) shipped and were retired into this changelog.

## [0.162.0] - 2026-06-28

### Changed
- **Trimmed the superfluous found-it explanation from "The Rare Catch" intro** (the "A green [x] turns up under each
  once you've gathered the makings" line) — players notice the checkmarks without being told. Removed from the shared
  Patchouli source, so both guide backends drop it.

## [0.161.0] - 2026-06-28

### Fixed
- **The Modonomicon guide really stops showing "found it!" now.** 0.158.0 dropped the entry-level reveal *condition*,
  but the false "found it!" was the page-level checklist in "The Rare Catch": each `[x] … found it!` page is gated by a
  page-level `advancement` that Patchouli hides until earned — and Modonomicon doesn't honour page gates, so they all
  showed. `generateGuide` now drops those gated progress-checklist pages from the Modonomicon book. (The Patchouli book
  keeps them; page gating works there.)

## [0.160.0] - 2026-06-28

1.21.1-only fix (see [CHANGELOG_1.21.1.md](CHANGELOG_1.21.1.md)): the 0.157.0 guide-icon fix crashed the 1.21.1 client
at startup (`RegisterAdditional` rejected the `inventory` variant). **No 26.1.2 change** — its guide icon was never
affected (it uses the generated `items/guide.json` definition).

## [0.159.0] - 2026-06-28

### Fixed
- **The seed throw wind-up (raise-to-throw) animation plays again.** The port mapped 1.21.1's `UseAnim.SPEAR` to the
  literal `ItemUseAnimation.SPEAR`, but on 26.1.2 that enum split: the trident raise (what 1.21.1's SPEAR was) is now
  `ItemUseAnimation.TRIDENT`, while `SPEAR` is a new spear-weapon animation that shows no wind-up for a thrown item.
  The seed now returns `TRIDENT`. Visual only — throwing and landing already worked.

## [0.158.0] - 2026-06-28

### Fixed
- **The Modonomicon guide no longer shows "found it!" on entries nothing has been found for.** Patchouli's per-entry
  `advancement` (reveal-when-found) was translated to a `modonomicon:advancement` condition, which Modonomicon renders
  as an always-already-met "found it!" completion flag — worse than no gating. `generateGuide` no longer emits the
  condition, so Modonomicon entries are simply always visible. (The Patchouli book keeps reveal-on-found.)

## [0.157.0] - 2026-06-28

1.21.1-only fix (see [CHANGELOG_1.21.1.md](CHANGELOG_1.21.1.md)): the Modonomicon guide-book icon. **No 26.1.2
change** — the book already renders the Skyfarer's Almanac there via its generated `items/guide.json` definition (0.156.0).

## [0.156.0] - 2026-06-28

The first real `:26.1.2:runClient` session surfaced runtime issues the headless gametests can't (no client model load, no integrated-server→client handshake). All fixed.

### Fixed — 26.1.2 runClient
- **`runClient` no longer hangs on "Loading terrain".** `test_instance` is a network-synced registry, so the client handshake (`RegistrySynchronization.packRegistry`) serializes every gametest — and the code-registered tests' codec threw. Registered a real `skyseed:gametest` codec in `TEST_INSTANCE_TYPE` and backed `SkyseedTest.codec()` with it (encode is all the handshake needs; decode → no-op). New gametest `every_test_instance_serializes_for_client_sync` guards it. Verified: the client reaches "joined the game".
- **All item icons render.** On 1.21.5+ every item needs an `assets/<ns>/items/<id>.json` definition or it renders as the missing-texture checkerboard; Skyseed shipped none (1.21.1 uses the old `models/item/` system + a bake hook, which has no base model to copy here). A new `generateItemModelDefinitions` task emits one per item — the committed seed/relic/edge/guide models **and** the generated debug-seed models (237 total). Missing-item-model warnings: ~230 → 0.
- **The Modonomicon guide book shows the Skyfarer's Almanac icon** (its `items/guide.json` definition + the book's `model: skyseed:guide` field), instead of the default brown book.
- **Dropped the obsolete global-loot-modifier index** (`data/neoforge/loot_modifiers/global_loot_modifiers.json`): on 1.21.5+ NeoForge loads each `loot_modifiers/` file as a codec GLM, so the legacy `{replace,entries}` index logged `ERROR: No key type`. The per-relic GLMs load directly and still drop.

## [0.155.0] - 2026-06-28

The whole 26.1.2 port landed under this version (1.21.1 stays byte-for-byte identical — the shared data additions below
are inert on 1.21.1, so its build is functionally unchanged).

### Added — the 26.1.2 build
- **Production code compiles + builds on 26.1.2** (NeoForge `26.1.2.76`, Java 25), driven entirely from `compat`
  directives. ~18 months of MC + NeoForge churn resolved: `ResourceLocation`→`Identifier` (the crux, 171×, via
  String-id codecs + the facade), the entity NBT rewrite (`ValueInput`/`ValueOutput`), `SavedData`→Codec, the recipe
  API, the `LootModifier` codec, GameRules→registry, the spawn/respawn API, the client model/key APIs, mob-class
  reorg, `MobSpawnType`→`EntitySpawnReason`, `registryOrThrow`→`lookupOrThrow`, and the scattered 1-offs. The void
  noise-settings gained `preliminary_surface_level` (shared JSON; verified by `void_worldgen_setup_loads_and_is_void`).
- **A native 26.1.2 gametest harness — 134 tests** (was *GAMETESTPLAN*). A separate `gametest_26_1_2` source set on the
  new `GameTestInstance` framework (the old `@GameTest`/`@GameTestHolder` annotations were removed), registered via
  `RegisterGameTestsEvent`. Covers all four phases (generation invariants, world-apply, structure, book/icon incl.
  recipe-resolution, loot, and a 26.1.2-captured golden master that is 4/5 byte-identical to the 1.21.1 suite). The
  1.21.1 suite stays frozen as the regression witness.
- **Golden-source recipe generation** (was *RECIPEGENPLAN*). Recipes are authored once as "golden" (modern string-
  ingredient form) under `recipes/`; the `generateRecipes` Gradle task emits version-correct JSON per node (26.1.2
  verbatim, 1.21.1 downgraded to `{item}`/`{tag}`). A `recipes/_modern_only/` subtree is version-gated out of the
  1.21.1 build.
- **The guide is now an optional Modonomicon book** (was *MODONOMICONPLAN*), preferred over Patchouli on every version
  (Patchouli kept as a first-class fallback; graceful if both are installed). The Modonomicon book is generated by
  `generateGuide` from the golden Patchouli content; `$(br)`/`$(li)` emit Markdown hard breaks so single line breaks
  render. `SkyseedGuide.book()` walks Modonomicon → Patchouli → written book.
- **All production stubs wired to real APIs:** `ThemeScanner` walks `IModFile.getContents().visitContent` (the 152 auto
  debug seeds regenerate); the bonus chest reads `server.getWorldGenSettings().options()`; `FMLEnvironment.isProduction()`;
  the auto-debug-seed icon hook uses `ModelEvent.ModifyBakingResult.getBakingResult().itemStackModels()`.
- **CI / multi-version build (Stage 3 start):** `chiseledBuild` + `chiseledRunGameTestServer` fan a task across all
  version nodes; a `ci-skyseed.yml` GitHub Actions matrix builds + gametests each node on its JDK (1.21.1→21, 26.1.2→25).

### Added — worldgen content (the 1.21.4 / 1.21.5 delta; inert on 1.21.1)
- **Pale Garden** — a `pale_garden` biome override on the Forest line (pale oak, pale moss, eyeblossom, hanging moss),
  plus a **dedicated 26.1.2-only Pale Garden seed** (pale-oak with creaking hearts → a Creaking at night). The seed is
  the template for modern-only content: `//?`-gated `SEED_THEMES` entry, modern-only recipe, tag-based advancements,
  `required:false` `#skyseeds` tag entry, and a guide-gen filter.
- **1.21.5 vegetation** — leaf litter, bush, firefly bush, wildflowers, golden dandelion (forest, meadow); short/tall
  dry grass, cactus flower (desert, badlands); and **fallen logs** (forest/taiga/jungle — a jar-diff caught these).
- **New mobs** — nautilus + zombie nautilus (aquatic), parched + camel husk (desert), happy ghast (huge meadow, a sky-
  mount reward), copper golem (the big village). The cow/pig/chicken biome-temperature variant defaults automatically
  through the existing spawn path (`finalizeMobSpawn` → biome selection) — verified, no change.
- A vanilla **jar diff confirmed 0 new structures / 0 new structure sets / 1 new biome** (pale_garden) between 1.21.1
  and 26.1.2; all 109 new blocks are obtainable.

### Added — bootstrap (Stage 2a, historical)
- The `26.1.2` node (NeoForge `26.1.2.76`, Java 25) added to the Stonecutter matrix, with per-node MC / NeoForge / Java
  / Parchment / Patchouli selection from the version-keyed root `gradle.properties`.
