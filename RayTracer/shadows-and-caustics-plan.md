# Lighting & Shading — Volumetric Shadows, Translucent Shadows & Caustics

A plan for the "something's missing" in the current lighting: **fog that casts shadows**,
**bubbles that cast vague shadows**, and **prisms & bubbles that throw rainbows onto the
walls**. It complements `design.md` (Arc 2), `propervolumetricfogplan.md`, and
`spectral-effects-plan.md` (§1.3.4 caustics, §2.1 dispersion, §2.6 bubbles), all of which
name these effects but leave the enabling *light transport* unbuilt.

---

## TL;DR — the three asks share one root cause

The engine is a **backward path tracer**: rays leave the camera, and the only way light
reaches a surface is Next-Event Estimation (NEE) with a **binary shadow ray**. Everything
the fog / prism / bubble does today happens on that *backward, camera-side* ray. Nothing
travels *toward* a surface through a medium or a dielectric. So:

| You want | Why it's missing today | What it needs |
|---|---|---|
| Moving fog casts shadows | Fog σ is integrated only on the **camera** ray, never on the light/shadow ray; fog doesn't self-shadow | **Transmittance-aware occlusion** (integrate fog σ along the shadow segment) + fog self-shadow in the march |
| Bubbles cast *vague* shadows | Shadow test is **binary** — CPU treats a bubble as a solid black occluder, GPU skips it entirely | **Transmittance-aware occlusion** through thin dielectrics (Fresnel + film + Beer–Lambert) |
| Prisms & bubbles throw rainbows on walls | **No forward light transport at all** — dispersion/thin-film run only in the backward `TraceSpecularRadiance` eye chain; the word "caustic" appears only in plan docs | **Caustics** — a genuinely new forward transport pass (spectral photon mapping) |

Two new capabilities cover all three: **(A) transmittance-aware shadow rays** (asks 1 & 2)
and **(B) forward caustic transport** (ask 3). A third, supporting item — **(C) area lights /
soft shadows** — is what makes all of it read as physically grounded rather than crisp-and-fake,
and is a natural prerequisite for "vague" and "soft."

---

## What we have today (with evidence)

### Shading & shadows are backward + binary

- Direct lighting is point-light NEE with a **hard** shadow ray at four sites, all identical:
  `PathTracer.cs:418` (`DirectTermAt`), `:603` (primary), `:712` (secondary bounce), `:777`
  (tertiary bounce). Each calls `_bvh.IsOccluded(...)`.
- `BVH.IsOccluded` (`BVH.cs:115–153`) returns `true` on the **first** primitive hit before
  `maxDist`, with **no `SurfaceKind` check** — note `FindClosest` returns surface/ior/extinction
  but `IsOccluded` returns `bool` and ignores all of it. So **glass, jewels, and bubbles all cast
  solid black shadows on the CPU.**
- GPU mirror: `TraceOccluded` uses `RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH`
  (`PathTracePhase5.hlsl:242`, `PathTracePhase6.hlsl:483`). Phase 6 **explicitly skips bubbles**
  (`s.Surface != SURFACE_BUBBLE`, `PathTracePhase6.hlsl:504–505`) so a stream doesn't stack into
  "muddy dark blobs" — but glass/jewels still occlude solid. **This is a CPU↔GPU divergence:**
  CPU bubbles occlude, GPU bubbles don't; neither is a soft, faint shadow.
- Lights are **point-only**: `Light.cs` has `Position`, `Color`, `Ambient` — no radius, no
  direction, no area. Shadow rays target a single point → **hard shadows only**, no soft
  shadows, no area-light NEE. (`LightCones.cs` is a colour-accumulation struct despite the name,
  not a spot/cone light.)

### Fog is a camera-ray effect (and CPU-static, GPU-moving)

- `IntegrateVolumetricSegment` (`VolumetricIntegration.cs:27`) marches **one straight segment**
  and returns `(Transmittance, Inscatter)`. It's applied to the **camera→hit** segment
  (`PathTracer.cs:855`) and to **specular reflection/refraction** segments (`PathTracer.cs:272`).
  It is **never** applied along an NEE shadow ray → **fog never dims light arriving at a surface.**
- The fog's own in-scatter *can* be shadowed by **solid geometry** via `EstimateInscatterLight`
  (`VolumetricIntegration.cs:120–156`, `bvh.IsOccluded`), gated by `ShadowStepInterval`
  (0 = never, used by Low/Medium; N = every Nth; 1 = Ultra). But it uses the same **binary**
  test and the fog **does not integrate its own σ toward the light**, so **fog does not
  self-shadow** — no crisp shafts through the fog body, only where a solid wall clips the beam.
- The density field itself: CPU `SmokeTurbulence(p)` is **position-only / static**
  (`PathTracer.cs:142`); the GPU advects it with `time` (`PathTracePhase6.hlsl:702`, `q += time *
  (0.30,0.10,0.18)`, fed by `VolTime`/`SetFogTime`). So the **"moving" fog is the GPU path** — and
  it currently moves without ever changing where shadows fall, because it casts none.

### Prisms & bubbles are camera-backward only — no forward transport

- Dispersion `n(λ)=A+B/λ²` lives in `MaterialData.IorAt` (`MaterialsLookup.cs:485–492`) and is
  consumed only inside the **specular eye chain**: CPU `SpecularRadiance`/`TraceSpecularRadiance`
  (`PathTracer.cs:247–336`), GPU `TraceSpecularRadiance` (`PathTracePhase6.hlsl:1003–1120`,
  dielectric branch `:1054`, bubble branch `:1085`). Both are invoked **only for the primary
  camera ray** (`PathTracer.cs:491`, `PathTracePhase6.hlsl:1176`).
- **Diffuse GI bounce rays don't even refract**: the secondary (`secRay`, `PathTracer.cs:677`) and
  tertiary (`tertRay`, `:742`) rays destructure `SurfaceKind` as `_` and shade whatever they hit
  as ordinary diffuse — a GI ray that lands on a jewel or bubble ignores that it's a dielectric.
- **No photon mapping, no light tracing, no NEE-through-dielectric anywhere.** "caustic" occurs
  only in `spectral-effects-plan.md`; a search for `photon|light-trace|forward` hits only
  `design.md`. **Net: the camera sees a rainbow when it looks *through* the prism, but the prism
  casts none onto the floor.**

### The good news — the physics toolkit already exists

`Optics.cs` already has everything the forward passes need, shared verbatim by CPU / GPU
reference / HLSL:

- `Refract` (Snell + TIR, `:63`), `FresnelDielectric` (exact, `:96`), `FresnelSchlick` (`:170`).
- `ThinFilmReflectance` (two-beam sin² interference, `:124`), `FilmSwirl`/`FilmThicknessAt`
  (`:144`/`:154`).
- `AbsorptionAt` (Beer–Lambert σ(λ) from R/G/B anchors, `:45`), `IorAt` Cauchy dispersion.
- The volumetric marcher (`IntegrateVolumetricSegment`) is already a reusable segment integrator.

So none of this needs new physics — it needs the transport that *applies* the physics in the
forward / light-toward-surface direction.

---

## Design principles (carry over from the existing plans)

1. **CPU first, then GPU, in lockstep.** Each step ships in `RayTracer.Core` (`PathTracer.cs` /
   `VolumetricIntegration.cs` / `Optics.cs`) with unit tests, then is ported to the pure-C#
   references (`Gpu/*Reference.cs`) and the HLSL (`PathTracePhase6.hlsl`), with a CI parity test
   pinning the reference to the CPU result and an on-hardware `--phase6-selftest`. The C# reference
   *is* the contract the HLSL is a line-for-line port of. (`spectral-effects-plan.md` §"workflow".)
2. **Gate on cost, expose via presets.** New work is off on Low/Playable, scaled on
   Medium/High/Ultra. Reuse the existing `VolumetricOptions`/`RenderPreset`/`VolumetricQuality`
   machinery; avoid raw physics constants in the UI.
3. **Keep the no-effect path byte-identical.** With fog off and no caustic casters, goldens must
   not move (as the volumetric and dielectric work already guarantees).
4. **Spectral-native.** Every ray/photon carries a wavelength; colour comes from physics
   (dispersion, film, Beer–Lambert), never a hardcoded tint. Lean on `AccumulationBuffer`/TAA to
   average spectral/stochastic noise over frames rather than brute-forcing samples.

---

## Phase A — Transmittance-aware shadow rays (asks 1 & 2)

> **Status: CPU + C# GPU-reference + HLSL implemented; CI-green and validated on the RTX 3070 (DXR 1.1).**
> - **A0** — `BVH.Transmittance` (opaque→0; bubble keeps `1−reflectProb`; glass keeps `1−Fresnel`).
> - **A1** — NEE wiring via the `PathTracer.ShadowVisibility` helper at all four shadow sites; the
>   fog shadow term (`JobSystem.SegmentTransmittance` + `VolumetricOptions.ShadowTransmittance`, on
>   for High/Ultra); the shared `Optics.BubbleReflectProbability`; the **Fog Shadow Debug** preset.
> - **A2** — fog *self*-shadow / god-ray falloff: each in-scatter sample dims its per-light term by
>   the fog transmittance toward that light (`InscatterShadowTransmittance`), implemented in lockstep
>   on the CPU (`JobSystem.EstimateInscatterLight`) and the pure-C# reference
>   (`Phase4Reference.EstimateInscatterLight`); the existing `GpuPhase4Tests` parity test now pins
>   CPU==reference for it.
> - **A3 (GPU / HLSL port) — done, including glass triangles.** `PathTracePhase6.hlsl` gained
>   `TraceTransmittance` (the GPU twin of `BVH.Transmittance`): opaque geometry keeps the hardware
>   `ACCEPT_FIRST_HIT` fast path and returns exactly 0/1, but bubble/dielectric **spheres** attenuate
>   (`1−reflectProb` / `1−Fresnel`, the bubble reflect probability computed identically to the specular
>   branch) and let the ray continue — closing the CPU↔GPU bubble divergence (the old `TraceOccluded`
>   skipped bubbles, so they cast *no* GPU shadow). Wired at all three surface NEE sites (`heroIdx`
>   threaded through `UniformLightTerm`/`MirrorDiffuseScalar`). The A2 fog self-shadow is
>   `InscatterShadowTransmittance` in the shader's `EstimateInscatterLight`, gated by a new
>   `VolShadowTransmittance` cbuffer flag (from `_volumetrics.ShadowTransmittance`).
> - **Glass-window / jewel triangles now transmit too** (the former "left for later" divergence is
>   closed). When the scene carries dielectric **triangles** (glass windows §1.2, glass jewels) the
>   host sets a `ShadowTransmitTriangles` cbuffer flag (`Phase6Renderer.HasTransmissiveTriangles` scans
>   the packed primitives once per scene build/rebuild); `TraceTransmittance` then adds
>   `RAY_FLAG_FORCE_NON_OPAQUE` so glass quads surface as `CANDIDATE_NON_OPAQUE_TRIANGLE` and attenuate
>   while every other triangle stays an opaque blocker that commits and — via `ACCEPT_FIRST_HIT` — ends
>   the ray on first hit. **Glass-free mazes never set the flag, so they keep the pure accept-first-hit
>   fast path and stay byte-identical** (the selftest / regress scenes carry no glass — see validation
>   below). This is the exact GPU twin of `BVH.Transmittance`'s dielectric case, which already handled
>   triangles uniformly on the CPU.
> - **Beer–Lambert-tinted glass shadows.** The dielectric transmittance is now
>   `(1 − Fresnel)·exp(−σ(λ)·d)` (dispersion via Cauchy `n = A + B/λ²`, absorption σ from the same
>   R/G/B anchors the specular branch uses), so **stained glass casts a shadow tinted toward its
>   transmitted hue**, not a merely dimmed grey one — clear glass (σ = 0) is unchanged. The per-interface
>   path `d` is the shared `Optics.GlassShadowInterfaceThickness` (= HLSL `GLASS_SHADOW_INTERFACE_THICKNESS`,
>   0.03), so a two-quad pane accrues ≈ the pane's in-glass absorption and the shadow's hue tracks the
>   pane's transmitted colour. Applied identically on the CPU (`BVH.Transmittance`, one dielectric
>   `case` covering spheres and triangles) and in both HLSL dielectric branches (sphere + triangle),
>   keeping CPU/GPU parity exact (`HitInfo.Extinction` == `AbsorptionAt(P0,P1,P2,λ)` by construction).
>   Pinned by a new CPU unit test (`ShadowTransmittanceTests.Transmittance_StainedGlass_TintsShadowTowardTransmittedHue`).
> - Sphere/triangle glass shadows are Phase6-only (spheres/windows have no CI reference, like the
>   other sphere/bubble/window features), so they are validated on the box, not in CI.
>
> Validation on the RTX 3070: **`--phase6-selftest` 700/700 within 8/255** (bit-for-bit unchanged —
> the plain maze has no spheres and `ShadowTransmittance` is off at Medium, so `TraceTransmittance`
> equals `TraceOccluded`); **`--phase6-regress` all 19 golden views bit-exact**; `--bubble-maze-demo`
> renders the bubble stream cleanly (no artifacts); and a new **`--phase6 --fog --fog-shadows`** capture
> (High volumetrics → `ShadowTransmittance` on, the GPU analog of the CPU **Fog Shadow Debug** preset)
> shows the fog self-shadowing — bright at the ceiling light, falling off with depth. The CPU/CI build
> and all `dotnet test` stay green; existing renders are byte-identical where no transmitter/fog lies
> on a shadow ray.
>
> For the glass-triangle work: `--phase6-selftest` **still 700/700 within 8/255** and `--phase6-regress`
> **all 19 goldens bit-exact** (both scenes are glass-free → the `ShadowTransmitTriangles` flag stays 0
> → the accept-first-hit fast path is unchanged), confirming the change is inert where there is no
> glass. A controlled A/B on `--glass-demo --seed 42` (same maze, flag forced off vs on) shows the
> window glass switch from a **hard black shadow** to a **soft Fresnel-dimmed** one: the room seen
> through the pane is lit through the glass instead of falling dark, ~38 % of pixels change (all behind
> or around the glass; glass-free walls/ceiling untouched), and the scene brightens sensibly. A second
> A/B with **stained** panes (same seed, tinted-shadow on vs hard-shadow off) shows the far room not
> just brightening but taking on a **coloured** cast — over the window region 83 % of pixels change
> with a warm-weighted shift (ΔR +10, ΔG +5, ΔB +2), i.e. the transmitted light is tinted, matching the
> Beer–Lambert `exp(−σ(λ)·d)` term. The full `dotnet test` suite (279) stays green.
> **Phase A is now complete end-to-end (CPU + reference + HLSL), glass triangles + Beer–Lambert tint included.**

**Goal:** replace the binary occlusion test with a **transmittance** that a shadow ray accumulates
as it passes through fog and thin dielectrics. Opaque geometry still returns 0; fog returns
`exp(−∫σ)`; a bubble returns "most of the light minus the Fresnel/film reflection"; coloured glass
returns a wavelength-dependent (coloured) fraction. This single primitive delivers both **fog
shadows** and **vague bubble shadows**.

### A0 — The occlusion → transmittance primitive

- **What:** add `float BVH.Transmittance(Ray ray, float maxDist, ...)` (CPU) /
  `float TraceTransmittance(...)` (HLSL) returning a scalar in `[0,1]` at the ray's hero
  wavelength (spectral colouring falls out because each hero wavelength is traced separately and
  accumulated). `1` = fully lit, `0` = fully shadowed.
- **Behaviour per surface hit along the ray:**
  - `Diffuse` / `Mirror` / opaque → return `0` immediately (unchanged hard shadow; keep the fast
    `ACCEPT_FIRST_HIT` path for scenes with no transmitters so goldens don't move).
  - `Dielectric` / `Bubble` → **do not stop**; multiply running transmittance by the
    *transmitted* fraction and continue the ray:
    - Bubble (thin shell): `T *= (1 − Fresnel_reflect(cosθ, iorFilm)·film(λ,d,cosθ))` — reuses the
      exact reflect-probability already computed in the bubble branch
      (`PathTracer.cs:302`, `PathTracePhase6.hlsl:1100`). A bubble reflects only a few percent, so
      it drops a **faint, thin-film-tinted** shadow — the "vague shadow" you want.
    - Glass slab: `T *= (1 − FresnelDielectric) · exp(−σ(λ)·pathInside)` — a coloured-glass window
      casts a **coloured** shadow (Beer–Lambert σ already in `Optics.AbsorptionAt`).
    - Optional early-out when `T < ε`.
- **Fog:** integrate the fog optical depth **along the shadow segment** and multiply:
  `T *= exp(−∫σ_fog ds)`. Reuse the marcher — factor a `TransmittanceOnly` variant of
  `IntegrateVolumetricSegment` that skips in-scatter (cheaper). **This is what makes the moving fog
  cast shadows on surfaces.**
- **Effort:** Medium. **Files:** `BVH.cs` (new method), `VolumetricIntegration.cs` (transmittance
  variant), `Optics.cs` (reuse). Unit tests: opaque→0; empty→1; monotonic decrease with fog
  thickness; bubble transmittance ≈ 1 − reflectProb; glass halving thickness squares transmittance.

### A1 — Wire NEE onto transmittance (CPU)

- Replace `visible = !IsOccluded(...)` at `PathTracer.cs:418/603/712/777` with
  `float vis = Transmittance(shadowRay, d); directTerm += vis * (…)`. `vis == 0/1` reproduces
  today's hard shadow when nothing transmissive/foggy is in the way.
- **Fix the CPU↔GPU bubble divergence here for free:** bubbles now attenuate rather than fully
  block (CPU) / fully pass (GPU) — both converge on the same faint shadow.
- **Verify:** a bubble/glass sphere over a lit floor drops a soft grey/coloured patch, not a black
  disc; a fog bank between a torch and a wall darkens the wall smoothly.

### A2 — Fog self-shadow / god-rays in the march

- In `EstimateInscatterLight` (`VolumetricIntegration.cs:120`), replace the binary
  `bvh.IsOccluded(shadowRay, …)` with the A0 transmittance so **fog integrates its own σ toward
  the light** — thick fog nearer the light is dimmer, producing real volumetric self-shadowing and
  crisp shafts, not just hard-occluder gaps.
- Keep it gated by `ShadowStepInterval` for cost; add a coarse-step option for the fog-toward-light
  integral (fewer steps than the camera march).
- **Verify:** a single torch in `Fog Debug` shows visible light shafts that fall off with depth
  into the fog, and a wall behind thick fog is shadowed.

### A3 — GPU port

- Port A0–A2 to `PathTracePhase6.hlsl` (`TraceOccluded` → `TraceTransmittance`; NEE sites at
  `:751`, `:636`, `:326`; fog in-scatter at `:794`) and the Phase 4/5 references + shaders that
  carry the fog. Keep the `ACCEPT_FIRST_HIT` fast path for opaque-only shadow rays; only fall into
  the accumulating Proceed loop when the ray's AABB set contains a transmitter or fog is enabled.
- Add `GpuPhase6Tests` parity pinning reference↔CPU for a fog-shadow and a bubble-shadow scene;
  run `--phase6-selftest`.

### A4 — Presets & knobs

- Extend `VolumetricOptions` with `ShadowTransmittance` (bool, default on for High/Ultra) and a
  `ShadowMarchSteps` (coarse). Map in `VolumetricOptions.FromQuality`:
  Low/Playable off, Medium binary (as today), High/Ultra full transmittance.
- Add a **"Fog Shadows"** demo preset (a torch + a fog bank + a back wall) analogous to
  `FogDebug`/`GroundSmokeDebug` in `RenderPreset.cs`.

**Phase A cost note:** transmittance shadow rays no longer early-out on first hit through
transmitters, and the fog-toward-light integral adds marching. Budget it: keep the opaque fast
path, gate the fog integral behind `ShadowStepInterval`, and cap transmitter chain length. Target
≤ ~20–30 % over the current NEE cost on High.

---

## Phase B — Caustics: rainbows on the walls (ask 3) ⭐ the big one

> **Status: CPU foundation + render compositing implemented & CI-green.** Spectral photon mapping
> (B0) is chosen and its core is built and tested in `RayTracer.Core/Lighting/`:
> - `Photon` — a wavelength-carrying energy packet stored only for light→specular→diffuse paths.
> - `PhotonMap` — uniform spatial-hash storage + a spectral density estimate
>   (`Σ power·XYZ(λ) / (π r²)`), with normal rejection and focus diagnostics.
> - `PhotonTracer` — forward emission toward the caster bounds and forward tracing through the
>   specular surfaces (dielectric dispersion via the per-wavelength IOR, bubble thin-shell split,
>   mirror, Beer–Lambert), depositing caustic photons. Deterministic RNG. `Emit` now delegates to a
>   reusable `EmitInto(map, …)` so several casters accumulate into one shared map.
> - `CausticsTests` — map storage/estimate/rejection/scaling; tracer caustic-only invariant,
>   glass-sphere focus, energy bound, and a dispersion test (blue vs red caustics shift by
>   wavelength — the prism rainbow in miniature).
>
> **Compositing into the render (B1 done, CPU):** the map is now folded into `PathTracer.TraceCore`:
> - **Caster enumeration** — `Tracable` gained a defaulted `Surface` member (`Diffuse` by default,
>   overridden by `Plane`/`Sphere` to their material's kind), so `JobSystem` collects the
>   dielectric/bubble casters and unions their bounds without an intersection.
> - **Map lifecycle** — the CPU scene/BVH is static, so `JobSystem` builds the map **once** after the
>   BVH (`BuildCausticMap`), splitting the `CausticOptions.PhotonsPerFrame` budget across the casters
>   (bounded cost regardless of caster count) via `EmitInto`. Deterministic → it converges cleanly
>   under the existing accumulation. (The GPU port will rebuild per frame as bubbles drift.)
> - **Shade-time add** — at each diffuse hit the caustic estimate `EstimateXyz(…)·albedo/π·Strength`
>   is added to the radiance (and the indirect decomposition) **before** the volumetric step (so fog
>   attenuates it) and the deterministic correction (so it is normalised like the direct term).
>   Skipped on specular hits and when the map is absent.
> - **Knobs** — `CausticOptions { Enable, PhotonsPerFrame, GatherRadius, Strength, MaxBounces }` +
>   `CausticQuality.FromQuality` (off ≤ Medium, modest High, full Ultra), threaded through
>   `DenoiseOptions` → `JobSystem` and `RenderPreset` → `CpuRenderForm`. Default **off**; the standard
>   quality presets keep it off until the GPU port lands (CPU/GPU parity), so no shipped render moves.
> - **Tests** — composite render (a glass sphere brightens the floor caustic vs off; caustics never
>   darken a pixel), the byte-identical-with-no-casters guarantee, the quality mapping, `Surface`
>   enumeration, and `EmitInto` multi-caster merge.
>
> **B4 GPU port — implemented & validated on the RTX 3070.** The parity-first bridge is built:
> `PhotonMap.BuildGrid()` flattens the dictionary hash into a GPU-uploadable **uniform grid** (photons
> sorted into cell order + per-cell `(start,count)` ranges + origin/dims/cell-size over the photons'
> bounding box), and `RayTracer.Core/Gpu/CausticReference.EstimateXyz` gathers over it with a fixed
> loop the shader ports line-for-line. `CausticsTests` pins the grid gather to `PhotonMap.EstimateXyz`
> bit-for-bit over a real dispersive glass-sphere caustic (dense probe sweep) — CPU/GPU caustic parity
> testable in CI, like the other `*Reference` replicas.
>
> On the GPU: `PathTracePhase6.hlsl` gained `TraceCausticEstimate` (the line-for-line port of the
> reference gather) over a photon SRV (`t9`) + cell-range SRV (`t10`), added to the diffuse term
> (`albedo/π · strength`) before the shared correction so the resolve's fog composites it like any
> surface. `Phase6Renderer` grew the two SRVs (dummy when off), two root params, the cbuffer grid
> params, and `SetCausticGrid(grid, radius, strength)` which uploads the flattened grid (each photon's
> wavelength baked to its `DeterXYZ` row). Photon *tracing* runs CPU-side for now (the tested
> `PhotonTracer` against a CPU `BVH`), feeding the GPU gather — the first cut of the port; the grid +
> gather contract is fixed, so the forward trace can later move to a compute pass. The new
> **`--caustic-demo`** (dispersive glass sphere over a lit floor) renders a bright focused caustic on
> the RTX 3070; **`--phase6-selftest` (700/700) and `--phase6-regress` (19 views bit-exact) confirm no
> regression** with caustics off (`CausticEnabled == 0` → the gather early-outs).
>
> **Per-frame rebuild (moving casters) — done.** `--caustic-demo --move` slides the glass sphere and
> rebuilds the photon map every frame (`UpdateSpheres` + `SetCausticGrid` from the caster's new
> position); the caustic **tracks the sphere** across the floor, TDR-free, with dispersive colour
> fringes at its edges (B2). This is the moving-caster showpiece capability the drifting bubbles need.
>
> **Caustics in the product — done, opt-in (off by default).** The shared `Program.BuildJewelCausticGrid`
> helper builds one static caustic grid covering every floating jewel (forward photons from a synthetic
> light under the ceiling above each jewel, through it, onto the floor; each jewel's target box is its
> known cube-on-corner AABB, so scene window glass — also dielectric — is never mistaken for jewel
> geometry). `RunPhase6Windowed` calls it after `Initialize` and on every `Regenerate` **only when the
> "Jewel caustics" config toggle is on** (`AppSettings.JewelCaustics`, default false; skipped in
> Classic/unlit mode). It is off by default because the forward photon trace is a **CPU** pass that
> hitches startup and each regeneration — leaving the default app unchanged (the on-by-default first cut
> disrupted the live bubble stream via that hitch). Validated on the RTX 3070: `--caustic-maze` (all
> jewels, **windows in the scene**, no contamination) renders a vivid wavelength-ordered rainbow;
> `--jewel-demo` shows the crystal plus its floor caustic; `--bubble-maze-demo --caustics` confirms
> bubbles and jewel caustics render together. Moving the photon trace to a GPU compute pass (below) is
> what makes it cheap enough to enable by default.
>
> **Remaining (not yet done):**
> 1. **B2/B3 aesthetic tuning** — a dedicated "Prism Caustics" scene (collimated beam + prism) for the
>    cleanest *wavelength-ordered* rainbow band, and the drifting-bubble iridescent ring.
> 2. **B4 perf — GPU compute photon pass.** The forward photon *tracing* is CPU-side today (fine for
>    stills/static casters, ~per-frame CPU work for movers). Moving it to a compute pass (emit + forward
>    `RayQuery` trace against the live TLAS + deposit) would make real-time moving caustics free and let
>    the maze bubbles cast caustics in the product. The grid + gather contract (`CausticReference`,
>    CI-pinned) is already fixed, so this is a self-contained transport swap behind the same interface.

**Goal:** light that refracts through a jewel/window or reflects off a bubble film should land on
the floor/walls as a **caustic** — a focused bright pattern for glass, a **wavelength-ordered
rainbow** for a dispersive prism, and a **faint iridescent ring** for a bubble. A pure backward
path tracer + point-light NEE **cannot** produce these (the light–specular–diffuse–eye path has a
delta bounce NEE can't connect), so this is a genuinely new transport pass, not a tweak.

### B0 — Choose the transport (recommended: spectral photon mapping)

| Option | Fit | Notes |
|---|---|---|
| **Spectral photon mapping** ⭐ | **Best** | Each photon already *is* a wavelength-carrying ray (`Ray.Wavelength`). Emit photons from lights, trace **forward**, refract with dispersion / reflect off film, store the ones that land on diffuse surfaces, estimate density at shade time. Dispersion → rainbow falls out because each photon's λ sets its bend angle. Maps cleanly to a GPU compute pass + splat. |
| Light tracing / particle tracing w/ next-event-to-camera | Medium | Simpler storage, but connecting to a moving camera fights the tiled accumulation/TAA design; caustics are exactly the paths where camera-connect has low efficiency. |
| Screen-space / caustic-map approximation | Cheap | Good enough for a soft glow, but not a physically-ordered spectrum; loses the "how is it doing that" spectral payoff. Possible Low/Medium fallback. |

Recommend **progressive spectral photon mapping restricted to caustic casters** (jewels, windows,
bubbles — a tiny fraction of the scene), accumulated across frames by the existing
`AccumulationBuffer`/TAA the same way spectral noise is already averaged. Restricting emission to
photons aimed at caster AABBs keeps the photon budget small.

### B1 — Monochrome caustics (glass focus) — prove the pipeline

- **What:** emit N photons/frame from each point light toward the caster set; trace forward through
  `Optics.Refract`/`FresnelDielectric` (reuse the eye-chain logic, run *forward*); on the first
  **diffuse** hit, deposit the photon's energy into a **caustic buffer** keyed to world position
  (a hashed grid / small kd-tree on CPU; a screen-space or texture-space splat on GPU). At shade
  time add a density estimate of the caustic buffer to the surface's direct term.
- Start **non-dispersive** (`CauchyB = 0`, e.g. the clear windows in `MazeWindows.cs`) so the
  focus pattern is a single bright caustic with no colour — easiest to validate energy and
  placement.
- **Effort:** High (new buffer + forward tracer + density estimate). **Verify:** a glass sphere /
  slab under a torch focuses a bright spot on the floor; total deposited energy is conserved
  (photons in ≈ energy out, minus Fresnel/absorption).

### B2 — Dispersive caustics = the prism rainbow

- Turn on Cauchy dispersion for the jewels (`MazeJewels.cs` already sets `cauchyA:1.5,
  cauchyB:0.05`, `SurfaceKind.Dielectric`). Each photon refracts by `n(λ)=A+B/λ²`, so photons of
  different λ land in **spectral order** → a continuous red→violet band on the wall/floor.
- Because photons are single-wavelength, dispersion needs **no special path handling** — it's the
  forward twin of the hero-only eye path (`Optics.IsWavelengthDependent`), and accumulation builds
  the continuous spectrum over frames.
- **Verify:** a white torch beam through a prism lands as a smooth wavelength-ordered gradient;
  angular spread matches `n(λ)`; widening `cauchyB` widens the rainbow.

### B3 — Bubble caustics (faint iridescent ring)

- Bubbles are thin shells: most light transmits nearly straight (already ~see-through), a few
  percent reflects **tinted by `ThinFilmReflectance`** (`PathTracePhase6.hlsl:1099`). Deposit that
  reflected fraction as a photon → a **soft, coloured ring/halo** on nearby walls that shifts with
  the film's gravity-thickness gradient. Faint by construction (low reflect probability), matching
  "vague."
- Bubbles move (`MazeBubbles.Animate`), so their caustics move — a natural showpiece. This reuses
  B1's forward tracer with the bubble reflect branch instead of refraction.
- **Verify:** a drifting bubble casts a faint moving iridescent smudge; colour tracks the same film
  rings the camera sees on the bubble itself.

### B4 — GPU port + budget

- Port the forward tracer to a compute pass (photon emission + forward trace via `RayQuery`, splat
  into a caustic UAV), and the density estimate into the resolve. Mirror in a `*Reference.cs` for
  CI parity. Interleave caustic photons with the path-trace budget; progressive accumulation hides
  the low per-frame photon count during long stills (the slideshow camera in `plan.md` Part C is
  ideal — lots of still frames to accumulate caustics).
- **Verify:** CPU↔reference parity on a prism-caustic scene; `--phase6-selftest`; goldens unchanged
  when no casters/fog present.

### B5 — Presets & knobs

- Add `CausticOptions { bool Enable; int PhotonsPerFrame; float Radius; }` and a
  `CausticQuality` mapped in `RenderPreset`: off on Low/Playable/Medium, modest on High, full on
  Ultra. Add a **"Prism Caustics"** demo preset (skylight beam + prism on a pedestal) and reuse the
  bubble scene for **"Bubble Caustics."**

---

## Phase C — Supporting: area lights & soft shadows (recommended, unblocks "soft"/"vague")

The "something's missing" feeling is partly that **every** shadow is a razor-sharp point-light
shadow. Soft shadows are cheap relative to Phase B and make fog/bubble shadows read as physical.

- **C1 — Area/emissive lights:** extend `Light.cs` with a radius (spherical light) or emissive
  quad; sample a point on the light in NEE instead of the centre (`SelectLight`,
  `PathTracer.cs:524`). Transmittance from Phase A composes directly. Gives **soft shadows** and
  penumbrae for free.
- **C2 — Directional / sun light** for open-roof cells (`design.md` Phase 8/9) — a distant area
  light casts soft parallel shadows and is the natural source for god-rays through fog and for
  prism rainbows under a skylight.
- **Effort:** Low–Medium (mostly reuses existing NEE + Phase A). **Verify:** a sphere over a floor
  shows a soft-edged penumbra that widens with light radius.

---

## Suggested build order

1. **A0 → A1** — transmittance primitive + NEE wiring (CPU). Immediately fixes bubble shadows
   (both the black-disc CPU bug and the no-shadow GPU behaviour) and coloured glass shadows.
2. **A2** — fog self-shadow / god-rays; **A3** GPU port; **A4** presets. Ships "moving fog casts
   shadows" end-to-end.
3. **C1** area lights / soft shadows — small, high perceptual payoff, composes with A.
4. **B1 → B2 → B3** — caustics pipeline: mono focus → prism rainbow → bubble ring (CPU).
5. **B4 → B5** — GPU port + presets for caustics.
6. **C2** sun/sky directional light as the showcase source for shafts + rainbows.

Phases A and C are self-contained wins shippable in days; Phase B is the large, novel piece and
should be scheduled as its own arc.

---

## Cross-cutting concerns

- **Parity is the contract.** Every step lands in CPU `PathTracer.cs`/`VolumetricIntegration.cs`
  first, then the `*Reference.cs` replica, then `PathTracePhase6.hlsl`, with a `GpuPhase6Tests`
  parity test and a `--phase6-selftest`. Don't let CPU/GPU drift (Phase A explicitly *closes* the
  existing bubble-shadow divergence).
- **Keep the fast path.** Transmittance shadow rays keep `ACCEPT_FIRST_HIT` when the shadow ray's
  region has no transmitters and fog is off, so opaque scenes and all current goldens stay
  byte-identical.
- **Determinism & noise.** Forward photons and softened shadows add stochastic noise; keep the
  deterministic RNG/wavelength cycle and let `AccumulationBuffer`/TAA average it. The slideshow
  camera (`plan.md` Part C) gives long stills that converge caustics cleanly.
- **Cost budget.** Publish a rays/sec (and photons/sec) budget per preset as
  `propervolumetricfogplan.md` does; gate everything behind quality tiers.

## Testing & validation

Per-effect physics unit tests, in the style of `SpectralColorTests`/`VolumetricRenderingTests`:

- **A:** transmittance is `1` in vacuum, `0` through an opaque wall, monotonically decreasing with
  fog thickness; a Fresnel-only bubble returns `≈1 − reflectProb`; doubling glass thickness squares
  the transmitted fraction (Beer–Lambert); coloured glass tints the shadow toward its own hue.
- **B:** a forward photon through a prism lands at the Snell/`n(λ)`-predicted position; photons of
  ascending λ land in spectral order; energy conservation (deposited ≈ incident − Fresnel −
  absorption) within tolerance; caustic-off path is byte-identical.
- **C:** penumbra width scales with light radius; a point light (radius 0) reproduces today's hard
  shadow exactly.
- **Regression:** new `Regression/golden` images for a fog-shadow scene, a bubble-shadow scene, a
  prism-caustic scene; goldens for existing scenes unchanged.

## Definition of done

- A lit surface behind moving fog is visibly darkened by the fog, and torchlight shows shafts that
  fall off with fog depth.
- A bubble and a glass object cast **soft, faint, correctly-tinted** shadows (identical on CPU and
  GPU) — no black discs, no missing shadows.
- A prism under a beam throws a **continuous wavelength-ordered rainbow** onto the wall, and a
  drifting bubble throws a **faint moving iridescent ring**.
- All effects gate cleanly through presets; no-effect scenes stay byte-identical; CPU↔GPU parity
  tests and `--phase6-selftest` pass; build + `dotnet test` green.
