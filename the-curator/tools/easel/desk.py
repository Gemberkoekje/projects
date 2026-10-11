import sys
from easel import Session, span, blob, polygon, ellipse

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
s = Session(1920, 720, texture="linen", ground="toned_warm_grey", seed=31, budget=170, out_dir=".")
p = s.palette
oak = p.mix(p.mix("yellow_ochre", "burnt_sienna", 0.25), p.mix("burnt_umber", "titanium_white", 0.5), 0.35)
p["oak"] = p.at_value(p.mix(oak, "burnt_umber", 0.25), 0.50)
p["oak_light"] = p.at_value(p.mix(oak, "titanium_white", 0.25), 0.62)
p["oak_dark"] = p.at_value(p.mix(oak, "burnt_umber", 0.55), 0.36)
p["seam"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.2), 0.18)
p["knot"] = p.at_value(p.mix("burnt_umber", "burnt_sienna", 0.4), 0.26)

# Planks run the width of the desk; their seams are where one plank's paint stops and the next begins.
edges = [-0.02, 0.215, 0.43, 0.60, 0.79, 1.02]
planks = [polygon([(-0.02, top + 0.006), (1.02, top + 0.004), (1.02, bottom - 0.006), (-0.02, bottom - 0.004)])
          for top, bottom in zip(edges, edges[1:])]

s.plan(why="the curator's desk, oak worn pale where the work is done, under the documents",
       values={span("C3", "F5"): 0.60, span("A1", "H1"): 0.40, span("A8", "H8"): 0.44},
       lightest=span("C3", "F5"), subject_share=0.15, bands="subject")

# 1. The seams first, as the dark the planks sit in.
s.block_in(polygon([(-0.02, -0.02), (1.02, -0.02), (1.02, 1.02), (-0.02, 1.02)]), "flat", "seam", density=1.0, size=0.12, solid=True, direction="horizontal")
# 2. Each plank its own shade of oak, along its own length.
for i, plank in enumerate(planks):
    shade = p.at_value(p.mix(oak, "burnt_umber", 0.2 + 0.08 * (i % 3)), 0.47 + 0.035 * ((i * 2) % 3))
    s.block_in(plank, "bristle", shade, density=0.95, size=0.07, direction="horizontal", edge="hard")
    # A second pass a hair off the first fills the comb, so the dark beneath shows only at the seams.
    s.block_in(plank.inset(0.004), "bristle", shade, density=0.9, size=0.05, direction=1.5, opacity=0.8, edge="hard")
if STAGE >= 3:
    # 3. Grain: long passes a shade darker and lighter, broken and wandering, never the same twice.
    for i, (y, x0, x1, c, w) in enumerate([
        (0.08, 0.05, 0.62, "oak_dark", 0.018), (0.15, 0.40, 0.98, "oak_light", 0.014),
        (0.29, -0.02, 0.45, "oak_light", 0.016), (0.36, 0.55, 1.02, "oak_dark", 0.02),
        (0.50, 0.12, 0.70, "oak_dark", 0.015), (0.55, 0.62, 0.95, "oak_light", 0.012),
        (0.67, -0.02, 0.38, "oak_dark", 0.02), (0.73, 0.30, 0.88, "oak_light", 0.015),
        (0.85, 0.08, 0.55, "oak_light", 0.016), (0.93, 0.48, 1.02, "oak_dark", 0.018)]):
        mid = (x0 + x1) / 2
        s.stroke([(x0, y), (mid, y + 0.006 * (1 if i % 2 else -1)), (x1, y - 0.003)], "bristle", c,
                 size=w * 2.2, opacity=0.45, load=0.7, pressure="taper")
if STAGE >= 4:
    # 4. Worn pale where the curator works, darker toward the far edge and the corners.
    s.block_in(blob((0.48, 0.55), 0.34, 0.30, wobble=0.3, seed=5), "bristle", "oak_light", density=0.45, size=0.12, direction="horizontal", opacity=0.28)
    for pts in ([(-0.02, 0.04), (0.5, 0.05), (1.02, 0.035)], [(-0.02, 0.97), (0.4, 0.965), (1.02, 0.975)]):
        s.stroke(pts, "bristle", "oak_dark", size=0.07, opacity=0.4, load=0.8)
if STAGE >= 5:
    # 5. Two knots drawn out along the grain, each its own: a tight dark one, a long pale-ringed one.
    s.stroke([(0.215, 0.33), (0.23, 0.328), (0.248, 0.331)], "round_hard", "knot", size=0.012, opacity=0.7, pressure="swell", tip_wobble=0.6)
    # The grain bends round each knot: a thin line above and below, not a band with square ends.
    s.stroke([(0.16, 0.318), (0.205, 0.312), (0.232, 0.309), (0.26, 0.312), (0.30, 0.318)], "round_hard", "oak_dark", size=0.003, opacity=0.45, pressure="taper")
    s.stroke([(0.17, 0.343), (0.21, 0.349), (0.232, 0.352), (0.255, 0.349), (0.29, 0.343)], "round_hard", "oak_dark", size=0.003, opacity=0.35, pressure="taper")
    s.stroke([(0.74, 0.69), (0.77, 0.688), (0.80, 0.692)], "round_hard", p.mix("knot", "oak", 0.4), size=0.014, opacity=0.55, pressure="swell", tip_wobble=0.6)
    s.stroke([(0.69, 0.676), (0.74, 0.671), (0.77, 0.669), (0.80, 0.671), (0.85, 0.677)], "round_hard", "oak_light", size=0.003, opacity=0.45, pressure="taper")
    s.stroke([(0.70, 0.703), (0.745, 0.709), (0.77, 0.711), (0.795, 0.709), (0.84, 0.702)], "round_hard", "oak_light", size=0.003, opacity=0.35, pressure="taper")
if STAGE >= 6:
    # 6. A few highlights on the plank edges nearest the candle (right), lost toward the left.
    for y in (0.218, 0.433, 0.603):
        s.stroke([(0.55, y), (0.80, y + 0.001), (1.0, y)], "round_hard", "oak_light", size=0.004, opacity=0.6, pressure="lift_off", note="subject")
s.look(path=f"stage{STAGE}.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([(0.965, 0.95), (0.975, 0.94), (0.985, 0.948)], "liner", "seam", size=0.002, opacity=0.25, note="signature")
    print(s.checklist())
    s.export("desk.png")
