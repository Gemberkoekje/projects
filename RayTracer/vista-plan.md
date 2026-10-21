# Vista — a Spectral Path-Traced Landscape Wallpaper Generator

The single source of truth for the landscape-wallpaper effort: the design (§1–§9), the roadmap (§10)
and the risks (§11). What each finished slice built, checked and measured is in its log in
[`vista-log/`](vista-log/README.md); its entry in §10 keeps the goal, deliverable and verify plus one
status line that links there. **M0 is done** (2026-10-19): `Vista.Core` / `Vista.App` / `Vista.Tests`
render a recipe + seed end to end on the RTX 3070 through offline integrator v0. M1 is planned as
twenty-one slices in §10 (**M1 plan**); M1.1–M1.10 (iteration tools, astronomy, the spectral atmosphere, aerial
perspective, sun and sky importance sampling, OIDN denoising, GPU erosion, watch mode, the view-dependent terrain mesh and the
measured decision to keep it for the far terrain), M1.10a (exposure and display), M1.11 (linked sliders and the weather chain)
M1.11a (drainage) and M1.11b (the display look) are done, M1.11c (uplift and rock hardness) is next. It sits
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
- **As built (M1.11):** the pins live in a **slider file** (`--sliders`), beside the recipe
  (what the world is made of) and the camera; a slider the file does not name is auto. Ten
  sliders, two of them coarse/fine pairs: season ⇄ date and the time-of-day preset ⇄ the solar
  time (pinned by the clock or by the sun's height). A recipe may still pin the place and time
  fields it held since M1.2, but never beside a slider file. The sidecar records every slider's
  value, whether it was pinned, and what an auto value followed. Log:
  [`vista-log/M1.11.md`](vista-log/M1.11.md).

### 3.5 Location & astronomy

- **Real astronomy:** sun and moon position, moon phase and the star field from
  latitude, date and time (Meeus-style algorithms / the NOAA solar position algorithm).
- **Location dropdown** of large places and iconic landscapes so nobody has to look up a
  latitude: e.g. Yellowstone, Banff, Yosemite, Lofoten, Iceland, Scottish Highlands,
  Zermatt, Dolomites, Torres del Paine, Fiordland, Hokkaido — plus "custom latitude".
  The location sets **only the sky and the climate defaults**; the recipe decides what
  the terrain looks like.
- **Time of day is local solar time**, or a preset by the sun's height (M1.11; M1.2's
  offsets from sunrise and sunset put golden hour anywhere from 4° to 11° up): dawn is the
  rising sun at −4°, golden hour the setting sun at +2°, dusk the setting sun at −4°, day and
  night solar noon and midnight. Longitude and time zones drop out.
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

*As built (M1.11; [`vista-log/M1.11.md`](vista-log/M1.11.md)):* the normals are 1991–2020
station tables (stand-ins where a place has no station: Skrova for Lofoten, Hjarðarland for
Þingvellir, Tulloch Bridge for Glen Coe, Punta Arenas for Torres del Paine) moved to the
scene's valley floor along 6.5 °C/km; a custom latitude uses North, Cahalan & Coakley's (1981)
zonal-mean fit. The daily curve is two half cosines, the low at sunrise and the high at 14:00.
The dew point is the day's low (FAO-56), so the humidity follows the temperature. The **snow**
slider is the seasonal snowline in metres above sea level, from a degree-day snowpack run over
the climate's year (snow is history), moved by half of the freezing level's move when the
temperature is pinned or pushed (temperature nudges it). The **fog/haze** slider is the
visibility at the valley floor (the WMO's meteorological optical range); its auto value is the
haze grown by the **day's** humidity, (1 − RH)^−0.4, so moving the time of day does not rebuild
the air's tables. Only the frozen lake pushes defaults so far (winter, ≤ −5 °C).

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
  wetness maps that later stages reuse (rivers, lakes, snow, vegetation). Built in M1.7 ([`vista-log/M1.7.md`](vista-log/M1.7.md)):
  virtual-pipe water and thermal erosion over bedrock and loose material, on a 4097² × 8 m
  grid (32.8 km), cached per seed.
- **Drainage before erosion** (M1.11a, [`vista-log/M1.11a.md`](vista-log/M1.11a.md)): the ridged base terrain is half closed
  basins, which the pipe model cannot open (its rain evaporates long before a basin hundreds of metres deep fills). So rivers first
  incise the base terrain on a 64 m grid by the stream-power law, on the CPU, with the rain routed through the filled hollows: each
  basin's rim cuts down, the lake behind it fills a little each step, and the basins become valleys that branch and join. The GPU
  erosion then carves the gullies and lays the loose material on the 8 m grid as before. Every eroded world carries its
  **drainage** (each cell's way down over the depression-filled surface, its upstream area, and how deep its hollow fills), cached
  with it: the flow accumulation materials (M1.12) and rivers (M3) read.
- **Geometry:** a 16k² height field as triangles (~500M) does not fit in 8 GB. Because
  the camera is fixed for a still, build a **view-dependent mesh once per camera** to a
  screen-space error target, conservative for off-screen geometry that still casts
  shadows or appears in reflections. Alternative for far terrain: a custom height-field
  intersection (min/max quadtree) in the inline ray query loop. Decide in M1 by measuring
  (M1.9, M1.10). M1.7 measured the stopgap, the uniform mesh of the whole eroded grid:
  33.6 M triangles, 805 MB plus a 2.35 GB BLAS. Beyond the grid, elevated views still see
  9–18% of the frame ([`vista-log/M1.7.md`](vista-log/M1.7.md)), so the far field belongs to M1.9/M1.10 too.
  **Built in M1.9** ([`vista-log/M1.9.md`](vista-log/M1.9.md) has the table): a 4-8 mesh refined to 1 px of the frame's centre on screen and half the
  sun's angular radius off screen, over the eroded grid, a far field of base terrain at 32 m and 256 m out past the
  horizon, and seeded detail below the grid near the camera, crack-free by saturated errors, bent onto the Earth, with
  normal maps putting the grid's shading back on coarse triangles. At 4K a view takes 6.4–9.8 M triangles and
  1.0–1.3 GiB for the process (the uniform mesh: 33.6 M and 3.2 GiB); a 320 px preview 0.26–1.5 M. The far field is
  40–56% of an elevated view's triangles: what M1.10 measures a height-field path against.
  **Decided in M1.10** ([`vista-log/M1.10.md`](vista-log/M1.10.md) has the tables): **the mesh stays the far terrain's path.** The height field — the far grids
  traced as procedural tiles in the inline ray query, the cells walked on the world's own grid and their triangles bent onto
  the Earth exactly — renders what the mesh converges to as its error target shrinks, and saves about 200 MiB of video memory
  and 0.4–1.2 s of build per 4K view, but costs 1.27–1.39× per pixel-sample on the views that show the far field (parity on
  those that do not), and a still pays for its samples. Revisit at M11 (8K, 16K), where the mesh's far field grows with the
  pixel count and the height field's does not; the prototype is on the branch `claude/vista-m1-10-heightfield`.
- **Materials by rule:** rock strata, scree, soil, grass, moss, snow, wet vs. dry —
  placed by slope, altitude, aspect, erosion flow and moisture.

### 4.4 Hydrology & water bodies

- Lakes and rivers placed from the erosion flow map, with the shot-first basin for the
  "lake" building block. Since M1.11a the eroded world's drainage gives the flow accumulation and
  where hollows would fill; the pools the GPU erosion's sediment dams along the valley floors are
  filled to their outlets (flat floors), so M3's rivers need channels cut through them.
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
- **Metering** (M1.10a, [`vista-log/M1.10a.md`](vista-log/M1.10a.md)): an incident meter by default — an 18% grey card
  where the camera stands, facing the sun (never below the horizontal), lit by the sun through the air and the camera's
  sky view, at middle grey — so the light of the place and hour sets the exposure, not the framing. The frame's
  log-average (M1.3) stays as `--meter average`; the camera's EV is a compensation on top of either.
- **Filters as camera settings** (pillar 2): spectral sensor response curves and film
  stocks instead of LUTs; real white balance as chromatic adaptation against the scene
  illuminant; lens chromatic aberration from the dispersion IOR.

### 5.7 Output

- **Denoising:** Intel Open Image Denoise (Apache 2.0, C API via P/Invoke, CPU or GPU),
  fed albedo and normal buffers. Built in M1.6 ([`vista-log/M1.6.md`](vista-log/M1.6.md)): OIDN 2.5.1, fetched and checksummed, never committed;
  `--denoise` writes the denoised image beside the raw one.
- **Formats:** EXR (scene-referred, for the user's own grading) plus PNG/JPEG through a
  filmic tonemap: AgX since M1.10a (three.js r169's port of Blender's AgX base), with its punchy look and
  +1 EV on the incident meter since M1.11b ([`vista-log/M1.11b.md`](vista-log/M1.11b.md); `--look base`, `--compensation`),
  the per-channel ACES fit one option away (`--display aces`).
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
single target of **3840×2160**. Milestones are cut into slices (M1's are below), and the rules
for a slice hold for every milestone.

*Every slice:*

- **Lands directly on `main`, then work stops for review.** There is no PR gate (the user's choice,
  2026-10-21): the user reviews each slice on `main`. Its commits and its log say what was measured
  and with which commands.
- **Green before main.** Because nothing enforces it, before pushing: the whole solution builds in
  Release, all tests pass (the full solution, not only `Vista.Tests`), and `Vista.App --gpu-selftest`
  passes on the RTX 3070, growing with the slice where noted. No engine change is planned; a slice
  that does touch `RayTracer.Core` / `RayTracer.Gpu` also passes `--phase6-selftest` and
  `--phase6-regress` (§9.2). The CI run on `main` confirms afterwards; a red run there is fixed
  before anything else lands.
- **A slice that changes the look** commits a before/after contact sheet to `vista-views/sheets/`
  (with its row in that folder's README) and adds a row to the cost ledger
  ([`vista-log/cost-ledger.md`](vista-log/cost-ledger.md)).
- **Sheets stay small:** PNG (lossless, so noise and banding can be compared), cells of at most
  640 px, about 1 MB a sheet at most. Where full-resolution detail matters, commit a crop of that
  region (a loupe), not the whole 4K frame. No Git LFS. (Since 2026-10-21; the M1.1–M1.10 sheets,
  70 MB in all, stay as committed.)
- **It ends with its log**, `vista-log/<slice>.md`, in the layout of the existing logs (status,
  checks, decisions, gotchas, hand-off, commands, files, next; [`vista-log/README.md`](vista-log/README.md)).
  Its entry here gets one status line linking to it. A decision that changes the design or a later
  milestone is also written where it applies (§1–§9, that milestone's entry, §11), not only in the log.

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

**M0 status (2026-10-19): done.** Integrator v0 and Vista's own D3D12 host render a recipe + seed end to
end on the RTX 3070, bit-identically under any dispatch budget; the only engine change is one
`InternalsVisibleTo` line (§8.1). Checks, the measured batch defaults (250 ms dispatches; the 16K
row-band risk), decisions, hand-off and gotchas: [`vista-log/M0.md`](vista-log/M0.md).

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

**M1 plan (agreed with the user 2026-10-19).** Twenty-one slices in the order below, each under the
"every slice" rules at the top of this section. (M1.1 went through a PR; from M1.2 on, slices land
directly on `main`.)

*Added 2026-10-21, after a review of the M1.1–M1.10 sheets (agreed with the user):* **M1.10a —
Exposure & display** and **M1.11a — Drainage**, both before the materials (M1.12), so rock and snow
are tuned once, under the final light and on the final terrain, as the strategy below intends. M1.11
gains time-of-day presets by sun height, and M1.12 gains near-camera detail by material. The letter
suffixes keep the code's references to M1.11–M1.17 valid.

*Added 2026-10-21, after a review of the M1.10a–M1.11a sheets (agreed with the user):* **M1.11b — The
display look** (AgX's punchy look and a default compensation, by sheet, because incident + plain AgX
reads dark and flat) and **M1.11c — Uplift & rock hardness** (an uplift map so ranges keep their
massifs, a rock-hardness field against uniform 45° facets, and the two measurement cameras the
incision left 500 m up re-placed), both still before the materials. M1.12's strata read M1.11c's
hardness field; M1.13 gains per-place precipitation and glacial U-valleys; M3 takes M1.11a's flat
floors as lake sites; M4 gains a seasonal aerosol load. M1.11b's look and M1.10a's meter are
re-judged at the sign-off (M1.17), once real materials exist.

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
  The EXR stays scene-referred. (M1.10a makes an incident meter the default; the log-average stays
  an option.)
- **Linked sliders land before materials and snow**, so snow reads its freezing level from the
  weather chain instead of a stub that gets rewired later.
- **The view-dependent mesh comes first, height-field intersection second** (M1.9, M1.10). The mesh
  rides the 3070's hardware triangle path, so it is the default; the height-field path is built only
  far enough to measure it against the mesh on the same views. M1.10 measured it: the mesh stays (§4.3).
- **The estimate comes before the window**, so the window can show it.
- **Vista goldens arrive at the sign-off (M1.17).** Every look slice moves every image, so earlier
  goldens would only be re-baselined each time. Until then the self-test's bit-identity checks guard
  determinism and contact sheets show the look.

*Cost ledger:* in [`vista-log/cost-ledger.md`](vista-log/cost-ledger.md): ns per pixel-sample on the
four measurement views at 3840×2160, 64 spp, seed 42, one row per slice.

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

*Status (2026-10-19): done.* `--sheet`, `--sheet-from` and `--loupe` (a loupe is bit for bit a crop of
the final); the measurement views are committed in `vista-views/`. Log:
[`vista-log/M1.1.md`](vista-log/M1.1.md).

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

*Status (2026-10-19): done.* The NOAA sun (within 0.0002° and 1.2 s of NOAA's own code), time-of-day
presets relative to sunrise and sunset, season ↔ date by hemisphere, the location list. Log:
[`vista-log/M1.2.md`](vista-log/M1.2.md).

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

*Status (2026-10-20): done.* The spectral atmosphere in physical units, with PNG auto exposure:
Hillaire-style transmittance and sky-view LUTs, multiple scattering from Bruneton 2017's scattering
orders (Hillaire's Ψ_ms missed the reference by 2–4×), and `--atmosphere reference` as the brute-force
check. Twilight, the Earth's shadow, the Belt of Venus and red summits after the valley's sunset emerge.
Log: [`vista-log/M1.3.md`](vista-log/M1.3.md).

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

*Status (2026-10-20): done.* Aerial perspective as a short march through the LUTs (chosen over the
sky-view difference by error and cost) and the altitude control; its segment check found and fixed four
errors in M1.3's LUT model. The first real cost jump (+21–48% on terrain). Log:
[`vista-log/M1.4.md`](vista-log/M1.4.md).

**M1.5 — Sun & sky importance sampling.**
- *Goal:* converge golden hour and twilight, where the sky carries most of the fill light.
- *Deliverable:* a sky distribution per render (a piecewise-constant 2D CDF over the sky-view LUT,
  luminance-weighted over the wavelengths), the sun disk as its own light, and multiple importance
  sampling (power heuristic) between BSDF, sky and sun sampling. Bounce rays that hit the sun disk
  now count, with their MIS weight. Russian roulette is re-measured.
- *Verify:* 3070: unbiased, because at 4096 spp the new estimator matches the old one within the
  per-pixel standard error (variance image); variance at equal spp on dawn, golden-hour and day
  views drops by a measured factor, with the time per sample recorded; the self-test passes.

*Status (2026-10-20): done.* A sky distribution over the sky-view LUT, the sun disk as its own light, and
MIS; only a path's first surface samples the sky, Russian roulette starts at bounce 2, and the estimator
is checked unbiased against M1.4's. Log: [`vista-log/M1.5.md`](vista-log/M1.5.md).

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

*Status (2026-10-20): done.* OIDN 2.5.1 behind a `Vista.Core` seam, fetched and checksummed by
`tools/fetch-oidn.ps1`, never committed; CPU by default, CUDA 16–21× faster with the same error,
bit-identical on both. Log: [`vista-log/M1.6.md`](vista-log/M1.6.md).

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

*Status (2026-10-20): done.* Virtual-pipe hydraulic and thermal erosion on the GPU over a 4097² × 8 m
world (32.8 km), 19 s a seed, cached with its hash and checked against a C# reference. **Weaker than
planned:** the flow map is the simulation's mean discharge, whose channels are short, so M3's rivers (and
anything else that reads flow) need a real flow accumulation. Log:
[`vista-log/M1.7.md`](vista-log/M1.7.md).

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

*Status (2026-10-20): done.* `--watch` keeps the device, shaders, world, mesh, acceleration structures
and LUTs alive and rebuilds only the stages a change touches; every watch render is bit-identical to a
cold `--preview`. Until M1.11's slider files it reads an options file. Log:
[`vista-log/M1.8.md`](vista-log/M1.8.md).

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

*Status (2026-10-20): done.* The terrain is meshed per view: a 4-8 mesh to 1 px over the eroded grid, a
far field to the horizon, seeded detail near the camera, bent onto the Earth, with normal maps on coarse
triangles. At 4K: 6.4–9.8 M triangles, 1.0–1.3 GiB, about 1.5 s to build. Two approximations remain: the
sky view is read in the camera's frame across the far field (its vertical tilts up to 4°), and aerial
perspective's 8-step march to the far field was never compared with the reference. Log:
[`vista-log/M1.9.md`](vista-log/M1.9.md).

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

*Status (2026-10-21): done; the mesh stays.* The height-field far field renders the same image and saves
about 200 MiB and 0.4–1.2 s of build a view, but costs 1.27–1.39× per pixel-sample where the far field
shows. Revisit at M11 (§4.3); the prototype is on the branch `claude/vista-m1-10-heightfield`. Log:
[`vista-log/M1.10.md`](vista-log/M1.10.md).

**M1.10a — Exposure & display.**
- *Goal:* golden hour that looks golden, so the materials and snow (M1.12, M1.13) are tuned under the
  exposure and display curve they will ship with.
- *Why (the sheet review, 2026-10-21):* the golden-hour views show pale cream sunlit faces and
  saturated blue shadows. The log-average meter over-exposes the sunlit slope by 3.9 EV on the
  down-slope view (`m1-3-down-slope-exposure.png`), because half the frame is in shadow; and the
  display curve, a per-channel ACES fit, pushes bright warm colours toward yellow-white on its shoulder.
- *Deliverable:* an **incident meter**, the new default: the exposure that puts an 18% grey card facing
  the sun, lit by the sun (through its transmittance) and the sky the renderer already computes, at
  middle grey. It does not depend on the framing; with the sun below the horizon only the sky term is
  left. The log-average meter stays an option (`--meter average|incident`), EV compensation stays, and
  the sidecar records the meter and its reading. **AgX** as the default display curve, with the ACES
  fit one option away (`--display agx|aces`). The EXR stays scene-referred and unchanged; a watch
  re-exposes and re-displays a kept render without rebuilding anything upstream.
- *Verify:* 3070: the four measurement views, a golden-hour view and the alpenglow sweep (+2° to −1.5°)
  under {average, incident} × {ACES, AgX}, from the same EXRs (byte-identical; only the PNGs differ);
  the sunlit slope no longer clips; dawn and dusk are exposed from the sky alone. CI: the meter's
  maths (a sun-facing card; the sun below the horizon) and each display curve against values from the
  implementation it is ported from. Cost ledger: no change (display only).

*Status (2026-10-21): done.* The incident meter (a grey card at the camera, facing the sun, lit by the sun
through the air and the camera's sky view; about 1 ms of GPU) and AgX (three.js r169's) are the defaults;
`--meter average --display aces` writes `main`'s PNG byte for byte. The washed-out slope is the meter's doing:
down the slope the card exposes 2.24 EV below the log-average, and the sunlit faces drop from near white to
mid-tones with their colour. AgX is flatter and less saturated than the ACES fit at the same exposure, and
skies come out darker (the sky-only view 2.5 EV), both left for review. Log: [`vista-log/M1.10a.md`](vista-log/M1.10a.md).

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
  value and whether it was auto or pinned. **Time-of-day presets by sun height** instead of offsets
  from sunrise and sunset: M1.2's "sunset − 60 min" put golden hour anywhere from 4.3° to 10.7° up,
  depending on place and season (`m1-2-places-golden-hour.png`), while the warm light only arrives
  around 2° and below (`m1-3-peaks-lit.png`). Dawn and dusk at −4° (rising, setting), golden hour at
  the height a +2° / +4° / +6° sheet picks (about +4° expected), day at solar noon, night at solar
  midnight; where the sun never reaches a preset's height, M1.2's polar fallbacks apply, with the hint.
- *Verify:* CI: the chain (pinning temperature still lets snow follow; reset re-links; a set piece
  pushes its default; the implausible hint; autumn at Torres del Paine is April; Lofoten milder than
  its latitude model); each preset lands at its sun height at every dropdown place in every season,
  or falls back with the hint. 3070: a month sheet at one place changes sun and haze from the
  defaults alone; the golden-hour height sheet.

*Status (2026-10-21): done.* Ten linked sliders, auto or pinned, in a slider file (`--sliders`), and the weather chain from 1991–2020
station normals: the daily curve, the dew point, the freezing level, a degree-day snowline and haze grown by the day's humidity, all in the
sidecar. The presets are sun heights, and the height sheet put golden hour at **+2°**, not the +4° expected: only there do sunlit faces read
golden. Left for the review: +2° itself, and a haze that humidity alone barely moves through the year. Log:
[`vista-log/M1.11.md`](vista-log/M1.11.md).

**M1.11a — Drainage.**
- *Goal:* valleys that branch and join like real drainage, before the materials read the erosion maps
  (M1.12) and the snow reads their gullies (M1.13).
- *Why (the sheet review, 2026-10-21):* the eroded slopes show evenly spaced, parallel fluting, and the
  discharge map shows short, scattered rills rather than tributaries feeding main valleys
  (`m1-7-maps.png`, `m1-7-seeds.png`): M1.7's 1500 steps at 8 m do not carry water across a catchment
  (M1.7's status).
- *Deliverable:* a **real flow accumulation** (each cell's upstream area over the eroded surface, with
  depressions filled or breached), deterministic, cached with the world and shown beside the existing
  maps; M1.12 and M3 read it. Then **coarse-to-fine erosion**: M1.7's passes at 32 m and 16 m for many
  more steps, upsampled and refined at 8 m, reusing the passes and their C# reference. **Only if that
  does not give branching valleys:** a stream-power uplift-and-erosion model (Cordonnier et al. 2016),
  which grows dendritic drainage by construction. The choice is measured: the longest channel, the
  share of the grid that drains into channels above a threshold, the network's Strahler orders, the
  erosion time per seed, and a four-seed sheet.
- *Verify:* CI: flow accumulation on synthetic surfaces (a tilted plane, a V-shaped valley, a pit) and
  its determinism; the coarse-to-fine reference conserves water and sediment as M1.7's does. 3070: the
  measures above before and after, on four seeds; the eroded world bit-identical twice and through the
  cache; the self-test's driver check against the default erosion cache re-baselined deliberately
  (every cached world changes); the measurement views' cameras still above the ground (a test flags
  it); a cost-ledger row.

*Status (2026-10-21): done, by the fallback.* The flow accumulation showed the ridged base terrain is half closed basins (43–51% of the
grid, 41–49% after M1.7's erosion), and coarse-to-fine could not open them: sixteen times M1.7's steps on its 32 m level still left 41%,
so it was not built. **Stream-power incision** on a 64 m grid, on the CPU before the GPU erosion (13–15 s a seed), drains every basin;
the pools the GPU erosion's sediment leaves fill to their outlets. On the eroded world the longest lake-free channel runs 38–51 km
(M1.7: 1.6–1.9) and 98% of the grid drains into a channel of 1 km² (3–6%). The coarse-to-fine conservation check became the incision's own checks.
Log: [`vista-log/M1.11a.md`](vista-log/M1.11a.md).

**M1.11b — The display look.**
- *Goal:* finished-looking images under M1.10a's incident meter, so the materials (M1.12) are tuned
  under the look they will ship with.
- *Why (the review of 2026-10-21):* the incident meter is right (the sunlit slope keeps its colour),
  but incident + plain AgX reads dark and flat: the day views grey next to the old look, the down-slope
  view muddy (`m1-10a-before-after.png`, `m1-10a-down-slope-variants.png`). Plain AgX is a base meant
  for grading. The darker skies are plausible (the part of a clear sky 70–90° from the sun is its
  darkest), so the sky gets no correction of its own.
- *Deliverable:* AgX's punchy look as an option of the curve (`--look base|punchy`: power 1.35 and
  saturation 1.4 after the sigmoid, as Blender's and Filament's), and a default exposure compensation
  for the incident meter. Both chosen by one sheet: {AgX, AgX punchy} × {0, +0.5, +1 EV} on the four
  measurement views, the default view at golden hour (+2°), the alpenglow sweep and the ridges in
  October at +2°. The choice is provisional: it and M1.10a's meter are re-judged at M1.17, once real
  materials exist. The EXR is unchanged; `--look base` with no compensation writes M1.10a's PNG byte
  for byte.
- *Verify:* 3070: the sheet, and the chosen defaults on the measurement views beside M1.10a's. CI: the
  punchy look against values from the implementation it is ported from. Cost ledger: no change
  (display only).

*Status (2026-10-21): done.* **AgX punchy at +1 EV** is the new default (`--look punchy`, the incident meter's `--compensation`
+1): on the day and golden-hour views it is as bright as plain AgX at +0 (the frame's mean 129, 129, 97 and 147 of 255 against 128,
126, 99 and 145) with 1.6–2.5 times its saturation, because the look's power puts middle grey a stop lower (sRGB 88 for 128).
Plain AgX at +1 is brighter and paler still. Down the slope every variant is dark (M1.11a left that camera 552 m above a valley in
shadow; M1.11c re-places it), and punchy darkens it most. The sky-only view turns deep blue, its red channel at 0. `--look base
--compensation 0` writes `main`'s PNG byte for byte; the EXR is unchanged. Sheets: `m1-11b-before-after.png`,
`m1-11b-golden-looks.png`, `m1-11b-ridges-looks.png`, `m1-11b-down-slope-looks.png`, `m1-11b-alpenglow.png`.
Log: [`vista-log/M1.11b.md`](vista-log/M1.11b.md).

**M1.11c — Uplift & rock hardness.**
- *Goal:* ranges with hierarchy (high massifs, broad main valleys, lower foothills) and slopes that
  vary (cliffs, ledges, gentler ground) instead of uniform facets, before the materials (M1.12) are
  tuned on the terrain.
- *Why (the review of 2026-10-21):* M1.11a's incision gave real drainage, but from the aerial camera
  the worlds became a uniform field of similar peaks to the horizon and lost the base terrain's
  massifs (`m1-11a-seeds.png`), and many peaks show planar facets at the talus angle
  (`m1-11a-views.png`). Both follow from uniform uplift and uniform rock. And the incision lowered the
  ground under two pinned measurement cameras: the default and down-slope views now stand 532 and
  552 m above a valley floor, so "down the slope" no longer shows every pixel as terrain (the
  ledger's M1.11a note).
- *Deliverable:* an **uplift map** from the base terrain's large-scale shape (its lowest octaves),
  seeded, so the massifs rise and the foothills stay low; Cordonnier et al. 2016 take an uplift map as
  their main input. A **rock-hardness field** (seeded and procedural: warped strata bands plus noise)
  that sets the incision's erodibility and the thermal erosion's talus angle, so hard bands stand as
  cliffs and ledges and soft ones weather back; the field is stored with the world, and M1.12's rock
  strata read the same field, so cliffs and material bands line up. It also varies the fluting M1.7's
  passes cut, which on big faces now reads like couloirs, rather than removing it. The flat floors
  behind fans stay (M3's lake sites). The default and down-slope cameras are re-placed to their
  original intent on the new terrain (the default near the ground facing the range, the down-slope
  view all terrain), and the ledger notes that their rows before and after are not comparable.
- *Verify:* CI: both fields are pure functions of seed and position; hardness moves erodibility and
  talus angle as specified (synthetic tests); the incision still drains every basin (M1.11a's
  checks). 3070, before and after on four seeds: the aerial sheet; the relief's hierarchy, measured
  (the spread of local relief at 1 km and 5 km scales); the share of slopes within ±2° of the talus
  angle (the facets, measured); M1.11a's drainage numbers holding (the longest channel, the share
  drained). The world bit-identical twice and through the cache; the self-test's driver check against
  the default erosion cache re-baselined deliberately; a cost-ledger row.

**M1.12 — Materials by rule.**
- *Goal:* rock that reads as rock: strata, scree, soil, alpine grass and wet ground where nature
  puts them (§4.3).
- *Deliverable:* materials chosen and blended in the shader per hit, from slope, altitude, aspect
  and the erosion maps (M1.11a's flow accumulation, deposition, wetness), all procedural: rock strata
  from M1.11c's hardness field (so the material bands line up with the cliffs and ledges it shaped),
  scree on deposition under steep faces, soil, grass as a material (its
  geometry is M5), and wet darkening. Built-in-tier spectra (tier 5, recorded per material).
  Rough-diffuse rock (Oren–Nayar-style) is measured against Lambert in a sheet, because backscatter
  from a low sun is the golden-hour look. The rules live in `Vista.Core`, with tests. **Near-camera
  detail by material:** M1.9's seeded detail is smooth gradient noise, which reads as sand or snow
  drifts in the 4K loupes (`m1-9-loupes.png`). Rock gets sharp, broken or terraced detail (ridged
  noise, strata steps), scree small angular bumps, and soil and grass stay smooth. The detail is
  geometry, so the material rules run where the mesh is built (C#) as well as per hit (HLSL),
  from one definition with a parity test, and each kind's error bound is measured as M1.9's was.
- *Verify:* CI: rule tests (steep → rock, deposition below a steep face → scree, …); the detail's
  bounds per kind. 3070: material-id and tier previews over four seeds; a before/after beauty sheet;
  a 4K loupe sheet of rock, scree and soil near the camera; a cost-ledger row.

**M1.13 — Snow & glaciers.**
- *Goal:* snowcaps that read as real because of where the snow sticks (§4.5).
- *Deliverable:* snow cover from slope (shed above ~35–40°), aspect (poleward faces keep more,
  hemisphere from the latitude), wind and curvature (ridges scoured and hollows filled, from the
  erosion curvature), and altitude against the freezing level and the snow slider (M1.11: a seasonal snowline in metres above sea
  level; both are in the sidecar's `sliders`).
  Recipe-owned glaciers and permanent snowfields for the mountain set pieces, carved at the world
  stage, so snow at zero leaves them. **Glacial carving:** where the recipe's glaciers ran, the
  world stage carves U-shaped valley cross-sections, cirques at their heads and the arêtes and horns
  between them; M1.11a's incision only cuts V-shaped valleys, and the U-valley is what makes
  snow-capped alpine scenery read as alpine. **Per-place precipitation:** M1.11's chain uses one
  precipitation for every place, so maritime snowlines (Fiordland, Lofoten) come out too high and dry
  ones (Yellowstone) too low (M1.11's known limits); the climate table gains monthly precipitation per
  place, hand-entered with a source like the normals, and the snowline default uses it. A dry / wet /
  melting state by altitude against the freezing level. A built-in snow spectrum from ice absorption
  and grain size (tier 5). Russian roulette is re-measured on bright snow (M0 note). Close-up snow
  stays in M7.
- *Verify:* CI: the placement rules; the snowline default moving with each place's precipitation. 3070:
  a glacier valley's cross-section measured U-shaped (its floor's width at half depth against the
  incised V's); **bare steep faces**, because on the test views the snow
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
  window saves; the M1 checks walked through in the window, noted in its log.

**M1.17 — M1 sign-off.**
- *Goal:* close M1 against its verification.
- *Deliverable:* the Vista golden suite at preview size (fixed seeds, committed PNG/EXR goldens,
  `--golden-check` with a tolerance, re-baselined deliberately) covering the M1 checks; the hero
  render "alpine range at golden hour" at 3840×2160 with its sidecar; the M1 status, decisions,
  gotchas and hand-off in its log, and M1's overall status in this section.
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
rivers from M1.11a's flow accumulation (its filled pools, the flat floors behind fans whose
fill depths the world cache already holds, are natural lake sites); spectral water (pure-water absorption, organics, glacial
flour); ripples; wet shores; reflections. Verify: a lake shows turquoise shallows
grading to blue depth from absorption alone; the mountain backdrop reflects correctly.

**M4 — Fog & clouds.**
Goal: atmosphere you can see. Deliverable: altitude fog, valley inversion fog / sea of
clouds, steam fog from lake vs. air temperature, humidity haze, frost on surfaces;
volumetric clouds with multiple scattering; spectral Mie phase (fogbows, glories). A
**seasonal aerosol load** per place: M1.11's haze follows the day's humidity alone, so at
Zermatt it barely moves through the year (79–84 km visibility) and is, if anything, hazier
in winter (M1.11's known limits), where a real alpine summer is hazier from convection and
winter inversions trap the haze below the peaks. Verify: a cold autumn dawn over a lake
produces steam fog from the defaults alone; a month sheet at one place shows summer hazier
than winter from the defaults alone.

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
  on the 3070 ([`vista-log/M0.md`](vista-log/M0.md), "M0 batch defaults, measured"); M1–M5 add the expensive parts, and the
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
  (~6× on M0 terrain); M11 must plan bands defensively ([`vista-log/M0.md`](vista-log/M0.md), "M0 batch defaults, measured").
- **Mass TLAS instancing** is new; today's TLAS holds a handful of instances.
- **Leaf alpha** needs non-opaque candidates in the inline ray query loop, slow on Ampere;
  real leaf geometry costs memory.
- **GPU determinism across driver updates** is not guaranteed; goldens may need
  re-baselining.
- **Eroded worlds depend on the GPU driver** (M1 decision: erosion stays on the GPU). The
  per-seed cache and the sidecar's height-field hash make a change visible, but a fresh machine
  or driver may erode the same seed slightly differently. Since M1.7, `--gpu-selftest` erodes
  seed 42 and compares it with the cached copy, naming both drivers when they disagree. The
  stream-power incision before the GPU passes (M1.11a) runs on the CPU and is the same on every
  machine; only the passes depend on the driver.
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
