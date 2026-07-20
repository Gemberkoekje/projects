# DatBlueprint — top-down tracing overlay from `pinball.dat`

Turns a 3D Pinball / Full Tilt `PARTOUT(4.0)RESOURCE` data file into a **top-down blueprint**
(`blueprint.svg`) you drop into `tools/table-editor.html` as the reference overlay, plus a full
`blueprint.json` dump for calibration.

## Why

The goal screenshot is a baked 2.5-D **perspective** render — you can't read accurate top-down
`(x, z)` off it (that's the wall we kept hitting). But the original **simulation is 2-D top-down**,
and its collision geometry sits in the `.dat` as plain float arrays. This tool reads those and plots
them flat — a real top-down blueprint to trace, instead of guessing metric coords off foreshortened
pixels.

## Clean-room / legal

- Reads **only** the file you pass on the command line. Contains, embeds, and redistributes **no**
  game data. Point it at **your own** `pinball.dat`.
- Recommended posture (matches `pinball-plan.md` §2/§10): use the blueprint as a **tracing
  reference** in the editor — the geometry you export stays your own authored `table.json`, not a
  copy of Microsoft's float arrays.

## Run

```bash
dotnet run --project tools/DatBlueprint -- /path/to/pinball.dat
# writes blueprint.svg + blueprint.json next to the .dat (or pass an output dir as the 2nd arg)
```

Options:

| flag | meaning |
|------|---------|
| `--offset N` | floats skipped at the head of each array before pairing (default **2** — the original exposes collision floats at `FloatArr+2`). Try `--offset 0` if the plot looks wrong. |
| `--min-points N` | minimum coordinate pairs for a field to be drawn (default 3) — filters out short sprite-position arrays. |
| `--flip-y` | mirror vertically if the plot is upside-down vs the game. |
| `--filter x0 y0 x1 y1` | draw only fields whose points mostly fall in this world box. Use after a first pass (read bounds from `blueprint.json`) to drop non-table noise. |

## Workflow

1. Run it on your `.dat`. Read the console summary (group count, world bounds).
2. Open `blueprint.svg`. If it's upside-down → `--flip-y`. If it's a hairball → note the table's
   real bounds from `blueprint.json` and re-run with `--filter`.
3. In `tools/table-editor.html`: **Load reference image → blueprint.svg**, opacity ~50%, and trace
   walls / bumpers / flippers over it. Export `table.json` as usual.
4. Run the table: `--table-topdown --table table.json`.

## Status / next

Container parsing is byte-exact for both 3DPB and Full Tilt dats (verified against
`partman.cpp load_records`; every variable field consumes exactly its `uint32` size, so bitmaps and
zmaps need no special-casing). **Not yet calibrated:** which `.dat` groups are collision walls vs.
sprites, the exact float offset per component type, and the affine map from `.dat` table coordinates
into the editor's `x∈[-5.5,5.5], z∈[0,24]` space. Those get pinned from the first real `blueprint.json`
— at which point this can optionally emit `table.json` directly (the "extract" posture) instead of a
trace-over overlay.
