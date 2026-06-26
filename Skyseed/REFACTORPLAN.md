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

0. **Stonecutter skeleton + build proof.** ✅ **DONE** (branch `refactor/stonecutter-spike`) — Stonecutter 0.9.6 wraps
   the existing ModDevGradle build cleanly on Gradle 9.2.1 with the configuration cache on. Tasks now run under the node
   (`./gradlew :1.21.1:runGameTestServer` / `:1.21.1:build`; bare `runGameTestServer` is gone; `chiseledBuild` builds
   every node); `versions/` + `.stonecutter/` are gitignored. The build script needed only minor adaptations
   (`rootProject.file(...)` paths, `duplicatesStrategy = EXCLUDE`).
1. **Concentrate the volatile surface into `compat`.** ✅ **DONE** — `Ids` (every `ResourceLocation`), `Lookup`
   (registry access) and `Jigsaw` (the one `generateJigsaw` site) now front the version-volatile calls across ~18 files;
   the algorithm / codecs / templates call the facade and never touch the volatile APIs. Behaviour-preserving — the
   `islandOutputIsStable` golden master stays byte-identical. The NeoForge registration / network / config glue already
   lives in its own classes (those *are* the facade for that concern), and the gametest keeps direct calls as the
   golden-master oracle (route it when Stage 2 lands).
2. **Add the second MC/NeoForge version** (target **TBD — you pick**: latest 1.21.x, 1.22, …). Add the Stonecutter
   node; resolve the per-version compile diffs with directives — almost all landing in `compat`. Get **both** versions
   building and each one's gametests passing.
3. **Generalize + document.** Add further versions as wanted; write the "how to add a version" recipe (a node + the
   expected directive sites); optionally a CI matrix (`chiseledBuild`).

> **Priority (per the maintainer): 1.21.1 only for now.** Stages 0–1 (the structural value) land first and are useful
> immediately; the **second version (Stage 2+) is deliberately deferred until Skyseed is feature-complete on 1.21.1**.
> The eventual driver is adopting the **world-gen-impacting features** newer versions introduced — so when a target is
> named it's chosen for what its worldgen brings, not merely to prove the matrix. Stage 1 is still worth doing now: it
> is a pure, golden-master-guarded code-organization refactor (no behaviour change) and the on-ramp for that later work.

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
