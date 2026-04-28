# Proper Volumetric Fog Plan

## Objective
Implement physically-plausible volumetric fog/smoke in the ray tracer so attenuation and in-scattering are based on **actual traveled distance through participating media**, not a single-point approximation.

This should fix hard transitions and make near objects in thin fog look clearer than far objects in thick fog.

**Status:** Implemented phases 1–4. The main path now uses segment ray marching, presets map to volumetric tiers, the custom panel exposes volumetric quality, and automated coverage was added for attenuation/preset behavior.

---

## Current Limitation
**Done:** The midpoint implementation has been removed from the main path.

Previously, the implementation applied fog by sampling density at one point (midpoint between camera and hit point) and scaling by full path length.

This causes:
- visible boundary artifacts,
- unstable thickness perception,
- non-physical behavior in mixed-density regions.

---

## Target Model (Single Scattering)
For camera ray segment `[t0, t1]` through medium:

- Extinction: `sigma_t(x) = sigma_a(x) + sigma_s(x)`
- Transmittance: `T = exp(-∫ sigma_t ds)`
- Out radiance:
  - `L = T * L_surface + L_inscatter`

Approximate with ray marching:
- Divide segment into `N` steps.
- At each step sample density and accumulate:
  - local transmittance update,
  - in-scattered light contribution.

Use white smoke/fog albedo by default.

---

## Implementation Scope

### 1) Add explicit volumetric settings
**Done:** Added `VolumetricOptions` and `VolumetricQuality`, and wired them through `DenoiseOptions`, `RenderPreset`, calibration, and runtime job-system creation.

Introduce a dedicated options record (or extend `DenoiseOptions` minimally) for volume rendering:

- `EnableVolumetrics` (bool)
- `SmokeMode` (existing enum: None / Biome / AlwaysFog / AlwaysGroundSmoke)
- `MarchSteps` (int)
- `MaxMarchDistance` (float)
- `SigmaScaleFog` (float)
- `SigmaScaleGround` (float)
- `AnisotropyG` (float, optional phase function)
- `InscatterStrength` (float)
- `EarlyOutTransmittance` (float, e.g. 0.01)

Keep defaults conservative for performance.

### 2) Separate medium description from integrator
**Done:** Density functions are separated as `GetDensityBiome`, `GetDensityFog`, `GetDensityGround`, and `GetDensity`.

Keep density functions focused only on density field:

- `GetDensityBiome(p)`
- `GetDensityFog(p)`
- `GetDensityGround(p)`
- `GetDensity(p, smokeMode)`

No color/transmittance logic in these methods.

### 3) Replace midpoint fog with segment integration
**Done:** Replaced `ApplyVolumetricSmoke(...)` with `IntegrateVolumetricSegment(...)`, which marches the camera-to-hit segment, accumulates transmittance and in-scatter, and applies in-scatter once to the final shaded result.

Replace `ApplyVolumetricSmoke(...)` with segment marcher:

1. Determine start `x0 = rayOrigin`, end `x1 = hitPoint`.
2. Compute length `L = |x1-x0|`.
3. Clamp to `MaxMarchDistance`.
4. March `N` fixed/stratified steps.
5. Per step:
   - sample `density` at position,
   - compute `sigma_t = density * sigmaScale`,
   - update cumulative transmittance,
   - optionally sample direct lights for in-scatter (single scattering).
6. Output `(transmittance, inscatter)` and apply once:
   - `L_final = transmittance * L_surface + L_inscatter`.

Important: apply volumetric integration once to the final shaded result per camera ray, not repeatedly to each debug component in ways that can double-count.

### 4) Optional light-aware in-scattering (phase 2)
**Done:** Implemented isotropic phase by default, optional HG phase via `AnisotropyG`, and direct light-aware in-scattering with shadow checks when a BVH is available.

Start with isotropic single-scatter approximation:
- `phase = 1 / (4π)` (constant)

Then optionally add HG phase:
- `HG(cosTheta, g)` for forward/backward scattering control.

### 5) Boundary softness by construction
**Done:** The existing biome density blend remains small and continuity now primarily comes from integrating density along the ray segment.

Do not rely on biome-edge smoothing hacks as primary solution.

If needed, keep a **small** density blend zone at biome borders, but let integration handle most visual continuity.

---

## Preset Strategy (important)
Use presets to balance quality and cost.

### Existing quality presets
- **Low**
  - **Done:** volumetrics off (`SmokeMode.None`)
- **Medium**
  - **Done:** volumetrics on
  - **Done:** 8 steps, modest sigma
- **High**
  - **Done:** volumetrics on
  - **Done:** 16 steps, better in-scatter
- **Ultra**
  - **Done:** volumetrics on
  - **Done:** 24 steps, HG phase, light-aware inscatter

### Debug smoke presets
- **Fog Debug**
  - **Done:** `SmokeMode.AlwaysFog`
  - **Done:** same marcher, higher `SigmaScaleFog`
- **Ground Smoke Debug**
  - **Done:** `SmokeMode.AlwaysGroundSmoke`
  - **Done:** same marcher, higher near-ground density + shorter march distance

### Custom panel additions
Expose minimally:
- **Done:** Smoke mode (already present)
- **Done:** Volumetric quality (Off/Low/Medium/High/Ultra) mapped internally to step count + sigma scales

Avoid exposing too many raw physical constants in UI.

---

## Performance Plan

1. Early exit:
   - **Done:** if `SmokeMode.None` return original color
   - **Done:** if `L <= epsilon` return original color
   - **Done:** stop marching when `T < EarlyOutTransmittance`

2. Adaptive quality:
   - **Done:** when `jobSystem.IsMoving == true`, reduce step count by half
   - **Done:** when stationary, restore full step count

3. Cache-friendly math:
   - **Done:** precompute step length
   - **Done:** avoid allocations inside march loop

4. Keep scalar math predictable for CPU branch behavior.

---

## Validation & Testing

### Visual acceptance
- Near wall in fog remains visible; far wall fades significantly.
- No hard line where smoke “starts”.
- Ground smoke obscures floor strongly but does not fill entire ceiling unless configured.

### Automated checks
Add/extend tests for:
- **Done:** monotonic attenuation with distance,
- **Done:** zero-density returns unchanged radiance,
- **Done:** high-density long path strongly attenuates,
- **Done:** `SmokeMode.None` exact bypass,
- **Done:** preset mapping sets expected volumetric step counts.

### Performance checks
Measure rays/sec impact per preset and set budget:
- Medium: acceptable drop (target <= ~25–35%)
- High/Ultra: higher cost acceptable with expected quality gain

---

## Rollout Plan

### Phase 1 (safe refactor)
- **Done:** Introduce volumetric settings structure.
- **Done:** Move current smoke logic behind new API.

### Phase 2 (proper integration)
- **Done:** Implement segment marcher with transmittance + simple in-scatter.
- **Done:** Remove midpoint approximation path.

### Phase 3 (preset tuning)
- **Done:** Tune step counts/sigma per preset.
- **Done:** Tune Fog Debug and Ground Smoke Debug to remain clearly demonstrative.

### Phase 4 (optional quality upgrades)
- **Done:** HG phase function
- **Done:** light-aware single scattering
- **Not done:** temporal stabilization for volumetric noise. Current marcher is deterministic fixed-step, so no volumetric stochastic noise was introduced that requires temporal stabilization.

---

## Definition of Done
- Midpoint fog approximation removed from main path.
- Camera-to-surface fog behavior is distance-consistent.
- Presets map to predictable volumetric quality tiers.
- Debug smoke modes remain available and visually distinct.
- Automated volumetric tests added. Build/test status should be checked after implementation.
