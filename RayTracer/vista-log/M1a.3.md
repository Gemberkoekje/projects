# Vista M1a.3 — The washboard gone

The slice's goal, deliverable and verify are in [`vista-plan.md`](../vista-plan.md) §10 (M1a plan, M1a.3), with a status line that
links here. Section references (§n) are to the plan.

*M1a.3 status (2026-11-03): done.* **The rows of dots are gone: the loose ground creeps.** The pipe model reads its capacity's slope
by central differences, which never see a cell's own height; so on gentle loose ground a ripple two to four cells long feels no pull
back, and where the water drops its load below the faces it grows into a washboard, which the material rules cut into rows of scree,
meadow and forest. Soil creep (the loose material moving toward every lower neighbour at any slope, in proportion to the drop: a
diffusion of the loose ground) damps that ripple by the square of how short it is and leaves the landforms and the rock alone. On
seed 42's world the loose ground's windows rippling at 2–4 cells fall from 3.4% to 0.4% (the base terrain 1.4%); in the dots' loupe
the rows and the lattice of dimples under them are gone. Drainage, relief, the facets and the materials' shares do not move.

**But today's glacier lies on the hero's lit crest again.** The crest's highest 64 m sample stood 1.6 cm *under* the glaciers' line on
M1a.2's world and stands 7 cm *over* it with the creep; a crest at the line carries no ice or a full cap of smooth pale domes, nothing
between, because the ice's own surface lifts it into the zone where snow gathers (the small ice cap's feedback). M1a.2's crest was
clear of ice by chance, as the parked item "Ice caps laid as smooth domes" already said, and any change of the eroded ground moves
caps on and off the crests at the line. The cap is back in the picture's subject (`m1a-3-lit.png`): the user's call whether that
item comes next.

*What was built:*

- **The creep** (`ErosionSettings.CreepRate`, the mountains' 0.03; `ErosionReference.ThermalOut` and `ThermalApply`, mirrored in
  `Shaders/VistaErosion.hlsl` `CSThermalOut` and `CSThermalApply`): in the thermal passes, each cell's loose material sends toward
  every lower neighbour the rate times the drop, with the isotropic nine-point Laplacian's weights (an edge neighbour ⅔, a corner
  ⅙, `ErosionConstants.CreepEdge` and `CreepCorner`, where the cbuffer had two padding words), less where it has too little loose
  material left after the talus slide; the share it sends goes in the thermal buffer's spare fourth float, and each neighbour gathers
  its part by the same drop computed the same way, so the creep keeps its total and the passes stay gather-only. Rock never creeps.
  In a branch of its own on both sides: without creep the shader compiles as before and erodes bit for bit as M1a.2's (gotchas).
  In the cache key only when on (`ContentHash`), so M1a.2's and every earlier world keep their keys. The hills keep none until
  their own milestone.
- **Tests** (`ErosionReferenceTests`): creep damps a washboard three cells long, along the rows or the diagonals, to under 2% in 60
  steps while a 24-cell swell keeps over 80% (and the thermal passes without it leave both alone); it moves only loose material, keeps
  its total and leaves a bare knoll as it was; and on the alpine range at 257² × 8 m the pipe model's loose ground ripples in over 5%
  of its windows without creep and under 2% with the shipped settings. The shipped settings are pinned anew; M1.7's and M1a.2's passes
  keep their pins with the creep off.
- **`vista-views/ground/render.ps1 -Sheets m1a3`**: M1a.2's world and M1a.3's, each made and measured again, the hero, the dots' and
  the lit loupes, M1.12's meadow view, and the five sheets.

*The cause* (a prototype on the CPU reference, scratch only: seed 42's alpine range on a 1025² × 8 m grid, the incision and 1500 steps
of the passes, 30 s a run; the loose ground's windows rippling, the gentle loose ground's under 20°; the base terrain on the same
cells 2.1%):

| The passes | Loose ground | Gentle loose ground | Rock crests (the sharpest 5%), convexity |
|---|---|---|---|
| M1's | 12.5% | 9.2% | |
| M1a.2's | 5.3% | 4.2% | (the reference) |
| … the capacity's slope by steepest descent (each axis' larger drop to a neighbour) | 0.7% | 0.3% | −12% |
| … creep 0.01 / 0.02 / **0.03** / 0.05 / 0.1 | 2.5% / 0.8% / **0.4%** / 0.3% / 0.3% | 1.3% / 0.2% / **0.1%** / 0.1% / 0.0% | 0.03: −1.7%; 0.05: −3.3% |

A uniform gentle plane of loose material does not washboard (5–15°, any direction: its roughness smooths away); the ripple needs the
water dropping a load on the fans below eroding faces. The central difference `(h[i+1] − h[i−1]) / 2` never sees `h[i]`, so a cell
standing over both neighbours, or under them, gets no more and no less capacity than its neighbours' slope gives it: the checkerboard
and its kin feel no restoring pull and grow. A slope by steepest descent sees the cell and stops it (0.7%), which confirms the cause;
but it raises the capacity on every convex cell, so every crest erodes more (the sharpest 5% of the rock's crests 12% less convex, the
rock moved 1.1 m on average), where the user's M1a.2 review wanted crests pointed. Creep moves only the loose ground (the rock 0.3 m
on average, at 0.03).

*Checks:*

- **The eroded world** (`--erosion-measure`, seed 42, 4097² × 8 m; `measure-m1a2.log` and `measure-m1a3.log` of the render script):

| | M1a.2 | M1a.3 |
|---|---|---|
| eroded: along the grid (steep faces, loose ground) | 17.7% (17.8, 18.0) | 17.7% (17.9, 18.2) |
| eroded: rippling (steep faces, loose ground) | 2.0% (1.8, 3.4) | 0.8% (1.1, 0.4) |
| channels ≥ 1 km²: longest without a lake; into one; lakes; Strahler | 39.3 km; 96.2%; 0%; 5 | 39.3 km; 96.2%; 0%; 5 |
| channels ≥ 0.1 km²: longest; into one; streams of the first order | 40.4 km; 97.6%; 2323 | 40.4 km; 97.5%; 2438 |
| relief 1 km (10/50/90%); 5 km | 346/552/783 m; 972/1179/1474 m | 346/551/782 m; 972/1178/1472 m |
| 8 m slopes: mean; over 40°; facet; near its talus | 36.5°; 49.1%; 10.2% at 58.7°; 11.8% | 36.4°; 48.9%; 10.3% at 58.7°; 11.9% |
| spikes over 5 m (base 1421) / over 20 m | 1081 / 2 | 1048 / 2 |
| loose: mean; over 1 m | 13.5 m; 38% | 13.4 m; 37% |
| at Zermatt: rock, scree, meadow, forest, ice | 45.9, 8.2, 11.2, 25.4, 5.0% | 45.9, 7.9, 11.1, 25.7, 4.9% |
| glaciers: share; volume; thickest | 7.6%; 3.35 km³; 226 m | 7.6%; 3.36 km³; 207 m |
| erosion (GPU s; ms a step) | 25.5 s; 17.0 ms | 25.6 s; 17.1 ms |
| hash | `221d35af5bebe0e0` (M1a.2's own, bit for bit) | `d00391ab69d1e932` |

The creep costs the erosion nothing measurable (its two loops of eight neighbours run in the thermal passes, which were never the
cost). The eroded world has more first-order channels of 0.1 km² (2323 → 2438); the larger ones are unchanged.

- **The places** (the crops' lines, M1a.2 → M1a.3): the lit middle range (2000, 850), 3.8 km off: rippling 1.4% → 0.7%, under more than 5 m
  of ice 0.0% → 16.9% (the cap, above); the rows of dots (1300, 1250), 3.4 km: rippling 1.4% → 1.4% (the crop's windows are mostly
  the rock around the fan; the loupe shows the change); the near face (3520, 1680), 133 m off: rippling 4.9% → 2.8%. The hero's
  near ridge (parked: with M1's camera it loomed 130–160 m off at the frame's right on M1a.2's world) shrinks back to a sliver at the
  frame's edge on M1a.3's (`m1a-3-hero.png`): its loose cover crept off.
- **The dots' loupe** (`m1a-3-dots.png`): the rows of scree, meadow and forest are gone, and with them the lattice of dimples in the
  normals (`loupe-dots-*.normal.png` in the render script's folder; with them the sheet was over 1 MB); the forest and meadow patches
  keep their ragged, irregular edges (M1a.6's).
- **The meadow up close** (`m1a-3-meadow.png`, M1.12's meadow view on the snow-capped mountains): the same wavy texture in both, which is the grass's own bump seen at a grazing
  angle (M1.12's tufts), not the ground: no band at the washboard's 16–32 m in either. So the parked banding is the grass, M5's, as
  first placed. A patch of bare soil shows at the window's corner on M1a.3's world: other ground under the same pixels.
- **Tests** (Release, the whole solution: Vista.Tests 966, three more; RayTracer.Tests 336, Pinball.Tests 55).
- **The GPU**: `--gpu-selftest` passes (90 checks; its erosion parity now runs the creep, the shipped settings: the thermal
  passes within 9e-10 of the largest value, 200 steps within 3 mm), and the cached shipped world re-erodes to the same bits; 22
  committed views above the eroded ground, the lowest 2.2 m. With the creep off the GPU erodes M1a.2's world bit for bit (4097²:
  `221d35af5bebe0e0`; 1025²: `main`'s `f5c1b2e7e145b075`).
- **The goldens** (`--golden-check`): eight of ten differ from M1.17's, the same eight as M1a.2 (every view over the eroded world;
  re-baselined at M1a.7): aerial −1.9% mean luminance (M1a.2 −2.3%), default +0.2% (+0.4%), down-slope −0.1% (its blocks 73% rms:
  other ground in every pixel), hero +0.02% (−3.6%), alpenglow at dusk +0.02% and dawn +1.3% (+1.0%), April's slopes −3.9%
  (−3.9%), the misty ridges −0.4% (−0.4%); the sky and twilight within tolerance.
- **The cost ledger** (`vista-views/measure.ps1`): 0.26 / 5.74 / 4.12 / 15.55 ns a pixel-sample, the GPU idle. M1a.2's world, timed in turns
  with it in the same session (two rounds each): 0.27 / 5.75 / 4.13 / 15.53, so the creep costs the trace nothing (ratios 0.95, 1.00,
  1.00, 1.00; the sky's is noise at a quarter of a nanosecond). The fall from M1a.2's row (0.30 / 6.92 / 5.11 / 18.70) is the idle GPU
  and the reworked world: that row was measured on the first M1a.2's world with other work on the GPU.

*Decisions (revisit freely):*

- **Creep, not a new slope**: the steepest-descent slope removes the cause, and the numbers would have taken it, but it blunts the
  crests M1a.2's rework made pointed; creep is a real process of loose ground (soil creep, rain splash) and acts only there.
- **0.03**: the smallest rate that leaves no trace of the washboard in the loupes (0.02 left a faint one on the 1025² world's largest
  fan); over 1500 steps it smooths loose ground over about 75 m (8 m · √(2 · 0.03 · 1500)), the scale of a fan's own shape, not its
  channels, which the water keeps cutting.
- **The rules keep reading the 8 m normal**: the plan also proposed reading the rules' slope over a few cells; with the washboard gone
  the dots' material ids show no rows, and a smoothed slope would also blur every boundary at the grid's scale, which M1a.6's ragged
  edges would then have to fight. Not built.
- **The creep in its own branch**, on both sides, though a creep of 0 adds nothing: folded into the passes' loops it changed how the
  compiler built them, and M1a.2's world came out other bits (gotchas).
- **The lit crest's ice left as the solve puts it**: reported, not tuned away. A rate that happened to leave the crest under the line
  would be chance again, undone by the next change of the ground.

*Gotchas:*

- **A creep of 0 changed the GPU's world**: the first shader computed the creep inside the thermal passes' loops; with it off every
  term was an exact zero, and the C# reference stayed bit for bit (its pins held), but the GPU's eroded world came out other bits
  (1025²: `a844eb2b71507811` against `main`'s `f5c1b2e7e145b075`): the compiler built the changed loops differently. In a branch of its
  own it is `main`'s bit for bit. Worse, the first measure of M1a.2's settings had stored those other bits in the cache under M1a.2's
  key (the creep is not in the key when off); the render script's measure stored M1a.2's own world again.
- **The ice moves by centimetres**: the parked item's "by chance" measured: 9 cm of the crest's height decide between no ice and a
  cap. Neither the solve's iterations (60 to 480: the same answer on each world) nor the rate change that.
- **`dotnet build` of the whole solution while a render runs** fails to copy `Vista.Core.dll` into `Vista.App`'s folder (M1a.2's
  gotcha again); the compile itself is fine.

**M1a.3 hand-off.** To understand this, start with `vista-views/sheets/m1a-3-dots.png` (the rows of dots, M1a.2 above, M1a.3 below)
and `m1a-3-lit.png` (the ice back on the lit crest), then `Vista.Core/World/ErosionReference.cs` (`ThermalOutCell`, `ThermalApplyCell`,
`CreepToward`, mirrored in `Vista.App/Shaders/VistaErosion.hlsl`) and `ErosionSettings.CreepRate`. What is left of the grid's
patterns: the talus planes' creases (parked, for M1a.4). What M1a.3 brought back: today's ice on the lit crest, the parked "Ice caps
laid as smooth domes", now in the hero's subject.

*Commands (from `RayTracer/`, after `dotnet build Vista.App -c Release`):*

```
powershell -File vista-views/ground/render.ps1 -Sheets m1a3 [-Out <dir>]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --erosion-measure --recipe vista-views/autocam/range-golden.recipe.json --seeds 42 --out <dir> --crops-camera vista-views/signoff/hero.camera.json --crops "2000,850;1300,1250;3520,1680" [--set CreepRate=0] [--grid 1025x8] [--parity]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --golden-check
powershell -File vista-views/measure.ps1
```

M1a.2's world for comparison: `$env:VISTA_EROSION_SETTINGS = 'CreepRate=0'`; M1's:
`'RefineCubic=0;RefineChannelCurves=0;RefineFaceDetail=0;DischargeSpread=0;ThermalRate=0.25;CreepRate=0'`.

*Files:*

- Changed: `Vista.Core/World/ErosionSettings.cs` (`CreepRate`, the mountains' 0.03, the hills' 0, the key, `ErosionConstants.CreepEdge`
  and `CreepCorner`), `ErosionReference.cs` (the creep in the thermal passes); `Vista.App/Shaders/VistaErosion.hlsl` (the same);
  `Vista.Tests/ErosionReferenceTests.cs` (three tests, the pins), `GlacierTests.cs` and `StrataFieldTests.cs` (the older settings
  without the creep); `vista-views/ground/render.ps1` (`-Sheets m1a3`, M1's settings without the creep); `vista-plan.md` (§4.3,
  M1a.3's status, the parked items), `vista-log/cost-ledger.md`, the READMEs.
- New: `vista-log/M1a.3.md`, `vista-views/sheets/m1a-3-dots.png`, `m1a-3-ground.png`, `m1a-3-hero.png`, `m1a-3-lit.png`,
  `m1a-3-meadow.png`.

*Next:* the user's call on today's ice on the lit crest (the parked "Ice caps laid as smooth domes" now, or M1a.4 — rock below the
grid — with the cap in the hero until M7).

*Judge this:* `m1a-3-dots.png`, the rows of dots: M1a.2 above, M1a.3 below, the beauty with the material colours beside it;
then `m1a-3-ground.png` (from above). *Ignore this:* the rock's own texture and the near faces' smooth look (M1a.4); the light
and dark bands on the rock (M1a.5); the meadows' hard edges and flat colour (M1a.6); the trees (M5); and the pale domes on the
lit crest in `m1a-3-lit.png` and the hero, which are today's glacier (the question below). *Questions:* In the bottom row, are the
rows of dots gone, and does the ground there read as one slope of forest, meadow and scree? From above, does the right square of
each pair look more like mountain ground than the left? In `m1a-3-lit.png` the crest has pale domes again: should fixing how ice
caps look come next, before the rock's detail?
