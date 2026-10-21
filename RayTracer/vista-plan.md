# Vista — a Spectral Path-Traced Landscape Wallpaper Generator

The single source of truth for the landscape-wallpaper effort. **M0 is done** (2026-10-19):
`Vista.Core` / `Vista.App` / `Vista.Tests` render a recipe + seed end to end on the RTX 3070
through offline integrator v0 (see **M0 status** and **M0 hand-off** in §10). M1 is planned as
seventeen slices in §10 (**M1 plan**); M1.1–M1.10 (iteration tools, astronomy, the spectral atmosphere, aerial
perspective, sun and sky importance sampling, OIDN denoising, GPU erosion, watch mode, the view-dependent terrain mesh, and the
measured decision to keep it for the far terrain) are done, M1.11 (linked sliders and the weather chain) is next. It sits
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
  wetness maps that later stages reuse (rivers, lakes, snow, vegetation). Built in M1.7 (§10):
  virtual-pipe water and thermal erosion over bedrock and loose material, on a 4097² × 8 m
  grid (32.8 km), cached per seed.
- **Geometry:** a 16k² height field as triangles (~500M) does not fit in 8 GB. Because
  the camera is fixed for a still, build a **view-dependent mesh once per camera** to a
  screen-space error target, conservative for off-screen geometry that still casts
  shadows or appears in reflections. Alternative for far terrain: a custom height-field
  intersection (min/max quadtree) in the inline ray query loop. Decide in M1 by measuring
  (M1.9, M1.10). M1.7 measured the stopgap, the uniform mesh of the whole eroded grid:
  33.6 M triangles, 805 MB plus a 2.35 GB BLAS. Beyond the grid, elevated views still see
  9–18% of the frame (§10, M1.7), so the far field belongs to M1.9/M1.10 too.
  **Built in M1.9** (§10, M1.9 has the table): a 4-8 mesh refined to 1 px of the frame's centre on screen and half the
  sun's angular radius off screen, over the eroded grid, a far field of base terrain at 32 m and 256 m out past the
  horizon, and seeded detail below the grid near the camera, crack-free by saturated errors, bent onto the Earth, with
  normal maps putting the grid's shading back on coarse triangles. At 4K a view takes 6.4–9.8 M triangles and
  1.0–1.3 GiB for the process (the uniform mesh: 33.6 M and 3.2 GiB); a 320 px preview 0.26–1.5 M. The far field is
  40–56% of an elevated view's triangles: what M1.10 measures a height-field path against.
  **Decided in M1.10** (§10, M1.10 has the tables): **the mesh stays the far terrain's path.** The height field — the far grids
  traced as procedural tiles in the inline ray query, the cells walked on the world's own grid and their triangles bent onto
  the Earth exactly — renders what the mesh converges to as its error target shrinks, and saves about 200 MiB of video memory
  and 0.4–1.2 s of build per 4K view, but costs 1.27–1.39× per pixel-sample on the views that show the far field (parity on
  those that do not), and a still pays for its samples. Revisit at M11 (8K, 16K), where the mesh's far field grows with the
  pixel count and the height field's does not; the prototype is on the branch `claude/vista-m1-10-heightfield`.
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
  fed albedo and normal buffers. Built in M1.6 (§10): OIDN 2.5.1, fetched and checksummed, never committed;
  `--denoise` writes the denoised image beside the raw one.
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

Measured in M1.7: the uniform mesh of the 4097² eroded grid takes 0.8 GB plus a 2.3 GB BLAS
(3.1 GiB for the process at render), twice the terrain line, until M1.9's view-dependent mesh;
erosion's own state (1.15 GiB) is freed before the mesh is built. Measured in M1.9: at 4K and 1 px the
view-dependent mesh takes 160–240 MiB plus a 430–650 MiB BLAS and 134 MB of normal maps, 1.0–1.3 GiB for the
process with its image buffers: inside the terrain line. Twice as fine (0.5 px) takes 1.7–2.3 GiB. M1.10's height-field
far field would take about 200 MiB of that away at 1 px (0.3–0.5 GiB at 0.5 px), for 1.27–1.39× the cost per sample; it stays
on its branch (§4.3).

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
  anywhere. Since erosion runs on the GPU (M1.7), `--dry-run` uses the cached eroded world when
  there is one and the un-eroded terrain otherwise, with a warning (§10, M1 decisions).

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
  far enough to measure it against the mesh on the same views. M1.10 measured it: the mesh stays (§4.3).
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
| M1.4 | 0.22 | 2.63 | 2.10 | 3.40 |
| M1.5 | 0.22 | 2.81 | 2.17 | 3.62 |
| M1.6 | 0.22 | 2.81 | 2.17 | 3.62 |
| M1.7 | 0.17 | 3.62 | 2.20 | 3.94 |
| M1.8 | 0.17 | 3.62 | 2.20 | 3.94 |
| M1.9 | 0.26 | 4.04 | 2.80 | 5.56 |
| M1.10 | 0.26 | 4.04 | 2.80 | 5.56 |

M1.1 changes no pixel: the committed views trace exactly M0's rays per sample (1.00, 2.42, 2.29, 3.27),
so the M1.1 row shows the run-to-run timing noise, about ±4%. Differences smaller than that mean nothing.
M1.2 moves the sun but adds no work: a second run gave 0.24, 0.69, 0.84, 1.28, and the views trace 1.00,
2.42, 2.29 and 3.23 rays per sample (the low sun now casts other shadows on the down-slope view).
M1.3 traces the same rays per sample as M1.2 (1.00, 2.42, 2.29, 3.23); a second run gave 0.22, 2.17, 1.42 and
2.00. The 1.7–3× on terrain is the atmosphere's lookups: every sun shadow ray now reads the columns LUT (four
float4 loads and four exponentials), and every ray that leaves the terrain after a bounce reads two sky-view slices
(32 float2 loads for its four wavelengths). Camera rays read one slice, so the sky view got no dearer. The exposure
meter (a 320 px render, §M1.3 decisions) is not in these numbers; it costs about 1/150 of a 4K frame.
M1.4 traces the same rays per sample again; a second run gave 0.21, 2.55, 2.06 and 3.39. The +21%, +41% and +48% on terrain
is aerial perspective, the first real jump since M0: every segment that ends on the terrain marches its air (up to 8 columns
lookups and two 16-corner reads of the scattering orders for its four wavelengths). The sky view costs what it did.
M1.5 was measured on a GPU shared with a game, so its row is M1.4's times the ratio of this build to `main` (d8f31ea) rendered
alternately, view by view, three rounds each: medians 0.98, 1.067, 1.035 and 1.066 (`main` read 0.23, 2.66, 2.14 and 3.58 in the
two steady rounds; in the third the game's load changed and both builds sped up). The views trace 1.00, 2.63, 2.49 and 3.77 rays
per sample (M1.4: 1.00, 2.42, 2.29, 3.23): the sky's shadow ray at a path's first surface. A sample costs 3–7% more on terrain at
4K, against 7–22% in the 320 px comparisons of M1.5's checks; neighbouring pixels' sky lookups and shadow rays are likely more
coherent at 4K. Russian roulette from bounce 2 pays back 7–10% of the new cost.
For timings on a shared GPU, `measure.ps1` alone is no longer enough: alternate the builds and compare ratios.
M1.6 changes no pixel of the raw image and no line of the integrator or its host, so its row is M1.5's: five views (the four
ledger views and dawn, 640 px, 16 spp) rendered by `main` (b1dac45) and by this slice are byte-identical, PNG and EXR. Denoising
is a separate cost after the render, in M1.6's checks: 7.1 s on the CPU and 0.39 s on CUDA for a 4K frame.
M1.7 changes the world (eroded, and 32.8 km instead of 16.4), so the views trace other rays: 1.00, 2.81, 2.47 and 3.77 per sample
(M1.6: 1.00, 2.63, 2.49, 3.77). Measured as M1.5 was, on a GPU 37–40% busy with other programs: this slice and `main` (45109372)
rendered alternately, view by view, three rounds; median ratios 0.751, 1.288, 1.015 and 1.089 (0.745–0.870, 1.263–1.318,
0.980–1.034, 1.022–1.093). The aerial view's +29% is terrain where M1.6 showed the grid's edge (13% of its frame lies beyond the
grid now, 21.5% before); the down-slope view's +9% the eroded ground under a low sun. The sky view's −25% is not explained: it traces
one camera ray per sample on both builds, and the terrain's height range only picks the sky-view slice; it is 0.06 ns a sample.
M1.8 changes no pixel and no line of the integrator, so its row is M1.7's: the four ledger views and a golden-hour view with every
debug output (640 px, 16 spp) rendered by `main` (b96f2e57) and by this slice are byte-identical, PNG and EXR, sixteen files.
M1.9 changes the geometry (the view-dependent mesh with its far field and detail, bent onto the Earth) and the shading (normal maps).
Measured as M1.5 and M1.7 were, on a GPU 16–34% busy with other programs: this slice and `main` (04bd3154) rendered alternately, view
by view, three rounds; median ratios 1.508, 1.117, 1.271 and 1.411 (1.495–1.538, 1.116–1.117, 1.270–1.272, 1.405–1.423). The views trace
1.00, 3.07, 2.51 and 3.89 rays per sample (M1.8: 1.00, 2.81, 2.47, 3.77): the far field turns rays that left past the grid's edge into
terrain. The normal maps are 5% of it (the same build with and without them, alternately: 1.00, 1.06, 1.05, 1.05); the rest is the far
field and, down the slope, the 3.7 M triangles of detail near the camera. The sky view's +0.1 ns is its camera rays crossing a BLAS whose
root now spans the far field.
M1.10 changes nothing on `main` but this plan (the height field stays on its branch), so its row is M1.9's. On the branch the mesh path
is byte-identical to `main` (the aerial view with every debug output and the down-slope view, 640 px, PNG and EXR).

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
  this branch are byte-identical pair by pair; `--sheet-from` tiles the eight and labels each pair by build
  (`vista-views/sheets/m1-1-before-after.png`).
- **Sheets headless:** a four-seed sheet (2.7 s wall, four worlds) and a five-cell time-of-day sheet with
  a normal-map sheet (2.0 s wall, one world) rendered from one `Vista.App` call each
  (`vista-views/sheets/m1-1-seeds.png`, `m1-1-time-of-day.png`, `m1-1-time-of-day-normals.png`). A 4K loupe of a peak
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
  640×360, 64 spp, default camera), one `Vista.App --sheet` call (`vista-views/sheets/m1-2-time-of-day.png`):

  | Preset | Local solar time | Azimuth | Elevation |
  |---|---|---|---|
  | dawn (sunrise − 20 min) | 06:10:53 | 97.9° | −4.17° |
  | day (solar noon) | 12:00:00 | 180.0° | 35.38° |
  | golden hour (sunset − 60 min) | 16:28:23 | 247.3° | 9.21° |
  | dusk (sunset + 20 min) | 17:48:23 | 261.9° | −4.18° |
  | night (solar midnight) | 00:00:00 | 0.0° | −52.41° |

  Sunrise 06:30:53 and sunset 17:28:23 solar on 2026-10-15 are NOAA's to the second. Dawn, dusk and night
  render dark, as planned: the M0 dome has no twilight until M1.3. A solar-time sweep (07:00 to 17:00 from
  the aerial view) shows the sun crossing east, south and west (`m1-2-solar-time.png`); a golden-hour sheet
  over four places on the same October date shows the sun at 4.3° at Lofoten against 10.7° at Yosemite (8.1°
  at Torres del Paine in April, its autumn) (`m1-2-places-golden-hour.png`).
- **Self-test:** `--gpu-selftest` passes (the self-test scene now has Zermatt's July golden-hour sun).
- **Before/after:** the four measurement views, `main` (370690d) against this slice, tiled with
  `--sheet-from` (`vista-views/sheets/m1-2-before-after.png`). The views now pin Zermatt on 2026-07-15. The noon sun stands due south at 65.5°, behind the
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

*M1.4 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` / `RayTracer.Gpu` changed.
`Vista.Tests` has 460 tests (433 before). Aerial perspective is a short march through the LUTs, chosen over the sky view's
difference by error and by cost. The new check along finite segments found three errors in M1.3's LUT model near the ground
and one in its ground irradiance; all are fixed here (decisions below), so M1.3's sky check was rerun and its new numbers are
below too.

*Checks (on the RTX 3070 unless noted):*

- **The LUT air along segments against the reference** (`--atmosphere-check --segments`): the light the air scatters toward
  an observer along segments of 1, 4 and 16 km, up from 2 m above Zermatt's valley floor (M1.3's 54 directions above the
  horizon) and down from 1.7 km above it (its 36 below-horizon directions; a segment that reaches the ground sphere ends
  there), 50 wavelengths, 2²⁰ reference paths per value. Mean |luminance error| (worst):

  | Sun | Up 1 km | Up 4 km | Up 16 km | Down 1 km | Down 4 km | Down 16 km |
  |---|---|---|---|---|---|---|
  | +30° | 0.8% (−2.7) | 0.8% (−2.7) | 0.8% (−2.7) | 0.5% (−1.6) | 0.6% (−1.6) | 0.8% (−2.4) |
  | +5° | 1.1% (+4.5) | 1.2% (+4.3) | 1.4% (+4.2) | 0.6% (+2.3) | 0.7% (−2.8) | 1.0% (−2.8) |
  | −2° | 1.8% (−5.2) | 1.7% (−5.6) | 2.0% (−5.7) | 1.0% (+3.8) | 1.1% (+4.1) | 1.1% (+3.3) |
  | −6° | 4.7% (+13) | 3.8% (+12) | 3.7% (−10) | 4.7% (+17) | 2.7% (+7.3) | 2.8% (−6.9) |

  At −6° the reference's own noise (1.6–6.0%) is most of the number. Single scattering alone (`SINGLE_SCATTERING_ONLY`) is
  within 0.2%, the reference's noise floor, at every length; what remains is the scattering orders' error: worst toward the
  sun just over the horizon (the haze's lobe, as in the M1.3 sky) and, in twilight, looking up toward the sun with haze.
- **One view three ways** (`--atmosphere-compare`, 320×180; the LUTs at 1024 spp, the reference and the reference with the
  terrain's shadow in the air at 16 384 spp; seed 42). Mean luminance by distance to the first hit, LUTs ÷ reference − 1 |
  shadowed ÷ reference − 1 (± the noise of the means):

  | Distance | Layered ridges, noon | Layered ridges, golden hour, toward the sun | Shadowed valley (down-slope), golden hour |
  |---|---|---|---|
  | 0–1 km | | | +0.01% \| −0.05% |
  | 1–2 km | | | +0.15% \| −2.4% (±0.15) |
  | 2–4 km | −0.03% \| 0.00% | −0.38% \| −4.4% (±0.16) | +0.11% \| −7.6% (±0.06) |
  | 4–8 km | −0.01% \| 0.00% | −0.25% \| −8.1% (±0.06) | +0.02% \| −10.0% (±0.17) |
  | 8–16 km | −0.02% \| 0.00% | +0.71% \| −5.4% (±0.04) | |
  | sky | −0.04% \| 0.00% | +1.00% \| 0.00% | |

  The LUTs evaluate their model to within 0.4% on terrain (+0.7% beyond 8 km into the low sun, +1.0% in that sky). The
  **known limit** is the third column of each pair: the air in a mountain's shadow, which the LUTs keep lit, would make the
  valleys at 2–8 km 4–10% darker at golden hour. At noon the shadows touch none of the air the camera sees. A side-lit golden-hour
  view (the sun behind the camera's left, across the range) loses only 1.1–1.3%. M4's fog volumes revisit it.
- **Sea level against 3000 m** (`vista-views/aerial`, the layered ridges at noon, `--vary altitude=0,3000`): at 3000 m the sky
  is a deeper blue, the horizon haze thinner and the far ridges clearer. The zenith depths from the base fall from 0.097
  (Rayleigh, 550 nm) and 0.100 (haze) to 0.068 and 0.008; the sun reaches the camera with T₄₅₀ 0.83 → 0.89.
- **The cost ledger** has the first real jump since M0: +21%, +41% and +48% on the terrain views over M1.3 (the M1.4 row).
- **Self-test:** passes, and grows: the aerial perspective of five world segments (down from the camera, a short bounce off
  to the side, a long grazing one across the tile, one up a slope) at three wavelengths matches its C# reference fed the
  GPU's own LUTs — transmittance, in-scattering and the bent geometry; the shadowed reference is sane and bit-identical under
  the 0.05 ms budget. Every M1.3 parity check still passes on the changed tables. The LUTs build in 0.87 s (0.82 s).
- **CI:** 460 tests. The curvature drop (196.5 m at 50 km), the bend (heights kept, verticals tilted by s/R), the chord ending
  at exactly the far point's height and dipping by s/2R toward a level point; the march's transmittance against a fine
  integral (0.2%) and its single scattering against a brute-force march (0.5%); the penumbra; the density quadrature (covers
  the sphere exactly, never straddles the horizon); the ground irradiance's sun map; the 4D view map's round trip and its
  zenith and nadir coverage near the ground; the altitude control (recipe, CLI, pipeline, sidecar, sheet conflicts); the
  frame's reach and `SunReachesScene`; the comparison's bins; the committed view.
- **Before/after:** the four measurement views, `main` (3bf4c77) against this slice at 640 px, 256 spp. The sky view is
  unchanged to the eye. From the aerial view the far range recedes into blue haze and the shadows turn blue; from the default
  camera the far peaks pale; in the down-slope view a veil of lit air lifts the shadowed valley (the known limit, in a view).
- **M1.3's sky check again** (`vista-views/atmosphere/render.ps1`), with M1.4's tables. Mean |error| (worst) per band:

  | Sun | 400–460 nm | 460–510 nm | 510–560 nm | 560–600 nm | 600–660 nm | Luminance |
  |---|---|---|---|---|---|---|
  | +30° | 1.0% (−2.8) | 0.6% (−2.6) | 0.5% (−2.4) | 0.4% (−2.3) | 0.4% (−2.2) | 0.4% (−2.4) |
  | +5° | 0.8% (+3.4) | 0.6% (+3.5) | 0.5% (+3.4) | 0.6% (+3.5) | 0.7% (+3.4) | 0.5% (+3.4) |
  | −2° | 0.7% (−1.2) | 0.6% (−1.2) | 0.5% (−1.2) | 0.7% (+3.0) | 1.1% (+4.8) | 0.5% (+2.0) |
  | −6° | 1.2% (+2.6) | 1.2% (+4.0) | 1.7% (+6.5) | 2.8% (+11.5) | 3.3% (+13.1) | 1.6% (+7.1) |

  Below the horizon from 4.2 km (2¹⁸ paths): mean luminance error 0.4% (+5°), 0.8% (−2°), 0.8% (−4°), 2.1% (−6°), against
  M1.3's 1.5%, 2.7%, 1.5% and 2.6%; the worst, −15% at −2°, is now −5.4% (−10.6% in the red band). Above the horizon the
  means fell (+30°: 0.8% → 0.4%) except at −6°, where 1.5% became 1.6% and the red bands rose by about a point: a fixed
  32-angle quadrature that converges to the same numbers at 48 angles shows M1.3's −6° figure was an error cancelling the
  horizon bug below, not accuracy.

*M1.4 decisions taken in code (revisit freely):*

- **The march, not the sky view's difference.** `L(a) − T·L(b)` was built (`AERIAL_SKY_DIFFERENCE`, kept as a diagnostic
  define) and measured: looking down, where the check's slices cover the far ends, it was off by 2.3–6.2% by day and 45–58%
  at −2° (near the horizon up to +970%: two large, nearly equal LUT values subtracted); looking up the check could not give it
  slices at the far ends, so those numbers say nothing. Nor was it cheaper: 3.98 ns against 2.78 on the aerial view, because a
  bounce's sky reads two terrain slices per end. The march reads the same tables the sky view is built from.
- **The march** (`AerialSegment`; C# `AerialPerspective.Integrate`): 2 steps plus one per kilometre, at most 8, dense at the
  segment's lower end; extinction from the analytic densities; single scattering through the columns LUT; multiple scattering
  as the summed orders' phase-weighted radiance read at the two ends and blended along the segment, times the local densities.
  Measured: single scattering at 8 steps is within 0.2% for every segment the terrain can make (48 steps change nothing);
  reading the orders at every step instead of the ends changes nothing either; and a segment that climbs or drops less than
  250 m reads them once, in its middle, which saves 5%, 11% and 17% on the terrain views for no measurable error (reading every
  segment once doubled the error of long ones that climb, 1.8% → 3.5% at −2°). Cost per segment is two 16-corner lookups of
  four wavelengths plus up to 8 columns lookups: before the single-read rule the orders were 0.6–1.6 ns of the ledger and the
  steps 0.3–0.5 ns.
- **The world is bent onto the Earth for the air, around the camera** (`Bend`, `SegmentBetween`; C# `AerialPerspective.Bend`,
  `Between`). A world point keeps its height above the ground sphere and sits at its horizontal distance from the camera along
  the sphere, so it sinks by `2R·sin²(s/2R)` and its vertical tilts by `s/R`. A segment is the straight chord between the bent
  ends, seen from its start: it ends at exactly the far point's height, not 200 m higher at 50 km as a flat ray through
  spherical air would put it. The surface's sun takes its own tilted vertical (its transmittance and the Earth's shadow), as the
  air just above it does; rays that leave the terrain still read the sky view in the camera's frame, which is built for one
  vertical (under 0.1° of tilt across today's tile). `SunReachesScene` counts the terrain's reach from the camera. Whether the
  geometry itself bends stays M1.9's question. (It does since M1.9: the mesh is built in the bent frame, `EarthFrame`.)
- **The 4D tables' view axis is by angle** (C# `TexelToSample`, `CellOf`): split at the ground sphere's horizon, each half
  dense toward it, as the sky view's rows are. Bruneton's map by distance put every texel of the ground half within 0.5° of the
  horizon near the ground (at 10 m: texel 0 straight down, texel 1 1.5° below the horizon) and left the zenith a texel apart
  from 49°: downward segments that end near the ground were 8.8% off (worst −30%) at −2°, 3.5% at +5°. By angle: 1.1% and 1.0%.
- **The scattering density's quadrature splits at the dipped horizon** (`DensityPolar`, `DensityScales`): 16 polar angles
  above the ground sphere's horizon and 16 below, each half dense toward it, its weights scaled so a uniform field integrates
  exactly (the warped midpoint rule alone overshoots by 0.2%). A fixed grid of 32 had one angle straddling the horizon: a few
  kilometres up, the twilight glow lies in the 2° between the horizontal and the dipped horizon, and the grid called it ground.
  With the point sun (next) it left upward twilight segments 6–9% dark; the split took the worst from −8.8% to −6.7% and the
  middle elevations into the noise. Uniform halves did worse than warped ones (the −6° sky 3.2%, below the horizon 4.7%);
  24 + 8, 24 + 16 and 32 + 16 gave the same numbers as 16 + 16.
- **The sun is a disk in the Earth's shadow** (`SunVisibility`, `SunLight`): the fraction of its 0.53° disk above the ground
  sphere's horizon, its light taken along the horizon while the centre is below it. The penumbra is kilometres deep near the
  terminator (2.9–4.9 km up with the sun 2° down); with a point sun a 4 km segment climbing 75° into it was 6.7% dark against
  the reference, which samples the disk, and 3.0% with the disk. Surfaces already sampled the disk.
- **The ground irradiance sits on the sun map** (`GroundIrradianceMuS`): the 4D tables' μ_s map, dense near the horizon, 128
  entries as before. Evenly spaced cosines were 0.9° apart there, where twilight irradiance changes several-fold per degree, and
  the overshoot lit the twilight ground 2–3% too bright; 1024 even entries confirmed it but cost 8× the sky view.
- **What the earlier slices measured and M1.4 did not change:** 16 heights (32 changed nothing), 32 + 32 view angles (64 per
  half helped only at 50° up), 11 table wavelengths (all 50 helped the red twilight band by 0.7 points at 5× the cost), 5 orders
  (8 changed 0.4%).
- **The reference along segments** (`AirPath`): delta tracking along the chord up to the surface; if it scatters first, the
  rest of the path runs in the air alone, as the LUTs assume, and `--atmosphere reference` checks the LUTs' model.
  **`--atmosphere referenceShadowed`** (`REFERENCE_TERRAIN_SHADOWS`) lets the terrain hide the sun from each path's first event
  in the air, which lies on a segment of open air. Only the first: later events of a path that ignores the terrain may sit
  under the mountains or on the ground sphere beneath the tile, and shadowing them darkened even the noon sky by 3%.
- **The altitude control** is the recipe's `altitude` (schema 4, `--altitude`, 0–8000 m): the scene's base altitude, world
  `y = 0`, instead of the place's valley floor (`Places.BaseAltitude`); the terrain keeps its shape and the whole scene moves in
  the air. M1.11's linked sliders make it auto or pinned. Sheets vary it as any recipe field.
- **Sidecar schema 5** adds `atmosphere.groundAltitudeFrom` and `atmosphere.aerialPerspective` (how the air along segments was
  evaluated in that mode).
- **`--atmosphere-compare`** bins pixels by sample 0's first-hit distance (0–1, 1–2, 2–4, 4–8, 8–16, 16+ km, sky) and compares
  the bins' mean luminance with the noise from each render's variance output; means over thousands of pixels show a reference
  render's bias without needing it converged per pixel.

*M1.4 gotchas:*

- **The GPU's `sin` has an absolute error near 10⁻⁶.** Times the Earth's radius that is metres: the parity probe saw a 360 m
  bounce chord turn by 0.04°. `Bend` uses the series of sin θ/θ, cos θ and the drop instead (θ stays under 0.01).
- **A mean |error| includes the reference's noise:** at −6° it is most of the number; compare the noise column before reading
  a twilight figure.
- **The sky-view difference needs slices at both ends.** `--atmosphere-check --segments` spans the terrain slices over the
  observer's height only, so its upward segments end above every slice: those numbers are not a test of the difference.
- **`SINGLE_SCATTERING_ONLY` with the observer in the Earth's shadow** makes a band's reference zero everywhere; the check used
  to crash on it (an empty `MaxBy`) and now leaves the band out.
- **A pinned define that is never re-pinned** (`#define X 16u` without `#ifndef`) cannot be swept with `VISTA_SHADER_DEFINES`;
  the table sizes are pinned in both C# and HLSL, so a sweep edits both and rebuilds.

**M1.4 hand-off.** To understand M1.4, start by reading `Vista.Core/Atmosphere/AerialPerspective.cs` (the bend and the march),
then the `AerialSegment` and `SunVisibility` functions of `Vista.App/Shaders/VistaAtmosphere.hlsli`, then the lines after
`TraceClosest` in `Vista.App/Shaders/VistaIntegrator.hlsl` (where every segment gets its air, in both modes), then
`DensityPolar` and `TexelToSample` in `Vista.Core/Atmosphere/AtmosphereScattering.cs` (the table changes).

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png [--altitude 3000]
    [--atmosphere luts | reference | referenceShadowed]
dotnet run --project Vista.App -c Release -- --atmosphere-check --segments 1000,4000,16000 [--observer-height 1700 --below-horizon]
dotnet run --project Vista.App -c Release -- --atmosphere-compare --recipe r.json --camera c.json [--spp 1024]
    [--reference-spp 16384] [--out dir]
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --vary altitude=0,3000 --out altitude.png
powershell -ExecutionPolicy Bypass -File vista-views/aerial/render.ps1
VISTA_SHADER_DEFINES=AERIAL_SKY_DIFFERENCE   (the measured alternative, in renders and the check)
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Atmosphere/AerialPerspective.cs` | C# reference: the bend, the chord, the march |
| `Vista.Core/Atmosphere/AtmosphereScattering.cs` | The 4D view axis by angle; the split, warped, normalised density quadrature; the sun map for the irradiance by order |
| `Vista.Core/Atmosphere/AtmosphereLuts.cs` | `SunVisibility` / `SunLight` (the penumbra); the ground irradiance on the sun map |
| `Vista.Core/Atmosphere/AtmosphereFrame.cs`, `Rendering/RenderTypes.cs` | The terrain's reach; `VistaScene.FrameFor`; `AtmosphereMode.ReferenceShadowed` |
| `Vista.Core/Atmosphere/SkyCheck.cs`, `RenderComparison.cs` | Segment lengths in the check; the by-distance comparison |
| `Vista.Core/Recipes/Recipe.cs`, `Astronomy/Places.cs` | Schema 4: `altitude`; `Places.BaseAltitude` |
| `Vista.Core/Cli/PreviewCommandLine.cs`, `PreviewPipeline.cs`, `Output/Sidecar.cs` | `--altitude`, `referenceShadowed`; the base altitude; schema 5 |
| `Vista.App/Shaders/VistaAtmosphere.hlsli` | `AerialSegment`, `LookupWeighted4`, `SunVisibility`; the view map by angle; the irradiance on the sun map |
| `Vista.App/Shaders/VistaAtmosphereLuts.hlsl` | The split density quadrature; the penumbra in single scattering and the sky march |
| `Vista.App/Shaders/VistaIntegrator.hlsl` | `Bend`, `SegmentBetween`; the air on every segment; `AirPath` with surfaces and terrain shadows; the segment check; `CSAerialProbe` |
| `Vista.App/Gpu/D3D12VistaRenderer.cs`, `D3D12VistaRenderer.Atmosphere.cs` | The summed orders bound to the integrator; the shadowed pipeline; the probe; segment lengths |
| `Vista.App/Gpu/AtmosphereCheck.cs`, `AtmosphereCompare.cs`, `GpuSelfTest.cs`, `Program.cs` | `--segments`; `--atmosphere-compare`; the aerial parity and shadowed checks |
| `vista-views/aerial/` | The layered-ridges camera and `render.ps1` |
| `Vista.Tests/*` | `AerialPerspective`, `Altitude` and `RenderComparison` tests; schema, CLI, sky-check and view additions |

**Next: M1.5** (sun & sky importance sampling). Its sky distribution is built over the sky-view LUT these checks now cover,
and its MIS will want the march's cost in mind: aerial perspective is now up to a third of a terrain view's time.

**M1.5 — Sun & sky importance sampling.**
- *Goal:* converge golden hour and twilight, where the sky carries most of the fill light.
- *Deliverable:* a sky distribution per render (a piecewise-constant 2D CDF over the sky-view LUT,
  luminance-weighted over the wavelengths), the sun disk as its own light, and multiple importance
  sampling (power heuristic) between BSDF, sky and sun sampling. Bounce rays that hit the sun disk
  now count, with their MIS weight. Russian roulette is re-measured.
- *Verify:* 3070: unbiased, because at 4096 spp the new estimator matches the old one within the
  per-pixel standard error (variance image); variance at equal spp on dawn, golden-hour and day
  views drops by a measured factor, with the time per sample recorded; the self-test passes.

*M1.5 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` / `RayTracer.Gpu` changed.
`Vista.Tests` has 486 tests (460 before). Two choices were made by measurement: only a path's first surface samples the sky
(deeper surfaces leave it to the bounce, for no measurable loss), and Russian roulette now starts at bounce 2. One deviation
from the verify as written: the per-pixel standard error comes from the spread between independent renders, not from the
variance image, which overstates it (gotchas). M1.4's estimator stays one option away: `--light-sampling sunOnly --rr-start 3`
renders M1.4's images bit for bit (checked on three views against `main`, d8f31ea).

*Checks (on the RTX 3070 unless noted):*

- **Unbiased** (`--sampling-compare`, 320×180): each estimator rendered 32 times at 128 spp, every render on its own random
  stream, 4096 spp each in all; per pixel, z is the difference of the two means over its standard error from the spread
  between renders. The null test, MIS against itself on other streams, shows what agreement looks like in these statistics:

  | View | Sun | Image mean, M1.5 ÷ M1.4 − 1 | RMS z | \|z\| > 3 |
  |---|---|---|---|---|
  | Dawn, default camera | −3.6° | +0.001% ± 0.001% | 1.012 | 0.36% |
  | Golden hour, down a slope | 8.5° | +0.001% ± 0.001% | 1.018 | 0.38% |
  | Golden hour, default camera | 8.5° | 0.000% ± 0.001% | 1.019 | 0.35% |
  | Day, default camera | 65.5° | 0.000% ± 0.000% | 1.014 | 0.37% |
  | Null test: MIS against MIS, golden hour down a slope | 8.5° | +0.002% ± 0.001% | 1.019 | 0.37% |

  A Student t with the renders' 62 degrees of freedom has an RMS of 1.016 and \|t\| > 3 in 0.39%; every comparison sits where
  the null test does. The mean z (−0.005 to +0.025; the null test +0.011) is not the test: M1.4's per-pixel noise is skewed
  (rare bright bounces toward the glowing sky), and skew moves a t-statistic's mean without any bias. The image's mean, a sum
  over 57 600 pixels, is the test.
- **Variance at equal samples, and the time per sample** (the same renders: the variance between renders of 128 spp, summed
  over the terrain away from the skyline; "relative" divides each pixel's variance by its mean squared, so a dark shadow counts
  as much as a bright slope):

  | View | Variance, M1.4 ÷ M1.5 | Relative | ns per sample at 320 px, M1.4 → M1.5 | Efficiency, 1 / (variance × time) |
  |---|---|---|---|---|
  | Dawn | 1.69× | 1.68× | 2.83 → 3.03 | 1.57× |
  | Golden hour, down a slope | 2.27× | 1.28× | 3.40 → 4.15 | 1.86× |
  | Golden hour, default camera | 1.53× | 1.36× | 3.06 → 3.35 | 1.40× |
  | Day | 0.98× | 0.98× | 2.82 → 3.10 | 0.89× |

  The day gains nothing: the sun dominates its light, and the sky's sample costs 10%. At 16 spp and 640 px the factors are the
  same (1.70×, 2.46×, 1.77×, 0.99×).
- **Russian roulette re-measured** (MIS, the same renders; efficiency against the start at bounce 2):

  | Start | Dawn | Golden hour, down a slope | Golden hour, default | Day |
  |---|---|---|---|---|
  | 0 | 0.23 | 0.43 | 0.63 | 0.19 |
  | 1 | 0.49 | 0.96 | 1.02 | 0.89 |
  | **2** | 1 | 1 | 1 | 1 |
  | 3 (M1.4) | 0.92 | 0.93 | 0.93 | 0.91 |
  | 4 | 0.90 | 0.89 | 0.91 | 0.91 |
  | none | 0.89 | 0.90 | 0.90 | 0.87 |

  Starting at 2 instead of 3 adds under 0.6% of variance at equal samples and saves 7–10% of the time per sample. At 1, the
  second surface, which does not sample the sky, loses its bounce (and with it the sky) to roulette about 80% of the time at
  this albedo: dawn's variance rises 2.4×.
- **Self-test:** passes, and grows. The sky distribution rebuilt for the same frame is bit-identical (measurement and CDFs);
  `CSSkyLuminance` matches `SkyDistribution.CellLuminance` on the read-back sky view for 30 (cell, slice) pairs; `CSSkyProbe`'s
  sampler (4096 random triples: direction and pdf) and pdf and cell lookup (4096 random directions) match `SkyDistribution`;
  and 8 renders × 16 spp of each estimator agree on the image's mean (−0.027% ± 0.015%). Every earlier check now runs with MIS
  and still passes: bit-identical twice, under the 0.05 ms budget, and in a loupe. The distribution costs 5–7 ms of GPU time and
  10–15 ms in all per sky (the LUTs: 0.89 s).
- **CI:** 486 tests. The distribution: band edges exact at the poles and the horizon; CDFs from 0 to 1 with every cell's chance
  positive; the pdf integrates to 1; a uniform sky's pdf above and below the horizon; a dark sky's uniform fallback; a glow's
  share and the floor; every sample landing in its cell with the pdf `Pdf` gives; the mirror halves; an importance-sampled
  integral against its exact value; the cell points' stratification; `CellLuminance` on synthetic LUTs; bad input. The
  comparison: replicate statistics, standard-normal z for equal means, half the noise as a quarter of the variance, a 1% bias
  found, only terrain counted, the skyline mask. The plumbing: the settings' defaults, `--light-sampling`, `--rr-start`, the
  sheet axis, the schema-6 sidecar, the reference recorded as sun-only, the labels.
- **The cost ledger:** the M1.5 row, 0.22, 2.81, 2.17 and 3.62 ns: 3–7% over M1.4 on terrain at 4K (measured against `main`
  alternately on a shared GPU; see the note under the ledger). The efficiencies above use the 320 px cost, the higher one, so at
  4K they are, if anything, larger.
- **Before/after:** the four measurement views and the dawn view, `main` (d8f31ea) against this slice at 640 px and 16 spp:
  dawn's speckled slopes and the golden hour's shadowed valley come out smooth, the day views look alike, and the sky view
  (camera rays only) is unchanged. `vista-views/sampling/render.ps1` also writes a 16 spp pair of each view.

*M1.5 decisions taken in code (revisit freely):*

- **Two lights and the bounce.** The sun's disk (at every surface, uniform in its cone) and the sky (at the first surface, from
  the distribution) are separate lights, each weighed against the cosine bounce by the power heuristic; the sky's sample does
  not look for the disk, nor the sun's for the sky. The disk is 6.8·10⁻⁵ sr, so the sun's weight is 1 to float precision; a
  bounce that hits the disk now counts, with its weight of about 5·10⁻¹⁰.
- **The bounce's density in the weights is its real one:** cos/π times its Russian-roulette survival (known before its
  direction is drawn), and zero when no bounce follows, where the lights carry everything. Any weights that sum to one are
  unbiased; these are the balanced ones.
- **The distribution** (`SkyDistribution`; `VistaSkyLight.hlsli`): 128 bands of zenith angle (1.41°, the horizon an edge) × 64
  columns of azimuth from the sun (2.81°), a cell standing for both mirror halves. A cell weighs its mean luminance — Σ ȳ·E·(smooth
  + HG·haze) at 4 × 4 points, the haze's phase taken at each point so the aureole is resolved, averaged over the 12 terrain slices
  the bounce rays read — times its solid angle. Measured on the GPU (`CSSkyLuminance`, a thread per cell and slice, gather-only),
  averaged and turned into CDFs on the CPU, uploaded; rebuilt with the sky view. 2% of the probability is uniform over the sphere.
- **Below the horizon a cell weighs a quarter** (`BelowHorizonWeight`): there the sky view holds the land beyond the tile, which
  the terrain mostly hides from a surface. Measured variance reductions (dawn, golden hour down a slope, golden hour default, day):
  weight 1: 1.64, 2.00, 1.45, 1.02×; ¼: 1.71, 2.27, 1.53, 0.98×; 0: 1.73, 2.42, 1.54, 0.92×; (1 + cos θ): 1.68, 2.09, 1.48, 0.98×;
  (1 + cos θ) above the horizon and 0 below: 1.73, 2.35, 1.53, 0.90×; max(cos θ, 0): 1.64, 1.85, 1.39, 0.82×. ¼ keeps most of 0's
  gain at golden hour without its loss by day.
- **Only the first surface samples the sky** (`SKY_LIGHT_BOUNCES`, a define for re-measuring): sampling it at every surface gave
  the same variance (dawn 1.63×, golden hour down a slope 2.00×, both ways) for 15% more time. The terrain's albedo is 0.16;
  snow (M1.13) makes deeper surfaces bright, so re-measure there. With a bounce after the first surface the image keeps exactly
  M1.4's orders of light.
- **Rejected: a chance to skip the sky on sunlit points** (sample it with probability E_sky / (E_sky + E_sun), the weights scaled by
  that chance): golden hour down a slope fell from 2.00× to 1.18×, because sunlit slopes under a low sun still gain from the sky,
  and the day gained nothing. A per-render switch for scenes the sun dominates (the day's 10%) is a candidate, not built.
- **Russian roulette from bounce 2** (`RenderSettings.RussianRouletteStart`, `--rr-start`), re-measured above; the survival rule
  (the largest companion throughput, at least 5%) is M1.4's.
- **Three random numbers per sky sample:** the band from the marginal CDF and cos θ within it from what is left of the first, the
  column and φ from the second, the mirror half from the third. With one number per CDF every cell's chance is far above the 2⁻²⁴
  steps of the shader's random floats, so the chance a cell is drawn is the CDF difference its pdf uses; both read the same stored
  floats.
- **Independent renders of one view** (`RenderSettings.RandomStream`, 0 for every image anyone sees): the stream joins a hashed
  key. `--sampling-compare` interleaves its estimators' renders, so a drift in the GPU's speed falls on both alike.
- **The reference atmosphere keeps M1.4's estimator:** it has no sky view to sample, and its sidecar says `sunOnly`.
- **Sidecar schema 6** adds `image.lightSampling` and `image.russianRouletteStart`; sheets vary `lightSampling`, and labels name
  it when it is not `mis`.

*M1.5 gotchas:*

- **The variance image overstates the noise of a pixel's mean.** It treats samples as independent, but the hero wavelength runs a
  fixed cycle, the same in every render, so the spread between wavelengths counts as noise though it is stratified away: at 4096
  spp the z-scores came out with an RMS of 0.63, and both estimators' variances carried that shared term. The spread between
  independent renders is the real noise. M1.4's `--atmosphere-compare` noise columns use the variance image, so they are
  conservative.
- **XOR-mixed streams overlap:** with `key ^ hash(stream)`, sample i of stream s drew exactly the numbers of sample s of stream i,
  so the replicates shared paths; RMS z came out 1.13, and 1.40 with 64 renders. The null test exposed it.
- **Skyline pixels swamp a variance sum:** a pixel on a ridge mixes bright sky and dark rock under its jitter, the same with any
  light sampling. With them in, dawn read 1.07×; without, 1.67×.
- **The mean of z is not a bias test when the noise is skewed** (above); compare the image's mean.
- **The GPU is shared:** a game started mid-session and slowed `main`'s ledger views by 21%, then changed its load from round to
  round. Every comparison in M1.5's tables finished before it started; the ledger row was measured against `main` alternately,
  and `--sampling-compare` now interleaves its estimators. Read `nvidia-smi` before any timing, and time builds as ratios.
- **A root signature must bind every buffer a shader reads:** an experiment that read the ground-irradiance LUT failed at pipeline
  creation (E_INVALIDARG) until it was bound.

**M1.5 hand-off.** To understand M1.5, start by reading `Vista.Core/Rendering/SkyDistribution.cs` (the cells, the weights, the CDFs,
sampling and pdf), then `Vista.App/Shaders/VistaSkyLight.hlsli` (its float twin), then `TraceSample` in
`Vista.App/Shaders/VistaIntegrator.hlsl` from "The bounce that may follow" (the weights) and its miss branch, then
`Vista.Core/Rendering/EstimatorComparison.cs` (how the checks measure).

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png [--light-sampling mis | sunOnly] [--rr-start 2]
dotnet run --project Vista.App -c Release -- --sampling-compare --recipe r.json --camera c.json [--spp 128] [--replicates 32]
    [--rr-starts 0,1,2,3,4,64] [--baseline sunOnly | mis] [--bounces 6] [--out dir]
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --vary lightSampling=sunOnly,mis --out s.png
powershell -ExecutionPolicy Bypass -File vista-views/sampling/render.ps1
VISTA_SHADER_DEFINES=SKY_LIGHT_BOUNCES=64u   (the sky sampled at every surface, for re-measuring)
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Rendering/SkyDistribution.cs` | The distribution: cells, weights, CDFs; the C# reference of sampling, pdf and the luminance measurement |
| `Vista.Core/Rendering/EstimatorComparison.cs` | Replicate statistics, the per-pixel comparison, the skyline mask |
| `Vista.Core/Rendering/RenderTypes.cs` | `LightSampling`, `RussianRouletteStart`, `RandomStream` |
| `Vista.Core/Cli/PreviewCommandLine.cs`, `PreviewPipeline.cs`, `SheetAxis.cs` | `--light-sampling`, `--rr-start`; the `lightSampling` axis |
| `Vista.Core/Output/Sidecar.cs`, `SheetLabels.cs` | Schema 6; the `lights` label |
| `Vista.App/Shaders/VistaSkyLight.hlsli` | Sampling, pdf, the cell lookup; the measurement's cell points |
| `Vista.App/Shaders/VistaIntegrator.hlsl` | The MIS weights, the sky's next-event estimation, roulette from a constant, random streams; `CSSkyLuminance`, `CSSkyProbe` |
| `Vista.App/Gpu/D3D12VistaRenderer.cs`, `D3D12VistaRenderer.SkyLight.cs`, `D3D12VistaRenderer.Atmosphere.cs` | Constants and bindings; the distribution's build and cache; the probe |
| `Vista.App/Gpu/SamplingCompare.cs`, `GpuSelfTest.cs`, `Program.cs`, `Vista.App.csproj` | `--sampling-compare`; the sky-light checks; the include shipped |
| `vista-views/sampling/` | The dawn recipe and `render.ps1` |
| `Vista.Tests/*` | `SkyDistribution`, `EstimatorComparison` and `LightSampling` tests; the schema pins |

**Next: M1.6** (OIDN). The denoiser gets cleaner input at golden hour and in twilight; its 16 spp sheet should be compared
against this slice's 16 spp renders, and its albedo and normal come from the first surface, which this slice did not change.

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

*M1.6 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` / `RayTracer.Gpu` changed, and
neither did the integrator or its host: the raw images are byte-identical to `main`'s. `Vista.Tests` has 508 tests (486 before).
The pinned release is OIDN 2.5.1 (2026-08-18, the latest), its Windows archive (56.6 MB) checked against GitHub's SHA-256 by
`tools/fetch-oidn.ps1` into the git-ignored `RayTracer/.deps/oidn`. Three settings were chosen by measurement: high quality, the
albedo and normal given as noise-free (`cleanAux`), and the input scale from the exposure meter. The CPU is the default device; CUDA
gives the same error 16–21× faster and is one option away (`--denoise-device cuda`). Denoising the same input twice is
bit-identical on both.

*Checks (on the RTX 3070 and its host's Ryzen 7 5800X unless noted):*

- **Raw | denoised | reference** (`--denoise-compare`, `vista-views/denoise/render.ps1`): M1.5's four views at 16 spp, the image
  anyone would see (random stream 0), against a 4096 spp reference rendered as two independent halves (streams 1 and 2). The error
  is relMSE, `(x − r)² / (r² + 0.01)` at the meter's exposure (so 0.18 is middle grey), averaged over every pixel and channel, sky
  and skyline included, with the reference's own noise taken out (`(a − b)²/4` per value; it is under 1% of every denoised error
  here). "As raw" is the samples per pixel the raw render needs for the same error (its relMSE falls as 1/spp). Display RMSE is
  between the PNGs, in 8-bit steps.

  | View | Sun | relMSE raw → denoised, 640 px | As raw, 640 px | Display RMSE, 640 px | Mean | As raw, 320 px |
  |---|---|---|---|---|---|---|
  | Dawn, default camera | −3.6° | 0.00707 → 0.00056 | 203 spp | 5.73 → 1.64 | +0.23% | 114 spp |
  | Golden hour, down a slope | 8.5° | 0.00533 → 0.00036 | 235 spp | 5.71 → 1.71 | −0.13% | 135 spp |
  | Golden hour, default camera | 8.5° | 0.00699 → 0.00078 | 144 spp | 6.69 → 2.24 | −0.01% | 89 spp |
  | Day, default camera | 65.5° | 0.00581 → 0.00085 | 110 spp | 6.40 → 2.30 | −0.25% | 63 spp |

  So 16 denoised samples do the work of 110–235 raw ones at 640 px (7–15×), and of 63–135 at the preview's 320 px (4–8×): a
  smaller image gives the filter less around each pixel. The day gains least; its loupe shows the sunlit faces' fine shading
  partly smoothed. The mean brightness moves by at most 0.25%. The sheet
  (`denoise-sheet.png`) shows the raw speckle gone and the shadowed faces, the skylines and the sunlit slopes kept; the faintest
  far-slope texture is softened.
- **Every way, measured** (equal-error spp at 640 px, 320 px in brackets; the time to filter with a prepared filter, over the four
  views):

  | Way | Dawn | Golden, down a slope | Golden, default | Day | ms at 640 (320) px |
  |---|---|---|---|---|---|
  | CPU, high, noisy aux, metered | 135 (92) | 192 (103) | 114 (71) | 58 (38) | 80–88 (20–22) |
  | **CPU, high, clean aux, metered** | **203 (114)** | **235 (135)** | **144 (89)** | **110 (63)** | **181–208 (46–48)** |
  | CPU, high, prefiltered aux, metered | 192 (112) | 245 (140) | 156 (96) | 123 (74) | 535–763 (136–149) |
  | CPU, high, clean aux, automatic scale | 202 (114) | 235 (135) | 144 (89) | 110 (63) | as metered |
  | CPU, high, clean aux, physical scale | 204 (115) | 201 (117) | 100 (70) | 43 (35) | as metered |
  | CPU, balanced, clean aux, metered | 131 (83) | 165 (98) | 98 (63) | 54 (35) | 84–90 (20–21) |
  | CPU, fast, clean aux, metered | 95 (69) | 152 (91) | 75 (53) | 48 (33) | 43–49 (11) |
  | CUDA, high, clean aux, metered | 202 (114) | 234 (135) | 144 (89) | 110 (63) | 8.6–9.8 (2.8–2.9) |
  | CUDA, balanced | 126 (81) | 158 (96) | 96 (62) | 53 (35) | 4–5 (1–2) |
  | CUDA, fast | 92 (67) | 148 (89) | 74 (52) | 47 (33) | 3–4 (1–2) |

- **Bit-identical twice:** every way above, on the CPU and on CUDA, at both sizes, and the default settings four times in a row at
  4K on each device. CUDA's image differs from the CPU's by at most 0.6–1.9% per value (dawn, the darkest, the most), and their
  mean brightness by under 0.1%; their errors against the reference agree to 0.4%.
- **Cost at 4K** (3840×2160, the down-slope view at 16 spp, whose render took 0.50 s of GPU): the CPU filters in 7.1 s (7.4 s the
  first time), CUDA in 0.39 s (0.49 s the first time, 88 ms to prepare). `nvidia-smi` read 20–25% from other programs before each
  run, and nothing else of mine ran on the CPU.
- **A loupe denoised alone** is not a crop of the denoised frame: the filter sees only the window. Against that window of the
  denoised frame, the PNGs differ by 0.7–1.4 steps RMS at 640 px (1.2–2.6 at 320 px), at most 26–34% in one value. The filter's
  own input scale (from the loupe's pixels) makes no measurable difference here (0.71 against 0.71 down the
  slope). The raw loupe is still exactly a crop.
- **Without the library** (`OpenImageDenoise.dll` hidden from the build output), `--preview --denoise` warns ("Not denoised: Intel
  Open Image Denoise is not next to Vista.App …: run tools/fetch-oidn.ps1, then rebuild Vista.App. Wrote the raw image only."),
  writes the raw files and exits 0.
- **Self-test:** passes, and grows: the reference render denoised twice on the CPU is bit-identical, finite, not negative and within
  2% of the raw image's mean; on CUDA, bit-identical twice and within 1% of the CPU's mean. Without the library the checks are
  skipped; a CUDA device that cannot run is reported and fails nothing.
- **CI:** 508 tests. The settings and their measured defaults; the input scale (automatic, physical, metered, a black meter); the
  input (negative and non-finite colour to 0 and counted, albedo and normal clamped, the render's buffers untouched, a missing or
  short buffer an error); the result type; `--denoise` and `--denoise-device` (and needing a render, and once only); the pipeline
  with a fake denoiser (the albedo and normal requested though no debug image is, the denoised PNG at the raw one's exposure and the
  scene-referred EXR, raw files byte-identical with and without denoising, a loupe denoised at its size, a denoiser that cannot run
  leaving a warning and the raw files, the schema-7 sidecar read back); sheets (`<sheet>.denoised.png`, only when every cell
  denoised; `--sheet-from` telling a render's raw and denoised images apart); relMSE with the reference's noise taken out (exact
  values, and 200 000 noisy values where the reference's noise is larger than the image's); display RMSE, mean bias, equal-error
  spp, bit identity; the magnified loupe.
- **Before/after:** the raw images do not change (byte-identical to `main`, above); the denoised ones are new, in the sheet.

*M1.6 decisions taken in code (revisit freely):*

- **The seam is `IDenoiser` in `Vista.Core`** (`Output/Denoising.cs`), with OIDN behind it in `Vista.App/Denoise`. A denoiser that
  cannot run returns a result that says why instead of throwing, so the missing library, a missing CUDA device and a failed filter
  all end the same way: a warning, the raw files, and the reason in the sidecar.
- **Fed the EXR's linear Rec.709**, negatives as 0 (OIDN takes [0, ∞); none of the views had one), with M0's albedo and normal: the
  first hit's, averaged over the pixel's samples, the sky 0. OIDN's documentation says nothing about the sky's albedo; with 0 the
  skylines come out right.
- **Clean aux (`cleanAux`), not prefiltered:** this terrain is one material with smooth normals, so its albedo and normal are
  noise-free but for the 16-sample coverage of edges. Clean beats noisy everywhere (dawn 203 against 135 spp, day 110 against 58).
  Prefiltering each first gains −5% to +12% at 640 px (−2% to +17% at 320) for three times the time. Re-measure in M1.12, when rock,
  scree, grass and snow give the albedo texture.
- **High quality:** on the CPU, clean aux at high quality is OIDN's costly filter (190 ms at 640 px, against 85 at balanced and 80
  for noisy aux at high) and is worth it: balanced loses a third to a half of the equal-error spp, and does worse than high quality
  with noisy aux in the same time.
- **The input scale is the exposure meter's** (`DenoiseInputScale.Metered`: the frame's metered log-average luminance to 0.18, the
  rule OIDN's own guess uses). It equals the automatic scale in error to three digits, and it is measured on the whole frame, so a
  loupe or a tile is scaled like its frame (OIDN asks for a set scale when tiling). OIDN's documented physical convention (1 is
  100 cd/m², `LuminancePerY / 100`) loses on three views of four, the day by 2.5×; in these scenes exposed values serve it better.
- **The CPU by default, CUDA as `--denoise-device cuda`:** the same error, both bit-identical, but the CPU's 7 s at 4K is small
  next to a final's minutes, it does not touch the GPU the render and the user's games share, and it does not depend on the GPU
  driver. CUDA is 16–21× faster: the device to choose when watch mode (M1.8) or the window (M1.16) denoise every preview.
- **Raw is always written; the denoised files sit beside it** (`<out>.denoised.png`, `<out>.denoised.exr`). The denoised PNG takes
  the raw one's metered exposure; the EXR stays scene-referred.
- **One filter is kept** (and its buffers) while the image size and settings stay the same, as a sheet's cells do; one device per
  kind for the process.
- **Sidecar schema 7** adds `denoise`: the settings asked for, the run (library, the version the loaded library reports, the device
  with its name, the filter and its parameters, the input scale, the time to prepare and to filter) or the problem, the colour
  values given as 0, and what the filter was fed.
- **Sheets:** `--sheet --denoise` also tiles the denoised cells (`<sheet>.denoised.png`); `--sheet-from` labels each image by its
  role in its sidecar's outputs (`output=beauty`, `output=denoised`, `output=normal`), so one render's raw and denoised images, which
  share a sidecar, are told apart. The beauty's role is left out of titles.
- **The DLLs are copied by the build** from `.deps/oidn/bin` when they are there: the CPU and CUDA devices, the core and TBB;
  SYCL and HIP are left out (there is no Intel or AMD GPU here).

*M1.6 gotchas:*

- **The halves' correction is only first-order:** it subtracts the reference's noise over a denominator taken from the noisy
  reference. At 4096 spp the reference's noise is under 1% of every denoised error and the correction is exact enough; the timing
  run's 64 spp reference read the raw image's relMSE 24% low (0.00404 against 0.00533). Keep the reference at hundreds of times the
  image's samples.
- **PowerShell's `-f` formats in the current culture** (here Dutch: `0,0071`), and a comma breaks `--labels`, a comma-separated
  list. Format numbers with `[string]::Format([CultureInfo]::InvariantCulture, …)`.
- **Time the CPU denoiser with the CPU idle:** a build alongside slows it; the first filter on a device includes its warm-up (CUDA:
  0.49 against 0.39 s at 4K).
- **C's `bool` is one byte** in OIDN's API (`[MarshalAs(UnmanagedType.U1)]`); `oidnGetDeviceError` with a null device gives the error
  of a failed device creation.

**M1.6 hand-off.** To understand M1.6, start by reading `Vista.Core/Output/Denoising.cs` (the settings, the input, the seam), then
the denoising block at the end of the render branch of `Vista.Core/Cli/PreviewPipeline.cs`, then `Vista.App/Denoise/OidnDenoiser.cs`
(devices, the prepared filter), then `Vista.App/Gpu/DenoiseCompare.cs` with `Vista.Core/Rendering/ImageError.cs` (how the checks
measure).

*Commands* (from `RayTracer/`):

```
powershell -ExecutionPolicy Bypass -File tools/fetch-oidn.ps1      (once; then rebuild Vista.App)
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png --denoise [--denoise-device cpu | cuda]
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --vary time=dawn,day --out s.png --denoise
dotnet run --project Vista.App -c Release -- --denoise-compare --recipe r.json --camera c.json [--width 640] [--spp 16]
    [--reference-spp 4096] [--devices cpu,cuda] [--loupe x,y,w,h] [--timing-width 3840] [--out dir]
powershell -ExecutionPolicy Bypass -File vista-views/denoise/render.ps1
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Output/Denoising.cs` | The settings, the input and its preparation, the result, `IDenoiser`, the sidecar's record |
| `Vista.Core/Rendering/ImageError.cs` | relMSE against a reference's two halves, display RMSE, mean bias, equal-error spp, bit identity |
| `Vista.Core/Cli/PreviewCommandLine.cs`, `PreviewPipeline.cs`, `SheetCommandLine.cs`, `SheetPipeline.cs` | `--denoise`, `--denoise-device` (flags split generally); the denoised files; the denoised sheet |
| `Vista.Core/Output/Sidecar.cs`, `SheetLabels.cs`, `ContactSheet.cs` | Schema 7; the `output` label; `Magnify` |
| `Vista.App/Denoise/Oidn.cs`, `OidnDenoiser.cs` | The P/Invoke declarations; `IDenoiser` on OIDN |
| `Vista.App/Gpu/DenoiseCompare.cs`, `GpuSelfTest.cs`, `Program.cs`, `Vista.App.csproj` | `--denoise-compare`; the denoiser checks; the wiring; the DLLs copied |
| `tools/fetch-oidn.ps1`, `../.gitignore` | The pinned fetch; `RayTracer/.deps/` ignored |
| `vista-views/denoise/render.ps1`, `vista-views/README.md` | The M1.6 checks |
| `Vista.Tests/*` | `DenoisingTests`, `ImageErrorTests`, `FakeDenoiser`; the schema pins |

**Next: M1.7** (GPU erosion). Its before/after sheets can now be read denoised, but compare raw images for the look: the denoiser
softens the faintest texture, which is what erosion adds. Watch mode (M1.8) should denoise its previews on CUDA (3 ms at 320 px).

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

*M1.7 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` / `RayTracer.Gpu` changed, and
neither did the integrator. `Vista.Tests` has 533 tests (508 before). The world grid is now **4097² samples 8 m apart (32.8 km)**,
eroded on the GPU in 19 s per seed and cached on disk; every render, sheet and dry run reads the eroded world. One deliverable is
weaker than planned: the flow map is the simulation's mean discharge, whose channels are short, not a full flow accumulation (see
**Next**).

*Checks (on the RTX 3070, driver 32.0.16.1664, unless noted; `nvidia-smi` read 37–40% from other programs throughout):*

- **HLSL against the C# reference** (`--gpu-selftest`, `--erosion-measure --parity`): a 65² patch of the alpine range at the world's
  8 m, warmed up by 40 reference steps so it holds water, sediment, flux and loose material. Each pass run once on both sides from
  that state differs by at most 3.3·10⁻⁵ of its largest output (the flux: its damped sum cancels, so a fused multiply-add on the GPU
  shows), 1.0·10⁻⁷ for the water pass, 8·10⁻⁸ for the transport, 0 for the thermal moves and 8·10⁻¹⁰ for their application. 200 steps
  from the bare ground on both sides leave the surfaces 6–9 mm apart at most and 0.08 mm on average, against 12.7 m of mean ground
  movement and 632 m of relief. Tolerances: 10⁻⁴ per pass, 10⁻³ of the relief for the run.
- **Bit-identical twice and through the cache:** the self-test erodes its world (seed 42, the full grid), stores it in a fresh cache,
  erodes it again and reads it back: surface, maps and hash identical every time. When the default cache holds the world, the
  self-test also checks that this GPU and driver still erode it to the cached bits (it passed against the world the sheets had
  cached); a driver that moves the world fails there instead of passing silently. A Debug build (GPU-based validation) reports
  nothing for the parity checks and an erosion.
- **Time and memory per seed** (`--erosion-measure`, the alpine range, four seeds):

  | Seed | GPU time | Per step | Wall | Submissions (longest) | Ground moved, mean (cut / filled at most) | Loose material, mean (cells over 1 m) | Spikes over 5 m: base → eroded |
  |---|---|---|---|---|---|---|---|
  | 42 | 18.85 s | 12.6 ms | 19.5 s | 202 (106 ms) | 29.4 m (305 / 256 m) | 16.4 m (55%) | 1421 → 184 |
  | 7 | 19.09 s | 12.7 ms | 19.6 s | 204 (286 ms) | 29.6 m (317 / 269 m) | 16.5 m (55%) | 1438 → 183 |
  | 1234 | 19.00 s | 12.7 ms | 19.5 s | 204 (135 ms) | 29.5 m (273 / 255 m) | 16.5 m (55%) | 1336 → 163 |
  | 2026 | 19.02 s | 12.7 ms | 19.6 s | 203 (148 ms) | 29.4 m (268 / 242 m) | 16.4 m (54%) | 1306 → 189 |

  1500 steps of five passes each. The state takes 1153 MiB; the process used 1242 MiB of video memory while it existed (89 MiB
  before). The buffers are freed before the mesh is built, so the erosion's memory does not add to the render's. The base terrain
  takes 0.4–0.9 s on the CPU, the upload, readback and maps about 0.6 s.
- **The grid, measured** (the aerial view rendered at 640 px on each grid, all 32.8 km; the erosion settings are tuned for 8 m, so
  only the costs compare, not the looks; 4097² × 4 m costs exactly what 4097² × 8 m does):

  | Grid | Erosion, GPU | State | Mesh triangles | Mesh | BLAS | Process VRAM at render | Mesh (CPU) / upload / BLAS build |
  |---|---|---|---|---|---|---|---|
  | 1025² × 32 m | 1.28 s | 72 MiB | 2.1 M | 50 MB | 146 MB | 325 MiB | 0.08 / 0.03 / 0.08 s |
  | 2049² × 16 m | 5.02 s | 288 MiB | 8.4 M | 201 MB | 586 MB | 888 MiB | 0.16 / 0.08 / 0.14 s |
  | **4097² × 8 m** | **19.2 s** | **1152 MiB** | **33.6 M** | **805 MB** | **2346 MB** | **3144 MiB** | **0.27 / 0.33 / 0.30 s** |

  8193² would need 4.5 GiB of erosion state alone, and its uniform mesh would not fit: 4097² is the largest grid this card takes.
- **What the views see beyond the grid** (`--sight-lines`: each pixel's ray of six views marched over the base terrain, 320×180;
  "beyond" is a ray that leaves the grid's footprint below its highest point, where a render shows the planet's bare ground sphere):

  | View | 16.4 km: beyond the grid | 32.8 km | 65.5 km | Terrain hits at 32.8 km: median / 90% / 99% |
  |---|---|---|---|---|
  | Aerial (4.2 km up) | 21.5% | 13.0% | 7.3% | 7.7 / 18.0 / 23.7 km |
  | Default camera | 1.1% | 0.2% | 0.0% | 3.3 / 5.6 / 7.6 km |
  | Down a slope | 0.0% | 0.0% | 0.0% | 0.1 / 2.7 / 4.1 km |
  | Layered ridges (3 km up) | 18.9% | 12.9% | 7.1% | 9.3 / 17.4 / 23.6 km |
  | Range (3.4 km up) | 15.0% | 8.7% | 4.8% | 7.4 / 15.9 / 23.7 km |
  | Toward the sun (4.2 km up) | 37.8% | 18.2% | 7.8% | 10.6 / 17.1 / 22.6 km |

  From the ground nothing lies beyond 32.8 km; from 3–4 km up, 9–18% of the frame still does, and doubling the extent again only
  halves it. So the eroded grid stays at 32.8 km, and the far field (base terrain beyond the grid, a pure function of position)
  belongs to the view-dependent mesh (M1.9) or the height-field path (M1.10), with the eroded edge blended into it there.
- **Before/after sheets** (`vista-views/erosion/render.ps1 -Before <main's Vista.App.dll>`, 640 px, 64 spp, raw): the four seeds from
  the aerial camera and the seed-42 views (default, down a slope, layered ridges, range), each as `main` (M1.6: 16.4 km at 16 m) |
  this build un-eroded (32.8 km at 8 m) | eroded, plus the four seeds' erosion maps. Eroded faces are fluted with gullies from crest
  to foot and the crests stay sharp; loose aprons and flat sediment floors fill the valleys; the far range is now terrain where M1.6
  showed the grid's edge. What still looks wrong: some loose valley fills read as a field of low hummocks ("dunes") under a low sun,
  and the 8 m facets near a camera on the ground are as plain as M1.6's 16 m ones (M1.9's near-camera detail).
- **Cache:** a world is 201 MB on disk (the surface in float, four maps in half precision). A render that finds it spends 0.85 s on
  the world stage instead of 22 s; a dry run of the full grid takes 3.1 s with it.
- **Every committed camera stands above the eroded seed-42 ground.** Under `default.camera.json` the ground dropped from 1692.0 m
  (the un-eroded 8 m grid, 0.2 m below the camera; M0's 16 m grid had it at 1690.5 m) to 1684.0 m, so the default view now stands
  8.2 m up; the down-slope and sky views 28 m.
- **CI:** 533 tests. The reference: water balanced step by step (rain in, the edges' outflow and evaporation out, to 10⁻⁵ of the
  total), material balanced (bedrock + loose + suspended, but for the sediment carried over the edges, to 10⁻⁶), the thermal passes
  conserving the ground while a loose cone settles to 35° and a 70° rock wall to 50° (and a 40° rock slope stands), water running
  downhill and draining over the low edge, no NaN or negative water, sediment or loose material on flat, spiked, cliffed, noisy and
  pitted fields under three settings, deterministic and pinned (surface and full hashes), loose material deeper in hollows than on
  ridges, half-precision maps, the Laplacian; the settings (validation, hash, overrides) and the cbuffer's 96 bytes; the cache (round
  trip bit for bit, the key and its independence from the culture, a damaged file is a miss with a reason, trimming keeps the
  newest); the builder (erode and store, then read; un-eroded with a warning; off; erode again after damage); the pipeline (the eroded
  world rendered and its hash in the sidecar, a dry run reading it from the cache with its map, a dry run without warning and showing
  the base); `--erosion`, `--grid`, `--vary erosion`; sight lines; the maps image. The pipeline tests run on a 65² world
  (`TestWorlds`), since the real one takes a second to make and most of a gigabyte to mesh.

*M1.7 decisions taken in code (revisit freely):*

- **Two layers, bedrock and loose material.** 62% of the base terrain is steeper than 30°, so one talus angle would have flattened
  the range. Loose material (sediment, scree, collapsed rock) rests at 35°; bedrock stands to 50° and collapses into loose material
  beyond. Water picks up loose material first and rock at a tenth of the rate. The loose depth is the deposition map M1.12 reads.
- **Capacity from discharge, not speed:** `C = K·sin α·q` with `q` the net flow per metre of width (`DischargeExponent` 1; ½ is the
  geomorphologists' usual m/n and was measured too). Mei et al.'s `K·sin α·|v|` eroded every wet slope alike, since the pipe model's
  water moves at the grid's speed limit wherever it flows, and melted the range into smooth sand-like slopes.
- **Sediment moves with the water's own fractions** (each cell sends the share of its sediment its water sent), not semi-Lagrangian
  advection: gather-only, and conserving.
- **Limiters, all local:** the flux keeps 20% of its last value per step (`FluxDamping` 0.8; undamped water sloshed uphill and piled
  sediment 2 km high), and in one step a cell never cuts below its lowest edge neighbour nor fills above its highest. Without them
  the valley floors filled with single-cell needles (33 000 cells over 5 m above all eight neighbours, against 1 400 in the base
  terrain; now under 200). The pick-up and drop-off rates are 0.1 per step: faster ones left a washboard ripple on loose slopes.
- **Open edges:** past the grid the ground continues at the edge's slope, dry, so water and its sediment leave where the land falls
  away; the thermal passes see no neighbour there. The balance tests account for both.
- **Parameters by sheet, per profile:** the mountains' were chosen from nine sheets of the four views (`ErosionSettings.Mountains`); the
  hills share them with half the capacity and rock to 45°, and change only gently. `VISTA_EROSION_SETTINGS` overrides any of them for
  tuning (the settings are in the cache key and the sidecar).
- **The grid is 4097² × 8 m (32.8 km), not 4 m (16.4 km):** the same cost, twice the reach (the sight lines above), and at 8 m a
  gully is a few pixels wide on a 4K frame of the range 5–10 km away. Detail finer than 8 m near the camera is M1.9's position-only
  noise.
- **The uniform mesh of the full grid stays until M1.9**, at 3.1 GiB of video memory with its BLAS, twice §7.1's terrain budget: the
  eroded detail must be seen to be judged, and it fits beside what else runs on this card today (2.7 GiB). M1.9's view-dependent mesh
  has to bring it under budget.
- **The cache** is `%LOCALAPPDATA%\Vista\worlds` (`VISTA_WORLD_CACHE` moves it), one file per (profile, seed, grid, settings, pass
  version, terrain version), checked against its hash on every read, written through a temporary file, trimmed to 8 GB oldest first.
  Bump `ErosionCache.PassesVersion` with any change to what the passes compute (the reference's pinned hash flags one).
- **Maps:** the loose depth, the mean discharge and mean water depth over the second half of the steps (the flow and wetness maps),
  and the water standing at the end; rounded to half precision when made, so a fresh world and a cached one are the same bits.
  Curvature (`TerrainMaps.Curvature`) is computed from the surface when wanted. The suspended sediment is set down where it is at the
  end. `--dry-run` writes `<out>.erosion.png` (loose tan, the top tenth of the discharge blue, standing water dark blue).
- **Sidecar schema 8:** `world.erosion` records the source (eroded, cache, uneroded, off), the settings and their hash, the base
  field's hash, the device and driver, when, the cache file and the cost; `world.heightFieldHash` is the eroded surface's. A render's
  stats also record the BLAS bytes and the process's video memory (for M1.15).
- **CLI:** `--erosion off` and `--vary erosion=off,on` render the un-eroded world on the same grid (the cleanest before/after);
  `--grid SIZExCELL` makes another world, for measurements. `--erosion-measure` erodes seeds and reports, `--parity` runs the
  checks, `--sight-lines` measures extents.

*M1.7 gotchas:*

- **HLSL's `?:` evaluates both sides**, and root UAVs have no bounds checking: `i > 0 ? Ground(c - 1) : ...` read index −1 at the
  grid's edge and hung the device (`DXGI_ERROR_DEVICE_HUNG`, reported as a TDR). Every neighbour index is clamped into the grid first.
- **A transfer smaller than half an ulp of a 2 km height vanishes from the rock but not from the sediment**, creating material.
  Amounts are made representable first (`a − (a − x)`), and HLSL needs `precise` on them or the compiler folds that back to `x`.
- **`pow`'s last bit is the C library's.** The exponents the profiles use (1, ½) are done exactly on both sides, so the pinned hash
  holds wherever CI runs.
- **A record's `ToString` follows the culture** (decimal commas here): the cache key is built from the fields' bits, never from it.
- **`cond ? int.Parse(…) : float.Parse(…)` is a float.** Boxed for reflection it stops being an `int`; cast a branch to `object`.
- **Windows PowerShell 5.1 drops empty-string arguments** to native programs (`--seeds ''` arrived as a bare `--seeds`).
- **A shared GPU stretches submissions:** one batch planned for 100 ms took 482 ms while another program worked. Erosion submissions
  stay at about 100 ms planned, far below the ~2 s timeout; the self-test only fails one over 1.5 s.

**M1.7 hand-off.** To understand M1.7, start by reading `Vista.Core/World/ErosionReference.cs` (the five passes and why each is
gather-only), then `Vista.App/Shaders/VistaErosion.hlsl` beside it, then `Vista.Core/World/WorldBuilder.cs` with `ErosionCache.cs`
(where a world comes from), then `Vista.App/Gpu/D3D12VistaRenderer.Erosion.cs` (buffers, batches, bindings).

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png [--erosion off] [--grid 2049x16]
dotnet run --project Vista.App -c Release -- --sheet --recipe r.json --seed 42 --vary erosion=off,on --out s.png
dotnet run --project Vista.App -c Release -- --erosion-measure [--recipe r.json] [--seeds 42,7] [--grid 4097x8] [--set Capacity=3]
    [--parity] [--out dir] [--sight-lines c1.json,c2.json [--sight-grids 1025x16,1025x32]]
powershell -ExecutionPolicy Bypass -File vista-views/erosion/render.ps1 [-Before <main's Vista.App.dll>] [-Out folder]
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/World/ErosionSettings.cs` | The parameters per profile, their validation, hash and overrides; the per-grid constants (the shader's cbuffer) |
| `Vista.Core/World/ErosionReference.cs` | The state and the five passes in C# |
| `Vista.Core/World/ErosionResult.cs` | The eroded world and its maps; curvature |
| `Vista.Core/World/TerrainEroder.cs`, `WorldBuilder.cs`, `ErosionCache.cs`, `WorldCache.cs` | The seam, the world stage, the disk cache, the in-memory cache |
| `Vista.Core/World/SightLines.cs`, `TerrainGenerator.cs` | The extent measurement; the 4097² × 8 m grid |
| `Vista.Core/Cli/*`, `Output/Sidecar.cs`, `Output/DebugImages.cs`, `Rendering/RenderTypes.cs` | `--erosion`, `--grid`, the erosion axis; schema 8; the maps image; BLAS and video memory in the stats |
| `Vista.App/Shaders/VistaErosion.hlsl`, `Gpu/D3D12VistaRenderer.Erosion.cs` | The passes on the GPU and their host |
| `Vista.App/Gpu/ErosionParity.cs`, `ErosionMeasure.cs`, `GpuSelfTest.cs`, `Program.cs` | The parity checks, `--erosion-measure`, the self-test's erosion checks, the wiring |
| `vista-views/erosion/render.ps1`, `vista-views/README.md` | The M1.7 checks |
| `Vista.Tests/ErosionReferenceTests.cs`, `WorldStageTests.cs`, `TestWorlds.cs` (+ the pipeline tests on the small world) | 25 new tests |

**Next: M1.8** (watch mode). Erosion now costs 19 s a seed the first time and 0.85 s from the cache, and meshing the full grid and
building its BLAS another second; those are what watch mode keeps alive. The discharge map is the simulation's mean outflow, and its
channels are short: 1500 steps do not carry rain across a whole catchment, so M3's rivers will need a real flow accumulation.

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

*M1.8 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` / `RayTracer.Gpu` changed, and
neither did the integrator or any pixel. `Vista.Tests` has 560 tests (533 before); `--gpu-selftest` passes with 3 new checks (46 in
all, its check of the driver against the default erosion cache included, now that the cache holds seed 42). There
are no slider files yet (M1.11 makes them): the watch reads the recipe, the camera and an **options file**, a text file of
`--preview` options through which the seed and everything else outside the recipe and the camera change.

*Checks (on the RTX 3070, driver 32.0.16.1664; `nvidia-smi` read 31–95% from other programs throughout):*

- **The scripted sequence** (`--watch-check`, `vista-views/watch/render.ps1`): a real `--watch` process on copies of the day recipe
  and the aerial camera, changed as a person or an agent would — the exposure, the time of day, a camera turn, a camera move, the
  base altitude, the seed (in the options file) and the seed back — each file saved whole and moved over the old one, as editors
  do. After each render the sidecar must list exactly the stages that change touches, as built, and every other one as reused;
  then each step is rendered again by a cold `--preview` process of the same files and compared byte for byte. Every step passed
  in all five runs (three at 640 px with every debug output and the denoised image, 10 files per step bit-identical; two at 320 px,
  the PNG and the EXR). The wall time per change, from `render.ps1`'s run ("save to files" is from the file's move to the watch's
  line, so it includes the poll and the settling):

  | Change | Built (the denoised image too at 640 px) | 640 px: run | 640 px: save to files | 320 px: run | 320 px: save to files | Cold `--preview` (640 / 320 px) |
  |---|---|---|---|---|---|---|
  | First render | every stage | 28.44 s (seed 42 eroded) | 29.86 s from start | 3.57 s (cached) | 5.33 s from start | 6.03 / 5.46 s |
  | Exposure +1 EV | nothing | 0.53 s | 0.70 s | 0.06 s | 0.25 s | 5.99 / 5.07 s |
  | Time of day: goldenHour | skyView, skyDistribution, render, exposureMeter | 0.58 s | 0.76 s | 0.20 s | 0.37 s | 6.24 / 5.10 s |
  | Camera turn: yaw +20° | render, exposureMeter | 0.48 s | 0.65 s | 0.13 s | 0.31 s | 6.07 / 5.06 s |
  | Camera move: 500 m up | skyView, skyDistribution, render, exposureMeter | 0.54 s | 0.71 s | 0.16 s | 0.33 s | 6.56 / 5.45 s |
  | Base altitude: 3000 m | scatteringOrders, skyView, skyDistribution, render, exposureMeter | 1.57 s | 1.74 s | 1.29 s | 1.47 s | 6.23 / 5.21 s |
  | Seed 42 → 7 (options file) | world, mesh, accelerationStructure, skyView, skyDistribution, render, exposureMeter | 31.46 s (eroded) | 31.63 s | 1.94 s (cached) | 2.12 s | 6.16 / 5.02 s |
  | Seed 7 → 42 | the same | 3.44 s (cached) | 3.61 s | 1.83 s | 2.02 s | 6.07 / 5.06 s |

  At 640 px every render writes the beauty, the EXR, six debug images and the denoised pair (CUDA); at 320 px the beauty and the
  EXR. The 640 px run eroded both seeds into a fresh cache; the 320 px run found them there.

  A cold `--preview` takes 5–6 s (process start, device, shaders, the world from the cache, the mesh and its BLAS, the LUTs). In a
  watch, a new seed costs its world from the cache, its mesh and its BLAS, about 2 s (3.4 s while the GPU was busiest), or, the
  first time, its erosion too (about 30 s); a change of the base altitude rebuilds the scattering orders (0.95 s of GPU); every
  other change takes a fraction of a second, most of it writing files at 640 px. An exposure change renders nothing.
- **Partial rebuilds render as full ones** (`--gpu-selftest`, new): on one renderer, after a change of the time of day, of the
  camera's height and of the base altitude, the render that rebuilt only the sky view and the sky distribution (and, for the
  altitude, the scattering orders) is bit-identical to the same view rendered after `ForgetAtmosphere`, and each reports
  rebuilding exactly those parts.
- **No pixel moved:** the four ledger views and a golden-hour view with every debug output, rendered by `main` (b96f2e57) and by this
  slice at 640 px, 16 spp, are byte-identical (16 files). A four-cell sheet varying `camera.exposureEv` renders once and
  re-exposes three times.
- **By hand:** `--watch` in a terminal re-renders on a camera save (exposure 0.05 s), an options-file `--time dusk` (0.19 s), and
  reports a broken recipe with its path and keeps watching.
- **CI:** 560 tests. The session: one-shot and dry runs build every stage that applies; a sequence of eleven changes in one session
  rebuilds exactly each change's stages and writes, after each, the same PNG and EXR as a cold render of the same request (the
  fake renderer's image depends on what the real one reads, so a stale reuse would show); the exposure re-exposes without
  rendering; fewer debug outputs reuse the render and more render again; the denoised image is reused until the render or the
  settings change, and a failed one is tried again; a failing request leaves the session as it was; the render key covers every
  field of `VistaScene` (by reflection); schema 9 reads back. The watch: the first poll renders, a change waits to settle, edits in a
  row are one render, a save without a change renders nothing, the options file changes the seed and overrides the command line
  (and a removed line restores it), a broken file is reported once with its path, bad or forbidden options are reported, a missing
  file is waited for, another camera is watched instead, `Run` stops when cancelled; the field-by-field change list; the command
  line, the options file's syntax and the merge.

*M1.8 decisions taken in code (revisit freely):*

- **Stages are reused by what they read, not by which file changed.** `PreviewSession` keys each of its stages on its inputs: the
  world on the terrain profile, seed, grid and erosion (`WorldCache`); the mesh on the world instance (M1.9 adds the camera); the
  render on everything the renderer reads; the exposure meter on the same at its own size; the denoised image on the render, the
  settings and the input scale. The renderer keeps the device, the BLAS and the air and reports what it rebuilt
  (`RenderStageKeys`). So a change rebuilds what it touches in whichever file it arrives, a file saved unchanged (or only
  reformatted, or only renamed in the recipe) rebuilds nothing, and a sheet's cells share a session and get the same reuse.
- **The air's three parts are rebuilt and reported separately:** the wavelength tables and the columns LUT follow the model; the
  scattering orders also the base altitude; the sky view (43 ms GPU) and the sky distribution (6 ms) also the sun, the camera's
  altitude and the terrain's span. A turn of the camera rebuilds none of them; a move, the time of day or a new seed only the
  last two.
- **The exposure is display only.** The render key zeroes the camera's exposure, so changing it re-exposes the kept render and
  meter. The meter's key leaves out the size and the loupe (it always renders the whole frame at 320 px), so changing them renders
  the image but not the meter. A render that made more debug outputs than are asked for now is reused (asking for fewer never
  changes the beauty, M0). The denoiser's input scale is the meter's, not the exposure's, so the denoised image survives an
  exposure change.
- **The render key is everything the renderer reads:** the scene without its recipe and seed (the renderer reads them only through
  the world, the sun, the materials and the base altitude, which are in the key) and with the exposure zeroed, plus the settings.
  Worlds, meshes and lists compare by instance, so a scene built afresh can cost a render but never reuse a wrong one. A test
  enumerates `VistaScene`'s fields: a new one (M1.11's sliders, M1.12's materials) must join the key or the excluded list.
- **One world is kept, not several.** A seed seen before comes back from the erosion cache on disk in about 1.8 s all told (the read,
  the mesh, the upload and the BLAS); a second full-grid mesh would hold another 805 MB of memory, and the renderer keeps one BLAS
  anyway.
- **The files are polled by content** every 50 ms and acted on once they have read the same for 100 ms, rather than with
  `FileSystemWatcher`: an editor's save in several writes or by rename, and several files saved together, are one render. A missing
  or locked file is waited for (said after a second); a file that does not parse, or options that do not make a preview, are
  reported once with the file's path while the watch goes on, and the next render's changes are named against the last image.
- **The options file** holds `--preview` options as on a command line (`#` comments, double quotes); its values override the command
  line's, its flags add, and a line taken out gives the command line's value back. The seed lives there, so a script or an agent
  drives the whole session by editing files; the console only takes `quit` (or Ctrl+C, which stops after the render in progress).
  `--dry-run` is fixed at the start (a dry-run watch needs no GPU). M1.11's slider file joins the files the request names.
- **The denoiser keeps the CPU default.** M1.6 suggested CUDA for watch previews, but a watch render must equal a cold `--preview`
  of the same options; `--denoise-device cuda` in the options file is the way to it.
- **Sidecar schema 9:** `stages` on every sidecar (the stages built and the stages reused, in pipeline order; a dry run has only the
  world and the mesh), `watch` on a watch render's (its number, the changes as `camera.exposureEv: 0 → 1` or `--seed: 42 → 7`, the
  settling wait and the run's wall time). The kind stays `preview`.
- **A height field computes its range and hash once.** They were recomputed on every access: the lowest and highest point four
  times a render and a byte-wise hash of 67 MB for the sidecar, about a quarter of a second per watch render on the full grid.
- **Parse errors name the file**, in `--preview` too: several files are watched, and the parser's message alone does not say which.
- **A render runs to the end** before the next change is looked at; a preview takes 0.05–2 s. The window's progressive preview
  (M1.16) will want to cancel between dispatches.

*M1.8 gotchas:*

- **File writing is most of an exposure change** at 640 px with every debug output: eight PNGs at optimal deflate and two EXRs take
  0.2–0.5 s, though only the beauty and the denoised PNG change. Skipping rewrites of identical files would need the session to
  trust that nobody touched them; at the default 320 px without debug outputs the write is a few tens of milliseconds.
- **The default erosion cache was empty** on this machine (M1.7 measured in scratch caches), so the first watch eroded for 19 s.
  `vista-views/watch/render.ps1` erodes into a cache under `-Out`; `--watch-check` alone uses the user's (its children inherit
  `VISTA_WORLD_CACHE`).
- **A worktree of the whole repository under the scratch folder fails** (`Filename too long`, from TheCuratool's vault): build `main`
  for a comparison in a short path such as `C:\git\projects-vista-main`.
- **A background reader of the console ends at once when there is no console** (stdin redirected from NUL): that is fine, Ctrl+C
  and the process's end still stop the watch; it must never be what stops it.

**M1.8 hand-off.** To understand M1.8, start by reading `Vista.Core/Cli/PreviewSession.cs` (the stages and their keys), then
`Vista.Core/Cli/PreviewWatch.cs` (polling, settling, the change list), then `Vista.App/Gpu/WatchCheck.cs` (the scripted check).
The M1.16 window drives a `PreviewSession` directly.

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --watch --recipe r.json --camera c.json --out p.png [--options o.txt] [any --preview option]
dotnet run --project Vista.App -c Release -- --watch-check [--recipe r.json] [--camera c.json] [--seed 42] [--second-seed 7]
    [--width 640] [--spp 64] [--debug all] [--denoise [--denoise-device cuda]] [--out folder]
powershell -ExecutionPolicy Bypass -File vista-views/watch/render.ps1 [-Spp 64] [-Out folder]
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/Cli/PreviewSession.cs` | The preview's stages, kept and keyed (the old `PreviewPipeline.Run` body) |
| `Vista.Core/Cli/PreviewPipeline.cs`, `SheetPipeline.cs` | A one-shot session; a sheet's cells share one |
| `Vista.Core/Cli/WatchCommandLine.cs`, `PreviewWatch.cs` | `--watch` and its options file; polling, settling, the change list |
| `Vista.Core/World/WorldCache.cs`, `HeightField.cs` | Says whether it reused; range and hash computed once |
| `Vista.Core/Rendering/RenderTypes.cs` | `RenderStageKeys`: what a renderer reports it rebuilt |
| `Vista.Core/Output/Sidecar.cs` | Schema 9: `stages`, `watch` |
| `Vista.App/Gpu/D3D12VistaRenderer*.cs` | The air's parts reported separately |
| `Vista.App/Program.cs`, `Gpu/WatchCheck.cs`, `Gpu/GpuSelfTest.cs` | The watch loop, `--watch-check`, the partial-rebuild checks |
| `vista-views/watch/render.ps1`, `vista-views/README.md` | The M1.8 checks |
| `Vista.Tests/PreviewSessionTests.cs`, `PreviewWatchTests.cs`, `WatchCommandLineTests.cs`, `FakeRenderer.cs` | 27 new tests; the fake keeps and reports stages and can make its image depend on the scene |

**Next: M1.9** (the view-dependent terrain mesh). Its mesh follows the camera, so the mesh stage's key gains the camera
(`WorldCache.Mesh`): a camera turn will then rebuild the mesh and the BLAS where M1.8 rebuilds only the render, which is what M1.9's
build-time measurement has to keep small.

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

*M1.9 status (2026-10-20): done.* Every deliverable and check is met; nothing in `RayTracer.Core` / `RayTracer.Gpu` changed.
`Vista.Tests` has 596 tests (560 before); `--gpu-selftest` passes with 4 new checks (50 in all). The terrain is now meshed per view:
a 4-8 mesh refined to an error in pixels over the eroded grid, a far field of base terrain out past the horizon, and seeded detail
below the grid near the camera, all bent onto the Earth around the camera, with the grid's own shading put back on coarse triangles
by normal maps. At 4K and 1 px a view takes 6.4–9.8 M triangles and 1.0–1.3 GiB of video memory for the process, against M1.7's
uniform mesh at 33.6 M and 3.2 GiB, and it builds in about 1.5 s; the preview's takes 0.26–1.5 M and 0.1–0.25 s.

*Checks (on the RTX 3070, driver 32.0.16.1664; `nvidia-smi` read 10–35% from other programs throughout):*

- **The measurement table** (`vista-views/mesh/render.ps1`, `--mesh-measure`, seed 42, golden hour, 16 spp; "VRAM" is the process's,
  normal maps and image buffers included; the GPU was shared, so read the last column as ratios within a row group):

  | View | Size | Mesh | Triangles (below the grid / far field) | Mesh | BLAS | VRAM | Build: refine + place (CPU), upload + BLAS | ns per pixel-sample |
  |---|---|---|---|---|---|---|---|---|
  | default | 320×180 | view 1 px | 0.71 M (0.10 / 0.27) | 18 MiB | 48 MiB | 327 MiB | 0.12 + 0.02 s, 0.05 s | — |
  | default | 3840×2160 | view 1 px | 9.77 M (2.35 / 3.84) | 242 MiB | 651 MiB | 1282 MiB | 1.14 + 0.31 s, 0.19 s | 3.28 |
  | aerial | 320×180 | view 1 px | 0.26 M (0 / 0.17) | 6 MiB | 17 MiB | 286 MiB | 0.07 + 0.01 s, 0.04 s | — |
  | aerial | 3840×2160 | view 1 px | 7.20 M (0 / 4.05) | 179 MiB | 480 MiB | 1046 MiB | 0.77 + 0.24 s, 0.15 s | 4.55 |
  | down the slope | 320×180 | view 1 px | 0.36 M (0.06 / 0.11) | 9 MiB | 24 MiB | 295 MiB | 0.16 + 0.01 s, 0.04 s | — |
  | down the slope | 3840×2160 | view 1 px | 6.40 M (3.69 / 1.23) | 159 MiB | 427 MiB | 973 MiB | 0.99 + 0.20 s, 0.12 s | 5.90 |
  | layered ridges | 320×180 | view 1 px | 1.47 M (0 / 0.83) | 36 MiB | 98 MiB | 396 MiB | 0.19 + 0.05 s, 0.06 s | — |
  | layered ridges | 3840×2160 | view 1 px | 8.23 M (0.71 / 3.94) | 204 MiB | 549 MiB | 1142 MiB | 0.84 + 0.34 s, 0.13 s | 3.48 |
  | range | 320×180 | view 1 px | 0.29 M (0 / 0.17) | 7 MiB | 19 MiB | 289 MiB | 0.06 + 0.01 s, 0.04 s | — |
  | range | 3840×2160 | view 1 px | 7.30 M (0.06 / 4.01) | 181 MiB | 487 MiB | 1056 MiB | 0.80 + 0.24 s, 0.13 s | 3.51 |
  | each view | either | uniform (M1.7's, bent) | 33.55 M | 768 MiB | 2238 MiB | 3142–3268 MiB | 1.8–2.3 + 0.8–1.4 s, 0.5–0.9 s | 2.52, 4.01, 4.13, 2.95, 3.19 (4K) |

  At 0.5 px the 4K meshes double (14.6–21.1 M, 1.7–2.3 GiB) and at 2 px they shrink by 1.3–2.3× (3.1–6.2 M); the 0.5 px default
  view's 9.9 M triangles below the grid are where its cost goes. 0.25 px asks for 64 M and is refused (below). Without the detail and
  the far field ("grid only", the uniform mesh's world) the 1 px 4K meshes are 1.2–2.4 M triangles at 0.5–0.6 GiB and cost 2.88, 3.96,
  4.54, 2.99, 3.25 ns: the level of detail itself costs about what the uniform mesh did per sample, and the far field (terrain where
  rays used to escape to the sky) and the detail (more surface near the camera) are what the view mesh adds. The far field is 40–56%
  of every elevated view's 4K triangles: what M1.10's height-field path would replace. The world's hierarchy takes 2.7–3.2 s, once per
  world (a watch keeps it).
- **No facets beyond the target near the camera** (`loupes-sheet.png`, 4K, 960×540 loupes of the default view's foreground ridge and
  the down-slope view's slope, 64 spp): the grid alone (no detail) shows the 8 m grid's flat triangles as planes with hard edges; the
  view mesh at 1 px and at 0.5 px (twice the triangles) cannot be told apart, both smooth, with the detail's bumps on the ridge line and
  in the shadow's edge.
- **The shadow of a peak off screen** (`vista-views/mesh/offscreen-shadow.camera.json`, 1280 px): 30 m over a valley floor, looking
  away from the golden-hour sun, the valley lies in the shadow of a ridge about a kilometre behind the camera. `main` (M1.8, the flat
  8 m mesh), the uniform mesh (everything kept, bent) and the view mesh cast the same shadow, its edge across the valley in the same
  place.
- **The same world at every size** (`sizes-sheet.png`, default, aerial and layered ridges, 64 spp): each view at 4K box-filtered to
  320 px beside its 320 px preview at one exposure: the same ridges, valleys and shadows; mean luminance −1.45%, −0.30% and −0.60%, mean
  |ΔY| 3.4%, 6.6% and 1.3% of the mean (most of it the preview's noise). Before the normal maps the preview had the same world but
  lost the eroded fluting the 4K showed (coarse triangles carry their corners' normals only); with them the two match.
- **Before and after** (`before-after-day.png`, `before-after-golden.png`, 640 px): where M1.8 and M1.9 overlap they match; the far
  field now fills the aerial, layered-ridges and range views to the horizon where M1.8 showed the bare ground sphere; the ground near
  the camera is smooth with its detail where M1.8 showed 8 m facets.
- **`--gpu-selftest`** (50 checks): the self-test world's view mesh built twice is bit-identical, has no crack (no vertex at the
  midpoint of a triangle's edge), keeps the ground behind the camera and reaches past the grid; every render check, the loupe, the
  atmosphere parity (now on frame points, with a new 150 km far-field segment) and the watch checks (the base altitude now rebuilds
  the mesh) pass.
- **Watch mode** (`vista-views/watch/render.ps1`, 4K and 320 px): every step's files are bit-identical to a cold `--preview`. A turn
  now rebuilds the mesh and its BLAS (0.79 s at 4K, 0.31 s at 320 px; M1.8 rebuilt only the image) and a move up 0.68 s / 0.30 s;
  a seed already seen costs its world from the cache plus the hierarchy, 3.4 s / 2.7 s (M1.8: about 2 s).
- **CI:** 596 tests. The detail (a pure function of position and seed; within its amplitude; the midpoint bound on random segments at
  every level; the measured envelope on fresh segments, never falling; the noise's bounds); the Earth frame (the camera at its origin;
  heights kept past the horizon, from floats too; the horizon reach); the hierarchy (levels, split edges joining older vertices,
  children naming their parent, saturation over every stored vertex, the grid unchanged inside its blend, the far field at its edge,
  Catmull–Rom on a plane, determinism, the prefiltered octaves keeping their mean); the mesher (no crack from six cameras × seven
  settings × two sizes, every triangle facing up, every vertex on the world with its detail, the ground covered exactly once,
  determinism, a finer target meshing more, the uniform mesh, a turn re-meshing but keeping what the air reads, the ground behind the
  camera); the normal maps; the cache (the mesh follows the view, the hierarchy the world); the options, axes and labels; schema 10.

*M1.9 decisions taken in code (revisit freely):*

- **A 4-8 mesh refined top down to saturated errors** (Lindstrom and Pascucci 2001): a vertex is kept when its error over its
  distance less its radius is above the target, `e·λ/τ + r > d`, with errors and bounding spheres saturated from the finest level up,
  so a kept vertex keeps its parents and the mesh never cracks. A test and the self-test look for T-junctions (a vertex at the
  midpoint of a kept triangle's edge, which any crack would leave); none in thousands of meshes.
- **Three grids, one hierarchy:** g0 the eroded 4097² × 8 m world grid (32.8 km), g1 the base terrain at 32 m over 131 km, g2 at 256 m
  over 1049 km, nested on one lattice (sub-units of 1/128 of a g0 cell, 2²⁴ to the root). The coarse grids sample only the octaves they
  can hold (64 m and 512 m wavelengths and up), each dropped octave replaced by its mean (`TerrainProfile.RidgedOctaveMean` 0.5948), so
  nothing aliases and the mean height stays. Each grid blends over its outer 256 cells into the next one's linear surface (2 km for the
  eroded grid, 8 km for g1): the eroded world fades into base terrain with no step. Stores hold heights, errors and radii on each grid's
  lattice plus margins (16 g0 cells, 16 g1 cells) for the vertices whose descendants reach into a finer grid; every other vertex is
  analytic (its edge's midpoint, the detail's error bound, a radius from the steepest slope).
- **The far field reaches the horizon:** triangles wholly beyond the camera's horizon distance plus the highest terrain's (×1.05) are
  left out: 330–430 km from the committed cameras.
- **On screen the target is τ pixels of the frame's centre (default 1); off screen it is half the sun's angular radius (2.3 mrad, about
  7 px at 4K).** Off-screen geometry matters through its shadows, and the sun's disk blurs a shadow's edge over twice its angular
  radius times the occluder's distance, so an occluder that far off moves the edge by a quarter of its blur. By distance alone (the
  first build) the far field behind the camera made 23–52 M triangles at 4K. Below the grid the off-screen target is the sun's whole
  disk. A vertex counts as on screen when its sphere, lowered by the curvature drop and widened by the drop's spread across it, reaches
  into the frustum (2% wider than the frame's): still monotone. So a camera turn re-meshes; what the air reads (the reach and the
  height range) depends only on where the camera stands, so a turn rebuilds no sky view.
- **Below the grid the surface is Catmull–Rom through g0's samples plus seeded detail.** Refined straight through the grid's flat
  triangles, the mesh's own normals shaded every 8 m triangle flat, with staircase edges (the first renders showed them as dark
  spikes). The detail is six octaves of gradient noise from 16 m to 0.5 m, each 4% of its wavelength high, turned between octaves
  like the base terrain's (`TerrainDetail`, sub-seed `terrain.detail`); 4% was chosen from a 2/3/4/5/10% sheet: 2% reads as a fuzz on
  the shadow's edge, 10% crumples, and the triangles grow with the square. Levels go down to 6.25 cm.
- **The detail's error bound is measured, not proven:** per octave the curvature bound `C₂t²/8` below a quarter of a lattice unit, above
  it the worst midpoint deviation over 2 M random segments at nine lengths, ×1.15 (`TerrainDetail.Envelope`). The provable bound from
  the noise's largest gradient and value was 1.5–1.8× looser near a unit and cost two to three times the triangles below the grid. A
  test pins the envelope on fresh segments.
- **On screen, triangles longer than 8 px (`MaxTrianglePixels`) are split too, counting edges only up to a grid cell's diagonal**, so
  the smooth surface below the grid reads smooth near the camera and the far field is left to the error alone (measured by distance,
  the size rule over-refined grazing far terrain: 6.7 → 10.5 M triangles).
- **Normal maps give coarse triangles the grid's shading back** (`NormalMap`, g0 and g1, half-precision x and z per sample, 134 MB on
  the GPU): a hit adds the map's normal there less its corners' map normals interpolated (`TerrainMesh.MapNormals`, 4 bytes a vertex),
  which is nothing on a fine triangle; they cost 5% of a terrain view at 4K. Without them a preview lost the eroded fluting a final
  shows (the same-world sheet). g2 needs none: beyond g1 its 256 m samples are wider than a pixel wherever the mesh reaches them.
- **The geometry bends with the Earth** (`EarthFrame`): positions are in the frame around the camera, which stands at `(0, h, 0)`;
  the shader reads heights and verticals from positions (`HeightAbove`, `LocalUp`), a segment is the chord between two points, and
  the reference atmosphere starts a path from its true place. Past the horizon a flat world would put the far ranges kilometres too
  high (12.5 km of drop at 400 km). The camera's frame is also why near-camera coordinates keep their precision in float. The mesh is
  bent onto the scene's ground sphere, so a base-altitude change re-meshes.
- **The mesh's key** is the camera's pose (position, axes, field of view), the frame's size, the ground sphere and the mesh settings
  (`MeshView`, `MeshSettings`); the hierarchy's is the world and whether there is detail. Normals are the angle-weighted mean of the
  faces around a vertex, so shading follows the mesh at its detail. The refinement is depth first and single threaded, so the order
  of triangles and vertices, and with it the acceleration structure and the image, is the same every time.
- **The renderer refuses what does not fit** before uploading: more than 48 M triangles in one acceleration structure (a 64 M one
  hung the GPU in its build) or more memory than the adapter's budget has free; the message says to raise `--mesh-error`. A watch
  reports it and goes on.
- **CLI and sidecar:** `--mesh-error <px>`, `--detail off`, `--far-field off`, `--mesh uniform` (every sample of the world grid,
  nothing else, no detail: M1.7's mesh, bent), as sheet axes too (`meshError`, `detail`, `farField`, `mesh`); `--mesh-measure`;
  sidecar schema 10 `world.mesh` (settings, triangles below the grid and beyond it, bytes, reach, heights, build times), and the
  console's `mesh:` line. `world.triangles` and `meshBytes` are the view's mesh now.

*M1.9 gotchas:*

- **Coplanar triangles shade flat:** a mesh refined through a linear surface gets each vertex's normal from coplanar faces, so the
  coarse triangles' facets show with staircase edges, even though the geometry is right (heights checked to 3 m, coverage exact). It
  looked like geometry gone wrong.
- **Saturation needs one bound for all:** radius bounds per region (the steep eroded grid, the gentle far field) did not nest at a
  region's border, where a gentle vertex has a steep child; the crack test found two T-junctions. One global slope bound, Catmull–Rom's
  overshoot included (its gradient reaches 1.875 times the steepest step between samples), nests.
- **An off-screen size rule scaled with 1/τ**, so a coarser target meshed more; the size rule is on screen only.
- **A 64 M-triangle BLAS build hung the GPU** (`DXGI_ERROR_DEVICE_HUNG`); hence the refusal above. 0.25 px at 4K is out of reach on this
  card.
- **The hierarchy costs 2.7–3.2 s a world** (1.2 s sampling the far grids' base terrain, 1.0 s saturating, 0.5 s heights), paid by every
  cold preview and every new seed in a watch. A cache of the far grids beside the erosion cache would take most of it.
- **The sky view is read in the camera's frame** for rays leaving the terrain, built for one vertical; across the far field the
  vertical tilts by up to 4° (s/R at 430 km). Far terrain's sky light is approximate there; its sunlight (through `LocalUp`) is not.
- **Aerial perspective marches at most 8 steps**, also along a 300 km segment to the far field; the far ranges look plausible, but the
  reference was never compared that far.
- **PowerShell:** `$Views` and `$views` are one variable; a function named `Measure` loses to the `Measure-Object` alias.

**M1.9 hand-off.** To understand M1.9, start by reading `Vista.Core/World/TerrainHierarchy.cs` (the lattice, the levels, the three
grids, saturation), then `Vista.Core/World/ViewMesher.cs` (the refinement, the targets, the vertices and normals), then
`Vista.Core/World/EarthFrame.cs` with `HeightAbove` and `TraceClosest` in `Vista.App/Shaders/VistaIntegrator.hlsl` (the bent frame
and the normal maps on the GPU).

*Commands* (from `RayTracer/`):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png [--mesh-error 1] [--detail off]
    [--far-field off] [--mesh uniform]
dotnet run --project Vista.App -c Release -- --mesh-measure --cameras a.json,b.json [--sizes 320,3840] [--errors 0.5,1,2]
    [--uniform] [--plain] [--downsample] [--spp 16] [--out dir]
powershell -ExecutionPolicy Bypass -File vista-views/mesh/render.ps1 [-Before <main's Vista.App.dll>] [-Spp 64] [-Out folder]
```

*Files added or changed:*

| File | What |
|---|---|
| `Vista.Core/World/TerrainHierarchy.cs` | The 4-8 lattice, the three grids and their blends, the stores, heights, saturated errors and radii, the smooth surface below the grid |
| `Vista.Core/World/ViewMesher.cs` | `MeshKind`, `MeshSettings`, `MeshView`, `MeshInfo`; the refinement, the vertices (detail, bend, map normals), the normals, the T-junction check |
| `Vista.Core/World/TerrainDetail.cs`, `NormalMap.cs`, `EarthFrame.cs` | The detail and its bound; the grids' normal maps; the bent frame |
| `Vista.Core/World/TerrainMesh.cs`, `WorldCache.cs`, `TerrainProfile.cs`, `StableHash.cs` | The view's mesh (frame, reach, heights, map normals); the hierarchy and mesh kept by view; prefiltered octaves; the detail's sub-seed |
| `Vista.Core/Atmosphere/AerialPerspective.cs`, `Rendering/RenderTypes.cs` | Segments between frame points; the air's frame from the mesh |
| `Vista.Core/Cli/*`, `Output/Sidecar.cs`, `Output/SheetLabels.cs` | The mesh options and axes; the session meshes after the camera; schema 10; the mesh in a sheet's labels |
| `Vista.App/Shaders/VistaIntegrator.hlsl` | The bent frame (`HeightAbove`, `LocalUp`, `SegmentBetween`), the normal maps, the reference from the true place |
| `Vista.App/Gpu/D3D12VistaRenderer.cs` | The camera in the mesh's frame, the normal maps and map normals bound, the refusal of meshes that do not fit |
| `Vista.App/Gpu/MeshMeasure.cs`, `GpuSelfTest.cs`, `WatchCheck.cs`, `Program.cs`, `*Compare.cs` | `--mesh-measure`; the mesh checks; the watch's mesh stages; the tools mesh by view |
| `vista-views/mesh/render.ps1`, `mesh/offscreen-shadow.camera.json`, `vista-views/README.md` | The M1.9 checks |
| `Vista.Tests/TerrainDetailTests.cs`, `EarthFrameTests.cs`, `TerrainHierarchyTests.cs`, `ViewMesherTests.cs`, `NormalMapTests.cs`, `MeshOptionsTests.cs` (+ updates) | 36 new tests |

**Next: M1.10** (the height-field decision). The far field is 40–56% of an elevated view's 4K triangles and of its acceleration
structure: a height-field traversal of g1 and g2 beyond some distance would replace that part of the mesh, measured against these
meshes on the same views, with the same normal maps for its shading.

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

*M1.10 status (2026-10-21): done; the mesh stays the far terrain's path.* A height-field far field was built far enough to measure
it against M1.9's mesh on the five M1.9 views at 4K: the two render the same far field (the mesh converges onto the height field as
its error target shrinks), and the height field saves about 200 MiB of video memory and 0.4–1.2 s of build per view, but on the three
views that show the far field it costs 1.27–1.39× per pixel-sample. The prototype stays on the branch `claude/vista-m1-10-heightfield`
(one commit on top of this plan); `main` carries the numbers, these notes and two contact sheets (`vista-views/sheets/`).
Nothing in `RayTracer.Core` / `RayTracer.Gpu` changed. On the branch `Vista.Tests` has 603 tests (596 before) and `--gpu-selftest`
passes its 50 checks.

*What was built (on the branch):* `--far-field heightField` meshes the world grid alone (the triangles that overlap it, as
`--far-field off` does) and lays the far field out as square tiles of 8² cells: g1's outside the world grid's square, g2's outside
g1's, out to the same horizon reach (`FarHeightField`). Each tile is an axis-aligned box in the camera's bent frame (a procedural
primitive of a second BLAS beside the mesh's, a second TLAS instance) and a local frame at its centre. The integrator compiled with
`HEIGHT_FIELD` hands each tile a ray query returns to a walk: the ray is clipped to the tile's square and height range in the local
frame, then walks the cells on the world's own grid, tests each cell its height range does not clear against the cell's two 4-8
triangles bent exactly as the mesh's vertices are (`EarthFrame.ToFrame` in float), and commits the nearest hit; the hit is shaded
once, after the query, with g1's normal map (g2's normals by central differences in the shader). The far grids are kept per world
(two 4097² float grids, 128 MiB on the GPU, 0.03 s to build, 0.05 s to upload); the tiles per view (0.1 s).

*Checks (on the RTX 3070, driver 32.0.16.1664; the GPU idle but for the desktop, 0–38% busy):*

- **The numbers table** (`vista-views/heightfield/render.ps1`, `--height-field-compare`, seed 42, golden hour, 3840×2160, 16 spp, error
  1 px, 8² tiles; "VRAM" is the process's, normal maps and image buffers included; the cost is the median ratio of three rounds in
  which the mesh and the height field were rendered alternately by two renderers):

  | View | Pixels beyond the grid | Triangles: mesh (beyond the grid) → height field's mesh + tiles | Mesh + BLAS, MiB | VRAM, MiB | Build, CPU (refine + place + tiles) | Build, GPU (upload + BLAS) | Cost, height field / mesh |
  |---|---|---|---|---|---|---|---|
  | default | 0.0% | 9.77 M (3.84) → 5.92 M + 327,592 | 242 + 651 → 147 + 23 + 407 | 1282 → 1092 | 2.24 → 1.21 s | 0.38 → 0.19 s | 1.025 (1.001–1.035) |
  | aerial | 9.8% | 7.20 M (4.05) → 3.15 M + 378,486 | 179 + 480 → 78 + 26 + 223 | 1047 → 844 | 1.02 → 0.56 s | 0.15 → 0.10 s | 1.337 (1.333–1.374) |
  | down the slope | 0.0% | 6.40 M (1.23) → 5.17 M + 328,046 | 159 + 427 → 128 + 23 + 356 | 973 → 1023 | 1.25 → 1.20 s | 0.13 → 0.12 s | 0.999 (0.974–1.010) |
  | layered ridges | 9.1% | 8.23 M (3.94) → 4.29 M + 355,473 | 204 + 549 → 106 + 24 + 299 | 1142 → 946 | 1.15 → 0.80 s | 0.19 → 0.12 s | 1.328 (1.236–1.341) |
  | range | 6.2% | 7.30 M (4.01) → 3.29 M + 363,342 | 181 + 487 → 82 + 25 + 233 | 1056 → 855 | 1.09 → 0.58 s | 0.17 → 0.11 s | 1.393 (1.319–1.453) |

  The height field's memory is its mesh, its tiles and its BLAS plus the world's 128 MiB of far grids, which is why the down-slope
  view, whose far field is only 1.2 M triangles, takes 50 MiB more. At 0.5 px the mesh's far field grows (5.8–7.2 M triangles on the
  elevated views) and the height field's does not: VRAM 2322 → 1952, 1762 → 1274, 1721 → 1760, 2064 → 1758 and 1786 → 1311 MiB, and the
  cost ratios narrow to 1.035, 1.267, 0.995, 1.291 and 1.276. 16² tiles (a quarter of the boxes, 20 MiB less) cost 3–5% more on the
  views with far field (1.383, 1.357, 1.443) and the same on the others.
- **The two agree** (`vista-views/sheets/m1-10-far-field.png` and `m1-10-convergence.png`; 4K, 128 spp, one random stream, so the meshes
  over the world grid are identical and only the far field differs). Mean |ΔY| over the pixels whose first hit lies beyond the world
  grid, as a share of their mean (and their mean luminance, mesh against height field):

  | View | Mesh 2 px | Mesh 1 px | Mesh 0.5 px | Noise floor (mesh 1 px, two streams) |
  |---|---|---|---|---|
  | aerial | 4.03% (−0.14%) | 2.14% (−0.16%) | 1.34% (−0.18%) | 0.49% |
  | range | 4.53% (−1.02%) | 2.42% (−0.54%) | 1.58% (−0.38%) | 0.50% |
  | layered ridges | 0.31% (+0.09%) | 0.15% (+0.01%) | 0.11% (+0.01%) | 0.20% |

  The difference halves with the error target and lies in the far band alone, as speckle at the mesh's own error; on the ridges,
  whose far field sits in haze, it is below the noise floor at every target. On the default and down-slope views the 1 px mesh and the
  height field are the same image to 0.00%. 16² and 8² tiles render the same image (0.00%).
- **Deterministic:** the height field rendered twice, and again in 2,444 dispatches of 0.05 ms, gives the same EXR bit for bit.
- **The mesh path is unmoved** on the branch: the aerial view with every debug output and the down-slope view (640 px, 16 spp) are
  byte-identical to `main`'s, PNG and EXR; `--gpu-selftest` passes (50 checks).
- **D3D12 debug layer with GPU-based validation** (a Debug build) reports nothing for a height-field preview with every debug output.
- **CI** (on the branch): 603 tests. The far grids hold the hierarchy's surface at every sample; the tiles cover the far field exactly
  once out to the reach, g1's first; every box holds its tile's samples bent onto the Earth; the local frames are orthonormal and their
  grids within the clip's margin at every sample; the mesh beside the tiles is the world grid's alone, with the far field's reach and
  heights; the same bits every time; tiles that do not divide the inner squares are refused; `--far-field heightField`.

*M1.10 decisions (revisit freely):*

- **The mesh stays.** A still's time is its samples: a 4K final of the aerial view at 1024 spp traces for about 36 s, and the height
  field would add 12 s to save 0.5 s of build and 200 MiB of 8 GB, while the mesh's 973–1282 MiB at 1 px is inside §7.1's terrain
  line. The height field wins only where memory binds, which at 4K it does not.
- **Revisit at M11.** At 8K and 16K the mesh's far field grows with the pixel count and one acceleration structure takes 48 M
  triangles (§11); the height field's grids do not grow, and at 0.5 px (about 1 px at 8K) its cost gap is already down to 1.27–1.29×.
  The branch is the starting point; to ship it would need a C# reference of the walk with a parity probe (§9.1), the self-test's
  checks, and the sidecar's mesh fields (the prototype adds them to `MeshInfo` unversioned).
- **Procedural tiles in the inline ray query, not a traversal of its own.** The hardware BVH over the tiles' boxes is the min/max
  quadtree above the tiles and orders them along the ray with the mesh's triangles, so one query returns the nearest of both. The
  query loop itself is nearly free (with the walk stubbed out it cost 2–6%).
- **The walk runs on the world's grid; the local frame only clips and gives heights.** A straight ray projects onto the sphere along a
  great circle, which the camera-centred map draws straight to millimetres across a tile, so walking the ray's mapped ends visits the
  cells the ray crosses exactly; the local frame's grid is off the bent samples by up to 0.9% of a cell (16² tiles at the reach),
  covered by a margin on the clip and a pad on the height test (both scaled by the ray's slope), and the triangle test's tolerance is
  0.1%, for float rounding.
- **8² tiles.** At 1920 px on the aerial and ridges views (with the first, local-frame walk): 4² 1.36–1.37×, 8² 1.38×, 16² 1.47×,
  32² 1.61–1.71×; 4² needs about 1.5 M boxes.
- **No detail in the far field's heights.** The mesh adds the detail at its far vertices (±1.3 m, under a fifth of a 4K pixel 16 km
  out); the height field's samples do not. g2's normals are central differences in the shader, as `NormalMap` would compute.

*M1.10 gotchas:*

- **Code that never runs can cost half the frame.** The first walk, with the shading inside the query loop and three inlined copies
  (the camera and bounce ray, the sun's and the sky's shadow rays), cost +55% on a view with no tiles at all; register pressure, not
  work. Keeping only (cell, triangle, barycentrics) through the walk and shading once after the query brought it down to the
  default view's 2.5% at 4K.
- **A tolerance that covers a walk's mismatch fattens silhouettes.** With the cells walked in the local frame and a 1–2% tolerance,
  16² and 8² tiles differed by 0.28% in the far pixels' mean luminance; walked on the world's grid they agree to 0.00%.
- **Compare at the final's size.** At 640 px a 1 px mesh is 6 px at 4K, so preview-size comparisons made the mesh look worse than it is
  (7.6% far |ΔY| at 1 px on the aerial view, against 2.1% at 4K).
- **Same stream, same near field:** with one random stream the two renders agree bit for bit until a path reaches the far field, so
  "all pixels" differences are the far field's alone; the noise floor needs two streams.
- **Clipping each tile's ray to its height range barely moved the cost:** it is in the rays that do reach the terrain.
- Vortice's `RaytracingInstanceDescription.InstanceID` is a `UInt24`; `string.Create` with an interpolated string concatenated to a
  plain one does not compile (CS1620); Windows PowerShell 5.1 reads a script without a BOM as ANSI, so the scripts with `·` or `²` keep one.

**M1.10 hand-off.** To understand M1.10, start with the decision in §4.3 and the tables above; then, on the branch
`claude/vista-m1-10-heightfield`, read `Vista.Core/World/FarHeightField.cs` (the grids, the tiles, their boxes and frames), the
`HEIGHT_FIELD` block of `Vista.App/Shaders/VistaIntegrator.hlsl` (`HfIntersect`, `HfShade`, and the query loops in `TraceClosest` and
`Occluded`), and `Vista.App/Gpu/HeightFieldCompare.cs` (how it was measured).

*Commands* (from `RayTracer/`, on the branch):

```
dotnet run --project Vista.App -c Release -- --preview --recipe r.json --seed 42 --out p.png --far-field heightField
dotnet run --project Vista.App -c Release -- --height-field-compare --cameras a.json,b.json [--size 3840] [--errors 1,0.5]
    [--tiles 8,16] [--rounds 3] [--spp 16] [--image-width 3840] [--image-spp 128] [--image-errors 2,1,0.5|none] [--out dir]
powershell -ExecutionPolicy Bypass -File vista-views/heightfield/render.ps1 [-ImageSpp 128] [-Out folder]
```

*Files added or changed* (on the branch; on `main` only this plan and `vista-views/sheets/`):

| File | What |
|---|---|
| `Vista.Core/World/FarHeightField.cs` | `HeightFieldGrids` (the far grids, once per world) and `FarHeightField` (the view's tiles, boxes, frames, margin and pads) |
| `Vista.Core/World/ViewMesher.cs`, `TerrainMesh.cs`, `TerrainHierarchy.cs` | `MeshSettings.HeightField` and its tile size; the world grid's mesh beside the tiles; `MeshInfo`'s tile fields; the far grids kept with the hierarchy |
| `Vista.Core/Cli/PreviewCommandLine.cs` | `--far-field heightField` |
| `Vista.App/Shaders/VistaIntegrator.hlsl` | The `HEIGHT_FIELD` variant: the walk, the shading, the query loops |
| `Vista.App/Gpu/D3D12VistaRenderer.cs`, `D3D12VistaRenderer.HeightField.cs` | The variant's pipeline, the far grids and tiles on the GPU, the procedural BLAS, the two-instance TLAS |
| `Vista.App/Gpu/HeightFieldCompare.cs`, `Program.cs` | `--height-field-compare` |
| `vista-views/heightfield/render.ps1`, `vista-views/README.md` | The M1.10 checks |
| `Vista.Tests/FarHeightFieldTests.cs`, `MeshOptionsTests.cs` | 7 new tests, one new assertion |

**Next: M1.11** (linked sliders and the weather chain).

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
  off-screen geometry. Shadows are covered since M1.9 (off screen the mesh keeps half the sun's angular radius, the
  shadow's own blur); reflections are not yet: water (M3/M4) will need off-screen geometry where its reflected rays go.
- **A cold preview pays the world's hierarchy** (2.7–3.2 s since M1.9) and a 4K camera move about 1.5 s of meshing;
  caching the far grids and a parallel refinement are the levers if either starts to matter.
- **The mesh at 8K and 16K.** The far field's triangles grow with the pixel count: 0.5 px at 4K (about 1 px at 8K) is
  14.6–21.1 M triangles and 1.7–2.3 GiB, and 1 px at 16K (0.25 px at 4K) is about 64 M, past what one acceleration structure
  takes here (M1.9). M11 decides between a coarser target in pixels at 16K, tiles with meshes of their own, and M1.10's height
  field (on its branch), which at 0.5 px saves 0.3–0.5 GiB and costs 1.27–1.29× per sample.

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
  or driver may erode the same seed slightly differently. Since M1.7, `--gpu-selftest` erodes
  seed 42 and compares it with the cached copy, naming both drivers when they disagree.
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
