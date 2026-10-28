# Vista — a Spectral Path-Traced Landscape Wallpaper Generator

The single source of truth for the landscape-wallpaper effort: the design (§1–§9), the roadmap (§10)
and the risks (§11). What each finished slice built, checked and measured is in its log in
[`vista-log/`](vista-log/README.md); its entry in §10 keeps the goal, deliverable and verify plus one
status line that links there. **M0 is done** (2026-10-19): `Vista.Core` / `Vista.App` / `Vista.Tests`
render a recipe + seed end to end on the RTX 3070 through offline integrator v0. M1 is planned as
twenty-seven slices in §10 (**M1 plan**); M1.1–M1.10 (iteration tools, astronomy, the spectral atmosphere, aerial
perspective, sun and sky importance sampling, OIDN denoising, GPU erosion, watch mode, the view-dependent terrain mesh and the
measured decision to keep it for the far terrain), M1.10a (exposure and display), M1.11 (linked sliders and the weather chain),
M1.11a (drainage), M1.11b (the display look), M1.11c (the output finish), M1.11d (uplift and rock hardness), M1.12 (materials by
rule), M1.12a (the colour quadrature), M1.12b (forest as material), M1.12c (the world anchored to the place's valley floor,
and folded strata), M1.13 (snow and glaciers), M1.13a (the valley haze layer), M1.13b (terrain-following valley mist), M1.14
(auto camera v1, with its detail budget), M1.15 (the time and VRAM estimate), M1.16 (the preview and tweak window) and
M1.17 (the sign-off) are done: **M1 is done** (2026-10-28), its goldens committed and passing and its hero rendered. M2 (measured
spectra) is next, its slices planned first; what M1 found and did not need is in the parked list (§10). It sits
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

*As built (M1.16, `Vista.App --window`; [`vista-log/M1.16.md`](vista-log/M1.16.md)):* the window holds a scene and writes it as the
CLI's own files (recipe, camera, slider and options files) before each run, and runs it as the CLI's own command line, so what it shows
is what the CLI renders from the files it saves. Its preview is 640 px wide by default and refines pass by pass, the final's own passes
read back as they finish; a change restarts it once the controls have been still for 0.2 s. The camera keeps eye height above the
ground, its detail and the forest's crowns; there is no water to keep out of until M3.

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
- **Weather:** temperature (°C, with a small °C/°F toggle), humidity, and since M1.13a the valley haze (since M1.13b the
  visibility at the floor of the densest valley's pool, §3.6) and since M1.13b the mist (the visibility in the scene's valley's
  mist, or none).
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
  not instant — still deterministic from seed + slider, so nothing moves. *Measured in M1.16's window:* the camera and the
  exposure are the fast speed (0.1–1.7 s at 640 px), but the time of day and the fog are not: since M1.13a the auto valley
  haze follows the hour, and any change of the air rebuilds its tables, about 9.5 s at half the GPU (the parked list).
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

*Since M1.12c ([`vista-log/M1.12c.md`](vista-log/M1.12c.md)):* the scene's valley floor is the world's own lowest valleys, the
5th percentile of its ground. The altitude slider is still the altitude of the world's height 0, but its auto value puts that
footing, not height 0, at the place's valley floor (never below sea level), and the weather chain reads the air there: the
temperature, the humidity and the visibility at the valley floor, whatever the world's footing. A pinned altitude stays the
altitude of height 0; the valley floor then stands the footing above it.

*Since M1.13 ([`vista-log/M1.13.md`](vista-log/M1.13.md)):* each dropdown place carries its own monthly precipitation (1991–2020,
from its temperatures' station but for Lofoten, whose is Leknes'), and the snowpack falls by it: each day the month's rate
interpolated between the months' middles, 2.5% more per 100 m above the station (Huss and Hock 2015's upper value, up to twice the
station's), snowfall counted 1.2 times what the gauge caught (gauges miss snow). Maritime snowlines come down (Fiordland's January
line 1733 → 1098 m, Lofoten's July line 1193 → 835 m), dry ones go up (Banff's April line 966 → 1316 m); a custom latitude keeps
M1.11's one rate. The season's snow and the freezing level now show: snow lies above the snowline by the material rules, wet below
the freezing level and dry above it (§4.5).

*Since M1.13a ([`vista-log/M1.13a.md`](vista-log/M1.13a.md)):* the **valley haze** slider (advanced), the visibility under the lid
of the haze a cold pool holds in the valleys, auto or pinned. Its auto value is a chain of its own from the climate's day: the
night's cooling at the valley floor below the free air is 0.75 of the day's normal range (the night before a clear sky was clear;
a cloudy or windy coast has a small range), and the pool is as deep as that cooling, undone at its top by the inversion's
10 °C/km rise and the lapse rate (at most half the world's relief): 480 m at Zermatt in April, under 200 m at Lofoten, 800 m at
Yosemite in July. The morning sun breaks it up: what is left is the share of its heat deficit the clear-sky sun since sunrise has
not made up (Whiteman and McKee 1982's model, simplified: 0.3 of the sunlight the floor absorbs heats the air, twice as fast in a
valley; snow on the floor turns most of it away, so a snowy valley keeps its pool all day), and its lid sinks as it goes, about
four hours after sunrise in April; after the day's high it forms again as the air cools. Under the lid the valley's own haze is
held (up to twice as much by dawn) and grown by the hour's humidity against the day's; the layer is what that adds. A pinned
visibility under the lid takes the night's whole pool at any hour; `off` (500 km) adds nothing. Because the auto layer follows the
hour, the time of day now moves the air's tables (about 1 s more per change in watch mode); a pinned valley haze does not.

*Since M1.13b ([`vista-log/M1.13b.md`](vista-log/M1.13b.md)):* every valley has its own pool. The world's **pool map** (cached
with it) gives each place the valley floor its air drains to (the first cell downstream draining 1 km², or a closed hollow, filled
flat to its rim), how well that valley holds the night's pool (by the floor's gradient over the next kilometre, half at 6%: steep
valleys drain it, flat floors and basins keep it) and the deepest pool its walls allow; the far field's base terrain beyond the world
has a coarser map of its own. The chain above sets the night's pool over the scene's valley; each valley holds its share of it, its
cooling and its heat deficit scaled with its depth, so the sun breaks a shallow pool sooner. In a pool the haze is the valley's own
held and grown by the humidity at each height (the inversion warms upward, the dew point falls), so it is densest at the humid
floor and thins toward a soft top. The **mist** slider (advanced): where a pool's air is colder than its dew point, from the floor
up to where they meet, droplets form, more water the colder below its dew point (Kunkel's extinction, at most about 200 m to see:
fog proper is M4's). Its dew point comes from each place's **humidity normals** (monthly mean vapour pressure, from its station or a
near one, NASA POWER where none publishes it; carried between altitudes by Hann's formula); the humidity slider and the haze keep
the day's low as their dew point. The auto mist forms where a climate's dawns saturate (Fiordland, the Highlands, Iceland, Hokkaido's
autumn, Yellowstone's basins) and not at dry inner-alpine Zermatt. A pinned mist fills 60% of the scene's valley's pool at the pinned
visibility, keeping the night's whole pool at any hour, as a pinned valley haze does; `off` (0) none.

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
  estimator measures at the final's own resolution and detail (§7.2).

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
- **Uplift and rock** (M1.11d, [`vista-log/M1.11d.md`](vista-log/M1.11d.md)): the incision lifts the ground by an **uplift map**
  — the base terrain's lowest octave (6 km), raised to the power 1.5, up to 8 m a step — so in the steady state of uplift and
  incision the rivers stand high where the map is high: the massifs rise and the foothills stay low, where uniform uplift had
  left a field of similar peaks. The rock has **strata**: seeded beds of hard and soft rock (60 m thick on average), dipping up to
  8° and slowly folded, with a slight lateral mottle. Hardness sets the erodibility (the hardest rock 0.3 times the softest's) and
  the talus slope (35° to 60° in the incision, 40° to 65° in the GPU erosion, the tangent following the hardness), so hard beds
  stand as cliffs and ledges and soft ones weather back, instead of uniform 45° facets. The field is a pure function of the
  seed and a position in the rock's frame (the uplift carries the beds up with the ground); every eroded world stores the hardness
  at its bedrock surface and the frame, so the materials' strata (M1.12) are the beds that shaped its cliffs.
- **Folded strata** (M1.12c, [`vista-log/M1.12c.md`](vista-log/M1.12c.md)): M1.11d's beds lay nearly level, so every hill wore
  them as benches at the same heights, a topographic map's contour lines. Now the beds keep a dip and a strike of their own over
  each region about 10 km across (40° at each region's centre, turning between regions) and fold into long anticlines and
  synclines (600 m high, 5 km apart, their axes wandering); they come in **formations** about 300 m thick, each its own kind of
  rock (thin- or thick-bedded, leaning hard or soft, banded or uniform, its own tint); and over a seeded 30% of the ground they
  fade into **massive rock**, one rock whose hardness changes only slowly with height and varies from place to place, without beds,
  bands or laminae. The incision, the GPU erosion and the materials read the same field. Measured by the share of the beds'
  outcrop lines on rock that run within 10° of the contour (the angle on the slope): M1.12b's nearly level beds 15.1–16.6% on four
  seeds, the folded ones 11.7–12.6% (lines in random directions would give 11.1%). The worlds are steeper for it (the 8 m slopes'
  most crowded ±2° window 12.4–13.8% against 8.6–10.8%).
- **Glacial carving** (M1.13, [`vista-log/M1.13.md`](vista-log/M1.13.md)): after the incision, on the finished world and in its own
  metres, three glacial cycles of 20 steps (the equilibrium line swinging between 900 and 1500 m above the world's height 0, up to
  900 m below today's glaciers' line) each solve the steady ice of their climate (`GlacierFlow`: the balance flux routed over the
  ice's surface to every lower neighbour, the thickness from the shallow-ice approximation, the surface smoothed over a few ice
  thicknesses) and lower its bed by its sliding speed (averaged over the glacier's width, a plug), where the glacier is thick enough
  to be warm at its bed, a wall's cut over its slope's cosine, less on hard rock, and never deeper than 80 m below the spill of the
  hollow it digs (the erosion's pools then fill it with sediment: flat floors). The rivers cut where the ice is not, and every slope
  keeps its talus. The valleys glaciers run in round from V's into U's (the troughs' width at half their bottom 100 m
  over their width at 100 m, a V 0.5, a parabola 0.71: medians 0.80, 0.62, 0.69 and 0.84 on the four seeds against 0.44–0.48
  uncarved), the relief grows and the summits nearly keep their height; the ice's water runs under it, along the ground's
  drainage, which holds (95–97% of the grid into a 1 km² channel).
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
  placed by slope, altitude, aspect, erosion flow and moisture; the strata from the world's own beds (M1.11d).
  **Built in M1.12** ([`vista-log/M1.12.md`](vista-log/M1.12.md)): rock (hard and soft by the bed the surface cuts), scree, soil
  and grass, and how wet the ground is, blended per hit by rules in `Vista.Core` (`MaterialRules`) that the shader evaluates
  operation for operation: bare rock steeper than loose material lies, earlier on hard beds; scree where the erosion's loose material
  lies at the talus angle or in a stream's channel; meadow below a grass line from the place's warmest month (lower on poleward
  faces), stony soil above it and on slopes too steep for grass; wet ground in channels and where the erosion's water stood. The
  world carries its maps and beds to the GPU (`MaterialField`, 8 bytes a sample); near the camera the mesh adds detail by material
  (rock crests, scree stones, smooth ground, to 2 m), and below that the shading carries roughness (a triplanar bump, the laminae's
  steps on hard faces, scree's stones). Rough diffuse reflection (Oren–Nayar in Fujii's form) by default.

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

*Built in M1.13 ([`vista-log/M1.13.md`](vista-log/M1.13.md)):* **today's glaciers** are the recipe's: the steady ice of its own
equilibrium line (1800 m above the world's height 0 for the mountain set pieces) solved on the eroded world's 64 m average and laid
on the 8 m ground as a smooth surface without closed hollows (`Glaciers`), part of the world and cached with it, its water
running under it along the ground's drainage; firn above that line, bare ice below it
(grey, debris-covered where it thins toward its edges and snout, cracked into crevasse fields where it flows over ground steeper than
8–14°), whatever the place, the season and the snow slider. **The season's snow** lies by the material rules (`MaterialRules`, the
shader's twin): above the snow slider's snowline, 250 m lower on a fully poleward face, 150 m lower in a full hollow (the ground 6 m
below its 72 m surroundings) and in the westerlies' lee, higher on ridges and windward faces, ragged by two noises; shed from slopes
steeper than 35–45° (8° steeper in gullies), scoured from ridges; wet below the freezing level, dry above it. Forest keeps its ground
and shows the snow only as a dusting in the gaps between its crowns. **The spectra** come from ice's absorption (Picard et al. 2016
to 600 nm, Warren and Brandt 2008 beyond) and grain size, by Kokhanovsky and Zege's asymptotic radiative transfer, with dust:
dry snow (0.2 mm grains), wet snow (1.5 mm), firn (2.5 mm) and glacier ice (25 mm, dusty), Lambertian or nearly.

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
- **Forest as a material (built in M1.12b):** before any tree exists, forest is a material of the ground's rules
  (`MaterialRules`): below the place's tree line (its warmest month at 10 °C along the lapse rate, Körner and Paulsen 2004;
  Zermatt 2276 m, 100 m lower on a poleward face, ragged by the breakup noise), on slopes under 38–48° (past where grass thins
  out), where no water stands, and away from the clearings a 250 m noise opens, a few more of them on dry spurs and fewer in moist
  hollows (the drainage's upstream area). Its spectrum is a conifer canopy's as a whole (a few percent in the visible, a small green peak, tier 5), shaded by MODIS's
  kernel-driven BRDF (RossThick volume scattering and LiSparse-Reciprocal crowns casting shadows on the gaps, normalised to the
  spectrum's white-sky albedo) or, with `--canopy rough`, as very rough diffuse ground. Its crowns are a detail kind of the mesh
  (domes 8 m apart and 2 m high, bounded only where forest grows), so a forested ridge bumps the skyline; below them the shading
  adds the branches' texture and a 40 m tone of stands. M5's trees replace it near the camera and keep it beyond.

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
  ids). *Since M1.14:* the candidates' buffers (depth, normal, the ground's class, its height above the valley floor) are traced
  on the CPU over the height field, so a few hundred cameras need no mesh or acceleration structure each and the GPU stays free;
  the best ten are rendered small for what only light shows (the squint test, the values by layer) and their own buffers rescored.
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
- *Since M1.13a:* a **valley haze** layer in the haze's profile: more of the same haze under an inversion's lid, uniform below
  it and falling off over a logistic of 30 m above it (90% to 10% in 132 m), set by the weather chain (§3.6). It depends on
  altitude alone, so the LUTs, the aerial march and the reference take it in unchanged in kind; because its lid is sharper than
  any march's steps, every march takes the layer's exact mean over each step (its integral through the altitudes the step
  spans), never a sample, which would band the picture where the lid crosses a step's middle. A dense layer holds light through
  more scatterings than the clear air did, and its lid stood at the multiple-scattering tables' first height texel, so the
  tables grew with it: ten orders instead of five, 24 heights instead of 16 and 12 view-to-sun texels instead of 8 (their build
  1.0 → 3.7 s, their memory 2.25×), which took a strong layer's light from 10.5% dark against the reference to 1.1%.
- *Since M1.13b:* the **valleys' pools in three dimensions**: each valley's pool on its own floor, its haze and its mist a
  density the aerial march takes along every segment's part below the pools' highest reach, in steps of 150 m through their haze
  and shorter near a mist (an optical depth of 0.5 of it, at most 50 m), longer far from the camera, on a bounce and toward the sky,
  jittered so their edges turn into noise, striding over the air above every pool near it (a ceiling map of 1 km blocks, 4 km over
  the far field); a 3D noise moves the tops into wisps and a 2D one opens patches in the mist, at its edges and in a weak mist
  only: a strong deck's top over a valley lies flat (the Sunnegga webcam). A **sunlight volume**, built once a
  render over each level of the pool map in layers that follow its floors up to the pools' highest reach, holds the terrain's
  shadow and the pools' own depth toward the sun (and the mist above each point): the low air and the mist in a ridge's shadow are
  lit by the sky alone, the tables' light scattered many times there halved. The mist scatters the sun in
  octaves (Wrenninge et al. 2013), the light scattered many times weighted by the sun's elevation (a deck catches the sun per unit
  of its top; fitted to a Monte Carlo of a slab), and the sky's light from the tables; the tables carry the pools' haze as a flat
  layer, their altitude average, which the march leaves out where it takes the pools. The reference tracks the
  pools as part of the medium; with the terrain's shadow it shades every event in their reach.

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
- **Vista's camera** (M1.17, the user's picks of 2026-10-28 beside the user's own photos; `Vista.Core/Output/CameraSettings.cs`): the
  settings a photographer would set, applied to the PNG unless told otherwise, and printed after every render as `camera: …`. A white
  balance of 5500 K, a camera's daylight setting (a Planckian light of that temperature adapted to the display's white by Bradford's
  transform; `--white-balance`); the incident meter plus 1.5 EV (`--compensation`); an exposure that follows dusk's fall in light half
  way in stops below the card's light with the sun about 2° up (`--adaptation`), so twilight darkens as it does to the eye and not as
  it does to a phone in its low-light mode (none) or to a camera set at golden hour (all); AgX's punchy look kept 0.03 of each colour's
  luminance short of the gamut's edge (`--sky-margin`). The EXR is the light itself, unbalanced and unexposed. A camera the user wields
  (ISO, shutter time, aperture, white balance) is parked for later (§10).
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
  the per-channel ACES fit one option away (`--display aces`). Since M1.11c ([`vista-log/M1.11c.md`](vista-log/M1.11c.md)) the
  punchy look's saturation is held back per pixel where it would take a channel below zero, and the 8-bit PNG gets a triangular
  dither keyed by the frame pixel (`--dither off` for none); `--png16` writes 16 bits a channel instead.
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

All tables are resampled once (into the cache) onto one wavelength grid, so the
integrator only ever sees one grid. The visible range drives the image; measured data
outside it is dropped at import. Since M1.12a the grid is Vista's own (`WavelengthTable`): 50 bins of
equal x̄ + ȳ + z̄ mass, each wavelength the whole nanometre at its bin's median (413–661 nm), weighted by
the bin's own integrals of x̄, ȳ and z̄, so colour integrates true to within 1 ΔE₇₆ of dense integration.
The engine's grid (419–651 nm, weighted by the inverse of x̄ + ȳ + z̄ at each point) stays the maze's, and
Vista's with `--quadrature engine`, for `main`'s images before M1.12a. *Since M1.13* the default is the **blend**
(`--quadrature blend`; the equal bins stay as `--quadrature bins`): 50 bins of equal mass under white light with 30% of the
twilight sun at −1.8° (by colour-matching mass), each weighted by its bin's own integrals of the functions times that light over the
light at its wavelength (importance sampling, exact for the blend), so it reaches 701 nm and integrates alpenglow's light within
1 ΔE₇₆ where the equal bins missed by up to 6.7; a flat spectrum's luminance stays exact and its colour 0.05 ΔE₇₆ off.

---

## 7. Performance, memory & estimation

### 7.1 VRAM budget (RTX 3070, 8 GB)

**Measured in M1.15** (`--estimate`'s plan against each render's measured peak, seven views at 3840×2160 with the view-dependent
mesh at 1 px, 2.6–21.6 M triangles; [`vista-log/M1.15.md`](vista-log/M1.15.md)); the forest and cloud lines are still guesses:

| Item | At 4K |
|---|---|
| The process before its first render: the device, its pipelines, the air's fixed tables, the driver's build memory | 188 MiB |
| Image buffers: the colour sums 127 MiB; albedo and normal (denoising) 127 MiB each; variance and first hit (debug) 63 MiB each | 127–570 MiB |
| Terrain, view-dependent mesh (positions, normals, indices, map normals) | 66–537 MiB |
| The world's normal maps and material map | 321 MiB |
| Acceleration structures (2.7× the mesh) | 176–1,444 MiB |
| Their build's scratch, only while they are built | 53–437 MiB |
| The air: the valleys' pools 40 MiB; the scattering orders' scratch 248 MiB, only while built | 40–288 MiB |
| **Peak** (the build beside the structures, or the orders' scratch on small meshes; planned = measured within 1 MiB) | **1.0–2.9 GiB** of a 7.1 GiB budget |
| Vegetation (M5): ~50 unique plant meshes + millions of instances | ~1–1.5 GB (guess) |
| Cloud / fog density grids (M4) | < 0.1 GB (guess) |
| Textures | ~0 (procedural, evaluated in the shader) |
| OS, desktop, other programs | outside the budget DXGI gives the process |

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

Built in M1.15 (`--estimate`, [`vista-log/M1.15.md`](vista-log/M1.15.md)). Like the maze's `PerformanceCalibrator`, but per
scene and per view, because cost varies too much between views (open sky at 0.3 ns a sample, near terrain under a low sun at 19):

- **A 4K final at 64 spp is mostly setup**: the world, the mesh, the shaders and the air's tables take 20–40 s of a 25–62 s run
  on the 3070 shared at half its time, the trace 0.4–23 s, the files 3 s. So the estimate runs every stage the final repeats, as
  the final will, and times it: it is a final run without most of its trace.
- **The trace is projected from the final's own first passes**: a probe at the final's resolution and detail (detail scales with
  the output, §4.1) traces whole-frame passes exactly as the final will, dispatch for dispatch, past a second of GPU time until
  the passes reach the size the final's later passes keep, then stops at a pass's end. The trace is the probe's passes plus **the
  remaining pixel-samples × the last pass's measured GPU ns per pixel-sample × the passes' median wall seconds per GPU second** (a shared
  GPU's pauses, each submission's overhead); rays per sample and rays per second are recorded beside it. *Not random crops* as first
  planned: a sample's cost depends on how many samples a pass carries (the snow view's 5.2 ns at one sample a pixel, 6.7 at four, the
  same rays), and crops cheap enough to save anything are too small to fill the GPU, while one pass over a whole 4K frame costs
  4–170 ms of GPU. Crops return with M11's tiles.
- **The preview is no guide to the cost** (its dispatches cannot fill the GPU: the final ran at 0.1–0.7× its cost per sample); it
  gives the noise.
- **A range, not a single number**: the setup ±15%, the trace ±20% (or twice the probe's spread), the files ±25%, the denoising
  ±30% (from a crop), summed end to end.
- **Fixed spp** gives a predictable time; a **noise target** (`--noise`, in steps of the 8-bit PNG at the 95th percentile of the
  pixels) the samples it needs from the preview's measured variance through the exposure and the display curve, and their time.
- **VRAM**: planned from the sizes of the buffers the render will make, by the stage each lives through, before any is allocated,
  against DXGI's budget: refused over it, warned about over 80% of it ("never crash at 80%"). The plan meets the measured peak to
  the MiB (§7.1).
- **The ETA refines live** during the final: after every dispatch, from every row's last measured cost and the samples it still
  needs; a line every 5 s, and the ETA at 10–90% of the trace beside what was in fact left in the sidecar.
- **In the window (M1.16) the estimate runs on the kept stages**: its 4K mesh, acceleration structure and the air's tables stay for the
  final that follows, so it counts only what that final still does (the trace, the files, about 0.1 s of setup). On the hero scene it
  took 18 s and said 16–24 s; the final ran 21.1 s ([`vista-log/M1.16.md`](vista-log/M1.16.md)).

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
  `Sky.DomeRadiance`, and `RgbToReflectanceBasis` for built-in-tier materials. Since M1.12a Vista
  integrates colour on its own wavelength table (§6.4) and upsamples colours with its own port of
  the basis (`ReflectanceBasis`, the engine's arithmetic for any table); `WavelengthLookup` and
  `SpectralResources` remain for `--quadrature engine` only, and the CIE data is still the engine's file.

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
  `--phase6-regress` (§9.2). Every sheet the push adds or replaces meets the size rule below: no cell
  over 640 px and no file over about 1 MB (`Get-ChildItem vista-views/sheets/<slice>-*.png | Where-Object
  Length -gt 1MB` lists nothing); a sheet over it is re-tiled with smaller cells or fewer of them, or
  cropped to a loupe, before the push, never after. The CI run on `main` confirms the build and tests
  afterwards; a red run there is fixed before anything else lands.
- **A slice that changes the look** commits a before/after contact sheet to `vista-views/sheets/`
  (with its row in that folder's README) and adds a row to the cost ledger
  ([`vista-log/cost-ledger.md`](vista-log/cost-ledger.md)).
- **Sheets stay small:** PNG (lossless, so noise and banding can be compared), cells of at most
  640 px, about 1 MB a sheet at most. Where full-resolution detail matters, commit a crop of that
  region (a loupe), not the whole 4K frame. No Git LFS. A slice's render script writes the sheets it
  commits at that size, so a rerun reproduces them. (Since 2026-10-21; the M1.1–M1.10 sheets, 70 MB
  in all, stay as committed. M1.11b first pushed two over it and replaced them after the review; the
  originals stay in the history.)
- **It ends with its log**, `vista-log/<slice>.md`, in the layout of the existing logs (status,
  checks, decisions, gotchas, hand-off, commands, files, next; [`vista-log/README.md`](vista-log/README.md)).
  Its entry here gets one status line linking to it. A decision that changes the design or a later
  milestone is also written where it applies (§1–§9, that milestone's entry, §11), not only in the log.
- **A slice that changes the look tells the user what to judge** (since 2026-10-22): its status line
  and its log end with three short items, written for someone who knows landscapes, not rendering.
  *Judge this:* what the slice is meant to change, and where to look (which sheet, which part of the
  frame). *Ignore this:* what is still a placeholder, each with the slice that replaces it. *Questions:*
  two or three questions the sheets answer without any rendering knowledge ("do the snowcaps read as
  real, or as icing?"), naming a real photo to compare against where one helps. Real photos are for
  judging only: the user's own may be committed beside a sheet (downscaled to the size rule, metadata
  stripped); anyone else's stay local. **Questions are about what the user sees, never about
  geography, geology or physics** (since 2026-10-25; the user lives in the Netherlands, and "should
  glaciers follow the place?" says nothing in words): ask "which of these looks more like real
  mountains?" or "does this look like the photo?", with the options in a sheet. A choice that needs
  knowledge of places or physics is made in the slice, from data, written into the log with its reason,
  and shown to the user as a picture for a yes or no on how it looks.
- **Findings go to the parked list** (since 2026-10-25): a new idea from a review that the current
  milestone's own checks do not need is written into the parked list (the end of this section) with
  the milestone it belongs to, not added as a slice. A milestone takes its parked items when its
  slices are planned.

*Truth references: the Sunnegga webcam* (since 2026-10-26). Real photos of a known place and time are
the truth references for judging physics (stock photos and the user's paintings are references for
taste and composition only). The best source found is Zermatt Bergbahnen's Sunnegga panoramic webcam
(a Roundshot, about 2290 m above Zermatt, its archive browsable by date and time back to 2020, the
Matterhorn to the south-west): unedited, at a known place, in every season. Clear mornings and
evenings are found from MeteoSwiss's open station data (sunshine minutes on Gornergrat in the hours
after sunrise and before sunset; Zermatt's humidity for mist under a clear sky). The frames are not
the user's, so they stay local: the user keeps them in the Downloads folder as
`DD-MM-YYYY-HHMM.webp` (Swiss local time), and a slice that needs one renders Zermatt at the same
date and time. The frames so far, with the slice that judges each:

| Frame | What it shows | Judges |
|---|---|---|
| `12-10-2026-0740` | First light on the Matterhorn's summit over a sea of valley fog, clear sky | M1.13b (the fog), M1.17 (alpenglow) |
| `25-10-2026-1700` | Overcast; fog filling the valley, wisps lapping the slopes; gold larches; fresh snow among dark trees | M1.13b (the fog's edges); M5 (larches) |
| `23-08-2026-0630`, `23-08-2026-0640` | Ten minutes either side of first light: the Belt of Venus over shadowed mountains, then only the highest summits orange | M1.17 (the alpenglow match) |
| `07-05-2026-0600` | Before sunrise in spring: snow down to the forest, blue-grey under the sky's light alone, the forest dark | M1.17 (the snowline and snow in skylight) |
| `23-08-2026-2010` | Low evening sun on the Matterhorn's upper face, the valley in flat blue shade; a banner cloud | M1.17 (golden hour at +2°); M4 (the banner cloud) |
| `12-10-2026-1820` | Flat light after the sun has gone behind the western ridge: grey rock, gold larches, soft haze | M1.17 (white balance and rock colour) |
| `12-10-2026-1810` | Straight into the sun: silhouettes, flare, the webcam's exposure | M1.17 (exposure toward the sun; the flare is the camera's) |
| `07-05-2026-2040` | Spring sunset: clouds lit pink and orange from below around the summit | M4 (lit clouds) |

*Truth references: the user's own photos* (since 2026-10-27). Photos the user takes at a known date and
time are truth references too. They show the view from the user's home and the repository is public,
so they are not committed, and the user keeps them under no naming scheme: **a slice that needs one asks
the user for it** when it reaches the comparison, naming it by date, time and what it shows (below).
The photos carry no location, so a slice that needs the sun's height also asks for the nearest town;
its log records the sun's height, never the place. The camera's settings are listed here in case a
pasted copy has lost them.

*Reading brightness from these photos* (measured 2026-10-28). The phone's JPEGs, read as sRGB, stretch
differences in light: in a Pro-mode bracket of one sky (the 28-10 series below), 4× the light measured
5.5× and 5× measured 7.4×, so a measured ratio is about the true one to the power 1.23 (true ≈
measured^0.8), at least in the mid-tones; near black it is shakier. The phone's automatic mode also
merges several frames (HDR) and lifts the shadows, so an automatic shot compares with a Pro-mode one only
roughly. The phone records the white balance: automatic or manual in the standard photo data, and in Pro
mode the manual value too, in Samsung's own data at the end of the file ("4400K").

*Dusk under a grey cloud layer, 27-10-2026* (a phone, through a window and a cat net, over flat land
facing the sunset; sunset between 17:13 and 17:25 depending on the town, so until it is asked the sun's
height is known within about ±0.8°; white balance automatic throughout, so the series compares
brightness, not colour). 17:24 and 17:31 were taken in Pro mode at 16:58's settings (the main lens,
1/180 s, ISO 125, f/1.8), so those two compare directly: the trees about 2× darker in those seven
minutes (near black, so shaky). 16:58 itself was the automatic mode, so the drop from it is only an
estimate: read naively, the trees 20× darker at 17:24 and 50× at 17:31 and the sky 10× at 17:24 (17:31
is zoomed out, so its sky does not compare); corrected for the stretch, about 11×, 24× and 6×, and
smaller still, by an unknown amount, for 16:58's lifted shadows. (*Corrected 2026-10-28:* the first
version gave the naive figures as measurements and took 16:58 for the same fixed exposure.) *The sun's
heights* (asked in M1.17, 2026-10-28; the place is not recorded; true heights, without refraction, as Vista's
`--sun-elevation` takes them): 16:58 +2.7°, 17:18 −0.1°, 17:24 −0.9°, 17:31 −1.9°, 17:43 −3.7°; and on
28-10 17:12 +0.6°, 17:21 −0.8°, 17:27:57 and 17:28:21 −1.7°, 17:46:48 −4.5°.

| Photo (CET) | Camera | What it shows | Judges |
|---|---|---|---|
| 16:58 | Automatic (HDR); its base frame on the main lens, 1/180 s, ISO 125, f/1.8, zoomed 1.8× | The sun 1–3° up but behind the cloud layer: the gold only in a thin clear strip on the horizon; the trees flat olive under the grey light, no shadows; the distance fading even over flat land | M4 (only what the sun reaches through the gap turns gold); M1.17 (the series' start) |
| 17:18 | Automatic (HDR); the wide lens, f/2.2, 1/50 s, ISO 100 | Around sunset: the strip deep orange and wider; the warmth starting on the cloud's underside nearest the gap | M4 (the underside lighting from the gap's side) |
| 17:24 | Pro mode at 16:58's settings | A few minutes later: the warm band higher up the underside, never overhead; small clouds in the gap lit along their edges and bottoms; the start of a sunbeam (a slanted shadow edge in the lit air) | M4 (lit undersides and their timing; the sunbeam); M1.17 |
| 17:31 | Pro mode at 16:58's settings, zoomed out | About ten minutes after sunset: the warmth gone from the cloud; a thin yellow strip with red-pink cloud edges; lower ragged clouds darker than the layer behind them; the land nearly black | M4; M1.17 |
| 17:43 | Automatic; the zoom lens, f/2.4, 1/33 s, ISO 1250 (low light, several frames merged) | A cat on the balcony rail: the phone made dusk look like the afternoon, at the cost of smeared detail and a pale sky | M1.17 (the exposure question) |

*Sunset under a clear sky with high streaks, 28-10-2026* (the same view; the main lens at 1.0× in Pro
mode throughout; sunset between 17:11 and 17:23 depending on the town). Three of the six share one
exposure, 1/12000 s, ISO 640, f/1.8, so their brightness compares directly. White balance automatic at
17:12 and 17:21, manual at 4400 K from 17:27:57 on (recorded by the phone), so 17:21 and 17:27:57 differ
in colour by the setting as well as the sky. 17:12 is through the closed window, whose dirty glass adds glare around the sun; the rest are
with it open. Measured on the blue sky and corrected for the stretch (rough): about 2× darker from 17:12
to 17:21 (the glass's glare makes it look more) and about 2× again to 17:27:57. At 17:46:48 the camera
took 83× the shared exposure's light and the upper sky measured about as bright as at 17:27:57, so it
had grown very roughly 80× darker in those 19 minutes (cloud had moved over much of it by then, and
unlit cloud is darker than clear sky).

| Photo (CET) | Camera | What it shows | Judges |
|---|---|---|---|
| 17:12 | The shared exposure; automatic white balance; through the glass | A few minutes before sunset, the sun in the frame setting into a low dark bank: the warm colour only around the sun; the high streaks and contrails white with yellow edges toward the sun; the sky blue almost to the horizon; at an exposure set by the sun, the land black | M1.17 (the exposure toward the sun; the clear sky's colour at sunset); M4 (high clouds still white) |
| 17:21 | The shared exposure; automatic white balance | Around or just after sunset: the high clouds peach, gold and orange; the contrail, the highest, still white; the low bank dark grey with a red-orange fringe along its bottom near the sunset (lit from the gap's side, as on 27-10); the sky in bands: deep blue overhead, a pale greenish-blue band, orange only at the horizon | M4 (high clouds lit after sunset, the highest the whitest); M1.17 (the greenish band) |
| 17:27:57 | The shared exposure; manual white balance | The contrail and the high cloud pink; the gold gone from the middle clouds; the low bank dark with a deep red fringe at the bottom; the sky blue-violet (partly the real violet above the glow a few minutes after sunset, partly the 4400 K setting) | M4 (the colours deepening, the low clouds losing the light first); M1.17 (the violet) |
| 17:28:21, 17:28:39 | Manual white balance; 1/3000 s, ISO 640 (4× the light); 1/12000 s, ISO 3200 (5×, grainy) | The same sky as 17:27:57, brighter: the darkest of the three reads as night, the brighter ones as early twilight | M1.17 (the exposure question: one sky's mood set by exposure alone); the stretch above |
| 17:46:48 | 1/90 s, ISO 400 (83× the shared exposure's light); manual white balance, 4400 K | The blue hour, the sun about 5° down: everything blue; a cloud layer over most of the sky, its thick parts darker than its thin parts and gaps (the reverse of the sunlit clouds earlier); the last colour a thin pink line on the horizon; the town's lights on | M1.17 (the blue hour's colour; the exposure question: the user raised the exposure 83× to keep the picture about as bright, which is what the meter does); M4 (clouds without sun darker than the sky) |

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

**M1 plan (agreed with the user 2026-10-19).** Twenty-seven slices in the order below, each under the
"every slice" rules at the top of this section. (M1.1 went through a PR; from M1.2 on, slices land
directly on `main`.)

*Added 2026-10-21, after a review of the M1.1–M1.10 sheets (agreed with the user):* **M1.10a —
Exposure & display** and **M1.11a — Drainage**, both before the materials (M1.12), so rock and snow
are tuned once, under the final light and on the final terrain, as the strategy below intends. M1.11
gains time-of-day presets by sun height, and M1.12 gains near-camera detail by material. The letter
suffixes keep the code's references to M1.11–M1.17 valid.

*Added 2026-10-21, after a review of the M1.10a–M1.11a sheets (agreed with the user):* **M1.11b — The
display look** (AgX's punchy look and a default compensation, by sheet, because incident + plain AgX
reads dark and flat) and **M1.11d — Uplift & rock hardness** (an uplift map so ranges keep their
massifs, a rock-hardness field against uniform 45° facets, and the two measurement cameras the
incision left 500 m up re-placed), both still before the materials. M1.12's strata read M1.11d's
hardness field; M1.13 gains per-place precipitation and glacial U-valleys; M3 takes M1.11a's flat
floors as lake sites; M4 gains a seasonal aerosol load. M1.11b's look and M1.10a's meter are
re-judged at the sign-off (M1.17), once real materials exist.

*Added 2026-10-21, after a review of the M1.11b sheets (agreed with the user):* **M1.11c — Output
finish** (the punchy look's saturation held back where it would clip a channel, a deterministic
dither for the 8-bit PNG, and an optional 16-bit PNG), display only, so the terrain work is judged
under the finished output. Uplift & rock hardness became **M1.11d**; nothing in the code referred to
it. The punchy look's dark shadows (down the slope the frame's mean is 23 of 255) are judged at
M1.17, with real materials and M1.11d's re-placed camera.

*Added 2026-10-22, after M1.11c (agreed with the user):* the deep clear sky's flat cobalt, its red
channel at 0, is the punchy look's own colour for that sky (held back just enough, it sits on
Rec.709's edge), not the clamp's. Whether it keeps some red (a margin or a knee short of the edge)
is a change of look, so it is one line of M1.17's re-judgement, not a slice: only the darkest part
of a clear sky is on the edge, and M1.11d and M1.12 tune the terrain and the materials, not the sky.

*Added 2026-10-22, after a review of the M1.12 sheets against the user's own photos of the Alps
(agreed with the user):* **M1.12b — Forest as material**, before the snow. From altitude a forest
is dark, rough texture rather than trees, and it stays dark among snow-covered open ground, so the
snow (M1.13) must be tuned with the forest in place. It is M5's distant-forest representation,
built early; M5 adds real trees near the camera and keeps the material beyond them.

*Added 2026-10-22, from the user's paintings (agreed with the user):* **M1.13a — Valley haze layer**,
before the auto camera. In the user's own words, mist at a mountain's foot "hides the lines, so the
mountain doesn't really need to land anywhere, and adds distance by increasing the contrast to the
layers in front of it." It is also physics: haze is densest in the lowest air, and on cold, still
mornings an inversion traps it under a lid (the white band with a crisp top in the user's photos
over the Alps). A layer that depends on altitude alone fits the atmosphere's existing tables, so it
comes long before M4's volumes, and M1.14 judges compositions with it available.

*Added 2026-10-23, after a review of the M1.12a–M1.12b sheets (agreed with the user):* **M1.12c — The
world's footing and folded strata**, before the snow. The user's call on where the world sits (§11):
its lowest valleys meet the place's valley floor, so the tree line, grass line, snowline and freezing
level read against true altitudes. And the photo sheet (`m1-12b-photo.png`) shows M1.11d's strata as
contour lines: every hill ringed by level benches like a topographic map, the most artificial thing
beside the user's photo. Both change the world itself, so they land before the snow is tuned on it.

*Added 2026-10-25, after the M1.13a review (agreed with the user):* **M1.13b — Terrain-following valley mist**, before the auto
camera. M1.13a's layer depends on altitude alone, so it lies across every view at one height: "it reads like mist, but it feels too
much like a line and not enough like peaks hidden in the mist". Photorealism comes before likeness to the paintings, which guide
composition only. Each valley's pool on its own floor, mist where the pool's air saturates, and the terrain's shadow on it are
pulled forward from M4. The user's answers: mist as well as haze (droplets on humid dawns, from per-place humidity normals);
pools by the valleys' shape; terrain shadows on the mist in this slice; up to twice the cost on views with mist in them.

*What M1 has taken from later milestones (2026-10-25, the user's concern that M2 should not find everything folded into M1):*
M5's distant-forest representation (M1.12b's forest material); M4's valley haze and mist (M1.13a, M1.13b) and the terrain's shadow
in the low air (M1.13b's sunlight volume). Those milestones keep the rest of their work and fine-tune what came early. M1.13b does not
take fog proper, clouds, steam fog, the daytime haze lid or the seasonal aerosol load: they stay M4's.

*M1's scope is frozen (2026-10-25, the user's call):* M1.13b is the last slice added to M1, which grew from seventeen slices to
twenty-seven (seven corrections that sheets and measurements showed were needed, three pulled forward from later milestones).
What remains is M1.13b, M1.14, M1.15, M1.16 and M1.17, and nothing new enters M1 unless M1.17's own checks need it: the hero
render "alpine range at golden hour", alpenglow, bare steep faces, the estimate inside its range, the goldens. A fix those checks
need is made inside the slice that finds it, or in M1.17. Everything else a review finds goes to the parked list (the end of this
section) with the milestone it belongs to.

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
shadow; M1.11d re-places it), and punchy darkens it most. The sky-only view turns deep blue, its red channel at 0. `--look base
--compensation 0` writes `main`'s PNG byte for byte; the EXR is unchanged. Sheets: `m1-11b-before-after.png`,
`m1-11b-golden-looks.png`, `m1-11b-ridges-looks.png`, `m1-11b-down-slope-looks.png`, `m1-11b-alpenglow.png`.
Log: [`vista-log/M1.11b.md`](vista-log/M1.11b.md).

**M1.11c — Output finish.**
- *Goal:* a PNG that is finished output, with no clipped hues and no banding, so the terrain
  (M1.11d) and the materials (M1.12) are judged under it.
- *Why (the review of M1.11b, 2026-10-21):* the punchy look's saturation (1.4 about the luma)
  pushes the lowest channel of an already saturated colour below zero, and the curve's clamp cuts
  it off: the sky-only view comes out a flat cobalt with its red channel at 0
  (`m1-11b-before-after.png`), and across a large sky gradient a hard clip can show as a hue shift
  or a visible edge. And the 8-bit PNG is quantised with no dither at all: the path tracer's noise
  hides that in previews, but a denoised or high-sample 4K final has smooth sky gradients, where
  banding shows, and the sky is most of a wallpaper.
- *Deliverable:* **gamut-safe saturation:** per pixel, the punchy look's saturation is lowered from
  1.4 just enough that no channel of the PNG's linear Rec.709 goes below zero, never below 1 (so the
  base look is unchanged, and a colour the base itself clips is not the look's to fix); found in
  closed form at the look where that suffices, else by a few bisection steps through the rest of
  the curve. Colours that never clipped keep Filament's look exactly. **Dither:** before the 8-bit
  quantisation, a triangular (TPDF) dither of ±1 step in the sRGB-encoded value, from a hash of the
  frame pixel (not the loupe window's, so a loupe stays an exact crop of the frame) and the channel:
  zero-mean, the same bytes every run, on by default for the beauty and the denoised PNG;
  `--dither off` writes M1.11b's bytes. **16-bit PNG:** `--png16` writes the PNGs at 16 bits a
  channel instead (no dither needed), through the same exposure and curve. Recorded in the
  sidecar (schema 15: the dither and the bit depth).
- *Verify:* CI: the saturation never lets a channel go below zero and equals 1.4 wherever Filament's
  look did not clip (M1.11b's reference values for those inputs), greys unchanged, the hue kept (the
  Oklab hue angle of a saturated blue within a few degrees of the base look's, where the clipped
  look moved it); the dither deterministic, zero-mean over a flat field, keyed by the frame pixel (a
  loupe still a crop), off writing M1.11b's bytes; a 16-bit PNG read back by the decoder.
  3070: the sky-only and default views, the share of sky pixels with a channel at 0 before and
  after; banding on a 3840×2160 denoised sky (a loupe of it in the sheet): the longest run of equal
  8-bit values down a sky column, and the step edges visible in a contrast-stretched loupe, before
  and after; the measurement views with `--dither off` byte-identical to M1.11b. Cost ledger: no
  change (display only, on the CPU).

*Status (2026-10-22): done.* The punchy look's saturation is held back per pixel where it would clip (the closed form at the look
only bounds it, so a bisection finds it), the 8-bit PNG is dithered by default (`--dither off`), and `--png16` writes 16 bits. On a
3840×2160 sky at 1024 spp, denoised, the undithered PNG's levels run 260–530 rows down a column; dithered, at most 27. The sky view's
red channel stays at 0 (96.6% of its pixels): held back just enough, this sky's colour sits on Rec.709's edge, 2–4 levels greener and
2–3° toward cyan; whether it should keep some red (a margin short of the edge) is a change of look, re-judged at M1.17. Only the sky
view and the deepest shadows down the slope change; `--look base --dither off` writes `main`'s PNG byte for byte. Sheets:
`m1-11c-before-after.png`, `m1-11c-sky-loupe.png`. Log: [`vista-log/M1.11c.md`](vista-log/M1.11c.md).

**M1.11d — Uplift & rock hardness.**
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

*Status (2026-10-22): done.* The incision lifts the ground by an uplift map (the base terrain's 6 km octave to the power 1.5, up to
8 m a step), and the rock has seeded strata whose hardness sets the erodibility and the talus slope in the incision and the GPU
erosion. The aerial sheet shows massifs with lower ground between them (`m1-11d-seeds.png`); the facets are gone (the most crowded ±2°
of 8 m slopes holds 8.6–10.8%, M1.11a's 36–39%, `m1-11d-views.png`); the 1 km relief spreads 1.2–1.4 times more, the 5 km relief less
(back toward the base terrain's); the drainage holds (34–52 km lake-free channels, 96–97.5% drained). The world stores its hardness
and the rock's frame for M1.12 (`m1-11d-maps.png`). The default and down-slope views stand 3 m above rock again; the ledger's rows before
and after are not comparable. With M1.11a's settings this build makes `main`'s worlds bit for bit. Log: [`vista-log/M1.11d.md`](vista-log/M1.11d.md).

**M1.12 — Materials by rule.**
- *Goal:* rock that reads as rock: strata, scree, soil, alpine grass and wet ground where nature
  puts them (§4.3).
- *Deliverable:* materials chosen and blended in the shader per hit, from slope, altitude, aspect
  and the erosion maps (M1.11a's flow accumulation, deposition, wetness), all procedural: rock strata
  from M1.11d's hardness field (so the material bands line up with the cliffs and ledges it shaped),
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

*Status (2026-10-22): done.* The ground has rock (hard and soft by the bed it cuts, so its bands line up with M1.11d's ledges), scree,
soil and grass, and how wet it is, by rules C# and the shader share (`MaterialRules`; the GPU agrees within 0.0013 of a weight), on
built-in spectra (tier 5) the sidecar records (schema 17). Over four seeds rock covers 36–48% of the world, scree 6–10%, soil 11–20%
(mostly stony ground above the grass line, from the place's warmest month: Zermatt 2892 m), grass 31–38% (`m1-12-maps.png`,
`m1-12-ids.png`). Near the camera the mesh adds detail by material (rock's crests and scree's stones to 2 m, each kind's bound measured
as M1.9's, the bound taken per region) and below 2 m the shading carries the roughness (`m1-12-loupes.png`); a 4K mesh near the ground
is 1.5–2.0 times `--materials off`'s (the default view 15.5 M triangles). Rough diffuse surfaces (Oren–Nayar) by default: on the ground
+6.6% with the sun behind the camera, 3% darker looking toward it (`m1-12-diffuse.png`). `--materials off` renders `main`'s image to float
rounding. Found on the way: the hero wavelengths' colour cast (§11, M1.12a) and the display's missing white balance (M1.17). A sample costs
1.40–1.55 times M1.11d's on the ground views, nearly all of it the materials' evaluation at each hit (the cost ledger).
Log: [`vista-log/M1.12.md`](vista-log/M1.12.md).

**M1.12a — Colour quadrature.**
- *Goal:* colour that integrates true, so snow and ice (M1.13), water (M3) and the M1.17 look are
  judged on their spectra and not on the hero wavelengths' cast.
- *Why (found in M1.12, 2026-10-22; §11):* the engine's 50 hero wavelengths sit where the
  colour-matching functions' cumulative mass (x̄ + ȳ + z̄) passes i/51, each weighted by the inverse
  of that sum at its own wavelength (the engine's fix for a magenta cast). That weight is a point
  value standing in for the width of the wavelength's share of the mass, and x̄, ȳ and z̄ change
  differently across it, so every colour carries a cast against dense 1 nm CIE 1931 integration: a
  flat grey 2.6 ΔE₇₆ greenish, M1.12's ground spectra 1.7–2.9, ten metres of water 3.0, a Rayleigh
  sky 3.4, a sun at air mass 20 4.1; luminance within 1.7%. The span (419–651 nm) is not the cause:
  only 1.8–1.9% of the functions' mass lies beyond it at each end.
- *Deliverable:* **Vista's own wavelength table** in `Vista.Core`, the engine's `WavelengthLookup`
  untouched (the maze does not move, §9.2): 50 bins of equal x̄ + ȳ + z̄ mass, each wavelength at its
  bin's median (whole nanometres, 413–661 nm, still ascending, so the multiple-scattering tables'
  interpolation and the companion stride hold), and each entry's tristimulus weight its bin's own
  integrals of x̄, ȳ and z̄, so a flat spectrum integrates exactly to equal-energy white. Everything
  that reads the engine's cycle today reads Vista's: the integrator's CIE table and its correction,
  the materials' spectra and the RGB-to-reflectance basis, the atmosphere's tables and LUTs, the
  multiple-scattering positions, the incident meter and the albedo output's colours. The shader does
  not change: it already sums `Cie[i]·L(λᵢ)` under one correction. **`--quadrature bins|engine`**
  (default bins): `engine` is the engine's table and renders `main`'s image byte for byte. The
  setting joins the render key; the sidecar records it with the table's span (schema 18).
- *Verify:* CI: the table against dense 1 nm integration of a set of spectra (flat, M1.12's five
  ground spectra, water through 2 m and 10 m, the solar spectrum through air mass 1 to 20, a λ⁻⁴
  sky, and **the twilight light that makes alpenglow**: the sun at −1°, its path through the air
  beyond air mass 20 and its light mostly deep red, where the last wavelength (661 nm) stands for
  everything above it, on a flat 0.9 reflector standing in for snow until M1.13 re-runs it with its
  own spectrum): a flat spectrum exactly neutral, every one within 1 ΔE₇₆ and 1% in luminance (0.9 ΔE₇₆ at
  worst predicted, grass, half of it the red edge past the last wavelength; the twilight case's error
  recorded, and if it misses, the log names the fix, such as a narrower last bin) where the engine's table
  misses by 1.7–4.1; the wavelengths ascending, each inside its bin, every entry visited four times
  in a 50-sample cycle; `engine` reproducing the engine's table to the bit; the pins that move
  re-baselined deliberately, each named in the log. 3070: `--quadrature engine` byte-identical to `main` on the ledger views; a
  before/after sheet of the ledger and M1.12 views with a ΔE₇₆ map per view; each view's mean
  colour shift against the shift CI predicts for its light (the sky view's Rayleigh sky about 3.4
  ΔE₇₆, less green); the exposure the incident meter picks, before and after (the luminance moves
  under 2%); the sky view's share of red channels at 0 (M1.11c: 96.6%), before and after;
  `--gpu-selftest` with the atmosphere's checks at the new wavelengths; a cost-ledger row (expected
  unchanged: only the tables change).

*Status (2026-10-23): done.* Colour integrates true: Vista traces its own 50 wavelengths, the medians of 50 bins of equal
colour-matching mass (413–661 nm), each weighted by its bin's own integrals (`WavelengthTable`, `--quadrature bins|engine`, sidecar
schema 18). Every spectrum the CI checks (flat, M1.12's ground, water, the sun through 1 to 20 air masses, a Rayleigh sky) lands within
0.93 ΔE₇₆ and 0.44% in luminance of dense 1 nm integration, where the engine's table missed by 2.3–8.3 and up to 2.7%, each colour
judged at the luminance of white (stricter than M1.12's figures above; at a grey of half that luminance the two agree). The twilight
light on a 0.9 reflector misses by 1.08, the last wavelength standing for everything past 650 nm; a last bin of three quarters of a
bin's mass (665 nm) would take it to 0.92 and the sky stays 0.09 (measured, not taken: the user kept the bins equal on 2026-10-23,
and the red end is decided in M1.13, whose attention point holds the options measured since). In the renders each view moves as its light predicts and always away from green: the sky
4.16 ΔE₇₆ (a Rayleigh sky predicts 4.14), the daylit ground 3.25–3.32, down the slope at golden hour 3.96, M1.12's close-ups 2.06–2.60
(`m1-12a-ledger.png`, `m1-12a-closeups.png`); the incident meter's exposure moves 0.001 EV; the sky view's red channel at 0 falls from
95.3% of its pixels to 86.2%. `--quadrature engine` renders `main`'s images byte for byte, denoised and albedo too, and a sample costs
what it did (the cost ledger). *Judge this:* truer colour, not a new look: the sky a shade less teal, sunlit bare rock less
green-grey, grass nearly unchanged (the middle columns of the two sheets against the left; the maps show where). *Ignore this:* warm
greys (no white balance yet, M1.17); the sky's deep cobalt and the close-ups' saturated greens (the look, M1.17); grass as a carpet and
no forest (M5, M1.12b). *Questions:* does the middle column's sky read more like a clear alpine sky than the left one, or can you not
tell them apart? Does the bare rock read as plain grey stone or still faintly green (against a photo of limestone at midday)?
Log: [`vista-log/M1.12a.md`](vista-log/M1.12a.md).

**M1.12b — Forest as material.**
- *Goal:* forested lower slopes that read as forest from a distance, so the aerial and range views
  get their dark depth and colour before M5's trees, and the snow (M1.13) is tuned with the forest in
  place.
- *Why (the review of 2026-10-22):* from altitude a forest is texture, not trees. In the user's
  photos over the Alps (11 April 2025, about 10 km up) the lower slopes are dark blue-black and rough,
  and stay dark among snow-covered open ground, because snow falls through or off the canopy. Vista's
  ground below the grass line is grass or soil today.
- *Deliverable:* **where forest grows**, by rules `MaterialRules` shares between C# and the shader:
  below a tree line from the place's climate (its warmest month, as M1.12's grass line), on slopes
  gentle enough for trees, favouring moister ground (M1.11a's drainage), with seeded clearings and
  ragged edges; above the tree line M1.12's grass, soil and rock as before. A **canopy spectrum**
  (tier 5): conifer forest's low visible reflectance with a small green peak, recorded per material.
  **Canopy shading:** very rough, since a canopy is mostly gaps and shadow, measured against M1.12's
  rough diffuse in a sheet. **Crowns as near-camera detail**, a detail kind with its bound measured
  as M1.9's, so a forest edge on a ridge shows a bumpy silhouette; close up it is still not trees
  (M5, which keeps this material beyond the distance where its trees take over).
- *Verify:* CI: the placement rules (the tree line from the climate; steep ground bare; moist ground
  favoured; deterministic). 3070: material-id previews over four seeds with the forest's share; a
  before/after sheet of the ledger and M1.12 views; the aerial view beside one of the user's April
  photos, judged on how forest and open ground relate rather than on the phone's colours; a 4K loupe
  of a forest edge on a ridge; a cost-ledger row.

*Status (2026-10-23): done.* Forest grows below the place's tree line (its warmest month at 10 °C: Zermatt 2276 m) as a material of
the ground's rules, on slopes under 38–48°, away from standing water and from the clearings its own noise opens, taking only the
open ground; its spectrum is a conifer canopy's as a whole, shaded by MODIS's kernel-driven BRDF (`--canopy kernels|rough`: 3–5%
brighter than very rough diffuse on the forest), and its crowns are a detail kind of the mesh (domes 8 m apart, 2 m high, bounded only
where forest grows: the largest 4K view 38.2 M triangles). `--forest off` renders `main` byte for byte, meshes included. **Seed 42's
world sits high:** its lowest ground stands 391 m above Zermatt's valley floor, so only 3.2% of it lies below the tree line and the
committed views barely change (`m1-12b-views.png`); with the world's base at 1000 m a third of it is forest (`m1-12b-maps.png`,
`m1-12b-low.png`), and the over-forest view reads like the user's April photo (`m1-12b-photo.png`). Where the world should sit is the
user's call (§11). A sample costs 4–6% more on the ground (the cost ledger). *Judge this:* the forest where it shows, against the
photo (`m1-12b-photo.png`, bottom) and in the low world (`m1-12b-low.png`, right): dark rough lower slopes under pale open ground;
the forested crest's skyline in `m1-12b-loupe.png`. *Ignore this:* no snow (M1.13); rounded bumps, not trees, close up (M5); one
conifer forest in July (later); warm greys and saturated greens (M1.17); forested valley floors where real ones are farmland.
*Questions:* does the bottom cell's forest read like the dark lower slopes in the photo, or too dark, blue or patchy? Does the
forest's upper edge sit where you would expect, ragged rather than a contour line? Does the crest read as trees on a skyline or as a
dark carpet? And should the world's lowest valleys sit at the place's valley floor? Log: [`vista-log/M1.12b.md`](vista-log/M1.12b.md).

**M1.12c — The world's footing and folded strata.**
- *Goal:* a world that stands at the place's real altitudes, and rock layers that read as geology,
  not as contour lines, before the snow (M1.13) is tuned on it.
- *Why (the review of 2026-10-23):* **footing:** height 0 is the place's valley floor, but the eroded
  seed-42 world's lowest ground stands 391 m above it, so at Zermatt its valleys lie near 2000 m and
  only 3.2% of it falls below the tree line (§11). **Strata:** M1.11d's hard and soft bands follow
  altitude, so they make level benches around every hill at the same heights; beside the user's photo
  (`m1-12b-photo.png`, and the aerial and layered-ridges cells of `m1-12b-low.png`) they read as a
  topographic map's contour lines or rice terraces. Real alpine strata are tilted and folded, vary in
  thickness, break off, and are absent in crystalline massifs.
- *Deliverable:* **the world anchored to the place's valley floor:** the default base altitude puts
  a low percentile of the eroded ground (the 5th, so a single pit cannot move the world) at the
  place's valley floor (Zermatt 1608 m, which would put seed 42's peaks near 3600 m); the altitude
  slider's auto value becomes this anchor, a pinned altitude stays pinned, and the sidecar records
  the anchor and the percentile's height. **Folded strata:** the hardness field becomes a field in
  three dimensions, its bedding planes tilted (a dip and a strike per region), folded (a gentle
  long-wavelength fold, warped), with uneven bed thickness, and faded out over regions (a seeded mask
  of where the rock is layered and where it is massive). The incision, the GPU erosion and M1.12's
  rock bands read the new field, so cliffs, ledges and material bands still line up, now across the
  contours instead of along them. Its dip, fold and fade ranges are chosen by sheet.
- *Verify:* CI: the anchor (the 5th percentile at the valley floor for any seed; a pinned altitude
  untouched; deterministic); the strata field (a pure function of seed and position; dip and fold
  within their ranges; the mask's share). 3070, before and after on four seeds: **the contour-line
  measure**, the share of ledge length (lines of high curvature on rock) that runs within 10° of the
  local contour, which level benches drive toward one and folded strata pull down, its target recorded
  here; the forest's, grass's and rock's shares at the anchored altitude against M1.12b's
  1000 m-base figures (the Swiss Alps are about 30% forest); the photo sheet redone beside the user's
  April photo; M1.11d's drainage numbers and facet measure holding; the world bit-identical twice and
  through the cache, the self-test's driver check re-baselined deliberately; a cost-ledger row.
  *The measure's target (recorded in M1.12c):* lines of high curvature read below random whatever the
  beds do (they are the erosion's gullies, running down the slope), so the measure is the beds' own
  outcrop lines on rock, where the hardness at the surface changes fastest, the angle taken on the slope;
  level beds put all of them on contours, folded ones keep a little lean toward the contour (an outcrop's
  direction is the slope's plus the dip), so random (11.1%) is a floor: **within 1.5 points of random,
  at most 12.6%, on all four seeds**.

*Status (2026-10-24): done.* The auto altitude puts each world's lowest valleys (the 5th percentile of its ground) at the place's
valley floor, and the weather is read there: seed 42's height 0 sits at 912 m at Zermatt, and 28–31% of the four seeds' worlds is
forest (M1.12b's 2.4–3.9%; the Swiss Alps about 30%). The beds dip 40° at the centre of each region about 10 km across and turn
between regions, fold 600 m high every 5 km, come in formations about 300 m thick, and over 30% of the ground fade into massive rock:
11.7–12.6% of their outcrop lines on the rock run within 10° of the contour (M1.12b 15.1–16.6%; the target, within 1.5 points of
random, met on all four seeds), and seen from the user's photo's height the slopes no longer wear rings at one height
(`m1-12c-photo.png`; the variants in `m1-12c-choice.png`, the hardness from above in `m1-12c-maps.png`). The drainage holds; the facet
measure does not: 12.4–13.8% against 8.6–10.8%, the slopes steeper and more alike, though not at their talus angle (the log). With
the new strata off this build erodes `main`'s world bit for bit (seed 42). The default, sky, down-slope and materials views are re-placed on
the new world (`m1-12c-views.png`). The aerial view, the one ledger view that keeps its pose, costs 4% more a sample (the
cost ledger). *Judge this:* the bottom cell of the photo sheet against the photo and M1.12b's cell
above it: tilted, folded layers rather than contour lines; in the views (right column) forested valleys and the tilted beds as diagonal
bands of rock and meadow; the choice sheet for the dip and the share of massive rock. *Ignore this:* no snow (M1.13); warm greys and
saturated greens (M1.17); one forest of rounded crowns (M5); new poses for the near views; upright folds without faults (a height
field's beds). *Questions:* does the bottom cell read as tilted, folded layers, or still as contour lines or rice terraces? Do the
diagonal stripes in the aerial view read as tilted beds, or too regular, like corduroy? Should the beds dip more gently, as shipped
or more steeply, and do the calm patches of massive rock look like granite massifs or blank spots? Log:
[`vista-log/M1.12c.md`](vista-log/M1.12c.md).

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
  melting state by altitude against the freezing level. **Snow and the forest:** forested ground
  (M1.12b) stays dark under snow cover, the canopy shedding or hiding the snow, with at most a
  light dusting in its texture, as in the user's April photos. A built-in snow spectrum from ice absorption
  and grain size (tier 5). Russian roulette is re-measured on bright snow (M0 note). Close-up snow
  stays in M7.
- *Attention: the colour quadrature's red end* (from M1.12a; the user's ideas, 2026-10-23). M1.12a's 50 equal bins put one
  wavelength (661 nm) in for everything from 650 to 830 nm, and the light alpenglow is made of lives there: the sun 1° below
  the horizon from 4000 m misses dense integration by 1.08 ΔE₇₆, at −1.5° by 2.6, at −1.8° by 6.8, and grass lit by light already
  bounced off grass by 1.3 (`vista-log/M1.12a.md`; ΔE₇₆ at the luminance of white). Decide how to cover the red end here, with
  snow's spectrum and the alpenglow sheet in hand, not before. What a throwaway comparison on `QuadratureCheck`'s spectra
  measured (2026-10-23; to be redone on this slice's own check):
  - *More equal wavelengths:* 64 bins take −1.8° to 5.6, 100 to 3.8 (twice-bounced grass 1.13): a weak return, and costly, since the
    atmosphere's per-wavelength LUTs grow in proportion, a pixel needs that many samples to complete a cycle, and the shader's
    50 and its companion stride of 12 change.
  - *The same 50, more of them in the red tail* (46 bins of equal mass to 650 nm and 4 sharing the tail, the last at 682 nm): −1° 0.36,
    −1.5° 0.81, −1.8° 2.3, twice-bounced grass 0.86, the sun at air mass 20 0.31; a flat spectrum still exact, a Rayleigh sky
    0.35 (from 0.08), no cost per sample. A three-quarter last bin, the patch M1.12a named, only gets −1° to 0.92 and −1.8° to 6.1.
  - *A table tuned to the scene's light:* exact for that light, but one table serves every path in a frame, and a table tuned to
    deep twilight keeps no wavelength under 530 nm: the blue sky beside the alpenglow then misses by 180 ΔE₇₆ (tuned to the sun at
    air mass 20, by 41). Unusable as one table per render. Per path it is spectral importance sampling (each path draws its hero
    wavelength by the light it sees and weighs it by that probability): unbiased, but noisier and a change to the integrator.
  - *One fixed table tuned to a blend* (half white light, half the −1.5° twilight sun, by colour-matching mass): every case within
    0.62, −1.8° included; a flat spectrum 0.13 off (no longer exact), a Rayleigh sky 0.39.
  The red-tail split and the blend are the candidates; choose between them on spectra that were not used to tune them (this
  slice's snow, the alpenglow sheet's light), so the table is not fitted to its own test.
- *Verify:* CI: the placement rules; the snowline default moving with each place's precipitation. 3070:
  a glacier valley's cross-section measured U-shaped (its floor's width at half depth against the
  incised V's); **bare steep faces**, because on the test views the snow
  fraction on faces steeper than 40° (read from the material-id output) stays under a threshold
  recorded here while ledges and gullies hold snow. **Alpenglow**, because a dawn sheet from −4° to
  +4° shows the peaks pink and red while the valley is in shadow, with no code that knows about
  alpenglow. A season sheet moves the snowline from the defaults alone. CI: M1.12a's twilight check
  re-run with the snow spectrum in place of its flat stand-in, within 1 ΔE₇₆ of dense integration, and
  the check grown by the deeper twilight (the sun at −1.5° and −1.8°) and twice-bounced grass; the
  red end's table (the attention point above) chosen and its before/after on the alpenglow sheet.
  The brown "stony ground above the grass line" (M1.12's soil), which dominates some views and reads
  as desert (seed 42 in `m1-12-ids.png`), is judged again once snow covers the high ground, before
  any retuning of the soil rule.

*Status (2026-10-25): done.* Glaciers carve the valleys they run in: after the incision, three glacial cycles of steady ice (balance
flux, shallow-ice thickness; the line 900–1500 m above height 0, up to 900 m below today's) lower the bed by the ice's sliding speed
where it is thick enough to slide, and the troughs round from V's into U's: the width at half a trough's bottom 100 m over its width
at 100 m (a V 0.5, a parabola 0.71) has medians 0.80, 0.62, 0.69 and 0.84 on the four seeds against 0.44–0.48 uncarved
(`m1-13-valleys.png`). The relief grows, the summits nearly keep their height, the drainage holds (95.2–96.7%, the ice's water
running under it) and the footing drops 45–89 m. Today's glaciers are the recipe's (their line 1800 m above height 0), 2.4–10.8% of
the worlds under ice: firn above the line, grey ice below it, crevassed where it steepens (`m1-13-maps.png`). The season's snow lies
by rule (aspect, hollows, wind, shedding from steep faces, scoured ridges, wet below the freezing level; forest dark with a dusting):
in April the range views' faces steeper than 50° are 12–17% white where flats are 78–86% (`m1-13-slopes.png`; the threshold recorded:
at most 35% white over 40°, 20% over 50°). The snowline follows each place's monthly precipitation (Fiordland's January line 1733 →
1098 m, Banff's April line 966 → 1316 m; Zermatt's year in `m1-13-seasons.png`). Snow and ice spectra come from ice's absorption and
grain size. The red end is decided: the blend, now the default, holds every spectrum of the check within 0.90 ΔE₇₆ (the bins missed
−1.8° by 6.7); in the renders the two agree within the noise: the summits in view are lit at −0.5° and dark by −1°. Alpenglow
shows unprogrammed: summits orange to red at 0° and −0.5° over valleys in shadow (`m1-13-alpenglow.png`). The brown stony ground is
1.9–2.3% at Zermatt in July, a thin fringe: not retuned. Russian roulette on bright snow: a start at bounce 1 would be 1.29× as
efficient (M1.5 measured 0.49× at dawn); the start stays at 2. The aerial view costs 10% more a sample (the cost ledger). *Judge this:*
the views (`m1-13-views.png`, right): the glaciated range in July, summer snow on ledges and in gullies, the troughs; the maps; the
seasons; the photo sheet against your April photo, at Zermatt's line and at 2400 m; the alpenglow sweep. *Ignore this:* no depth,
cornices or sparkle to the snow, no trees (M7, M5); warm greys and saturated greens (M1.17); the ranges' busy feet (M1.13a); new
poses for the near views; the same glaciers in every season and place; the red end, which no frame here shows. *Questions:* do the
valleys read as glacial troughs and the snow as lying where it would? Is Zermatt's mid-April world as white as you would expect?
Should today's glaciers follow the place (lower at Lofoten and Fiordland)? Should every world's carving glaciers be sized to its own
height, or is seed 7's shallower carving the variety you want? Log: [`vista-log/M1.13.md`](vista-log/M1.13.md).

**M1.13a — Valley haze layer.**
- *Goal:* the painter's mist at the foot of the mountains, from physics: a range's base fading into a
  pale layer that hides where it meets the valley and separates it from the layers in front.
- *Why (the user, 2026-10-22):* the foot of a distant range, where it meets the valley floor, is the
  busiest and weakest part of a render, the middle-ground counterpart of M1.14's near-field penalty;
  a haze layer hides it honestly, because real haze is densest in the lowest air and an inversion
  traps it under a fairly sharp lid. And the pale band reads as distance: it raises the contrast
  between the layers, which M1.14's layer and squint scores reward.
- *Deliverable:* an **inversion haze layer** in the atmosphere: denser aerosol below an inversion
  height, with a soft but distinct top, on top of M1.11's haze. It depends on altitude alone, so it
  goes through the existing LUTs (transmittance, multiple scattering, sky view) and the aerial
  perspective march unchanged in kind, with the brute-force reference mode checking it. **The weather
  chain sets it**: strongest on cold, clear, still mornings (dawn, autumn and winter, a valley well
  below the peaks, high humidity), its height from the valley floor and the temperature, fading as
  the day warms; a slider (`valleyHaze`, auto or pinned, advanced) with the sidecar recording it.
  Terrain-following, patchy mist that fills some valleys and not others stays M4's.
- *Verify:* CI: the profile (denser below the lid, the lid's sharpness, continuity), the chain (a cold
  autumn dawn gives a layer, a warm afternoon none). 3070: the LUT air against the reference mode with
  the layer (as M1.4's segment check); a dawn sheet in which the range's foot fades into the layer
  while the peaks stay crisp; the layered-ridges and aerial views beside the user's photo over the
  Alps (the haze band's crisp top), judged on how the band sits rather than on the phone's colours;
  a valley-haze sheet (off, auto, strong); a cost-ledger row (expected near unchanged: the profile
  changes, not the number of lookups).

*Status (2026-10-25): done.* A valley haze in the air: the haze under an inversion's lid, uniform below it and gone within 132 m
above it, each march taking its exact mean over every step (a sample per step would band a slope by up to 3%); without it the air is
M1.13's bit for bit. The weather chain sets it from the night's cold pool: its depth from the night's cooling and the inversion's
rise (446 m at Zermatt on an October dawn, under 200 m at Lofoten, up to half the world's relief), broken up by the sun's heat since
sunrise (about four hours in spring, past noon over a snowy floor), forming again after the day's high, its haze the valley's own
held and grown under the lid (25 km to see under it at that dawn); `valleyHaze` pinned stands at any hour. From shoulders just above
the pool (two new views) the ranges' feet fade into the layer while the peaks stay crisp (`m1-13a-morning.png`,
`m1-13a-paintings.png`); the committed views stand above it. A dense layer exposed the scattering orders: a strong layer's view was
10.5% dark against the reference, so the tables grew to ten orders, 24 heights and 12 view-to-sun texels (1.1% dark; the LUT build
1.0 → 4.0 s), and the segment check is closer everywhere, still 6–8% inside a strong layer toward a low sun. The photo's band is the
lowlands' daytime haze, which the chain does not make and the far field's mountains break up (`m1-13a-photo.png`). Terrain views
cost 3–4% more a sample (the larger tables), a strong layer nothing more (the cost ledger).
*Judge this:* the mist at the mountains' foot beside your paintings (`m1-13a-paintings.png`); the October morning from the defaults
(`m1-13a-morning.png`); off, auto and strong (`m1-13a-strengths.png`); winter and the places; the band from 10 km against your photo.
*Ignore this:* one lid for every valley and the haze in a ridge's shadow still lit (M4); no daytime lid or lowlands at the horizon
(M4); the colours (M1.17); the high views barely changing. *Questions:* does the mist read like your paintings' and is its top too
crisp? Should the auto layer lean stronger than physics, or leave that to a pinned one? Should a daytime haze lid come before M4?
Keep the larger tables (3 s more per atmosphere build), or the old speed? Log: [`vista-log/M1.13a.md`](vista-log/M1.13a.md).

**M1.13b — Terrain-following valley mist.**
- *Goal:* mist and haze that lie in the valleys, each pool on its own floor, thinning upward and lapping the slopes, patchy, lit and
  shadowed by the terrain: peaks rising out of mist, not standing above a line.
- *Why (the user's review of M1.13a, 2026-10-25):* the flat lid "reads like mist, but it feels too much like a line and not enough
  like peaks hidden in the mist"; photorealism comes before likeness to the paintings. An altitude-only layer lies at one height in
  every valley and across every view; real dawn pools sit on their own floors, fill some valleys and not others, thin upward,
  saturate into mist where the air is humid, and lie dim in a ridge's shadow. The review's other answers: the auto strength stays
  physical (a painted look is a pinned one), the daytime haze lid stays M4's, M1.13a's larger tables stay unless this slice's checks
  say otherwise.
- *Deliverable:*
  - **Pools by the valleys' shape:** a pool map per world, cached with it: where cold air is trapped (flat floors, basins, narrow
    outlets, from the drainage and the relief) a pool fills to its own surface above its own floor; out of steep, open valleys it
    drains. M1.13a's chain sets the night's depth and the sun's break-up; each valley's shape scales them, so some fill and others
    stay clear.
  - **A density, not a lid:** densest at the pool's floor and thinning upward to a soft top, continuous across valley sides, with a
    3D noise for patches and wisps (its strength and scale chosen by a sheet). M1.13a's flat layer becomes the altitude average the
    LUTs carry.
  - **Mist where the pool saturates:** per-place monthly humidity normals (dew point or relative humidity, from each place's station
    with a source per place, like the temperatures) give the dawn's dew-point depression; where the pool's air cools to its dew
    point, droplets form: a mist spectrum (water droplets: flat in wavelength, strongly forward-scattering, nearly white), visibility
    down to a few hundred metres, the haze above and around it. Fog proper (under about 200 m, ground fog that buries the camera,
    a sea of clouds) stays M4's.
  - **Through the march, not the tables:** the pools are a 3D density (the map as a texture plus the noise) that the aerial march
    and the reference sample; the march takes more steps inside the mist, so its edges and patches neither band nor blur; its
    multiple scattering comes from a stand-in (the tables' altitude average, scaled by the local density), checked against the
    reference.
  - **The terrain's shadow:** a sunlight volume over the low air, built once per render (the terrain's shadow and the mist's own
    transmittance toward the sun), which the march reads below its top: mist in a ridge's shadow is dim and blue-grey, its sunlit
    tops glow. It also lifts M1.4's lit-air limit for the low air.
  - **Sliders:** `valleyHaze` keeps its meaning (the visibility in the densest pool, auto or pinned), and the mist joins it (auto
    from the humidity, or pinned); the sidecar records the pools and the mist.
  - **Cost:** up to twice the time a sample on views with mist in them; views without mist near unchanged.
- *Verify:* CI: the pool map (a closed basin fills, a steep open valley does not, deterministic, cached with the world), the density
  (graded, continuous, the noise bounded), the mist chain (a humid autumn dawn saturates, a dry summer one stays haze; every place's
  humidity normals sourced), the sunlight volume on a synthetic ridge. 3070: the march with mist against the reference (as M1.13a's
  image check), with and without the shadow; the self-test's parity for the pool map, the density and the volume; a dawn sheet in
  which peaks rise out of mist lying in the valleys, judged against real photos (the user's own; others stay local) rather than the
  paintings; a shadow sheet (mist in a ridge's shadow, against the shadowed reference); a morning from formation to break-up; a
  sheet of the noise's strength; a cost-ledger row and the cost of a misty view (at most 2×). **Against the webcam**
  (`12-10-2026-0740` and `25-10-2026-1700`, the truth references above): under a strong inversion the fog's top is
  flat and sharp and its body opaque, a white surface seen from above, with frayed, wispy edges where it laps the
  slopes and ridges standing clear; so the patches and the thinning upward belong at the edges and in weaker pools,
  not on a strong pool's top. A Zermatt render at the frame's date and time beside it.

*Status (2026-10-27): done.* Each valley holds its own pool: a pool map per world (where its air drains, how well it holds a pool,
how deep its walls allow), reaching into the far field, so some valleys fill and others stay clear (`m1-13b-maps.png`); its haze
thins upward from the floor, and where the air cools to the dew point (per-place humidity normals, sourced) mist forms, uniform
under a sharp top that lies flat over a strong deck and frays at its edges and in weak mist (the webcam). The march takes the pools
in three dimensions through a sunlight volume over the low air; the mist's light scattered many times is weighted by the sun's
elevation (fitted to a Monte Carlo of a slab). Peaks rise out of a mist lake at Fiordland's autumn dawn and it breaks up through
the morning (`m1-13b-dawn.png`, `m1-13b-morning.png`); Zermatt at the webcam's 07:40, the fog pinned, shows a flat, opaque fog sea
under clear peaks (`m1-13b-webcam.png`). Against the reference: within 2–3% without the terrain's shadow, a sunlit deck about 10%;
with the shadow +19% over the shadowed reference, the shaded valleys' sky still the open sky's (parked). Views without pools run as
before; with pools more than 2× (the golden hour's haze pools 1.84× `main`, a strong misty pool 3.1× the flat layer; parked).
The GPU runs at half its time by default (`VISTA_GPU_SHARE`). Log: [`vista-log/M1.13b.md`](vista-log/M1.13b.md).
*Judge this:* Zermatt at the webcam's times beside your two frames (`m1-13b-webcam.png`, the frames kept local); peaks out of mist
(`m1-13b-dawn.png`); the morning's mist lake (`m1-13b-morning.png`); which valleys fill at each place (`m1-13b-places.png`,
`m1-13b-maps.png`). *Ignore this:* the twilight's brightness and the slopes' colour (M1.17); the 17:00 frame's overcast and fog
thicker than 200 m to see (M4); a ridge's shadow still a little bright, and the cost over 2× (parked). *Questions:* does the fog
sea's top read like the webcam's, flat and sharp, and are its edges frayed enough? Should Zermatt's dawns make mist on their own
more often, or is pinning it when wanted enough? Is the extra time on views with pools acceptable until a later speed pass?

**M1.14 — Auto camera v1.**
- *Goal:* good compositions for the mountain set pieces without placing a camera by hand (§4.2,
  §4.8).
- *Deliverable:* shot-first planting for snow-capped mountains and alpine range: a viewpoint and
  heading toward the main range, chosen with the sun's azimuth for side or back light. A few hundred
  candidates around it are scored from low-resolution G-buffer renders (depth, normal, material id)
  on: horizon near a third, foreground / midground / background layering, sun angle, sky fraction,
  the peaks in frame, nothing blocking the lens. Camera collision. `--auto-camera` in the CLI; the
  scores and the winner go in the sidecar. **A near-field penalty until M5:** the materials' close-up
  look does not hold up yet (`m1-12-loupes.png`: scree as large flat shards with a marbled texture,
  the meadow in regular wavy bands that look like a single-frequency pattern and are worth a check of
  their own, the rock face blotchy), and grass geometry is M5's. A candidate whose ground within
  about 100 m (from the depth buffer) covers more than a recorded share of the frame is penalised,
  unless that ground is snow (M1.13); the share is relaxed as M5 and M7 make the near field hold.
  **Composition after the user's paintings** (added 2026-10-22): the user's own oil landscapes in
  Bob Ross's manner (2018–2022) are the composition references; they are the user's work, so they
  may be committed downscaled when the user provides them. The score favours what they do
  consistently: clear depth layers (a light background range, a darker middle ground, a dark
  foreground); a framing mass at one edge, never centred (a ridge or rock in shadow until M5's trees,
  and beyond the near-field limit above); the main peak off-centre near a third, with water or low
  ground in the lower third where there is any; a diagonal leading line (a shore, a ridge, a valley).
  **Focal length is part of the search** (about 24–135 mm): a long lens stacks the layers and makes
  the peak loom behind them, where 28 mm leaves it a small bump on a wide landscape. **The squint
  test as a score:** the frame's value structure at thumbnail size (a blurred, downscaled luminance
  image falling into a few distinct groups rather than muddy mid-tones). *Added 2026-10-25 (M1.13a):* the valley haze shows from
  a viewpoint just above its lid looking across a valley at a range (`vista-views/haze/`), where the committed views, 3–4 km up,
  see little of it; candidates on such shoulders are worth having among the searched ones when a pool stands.
- *Verify:* CI: scoring tests on synthetic G-buffers, the near-field penalty, the layers, the
  framing, the leading line and the squint score among them. 3070: an auto-camera sheet over eight
  seeds × both set pieces against the default camera, every pick's near ground under the recorded
  share and its focal length recorded; the picks beside the user's paintings in a sheet, judged on
  composition rather than on colour or style; the same pick twice; the scoring time recorded.
  **Triangle headroom:** each pick's 4K mesh triangle count recorded with its focal length (M1.12b's
  largest 4K view is 38.2 M against the 48 M one acceleration structure takes); a candidate whose
  final mesh would pass a recorded margin under 48 M is penalised, so a long lens on a busy view
  cannot reach the ceiling unnoticed.

*Status (2026-10-27): done.* `--auto-camera` frames the mountain set pieces: the shot planted on the world (the main massif and
peak, the range's axis, a heading by the sun), a few hundred candidates around it (rings, the line to the subject, shoulders over a
standing pool; at eye height, 40 m and 150 m; the focal lengths that suit the subject there, 24–135 mm; the subject on a third),
each traced on the CPU over the height field (depth, normal, the ground's class, its height above the valley floor) and scored after
the paintings and §4.8 (the skyline and the sky, the peak or the range's summits, the layers, a framing mass, low ground, a leading
line, side or back light, the subject in the sun), the best ten rendered small for the squint test and the values by layer, and
their 4K mesh counted against the ceiling. Limits rank first: the near field until M5 (2% of the frame, snow excepted), the lens, 15%
under 48 M triangles (it turned down a 44.8 M finalist). On eight seeds × both set pieces at golden hour every pick beats the default
camera (`m1-14-seeds-peak-a.png`, `-b`, `m1-14-seeds-range-a.png`, `-b`), its near field 0%, its mesh 14–39 M triangles, in about
40 s (most of it the finalists' renders). The candidates' buffers on the CPU agree with the renders' (the self-test's new check).
After the first sheet, whose back-lit picks left their peaks dark, the subject must catch the sun. Most picks stand 40–150 m
above the ground: the near-field limit keeps the ground near a person's feet out of the frame until M5.
*Judge this:* the default camera against the pick, seed by seed, for snow-capped mountains and for an alpine range (the four seed
sheets); the picks beside your paintings, on composition only (`m1-14-paintings.png`); how one run ranked its finalists
(`m1-14-finalists.png`). *Ignore this:* the shadowed slopes' darkness (the punchy look's shadows, M1.17); the nearest faces' lack of
detail (parked: your question of 2026-10-27); no trees or water in front (M5, M3); seed 4's smooth snow dome in its near corner and
the terrain's odd lit facets on a few faces; golden hour everywhere. *Questions:* most picks are taken from 40–150 m up, like a drone:
do they look right, or should the camera stand on the ground like a person? Which picks would you keep as a wallpaper, and which look
wrong? Beside your paintings, is anything of their composition missing? Log: [`vista-log/M1.14.md`](vista-log/M1.14.md).

*M1.14 follow-up, before M1.15 (the user's call, 2026-10-27): a detail budget.* The nearest faces lack detail (the parked item
of that name), and lit faces show it most: raking sun reveals the relief a face lacks, while sky light hides it (the paintings'
dark foreground under a lit distance is the same rule). Until that detail is built, the auto camera keeps such frames out of its
picks. For each candidate pixel on rock or scree in the sun, it compares the ground one pixel covers at the final resolution
(distance × the pixel's angle) with the finest detail the terrain carries there (the 8 m grid with its normal maps beyond the
near-camera detail). A frame whose share of lit, over-magnified rock passes a recorded limit ranks down. It measures metres per
pixel, not distance: a far face at 135 mm is as magnified as a near one at 24 mm, and a frame that passes at 4K can fail at 8K, so
M11's larger targets tighten it on their own. The limit is chosen from a sheet of finalists by the user's eye. *Verify:* CI on
synthetic buffers (a near lit face ranks down; the same face in shade, or under a shorter lens, does not; 8K stricter than 4K).
3070: the finalists sheet again, with the big lit slopes in the corners of finalists 6, 7, 9 and 10 and seed 4's snow dome ranked
down; each pick's share recorded; M1.14's log gains the follow-up.

*Status (2026-10-27): done.* A pixel of lit rock, scree or snow on the world grid counts when the 8 m cell spans more than 12 pixels of
the 4K frame there (about 3 km at 50 mm); a frame where such ground covers more than 5% breaks the new limit and ranks below every
frame that keeps it. On M1.14's seed-42 finalists it breaks exactly 6, 7, 9 and 10 (6.2–14.5%; the rest 0–1.7%,
`m1-14-budget.png`); of the sixteen seed picks it changes three, seed 4's snow dome among them (26.2% before), and every pick now stands
at 0–4.8% (`m1-14-budget-changed.png`). `--debug detail` shows what counts (magenta; lit meadow and forest yellow) and records the
shares at 2–16 pixels a cell; `--detail-budget off` runs M1.14's search as it was. Snow counts beside rock and scree (the snow dome);
the far field never does. *Judge this:* the magenta on M1.14's finalists, and the three picks before and after. *Questions:* is
magenta on exactly the faces that look too smooth to you, no more and no less? Are the three new picks better pictures than the ones
they replace? Log: [`vista-log/M1.14.md`](vista-log/M1.14.md), its last section.

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

*Status (2026-10-27): done.* `--estimate` runs every stage a final repeats (the world, the mesh, the shaders, the air's tables: 20–40 s
of a 4K final at 64 spp) and times it, traces the final's own first passes until they settle and projects the rest, times the files on
the probe's own, and plans the video memory from the sizes of the buffers the render will make, before any is allocated (a render over
DXGI's budget is refused, one over 80% of it warned about); it writes `<preview>.estimate.json`. On seven views at 4K and 64 spp (the
sky, the day's terrain, golden hour, dawn, snow, dense mist), a fresh round with the final design: every final's whole run inside its
range and 1–3% from its middle (27–64 s), every trace inside (−7% to +8% from the middle), every peak planned within 1 MiB (1.0–2.9
GiB). Not the plan's random crops: a sample costs up to 29% more in passes of several samples a pixel, so the probe traces the final's
own passes. The preview is no guide to the cost (the final ran at 0.10–0.72× it) but gives the noise, in steps of the 8-bit PNG (for a
`--noise` target the final needs a third to all of the preview's samples). The live ETA refines after every dispatch (a line every 5 s;
2–9% off at 10–50% of a trace). §7.1's table is measured. Log: [`vista-log/M1.15.md`](vista-log/M1.15.md).

**M1.16 — Preview & tweak window.**
- *Goal:* drive all of M1 by hand (§3.1).
- *Deliverable:* a WinForms window on the watch-mode session: a progressive preview that restarts
  and refines after each change; the set-piece picker (a new scene); seed and new seed; the basic
  sliders and the advanced panel (location dropdown, custom latitude, altitude, date, temperature
  with the °C/°F toggle, humidity), each showing auto/pinned with reset and the implausible hint;
  camera nudges in metres, pan / tilt / roll, focal length, focus, aperture and exposure, with
  collision; the auto-camera button; a debug-view picker; the estimate and "render final" (4K EXR +
  PNG + sidecar, live ETA). Recipe, camera and slider files save and load, so the window and the CLI
  share them. *From M1.15:* the window's estimate runs M1.15's probe on the session's kept stages
  (the final's mesh, the acceleration structure and the air's tables built once, for the estimate and
  the final alike), so it counts only what the final will still build; the preview's own cost per
  sample is no guide to the final's (the final ran at 0.1–0.7× it).
- *Verify:* 3070: a converged window preview is bit-identical to a CLI preview of the files the
  window saves; the M1 checks walked through in the window, noted in its log.

*Status (2026-10-28): done.* `Vista.App --window` drives M1 by hand on one session that keeps its stages: the progressive preview (the
final's own passes read back, 640 px by default), the set pieces (a new scene, its camera the auto camera's) and the seed, every slider
with its value, auto or pinned, a reset and an implausible mark, the camera's nudges, turns, lens and exposure with collision, the auto
camera, the debug views and the denoiser, the estimate and the 4K final with a live ETA. The scene is the CLI's own files and each run
the CLI's own command line. `--window-check` walks the real window through M1 (off the screen): three times its converged preview was a
cold CLI preview of the saved files, PNG and EXR byte for byte; every step rebuilt only its stages (a nudge or a pan 1.6 s with its
mesh, an exposure 0.14 s, a debug view 0.7 s, a new scene's auto camera 15–40 s); on the kept stages the estimate took 18 s and said
16–24 s, and the 4K final ran 21.1 s. No image moved. Found: a change of the air (the time of day, the date, the weather) takes about
9.5 s, 8.7 s of it the air's tables (parked for M1.17's performance pass). *Judge this:* the window itself (`--window`), and `m1-16-window.png`,
`m1-16-controls.png`. *Ignore this:* the air's 9.5 s per change (M1.17); the images' look (M1.17). *Questions:* is anything you reach for
missing, or in the wrong place? Is a step of 1.5 m and 5° the right "a little"? Is the 640 px preview sharp enough, or should the window
default to 960 px (about twice the time)? Log: [`vista-log/M1.16.md`](vista-log/M1.16.md).

**M1.17 — M1 sign-off.**
- *Goal:* close M1 against its verification.
- *Deliverable:* the Vista golden suite at preview size (fixed seeds, committed PNG/EXR goldens,
  `--golden-check` with a tolerance, re-baselined deliberately) covering the M1 checks; the hero
  render "alpine range at golden hour" at 3840×2160 with its sidecar; the M1 status, decisions,
  gotchas and hand-off in its log, and M1's overall status in this section. The display look
  (M1.11b: AgX punchy, +1 EV on the incident meter) and M1.10a's meter re-judged under the real
  materials, the punchy look's shadows first (down the slope the frame's mean was 23 of 255 at
  M1.11b, in a valley M1.11d's camera no longer looks into); then the deep clear sky, which M1.11c's
  held-back saturation leaves on Rec.709's edge with its red channel at 0 (96.6% of the sky view):
  keep it, or stop the look's saturation short of the edge (a margin or a knee), judged against
  `m1-11b-before-after.png` and `m1-11c-before-after.png`. And a white balance (M1.12): the display has none, so a spectrally
  flat grey, which is what gneiss debris is, renders warm (equal-energy white comes out as Rec.709 1.20, 0.95, 0.91) and the
  materials' greys read beige under a high sun; keep it (the sun's colour, unbalanced) or balance for daylight, judged on
  `m1-12-views.png`. *Added 2026-10-25, after the M1.13 review (the user's call):* both the white balance and the look are also
  judged against the user's April photo (`m1-13-photo.png`). Its snow-free mountains read much darker than the renders', yet
  measure lighter (median L* 64 below the horizon, against 51 for M1.13's cell with the snowline at 2400 m): they are a cold,
  saturated blue-grey under thick haze beside bright snow, where the renders' rock is warm beige-grey (the built-in rocks reflect
  8–19%, so the rock is not too bright). Re-render that sheet's cells under each candidate, with the visibility slider lowered to the
  photo's haze, so the comparison is of the ground's colour and not the air's; the forest's share (the photo's ground lies lower,
  most of it forest) stays M5's. *Added 2026-10-26:* the Sunnegga webcam's frames (the truth references at the top of this
  section), each beside a Zermatt render at its date and time: the alpenglow's timing and colour (`23-08-2026-0630` and
  `-0640`, `12-10-2026-0740`); the snowline and snow under the sky's light alone (`07-05-2026-0600`); golden hour on the high
  ground only (`23-08-2026-2010`); the rock's colour and the white balance in flat light (`12-10-2026-1820`); the exposure
  toward the sun (`12-10-2026-1810`). The terrain is not the Matterhorn's, so the comparison is of light, colour and snow,
  not of shapes. *Added 2026-10-27:* the exposure at dusk, the user's call, from the user's dusk series (the truth references
  at the top of this section; the slice asks the user for the photos). At 16:58's settings the land went from the afternoon
  to nearly black in half an hour, while the phone's own low-light mode made the same dusk look like the afternoon (17:43).
  Vista's meter can do either, or stop between, nearer what the eye saw: a dusk sheet of one view at the series' sun heights
  (about +2° to −4°), each at a fixed exposure and at the meter's, shown to the user beside the photos for a pick. M1 renders no
  clouds and the series' sky is overcast, so the comparison is of the exposure and the land's darkness, not of the sky.
  *Added 2026-10-28:* the user's sunset series of 28-10-2026. Its bracket at 17:28 shows one sky reading as night or as early
  twilight by exposure alone (for the same pick), and its clear sky is M1's to match: the bands just after sunset (deep blue
  overhead, a pale greenish-blue band, orange only at the horizon; 17:21) and the violet above the glow a few minutes later
  (17:27:57, at a manual 4400 K, so that render is balanced to 4400 K too), then the blue hour (17:46:48, also at 4400 K).
  *Added 2026-10-28 (the user's call):* in two stops. Stop 1 builds the tools (the white balance, the look's margin and the exposure at
  dusk as display options that change nothing by default; `--golden-check`; the air's tables in fewer dispatches) and shows each open
  choice as a sheet for the user's pick; stop 2 bakes the picks in, takes the goldens, renders the hero and closes M1. The goldens
  come after the picks, which move every image.
- *Verify:* alpenglow at dawn and dusk with no special-casing; bare steep faces; the estimate inside
  its own range on the test views; goldens added and passing; `--phase6-selftest` 700/700 and
  `--phase6-regress` 19/19 (maze unmoved).

*Stop 1 status (2026-10-28): done, waiting for the user's picks.* The look's open choices are display options that change nothing by
default: a camera's white balance in Kelvin (`--white-balance`), a margin that keeps the punchy look's deep sky off the gamut's edge
(`--sky-margin`), and how far the meter's exposure follows the light into dusk (`--adaptation`, below the card's light with the sun 2°
up). Seven sheets show them on M1's hero (the auto camera's pick, `vista-views/signoff/hero.camera.json`), beside the user's photos and
at the webcam's dates. `--golden-check` renders `vista-views/goldens/suite.json` (ten previews, the M1 checks) against committed goldens,
by means and blocks with a tolerance at four times a re-rolled noise and by the PNG's channels; a provisional baseline came back ten of
ten identical, a white balance different. The air's tables in about 200 dispatches instead of 1400: 9.5 s a change of the air instead
of 11.0, no table changed. No image moved. Found: no purple light after sunset (parked). *Judge this:* `m1-17-dusk.png`,
`m1-17-balance.png`, `m1-17-photo.png`, `m1-17-sky.png`, `m1-17-shadows.png`, each a choice; `m1-17-sunset.png` and `m1-17-webcam.png`
beside the photos. *Ignore this:* the mountains' shapes against the Matterhorn's; clouds (M4). *Questions:* which dusk row looks like
what you saw? Grey rock grey at noon, or warm as today? The deep sky with the margin or without? The shaded slopes lighter? Log:
[`vista-log/M1.17.md`](vista-log/M1.17.md).

*Stop 2 status (2026-10-28): done.* The user's picks (half of dusk's fall, 5500 K, +1.5 EV, the 0.03 margin) are **Vista's camera**,
named as the camera's settings in one place (`CameraSettings`, §5.6) and printed after every render; `m1-17-before-after.png` shows
them against M1.16's look (golden hour's shaded slopes lighter, dusk a twilight, noon's rock grey). The **goldens** are committed
(`vista-views/goldens/`, ten, 3.1 MB) and pass ten of ten. The **hero**, "alpine range at golden hour" at 3840×2160, 256 spp, denoised
(`m1-17-hero.png`; sidecar `vista-views/signoff/hero-4k.json`): 30.1 GPU s of trace, 3.7 GB of video memory as planned. The estimate
lands inside its own ranges on all seven test views (`estimate/check.ps1`); the maze is unmoved (`--phase6-selftest` PASS,
`--phase6-regress` 19 of 19). The auto camera's pick on the hero scene is unchanged. Stop 1's "passes of several samples" closure was
wrong (the parked item is open again). *Judge this:* `m1-17-hero.png`, `m1-17-before-after.png`. *Ignore this:* the near faces'
smoothness (parked), clouds (M4), trees (M5). *Questions:* does the hero read as an alpine range at golden hour you would put on your
desktop? Does dusk now darken the way you saw it? Log: [`vista-log/M1.17.md`](vista-log/M1.17.md).

**M1 status (2026-10-28): done.** Twenty-seven slices built "alpine range at golden hour" at 3840×2160, and its verify holds: alpenglow
at dawn and dusk from the spectral air alone, with no special-casing (the goldens `alpenglow-dawn`, `alpenglow-dusk`; M1.13's sweep);
snowcaps with bare steep faces (the golden `april-slopes`; M1.13's measure: in April 75% or more of the flats white, at most 35% of the
faces over 40°); the estimate inside its own stated range on the test views (M1.17's run above); goldens added (`--golden-check`, ten of
ten). Its summary, carried-forward items and hand-off: [`vista-log/M1.17.md`](vista-log/M1.17.md), "M1 sign-off".

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
winter inversions trap the haze below the peaks. **Mist at the foot of the mountains comes
first** (added 2026-10-22): low valley mist and haze layers, the base of a range fading into
them, appear in nearly every one of the user's paintings, so when M4 is sliced they lead,
ahead of the cumulus (M1.13a's altitude-only inversion layer comes first; *since 2026-10-25* M1.13b builds the
terrain-following, patchy mist that fills some valleys and not others, and the terrain's shadow on the low air, so M4 fine-tunes
them and adds fog proper: ground fog that reaches the camera, a sea of clouds over the peaks' feet, the daytime haze lid); then cloud
shadows on the terrain, then cumulus fields with full multiple scattering checked against a reference, then the optical effects
(fogbows, glories). *Added 2026-10-27:* a **grey cloud layer with a clear gap** on the horizon, a very common sky that the list
above does not name: only what the sun reaches through the gap turns gold, the rest lies
in the layer's grey light; around sunset the layer's underside lights from the gap's side, the warmth climbing toward the
camera for some ten minutes and then gone; sunbeams through the gap; lower ragged clouds darker than the layer at dusk. Judged
against the user's dusk series (the truth references at the top of this section; the slice asks the user for the photos).
*Added 2026-10-28:* high clouds after sunset, from the user's sunset series of 28-10-2026: white before sunset, peach and gold
just after, pink some minutes later; the highest (a contrail) the last to colour, the low clouds the first to lose the light.
Verify: a cold autumn dawn over a lake produces steam fog from the defaults alone; a month
sheet at one place shows summer hazier than winter from the defaults alone; a range's foot
fades into valley mist from the defaults of a cool, still morning; a grey layer with a gap on the horizon, at the dusk
series' sun heights, shows the series' look: gold only in the gap before sunset, the underside warm from the gap's side
just after, and gone some ten minutes later; high streaks over a low bank, at the sunset series' sun heights, turn white,
then gold, then pink, while the low bank stays dark but for a red fringe.

**M5 — Vegetation.**
Goal: forests, and with them the north star. Deliverable: procedural tree species,
bark, grass, ferns, shrubs, leaf litter; ecosystem placement; mass TLAS instancing;
leaf translucency; PROSPECT-D leaves driven by the season slider; leaf-off in winter;
distant-forest representation (measured decision; M1.12b's forest material is its starting point,
kept beyond the distance where the trees take over). Verify: **"autumn lake, dawn, fog"**
renders at 4K within the 8 GB budget; backlit leaves glow; M1.12a's colour check gains PROSPECT's
leaf spectra, green and autumn (reflectance and transmittance), whose sharp red edge lies past the
last wavelength (661 nm), each within 1 ΔE₇₆ of dense integration or the miss recorded with its fix.

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
*Notes from Hamlet (`hamlet-plan.md` §8, agreed 2026-10-21), designed in from the start:*
- **Props come from a prop-instance list** — `(prop, state, position, orientation, scale)` —
  that the shot-first layout writes from the recipe's rules or that is read from a file. Props
  have **states** (new, weathered, burned, ruined, overgrown…), part of the scene's static state.
- **A prop is a type or a given mesh.** Vista's own grammars (cabins, docks, rowboats, paths,
  fences) build the types; a client such as Hamlet hands over **procedurally generated meshes**
  with Vista material ids (still procedural, so §2's purity holds), and Vista renders them like its
  own. Vista does not grow a medieval architecture catalogue: Hamlet owns its buildings and every
  state of them.
- **A "village" set piece** reserves a buildable stage near the camera (a flattish valley floor, the
  river leaving the frame), and the recipe plants the viewpoint looking across it (with M3's layout).
- **Light stays separable:** sun, sky and each emitter (or cluster) can be written as their own
  additive buffers, which the integrator already samples apart (M1.5); Hamlet's stage composites them.

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
support checks out. A **cylindrical (panoramic) projection** for very wide frames: a
perspective camera stretches the edges of a multi-monitor span, and of Hamlet's 32:1 strip
(3840×120: even a 5° vertical field of view is about 110° across, the edges stretched about
3×); the lens models, the loupe's frame-keyed pixels and the auto camera learn it, and the
sidecar records it so a client can turn depth back into positions. It is independent of the
tiling and can be pulled forward as its own slice if Hamlet's H4 comes first. **Needs the
mid-distance rock detail first** (the parked "nearest faces lack detail"): at 8K and 16K a pixel
covers half or a quarter of the ground it does at 4K, and M1.14's detail budget would reject most
frames otherwise. Verify: a 16K
render completes within the VRAM budget and resumes after an interruption; a span and a 32:1
strip show no edge stretch (a sphere at the edge renders round).

**Ongoing:** composition scoring, camera filters (sensor / film spectral curves, white
balance, lens CA), estimator accuracy.

**Later, after photorealism:** painterly filters, such as a wet-on-wet oil look after the
user's own Bob Ross paintings, applied to the finished photoreal render as a display-side
filter (pillar 2). Welcome feature creep (the user, 2026-10-22), but only once photorealism is
reached, or once the user is done pushing it; nothing before that bends the physics toward a
style.

**Parked** (since 2026-10-25): findings that no current milestone's checks need, each with where it
belongs; a milestone takes its items when its slices are planned, and the user may promote or drop
any of them.
- **Farmland on the valley floors** (M1.12b): forest covers valley floors where real alpine ones are
  fields and meadows. *Belongs:* M8 (fields beside the props), and Hamlet's fields.
- **One forest for every place and season** (M1.12b): a single conifer canopy in July colours.
  Around Zermatt much of it is larch, a conifer that turns gold in October and drops its needles
  (`25-10-2026-1700`, `12-10-2026-1820`). *Belongs:* M5 (deciduous and mixed forest, larch, the
  season's colours).
- **Banner clouds** (the webcam, `23-08-2026-2010`): the flag of cloud that forms in the lee of a
  sharp summit when the wind blows across it, the Matterhorn's trademark. *Belongs:* M4, once clouds exist.
- **Clouds lit from below at sunset** (the webcam, `07-05-2026-2040`; both stock photos): the classic
  wallpaper sky. *Belongs:* M4.
- **The meadow's regular banding** (M1.12's close-ups): wavy bands that look like a single-frequency
  pattern. *Belongs:* M5 (grass), checked when grass geometry lands.
- **Folds without faults** (M1.12c): the beds bend but never break, as a height field's beds must.
  *Belongs:* unscheduled; faults and overhangs need more than a height field.
- **Steeper, more alike slopes since the folded strata** (M1.12c): the facet measure rose from
  8.6–10.8% to 12.4–13.8%, though not at the talus angle. *Belongs:* watched at M1.17; acted on only
  if the hero render shows it.
- **Glaciers that follow the place** (M1.13): today's glaciers are the recipe's, the same at Zermatt,
  Lofoten and Fiordland, in every season. *Decided (2026-10-25):* yes; how low glaciers reach follows
  the climate (far lower where it is cold and wet than in the Alps), so today's glaciers come from
  the place's temperatures and precipitation, the same numbers as the snowline. *Belongs:* M7.
  *The user's check:* Lofoten's render beside a real photo of Lofoten.
- **Carving sized to each world's height** (M1.13): seed 7 came out more shallowly carved than seed
  42 by how its numbers fell, not by design. *Decided (2026-10-25):* every snow-capped world's carving
  glaciers are sized to its own height, so each gets real U-valleys, with a seeded variation within a
  range so worlds still differ. *Belongs:* M7. *The user's check:* seed 7 beside seed 42: do both read
  as alpine valleys?
- **Cornices on ridges** (M1.13): not in M7's list of close-up snow. *Belongs:* M7.
- **Straight lines across flat valley floors** (M1.13b): seen from a shoulder over a filled valley floor (the seed-42 world's
  southern valley at Fiordland's altitude, `pools/mist-lake`'s neighbour looking west), faint bright lines run straight toward the
  vanishing point, with or without the pools: in the terrain or its shading, not the air. *Belongs:* M1.14 (its candidate
  cameras look over such floors), or M1.17 if the hero render shows them. *M1.14:* none of its sixteen picks (eight seeds, both set
  pieces, 320 px) shows them, so they wait for M1.17's hero render.
- **A leaner pools integrator** (M1.13b): the pools' variant costs a view with pools about 9 ns a sample before it takes a step
  (the golden-hour down-slope view 1.84× `main`, the misty ridges with a strong pool 3.1× the flat layer, over the plan's 2×):
  the march's state in fewer registers (its four wavelengths' coefficients and multiple-scattering ends live through every step),
  one pool and volume lookup a surface instead of one for its sun and one for its sky. *Belongs:* M1.17's performance pass, or M4
  with the fog proper.
- **The sky's light in a ridge's shadow** (M1.13b): the sky view and a surface's sky light are a world's without terrain, so a
  shaded valley's floor and the horizon over shaded valleys stay as bright as in the open; with a low sun the shadowed reference
  is 19% darker on the misty ridges than the march (vista-log/M1.13b.md). A sky view per shaded column, or the sunlight volume's
  shadow folded into the sky light's lower half. *Belongs:* M4 (with the clouds' shadows), or M1.17 if the hero render shows it.
- **The mist deck's light per direction** (M1.13b): the octaves weighted by the sun's elevation and the tables' sky light leave a
  side-lit deck +10% at a 25° sun and −9% at 6° against the reference, its nearest part −26% and its far part +17%
  (vista-log/M1.13b.md). A table of the slab's Monte Carlo by sun, view and depth, or a two-stream field with its forward lobe.
  *Belongs:* M4, with fog proper.
- **The nearest faces lack detail** (the user, 2026-10-27, during M1.14): the mountain and hill faces nearest the camera (about
  100 m to 3 km) read as smooth, and "the brain assumes less detail = far away", so the whole picture loses depth. The world grid's
  8 m cell is about 37 pixels of a 4K frame at 50 mm a kilometre off (12 at 3 km); the detail below the grid (M1.12) is crests and
  stones of at most 2 m near the camera, and the shading follows the grid's normals, so the structure real faces show at 5–50 m (rock
  bands, ribs and gullies down the fall line, ledges, scree streaks, colour) is missing. M5 and M7 take the ground within about 100 m;
  nothing takes this band. Options: shading and colour detail at those scales (no triangles, most of the perceived detail, smooth
  silhouettes), geometric detail of 5–50 m by material and slope on the nearest faces (rougher ridgelines, at a cost in triangles
  against the 48 M ceiling), or a finer erosion tile around the shot. *Belongs (decided 2026-10-27):* a slice of its own, before
  M11, whose 8K and 16K halve the metres per pixel and need it; the iconic shot needs it too (a lit face like the Matterhorn's
  from Sunnegga, about 8 km off, filling much of the frame). Shading and colour detail first (no triangles), geometry only where a
  silhouette needs it, a finer erosion tile if those fall short; judged by the same metres-per-pixel measure, after which M1.14's
  detail budget relaxes. Until then that budget keeps such frames out of the auto camera's picks.
- **A sample costs more in dispatches of several samples a pixel** (M1.15): the snow view's sample costs 5.2 ns in passes of one
  sample a pixel and 6.7 in passes of four, the aerial view's 5.0 and 5.65, with the same rays (vista-log/M1.15.md); a cheap view's
  final is planned as passes of up to 64 samples a pixel. Likely the sample loop's registers lowering the GPU's occupancy; passes of
  one sample in row bands, or a loop the compiler keeps lean, may win back 10–25%. The estimate is unaffected (its probe traces the final's own
  dispatches). *Belongs:* M1.17's performance pass, beside the leaner pools integrator. *M1.17:* still open. The planner plans passes
  against its own 250 ms budget, and under the GPU cap the renderer splits a pass into dispatches of fewer rows but the same samples a
  pixel, so a final still traces passes of several: the hero's 4K final at 256 spp ran 131 passes, about two samples each, and a cheap
  view runs more. Not taken in M1.17 (its checks do not need it); *belongs now:* the next slice that works on the trace's cost.
- **A change of the air costs about 9.5 s in the window** (M1.16): the time of day, the date, the temperature, the humidity or the
  haze rebuilds the air's tables and scattering orders, 8.7 s of a 9.5 s step at half the GPU's time (vista-log/M1.16.md), where
  §3.4 puts the time of day with the camera as a change that only restarts the preview (0.1–1.7 s). Since M1.13a the auto valley haze
  follows the hour, so even the time of day moves the tables; M1.13b's are larger (10 orders). Levers: fewer or coarser orders for the
  layer's share, a table per hour reused while a slider is dragged, the orders' dispatches at full speed while the user waits on them.
  (M1.13a found a pinned valley haze kept the tables over a change of time; not measured since M1.13b's pools.) *Belongs:* M1.17's
  performance pass. *M1.17 stop 1:* the tables' dispatches sized to about 20 ms each (200 instead of 1400): 11.0 → 9.5 s of wall time
  a change, no table changed. The rest is the GPU's 3.9 s (the scattering orders' gather of 1024 directions a texel, nine times) doubled
  by the cap's pauses; the levers left are the cap off while the window waits (about 4–5 s), or a density integral factored by height
  and sun (fewer lookups, a small change to the sky, checked against the reference). The user's call (asked at M1.17's end).
- **The purple light** (M1.17's sunset sheet, `m1-17-sunset.png`, against the user's 28-10 photos): with the sun 1.7° down Vista's sky
  toward the sunset is a yellow-to-orange band under deep blue, where the photo at 17:27:57 shows violet and pink above the glow;
  balanced to the photo's own 4400 K, Vista's sky comes out bluer, not violet, so the violet is the sky's and not the setting's. The
  purple light is mostly sunlight reddened on its long path, scattered down by the aerosol of the stratosphere (some 20 km up) and
  mixed with the blue; Vista's aerosol lies in the lowest kilometres (a 1.2 km scale height) and has no stratospheric layer.
  *Belongs:* M4 (beside the seasonal aerosol load) or M6 (twilight and night).
- **A camera you wield** (the user, 2026-10-28, picking M1.17's look: "In the future, I'd like it if we can set the ISO, shutter time,
  white balance etcetera as if I'm wielding a real camera"): Vista's camera (§5.6) is a set of display settings with a meter; a real
  camera's controls instead, ISO, shutter time and aperture (the exposure triangle of §5.6, the engine's `plan.md` §4) with the meter as
  a guide, the white balance in Kelvin as now, and the window's controls laid out as a camera's. *Belongs:* later, beside "Ongoing:
  camera filters"; feature creep the user asked to keep out of M1.

---

## 11. Open questions & risks

**Realism.**
- **The colour quadrature (measured in M1.12; fixed in M1.12a).** The engine's 50 hero wavelengths, placed where the
  colour-matching functions' cumulative mass passes i/51 and each weighted by the inverse of the functions' sum at its own
  wavelength, leave a cast on everything: a flat grey integrates 2.6 ΔE₇₆ greenish of its true colour, the M1.12 spectra 1.7–2.9,
  ten metres of water 3.0, a sun at air mass 20 4.1, a Rayleigh sky 3.4 (dense 1 nm CIE 1931 integration as the reference; the span,
  419–651 nm, leaves only 1.8–1.9% of the functions' mass out at each end and is not the cause). 50 equal-mass bins with each
  wavelength at its bin's median, weighted by the bin's own integrals of x̄, ȳ and z̄, give 0.0–0.9 ΔE₇₆ on every one, for no cost
  per sample. The hero table is the maze's too (§9.2), so M1.12a gives Vista its own, gated, before snow, ice and water (M1.13, M3)
  and the M1.17 look; the maze keeps the cast (the engine's `plan.md` could take the same fix). *Done (M1.12a):* every checked
  spectrum within 0.93 ΔE₇₆ at the luminance of white but **light past 650 nm**: the twilight light on snow misses by 1.08, since one
  wavelength (661 nm) stands for the whole last bin, and deeper twilight misses by more (−1.8°: 6.8). How to cover the red end
  (more of the 50 in the tail, or a table tuned to a blend of white and twilight light) is decided in M1.13, its attention point.
  *Decided (M1.13):* the blend. Four of the 50 in the red tail (46 + 4, the last at 681 nm) leave alpenglow at −1.8° 2.8 ΔE₇₆ off and
  more in the tail does not help (6 + 44: 2.4); a table tuned to white light and 30% of the −1.8° twilight sun holds every case within
  0.90 ΔE₇₆, those it was not tuned to too (twilight at −1° and −1.5°, the snow and ice spectra, twice-bounced grass), for no cost
  per sample (`vista-log/M1.13.md` has the table).
- **Where the world sits against the place's climate (found in M1.12b; decided 2026-10-23: the
  world's lowest valleys, its 5th percentile, meet the place's valley floor, built in M1.12c).** Height 0 of the world is the place's valley floor
  (Zermatt 1608 m), but since M1.11d's uplift the eroded seed-42 world's lowest ground stands 391 m above it and its peaks 2387 m:
  at Zermatt the valleys lie near 2000 m, above most of the real valley's forest. So M1.12b's forest covers 3.2% of that world at
  the valley floor's altitude, and 9.5%, 20.4%, 34.5% and 42.9% with the base at 1400, 1200, 1000 and 800 m (the Swiss Alps are
  about 30% forest). The grass line, the snowline and the freezing level are read against the same altitudes. Whether the world's
  floor should meet the place's valley floor (anchoring its lowest valleys, not its height 0, to the base altitude) was the user's
  call: yes, at the ground's 5th percentile, built in M1.12c; M1.12b's sheets show both. Anchored, the four seeds' height 0 sits at
  895–1009 m at Zermatt and 28–31% of each world is forest; a coastal place whose valley floor lies below a world's footing keeps
  its height 0 at sea level, its valleys some hundreds of metres up (M1.12c).
- **Vegetation is the biggest risk.** Procedural trees that survive close inspection at
  4K+ take many iterations; procedural purity rules out the shortcut of scanned assets.
- **Composition.** Generators produce mediocre images; shot-first generation plus
  scoring (§4.8) is the bet. Expect to tune the scoring heavily.
- **Waterfalls** are hard to make convincing without simulation (M10).

**Performance & memory.**
- **Detail near the camera costs triangles.** M1.12's rock crests and scree stones raised a 4K mesh standing on the ground from
  7–13 M triangles to 14–24 M (`vista-log/M1.12.md`), with the bound already taken per region (a vertex below the grid takes only the
  kinds around it): sharp detail's bound shrinks only linearly with an edge. Below 2 m the shading carries the roughness instead; finer
  crests or stones in the mesh took the default view to 72 M, past the 48 M one acceleration structure takes here. M1.12b's crowns
  cover far more ground than rock or scree near the camera: pointed crowns 3.6 m high took a forested crest 2 km off at 4K to 78 M
  (35 M without forest); domes 2 m high, bounded only where forest grows, take it to 38 M (`vista-log/M1.12b.md`). Taller crowns
  (M5) need trees as instances, not detail.
- **8 GB VRAM** limits forest density; instancing, procedural materials and measured LOD
  decisions are the levers.
- **Render times are measured, not known in advance.** Bare M0 terrain cost 0.24–1.4 ns per pixel-sample on the 3070
  ([`vista-log/M0.md`](vista-log/M0.md)); M1.14's terrain costs 0.3–21 ns (the cost ledger). Since M1.15 `--estimate` measures
  each view at the final's own size and detail, and the §7.1 numbers are measured for M1's terrain; M5's forest and M4's clouds
  will move both, and the estimate measures them as they come. A sample costs more in dispatches of several samples a pixel than
  in dispatches of one (the parked list).
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
| Painterly filter (2026-10-22) | Welcome as later feature creep (an oil look after the user's Bob Ross paintings), but only after photorealism is reached or the user is done pushing it |
| World footing (2026-10-23) | The world's lowest valleys (the 5th percentile of its eroded ground) meet the place's valley floor, so every altitude rule reads against true altitudes (M1.12c) |
| Composition (2026-10-22) | The user's own paintings are the composition references for the auto camera: depth layers, a framing mass at one edge, the peak on a third, a leading line, focal length searched, the squint test |
| M1 strategy (2026-10-19) | All of the sky before the terrain; contact sheets + loupe first, tweak window last; erosion on the GPU, cached per seed with its hash in the sidecar; climate normals hand-entered for M1 (§10, M1 plan) |
