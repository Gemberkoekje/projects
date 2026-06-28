# Skyseed — Golden-Source Recipe Generation Plan (SKYRECIPEGENPLAN)

**Goal.** Author every Skyseed recipe **once** (a "golden source") and **generate the per-version data JSON** from it,
so the multi-version build (1.21.1 + 26.1.2 + future nodes) ships each node a recipe file in *its* format — without
hand-maintaining two copies. The golden source is the single edit/config point; the shipped data is derived.

**Why this is needed (the divergence).** The 1.21.5+ recipe rewrite changed the **ingredient encoding**, and the two
forms are **mutually exclusive** — neither version accepts the other's:
| | 1.21.1 (legacy) | 26.1.2 (modern) |
|---|---|---|
| item ingredient | `{"item": "minecraft:dirt"}` | `"minecraft:dirt"` |
| tag ingredient | `{"tag": "minecraft:planks"}` | `"#minecraft:planks"` |

A shared JSON can't satisfy both (unlike the void `noise_settings`, where a shared file worked because the extra key
is *ignored* by the older codec — see [[skyseed-refactor]]). On 26.1.2 today this throws **~45 non-fatal parse errors**
and the seed crafting recipes simply don't load.

---

## Scope (measured from the current `src/main/resources/data/skyseed/recipe/`)

- **75 recipe JSONs total**, of which **74 are data recipes** that carry ingredients:
  - 70 `minecraft:crafting_shaped` (ingredients in the `key` map),
  - 4 `minecraft:crafting_shapeless` (ingredients in the `ingredients` array).
- **1 is the code recipe** `{"type":"skyseed:guide"}` — no ingredients, already loads on both (handled by `GuideRecipe`).
- **13** of the 74 use **tag** ingredients (`minecraft:planks`/`logs_that_burn`/`small_flowers`/`wool`, `skyseed:ice`).
- **No ingredient-choice lists** (`"A": [ {...}, {...} ]`) exist — but the transform handles them defensively.
- **Everything except the ingredient fields is already version-identical**: `pattern`, `category:"misc"`,
  `result:{"id":"…"}` (no `count`), and the `type`. So the transform touches **only** `key` values and
  `ingredients` entries.

---

## Design

### What's golden, and which way the transform runs
- **Golden format = the modern (26.1.2) string-ingredient form.** It's the concise, forward-looking encoding; future
  MC versions keep it. So **modern nodes (26.1.2+) get the golden file verbatim** (a straight copy), and **only the
  legacy 1.21.1 node is transformed** (downgrade: string → `{"item"/"tag"}`). As 1.21.1 is eventually dropped, the
  transform becomes a no-op and the whole mechanism can be deleted.
- Golden lives **outside `src/main/resources`** (so it's never shipped raw — that's what fails on 26.1.2). Proposed:
  **`recipes/data/skyseed/recipe/*.json`** at the module root (`rootProject.file('recipes')`), mirroring the data
  path 1:1 so the generator is a pure path map.

### The generator = a build-time Gradle task (no new deps, no datagen entanglement)
- A `generateRecipes` task (Groovy `JsonSlurper`/`JsonOutput`, built into Gradle) reads every golden JSON and writes
  the node's version into **`build/generated/recipes/data/skyseed/recipe/`** (per-node — `build/` is per-node under
  Stonecutter). That dir is added as a `sourceSets.main.resources.srcDir`, and `processResources.dependsOn` it.
- Per-node behaviour keyed on `mcv = project.name`:
  - `mcv == "1.21.1"` → **downgrade** each ingredient (string → object).
  - else (26.1.2, future) → **copy verbatim** (byte-for-byte; no reserialize, so output is identical to golden).
- The task is **incremental** (`@InputDirectory recipes/`, `@OutputDirectory build/generated/recipes/`); output is
  **build-only / gitignored** (golden is the committed artifact — no generated noise in git).

### The transform (the entire ruleset — downgrade, golden→1.21.1)
Walk only `key` (object values) and `ingredients` (array elements); for each ingredient node:
- string `"ns:path"` (no `#`) → `{ "item": "ns:path" }`
- string `"#ns:path"` → `{ "tag": "ns:path" }`
- array `[a, b, …]` → map each element by the rules above (defensive; none today)
- object with a `type`/`neoforge:ingredient_type` (custom ingredient) → **pass through unchanged** (none today)
- everything else in the file (pattern, result, category, type, custom keys) → **copied untouched**

The upgrade direction (used once, for migration) is the inverse: `{"item":"x"}`→`"x"`, `{"tag":"x"}`→`"#x"`.

### Considered alternative — NeoForge datagen (`RecipeProvider`), rejected for now
"Code as golden source" via `ShapedRecipeBuilder` + `runData` would serialize version-correct JSON automatically and
absorb *any* future format change. But: it means rewriting all 74 recipes into Java; the **builder API itself
diverges** between 1.21.1/26.1.2 (would need its own `//?`); and the generated output collides in the **shared**
`src/generated/resources` (the existing `runData` writes there — version-dependent recipes can't live in a shared
dir). The current divergence is a *single, trivial* encoding swap, so a 40-line transformer is far lower-risk and
matches the "golden JSON → per-version JSON" ask directly. **Revisit datagen if the recipe format diverges in depth.**

---

## Migration (one-time)
1. Run the **upgrade** transform over the existing 74 `src/main/resources/.../recipe/*.json` → write golden (modern
   form) to `recipes/data/skyseed/recipe/`. (Golden = the 26.1.2-format version of today's files.)
2. Move the code recipe `*_guide.json` (or `guide.json`) into golden too (verbatim; no ingredients to touch) for one
   uniform source — or leave it shared. Either is fine.
3. **Delete** the 74 (+guide) originals from `src/main/resources/data/skyseed/recipe/` so only generated recipes ship.
4. **Golden-master check:** generate the *1.21.1* recipes from golden and diff (semantically) against the deleted
   originals — they must match (the round-trip upgrade→downgrade is identity). Guarantees zero 1.21.1 behaviour change.

---

## Validation
- `:1.21.1:runGameTestServer` → still **126 green**, recipes load, no new errors.
- `:26.1.2:runGameTestServer` → the **~45 recipe parse errors are gone**.
- **Recommended new gametest** (ties into SKYGAMETESTPLAN): assert the seed recipes are present in the server's
  `RecipeManager` (e.g. a `<theme>_skyseed` recipe resolves), so a future format drift fails a test instead of
  silently dropping recipes. Add to both suites.

---

## Phases
- **Phase 0** — the generator + wiring with **one** recipe migrated (prove the task, the per-node srcDir, both builds
  green, the 1.21.1 byte-diff identity). 
- **Phase 1** — migrate all 74 to golden, delete originals, verify both `runGameTestServer` green + the recipe count
  loads on 26.1.2.
- **Phase 2** — the recipe-presence gametest (regression guard).

---

## Risks / edge cases / open decisions
- **Custom/typed ingredients** (`neoforge:ingredient_type`, conditional ingredients): none today — passed through
  untouched; revisit if added (they may themselves be version-specific).
- **Formatting/determinism of the 1.21.1 output:** use a stable pretty-printer (sorted keys or preserved order) so
  regen diffs are clean; modern nodes copy verbatim so they're trivially stable.
- **Result `count`/components:** today all results are bare `{"id":"…"}` (shared). If a future recipe needs `count` or
  data components, confirm that field is version-stable or extend the transform.
- **Open decision — golden location/name:** `recipes/` (module root) vs `src/recipes/` vs `data_src/`. Default
  proposed: `recipes/`.
- **Open decision — generalise the framework?** Recipes are the first genuinely-divergent data; `noise_settings`
  stayed a shared file. If more data types diverge, the same `generate<X>` per-node-transform pattern extends — but
  keep it per-type, don't over-abstract up front.

## Status log
- **★ Phases 0 + 1 DONE (commits `bfdd382` Phase 0, `<this>` Phase 1, 2026-06-28) — the ~45 recipe parse errors on
  26.1.2 are GONE.** All 75 recipes now live as **golden** (modern string-ingredient form) under `recipes/data/skyseed/
  recipe/`; the originals were removed from `src/main/resources`. The `generateRecipes` Gradle task emits the node's
  data JSON into `build/generated/recipes` (a resource srcDir): **26.1.2 copies golden verbatim, 1.21.1 downgrades**
  each ingredient (`"x"`/`"#tag"` → `{"item":"x"}`/`{"tag":"x"}`) via a `JsonSlurper` transform of `key`/`ingredients`
  only. Verified: **26.1.2 = 0 recipe parse errors, 75 recipes ship + load, 58 gametests green**; **1.21.1 = 126
  gametests green** (its `everySeedRecipeAndBookEntryMatchesSeedKind` test confirms every seed is still craftable —
  functional identity of the downgrade). The one-off `upgradeRecipesToGolden` migration task (a narrow regex that
  collapsed only the ingredient objects, preserving 2-space formatting + key order) was used once and removed.
  Implementation notes (save re-deriving): golden lives **outside `src/main`** so Stonecutter never stages it raw and
  it never ships ungenerated; the generated dir is per-node (`build/` under Stonecutter); deleting a recipe from
  `src/main/resources` also needs the stale **Stonecutter-staged** `versions/<v>/src/...` copy removed (the documented
  staging trap) — done. The `skyseed:guide` code recipe (`guide.json`, no ingredients) is golden too, copied verbatim.
- _Phase 2 (the recipe-presence regression gametest) is now UNBLOCKED: the deferred `everySeedRecipeAndBookEntryMatches-
  SeedKind` can be ported to the 26.1.2 suite (SKYGAMETESTPLAN Phase 4) now that recipes load — it doubles as this
  plan's regression guard._
