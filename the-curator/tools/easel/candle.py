import json, math, sys
from easel import Session, span, polygon

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
W, H = 120, 300
s = Session(W, H, texture="linen", ground="toned_warm_grey", seed=61, budget=110, out_dir=".")
p = s.palette

def n(x, y):
    return (x / W, y / H)

# The candle in slot pixels, matching the placeholder's geometry so the flame (drawn in code at the
# layout's flame point, x 60 y 16 here) still sits on the wick.
BODY = [(42, 31), (47, 27), (55, 25.5), (65, 25.5), (73, 27), (78, 31), (78, 250), (42, 250)]
WICK = [(58.6, 13), (61.4, 13), (61.6, 27), (58.4, 27)]
DISH = [(60 + math.cos(t) * 50, 256 + math.sin(t) * 9) for t in [k * math.pi / 12 for k in range(24)]]
BASE = [(30, 262), (90, 262), (94, 290), (88, 293), (32, 293), (26, 290)]

def grown(points, by):
    cx = sum(x for x, _ in points) / len(points)
    cy = sum(y for _, y in points) / len(points)
    return [(x + (x - cx) / (math.hypot(x - cx, y - cy) or 1) * by, y + (y - cy) / (math.hypot(x - cx, y - cy) or 1) * by) for x, y in points]

json.dump({"body": BODY, "wick": WICK, "dish": DISH, "base": BASE}, open("outline.json", "w"))
wax = p.mix("titanium_white", "yellow_ochre", 0.14)
p["wax"] = p.at_value(wax, 0.82)
p["wax_shadow"] = p.at_value(p.mix(wax, "burnt_umber", 0.25), 0.60)
p["wax_lit"] = p.at_value(p.mix(wax, "titanium_white", 0.5), 0.93)
p["brass"] = p.at_value(p.mix("yellow_ochre", "burnt_sienna", 0.3), 0.50)
p["brass_dark"] = p.at_value(p.mix(p.mix("yellow_ochre", "burnt_sienna", 0.3), "burnt_umber", 0.5), 0.30)
p["brass_lit"] = p.at_value(p.mix("yellow_ochre", "titanium_white", 0.35), 0.76)
p["wick"] = "#1a1311"           # darker than the mixes reach

s.plan(why="a tallow candle in a brass dish, lit from its own flame above, the wax brightest near the top",
       values={span("D1", "E2"): 0.9, span("C8", "F8"): 0.35},
       lightest=span("D1", "E2"), subject_share=0.4, bands="subject")

# 1. Back to front: the base, the dish, then the wax over them; every mass runs past the outline.
s.block_in(polygon([n(x, y) for x, y in grown(BASE, 3)]), "flat", "brass_dark", density=1.0, size=0.03, solid=True, direction="horizontal", edge="hard")
s.block_in(polygon([n(x, y) for x, y in grown(DISH, 3)]), "flat", "brass", density=1.0, size=0.03, solid=True, direction="horizontal", edge="hard")
s.block_in(polygon([n(x, y) for x, y in grown(BODY, 3)]), "flat", "wax", density=1.0, size=0.03, solid=True, direction="vertical", edge="hard")
if STAGE >= 2:
    # 2. The wax turns from the light: shade down the left, the lit face right of centre, strongest at the top.
    for dx in (0, 1.5):
        s.stroke([n(47 + dx, 34), n(46.5 + dx, 140), n(47 + dx, 247)], "bristle", "wax_shadow", size=0.03, opacity=0.9, pressure="even")
        s.stroke([n(68 - dx, 32), n(68.5 - dx, 120), n(68 - dx, 230)], "bristle", "wax_lit", size=0.022, opacity=0.9, pressure=[1.0, 0.6, 0.15])
    s.stroke([n(43.5, 36), n(43.5, 140), n(43.5, 247)], "round_hard", p.mix("wax_shadow", "burnt_umber", 0.15), size=0.012, opacity=0.8, pressure="even")
    s.stroke([n(46, 33), n(60, 30.5), n(74, 33)], "round_hard", "wax_lit", size=0.012, opacity=0.6, pressure="swell")
if STAGE >= 3:
    # 3. Drips down from the rim, uneven in length, each ending in a bead.
    for x0, x1, y1 in ((49, 48.5, 62), (74, 74.5, 47), (58, 57.5, 38)):
        s.stroke([n(x0, 29), n((x0 + x1) / 2, (29 + y1) / 2), n(x1, y1)], "round_hard", "wax_lit", size=0.009, opacity=0.6, pressure="taper")
        s.stroke([n(x1 - 0.5, y1 - 0.5), n(x1 + 0.5, y1 + 0.5)], "round_hard", "wax_lit", size=0.013, opacity=0.55, pressure="dab")
if STAGE >= 4:
    # 4. The wick, and the brass catching the flame: the dish's far rim and the base's right shoulder.
    s.stroke([n(60, 27), n(60.2, 20), n(60.6, 14)], "round_hard", "wick", size=0.008, opacity=1.0, pressure="even")
    s.stroke([n(66, 248.5), n(88, 250.5), n(106, 255)], "round_hard", "brass_lit", size=0.006, opacity=0.7, pressure="taper")
    s.stroke([n(84, 266), n(88, 278), n(90, 288)], "round_hard", "brass_lit", size=0.006, opacity=0.5, pressure="taper")
    s.stroke([n(14, 258), n(40, 264), n(80, 264.5), n(106, 258)], "round_hard", "brass_dark", size=0.006, opacity=0.5, pressure="taper")
s.look(path=f"stage{STAGE}.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([n(36, 284), n(40, 282), n(43, 284)], "liner", "brass", size=0.004, opacity=0.25, note="signature")
    print(s.checklist())
    s.export("candle_rgb.png")
