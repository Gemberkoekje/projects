import math, random, sys
from easel import Session, span, polygon

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
W, H = 1920, 720
CX, CY, R = 960, 400, 260          # the opening (layout.json "arch"): a semicircle on straight jambs
RING = 62                          # depth of the arch stones
rng = random.Random(23)
s = Session(W, H, texture="linen", ground="umber_wash", seed=23, budget=320, out_dir=".")
p = s.palette

def n(x, y):
    """Pixels to the canvas's 0-1 coordinates."""
    return (x / W, y / H)

p["plaster"] = p.at_value(p.mix("burnt_umber", "burnt_sienna", 0.25), 0.27)
p["plaster_dark"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.25), 0.17)
p["plaster_warm"] = p.at_value(p.mix("burnt_sienna", "burnt_umber", 0.5), 0.31)
p["stone"] = p.at_value(p.mix(p.mix("burnt_umber", "ultramarine", 0.18), "titanium_white", 0.2), 0.40)
p["joint"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.35), 0.18)
p["rim"] = p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.18), "burnt_sienna", 0.06), 0.66)

s.plan(why="the arch stones catch the light coming through from the room beyond; the wall round them sinks into shade",
       values={span("D1", "E2"): 0.40, span("A1", "B3"): 0.20},
       lightest=span("D1", "E2"), subject_share=0.3, bands="subject")

# 1. The wall, one mass over everything (the opening is cut out afterwards).
s.block_in(polygon([n(-20, -20), n(W + 20, -20), n(W + 20, H + 20), n(-20, H + 20)]), "bristle", "plaster",
           density=1.0, size=0.06, direction="horizontal")
if STAGE >= 2:
    # 2. The wall darkens upward into the ceiling's shade, and a little toward the corners.
    for i in range(4):
        y = 10 + i * 34
        s.stroke([n(-40, y), n(W / 2, y + 6), n(W + 40, y)], "bristle", "plaster_dark", size=0.05, opacity=0.55 - i * 0.1,
                 load=1.0, load_falloff=0.0)
    # Warmer patches of plaster where the room's candle reaches, low beside the jambs.
    for x0, x1, y in ((520, 650, 600), (1270, 1400, 560), (560, 660, 470)):
        s.stroke([n(x0, y), n((x0 + x1) / 2, y - 8), n(x1, y + 4)], "bristle", "plaster_warm", size=0.03, opacity=0.45)

def voussoir(a0, a1, r0, r1):
    """One arch stone: the band between two radii and two angles (degrees, 180 is the left springing)."""
    pts = []
    for k in range(5):
        a = math.radians(a0 + (a1 - a0) * k / 4)
        pts.append(n(CX + math.cos(a) * r1, CY - math.sin(a) * r1))
    for k in range(5):
        a = math.radians(a1 + (a0 - a1) * k / 4)
        pts.append(n(CX + math.cos(a) * r0, CY - math.sin(a) * r0))
    return polygon(pts)

if STAGE >= 3:
    # 3. The arch stones, each its own mass, a shade apart; the darker gap left between them is the joint.
    count = 11
    step = 180 / count
    for i in range(count):
        a1 = 180 - i * step - 0.9
        a0 = 180 - (i + 1) * step + 0.9
        key = i == count // 2
        r1 = R + RING + (14 if key else rng.uniform(-4, 6))
        v = (0.44 if key else 0.36 + 0.06 * rng.random())
        colour = p.at_value(p.mix(p.mix(p.mix("burnt_umber", "yellow_ochre", 0.3), "ultramarine", 0.04 + 0.05 * rng.random()), "titanium_white", 0.22), v)
        s.block_in(voussoir(a0, a1, R - 6, r1), "flat", colour, density=1.0, size=0.012, edge="hard", direction="vertical")
    # The jambs: stones in courses down to the counter, inside and just past the shelves' edges.
    for side, (x0, x1) in (("left", (628, 700 + 6)), ("right", (1220 - 6, 1292))):
        y = CY + 3
        for course, h in enumerate((70, 92, 80, 90)):
            y1 = min(y + h, H + 10)
            v = 0.35 + 0.05 * ((course + (side == "right")) % 2) + 0.02 * rng.random()
            colour = p.at_value(p.mix(p.mix(p.mix("burnt_umber", "yellow_ochre", 0.3), "ultramarine", 0.06), "titanium_white", 0.22), v)
            jx = rng.uniform(-3, 3)
            s.block_in(polygon([n(x0 + jx, y), n(x1, y + 2), n(x1 - 1, y1 - 3), n(x0 + jx + 2, y1 - 2)]), "flat", colour,
                       density=1.0, size=0.012, edge="hard", direction="horizontal")
            y = y1 + 4
if STAGE >= 4:
    # 4. Light from beyond: the inner edge of the stones takes it, strongest at the crown, gone by the jambs' feet.
    arc = [n(CX + math.cos(math.radians(a)) * (R + 3), CY - math.sin(math.radians(a)) * (R + 3)) for a in range(180, -1, -15)]
    s.stroke(arc, "round_hard", "rim", size=0.004, opacity=0.65, pressure=[0.3, 0.8, 1.0, 0.8, 0.3])
    for x in (700 + 3, 1220 - 3):
        s.stroke([n(x, CY), n(x, 560), n(x, 700)], "round_hard", "rim", size=0.003, opacity=0.45, pressure=[0.6, 0.3, 0.0])
    # The wall's shade just outside the ring, so the stones stand forward.
    outer = [n(CX + math.cos(math.radians(a)) * (R + RING + 10), CY - math.sin(math.radians(a)) * (R + RING + 10)) for a in range(176, 3, -16)]
    s.stroke(outer, "bristle", "plaster_dark", size=0.012, opacity=0.35)
s.look(path=f"stage{STAGE}.png")
s.look(values=True, path=f"stage{STAGE}_values.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([n(1240, 690), n(1252, 684), n(1262, 689)], "liner", "joint", size=0.002, opacity=0.25, note="signature")
    print(s.checklist())
    s.export("doorway_wall_rgb.png")
