import json, math, sys
from easel import Session, span, polygon

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
W, H = 400, 620
s = Session(W, H, texture="linen", ground="toned_grey", seed=41, budget=160, out_dir=".")
p = s.palette

def n(x, y):
    return (x / W, y / H)

# The figure, in slot pixels. Below y 480 the counter hides it.
HOOD = [(206, 30), (238, 40), (266, 62), (286, 98), (296, 140), (296, 184), (288, 222), (272, 250),
        (132, 250), (114, 222), (106, 182), (108, 136), (120, 94), (144, 60), (174, 40)]
CLOAK = [(140, 236), (264, 236), (304, 262), (340, 292), (362, 332), (376, 392), (384, 470), (390, 640),
         (10, 640), (16, 470), (24, 392), (38, 332), (60, 292), (96, 262)]
FACE_C, FACE_R = (202, 160), (50, 66)


def closed_spline(points, samples=8):
    """A closed Catmull-Rom curve through the points, so the outline has no corners nobody drew."""
    out = []
    count = len(points)
    for i in range(count):
        p0, p1, p2, p3 = (points[(i + j - 1) % count] for j in range(4))
        for k in range(samples):
            t = k / samples
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[a]) + (-p0[a] + p2[a]) * t + (2 * p0[a] - 5 * p1[a] + 4 * p2[a] - p3[a]) * t2
                                    + (-p0[a] + 3 * p1[a] - 3 * p2[a] + p3[a]) * t3) for a in (0, 1)))
    return out


HOOD, CLOAK = closed_spline(HOOD), closed_spline(CLOAK)

def grown(points, by):
    """The outline pushed outward, so the paint runs past the edge the cut-out keeps."""
    cx = sum(x for x, _ in points) / len(points)
    cy = sum(y for _, y in points) / len(points)
    out = []
    for x, y in points:
        d = math.hypot(x - cx, y - cy) or 1
        out.append((x + (x - cx) / d * by, y + (y - cy) / d * by))
    return out

json.dump({"hood": HOOD, "cloak": CLOAK}, open("outline.json", "w"))
p["cloak"] = "#1b1512"           # darker than the mixes reach: near-blacks of their own, with a little warmth
p["hood"] = "#211915"
p["fold"] = "#2c221c"
p["face"] = "#0d0908"
p["rim"] = p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.2), "burnt_sienna", 0.08), 0.70)

s.plan(why="a figure against the bright doorway: all shadow, with the light behind just catching hood and shoulders",
       values={span("D1", "E3"): 0.10, span("B4", "G6"): 0.10},
       lightest=span("D1", "E3"), subject_share=0.8, bands="subject")

# 1. The cloak, then the hood over it: one dark mass each, run past the outline.
s.block_in(polygon([n(x, y) for x, y in grown(CLOAK, 8)]), "bristle", "cloak", density=1.0, size=0.06, direction="vertical", solid=True)
s.block_in(polygon([n(x, y) for x, y in grown(CLOAK, 4)]), "flat", "cloak", density=1.0, size=0.05, direction="horizontal", solid=True)
s.block_in(polygon([n(x, y) for x, y in grown(HOOD, 8)]), "bristle", "hood", density=1.0, size=0.05, direction="vertical", solid=True)
if STAGE >= 2:
    # 2. The face lost in the hood: the darkest place, soft-edged.
    face = [n(FACE_C[0] + math.cos(t) * FACE_R[0], FACE_C[1] + math.sin(t) * FACE_R[1]) for t in [k * math.pi / 8 for k in range(16)]]
    s.block_in(polygon(face), "bristle", "face", density=1.0, size=0.03, direction="vertical", edge="ragged")
    # Drape: a few long folds falling from the shoulders, uneven in place and length.
    for x0, x1, y1 in ((150, 132, 470), (238, 252, 480), (98, 70, 450), (300, 326, 440), (196, 192, 400)):
        s.stroke([n(x0, 262), n((x0 + x1) / 2 + 4, (262 + y1) / 2), n(x1, y1)], "bristle", "fold", size=0.02, opacity=0.6, pressure="taper")
if STAGE >= 3:
    # 3. The light from behind: a thin rim inside the outline, on the hood's crown and the shoulders' tops,
    #    strongest on the left where the doorway is brightest.
    s.stroke([n(128, 108), n(146, 70), n(176, 46), n(206, 38), n(232, 46)], "round_hard", "rim", size=0.006, opacity=0.7, pressure="press_in")
    s.stroke([n(68, 298), n(98, 272), n(132, 252)], "round_hard", "rim", size=0.005, opacity=0.6, pressure="press_in")
    s.stroke([n(270, 254), n(302, 270), n(334, 298)], "round_hard", "rim", size=0.004, opacity=0.4, pressure="lift_off")
s.look(path=f"stage{STAGE}.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([n(330, 455), n(338, 451), n(344, 454)], "liner", "fold", size=0.004, opacity=0.6, note="signature")
    print(s.checklist())
    s.export("patron_rgb.png")
