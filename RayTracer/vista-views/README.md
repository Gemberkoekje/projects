# Vista measurement views

The four views the M1 cost ledger is measured on ([`vista-log/cost-ledger.md`](../vista-log/cost-ledger.md)), committed in M1.1 so
every slice measures the same pixels. They are the views M0's batch defaults were measured on, at
3840×2160, 64 spp, seed 42:

| View | Recipe | Camera | What it costs |
|---|---|---|---|
| Sky only | `mountains-day.recipe.json` | `sky.camera.json` (pitch +45°) | camera rays that escape: the floor |
| Aerial over the range, day | `mountains-day.recipe.json` | `aerial.camera.json` (4.2 km up, pitch −12°) | the range in depth |
| Default camera, day | `mountains-day.recipe.json` | `default.camera.json` | near the ground, facing the range |
| Down a slope, golden hour | `mountains-golden.recipe.json` | `down-slope.camera.json` (pitch −30°) | every pixel terrain, low sun |

`default.camera.json` was the pose `DefaultCamera.For` gave seed 42 on the M0 terrain (1.7 m above the
ground there), a file so later terrain changes do not move the view; if erosion lifts the ground above a view, the preview
warns. M1.11a's incision left it and the down-slope view 532 and 552 m above a valley floor, and on M1.11d's world that spot is
83 m underground, so M1.11d re-placed both on the eroded seed-42 ground, 3 m above rock the GPU erosion barely moves (CI checks
them over the incised ground, the self-test over the eroded one): the default at (2625, −4375) looking 20° west of north at the
range, down the slope on a lit 37° face at (−2850, 0) looking down it toward the golden-hour sun. The sky view stands at the
default's spot, pitched up 45°. On M1.12c's world (folded strata) that spot is 184 m underground and the down-slope one 90 m, so
M1.12c re-placed them by the same search: the default 3 m above rock at (−250, −5125), 1645.3 m, again looking 20° west of north at
the range (45% sky, the range 1.4–3.8 km away), down the slope 3 m above a lit 35° face at (−6725, −375), 1510.8 m, looking down it
toward the golden-hour sun across a valley whose far side shows folded beds; the sky view at the new default's spot. On M1.13's
world (glacial carving) that spot is 15 m underground and the down-slope one 75 m, so M1.13 re-placed them by the same search, now
kept 300 m off the glaciers and, down the slope, out of the ridges' golden-hour shadow: the default on a meadow at (4450, −8825),
again looking 20° west of north, at the glaciated range (48% sky, the range 2.5 km away, glaciers in 8% of the frame), down the slope
3 m above a lit 37° face at (−3025, 3325), 1551.4 m; the sky view at the default's spot. When the carving's glaciers grew (their line
from 1500 to 1200 m above height 0) the default's ground rose 17 m, and the default and sky views now stand at 1698.5 m, 6 m above
the eroded ground and 2 m above the incised ground CI checks. The rock-face view stands 2 m above its old place's new ground; the
meadow view moved to (605, 6161), 2 m up, looking 240° (south-west) up a meadow with scree fans above it, and the scree-cone view
to the same spot, looking 285° and 10° up at one of those fans; the forested crest is kept 20 m above the incised ground. On M1a.2's
world (the incision's faces keep the base terrain's finer detail) the default's ground rose 18 m and the down-slope one 9 m: the
default and sky views now stand at 1716.8 m (2 m above the incised ground, 6.5 m above the eroded), down the slope at 1560 m (3 m),
and the pools' fog-sea and mist-lake views 2 m above the eroded ground (1502.4 and 1151.6 m), their poses kept. Ledger rows from
before M1.11d, from before M1.12c, from before M1.13, and from before M1a.2, measured other poses or other ground.

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

## Relief checks (M1.11d)

`relief/render.ps1` reruns the M1.11d checks: `--erosion-measure` on four seeds with M1.11a's world (this build with M1.11a's uplift
and no hardness: `UpliftOctaves=0` and the old talus and erodibility, which makes `main`'s world bit for bit) and with this build's
uplift map and rock, printing each world's drainage, its relief (the local relief in 1 km and 5 km squares, the highest point of each
1 km square: their 10th, 50th and 90th percentiles), its slopes (the share in the most crowded ±2° window from 20° up: the facets)
and its rock's hardness; then a sheet of each seed's uplift map and hardness map. With `-Before` (a `main` build) it also renders the
four seeds from the aerial camera and the seed-42 range and layered-ridges views with both builds, and the default and down-slope views
of `-BeforeRef` (`main` before M1.11d) against the re-placed ones, in a low afternoon sun (the day recipe at `--sun-elevation 12`). A
dry run writes `<out>.hardness.png` beside `<out>.drainage.png` when its world is eroded.

```
powershell -ExecutionPolicy Bypass -File vista-views/relief/render.ps1 [-Before ..\main\Vista.App.dll] [-CellWidth 400] [-Spp 64] [-MapSize 256] [-Out folder]
```

## Materials checks (M1.12)

`materials/render.ps1` makes the M1.12 sheets: the four seeds from the aerial camera on a summer afternoon with their material-id and
tier outputs (`ids.png`; the renders erode any seed the cache lacks), each seed's materials from above with the shares of the world
each covers (`maps.png`, from dry runs: a dry run writes `<out>.materials.png` and the sidecar's `materials.shares`), with `-Before` (a
`main` build) the seed-42 views from both builds (`views.png`), Lambert's ground against rough ground at golden hour
(`diffuse.png`, from `--diffuse-compare`), and windows of 3840×2160 frames near the camera (`loupes.png`). `-Sheets` makes only some
of them (`ids,maps,views,diffuse,loupes`); `-Worlds` names another world cache.

`materials/scree-cone.camera.json`, `rock-face.camera.json` and `meadow.camera.json` stand 2 m above the eroded seed-42 ground (CI checks
them over the incised ground, which the GPU erosion's sediment buried 1.5–39 m deep there; the self-test over the eroded ground). M1.12c
re-placed them on its world, chosen by the rules' materials where rays from the frame's middle land 40–500 m off: north at a pale scree
slope above meadow (−7400, 2400), south at a face with its talus below (6332, −3948), north-east over a meadow toward rocky slopes
(−920, 5836).

```
powershell -ExecutionPolicy Bypass -File vista-views/materials/render.ps1 [-Before ..\main\Vista.App.dll] [-Sheets ids,maps,views,diffuse,loupes] [-CellWidth 400] [-Spp 64] [-Out folder]
```

## Forest checks (M1.12b)

`forest/render.ps1` makes the M1.12b sheets and checks: with `-Before` (a `main` build) the ledger views by `main` and by this build's
`--forest off`, byte for byte (`same`), and the seed-42 views from both builds at Zermatt's valley floor (`views.png`) and with the
world's base at `-LowAltitude` metres (default 1000, `low.png`); every committed view's 4K mesh with the forest and without (`mesh`,
dry runs); each seed's materials from above with the forest's share at both altitudes (`maps.png`); the four seeds from the
over-forest camera with their material ids (`ids.png`); with `-Photo` (the user's own photo over the Alps, 11 April 2025, kept out of
the repository; the sheet carries a downscaled crop without its metadata) the photo beside the over-forest view in April light, forest
off and on (`photo.png`); the canopy's kernels against rough diffuse ground (`canopy.png`, from `--canopy-compare`); a forested crest at
4K (`loupe.png`); and with `-Before` the cost against `main` (`cost`). The log, `forest.log`, has every number.

`forest/over-forest.camera.json` stands 7 km above the base over the seed-42 range's east side, looking north-west across the valleys
that are forest when the base is 1000 m; `toward-sun.camera.json` and `away-from-sun.camera.json` stand 5 km up looking toward and
away from an afternoon sun 25° up; `forest-crest.camera.json` stands 2 m above a valley floor, looking up with a 50 mm lens at a
forested crest about 2 km off against the sky (on M1.12c's world it stands 28 m up, clear of the incised ground, and the crest is
0.5–1.8 km off).

```
powershell -ExecutionPolicy Bypass -File vista-views/forest/render.ps1 [-Before ..\main\Vista.App.dll] [-Photo <photo>] [-Sheets same,mesh,maps,ids,views,low,photo,canopy,loupe,cost] [-LowAltitude 1000] [-Out folder]
```

## Strata checks (M1.12c)

`strata/render.ps1` makes the M1.12c sheets and checks: `--erosion-measure` on four seeds with M1.12b's rock (this build with M1.12c's
strata off, the same bits as `main`) and M1.12c's, one seed at a time (`measures`: the footing, the contour-line measure, the relief,
facets and drainage, and the materials' shares at the anchored altitude, in `strata.log`); each seed's rock hardness from above
(`maps.png`); the over-forest view in April light for the strata variants the defaults were chosen from (`choice.png`); with
`-Before` (a `main` build) the seed-42 views at each build's own altitude (`views.png`); with `-Photo` (the user's photo over the
Alps) the photo above the over-forest view by `main` at its own altitude and at 1000 m and by this build at its anchored altitude
(`photo.png`); and the cost against `main` (`cost`). Worlds for the measures and the variants go to `-Worlds` one at a time and are
deleted after (a world is 285 MB).

```
powershell -ExecutionPolicy Bypass -File vista-views/strata/render.ps1 [-Before ..\main\Vista.App.dll] [-Photo <photo>] [-Sheets measures,maps,choice,views,photo,cost] [-Out folder]
```

## Snow and glacier checks (M1.13)

`snow/render.ps1` makes the M1.13 sheets and checks: `--erosion-measure` on four seeds with M1.12c's world (this build with the
glaciers off, M1.12c's settings) and M1.13's (`measures`: the footing, relief, facets, drainage, the glaciers, and the
materials' shares at Zermatt in July, snow, firn and ice among them, in `snow.log`); `--valley-measure` (`valleys`: the troughs
where glaciers run, carved against the rivers' alone, and `valleys.png` of their sections); each seed's materials from above
(`maps.png`); with `-Before` (a `main` build) the seed-42 views in July (`views.png`); the default and aerial views on the 15th of
January, April, July and October from the defaults alone (`seasons.png`); M1.10a's alpenglow sweep, the range view on a July
evening with the sun setting from +4° to −3° (`alpenglow.png`), and the same at 0°, −0.5° and −1° with M1.12a's equal bins against
the blend (`redend.png`); four April views with their material ids and each one's share of white ground by slope (`slopes`, the bare
steep faces); with `-Photo` the user's photo beside the over-forest view in its light, `main`'s, this build's and this build's with
the snowline at 2400 m (`photo.png`); Russian roulette swept on the April default view's bright snow (`rr`); and
the cost against `main` (`cost`). `snow/mountains-april.recipe.json` is the day recipe on 15 April.

```
powershell -ExecutionPolicy Bypass -File vista-views/snow/render.ps1 [-Before ..\main\Vista.App.dll] [-Photo <photo>] [-Sheets measures,valleys,maps,views,seasons,alpenglow,redend,slopes,photo,rr,cost] [-Out folder]
```

## Valley haze checks (M1.13a)

`haze/render.ps1` makes the M1.13a sheets and checks: `--atmosphere-check --segments` with and without a valley haze (its lid 450 m
above the ground sphere, 0.1/km and 0.5/km at 550 nm), up from inside the layer and down through it, with single scattering alone
too (`check`, in `haze.log`); the two shoulder views over the valleys on 15 October from dawn to noon, the valley haze auto
(`morning.png`); the misty ridges at 10:00, 12:00 and 14:00 in January, the valley floor snowy, against October (`winter.png`);
four views with the valley haze off, auto and strong (`strengths.png`); the misty ridges in autumn at six places (`places.png`);
with `-Photo` the user's photo over the Alps beside the layered ridges and the aerial view in its light (`photo.png`); with
`-Paintings` the user's paintings beside the shoulder views with a strong layer (`paintings.png`); the misty ridges through the
LUTs, the reference and the shadowed reference (`shadow.png`, the known limit); and with `-Before` (a `main` build) the cost
against `main`, auto and strong (`cost`). `haze/misty-ridges.camera.json` and `haze/across-valley.camera.json` stand on shoulders
just above the pool (about 500 m over the valley floor), looking across valleys at the ranges beyond: the painters' place.

```
powershell -ExecutionPolicy Bypass -File vista-views/haze/render.ps1 [-Before ..\main\Vista.App.dll] [-Photo <photo>] [-Paintings <a.jpg,b.jpg>] [-Sheets check,morning,winter,strengths,places,photo,paintings,shadow,cost] [-Out folder]
```

## Valley pools checks (M1.13b)

`pools/render.ps1` makes the M1.13b sheets and checks: `--pools-compare` on the misty ridges at 07:30 on 15 October with a strong
valley haze and mist pinned, the march against the reference without the terrain's shadow, with single scattering alone, without
the mist, and with the shadow against the plain and the shadowed reference, and over the mist lake at Fiordland in autumn
(`pools/mist-lake.camera.json`) a deck pinned at 600 m to see with the sun at 25° and 6° (`check`, in `pools.log`, and
`check.png`); peaks out of mist at the places whose autumn dawns saturate and at Zermatt with the mist pinned, each beside M1.13a's
flat layer for the same air (`dawn.png`); a morning over the mist lake from before sunrise to the break-up (`morning.png`);
Zermatt at the two Sunnegga webcam frames' dates and times, fog pinned, from `pools/fog-sea.camera.json` (`webcam.png`; the frames
themselves are not ours and stay local, vista-plan §10); the noise off, as chosen and stronger (`noise.png`); the misty ridges at autumn
dawn at six places (`places.png`) and the pools from above at four (`maps.png`, the dry run's `.pools.png`); and with `-Before`
(a `main` build) the cost against `main` and a misty view against the flat layer (`cost`). The views are M1.13a's shoulders
(`haze/`) and the mist lake. `pools/mist-slab.py` is the Monte Carlo of a mist slab that the mist's multiple scattering is weighted
against (`python vista-views/pools/mist-slab.py`, numpy, about two minutes).

```
powershell -ExecutionPolicy Bypass -File vista-views/pools/render.ps1 [-Before ..\main\Vista.App.dll] [-Sheets check,dawn,morning,noise,places,maps,cost] [-Out folder]
```

## Auto camera checks (M1.14)

`autocam/render.ps1` makes the M1.14 sheets and checks, every run through `--auto-camera`: one run's finalists as it ranked them,
each rendered at the cell's size with its total and squint score (`finalists.png`, `-Seed`, `-Recipe`); eight seeds at golden hour,
the default camera beside the pick for snow-capped mountains (`seeds-peak.png`, `mountains-golden.recipe.json`) and for an alpine
range (`seeds-range.png`, `autocam/range-golden.recipe.json`), each pick's focal length, near-field share and 4K triangles in its
label; with `-Paintings` the user's paintings beside two picks (`paintings.png`); and the same pick twice (`repeat`). M1.14's
follow-up, the detail budget: one run with `--detail-budget off` (M1.14's search as it was), its finalists each with `--debug detail`
(lit rock and snow the 4K frame shows finer than the terrain carries, magenta; lit meadow and forest past it, yellow) and its share
of the frame (`budget.png`); and with `-Before` (an earlier run's `-Out`) the picks this run's seeds changed, before and after
(`changed.png`). Every run's search (its candidates, the plant, the stages' times, the pick's scores and detail share) goes to
`autocam.log`. A seed the erosion cache lacks is eroded on the first render that asks for it.

```
powershell -File vista-views/autocam/render.ps1 [-Seeds 1,2,3,4,5,6,7,42] [-Seed 42] [-Recipe <recipe>] [-Paintings <a.png,b.png>] [-Before <earlier -Out>] [-Sheets finalists,seeds,paintings,repeat,budget,changed] [-Out folder]
```

## Estimate checks (M1.15)

`estimate/check.ps1` checks `--estimate` against the final itself on seven views at 3840×2160, 64 spp, seed 42: the sky camera on a
summer day (sky-heavy), the ledger's aerial and default views (terrain-heavy) and its golden-hour down-slope view (the valleys'
pools), the aerial view at summer dawn (twilight) and in mid-April (snow down to the forest), and the pools' fog-sea camera with the
mist pinned at 200 m (a march through dense mist). For each, `--estimate --noise 1` and then the final run, each a fresh process timed
on the wall from outside; it prints the whole run's range against the final's wall time, the trace's range against the final's trace,
the planned peak video memory against the final's measured peak (inside the plan's 3%), and the live ETA's worst error at 10–50% of the
trace, and writes `check.csv`. `-Rounds` repeats the set.

```
powershell -File vista-views/estimate/check.ps1 [-Views sky,aerial,default,down-slope,dawn,snow,fog-sea] [-Rounds 2] [-Spp 64] [-Width 3840] [-Out folder] [-Keep]
```

## Window checks (M1.16)

`window/check.ps1` runs `--window-check`: the real window, opened off the screen on M1's hero scene (an alpine range at golden hour,
seed 42, Zermatt on 15 July), driven step by step through the methods its controls call. The new scene's auto camera, a 960×540
preview at 256 spp refining on screen, dawn, dusk and golden hour, the camera's nudges, turns, exposure and collision (1000 km down
lifts it clear of the ground), a temperature pinned, shown in °F and reset, an implausible date and season, the material, depth and
denoised views, the snow-capped mountains as a new scene with their materials, their dawn and the sun 2° under the horizon, the saved
scene opened again, then the estimate and the 4K final. After each step it checks which stages the runs built and reused and saves a
picture of the window in `<Out>\shots`; three times it saves the scene and renders its files with a cold `--preview` process, which
must write the window's PNG and EXR byte for byte. It ends by writing the slice's two sheets (`m1-16-window.png`,
`m1-16-controls.png`) from the pictures. `-Visible` puts the window on the screen; `-NoFinal` skips the estimate and the final.

```
powershell -File vista-views/window/check.ps1 [-Out folder] [-FinalSpp 64] [-NoFinal] [-Visible]
```

The window itself: `dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --window [--recipe name.recipe.json] [--seed 42]`.

## The sign-off's look sheets (M1.17)

`signoff/render.ps1` renders the look's open choices for the user's picks, each view once and displayed under every candidate (display
options re-expose a render, never trace it again): the exposure at dusk (`dusk.png`: the hero as the sun sets through the user's dusk
series' heights, the meter following the light all the way, half way and not at all, `--adaptation`), the sunset sky
(`sunset.png`: `atmosphere/toward-sun.camera.json` at the 28-10 series' heights, one exposure for all, the display's white and 4400 K),
the white balance (`balance.png`, `photo.png` beside the user's April photo), the deep clear sky's margin (`sky.png`, `--sky-margin`),
the punchy look's shadows (`shadows.png`) and Zermatt at the Sunnegga webcam frames' dates and times (`webcam.png`). `hero.camera.json`
is the auto camera's pick on M1's hero scene (`autocam/range-golden.recipe.json`, seed 42). With `-Photos` (a local folder of the
user's dusk photos as `DD-MM-YYYY-HHMMSS.jpg`) it also writes `dusk-photos.png` and `sunset-photos.png` with the photos beside the
renders: those stay local, never committed. With `-Before` (a build from before stop 2) it writes `before-after.png`, that build's look
against Vista's camera (stop 2: 5500 K, +1.5 EV, half of dusk's fall, a 0.03 margin), one render each.

```
powershell -File vista-views/signoff/render.ps1 [-Sheets dusk,sunset,balance,photo,sky,shadows,webcam,before-after] [-Photo vista-views/sheets/m1-13-photo.png] [-Photos folder] [-Before <Vista.App.dll>] [-CellWidth 320] [-Spp 64] [-Out folder]
```

## The golden suite (M1.17)

`goldens/suite.json` lists the Vista goldens: previews at seed 42, each named for the M1 check it keeps, whose PNG and EXR are committed
beside it. `--golden-check` renders every case through one session, as `--preview` would, and compares each with its golden: identical,
within the suite's tolerance (the image's mean and its 16-pixel blocks' luminance, not pixel by pixel, so a driver that re-rolls a
path's noise still passes), different or missing. `--update` writes the renders over the goldens, deliberately, when a slice means to
move them; its log line says what each moved by. The goldens were taken at M1.17's stop 2, under Vista's camera.

```
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --golden-check [--suite vista-views/goldens/suite.json] [--only hero,sky] [--update] [--out folder]
```

## The ground's map (M1a.1)

`ground/render.ps1` maps what makes M1's hero read as a model (vista-plan §10, M1a): the hero with the four places boxed (`map.png`), and
per place a 640×360 loupe of the 4K frame beside the debug output that shows its cause (`grooves.png`, `dots.png`, `beds.png`,
`meadows.png`; the shaded places 2 EV brighter). For the grooves and the dots it adds the ground under the loupe from above at each stage
of the world (the base terrain, the incision brought back to the 8 m grid, the pipe model's passes), from `--erosion-measure --crops`,
whose log (`measure.log`) also holds the grid's own patterns at each stage (`GridArtefacts`: the share of the relief along the grid's
eight directions, and of 128 m windows rippling at 2–4 cells) and each place's distance and metres a pixel. The erosion runs again, so a
full run takes about five minutes.

```
powershell -File vista-views/ground/render.ps1 [-Sheets map,grooves,dots,beds,meadows]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --erosion-measure --recipe vista-views/autocam/range-golden.recipe.json --seeds 42 --out <dir> --crops-camera vista-views/signoff/hero.camera.json --crops "2000,850;1300,1250"
```

## Ground that gathers its water (M1a.2)

`ground/render.ps1 -Sheets m1a2` makes M1's world and M1a.2's and sets them side by side (vista-plan §10, M1a.2; since its rework also
the first M1a.2's lit loupe, `RefineCubic=1`, and the auto camera's pick on M1a.2's world): M1's with M1's grid
transfer and erosion passes (`VISTA_EROSION_SETTINGS=RefineCubic=0;RefineChannelCurves=0;RefineFaceDetail=0;DischargeSpread=0;ThermalRate=0.25`),
each eroded and measured again by `--erosion-measure` (`measure-m1.log`, `measure-m1a2.log`: the grid's patterns, the drainage, the
relief and the places' distances), then the hero at 640 px (`m1a-2-hero.png`), 640×360 loupes of the 4K frame of the lit middle range
(`m1a-2-lit.png`) and of the near face at the frame's right, 130 m off and 2 EV brighter (`m1a-2-near.png`), and the ground under the
lit middle range and the rows of dots from above at each stage (`m1a-2-ground.png`). About twelve minutes. `--erosion-measure
--incise-only` reports the incision's patterns and writes its crops without the GPU's passes (about 50 s).

```
powershell -File vista-views/ground/render.ps1 -Sheets m1a2
```
