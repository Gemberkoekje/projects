# Vista M1a.1 — The map

The slice's goal, deliverable and verify are in [`vista-plan.md`](../vista-plan.md) §10 (M1a, and its plan), with a status line that
links here. Section references (§n) are to the plan.

*M1a.1 status (2026-10-28): done.* **Each of the four things that make M1's hero read as a model has a cause, and two of them share
one.** The grooves and planes, and the rows of dots, are both patterns of the 8 m world grid, made in two steps of the erosion: the 64 m
stream-power incision is brought back to the 8 m grid as flat 64 m facets with straight seams, the base terrain's finer detail faded off
them, and the pipe model then combs a rill every 2–4 cells down each steep facet and lays a washboard on the gentle ones, which the
slope rules cut into rows of scree, meadow and forest. The marble is the beds' colour painted by height alone on those smooth faces, with
no relief of its own; the felt is the meadow's 8 m rule sharpened to a clean edge with no texture a 1.2 m pixel can see. Two measures of
the grid's own patterns (`GridArtefacts`) now say how far each stage is from ground that runs every way: the base terrain 14.6% along the
grid's eight directions (13.3% if it ran every way) and 1.4% of its windows rippling at 2–4 cells; incised 67.5% and 0.2%; eroded 33.1%
and 7.5%. Nothing rendered changed. M1a's slices are planned from the map (vista-plan §10, M1a plan).

*What was built:*

- **The grid's patterns** (`Vista.Core/World/GridArtefacts.cs`): *alignment*, the share of the relief whose principal axis (the
  Hessian's, from second differences) lies within 3° of the grid's rows, columns or diagonals, each sample weighed by how much more it
  curves one way than the other; and *ripple*, the share of 128 m windows (16² samples, 8 apart, plane removed, Hann-windowed) whose power
  at wavelengths under 10 cells sits more than 40% in one peak at 2–4 cells. Both take a filter of samples (steep faces, loose ground,
  the ground around a point).
- **`--erosion-measure`** reports them for the base terrain, the incised ground and the eroded world (whole grid, steep faces of 30°
  and more, loose ground of 0.5 m and more), and with `--crops-camera c.json --crops x,y;…` finds the ground each pixel of the camera's
  3840×2160 frame sees (`TerrainSight.Hit`, as the auto camera traces), prints its distance, the metres a pixel covers and the patterns
  around it, and writes top-down crops of the three stages there (`DebugImages.ReliefCrop`: 96 samples, 768 m, north up, grey
  hillshade with no tint, each sample 4 pixels).
- **`vista-views/ground/render.ps1`**: the map's five sheets: the hero with the four places boxed, and per place a 640×360 loupe of the
  4K frame, pixel for pixel, denoised, beside the debug output that shows its cause, and for the grooves and the dots the crops.

*The map* (the hero: `autocam/range-golden.recipe.json`, seed 42, `signoff/hero.camera.json`; `m1a-1-map.png`):

1. **Grooves and planes** (`m1a-1-grooves.png`; the lit middle range, 3.8 km off, 1.22 m a pixel, the 8 m cell 6.6 pixels). Three causes
   stacked. *The facets:* `StreamPower.Refine` brings the incision (on 64 m cells) back to the 8 m grid by bilinear interpolation, so
   each 64 m cell is a near-flat patch with creases along the coarse grid's lines, and it adds the base terrain's finer detail back only
   where the incision left the ground within 25 m of where it was (`DetailFadeMetres`); the ground moved 320 m on average, so across the
   mountains the faces are bare 64 m facets (the incised grid: 67.5% of its relief along the grid's eight directions), and the channels
   it carves (`CarveLinks`) run as straight links in those eight directions. *The comb:* on a plane the pipe model's water runs in
   parallel sheets and never gathers across one (M1.11a's known limit), so it cuts a rill every 2–4 cells straight down each facet (the
   eroded grid: 33.1% along the grid, 7.5% of its windows rippling against the base terrain's 1.4%; at this place 35.9% and 9.0%). *Below
   the grid, nothing:* the mesh's detail is isotropic noise 4% of each wavelength high (0.64 m at 16 m) and the shading's bump ends at
   1.6 m, faded where a pixel covers more than a quarter of it, so at 1.2 m a pixel the facets between the rills shade as planes. The soft
   beds' outcrops, drawn out along the parallel gullies (the dark streaks in the material ids), double the comb.
2. **Rows of dots** (`m1a-1-dots.png`; 3.4 km off, 1.09 m a pixel). *The washboard:* on the gentle loose ground the incision leaves as
   flat facets (fans and benches) the pipe model's sediment transport leaves a ripple 2–4 cells apart, mostly diagonal (16.0% of this
   place's windows ripple; the base terrain's 1.4%), the instability M1.7 met when its pick-up and drop-off were faster ("a washboard
   ripple on loose slopes", its "dunes"). *The cut:* the rules read the slope from the 8 m grid's normal map (`GroundNormal`), and the
   ripple swings it across the forest's and the meadow's slope limits and scree's talus, so each ripple becomes a row of scree, meadow
   and forest by turns; the material ids are the same with the detail below the grid off. Probably the parked "meadow's regular
   banding" too (8.6% of the loose ground ripples).
3. **Marbled beds** (`m1a-1-beds.png`; the shaded foreground, 1.5 km off, 0.49 m a pixel, the cell 16 pixels). The rock's colour comes
   from its bed, looked up by height (the bed table, folded by its offset): the soft rock's spectrum mixed into the hard rock's by the
   bed's hardness, the bed's tints (±25% brightness, a warmer or cooler cast) and each lamina of about a metre ±12%. All of it is
   colour keyed on height alone: the normals show no step at any band (the laminae's riser-and-tread shading is faint and fades where a
   pixel covers a quarter of a lamina), and on smooth faces the contour lines are long smooth curves, so the bands swirl like marble or
   wood grain. Real beds stand out in relief (hard ledges, soft beds recessed under their own debris), broken by the gullies.
4. **Felt meadows** (`m1a-1-meadows.png`; a lit crest 3.9 km off, 1.23 m a pixel). The meadow's weight comes from the rules on the 8 m
   maps (the grid normal's slope, the altitude, the aspect), blended over the cells and then sharpened to the sixth power (`ShadeGround`)
   after a noise of 2.5 and 0.6 m that a 1.2 m pixel averages away: the edge is the 8 m rule's smooth contour, cut clean. Inside, the
   grass spectrum with only a 7 m drier-grass noise (at most 40% toward the soil's colour), and the grass's own bump (0.1–0.2 m) gone at
   this distance: flat colour. They sit on the crests because the crests between the incision's facets are gentle.

*Checks:*

- **The measures on synthetic fields** (`GridArtefactsTests`, three tests): relief made of 1500 waves in random directions sits at
  10–19% along the grid and ripples in under 2% of its windows; grooves along the rows or the diagonals are over 95% along it, the same
  grooves turned 20° under 5%, both ripple in over 90% of the windows at 3.3 cells apart and in under 5% at 12; a quarter of a field
  washboarded at 2.6 cells ripples in 15–35% of the windows and over 85% inside the quarter (the filter); a filter that keeps nothing
  gives NaN. The whole solution green in Release (Vista.Tests 957, three more; RayTracer.Tests 336, Pinball.Tests 55).
- **Calibration** (a prototype on the cached seed-42 world before the C#): isotropic fractals of slope −1.5 to −2.2 ripple in 0.05–0.16% of
  their windows at the 40% threshold, the eroded world near the hero in 10.4%; the alignment of an isotropic field 14.8%, the eroded
  world in the hero's footprint 26–33%.
- **The measure** (`--erosion-measure --recipe autocam/range-golden.recipe.json --seeds 42`): base 14.6% along the grid (steep faces
  14.6%), ripple 1.4% (1.3%); incised 67.5% (64.3%), 0.2% (0.2%); eroded 33.1% (32.8%, loose ground 34.1%), 7.5% (8.6%, 8.6%). The
  eroded world's hash is the cached one's (`a4b757024c5b5e7e`): the measure re-eroded the same world.
- **The places** (the crops' lines): grooves (2000, 850) at 3841 m, 6.6 pixels a cell, 14.3% → 35.9% along the grid, ripple 0.0% → 9.0%;
  dots (1300, 1250) 3427 m, 7.4 pixels, 13.7% → 32.1%, 1.4% → 16.0%; beds (300, 1800) 1532 m, 16.4 pixels, 15.1% → 23.8%, 0.7% → 1.4%;
  meadows (1060, 880) 3865 m, 6.5 pixels, 13.8% → 29.6%, 0.7% → 7.6%.
- **The detail budget passes the hero** (`--debug detail`, M1.14's measure): lit bare ground the final frame shows at 2 pixels a cell or
  more 8.3% of the frame, 4 or more 5.1%, 6 or more 4.3%, 8 or more 0.05%, 12 (the limit) none. The hero reads as a model at 6–7 pixels a
  cell, inside the budget: what shows there is the grid's own pattern, which M1a removes before the budget relaxes.
- **The dots are the grid's normals, not the detail below it**: the dots' loupe with `--detail off` has the same material ids (the
  normals lose the finer dimples only).
- **Nothing rendered moved**: the shaders, the materials and the meshes are unchanged (the new code is the measure and a debug image).
  `--gpu-selftest` passes (87 checks); `--golden-check` ten of ten identical.

*Decisions (revisit freely):*

- **Two measures, not one**: the grooves are a pattern of *direction* (the incision's eight) and of *period* (the pipe model's comb),
  the dots of period alone, and the two stages trade them (the pipe model halves the incision's alignment while adding the ripple), so
  each fix is checked on both.
- **Alignment by the Hessian's axis, cell by cell**, not a windowed spectrum: it can be filtered to any set of cells (steep faces,
  loose ground, a crop) and costs a pass; its second differences favour the grid slightly (an isotropic field measures 14.6–14.8%, not
  13.3%), so a fix is judged against the base terrain's own figure.
- **Ripple as a peak's share of the fine power**: a comb or a washboard puts its power in one peak, natural relief spreads it; 40% keeps
  isotropic fractals under 0.2% of their windows.
- **The map's crops come from `--erosion-measure`**, which already runs every stage, rather than a new mode; it re-erodes the world
  (about two minutes with the incision), as it always has.
- **No look changed**, so no before/after sheet and no cost-ledger row; the sheets are the map.
- **M1a's plan** (vista-plan §10, M1a plan): the grid's fixes first (M1a.2 the incision's facets and the comb, M1a.3 the washboard), the
  detail below the grid after them (M1a.4), then the colours (M1a.5 the beds, M1a.6 the meadows) and the sign-off (M1a.7), so the
  detail and the colours are tuned once, on the final grid, as M1 tuned its materials on its final terrain.

*Gotchas:*

- **A place's pixel can see past it**: the meadows' first pixel (930, 830), on the crest's edge in the 640 px frame, hit the glacier
  10.3 km behind it; read the crops' distance line before labelling a place.
- **Windows PowerShell reads a script without a BOM as ANSI**: the sheets' labels use "·", so `ground/render.ps1` is saved with one, as
  `signoff/render.ps1` is.
- **`Run` in the render scripts hides Vista.App's output**: the first run failed at once with only "failed"; the cause ("The first
  argument must be --preview") showed only once the output was let through.
- **Bash heredocs holding Python string literals** broke quoting again; edits went through script files.
- **Python on this machine opens text as cp1252** unless told otherwise: a sidecar needs `encoding='utf-8'`.

**M1a.1 hand-off.** To understand the map, start with `vista-views/sheets/m1a-1-grooves.png` and `m1a-1-dots.png` (the three stages
from above), then `Vista.Core/World/StreamPower.cs` (`Refine`, `Upsample`, `CarveLinks`: where the facets come from) and
`Vista.Core/World/GridArtefacts.cs` (what the next slices are checked by); the marble and the felt live in
`Vista.App/Shaders/VistaMaterials.hlsli` (`ShadeGround`: the beds' colour by height, the sixth-power sharpening).

*Commands (from `RayTracer/`, after `dotnet build Vista.App -c Release`):*

```
powershell -File vista-views/ground/render.ps1 [-Sheets map,grooves,dots,beds,meadows]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --erosion-measure --recipe vista-views/autocam/range-golden.recipe.json --seeds 42 --out <dir> --crops-camera vista-views/signoff/hero.camera.json --crops "2000,850;1300,1250;300,1800;1060,880"
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --preview --recipe vista-views/autocam/range-golden.recipe.json --seed 42 --camera vista-views/signoff/hero.camera.json --width 640 --debug detail --out hero.png
```

*Files:*

- New: `Vista.Core/World/GridArtefacts.cs`; `Vista.Tests/GridArtefactsTests.cs`; `vista-views/ground/render.ps1`;
  `vista-views/sheets/m1a-1-map.png`, `m1a-1-grooves.png`, `m1a-1-dots.png`, `m1a-1-beds.png`, `m1a-1-meadows.png`.
- Changed: `Vista.App/Gpu/ErosionMeasure.cs` (the patterns per stage, `--crops`); `Vista.Core/Output/DebugImages.cs` (`ReliefCrop`);
  `vista-plan.md` (M1a's plan and status, the parked items it takes); the READMEs.

*Next:* M1a.2 — ground that gathers its water (vista-plan §10, M1a plan).

*Judge this:* `m1a-1-map.png` (the four places boxed on the hero) and the bottom rows of `m1a-1-grooves.png` and `m1a-1-dots.png`: the
same 768 m of ground from above as the world is made, the mountain as first drawn (left), after the big valleys are cut (middle), and
after the water's erosion (right). *Ignore this:* the colours of rock and meadow (M1a.5, M1a.6, then M2); the trees (M5); nothing in these
sheets is a fix yet. *Questions:* Do the four boxes mark what you saw, and is there anything else in the hero that reads as a model? From
above, does the left square of each pair look more like real mountain ground than the right one? Is the order right: the grooves first,
then the dots, the rock's detail, the beds' colour and the meadows?
