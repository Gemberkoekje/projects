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

### Phase 3 — Temporal pipeline
- [ ] **3.1** TAA reprojection pass: previous camera constants, ping-pong history
      textures, bilinear history fetch with validity weights, disocclusion rejection.
- [ ] **3.2** Bilateral spatial filter pass.
- [ ] **3.3** Accumulation reset / soft-reset lifecycle on camera movement (port
      `AccumulationBuffer` semantics — this is control logic in C#, resets are a
      cheap GPU clear or a flag in constants).

### Phase 4 — Volumetrics
- [ ] **4.1** Port smoke/fog density functions and the volumetric segment integrator.
- [ ] **4.2** Verify biome banding against CPU images.

### Phase 5 — Debug views, UI wiring, statistics
- [ ] **5.1** Debug view switch in the resolve shader (beauty, spp, variance, history
      weight, clamp heatmap, depth, albedo, normals, lighting splits).
- [ ] **5.2** On-GPU reduction (sum/max) of frame statistics to a tiny readback
      buffer; wire `FrameDiagnostics` and the `CalibrationForm` readouts to it.
- [ ] **5.3** Re-evaluate which motion-quality knobs still make sense; remove dead
      ones from the UI rather than porting them.

### Phase 6 — Productization
- [ ] **6.1** Resize handling, device-removed recovery, fullscreen borderless.
- [ ] **6.2** Screensaver mode (`/s`, `/c`, `/p`) from the original design doc — now
      viable at 1080p60.
- [ ] **6.3** Image-comparison regression harness (see Testing, §6).

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
