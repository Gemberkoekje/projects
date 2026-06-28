# Skyseed — Modonomicon (optional guide book) Plan (SKYMODONOMICONPLAN)

**Goal.** Add **Modonomicon** as an optional rich-guide-book backend and make it the **primary guide backend on every
version** — preferred over Patchouli when both are present, because Modonomicon is the more actively developed mod.
Patchouli is demoted to a **legacy/secondary fallback** (still optional, still supported). On **26.1.2** Modonomicon
is the *only* rich backend (Patchouli has no released 26.1.2 build — `patchouli_26.1.2` is intentionally omitted in
`gradle.properties`). Keep everything **optional** (soft dependency), and **degrade gracefully if both book mods are
installed at once** (which shouldn't normally happen, but must not break).

**Non-goal / already true:** 26.1.2 is *already functional today* — with no book mod, `SkyseedGuide.book()` returns
the vanilla written-book fallback. Modonomicon just restores the *rich illustrated* edition on 26.1.2. So this is an
enhancement layered on a working fallback, not a blocker.

---

## Current state (what we're extending)
- **`SkyseedGuide.book()`** is the single entry point. It already does graceful degradation: if `patchouli` is loaded
  *and* yields a non-empty stack → the Patchouli book; otherwise → `writtenBook()` (a vanilla written book). Both the
  first-join grant and the `skyseed:guide` craft (`GuideRecipe`) call it, so they always agree.
- **`compat/PatchouliCompat`** is the Patchouli-specific bridge: `bookStack(Id)`, the `vazkii.patchouli` import under
  `//? if <26.1.2 {` (absent on 26.1.2), the body `//?`-split (1.21.1 = `PatchouliAPI.getBookStack`, 26.1.2 =
  `ItemStack.EMPTY`). Referenced only behind `ModList.isLoaded("patchouli")`, so the API classes load only when the
  mod is present.
- **Dependency wiring (the pattern to mirror):** `compileOnly "vazkii.patchouli:Patchouli:${patchouli_${mcv}}"` +
  `localRuntime …`, guarded by `if (project.hasProperty("patchouli_${mcv}"))`. The version is a **per-node property**
  `patchouli_1.21.1=…` in the root `gradle.properties` (26.1.2 deliberately has none). Repo: `maven.blamejared.com`.
- **Content:** 79 Patchouli JSON files — `book.json`, **6 categories**, **72 entries** — under
  `assets/skyseed/patchouli_books/guide/en_us/{categories,entries}` (+ `data/skyseed/patchouli_books/guide/`). Entries
  carry `name`/`category`/`icon`/`sortnum`, a reveal `advancement` gate, and `pages[]` of `patchouli:text` /
  `patchouli:crafting`, with Patchouli text macros (`$(item)`, `$(bold)`, `$(br)`, `$()`) and per-page advancement
  gates. The gametest `everySeedRecipeAndBookEntryMatchesSeedKind` reads these entries to verify coverage.

---

## Design

### 1. Backend-agnostic facade (small, mirrors PatchouliCompat)
- Add **`compat/ModonomiconCompat`** — same shape as `PatchouliCompat`: a `bookStack(Id)` whose Modonomicon API import
  is under `//? if >=26.1.2 {` (and a `//?` body returning `ItemStack.EMPTY` on nodes without the API). Referenced
  only behind `ModList.isLoaded("modonomicon")`.
- **`SkyseedGuide.book()`** becomes a small **ordered walk** over backends instead of a single Patchouli check:
  ```java
  // Modonomicon first (preferred everywhere), then Patchouli; first present + non-empty wins; else the written book.
  static final Backend[] BACKENDS = { MODONOMICON, PATCHOULI };   // ONE fixed global order — no per-version swap
  for (Backend b : BACKENDS) {
      if (ModList.get().isLoaded(b.modid)) {
          ItemStack s = b.book(BOOK_ID);        // ModonomiconCompat / PatchouliCompat
          if (!s.isEmpty()) return s;            // empty (no content yet on this node) → fall through
      }
  }
  return writtenBook();
  ```
  (A `Backend` is just `{ String modid; ItemStack book(Id); }` — two static entries, no heavy interface needed.) The
  order is the **same on every node**, so there's no `//?` on the precedence; only each compat class's *API import*
  stays `//?`-gated to the nodes where that mod's build exists.

### 2. Per-version backend wiring (mirror the Patchouli pattern exactly)
- `gradle.properties`: add `modonomicon_${mcv}` on **both** nodes (`modonomicon_1.21.1=…` *and* `modonomicon_26.1.2=…`)
  — Modonomicon is now the primary backend everywhere, so it's wired on every node that has a Modonomicon build.
  build.gradle: `if (project.hasProperty("modonomicon_${mcv}")) { compileOnly … ; localRuntime … }` + the Modonomicon
  maven repo. **Confirm coordinates/repo in Phase 0** (likely `com.klikli-dev:modonomicon:<mc>-<ver>` on the
  klikli-dev / Modrinth maven; Modonomicon ships builds for both 1.21.1 and 26.1.2).
- So: **1.21.1** has *both* backends available (Modonomicon preferred, Patchouli the fallback); **26.1.2** has only
  Modonomicon (Patchouli has no build → its API import compiles out via `//?`, its `localRuntime` is skipped). Each
  compat class's API import is `//?`-gated to the nodes where that mod's API exists (`PatchouliCompat`'s import stays
  `//? if <26.1.2`; `ModonomiconCompat`'s is present on both nodes).

### 3. Both-installed handling (the explicit ask) — deterministic precedence, no conflict
- **They don't actually conflict at the data layer:** Patchouli reads only `…/patchouli_books/…`, Modonomicon reads
  only `…/modonomicon/books/…` — disjoint trees. With both mods present, *each* loads its own copy of the Skyseed
  book into its own index. That's benign redundancy, not a clash.
- **Skyseed only ever hands out ONE book:** `book()` returns the **first present backend in `BACKENDS` order**, so the
  granted book == the crafted book == one item. Order = **Modonomicon first, then Patchouli — the same on every
  version** (Modonomicon is the more actively developed mod, so it's preferred everywhere). If both are installed,
  Modonomicon wins; if Modonomicon's stack comes back empty (its content hasn't been built on this node yet — see §4),
  it falls through to Patchouli, then to the written book — never a crash.
- **Debuggability:** log one INFO at startup if more than one backend is loaded, naming which Skyseed will use.
- Net: both-installed = the player gets exactly one Skyseed book (Modonomicon's); Patchouli still shows its redundant
  copy in *its own* book list, which is harmless. Documented so it's understood, not surprising.

### 4. Content — the real work (same divergent-data theme as SKYRECIPEGENPLAN)
The 72 entries + 6 categories + book.json are **Patchouli-format**; Modonomicon's book schema differs (its own
`book/category/entry/page` JSON, page types like `modonomicon:text` / `modonomicon:crafting_recipe`, its own
text/condition model). So the content must exist in **both** formats. Options, recommended order:
- **(A, recommended) Golden = the existing Patchouli JSON; transform → Modonomicon** at build time (a `generateGuide`
  Gradle task, sibling to SKYRECIPEGENPLAN's `generateRecipes`). Because Modonomicon is now preferred on **every**
  version, the transform runs on **both** nodes: each node ships the generated Modonomicon book (the primary), and
  1.21.1 *also* ships the Patchouli golden verbatim (the fallback). So the transform is now load-bearing for the
  primary book everywhere — its fidelity matters more than in the version-split design. **The hard part is the
  page-type + macro mapping**: `patchouli:text`→`modonomicon:text`, `patchouli:crafting`→`modonomicon:crafting_recipe`,
  and the `$(item)`/`$(bold)`/`$(br)`/`$()` macros → Modonomicon/MC formatting; plus the reveal-`advancement` gate →
  a Modonomicon condition. Worth a focused mapping pass; not all macros may have a 1:1 target (fall back to plain
  styled text).
- **(B, pragmatic interim) Hand-author a Modonomicon book** (or a thin subset) for 26.1.2, maintained separately.
  Higher drift risk across 72 entries, but unblocks a rich 26.1.2 book without the transformer. Could start as a
  smaller book and grow.
- **(C) A neutral guide schema → both** — cleanest long-term but the most upfront work; only worth it if Patchouli is
  eventually dropped or a third backend appears.
- **The precedence fall-through covers the interim gracefully:** until the Modonomicon content (the transform) lands,
  Modonomicon's book stack is empty, so `book()` falls through — **1.21.1 keeps showing the Patchouli book**, and
  **26.1.2 shows the written-book fallback** (both already working). So the integration + precedence are shippable
  before any Modonomicon content exists, with **no regression** to the current 1.21.1 Patchouli experience.

---

## Validation
- **No book mod:** `book()` → written book. **Patchouli only:** Patchouli book (the fallback path). **Modonomicon
  only:** Modonomicon book. **Both installed (either version):** the **Modonomicon** book (preferred), no crash, the
  INFO log fires.
- **1.21.1 behaviour change to verify:** with the Modonomicon content present, a 1.21.1 user who has *both* mods now
  gets the **Modonomicon** book where they previously got Patchouli — intended, but call it out in the changelog.
- Add a tiny unit-style gametest or manual check that `book()` is non-empty and stable under each combination
  (simulated via the precedence list, since you can't easily load both mods in one gametest run).
- **Content coverage:** extend `everySeedRecipeAndBookEntryMatchesSeedKind` (currently Patchouli-path) so on 26.1.2 it
  checks the **Modonomicon** entries instead — closing the loop with SKYGAMETESTPLAN Phase 4 (this is one of the two
  deferred generation tests). Each seed must still have an entry that carries its recipe and reveal gate.

---

## Phases
- **Phase 0 — integration only (no content).** Confirm Modonomicon coords/API/data-path; add `ModonomiconCompat` +
  the `modonomicon_${mcv}` build wiring + the precedence walk in `SkyseedGuide.book()` + the both-loaded INFO log.
  Verify: 1.21.1 still Patchouli, 26.1.2 still written-book fallback (Modonomicon present but no book yet → empty →
  fallback), both nodes green. **Shippable.**
- **Phase 1 — a minimal Modonomicon book.** A `book.json` + one category + ~2 entries so 26.1.2 returns a *real*
  Modonomicon book; proves the data path, the book item id, and `ModonomiconCompat.bookStack`.
- **Phase 2 — full content.** Land Option A (the `generateGuide` transform) or Option B (hand-authored), porting all
  72 entries + 6 categories; flip the coverage gametest to the Modonomicon path on 26.1.2.

---

## Risks / open decisions
- **RESOLVED (Phase 0) — Modonomicon API specifics:** Maven coords/repo, the book-stack approach (generic
  `modonomicon:book` item + `BOOK_ID` data component, existence via `BookDataManager`), the data path
  (`modonomicon/books`), and the 26.1.2 build (`1.134.2`) are all confirmed — see the Status log.
- **Macro/page mapping fidelity (Option A):** some Patchouli macros may lack a clean Modonomicon equivalent — decide a
  documented fallback (plain `§`-styled text) rather than failing the transform.
- **DECIDED — precedence when both present:** a single fixed global order, **Modonomicon → Patchouli → written book**,
  the same on every version (Modonomicon is the more actively developed mod). No `//?`-swap on the order.
- **DECIDED — Modonomicon on 1.21.1 too:** yes. Modonomicon is wired (`modonomicon_1.21.1`) and *preferred* on 1.21.1
  as well; Patchouli is demoted to the legacy fallback there. (Confirm Modonomicon publishes a 1.21.1 build in Phase 0.)
- **Open decision — content source of truth:** golden=Patchouli+transform (A) vs hand-authored Modonomicon (B). Lean
  A for single-source consistency with SKYRECIPEGENPLAN; B is the lower-effort interim.

## Status log
- **★ Phase 0 DONE (commit `a12b353`, 2026-06-28) — Modonomicon wired as an optional backend on both nodes,
  precedence + graceful fall-through verified.** Confirmed facts (save re-deriving):
  - **Maven:** `com.klikli_dev:modonomicon-<mc>-neoforge:<ver>` on repo `https://dl.cloudsmith.io/public/klikli-dev/
    mods/maven/`. Wired `compileOnly + localRuntime`, **`transitive = false`** (clean at dev runtime — 0 load
    failures both nodes). Versions: **`modonomicon_1.21.1=1.114.5`, `modonomicon_26.1.2=1.134.2`** (Modonomicon ships
    a build for *both* MCs). Per-node guard `if (project.hasProperty("modonomicon_${mcv}"))`, mirroring Patchouli.
  - **API (javap-confirmed, STABLE across 1.114.5 ↔ 1.134.2 — so no `//?` in `ModonomiconCompat`):** the book item
    is the generic `ItemRegistry.MODONOMICON.get()` (a `modonomicon:book`) carrying the book id in the
    `DataComponentRegistry.BOOK_ID.get()` data component (`DataComponentType<id>`); existence is
    `BookDataManager.get().getBook(id)` (returns null if absent → we return `ItemStack.EMPTY` so `SkyseedGuide` falls
    through). The only version-volatile thing is the MC id type, kept behind `var id = Ids.parse(book.value())` (never
    named). `RegistryObject<T> extends Supplier<T>` so `.get()` yields the value. Data path is
    `data/<modid>/modonomicon/books/<book>/…` (`ModonomiconConstants.Data.MODONOMICON_DATA_PATH = "modonomicon/books"`).
  - **`SkyseedGuide.book()`** walks Modonomicon → Patchouli → written book; logs once if both are installed. Verified:
    1.21.1 with BOTH mods at dev runtime → 126 green + the INFO log + falls through to Patchouli (no Modonomicon
    content yet); 26.1.2 with Modonomicon → 58 green + falls through to the written book. **No regression** (both nodes
    behave exactly as before content lands).
  - **Datagen API present for Phase 2:** the jar ships `…api.datagen.BookProvider`/`NeoBookProvider`/`EntryProvider`/
    `CategoryProvider` + `book.BookModel`/`BookEntryModel`/`BookCategoryModel` + condition models — i.e. Modonomicon
    supports *code* book authoring, relevant when wiring the golden→Modonomicon content (Option A).
- **★ Phase 1 DONE (commit `5b97c27`, 2026-06-28) — the data-driven Modonomicon book pipeline is proven on both
  nodes.** A minimal book at `data/skyseed/modonomicon/books/test_guide/` (book.json + an Overview category + a Welcome
  entry) loads cleanly on **both** Modonomicon versions (1.114.5 + 1.134.2), and the new 26.1.2 gametest
  `modonomicon_backend_resolves_and_degrades` asserts `ModonomiconCompat.bookStack` resolves a LOADED book to a
  non-empty stack and an ABSENT id to EMPTY (the fall-through guarantee). 59 green on 26.1.2, 126 on 1.21.1.
  **Confirmed JSON format (worked first try; field names from the datagen `*Model` builders, snake_case keys):**
  - `book.json`: `{ "name": "...", "tooltip": "..." }` (textures/model/colors all default — optional).
  - `categories/<id>.json`: `{ "name": "...", "icon": "minecraft:grass_block", "sort_number": 0 }` (icon accepts a
    plain item-id string; the `"title"`/`"landing_text"` shape from web search was a *different* mod, Modopedia).
  - `entries/<id>.json`: `{ "category": "<ns>:<category_id>", "name": "...", "icon": "<ns>:<item>", "x": 0, "y": 0,
    "pages": [ { "type": "modonomicon:text", "text": "..." } ] }` — the entry id comes from the file path; entries
    **reference** their category (the category does NOT list entries). Crafting page = `{ "type":
    "modonomicon:crafting_recipe", "recipe": "<ns>:<recipe_id>" }` (to wire in Phase 2; recipes are golden-generated).
  - A **throwaway `test_guide` id** was used so `SkyseedGuide.book()`'s real `skyseed:guide` precedence is untouched
    (no 1.21.1 Patchouli regression). **Phase 2 replaces test_guide with the real `skyseed:guide` Modonomicon book at
    full parity** (so both nodes switch to Modonomicon only once it's complete) and removes test_guide.
- _Next: Phase 2 — the full `skyseed:guide` content (golden→Modonomicon transform; the page-type + `$(...)` macro
  mapping is the meaty part), then flip the book-coverage gametest to validate BOTH backends (Patchouli AND
  Modonomicon stay first-class + complete — per the standing rule)._
