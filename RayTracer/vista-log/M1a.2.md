# Vista M1a.2 — Ground that gathers its water

The slice's goal, deliverable and verify are in [`vista-plan.md`](../vista-plan.md) §10 (M1a plan, M1a.2), with a status line that
links here. Section references (§n) are to the plan.

*M1a.2 status (2026-10-29): done.* **The grid's own patterns are mostly gone from the eroded world, by three changes, one of which
the plan did not foresee.** The incision now comes back to the 8 m grid smoothly, its channels along curves and the base terrain's
finer detail kept on the faces it cut: the incised ground's relief along the grid's eight directions falls from 67.5% to 15.2% (the
base terrain 14.6%). That alone left the eroded world at 24% and its comb in place, and the comb's cause was not the facets: on a
face that falls along the grid's rows, the pipe model's lanes of cells carry their own water, which no water crosses, and each cuts
its own rill. So the water now erodes over a channel's width (its discharge spread twice over its neighbours before the capacity
reads it), and the thermal pass runs at a tenth of M1.7's rate, which stops it pressing every steep face into one talus plane creased
in the grid's directions. The eroded world: 33.1% → 17.4% along the grid, 7.5% → 2.0% of its windows rippling at 2–4 cells (the base
terrain 1.4%), drainage and relief no worse. In the hero the lit middle range's rock is less combed, but a glacier now caps its crest
as smooth pale domes, and the rows of dots remain (M1a.3).

*Reworked (2026-10-29, the user's review: "they look more like blobs than M1 looked").* **The smooth transfer rounded every high
point.** The monotone cubic leaves every summit and crest the coarse samples hold level, so each came back a rounded top a coarse cell
(64 m, some 50 pixels at 3.8 km) wide; and a glacier lay on the lit crest as pale domes, its surface the solve's, smooth between 64 m
samples. The rework keeps the high points pointed (`StreamPower.UpsampleSharp`, `RefineCubic = 2`): at a maximum along a row or a
column the curve leaves the sample along each segment's own slope, a kink, so a summit stays a point; low points stay rounded. In the
lit middle range the crests are points again and no ice lies there (0.0% of the place under 5 m of ice, against 6.5% on the first
world; M1 0.03%). What was learned on the way, and not shipped:

- **The glacier left the crest by chance, not by the sharper summits.** World-wide, as much ice lies on convex ground on the reworked
  world as on the first (16.9% of its volume on 64 m crests, 15.9% first, M1 13.2%): the ice solve on the 64 m grid moves its ice with
  any change of the ground. A cap laid as the solve's smooth surface reads as a dome wherever it lies (parked, vista-plan §10, "Ice
  caps laid as smooth domes").
- **Ice draped over convex beds** (the solve's thickness over the rock's own shape, `GlacierDrapeMetres`): it turned the first world's
  domes into ice that follows the crests, but it gave every crest the rock had stood through the full thickness, and the ridge 130 m
  from the hero's camera rose under 15 m of ice into a smooth mass filling the frame's right. Capped at the smooth surface's thickness
  (so it adds no ice anywhere) it changed nothing visible. Removed.
- **Three measures of blobs, none of which saw them**: the crests' fall at 24 m over 48 m (the small crests of the faces' detail
  dominate it: M1 0.48, smooth 0.52, pointed 0.53), the share of ground domed both ways after smoothing (it counts the detail's knobs:
  base 10.5%, M1 7.8%, smooth and pointed both 11.8%) and the 64 m summits' drop to their neighbours (37, 40, 36 m). The blobs were
  ice, at one place. What would have caught them: the ice under each loupe place, which `--erosion-measure`'s crops now report.
- **The near ridge**: with M1's camera the ridge at the frame's right, 130–160 m off, rises into the frame on the reworked world (its
  silhouette from x ≈ 3600 to x ≈ 3000 of the 4K frame at y = 1100): metres of change that near the camera swing a sixth of the frame,
  and the face reads as a smooth dark mass (the near faces' detail is M1a.4). The auto camera's own pick on the new world frames the
  range without it (`m1a-2-hero.png`, right).

*What was built:*

- **The smooth transfer** (`StreamPower.Refine`, `UpsampleSmooth`): monotone cubic Hermite along the coarse rows, then down the fine
  columns (Fritsch and Butland's tangents), through every coarse sample with a continuous slope, no hump or hollow between two
  samples that they do not have (a plane comes back exactly). Today's glaciers are laid with the same interpolant (`Glaciers.Lay`).
- **Curved channels** (`CarveChannels`, `CutCorners`): each stream from its head to where it joins a larger one (the largest donor
  continues a stream), its coarse samples' line with the corners cut twice by Chaikin's scheme (ends kept), carved into the fine grid
  as a flat floor three quarters of a cell either side and a rounded V of the hard rock's incision talus (60°) out to a coarse cell;
  at an interior joint the floor runs on along its segment through the floor's width, never past a stream's ends.
- **The faces' detail** (`FinerThanCell`): the base terrain's detail finer than the 64 m cell (the detail less its mean over each
  cell, brought back smoothly) is kept on the ground the incision cut or buried, fading in over 24 m from a carved channel; where
  the incision left the ground within 25 m of where it was, all of the detail stays, as before.
- **The spread discharge** (`ErosionReference.SpreadDischarge`, `Shaders/VistaErosion.hlsl` `CSDischarge`, `CSSpreadX`, `CSSpreadZ`):
  each step, after the flux, each cell's discharge per metre of width, then `DischargeSpread` (2) times a `[1 2 1] / 4` spread along
  the rows and the columns; `CSWater` erodes with it. Two buffers more on the GPU (128 MiB at 4097²), gather-only like every pass;
  the self-test's parity checks the three new passes and the water pass that reads them.
- **The settings** (`ErosionSettings`): `RefineCubic`, `RefineChannelCurves`, `RefineFaceDetail`, `RefineChannelDetailMetres`,
  `DischargeSpread`, and the mountains' `ThermalRate` 0.25 → 0.02. Off (0), each is M1.11a's or M1.7's bit for bit, and in the cache
  key only when on, so M1.11d's, M1.12c's and M1.13's settings keep their keys (their pins hold). The hills keep M1.7's passes until
  their own milestone. The cache's passes version is 5.
- **`--erosion-measure --incise-only`** now reports the grid's patterns and writes the crops of the incision alone (50 s, no GPU),
  the loop most of this slice ran in; the incision's line names the time back to the world grid. Since the rework each crop's line
  also gives its share under more than 5 m of ice.
- **The rework's pointed high points** (`StreamPower.UpsampleSharp`, `RefineCubic = 2`, shipped): the same monotone cubic, but at a
  maximum along a row or a column the segments leave the sample along their own differences (still between their samples).
  `RefineCubic = 1` is the first M1a.2's smooth transfer; today's ice is laid smoothly for both.
- **`vista-views/ground/render.ps1 -Sheets m1a2`**: M1's world (M1's transfer and passes through `VISTA_EROSION_SETTINGS`) and
  M1a.2's, each made and measured again, the first M1a.2's lit loupe beside them, the auto camera's pick on M1a.2's world, and the
  four sheets.

*The comb's cause* (tilted planes, a prototype of the pipe model on the CPU, 257² cells of 8 m, a 35° plane roughened by half a metre,
1500 steps; relief along the grid / windows rippling):

| The fall line off the grid's rows | 0° | 10° | 22.5° | 35° | 45° |
|---|---|---|---|---|---|
| M1.7's pipe model | 79% / 76% | 64% / 71% | 43% / 62% | 20% / 38% | 21% / 44% |
| the discharge spread twice | 58% / 40% | 26% / 21% | 5% / 6% | 7% / 6% | 33% / 9% |
| eight pipes | 44% / 63% | 24% / 59% | 8% / 49% | 23% / 65% | 44% / 78% |
| eight pipes and the spread | 55% / 4% | 11% / 2% | 4% / 2% | 11% / 3% | 58% / 10% |

On a plane falling along the rows all the water goes through one pipe, no lane feeds another, and the instability grows in every lane
at once: a rill a cell or two wide every two to four cells, along the rows (at 10° the rills still run along the rows). Spread over
about five cells, the water cuts gullies that follow the fall line, start at the foot and grow upward, five to eight cells apart (at
0° and 45° gullies along the fall line lie along the grid by rights). Eight pipes alone move the comb to the faces that fall along the
diagonals; with the spread they follow the fall line a little better at 10°, but on real ground (a 513² prototype run) they gained
nothing (34.2% against 35.3% along the grid; 18.0% against 18.2% with the slow thermal pass), so they were not built.

*The choices* (a 1025² × 8 m world of the same recipe and seed, 25 s a run; the eroded world's relief along the grid / its windows
rippling; the base terrain 14.1% / 2.1%):

| The erosion | Along the grid | Rippling |
|---|---|---|
| M1's | 35.4% | 6.5% |
| the smooth transfer, curves and the faces' detail (M1.7's passes) | 24.9% | 4.6% |
| … with M1.7's pipe model weaker (rock erodibility, dissolve, capacity or the discharge's power: one at a time) | 22.9–23.7% | 1.9–2.7% |
| … with the discharge spread once / twice / three times | 21.7% / 21.2% / 21.1% | 2.1% / 1.6% / 1.2% |
| … spread twice, the thermal rate 0.1 / 0.05 / **0.02** / 0.01 | 20.7% / 20.0% / **18.4%** / 17.7% | 1.6% / 1.4% / **1.4%** / 1.3% |
| … and the faces' detail finer than the cell only (shipped) | 18.7% | 1.3% |
| (for scale: 10 steps of erosion) | 16.9% | 0.7% |

Higher talus angles, a slower thermal pass alone, more flux damping or a lower minimum slope did not help. The thermal rate 0.02 was
chosen over 0.01 for the margin: at 0.01 more steep faces stand over their talus at the end (55.7% of the 8 m slopes over 40°).

*Checks:*

- **The eroded world** (`--erosion-measure`, seed 42, 4097² × 8 m; `measure-m1.log` and `measure-m1a2.log` of the render script):

| | M1 | M1a.2 first (smooth) | M1a.2 reworked (pointed) |
|---|---|---|---|
| incised: along the grid / rippling | 67.5% / 0.2% | 15.2% / 0.8% | 15.9% / 0.8% |
| incised: channels ≥ 1 km² longest without a lake; into one; lakes | 6.8 km; 48.8%; 6.4% | 2.5 km; 26.2%; 7.7% | 2.5 km; 25.2%; 7.8% |
| eroded: along the grid (steep faces, loose ground) | 33.1% (32.8, 34.1) | 17.4% (17.7, 17.9) | 17.7% (17.8, 18.0) |
| eroded: rippling (steep faces, loose ground) | 7.5% (8.6, 8.6) | 2.0% (1.7, 3.4) | 2.0% (1.8, 3.4) |
| rock's lines of high curvature down the fall line (80–90° off the contour) / along it | 41.5% / 2.4% | 23.4% / 6.3% | 23.6% / 6.2% |
| channels ≥ 1 km²: longest without a lake; into one; lakes; Strahler | 33.6 km; 95.7%; 0%; 5 | 39.3 km; 96.2%; 0%; 5 | 39.3 km; 96.2%; 0%; 5 |
| relief 1 km (10/50/90%); 5 km | 332/541/774 m; 960/1173/1454 m | 348/555/785 m; 976/1181/1473 m | 346/552/783 m; 972/1179/1474 m |
| 8 m slopes: mean; over 40°; facet; near its talus | 35.3°; 48.6%; 9.9% at 48.6°; 11.0% | 36.7°; 49.5%; 10.5% at 58.8°; 12.2% | 36.5°; 49.1%; 10.2% at 58.7°; 11.8% |
| 64 m slopes: facet; near its talus | 10.9%; 2.5% | 10.6%; 3.2% | 10.6%; 3.1% |
| spikes over 5 m (base 1421) / over 20 m | 411 / 0 | 706 / 0 | 1081 / 2 |
| loose: mean; over 1 m | 12.7 m; 36% | 13.8 m; 38% | 13.5 m; 38% |
| at Zermatt: rock, scree, meadow, forest, ice | 45.2, 8.1, 10.9, 26.0, 5.3% | 46.2, 8.2, 11.0, 25.3, 5.0% | 45.9, 8.2, 11.2, 25.4, 5.0% |
| glaciers: share; volume; thickest | 7.9%; 3.42 km³; 297 m | 7.6%; 3.33 km³; 275 m | 7.6%; 3.35 km³; 226 m |
| erosion (GPU s; ms a step); back to the world grid (CPU) | 19.7–21.7 s; 13–14 ms; 0.1–0.2 s | 26.4 s; 17.6 ms; 0.5–0.7 s | 26–31 s; 17–21 ms; 0.65 s |

The reworked world's eroded hash is `221d35af5bebe0e0` in both of its runs. Its erosion times varied by run: other work held about
5 GB of the GPU's memory during the rework, and two renders hit the Windows GPU timeout before passing on a retry (gotchas). The
pointed summits leave more single cells standing over their neighbours (1081 spikes over 5 m, the base terrain 1421).

The incised ground drains worse than M1's before the GPU erosion (its longest channel without a lake 6.8 → 2.5 km): the faces' detail
brings the base terrain's small hollows back onto the cut faces. The erosion fills them (its pools to 100 m); the eroded world drains
better than M1's.

- **The places** (the crops' lines, M1 → reworked): the lit middle range (2000, 850), 3.8 km off, 35.9% → 16.6% along the grid,
  rippling 9.0% → 1.4%, under ice 0.03% → 0.0% (the first M1a.2: 6.5%); the rows of dots (1300, 1250), 3.4 km, 32.1% → 15.7%,
  16.0% → 1.4%; the near face (3520, 1680), 134 m off at 4 cm a pixel (the 8 m cell 188 pixels), 30.5% → 15.7%, 9.0% → 4.9%, its
  768 m square under ice 12.0% → 13.6% (the face itself is rock).
- **Tests** (Release, the whole solution: Vista.Tests 963, four more; RayTracer.Tests 336, Pinball.Tests 55): the sharp upsampling
  keeps a straight-sided ridge's point where the smooth one rounds it, and rounds a valley floor alike; the smooth upsampling
  keeps the coarse samples and a plane, stays between two samples along a row and has no crease at a bend; the curved channels take
  the saddles out of a diagonal V-valley's floor, keep its coarse samples, leave the ground beyond a coarse cell alone and report
  their distances; three corner cuts turn an east-north-east zigzag into a falling curve that keeps its ends and passes none of its
  corners; the spread is `[1 4 6 4 1] / 16` after two passes and keeps the total; on a face falling 10° off the rows, 300 steps
  comb it without the spread (over half the windows ripple, over 40% of the relief along the rows) and do not with it (under 10%,
  under 25%). M1.7's erosion keeps its pin, M1.11d's transfer its pin; the shipped ones are pinned anew.
- **The GPU**: `--gpu-selftest` passes (90 checks, three more: the discharge and its two spreads against the C# reference, the largest
  difference 8e-8 of the largest value; 200 steps within 2 mm); 22 committed views above the eroded ground, the lowest 2.0 m (the
  reworked world: 90 checks again, the lowest 2.6 m).
- **The goldens** (`--golden-check`): eight of ten moved, as the plan expects until M1a.7: every view over the eroded world. On the
  reworked world: aerial −2.3% mean luminance, default +0.4%, down-slope −0.1% (its blocks 73% rms: other ground in every pixel),
  hero −3.6%, alpenglow at dusk +0.03% and dawn +1.0%, April's slopes −3.9%, the misty ridges −0.4%; the sky and twilight within
  tolerance.
- **The cost ledger** (`vista-views/measure.ps1`): 0.30 / 6.92 / 5.11 / 18.70 ns a pixel-sample against M1.17's 0.44 / 6.95 / 5.84 /
  20.79, on the first M1a.2's world; the integrator is unchanged, the default and down-slope views stand on other ground. Not measured
  again for the rework: other work held the GPU at 95% (5.6 GB of its 8 GB) and the aerial view's render hit the Windows GPU timeout.
- **The views** (`MeasurementViewTests`, the self-test): on M1a.2's ground the default and sky views stood 16.3 m under the incised
  ground (11.8 m under the eroded), the down-slope view 5.6 m, `pools/fog-sea` 2.4 m under the eroded ground and `pools/mist-lake`
  0.4 m over it; each keeps its pose and is lifted to stand where it stood: the default and sky views 2 m above the incised ground
  (6.5 m above the eroded; the self-test's camera with them), down the slope 3 m, the two pools views 2 m above the eroded ground.

*Decisions (revisit freely):*

- **The spread discharge, not eight neighbours or varying rain** (the plan's guesses): the tilted planes show the comb is the lanes'
  isolation, which a spread fixes and eight pipes only rotate; rain or erodibility varying at 30–100 m would have given the rills
  places to start but not stopped a rill in every lane.
- **A slower thermal pass** (0.02): measured, not a look choice; it also leaves fewer slopes exactly at their talus (8 m: the share
  near the talus 21% → 14% on the small world). The talus planes' creases that remain are parked (§10, "Talus planes creased in the
  grid's eight directions"), for M1a.4.
- **The faces keep the detail finer than the cell, all of it**: half of it combed more (25.1% along the grid and 6.6% rippling against
  23.2% and 5.5% at M1.7's passes); the part at the cell's own scale was taken out in case it moved the glaciers and the near face,
  which it did not, but it adds nothing at 8 m and keeps the incision's 64 m shape the incision's.
- **The channels' V at the hard rock's talus (60°)**, so the carving never cuts a coarse wall back; the flat floor three quarters of a
  cell wide, so a route along a curve between samples meets no pit (with a V's foot alone, the curves cut the incised ground's longest
  channel without a lake from 6.9 to 3.8 km).
- **Pointed high points, smooth everywhere else** (the rework): the user's verdict on the first M1a.2 (blobs) over its numbers,
  which could not see them; the grid's patterns are the same either way (17.7% against 17.4% along the grid).
- **No draped ice**: draped, the ice buried the crests the rock stood through; capped, it changed nothing. Where the ice lies is the
  solve's, and it moves with the ground; how a cap looks is parked (§10, "Ice caps laid as smooth domes").
- **The near views lifted, not searched for again**: same spots, same poses, at the heights above the ground they had, so the ledger's
  views change as little as the ground lets them.
- **The hero's camera stays** (`signoff/hero.camera.json`, 150 m above M1's eroded ground), so every slice compares with M1's
  frame; the auto camera's pick on the reworked world is shown beside it (`m1a-2-hero.png`), a heading that leaves the near ridge out.

*Gotchas:*

- **The erosion cache key holds the settings, so tuning runs collide when a setting is missing from it**: every new setting went into
  `ContentHash` at once (only when on), or a later render would read another variant's world.
- **`--set` in a background PowerShell job after a rebuild**: a running `Vista.App` holds `Vista.Core.dll`; `dotnet build Vista.App`
  fails while a measure or a render runs, `dotnet test Vista.Tests` does not (its own copy).
- **.NET's current directory is not PowerShell's**: `[IO.File]::WriteAllText` with a relative path wrote three empty camera files at the
  repository's root before the error showed; absolute paths since.
- **`D3D12VistaRenderer.Erosion.cs` is LF in the working tree** while its neighbours are CRLF; a string replace with `\r\n` found
  nothing.
- **Root UAVs have no bounds**: `CSWater`'s read of the spread buffer sits behind a `[branch]` on the constant, and the buffer is
  bound even when the spread is off.
- **The curve test's head**: four side cells tie as the head's largest donor; the first in row order wins and turns the stream in
  from the side, so the test's channel threshold keeps only the floor's cells.
- **The numbers rewarded the blobs** (the rework's lesson): the grid's patterns score round shapes as natural, and none of three
  shape measures saw what the eye saw, which was ice at one place. Look at the loupes before believing a measure, and ask what a
  measure cannot see.
- **The Windows GPU timeout with the GPU shared**: about 5 GB of the 3070's memory was held by other work, and three renders
  (a loupe's trace twice, a near view's acceleration-structure build once) were removed by the driver's timeout (TDR); each passed on
  a retry, and the same renders had passed earlier in the day.

**M1a.2 hand-off.** To understand this, start with `vista-views/sheets/m1a-2-lit.png` (M1, the first M1a.2's blobs, the rework) and
`m1a-2-ground.png` (from above, M1's ground beside M1a.2's), then `Vista.Core/World/StreamPower.cs` (`Refine`, `UpsampleSharp`,
`CarveChannels`, `FinerThanCell`) and
`Vista.Core/World/ErosionReference.cs` (`SpreadDischarge`, mirrored in `Vista.App/Shaders/VistaErosion.hlsl`). What is left of the
grid's patterns: the washboard on gentle loose ground (3.4% of its windows ripple) and the rows of dots it leaves in the render
(M1a.3), and the talus planes' creases (parked, for M1a.4). What is left of the blobs: ice caps laid as smooth domes wherever the
solve puts a cap (parked), and near faces that read as smooth masses (M1a.4).

*Commands (from `RayTracer/`, after `dotnet build Vista.App -c Release`):*

```
powershell -File vista-views/ground/render.ps1 -Sheets m1a2 [-Out <dir>]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --erosion-measure --recipe vista-views/autocam/range-golden.recipe.json --seeds 42 --out <dir> --crops-camera vista-views/signoff/hero.camera.json --crops "2000,850;1300,1250;3520,1680" [--incise-only] [--grid 1025x8] [--set DischargeSpread=0 ...] [--parity]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --golden-check
powershell -File vista-views/measure.ps1
```

M1's world for comparison: `$env:VISTA_EROSION_SETTINGS = 'RefineCubic=0;RefineChannelCurves=0;RefineFaceDetail=0;DischargeSpread=0;ThermalRate=0.25'`
(its eroded hash is M1's, `a4b757024c5b5e7e`).

*Files:*

- The rework changed: `Vista.Core/World/StreamPower.cs` (`UpsampleSharp`), `ErosionSettings.cs` (`RefineCubic` 2), `Glaciers.cs`
  (smooth ice for 1 and 2), `Vista.App/Gpu/ErosionMeasure.cs` (the crops' ice), `Vista.Tests/StreamPowerTests.cs`,
  `ErosionReferenceTests.cs`; `vista-views/ground/render.ps1` (the first M1a.2's loupe, the auto camera's pick); the log, the plan,
  the READMEs, the ledger and the four sheets.
- Changed: `Vista.Core/World/StreamPower.cs` (the transfer, the curves, the faces' detail), `ErosionReference.cs` (the spread
  discharge, the state's `Discharge`), `ErosionSettings.cs` (the new settings, the mountains' and hills' values, the constants'
  `SpreadDischarge`), `Glaciers.cs` (the ice laid smoothly), `ErosionCache.cs` (passes version 5); `Vista.App/Shaders/VistaErosion.hlsl`
  and `Gpu/D3D12VistaRenderer.Erosion.cs` (the three passes, two buffers), `Gpu/ErosionParity.cs`, `Gpu/ErosionMeasure.cs`
  (`--incise-only`'s patterns and crops), `Gpu/GpuSelfTest.cs` (its camera); `Vista.Tests/StreamPowerTests.cs`,
  `ErosionReferenceTests.cs`, `GlacierTests.cs`, `StrataFieldTests.cs`; `vista-views/default.camera.json`, `sky.camera.json`,
  `down-slope.camera.json`, `pools/fog-sea.camera.json`, `pools/mist-lake.camera.json`; `vista-views/ground/render.ps1`;
  `vista-plan.md` (§4.3, M1a.2's status, four parked items), `vista-log/cost-ledger.md`, the READMEs.
- New: `vista-views/sheets/m1a-2-hero.png`, `m1a-2-lit.png`, `m1a-2-near.png`, `m1a-2-ground.png`.

*Next:* M1a.3 — the washboard gone (vista-plan §10, M1a plan).

*Judge this:* `m1a-2-lit.png`, the lit middle range: M1 (top), the first M1a.2 with its blobs (middle), the rework (bottom); then
`m1a-2-hero.png` (M1, M1a.2 from M1's camera, and the auto camera's own pick on M1a.2's world) and `m1a-2-ground.png` (from above).
*Ignore this:* the rows of dots and the washboard on gentle ground (M1a.3); the rock's own texture and the near face's smooth dark look
in shade (M1a.4); the light and dark bands on the rock (M1a.5); the meadows (M1a.6); the trees (M5). *Questions:* In the bottom loupe,
do the crests read as rock again, or still as blobs? Do its faces read as gullies and ribs, or as combed? In the hero from M1's camera
the slope at the right now looms large: does the auto camera's pick beside it look like a better picture of the same mountains?
