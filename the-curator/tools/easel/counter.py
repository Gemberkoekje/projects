import sys
from easel import Session, span, blob, polygon, ellipse

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
s = Session(1920, 360, texture="linen", ground="umber_wash", seed=41, budget=120, out_dir=".")
p = s.palette
oak = p.mix(p.mix("burnt_umber", "burnt_sienna", 0.3), p.mix("burnt_umber", "titanium_white", 0.5), 0.25)
p["oak"] = p.at_value(oak, 0.30)
p["oak_lit"] = p.at_value(p.mix(oak, "yellow_ochre", 0.2), 0.46)
p["oak_dark"] = p.at_value(p.mix(oak, "ultramarine", 0.15), 0.20)
p["seam"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.3), 0.14)

lip = polygon([(-0.02, -0.02), (1.02, -0.02), (1.02, 0.075), (-0.02, 0.08)])
board1 = polygon([(-0.02, 0.10), (1.02, 0.095), (1.02, 0.55), (-0.02, 0.555)])
board2 = polygon([(-0.02, 0.575), (1.02, 0.57), (1.02, 1.02), (-0.02, 1.02)])

s.plan(why="the counter between the curator and the patron: dark oak, its far lip worn pale by elbows",
       values={span("A1", "H1"): 0.44, span("C3", "F4"): 0.30, span("A8", "H8"): 0.22},
       lightest=span("A1", "H1"), subject_share=0.15, bands="subject")

# 1. The dark the boards sit in, then the boards and the lip, each along its length.
s.block_in(polygon([(-0.02, -0.02), (1.02, -0.02), (1.02, 1.02), (-0.02, 1.02)]), "flat", "seam", density=1.0, size=0.1, solid=True, direction="horizontal")
s.block_in(lip, "bristle", "oak_lit", density=0.95, size=0.05, direction="horizontal", edge="hard")
for i, board in enumerate((board1, board2)):
    shade = p.at_value(p.mix(oak, "burnt_umber", 0.15 + 0.1 * i), 0.31 - 0.03 * i)
    s.block_in(board, "bristle", shade, density=0.95, size=0.08, direction="horizontal", edge="hard")
    s.block_in(board.inset(0.004), "bristle", shade, density=0.9, size=0.06, direction=1.2, opacity=0.8, edge="hard")
if STAGE >= 2:
    # 2. Grain wandering along each board; a lighter run where hands pass, darker toward the near edge.
    for i, (y, x0, x1, c, w) in enumerate([
        (0.20, 0.02, 0.58, "oak_lit", 0.03), (0.30, 0.45, 0.98, "oak_dark", 0.035),
        (0.42, 0.10, 0.66, "oak_dark", 0.03), (0.66, 0.30, 0.92, "oak_lit", 0.025),
        (0.78, -0.02, 0.44, "oak_dark", 0.035), (0.90, 0.52, 1.02, "oak_dark", 0.04)]):
        s.stroke([(x0, y), ((x0 + x1) / 2, y + (0.012 if i % 2 else -0.01)), (x1, y - 0.004)], "bristle", c,
                 size=w * 1.6, opacity=0.4, load=0.7, pressure="taper")
    s.block_in(blob((0.5, 0.25), 0.36, 0.12, wobble=0.3, seed=3), "bristle", "oak_lit", density=0.45, size=0.08, direction="horizontal", opacity=0.25)
    s.stroke([(-0.02, 0.97), (0.5, 0.965), (1.02, 0.975)], "bristle", "oak_dark", size=0.06, opacity=0.5, load=0.8)
if STAGE >= 3:
    # 3. The lip's front edge catches the light, found in the middle and lost toward the ends.
    s.stroke([(0.18, 0.078), (0.5, 0.077), (0.84, 0.079)], "round_hard", p.at_value("oak_lit", 0.58), size=0.004, opacity=0.7, pressure="swell", note="subject")
s.look(path=f"stage{STAGE}.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([(0.965, 0.93), (0.975, 0.91), (0.985, 0.925)], "liner", "seam", size=0.002, opacity=0.25, note="signature")
    print(s.checklist())
    s.export("counter.png")
