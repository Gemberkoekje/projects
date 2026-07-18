# RayTracer — Remaining Work

The single source of truth for outstanding work. On 2026-07-16 the seven previous
planning docs — `design.md`, `gpu-raytracing-plan.md`, `classic-mode-and-props-plan.md`,
`propervolumetricfogplan.md`, `shadows-and-caustics-plan.md`, `spectral-effects-plan.md`,
and `outdoor-area-plan.md` — were folded into this file and removed, to converge the
roadmap and stop the per-file status headers from drifting out of sync.

**Everything not listed under _Remaining work_ below is built, validated on the local
RTX 3070 (DXR 1.1), and pinned by unit tests / CPU↔GPU parity tests / golden images.**
The completed work lives in the code and git history; this file only tracks what's left.

---

## What's shipped (orientation)

- **Classic maze recreation** — maze gen, geometry, BVH, first-person autonomous walk,
  slideshow camera, screensaver `.scr` switches (`/s`, `/c`, `/p`).
- **Classic render mode + five signature props** — unlit spectral "Classic" look (depth
  cue, retro pixelation), rat billboard, OpenGL logo, wall signs, overhead minimap, bumpy
  walls, plus maze lifecycle (build-in, regenerate, outro).
- **Full GPU hardware-DXR port** (`RayTracer.Gpu`) — Phases 0–6: spectral shading, NEE +
  indirect GI, temporal (TAA + spatial filter), volumetrics, debug views + stats readback,
  and productization (resize, device-removed recovery, screensaver, golden-image harness).
  CPU→C#-reference→HLSL parity is the standing discipline (CI pins reference↔CPU; the box
  runs `--phaseN-selftest` / `--phase6-regress`).
- **Volumetric fog** — segment ray-marching (extinction + in-scatter), per-biome density,
  3D-noise turbulence, moving fog, fog self-shadow / god-rays.
- **Shadows & caustics** — transmittance-aware shadows (fog / coloured glass / bubble),
  GPU spectral photon-map caustics (glass focus, prism rainbow, bubble ring), area lights +
  soft shadows, and a directional **sun**.
- **Spectral / optical effects** — mirrors, glass/dielectric, dispersion (prism/gem fire),
  thin-film iridescence (oil slick), Beer–Lambert absorption (stained glass), soap bubbles.
- **Outdoor garden half** (O0–O10) — indoor/outdoor region split, open roof, procedural
  Rayleigh sky + sun disk, hedges, grass, flat water pools, sky GI bleed, blackbody torch
  (1800 K) + sun (5500 K) spectra, day/night cycle, framed stone doorways, in-render clock.
  Shipped in the real walking maze behind a ConfigForm toggle (default on).

---

## Remaining work

### 1. Screensaver ↔ windowed-app unification  *(the one active must-do)*

The `.scr` screensaver (`RayTracer.Gpu/Screensaver.cs`, `RenderWalk`) still runs a
**separate, stale render/update loop** that predates the outdoor arc. As a result `/s`,
`/p`, and `/c` do **not** inherit the outdoor garden half, day/night cycle, blackbody
lights, frozen-bubble convergence, water pools, or the clock — all of which now live only
in the windowed path (`Program.RunPhase6Windowed`).

- **Do:** factor the windowed render+update loop (maze lifecycle, sky/sun/day-night
  updates, outdoor build, bubble snapshot, clock) into a shared driver that both the
  windowed app and the screensaver call, so the screensaver is just a thin host wrapper.
- **Gate/parity:** the CPU-only `--screensaver-selftest` (arg parsing + settings
  round-trip) stays green; on the box, `/s --frames N` renders the full outdoor + day/night
  scene.
- **Verify:** run the screensaver and confirm it shows the same garden/day-night as the
  windowed app.

### 2. Water pools — High tier (ripples + caustics)  *(O6-High)*

Flat water is done (`RayTracer.Gpu/MazeWater.cs`: a tinted dielectric surface reflecting
the sky over a light basin). The High tier is deferred:

- **Ripples:** animate the water surface normal with procedural waves (a small sum of
  Gerstner/sine waves) driven by the existing frame/animation clock → moving highlights.
- **Caustics:** aim the forward photon pass (`Phase6Renderer.BuildCausticsGpu`) at the
  water quad so refracted sunlight throws dancing caustics onto the basin.
- **Parity:** CPU → `*Reference.cs` → `PathTracePhase6.hlsl`; static/no-water scenes stay
  byte-identical (goldens unmoved).
- **Verify:** a pool shows animated highlights and moving floor caustics; drop-in with no
  water is unchanged.

### 3. Unbuilt spectral flagship effects  *(the only never-built features)*

These are the last effects that exist **only as unused `SurfaceKind` enum values** with no
shading. Each is a self-contained showpiece; each relies on the already-built hero-only
wavelength-dependent path fallback.

- **3.1 Fluorescence / UV reactivity** — a material absorbs a short wavelength and re-emits
  a longer one (highlighter ink, tonic water, fluorescent minerals under a "blacklight").
  Implement `SurfaceKind.Fluorescent` with a reradiation matrix (absorbed λ → emitted λ
  distribution) and add a UV (~400 nm) emitter. Files: material data + `MaterialsLookup`,
  `PathTracer.cs`, references, `PathTracePhase6.hlsl`. *Verify:* under a ~400 nm source a
  fluorescent patch emits visible green/orange brighter than its reflectance alone allows.
- **3.2 Diffraction grating (CD/DVD rainbow)** — `SurfaceKind.Grating`. A specialized BRDF
  from the grating equation `d·sinθ = m·λ` (per-wavelength, hero-only path). Scene hook: a
  CD on the floor under a torch beam. *Verify:* a white beam produces separated spectral
  orders at the grating-equation angles.
- **3.3 Metamerism demo** — two wall swatches whose spectra match under one illuminant and
  diverge under another. Mostly material data + a light switch; leans on the shipped
  blackbody spectra (§O9) and day/night switch (§O8). *Verify:* the pair matches under D65
  (sky) and visibly separates under the 1800 K torch (illuminant A).

*(§3.4 physically-correct chromatic aberration is effectively covered by the dispersion
work; the one place a dedicated thick-lens CA demo would earn its keep is the camera-lens
work in §4, where it falls out of the lens elements for free.)*

### 4. Camera lens models  *(§4.1–4.4 built; the extras below are open)*

**Shipped.** The camera is no longer a bare pinhole. `RayTracer.Core/Rendering/LensOptions.cs`
(a record struct alongside `RealismOptions`) carries the sensor + lens; the pure camera-space
math lives in `RayTracer.Core/Pipeline/LensSampler.cs`, which the CPU (`PathTracer.TraceCore`),
the GPU reference (`Phase1Reference.PrimaryRayThroughLens`) and the HLSL (`GenerateLensRay` in
`PathTracePhase6.hlsl`) all share or port from. Selectable in the ConfigForm ("Camera lens" +
portrait / zoom / focus), via `Phase6Renderer.SetLens`, and headless with
`--lens <pinhole|phone|portrait|simple|cine> [--zoom mm] [--focus m] [--fstop N]`.

- **4.1 Physical camera core** — focal length + sensor height + f-stop + focus distance, with
  thin-lens ray gen (sample the aperture, aim at the focus-plane point). `FocalLengthMm = 0`
  derives the focal length from the scene's `Camera.Fov`, so **switching a lens on does not
  reframe**; setting it is the zoom and reframes (that is what a zoom is). Units are real: the
  world is metres, so f/N gives photographic depth of field. Defocus is stochastic, so
  accumulation/TAA averages it for free (convention 4).
- **4.2 Phone** — 1/1.7" sensor, f/1.8, mild barrel, vignette. Physically it has near-infinite
  depth of field at maze distances, so its blurred-background look is a **synthetic** aperture
  (`LensOptions.PhonePortrait`, the ConfigForm's "Portrait mode").
- **4.3 Simple / old camera** — f/8, heavy barrel (k1 −0.12, k2 −0.03) and a deep vignette.
  The most visually distinct from the pinhole.
- **4.4 Cine** — Super35, f/1.4, 7-blade polygonal iris, cat's-eye edge vignetting, plus zoom
  and rack focus.
- **Pinhole is the default and is bit-exact**: it draws **no random numbers** (the lens has its
  own RNG stream via `LensSampler.SeedFor`, so it cannot perturb the wavelength/specular
  streams). Pinned by `LensTests` + `GpuLensTests` and verified on the box —
  `--phase6-selftest` 700/700 and `--phase6-regress` 19/19 bit-exact.
- *cbuffer note:* the lens fields fit — `TraceConstants` was at 288 of its 512 bytes, now 320.

**Verified on the RTX 3070:** at a fixed seed the old camera visibly bows the ceiling/floor
lines and darkens the corners; portrait mode softens the far wall; an 85 mm cine zoom reframes
to a tight view that is blurred at `--focus 1.2` and snaps sharp at `--focus 2.6` (i.e. the
focus plane is real and at the right distance).

**Deferred / not built:**

- **Depth of field is subtle at the scene's ~60° field of view.** That is honest physics — the
  implied focal length is ~16–21 mm, so even f/1.4 is a ~6 mm aperture. The cine lens only
  reads as "cinematic" once zoomed. If more defocus is wanted at wide angles, the lever is
  `ApertureScale` (synthetic, like the phone's portrait mode), not a physical f-stop.
- **§4.3's lateral chromatic aberration and corner softness (field curvature)** are not
  implemented — the old camera currently distorts and vignettes only.
- **§4.4's focus breathing and the anamorphic mode** are not implemented.
- **Phone extras:** dirty/smudged front element, rolling shutter.
- **Other phases:** the lens lives only in `PathTracePhase6.hlsl` (the shipping path). Phases
  2–5 stay pinhole-only, which keeps their self-tests and goldens untouched.

**Related ideas worth folding in (ordered by payoff, none required):**

- **Spectral sensor response** — the pipeline is spectral-native (convention 3), so a
  camera's *spectral sensitivity curve* can replace the CIE observer per model (phone CMOS
  vs. film stock vs. cine sensor). This is the physically-honest way to give each lens a
  colour character instead of a hard-coded tint, and it's mostly a lookup table swap next to
  `WavelengthLookup.cs`.
- **Real white balance** — for the same reason, white balance becomes a genuine chromatic
  adaptation (von Kries) against the scene illuminant, which pairs directly with the shipped
  blackbody torch (1800 K) / sun (5500 K) spectra and the day/night switch. It also gives
  the §3.3 metamerism demo a second axis.
- **Chromatic aberration for free** — model the lens elements with the existing dispersion
  IOR and each hero wavelength focuses at a slightly different distance. This is the honest
  version of the §3.4 note, and it's the *reason* the old-camera preset would get CA without
  a fake — the cheapest route to the §4.3 gap above.
- **Exposure triangle** — couple aperture ↔ shutter ↔ ISO so stopping down actually darkens
  (with a luminance-preserving option, ties into the §O9 normalisation item). Shutter time
  then gives real motion blur on the walking camera, and ISO gives sensor grain.
- **Autofocus** — "tap/centre to focus" by probing the existing G-buffer depth at the
  crosshair, plus a continuous-AF mode that racks focus as the autonomous walk moves. The
  slideshow camera could rack focus during its long stills.
- **Lens flare / veiling glare** — ghosts from inter-element reflections and bloom around
  the torches and the sun disk. Anamorphic streaks fall out of §4.4.

### 5. Optional polish  *(nice-to-have, none blocking)*

- **Classic / props:** real-geometry (non-billboard) rat with motion-aware history (§8.4;
  the billboard reads well). Optional CPU bit-parity references — `DecalReference`,
  bump-normal, rat-projection — the underlying math is already unit-tested and the goldens
  pin the shader output, so these are belt-and-suspenders.
- **Outdoor deferred detail:** distance-based aerial haze on primary rays for geometry
  (O3); stone kerb at hedge base + hedge translucency (O4); grass detail/bump normal (O5);
  importance-sampled sky-dome NEE for faster ambient fill (O7); stronger orange sunset glow,
  optional stars/moon, and a ConfigForm day/night toggle (O8); luminance-preserving light
  normalisation + dusk-reddening of the sun temperature (O9); arch chamfer, coping lip, ivy
  / torch bracket on the doorways (O10).
- **Torch lighting extras:** animated intensity flicker (design 7.6); glossy-floor
  Blinn-Phong highlights (design 7.7).
- **Cleanup:** finish the `JobSystem` B5 alias cleanup (partially done).

---

## Working conventions (carry-over — apply to every remaining item)

1. **CPU first, then GPU, in lockstep.** Land it in `RayTracer.Core` with unit tests, then
   port to the pure-C# `RayTracer.Core/Gpu/*Reference.cs` replica, then the HLSL
   (`RayTracer.Gpu/Shaders/PathTracePhase6.hlsl`). CI has no GPU, so the C# reference *is*
   the contract the HLSL is a line-for-line port of; pin reference↔CPU with a
   `GpuPhase*Tests` parity test and run `--phase6-selftest` on the RTX 3070.
2. **Keep the no-effect path byte-identical.** With a feature off / no casters, goldens must
   not move — gate new work behind a flag/preset and preserve the opaque/accept-first-hit
   fast paths. `--phase6-selftest` (700/700) and `--phase6-regress` (19 views bit-exact)
   are the guardrails.
3. **Spectral-native.** Colour comes from physics (dispersion, film, Beer–Lambert,
   blackbody, Rayleigh), never a hard-coded tint. Every ray/photon carries a wavelength.
4. **Gate on cost, expose via presets.** Lean on `AccumulationBuffer`/TAA (and the
   slideshow camera's long stills) to average stochastic/spectral noise rather than
   brute-forcing samples.

> **Dev-box note:** all GPU self-tests and the golden regression run only on the local
> RTX 3070 (DXR 1.1) — see `memory/gpu-devbox-local.md`. CI runs the CPU + parity tests.
