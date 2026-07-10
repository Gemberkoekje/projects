# Spectral & Optical Effects Plan

A staged plan for the visual effects that make this renderer worth being *spectral*,
plus the generic reflective/refractive effects (mirrors, water) that every good ray
tracer wants. It complements `design.md` (Arc 2) with concrete, code-level detail
tied to the current engine.

## Why this plan is organized the way it is

The engine already has the one property that is expensive to retrofit: **every ray
carries a single `Wavelength`** (`Ray.cs`), materials come from measured spectral
reflectance (`MaterialsLookup` / the `*_spectral.csv` data), and conversion to sRGB
happens only at display via CIE XYZ (`WavelengthLookup`, `LightCones`).

That means we split effects into two families:

- **Spectral flagships** — effects an RGB renderer *cannot compute correctly* because
  the light path, the emission, or the interference depends on the actual wavelength.
  These are the reason the project exists. Prioritized first.
- **Generic optics** — mirrors, glass, water. Not spectral-exclusive, but they share
  the refraction/Fresnel/recursion plumbing the flagships need, and they carry the
  scene visually. Built alongside the foundation so the flagships have something to
  refract *through*.

Each effect below lists: **what**, **why spectral** (or why generic), **scene hook**
(where it lives in the maze), **implementation sketch** against real types, **effort**,
**dependencies**, and **how to verify**.

---

## Phase 0 — Shared foundation (do this first)

Everything downstream depends on three engine capabilities that don't exist yet.

### 0.1 Transmission & IOR channel on materials and hits

`Tracable.Intersect` currently returns
`(float t, Vector3 location, Vector3 normal, Vector2? UV, float reflectance, float roughness)?`.
There is no way to express "this surface transmits" or "its index of refraction is n".

- Extend `MaterialData` with optical fields: `Transmission` (0 = opaque),
  `IorBase` / `IorDispersion` (Cauchy coefficients `A`, `B` so `n(λ) = A + B/λ²`),
  and per-wavelength `ExtinctionSigma(λ)` for absorption.
- Add a surface-type tag (`enum SurfaceKind { Diffuse, Mirror, Dielectric, ThinFilm,
  Grating, Fluorescent, Emissive }`) so the integrator can branch.
- Decide whether to widen the `Intersect` tuple or return a small `struct HitInfo`.
  Recommend a `HitInfo` struct now — the tuple is already at six fields and every
  flagship adds more.

### 0.2 Refraction + Fresnel helpers

- `Optics.Refract(Vector3 dir, Vector3 normal, float iorFrom, float iorTo)` → refracted
  direction + total-internal-reflection flag (Snell's law).
- `Optics.FresnelDielectric(cosTheta, iorFrom, iorTo)` → reflectance in `[0,1]`
  (Schlick is fine to start; upgrade to full Fresnel for grazing accuracy).
- These are wavelength-agnostic on their own; dispersion enters purely through
  `iorTo = n(ray.Wavelength)`.

### 0.3 The companion-wavelength caveat (critical)

`PathTracer` currently evaluates companion wavelengths **along the hero ray's
geometry** — `compRay` reuses the hero direction and only re-looks-up reflectance
(see `PathTracer.cs` around the companion loop). That reuse is valid for diffuse and
mirror surfaces where the path is wavelength-independent, but it is **wrong for every
flagship effect**, because with dispersion/thin-film/fluorescence the path or the
emitted wavelength depends on λ.

- Add a per-path flag `wavelengthDependentPath`. When a ray interacts with a
  `Dielectric` (with dispersion), `ThinFilm`, `Grating`, or `Fluorescent` surface,
  set it and **fall back to hero-only single-wavelength sampling** for the remainder
  of that path. Accumulation (`AccumulationBuffer`) averages the spectrum over frames.
- This is the single most important design decision here. Build it in Phase 0 so the
  flagships slot in cleanly rather than being bolted on.

**Verify Phase 0:** a plain diffuse scene renders bit-identically to today (regression
guard), and a single refracting quad bends a ray by the Snell-predicted angle in a
unit test.

---

## Phase 1 — Generic optics (foundation payoff)

These reuse Phase 0 directly and give the flagships surfaces to interact with.

### 1.1 Mirrors

- **What:** perfectly/glossy reflective walls. `SurfaceKind.Mirror`.
- **Generic, but with a spectral twist:** metals have wavelength-dependent reflectance
  (complex IOR `n,k`). A gold mirror should tint the reflection warm and a silver one
  neutral, *emerging from the spectral curve* — not a hardcoded tint. Load metal
  reflectance curves the same way materials are loaded today.
- **Scene hook:** mirror panels on select maze walls (design Phase 6). A corridor of
  facing mirrors gives an infinite-reflection showpiece (tests the bounce limit).
- **Sketch:** on hit, reflect `ray.Direction` about `normal`, jitter by `roughness`
  for glossy, spawn a recursive ray, multiply returned radiance by the metal's
  spectral reflectance at `ray.Wavelength`. Cap with the existing bounce depth.
- **Effort:** Low–Medium. **Verify:** a mirror facing a colored wall shows the wall's
  color; gold vs silver mirrors differ under white light.

### 1.2 Glass / clear dielectric

- **What:** transparent solids — a glass slab, window, or bottle. `SurfaceKind.Dielectric`.
- **Sketch:** at a hit, compute Fresnel reflectance `R`; stochastically choose reflect
  vs refract (Russian roulette on `R`), or trace both under a low bounce budget.
  Refract via `Optics.Refract`. This is the prerequisite for the prism (2.1) and water
  (1.3).
- **Effort:** Medium. **Verify:** a glass slab shifts the background laterally by the
  refraction offset; grazing angles go reflective.

### 1.3 Pools / puddles of water

- **What:** reflective + refractive water surfaces on the floor of select cells, from
  small puddles to a flooded chamber. This is the marquee *generic* effect.
- **Ingredients (each is a small, independently visible step):**
  1. **Flat water plane** as a `Dielectric` with `n ≈ 1.33` — Fresnel gives the
     characteristic "reflective at grazing, transparent looking down" behavior for
     free once 1.2 works.
  2. **Ripples / wave normals:** perturb the surface normal with animated procedural
     waves (sum of a few Gerstner/sine waves, or a scrolling normal map). Time drives
     the phase; reuse the existing frame/animation clock.
  3. **Beer–Lambert depth tint:** absorption `exp(−σ(λ)·depth)` through the water
     volume makes it green-blue with depth — this is where it quietly becomes a
     spectral win (see 2.3). Shallow puddles clear, deep pools turn teal on their own.
  4. **Caustics:** refracted rays focused onto the floor create bright wavy patterns.
     With dispersion enabled (2.1) the caustic edges are rainbow-fringed — a caustic
     no RGB renderer edges correctly.
- **Scene hook:** flooded roofless cells reflecting the sky (pairs with Rayleigh 2.5),
  puddles reflecting torchlight (pairs with blackbody 2.4).
- **Effort:** Medium (flat) → High (waves + caustics). Build incrementally; each of the
  four ingredients is a demo on its own.
- **Verify:** reflection of a known object appears mirrored and offset; looking
  straight down shows the floor refracted; ripples animate; deep water darkens toward
  blue-green.

### 1.4 Emissive area lights (supporting)

- Needed by torches (2.4) and for soft shadows. Small emissive quads sampled by NEE.
  Mostly reuses existing light sampling. **Effort:** Low–Medium.

---

## Phase 2 — Spectral flagships

Ordered by impact-to-effort. Each is something an RGB renderer gets wrong or cannot do.

### 2.1 Dispersion — prism, gem "fire", rainbow caustics ⭐ highest priority

- **What:** IOR varies with wavelength (`n(λ) = A + B/λ²`), so each wavelength refracts
  by a different angle. A glass prism splits white light into a **continuous** spectrum
  on the wall; a cut crystal/diamond throws colored sparkle caustics ("fire").
- **Why spectral:** RGB fakes this with 3 fixed rays and produces banding; the "fire"
  of a diamond *is* dispersion and is impossible in RGB. We already have the per-ray
  wavelength — this is mostly `iorTo = A + B/λ²` inside `Optics.Refract`.
- **Scene hook:** a floating faceted crystal as the maze's signature object (a natural
  replacement for the classic screensaver rat/OpenGL logo); a triangular prism on a
  pedestal under a skylight beam.
- **Sketch:** requires 1.2 (dielectric) + 0.3 (hero-only path fallback, since each
  wavelength now takes a different path). On refraction, compute IOR from the ray's
  wavelength. Let accumulation build the spectrum.
- **Effort:** Medium (given 1.2). **Verify:** white beam through a prism lands as a
  smooth red→violet gradient in wavelength order; angular spread matches `n(λ)`.

### 2.2 Thin-film interference / iridescence ⭐ best effort-to-wow

- **What:** soap bubbles, oil slicks on the floor, beetle-shell objects, a CD.
  Color comes from constructive/destructive interference across a thin film:
  `Δφ = 4π·n·d·cosθ / λ`, reflectance peaks where `Δφ` is a multiple of `2π`.
- **Why spectral:** it is a per-wavelength *phase* computation — RGB has no λ to plug
  in. This is the purest demonstration in the whole project.
- **Why easy:** it's a **reflectance modification only** — no new geometry, no path
  changes. Modulate the surface reflectance at `ray.Wavelength` by the thin-film Airy
  reflectance for a given film thickness `d` (constant, or varying by UV/position for
  the swirl of an oil slick).
- **Scene hook:** an oil puddle in a corridor; a soap bubble drifting through an open
  cell; an iridescent scarab as a floating object.
- **Effort:** Low–Medium. **Verify:** varying film thickness sweeps hue through the
  rainbow; viewing angle shifts the color (goniochromism).

### 2.3 Beer–Lambert spectral absorption

- **What:** light through colored glass/liquid attenuates as `exp(−σ(λ)·distance)`
  per wavelength, so thick media shift hue with depth (red glass → deep crimson at the
  thick edge; wine; stained glass; deep water in 1.3).
- **Why spectral:** RGB approximates with three exponents; the continuous
  hue-vs-thickness gradient is the tell, and it falls out for free per-wavelength.
- **Scene hook:** stained-glass windows in roofless cells casting colored light pools;
  a colored-glass slab; ties directly into water depth tint (1.3.3).
- **Sketch:** track distance travelled inside a dielectric between entry/exit hits;
  multiply ray intensity by `exp(−σ(λ)·d)`. `σ(λ)` per material (a few control points,
  interpolated).
- **Effort:** Low (once 1.2 tracks inside-medium distance). **Verify:** a glass wedge
  darkens and saturates toward the thick end; doubling thickness squares the
  transmission.

### 2.4 Blackbody emitters — torches & sun (design Phase 7/11)

- **What:** emission spectrum from a temperature via Planck's law (~1800 K flame,
  ~5800 K sun) instead of a hardcoded RGB color.
- **Why spectral:** correct warm/cool color temperature and correct metamerism between
  torchlight and skylight, emergent from physics.
- **Scene hook:** torch point/area lights in wall pools (Phase 7), the sun for open-roof
  cells; the payoff is the *transition* — warm torch pools vs cool skylight.
- **Sketch:** `Blackbody.Radiance(temperatureK, λ)` (Planck) → an emission spectrum
  sampled at the ray's wavelength; feed into NEE/emissive sampling. Optional
  low-frequency flicker on intensity.
- **Effort:** Medium. **Verify:** a 2000 K vs 6500 K emitter render warm vs neutral
  white with no per-channel tuning.

### 2.5 Rayleigh sky + aerial perspective (design Phase 9)

- **What:** `1/λ⁴` scattering yields a blue sky and orange sunset **from white
  sunlight with no hardcoded colors**, and distant maze walls shift blue on their own.
- **Why spectral:** the canonical "physics for free" spectral result.
- **Scene hook:** open-roof cells (design Phase 8) showing sky; long corridors gaining
  natural aerial haze; pairs with water reflections (1.3) and day/night (2.4).
- **Sketch:** procedural sky returning spectral radiance per direction+wavelength;
  a distance-based Rayleigh extinction/in-scatter term applied along primary rays.
- **Effort:** Medium–High. **Verify:** white sun + Rayleigh → blue zenith, warm
  horizon; sunset reddens as sun elevation drops.

---

## Phase 3 — Exotic showpieces (maximum "how is it doing that?")

Lower priority, highest novelty. Each is a strong standalone demo.

### 3.1 Fluorescence / UV reactivity

- **What:** a material absorbs a short wavelength and re-emits a longer one
  (highlighter ink, tonic water, fluorescent minerals under a "blacklight" torch).
- **Why spectral (impossible for RGB in principle):** you must know the *absorbed*
  wavelength to choose the *emitted* one — a wavelength shift RGB can't represent.
- **Sketch:** `SurfaceKind.Fluorescent` with a reradiation matrix (absorbed λ → emitted
  λ distribution). Requires 0.3 (this path is wavelength-dependent). Add a UV emitter.
- **Scene hook:** glowing marked passages, or wall minerals lighting up under a UV torch.
- **Effort:** Medium–High. **Verify:** under a ~400 nm source, a fluorescent patch
  emits visible green/orange brighter than its reflectance alone allows.

### 3.2 Diffraction grating (CD/DVD rainbow)

- **What:** wavelength-dependent diffraction orders splash a rainbow off a disc.
- **Why spectral:** the grating equation `d·sinθ = m·λ` is per-wavelength.
- **Scene hook:** a CD on the floor catching a torch beam.
- **Effort:** Medium (a specialized BRDF + hero-only path). **Verify:** a white beam
  produces separated spectral orders at grating-equation angles.

### 3.3 Metamerism demo

- **What:** two wall swatches that look identical under the sky but visibly diverge
  under a torch.
- **Why spectral:** the classic spectral "gotcha" only a spectral renderer reproduces.
- **Scene hook:** two adjacent panels; pairs with day/night light switching (2.4).
- **Effort:** Low (data + a light switch), given 2.4. **Verify:** measured swatch pair
  matches under illuminant A and separates under illuminant D65.

### 3.4 Physically-correct chromatic aberration

- **What:** a thick-lens camera model where focus varies with wavelength — real
  photographic CA, not a post-process.
- **Scene hook:** subtle color fringing at frame edges / out-of-focus highlights.
- **Effort:** Medium (camera-side). **Verify:** defocused white points show red/blue
  fringes on opposite sides.

---

## Suggested build order

1. **Phase 0** foundation (transmission/IOR, refract/Fresnel, hero-only path fallback).
2. **1.1 Mirrors** → **1.2 Glass** (proves the recursion + dielectric plumbing).
3. **2.1 Dispersion** (prism) and **2.2 Thin-film** — the two flagship wins, both cheap
   once the foundation exists.
4. **1.3 Water** (flat → ripples → depth tint → caustics), reusing dielectric + 2.3.
5. **2.3 Absorption**, then **2.4 Blackbody** + **2.5 Rayleigh** for the atmospheric arc.
6. **Phase 3** exotics as showcase set-pieces.

## Cross-cutting concerns

- **Performance:** dielectrics and water multiply ray counts. Keep a strict global
  bounce cap; use Russian roulette on Fresnel; lean on the existing
  `AccumulationBuffer`/TAA to hide per-frame spectral noise rather than brute-forcing
  samples.
- **Determinism:** flagships rely on hero-only sampling (0.3). Keep the deterministic
  wavelength cycle so accumulation converges to the correct spectrum; add tests that a
  known spectrum reconstructs within tolerance after N frames.
- **Testing:** every effect gets a unit test on its physics (Snell angle, Fresnel at
  0°/90°, Planck peak wavelength via Wien, thin-film peak spacing, Beer–Lambert
  halving) plus a small reference-image/scene smoke test, matching the existing
  `SpectralColorTests` / `VolumetricRenderingTests` style.
- **Materials data:** extend `materials_data.csv` (or add sidecar files) with optical
  fields (IOR/Cauchy, extinction σ(λ), film thickness, metal n/k) rather than
  hardcoding constants.
