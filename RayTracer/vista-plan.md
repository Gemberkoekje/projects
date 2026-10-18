# Vista — a Spectral Path-Traced Landscape Wallpaper Generator

The single source of truth for the landscape-wallpaper effort. This is a **planning
document** — no code yet. It sits alongside the engine's own `plan.md` (the RayTracer
remaining-work tracker) and `pinball-plan.md`, reuses the same engine foundation, but
deliberately departs from the engine's CPU-parity convention (§9). "Vista" is a working
name.

Everything below was settled in a brainstorm on 2026-10-17/18; the decisions are
collected in the appendix so they don't have to be re-litigated.

---

## 1. Vision

A generator for **photoreal landscape wallpapers**: distant mountains, hills, forests,
lakes, waterfalls and cabins, but also foggy forest paths and moonlit seascapes —
rendered **offline** by our own **spectral path tracer** at wallpaper resolutions (4K
first, eventually 8K, 16K, ultrawide, multi-monitor spans and phone portrait).

The reason to build it ourselves rather than use Terragen or Blender is the point of
the project: our own engine, our own settings, and **genuinely spectral** — colour
comes from measured spectra and physics, so "it is spectral" is literally true, not a
marketing line.

The workload is the mirror image of the maze. The maze is real-time, maze-scale and a
few thousand quads, converging for a screensaver. Vista is **stills only**, offline,
with enormous geometry, 50 km sight lines, and minutes to hours per frame. The engine's
spectral machinery, DXR backend and progressive-accumulation viewport carry over; the
integrator and the whole scene side are new.

**North star:** the recipe **"autumn lake, dawn, fog"** — a lake on a cold autumn dawn,
steam fog rising off the water, autumn colours from real leaf pigments, snow-dusted
peaks catching alpenglow behind. Milestones M1–M5 (§10) build toward it.

---

## 2. Design pillars & non-goals

### Pillars

1. **Spectral-native, from measured data.** Leaf, rock, soil, snow, ice and water
   colours come from measured spectra where they exist, from physical models fitted to
   measured data where they don't (PROSPECT-D for autumn leaves), and from a built-in
   fallback only as a last resort — and every render records which tier each material
   used (§6).
2. **Photoreal is the reference.** Anything stylised (film looks, painterly grading)
   is a camera setting or filter applied on top of a physically rendered image, never a
   change to the physics.
3. **Procedural purity.** No meshes, textures or HDRIs from outside. Terrain, trees,
   cabins and materials are generated. Measured physical data tables (spectra, a star
   catalogue, climate normals) are allowed: they are constants of nature, like the CIE
   tables already in `RayTracer.Core/Data/`, not assets.
4. **The world is a function of (recipe, seed).** Everything the user cannot change is
   deterministic. The camera decides only how much of the world is evaluated and at
   what detail (§4.1). Same recipe + seed + camera + sliders + data tiers → the same
   image, months later.
5. **Shot first.** The recipe plants an intended viewpoint and builds the scene around
   it, so "autumn lake" reliably produces an autumn-lake view (§4.2).
6. **Fit the RTX 3070 (8 GB).** VRAM, not compute, is the binding constraint. Prefer
   procedural materials (shader time) over textures (memory), instancing over unique
   geometry, and warn before a render that won't fit (§7).
7. **Honest estimates.** Before a final render the user sees how long it will take and
   whether it fits in memory, measured on this scene and this camera, not guessed (§7.2).

### Non-goals

- **No animation, no live wallpapers.** Stills only. Temporal stability, motion blur
  and simulation over time are a different beast and out of scope.
- **No scenery editing.** The user cannot move mountains, place trees or edit terrain.
  Camera adjustments are the remedy for "a bush in the wrong place" (§3.1).
- **No imported assets.** No glTF importer, no Megascans, no HDRI skies (pillar 3).
- **No CPU port of the renderer.** A CPU path tracer for a 100M-triangle forest has no
  value; see §9 for what replaces the parity discipline.
- **No changes to the maze's behaviour.** Shared engine code changes are additive and
  gated; maze goldens and self-tests stay green (§9).

---

## 3. User experience

### 3.1 Workflow

```
Recipe (building blocks + basic sliders) + seed
  → world generation   terrain, erosion, hydrology, snow, ecosystem, props (cached per seed)
  → auto camera        score a few hundred candidate views near the planted viewpoint
  → preview render     ~320 px wide, same quality settings as the final (§7.4)
  → tweak loop         nudge / pan / tilt / focal length / focus / exposure / sliders;
                       the progressive preview restarts and refines after each change
  → estimate           time + VRAM for the chosen target, resolution and detail level
  → final render       batched, resumable, live ETA → EXR + PNG + sidecar
```

The tweak viewport is where the engine's progressive accumulation pays off: move,
accumulation resets, the image converges again. Camera adjustments are expected to be
small ("the generator put a bush in the wrong place — move five feet left"), so the
tools are modest: nudge in metres, small pan/tilt/roll, focal length, focus distance,
aperture, exposure. The camera has collision: it cannot end up underground, underwater
or inside a tree.

### 3.2 Basic controls

- **Building blocks**, combined freely — e.g. "frozen lake + snow-capped mountains +
  dawn":
  - *terrain*: snow-capped mountains, alpine range, rolling hills, coast, forest;
  - *water*: lake, frozen lake, river, waterfall, sea, none;
  - *time*: dawn, day, golden hour, dusk, night;
  - *season*.
- **Sliders:** season, time of day, snow, fog.
- **Seed** (plus "new seed").
- **Camera:** auto camera, nudges, focal length.

### 3.3 Advanced controls

For fine-tuning a scene to look like a specific place at a specific time:

- **Place:** location dropdown or custom latitude (§3.5), altitude, date.
- **Weather:** temperature (°C, with a small °C/°F toggle), humidity.
- **Lens & exposure** details (reusing the engine's `LensOptions`: f-stop, focus, lens
  presets, ISO/shutter once the exposure triangle exists).

### 3.4 Linked sliders

One generic mechanism, not special cases:

- Every slider is either **auto** (derived from the controls upstream of it) or
  **pinned** (the user moved it). A reset button re-links it.
- Pinning one slider only stops that slider: pinning temperature still lets snow and
  fog follow from the pinned value.
- **Building blocks push defaults.** Picking "frozen lake" drops the auto temperature
  below zero; picking "dawn" sets time of day.
- **Contradictions are allowed.** A pinned 25 °C next to a frozen lake renders as asked
  (it's the user's picture) with a small "implausible" hint, never a block.
- **Two speeds.** Time of day, fog, exposure and camera changes only restart the
  preview. Snow and season (snow depth near the camera, snow on branches, bare
  deciduous trees) change near-camera geometry and rebuild part of the scene — seconds,
  not instant — still deterministic from seed + slider, so nothing moves.

### 3.5 Location & astronomy

- **Real astronomy:** sun and moon position, moon phase and the star field from
  latitude, date and time (Meeus-style algorithms / the NOAA solar position algorithm).
- **Location dropdown** of large places and iconic landscapes so nobody has to look up a
  latitude: e.g. Yellowstone, Banff, Yosemite, Lofoten, Iceland, Scottish Highlands,
  Zermatt, Dolomites, Torres del Paine, Fiordland, Hokkaido — plus "custom latitude".
  The location sets **only the sky and the climate defaults**; the recipe decides what
  the terrain looks like.
- **Time of day is local solar time**, or relative to sunrise/sunset ("dawn" = sunrise
  minus ~20 minutes). Longitude and time zones drop out.
- **Season and date drive each other.** "Autumn" at Torres del Paine means April
  (southern hemisphere), so the sun path always matches the season. An explicit date
  override stays available (advanced). A finer "early / peak / late" control sets the
  leaf pigment state.

### 3.6 Weather defaults chain

Temperature and humidity are invisible themselves; they are advanced controls that set
plausible defaults for visible effects:

```
location + season/date + time of day
  → temperature at the valley floor           [auto | pinned]   (advanced)
  → humidity                                  [auto | pinned]   (advanced)
      → freezing level (≈ 6.5 °C per 1000 m lapse rate)
          → snow default (with season)        [auto | pinned]   (basic)
          → snow state by altitude: dry / wet / melting
          → frost on grass and leaves at a cold dawn
      → lake water (season-lagged) vs. air temperature → steam fog over water
      → clear cold night → valley inversion fog / sea of clouds
      → warm + humid → whiter haze
  → fog / haze                                [auto | pinned]   (basic)
```

- **Climate defaults per location:** each dropdown place carries typical monthly lows
  and highs from public climate normals (Lofoten is much milder than its latitude
  suggests); a daily curve puts the minimum around sunrise and the maximum mid-afternoon.
  Custom latitude falls back to a rough latitude model.
- **Snow cover is history, temperature is now.** Temperature nudges the snow default
  via the freezing level but does not decide it; season does most of that.
- **Glaciers and permanent snowfields belong to the recipe**, like terrain. The snow
  slider adds seasonal snow on top; at zero only the glaciers remain.
- **Altitude** (advanced) also changes the sky: thinner air gives a deeper blue, less
  haze and a brighter sun — the spectral atmosphere with less air above, at no extra
  cost.

---

## 4. World generation

### 4.1 Determinism: the world is a function of (recipe, seed)

- All scenery is a pure function of the recipe and seed. The camera decides **how much**
  of the world is evaluated and at **what detail**, never what is there.
- Consequences: the 320 px preview and the 16K final show the same world at
  different detail; moving the camera never reshuffles anything; a recipe + seed +
  camera file reproduces the image.
- **Erosion is the exception to "pure function of position"** — it is a global
  simulation. So erosion runs on a **fixed-resolution global grid** (deterministic,
  independent of the camera), and fine detail near the camera comes from functions of
  position only (seeded noise, local rules), so it is identical whichever camera asks.
- Detail scales with output resolution (geometry is tessellated to a screen-space error
  in pixels), so a 16K final is denser than the preview. That is expected and is why the
  estimator measures final-detail crops (§7.2).

### 4.2 Shot-first generation

The recipe plants an intended viewpoint and view direction first, then builds the world
around it: a lake basin in front of the viewpoint, a mountain backdrop on the far side
of the lake for "snow-capped mountains", a clearing or path for "forest path", a cliff
edge for "coast". The auto camera then refines from the planted viewpoint by scoring
candidates (§4.8). The user can still nudge the camera afterwards; the world does not
change.

### 4.3 Terrain & erosion

- **Base shapes:** fBm, ridged multifractal, domain warping; per-building-block terrain
  styles (glacial U-valley, alpine range, rolling hills, coastal cliffs).
- **Erosion is the realism lever:** hydraulic (droplet or grid-based) and thermal
  erosion as GPU compute shaders, producing the height field plus flow, deposition and
  wetness maps that later stages reuse (rivers, lakes, snow, vegetation).
- **Geometry:** a 16k² height field as triangles (~500M) does not fit in 8 GB. Because
  the camera is fixed for a still, build a **view-dependent mesh once per camera** to a
  screen-space error target, conservative for off-screen geometry that still casts
  shadows or appears in reflections. Alternative for far terrain: a custom height-field
  intersection (min/max quadtree) in the inline ray query loop. Decide in M1 by measuring.
- **Materials by rule:** rock strata, scree, soil, grass, moss, snow, wet vs. dry —
  placed by slope, altitude, aspect, erosion flow and moisture.

### 4.4 Hydrology & water bodies

- Lakes and rivers placed from the erosion flow map, with the shot-first basin for the
  "lake" building block.
- Shorelines get wetness bands; lakes get a depth field for absorption.
- Waterfalls need a step in the river profile — planted by the recipe, carved into the
  terrain.

### 4.5 Snow & glaciers

What makes snow-capped mountains read as real is **where the snow sticks**:

- **Slope:** snow slides off faces steeper than roughly 35–40°, so dark rock faces stay
  between white ledges and gullies (the "striped" look, not icing).
- **Aspect:** poleward faces keep more snow (north faces in the northern hemisphere).
- **Wind & curvature:** ridges are scoured bare, hollows and lee sides fill (erosion
  curvature maps).
- **Altitude:** the snowline follows the freezing level and the snow slider (§3.6).
- **Glaciers / permanent snowfields:** recipe-owned, carved with the terrain.
- **Snow state:** dry powder high, wet and greyer near the freezing level, patchy melt
  below — the material changes spectrally too (grain size, liquid water).
- Close-up snow (depth as geometry, snow on branches, sparkle, subsurface scattering)
  arrives with the winter milestone (M7).

### 4.6 Ecosystem & vegetation

- **Placement:** Poisson-disc distributions with rules for slope, altitude, moisture,
  aspect, treeline and distance to water; ground cover (grass, ferns, shrubs, rocks,
  leaf litter) dense near the camera.
- **Trees:** procedural species (conifer, birch, maple, oak, …) via space colonization
  or L-systems; seasonal variants (pigment state, leaf-off in winter).
- **Leaves:** real geometry vs. alpha-tested cards is a measured decision. Alpha needs
  non-opaque candidates in the ray query loop (slow on Ampere); real geometry costs
  memory. The 3070 has neither SER nor hardware opacity micromaps (RTX 40-series
  features).
- **Leaf translucency** (thin-surface transmission from the measured/modelled
  transmittance spectrum) — what makes backlit forests glow.
- **Scale via instancing:** millions of TLAS instances of a few dozen unique plant
  meshes (~64 bytes per instance). The engine's TLAS today holds only a handful of
  instances (static, sphere, dynamic parts), so mass instancing is new work.
- **Distant forests** may need impostors or a volumetric canopy representation — a
  measured decision in M5.

### 4.7 Props

- **Cabins** from a shape grammar: stacked logs, roof, chimney, porch, windows with a
  warm blackbody interior glow (~2700 K) at dusk.
- **Docks, rowboats, paths, fences**, placed by the shot-first layout.

### 4.8 Auto camera & composition scoring

Procedural generators produce a lot of mediocre images; this is the main mitigation
alongside shot-first generation.

- Candidates sampled around the planted viewpoint, scored on: horizon near a third,
  foreground / midground / background layering, leading lines (shores, paths, rivers),
  sun angle (side or back light at golden hour), sky fraction, required features
  visible (the lake really is in frame), no trunk or rock blocking the lens.
- Scoring uses cheap low-resolution renders plus the G-buffer (depth, normals, material
  ids).
- Per aspect ratio once multiple targets exist (M11): the same composition is re-scored
  for 21:9 or 9:19.5 framings, with safe-frame overlays in the tweak viewport.

---

## 5. Rendering

### 5.1 Offline integrator

A new integrator shader, not an extension of `PathTracePhase6.hlsl` (which stays the
maze's real-time path):

- Full multi-bounce path tracing with Russian roulette and multiple importance sampling
  (MIS).
- Importance-sampled sun and sky.
- Volumetric path tracing with next-event estimation inside media (fog, clouds).
- Hero-wavelength spectral sampling as in the engine.
- Fixed-seed, deterministic sample streams per pixel.
- Short GPU dispatches (sample batches / tiles) so Windows' GPU timeout (TDR, ~2 s by
  default) never fires.
- Adaptive sampling against a noise target, using per-pixel variance (the engine's
  Welford approach).

### 5.2 Atmosphere

- Rayleigh + Mie haze + **ozone absorption** (the Chappuis band is why blue hour is
  blue), with **multiple scattering** (Bruneton-style precomputation or Hillaire 2020
  LUTs for speed; brute-force spectral path tracing of the atmosphere as the reference).
- **Aerial perspective** on every primary and secondary ray: the layered, receding blue
  ridges. Listed as unbuilt in the engine's `plan.md` (O3).
- Altitude-dependent density (scene altitude is an advanced control, §3.6).
- Emergent, not special-cased: **alpenglow** on snowcaps at dawn and dusk, the **Belt
  of Venus**, the reddened low sun.
- Replaces the engine's single-scattering Rayleigh `Sky` for Vista (which stays as the
  M0 placeholder).

### 5.3 Fog & clouds

- **Fog:** altitude-based density, valley inversion fog, steam fog over warm water,
  humidity haze — extending the engine's shipped volumetric fog (segment marching,
  3D-noise turbulence, self-shadowing god rays).
- **Clouds:** heterogeneous volumes with delta/ratio tracking and multiple scattering
  (silver linings and dark bases need it).
- **Spectral Mie phase** for water droplets (e.g. the Jendersie & d'Eon 2023
  approximation, or tabulated Lorenz–Mie): fogbows, glories, and **rainbows in waterfall
  spray** — showpieces only a spectral renderer gets honestly.

### 5.4 Water

- **Lakes:** Fresnel, mirror reflections broken by light ripples, depth absorption
  from the pure-water absorption spectrum (turquoise shallows, blue depths), dissolved
  organics (bog-brown) or glacial flour (milky alpine) as recipe variants, wet shores.
  Builds on `MazeWater` (flat dielectric) and the engine's Beer–Lambert absorption.
- **Frozen lake:** ice (ice absorption spectrum, cracks, bubbles) with snow on top from
  the snow slider.
- **Ocean:** FFT wave spectrum (Tessendorf / JONSWAP), foam where waves pinch (the
  Jacobian), whitecaps.
- **Moon glitter path:** thousands of small specular highlights from wave normals —
  noisy for a path tracer, needs good MIS between the moon as a light and the glossy
  water.
- **Waterfalls:** no fluid simulation for stills — a shaped flow surface with a
  whitewater scattering material, a mist volume at the base, and spray.

### 5.5 Night

- **Moon:** position and phase from astronomy (§3.5); spectrum = solar spectrum × measured
  lunar reflectance (slightly redder than sunlight).
- **Stars:** from a real catalogue (e.g. the Yale Bright Star Catalogue: colour index →
  blackbody temperature) or a procedural field as fallback; procedural Milky Way and
  airglow.
- **Purkinje / mesopic display transform:** simulate rod vision at low light levels, so
  moonlit scenes look blue *because of* human vision, not because of a tint.

### 5.6 Camera, exposure & filters

- Reuse the engine's lens models (`LensOptions` / `LensSampler`: pinhole, phone,
  portrait, simple, cine; physical f-stop, focus distance, polygonal iris).
- Exposure triangle (aperture ↔ shutter ↔ ISO) — listed in the engine's `plan.md` §4
  ideas; Vista needs it for real exposure.
- **Filters as camera settings** (pillar 2): spectral sensor response curves and film
  stocks instead of LUTs; real white balance as chromatic adaptation against the scene
  illuminant; lens chromatic aberration from the dispersion IOR.

### 5.7 Output

- **Denoising:** Intel Open Image Denoise (Apache 2.0, C API via P/Invoke, CPU or GPU),
  fed albedo and normal buffers.
- **Formats:** EXR (scene-referred, for the user's own grading) plus PNG/JPEG through a
  filmic tonemap (AgX- or ACES-style).
- **Sidecar** next to every image: recipe, seed, camera, all slider values (auto or
  pinned), data tier per material (§6.2), sample counts, render time, engine version.
- **HDR wallpapers** (e.g. JXR on Windows 11) — to verify before committing to it (M11).

---

## 6. Spectral data

### 6.1 Where colour comes from

| What | Source |
|---|---|
| Green leaves | Measured R+T (LOPEX93, ANGERS) and PROSPECT-D |
| Autumn leaves | **PROSPECT-D** (chlorophyll ↓, carotenoids revealed, anthocyanins ↑): no public measured autumn-leaf dataset found as of 2026-10 |
| Bark, dead leaves, soil, rock, minerals | Measured libraries (USGS splib07, ECOSTRESS) |
| Snow & ice | Measured spectra / ice optical constants with grain size |
| Water | Pure-water absorption spectrum (e.g. Pope & Fry), plus organics / glacial flour |
| Sky | Rayleigh + Mie + ozone absorption cross-sections, solar spectrum |
| Moon, stars, fire | Lunar reflectance × solar spectrum; blackbody (existing `BlackbodySpectrum`) |
| Fallback for anything | Analytic fits we author, or the existing `RgbToReflectanceBasis` upsampling |

PROSPECT-D is not a made-up approximation: it is a radiative-transfer leaf model whose
absorption coefficients were fitted to measured leaf datasets, and it yields reflectance
**and** transmittance for any pigment mix — which is what makes the season slider a
pigment slider. It is validated against the measured green-leaf spectra so it cannot
drift.

### 6.2 Data tiers

The repo holds a **manifest**, not the data: per dataset the source URL, expected
checksum, licence and citation notes, and which material keys it provides.

```
1. Local cache       %LOCALAPPDATA%\Vista\spectra, converted to the engine's wavelength grid
2. Download          on first use (or "fetch all"), with progress; checksum must match
3. Manual import     sources behind an order form: drop the file in the cache folder
4. Model             PROSPECT-D for leaves (its coefficient tables also downloaded + cached)
5. Built-in          always available, no network: analytic fits (pigment absorption
                     bands, water and ice absorption curves) or RGB upsampling
```

- Every render records the tier used per material in its sidecar.
- **"Require measured data"** switch, **off by default**: when on, a render refuses to
  fall back below tier 3 instead of silently degrading.
- **CI never touches the network:** tests run against tier 5 and small hand-made sample
  files.
- If a source ever disappears or gets walled off, renders keep working on tiers 4–5;
  the sidecar says so.

### 6.3 Licensing & citations

The repo is public, so nothing restricted is committed — data is downloaded to the
local cache, never redistributed.

- **USGS:** US government data, generally public domain — confirm on the data release
  page.
- **ECOSTRESS:** "Copyright © 2017, California Institute of Technology. ALL RIGHTS
  RESERVED", no open licence found; the download goes through a cart / order form, so
  it is **manual import (tier 3) only**.
- **LOPEX93, ANGERS, PROSPECT coefficients, star catalogue, climate normals:** access
  route and terms unverified — the first task of M2.
- Citations the libraries ask for go on an About / credits screen.

### 6.4 Wavelength grid

All tables are resampled once (into the cache) onto the engine's wavelength grid, so the
integrator only ever sees one grid. The visible range drives the image; measured data
outside it is dropped at import.

---

## 7. Performance, memory & estimation

### 7.1 VRAM budget (RTX 3070, 8 GB)

Rough budget for a 4K render with forest — **guesses to verify by measurement in M1/M5**:

| Item | Budget |
|---|---|
| Image buffers at 4K (accumulation, variance, albedo, normal) | ~0.8 GB |
| Terrain, view-dependent mesh | ~1–1.5 GB |
| Vegetation: ~50 unique plant meshes + millions of instances | ~1–1.5 GB |
| Cloud / fog density grids | < 0.1 GB |
| Textures | ~0 (procedural, evaluated in the shader) |
| OS, desktop, headroom | ~1–1.5 GB |

One RGBA32F buffer is ~133 MB at 4K, ~531 MB at 8K and ~2.1 GB at 16K, so image buffers
alone reach ~3 GB at 8K and ~13 GB at 16K: **tiled rendering** arrives with the larger
targets (M11).

### 7.2 Render-time estimate

Like the maze's `PerformanceCalibrator`, but per scene and per view, because cost varies
too much between views (a forest floor vs. open sky):

- **time ≈ pixels × spp × rays per sample ÷ rays per second**, where rays per sample
  and rays per second are **measured** for this scene and this camera.
- The preview measures them at preview detail. For the final, the estimator renders a
  handful of random crops at the **true final resolution and detail level** for a few
  seconds and extrapolates. It shows a range, not a single number.
- **Fixed spp** gives a predictable time; a **noise target** gives an estimate from the
  preview's measured variance.
- **VRAM estimate** alongside: warn before starting, never crash at 80%.
- The ETA refines live during the final render as tiles complete.

### 7.3 Batching, tiling, resumability

- Sample batches per dispatch stay well under the TDR limit.
- Large targets render in tiles with their own buffers; tiles are written to disk as
  they finish, so a render can resume after a crash or reboot.

### 7.4 Preview renders & the agent iteration loop

A final render can take an hour or more. Nobody, human or LLM, can iterate on look-dev
at that pace, so a small preview render is a first-class mode from M0:

- **Size:** ~320 px wide at the target's aspect ratio (320×180 for 16:9, 320×240 for
  4:3), so the composition matches the final. Configurable.
- **Same quality settings as the final:** bounces, spp, volumes, data tiers. Only the
  pixel count changes. That is ~100–150× fewer pixels than 4K, so a 90-minute 4K
  render becomes well under a minute of tracing at the same quality.
- **Headless CLI:** e.g. `Vista.App --preview --recipe r.json --seed N --camera c.json
  --out p.png` writes the PNG plus its sidecar (timings, rays traced, data tiers). An
  LLM or a script can render, look at the PNG, change one parameter, and render again.
- **Watch mode:** a long-running process keeps the device, compiled shaders, world and
  acceleration structures alive and re-renders whenever the recipe, camera or slider
  file changes. Only the stages a change touches rebuild (the two speeds of §3.4), so
  the loop isn't dominated by setup.
- **Loupe (crops at final detail):** a 320 px preview hides fine detail: leaf geometry,
  snow edges, aliasing, fireflies. A loupe renders a small window of the
  final-resolution image at final geometric detail, so detail can be checked without
  rendering the whole frame.
- **Contact sheets:** several variants (seeds, slider values, before/after) rendered
  into one labelled grid image, so one look compares them.
- **Debug outputs at preview size:** albedo, normals, depth, material ids, a data-tier
  map, a sample/variance heatmap, and raw plus denoised beauty, for diagnosing rather
  than guessing.
- **Cheap goldens:** regression goldens at preview size keep the golden suite fast.
- **First calibration:** preview measurements feed the render-time estimate (§7.2).
- **Needs the GPU:** rendering requires the RTX 3070, so the agent loop works in a local
  session on the dev box; cloud sessions and CI can build and unit-test but not render
  (§11).

---

## 8. Engine reuse & project structure

### 8.1 Reuse

Milestone E of the pinball plan already extracted the engine from the maze, so Vista
sits on the same seam as `RayTracer.Maze` and `Pinball.App`:

- **From `RayTracer.Core`:** `WavelengthLookup`, the CIE tables in `Data/`,
  `BlackbodySpectrum`, `LensSampler` / `LensOptions`, `RgbToReflectanceBasis`,
  `SpectralResources`; `Sky` and `DayNightCycle` as M0 placeholders only.
- **From `RayTracer.Gpu`** (the maze-free DXR 1.1 backend library): device creation,
  `ShaderCompiler`, acceleration-structure building, readback. Exactly which pieces can
  serve an offline integrator (rather than `Phase6Renderer`'s real-time loop) is
  **to be confirmed in M0**; anything Vista needs changed lands additively.

### 8.2 New

Recipes and the linked-slider model; world generation (terrain, erosion, hydrology,
snow, ecosystem, trees, props); mass instancing; the offline integrator and its compute
shaders; the full atmosphere; clouds; spectral data tiers; astronomy and climate
defaults; auto camera; estimator; tiling; OIDN; EXR output; the Vista app UI.

### 8.3 Project layout

| Assembly | Role | Depends on |
|---|---|---|
| **`Vista.Core`** *(new, net10.0)* | Pure, testable: recipes, linked sliders, seeded world generation, astronomy, climate defaults, spectral data tiers and material models, camera scoring, estimator. No GPU, no WinForms. | Core |
| **`Vista.App`** *(new, net10.0-windows)* | WinForms recipe picker, preview viewport, camera tweaking, render queue; the offline integrator and compute shaders (erosion, tessellation); tiling; OIDN; EXR/PNG output. | Core, Gpu, Vista.Core |
| **`Vista.Tests`** *(new, net10.0)* | Unit tests for everything in `Vista.Core`; small C# references for shared shader math (§9). | Vista.Core |

All three join `RayTracer.slnx`, so `ci-raytracer.yml` builds and tests them with no
workflow change. `Vista.Core` / `Vista.Tests` stay plain net10.0 so tests don't drag in
WinForms or DXR.

---

## 9. Working conventions

Vista inherits some of the engine's disciplines and deliberately drops one:

1. **No CPU port of the renderer** (agreed 2026-10-17). The engine's CPU → C# reference
   → HLSL lockstep does not carry over: the project outgrows a like-for-like CPU
   pipeline. Instead:
   - the math pieces — erosion steps, phase functions, PROSPECT, spectra resampling,
     astronomy, placement rules, linked defaults, the estimator — live in `Vista.Core`
     with unit tests;
   - where a function exists in both C# and HLSL (e.g. a phase function), keep a small
     C# reference and a parity test for that function only;
   - the GPU renderer is validated with fixed-seed golden images on the 3070.
2. **Don't move the maze.** Vista never changes the behaviour of `RayTracer.Core` /
   `RayTracer.Gpu`: shared changes are additive and gated, `--phase6-selftest` stays
   700/700, `--phase6-regress` stays 19/19 bit-exact, and the maze goldens don't move.
3. **Spectral-native, measured first.** Measured data → model → built-in fallback, and
   the tier is always recorded. Never a hard-coded tint.
4. **Photoreal first.** Stylisation only as camera settings / filters on top.
5. **Determinism.** Recipe + seed + camera + sliders + data tiers → the same image on the
   same machine. Goldens fix the seed (headless captures without a seed are
   incomparable, as in the maze). Driver updates may require re-baselining goldens.
6. **CI never needs a GPU or the network.** CI builds the DXR host and runs the
   `Vista.Core` tests on tier-5 data; GPU goldens run only on the local RTX 3070.
7. **Procedural purity.** No external meshes, textures or HDRIs; measured physical data
   only via the manifest and cache (§6).
8. **Measure before deciding.** Mesh LOD vs. height-field intersection, leaf geometry vs.
   alpha cards, forest impostors vs. volumes: each is decided by a measurement on the
   3070, recorded in this file.
9. **Look-dev in previews.** Visual changes are iterated with preview renders, loupes
   and contact sheets (§7.4); a final render confirms, it is not an iteration step.

---

## 10. Roadmap

Each milestone: **goal → deliverable → verification.** The proof of concept is M1, at a
single target of **3840×2160**.

**M0 — Skeleton & seam.**
Goal: an end-to-end offline render from a recipe + seed. Deliverable: `Vista.Core` /
`Vista.App` / `Vista.Tests` in `RayTracer.slnx`; confirm which `RayTracer.Gpu` pieces
serve an offline integrator (§8.1); recipe + seed model with serialization; a
height field → mesh; integrator v0 (multi-bounce, sun + the existing Rayleigh `Sky`
placeholder); TDR-safe sample batches; ray counter; EXR + PNG writer; sidecar; the
headless preview CLI with debug outputs (§7.4). Verify: the same recipe + seed + camera
renders bit-identically twice on the 3070; a 320 px preview renders headless and its
sidecar reports timings; recipe / seed determinism tests green in CI; maze goldens
unmoved.

**M1 — Proof of concept: "alpine range at golden hour" (3840×2160).**
Goal: Terragen-class mountain vistas, no vegetation yet. Deliverable: GPU hydraulic +
thermal erosion; view-dependent terrain mesh (or height-field intersection — measured);
procedural rock / scree / grass / snow materials on built-in-tier spectra; snow
placement by slope, aspect, curvature and freezing level, recipe-owned glaciers; the
full atmosphere (Rayleigh + Mie + ozone, multiple scattering, aerial perspective,
altitude); sun / sky importance sampling; astronomy (latitude, date, local solar time,
location dropdown with climate normals); the linked-slider framework (basic: season,
time of day, snow, fog/haze; advanced: location, altitude, date, temperature with °C/°F,
humidity); OIDN; auto camera v1 for the mountain building blocks; preview + tweak
window; preview watch mode, loupe and contact sheets (§7.4); time + VRAM estimate.
Verify: alpenglow appears at dawn/dusk without any
special-casing; snowcaps show bare steep faces; the estimate lands within its own
stated range on a set of test views; goldens added.

**M2 — Measured spectra.**
Goal: "it is spectral" backed by measured data. Deliverable: verify access routes and
terms for every source (§6.3); manifest + cache + download + manual import; resampling
to the engine grid; measured rock / soil / snow / water spectra; PROSPECT-D with season
pigment states, validated against measured green-leaf spectra; per-material tier in the
sidecar; "require measured data" switch (off by default); credits screen. Verify: with
the network off, renders still work on tiers 4–5 and the sidecar says so; with a
corrupted download, the checksum rejects it.

**M3 — Water.**
Goal: lakes and rivers that sell the scene. Deliverable: shot-first lake basins and
rivers from the flow map; spectral water (pure-water absorption, organics, glacial
flour); ripples; wet shores; reflections. Verify: a lake shows turquoise shallows
grading to blue depth from absorption alone; the mountain backdrop reflects correctly.

**M4 — Fog & clouds.**
Goal: atmosphere you can see. Deliverable: altitude fog, valley inversion fog / sea of
clouds, steam fog from lake vs. air temperature, humidity haze, frost on surfaces;
volumetric clouds with multiple scattering; spectral Mie phase (fogbows, glories).
Verify: a cold autumn dawn over a lake produces steam fog from the defaults alone.

**M5 — Vegetation.**
Goal: forests, and with them the north star. Deliverable: procedural tree species,
bark, grass, ferns, shrubs, leaf litter; ecosystem placement; mass TLAS instancing;
leaf translucency; PROSPECT-D leaves driven by the season slider; leaf-off in winter;
distant-forest representation (measured decision). Verify: **"autumn lake, dawn, fog"**
renders at 4K within the 8 GB budget; backlit leaves glow.

**M6 — Night.**
Goal: moonlit scenes that look right for the right reason. Deliverable: moon position,
phase and lunar spectrum; stars (catalogue or procedural fallback); procedural Milky Way
and airglow; Purkinje / mesopic display transform. Verify: a moonlit scene reads blue
with neutral-white materials, and the moon phase matches the date.

**M7 — Winter.**
Goal: winter as a first-class season. Deliverable: frozen lakes (ice + snow on ice);
close-up snow (depth as geometry, snow on branches, sparkle, subsurface scattering); dry
/ wet / melting snow states from temperature. Verify: "frozen lake + snow-capped
mountains + dawn" renders with plausible snow everywhere from the defaults.

**M8 — Props.**
Goal: signs of people. Deliverable: shape-grammar cabins with blackbody window glow,
docks, rowboats, paths, fences, placed by the shot-first layout. Verify: a cabin at dusk
glows warm against a blue-hour sky.

**M9 — Coast & ocean.**
Goal: seascapes. Deliverable: FFT ocean, foam and whitecaps, cliffs and beaches, moon
glitter path with MIS. Verify: a moonlit seascape converges without fireflies dominating
the glitter path.

**M10 — Waterfalls & spray rainbows.**
Goal: the hardest prop. Deliverable: planted river steps, whitewater surface, mist volume,
spray; rainbows from the spectral Mie phase with the sun behind the camera. Verify: a
rainbow appears at the correct ~42° angle from the antisolar point.

**M11 — All targets.**
Goal: every wallpaper shape. Deliverable: 8K; tiled 16K; ultrawide; multi-monitor spans;
phone portrait; per-aspect auto camera and safe-frame overlays; HDR output if Windows
support checks out. Verify: a 16K render completes within the VRAM budget and resumes
after an interruption.

**Ongoing:** composition scoring, camera filters (sensor / film spectral curves, white
balance, lens CA), estimator accuracy.

---

## 11. Open questions & risks

**Realism.**
- **Vegetation is the biggest risk.** Procedural trees that survive close inspection at
  4K+ take many iterations; procedural purity rules out the shortcut of scanned assets.
- **Composition.** Generators produce mediocre images; shot-first generation plus
  scoring (§4.8) is the bet. Expect to tune the scoring heavily.
- **Waterfalls** are hard to make convincing without simulation (M10).

**Performance & memory.**
- **8 GB VRAM** limits forest density; instancing, procedural materials and measured LOD
  decisions are the levers.
- **Render times are unknown** until M0/M1 measure them; the §7.1 numbers are guesses.
- **Moon glitter** and caustic-like paths are noisy; MIS quality decides whether they
  converge in reasonable time.
- **View-dependent LOD** must stay conservative for shadow and reflection rays that see
  off-screen geometry.

**Data.**
- **No public measured autumn-leaf spectra found** (2026-10) — PROSPECT-D covers it; revisit
  if a dataset appears (a Saskatchewan dataset of 290 leaves with pigment measurements
  may include senescent leaves — unconfirmed).
- **Access and licences** for LOPEX93, ANGERS, PROSPECT coefficients, the star catalogue
  and climate normals are unverified (M2). ECOSTRESS is order-form only and all-rights-
  reserved.
- **Sources can disappear**; the tier system keeps renders working but changes the image,
  which the sidecar records.

**Engine.**
- **Which `RayTracer.Gpu` pieces serve an offline integrator** is unconfirmed (M0).
- **Mass TLAS instancing** is new; today's TLAS holds a handful of instances.
- **Leaf alpha** needs non-opaque candidates in the inline ray query loop, slow on Ampere;
  real leaf geometry costs memory.
- **GPU determinism across driver updates** is not guaranteed; goldens may need
  re-baselining.
- **The agent loop needs the GPU.** Only the local RTX 3070 can render, so LLM look-dev
  iteration (§7.4) works only in a local session on the dev box. A CPU preview
  renderer would lift that, but contradicts the no-CPU-port decision (§9); revisit
  only if iterating from cloud sessions matters.
- **Small previews hide detail.** A 320 px preview cannot show leaf-level or
  edge-level problems, and denoising can hide noise; loupes and raw (undenoised)
  outputs cover that (§7.4).

**Product.**
- **Location list and climate data**: which places, and from which public normals.
- **HDR wallpaper support** on Windows is unverified (M11).

---

## Appendix — decisions from the brainstorm (2026-10-17/18)

| Topic | Decision |
|---|---|
| Why build it | Own engine, own settings, genuinely spectral — not a Terragen/Blender replacement |
| Location | A new project in the `RayTracer/` folder and solution, beside Pinball |
| CPU parity | Dropped for Vista: unit-test the math, golden-image the GPU |
| Assets | Procedural purity; more iterations accepted |
| Spectral data | Measured where available; models (PROSPECT-D) where not; downloaded locally / on the fly with a built-in fallback; nothing restricted committed |
| "Require measured data" | Off by default |
| User control | Recipe building blocks + seed; auto camera; small camera tweaks; no scenery editing |
| Generation | Shot first |
| Basic sliders | Season, time of day, snow, fog |
| Advanced controls | Location / latitude, altitude, date, temperature (°C with °C/°F toggle), humidity |
| Linked sliders | Auto from upstream until pinned; reset re-links; contradictions allowed with a hint |
| Snow | Own slider, default from season (+ freezing level), overridable; glaciers recipe-owned; snow-capped mountains from M1 |
| Astronomy | Real sun / moon / stars from latitude, date, local solar time; location dropdown of iconic places |
| Targets | All eventually; PoC at 3840×2160; time + VRAM estimate per scene and view |
| Preview | ~320 px preview at final quality settings, headless and scriptable, so people and LLMs can iterate in seconds instead of hours |
| Hardware | RTX 3070, 8 GB |
| Scope | Stills only |
| Style | Photoreal target; anything else via filters / camera settings |
