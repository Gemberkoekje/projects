import random, sys
from easel import Session, span, polygon

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
W, H = 300, 460
rng = random.Random(31)
s = Session(W, H, texture="linen", ground="umber_wash", seed=31, budget=240, out_dir=".")
p = s.palette

def n(x, y):
    return (x / W, y / H)

def box(x0, y0, x1, y1, wob=1.5):
    """A rectangle drawn by hand: corners a little off true."""
    j = lambda: rng.uniform(-wob, wob)
    return polygon([n(x0 + j(), y0 + j()), n(x1 + j(), y0 + j()), n(x1 + j(), y1 + j()), n(x0 + j(), y1 + j())])

p["carcass"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.12), 0.16)
p["frame"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.1), 0.18)
p["frame_lit"] = p.at_value(p.mix(p.mix("burnt_umber", "burnt_sienna", 0.3), "yellow_ochre", 0.2), 0.30)
p["inside"] = "#17100e"          # under the mixes' reach: a warm near-black of its own
p["iron"] = "#1d1b21"
p["iron_lit"] = p.at_value(p.mix(p.mix("ultramarine", "burnt_umber", 0.5), "titanium_white", 0.3), 0.38)
p["brass"] = p.at_value(p.mix("yellow_ochre", "burnt_sienna", 0.3), 0.50)
p["brass_lit"] = p.at_value(p.mix("yellow_ochre", "titanium_white", 0.35), 0.72)

s.plan(why="a locked case, the grille catching a little light, the brass lock its one bright note",
       values={span("D4", "E5"): 0.6, span("B2", "G7"): 0.12},
       lightest=span("D4", "E5"), subject_share=0.25, bands="subject")

FX0, FY0, FX1, FY1 = 30, 40, 270, 430           # the door's opening, inside the frame
# 1. The case and its frame, then the dark inside behind the grille.
s.block_in(box(-6, -6, W + 6, H + 6, 0), "bristle", "frame", density=1.0, size=0.08, direction="vertical")
s.block_in(box(FX0, FY0, FX1, FY1), "flat", "inside", density=1.0, size=0.05, solid=True, direction="vertical", edge="hard")
if STAGE >= 2:
    # 2. Locked books in the dark: low, dull spines on two shelves, barely there.
    for base, ceiling in ((225, 60), (415, 245)):
        s.block_in(box(FX0 + 2, base, FX1 - 2, base + 10, 0.5), "flat", "carcass", density=1.0, size=0.02, edge="hard", direction="horizontal")
        x = FX0 + 6
        while x < FX1 - 14:
            w = rng.uniform(10, 22)
            h = (base - ceiling) * rng.uniform(0.6, 0.92)
            v = rng.uniform(0.15, 0.21)
            colour = p.at_value(p.mix(rng.choice(["alizarin", "viridian", "ultramarine", "yellow_ochre", "burnt_sienna"]), "burnt_umber", 0.6), v)
            s.block_in(box(x, base - h, min(x + w, FX1 - 6), base, 0.8), "flat", colour, density=1.0, size=0.02, edge="hard", direction="vertical")
            x += w + rng.uniform(0.5, 3)
if STAGE >= 3:
    # 3. The grille: bars a little uneven, two rails across; most of each bar in shade.
    xs = [FX0 + 18 + i * 41 + rng.uniform(-2, 2) for i in range(6)]
    for x in xs:
        s.stroke([n(x, FY0 - 4), n(x + rng.uniform(-1, 1), FY1 + 4)], "round_hard", "iron", size=0.016, opacity=0.95, pressure="even" if "even" in __import__("easel").PRESSURE_PROFILES else "taper")
    for y in (FY0 + 125, FY0 + 260):
        s.stroke([n(FX0 - 4, y), n(FX1 + 4, y + rng.uniform(-1, 1))], "round_hard", "iron", size=0.016, opacity=0.95)
if STAGE >= 4:
    # 4. The frame's edges catch the room's light from the left; the bars take a thin glint on that side.
    s.stroke([n(6, 4), n(7, 230), n(6, 456)], "bristle", "frame_lit", size=0.03, opacity=0.6, pressure=[1.0, 0.7, 0.3])
    s.stroke([n(4, 8), n(150, 9), n(296, 7)], "bristle", "frame_lit", size=0.02, opacity=0.45, pressure=[0.9, 0.5, 0.1])
    for x in xs[:4]:
        s.stroke([n(x - 3, FY0 + 10), n(x - 3, FY0 + 200)], "liner", "iron_lit", size=0.004, opacity=0.5, pressure="taper")
if STAGE >= 5:
    # 5. The lock: a brass plate on the middle rail, the keyhole dark, one bright edge.
    s.block_in(box(132, FY0 + 108, 168, FY0 + 150, 0.8), "flat", "brass", density=1.0, size=0.02, edge="hard", solid=True, direction="vertical")
    s.stroke([n(150, FY0 + 121), n(150, FY0 + 133)], "round_hard", "inside", size=0.012, opacity=1.0, pressure="even")
    s.stroke([n(134, FY0 + 110), n(134, FY0 + 146)], "liner", "brass_lit", size=0.004, opacity=0.8, note="subject")
s.look(path=f"stage{STAGE}.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([n(250, 448), n(258, 444), n(264, 447)], "liner", "carcass", size=0.004, opacity=0.6, note="signature")
    print(s.checklist())
    s.export("locked_shelf.png")
