# Contact sheets

Sheets that back a decision in `vista-plan.md` §10, kept where the plan can point at them. The scripts that make them write into
`%TEMP%`; a sheet lands here when a slice's status cites it.

| Sheet | Slice | What it shows | Made by |
|---|---|---|---|
| `m1-10-far-field.png` | M1.10 | The far field traced as a height field, beside the 1 px mesh and their per-pixel difference (white = 25% of the mean luminance), on the aerial, layered-ridges and range views; 4K, 128 spp, golden hour, seed 42, box-filtered to 1280 | `vista-views/heightfield/render.ps1` (on the branch `claude/vista-m1-10-heightfield`) |
| `m1-10-convergence.png` | M1.10 | The same three views: the mesh's difference from the height field at 2, 1 and 0.5 px, shrinking with the error target, beside the noise floor (the 1 px mesh on two random streams); 640 px cells | the same run's difference images, tiled with `--sheet-from` |
