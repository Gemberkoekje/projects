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
| `--rotate 0\|90\|180\|270` | rotate the view. Use **180** if the table comes out upside-down *and* mirrored vs the game (a plain `--flip-y` only handles the vertical half). Display-only: `blueprint.json` and `--filter` stay in the `.dat`'s own coordinates. |
| `--filter x0 y0 x1 y1` | draw only fields whose points mostly fall in this world box. Use after a first pass (read bounds from `blueprint.json`) to drop non-table noise. Overrides the auto-filter. |
| `--no-filter` | keep every field; disables the automatic outlier drop (below). Use to see the raw dump. |

**Auto-filter (default):** not every 4-byte-aligned field is a coordinate array — some `.dat` groups
are camera parameters, sprite rectangles, or state tables whose floats, read as `(x, y)`, land
hundreds of units off and crush the real table into a sliver. By default the tool drops any field
with a point outside a **Tukey 3×IQR fence** (a handful of junk arrays can't move the quartiles), so
the table fills the canvas. It prints which groups it dropped. Pass `--no-filter` to keep them or
`--filter` to set the box by hand.

## Workflow

1. Run it on your `.dat`. Read the console summary (group count, world bounds).
2. Open `blueprint.svg`. Non-coordinate groups are auto-dropped, so it should already be table-shaped.
   If it's upside-down → `--flip-y`; if it's upside-down *and* mirrored (180° off) → `--rotate 180`
   (the "3D Pinball" launch profile already passes this). If the auto-filter clipped something you wanted (or you want the
   raw dump) → `--no-filter`, then narrow it back with an explicit `--filter` box from `blueprint.json`.
3. Open `tools/table-editor.html`: it auto-loads this `blueprint.svg` as the tracing overlay (served
   from `DatBlueprint/blueprint.svg` next to it; use **Load blueprint** to reload after a fresh run).
   The `.dat` coords aren't calibrated to the editor's `x∈[-5.5,5.5], z∈[0,24]` yet, so use the
   **opacity / scale / nudge** controls to register it over the grid, then trace walls / bumpers /
   flippers. Best served via `python tools/table-editor-server.py` — that also lets the editor load
   from and **Save → game** straight into `Pinball.App/table.json` (auto-loads the blueprint too). A
   plain `file://` open or `python -m http.server` still works for tracing, but without live save.
4. Run the table: `--table-topdown --table table.json`.

## Status / next

Container parsing is byte-exact for both 3DPB and Full Tilt dats (verified against
`partman.cpp load_records`; every variable field consumes exactly its `uint32` size, so bitmaps and
zmaps need no special-casing). Non-geometry groups (camera params, sprite rects, state tables) whose
floats aren't coordinates are now auto-dropped by the Tukey-fence filter, so the raw plot comes out
table-shaped without hand-tuning.

**Record decoding.** Most fields are flat `(x, y)` collision lists (code `600`, read at `+2` — what
the game's `loader::query_visual` does). Ramps are the exception: they store 3-D data, and reading it
as flat pairs produced a scrambled knot in the middle of the table. Those are now decoded to match the
source (`k4zmu2a/SpaceCadetPinball` `TRamp.cpp` + the `ramp_plane_type` struct in `maths.h`):
code `1300` is a list of ramp *planes* (each a 13-float `ramp_plane_type`: a `BallCollisionOffset` xyz
— the Z is the "faux-3D" height — then triangle `V1/V2/V3`, two gravity angles, and a field-force
vector), drawn as the `V1→V2→V3` triangle the game builds its collision edges from; codes `1301/1302/1303`
are the ramp wall segments. Each ramp plane becomes its own triangle polyline, so a ramp is ~18 small
triangles rather than one field.

**Calibration block.** `blueprint.json` carries a `calibration` object the editor uses to place the
overlay at true proportions and size the ball:
- `ballRadius` — the real ball radius in `.dat` units, read from `query_float_attribute(group, 0, 500)`
  (see `TBall.cpp`: `Radius = attr 500`). For Space Cadet it's `0.30`.
- `table` — the outer collision extent (the `table` group's code-600 list), **16 × 29** `.dat` units,
  rotated into the same frame as the SVG.
- `svg` — the world rect the rendered `blueprint.svg` canvas spans plus its pixel size, so the editor
  knows where the table sits inside the image.

When a `table` group is found, the plot is **framed on the table extent** (±2% pad) rather than the
drawn-geometry percentile — so the full outer wall shows and `svg` ≈ `table`, letting the overlay map
1:1 onto the playfield instead of clipped/off-centre.

The editor's playfield is sized to the real table's **16:29 aspect** (`x∈[-5.5,5.5], z∈[0,19.94]`), so
the `table` extent **contain-fits and fills it** at 1 `.dat` unit = 0.6875 editor units; the ball size
then falls out as `ballRadius × fit-scale` (0.30 × 0.6875 = **0.206**), which **Ball → original size**
applies. (The blueprint always renders undistorted; if the playfield aspect didn't match, it would just
letterbox — the earlier `z∈[0,24]` field left a vertical gap.)

**Still not calibrated:** which *surviving* groups are collision walls vs. sprites for the remaining
component types. Once pinned, this could emit `table.json` directly (the "extract" posture) instead of
a trace-over overlay.
