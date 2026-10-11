# Interim art, painted in Easel

The paintings in `game/art/easel/` were made stroke by stroke with [Easel](https://pypi.org/project/easel-paint/) (`easel-paint` 0.8.0). They stand in until the designer's photographed oils arrive in `game/art/painted/`, which override them slot by slot (`docs/QUESTIONS.md` Q6).

Each script here is the painting's source. Every one is seeded, so a run at stage 99 writes the shipped PNG again, pixel for pixel. Lower stages stop partway and write `stage<N>.png`, a look at the painting so far.

```bash
pip install easel-paint
mkdir scratch && cd scratch            # the scripts write into the current folder
python ../tools/easel/beyond_doorway.py 99      # beyond_doorway.png
python ../tools/easel/desk.py 99                # desk.png
python ../tools/easel/counter.py 99             # counter.png
python ../tools/easel/shelves.py left 99        # shelf_left.png (and `right` for shelf_right.png)
python ../tools/easel/locked_shelf.py 99        # locked_shelf.png
python ../tools/easel/doorway_wall.py 99 && python ../tools/easel/cut_arch.py doorway_wall_rgb.png doorway_wall.png
python ../tools/easel/patron_default.py 99 && python ../tools/easel/cut_outline.py patron_rgb.png outline.json patron_default.png
python ../tools/easel/candle.py 99 && python ../tools/easel/cut_outline.py candle_rgb.png outline.json candle.png
```

The two `cut_` scripts give the wall a see-through arch, and the patron and the candle transparent surrounds, using the same outlines the paintings were laid in. The candle keeps the placeholder's geometry, so the flame, drawn in code at the layout's flame point, still sits on its wick. Copy the PNGs into `game/art/easel/`, then run Godot's `--import` once.

Not painted: the candle's flame (still drawn in code), the paper textures (`paper_card`, `paper_notebook`, `paper_page`, `newspaper`; the documents use the layout's paper colours), and per-patron silhouettes (`patron_<id>`; everyone uses `patron_default`).
