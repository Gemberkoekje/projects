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
