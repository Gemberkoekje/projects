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

---

## Risks & things to verify

- **Stonecutter ↔ ModDevGradle integration — the main unknown.** Stonecutter is most battle-tested with Loom; verify
  it drives a NeoForge/ModDevGradle build cleanly (or whether the NeoForge build needs adjusting). If it can't, that
  changes the approach — which is exactly why **Stage 0 is a throwaway spike** before any investment.
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
