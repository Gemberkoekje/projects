# Master Plan — prioritized roadmap

**This file replaces four separate plan docs**, kept as one so the whole
not-yet-done backlog is visible in one place instead of scattered across
files. What moved where:

- `MAIN_MENU_PLAN.md` and `VISUAL_STYLE_PLAN.md` (both proposed, not started)
  → folded in below, unchanged in substance.
- `M4_BUNKER_VIEW_PLAN.md` (proposed, not started) → folded in below,
  condensed; the original's full geometry tables and per-decision reasoning
  are still recoverable from git history if needed once that work starts.
- `SURFACES_PLAN.md` (**finished** — all five phases shipped) → moved
  verbatim into [SURFACES_NOTES.md](SURFACES_NOTES.md#appendix-the-original-plan-as-written)
  as an appendix, so the plan and what actually shipped against it stay
  readable side by side in one document. Not repeated here since it's done;
  see [Completed](#completed) below.

**Revision 2** added four new items (Random Map Generator, Destructible
Wall Sections, 1-Player Mode, Sounds and Music) and re-derived the priority
order across all ten items together, rather than appending the new ones at
the end unranked. This moved Main Menu from #2 to #4 — the two new
tooling/content items (generator, walls) are cheap and immediately useful
enough to front-run it. That's the biggest reshuffle from revision 1 and
the one most worth a second look if it doesn't feel right.

## Priority order, at a glance

Ordered by impact per unit of effort and risk — not by when each item was
proposed. Later items aren't blocked on earlier ones shipping, but 1 and 9
each reshape how several other items should look, so doing those first (or
at least deciding their direction first) avoids re-skinning work done in
between.

1. ~~[Lighting, Sky & Post-Processing](#1-lighting-sky--post-processing)~~ — **done**, see [LIGHTING_NOTES.md](LIGHTING_NOTES.md).
2. ~~[Random Map Generator](#2-random-map-generator)~~ — **done**, see [GENERATOR_NOTES.md](GENERATOR_NOTES.md).
3. ~~[Destructible Wall Sections](#3-destructible-wall-sections)~~ — **done**, see [WALLS_NOTES.md](WALLS_NOTES.md).
4. ~~[Main Menu](#4-main-menu)~~ — **done**, see [MAIN_MENU_NOTES.md](MAIN_MENU_NOTES.md).
5. ~~[Combat & Movement VFX](#5-combat--movement-vfx)~~ — **done**, see [VFX_NOTES.md](VFX_NOTES.md).
6. ~~[1-Player Mode](#6-1-player-mode)~~ — **done**, see [SOLO_NOTES.md](SOLO_NOTES.md).
7. [Ground & Water Material Upgrade](#7-ground--water-material-upgrade) — bigger effort, still self-contained.
8. [Sounds and Music](#8-sounds-and-music) — highest scope/ambiguity of everything here; first feature needing real external assets.
9. [UI Visual Identity](#9-ui-visual-identity) — determines the final skin for items 4 and 10.
10. [Bunker View Rework](#10-bunker-view-rework) — polish pass on an already-functional screen.

## Design stance for the visual work (items 1, 5, 7, 9)

This project has treated "primitives-only, flat vertex-color shading, no
textures, no particle systems, no custom shaders" as **hard rules**, not
technical limitations — documented deliberately, e.g. `DebrisBurst.cs`:
*"a particle system is an asset nobody can review in a diff."* Given the
go-ahead to make bigger changes, this plan loosens the rules selectively
rather than discarding them:

- **Kept as-is**: primitives-only geometry, the fixed hand-picked palette
  (`blender/rf/palette.py`), vertex-color shading on vehicles/props, the
  ground surface *color/value* ramp (`SurfaceTuning.cs`). These *are* the
  art style — changing them would abandon the Return Fire homage, not
  improve it. Item 3 (Destructible Wall Sections) stays entirely inside
  these rules — it's just one more asset through the existing pipeline.
- **Opened up**: custom shaders (the project currently has zero — water and
  sky are the first candidates), particle systems for **cosmetic,
  state-driven** effects (smoke, dust, splashes) where the randomness has no
  gameplay/determinism consequence, and light texture use (normal-map-only
  detail, decals) that adds surface detail without changing the read-at-a-
  glance color logic.
- **Still hand-coded/deterministic**: high-frequency, gameplay-tied combat
  VFX (muzzle flash, impact sparks) stay in the closed-form-math style of
  `Explosion.cs`/`DebrisBurst.cs`, consistent with the existing pattern and
  because they fire constantly and are worth keeping reviewable.

> **Settled for item 5**: the balance above was confirmed as written. Particle
> systems are in for cosmetic state-driven effects; the two high-frequency
> combat effects stayed closed-form. Textures stayed out entirely — the
> particles are meshes, not billboards. See [VFX_NOTES.md](VFX_NOTES.md).
> Items 7 and 9 still have their own versions of this question open.

If this balance is wrong in either direction, redirect — this is a judgment
call, not a rule extracted from anything said explicitly. Item 10 (Bunker
View) raises a sharper version of the same question for hand-authored art,
and item 8 (Sounds and Music) raises the audio equivalent — see each
item's own open questions.

---

## 1. Lighting, Sky & Post-Processing

> **Done.** Shipped as written, with three of its premises corrected along the way (HDR was
> already on, SSAO already existed on the PC renderer, and metal already had a skybox
> reflection source — so the reflection-probe step collapsed into the skybox step). The plan
> also missed the actual blocker: no camera in the project had ever been told to run
> post-processing. What was built, what was decided and what is still open is in
> [LIGHTING_NOTES.md](LIGHTING_NOTES.md). The original text is left below unchanged.

**Why first**: literally nothing is applied right now — `DefaultVolumeProfile.asset`
is Unity's untouched template (bloom, AO, color grading, vignette all at
zero/neutral), and it still has Unity's own leftover test components
(`CopyPasteTestComponent1/2/3`, `TestVolume`,
`VolumeComponentSupportedOnAnySRP`) sitting in it. Every emissive material
in the game (`RF_Light_Front/Rear`, `RF_Tracer`, `RF_Blast`, emission values
1.5–3.4) was clearly authored *assuming* bloom would exist — right now that
work is invisible. This is the highest-ROI item: zero content risk,
whole-game impact.

1. **Verify HDR/bloom prerequisites** — check `UniversalRenderPipelineAsset`
   (both Mobile and PC quality tiers) for `m_SupportsHDR`; Bloom needs an
   HDR frame buffer. Confirm before tuning values.
2. **Clean and configure `unity/Assets/Settings/DefaultVolumeProfile.asset`**:
   - Remove the leftover test/debug components.
   - Bloom: threshold ~1.0 (just above material emission baseline), modest
     intensity — enough that `RF_Blast`/`RF_Tracer` genuinely glow, not so
     much the whole HUD blooms out. Tune visually.
   - Tonemapping: ACES (or similar filmic curve) instead of None, so the
     flat-color palette doesn't clip harshly under bloom/bright emissives.
   - Color grading: subtle post-exposure/contrast/saturation pass to unify
     the hand-picked palette across lighting conditions, not a stylistic
     overhaul of the colors themselves.
   - Ambient Occlusion: add/enable URP's Screen Space Ambient Occlusion
     render feature on the Universal Renderer asset(s) — check the Renderer
     Features list first, it wasn't confirmed present during investigation.
     This matters more than usual here because geometry is flat-shaded
     primitives with no texture detail; AO is what will sell contact and
     depth between vehicle parts, wheels-on-ground, structure-on-ground.
   - Vignette: subtle only. Confirm actual gameplay camera angle/distance
     first (not fully established during investigation) before committing
     to a strength — a top-down tactical camera tolerates vignette
     differently than a chase camera.
3. **Directional light & shadows** — `VehicleSandboxScene.cs`
   `ConfigureLighting()`: shadow distance is currently 40m at the quality-
   settings level while at least one level's shelf geometry spans ~240m
   (per `SURFACES_NOTES.md`). Verify actual level extents and raise shadow
   distance/cascades if vehicles near map edges are losing shadows — this
   reads as a likely oversight from scaffolding defaults, not a deliberate
   choice, worth confirming.
4. **Reflection probe** — bake or place a reflection probe over the play
   area so `METAL`/`METAL_DARK` palette materials (barrels, rails, gun
   metal — currently smoothness-only with no reflection source) get real
   environment reflection instead of reading flat.
5. **Skybox** — replace Unity's default procedural skybox with a small
   hand-written gradient skybox shader (2–3 color stops + sun disk),
   matching the ambient trilight reference colors already established
   (sky `0.45, 0.51, 0.60`). This is the project's first custom shader —
   still fully code, still fully reviewable in a diff, just not a stock
   material. Alternative: retune the existing procedural skybox's tint/
   exposure/atmosphere-thickness parameters instead of authoring one, if
   that's close enough after tuning — cheaper, try it first.

**Validation**: use `CameraCapture.cs`'s existing headless-render path (same
mechanism as `ArtPreviewScene`) to capture before/after stills from a fixed
camera position/level/time — don't eyeball only in the live editor, keep a
comparable pair of screenshots per change so the effect of each setting is
actually visible in isolation.

**Files touched**: `unity/Assets/Settings/DefaultVolumeProfile.asset`,
`UniversalRenderPipelineAsset` (both tiers) + their Renderer assets,
`unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs`
(`ConfigureLighting`), new `RF_Skybox.shader` (if authored) +
skybox material.

**Open questions**:
1. Gameplay camera angle/distance — not fully confirmed during
   investigation; needed before committing to vignette strength and to
   judge how much ground micro-detail (item 7) will actually be visible.
2. Bloom/HDR prerequisite — needs a direct check of `m_SupportsHDR` on
   both quality tiers' `UniversalRenderPipelineAsset`s before bloom tuning
   can start.
3. Level size range — confirm actual min/max level extents (the 240m
   figure was for one specific island's shelf sheet) to size shadow
   distance and reflection probe placement correctly.

---

## 2. Random Map Generator

A "Generate Map" button in the in-game level editor (M8) that produces a
random, playable-by-construction level, with options for difficulty,
player count, and layout symmetry. The generated map doesn't need to be
polished — the editor's own Problems panel already reports whatever
validation catches, and normal editor tools handle any manual touch-up.

**Why this priority**: directly requested as a productivity tool for map
authoring, self-contained (new generator code + one editor button, no
ripple into rendering/combat systems), and every map-shaped item below
(destructible walls, 1-player mode) benefits from having more test maps
sooner.

**Grounding**: the level format (`LevelDefinition`/`LevelLand`/`LevelBunker`/
`LevelTower`/`LevelStructure`), its validation rules (`LevelValidation.cs`),
and the editor's save/mutation plumbing (`LevelEdits.cs`) are all already
well-suited to this — `LevelEdits.Starter()` (the function behind the
existing "New" button) is effectively a *fixed* miniature version of
exactly this feature: a static function that builds a valid
`LevelDefinition` from scratch and hands it to `session.Adopt()`. A
generator is the same shape, with randomized choices instead of fixed ones.

### Validation rules a generator must satisfy (the actual numbers)
- `ShoreMargin = 2.5m` — clearance every non-bunker prop needs from the
  coastline.
- `BunkerShoreMargin = 10.0m` — clearance a bunker needs.
- Tower spacing `> 2 × widest weapon splash radius` (currently the ASV
  rocket's 4.5m, so >9.0m between same-side towers) — computed live from
  `WeaponTuning`, don't hardcode a number that could drift out of sync.
- Exactly one real tower (`HoldsTheFlag`) per side, and at least one decoy
  — validation only requires *at least* 2 towers per side, not exactly 2,
  so extra decoys are fine. Item 6's 1-player mode relies on this: "the
  enemy can have multiple flag towers" needs no validation change, just
  more decoys authored/generated than the standard pair.
- Exactly one bunker per side, except the 1-player option (see item 6),
  which is one bunker *total* — the enemy side gets towers and turrets but
  no bunker.
- At least one `DepotFuel` and one `DepotAmmo` *somewhere* on the map (not
  per side).
- **Land connectivity is the one topological rule**: the two bunkers must
  be reachable from each other via a *land-only* path (bridges excluded) —
  validated by a BFS flood fill over the realised, noise-displaced
  coastline. This is the rule most likely to break by accident in a
  from-scratch random layout. Safest approach: always author a guaranteed
  dry-land corridor (an `Asphalt` causeway rectangle, matching
  `iron-channel.json`'s own pattern) at the seam between the two halves
  *first*, then randomize everything else around it, rather than hoping
  randomly-placed terrain happens to connect.

### Generation approach
1. **Difficulty → scale**: pick `LevelBounds.HalfExtent` and structure
   counts (trees/buildings/turrets) from a difficulty tier — e.g.
   Easy/Medium/Hard mapping to roughly 100m/140m/180m half-extent and a
   proportionally larger turret count. Turret *stats* (health, rate of
   fire, range) are global constants today (`StructureTuning.For(Turret)`,
   `WeaponTuning.Emplacement()`), not per-instance — so difficulty can only
   scale turret *count and placement density* right now, not toughness.
   Extending the format for per-turret stat overrides is possible later
   but not needed for a first version.
2. **Skeleton first, decoration second**: place the two bunkers, the
   real+decoy tower pairs, the depots, and the connecting causeway using
   something close to `LevelEdits.Starter()`'s proven layout ratios (these
   are already known-valid against every rule above), then randomize the
   *rest* — natural land shape (ellipses for shores/patches, in
   `Grass`/`Sand`), and scattered `Tree`/`BuildingA`/`BuildingB`/`Turret`
   (and `Wall`, once item 3 exists) placements, each checked against
   `ShoreMargin` before being kept.
3. **Determinism via seed**: reuse `SurfaceNoise`'s existing pattern — a
   hash of `(index, LevelDefinition.Seed)`, never `UnityEngine.Random` — so
   a given seed always reproduces the same map. This is a nice side
   benefit that falls out of following the codebase's existing discipline:
   seeds become shareable/reproducible for free.
4. **Mirrored option**: generate Green's half only, then produce Brown's
   half by applying the same 180°-rotation-about-origin math the existing
   (private, one-at-a-time) `LevelEdits.Mirror()` uses (`(x,y,z) →
   (-x,y,-z)`, yaw `+180`) as a *batch* pass over every generated element —
   not by calling `Mirror()` once per element (avoids its name-copying
   quirk and per-call overhead). This is true fairness in the game's own
   terms — identical-shape runs to the flag — not a left/right reflection.
5. **Asymmetrical option**: run the per-side generation independently for
   each team with different random draws, still satisfying every per-side
   validation rule and still sharing one guaranteed crossing at the seam.
6. **1-player option**: see item 6 below — generate only Green's bunker
   (no Green towers, no Green flag), and generate Brown's flag towers
   (multiple: one real + several decoys, no Brown bunker/roster) guarded by
   scattered `Team.Brown` turrets — decoy count is a natural second
   difficulty lever specific to this mode (more towers to check before
   finding the real one), independent of or combined with turret count.
   Turret clusters around each Brown tower pair nicely with item 3's
   destructible walls, once that exists, to make each tower feel like a
   small fortress to crack open. **Coupled to item 6's runtime work**:
   generating a solo-shaped JSON file is easy; actually *playing* it today
   would still boot the hardcoded 2-player `Sandbox` scene and try to
   seat/build a Brown roster that has nowhere to spawn from. Ship the
   generator option, but flag in the UI (or just in this plan) that solo
   maps aren't playable until item 6 lands.

### Editor integration
- **Button**: add "GENERATE" to the top bar (`EditorUi.BuildTopBar`), as a
  peer to New/Open/Save, wrapped in the same `Guard(...)` two-press-confirm
  pattern already used for New/Open/Revert (generating discards the
  current map).
- **Options dialog**: a centered modal reusing the existing "Open panel"
  pattern (`BuildOpenPanel`'s `Image plate` + centered anchor + `SetActive`
  show/hide) for difficulty (Easy/Medium/Hard buttons), player count (1/2),
  and symmetry (Mirrored/Asymmetrical) toggles.
- **Wiring**: the handler builds a `LevelDefinition` via a new static
  generator (mirroring `LevelEdits.Starter()`'s shape) and calls
  `session.Adopt(generated, dirty:true)` — likely via a new
  `session.GenerateLevel(options)` following `NewLevel()`'s exact shape. No
  mouse/tool state machine involvement needed; generation is pure data,
  exactly like `Starter()`.
- **Validation feedback is free**: `Adopt()` already calls `Revalidate()`,
  which already refreshes the existing Problems panel — so generated-map
  warnings show up immediately with zero new UI work, directly matching
  "doesn't have to be perfect, happy to touch up."

### Files touched
- New `unity/Assets/RF/Scripts/Editing/LevelGenerator.cs` (or similar) —
  the static generation logic, callable independent of any UI (same "no
  scene, no camera, arithmetic on the level file" shape as `LevelEdits.cs`).
- `unity/Assets/RF/Scripts/Editing/LevelEditorSession.cs` — new
  `GenerateLevel(options)` method alongside `NewLevel()`.
- `unity/Assets/RF/Scripts/Editing/EditorUi.cs` — new top-bar button +
  options modal.
- Possibly `unity/Assets/RF/Scripts/Editing/LevelEdits.cs` — if the
  batch-rotation helper (`Turned()`) is extracted from `private` to a
  shared/internal location for the generator to reuse.

### Open questions
1. **Exact difficulty tiers** — how many (2? 3?), and the actual
   size/turret-count numbers per tier, need picking; the ratios above are
   starting guesses, not measured.
2. **How random is "random"?** Fully free placement (max variety, more
   validation retries/rejections likely) vs. randomizing *within* the
   `Starter()`-style fixed skeleton (fewer surprises, less variety). Worth
   trying the constrained version first given the "doesn't have to be
   perfect" bar — less generator complexity for a similar payoff.
3. **Retry-on-invalid vs. generate-then-report**: should the generator
   retry internally until a candidate has zero validation problems, or
   always produce one candidate and let the Problems panel guide manual
   fixes (matching how "not perfect, happy to touch up" was framed)? The
   second is simpler and is what "free validation feedback" above assumes;
   the first is more polished but adds retry-loop complexity for uncertain
   benefit given the stated quality bar.

---

## 3. Destructible Wall Sections

> **Done.** Shipped as written, with three deliberate departures: the wall is a `Prop` rather
> than a `Structure` (so step 5's `CategoryOf` gotcha does not arise at all), it is *tougher*
> than a tree rather than thinner, and eight segments went onto the shipped map so the feature
> is something you can drive at rather than something the palette merely offers. Both open
> questions were answered "no exception" — rubble is not solid, and a wall belongs to nobody.
> The generator was deliberately left alone. What was built, what was decided and what is
> still open is in [WALLS_NOTES.md](WALLS_NOTES.md). The original text is left below unchanged.

Short, placeable destructible wall segments — e.g. between turrets, to
build small fortified areas — as a new structure kind alongside the
existing Tree/Building/Turret placements.

**Why this priority**: the structure pipeline (Blender asset discovery,
tuning table, prefab building, level-editor palette) is already fully
generic and self-registering — the investigation found this is one of the
cheapest features on the whole board, and it directly complements item 2
(more interesting generated layouts) and hand-authored maps alike.

### What already makes this cheap
- `blender/assets/__init__.py` auto-discovers every asset module — a new
  `structure_wall.py` needs no registration anywhere.
- `StructureTuning.Roster()` is the single source of truth consumed by
  *both* `DestructiblePrefabBuilder.BuildAll()` *and* the level editor's
  placement palette (`LevelEdits.Palette() ⇒ StructureTuning.Roster()`) —
  adding one enum value to that array makes it buildable *and* placeable
  with no separate editor-UI work.
- `LevelBuilder.BuildStructures` already resolves any `StructureKind`
  generically via `catalog.PrefabFor(kind)` — needs zero code changes.
- No schema bump needed — precedent (the M9 turret addition, the earlier
  surfaces work) says only *new semantics* an older build would misread
  require one; a plain scenery kind doesn't.

### Steps
1. **`StructureKind`** — add `Wall = 9`
   (`unity/Assets/RF/Scripts/Destruction/StructureKind.cs`).
2. **Blender asset** — new `blender/assets/structure_wall.py`, following
   `structure_turret.py`'s three-state pattern (`ASSET_INTACT`/
   `ASSET_DAMAGED`/`ASSET_DESTROYED`, primitives-only, flat vertex color
   per the asset spec's palette — `CONCRETE` or `METAL` are the natural
   picks). Short and thin, sized to read as a fence/barrier segment rather
   than a building.
3. **`StructureTuning.For(StructureKind.Wall)`** — new case: `HitPoints`/
   `DamagedAt`/`DebrisRadius` balanced thinner than a Tree (40hp) or
   Building (220-260hp) — it's meant to be a breakable barrier, not a
   fortification in itself.
4. **`StructureTuning.Roster()`** — add `Wall`. This single line is what
   makes it buildable and placeable automatically.
5. **`DestructiblePrefabBuilder.CategoryOf(kind)`** — add a case. **Known
   gotcha**: this exact switch bit the turret's addition in M9 (fell
   through to the wrong naming category) and was only caught by the
   existing audit test — see step 7.
6. **`LevelCatalog`** — add a prefab row via **Tools > IronFlag > Build
   Level Catalog**; `LevelCatalog.Problems()` already self-reports a
   missing row.
7. **Run `StructureRosterTests.EveryStructureKindIsDestructibleEndToEnd`**
   (the existing audit test that caught the M9 category bug) as the
   concrete check that nothing was missed.

### Scope decision: fixed-length segments, not a parametric wall
`LevelStructure` only carries `Position` + `YawDegrees` — a single point,
exactly like a Tree. That fits "short segments placed like fence posts"
with zero format changes, and matches how everything else in the level
format works. A variable-length wall spanning two arbitrary points would
need a new placement primitive (closer to `LevelLand`'s rectangle system
than anything `LevelStructure` does today) — a materially bigger feature.
**Recommendation: fixed-length segments for v1** — place several in a row
to build a longer wall, same as the reference game's construction-by-
repetition style everything else already follows. Revisit a parametric
version only if fixed segments turn out to feel wrong in practice.

### Files touched
- `unity/Assets/RF/Scripts/Destruction/StructureKind.cs` — new enum value
- `blender/assets/structure_wall.py` — new
- `unity/Assets/RF/Scripts/Destruction/StructureTuning.cs` — new case +
  roster entry
- `unity/Assets/RF/Editor/Gameplay/DestructiblePrefabBuilder.cs` — new
  category case
- `unity/Assets/RF/Levels/LevelCatalog.asset` — new row (via the Tools
  menu build step)
- `return-fire-homage-asset-spec.md` — palette/dimensions entry, per the
  project's own documentation convention

### Open questions
1. **Does a destroyed wall block movement?** Every other destructible's
   rubble is explicitly *not* solid (colliders switch off on the
   `Destroyed` state) — worth deciding on purpose whether a wall should be
   the first exception, since "little fortified areas" plausibly wants the
   rubble to still obstruct, at least partially.
2. **Does a wall belong to a side?** Turrets are the only structure kind
   today with `NeedsASide` (`Team` ownership). A neutral wall (anyone can
   hide behind it) is simplest and matches the "little fortified area"
   framing; a team-owned wall would need the same `Side` plumbing turrets
   already have, for unclear extra benefit.

---

## 4. Main Menu

> **Done.** Shipped close to the plan, with one of its steps corrected and one of its
> exclusions reversed. **Step 3 was wrong**: level-select must not call
> `LevelHandoff.Playtest`, which sets the flag `PlaytestReturn` reads — that would have put a
> "back to the editor" notice over every match in the game. `LevelHandoff.Play` exists now to
> say the *menu* sent this. And open question 3 (return-to-menu) is **in**, both ways, because
> without it the menu is a screen the game shows once per launch: Escape twice out of a match,
> a guarded MENU button out of the editor — Escape could not be used there, it already clears
> the selection. Question 1 was answered "screen mode, size and quality tier", on the rule that
> a setting has to do something the day it ships; question 4 was answered by opening a level
> file — the map list shows each map's own name over its size, towers and props, and not the
> `Description`, which on the shipped map is 1,900 words. The unplanned cost was the backdrop:
> this game has no horizon, and framing an oblique camera that cannot see the edge of the world
> took three attempts. What was built, what was decided and what is still open is in
> [MAIN_MENU_NOTES.md](MAIN_MENU_NOTES.md). The original text is left below unchanged.

Add a Main Menu scene as the game's new startup scene, with Play (level
select), Level Editor, Settings, and Quit. Reuse the project's existing
conventions: scenes are code-generated, UI is hand-built UGUI-from-code
following `EditorTheme`, and scene flow goes through `LevelScenes` +
`LevelHandoff`.

**Note**: item 9 (UI Visual Identity) will introduce TMP, a real typeface,
and a tactical-HUD visual language for the whole game's UI. If that item
lands first (or alongside), build the menu against its output instead of
raw `EditorTheme` to avoid a re-skin later. Also note: Play's level-select
list already reads `LevelLibrary.Names()`, so any map produced by item 2's
generator shows up there automatically — no extra wiring needed.

### Current flow (before this change)
- Build order: `Sandbox` (index 0, boot scene) → `LevelEditor` (index 1).
- Both scenes are generated by editor builder classes (`VehicleSandboxScene.cs`,
  `LevelEditorScene.cs`) under `Assets/RF/Editor/Gameplay/`, not hand-authored.
- `LevelScenes.cs` centralizes scene name constants (`Game`, `Editor`).
- `LevelHandoff.cs` is the static cross-scene handoff: `Playtest(name)` /
  `Edit(name)` set state, then caller does `SceneManager.LoadScene(...)`.
- `LevelLibrary.Names()` enumerates all available level names (shipped +
  user), already consumed by `EditorUi.ShowOpenPanel()` as a scrollable
  list of buttons — the template for a level-select screen.
- No `GameManager`/bootstrap singleton exists. No settings/options system
  exists (no `PlayerPrefs` usage anywhere in the project).

### Target flow (after this change)
- Build order: `MainMenu` (index 0, new boot scene) → `Sandbox` (1) →
  `LevelEditor` (2).
- Main Menu offers: **Play** (opens level-select list) → **Level Editor** →
  **Settings** → **Quit**.
- Play → level select uses `LevelLibrary.Names()`; choosing a level calls
  `LevelHandoff.Playtest(name)` then `SceneManager.LoadScene(LevelScenes.Game)`
  — same mechanism the editor's "PLAY THIS MAP" button already uses.
- Level Editor entry calls `SceneManager.LoadScene(LevelScenes.Editor)`
  directly (new: currently there's no direct entry point to the editor other
  than the playtest round-trip via F1).
- From `Sandbox`/`LevelEditor`, no return-to-menu path is required by scope,
  but note where one would hook in (`PlaytestReturn.cs` already models a
  return-to-editor flow via F1; a symmetric "back to menu" key could reuse
  that pattern later — out of scope now, see open questions).

### Steps

1. **`LevelScenes.cs`** — add `public const string MainMenu = "MainMenu";`.
2. **Theme reuse** — reuse `EditorTheme.cs` directly for menu chrome
   (panel/button/label factories already fit a dark-panel menu look) rather
   than inventing a `MenuTheme.cs`, *unless* item 9 has landed first, in
   which case build against its output instead. Confirm during
   implementation whether any editor-specific assumptions in `EditorTheme`
   (e.g. field/inspector widgets) leak in; if so, extract a minimal shared
   base instead.
3. **`MainMenuController.cs`** (new, `Assets/RF/Scripts/UI/` or a new
   `Assets/RF/Scripts/Menu/`) — MonoBehaviour driving the generated menu
   Canvas:
   - Top-level panel: Play / Level Editor / Settings / Quit buttons.
   - Play → swaps to a level-select panel built from `LevelLibrary.Names()`
     (same list-of-buttons pattern as `EditorUi.ShowOpenPanel()`), each
     button does `LevelHandoff.Playtest(name)` +
     `SceneManager.LoadScene(LevelScenes.Game)`.
   - Level Editor → `SceneManager.LoadScene(LevelScenes.Editor)`.
   - Settings → swaps to a settings panel (see step 5).
   - Quit → `Application.Quit()` (guarded with `#if UNITY_EDITOR` no-op vs.
     `EditorApplication.isPlaying = false` for in-editor testing, matching
     however the project already handles editor-vs-build differences
     elsewhere, if it does).
4. **`MainMenuScene.cs`** (new, `Assets/RF/Editor/Gameplay/`) — editor
   builder class generating `MainMenu.unity`, following
   `VehicleSandboxScene.cs`/`LevelEditorScene.cs` as templates: Canvas +
   EventSystem + `MainMenuController` root object. Add a `Tools > IronFlag >
   Build Main Menu Scene` menu item consistent with existing tooling.
5. **Settings panel** — scope is minimal since no system exists yet. Plan:
   a small `GameSettings.cs` (static, `PlayerPrefs`-backed) holding whatever
   the user actually wants exposed. **Needs decision** — see open questions.
6. **Build settings** — insert `MainMenu.unity` at index 0 in
   `EditorBuildSettings.asset`, shifting `Sandbox` to 1 and `LevelEditor` to 2.
   Do this via the `Tools` menu item's generation step or a one-time manual
   edit, whichever matches how the project already manages build scene order.
7. **Manual test pass** — boot into `MainMenu`, verify: Play → level list
   shows all shipped + user levels → selecting one loads `Sandbox` with the
   right level; Level Editor button loads `LevelEditor` cleanly; Quit works
   in a built player (can't fully verify `Application.Quit()` in-editor);
   Settings panel opens/closes and persists a value across a scene reload.

### Files touched/added (summary)
- `unity/Assets/RF/Scripts/Levels/LevelScenes.cs` — edit (add constant)
- `unity/Assets/RF/Scripts/Menu/MainMenuController.cs` — new
- `unity/Assets/RF/Scripts/Menu/GameSettings.cs` — new
- `unity/Assets/RF/Editor/Gameplay/MainMenuScene.cs` — new
- `unity/Assets/RF/Scenes/MainMenu.unity` — new (generated)
- `unity/ProjectSettings/EditorBuildSettings.asset` — edit (scene order)

### Open questions
1. **Settings scope** — what should actually be in the Settings panel?
   Candidates: audio volume, mouse sensitivity/invert, graphics quality,
   fullscreen/resolution, key rebinding. Since nothing exists yet, this is
   greenfield — pick a minimal starting set.
2. **Quit behavior in editor** — should Quit be hidden/disabled when running
   in the Unity Editor (since `Application.Quit()` is a no-op there), or is a
   stub acceptable?
3. **Return-to-menu** — out of scope per current answers, but should ESC or
   a menu button anywhere in `Sandbox`/`LevelEditor` eventually get back to
   `MainMenu`? Flagging so it's not forgotten, not blocking this item.
4. **Level select preview info** — `EditorUi.ShowOpenPanel()` lists names
   only. Does Play's level-select want any preview (thumbnail, size, best
   time) or is a plain name list sufficient for v1?

---

## 5. Combat & Movement VFX

> **Done**, all six items, in two passes — see [VFX_NOTES.md](VFX_NOTES.md). The
> hand-coded pair shipped first; the four particle items followed once the Design Stance
> question above was answered, and the answer was **yes, use particle systems**. The old
> objection ("an asset nobody can review in a diff") is answered rather than waived: one
> file knows Unity's particle API, every effect is a dozen named numbers, and all five sets
> of numbers sit on one screen. Two of the plan's premises were wrong and are corrected in
> the notes — the existing `Explosion` is *not* kill-only, and there is no such thing as a
> vehicle moving through shallow water, because both water surfaces drown you. The original
> text is left below unchanged.

**Why this priority**: highest player-facing "juice" payoff, and the anchor
points already exist unused — `Muzzle` transforms ship on every vehicle
prefab, `Underfoot`/`Standing` hooks already exist per `SURFACES_NOTES.md`.
This is finishing wiring that was clearly left as a stub, not new
architecture.

1. **Muzzle flash** *(hand-coded, deterministic — fires on every shot)* —
   mirror `Explosion.cs`'s pattern: a small billboard quad at the `Muzzle`
   anchor, scale+opacity flash over ~0.05–0.08s, paired with a brief small
   point-light pulse. New small emissive material. Wire into the firing
   method in `VehicleWeapon.cs`/`AutoTurret.cs` (locate exact call site
   during implementation — both already share the projectile pipeline).
2. **Impact sparks** *(hand-coded, same closed-form-arc approach as
   `DebrisBurst.cs`)* — brief bright line-segment/quad burst on non-lethal
   hits against `METAL`-palette surfaces, giving hit feedback distinct from
   the kill-only `Explosion`.
3. **Damage smoke** *(ParticleSystem — cosmetic, state-driven, first
   ParticleSystem in the project)* — low-count (~5–10), soft dark billboard
   puffs, slow rise+fade, active while a vehicle/structure sits in the
   `Damaged` state (variants already exist per the asset spec); on/off is
   driven deterministically by health state even though individual puff
   motion isn't — that asymmetry is exactly the "cosmetic-only randomness"
   carve-out from the Design Stance above.
4. **Destruction smoke column** *(ParticleSystem)* — heavier burst +
   lingering smoke layered alongside the existing `DebrisBurst`/`Explosion`
   on the `Destroyed` transition. Those two stay unchanged — they already
   work; this only adds a lingering layer on top.
5. **Dust trails** *(ParticleSystem, emission rate driven by vehicle
   speed)* — wheels/tracks kick up dust while moving on `Sand`/`Grass`/
   `Asphalt`, using the existing but currently-unused `Underfoot`/
   `Standing` hooks. Sample dust tint from `SurfaceTuning` per-surface so
   dust color matches the ground it's kicked from, instead of one generic
   dust color — continuity with the existing surface-color system rather
   than a new authoring surface.
6. **Water splashes** *(ParticleSystem + hand-coded ring)* — radial
   splash on shell impact in `ShallowWater`/`DeepWater`; small persistent
   wake/foam behind vehicles moving through shallow water. Depends
   loosely on item 7's water pass landing first for visual consistency,
   but can be stubbed independently.

**Files touched**: `unity/Assets/RF/Scripts/Combat/` (new muzzle
flash/spark scripts alongside `Explosion.cs`), `unity/Assets/RF/Scripts/
Destruction/` (smoke, alongside `DebrisBurst.cs`), likely a new `Assets/RF/
Scripts/Vfx/` or `Effects/` folder for the ParticleSystem-driven pieces to
keep the deterministic-vs-cosmetic split visible in the folder structure
itself, `CombatPrefabBuilder.cs` (new prefabs), `VehicleWeapon.cs`/
`AutoTurret.cs` (fire hook), `VehicleHealth.cs`/`Destructible.cs`
(state-transition hooks), `GroundVehicle` (dust hook-up).

---

## 6. 1-Player Mode

> **Shipped** — see [SOLO_NOTES.md](SOLO_NOTES.md). The plan below is left as written, and
> what it got right is worth reading: the objective loop needed zero changes, the
> player/input/camera layer really was already generic over player count, and the only thing
> hardcoded to two was where seats are built. Three things came out differently. The seat
> count was decoupled at *runtime* (`SessionSeating`) rather than in the scene builder,
> because the map is chosen from a menu long after the scene is saved. Step 3 was not taken:
> rather than accepting validation warnings as expected noise, `LevelValidation` learned what
> a one-player map is — which also let the generator delete its private copy of the same
> rules. And the two open questions were already answered by the generator's own solo shape:
> no defence on the home bunker, and decoy count as the difficulty lever.

The garage is yours; the flag is the enemy's, held in one of potentially
several flag towers — you have to find it by destroying towers (decoys
look identical until damaged, exactly like multiplayer), then run it home
in the jeep to win. The opposition is turrets only; no second human, no
AI-driven vehicle.

**Why this priority**: real player-facing value, and — now that the win
condition is confirmed to be the existing flag-capture loop rather than
something new — a smaller lift than originally scoped. Still benefits from
item 2 (Map Generator) already existing to produce solo-shaped test maps
quickly.

### Confirmed shape
- **Green (player) side**: one bunker, **zero** flag towers. Home base
  only — nothing there to defend, no reason for it to be guarded, since
  nothing in this mode attacks it.
- **Brown (enemy) side**: **zero** bunker, **multiple** flag towers (one
  real, `HoldsTheFlag`, indistinguishable from the decoys until damaged —
  identical to how the *opponent's* flag already works in a 2-player
  match), guarded by `Team.Brown` turrets.
- **Win condition: unchanged from multiplayer.** `Flag.Capture()` already
  fires when a jeep carrying the flag reaches a `SupplyPoint` belonging to
  its own team; `Match.OnCaptured` → `Match.Win()` already handles the
  rest. No new `Match.cs` logic needed — this mode is the existing
  2-player objective loop with a human missing from one side, not a new
  objective.

### What's already there (better than expected)
- **The entire flag/tower/decoy/capture loop needs zero changes.**
  `FlagTower`'s decoy mechanic and `Flag`/`Match`'s capture-and-win wiring
  already implement exactly "hunt down which tower holds the flag by
  damaging towers, then run it home" — that's what a 2-player match's
  attack on the *opponent's* flag already is. 1-player mode is that same
  mechanic pointed at a Brown side with no human behind it, not a new
  system.
- The player/input/camera/HUD layer is **already generic over player
  count**, not hardcoded to 2: `LocalMultiplayer` holds a
  `List<PlayerVehicleDriver>`, `DeviceAssignment.Solve(playerCount, ...)`
  already handles any count, and `SplitScreenLayout.ViewportFor` already
  has a working 1-player full-screen path (`playerCount < 2 →
  FullScreen`). None of this needs to be built — it exists and works
  today.
- `AutoTurret.Target()` has **zero concept of "human"** — it targets any
  deployed `VehicleController` whose team is hostile, regardless of
  what/who is driving it. Turret-only defense of Brown's towers requires
  no changes to combat code at all.
- The level-building layer (`LevelBuilder.BuildBunkers`/`BuildObjective`)
  is already generic over however many entries
  `LevelDefinition.Bunkers[]`/`Towers[]` actually contain — a level file
  with one bunker and zero-or-many towers per side "just works" at this
  layer, including the "multiple flag towers" ask: `CheckTowers` only
  rejects *fewer* than 2 towers for a side, never more.
- `LevelValidation` is **advisory, not enforced** — `LevelLoader.Show()`
  logs problems rather than refusing to load a level. This asymmetric
  level file wouldn't be blocked from loading, only flagged.

### What's actually hardcoded to 2, and needs to change
- `VehicleSandboxScene.PlayerCount = 2` (Editor-only, bakes the boot scene
  `Sandbox.unity`) — today, "how many humans are seated" and "how many
  sides get a roster built" are the *same* per-slot loop. These need
  decoupling: seat 1 human, but only build/deploy a roster for Green;
  Brown gets turrets only, no `PlayerVehicleDriver`, no vehicles.
- `Team` enum only has `Green`/`Brown` — fine as-is; 1-player mode doesn't
  need a 3rd team, it needs Brown to have no *roster*, not to not exist as
  a team (turrets and towers still need a real `Team` value).

### Steps
1. Decouple `VehicleSandboxScene`'s per-slot loop: seat count (humans) and
   roster-building (which sides deploy vehicles) become independent
   parameters instead of one shared `PlayerCount` loop variable.
2. Level format: author (or generate, via item 2's 1-player option) a
   level with one `Bunkers[]` entry (Green) and zero Green towers, and
   multiple `Towers[]` entries for Brown (one real + several decoys, per
   the existing `HoldsTheFlag` rule) with no Brown bunker, guarded by
   `Team.Brown` turrets.
3. Accept the resulting `LevelValidation` warnings ("Brown has no bunker",
   "Green has fewer than 2 towers") as expected noise for this mode's
   shape — consistent with the project's existing "log, don't block"
   philosophy and this feature's own "doesn't have to be perfect" spirit.
   Only teach `LevelValidation` an explicit "solo" concept if the constant
   warnings turn out to actually bother anyone in practice.
4. Manual test pass: boot a 1-player map, confirm no Brown vehicles/driver
   ever spawn, confirm turrets engage the solo player correctly, confirm
   damaging a decoy tower reveals nothing while damaging the real one
   makes the flag visible/carriable, confirm reaching Green's bunker with
   it wins.

### Files touched
- `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` — decouple seat
  count from roster-building
- Level file(s) — at least one hand-authored or generated 1-player test
  map
- `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` — only if warning
  suppression turns out to be needed (see step 3)

### Open questions
1. **Decoy tower count** — fixed, or scaling with item 2's difficulty
   tiers (more towers to check before finding the real one is a natural,
   thematically-fitting difficulty lever for this mode specifically,
   separate from turret count/placement)?
2. **Does Green's bunker get any turret defense**, or is home base always
   safe in this mode? Nothing in the confirmed shape attacks it, so the
   default assumption is no — worth a one-line confirmation rather than
   silently deciding it.
3. **Turret difficulty scaling** — turret stats are global (not
   per-instance), so difficulty still comes from turret count/placement
   (shared mechanism with item 2's difficulty option) and now also decoy
   tower count (question 1), rather than tougher individual turrets,
   unless the format is extended later.

---

## 7. Ground & Water Material Upgrade

**Why this priority**: significant visual payoff but the higher-effort
item — real shader authoring, and it must respect the surface color ramp
(a tested, deliberate decision from the finished Surfaces pass — this item
adds *detail*, it does not reopen the `SurfaceTuning.cs` color/value
debate).

1. **Water shader** *(new custom shader — decide Shader Graph vs. hand-
   rolled HLSL during implementation)* replacing the flat `RF_Water`/
   `RF_Surface_ShallowWater` materials:
   - Fresnel-based edge brightening.
   - Gentle scrolling distortion — either a small tiling normal texture
     (first texture asset in the project) or pure procedural sine-wave
     UV/vertex distortion if staying textureless is preferred.
   - Depth-based blending via URP's camera depth texture, for a soft
     shore edge instead of today's hard material boundary.
   - A specular highlight so sun direction actually reads on the water.
   - Keep it subtle — the goal is "no longer reads as flat dead paint,"
     not a photorealistic ocean; that would fight the low-poly style.
2. **Ground micro-detail** — very low-intensity tiling normal-map-only
   detail per surface (no albedo change, so the readability-driving base
   color is untouched), or a fully textureless alternative: procedural
   triplanar noise perturbation of the surface normal in a small hand-
   written shader. Pick one during implementation; both avoid touching
   `SurfaceTuning.cs`'s actual colors.
3. **Decals** — URP's built-in Decal Projector (engine tooling, no new
   shader/texture authoring) for tire tracks on `Sand`/`Grass` and scorch
   marks around destroyed structures, extending the existing `CHARRED`/
   `SCORCH` palette onto the ground itself instead of stopping at the
   destroyed prop mesh.
4. **Coastline foam** — thin animated foam line where `ShallowWater` meets
   the beach band, driven by the signed-distance-to-coast field that
   `SurfaceField.cs` already computes per cell — this is reusing existing
   data, not new authoring, just a new consumer of it.

**Files touched**: new `RF_Water.shader` (+ updated `RF_Water.mat`,
`RF_Surface_ShallowWater.mat`), `unity/Assets/RF/Editor/ArtPipeline/
GeneratedMaterials.cs` (wire the new shader in), possibly `SurfaceMesh.cs`
(if foam/detail needs extra per-vertex data like distance-to-coast piped
into a UV channel), new Decal Projector prefabs.

**Open questions**:
1. Water shader authoring approach — Shader Graph vs. hand-rolled HLSL;
   affects both the diff-reviewability tradeoff (HLSL is plain text, Shader
   Graph is a serialized graph asset — closer to the "asset nobody can
   review in a diff" objection than a texture would be) and iteration
   speed. Worth a deliberate choice rather than defaulting to whichever is
   more familiar.

---

## 8. Sounds and Music

The design doc's own M8 "polish pass" has always listed "per-vehicle
music/SFX hook" as its scope — this isn't new scope, it's finally starting
work that was planned from the beginning and has simply never been
touched.

**Why this priority**: high player-facing value, but the highest
scope/ambiguity of everything newly added here — it touches nearly every
system for wiring (combat, vehicles, flag, match, UI), and it's the
**first feature in the whole project that unavoidably needs real external
asset files** rather than code-generated content. Sequencing it near items
5 (Combat & Movement VFX) and 9 (UI Visual Identity) makes sense
thematically — all three are "juice" passes — but it's kept as its own
item given its distinct scope and the sourcing decision it needs first.

### Where this starts from: genuinely nothing
The investigation confirmed audio is 100% greenfield: zero
`AudioSource`/`AudioClip`/`AudioMixer` references anywhere in scripts or
scenes, zero `.wav`/`.mp3`/`.ogg` files in the repo. Two empty placeholder
folders already exist (`unity/Assets/RF/Audio/Music/`,
`unity/Assets/RF/Audio/SFX/`, each holding only a `.gitkeep`) — someone
scaffolded the convention without filling it in. The **only** existing
audio-related code is `AudioListener` placement/hygiene for split-screen:
exactly one `AudioListener` exists across the whole multi-camera rig (seat
0's camera only), enforced by an existing test (`SandboxWiringTests`) with
the comment "a split screen still only has one set of speakers." Any new
audio work must preserve this invariant, not add per-seat listeners.

### The precedent this sets — a decision worth making explicitly
Every other asset in this project is generated by code (Blender-Python for
models, C# for materials/levels/UI). Audio can't follow that pattern the
same way — real sound needs either licensed/sourced clips, commissioned
original work, or runtime-synthesized audio (procedurally generated
tones/noise, which *would* fit the project's existing code-generates-
everything philosophy but is a much bigger technical undertaking for
anything beyond simple beeps and hits). This is the same tension the
Bunker View item's open question #3 already flagged for hand-authored
console art — worth deciding once, consistently, rather than separately
for each feature that hits it. **Recommendation: license/curate free or
affordable SFX and music (e.g. CC0 libraries, royalty-free packs) for a
first pass**, since commissioning original work is a bigger commitment
than a first version needs and full runtime synthesis is a research
project in itself — but this is genuinely the user's call, not a technical
one.

### A real architectural wrinkle: split-screen + positional audio
With exactly one `AudioListener` for the whole split-screen rig (a hard
constraint, not a preference — it's test-enforced), 3D positional sound
only ever sounds correctly *positioned* for whichever seat holds that
listener; the other seat's positional audio would be spatially wrong
relative to their own camera. Most split-screen games sidestep this by
keeping gameplay SFX non-positional (2D) and reserving true 3D positioning
for cases where it doesn't matter which seat "owns" the mix. Recommend
starting with non-positional SFX everywhere and only reaching for
positional audio if flat 2D sound turns out to feel wrong in practice.

### Proposed architecture (matching the project's existing patterns)
- **`AudioCatalog`** (new `ScriptableObject`,
  `unity/Assets/RF/Audio/AudioCatalog.asset`) — mirrors `LevelCatalog`'s
  existing, already-proven solution to the exact same problem ("a built
  player has no asset database, so code can't reference a clip by path
  alone"): a table of `SfxKind`/`MusicKind` → `AudioClip` references, built
  once and shipped as an asset.
- **`Sfx` static helper** (mirrors `Explosion.Spawn(...)`'s shape) — e.g.
  `Sfx.PlayUI(SfxKind kind)` for non-positional one-shots (menu clicks,
  HUD alerts), the safe starting point given the listener constraint
  above.
- **`MusicPlayer`** — one dedicated `AudioSource` for background music
  (menu theme, in-match ambient loop, win/lose stings), separate from SFX,
  driven by existing state changes (`Match.IsFinished`/`Match.Win` for
  stings).

### Concrete wire-up points (all existing hooks, no new systems required to trigger them)
- Weapon fire — `VehicleWeapon.TryFire()` (same call site as item 5's
  muzzle flash — good shared-implementation-session synergy).
- Impacts/explosions — `Explosion.Spawn(...)`, structure damage-state
  transitions in `Destructible`.
- Engine sound — per-vehicle looping `AudioSource` on each
  `VehicleController`, volume/pitch tied to throttle. The one genuinely new
  *continuous* (not one-shot) audio behavior here — budget more time for
  this than the one-shot hooks.
- Flag pickup/capture/return — `Flag`'s state transitions.
- Match win/lose — `Match.Win`.
- UI — Main Menu button clicks, once item 4 (Main Menu) exists.

### Files touched
- `unity/Assets/RF/Audio/AudioCatalog.asset` — new (+ its `SfxKind`/
  `MusicKind` enums and a builder script, following `LevelCatalog`'s
  pattern)
- `unity/Assets/RF/Scripts/Audio/` — new folder: `Sfx.cs`, `MusicPlayer.cs`
- `VehicleWeapon.cs`, `Explosion.cs`, `Destructible.cs`, `Flag.cs`,
  `Match.cs`, `VehicleController.cs` — wire-up call sites
- `unity/Assets/RF/Audio/Music/`, `unity/Assets/RF/Audio/SFX/` — actual
  clip files, once sourced

### Open questions
1. **Sourcing** — license/curate existing clips (recommended above) vs.
   commission vs. runtime-synthesize. Blocks everything else in this item.
2. **Positional vs. non-positional** — confirm starting non-positional
   given the single-listener constraint, or accept the spatial compromise
   for one seat.
3. **Scope of a first pass** — every wire-up point above, or a smaller
   starting subset (e.g. weapon fire + music only, engine sound later
   given it's the one continuous/non-trivial hook)?

---

## 9. UI Visual Identity

**Why this priority**: cosmetic/UX character rather than "does the game
look good in a screenshot" — and it directly determines how the Main Menu
(item 4) and Bunker View (item 10) should be themed, so sequencing it
before or alongside those avoids re-skinning work twice.

1. **TMP migration** — import TextMeshPro (Essentials), replace
   `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` usage in
   `HudPalette.cs`/`EditorTheme.cs` with TMP equivalents. Unlocks real
   outlines, better small-text legibility, an actual chosen typeface
   instead of an Arial fallback.
2. **Typeface** — pick a condensed/stencil tactical-styled font consistent
   with the military-vehicle theme. *(Open question: no specific font
   picked yet — needs a concrete choice, e.g. from an OFL-licensed
   family.)*
3. **Tactical-HUD visual language** — corner-bracket frame accents on
   panels instead of plain rectangles, a thin scanline/vignette treatment
   that echoes item 1's world-space vignette so HUD and world read as one
   consistent filter, and a small icon set (fuel/ammo/armour glyphs) next
   to the existing bars in `HudBar.cs` instead of text-only labels.
4. **Motion/juice** — eased bar-fill transitions instead of instant snaps,
   a pulse/flash on `Alarm`-threshold values (reusing `HudPalette.Alarm`),
   panel slide/fade transitions in the Main Menu.
5. **Editor UI parity** — apply the same typography/motion polish to
   `EditorTheme.cs` afterward, lower priority than in-game HUD/menu since
   it's a developer-facing tool, not player-facing.

**Files touched**: `unity/Assets/RF/Scripts/UI/HudPalette.cs`,
`unity/Assets/RF/Scripts/UI/HudBar.cs`, `HudLayers.cs`, `PlayerHud.cs`,
`unity/Assets/RF/Scripts/Editing/EditorTheme.cs`/`EditorButton.cs`
(parity pass), plus item 4's Main Menu build, which should consume
whatever typeface/visual-language decisions land here.

**Open questions**:
1. Typeface pick — needs an actual font chosen, not just
   "condensed/stencil/tactical."

---

## 10. Bunker View Rework

Condensed from the original `M4_BUNKER_VIEW_PLAN.md`. A follow-on pass over
M4's vehicle-select screen — M4 itself is finished (`M4_NOTES.md`); nothing
here changes the rules it established, only what the player is looking at
while those rules run. Read `M4_NOTES.md` first if implementing this: it
assumes the bunker flow, `VehicleBay`, `TeamBunker` and `PlayerHud` as they
stand.

### What's there now vs. the goal

M4 gives the player a translucent panel with four text rows, floating over
a top-down view of their bunker's roof — true, but not a place. The
reference (the original game this is a homage to) instead shows a cutaway
of an underground base: four lit bays around a central elevator shaft
(helicopter + tank upper level, support vehicle + jeep lower level), each a
dark box with the vehicle inside, lit, plus a hardware console along the
bottom third (vehicle status, fuel, deploy/map buttons). The key property:
**none of it is a menu** — the player is looking at their own base, and the
thing they press is a button on a console, not a highlighted row of text.

Our bunker is a different building from the original's (an above-ground
concrete blockhouse per the asset spec, not a hatch in open ground), so
this isn't a straight copy — the proposal builds the underground half our
exterior already implies.

### The proposal in one paragraph

Build a two-level underground hall with four bays and a central shaft below
the existing blockhouse, put the stowed vehicles in it *visibly* (currently
hidden — renderers/colliders off), and point the choosing player's camera
at it from the side in cutaway. Turn M4's ride-out into a real elevator
carrying the vehicle up the shaft to the existing lift platform. Move the
roster panel from a floating list to a console strip along the bottom of
the viewport; show selection by lighting the bay in the world rather than
by a highlighted row. Keep the helipad — the helicopter rides the same
shaft and steps off at the roof instead of at ground level.

### Key decisions (recommendations given, real choices)

1. **Bay location** — hall goes under the blockhouse, shaft rises to the
   existing `TeamBunker.LiftPoint` platform (5.2m in front). No gameplay
   geometry moves, no M4 test changes. *Recommended.*
2. **Helicopter bay** — give it an underground bay too, riding the same
   shaft, stepping off at the roof pad. Keeps the single-shaft read; a
   separate roof-level space would cost an extra asset for no real gain.
3. **Select camera heading** — use each bunker's own facing (not the
   fixed-yaw rule M2 established for the *battlefield* camera — that rule
   protects split-screen agreement about world directions, which isn't in
   play while indoors choosing).
4. **Stowed vehicles become visible** — parked in-bay, colliders/movement
   off, underground and out of weapon reach. Combat is unaffected; this
   just changes the *mechanism* by which they're unreachable (was "hidden,"
   becomes "underground").
5. **Console contents** — since there's one of each vehicle (no fleet
   count to show), the bays themselves act as the icons (in-world, lit,
   vehicle visible in them); the console carries name, state, fuel, rounds,
   and the deploy prompt.

### Phases

- **A — the underground base, in Blender.** Two new spec-conformant assets
  (primitives, flat vertex color): `RF_Structure_BunkerHall` (two-level
  hall, four bay recesses, central shaft with guide rails, hazard chevrons
  — the near/field-facing wall is simply not built, which is what makes a
  bay visible from the select camera) and `RF_Structure_BunkerLift` (the
  elevator car, since it moves and the hall doesn't). Four marker children
  `Bay0`..`Bay3` on the hall give bay positions as an art decision, same
  trick `LiftPlatform`/`Helipad` already use. Strip the old static
  `LiftPlatform` slab; the elevator car becomes the platform at its up
  position.
- **B — the cutaway view.** `TeamBunker` gains the hall + `BayFor(slot)`; a
  new static/testable `BunkerView` component computes the cutaway camera
  pose (low pitch 8–14°, bunker's own yaw); `TopDownCameraRig` gains a
  parked-pose overload. Occlusion is mostly free (underground, single-sided
  ground plane) except a player driving over their own base — mitigate by
  enabling each hall only while its own player is choosing. Needs a new
  `RF_BayLight` emissive material (pattern already established by tracer/
  blast materials) plus one small warm point light per bay — an underground
  hall gets nothing from the sun, so bay lighting is most of what sells the
  look.
- **C — the elevator.** `VehicleBay` drives the car (two straight-lerp
  legs: bay→shaft, shaft→surface) instead of lerping a vehicle through
  empty ground; helicopter's leg ends at the roof pad. No physics hole
  needed — vehicle stays kinematic for the whole ride, same as today.
- **D — the console.** Replace the floating bunker panel with a bottom-of-
  viewport strip (same slot the driving HUD strip already uses). Selection
  moves into the world (brighter/team-accented bay lighting) instead of a
  highlighted row. Optional: real vehicle icons via the existing
  `ArtPreviewScene`/`CameraCapture` render-to-PNG tooling, only if the bays
  don't read at a glance on their own.
- **E — tests, still, notes.** Edit-mode geometry/ordering tests, play-mode
  visibility/elevator/occlusion tests, a new reference screenshot, and the
  result folded into `M4_NOTES.md` (same milestone, finished properly) —
  not a fifth notes file.

### Deliberately excluded
Minimap (M8's), sound (M8's per-vehicle audio hook — see item 8), bunker
destruction states (asset spec is explicit the bunker has none — it's the
win-condition target), a vehicle fleet/count (that's M6's attrition), a
separate select-scene or render texture (one generated scene is the whole
point).

### Biggest risks
- **Lighting an interior** — the scene has one sun + trilight ambient tuned
  for daylight; bay emissives have to carry the whole look. Expect real
  iteration time here.
- **Mesh colliders on generated geometry** — `AddStaticColliders` would put
  a `MeshCollider` on every hall `MeshFilter` for no reason (nothing can
  reach it underground); skip colliders for the hall explicitly.
- **The cutaway is a modelling convention, not a shader** — the near wall
  simply isn't built, so the hall only ever reads from one side; a future
  free-look camera would show it hollow.
- **Emission clipping** — M3 already learned emission much above 4.0 goes
  to flat white; keep bay lights well under that.

### File map, if it goes ahead
| Path | Change |
|---|---|
| `blender/assets/structure_bunker.py` | Loses the lift slab; gains the shaft mouth |
| `blender/assets/structure_bunker_hall.py` | **New.** Two-level hall, bays, shaft |
| `blender/assets/structure_bunker_lift.py` | **New.** The elevator car |
| `unity/Assets/RF/Scripts/Core/TeamBunker.cs` | Gains the hall and `BayFor(slot)` |
| `unity/Assets/RF/Scripts/Core/VehicleBay.cs` | Parks visibly in a bay; rides the car |
| `unity/Assets/RF/Scripts/Core/BunkerView.cs` | **New.** Cutaway framing, static/testable |
| `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs` | A parked pose, not just a parked point |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Shows the hall; enables its own |
| `unity/Assets/RF/Scripts/UI/PlayerHud.cs` | Roster panel becomes a console strip |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Gains `RF_BayLight` |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Places the hall and its lights |
| `unity/Assets/RF/Tests/...` | New bay-geometry and cutaway tests |
| `return-fire-homage-asset-spec.md` | Bunker entry gains the hall and bay markers |

### Open questions
1. **Two levels or one?** Two-storey reads closer to the reference and is
   what the plan above assumes; a single row of four is less art and less
   faithful. Leaning toward two.
2. **Should choosing be blind?** Already is, as of M4 (camera leaves the
   battlefield); going underground makes it more emphatic, which is
   faithful to the reference but a real cost in a two-player match where
   the other player is still driving. Worth deciding on purpose.
3. **How far to take the console?** Everything above stops at "the same
   idea as the reference." Matching its actual chrome (drawn joystick,
   bevelled metal, CRT panel) is a much larger job — and would be **the
   first hand-authored art in a project that generates all of it**, a
   sharper line than the shader/particle/texture openings in the Design
   Stance section above.

---

## Completed

### 1-Player Mode: one seat, and a map that says so
Finished — see [SOLO_NOTES.md](SOLO_NOTES.md). Item 6. There is no toggle: a level with one
bunker on it **is** a one-player map (`LevelDefinition.IsSolo`, derived rather than stored),
and `SessionSeating` empties the seat whose side the loaded map does not play before anybody
is dealt a controller or half a screen. The player who is left gets the whole screen, the
enemy is flag towers behind emplacements with nobody driving, and the win is the capture loop
a match already had. `LevelValidation` now judges both shapes instead of one, which let
`LevelGenerator.SoloFaults` — a private copy of the same rules — be deleted. Ships one
generated map, `IRON WATCH`, marked `1 PLAYER` on the menu. EditMode 474/474, PlayMode
172/172.

### Turret tweaks: one heading, an early swing, a slower gun
Finished — see [TURRETS_NOTES.md](TURRETS_NOTES.md). **Never in this plan**; three tweaks
asked for directly against M9's emplacement. A side's guns now all start on one heading
(`LevelEdits.FacingTheEnemy`) instead of the generator rolling each one within 25 degrees and
the editor placing every one square to the map — which pointed half of them at their own back
line. A turret watches twelve metres further than it can shoot (`AutoTurret.WatchMargin`), so
the barrel is already on you when you cross into range rather than starting its swing then.
And the gun went from 12 damage every 0.33 s to **15 every 1.5 s** — from 36 damage a second
to 10.

The last of those is the one with teeth: a stream at 36 dps was unanswerable at contact range
and irrelevant one metre outside twenty, so the only decision an emplacement left was which
vehicle outranges it. At 10 dps the **tank can close with one and win** — six seconds, for
sixty to seventy-five of its hundred — while the **jeep still cannot**, which keeps the design
document's first pillar intact. That pair is asserted against the live tables rather than left
as prose (`StructureRosterTests.TheEmplacementLosesToTheTankAndBeatsTheJeep`); 15 and 1.5
themselves are taste and nobody has played them yet.

Reach, hit points and traverse rate are all untouched, so M9's two answers to an emplacement —
stand off in the tank, or get inside sixteen metres and out-turn it — both still work.
Stowing to the rest heading was questioned and deliberately kept: it is what puts a side's row
of guns back on one heading after a raid. EditMode 468/468, PlayMode 166/166.

### Vehicle Reserves, and the second way to lose
Finished — see [RESERVES_NOTES.md](RESERVES_NOTES.md). **Never in this plan**; asked for
directly, on the back of the doors pass. A side now gets a fixed allotment of each vehicle
for the whole match — 8 jeeps, 3 tanks, 3 ASVs and 3 helicopters by default, and whatever
the level file says instead — every wreck comes off it, and nothing puts one back. Run out
of jeeps and you have lost, because only a jeep can carry a flag.

This is the design document's *secondary* win condition, deferred at M6 for want of exactly
this: "it needs either a finite vehicle roster or a destructible bunker, and v0.1 has
neither."

Three decisions to carry forward. **The level file now carries balance for the first time**,
against `LevelDefinition`'s own stated rule, on the argument that how many vehicles a side
is given is a quantity placed on a map rather than a rule about what one of them does — the
remark now says "there is exactly one exception" so that a second is an argument somebody
has to make. **The allotment is one block for the level, not one per bunker**, so it cannot
be asymmetric at all; a handicap belongs to [1-Player Mode](#6-1-player-mode) and its shape
is a per-bunker override of a level default. And **scuttling is no longer free** — it costs
a vehicle exactly like dying does, which is what makes the drive home a decision.

Two things it does not do: the generator does not vary the allotment by difficulty (the
cheapest difficulty lever this game has, still unused), and **neither player can see how
many the enemy has left**, which is arguably where the tension in attrition actually lives.
EditMode 438/438, PlayMode 160/160.

### Main Menu
Finished — see [MAIN_MENU_NOTES.md](MAIN_MENU_NOTES.md). The game starts in a menu now
rather than in a match: `MainMenu.unity` is index 0, which makes it the one scene every
session passes through and therefore the only honest place to apply a stored setting.
Play lists every map in both level folders with each one's size, towers and props read
off its file; Level Editor is the first direct door into the editor the project has had;
Settings is three rows that all do something today, backed by the first `PlayerPrefs`
this project has ever written.

Two things worth carrying forward. **`LevelHandoff.Play` is not `Playtest`** — the plan
said to reuse `Playtest`, and it would have claimed every match came from the editor.
**And this game has no horizon**: `LevelBuilder` builds the sea exactly as wide as the
map, so the first oblique camera anyone has pointed across a level photographed the edge
of the world. A wide shot of a whole map is not available at any distance; the menu shows
a close view of one side's half instead, aimed short of the middle because the middle is
the one place that belongs to nobody.

Escape twice leaves a match and a guarded MENU button leaves the editor — out of scope in
the plan, and the difference between a menu you use and one you pass. EditMode 428/428,
PlayMode 153/153.

### Team Doors, and the gun tower raised to suit
Finished — see [DOORS_NOTES.md](DOORS_NOTES.md). **Never in this plan**; asked for
directly, on the back of the walls pass. `StructureKind.Door` is a wall segment that
belongs to a side and sinks into the floor for that side's vehicles: same five metres,
same grid, same piers at the same joins, and to the enemy it is simply the part of the
wall that is a different colour — and the cheapest part to break, at 60 hit points
against the wall's 80.

It is the **second** structure to take a side, which is the first real test of M9's
claim that `StructureTuning.BelongsToASide` being one method rather than four
comparisons would make a second row cheap. It held: the validator, the builder, the
inspector, the mirror tool and the palette all needed nothing. What did need touching
was the *wording* — four messages and a handful of doc comments said "only a turret."

Four of the shipped map's eight wall segments — the inland one of each bridgehead run —
are now that bridgehead's own gate. EditMode 409/409, PlayMode 147/147.

**The generator places them too**, added straight afterwards: a two-sided map now gets
one gated wall run per crossing per side, sized by difficulty. That needed a placement
path of its own rather than one more loop, because `Settle` exists to keep things apart
and a wall only reads as a wall when its segments touch — see
[GENERATOR_NOTES.md](GENERATOR_NOTES.md#wall-runs-added-after-the-doors-pass).

Two things carried forward from that. **Solo maps get no runs**, deliberately: green's
front is never attacked, and brown's fortresses — where a wall genuinely belongs — need
a run laid *against* a tower rather than settled away from one. And it turned up a
**pre-existing generator bug that is still open**: `Garrison` rings a solo map's towers
at 40–53 m rather than the 13–19 m it intends, because `FortressRoom` is both the
tower's reservation and its keep-away distance. No emplacement on a solo map covers the
tower it guards.

**The gun tower went from 1.68 m to 4.00 m** on the back of this, because a wall at
2.0 m left the emplacement shorter than the fence beside it. Cosmetic only, and the
reason M9 built it low — "so it never becomes cover in its own right" — turned out never
to have been true: a round sweeps a `CombatPlane` column from 0.5 m to 30 m whatever is
in the way, so height in this game is silhouette and nothing else. The three built
structures now read as a sequence — wall 2.0 m, gun tower 4.0 m, flag tower 6.2 m — and
`StructureRosterTests.TheBuiltStructuresReadAsAHeightSequence` is the only thing in the
project that reads a structure's height at all.

### Destructible Wall Sections
Finished — see [WALLS_NOTES.md](WALLS_NOTES.md). `StructureKind.Wall`, a three-state
Blender asset, a tuning row at 80 hit points, and eight segments behind the shipped
map's four bridgeheads. The plan's two open questions were answered **rubble is not
solid** (a breach that still blocks is not a breach) and **a wall belongs to nobody**
(so the wall a side built is cover for whoever reaches it first). Three departures
from the plan's letter, each argued in the notes: the wall is a `Prop`, not a
`Structure`; it is tougher than a tree rather than thinner; and it went onto the
shipped map, which the plan did not ask for. EditMode 405/405, PlayMode unchanged.

The one thing carried forward was **the generator places no walls**, and the generator
notes' estimate that they "slot straight into `Scenery`/`Garrison` — one list and one
loop away" turning out to be optimistic. That has since been done as part of the team
doors work above, and the estimate really was wrong: `Settle` exists to keep placements
*apart* and a wall only reads as a wall when its segments touch, so runs needed a
placement path of their own.

### Random Map Generator
Finished — see [GENERATOR_NOTES.md](GENERATOR_NOTES.md). A GENERATE button in the
level editor draws a whole map from a seed: three ground layouts (island, channel,
lagoon), three sizes, mirrored or asymmetrical halves, and a 1-player option. The
plan's three open questions were answered **archetypes** (not jitter on the starter
skeleton), **ship the solo option now** (flagged as unplayable until item 6), and
**retry-then-report** (up to eight candidates, first clean one wins). Every layout
pays the land-connectivity rule by construction rather than by luck, and every
placement is settled against the realised coastline. EditMode 405/405, 23 of them new.

Two things worth carrying forward. **A solo map breaks three of `LevelValidation`'s
rules on purpose**, because validation states the rules of a *match*; the generator
scores solo candidates on its own `SoloFaults` and those rules should move into
`LevelValidation` when item 6 lands. And **item 3's walls slot straight into
`Scenery`/`Garrison`** — the turret rings around a solo map's flag towers are one
list and one loop away from being small fortresses.

### Lighting, Sky & Post-Processing
Finished — see [LIGHTING_NOTES.md](LIGHTING_NOTES.md). Post-processing now actually
runs (it never had), the default volume profile is generated from a code table
rather than being Unity's untouched template, lighting moved into a
`LightingMood`/`LightingTuning` table shaped so a night-ops mood is a new row,
the procedural sky is retuned for the two places this game's sky is really seen
(metal reflections, and the gap past the edge of a level's sea), and the HUD and
the level editor's panels moved onto stacked cameras so the grade leaves them
alone. Shadow distance went 40 → 120 m. EditMode 382/382, PlayMode 137/137.

### Surfaces — ground materials, coastlines, per-surface handling
Finished, all five phases shipped (see git history / [SURFACES_NOTES.md](SURFACES_NOTES.md)
for the commit). Added the `SurfaceKind`/`SurfaceTuning` table, per-surface
ground materials, natural noise-displaced coastlines with derived beach/
shelf bands, and per-vehicle surface-sensitive handling. The original plan
and the full outcome — decisions taken, gotchas found — are kept together
in [SURFACES_NOTES.md](SURFACES_NOTES.md), with the original plan preserved
verbatim in its [appendix](SURFACES_NOTES.md#appendix-the-original-plan-as-written).
