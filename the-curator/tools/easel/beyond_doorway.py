import sys
from easel import Session, cell, span, blob, region, polygon, ribbon, ellipse

STAGE = int(sys.argv[1]) if len(sys.argv) > 1 else 99
s = Session(560, 600, texture="linen", ground="toned_grey", seed=11, budget=150, out_dir=".")
p = s.palette
p["stone"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.25), 0.44)
p["stone_shade"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.3), 0.34)
p["joint"] = p.at_value(p.mix("ultramarine", "burnt_umber", 0.5), 0.26)
p["lit"] = p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.1), "burnt_umber", 0.05), 0.76)
p["lit_mid"] = p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.08), "burnt_umber", 0.08), 0.60)
p["floor"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.2), 0.20)
p["floor_lit"] = p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.1), "burnt_umber", 0.45), 0.40)

floor = polygon([(-0.02, 0.80), (1.02, 0.78), (1.02, 1.02), (-0.02, 1.02)])
wall = polygon([(-0.02, -0.02), (1.02, -0.02), (1.02, 0.79), (-0.02, 0.81)])

s.plan(why="the wall beyond the arch, brightening toward a window out of sight on the left",
       values={span("A3", "B6"): 0.72, span("G2", "H6"): 0.36, span("A8", "H8"): 0.26},
       lightest=span("A3", "B6"), subject_share=0.2, bands="subject")

# 1. Big masses, back to front: the wall in its courses, then the floor.
s.block_in(wall, "bristle", "stone", density=0.85, size=0.15, direction="horizontal")
s.block_in(floor, "bristle", "floor", density=0.9, size=0.12, direction="horizontal")
if STAGE >= 2:
    # 2. The wall darkens away from the window: passes along the courses, full on the right, none on the left.
    for i in range(7):
        y = 0.04 + i * 0.12
        s.stroke([(0.30, y + 0.004), (0.66, y - 0.003), (1.04, y)], "bristle", "stone_shade",
                 size=0.16, opacity=0.55, load=1.0, load_falloff=0.0, pressure=[0.0, 0.5, 1.0])
    # ...and lightens toward it: the same passes the other way, a lighter mix, full on the left.
    for i in range(7):
        t = i / 6
        y = 0.10 + i * 0.11
        s.stroke([(-0.04, y), (0.26, y + 0.004), (0.58, y - 0.002)], "bristle", p.mix("lit_mid", "lit", 0.4 + 0.3 * (1 - abs(t - 0.5) * 2)),
                 size=0.15, opacity=0.6, load=1.0, load_falloff=0.0, pressure=[1.0, 0.55, 0.0])
if STAGE >= 3:
    # 3. The floor takes the same light near the window.
    for i in range(3):
        y = 0.84 + i * 0.065
        s.stroke([(-0.04, y), (0.30, y + 0.004), (0.70, y)], "bristle", "floor_lit",
                 size=0.10, opacity=0.6, load=1.0, load_falloff=0.0, pressure=[1.0, 0.5, 0.0])
if STAGE >= 4:
    # 4. Stones: masses a shade apart, laid course by course; the darker stone left between them
    #    is the mortar. Varied widths and values, and the courses lose themselves toward the light.
    courses = [(0.02, 0.115), (0.125, 0.30), (0.31, 0.49), (0.50, 0.67), (0.68, 0.78)]
    starts = [0.40, 0.46, 0.52, 0.44, 0.50]
    widths = [[0.17, 0.22, 0.25], [0.21, 0.15, 0.24], [0.26, 0.18, 0.14], [0.14, 0.27, 0.20], [0.23, 0.19, 0.21]]
    for row, ((top, bottom), x0) in enumerate(zip(courses, starts)):
        x = x0
        for k, w in enumerate(widths[row]):
            v = 0.40 + 0.03 * ((row + k) % 3) - (0.03 if x > 0.75 else 0.0)
            colour = p.at_value(p.mix("burnt_umber", "ultramarine", 0.22 + 0.04 * (k % 2)), v)
            stone = polygon([(x + 0.008, top + 0.008), (x + w - 0.006, top + 0.012), (x + w - 0.01, bottom - 0.008), (x + 0.004, bottom - 0.01)])
            s.block_in(stone, "flat", colour, density=0.9, size=0.05, direction="horizontal", opacity=0.75)
            x += w + 0.012
    # In the light the same courses carry on, a breath apart in value, so they nearly vanish.
    lit_courses = [(0.12, 0.30, 0.03, [0.18, 0.16]), (0.31, 0.49, -0.02, [0.13, 0.22]), (0.50, 0.67, 0.05, [0.20, 0.12])]
    for row, (top, bottom, x0, ws) in enumerate(lit_courses):
        x = x0
        for k, w in enumerate(ws):
            colour = p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.1), "burnt_umber", 0.08), 0.66 + 0.03 * ((row + k) % 2))
            stone = polygon([(x + 0.008, top + 0.008), (x + w - 0.006, top + 0.012), (x + w - 0.01, bottom - 0.008), (x + 0.004, bottom - 0.01)])
            s.block_in(stone, "flat", colour, density=0.85, size=0.05, direction="horizontal", opacity=0.55)
            x += w + 0.012
if STAGE >= 5:
    # 5. Edges: the wall's foot is lost into the floor in the shade, kept in the light.
    s.smudge([(0.70, 0.787), (0.86, 0.783), (0.98, 0.781)])
if STAGE >= 6:
    # 6. Highlights: where the light lands at the foot of the wall.
    s.stroke([(0.01, 0.806), (0.18, 0.804), (0.36, 0.80)], "round_hard", "lit", size=0.010, opacity=0.75, pressure="taper", note="subject")
s.look(path=f"stage{STAGE}.png")
s.look(values=True, path=f"stage{STAGE}_values.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    # Signed small, low in the shade, close in value to what it sits on.
    s.stroke([(0.93, 0.955), (0.95, 0.945), (0.965, 0.952)], "liner", "joint", size=0.004, opacity=0.25, note="signature")
    print(s.checklist())
    s.export("beyond_doorway.png")
