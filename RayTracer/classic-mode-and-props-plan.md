# Classic Mode + Classic Props — Design & Implementation Plan

An addendum to [`design.md`](design.md) (the original screensaver recreation) and
[`gpu-raytracing-plan.md`](gpu-raytracing-plan.md) (the GPU port). It covers two
requests:

1. **A "Classic" render mode** that looks as much like the original Windows *3D Maze*
   screensaver (`SS3DFO.SCR`) as possible, *while still rendering through the spectral
   path tracer* — not a flat-textured fake.
2. **Adding the five signature props the recreation is still missing** — rat, OpenGL
   logo, wall signs, overhead map, bumpy walls — each with its own on/off switch, and
   each available in **every** render mode (Classic *and* the current Enhanced look).

Everything below targets the productized GPU app (`RayTracer.Gpu`, the Phase 6
`Phase6Renderer` + forked `PathTracePhase6.hlsl` / `ResolvePhase6.hlsl` pipeline). Shared,
deterministic math lives in `RayTracer.Core` with a CPU reference + parity test, per the
project's existing discipline (see `Phase*Reference.cs` + `GpuPhase*Tests.cs`). Overlay
and animation features that are screen-space are validated by dev-box self-tests and the
golden-image harness instead of bit-parity.

---

## Status — implemented & verified (RTX 3070)

All of Classic mode, the five props, and the maze lifecycle are implemented and GPU-verified.
The forked Phase 6 shaders keep the validated Beauty path **bit-identical (Phase 6 self-test
100 %)** whenever a feature is toggled off.

**Default launch** (no args) shows the setup dialog (look, resolution, fullscreen, smoke,
start view, maze size, fog-drift, firefly clamp, and the eight prop/anim toggles — all
persisted to `%APPDATA%\RayTracer.Gpu\settings.json`), then runs. The screensaver switches
`/s`, `/p <hwnd>`, `/c` use the same settings.

**Feature flags** (on the `--phase6` windowed walk and the `--phase6 … --save out.png`
capture):

| Flag | Feature |
|---|---|
| `--classic` | Classic look — unlit fullbright spectral, smoke off, ~90° FOV (§2) |
| `--props` | OpenGL logo (goal/start floor) + wall signs (§3–§5) |
| `--bumpy` | Bump-mapped brick emboss (§6, lit modes) |
| `--map` | Overhead minimap + player marker (§7) |
| `--rat` | Animated rat billboard (§8) |
| `--regen` | Regenerate a fresh maze on reaching the goal (§9.1) |
| `--buildin` | Walls rise from the floor at each maze start (§9.3) |
| `--outro` | Fade to black on completion before regenerating (§9.4) |
| `--seed N`, `--frames N`, `--fullscreen` | reproducible maze / bounded run / fullscreen |
| capture only: `--reveal H`, `--walk S`, `--debug <view>` | frozen build-in height / walk-in / debug view |

**Dev-box self-tests:** `--phase6-selftest` (Beauty parity + resize + recovery),
`--regen-selftest` (6-maze rebuild loop), `--screensaver-preview-selftest`. **CI-safe:**
`--setup-selftest`, `--screensaver-selftest`, and the MSTest suite (200 tests incl.
`RgbToReflectanceBasisTests`, `MazeMinimapTests`, `CameraControllerTests` goal detection).

---

## 0. Guiding constraints (from the existing code)

| Fact | Consequence for this plan |
|---|---|
| The scene is `Tracable[]` → `PackedScene` (`GpuScenePacker.Pack`), every quad an `IQuadPrimitive` with a `QuadPattern` (`Plain`/`Brick`/`CeilingTile`) + 4 params. | New surface types = new `QuadPattern` values + shader branches, packed the same way. |
| Shading = spectral reflectance table `MaterialReflectance[row*50 + heroIdx] × DeterXYZ[idx]` (`PathTrace*.hlsl` `BaseXyz`). No image textures exist anywhere in the pipeline. | Props with pictures (rat, logo, signs) need **image-texture support**, and RGB art must be turned into spectral reflectance to stay physically consistent. This is the one real infrastructure prerequisite. |
| `LightingMode.None` returns raw spectral albedo — `design.md` §Phase 3 already calls this "the spectral equivalent of fullbright rendering," i.e. exactly the original's *unlit* look. | **Classic mode is mostly a preset, not new rendering** — flip lighting to `None`, smoke off, overlays off, tune proportions. Cheap. |
| Overlays already exist in-shader (`ApplyBiomeIndicator`, gated by a `BiomeIndicator` constant, keyed off world pos + normal). The resolve pass has `CamPos`, the G-buffer (`HitPointOut`/`NormalOut`) and composites `FogOut`. | The **overhead map** and the **rat billboard** are screen-space overlays in the resolve pass — no new geometry, no TLAS churn. |
| Options flow `AppSettings` (persisted JSON) → `SetupDialog` → renderer constructor → cbuffer; the screensaver `/c` reuses the same dialog. | All new toggles are added in those four places once, then read by the shader from `cbuffer Constants`. |
| TLAS is static / one instance (pitfall P9); accumulation + TAA assume a **static** scene and converge it. | Any **moving** object (the rat) either needs a per-frame TLAS instance transform *and* motion-aware history handling, or — recommended — is drawn as a **screen-space composite** so the path tracer stays static. |

---

## 1. Feature model & settings

Add one mode selector plus five independent prop switches. The props are **orthogonal
to the mode** (the user asked for them "in all modes"), so they are five separate bools,
not part of the mode enum.

```csharp
// RayTracer.Gpu/SetupDialog.cs — AppSettings
public enum RenderStyle { Classic, Enhanced }      // Enhanced == today's look
public RenderStyle Style { get; set; } = RenderStyle.Enhanced;

public bool ShowRat         { get; set; } = true;  // default ON to match the original
public bool ShowOpenGlLogo  { get; set; } = true;
public bool ShowWallSigns   { get; set; } = true;
public bool ShowOverheadMap { get; set; } = false; // original defaulted OFF
public bool BumpyWalls      { get; set; } = true;

public bool MazeBuildInAnim { get; set; } = true;  // §9 walls rise from the floor
public bool MazeRegenerate  { get; set; } = true;  // §9 finish maze → start a new one
public bool MazeOutroAnim   { get; set; } = true;  // §9 completion transition before regen
```

- **`SetupDialog`**: a "Look" combo (Classic / Enhanced) + a "Classic props" group box with
  five checkboxes. `RenderStyle.Classic` greys out the smoke / fog-drift / firefly rows
  (they don't apply unlit) but leaves the five prop checkboxes active.
- **Persistence & screensaver `/c`**: the fields serialize with the rest of `AppSettings`
  to `%APPDATA%\RayTracer.Gpu\settings.json`; the `/c` dialog is the same `SetupDialog`,
  so it's automatic. Extend `--setup-selftest` / `--screensaver-selftest` to round-trip
  the new fields.
- **cbuffer**: add a `uint FeatureFlags` bitfield (bit 0 rat, 1 logo, 2 signs, 3 map,
  4 bump, 5 classic-unlit, 6 retro-pixelate) to `Constants` in both `PathTracePhase5.hlsl`
  and `ResolvePhase5.hlsl`, reusing existing padding slots. Bitfield keeps the cbuffer
  layout churn to one field.

The frozen Phase 1–4 hosts/shaders are **not** touched (duplicate-don't-refactor
convention); all work lands on the Phase 5/6 productized path the app and screensaver run.

---

## 2. Part 1 — Classic Mode

**Goal:** the flat, evenly-lit, texture-mapped corridors of the 1995 original, produced
by the spectral tracer rather than faked.

### 2.1 What Classic mode *is* (a preset)

When `Style == Classic`, `Phase6Renderer` / `Screensaver.BuildRenderer` overrides:

| Setting | Enhanced (today) | Classic |
|---|---|---|
| `LightingMode` | `NEE` | **`None`** (fullbright spectral albedo = the original's unlit textures) |
| Volumetrics | `Biome`/user | **Off** (`VolEnabled = 0`) |
| Torches / ceiling point lights | placed | not needed (unlit); skip `BuildLights` to save the TLAS/light buffers |
| Biome ceiling indicator | on | off |
| Firefly clamp / motion cap | on | irrelevant unlit — noise-free, so accumulation converges in a few frames |

No new shader code is required for the *core* look — `LightingMode.None` already exists
and is exercised by Phase 1. Classic mode is a wiring + tuning task.

### 2.2 Matching the original's *proportions & motion* (tuning)

- **FOV**: original feels ~90° horizontal; scene uses `MathF.PI/3` (60°). Add a
  `Fov` to the classic preset (start ~ `PI/2`) and A/B against reference screenshots.
- **Walk cadence**: original moved at a steady clip with a brief settle at each 90° turn.
  Tune `CameraController.MoveTime` / `TurnTime` / `StillTime` in a classic profile.
- **Palette**: the recreation already uses red running-bond brick (`00115`), grey ceiling
  tiles (`01085`), dark-red floor (`01138`) — close to the original's brick/acoustic-tile/
  stone set. Pin these as the explicit "classic palette" so an Enhanced-mode palette
  change can't drift the classic look. Compare against captures and nudge if needed.
- **Goal**: replace the plain colored floor patch with the **OpenGL logo** at the exit
  (see §4) — the original's end-of-maze payoff.

### 2.3 Optional authenticity toggles (nice-to-have, default off)

These deliberately *depart* from physical correctness to chase the retro look; keep them
opt-in so Classic-mode-with-them-off is still "spectral, just unlit."

- **Classic depth cue** — the original's corridors fade toward dark with distance.
  Unlit fullbright can look flat/too bright, so optionally apply a display-space
  exponential darkening keyed on the primary-hit distance (already available from the
  G-buffer). It is a tone curve, not a light, so it stays a resolve-pass step and is
  clearly labelled non-physical.
- **Retro pixelation** — render the beauty at a low internal resolution (e.g. 640×480)
  and nearest-neighbour upscale, AA off, for the chunky 90s pixel look. Implemented as a
  resolve/present sampling mode behind `FeatureFlags` bit 6.

### 2.4 Classic-mode tasks

- [x] **1.1** `RenderStyle` enum + `Style` + 8 prop/anim toggles in `AppSettings`;
      `SetupDialog` "Look" combo + "Classic props & animations" group; persistence +
      `--setup-selftest` / `--screensaver-selftest` round-trip extended. **Done & verified.**
- [x] **1.2** Classic preset threaded through `RunApp`/`RunPhase6Windowed`/`Screensaver`
      (+ `--phase6 --classic` test flag): `LightingMode.None`, volumetrics off, wider FOV
      (`ClassicMode.WithClassicFov`, ~90°). **Done & GPU-verified** (see capture). Palette
      pinning + walk cadence tuning remain a follow-up refinement.
- [ ] **1.3** Goal = OpenGL logo (depends on §4).
- [ ] **1.4** (optional) classic depth-cue tone curve in `ResolvePhase5.hlsl`, gated.
- [ ] **1.5** (optional) retro low-res/nearest-neighbour present path, gated.
- [x] **1.6** Dev-box capture: `RunPhase6Capture` (`--phase6 [--classic] --save out.png
      [--walk S] [--frames N]`); Classic vs Enhanced A/B confirmed on the RTX 3070.

---

## 3. Part 2, prerequisite — Image decals in a spectral renderer

The rat, logo, and signs are pictures. The pipeline has no textures, so add a minimal,
spectral-consistent decal path once; §4–§5 reuse it.

### 3.1 RGB → spectral reflectance

To keep NEE/indirect/fullbright working unchanged, a decal texel must yield a *reflectance
per hero wavelength*, exactly like `MaterialReflectance`. Use a fixed **linear-sRGB →
reflectance basis** (Scott Burns' "RGB-to-reflectance", or a least-squares fit against the
CIE observer under the chosen illuminant), sampled at the same 50 `DeterWavelengths`:

```
reflectance[i]  =  dot( Basis[i].rgb , texelLinearRGB )      // i in 0..49
```

- Baked **once** in `RayTracer.Core` (a `RgbToReflectanceBasis` class, 3×50 floats) and
  uploaded as a small structured buffer alongside `DeterXYZ`.
- Slots straight into `BaseXyz`/`IndirectBaseXyz`: for a decal hit, replace the
  `MaterialReflectance[...]` lookup with the basis dot-product on the sampled texel.
- **CPU reference + parity test** (`DecalReference.cs` + `GpuDecalTests.cs`): round-trip a
  handful of sRGB swatches through basis→XYZ and assert ΔE stays small; assert the shader
  path matches the reference bit-for-bit at the hero wavelengths (the project's standard).

### 3.2 Decal geometry & texture atlas

- New `QuadPattern.Decal = 3`. A decal quad carries a **texture index** (reuse `MatPrimary`
  as an atlas layer index, or add `P0` = layer) and its UV comes from the existing quad
  basis reconstruction (`PatternShade`), so no new UV math.
- Textures live in a `Texture2DArray<float4>` (RGBA8, linearized on sample) built from
  embedded PNGs; one array layer per distinct prop image. A tiny `DecalAtlas` builder in
  `RayTracer.Gpu` loads the PNGs → the array + an SRV.
- **Placement helpers** in `MazeGeometryBuilder` emit decal quads: mounted flush on a wall
  (offset `+1e-3` along the wall normal to avoid z-fighting) or as a goal plaque.

### 3.3 Transparency — two options

- **v1 (recommended): opaque framed plaques.** Signs/logo are rectangular plaques that
  fill their quad (the original's signs were rectangular). No alpha, no traversal changes.
- **v2 (refinement): alpha-tested cut-outs.** For irregular shapes, handle it in the
  inline `RayQuery` candidate loop: on a decal-triangle candidate, sample alpha; if below
  threshold, **don't commit** and let traversal continue to the wall behind. Costs a texture
  fetch inside traversal; add only if plaques look too boxy.

### 3.4 Decal-infrastructure tasks

- [x] **2.1** `RgbToReflectanceBasis` in Core — least-squares right-inverse of the pipeline's
      reflectance→colour map, so decal colours round-trip **exactly**. Unit-tested
      (`RgbToReflectanceBasisTests`, 9 cases). GPU upload lands with §2.2.
- [x] **2.2** `QuadPattern.Decal` + `DecalRectangle` (Core) + packer support. **Atlas is a
      structured buffer, not a `Texture2DArray`** — a simplification that avoids a descriptor
      heap/sampler: decal pixels (`PropTextures`, linear RGBA) + the basis (float4 rows) are
      two extra **root SRVs** (t7/t6), bound each frame like the scene buffers. **Done.**
- [x] **2.3** `BaseXyz`/`IndirectBaseXyz` decal branch in the forked `PathTracePhase6.hlsl`
      → basis reflectance (texel·basis). Phase 6 self-test confirms decal-free Beauty is still
      100% parity. **Done & GPU-verified.** (A CPU `DecalReference` bit-parity test is a §10
      follow-up; the basis round-trip is already unit-tested and the shading path visually
      validated.)
- [x] **2.4** `MazeProps` placement (Gpu): goal/start floor logos + double-sided wall signs,
      threaded through `Phase3Scene.Build(props:)` (off for the fixed-seed self-tests). **Done.**
- [x] **2.5** Non-opaque candidate handling in the trace `RayQuery` loop — **now exists**
      (added for the §9.3 build-in height-cull). Decals still use opaque plaques (v1 looks
      right); the mechanism is available if alpha-tested cut-out decals are wanted later.

> **Asset/IP note.** Ship **original recreations** of the props (a stylized "GL" mark, an
> original rat sprite, original sign art) as embedded PNGs rather than the copyrighted
> Microsoft/SGI originals. Flag this before sourcing art.

---

## 4. OpenGL logo

- **On walls**: place logo decals (§3) on a sparse, deterministic set of walls (seeded from
  the maze seed so a run is reproducible), gated by `ShowOpenGlLogo`.
- **At the goal**: a logo plaque (or, higher-fidelity, the rotating billboard of §7 reused)
  at the exit cell, replacing the plain goal patch. In Classic mode this *is* the goal
  payoff (§2.2).
- Tasks:
  - [x] **4.1** Original stylised "GL" logo (`PropTextures.DrawOpenGlLogo`) → atlas layer;
        `ShowOpenGlLogo` wired through settings → `MazeProps.Options.Logo`. **Done.**
  - [x] **4.2** Logo on the goal-cell (and start-cell) floor via `MazeProps`. **Done.**
        (Wall-mounted logos + a rotating billboard remain optional polish.)

---

## 5. Wall signs

- Decal plaques (§3) on select walls — the original's smiley faces, "START", arrows, etc.,
  as **original** sign art. Choose walls deterministically from the maze seed (e.g. one sign
  per N corridors, biased toward junctions so the walker passes them), gated by
  `ShowWallSigns`.
- Tasks:
  - [x] **5.1** Original sign art (`PropTextures`): EXIT, arrow, smiley (+ a rat sprite for §8)
        drawn procedurally into atlas layers. **Done.**
  - [x] **5.2** Deterministic, seeded, **double-sided** sign placement on a ~22% subset of
        walls (`MazeProps.AddWallSigns`); `ShowWallSigns` wired through settings. GPU-verified
        (EXIT/smiley/arrow render upright and correctly-oriented after the UV fix). **Done.**

---

## 6. Bumpy walls

The original's "bumpy walls" embossed the brick. In a path tracer this is **shading-normal
perturbation** from the brick height field — no displacement, so silhouettes are unchanged.

- The brick UV → cell coords already exist (`IsMortar`'s `cellU/cellV`). Define a height
  field `h(u,w)` (≈1 on the brick face, dipping through a smooth ramp into the mortar
  grooves), take its analytic gradient, and tilt the shading normal in tangent space, then
  rotate into world space with the quad basis (`Edge1`, `Edge2`, geometric normal).
- **Lit modes (Enhanced NEE/Direct):** use the perturbed normal in `dot(N,L)` for NEE and
  indirect — this is where bump actually reads, giving the grooves real relief under the
  torches.
- **Classic (unlit) mode:** fullbright ignores normals, so bump is invisible unless faked.
  Optionally fold a fixed-direction **emboss** term into the brick `atten` (brighten faces
  facing a virtual key direction, darken the far mortar shoulder) so the embossed look
  survives unlit — matching how the original showed bump without a real light.
- Gated by `BumpyWalls`. Mirror the normal-perturbation math in the Phase 2/3/4/5 reference
  (`PatternShade` lives in the reference chain) and pin with a parity test; emboss term
  likewise.
- Tasks:
  - [x] **6.1** `BrickBumpNormal` in `PathTracePhase6.hlsl` — analytic tangent-space slope at
        the mortar bevels (nonzero only near edges) tilts the geometric normal. **Done.**
        (CPU reference + parity test deferred to §10, like decals; bump-off keeps Phase 6
        parity at 100%.)
  - [x] **6.2** Perturbed normal drives the **direct-lighting** cosine (`SelectLight`); the
        geometric normal is kept for the G-buffer, shadow/secondary ray offsets, and indirect
        cosine sampling. GPU-verified (mortar bevels catch the light — see bump on/off A/B).
  - [ ] **6.3** (optional) unlit emboss term for Classic mode — deferred (bump reads only in
        lit/Enhanced mode for now).
  - [x] **6.4** `bumpyWalls` through `AppSettings.BumpyWalls` → dialog → `Phase6Renderer`
        (cbuffer `BumpyWalls`, repurposed from a pad) + `--phase6 --bumpy`. **Done.**

---

## 7. Overhead map

The original's toggle showed a small top-down maze map with the player's dot. Implement as a
**screen-space overlay in `ResolvePhase5.hlsl`** — the resolve pass already has `CamPos` and
runs per output pixel, exactly like the existing `ApplyBiomeIndicator` overlay but in screen
space.

- Upload maze wall occupancy as a small buffer (maze ≤ 48×48 → a bit-packed
  `StructuredBuffer<uint>` or a `Texture2D`), plus `CamCellX/Y` + heading derived from
  `CamPos`.
- For pixels inside a corner rectangle (say top-right, ~25% of the shorter screen dim), map
  pixel → maze cell, draw wall vs. corridor, then stamp the camera position + a heading
  arrow. Alpha-blend over the beauty image.
- Gated by `ShowOverheadMap` (default off, like the original). Pure overlay → no geometry,
  no path-trace cost; validated by a dev-box capture + golden image, and the cell-mapping
  math unit-tested in C#.
- Tasks:
  - [x] **7.1** `MazeMinimap.Build` → a `(2W+1)×(2H+1)` wall bitmap (structured buffer, t0);
        player grid cell + dims in the resolve cbuffer (`SetMinimap` uploads per maze). **Done.**
  - [x] **7.2** `ApplyMinimap` corner-rect minimap + player marker in the **forked**
        `ResolvePhase6.hlsl` (Phase 5 resolve untouched). GPU-verified (see the map capture).
        **Done.** (Heading arrow → a plain player dot for now.)
  - [x] **7.3** `showOverheadMap` through `AppSettings.ShowOverheadMap` → dialog →
        `Phase6Renderer` + `--phase6 --map`; wired into the walk + regeneration + screensaver.
        Phase 6 self-test stays 100% with the map off. **Done.** (C# `MazeMinimap` unit test +
        golden → §10.)

---

## 8. Rat (animated)

The rat moves, and the whole pipeline assumes a **static** scene (§0). Two approaches:

- **Recommended — screen-space animated billboard, composited in the resolve pass.**
  Keep the path tracer static; the rat never enters the TLAS. Each frame: run a second
  `MazeNavigator` (own start/seed, its own speed) on the CPU to advance the rat's world
  position; pass its world pos + size to the resolve shader; project to screen; sample the
  rat sprite (alpha-tested) scaled by depth; **occlude** it by comparing the sprite's view
  depth against the G-buffer depth from `HitPointOut` at that pixel (so walls hide it).
  No TLAS churn, no temporal ghosting, cheap. Trade-off: the rat casts no light/shadow and
  isn't reflected — negligible for a decorative sprite and true enough to the original.
- **Higher-fidelity alternative — real geometry instance.** Give the rat its own BLAS and a
  per-frame TLAS instance transform (a refit, cheap for one small mesh). Requires marking the
  rat's screen region as "moving" so TAA doesn't smear it (reuse the soft-reset/motion path
  locally, or reduce history weight where the rat is). More correct (it can be lit and cast a
  faint shadow) but materially more complex. Defer unless the billboard looks too flat.

- Gated by `ShowRat`.
- Tasks:
  - [x] **8.1** Alpha-masked rat sprite (`PropTextures.DrawRat`, transparent background) →
        atlas layer; `Phase3Scene.CreateRatController` reuses `CameraController` at floor
        height with scurry timing to advance the rat each frame. **Done.**
  - [x] **8.2** `ApplyRat` in `ResolvePhase6.hlsl`: projects the rat to screen (mirrors the
        reprojection math), alpha-tests the sprite, depth-occludes against the G-buffer
        (`HitPointIn`/`LastHit`), and composites. `showRat` + `SetRatPosition` uniforms.
        **Done & GPU-verified** (see the rat capture); Phase 6 parity holds with it off.
  - [x] **8.3** Scurry timing (Move 0.45 / Turn 0.35 / Still 0.25) + 0.25-unit floor height +
        0.7-unit billboard; wired through walk + regeneration + screensaver. **Done.**
        (Golden capture → §10.)
  - [ ] **8.4** (optional, later) real-geometry rat instance with motion-aware history —
        deferred; the billboard reads well.

---

## 9. Maze lifecycle — build-in intro, completion & regeneration

Two behaviours remembered from the original, added as a small state machine around the walk
loop (`Screensaver.RenderWalk` + the windowed Phase 6 loop). **Provenance, stated plainly:**
maze-regenerates-on-completion is very likely authentic and, regardless, is what a
screensaver running for hours needs (today it wall-follows one maze forever); the wall-rise
intro I *cannot confirm* from memory for the Windows *3D Maze* specifically — it may be
another version or another maze saver — but it's a cheap, attractive transition worth having.
Both are behind toggles (default on) and apply in Classic and Enhanced.

### 9.1 Lifecycle state machine

Replace "walk one maze forever" with:

```
BuildIn (walls rise from floor)  →  Walk (autonomous right-hand rule)
   →  reach goal cell  →  Outro (celebrate/fade)  →  regenerate new maze  →  BuildIn …
```

- **Goal detection**: the walker reaches `(maze.Width-1, maze.Height-1)` — the existing goal
  cell / OpenGL-logo location. `MazeNavigator` already exposes `CellX/CellY`.
- **Regeneration** (`MazeRegenerate`): build a fresh `Phase3Scene` with a new `Random.Shared`
  seed and rebuild the scene-dependent GPU resources (vertex/index/BLAS/TLAS, `GpuPrimitive`
  buffer, spectral + light tables). This reuses the teardown/rebuild machinery
  `Phase6Renderer` already has for device recovery (pitfall P9 — "full rebuild on scene
  change, milliseconds for this size"). Accumulation hard-resets like a resize; the camera
  returns to the start cell. Loops forever → genuine variety over a long session, matching the
  original. With the toggle off, the walker just keeps wall-following the one maze (today's
  behaviour).

### 9.2 Wall build-in animation (`MazeBuildInAnim`)

Geometry is static and inline `RayQuery` traces the real triangles, so animate the *reveal*,
not the vertices:

- **Recommended — height-cull reveal (no geometry churn).** A `RevealHeight` uniform eases
  `0 → WallHeight` over ~1–2 s. In the trace `RayQuery` candidate loop, **reject wall hits
  with `hitPoint.y > RevealHeight`** (don't commit; continue traversal), so each wall is only
  visible up to the rising line → walls grow from the floor. This reuses the same
  candidate-rejection mechanism as alpha-tested decals (§3.3 v2 — build it here if decals
  stayed opaque); the BLAS stays static and the render deterministic, and it costs nothing
  once `RevealHeight` is full. Floor stays; ceiling fades in on a tone term or pops at the end.
- **Alternative — per-frame geometry animation.** Scale each wall's Y `0 → full` and
  refit/rebuild the BLAS each intro frame (a heavier vertex-reupload + scratch/barrier dance;
  feasible for a ~1–2 s one-off). Needed only if walls should literally *slide up into place*
  rather than *grow*.
- The scene effectively changes each intro frame, so treat build-in like camera motion
  (accumulation soft-reset / low history) so TAA doesn't smear the rising edge.

### 9.3 Completion / outro (`MazeOutroAnim`)

A short transition on reaching the goal, before regenerating:

- Ease the camera to face the goal (the OpenGL logo, §4), brief hold, then fade to black — or
  play build-in in reverse (walls sink) as the "old maze dissolves" beat.
- Purely time-driven in the walk loop; no new render features — the fade reuses the resolve
  tone term introduced for the classic depth cue (§2.3). Toggle off → regeneration is an
  instant cut.

### 9.4 Lifecycle tasks

- [x] **9.1** Goal detection + regeneration wired into the windowed loop (`RunPhase6Windowed`,
      `--phase6 --regen`) and `Screensaver.RenderWalk`, gated by `MazeRegenerate`. Fires once
      the walker settles on the goal cell (`CameraController.CurrentCell`, new). **Done.**
- [x] **9.2** `Phase6Renderer.RebuildScene(scene, spectral, lights, camera)` — a lightweight
      **scene-only** rebuild (vertex/index/primitive + spectral/light tables + BLAS/TLAS), no
      shader recompile / device recreation (scene buffers are root-descriptor bound, so the
      next trace picks up the new VAs). Accumulation + temporal history hard-reset. **Done &
      GPU-verified** — `--regen-selftest` cycles 6 mazes TDR-free/leak-free.
- [x] **9.3** `RevealHeight` uniform + wall height-cull in the trace `RayQuery`: below the
      wall top it forces non-opaque traversal and rejects hits with `y > RevealHeight` (floor
      always passes, ceiling last) — this also *is* the §2.5 candidate-loop handling. Host
      animates 0→wall-height over 1.5 s at each maze start, holding the walker and resetting.
      `MazeBuildInAnim` / `--buildin`. **Done & GPU-verified** (frozen mid-reveal capture;
      opaque fast path preserves Phase 6 parity when built).
- [x] **9.4** Outro `FadeLevel` (resolve tone term): on reaching the goal, fade to black over
      0.6 s, then regenerate — seamless since the build-in rises from black. `MazeOutroAnim` /
      `--outro`; wired in the windowed loop + screensaver (factored `Regenerate`). **Done**
      (parity holds at fade 0).
- [x] **9.5** Dev-box `--regen-selftest`: renders + `RebuildScene` over 6 mazes, asserting
      every maze is non-degenerate; plus a CPU `Walk_ReachesGoalCell` test (6×6/8×8/16×16)
      proving the walker reaches the goal so regeneration fires. **Done.**

---

## 10. Testing & validation (per project convention)

| Feature | CI unit test | Dev-box selftest | Golden image |
|---|---|---|---|
| Classic preset | reuses Phase 1 `None` parity | ✅ `--phase6-selftest` 100% w/ features off | ⬜ classic golden |
| RGB→reflectance basis | ✅ `RgbToReflectanceBasisTests` (round-trip) | ✅ visual capture | — |
| Decal shading (logo/signs) | basis pinned ✅; `DecalReference` parity ⬜ | ✅ visual capture | ⬜ |
| Bumpy walls | perturbed-normal reference ⬜ | ✅ bump on/off A/B | ⬜ |
| Overhead map | ✅ `MazeMinimapTests` (grid math) | ✅ visual capture | ⬜ |
| Rat billboard | projection/occlusion parity ⬜ | ✅ visual capture | ⬜ |
| Maze lifecycle | ✅ `CameraControllerTests` goal detection | ✅ `--regen-selftest` (6 mazes) | ⬜ |
| Settings/toggles | ✅ `--setup-selftest`/`--screensaver-selftest` round-trip | — | — |

**Done:** the four CI unit tests above (200 total), all dev-box self-tests (`--phase6-selftest`
stays 100% with every feature off), and the visual captures for every feature. **Remaining
(dev-box follow-up):** golden-image regression captures — extend `--phase6-regress` to cover
Classic, each prop, and a fixed-`--reveal` build-in frame (reproducible via `--seed`); and the
optional bit-parity `DecalReference` / bump-normal / rat-projection references (the basis and
minimap math are already unit-tested, and the shaders are visually validated + parity-safe
when off). All GPU self-tests remain dev-box-only (RTX 3070, DXR 1.1) — see
[`memory/gpu-devbox-local.md`].

---

## 11. Suggested order (each step visually verifiable, each toggle independent)

1. **§1 settings scaffolding** — `RenderStyle` + all toggles through
   `AppSettings`/`SetupDialog`/screensaver/cbuffer (no visual change yet).
2. **§2 Classic mode** — biggest visual payoff, no new assets; unlit spectral preset + tuning.
3. **§9.1–9.2 maze regeneration** — the scene-rebuild loop (reuses device-recovery teardown);
   high value for a long-running screensaver and unblocks the goal/outro beats. Build-in
   reveal (§9.3) can follow once §3.3 v2 candidate handling exists.
4. **§3 decal infrastructure** — RGB→reflectance basis + `Decal` pattern + atlas.
5. **§5 wall signs** → **§4 OpenGL logo** — first decal payoffs; logo also finishes the
   Classic goal and the §9.3 outro.
6. **§6 bumpy walls** — normal perturbation (+ optional unlit emboss).
7. **§7 overhead map** — resolve-pass overlay.
8. **§8 rat** + **§9.3 build-in/outro polish** — resolve-pass animated billboard and the
   transition beats (hardest; do last).
9. **§10** — goldens, selftest extensions, docs.

---

## 12. Open decisions

- **Classic default?** Recommend shipping **Enhanced** as default (it's the project's
  showcase) with Classic one combo-box away. Confirm before wiring the default.
- **Prop art:** original recreations vs. sourced assets — recommend original art to avoid
  Microsoft/SGI IP (§3.3 note).
- **Rat fidelity:** ship the billboard (§8 recommended) first; only build the geometry-instance
  rat if the flat sprite disappoints.
- **Alpha-tested decals:** start with opaque plaques (§3.3 v1); add cut-outs only if needed.
- **Build-in / regeneration provenance:** both are from memory (§9) and may not match every
  version of the original — regeneration is desirable regardless. **Decided:** both default
  **on** (`MazeBuildInAnim`, `MazeRegenerate`, `MazeOutroAnim` = `true`), toggleable in the
  setup dialog.
