# GPU Ray Tracing Port — Feasibility & Plan

A plan for a new project that reproduces the current spectral maze renderer, but runs
the ray tracing on the GPU using **actual hardware ray tracing** (RT cores via DirectX
Raytracing). This document covers feasibility, technology choice, what the port
entails, a phased implementation plan, and the pitfalls to keep in mind.

---

## 1. Verdict: is it possible?

**Yes.** Everything this renderer does maps onto GPU ray tracing, and the development
machine (NVIDIA GeForce RTX 3070, Windows 11) supports the full DXR 1.1 feature set
including inline ray tracing. The scene is ideally shaped for it:

- The scene is entirely **quads** (`TracableRectangle`, `BrickRectangle`,
  `CeilingTileRectangle`, `Plane`) — trivially converted to triangle pairs, which is
  exactly what the hardware BVH (BLAS/TLAS) consumes.
- Geometry intersection is **wavelength-independent** — only the *reflectance* returned
  at the hit depends on wavelength. So hardware traversal (which knows nothing about
  spectra) loses nothing; spectral evaluation happens after the hit, in shader code.
- The per-pixel state machine (accumulation, Welford variance, TAA, clamping) is
  embarrassingly parallel screen-space work — natural compute-shader territory.
- The RNG is already deterministic integer hashing (PCG-style), which ports to HLSL
  verbatim.

Expected outcome: the maze scene is a few thousand primitives; an RTX 3070 traces
hundreds of millions to billions of rays per second on scenes like this. Where the CPU
renderer fights for a handful of samples per pixel per frame at reduced resolution,
the GPU version should sustain **many samples per pixel at 1080p at 60+ FPS**. Much of
the machinery built to hide CPU slowness (checkerboard-while-moving, motion sample
caps, performance calibrator, CPU throttle) likely becomes unnecessary.

---

## 2. Technology choice

The requirement "actual graphics card raytracing functionality" means using the RT
cores through a ray tracing API — not just a compute shader with a hand-rolled BVH.
Realistic options:

| Option | Language | HW RT cores | Assessment |
|---|---|---|---|
| **D3D12 + DXR via a C# binding (Vortice.Windows or Silk.NET)** | C# host + HLSL shaders | ✅ | **Recommended.** Stays in .NET, mature bindings, Windows-native, integrates with the existing WinForms shell via an HWND swap chain. |
| Vulkan + `VK_KHR_ray_tracing_pipeline` via Silk.NET | C# host + GLSL/HLSL | ✅ | Cross-platform, but substantially more boilerplate and the project is Windows-only (WinForms, screensaver). Not worth it here. |
| ComputeSharp (C#-authored compute shaders) | Pure C# | ❌ | Very pleasant developer experience, but **no DXR access** — you'd be writing your own BVH traversal in compute. Fails the "actual hardware RT" requirement. |
| NVIDIA OptiX | C++ | ✅ | Best-in-class for offline rendering, but effectively a C++ ecosystem; poor fit for a .NET codebase. |
| Unity / Unreal HDRP ray tracing | Engine | ✅ | Loses the from-scratch engine control that is the point of this project. |

### Recommended stack

- **Host:** C# / .NET 10, new project `RayTracer.Gpu` alongside the existing ones.
- **Binding:** `Vortice.Windows` (D3D12, DXGI, DXC wrappers) — actively maintained,
  has DXR samples. Silk.NET is a fine alternative if preferred.
- **Ray tracing mode: DXR 1.1 *inline* ray tracing (`RayQuery`) inside compute
  shaders**, not the full DXR state-object pipeline. Rationale:
  - No shader binding table, no state objects, no raygen/miss/hit shader plumbing —
    the single biggest source of DXR beginner pain disappears.
  - `TraceCore` is already a "megakernel": one function that traces primary ray,
    shadow rays, and bounce rays sequentially. That maps 1:1 onto one compute shader
    with several `RayQuery` calls in a loop.
  - The full pipeline (raygen/closesthit/anyhit + SBT) can be adopted later if there's
    ever a reason (e.g. many divergent material shaders); for a handful of quad
    materials, inline RT is the right call.
- **Shaders:** HLSL, compiled with DXC to Shader Model 6.5 (RayQuery requirement),
  compiled at build time or on first run.
- **Presentation:** DXGI swap chain bound to the existing WinForms window handle. The
  `CalibrationForm` UI, camera controller, and maze logic stay as-is in C#.

---

## 3. What the port entails

### 3.1 What is *replaced by hardware* (deleted, not ported)

| Current component | Replacement |
|---|---|
| `BVH.cs`, `AABB.cs` (build + traversal) | Driver-built BLAS/TLAS; `RayQuery::Proceed()` traversal on RT cores |
| `JobSystem` tile scheduling, `TileScheduler`, worker tasks, `Channel<Tile>` | One compute dispatch per frame (one thread per pixel) |
| `CpuThrottle`, `PerformanceCalibrator` | Not needed; GPU frame pacing via swap chain |
| Checkerboard-while-moving, motion sample caps | Probably not needed; re-evaluate after measuring |

### 3.2 What is *ported to HLSL* (the bulk of the work)

| Current component | GPU form |
|---|---|
| `PathTracer.TraceCore` (~600 lines) | Main path-trace compute shader: jitter, hero wavelength selection, primary `RayQuery`, NEE light selection + shadow `RayQuery`, one/two-bounce indirect, per-pixel accumulation and variance updates |
| `BrickRectangle` / `CeilingTileRectangle` procedural patterns | Pure-math HLSL functions keyed by a per-primitive material/pattern ID + UV (the running-bond and tile logic is branch-light math — ports directly) |
| Volumetric integration (`VolumetricIntegration`, smoke/fog density functions in `PathTracer.cs`) | HLSL functions — they are already pure `sin/exp/floor` math with no state |
| `TaaResolver` (reprojection, bilinear history, disocclusion rejection) | Full-screen compute pass with ping-pong history textures; previous camera matrix in a constant buffer |
| Bilateral spatial filter | Full-screen compute pass |
| `ColorResolve` / `DisplayResolver` (XYZ→sRGB, gamma) | Final compute pass writing the swap-chain image (or an intermediate UAV texture) |
| `DebugBufferRenderer` views | Same final pass, switched by a debug-mode constant |
| Hash/RNG functions (`Hash2D`, `HashCell`, LCG chains) | Verbatim HLSL translation — they're integer math |

### 3.3 What is *restructured* (the design-thinking work)

**Scene representation.** The C# `Tracable` interface with virtual `Intersect` cannot
exist on the GPU. Flatten to data:

- Vertex/index buffers: each quad → 2 triangles (BLAS input).
- A `StructuredBuffer<PrimitiveInfo>` indexed by primitive/instance ID: quad basis
  vectors (for UV reconstruction), material ID, pattern type (plain / brick /
  ceiling-tile), pattern parameters (bricks across/down, mortar fractions).
- The shader recomputes UV from the hit position and the quad basis — same math as
  `TracableRectangle.Intersect` does today — then evaluates the pattern function to
  pick brick vs. mortar material.

**Spectral data as textures.**

- CIE 1931 XYZ table (`CIE_xyz_1931_2deg.csv`) → a small `Texture1D<float4>` or
  structured buffer indexed by wavelength.
- Per-material spectral reflectance curves (`*_spectral.csv`) → one row each in a
  `Texture2D<float>` (materials × wavelengths). Hardware linear sampling gives the
  interpolation `MaterialData.GetSpectralReflectance` does today, for free.
- The hero-wavelength cycle (`_deter` array, 50 entries) and
  `DeterministicCorrection` → small constant/structured buffer. **All of this baking
  stays in C#** — the existing CSV parsing code is reused at startup to build the
  GPU resources.

**Companion wavelengths get cheaper.** The CPU code re-runs `Intersect` on the hit
primitive for each of the 3 companion wavelengths, only to obtain a different
reflectance. On the GPU there is no reason to re-intersect: keep the hit, sample the
material's spectral texture at 4 wavelengths. Same math, strictly cheaper.

**Per-pixel buffers.** `RenderBuffers` holds ~25 per-pixel arrays (accumulation,
Welford M2/variance ×3, clamp heatmap, depth, albedo, normals, per-bounce XYZ splits,
TAA history…). Each becomes a UAV texture or structured buffer that *lives on the
GPU and stays there*. Only the statistics the UI shows (average variance, SPP, clamp
counts) are reduced on-GPU and read back — a few bytes per frame, never full buffers
(see pitfall P6).

**Diffuse irradiance cache.** `DiffuseIrradianceCache` is a concurrent hash-grid
that amortizes the 2nd+ bounce. On GPU this is awkward (hash probing + atomics +
frame-lifetime management) and probably unnecessary — the GPU can afford to just
trace the tertiary ray every sample. **Recommendation: drop it initially**; if 2+
bounce cost ever matters, a fixed voxel-grid cache with atomics is the GPU-friendly
re-design.

### 3.4 What stays in C# untouched

- Maze generation, `MazeGeometryBuilder`, `MazeNavigator`, `CameraController`,
  `Camera`, presets, options, the WinForms app shell and `CalibrationForm`.
- Scene building still produces the same logical scene; a new packer converts it to
  GPU buffers instead of a `Tracable[]`.
- Ideally the new project references `RayTracer.Core` for all of this shared logic
  rather than copying it.

---

## 4. Phased implementation plan

Each phase produces something visually verifiable, mirroring the incremental style of
`design.md`. Keep the CPU renderer around throughout — it is the reference
implementation for A/B comparison.

### Phase 0 — Spike: prove the stack — ✅ DONE

Implemented in the `RayTracer.Gpu` project (added to `RayTracer.slnx`). Uses
Vortice.Windows 3.8.3 (D3D12 + DXGI) and Vortice.Dxc for runtime SM 6.5 compilation.
Verified on the RTX 3070: the headless self-test (`--selftest`) confirms the inline
`RayQuery` actually hits the triangles (center pixel hit, corner miss, ~14% coverage),
and the windowed path (`--frames N`) dispatches, copies, and presents without error.

- [x] **0.1** New `RayTracer.Gpu` project; bring up D3D12 device, command queue,
      swap chain on a WinForms window via Vortice. — `GpuRayTracer.Initialize`,
      `Program.RunWindowed`.
- [x] **0.2** Verify `D3D12_RAYTRACING_TIER_1_1` support at startup; fail with a
      clear message otherwise (drives the fallback story, see P1). —
      `GpuRayTracer.VerifyRaytracingSupport` (checks `Options5.RaytracingTier`).
- [x] **0.3** Compile an SM 6.5 compute shader with DXC; dispatch it; write to the
      swap chain. — `ShaderCompiler` (runtime DXC), `Shaders/RayQuery.hlsl`; the miss
      path draws the gradient, offscreen UAV is copied to the back buffer.
- [x] **0.4** Build a BLAS/TLAS for two hardcoded triangles; use `RayQuery` in the
      compute shader to color pixels by hit/miss. *De-risks the entire project.* —
      `GpuRayTracer.BuildAccelerationStructures`; validated by the headless self-test.

**How to run:** `dotnet run --project RayTracer.Gpu -c Release -- --selftest` (headless
pass/fail), or `dotnet run --project RayTracer.Gpu -c Release` (opens the window).

### Phase 1 — Scene on GPU, fullbright spectral parity — ✅ IMPLEMENTED (pending dev-box validation)

Phase 1 lives in `RayTracer.Core/Gpu/` (pure, unit-tested data) and
`RayTracer.Gpu/` (D3D12 host + `Shaders/PathTrace.hlsl`). The renderer now
references `RayTracer.Core`, packs the *same* `Tracable[]` the CPU renderer
builds, and traces the real maze with fullbright spectral shading. Because no
GPU is available in CI, the shader math is mirrored by `Phase1Reference.cs` and
pinned to the CPU classes by `RayTracer.Tests/GpuPhase1Tests.cs`; the D3D12
execution path is validated on the RTX dev box via `--phase1-selftest`.

- [x] **1.1** Scene packer: `GpuScenePacker.Pack` turns the `Tracable[]` into
      vertex/index buffers + a `GpuPrimitive[]` (quad basis, `1/|edge|²`, normal,
      material rows, pattern params). Quads expose their basis via the new
      `IQuadPrimitive` interface. Two triangles per quad; `primIndex >> 1` on the GPU.
- [x] **1.2** `SpectralResourceBaker.Bake` uploads the CIE XYZ for the 50 hero
      wavelengths (`DeterXYZ`), the per-material reflectance table
      (`MaterialReflectance[material*50 + index]`), and `DeterministicCorrection`.
      Both hero and companions are deterministic-cycle entries, so lookups collapse
      to indexing by hero index — no CIE/spectral textures with runtime sampling needed.
- [x] **1.3** `PathTrace.hlsl` ports camera ray generation, PCG sub-pixel jitter,
      and hero+3-companion evaluation (`LightingMode.None` fullbright), a line-for-line
      port of `Phase1Reference.cs`.
- [x] **1.4** Brick running-bond and ceiling-tile bevel pattern functions ported to
      HLSL; parity with `BrickRectangle`/`CeilingTileRectangle` asserted in tests
      across straight and oblique rays, mortar and bevel regions.
- [x] **1.5** Per-pixel running-mean accumulation buffer (UAV, persists across frames)
      + inline XYZ→sRGB resolve to the output texture.
      *Milestone: a static maze view accumulates toward the CPU image.* Run with
      `dotnet run --project RayTracer.Gpu -c Release -- --phase1` (windowed) or
      `--phase1-selftest` (headless CPU cross-check).

> Not yet done in Phase 1: a *separate* resolve pass (resolve is inline; the
> spatial/bilateral filter is a Phase 3 concern), accumulation reset on camera
> motion beyond the frame-0 reset flag, and any lighting (Phase 2).

### Phase 2 — Lighting — ✅ IMPLEMENTED (pending dev-box validation)

Phase 2 lives in `RayTracer.Core/Gpu/Phase2Reference.cs` (pure, unit-tested
lighting math), `RayTracer.Gpu/Shaders/PathTracePhase2.hlsl` (the ported
shader), and `RayTracer.Gpu/Phase2Renderer.cs` + `Phase2Scene.cs` (D3D12 host +
light packing). As in Phase 1, the shader is a line-for-line port of the C#
replica; because no GPU runs in CI, `RayTracer.Tests/GpuPhase2Tests.cs` pins the
replica to the **actual** CPU renderer — one sample of `JobSystem.TraceCore`
over the real `BVH`/lights (cache off, volumetrics off), matched bit-for-bit —
and the D3D12 path is checked on the RTX dev box via `--phase2-selftest`.

- [x] **2.1** Light positions packed into a `StructuredBuffer<float4>`
      (`GpuLight`/`LightPacker`); weighted light selection (`SelectLight`) ported
      — recomputed weights rather than a per-thread `MAX_LIGHTS` array — and NEE
      shadow rays via an occlusion `RayQuery`
      (`RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH`).
- [x] **2.2** Cosine-hemisphere sampling and the one-bounce + tertiary-bounce
      indirect estimator ported as an iterative (non-recursive) chain, with the
      exact PCG RNG sequence of `TraceCore`.
- [x] **2.3** Sample clamping (on the total only) and per-bounce direct/indirect
      running-mean accumulation buffers (`DirectAccum`/`IndirectAccum`) ported.
      *Welford variance and the direct/indirect variance splits are deferred to
      Phase 5*, where the on-GPU statistics reduction + readback that consumes
      them is built — the M2 buffers are write-only until then.
- [x] **2.4** Irradiance-cache decision: **dropped**. The GPU re-traces the
      tertiary ray every sample instead of maintaining a concurrent hash-grid
      cache (hash probing + atomics + frame-lifetime management port poorly and
      the GPU can afford the extra bounce). The port therefore mirrors the
      cache-off CPU path; a fixed voxel-grid cache remains the GPU-friendly
      redesign if 2+-bounce cost ever matters.

> Run with `dotnet run --project RayTracer.Gpu -c Release -- --phase2` (windowed)
> or `--phase2-selftest` (headless CPU cross-check). Phase 1's `--phase1`
> fullbright path is untouched.

### Phase 3 — Temporal pipeline — ✅ IMPLEMENTED (validated on the RTX dev box)

Phase 3 splits the single Phase 2 megakernel into a **two-pass** pipeline:
`RayTracer.Gpu/Shaders/PathTracePhase3.hlsl` (the Phase 2 trace, now also
writing a G-buffer — world hit point, oriented normal, hit mask — and honouring
a soft-reset flag) and `RayTracer.Gpu/Shaders/ResolvePhase3.hlsl` (the temporal
resolve). The resolve math is a line-for-line port of
`RayTracer.Core/Gpu/Phase3Reference.cs`, and — as in Phases 1–2 — the
`RayTracer.Tests/GpuPhase3Tests.cs` tests pin that replica to the **actual** CPU
renderer (`JobSystem.TaaResolver` / `ResolveFilteredXYZ`) over a real two-frame
render, matching its bilateral filter, reprojection, history blend and history
weight. The host is `RayTracer.Gpu/Phase3Renderer.cs` + `Phase3Scene.cs` (the
maze walked autonomously by a `CameraController` so motion exercises the
pipeline). The RTX 3070 self-test (`--phase3-selftest`) confirms the frame-0
pass-through matches the CPU reference **100%** and the temporal path runs
TDR-free.

- [x] **3.1** TAA reprojection pass: previous-camera constants, ping-pong history
      buffers (XYZ / hit point / valid, selected per frame), bilinear history
      fetch with per-tap validity weights, and disocclusion rejection (reprojection
      error + normal agreement), neighborhood-clamped temporal blend. Ported in
      `ResolvePhase3.hlsl`, mirroring `TaaResolver.ResolveDisplayBufferWithTaa`.
- [x] **3.2** Bilateral spatial filter pass: motion-gated box / luminance-weighted
      edge-preserving filter over the accumulation buffer
      (`Phase3Reference.FilteredXYZ`, mirroring `JobSystem.ResolveFilteredXYZ`),
      folded into the resolve pass and used both for the current color and the
      neighborhood clamp bounds.
- [x] **3.3** Accumulation reset / soft-reset lifecycle on camera movement:
      `Phase3Renderer` hard-clears via the trace `ResetFlag` on frame 0 and, while
      the `CameraController` reports motion, sets a soft-reset flag that caps each
      pixel's effective sample count to `MotionSampleCap` (mirroring
      `JobSystem.SoftResetAccumulationCore`) while the resolve tightens its
      reprojection threshold and enables the spatial filter.

**Firefly note.** Because the diffuse irradiance cache was dropped (§2.4), the
indirect estimator (uniform light selection × `nLights`, ~300 here) is
high-variance and single-wavelength, so it sprays *coloured fireflies* the cache
would otherwise pre-average — and during motion the soft-reset caps each pixel to
`MotionSampleCap` samples, far too few to average them, so TAA holds each speck
on screen for ~`1/alpha` frames (they read as accumulating confetti). The windowed
demo therefore enables the per-sample firefly clamp the CPU renderer already
provides for its cache-less presets: `--clamp` (default 3.0; on the dev box this
cut peak firefly pixels during the walk from ~690 to ~18 while leaving the D65
white point, Y≈1, untouched). The self-tests keep `--clamp 0` for exact parity
with the unclamped CPU reference.

> Run with `dotnet run --project RayTracer.Gpu -c Release -- --phase3` (windowed
> autonomous maze walk; `--clamp <v>` tunes the firefly clamp) or
> `--phase3-selftest` (headless CPU cross-check + temporal smoke test). Phases 1–2
> remain untouched.

### Phase 4 — Volumetrics — ✅ IMPLEMENTED (validated on the RTX dev box)

Phase 4 keeps the Phase 3 two-pass temporal pipeline and adds **camera-segment
volumetrics** to the trace pass. `RayTracer.Gpu/Shaders/PathTracePhase4.hlsl` is
the Phase 3 trace shader with the smoke/fog density field and the single-scatter
marcher folded into `ShadeSample`: after shading the primary hit, the segment
`[CamPos → hitPoint]` is ray-marched and the result composited onto the *raw*
(pre-`DeterministicCorrection`) XYZ as `xyz·T + inscatter`, with the per-bounce
AOVs scaled by `T` — a line-for-line port of the CPU's `if (hit) { volume =
IntegrateVolumetricSegment(...); xyz = volume.Apply(xyz); … }` in `TraceCore`.
The resolve pass is unchanged, so `ResolvePhase3.hlsl` is reused verbatim. The
marcher math mirrors `RayTracer.Core/Gpu/Phase4Reference.cs`, which
`RayTracer.Tests/GpuPhase4Tests.cs` pins to the **actual**
`JobSystem.IntegrateVolumetricSegment` (real `BVH` + lights) across every smoke
mode and quality tier — Medium (ambient-only inscatter), High (shadow ray every
Nth step) and Ultra (shadow every step + Henyey–Greenstein phase), moving and
still. The host is `RayTracer.Gpu/Phase4Renderer.cs` (Phase 3 host + a per-light
colour buffer at `t5` for the inscatter tint + volumetric constants). The RTX
3070 self-test (`--phase4-selftest`) confirms the frame-0 fog composite matches
the CPU reference **100%** (700/700 px within 8/255, fog present on every pixel)
and the temporal path runs TDR-free.

- [x] **4.1** Density fields (`GetDensityBiome`/`Fog`/`Ground` + `SmokeCoverage`,
      biome hashing, `SmoothStep01`) and the segment integrator (transmittance,
      in-scatter, HG phase, per-light shadow rays on the configured step interval,
      the moving-path half-step reduction) ported to HLSL. The light packer now
      carries colour (`PackedLights.Colors`/`ColorData`) since inscatter tints by
      `light.Color`. Companion wavelengths are unaffected — volumetrics acts on the
      composited XYZ, and the correction being scalar lets the shader composite
      after correction (`Phase4Reference.Apply`, unit-tested).
- [x] **4.2** Biome banding verified: `GetDensityBiome` parity is exercised by the
      integrator parity test over real camera segments, plus a standalone banding
      assertion (non-negative, banded, smoke-free biomes reach ~0). On hardware the
      windowed walk renders biome / fog / ground smoke without TDR.

**Smoke appearance (looks-like-smoke pass).** The original density field was a
smooth low-frequency 2D coverage that, integrated along the view ray, read as a
uniform *depth haze* that whited out with distance — old-fashioned draw-distance
fog. Two shared (CPU + GPU) changes fix this, keeping full parity:

- A texture-free **3D value-noise fBm turbulence** (`SmokeTurbulence`, added to
  `PathTracer.cs` and mirrored in `Phase4Reference.cs` + the HLSL) multiplies every
  density mode. Low horizontal frequency gives billows several world-units across
  (so neighbouring rays integrate visibly different amounts) and a thresholded,
  smoothstepped remap carves near-clear gaps between dense clumps (`~[0.05, 2.2]×`).
  Biome banding still decides *where* smoke lives (dry biomes stay at zero); the
  turbulence sculpts wisps *within* it.
- **Inscatter recalibrated down** (`VolumetricOptions.FromQuality`: Medium
  `0.35 → 0.16`, others in proportion). The single-scatter inscatter over ~300
  bright lights was clipping to white and hiding all density structure; at the
  lower strength the medium reads as a translucent, patchy smoke whose thick/thin
  variation is actually visible. Parity is unaffected (both sides read
  `js.Volumetrics`), and the synthetic attenuation tests were made spatially robust
  (density-scale invariant instead of ray-length monotonicity, which a non-uniform
  medium can legitimately violate).

**Biome smoke legibility.** In the default `--phase4` (Biome) mode the smoke was
near-invisible. The biome sub-types used their own *much thinner* densities (fog
biome ~7× thinner than the `AlwaysFog` mode), so a biome the ceiling indicator
labelled "full fog" (amber) delivered a fraction of the fog — it read as clear.

- **Biome sub-types now reuse the Always* profiles.** A "fog" biome is exactly
  corridor fog and a "ground" biome exactly ground smoke, via shared
  `FogProfile` / `GroundProfile` helpers used by both `GetDensityFog/Ground` and
  `GetBiomeCellDensity` (in `PathTracer.cs`, `Phase4Reference.cs`, and the HLSL).
  So an amber biome now looks like `--fog` and a green biome like `--ground`; clear
  biomes stay empty, giving the spatial variety. (This also fixed a latent parity
  bug: an earlier density bump had touched the two C# copies but not the HLSL, a
  mismatch the self-test missed because its start view sits in a *clear* biome with
  few fog pixels. With the profiles shared, HLSL↔reference match again — the
  cross-check tightened back to 100% within 8/255, mean |Δ| ≈ 0.2.) The maze's
  short 2-unit sightlines still limit how much fog any one ray accumulates, so the
  effect is strongest down longer corridors and in the full-fog biomes.
- **Smoke stays in its biome.** The biome bilinear was origin-aligned, so a point
  in the *middle* of a biome came out as a 50/50 blend with its +X/+Y neighbour —
  smoke bled a full half-biome outside where it belonged (and outside the ceiling
  indicator's colour). It's now centred on biome centres (interior samples purely
  itself) with a narrow-band edge blend (`BiomeEdgeBlend`, ±1.6 world units), so a
  biome's interior is pure and only the boundary feathers. Clear biomes are now
  genuinely clear. This shifted the self-test: its camera sits in a clear biome, so
  the fog-present fraction legitimately dropped (few rays reach a neighbouring fog
  biome); the frame-0 parity is still 100% and now also validates containment (the
  clear-biome pixels must match the reference at ~zero fog), so the `foggyRate` guard
  was relaxed accordingly.
- A **ceiling biome-category overlay** (on by default; `--no-indicator` to disable)
  recolours each ceiling tile by the smoke category of the biome it sits in —
  untinted = clear, green = ground smoke, amber = full fog — so the fog *layout* is
  legible at a glance. It's a shader-only debug overlay in `PathTracePhase4.hlsl`
  (`ApplyBiomeIndicator`, keyed off the oriented normal for "is a ceiling" + the
  same `IsSmokeBiome`/`IsFogBiome` classifiers the density uses), gated by a
  `BiomeIndicator` constant that the parity self-test forces off.

**Moving fog (decoupled resolve composite).** The smoke can now roll over time. A
naive approach — animating the density inside the trace — fails: the running-mean
accumulation + TAA converge a *static* scene, so per-frame motion averages away
(the fog freezes when the camera stops). Instead the fog is **decoupled** from the
surface. A `VolTime` uniform drifts the turbulence domain (`SmokeTurbulence(p,
time)`; `time = 0` is the old static field, so parity is unchanged). The trace
still marches the fog but writes it (transmittance + in-scatter) to a per-pixel
`FogOut` buffer instead of baking it into the accumulated colour; the surface
accumulates fog-free and keeps converging. A Phase-4 resolve variant
(`ResolvePhase4.hlsl` = the Phase 3 resolve + one composite line) reads that buffer
and composites `surface·T + inscatter` fresh each frame *after* the temporal blend
(history stays fog-free). Because the march is deterministic (no MC noise), the fog
drifts smoothly with zero added surface noise. The windowed demo advances `VolTime`
by wall-clock seconds (`--fog-drift <mult>`, default 1; 0 = static); the self-test
and capture keep it fixed so their cross-checks stay reproducible. Frame-0 output
is unchanged by the refactor (the self-test numbers are identical), because for one
sample `accumulate(surface)=surface` and the resolve composite reproduces the old
`Apply(surface, fog)`.

> Run with `dotnet run --project RayTracer.Gpu -c Release -- --phase4` (windowed
> autonomous maze walk, biome smoke; `--fog` / `--ground` force a single smoke
> mode, `--clamp <v>` tunes the firefly clamp, `--no-indicator` hides the ceiling
> biome overlay) or `--phase4-selftest` (headless CPU cross-check + temporal smoke
> test). Phases 1–3 remain untouched. A headless
> still capture aids visual tuning: `--phase4 --fog --save out.png [--frames N]
> [--walk S] [--inscatter v] [--sigma v]` converges a static frame (optionally
> after walking `S` seconds into the maze) and writes a PNG.

> **Maze randomization.** Every windowed demo (`--phase1`…`--phase4`) and the
> capture now seed the maze from `Random.Shared` so the start of the maze differs
> each run (matching the original app, which seeds from `Environment.TickCount`);
> the chosen seed is shown in the window title / capture log. Pass `--seed <n>` to
> reproduce a specific maze (the light placement is derived from the same seed, so
> one value fully determines the scene). The `--phaseN-selftest` paths keep their
> fixed seeds so the CPU cross-check stays deterministic.

> Shadow-traced volumetric qualities (High/Ultra) are validated by the unit tests
> but intentionally **not** exposed by the windowed demo: with ~300 lights, a
> per-light shadow ray on every march step is billions of rays/frame and would
> TDR. The demo stays on Medium (ambient-only inscatter); a shadow-traced GPU path
> would need light culling first.

### Phase 5 — Debug views, UI wiring, statistics

**5.1 — ✅ IMPLEMENTED (validated on the RTX dev box).** The resolve pass gains a
**debug-view switch**. `RayTracer.Gpu/Shaders/PathTracePhase5.hlsl` is the Phase 4
trace with one addition — it packs the primary hit's base reflectance luminance into
the unused `NormalOut.w` (the Albedo AOV) — and `ResolvePhase5.hlsl` is the Phase 4
resolve plus a `DebugMode` switch that renders the selected view. The colorization
(multi-stop palettes, normal/albedo encodings, per-mode dispatch) is a line-for-line
port of `RayTracer.Core/Gpu/Phase5Reference.cs`, whose palettes mirror
`JobSystem.DebugBufferRenderer`; `RayTracer.Tests/GpuPhase5Tests.cs` pins the palette
endpoints and mode routing in CI. The host is `RayTracer.Gpu/Phase5Renderer.cs` (the
Phase 4 host + debug-mode plumbing, the extra resolve bindings — effective sample
count, the direct/indirect accumulation splits — and the current camera position +
sample-count normalizer via the resolve constants). The RTX 3070 self-test
(`--phase5-selftest`) confirms **Beauty is a 100% (700/700 px within 8/255) match to
the CPU reference** — Beauty is bit-for-bit the Phase 4 path — and that all nine views
render TDR-free, non-degenerate, and routed distinctly.

- [x] **5.1** Debug view switch in the resolve shader. Views: beauty, sample count,
      depth, normal, albedo, direct/indirect lighting splits, history weight, rejection
      mask (plus variance + clamp heatmap added in 5.2). Run `--phase5` (windowed
      autonomous walk; number keys `0-9` / `←→` switch views live), `--phase5 --debug
      <name> --save out.png` (headless capture of one view), or `--phase5-selftest`
      (headless CPU cross-check + view smoke test).
**5.2 — ✅ IMPLEMENTED (validated on the RTX dev box).** The trace pass now
accumulates the **Welford luma variance** (`LumaM2`, reset with the running mean) and
a **clamp AOV** (cumulative L1 clamp amount + a per-frame clamped flag), mirroring
`PathTracer.cs`. A new single-thread-group **reduction pass** (`ReducePhase5.hlsl`,
a port of `Phase5Reference.Reduce` which mirrors the `TaaResolver` reduction) strides
over every pixel and collapses the per-pixel variance / sample count / clamp / history
weight / rejection / hit mask into an 8-float readback buffer — the only per-frame CPU
readback, a few bytes (pitfall P6). `Phase5Renderer` maps it into a `Phase5Stats`
(exposed as `FrameStats`), which drives the SampleCount / Variance view normalizers
(observed-max spp; 8×average variance — the real ranges, replacing 5.1's
frames-since-reset guess) and the windowed HUD (title bar shows spp, avg variance,
clamp %, rejection %). The **Variance and ClampHeatmap debug views** are now live. The
self-test's Part C asserts the reduced stats on hardware (at 1 spp: spp = 1, variance
= 0, clamp ≈ 0 %, hit ≈ 100 %), and the Debug build's GPU-based validation confirms the
extra passes/barriers are clean.

- [x] **5.2** On-GPU statistics reduction → tiny readback, surfaced as `Phase5Stats`
      / `FrameStats` and shown in the windowed title (the GPU port has no
      `CalibrationForm`; the HUD is the equivalent readout). Adds the Variance and
      ClampHeatmap views and the observed-max / 8×-average view normalizers.
**5.3 — ✅ DONE (re-evaluation + prune).** Audited the motion-quality machinery the
GPU port carries and confirmed the CPU-speed *workarounds* (P13) were never ported and
should stay that way: **checkerboard-while-moving, the performance calibrator, the CPU
throttle, and the diffuse irradiance cache have no GPU equivalent** (the GPU sustains
enough spp that they earn nothing). The remaining ported knobs were each judged:

| Knob | Verdict | Why |
|---|---|---|
| Motion sample cap (soft reset) | **Keep** | Structural, not a CPU hack — in the accumulate-then-TAA design it's what makes the running mean track the scene during motion (measured: spp settles at `cap+1 = 21` while moving, so recent samples dominate). |
| TAA blend α / tighter moving reproj threshold | **Keep** | Core temporal denoise; the moving path (α×10, tighter threshold) is what suppresses ghosting. |
| Motion-gated spatial (box) filter, `filterRadius` | **Keep** | Real denoiser, radius-0 when still so it only costs during motion. |
| **Edge-aware bilateral filter (`edgeAwareFilter`)** | **Remove** | Genuinely dead — never wired to any flag, so the `EdgeAware` branch was unreachable. Dropped from `Phase5Renderer` (constructor knob + constant) and `ResolvePhase5.hlsl` (the whole bilateral branch), leaving the box path the demo/self-test actually exercise. |

- [x] **5.3** Motion-quality knobs re-evaluated; the dead edge-aware bilateral knob
      removed from Phase 5, the structural motion knobs kept with a documented
      rationale, and the never-ported CPU workarounds confirmed absent. (The frozen
      Phase 3/4 hosts keep their copies, per the duplicate-don't-refactor convention.)

### Phase 6 — Productization

Phase 6 productizes the renderer; it adds no new rendering features, so it **reuses the
Phase 5 shaders verbatim** (`PathTracePhase5.hlsl` / `ResolvePhase5.hlsl` /
`ReducePhase5.hlsl`) and its Beauty image is bit-for-bit Phase 5's. The work is
host-side robustness the fixed-size demo hosts skipped.

**6.1 — ✅ IMPLEMENTED (validated on the RTX dev box).** `RayTracer.Gpu/Phase6Renderer.cs`
is the Phase 5 host with the size-dependent resources (output texture, UAV heap, and
every per-pixel buffer — accumulation, G-buffer, ping-pong TAA history, the statistics
AOVs) split out of one-time init into `CreateSizeDependentResources` /
`DisposeSizeDependentResources`, so **`Resize(w,h)`** can rebuild them at a new resolution
and `ResizeBuffers` the swap chain (accumulation hard-resets after a resize — a resized
image is a fresh convergence). The whole GPU stack is factored into `DisposeGpuResources`
so **`TryRecoverDevice`** can tear it down and re-run `Initialize` after a TDR / driver
reset (the retained scene/spectral/light data is enough to rebuild); `RenderFrame` returns
`false` when `Present` / the GPU wait reports the device lost (a timed, device-removed-aware
wait, not an infinite hang) so the windowed loop can recover (P10/P12). The windowed demo
adds a **resizable window**, a **fullscreen-borderless toggle** (F11; `--fullscreen` starts
fullscreen), and **Esc** to leave fullscreen / close. The RTX 3070 self-test
(`--phase6-selftest`) confirms Part A frame-0 Beauty is a **100% (700/700 within 8/255)**
match to the CPU reference, Part B resizes down to 960×540 and back with non-degenerate
images, and Part C's device recovery rebuilds and renders a valid frame; the windowed run
(`--phase6 --fullscreen --frames N`) exercised a live swap-chain resize to the monitor's
native 2560×1440 TDR-free.

- [x] **6.1** Resize handling, device-removed recovery, fullscreen borderless. Run
      `--phase6` (windowed, resizable autonomous walk; F11 fullscreen, Esc exit, `0-9`/`←→`
      switch debug views), `--phase6 --fullscreen` (starts fullscreen), or `--phase6-selftest`
      (headless frame-0 parity + resize + device-recovery cross-check).
**6.2 — ✅ IMPLEMENTED (validated on the RTX dev box).** `RayTracer.Gpu/Screensaver.cs`
adds the classic Windows screensaver switches (a `.scr` is this exe renamed):
`/s` runs full-screen (borderless, cursor hidden, **exit on any key / mouse input** —
the first spurious mouse-move is ignored), `/p <hwnd>` renders into the Screen Saver
settings dialog's preview pane (a child window `SetParent`-ed under the passed HWND via
the x64 `SetWindowLongPtr`/`WS_CHILD` dance, exiting when the pane closes), and
`/c[:<hwnd>]` opens a small `ConfigDialog` (smoke mode, maze size, fog-drift, firefly
clamp) persisted to `%APPDATA%\RayTracer.Gpu\screensaver.json` and read back by `/s`+`/p`.
All modes drive the productized `Phase6Renderer` (its fullscreen + device-removed
recovery is exactly what a screensaver left running for hours needs). `TryParse`
normalizes the switch letter case and the `:`/space HWND forms. The CPU-only
`--screensaver-selftest` (CI-safe) pins arg parsing + settings round-trip; on the dev
box `/s --frames N` rendered full-screen at native 2560×1440 and
`--screensaver-preview-selftest` reparented a child render window into a host pane and
rendered TDR-free.

- [x] **6.2** Screensaver mode (`/s`, `/c`, `/p`) from the original design doc, now
      viable at 1080p60. Register by renaming the built exe to `.scr`; test with
      `--screensaver-selftest` (arg/settings) and `--screensaver-preview-selftest`
      (the `/p` child-window path).

**6.3 — ✅ IMPLEMENTED (validated on the RTX dev box).** `RayTracer.Gpu/RegressionHarness.cs`
is the golden-image regression harness (Testing §6, layer 3). It renders the deterministic
default-seed scene (start camera, no motion, fog phase 0) converged to a fixed sample
count, then captures **every** debug view (regressions hide in the AOVs, not just Beauty)
and compares each to a committed golden PNG. Because the trace RNG and accumulation order
are deterministic, the same GPU reproduces every view **bit-exactly** run-to-run (measured:
max channel Δ = 0 across all 11 views), so the tolerance is tight (≥99.5 % of channels
within 2/255, mean |Δ| < 1.0) and only absorbs driver FP jitter (P4). `--phase6-regress`
compares (nonzero exit on any regression or missing golden); `--phase6-regress --update`
(re-)writes the goldens under `RayTracer.Gpu/Regression/golden` (override with `--dir`).
Like the phase self-tests it needs a DXR 1.1 GPU, so it is a dev-box tool, not CI.

- [x] **6.3** Image-comparison regression harness (see Testing, §6). The committed
      baseline lives in `RayTracer.Gpu/Regression/golden/*.png`; regenerate after an
      intended visual change with `--phase6-regress --update`.

**Unified app entry (config-then-run).** The phase-by-phase `--phaseN` flags were the
development scaffold; the **default launch** (no args) is now the finished product,
mirroring the original app's `CalibrationForm → RayForm` flow: `Program.RunApp` shows a
setup dialog (`SetupDialog` — resolution / fullscreen, smoke mode, starting debug view,
maze size, fog-drift, firefly clamp, persisted to `%APPDATA%\RayTracer.Gpu\settings.json`
as `AppSettings`), then **Start** runs the full renderer — the productized Phase 6
pipeline, which folds in every phase (spectral shading, NEE + indirect lighting, the
temporal denoiser, volumetrics, live debug views, resize / fullscreen / device-recovery).
The same `SetupDialog`/`AppSettings` back the screensaver `/c` config. WinForms init is
routed through a one-time `Program.EnsureAppConfigured` guard so the dialog-then-window
flow doesn't double-initialize. The old Phase 0 spike moved behind `--phase0`; all phase
demos, self-tests, screensaver switches, and the regression harness remain available as
flags. Verified on the dev box: default launch shows the dialog, and invoking **Start**
(UI-automation) transitions into the render loop (`spp` climbing), and `--setup-selftest`
pins the dialog build + settings round-trip.

**Phase 6 complete.** All three items are implemented and validated on the RTX 3070, and
folded into a single config-then-run executable; the GPU port now reaches feature +
productization parity with the plan.

---

## 5. Pitfalls and points of attention

**P1 — Hardware/driver gate.** DXR inline ray tracing needs a Tier 1.1 device (RTX
20-series+, RX 6000+, recent Intel Arc). The RTX 3070 dev box is fine, but the app
now has a hard hardware floor the CPU renderer didn't have. Detect tier at startup
and fail gracefully. Keeping the CPU renderer as a fallback path is the cheap answer
(another reason not to delete `RayTracer.Core`).

**P2 — HLSL is a different world.** No classes/interfaces/virtual dispatch, no
`Span`, no exceptions, no `stackalloc` of dynamic size (the `SelectLight` weights
array must become a fixed-size array with a `MAX_LIGHTS` constant), 16-byte
constant-buffer packing rules that silently corrupt data when violated, and
`float`-only math (no doubles without perf cliffs — the CPU code is float-based, so
fine). Budget real time for shader authoring; this is the majority of the effort.

**P3 — Debugging changes completely.** No breakpoints, no `Console.WriteLine` in
shaders. The workflow is: PIX for Windows captures (invaluable — learn it in Phase 0),
debug UAV buffers you write intermediate values into, and the existing debug-view
habit (which this codebase is already good at — that culture transfers directly).
Enable the D3D12 debug layer + GPU-based validation in dev builds from day one; it
catches most resource-state and binding mistakes that otherwise manifest as silent
garbage or TDRs.

**P4 — Don't expect bit-identical images.** GPU FMA contraction, fast-math, texture
filtering, and wave-level execution order mean results will differ from the CPU
renderer in the low bits (and Welford accumulation order differs too). Comparison
must be perceptual/statistical (per-pixel tolerance, MSE/SSIM thresholds), not
equality. The deterministic hash RNG *will* match exactly; float accumulation won't.

**P5 — Divergence and the megakernel.** Spectral hero-wavelength assignment differs
per pixel, and path lengths differ per pixel — threads in a wave diverge. For this
scene size a megakernel is still the right first architecture (don't build a
wavefront path tracer for a maze of quads), but keep the shader's worst-case register
pressure in mind: if occupancy tanks, split the tertiary bounce into a separate pass
before reaching for exotic architectures.

**P6 — Readback is the new performance enemy.** The CPU renderer freely reads any
buffer any time. On GPU, copying buffers back stalls the pipeline. Rule: per-pixel
data never leaves the GPU except through on-GPU reductions into a few-bytes readback
buffer, double-buffered so the CPU reads last frame's stats. The debug *views* are
rendered on GPU, so they cost nothing extra.

**P7 — Self-intersection epsilons need retuning.** The CPU code offsets ray origins
by `1e-3` along the normal and uses `1e-4` t-minimums. Hardware triangle intersection
has different precision characteristics than the analytic quad test (and the quads
are now two triangles — watch for shadow acne along the diagonal). Expect to revisit
these constants; consider the standard "offset along geometric normal scaled by
hit distance" trick if acne appears.

**P8 — Two-triangle quads change UV reconstruction.** Today `u,w` come from the
analytic rectangle test. In the shader, reconstruct UV from the hit point and the
quad's stored basis vectors (dot products against edge1/edge2 — same math), *not*
from triangle barycentrics, so brick/tile patterns stay seamless across the diagonal.

**P9 — TLAS/BLAS lifecycle.** The maze is static per run, so build BLAS once and
forget — but scene rebuilds (new maze, settings change) must rebuild acceleration
structures and there's a scratch-buffer dance to get right. Keep it simple: static
BLAS, one-instance TLAS, full rebuild on scene change (milliseconds for this size).
No refit/update machinery needed.

**P10 — WinForms + swap chain interop.** Rendering into a WinForms panel HWND works
(flip-model swap chain, `DXGI_SWAP_EFFECT_FLIP_DISCARD`), but: handle resize
(buffers must be recreated), don't let WinForms paint over the panel
(`ControlStyles.Opaque` / disable background paint), and decide the present cadence —
the current 120 Hz UI timer becomes "present on vsync" instead. Device-removed
(`DXGI_ERROR_DEVICE_REMOVED`, e.g. driver update or TDR mid-run) needs a recovery or
at least a clean error path — a screensaver that crashes the session is a bad look.

**P11 — Shader hot-reload is worth building early.** A file-watcher that recompiles
HLSL via DXC and swaps the PSO turns shader iteration from minutes to seconds. Half a
day of work in Phase 0, pays for itself tenfold.

**P12 — TDR (timeout detection and recovery).** A shader that runs >2 seconds gets
the device removed by Windows. Early bugs (infinite `while` in traversal loops,
runaway bounce loops) will present as "screen goes black, device lost" rather than a
hang you can break into. Keep iteration counts bounded and defensive in shader loops.

**P13 — Don't port the workarounds.** The checkerboard rendering, motion sample caps,
performance calibrator, CPU throttle, and possibly the irradiance cache exist because
the CPU is slow. Port the *features* (spectral rendering, NEE, TAA, volumetrics,
debug views), measure, and only then decide which workarounds still earn their
complexity. The new project is a chance to shed that weight, not re-implement it.

---

## 6. Testing strategy

The current MSTest suite tests C# logic directly. GPU shader code can't be unit
tested the same way. Three layers replace it:

1. **Shared-logic tests stay:** maze generation, geometry building, camera math,
   scene packing (assert on the packed buffers — material IDs, basis vectors, CIE
   table contents) — all plain C#, all still unit-testable.
2. **Shader-function tests:** put pure functions (hashes, density fields, pattern
   functions, wavelength math) in an HLSL include file, compile a tiny test compute
   shader around it, dispatch on inputs, read back outputs, compare with the C#
   implementations. This catches translation errors precisely where CPU/GPU parity
   matters most.
3. **Golden-image regression:** deterministic seed + fixed camera + N frames →
   compare against CPU-rendered references with a perceptual tolerance (see P4), and
   against previous GPU renders with a tighter one. Run per-debug-view, not just
   beauty, since the buffers are where regressions hide.

---

## 7. Effort summary

| Chunk | Relative size |
|---|---|
| Phase 0 spike (device, swap chain, first RayQuery) | Medium — mostly unfamiliar-API friction |
| Scene packing + spectral resources (Phase 1) | Medium — mostly straightforward C# |
| Path tracer + lighting in HLSL (Phases 1–2) | **Large — the heart of the port** |
| TAA/filter/resolve passes (Phase 3) | Medium |
| Volumetrics (Phase 4) | Small — pure math, ports directly |
| Debug views + stats readback (Phase 5) | Medium |
| Productization + tests (Phase 6) | Medium |

The single most important step is **Phase 0.4** — one triangle hit via `RayQuery` on
screen. Everything after that is porting known-working logic into a known-working
harness.
