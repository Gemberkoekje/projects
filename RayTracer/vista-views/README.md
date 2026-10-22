# Vista measurement views

The four views the M1 cost ledger is measured on ([`vista-log/cost-ledger.md`](../vista-log/cost-ledger.md)), committed in M1.1 so
every slice measures the same pixels. They are the views M0's batch defaults were measured on, at
3840×2160, 64 spp, seed 42:

| View | Recipe | Camera | What it costs |
|---|---|---|---|
| Sky only | `mountains-day.recipe.json` | `sky.camera.json` (pitch +45°) | camera rays that escape: the floor |
| Aerial over the range, day | `mountains-day.recipe.json` | `aerial.camera.json` (4.2 km up, pitch −12°) | the range in depth |
| Default camera, day | `mountains-day.recipe.json` | `default.camera.json` | the M0 default camera for seed 42, pinned |
| Down a slope, golden hour | `mountains-golden.recipe.json` | `down-slope.camera.json` (pitch −30°) | every pixel terrain, low sun |

`default.camera.json` is the pose `DefaultCamera.For` gave seed 42 on the M0 terrain (1.7 m above the
ground there). It is a file now so later terrain changes do not move the view; if erosion (M1.7) lifts the
ground above it, the preview warns and the view needs a new height. Since M1.11a's incision the ground there is a valley floor
532 m below it (552 m below the down-slope view), so both views look over the range from above; the views stay as they are.

Since M1.2 both recipes pin their place and date (Zermatt, 2026-07-15), so the sun stays put when the
defaults change: day is solar noon, 65.5° up due south. Golden hour was sunset − 60 min, 8.5° up at azimuth 292°,
until M1.11 made it a sun height: the setting sun at +2° (19:22:35 solar, azimuth 299°, 2.3° up with refraction). The weather sliders are
auto, so since M1.11 the haze follows Zermatt's July humidity (optical depth 0.119 against M1.3's fixed 0.1).

Measure (from `RayTracer/`, after `dotnet build Vista.App -c Release`):

```
powershell -ExecutionPolicy Bypass -File vista-views/measure.ps1
```

One view by hand, or a sheet of the day views:

```
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --preview --seed 42 --width 3840 --spp 64 \
    --recipe vista-views/mountains-golden.recipe.json --camera vista-views/down-slope.camera.json --out down.png
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --sheet --seed 42 --out day-views.png \
    --recipe vista-views/mountains-day.recipe.json \
    --vary camera=vista-views/sky.camera.json,vista-views/aerial.camera.json,vista-views/default.camera.json
```

## Atmosphere checks (M1.3)

`atmosphere/` holds the views the M1.3 atmosphere was checked on: two recipes pinned to Zermatt (a July evening and
15 October) and three cameras over the seed-42 range, two at 4.2 km looking toward and away from the evening sun (the
reddened sun; the Earth's shadow and the Belt of Venus) and one at 3.4 km looking at the peaks (lit after the valley's
sunset). `render.ps1` reruns every M1.3 check: `--atmosphere-check` above and below the horizon, the sun-elevation
sheets from −6° to +10°, the peaks sheet and an October time-of-day sheet.

```
powershell -ExecutionPolicy Bypass -File vista-views/atmosphere/render.ps1 [-Paths 1048576] [-Out folder]
```

## Aerial perspective checks (M1.4)

`aerial/layered-ridges.camera.json` stands 3 km above the base on the east side of the seed-42 range and looks west
along it with a 60 mm lens: ridge behind ridge to the far side of the tile. At noon the air between them is lit; at
golden hour the camera looks toward the sun across valleys in the ridges' shadow. `render.ps1` reruns every M1.4 check:
`--atmosphere-check --segments` up from the valley floor and down from 1.7 km, `--atmosphere-compare` on the ridges at
noon and at golden hour and on the down-slope view's shadowed valley, and the sea-level against 3000 m sheet.

```
powershell -ExecutionPolicy Bypass -File vista-views/aerial/render.ps1 [-Paths 1048576] [-ReferenceSpp 16384] [-Out folder]
```

## Sun and sky sampling checks (M1.5)

`sampling/mountains-dawn.recipe.json` is the day recipe at dawn (the sun 3.6° below the horizon, all light from the sky).
`render.ps1` reruns every M1.5 check on four views (dawn and day from the default camera, golden hour from the default camera
and down the slope): `--sampling-compare` of M1.4's estimator against MIS over 32 independent renders of 128 spp each
(agreement, variance at equal samples, time per sample, and the Russian-roulette sweep), a 16 spp pair of each view to look at,
and the null test (MIS against itself on other streams). The GPU is often shared with other programs: read `nvidia-smi` first,
and trust timings as ratios between estimators (`--sampling-compare` interleaves them) or between builds rendered alternately.

```
powershell -ExecutionPolicy Bypass -File vista-views/sampling/render.ps1 [-Replicates 32] [-Spp 128] [-Out folder]
```

## Denoiser checks (M1.6)

`denoise/render.ps1` reruns every M1.6 check on M1.5's four views: each rendered at 16 spp and denoised by Intel Open Image
Denoise every way `--denoise-compare` knows (albedo and normal noisy, clean or prefiltered; the input scale automatic, physical
or metered; each quality; the CPU and CUDA), against a 4096 spp reference rendered as two halves, at 640 and 320 px wide; then
the default settings timed at 4K on both devices, and one raw | denoised | reference sheet of the four views. It needs the
denoiser fetched first (it is never committed); the CUDA timings share the GPU with whatever else runs on it, and the CPU
timings the CPU, so read `nvidia-smi` first and build nothing meanwhile.

```
powershell -ExecutionPolicy Bypass -File tools/fetch-oidn.ps1
powershell -ExecutionPolicy Bypass -File vista-views/denoise/render.ps1 [-Spp 16] [-ReferenceSpp 4096] [-TimingWidth 3840] [-Out folder]
```

## Erosion checks (M1.7)

`erosion/render.ps1` reruns every M1.7 check: the GPU's erosion passes against their C# reference and the erosion of four
seeds on the world grid (`--erosion-measure --parity`: GPU time, memory, top-down maps), what six views see on grids of 16.4,
32.8 and 65.5 km (`--sight-lines`, the measurement behind the grid's extent), and three sheets: the four seeds from the aerial
camera (with `-Before`, a `main` build's image first, then this build un-eroded, then eroded), the seed-42 views the same way,
and the seeds' erosion maps from dry runs. The worlds it erodes are cached in a folder under `-Out`, not in the user's cache.

`default.camera.json` still stands above the eroded seed-42 ground (erosion lowered it there), and so does every other committed
camera: no render of them warns.

```
powershell -ExecutionPolicy Bypass -File vista-views/erosion/render.ps1 [-Before ..\main\Vista.App.dll] [-Seeds 42,7,1234,2026] [-Out folder]
```

## Watch mode checks (M1.8)

`watch/render.ps1` reruns every M1.8 check: `--watch-check` starts a `--watch` process on copies of the day recipe and the aerial
camera, changes them one at a time (the exposure, since M1.10a the meter and the display curve and back through the options file,
the time of day, a camera turn, a camera move, the base altitude, the seed through
the options file, and the seed back), checks that each render rebuilt exactly the stages that change touches, then renders every
step again with a cold `--preview` process and compares the files byte for byte. It runs at 640 px with every debug output and the
denoised image (eroding both seeds into a cache under `-Out`: the first-time cost), then at the default 320 px (the worlds now
cached), and prints the wall time of each change against the cold render's.

```
powershell -ExecutionPolicy Bypass -File vista-views/watch/render.ps1 [-Spp 64] [-Out folder]
```

Watching by hand: the seed and every other option outside the recipe and the camera go in the options file, so editing files
drives the whole session (type `quit` to stop):

```
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --watch --recipe r.json --camera c.json --options o.txt --out p.png
```

## View-dependent mesh checks (M1.9)

`mesh/render.ps1` reruns every M1.9 check: `--mesh-measure` on the five views (default, aerial, down the slope, layered ridges,
range) at 320 px and 4K, for the uniform mesh of the world grid (M1.7's, now bent onto the Earth) and the view-dependent mesh at
0.5, 1 and 2 px, with and without the detail below the grid and the far field beyond it — triangles, memory, build time and GPU
cost per pixel-sample, as a table; the same world at every size (three views at 4K box-filtered down to 320 px beside their 320 px
previews, one exposure per pair); 4K loupes near the camera (the grid alone, 1 px, 0.25 px); and the shadow of a peak off screen:
`mesh/offscreen-shadow.camera.json` stands 30 m above a valley floor and looks away from the golden-hour sun, across the valley in
the shadow of a ridge about a kilometre behind it, rendered with the uniform mesh (everything kept) and the view-dependent one. With
`-Before`, before/after sheets of the five views on both recipes.

```
powershell -ExecutionPolicy Bypass -File vista-views/mesh/render.ps1 [-Before ..\main\Vista.App.dll] [-Spp 64] [-Out folder]
```

One view's mesh by hand (the sidecar's `world.mesh` and the console's `mesh:` line say what it is and what it cost):

```
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --preview --recipe vista-views/mountains-golden.recipe.json --seed 42 \
    --camera vista-views/aerial.camera.json --width 3840 --mesh-error 1 [--detail off] [--far-field off] [--mesh uniform] --out p.png
```

## Exposure and display checks (M1.10a)

`exposure/render.ps1` reruns every M1.10a check: the four measurement views and the layered ridges at golden hour, each under
{average, incident} × {ACES, AgX} (two sheets per view, one per meter, varying the display curve; the four cells must share one EXR,
byte for byte), the alpenglow sweep (`atmosphere/zermatt-july.recipe.json` from `atmosphere/range.camera.json`, the sun at +2° to
−1.5°) the same way, and prints each variant's exposure, the meter's reading and the share of the frame that clips (a channel at
255) or is white. With `-Before` (a `main` build) it renders the five views with `main` too, checks that they are byte-identical to
this build's `--meter average --display aces`, PNG and EXR, and tiles the before/after sheets.

```
powershell -ExecutionPolicy Bypass -File vista-views/exposure/render.ps1 [-Before ..\main\Vista.App.dll] [-Width 480] [-SweepWidth 320] [-Spp 64] [-Out folder]
```

One view's four looks by hand (only the PNG changes; a watch re-exposes a kept render when `--meter` or `--display` changes in its
options file):

```
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --sheet --recipe vista-views/mountains-golden.recipe.json --seed 42 \
    --camera vista-views/down-slope.camera.json --meter average --vary display=aces,agx --out average.png
```

## Linked sliders (M1.11)

`sliders/render.ps1` reruns the M1.11 checks: the golden-hour height sheet (the setting sun at +2°, +4° and +6° on the
down-slope, layered-ridges and default views at Zermatt in July, and the layered ridges in October), with each cell's
sunlight reddening and the warmth and saturation of its brightest tenth; the month sheet (Zermatt at solar noon on the 15th
of every month from the aerial camera, every slider auto but the date), with the season, temperature, humidity, haze and
snowline the chain chose; and with `-Before`, a before/after of the measurement views and the ridges at golden hour against
`main`. A slider file pins sliders (`--sliders s.json`); the views' recipes still pin theirs themselves.

```
powershell -ExecutionPolicy Bypass -File vista-views/sliders/render.ps1 [-Before ..\main\Vista.App.dll] [-Width 480] [-Spp 64] [-Out folder]
```

## Drainage checks (M1.11a)

`drainage/render.ps1` reruns the M1.11a checks: `--erosion-measure` on four seeds with M1.7's erosion alone (`--set IncisionSteps=0 --set PoolFillMetres=0`)
and with the stream-power incision first, printing each world's drainage measures (the longest channel without a lake or a closed
basin, the share that drains into a channel, the lakes, the Strahler orders) for the base terrain, the coarse grid before and after
the incision, the ground handed to the GPU and the eroded world; then a sheet of the four seeds' drainage images, M1.7's world against
this build's. With `-Before` (a `main` build) it also renders the four seeds from the aerial camera and the seed-42 views (default,
down a slope, layered ridges, range) with both builds, in a low afternoon sun (the day recipe at `--sun-elevation 12`, which shows
the relief better than golden hour's +2°). A dry run writes `<out>.drainage.png` beside `<out>.erosion.png` when its world is eroded.

```
powershell -ExecutionPolicy Bypass -File vista-views/drainage/render.ps1 [-Before ..\main\Vista.App.dll] [-Width 640] [-Spp 64] [-Out folder]
```

## Display look (M1.11b)

`look/render.ps1` makes the M1.11b choice sheet: the four measurement views, the default camera at golden hour (+2°), the layered
ridges in October at +2° and the alpenglow sweep, each under {AgX base, AgX punchy} × {0, +0.5, +1 EV} of the incident meter's own
compensation (two sheets per view, one per look, varying `compensation`; the six cells must share one EXR, byte for byte), and prints
each variant's exposure, the frame's mean and median (the largest channel), its saturation and its brightest tenth. With `-Before`
(a `main` build) it checks that `main`'s four measurement views are byte-identical to this build's `--look base --compensation 0`.

```
powershell -ExecutionPolicy Bypass -File vista-views/look/render.ps1 [-Before ..\main\Vista.App.dll] [-Width 480] [-SweepWidth 320] [-Spp 64] [-Compensations 0,0.5,1] [-Out folder]
```

## Output finish (M1.11c)

`finish/render.ps1` checks the M1.11c finish against a `main` build (`-Before`, required). Six views (the four measurement views,
the default camera at golden hour and the layered ridges in October at +2°) rendered by `main` and by this build with `--dither off`,
one EXR each: the share of pixels with a channel at 0, the share that changed (every one must have had a channel at 0 under `main`,
so only clipped colours move), and over those the Oklab hue's mean distance from AgX's base look. Then `--look base --dither off`
against `main`'s `--look base` on the four measurement views, PNG and EXR byte for byte; the sky view at 3840×2160, denoised, the
dither off and on from one render (`--vary dither=off,on`): the longest run of equal 8-bit values down five sky columns and a loupe of
the sky, plain and contrast-stretched; and the default view with `--png16`, read back by GDI+ (an independent decoder) within a step
of the 8-bit PNG. It writes the two sheets the log cites: `before-after.png` and `sky-loupe.png`.

```
powershell -ExecutionPolicy Bypass -File vista-views/finish/render.ps1 -Before ..\main\Vista.App.dll [-Width 480] [-CellWidth 320] [-Spp 64] [-SkyWidth 3840] [-SkySpp 1024] [-DenoiseDevice cuda] [-LoupeWidth 400] [-Out folder]
```
