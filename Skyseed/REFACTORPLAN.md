# Skyseed — Multi-Version Refactor (NeoForge + Stonecutter)

**Goal.** Build Skyseed against multiple **Minecraft / NeoForge versions** from one codebase, by (1) isolating the
version-volatile API calls behind a thin **facade** so the algorithm stays version-stable, and (2) using
**Stonecutter** to manage the per-version build matrix. **NeoForge-only. No new runtime dependency.**

**Out of scope.** Fabric / cross-loader. If that ever happens it gets its own plan (that's what Architectury is for);
Architectury solves the *loader* split and does nothing for the version axis, so it's deliberately not used here.

---

## How Stonecutter works (so the plan is self-contained)

- A Gradle **settings plugin** declares a list of versions — each a "node" pinning its MC version, NeoForge version,
  Parchment mappings, etc. Stonecutter creates one build per node.
- Source is preprocessed with **comment directives** — `//? if >=1.21.4 {` … `//?}` (and `/*? if … {*/ … /*?}*/` to
  swap an expression) — so a line/block is active only for matching versions and compiles against that version's API.
- `./gradlew build` builds the **active** version (set by `stonecutter.active`, switchable for the IDE);
  `./gradlew chiseledBuild` builds **all** versions → one jar each.
- The actual MC/NeoForge dependency resolution stays with the existing build plugin (**ModDevGradle**); Stonecutter
  wraps it and feeds each node its versions. **Build-time only — players install nothing extra.**

---

## The version-volatile surface (what actually changes between versions)

The audit shows the bulk of Skyseed is version-stable: the generation math, the planners, the codec data model
(stable `Codec` API), and the structure `.nbt` builders (stable `BlockState`). The churn is concentrated:

- **Registry access** — `BuiltInRegistries.BLOCK`, `registryOrThrow(Registries.X)`, `RegistryAccess` (shape shifts
  across versions).
- **`ResourceLocation` construction** — `new ResourceLocation(...)` → `fromNamespaceAndPath` / `withDefaultNamespace`
  (changed in 1.21; keeps moving).
- **`JigsawPlacement.generateJigsaw(...)`** — the structure-assembly signature changes occasionally.
- **The NeoForge glue** — registration (`DeferredRegister`), the event bus + event classes, the **network/payload
  registration** (moved a lot recently), `ModConfigSpec`, the datapack-registry event. **This is the most
  version-volatile area across NeoForge builds**, and where most directives will end up.

**Strategy:** route all of this through a small **`compat` facade** with stable internal signatures, so when an API
changes between versions the Stonecutter directives live in a handful of named files — never in the algorithm.

---

## Approach

1. A **`compat` package** concentrating the volatile calls behind stable internal signatures (e.g. `Compat.block(id)`,
   `Compat.biome(access, id)`, `Compat.placeJigsaw(...)`, plus the registration / network / config / event helpers).
   The rest of the code calls the facade and never touches the volatile APIs directly.
2. **Stonecutter** drives the version matrix; the genuine per-version diffs are comment directives **inside the
   facade** (inline only where a facade can't hide it).
3. **Single NeoForge module** — no module split (that was the Architectury route; not needed for one loader).

---

## Migration stages (each ends green: `runGameTestServer` passes on the active version)

0. **Stonecutter skeleton + build proof — ✅ DONE** (branch `refactor/stonecutter-spike`). Stonecutter 0.9.6 wraps
   ModDevGradle on Gradle 9.2.1 (config cache on); tasks run under the node (`./gradlew :1.21.1:…`; `chiseledBuild`
   builds every node); `versions/` + `.stonecutter/` gitignored.
1. **Concentrate the volatile surface into `compat` — ✅ DONE.** `Ids` (every `ResourceLocation`), `Lookup` (registry
   access) and `Jigsaw` (the one `generateJigsaw` site) front the version-volatile calls across ~18 files;
   behaviour-preserving (the `islandOutputIsStable` golden master stays byte-identical). The gametest keeps direct calls
   as the golden-master oracle — route it when Stage 2 lands.
2. **Add the second MC/NeoForge version — target chosen: Minecraft `26.1.2` / NeoForge `26.1.2.76`.** Add the
   Stonecutter node; resolve the per-version compile diffs with directives — almost all landing in `compat`. Get
   **both** versions building and each one's gametests passing. **Detailed, data-driven plan: see
   [Stage 2 in detail](#stage-2-in-detail--target-mc-2612--neoforge-261276) below.**
3. **Generalize + document.** Add further versions as wanted; write the "how to add a version" recipe (a node + the
   expected directive sites); optionally a CI matrix (`chiseledBuild`).

> **Priority (per the maintainer): 1.21.1 was the only target while the chapters were built; Stage 2 is now active.**
> Stages 0–1 (the structural value) landed first; the **second version's driver is the worldgen content newer versions
> add** — and the jar diff below confirms `26.1.2` brings a real worldgen payload (the Pale Garden biome + the 1.21.5
> vegetation) on top of ~18 months of API churn. Stage 1 (the golden-master-guarded `compat` refactor) was the on-ramp.

---

## Stage 2 in detail — target MC 26.1.2 / NeoForge 26.1.2.76

> Every figure here is from **diffing the actual vanilla client jars** (1.21.1 vs 26.1.2, pulled from Mojang), not
> from changelogs — the changelogs misled (they list Poplar / Cinnabar / Sulfur, which the jar shows are **26.2**, not
> 26.1.2). `26.1.2` is a stable hotfix on the year-based 2026 line (1.21.1 → … → 1.21.11 → 26.1 → 26.1.2); 26.2 is in
> beta. Verified prerequisites: **NeoForge `26.1.2.76` resolves** on `maven.neoforged.net` (pom 200); the dev box has
> **JDK 26** (≥25) and **JDK 21**.

### 2.1 Toolchain & build wiring

- **Java 25 is mandatory** for 26.1.2 (the version JSON pins `javaVersion.majorVersion = 25`); 1.21.1 stays **Java 21**.
  JDK 26 runs it (≥25) but Gradle toolchain matching is by exact major — install/pin a **JDK 25** for that node, or set
  its `languageVersion` to 26 if you accept JDK 26 there.
- **NeoForge** uses the 4-component `<mcMajor>.<mcMinor>.<mcPatch>.<build>` scheme → `26.1.2.76`.
- **Patchouli** is version-pinned (`1.21.1-93-NEOFORGE`); needs a 26.1.2 build, else guard its `compileOnly`/
  `localRuntime` off for that node (the mod already runs without it — the guide falls back to a written book).
- **Parchment** for 26.1.2 may lag; omit it for that node if unpublished (Mojmap still compiles, just fewer param names).

**Per-node version values can't live in `versions/<v>/gradle.properties`** — the whole `versions/` tree is gitignored
and Stonecutter-regenerated. Keep them **version-keyed in the root `gradle.properties`** and select by
`stonecutter.current.version` at the top of `build.gradle`:

```properties
# gradle.properties — per-node, selected in build.gradle
mc_1.21.1=1.21.1
neo_1.21.1=21.1.233
java_1.21.1=21
parchment_mc_1.21.1=1.21.1
parchment_1.21.1=2024.11.17
patchouli_1.21.1=1.21.1-93-NEOFORGE
mc_26.1.2=26.1.2
neo_26.1.2=26.1.2.76
java_26.1.2=25
# parchment_26.1.2 / patchouli_26.1.2 omitted until published → those features skip for this node
```
```groovy
// settings.gradle
stonecutter { create(rootProject) { versions("1.21.1", "26.1.2"); vcsVersion = "1.21.1" } }

// build.gradle (top, before `version = ...`)
def mcv = stonecutter.current.version
ext.minecraft_version = property("mc_${mcv}")
ext.neo_version       = property("neo_${mcv}")
def javaVersion       = (property("java_${mcv}") as int)
java.toolchain.languageVersion = JavaLanguageVersion.of(javaVersion)   // was hard-coded 21
// parchment { … } and the Patchouli compileOnly/localRuntime → wrap in `if (project.hasProperty("parchment_${mcv}"))` / `…("patchouli_${mcv}")`
```

> **Sequencing — important.** Adding the node makes ModDevGradle set up the 26.1.2 **NeoForm decompile** (downloads +
> Java 25), and Skyseed will **not compile** against 26.1.2 until the `compat` directives in §2.2 exist. So the node
> flip is the **start of the compat work**, not a free-standing "bootstrap": do it on a branch, keep
> `:1.21.1:runGameTestServer` green as the guard, and don't merge until both nodes build.

### 2.2 The version-volatile API surface (where the `//?` directives go)

~18 months of MC + NeoForge churn (1.21.2 → 26.1). Expect directives **inside `compat` only**:

- **`Ids` / `Lookup` / `Jigsaw`** (exist) — re-verify `ResourceLocation`, `RegistryAccess`, and `JigsawPlacement.generateJigsaw` signatures on 26.1.2.
- **Registration** (`DeferredRegister`, registry keys) → likely a new `compat/Registration`.
- **Network / payload registration** — NeoForge's most-churned area → `compat/Net`.
- **Events** (mod bus, the datapack-registry event, spawn-placement) → `compat/Events`.
- **Data components / `BlockBehaviour.Properties` / item `Properties`** → `compat/*Props`.
- **`world_gen_settings.dat` (26.1):** WorldGenSettings moved out of `level.dat` into `data/<world>/world_gen_settings.dat`. Two impacts: (a) the legacy `/emptynether` `/emptyend` **level.dat-editing rescue commands break on 26.1.2** — they're already slated for removal pre-1.0, now also version-gated; (b) **re-verify the void dimensions load** — the `void` / `void_nether` / `void_end` noise-settings + the world-preset still drive the baked dimension generator, but the noise-settings/world-preset **codec shape may have shifted** (the vanilla `noise_settings` *names* are unchanged in the jar diff, which is reassuring). This is the **first concrete compat check** and touches the `void_*` standing rule.

### 2.3 The worldgen delta 1.21.1 → 26.1.2 (jar diff) → Skyseed's response

> **For Skyseed, mobs ARE worldgen.** Islands get no ambient spawns — the generator *places* every creature (theme
> `mobs` packs, animal pens, structure mob packs). So new mob **types** and new mob **variants** are part of this
> delta exactly like a block or a biome: Skyseed has to decide where each one goes.

**1 new biome · 0 new structures · vanilla noise-settings stable in name · 109 new blocks · 8 new placeable mobs + a biome-temperature variant system**, bucketed:

| Delta (from the jar diff) | In Skyseed's worldgen? | Skyseed response |
|---|---|---|
| **Biome `pale_garden`** | yes | A `pale_garden` `biome_override` on the Forest line (a Forest seed over a pale garden → a pale variant), and/or a dedicated **Pale Garden island theme**. Version-inert on 1.21.1 (see §2.4). |
| **Pale Garden blocks** — pale-oak wood set, pale moss / carpet / hanging moss, open/closed eyeblossom, creaking heart, resin block / bricks (+ slab/stairs/wall/chiseled) / clump (~40) | yes | Theme content: pale-oak trees, pale-moss surface + scatter, hanging-moss underside, eyeblossom/resin decoration + creaking hearts. Block-completeness: all obtainable. |
| **1.21.5 vegetation** — bush, wildflowers, firefly bush, leaf litter, short/tall dry grass, cactus flower, golden dandelion (~8) | yes | Decoration entries on existing themes: forest/meadow → bush/wildflowers/firefly bush/leaf litter; desert/badlands → dry grass + cactus flower. |
| **New mobs** — `creaking` (+ transient), `nautilus` + `zombie_nautilus`, `happy_ghast`, `copper_golem`, `parched`, `camel_husk`, `mannequin` | **yes — placement needed** | Slot into theme `mobs` packs / structures: **creaking** → the Pale Garden theme (with its creaking hearts); **nautilus / zombie_nautilus** → the Aquatic island (deep lake / ocean / aquarium); **happy_ghast** → the dried-ghast mechanic (a Nether soul-sand placement and/or a sky-mount reward); **copper_golem** → a copper-themed build/structure (player-built, but Skyseed can place one); **parched / camel_husk** → desert/badlands packs (verify their natural spawn rules first); **mannequin** = a display entity → likely no placement. |
| **Mob variants** — `cow_variant` / `pig_variant` / `chicken_variant` = cold/temperate/warm (plus wolf/cat/frog as data registries) | **yes — auto, but verify** | These resolve from the **biome temperature** at the spawn position, so Skyseed's existing pasture / farm / animal placements pick up the right variant per island biome for free (a Frozen pasture → cold cows). Verify the placement API still lets the variant default by biome; force one only if a theme wants a specific variant. |
| **Copper expansion** (blocks) — bars/chains/chests/lanterns/torches + oxidation + waxed, oxidizing lightning rods, iron chain (~55) | no (crafted) | Block-completeness only — all craftable from copper, already obtainable. |
| **Wooden shelves** ×12, **dried ghast** (block), test blocks ×2 | no | Block-completeness (shelves/dried-ghast craftable); test blocks = parity exclusion. |

So the **generation** work is: a **Pale Garden theme** (blocks + the creaking), the **1.21.5 vegetation** in existing themes, **placing the new aquatic / desert / sky mobs** into the themes that fit, and **confirming the cow/pig/chicken variant defaults** by biome — plus a **re-run of the block-completeness audit** for the craftable remainder.

### 2.4 The single-codebase data strategy (the key lever)

A 1.21.1 build **must not reference an id that doesn't exist in 1.21.1** (`pale_garden`, `pale_oak_log`, `bush`, …) or
its datapack fails to load. Rather than maintain per-version resource source-sets, **make Skyseed's theme /
decoration / biome-override / ore codecs tolerant of unknown block/biome/feature ids — skip-with-log instead of
hard-fail.** Then the **same dataset** (the Pale Garden override + the 1.21.5 decoration) ships to both nodes: active
on 26.1.2, **inert on 1.21.1**. This is a small, testable loader change and removes almost all per-version data
divergence. The residual hard cases — a renamed vanilla id, a shifted worldgen codec, the `void_*` settings if their
codec moved — still take a guarded data variant or a `//?` directive; handle those when they bite.

### 2.5 Stage 2 sub-steps (each ends with **both** nodes green)

- **2a — node wiring + compile.** §2.1 config; run the 26.1.2 NeoForm; drive the compile errors into `compat`
  directives (§2.2) until 26.1.2 compiles. 1.21.1 stays green throughout.
- **2b — tolerant codecs** (§2.4) — the unknown-id skip; a gametest proving a forward-referencing theme loads inert.
- **2c — re-audit blocks on 26.1.2** — the 109 new ids through the completeness rule (expect all craftable → pass).
- **2d — worldgen content** — the Pale Garden theme + the 1.21.5 vegetation decoration + **the new-mob placements**
  (creaking → Pale Garden, nautilus → Aquatic, etc.) and the cow/pig/chicken variant check, all version-inert on 1.21.1.
- **2e — per-version golden master + gametests**; write the "add a version" recipe. Route the gametest's remaining
  direct API calls through `compat` (the Stage-1 to-do).

### 2.6 The 26.1.2 compile — decompile proven + the live API delta (2026-06-28)

On branch `refactor/26.1.2-compat`. **`:26.1.2:compileJava` runs the full NeoForm pipeline cleanly** — download →
decompile (46 s) → patch → recompile (6882 files) — on the box's JDK 26 (no JDK-25 download needed). **This retires
the #1 risk below** (Stonecutter ↔ ModDevGradle on NeoForge). The decompile is cached, so subsequent compiles are fast.

Skyseed's own source then produced **101 errors** (javac's first-100 cap — more will surface once the big one is
fixed). The root causes, confirmed against the decompiled Mojmap sources jar:

| Old (1.21.1) | New (26.1.2) | Where |
|---|---|---|
| **`net.minecraft.resources.ResourceLocation`** | **`net.minecraft.resources.Identifier`** (a pure rename) | **everywhere — 171 occurrences across 31 files**; the dominant task |
| `net.minecraft.world.InteractionResultHolder<T>` | `net.minecraft.world.InteractionResult` (holder merged in; `.success(item)` shape changed) | `IslandSeedItem` |
| `net.minecraft.world.item.UseAnim` | `net.minecraft.world.item.ItemUseAnimation` | `IslandSeedItem` |
| `…projectile.ThrowableItemProjectile` | `…projectile.throwableitemprojectile.ThrowableItemProjectile` (subpackage) | `IslandSeedEntity` |
| `…client.resources.model.ModelResourceLocation` | moved/renamed (verify) | `SkyseedClientEvents` |
| NeoForge `…gametest.GameTestHolder` / `PrefixGameTestTemplate` | moved/removed — the gametest-registration API changed | `SkyseedGameTests` |
| `vazkii.patchouli.api.*` | none for this node (Patchouli build pending) | `PatchouliCompat` → needs a `//?` stub |
| `…entity.animal.Cow` / `…npc.Villager` / `…animal.IronGolem` | animal/npc package reorg (verify) | mob placement |

**Strategy for the `ResourceLocation` → `Identifier` rename (the crux).** Java has no import alias, so a renamed type
used 171× can't be hidden behind a single import directive. Two levers, used together:
1. **Confine the type to the facade** — switch the codec records (`Variant`/`Palette`/`OreEntry`/`BiomeOverride`/
   `Pond`/`MobEntry`/`JigsawConfig`/`AnimalPack`/`RareStructure`/`IslandTheme`/`FizzleRule`) to store the **raw id
   `String`** (`Codec.STRING`) instead of `ResourceLocation`, resolving to the MC id type via `Ids`/`Lookup` at
   use-time. This deletes most of the 171, is behaviour-preserving for valid ids (the golden master guards it), **and
   directly enables §2.4** (an unknown id is just a String that resolves to nothing → skip). 
2. **Per-file `//?` import + usage swaps** for the residual — the facade (`Ids`/`Lookup`/`Jigsaw`) and the few entity /
   client / gametest files where the id type genuinely lives.

Then the other rows are small per-file directives (mostly one import + one call-site each). **Order:** the
`String`-records refactor first (verified green on 1.21.1, no 26.1.2 needed), then the facade/file directives, then
recompile to surface the post-100 remainder, repeat to green.

### 2.7 The 26.1.2 shipped-code error map (120 errors) + per-category fix plan

The crux (the `ResourceLocation`→`Identifier` rename) is **DONE and validated** — recompiling `:26.1.2:` shows zero
`Identifier` errors. The gametest harness is **excluded** on non-1.21.1 nodes (it uses the removed pre-1.21.5
`@GameTest` API; it stays the 1.21.1 golden-master witness and is ported separately). With those out, the shipped code
has **120 errors** (true count, `-Xmaxerrs` lifted), which cluster into ~10 root deltas. Each fix is a `//?` block —
**1.21.1 branch = current code (must stay green via `runGameTestServer`), 26.1.2 branch in `/*…*/`, Javadoc-free** —
verified by recompiling `:26.1.2:` and watching per-file/per-symbol counts (the 100-cap hides the total until <100).

The per-category plan, highest-leverage first (each links a category of the 120 to its concrete fix):

| # | Category (errors) | 26.1.2 API (from the cached sources jar) | Fix |
|---|---|---|---|
| 1 | **Mob class reorg** (~10) — `Cow`/`Sheep`/`Bee`/`IronGolem`/`Villager`/`VillagerType` | moved into per-type subpackages: `animal.cow.Cow`, `animal.sheep.Sheep`, `animal.bee.Bee`, `animal.golem.IronGolem`, `npc.villager.Villager`/`VillagerType` (names + API unchanged) | `//?` import swaps (GenerationJob + any other user) |
| 2 | **`MobSpawnType`→`EntitySpawnReason`** (~5) | `net.minecraft.world.entity.EntitySpawnReason`, `.SPAWNER` constant kept | `//?` a **static import** of `SPAWNER`; rewrite `MobSpawnType.SPAWNER`→`SPAWNER`. Then recheck `EntityType.spawn(...)` / NeoForge `EventHooks.finalizeMobSpawn(...)` arg type |
| 3 | **`registryOrThrow`→`lookupOrThrow`** (~5, multi-file) | `RegistryAccess.lookupOrThrow(key)` returns `Registry<E>` on 26.1.2 (registryOrThrow removed) | funnel registry access through `Lookup.registry(...)` where possible, `//?` that one method; `//?` the residual direct callers (WorldData, …) |
| 4 | **Entity API** (IslandSeedEntity, the ~19 left after the superclass import fix) | `ThrowableItemProjectile` **constructor** signature changed; `moveTo`/`getRespawnPosition`/`hasImpulse` etc. | per-method `//?`; read each new signature from the jar |
| 5 | **Commands API** (~14) | `displayClientMessage`/`hasPermission`/`getSharedSpawnPos`/`setDefaultSpawnPos`/`getServer`/`worldGenOptions` shifted | per-call `//?`; several are Player/Server/ServerLevel method renames |
| 6 | **WorldData / level API** (~8) | `getDataVersion`/`findResource`/`getMinBuildHeight`/`getMaxBuildHeight`/`worldGenOptions` | per-call `//?` |
| 7 | **Recipe** (~7) | `SimpleCraftingRecipeSerializer` + its `Factory` changed | `//?` the serializer construction |
| 8 | **Client / rendering** (~6) | `ModelResourceLocation` moved; `KeyMapping` ctor gained a param; `registerEntityRenderer`/`getModels` shifted | `//?` imports + ctor/call sites (client-only, no gametest coverage — compile-checked only) |
| 9 | **`CHAIN`** (~4) — in structure templates (Bastion/Outpost) | a `Blocks`/property constant moved/renamed | identify the new constant, `//?` or direct swap |
| 10 | **`GameRules`** (~2) + scattered 1-offs (`getAsString`, `production`, `Factory`) | misc renames | per-site `//?` |

**Execution order:** 1→3 first (mob reorg + spawn-reason + registry — highest error count, mostly import/static-import
swaps), then the file-local clusters (entity, commands, worlddata, recipe), then client + the 1-offs. Recompile after
each cluster; the count only visibly drops below 100 once the bulk is cleared, so track the per-symbol histogram.

**Progress (2026-06-28): 120 → 49.** Cleared this session also: the IslandSeedEntity port (the 1.21.5 NBT rewrite
`addAdditionalSaveData(ValueOutput)`/`readAdditionalSaveData(ValueInput)` — `ValueOutput` keeps the `put*` names,
`ValueInput` uses `getIntOr`/`getString().ifPresent`/`getDoubleOr`; imports `//?`-guarded as those classes don't
exist on 1.21.1); the projectile ctor's new `ItemStack` arg; **`compat.Players`** (`displayClientMessage`→
`ServerPlayer.sendSystemMessage`, `teleportTo`'s new `Set<Relative>`+`resetCamera` args); `Lookup.elements`
(`holders`→`listElements`) + `Lookup.biomeHolder` (`getHolder`→`get`); `Entity.hasImpulse`→`hurtMarked`;
`EntityType.Builder.build(ResourceKey)`; `swapDimensionSettings`→no-op on 26.1.2 (level.dat WorldGenSettings is gone).
**The remaining 49 are the genuinely hard part — and several have NO 26.1.2 gametest coverage (compile-validated only):**
THREE meaty rewrites — (a) **`SavedData`** became Codec-based (`SavedDataType(Identifier, Supplier, Codec)`; the
`save(CompoundTag)`/`load` model is gone) in `SkyseedWorldData`; (b) the **recipe API** (`CustomRecipe` ctor +
`getSerializer`/`matches`/`assemble` shapes, `SimpleCraftingRecipeSerializer` gone) in `GuideRecipe`/`ModRecipes`;
(c) **`LootModifier`** codec/ctor in `AddDropModifier`. Plus deep/scattered renames: the **command/spawn API**
(`CommandSourceStack.hasPermission`→`PermissionSet`, `Level.getSharedSpawnPos`, `ServerPlayer.setRespawnPosition`→
`RespawnConfig`), **client** (`ModelResourceLocation` moved, `ModelEvent.getModels`, client `displayClientMessage`,
`KeyMapping.Category`), `Item.releaseUsing` now returns `boolean`, `ChunkPos(BlockPos)` ctor + `MaxDistance` (Jigsaw),
`WorldVersion.getDataVersion`→`dataVersion()`, `IModFile.findResource`, `FMLEnvironment.production`. These want a
focused pass (the NeoForge sources jar is located, per below).

**★★★ DONE — 120 → 0. `:26.1.2:build` BUILD SUCCESSFUL (last commit `c2ad486`, 2026-06-28); the full 26.1.2 jar
`skyseed-26.1.2_0.155.0.jar` assembles, 1.21.1 green 126 tests every step.** All three meaty rewrites landed clean
on 26.1.2 first try (SavedData→Codec+SavedDataType; recipe MapCodec/StreamCodec+RecipeSerializer record;
LootModifier priority ctor). The scattered renames resolved as: GameRules→registry (`set(rule,v,server)`,
`RULE_DISABLE_RAIDS=true`→`RAIDS=false` **inverted**, `SPAWN_PATROLS`); spawn→`setRespawnData`/`getRespawnData().pos()`
+`LevelData.RespawnData`, player→`ServerPlayer.RespawnConfig`/`getRespawnConfig`; `Commands.hasPermission(LEVEL_GAMEMASTERS)`;
client action bar `mc.gui.setOverlayMessage` (cross-version); `releaseUsing`→boolean+`ClientPacketDistributor`;
`KeyMapping.Category` record; `registerItem` Supplier; `ChunkPos(x>>4,z>>4)`+`JigsawStructure.MaxDistance`;
`dataVersion().version()`; `Entity.getServer()`→`level().getServer()`. **Pragmatic 26.1.2 stubs (TODO-commented,
re-wire later):** `worldGenOptions()` gone→bonusChest=false; `ModelResourceLocation` removed + `ModifyBakingResult`
reworked→auto-debug-seed icon hook no-op; `IModFile.findResource` moved→`ThemeScanner` yields none;
`FMLEnvironment.production` moved→`DevStructureGenerator` dev-gen disabled. **NEXT: port the gametest sourceset** (the
`@GameTest`/`@GameTestHolder` annotations were removed in 26.1.2 → the new `GameTestInstance`/datapack-registered
harness; currently `exclude`d on `mcv != "1.21.1"`). "Nothing regressed" = 1.21.1's 126 tests + golden master, green
after every slice.

(earlier this session: 120 → 81.) DONE: mob reorg + `MobSpawnType`→`EntitySpawnReason` (GenerationJob);
`registryOrThrow`→`lookupOrThrow` (NOT a clean swap — `//?` in `Lookup.registry()`, all callers route through it);
`Registry.get`→`getValue` (`//?` in `Lookup.byId()`, block/entity route through it); the item/entity superclass
import; **`compat.Entities`** (`Entity.moveTo`→`snapTo`, `EntityType.create` gained an `EntitySpawnReason` arg) +
`VillagerType.byBiome` guarded out (GenerationJob clean); **`Lookup.dimensionId`** (`ResourceKey.location()`→
`identifier()`); `getMinBuildHeight`/`getMaxBuildHeight`→`getMinY`/`getMaxY`; `Blocks.CHAIN`→`Blocks.IRON_CHAIN`
(block + id renamed in the copper update). **The whole `compat` facade + GenerationJob now compile clean on 26.1.2.**
**Tooling note:** the **NeoForge 26.1.2.76 sources** are in the gradle cache (`~/.gradle/caches/modules-2/files-2.1/
net.neoforged/neoforge/26.1.2.76/<hash>/`) — needed for the NeoForge-specific deltas (`DeferredRegister.registerItem`,
`FMLEnvironment.production`, `ModList.findResource`, `KeyMapping.Category`); the vanilla deltas come from the patched
sources jar as before. The remaining 81, accurately mapped from the full compile (`-Xmaxerrs` lifted via a throwaway
init script), is dominated by **ONE recurring rewrite not in the table above:**

- **★ the 1.21.5 NBT/serialization rewrite — `CompoundTag` direct access → `ValueInput`/`ValueOutput` with
  `Optional<T>` getters.** Hits `IslandSeedEntity` (`addAdditionalSaveData(ValueOutput)`/`readAdditionalSaveData(
  ValueInput)`, ~13 errors), `SkyseedCommands` (`CompoundTag.contains`/`get` → Optional, ~11) and `SkyseedWorldData`
  (~8) — **~30 of the 102, and the meatiest** (each save/load method is a real per-version rewrite, not a rename).
  Do these carefully: write the 1.21.1 body (current) and the 26.1.2 `ValueInput`/`ValueOutput` body as a `//?` method
  pair, reading the new accessor names from the jar.

Other clusters (mechanical renames, per the histogram): **entity API** — `EntityType.create(Level)`→`create(Level,
EntitySpawnReason)`, `Entity.moveTo`→(renamed), `teleportTo`/`setRespawnPosition` signatures (GenerationJob,
PlayerEvents, IslandSeedEntity ctor needs an `ItemStack`); **recipe API** — `CustomRecipe`/`SimpleCraftingRecipe
Serializer`/`LootModifier` shape (GuideRecipe, ModRecipes, AddDropModifier); **`DeferredRegister.registerItem`** sig
(ModItems); **client** — `ModelResourceLocation`, `KeyMapping` ctor, `SkyseedClientEvents`, `WorldSetupEvents`;
**1-offs** — `ChunkPos` ctor + `MaxDistance` (Jigsaw), `ModEntities` `ResourceKey`, `TwinPlacer`, structure templates
(`CHAIN`), etc. **Realistic scope: a multi-session grind** — the crux (RL) + the facade are done; this is volume, with
the NBT rewrite the one genuinely careful piece.

### 2.8 Stage 2d content progress (2026-06-28)

**2b (tolerant codecs) — ✅ VERIFIED COMPLETE.** Every resolve path skips unknown ids: blocks (`Lookup.hasBlock`
gate), mobs (`MobPlanner.resolveEntity`/`hasEntityType`), features (`DecorationPlanner` skip-with-log), template pools
(`hasTemplatePool`), biomes (`Lookup.biomeMatches` → false for an unknown id/tag). All codecs store raw `String`
(`Id`=`Codec.STRING`, `BiomeOverride.biomes`=`Codec.STRING.listOf`) → parse-tolerant. The 1.21.1 inert-load is already
gametested by `unknownThemeIdsFallBack` (a forward id is indistinguishable from an unknown id there) — so the suite
stays frozen; the resolve-side proof is on the 26.1.2 node.

**2d-1 Pale Garden — biome override ✅ DONE** (`dd2860b`): a `pale_garden` override on the Forest line (pale oak +
moss + carpet + eyeblossom + hanging-moss underside). Same `forest.json` ships to both nodes — resolves on 26.1.2,
inert on 1.21.1. Gametest `forest_over_pale_garden_grows_pale_variant` (resolve-side: pale moss in `p.blocks()` + the
`pale_oak` CF in `p.trees()`). Both green: 1.21.1 126, 26.1.2 128. Verified 26.1.2 ids: `pale_oak`/`pale_oak_creaking`
(configured features), pale_oak_log, pale_moss_block/carpet, pale_hanging_moss, open/closed_eyeblossom, creaking_heart,
resin_*; entities creaking/nautilus/happy_ghast/copper_golem.

**2d-1 Pale Garden — dedicated seed ✅ DONE** (`c85e5d0`): a craftable 26.1.2-only Pale Garden Skyseed (pale_oak_creaking
→ Creaking, pale moss, eyeblossom, hanging-moss underside; crafted from pale_oak_log + pale_moss_block). The full
cross-version-gating pattern landed and is the **template for all future modern-only content** — both nodes green
(1.21.1 126 clean, 26.1.2 All 129; recipe present on 26.1.2, absent on 1.21.1). It went exactly per the plan below,
plus one find: the `#skyseeds` item tag needed the new entry as `{id, required:false}` (else the absent item breaks the
1.21.1 tag load). Gametest `pale_garden_seed_grows_creaking_pale_forest` locks the content + confirms the theme resolves.
The original (verified-correct) plan was:
- **theme** `pale_garden.json` — the full eerie island (use `minecraft:pale_oak_creaking` so the trees carry creaking
  hearts → Creakings; pale-moss surface, resin decoration). Ships to both, inert on 1.21.1.
- **registration** — add `"pale_garden"` to `ModItems.SEED_THEMES` via a `//? if >=26.1.2` directive (26.1.2-only → no
  dormant item on 1.21.1). NB this is what makes the coverage test demand the rest, on 26.1.2 only.
- **recipe** — golden recipe with pale ingredients (e.g. pale_oak_planks); filter it to 26.1.2 in `generateRecipes`
  (a `recipes/_26_1_2_only/…` subtree or a marker the task skips when `mcv=="1.21.1"`) so the 1.21.1 vanilla recipe
  loader never sees the pale ids.
- **advancements** — `craft_pale_garden` is safe as-is (references `recipe_id` as a STRING). `gathered_pale_garden`
  references items: use a **tag** (`#minecraft:pale_oak_logs` — unknown tag resolves to empty, tolerant) NOT a direct
  `minecraft:pale_oak_*` id (a direct unknown item id breaks the 1.21.1 advancement load). `reveal_pale_garden` per the
  existing reveal pattern. ⚠ VERIFY the tag-tolerance assumption with a 1.21.1 datapack-load run.
- **book** — a Patchouli source entry; filter it to 26.1.2 in `generateGuide` (the Modonomicon book) and confirm the
  Patchouli book load on 1.21.1 (its crafting page references the 26.1.2-only recipe → likely also needs the 26.1.2
  gate, or a tolerant page).
- **lang + icon** — a `pale_garden_skyseed` lang name + a PowerShell-painted 16×16 pale seed texture/model.
Then re-run both nodes (1.21.1 must stay 126 + load clean; 26.1.2 coverage test green with the new seed).

**2d-2 vegetation / 2d-3 mobs / 2d-4 variant-check** — still pending (mostly tolerant theme data, lower risk).

---

## Risks & things to verify

- **Stonecutter ↔ ModDevGradle integration — ✅ RETIRED.** Proven twice: Stage 0 on 1.21.1, and now §2.6 — Stonecutter
  drives the full ModDevGradle/NeoForm pipeline for a *second*, 18-months-newer node (`26.1.2`) cleanly (decompile +
  recompile), with the 1.21.1 node staying green. The integration is not a risk.
- **NeoForge API churn is the real work.** The network/payload + registration APIs moved meaningfully between recent
  NeoForge versions — expect most directives there. Concentrating it in `compat` (Stage 1) pays off before adding a
  version.
- **Mappings across versions.** Each node pins its own Parchment version; Mojmap names are stable, but Parchment
  **parameter** names can shift — keep the source on names common to all targets, or directive the rare diff.
- **Golden-master fingerprints may be per-version.** The generation fingerprints assume fixed `BlockState` string
  forms + vanilla feature behaviour; if a block's serialized state or a referenced vanilla feature changes between MC
  versions, that version's expected fingerprint differs. Plan a version-keyed expected map (directive or constant) in
  the golden-master test.
- **Per-version data.** `.nbt` + datapack JSON are mostly version-stable, but a vanilla block-id rename in a future
  version would need a per-version data variant (rare; handle when it bites).

---

## Definition of done

- Stonecutter builds the current version (and ≥1 more, once a target is chosen); `chiseledBuild` yields a jar per
  version and each passes its gametests.
- The version-volatile calls live in `compat`; the algorithm / codecs / templates carry **no** version directives.
- No new runtime dependency; still NeoForge-only.
- Adding a version is "a Stonecutter node + a handful of `compat` directives", documented.
