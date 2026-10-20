# Vista — a Spectral Path-Traced Landscape Wallpaper Generator

The single source of truth for the landscape-wallpaper effort. **M0 is done** (2026-10-19):
`Vista.Core` / `Vista.App` / `Vista.Tests` render a recipe + seed end to end on the RTX 3070
through offline integrator v0 (see **M0 status** and **M0 hand-off** in §10). M1 is planned as
seventeen slices in §10 (**M1 plan**); M1.1 (contact sheets and the loupe) is done, M1.2 is next. It sits
alongside the engine's own `plan.md` (the RayTracer remaining-work tracker) and
`pinball-plan.md`, reuses the same engine foundation, but deliberately departs from the
engine's CPU-parity convention (§9). "Vista" is a working name.

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

- **Set pieces**: one open-ended catalogue (eventually long) of things a wallpaper can
  contain — today snow-capped mountains, alpine range, rolling hills, coast, forest,
  lake, frozen lake, river, waterfall, sea. The recipe holds the set currently chosen:
  add a piece to get it, remove it to drop it. "Mountains + forest + lake" is three
  pieces; removing forest leaves mountains + lake. Set pieces are chosen **before**
  anything renders, and changing the set starts a **new scene** (§3.4). Order never
  matters, and an empty set is plain base ground. Some pieces **conflict** (lake and
  frozen lake): a recipe never holds both, and adding one swaps the other out.
- **Time of day** (dawn, day, golden hour, dusk, night) and **season**: one value each,
  beside the set, e.g. "frozen lake + snow-capped mountains" at dawn.
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
- **Changing the set pieces starts a new scene.** The world, the planted viewpoint and
  the auto camera are regenerated; nothing is promised to stay where it was. (Set pieces
  are picked before the first render, §3.2.)
- **Two speeds** within a scene. Time of day, fog, exposure and camera changes only
  restart the preview. Snow and season (snow depth near the camera, snow on branches, bare
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
  intersection (min/max quadtree) in the inline ray query loop. Decide in M1 by measuring
  (M1.9, M1.10).
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
  (§11). What they *can* run is `--dry-run`: world generation, mesh, sidecar and a
  top-down height map with the camera marked, so the world side can be iterated
  anywhere. Once erosion runs on the GPU (M1.7), `--dry-run` uses a cached eroded field when
  there is one and the un-eroded terrain otherwise (§10, M1 decisions).

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

**M0 reuse assessment (2026-10-18, by reading the code; confirmed at runtime 2026-10-19 with
integrator v0, see the end of this list):**

- **Reusable as is:** `ShaderCompiler.CompileCompute` (DXC, SM 6.5). It is `internal`, so
  `RayTracer.Gpu.csproj` needs one additive `InternalsVisibleTo Include="Vista.App"`, as
  it already has for the maze and pinball apps.
- **Not callable, by design:** device creation, the RT-tier check, command objects, the
  fence wait, upload/UAV buffer helpers, BLAS/TLAS building and readback are private
  methods copied into each `PhaseNRenderer` ("the D3D12 scaffolding is intentionally
  duplicated rather than refactoring the earlier (separately dev-box-validated)
  hosts").
- **`Phase6Renderer` does not fit an offline integrator:** its BLAS assumes quads
  (`StaticQuadCount × 4` vertices, `× 6` indices) from `PackedScene`, its cbuffers are the
  maze's, it owns a swap chain and the TAA real-time loop, and `ReadbackOutput` returns
  RGBA8 only. Vista needs a triangle-mesh BLAS (`TerrainMesh`), float readback of XYZ and
  the AOVs, and dispatches driven by `BatchPlanner`.
- **Recommendation:** follow the engine's own convention: Vista.App gets its own D3D12
  host with the scaffolding copied from `Phase6Renderer`, and `RayTracer.Gpu` changes by
  the one `InternalsVisibleTo` line only. That keeps "don't move the maze" (§9.2) trivially
  true. A shared helper extracted from the phase renderers would touch validated maze
  code, so it is not worth it for one more host.
- **Shaders:** `RayTracer.Gpu` loads HLSL from `AppContext.BaseDirectory/Shaders`; Vista.App
  links its own shader folder the way `Pinball.App` links the engine's.
- **HLSL compile checks still need Windows:** `Vortice.Dxc.Native` ships a linux-x64
  `libdxcompiler.so`, but in a cloud container it returns "Internal Compiler error" even
  for an empty compute shader (2026-10-18). A standalone Linux DXC build is untested.
- **From `RayTracer.Core`, already in use by `Vista.Core`:** `DayNightCycle` / `SunState`
  (the M0 sun), `JobSystem.ToSRGB` (XYZ → linear sRGB, so Vista and the maze agree on
  colour), and `LensOptions`' focal-length convention (pinned by a test). Integrator v0
  added `WavelengthLookup` / `SpectralResources` (hero wavelengths, `DeterXYZ`), a port of
  `Sky.DomeRadiance`, and `RgbToReflectanceBasis` for built-in-tier materials.

**Confirmed at runtime (2026-10-19).** The assessment held with no surprises: `ShaderCompiler`
compiles `VistaIntegrator.hlsl` (SM 6.5, inline `RayQuery`, wave intrinsics) unchanged, behind the
one `InternalsVisibleTo Include="Vista.App"` line, which is the only change to `RayTracer.Gpu`.
Everything else in `D3D12VistaRenderer` is copied scaffolding (device, RT-tier check, command
objects, fence wait, BLAS/TLAS build, buffer helpers), plus two things no phase renderer had:
default-heap upload of the mesh (the shader reads normals and indices on every hit, so they must
not sit in an upload heap across PCIe) and timestamp queries for per-dispatch GPU time. The engine's
shaders also land in `Vista.App`'s output through the project reference; harmless, and Vista
never loads them.

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

One deliberate shift from the table, made in M0: the **EXR / PNG encoders, the debug-image
encoders and the headless preview pipeline live in `Vista.Core`**, not `Vista.App`. None of
them needs a GPU, and in `Vista.Core` they are unit-tested in CI. `Vista.App` only parses the
command line and will supply the D3D12 renderer behind the `IVistaRenderer` seam.

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

**M0 status (2026-10-19): done.** Every M0 deliverable and check is met. The GPU-free half
landed on 2026-10-18; integrator v0, its D3D12 host, the shader's ray counter and the batch
measurements followed on 2026-10-19 on the RTX 3070. `Vista.Tests` has 141 tests (net10.0, so
they run in hosted CI). The only engine change is one `InternalsVisibleTo` line in
`RayTracer.Gpu.csproj` (§8.1).

| M0 deliverable | Where | Notes |
|---|---|---|
| `Vista.Core` / `Vista.App` / `Vista.Tests` in `RayTracer.slnx` | | Vista.App is the CLI front end plus the GPU host; no UI yet |
| Recipe + seed model with serialization | `Vista.Core/Recipes` | A set of set pieces + time + season. Strict JSON: unknown fields, integer enums and future schemas are errors, so typos fail loudly |
| Camera file | `Scene/CameraSpec.cs` | Engine axes (+X east, +Y up, +Z north); yaw = compass heading; `ToEngineRotation()` tested against the engine's ray convention |
| Height field → mesh | `Vista.Core/World` | Seeded ridged / fBm terrain with domain warp on a fixed 1025² × 16 m grid (16.4 km); uniform mesh, 2.1 M triangles, 50 MB; winding and normals tested |
| Recipe / seed determinism tests | `WorldDeterminismTests` | Pinned noise and terrain hashes; terrain provably unmoved by time, season, name or set-piece order |
| Integrator v0 | `Vista.App/Shaders/VistaIntegrator.hlsl` | Multi-bounce Lambertian path tracing, Russian roulette after bounce 3, next-event estimation on a real-size sun disk, the engine's Rayleigh dome (port of `Sky.DomeRadiance`, parity-tested), a hero plus three companion wavelengths over the engine's 50-wavelength cycle; every random number keyed by (pixel, sample index) |
| Its light model and tables, in C# | `Rendering/SkyLight.cs`, `Rendering/IntegratorTables.cs` | The reference for the shader's light maths (vista-plan §9), unit-tested |
| D3D12 host | `Vista.App/Gpu/D3D12VistaRenderer.cs` | Mesh in default-heap buffers, BLAS (32-bit indices) + one-instance TLAS, `BatchPlanner`-driven dispatches timed by timestamp queries, float readback (`Rendering/IntegratorReadback.cs`); device, pipeline and BLAS survive re-renders of the same mesh |
| TDR-safe sample batches | `Rendering/BatchPlanner.cs` + the host | Row bands × sample ranges; every (pixel, sample) issued once; defaults measured below |
| Ray counter | shader + `RenderStats.RaysTraced` | `WaveActiveSum`, then one 32-bit atomic per wave with a carry into a high word: an exact 64-bit count on SM 6.5. A 4K, 1024 spp render counted 27.8 G rays (six carries) |
| EXR + PNG writers | `Vista.Core/Output` | Dependency-free; checked bit-exact against OpenEXR 3.5 and Pillow outside CI |
| Sidecar | `Output/Sidecar.cs` | Recipe, seed, camera, data tiers, height-field hash, engine version with commit, device; stage timings, rays traced / per sample / per second, dispatches, longest dispatch, the dispatch budget |
| Headless preview CLI with debug outputs | `Vista.Core/Cli`, `Vista.App` | Renders on the 3070, or `--dry-run` anywhere; albedo, normal, depth, material, tier and variance images; `--dispatch-ms` |
| Which `RayTracer.Gpu` pieces serve an offline integrator | §8.1 | Assessed by reading the code, confirmed at runtime |

*Checks (all on the RTX 3070 unless noted):*

- **Same recipe + seed + camera renders bit-identically twice:** `Vista.App --gpu-selftest`
  (snow-capped mountains, golden hour, seed 42, 320×180, 16 spp, every debug output): XYZ and every
  debug buffer identical bit for bit, same ray count.
- **Bit-identical under two dispatch budgets:** the same self-test renders at the default budget
  (4 dispatches, full-frame passes of several samples) and at 0.05 ms with a 20 µs first guess
  (97 dispatches, row bands of one sample). Identical, same ray count. A fourth render without
  debug outputs gives the same beauty, so asking for debug images never changes the picture.
- **A 320 px preview renders headless** and its sidecar reports timings (device init, shader
  compile, mesh upload, BLAS build, trace, readback) and rays (traced, per sample, per second).
- **Maze unmoved:** `--phase6-selftest` PASS (700/700 within 8/255), `--phase6-regress` 19/19 PASS.
  Not bit-exact against the committed goldens: 12 of the 19 views show a maximum channel Δ of
  1/255. Unmodified `origin/main` shows exactly the same 12 views, so this is the goldens against
  today's driver, not Vista (§11 already expects re-baselining after driver updates).
- **CI:** recipe / seed determinism and every other `Vista.Tests` test is green (141).
- **D3D12 debug layer with GPU-based validation** (a Debug build) reports nothing for the self-test
  or for previews with each mix of debug outputs.

**M0 batch defaults, measured (2026-10-19, RTX 3070, M0 terrain, 3840×2160, seed 42):**

| View | Rays per sample | ns per pixel-sample (mean / worst dispatch) | Rays per second |
|---|---|---|---|
| Sky only (pitch +45°) | 1.00 | 0.24 / — | 4.2 G |
| Aerial over the range, day | 2.42 | 0.70 / 1.00 | 3.4 G |
| Default camera, day | 2.29 | 0.88 / 1.24 | 2.6 G |
| Down a slope, golden hour (every pixel terrain) | 3.27 | 1.32 / 1.39 | 2.5 G |

- A 4K render at 64 spp takes 0.4–0.7 s of GPU time. 1024 spp on the costliest view took 10.9 s
  in 56 dispatches; the longest was 245.6 ms against the 250 ms budget. At the 1000 ms cap the
  longest was 644 ms, because the 64-samples-per-dispatch cap binds first. Nothing came near the
  ~2 s TDR. The host adds about 1 ms per dispatch (wall time against GPU time).
- **250 ms kept:** the per-dispatch overhead stays under 0.5%, the ETA updates four times a second,
  and the TDR is 8× away.
- **2 µs initial cost kept:** about 1,400× the worst M0 cost, so the first dispatches are tiny and
  the estimate ramps down over about ten dispatches (≈ 10 ms at 4K). That is cheap insurance:
  atmosphere, volumes and forests (M1–M5) will raise the per-sample cost by orders of magnitude,
  and the first dispatch is the only one planned without a measurement.
- **Russian roulette** (after bounce 3; survival = the largest throughput, at least 5%) ends nearly
  every path by bounce 5 on the M0 albedo (≈ 0.17): 6 and 64 maximum bounces trace the same rays.
  Bright snow (M1) will lengthen paths; measure again then.
- **Risk for M11:** row-band mode plans each band from the previous band's cost, and bands exist
  only when one full-frame sample exceeds the budget: never at 4K on M0 terrain (≈ 2–12 ms per
  sample) but always at 16K. Sky rows cost about 6× less than terrain rows, so a band planned on
  sky could overrun by that factor on the terrain below it (250 ms → ~1.5 s). M11's tiling should
  plan bands with a safety factor or a per-row cost history.

*M0 decisions taken in code (revisit freely):*

- **A recipe is a set of set pieces** (one `SetPiece` catalogue, §3.2; confirmed
  2026-10-18) plus a single time of day and season. `Recipe.With` / `Without` add and
  remove pieces; the JSON is `"setPieces": [...]`. The catalogue is an enum for now, and
  files store its camelCase names, so moving to a registry as it grows changes no files.
  Changing the set starts a new scene, so nothing promises the other pieces stay put.
  Conflicts are a symmetric table (`SetPieceConflicts`, today only lake ↔ frozen lake):
  `With` swaps the conflicting piece out, and a recipe file listing both is rejected. A UI
  could grey them out instead; it reads the same table.
- **Sub-seeds are keyed by stage name** (`SeedDerivation`), not by the whole recipe. So
  changing time or season never reshuffles the terrain, and new stages don't perturb
  existing ones. Tests pin this, and that set order doesn't matter.
- **Mountains win** when pieces combine; placing hills, coast and forest *around* them is
  the shot-first layout's job (§4.2). Every other set, including the empty one, gets the
  hills shape as plain base ground until those pieces' milestones.
- **Placeholders, to be replaced in M1:** the sun follows the maze's `DayNightCycle`.
  Dawn and dusk sit about 3° *above* the horizon, because the single-scattering sky
  cannot do twilight. The PNG display transform is exposure + Narkowicz ACES + sRGB,
  the same curve as the engine's `Filmic`. Without a camera file, the default camera
  stands at eye height on the south side looking north.
- **Gradient noise has no axis-aligned gradients, and the terrain is domain-warped.**
  Without both, ridged noise drew a ruler-straight crest along every coarse lattice line.
  A regression test now guards this.
- **Debug outputs:** albedo and normal average over every sample of a pixel (what OIDN wants in
  M1). Depth and material id cannot be averaged, so they come from the pixel's sample 0. Variance
  is the variance of the pixel's mean luminance (sample variance ÷ samples), from a Welford update.
- **Accumulation is a plain float sum in sample order, starting from zero at sample 0** (no
  clear pass), divided once on the CPU. Bit-exactness under any batch partition rests on that order
  plus keying every random number by (pixel, sample index). The 3070 needs no `precise`; the
  self-test would catch a compiler that reorders the sum.
- **The M0 sun** has the real angular radius (0.2666°; the maze uses 0.04 rad) and an irradiance of
  `4.5 × SunLightLevel × solar shape` at normal incidence (`SkyLight.PlaceholderSunIrradiance`):
  about five times the dome's irradiance on level ground at the "day" sun, the clear-sky ratio of
  direct to diffuse daylight. Camera rays see a disk of the matching radiance, so the EXR is
  consistent; bounce rays see the dome only, because the sun is next-event estimation's job.
- **Built-in reflectance** is `RgbToReflectanceBasis` applied to the material's linear-sRGB albedo,
  clamped to [0, 1]. The M0 terrain needs no clamping, and a test pins its round trip.
- **Surfaces are two-sided** with smooth shading normals; new rays leave along the geometric normal,
  offset by 1 mm + 4·10⁻⁶ × the coordinate size (float spacing at 8 km is 1 mm). A bounce sampled
  under the true surface ends the path.

**M0 hand-off.** To understand M0, start by reading `Vista.Core/Cli/PreviewPipeline.cs` (the whole
flow), then `Vista.App/Gpu/D3D12VistaRenderer.cs` and `Vista.App/Shaders/VistaIntegrator.hlsl`
(the GPU half), then `Vista.Core/Rendering/BatchPlanner.cs` (the determinism contract).

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --gpu-selftest
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png --debug all
    [--camera c.json] [--width 3840] [--spp 64] [--bounces 6] [--dispatch-ms 250] [--dry-run]
```

*Files added or changed on 2026-10-19:*

| File | What |
|---|---|
| `Vista.App/Shaders/VistaIntegrator.hlsl` | Integrator v0 |
| `Vista.App/Gpu/D3D12VistaRenderer.cs` | The D3D12 `IVistaRenderer` |
| `Vista.App/Gpu/GpuSelfTest.cs` | `--gpu-selftest` |
| `Vista.App/Program.cs`, `Vista.App.csproj` | Preview on the GPU, the self-test, the shader copied next to the .exe |
| `Vista.Core/Rendering/SkyLight.cs` | The M0 sun and dome; C# reference of the shader's light |
| `Vista.Core/Rendering/IntegratorTables.cs` | Spectral tables uploaded per render |
| `Vista.Core/Rendering/IntegratorReadback.cs` | Raw GPU sums → the `Aov` layouts |
| `Vista.Core/Rendering/RenderTypes.cs`, `Output/Sidecar.cs`, `Cli/PreviewCommandLine.cs`, `Cli/PreviewPipeline.cs` | `Aov` semantics, longest-dispatch and worst-cost stats, the budget in the sidecar, `--dispatch-ms` |
| `RayTracer.Gpu/RayTracer.Gpu.csproj` | `InternalsVisibleTo Vista.App` |
| `Vista.Tests/SkyLightTests.cs`, `IntegratorTablesTests.cs`, `IntegratorReadbackTests.cs` (+ CLI and pipeline additions) | 28 new tests |

*Gotchas:*

- **`1 − cos θ` of the sun loses 0.2% in float** (θ ≈ 0.0047 rad). Compute it as `2·sin²(θ/2)` in
  double (`SkyLight.SunOneMinusCosMax`); a test caught this.
- **Root UAVs have no bounds checking.** An output nobody asked for binds a 16-byte dummy, which is
  safe only because the shader never writes a buffer whose `AovMask` bit is off. Keep it that way.
- **Buffers are always created in COMMON**, whatever the initial state says. Every command list here
  is its own submission, so buffers decay back to COMMON between them and promote implicitly; the
  only explicit barrier moves an uploaded copy to the shader-resource state the BLAS build needs.
- **Debug builds** run the D3D12 debug layer with GPU-based validation and print its messages to
  stderr. The engine's hosts never read the info queue, so their Debug runs are silent.
- **A preview's cost is tiny for now** (a 320×180, 64 spp preview traces in about 4 ms), so the
  wall time is world generation, mesh and file writing. M1's watch mode (§7.4) is where that matters.

**Next: M1**, planned below as seventeen slices, starting with M1.1 (contact sheets and loupe). Its
measured decisions, the view-dependent mesh against height-field intersection (§4.3) and sun / sky
importance sampling, are M1.9–M1.10 and M1.5. The M0 placeholders to replace are listed above.

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

**M1 plan (agreed with the user 2026-10-19).** Seventeen slices, one PR each, in the order below.
Work stops after each PR for review.

*Strategy decisions (the user's answers, 2026-10-19):*

| Question | Decision |
|---|---|
| How much of the sky lands before the terrain work? | **All of it.** Astronomy, the spectral atmosphere with multiple scattering, aerial perspective and sun / sky importance sampling come before erosion. Rock and snow are then tuned once, under the final light, and twilight exists before the snow slice that checks alpenglow. |
| When do the iteration tools arrive? | **Contact sheets and loupe first, tweak window last.** Sheets and loupe are M1.1, so every later look PR carries a before/after sheet. Watch mode follows erosion, when world generation stops being free. The window comes last, once the sliders, auto camera and estimate exist to go in it. |
| Where does erosion run? | **On the GPU, as planned (§4.3).** Gather-only compute passes (each cell writes only itself, no float atomics), so erosion is deterministic on one driver. The eroded field is cached per (recipe, seed) and its hash goes in the sidecar, so a driver update that changes the world shows up instead of passing silently. A C# reference of each pass is unit-tested in CI on small grids; HLSL-against-C# parity runs in `--gpu-selftest`. CI and `--dry-run` use the cached field when there is one, else the un-eroded terrain with a warning. |
| Where do the location dropdown's climate normals come from in M1? | **A hand-entered table:** monthly mean daily lows and highs for each dropdown place, with a source per place, treated as constants like the CIE tables. M2 may replace them through the manifest (§6.2). |

*Decisions that follow (proposed with this plan; revisit freely):*

- **Ozone cross-sections** are entered the same way as the climate normals: a few numbers at the
  engine's wavelengths, source cited, tier 5 until M2. The extraterrestrial sun keeps the engine's
  5778 K Planck shape; the atmosphere does the reddening.
- **The atmosphere is LUTs, validated against a brute-force reference** (§5.2). The reference mode
  ships in the same slice as the LUTs, so every later atmosphere change can be checked against it.
- **Light gets physical units in M1.3.** Day and twilight differ by about 10⁴ in the real atmosphere,
  so the PNG gets auto exposure (log-average luminance plus EV compensation, recorded in the sidecar).
  The EXR stays scene-referred.
- **Linked sliders land before materials and snow**, so snow reads its freezing level from the
  weather chain instead of a stub that gets rewired later.
- **The view-dependent mesh comes first, height-field intersection second** (M1.9, M1.10). The mesh
  rides the 3070's hardware triangle path, so it is the default; the height-field path is built only
  far enough to measure it against the mesh on the same views.
- **The estimate comes before the window**, so the window can show it.
- **Vista goldens arrive at the sign-off (M1.17).** Every look slice moves every image, so earlier
  goldens would only be re-baselined each time. Until then the self-test's bit-identity checks guard
  determinism and contact sheets show the look.

*Every slice:*

- One PR, then stop for review. The PR says what was measured and with which commands.
- `Vista.Tests` green in CI; `Vista.App --gpu-selftest` passes on the RTX 3070 and grows with the
  slice where noted.
- A slice that changes the look attaches a before/after contact sheet (from M1.1 on) and adds a row
  to the cost ledger below.
- No engine change is planned. A slice that does touch `RayTracer.Core` / `RayTracer.Gpu` also runs
  `--phase6-selftest` and `--phase6-regress` (§9.2).
- It ends with its status, decisions and gotchas written into this section.

*M1 cost ledger:* ns per pixel-sample (mean over dispatches) on the four M0 measurement views at
3840×2160, 64 spp, seed 42. The views are committed in `vista-views/` (M1.1);
`powershell -ExecutionPolicy Bypass -File vista-views/measure.ps1` renders them and prints the row.

| After | Sky only | Aerial, day | Default camera, day | Down a slope, golden hour |
|---|---|---|---|---|
| M0 | 0.24 | 0.70 | 0.88 | 1.32 |
| M1.1 | 0.24 | 0.73 | 0.87 | 1.31 |
| M1.2 | 0.24 | 0.68 | 0.83 | 1.26 |
| M1.3 | 0.22 | 2.18 | 1.49 | 2.30 |

M1.1 changes no pixel: the committed views trace exactly M0's rays per sample (1.00, 2.42, 2.29, 3.27),
so the M1.1 row shows the run-to-run timing noise, about ±4%. Differences smaller than that mean nothing.
M1.2 moves the sun but adds no work: a second run gave 0.24, 0.69, 0.84, 1.28, and the views trace 1.00,
2.42, 2.29 and 3.23 rays per sample (the low sun now casts other shadows on the down-slope view).
M1.3 traces the same rays per sample as M1.2 (1.00, 2.42, 2.29, 3.23); a second run gave 0.22, 2.17, 1.42 and
2.00. The 1.7–3× on terrain is the atmosphere's lookups: every sun shadow ray now reads the columns LUT (four
float4 loads and four exponentials), and every ray that leaves the terrain after a bounce reads two sky-view slices
(32 float2 loads for its four wavelengths). Camera rays read one slice, so the sky view got no dearer. The exposure
meter (a 320 px render, §M1.3 decisions) is not in these numbers; it costs about 1/150 of a 4K frame.

**M1.1 — Contact sheets & loupe.**
- *Goal:* compare variants in one look, and check final-resolution detail without rendering the
  whole frame (§7.4).
- *Deliverable:* `--sheet` renders variants along one axis (e.g. `seed=1,2,3,4`, time of day, any
  recipe field; sliders once they exist) as previews and tiles them into one labelled PNG, with one
  sidecar listing every cell. `--sheet-from a.png b.png …` tiles existing renders, labelled from
  their sidecars, which builds a PR's before/after from a render on `main` and one on the branch.
  Labels use a small built-in bitmap font (procedural purity). `--loupe x,y,w,h` renders only that
  window of the full-resolution frame, with the full frame's pixel coordinates and random keys, so a
  loupe is exactly a crop of the final. The four M0 measurement views are committed as camera files.
- *Verify:* `--gpu-selftest` adds a loupe check: a 64×64 window of a 640×360 frame is bit-identical
  to the same window cut from the full render. A four-seed sheet and a time-of-day sheet render
  headless on the 3070. CI: sheet layout, labels and the sheet sidecar.

*M1.1 status (2026-10-19): done.* Every deliverable and check is met; nothing in `RayTracer.Core` /
`RayTracer.Gpu` changed. `Vista.Tests` has 272 tests (131 new).

*Checks (on the RTX 3070 unless noted):*

- **Loupe = crop:** `--gpu-selftest` renders the self-test scene at 640×360, then the 64×64 window at
  (291, 137), off the 8-pixel thread-group grid and straddling the central peak and the sky. Beauty and
  every debug buffer equal that window of the full frame bit for bit; so does the window rendered again
  under the 0.05 ms budget (87 dispatches against 1). A Debug build (GPU-based validation) reports nothing.
- **Full frames unchanged:** four previews (default, aerial, down-slope, a 640×360 golden-hour view with
  every debug output) are byte-identical to the M0 build's PNGs and EXRs, and the ledger views trace the
  same rays per sample as M0.
- **Before/after, the PR way:** the four measurement views rendered by a `main` build (11e489b) and by
  this branch are byte-identical pair by pair; `--sheet-from` tiles the eight and labels each pair by build.
- **Sheets headless:** a four-seed sheet (2.7 s wall, four worlds) and a five-cell time-of-day sheet with
  a normal-map sheet (2.0 s wall, one world) rendered from one `Vista.App` call each. A 4K loupe of a peak
  (320×180 out of 3840×2160) takes 1.4 s wall, almost all of it world generation.
- **CI:** layout geometry, the font, label and title derivation, the sheet sidecar (written and read
  back), the axis parser, `--sheet-from` labelling, the PNG decoder (every filter type), JSON overrides,
  the world cache, loupe requests and the committed view files.

*M1.1 decisions taken in code (revisit freely):*

- **A loupe keys pixels by their place in the frame.** The shader's pixel hash and camera ray use
  `(WindowX + x, WindowY + y)` and the frame size; buffers, dispatches and the batch planner are
  window-sized. A full render is the window at (0, 0) of its own size, which is why every M0 value is
  unchanged. `RenderSettings.Width/Height` stay the frame; `Window` is empty for the whole frame and
  `Rendered` resolves it, so host code sizes buffers from `Rendered`, never from `Width × Height`.
- **Sidecar schema 2** adds `image.window`, always present (the whole frame when there is no loupe).
  `image.width/height` remain the frame, so a loupe's sidecar still says which final it is a piece of.
- **A sheet axis is `key=v1,v2,…`:** `seed`, `recipe` (files), `camera` (files or `default`), `spp`,
  `bounces`, `camera.<field>`, or any recipe field by dotted path. Field values are written loosely
  (`time=dawn,dusk`, `setPieces=[snowCappedMountains],[snowCappedMountains, lake]`) and applied as JSON
  overrides *before* the strict parser, so a misspelt field still fails, and every cell is checked before
  the first renders. The linked sliders (M1.11) become recipe fields, so they get sheets for free.
- **Each cell is a complete preview** under `<sheet>.cells/` (PNG, EXR, sidecar, debug images), and the
  sheet tiles the written PNGs. Each requested debug output gets its own sheet (`<sheet>.normal.png`); a
  `--dry-run` sheet tiles the cells' height maps, so world variants can be compared without a GPU.
- **Labels:** `--sheet` labels cells `key=value`. `--sheet-from` labels each image by the identity fields
  of its sidecar that differ from the others' (recipe, seed, camera, size, loupe, spp, bounces, display
  transform, build commit — never timings or ray counts), so a main-vs-branch pair is labelled by build;
  `--labels` overrides. Fields with the fewest distinct values come first (ties in reading order), so in a
  before/after of the four measurement views the build is never the part that gets cut. The title is the
  fields every cell shares. Sidecars are read as plain JSON, not as
  `Sidecar`, so renders from older and newer schemas tile together. The default camera is compared as
  "default", not by its pose, which follows the terrain (else a seed sheet would label every cell with a
  camera height).
- **Font:** 5×7 glyphs on a 9-row cell (two rows of descenders), printable ASCII plus `°×·−…`, drawn at
  one scale per 320 px of cell width (at most 4). An unknown character draws a hollow box. Labels wrap at
  spaces onto up to three lines (every label strip as tall as the longest label), the third cut with `…`;
  titles wrap at `" · "`.
- **One-world cache:** the cells of a sheet share a `WorldCache` keyed by what `TerrainGenerator` reads
  (terrain profile and seed). A time-of-day sheet generates and meshes once, and the same mesh instance
  lets the renderer keep its BLAS. Watch mode (M1.8) replaces it with a cache per stage.
- **The measurement views are files** in `vista-views/`: two recipes and four cameras.
  `default.camera.json` pins M0's default camera for seed 42 (y = 1692.21 m), so terrain changes cannot move
  the view; a test keeps every camera above the seed-42 ground, which will flag it if erosion buries one.
- **CLI:** a missing or invalid input file prints one `error:` line and exits 1 instead of a stack trace;
  console output is UTF-8 so sizes print as `640×360`.

*M1.1 gotchas:*

- **This machine's locale writes decimal commas.** An interpolated `double` in JSON or in a script's
  printout came out as `1692,21`; anything machine-read uses the invariant culture.
- **`--sheet-from` labels builds by the commit in `engine.vistaVersion`, which is `HEAD`'s.** An
  uncommitted branch build labels itself with its base commit, so commit before rendering the "after".
- **The sheet's PNGs are read back with a small decoder** (`PngDecoder`): 8-bit, non-interlaced grey, RGB
  or RGBA only, CRCs unchecked. That covers every file Vista writes, not arbitrary PNGs.

**M1.1 hand-off.** To understand M1.1, start by reading `Vista.Core/Cli/SheetPipeline.cs` (both sheet
flows), then `Vista.Core/Output/SheetLabels.cs` (how cells are labelled), then the `WindowX` / `FrameWidth`
lines of `Vista.App/Shaders/VistaIntegrator.hlsl` (the loupe).

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --width 3840 --loupe 1700,800,320,180 --out l.png
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --vary time=dawn,day,goldenHour,dusk --out s.png
    [--columns N] [--title text] [--debug normal] [--loupe x,y,w,h] [any --preview option] [--dry-run]
dotnet run --project Vista.App -c Release -- --sheet-from main.png branch.png --out before-after.png [--labels before,after]
powershell -ExecutionPolicy Bypass -File vista-views/measure.ps1
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Rendering/PixelWindow.cs`, `RenderTypes.cs` | The loupe window; `RenderSettings.Window` / `Rendered` |
| `Vista.App/Shaders/VistaIntegrator.hlsl`, `Gpu/D3D12VistaRenderer.cs` | Window-sized buffers, frame-keyed pixels |
| `Vista.App/Gpu/GpuSelfTest.cs` | The three loupe checks |
| `Vista.Core/Cli/PreviewCommandLine.cs`, `PreviewPipeline.cs` | `--loupe`, recipe / camera overrides, the world cache; options shared with `--sheet` |
| `Vista.Core/Cli/SheetAxis.cs`, `SheetCommandLine.cs`, `SheetPipeline.cs` | `--sheet` and `--sheet-from` |
| `Vista.Core/Output/BitmapFont.cs`, `ContactSheet.cs`, `SheetLabels.cs`, `SheetSidecar.cs`, `PngDecoder.cs` | Font, layout and compositing, labels, the sheet sidecar, reading PNGs back |
| `Vista.Core/Output/Sidecar.cs` | Schema 2: `image.window` |
| `Vista.Core/Recipes/JsonOverrides.cs`, `Vista.Core/World/WorldCache.cs` | Field overrides and loose values; the one-world cache |
| `Vista.App/Program.cs` | The two sheet modes, one-line input errors, UTF-8 console |
| `vista-views/` | The four measurement views, their recipes, `measure.ps1`, a README |
| `Vista.Tests/*` | `PixelWindow`, `JsonOverrides`, `WorldCache`, `BitmapFont`, `ContactSheet`, `PngDecoder`, `SheetLabels`, `SheetCommandLine`, `SheetPipeline`, `MeasurementView` tests; loupe and override cases in the preview tests; the fake renderer is shared and window-aware |

**Next: M1.2** (astronomy and place). From here on, a look-changing PR attaches a `--sheet-from`
before/after of the measurement views and adds a ledger row.

**M1.2 — Astronomy & place.**
- *Goal:* the real sun for a place, a date and a local solar time, with dawn and dusk below the
  horizon (§3.5).
- *Deliverable:* solar position in `Vista.Core` (NOAA / Meeus: declination from the date, hour
  angle from local solar time, so longitude and time zone drop out) with sunrise and sunset. The
  time-of-day presets are defined relative to sunrise and sunset ("dawn" = sunrise − ~20 min), with
  a hint when polar day or night has none. Season ↔ date by hemisphere (autumn at Torres del Paine
  is April), with a date override. The location list: the §3.5 places plus custom latitude, each
  with a latitude and a typical valley altitude. Recipe and CLI gain location, latitude, date and
  time overrides; the sidecar records them plus the sun's azimuth and elevation. `DayNightCycle` and
  `SunPlaceholder` retire. The M0 dome stays until M1.3, so a sun below the horizon renders dark for
  one slice.
- *Verify:* CI: sun positions against NOAA calculator values (both hemispheres, equinoxes and
  solstices, polar day and night) within 0.05°; season/date tests. 3070: a time-of-day sheet at
  Zermatt in October; the self-test passes.

*M1.2 status (2026-10-19): done.* Every deliverable and check is met; nothing in `RayTracer.Core` /
`RayTracer.Gpu` changed. `Vista.Tests` has 388 tests (272 before).

*Checks (on the RTX 3070 unless noted):*

- **CI, against NOAA:** 33 sun positions (Greenwich, Zermatt, Torres del Paine, Cape Town and the equator
  at both solstices and equinoxes; Svalbard and McMurdo in midnight sun and polar night; Zermatt's sunset
  into twilight) and 7 sunrise / sunset pairs. The expected values come from NOAA's own code: the solar
  calculator's `calcAzEl` and `calcSunriseSet` (gml.noaa.gov/grad/solcalc/main.js), run in a browser at
  longitude 0 and time zone 0, where universal time plus NOAA's equation of time is exactly Vista's local
  solar time. The tests hold the plan's 0.05° and 15 s; the actual agreement is within 0.0002° and 1.2 s
  (probed by tightening them). Eight polar day / night cases, the season ↔ date tests, the presets and the
  polar fallbacks are CI too.
- **A time-of-day sheet at Zermatt in October** (a plain `season: autumn, location: zermatt` recipe, seed 42,
  640×360, 64 spp, default camera), one `Vista.App --sheet` call:

  | Preset | Local solar time | Azimuth | Elevation |
  |---|---|---|---|
  | dawn (sunrise − 20 min) | 06:10:53 | 97.9° | −4.17° |
  | day (solar noon) | 12:00:00 | 180.0° | 35.38° |
  | golden hour (sunset − 60 min) | 16:28:23 | 247.3° | 9.21° |
  | dusk (sunset + 20 min) | 17:48:23 | 261.9° | −4.18° |
  | night (solar midnight) | 00:00:00 | 0.0° | −52.41° |

  Sunrise 06:30:53 and sunset 17:28:23 solar on 2026-10-15 are NOAA's to the second. Dawn, dusk and night
  render dark, as planned: the M0 dome has no twilight until M1.3. A solar-time sweep (07:00 to 17:00 from
  the aerial view) shows the sun crossing east, south and west; a golden-hour sheet over four places on the
  same October date shows the sun at 4.3° at Lofoten against 10.7° at Yosemite (8.1° at Torres del Paine in
  April, its autumn).
- **Self-test:** `--gpu-selftest` passes (the self-test scene now has Zermatt's July golden-hour sun).
- **Before/after:** the four measurement views, `main` (370690d) against this slice, tiled with
  `--sheet-from`. The views now pin Zermatt on 2026-07-15. The noon sun stands due south at 65.5°, behind the
  north-facing cameras, so the range is front-lit and flatter than under M0's sun, which stood high in the
  north and back-lit it. The down-slope view's golden hour moves to azimuth 292°, 8.5° up.

*M1.2 decisions taken in code (revisit freely):*

- **Local solar time is apparent (sundial) time:** noon is 12:00 and the hour angle 15° per hour, so no
  longitude or time zone. The instant that sets the declination is taken on the prime meridian (universal
  time = solar time − equation of time). At a real longitude λ the true instant is λ/15 hours away, which
  moves the declination by at most 0.2° (λ = ±180° at an equinox) and sunrise by under a minute.
- **The rendered sun is the apparent one**, lifted by NOAA's refraction fit (0.48° at the horizon), so the
  disk sits where a camera sees it. Sunrise and sunset are NOAA's: the centre 0.833° below the geometric
  horizon. The sidecar records both elevations.
- **Presets:** dawn = sunrise − 20 min, golden hour = sunset − 60 min (never before noon), dusk = sunset +
  20 min, day = solar noon, night = solar midnight. Golden hour is the last hour before sunset, not a sun
  elevation, so high latitudes get a lower, redder sun, as they do. In polar day the low-sun presets take
  the lowest sun (midnight) and in polar night the daytime ones the highest (noon); each says so in the
  sidecar's warnings, which the CLI prints.
- **Seasons are meteorological** (whole months; spring is March to May in the north). A season's date is the
  15th of its middle month in 2026 (`SeasonCalendar.ReferenceYear`, fixed so a recipe always gives the same
  sun), six months later south of the equator; the equator counts as north.
- **Overrides:** a recipe `date` replaces the season (the sidecar records the season of that date), and a
  `solarTime` replaces the preset. Sheet labels leave out the field an override replaced, and a location of
  "none".
- **Places:** the `Place` enum holds the eleven §3.5 places, each with a latitude and a typical valley
  altitude (source per place in `Places.cs`), plus `None` for "no place named". A recipe with neither a
  location nor a `latitude` is at Zermatt (`Places.Default`); a `latitude` is a custom latitude, at sea level
  until the altitude control (M1.4); giving both is an error.
- **Recipe schema 2** adds `location`, `latitude`, `date` (yyyy-MM-dd) and `solarTime` (HH:mm); schema 1
  files still read. **Sidecar schema 3** adds `place` and a fuller `sun` (date, solar time, sunrise,
  sunset, azimuth, apparent and true elevation, declination, each with where it came from).
- **CLI place options are recipe overrides:** `--location` (clears a latitude), `--latitude` (clears a
  location), `--date`, and `--time` (a preset, which clears a solar time, or `hh:mm`). A sheet refuses an
  axis over a field a place option already decides (`--date` with `--vary season=…`).
- **The M0 light keeps its curves.** `SkyLight.ForSunDirection` makes brightness and colour from the sun's
  height exactly as `DayNightCycle` did (a test pins it against `DayNightCycle.At` over the whole cycle), so
  only the direction is new. `SunPlaceholder` is gone and Vista no longer calls `DayNightCycle`.

*M1.2 gotchas:*

- **NOAA's refraction fit is weak below −0.575°:** at the sunrise depression (true −0.833°) it gives only
  0.40°, not the 0.57° the 0.833 assumes, so at "sunrise" the rendered centre is at −0.44° and the disk is
  just hidden. Harmless now; a refraction-aware sunrise is not worth the inconsistency with NOAA.
- **The effective polar circle is about 65.7°, not 66.6°:** refraction and the sun's radius keep the June
  midnight sun up at latitude 66.0. NOAA's calculator also finds no sunset there, but its fallback branch
  then guesses polar *night* from a 66.4° rule; `SolarPosition.DaylightOn` classifies from the hour angle.
- **`System.Text.Json` reads `TimeOnly` as `17:30` or `17:30:00` and writes `17:30:00`;** `DateOnly` must be
  exactly `yyyy-MM-dd`.
- **Sidecar text has non-ASCII** (`−`, `°`): a Python script printing it on this machine needs
  `PYTHONIOENCODING=utf-8`.

**M1.2 hand-off.** To understand M1.2, start by reading `Vista.Core/Astronomy/Almanac.cs` (recipe → sun, the
presets), then `Vista.Core/Astronomy/SolarPosition.cs` (the NOAA algorithm and its instant), then the `Sun`
and `Place` lines of `Vista.Core/Cli/PreviewPipeline.cs` and `SkyLight.ForSunDirection`.

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png
    [--location zermatt | --latitude 46.0] [--date 2026-10-15] [--time dawn | --time 17:30]
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --location zermatt --date 2026-10-15
    --vary time=dawn,day,goldenHour,dusk,night --out times.png
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --time goldenHour
    --vary location=zermatt,torresDelPaine,lofoten,yosemite --out places.png
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Astronomy/SolarPosition.cs` | NOAA solar position, sunrise / sunset, polar day and night, refraction |
| `Vista.Core/Astronomy/Places.cs` | The location dropdown: latitude, valley altitude, source |
| `Vista.Core/Astronomy/SeasonCalendar.cs` | Season ↔ date by hemisphere |
| `Vista.Core/Astronomy/Almanac.cs` | A recipe resolved to place, date, solar time and sun; the presets and polar hints |
| `Vista.Core/Recipes/Recipe.cs` | Schema 2: `location`, `latitude`, `date`, `solarTime` |
| `Vista.Core/Rendering/SkyLight.cs`, `RenderTypes.cs` | The M0 light from a sun direction; `VistaScene.Sun` is a `SunPosition` |
| `Vista.Core/Output/Sidecar.cs` | Schema 3: `place` and the astronomy in `sun` |
| `Vista.Core/Output/SheetLabels.cs` | Leaves out overridden recipe fields |
| `Vista.Core/Cli/PreviewCommandLine.cs`, `PreviewPipeline.cs`, `SheetCommandLine.cs` | Place options; the almanac in the pipeline; sheet conflicts |
| `Vista.App/Program.cs`, `Gpu/GpuSelfTest.cs` | A `sun:` line after each preview; the self-test's sun |
| `Vista.Core/Scene/SunPlaceholder.cs`, `Vista.Tests/SunPlaceholderTests.cs` | Removed |
| `vista-views/*.recipe.json` | Schema 2, pinned to Zermatt on 2026-07-15 |
| `Vista.Tests/*` | `SolarPosition`, `SeasonCalendar` and `Almanac` tests; place cases in the recipe, CLI, sheet, pipeline, label and measurement-view tests; `SkyLight` against `DayNightCycle` |

**Next: M1.3** (spectral atmosphere: sky and sunlight), which makes dawn and dusk more than black.

**M1.3 — Spectral atmosphere: sky & sunlight.**
- *Goal:* replace the single-scattering Rayleigh dome with a spectral atmosphere on a spherical
  Earth, so the reddened low sun, twilight, the Earth's shadow and the Belt of Venus emerge (§5.2).
- *Deliverable:* Rayleigh, Mie haze (Henyey–Greenstein for now; spectral Mie is M4) and ozone
  absorption, with altitude profiles. Per-wavelength LUTs for the engine's 50 wavelengths in the
  style of Hillaire 2020 (transmittance, multiple scattering, and a sky-view LUT per render's sun),
  built by compute shaders, with a C# reference of each integrand. Sun next-event estimation goes
  through the transmittance from the shading point, the planet's shadow included; escaping rays
  read the sky-view LUT. The scene's base altitude becomes an input. `--atmosphere reference`
  path-traces the air as a medium (delta tracking, next-event estimation to the sun): the slow,
  unbiased check. Physical units and PNG auto exposure (decision above).
- *Verify:* CI: zenith optical depths against published values (Rayleigh ≈ 0.097 at 550 nm, ozone's
  Chappuis peak near 600 nm) and the LUT integrands. 3070: the LUT sky against the reference mode at
  sun elevations +30°, +5°, −2° and −6°, with the error per wavelength band recorded (target: a few
  percent). A sun-elevation sheet from −6° to +10° shows the red sun, the Earth's shadow and the
  Belt of Venus. Peaks stay lit red after the valley floor's sunset, from transmittance alone. The
  self-test covers the LUT build.

*M1.3 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` /
`RayTracer.Gpu` changed. `Vista.Tests` has 433 tests (388 before; the M0 `SkyLight` and its tests are gone). One
deviation from the deliverable as written: the multiple scattering is not Hillaire's. His isotropic Ψ_ms was built,
measured against the reference and replaced by Bruneton 2017's iterated scattering orders, because it missed the
target by a factor of two to four (decisions below). Transmittance and sky view stay in Hillaire's style.

*Checks (on the RTX 3070 unless noted):*

- **CI, against published values:** the Rayleigh zenith depth at 550 nm is 0.0969, from the refractive index of
  air (Peck & Reeder 1972), the King factor (Bates 1984) and the standard atmosphere's column, against the tabulated
  0.0973 (Bodhaine et al. 1999, Bucholtz 1995); the ozone cross-section on the engine's grid peaks at 606 nm (the
  Chappuis band), 0.040 at 602 nm for 300 DU. The integrands: zenith columns against the exact integral, sampled
  transmittance against a 20 000-step integral down to the horizon (0.5%), the single-scattering sky against a
  brute-force march (1%), the phase functions' integrals, the 4D maps round trip, a uniform field's density (each
  phase's integral, 1) and a uniform sky's irradiance (π).
- **Self-test:** the LUT build is bit-identical twice; texels of every GPU kernel (columns, direct and order-1 ground
  irradiance, 4D single scattering, order-2 density, order-2 radiance, ground irradiance, sky view) are within 2·10⁻³ of
  their C# reference fed the GPU's own input tables; a twilight render (sun at −4°) is lit by the sky alone; the
  reference atmosphere is bit-identical twice and under the 0.05 ms budget. The LUTs build in 0.82 s of GPU time.
- **The LUT sky against the reference** (`--atmosphere-check`): an observer 2 m above Zermatt's valley floor, 54
  directions (elevations 0.5–75°, 0–180° from the sun), 50 wavelengths, 2²⁰ reference paths per value. Mean |error|
  and worst error per band:

  | Sun | 400–460 nm | 460–510 nm | 510–560 nm | 560–600 nm | 600–660 nm | Luminance |
  |---|---|---|---|---|---|---|
  | +30° | 1.4% (−3.3) | 1.0% (−3.1) | 0.8% (−3.1) | 0.8% (−3.1) | 0.7% (−3.0) | 0.8% (−3.1) |
  | +5° | 1.1% (+2.4) | 0.9% (+2.0) | 0.8% (+1.5) | 0.7% (−1.2) | 0.6% (−1.2) | 0.8% (+1.3) |
  | −2° | 0.8% (−1.7) | 0.6% (−1.3) | 0.7% (−1.9) | 0.7% (+2.7) | 0.8% (+3.3) | 0.7% (−2.1) |
  | −6° | 1.4% (−4.2) | 1.5% (−5.0) | 1.8% (+6.1) | 2.4% (+10.1) | 2.5% (+9.5) | 1.5% (+6.2) |

  The reference's own noise is 0.03–0.2% by day and 1.1–1.4% at −6°. The worst errors sit at 0.5° above the horizon:
  toward the sun by day (the haze's lobe over the horizon), and in the dark anti-solar sky at −6°, the Earth's shadow.
- **Below the horizon** (`--below-horizon`, an observer 4.2 km up, where rays reach the land beyond the tile through a
  long path of air): mean luminance error 1.5% (+5°), 2.7% (−2°), 1.5% (−4°), 2.6% (−6°); worst −15%, at −2° just
  under the dipped horizon toward the sun. The bright twilight lowlands in the anti-solar sheet are therefore physical.
- **A sun-elevation sheet from −6° to +10°** (`vista-views/atmosphere`, from 4.2 km): toward the sun, the sun reddens
  and sets into an orange band that fades through twilight; away from it, the dark-blue Earth's shadow rises on the
  horizon under the pink Belt of Venus at −2° and climbs and fades by −4°. A LUT and a reference render of the anti-solar
  sky at −4° and +5° (the reference at 16 384 spp) show the same bands.
- **Peaks stay lit after the valley floor's sunset**, from transmittance alone: from the range camera, at a true sun
  elevation of −1° only the summits are lit, orange-red, above valleys in shadow, and at −1.5° only the highest. The
  sun's transmittance at three heights above the ground sphere (Zermatt's floor, 1608 m):

  | True sun elevation | 5 m | 1200 m | 2400 m |
  |---|---|---|---|
  | 0° | T₄₅₀ 0.001, T₆₅₀ 0.070 | 0.005, 0.179 | 0.014, 0.272 |
  | −0.5° | 0.000, 0.033 | 0.002, 0.123 | 0.007, 0.218 |
  | −1° | Earth's shadow | 0.000, 0.031 | 0.001, 0.107 |
  | −1.5° | Earth's shadow | Earth's shadow | 0.000, 0.019 |
  | −2° | Earth's shadow | Earth's shadow | Earth's shadow |

- **Time of day at Zermatt on 2026-10-15:** dawn meters at EV100 6.7, day 14.9, golden hour 14.0, dusk 5.6, and night
  is black (no light at all, so only the camera's EV is applied). M1.2's dawn and dusk were black.
- **Before/after:** the four measurement views, `main` (bb26be7) against this slice at 640 px: a deeper blue sky that
  darkens toward the zenith; a horizon glow and, from the aerial view, the land beyond the tile as a hazy plain; on the
  down-slope golden hour, blue sky-lit shadows and a pale, warm-white sunlit slope. That slope is right for the light (the
  8.5° sun reaches the 3.3 km camera with T = 0.36, 0.54, 0.70 at 450, 550, 650 nm: a mild warm cast, because almost no
  haze is left above 3 km) but over-exposed: the view is mostly shadow, so the meter lifts it 3.9 EV and the ACES curve
  bleaches the slope. M0's orange came from its 2400–3000 K placeholder. The camera's EV compensation recovers it; a
  highlight-aware meter is a candidate for the display transform.

*M1.3 decisions taken in code (revisit freely):*

- **Multiple scattering is Bruneton 2017's, measured.** Hillaire's Ψ_ms (64 directions, isotropic second scattering)
  came out 6.0% (+30°), 9.5% (+5°), 9.7% (−2°) and 12.8% (−6°) dark on average, worst −12% to −29% at the horizon. With
  the haze off it was still 3.3–4.4% dark, and neither more directions nor a zero ground albedo helped. Its second
  scattering is isotropic, and the haze's forward lobe picks up the bright horizon. Orders 2–5 in a 4D table
  (height, view, sun, view-to-sun) fixed it: the table stores the *phase-weighted incident radiance* per species,
  times the local coefficients at lookup, so it stays smooth where the haze's density falls off over 1.2 km.
- **The 4D sizes are measured.** 16 heights × 32 + 32 view angles (split at the horizon, never blended across it) × 64
  sun angles × 8 view-to-sun angles, a 32 × 32 sphere quadrature for the density. On the way: 16 sun angles left the
  twilight anti-solar sky +30–80% bright (light leaking across the terminator), 32 left −6° at +12–22%, 64 brought it to
  1.5%; 16 × 16 directions left a wavelength-flat −5% at the horizon (the haze lobe across the sky–ground edge);
  32 heights bought nothing over 16.
- **The 4D tables hold 11 of the 50 wavelengths** (every fifth hero and the last), interpolated in wavelength:
  multiple scattering is smooth in wavelength, and the full 50 took 5.1 s of GPU time and 210 MB for the sum alone.
  Now the build is 0.82 s, and the sum keeps 46 MB resident (115 MB more during the build).
- **Transmittance is a wavelength-free columns LUT:** 256 × 256 texels of the three density columns to the top
  (molecules, haze, ozone) on Bruneton's horizon map, 1024 steps each; `exp(−Σ coefficient · column)` gives any
  wavelength. One float4 per texel instead of 50 transmittances, and interpolating columns beats interpolating
  exponentials.
- **The sky view keeps the haze's phase out.** Each texel holds (Rayleigh with its phase + orders 2–5 + the ground) and
  the haze's single scattering without its phase; the lookup applies Henyey–Greenstein at the exact angle, so the
  aureole is sharper than any texel. 64 azimuths × (64 rows above the horizon, dense near it, + 32 below) × 13 slices:
  slice 0 at the camera's exact altitude for camera rays, 12 over the terrain's altitudes blended for rays that leave
  the terrain after a bounce.
- **Marches step quadratically from their dense end** (the start, or the ground for a ray that ends there): uniform steps
  of 780 m on a vertical ray left the haze's single scattering 1.3% low, and the 40-step 4D tables far worse.
- **The ground is a sphere at the scene's base altitude,** with the air's densities by altitude above sea level. The
  valley floor's horizon is then flat and its sunset the almanac's; peaks see the sun longer by their dip (1.6° at
  2400 m). The ground beyond the tile is Lambertian at a flat albedo of 0.2, lit by the sun and the sky. The base
  altitude is `VistaScene.GroundAltitudeMetres`, the place's valley floor (a custom latitude: sea level) until M1.4's
  altitude control.
- **The air's constants** (tier 5, `AtmosphereModel.Default`): Rayleigh from the refractive index of standard air with
  the scale height `RT/(Mg)` = 8435 m, so the column is the standard atmosphere's (Bruneton and Hillaire use 8 km and get
  τ₅₅₀ ≈ 0.108); haze of optical depth 0.1 at 550 nm from sea level, Ångström α 1.3, albedo 0.9, g 0.8, scale height
  1.2 km, so Zermatt sits above three quarters of it; 300 DU of ozone in Bruneton's 10–40 km tent. The M1.11 haze slider
  will move the optical depth.
- **Physical units.** Spectral radiance in W·m⁻²·sr⁻¹·nm⁻¹; Y is the ȳ-weighted mean spectral radiance, and 683 · 106.857
  times it is luminance in cd/m². The sun above the air is the engine's 5778 K blackbody at its real radius (1368 W/m²),
  with no limb darkening.
- **Auto exposure** (`AutoExposure`): the log-average luminance with the brightest and darkest 1% clamped, to 18% grey,
  plus the camera's EV as compensation; EV100 is recorded (daylight meters at 13–15). It meters a render of its own: the
  whole frame at 320 px, 16 spp, through the LUT sky, whatever the image's size, window or atmosphere mode. A preview, its
  4K final and every loupe of it are therefore exposed alike, a loupe stays a crop of the final, and a LUT and a reference
  render of one view can sit side by side. Night (no light) applies the compensation only.
- **The reference atmosphere** (`--atmosphere reference`) carries the hero wavelength alone (free paths depend on the
  wavelength), weighted by the companion count. Delta tracking against a majorant from the segment's lowest point;
  ratio tracking for every sun ray, the disk sampled; Rayleigh or Henyey–Greenstein phase sampling; a Lambertian ground
  sphere; Russian roulette after 4 events. Inside the air it ignores the terrain, as the LUTs do: it checks the LUTs'
  model, not a different one. Terrain segments carry no air in either mode until M1.4.
- **`sunElevation` (recipe schema 3, `--sun-elevation`)** sets the time by the sun's *geometric* elevation (twilight is
  defined on it: civil ends at −6°), rising when the time is dawn and setting otherwise; an elevation the sun never
  reaches that day takes noon or midnight with a hint. Sheets vary it as any recipe field.
- **Below the 4D tables' lowest sun (30° down) multiple scattering is zero,** not the edge texel's last glimmer, which
  auto exposure lifted into a blue night sky.
- **Sidecar schema 4** adds `atmosphere` (mode, model, base and camera altitudes, zenith depths from sea level and from
  the base, sources), `exposure` (method, compensation, metered luminance and EV100, the applied EV, the meter's size)
  and `image.units`; `sun` drops the M0 colour temperature for `spectrum`, `inEarthShadowAtCamera` and
  `transmittanceToCamera` at 450, 550 and 650 nm.
- **Shader plumbing:** `ShaderSource` inlines `#include` (one level, with `#line`) and prepends defines, since the engine's
  `ShaderCompiler` takes neither. `VISTA_SHADER_DEFINES=SINGLE_SCATTERING_ONLY` drops multiple scattering from both modes;
  it isolated Hillaire's error (single scattering agreed to 0.3%). LUT dispatches are cut at 2²⁰ items.

*M1.3 gotchas:*

- **`(1 − e^(−σ·dt))/σ` cancels in float:** at σ·dt = 10⁻⁶ it was 4% off. Below 0.01 it is now its series, in both C# and
  HLSL; the self-test's parity check found it on a 1 m texel at the ground.
- **Two tables, two wavelength counts:** the ground irradiance by order holds 11 wavelengths and the sky's ground
  irradiance 50. Dispatching the second with the first's count left most entries at zero, and the sky-view parity could
  not see it because its C# side read the same zeros. Only the ground-irradiance parity caught it.
- **A buffer must never be bound as SRV and UAV in one dispatch:** the LUT builders bind a dedicated dummy SRV to every
  unused input, never the dummy UAV.
- **TDR:** at 50 wavelengths each density dispatch took 1.2 s. Dispatches are now capped, and a whole build takes 0.82 s.
- **The reference at twilight is mostly zeros and rare bright paths:** images need thousands of samples and still
  speckle, and a 16-sample meter on it blew the exposure out (hence the LUT meter). Compare it in numbers, with
  `--atmosphere-check`.
- **A worktree of `main` for the "before" build** failed on Windows path length (the repository's deep Unity folders);
  `git worktree add --no-checkout` plus `git sparse-checkout set RayTracer` works.
- **C# local functions cannot capture a `ReadOnlySpan`;** the samplers take spans as parameters.

**M1.3 hand-off.** To understand M1.3, start by reading `Vista.Core/Atmosphere/AtmosphereLuts.cs` (the table sizes, the
sphere geometry and the sky march), then `Vista.Core/Atmosphere/AtmosphereScattering.cs` (the orders), then
`Vista.App/Gpu/D3D12VistaRenderer.Atmosphere.cs` (the build order and what each kernel reads), then the `SkyRadiance` and
`SunTransmittance4` lines of `Vista.App/Shaders/VistaIntegrator.hlsl`.

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png
    [--sun-elevation -2] [--atmosphere reference]
dotnet run --project Vista.App -c Release -- --atmosphere-check [--sun-elevations 30,5,-2,-6] [--paths 1048576]
    [--ground-altitude 1608] [--observer-height 2] [--below-horizon] [--ground-albedo 0.2] [--aerosol-depth 0.1] [--out check.json]
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --vary sunElevation=-6,-4,-2,0,2,4,6,8,10 --out s.png
powershell -ExecutionPolicy Bypass -File vista-views/atmosphere/render.ps1
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Atmosphere/AirOptics.cs`, `SolarSpectrum.cs` | Rayleigh, ozone and phase functions; the sun above the air |
| `Vista.Core/Atmosphere/AtmosphereModel.cs`, `AtmosphereFrame.cs`, `AtmosphereTables.cs` | The air, one render's geometry and slices, the per-wavelength tables |
| `Vista.Core/Atmosphere/AtmosphereLuts.cs`, `AtmosphereScattering.cs` | C# references: columns, ground irradiance, sky view; the 4D orders |
| `Vista.Core/Atmosphere/SkyCheck.cs` | The check's directions, bands and aggregation |
| `Vista.Core/Output/AutoExposure.cs`, `DisplayTransform.cs`, `Sidecar.cs`, `SheetLabels.cs` | Metering; schema 4; `sun` and `atmosphere` labels |
| `Vista.Core/Rendering/RenderTypes.cs`, `IntegratorTables.cs` | `AtmosphereMode`, the meter flag, the scene's air and base altitude; the M0 light table gone |
| `Vista.Core/Recipes/Recipe.cs`, `Astronomy/Almanac.cs`, `Astronomy/SolarPosition.cs` | Schema 3: `sunElevation`; `HoursAtElevation` |
| `Vista.Core/Cli/PreviewCommandLine.cs`, `PreviewPipeline.cs`, `SheetAxis.cs` | `--sun-elevation`, `--atmosphere`, the meter render, the `atmosphere` axis |
| `Vista.App/Shaders/VistaAtmosphere.hlsli`, `VistaAtmosphereLuts.hlsl` | The shared atmosphere functions; the LUT builders |
| `Vista.App/Shaders/VistaIntegrator.hlsl` | LUT lookups; the reference atmosphere; `CSSkyCheck` |
| `Vista.App/Gpu/D3D12VistaRenderer.cs`, `D3D12VistaRenderer.Atmosphere.cs`, `ShaderSource.cs` | New bindings; LUT pipelines, caching and the order iteration; includes and defines |
| `Vista.App/Gpu/AtmosphereCheck.cs`, `GpuSelfTest.cs`, `Program.cs` | `--atmosphere-check`; the LUT parity, twilight and reference checks |
| `Vista.Core/Rendering/SkyLight.cs`, `Vista.Tests/SkyLightTests.cs` | Removed |
| `vista-views/atmosphere/` | The check views and `render.ps1` |
| `Vista.Tests/*` | `AirOptics`, `AtmosphereLuts`, `AtmosphereScattering`, `AutoExposure`, `SkyCheck`, `SunElevationAndAtmosphere` tests; the check views |

**Next: M1.4** (aerial perspective & altitude). Its short march can read the columns LUT and the summed 4D orders
directly, and its error against the reference is one more `--atmosphere-check`-style comparison, now along finite
segments.

**M1.4 — Aerial perspective & altitude.**
- *Goal:* the layered, receding blue ridges: every segment, camera and bounce, carries the air
  between its ends.
- *Deliverable:* transmittance and in-scattered light along each finite segment from the LUTs,
  either as the sky-view difference (L(p) − T·L(q)) or as a short march through the transmittance and
  multiple-scattering LUTs, chosen by error against the reference mode and by cost. The terrain's
  altitude for the air includes the Earth's curvature drop (≈ 200 m at 50 km). The altitude control
  (§3.6) goes in. A known limit, measured here: air in a mountain's shadow is still lit (no
  crepuscular rays); fog volumes revisit it in M4.
- *Verify:* 3070: the error against the reference on a layered-ridges view and on a shadowed valley
  at golden hour; a sea-level against 3000 m sheet shows the deeper blue and thinner haze; the cost
  ledger records the first real cost jump since M0.

**M1.5 — Sun & sky importance sampling.**
- *Goal:* converge golden hour and twilight, where the sky carries most of the fill light.
- *Deliverable:* a sky distribution per render (a piecewise-constant 2D CDF over the sky-view LUT,
  luminance-weighted over the wavelengths), the sun disk as its own light, and multiple importance
  sampling (power heuristic) between BSDF, sky and sun sampling. Bounce rays that hit the sun disk
  now count, with their MIS weight. Russian roulette is re-measured.
- *Verify:* 3070: unbiased, because at 4096 spp the new estimator matches the old one within the
  per-pixel standard error (variance image); variance at equal spp on dawn, golden-hour and day
  views drops by a measured factor, with the time per sample recorded; the self-test passes.

**M1.6 — OIDN.**
- *Goal:* clean previews at low spp, and a clean final (§5.7).
- *Deliverable:* Intel Open Image Denoise through its C API (P/Invoke) behind a `Vista.Core` seam,
  fed the beauty plus the averaged albedo and normal. `--denoise` writes the denoised PNG/EXR beside
  the raw one; raw is always written (§11). A script fetches a pinned OIDN release into a git-ignored
  folder and checks its checksum: never committed, never run in CI. Without it a render warns and
  writes raw only. CPU device first; OIDN's GPU device is measured as an option. The sidecar records
  the OIDN version, device and filter.
- *Verify:* 3070: a raw | denoised | reference sheet (16 spp denoised against 4096 spp), with the
  error recorded; denoising the same input twice is bit-identical on the chosen device, or this file
  records that it is not. CI: the seam, with a fake denoiser.

**M1.7 — GPU erosion.**
- *Goal:* the realism lever (§4.3): valleys, ridges, talus and fans that read as weathered rock.
- *Deliverable:* hydraulic erosion on the grid (virtual-pipe water flow with sediment transport and
  deposition; droplet erosion is out, because its scattered writes need atomics) plus thermal
  erosion to a talus angle, as gather-only compute passes, with parameters per terrain profile. The
  grid's resolution and extent are chosen by measurement (target around 4097²; what lies beyond it
  for 50 km sight lines is part of that measurement). The world stage now runs on the device, before
  the mesh. Its outputs are reused later: flow accumulation, deposition, wetness and curvature. The
  C# reference, the cache and the sidecar hash as decided above.
- *Verify:* 3070: the eroded field is bit-identical across two runs and across a cache round trip;
  HLSL against the C# reference on a 65² grid within tolerance, in `--gpu-selftest`; a before/after
  sheet over four seeds; erosion time and VRAM per seed recorded. CI: the C# reference conserves
  water and sediment within tolerance, never yields NaN, and has a pinned hash.

**M1.8 — Watch mode.**
- *Goal:* keep the expensive state alive between previews, now that erosion costs seconds (§7.4).
- *Deliverable:* `--watch` keeps the device, compiled shaders, the eroded world, the mesh and its
  acceleration structures, and the atmosphere LUTs alive. It watches the recipe, camera and slider
  files and rebuilds only the stages a change touches (world ← set + seed; LUTs ← sun, date,
  altitude, haze; mesh ← world + camera; render ← everything). Each re-render writes the preview and
  a sidecar that says which stages were reused. The window drives this same session in M1.16.
- *Verify:* 3070: a scripted change sequence (exposure → time of day → camera → seed) rebuilds
  exactly the expected stages, and each watch render is bit-identical to a cold CLI render of the
  same files; the wall time per change is recorded.

**M1.9 — View-dependent terrain mesh.**
- *Goal:* final-resolution terrain detail inside the VRAM budget (§4.3, §7.1).
- *Deliverable:* a mesh per camera from the eroded field to a screen-space error in pixels at the
  output resolution, plus near-camera detail from position-only seeded noise (§4.1), crack-free. It
  stays conservative off screen, where geometry still casts shadows and shows in reflections.
  Whether the geometry bends with the Earth is settled here. Triangles, VRAM, build time and ns per
  pixel-sample are measured on fixed test views at 320 px and 4K.
- *Verify:* 3070: the measurement table goes into §4.3; a 4K loupe near the camera shows no facets
  beyond the error target; a view with the sun behind an off-screen peak keeps its shadow; a
  downsampled 4K render and the preview of the same camera show the same world (sheet); the
  self-test passes.

**M1.10 — Height-field intersection: the measured decision.**
- *Goal:* decide mesh against height-field intersection for far terrain, by measurement (§4.3,
  §9.8).
- *Deliverable:* a min/max quadtree height-field traversal for terrain beyond some distance
  (procedural tiles in the inline ray query loop, or a traversal of its own), measured against M1.9
  on the same views: VRAM, build time, ns per pixel-sample, and the image difference (the two must
  agree before their costs mean anything). The decision and its numbers go into §4.3. If the height
  field wins, it ships as the far-terrain path; if not, the PR carries only the numbers and the
  prototype stays on its branch.
- *Verify:* 3070: the numbers table and difference images; the self-test passes on whatever ships.

**M1.11 — Linked sliders & the weather chain.**
- *Goal:* one auto/pinned mechanism, with plausible defaults from the place and the season (§3.4,
  §3.6).
- *Deliverable:* the linked-slider model in `Vista.Core`: every slider is auto or pinned, reset
  re-links, pinning stops only that slider, set pieces push defaults, and contradictions give an
  "implausible" hint, never a block. Basic: season, time of day, snow, fog/haze. Advanced: location,
  altitude, date, temperature (°C, with °F on request), humidity. The weather chain: climate normals
  (decision above) → valley temperature with a daily curve (minimum near sunrise, maximum
  mid-afternoon) → humidity → freezing level (6.5 °C per 1000 m) → the snow default, with the season;
  haze density from humidity and the fog/haze slider (fog itself is M4). Custom latitude falls back
  to a latitude model. A slider file holds the pinned values; the sidecar records every slider's
  value and whether it was auto or pinned.
- *Verify:* CI: the chain (pinning temperature still lets snow follow; reset re-links; a set piece
  pushes its default; the implausible hint; autumn at Torres del Paine is April; Lofoten milder than
  its latitude model). 3070: a month sheet at one place changes sun and haze from the defaults alone.

**M1.12 — Materials by rule.**
- *Goal:* rock that reads as rock: strata, scree, soil, alpine grass and wet ground where nature
  puts them (§4.3).
- *Deliverable:* materials chosen and blended in the shader per hit, from slope, altitude, aspect
  and the erosion maps (flow, deposition, wetness), all procedural: rock strata as warped altitude
  bands, scree on deposition under steep faces, soil, grass as a material (its geometry is M5), and
  wet darkening. Built-in-tier spectra (tier 5, recorded per material). Rough-diffuse rock
  (Oren–Nayar-style) is measured against Lambert in a sheet, because backscatter from a low sun is
  the golden-hour look. The rules live in `Vista.Core`, with tests.
- *Verify:* CI: rule tests (steep → rock, deposition below a steep face → scree, …). 3070:
  material-id and tier previews over four seeds; a before/after beauty sheet; a cost-ledger row.

**M1.13 — Snow & glaciers.**
- *Goal:* snowcaps that read as real because of where the snow sticks (§4.5).
- *Deliverable:* snow cover from slope (shed above ~35–40°), aspect (poleward faces keep more,
  hemisphere from the latitude), wind and curvature (ridges scoured and hollows filled, from the
  erosion curvature), and altitude against the freezing level and the snow slider (M1.11).
  Recipe-owned glaciers and permanent snowfields for the mountain set pieces, carved at the world
  stage, so snow at zero leaves them. A dry / wet / melting state by altitude against the freezing
  level. A built-in snow spectrum from ice absorption and grain size (tier 5). Russian roulette is
  re-measured on bright snow (M0 note). Close-up snow stays in M7.
- *Verify:* CI: the placement rules. 3070: **bare steep faces**, because on the test views the snow
  fraction on faces steeper than 40° (read from the material-id output) stays under a threshold
  recorded here while ledges and gullies hold snow. **Alpenglow**, because a dawn sheet from −4° to
  +4° shows the peaks pink and red while the valley is in shadow, with no code that knows about
  alpenglow. A season sheet moves the snowline from the defaults alone.

**M1.14 — Auto camera v1.**
- *Goal:* good compositions for the mountain set pieces without placing a camera by hand (§4.2,
  §4.8).
- *Deliverable:* shot-first planting for snow-capped mountains and alpine range: a viewpoint and
  heading toward the main range, chosen with the sun's azimuth for side or back light. A few hundred
  candidates around it are scored from low-resolution G-buffer renders (depth, normal, material id)
  on: horizon near a third, foreground / midground / background layering, sun angle, sky fraction,
  the peaks in frame, nothing blocking the lens. Camera collision. `--auto-camera` in the CLI; the
  scores and the winner go in the sidecar.
- *Verify:* CI: scoring tests on synthetic G-buffers. 3070: an auto-camera sheet over eight seeds ×
  both set pieces against the default camera; the same pick twice; the scoring time recorded.

**M1.15 — Time & VRAM estimate.**
- *Goal:* honest estimates before a final render (pillar 7, §7.2).
- *Deliverable:* rays per sample and rays per second measured from the preview and from a handful
  of random loupe crops at the final resolution and detail. Time as a range, for fixed spp or for a
  noise target (from the preview's variance). A VRAM estimate from the planned allocations, checked
  against DXGI's budget, with a warning before a render that won't fit. The live ETA refines per
  dispatch during the final. `--estimate` in the CLI. The §7.1 table gets measured numbers.
- *Verify:* 3070: on at least six test views (sky-heavy, terrain-heavy, twilight, snow) the real 4K
  render time lands inside the estimate's own range, and peak VRAM within its stated margin. CI: the
  estimator's math.

**M1.16 — Preview & tweak window.**
- *Goal:* drive all of M1 by hand (§3.1).
- *Deliverable:* a WinForms window on the watch-mode session: a progressive preview that restarts
  and refines after each change; the set-piece picker (a new scene); seed and new seed; the basic
  sliders and the advanced panel (location dropdown, custom latitude, altitude, date, temperature
  with the °C/°F toggle, humidity), each showing auto/pinned with reset and the implausible hint;
  camera nudges in metres, pan / tilt / roll, focal length, focus, aperture and exposure, with
  collision; the auto-camera button; a debug-view picker; the estimate and "render final" (4K EXR +
  PNG + sidecar, live ETA). Recipe, camera and slider files save and load, so the window and the CLI
  share them.
- *Verify:* 3070: a converged window preview is bit-identical to a CLI preview of the files the
  window saves; the M1 checks walked through in the window, noted in the PR.

**M1.17 — M1 sign-off.**
- *Goal:* close M1 against its verification.
- *Deliverable:* the Vista golden suite at preview size (fixed seeds, committed PNG/EXR goldens,
  `--golden-check` with a tolerance, re-baselined deliberately) covering the M1 checks; the hero
  render "alpine range at golden hour" at 3840×2160 with its sidecar; the M1 status, decisions,
  gotchas and hand-off in this section.
- *Verify:* alpenglow at dawn and dusk with no special-casing; bare steep faces; the estimate inside
  its own range on the test views; goldens added and passing; `--phase6-selftest` 700/700 and
  `--phase6-regress` 19/19 (maze unmoved).

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
- **Render times are unknown beyond M0.** Bare M0 terrain costs 0.24–1.4 ns per pixel-sample
  on the 3070 (§10, "M0 batch defaults, measured"); M1–M5 add the expensive parts, and the
  §7.1 numbers are still guesses.
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
- **Which `RayTracer.Gpu` pieces serve an offline integrator** is settled (§8.1): only
  `ShaderCompiler` is reused, confirmed at runtime with integrator v0 on 2026-10-19.
- **Row-band dispatches at 16K** can overrun their budget by the sky-to-terrain cost ratio
  (~6× on M0 terrain); M11 must plan bands defensively (§10, "M0 batch defaults, measured").
- **Mass TLAS instancing** is new; today's TLAS holds a handful of instances.
- **Leaf alpha** needs non-opaque candidates in the inline ray query loop, slow on Ampere;
  real leaf geometry costs memory.
- **GPU determinism across driver updates** is not guaranteed; goldens may need
  re-baselining.
- **Eroded worlds depend on the GPU driver** (M1 decision: erosion stays on the GPU). The
  per-seed cache and the sidecar's height-field hash make a change visible, but a fresh machine
  or driver may erode the same seed slightly differently.
- **The agent loop needs the GPU.** Only the local RTX 3070 can render, so LLM look-dev
  iteration (§7.4) works only in a local session on the dev box. A CPU preview
  renderer would lift that, but contradicts the no-CPU-port decision (§9); revisit
  only if iterating from cloud sessions matters.
- **Small previews hide detail.** A 320 px preview cannot show leaf-level or
  edge-level problems, and denoising can hide noise; loupes and raw (undenoised)
  outputs cover that (§7.4).

**Product.**
- **Location list and climate data**: M1 hand-enters monthly normals for each dropdown place,
  with a source per place (§10, M1 decisions). Which public normals to use, and whether M2's
  manifest replaces them, is still open.
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
| Set pieces (2026-10-18) | One open-ended catalogue; a recipe is the set currently chosen (add / remove), plus one time of day and one season; order-free; empty = plain base ground; chosen before rendering, and changing the set starts a new scene; conflicting pieces (lake / frozen lake) never coexist, adding one swaps the other out |
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
| M1 strategy (2026-10-19) | All of the sky before the terrain; contact sheets + loupe first, tweak window last; erosion on the GPU, cached per seed with its hash in the sidecar; climate normals hand-entered for M1 (§10, M1 plan) |
