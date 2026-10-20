# Vista measurement views

The four views the M1 cost ledger is measured on (vista-plan §10, "M1 cost ledger"), committed in M1.1 so
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
ground above it, the preview warns and the view needs a new height.

Since M1.2 both recipes pin their place and date (Zermatt, 2026-07-15), so the sun stays put when the
defaults change (M1.11 lets set pieces push them): day is solar noon, 65.5° up due south, and golden hour
sunset − 60 min, 8.5° up at azimuth 292°.

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
camera, changes them one at a time (the exposure, the time of day, a camera turn, a camera move, the base altitude, the seed through
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
