# Vista M1a.4 — Rock below the grid

The slice's goal, deliverable and verify are in [`vista-plan.md`](../vista-plan.md) §10 (M1a plan, M1a.4), with a status line that
links here. Section references (§n) are to the plan.

*M1a.4 status (2026-11-03): done, in the shading; the geometry is parked.* **Bare rock now carries structure at 4–64 m: gullies and
ribs down its fall line, ledges and cliff bands where its beds' hardness changes, the shadows its ribs cast, and jointed blocks near
the camera.** The world grid's 8 m faces were smooth planes with a fine bump on them: at 1.2 m a pixel (the lit middle range) nothing
below 16 m showed, and near the camera the rock read as crumpled plaster. The structure is a height over bare rock, a pure function of
position, that the shader evaluates per camera hit: its slope turns the hit's normal, and its coarse ribs cast their shadows on it,
marched toward the sun. The mesh does not carry it: as geometry its coarsest octave alone took the hero's 4K mesh from 27.7 M to
47.8 M triangles, and the ledges as much again, past the acceleration structure's ceiling (48 M). The world, the mesh and every
image with `--rock-structure off` are M1a.3a's bit for bit.

*What was built:*

- **Gullies and ribs** (`RockGullies` in `VistaMaterials.hlsli`; its twin `Vista.World.RockStructure.Gullies`): five octaves from
  64 m to 4 m, each phasor noise (Tricard et al. 2019): from a jittered point in each of the 3×3 cells (two wavelengths a side)
  around a point, a plane wave across the fall line with its own phase, under a kernel 1.1 cells wide, summed as complex numbers; the
  sum's angle is a stripe pattern of full contrast running down the slope, whose stripes start, end and fork where the waves cancel
  (there the octave fades out). The angle is shaped into a rib rounded over 0.3 rad and a rounded gully floor (`2t² − 2/3`). Each
  finer octave runs down the fall line of the grid and the coarser octaves together, so small gullies run down the sides of large ribs
  and join the large gullies below: they branch up the slope. Each is 0.09 of its wavelength deep across the face (the height is that
  over the slope's cosine, held at 76°), faded in from 25° to 40° and out where a pixel covers more than a quarter of a wavelength.
- **Ledges and cliff bands** (`RockLedges`; `RockStructure.LedgeTable` and `LedgeAt`): a bed harder than the beds within 8 m of it
  stands steeper and a softer one recedes, the slope along any direction times `1 + G'`, `G' = 2.5·(H − H̄)` held at ±0.7; a seeded
  variation within the beds (0.12 of hardness at 9 m) breaks thick beds into ledges too. `G`, the ledges' height by stratigraphic
  height, is a table per bed-table entry built once in C# and read after the beds on the GPU as a Catmull–Rom spline; it is
  high-passed over ±64 m so it cannot drift. The surface at stratigraphic level σ is offset by `G(σ)` (implicitly: the beds the surface
  cuts are the beds its ledges stand on), so its slope is `(∇h + L·∇offset)/(1 − L·scale)` with `L` the spline's slope. None on
  massive rock.
- **The ribs' shadows** (`RockSunShadow`): from a lit camera hit, up to five steps toward the sun (1.5 m, then each twice the last
  and 1.5 m more, out to 46 m) over the grid's own ground (its slope integrated along the way from the normal map) plus the gullies'
  height (their two coarsest octaves, three where a pixel covers less than 25 cm, each step on its own fall line); where that rises over
  the sun's ray the hit is in a rib's shadow, softened over a few pixels' worth of height, and the march stops once the ray stands
  higher over the ground than any rib can. The mesh casts the grid's own shadows as before; these are the shadows of the relief it
  leaves out. It runs first in the shading, before the materials, and sums only the phasors' values (no slopes), because most of its
  cost turned out to be the registers it holds (gotchas).
- **Jointed blocks** (`RockJoints`, in the bump): bare rock breaks along its bedding (the laminae) and two sets of vertical joints at
  right angles, about 3 and 4.2 m apart, turning slowly over the world and staggered from one bed to the next (a joint ends at a bedding
  plane); each block splits in two along each set. Each block's face turns its own way in the rock's tangent plane (a facet, up to a
  gradient of 0.6, more on hard rock), and an open joint (3 cm) is dark. Faded where a pixel covers more than a quarter of a block.
- **Debris in the gullies**: where a gully's floor lies on a face gentler than about 45°, up to 0.8 of its rock turns to scree before
  the shading sharpens the materials' edges.
- **The switch** (`--rock-structure on | off`, `RenderSettings.RockStructure`, the sidecar's `image.rockStructure`, schema 28) and
  the settings (`MaterialRules.StructureDepth`, `StructureCoarsestMetres`, `StructureOctaves`, `LedgeGain`, `LedgeMax`; the shader reads
  four of them in the cbuffer's padding, so the root signature, at its 64 words, is untouched, and the ledge table rides in the bed
  table's buffer after the beds).
- **Tests** (`RockStructureTests`, seven): the gullies' slope lies across the fall line (with one octave at most 0.35 of it along the
  fall line, with five 0.7, the finer ones running down the coarser ribs' sides); none below 25°, deeper on steeper faces by the
  slope's secant, within the octaves' depths, ribs and floors; their analytic slope is their height's own where they hold still (median
  error under 5%); the profile's rib and floor; the ledges steepen hard beds, flatten soft ones and never drift over the whole table;
  the spline's slope is its height's derivative; the switch and the rules' checks. **The self-test** (`--gpu-selftest`) checks the
  shader's structure against the C# twin at the 4096 places of its material parity check and the ledge table built twice.

*The geometry, tried and parked* (branch `claude/vista-m1a-4-geometry-prototype`, `b937b5be`; the hero at 4K, `--dry-run`): the
mesh carried the gullies' coarse octaves and the ledges over bare rock (`MaterialDetail.HeightOver`), its normals worked out without
them, bounded by region from pyramids of the rock's steepness, its layered share and how fast its beds change along the ground, held
to 3 pixels:

| The mesh carries | Triangles |
|---|---|
| nothing of the structure (M1a.3a, and this slice) | 27.7 M |
| the 64 m and 32 m octaves and the ledges | 123.4 M |
| the 64 m and 32 m octaves | 71.7 M |
| the 64 m octave, at 3 px (6 px) | 47.8 M (41.6 M) |
| the ledges, at 12 px (30 px) | 43.9 M (38.5 M) |

Most of the cost is below the grid at 0.5–3 km, where a rib rounded over a third of a radian needs edges of a metre or two to stay
within a few pixels; the ledges' worst case is beds tilted steeply by the uplift (up to 11 m of stratigraphic height a metre at the
99.9th percentile). Two earlier tries cost more (15.9 GB of video memory) until the beds' tilt was taken from a percentile and the
bound's weight-change term made quadratic for short edges. The way the follow-up should try: the structure sampled into the
hierarchy's own grid heights, so its grid-level errors are the true ones rather than a bound, and only the residual between the
samples as detail (parked, "The rock's structure as geometry").

*Checks:*

- **Off is M1a.3a**: with `--rock-structure off` the hero (640 px, 16 spp, every debug output) and M1.12's rock face render
  byte-identical to `main` (`f858d5b6`), EXR and PNG; the mesh is M1a.3a's (27,697,601 triangles on the hero at 4K), and the world.
- **The sheets** (`vista-views/ground/render.ps1 -Sheets m1a4`): the lit middle range (`m1a-4-lit.png`): the smooth planes broken into
  gullies and ribs, the thin-bedded formation at the right stepped into ledges, the ribs' shadows down the lit faces; the rock face at
  10:30 (`m1a-4-face.png`): ribs, couloirs and their shadows from afar, faceted blocks up close; the near face (`m1a-4-near.png`): the
  gullies and the blocks under the shade; the shaded foreground (`m1a-4-foreground.png`): little changes in shade, where relief shows
  by the sky's light alone and the beds' bands stay (M1a.5); the hero (`m1a-4-hero.png`); a low sun (`m1a-4-low-sun.png`, the
  golden-hour down-slope view): the lit near crest mostly in its ribs' long shadows.
- **The detail budget's measure** (`--debug detail`, the hero at 640 px) counts lit rock by the 8 m cell, so the structure does not
  move its rule; its shares fall a little because the ribs' shadows take some rock out of the sun (at 2 pixels a cell 8.0% → 7.7%, at
  6 4.1% → 3.9%, at 12 none either way). The limit is re-chosen by the user's eye at M1a.7, as planned.
- **The triangles**: none added (above).
- **The cost ledger** (`vista-views/measure.ps1`): `main` and this slice timed in turns, two rounds each, the GPU otherwise idle: the
  ratios 1.08 / 1.22 / 1.31 / 1.14 (sky, aerial, default, down-slope; the pairs 1.08–1.08, 1.17–1.27, 1.27–1.35, 1.13–1.16); M1a.3a's
  row scaled by them, 0.27 / 7.14 / 5.19 / 17.73 ns a pixel-sample. The sky's 8% is the bigger shader alone (its rays meet no ground).
  Most of the rest is the shadows' registers (gotchas; parked, "A leaner rock structure").
- **Tests** (Release, the whole solution): Vista.Tests 975 (seven new), RayTracer.Tests 336, Pinball.Tests 55.
- **The GPU** (`--gpu-selftest`): passes, 91 checks (one new): the structure at 2793 steep places of the 4096, its gullies within
  0.26% of the C# twin at the 99th percentile (of one plus their slope) and 7% at the worst place, where an octave forks; the ledges'
  slope within 0.0018 m an entry (the bed-table coordinate is a float near 8000, its last bit a thousandth of an entry); 22 committed
  views above the eroded ground, the lowest 2.2 m.
- **The goldens** (`--golden-check`): the same eight of ten differ from M1.17's as at M1a.3a, the sky and twilight within tolerance;
  against M1a.3a's moves: aerial −4.58% mean luminance (M1a.3a −4.6%), default −3.77% (−3.5%), hero −1.56% (−1.2%), alpenglow at
  dusk −0.51% (−0.5%) and dawn +1.11% (+1.3%), April's slopes −4.74% (−4.8%), the misty ridges −0.75% (−0.3%), and down-slope
  −11.81% (+0.01%): under a sun a few degrees up its lit near crest falls largely into its ribs' shadows (`m1a-4-low-sun.png`).
  Re-baselined at M1a.7.

*Decisions (revisit freely):*

- **The shading first, the geometry parked**, as the plan's order says: the shading gives the faces their structure at every
  distance and, with the marched shadows, the low sun's relief inside a face; what the mesh would add is the outlines of ridges and
  the structure's shadows on other ground, at three to four times the triangles. The parked item has the measurements and the next try.
- **Phasor noise along the fall line**, not ridged noise or a flow field: its stripes keep full contrast, follow the ground's own
  fall line at every point and fork where they cancel; following the coarser octaves' slope makes them branch. (The "erosion filters"
  of recent shader work do much the same with plain sums of waves, whose contrast fades where waves cancel.)
- **The depth across the face, not vertically**: a gully 3 m deep on a 70° face is 8.8 m of height; a height-field structure with a
  vertical depth would vanish on the steepest faces, where real rock shows the most.
- **Ledges from the hardness contrast, implicit in the beds**: the surface at a bed's level is offset by that bed's ledge, so a
  hard bed's colour lies on its cliff; offsetting by the bed at the unledged height would shift the colours off their ledges by up to
  2 m.
- **Joints in 3-D**: vertical joint planes and the bedding planes cut any face into blocks, so no face needs a projection of its own;
  bedding planes drawn as cracks read as ruled lines and were left to the laminae's steps.
- **The ribs' shadows marched over the grid's own ground**: a flat plane through the hit gave the same shadows (the dark tongues down
  the gullies are the ribs', not the plane's); the integrated slope is right on curved ground too. The march reads each octave on the
  grid's own fall line, where the shading turns each finer octave with the coarser ones' slope: the shadows of the coarse octaves it
  marches barely differ, and it holds far fewer registers.

*Gotchas:*

- **A kernel wider than the cell search misses points**: with a kernel 1.5 cells wide a 3×3 search skips jittered points two cells
  away, and the field jumps at every cell edge (knife cuts in the shading, a curvature of 12 million in the envelope's measure). Points
  lie at least 0.15 of a cell inside their cells, so a kernel up to 1.15 cells is complete.
- **Worst cases set a mesh's bound**: the gullies' worst midpoint deviation is 3–4 times their 99.9th percentile (where a fork turns
  the phase fast), and the beds' tilt 27 entries a metre at its maximum against 11 at the 99.9th percentile; bounds from maxima
  refined every rock face in view.
- **The shadows cost by their registers, not their work**: on the default view the structure first cost 1.55 times `main`; with the
  march compiled in but never run, 1.45 times; without it, 1.12 times. Halving its steps and octaves changed nothing; marching first in
  the shading (before the material weights and samples are live) and summing the phasors' values alone (no slopes, no octave turning
  the next) took it to 1.31 times. Its look did not move (the lit loupe before and after). `[loop]` on its loops changed nothing.
- **Heredocs lose backslashes**, as the memory says: a path `'materials\rock-face…'` written through one became a carriage return.
  Write scripts with the editor; and Python reading a file in text mode turns that CR into LF, so read with `newline=''`.
- **A sheet's labels are comma-separated**: a comma in a label splits it.

**M1a.4 hand-off.** To understand this, start with `vista-views/sheets/m1a-4-lit.png` and `m1a-4-face.png`, then
`Vista.App/Shaders/VistaMaterials.hlsli` (the section "The rock's structure", and in `ShadeGround` the block that calls it) and
`Vista.Core/World/RockStructure.cs` (the C# twin and the ledge table). The geometry prototype is on its branch; the parked item says
what it cost and how the next try should go.

*Commands (from `RayTracer/`, after `dotnet build Vista.App -c Release`):*

```
powershell -File vista-views/ground/render.ps1 -Sheets m1a4 [-Out <dir>]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --preview --recipe vista-views/autocam/range-golden.recipe.json --seed 42 --camera vista-views/signoff/hero.camera.json --width 3840 --loupe 1680,670,640,360 --spp 64 --denoise --out <png> [--rock-structure off]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --gpu-selftest
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --golden-check
powershell -File vista-views/measure.ps1 [-App <a build's Vista.App.dll>]
```

*Files:*

- Changed: `Vista.App/Shaders/VistaMaterials.hlsli` (the structure, the joints, the debris, the shadow), `VistaIntegrator.hlsl` (the
  sun's term times the structure's shadow; the material probe's structure); `Vista.App/Gpu/D3D12VistaRenderer.Materials.cs` (the bed
  table with the ledges, the switch), `D3D12VistaRenderer.cs`, `D3D12VistaRenderer.Memory.cs`, `GpuSelfTest.cs` (the parity check);
  `Vista.Core/World/MaterialField.cs` (the ledge table), `MaterialRules.cs` (the settings and their constants);
  `Vista.Core/Rendering/RenderTypes.cs`, `Cli/PreviewCommandLine.cs`, `Cli/PreviewSession.cs`, `Output/Sidecar.cs` (the switch, schema
  28); the tests that pin the sidecar's schema; `vista-views/ground/render.ps1` (`-Sheets m1a4`); `vista-plan.md`, the READMEs, the
  cost ledger.
- New: `Vista.Core/World/RockStructure.cs`, `Vista.Tests/RockStructureTests.cs`, `vista-log/M1a.4.md`,
  `vista-views/sheets/m1a-4-hero.png`, `m1a-4-lit.png`, `m1a-4-near.png`, `m1a-4-foreground.png`, `m1a-4-face.png`,
  `m1a-4-low-sun.png`.

*Next:* M1a.5 — beds that read as rock (vista-plan §10, M1a plan): the beds' colour tied to this slice's ledges.

*Judge this:* `m1a-4-lit.png`, the lit middle range: M1a.3a's smooth faces above, the rock's structure below; then `m1a-4-face.png`
(a rock face at mid-morning, the whole frame and a window of it up close), `m1a-4-hero.png` and `m1a-4-low-sun.png` (a sun a few
degrees up). *Ignore this:* the light and dark
bands in the shaded foreground (M1a.5); the meadows (M1a.6); the outlines of ridges against the sky, which are still smooth (the
parked geometry); the snow and ice up close (M7). *Questions:* In the bottom loupe, do the lit faces read as rock cut by gullies and
ribs, or as scratched or combed? Up close (the bottom right of the rock face), does it look like a gullied rock face, or like folds of
cloth? Do the dark stripes down the gullies look like the shadows you would see on a real face in morning light? With the sun just
above the horizon, is the crest in front too dark, or is that how low light looks?
