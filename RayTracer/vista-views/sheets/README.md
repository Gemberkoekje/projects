# Contact sheets

Sheets that back a decision in `vista-plan.md` §10, kept where the plan can point at them. The scripts that make them write into
`%TEMP%`; a sheet lands here when a slice's status cites it.

| Sheet | Slice | What it shows | Made by |
|---|---|---|---|
| `m1-1-before-after.png` | M1.1 | The four measurement views (sky, aerial, default, down the slope) from a `main` build (11e489b, left) and the M1.1 branch (b1df1da, right), byte-identical pair by pair; labelled by build from the sidecars; 320×180, 64 spp, seed 42, the M0 look | `--preview` of each view with both builds, then `--sheet-from main/x.png branch/x.png … --columns 2` |
| `m1-1-seeds.png` | M1.1 | Seeds 1–4 of the golden-hour recipe from the default camera, the four-seed check; the M0 look and terrain | `--sheet --recipe vista-views/mountains-golden.recipe.json --vary seed=1,2,3,4` |
| `m1-1-time-of-day.png` | M1.1 | Dawn, day, golden hour, dusk and night of the day recipe, seed 42, from the default camera: the time-of-day check, one world for all five cells; the M0 placeholder sun | `--sheet --recipe vista-views/mountains-day.recipe.json --seed 42 --vary time=dawn,day,goldenHour,dusk,night --debug normal` |
| `m1-1-time-of-day-normals.png` | M1.1 | The same run's normal-map sheet: a debug output gets a sheet of its own | the same call |
| `m1-10-far-field.png` | M1.10 | The far field traced as a height field, beside the 1 px mesh and their per-pixel difference (white = 25% of the mean luminance), on the aerial, layered-ridges and range views; 4K, 128 spp, golden hour, seed 42, box-filtered to 1280 | `vista-views/heightfield/render.ps1` (on the branch `claude/vista-m1-10-heightfield`) |
| `m1-10-convergence.png` | M1.10 | The same three views: the mesh's difference from the height field at 2, 1 and 0.5 px, shrinking with the error target, beside the noise floor (the 1 px mesh on two random streams); 640 px cells | the same run's difference images, tiled with `--sheet-from` |
