# Easel evidence: The Curator's interim paintings

`$SCRATCH` is this session's scratch folder: `/tmp/claude-0/-home-user-projects/effc84d7-d7e9-5d60-848f-887f6f888335/scratchpad`.

## Session

- Session id: `effc84d7-d7e9-5d60-848f-887f6f888335` (Claude Code transcript id). claude.ai session: `session_01Bts8nXN3Fyu2BnqVXw9iKw`.
- Model: not written in this file. This repository keeps model identifiers out of committed files.
- Reasoning effort: max.
- Start: 2026-10-10T23:10:35Z (first transcript entry). End: 2026-10-11T14:27:06Z (this folder written).
- Painting work: 2026-10-11T01:52:59Z (first painting run, transcript L2867) to 2026-10-11T04:12Z (candle cut-out written).
- easel-paint: 0.8.0. `pip show easel-paint`: "A headless painting engine for AI agents: brushes, paint load, wet blending, canvas texture."
- Installation, 2026-10-11T00:36:00Z (L1089): `python3 -m venv /opt/tools/easel/venv`, then `/opt/tools/easel/venv/bin/pip install --quiet "easel-paint[mcp]"`. Checked with `pip show easel-paint` (L1104). PyPI metadata for easel-paint was queried before installing (L221).
- Every script ran with `/opt/tools/easel/venv/bin/python -I`.

## Easel documents read, as far as the transcript shows

| Document | Parts read | Transcript |
|---|---|---|
| `easel --help` | first 30 lines | L1089 |
| `easel guide` (119 lines), saved as `scratch/easel-cli/easel_guide.txt` | all | L1182 (`head -150`), L1315 (`cat`) |
| `easel guide --full` (767 lines), saved as `scratch/easel-cli/easel_guide_full.txt`; byte-identical to `easel/docs/PAINTER.md` | lines 120–330, 330–560 and 560–767; lines 150–182 and 640–665 again; lines 1–119 by grep only | L1327, L2825, L2831, L3878 |
| `easel guide --full` | grep: alpha, transparen, export, save(, resolution, png | L1323 |
| `easel guide --full` | grep: headings, polygon(, blob(, ellipse, ring(, arc(, shapes | L3872 |
| `easel guide --full` | grep: ribbon, hull, union, s.px, annulus, difference, subtract | L3890 |
| `easel guide --full` | grep: `def look`, scale | L4831 |
| `easel/docs/RECIPES.md` (1286 lines) | heading list; lines 35–92, 271–301, 572–620, 735–787, 852–895 | L2844, L2848, L2881 |
| `easel/docs/REFERENCE.md` (841 lines) | lines matching "direction" | L2885 |
| `easel/session.py` | `def block_in`, `def scumble`, `def sweep`, `def cover`, `def glaze`, with 3 lines after each | L2885 |
| `easel` module | `dir(easel)`; `inspect.signature` of `ribbon`, `polygon`, `Session.block_in`, `Session.stroke` | L3890 |
| `easel` module | `easel.PIGMENTS` | L3904 |
| `easel` module | `easel.PRESSURE_PROFILES` | L3937 |
| `easel` module | `inspect.signature(easel.catmull_rom)` and its docstring | L4003 |
| `easel/docs/` listing | file names | L1323, L2838 |

Not opened: `CALIBRATION.md`, `DIAGNOSIS.md`, `PAINTING.md`. `PAINTER.md` was not opened directly; its text was read through `easel guide --full`.

## Calls

- `s.checklist()`: yes. Every painting script ends with `print(s.checklist())` at stage 99.
  - Transcript lines where those scripts were written or run: L2867, L2903, L2915, L2964, L2979, L3020, L3041, L3904, L3937, L3968, L4845.
  - It also ran in the re-runs whose output is in `scratch/repro`, `scratch/repaint`, `scratch/cand` and `scratch/review-agent/logs`.
- `s.report()`: no.
- `cost()`: no.
- `easel run`: no.
- `easel check`: no.
- Transcript line L5635 (2026-10-11T14:23:27Z) contains these five names as search terms in a search of the transcript, not as calls.
- Easel calls in the scripts (`scratch/art/*/paint.py`, `scratch/art/beyond/p0.py`, `scratch/art/beyond/swatch.py`, `the-curator/tools/easel/candle.py`), with counts: `p.mix` 94, `p.at_value` 62, `s.stroke` 52, `s.block_in` 35, `s.look` 12, `s.plan` 8, `s.export` 8, `s.checklist` 8, `s.budget_line` 8, `s.pencil` 2, `p.value_of` 2, `p.hex` 2, `s.smudge` 1, `p.chroma_of` 1.
- Imports: `Session`, `span`, `polygon`, `blob`, `ellipse`, `cell`, `region`, `ribbon`, `Region`.

## Secrets search

- Scanned all 33 text files and the metadata of all 76 PNG files for:
  - GitHub tokens, `sk-` keys, AWS keys, JWTs;
  - signed-URL parameters and bearer or authorization headers;
  - password, secret and API-key assignments;
  - URLs and email addresses.
- Redacted: none.
- Found and left in place: `scratch/easel-cli/easel_guide_full.txt` contains `https://github.com/Gemberkoekje/EaselAPI/tree/main/paintings`, part of the published easel-paint 0.8.0 documentation (`PAINTER.md`).
- No PNG carries text metadata.

## Not included, or not found

- `transcript/`: not included. The session transcript (`~/.claude/projects/-home-user-projects/effc84d7-d7e9-5d60-848f-887f6f888335.jsonl`, 59 MB) and four subagent transcripts (`.../effc84d7-d7e9-5d60-848f-887f6f888335/subagents/agent-*.jsonl`) exist on disk and were not copied.
- `versions/`: not created. No on-disk copy of a `tools/easel/` script differs from a committed version.
  - The working copies in `scratch/art` (`paint.py`, `cut_arch.py`, `cut_patron.py`) are byte-identical to commit `e48ea6f5`.
  - The final-review agent's copies (`$SCRATCH/review4/easel_scripts`, `clean/`, `probe/`) are identical to the same commit.
  - Intermediate versions were edited in place and no longer exist: the shelves, wall, cabinet and patron scripts before their colour, value and mass changes, and `candle.py` before its first commit.
- Earlier stage images: each run overwrote `stage<N>.png`, so only the last run of each stage survives.
- Crop scripts: the crops in `scratch/crops` were made with inline commands, and no script files exist.
- Transcript line numbers for the re-runs (`repro`, `repaint`, `cand`) were not collected.

## Files (109 files, 26845624 bytes, not counting this manifest)

| File | Bytes | Source | What |
|---|---|---|---|
| `scratch/art/beyond/beyond_doorway.png` | 297140 | `$SCRATCH/art/beyond/beyond_doorway.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/p0.py` | 946 | `$SCRATCH/art/beyond/p0.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/p0_draw.png` | 124615 | `$SCRATCH/art/beyond/p0_draw.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/paint.py` | 5189 | `$SCRATCH/art/beyond/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/stage3.png` | 307960 | `$SCRATCH/art/beyond/stage3.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/stage3_values.png` | 159642 | `$SCRATCH/art/beyond/stage3_values.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/stage6.png` | 297206 | `$SCRATCH/art/beyond/stage6.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/stage6_values.png` | 141448 | `$SCRATCH/art/beyond/stage6_values.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/stage99.png` | 297206 | `$SCRATCH/art/beyond/stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/stage99_values.png` | 141448 | `$SCRATCH/art/beyond/stage99_values.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/swatch.png` | 50916 | `$SCRATCH/art/beyond/swatch.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/beyond/swatch.py` | 1104 | `$SCRATCH/art/beyond/swatch.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/cabinet/locked_shelf.png` | 149003 | `$SCRATCH/art/cabinet/locked_shelf.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/cabinet/paint.py` | 4434 | `$SCRATCH/art/cabinet/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/cabinet/stage5.png` | 147116 | `$SCRATCH/art/cabinet/stage5.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/cabinet/stage99.png` | 148952 | `$SCRATCH/art/cabinet/stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/counter/counter.png` | 476766 | `$SCRATCH/art/counter/counter.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/counter/paint.py` | 3195 | `$SCRATCH/art/counter/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/counter/stage3.png` | 194975 | `$SCRATCH/art/counter/stage3.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/counter/stage99.png` | 194975 | `$SCRATCH/art/counter/stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/cut_arch.py` | 636 | `$SCRATCH/art/cut_arch.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/cut_patron.py` | 620 | `$SCRATCH/art/cut_patron.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/desk/desk.png` | 1187830 | `$SCRATCH/art/desk/desk.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/desk/paint.py` | 4480 | `$SCRATCH/art/desk/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/desk/stage6.png` | 474102 | `$SCRATCH/art/desk/stage6.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/desk/stage99.png` | 474102 | `$SCRATCH/art/desk/stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/outline.json` | 5799 | `$SCRATCH/art/patron/outline.json` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/paint.py` | 4413 | `$SCRATCH/art/patron/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/patron_default.png` | 135562 | `$SCRATCH/art/patron/patron_default.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/patron_rgb.png` | 115378 | `$SCRATCH/art/patron/patron_rgb.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/preview.png` | 78197 | `$SCRATCH/art/patron/preview.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/stage3.png` | 162380 | `$SCRATCH/art/patron/stage3.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/patron/stage99.png` | 115292 | `$SCRATCH/art/patron/stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/left_stage5.png` | 395053 | `$SCRATCH/art/shelves/left_stage5.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/left_stage99.png` | 395053 | `$SCRATCH/art/shelves/left_stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/paint.py` | 5805 | `$SCRATCH/art/shelves/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/right_stage5.png` | 372603 | `$SCRATCH/art/shelves/right_stage5.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/right_stage99.png` | 372603 | `$SCRATCH/art/shelves/right_stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/shelf_left.png` | 395085 | `$SCRATCH/art/shelves/shelf_left.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/shelves/shelf_right.png` | 372663 | `$SCRATCH/art/shelves/shelf_right.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/doorway_wall.png` | 1142444 | `$SCRATCH/art/wall/doorway_wall.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/doorway_wall_rgb.png` | 1008846 | `$SCRATCH/art/wall/doorway_wall_rgb.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/paint.py` | 5227 | `$SCRATCH/art/wall/paint.py` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/stage4.png` | 392351 | `$SCRATCH/art/wall/stage4.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/stage4_values.png` | 206515 | `$SCRATCH/art/wall/stage4_values.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/stage99.png` | 392351 | `$SCRATCH/art/wall/stage99.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/art/wall/stage99_values.png` | 206515 | `$SCRATCH/art/wall/stage99_values.png` | painting working folder (scripts, stage looks, exports) |
| `scratch/cand/candle.png` | 35514 | `$SCRATCH/cand/candle.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/candle_rgb.png` | 30946 | `$SCRATCH/cand/candle_rgb.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/outline.json` | 1055 | `$SCRATCH/cand/outline.json` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/p.log` | 3411 | `$SCRATCH/cand/p.log` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/patron_default.png` | 135562 | `$SCRATCH/cand/patron_default.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/patron_rgb.png` | 115378 | `$SCRATCH/cand/patron_rgb.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/rgb_big.png` | 41580 | `$SCRATCH/cand/rgb_big.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/stage4.png` | 29777 | `$SCRATCH/cand/stage4.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/stage4_big.png` | 40117 | `$SCRATCH/cand/stage4_big.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/cand/stage99.png` | 30920 | `$SCRATCH/cand/stage99.png` | candle painting and the patron re-run through cut_outline.py, 2026-10-11T04:10–04:12Z |
| `scratch/crops/candle_down.png` | 101721 | `$SCRATCH/review/candle_down.png` | crops made to check the desk knots and the candle |
| `scratch/crops/candle_dusk.png` | 100320 | `$SCRATCH/review/candle_dusk.png` | crops made to check the desk knots and the candle |
| `scratch/crops/candles.png` | 196208 | `$SCRATCH/review/candles.png` | crops made to check the desk knots and the candle |
| `scratch/crops/knot.png` | 51190 | `$SCRATCH/review/knot.png` | crops made to check the desk knots and the candle |
| `scratch/crops/knots2.png` | 165835 | `$SCRATCH/review/knots2.png` | crops made to check the desk knots and the candle |
| `scratch/easel-cli/easel_guide.txt` | 8539 | `$SCRATCH/easel_guide.txt` | output of `easel guide` and `easel guide --full` (transcript L1315) |
| `scratch/easel-cli/easel_guide_full.txt` | 41887 | `$SCRATCH/easel_guide_full.txt` | output of `easel guide` and `easel guide --full` (transcript L1315) |
| `scratch/repaint/beyond_doorway.log` | 3130 | `$SCRATCH/repaint/beyond_doorway.log` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/beyond_doorway.png` | 297260 | `$SCRATCH/repaint/beyond_doorway.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/counter.log` | 2170 | `$SCRATCH/repaint/counter.log` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/counter.png` | 476680 | `$SCRATCH/repaint/counter.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/desk.log` | 2150 | `$SCRATCH/repaint/desk.log` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/desk.png` | 1188021 | `$SCRATCH/repaint/desk.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/doorway_wall.log` | 1545 | `$SCRATCH/repaint/doorway_wall.log` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/doorway_wall.png` | 1142392 | `$SCRATCH/repaint/doorway_wall.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/doorway_wall_rgb.png` | 1008798 | `$SCRATCH/repaint/doorway_wall_rgb.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/stage99.png` | 392351 | `$SCRATCH/repaint/stage99.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repaint/stage99_values.png` | 206515 | `$SCRATCH/repaint/stage99_values.png` | repaint of four paintings after the final review, 2026-10-11T03:53–03:56Z |
| `scratch/repro/beyond.log` | 3192 | `$SCRATCH/repro/beyond.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/beyond_doorway.png` | 297140 | `$SCRATCH/repro/beyond_doorway.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/cabinet.log` | 1549 | `$SCRATCH/repro/cabinet.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/counter.log` | 2216 | `$SCRATCH/repro/counter.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/counter.png` | 476766 | `$SCRATCH/repro/counter.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/desk.log` | 2199 | `$SCRATCH/repro/desk.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/desk.png` | 1187830 | `$SCRATCH/repro/desk.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/doorway_wall.png` | 1142444 | `$SCRATCH/repro/doorway_wall.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/doorway_wall_rgb.png` | 1008846 | `$SCRATCH/repro/doorway_wall_rgb.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/left_stage99.png` | 395053 | `$SCRATCH/repro/left_stage99.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/locked_shelf.png` | 149003 | `$SCRATCH/repro/locked_shelf.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/outline.json` | 5799 | `$SCRATCH/repro/outline.json` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/patron.log` | 3522 | `$SCRATCH/repro/patron.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/patron_default.png` | 135562 | `$SCRATCH/repro/patron_default.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/patron_rgb.png` | 115378 | `$SCRATCH/repro/patron_rgb.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/right_stage99.png` | 372603 | `$SCRATCH/repro/right_stage99.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/shelf_left.png` | 395085 | `$SCRATCH/repro/shelf_left.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/shelf_right.png` | 372663 | `$SCRATCH/repro/shelf_right.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/sl.log` | 1421 | `$SCRATCH/repro/sl.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/sr.log` | 1430 | `$SCRATCH/repro/sr.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/stage99.png` | 115292 | `$SCRATCH/repro/stage99.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/stage99_values.png` | 206515 | `$SCRATCH/repro/stage99_values.png` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/repro/wall.log` | 1538 | `$SCRATCH/repro/wall.log` | reproduction re-run of all eight paintings, 2026-10-11T03:02–03:11Z |
| `scratch/review-agent/easel_repro/beyond_doorway.png` | 297140 | `$SCRATCH/review4/easel_repro/beyond_doorway.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/doorway_wall.png` | 1142444 | `$SCRATCH/review4/easel_repro/doorway_wall.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/doorway_wall_rgb.png` | 1008846 | `$SCRATCH/review4/easel_repro/doorway_wall_rgb.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/outline.json` | 5799 | `$SCRATCH/review4/easel_repro/outline.json` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/patron_default.png` | 135562 | `$SCRATCH/review4/easel_repro/patron_default.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/patron_rgb.png` | 115378 | `$SCRATCH/review4/easel_repro/patron_rgb.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/stage99.png` | 115292 | `$SCRATCH/review4/easel_repro/stage99.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/easel_repro/stage99_values.png` | 206515 | `$SCRATCH/review4/easel_repro/stage99_values.png` | final-review agent's re-run of beyond_doorway, doorway_wall + cut_arch, patron_default + cut_patron |
| `scratch/review-agent/logs/easel_beyond.log` | 3294 | `$SCRATCH/review4/logs/easel_beyond.log` | final-review agent's logs of that re-run |
| `scratch/review-agent/logs/easel_patron.log` | 3621 | `$SCRATCH/review4/logs/easel_patron.log` | final-review agent's logs of that re-run |
| `scratch/review-agent/logs/easel_wall.log` | 1574 | `$SCRATCH/review4/logs/easel_wall.log` | final-review agent's logs of that re-run |
