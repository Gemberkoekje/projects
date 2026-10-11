import random, sys
from easel import Session, span, polygon, blob

SIDE = sys.argv[1] if len(sys.argv) > 1 else "left"
STAGE = int(sys.argv[2]) if len(sys.argv) > 2 else 99
SEED = {"left": 51, "right": 57}[SIDE]
rng = random.Random(SEED)
s = Session(640, 720, texture="linen", ground="umber_wash", seed=SEED, budget=460, out_dir=".")
p = s.palette
oak = p.mix("burnt_umber", "burnt_sienna", 0.45)
p["case"] = p.at_value(oak, 0.33)
p["case_lit"] = p.at_value(p.mix(oak, "yellow_ochre", 0.3), 0.48)
p["back"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.15), 0.15)
p["shadow"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.3), 0.14)
cloths = [p.mix("alizarin", "burnt_umber", 0.35), p.mix("viridian", "burnt_umber", 0.4), p.mix("ultramarine", "burnt_umber", 0.35),
          p.mix("yellow_ochre", "burnt_umber", 0.35), p.mix("burnt_sienna", "burnt_umber", 0.3), p.mix(p.mix("cadmium_red", "burnt_umber", 0.5), "titanium_white", 0.2),
          p.mix(p.mix("cerulean", "burnt_umber", 0.4), "titanium_white", 0.25), p.mix("yellow_ochre", "burnt_sienna", 0.4), p.mix("burnt_umber", "titanium_white", 0.15)]

# The case: sides, top, four shelves; a divider in the middle crosses the shelves' bands.
upright = 0.035
divider = 0.5 if SIDE == "left" else 0.44
shelves = [0.215, 0.415, 0.615, 0.83]        # top of each shelf board
board = 0.028
top_board = 0.035

s.plan(why="a wall of old books, every spine its own, oak shelves catching the window's light",
       values={span("A1", "H1"): 0.40, span("C3", "F6"): 0.30},
       lightest=span("A1", "H1"), subject_share=0.5, bands="subject")

# 1. The dark interior behind the books, the whole case.
s.block_in(polygon([(-0.02, -0.02), (1.02, -0.02), (1.02, 1.02), (-0.02, 1.02)]), "flat", "back", density=1.0, size=0.1, solid=True, direction="horizontal")

def books(x0, x1, base, ceiling):
    """A run of spines along a shelf: widths, heights, colours, values and leans all vary; a few gaps."""
    x = x0 + 0.004
    while x < x1 - 0.02:
        if rng.random() < 0.07:
            x += rng.uniform(0.02, 0.05)                       # a gap where a book is out
            continue
        w = rng.uniform(0.022, 0.058)
        if x + w > x1 - 0.004:
            w = x1 - 0.004 - x
            if w < 0.016:
                break
        h = (base - ceiling) * rng.uniform(0.62, 0.96)
        lean = rng.choice([0, 0, 0, 0, 0.012, -0.010, 0.025]) if w < 0.05 else 0
        cloth = rng.choice(cloths)
        colour = p.at_value(p.mix(cloth, "burnt_umber", 0.2), rng.uniform(0.2, 0.40))
        spine = polygon([(x, base), (x + w, base), (x + w + lean, base - h), (x + lean, base - h)])
        yield spine, colour, (x, w, h, lean)
        x += w + rng.uniform(0.0, 0.004)

spines = []
for i, base in enumerate(shelves):
    ceiling = (shelves[i - 1] + board) if i > 0 else top_board
    for (a, b) in ((upright, divider - 0.012), (divider + 0.012, 1.0 - upright)):
        spines.extend((spine, colour, geo, base) for spine, colour, geo in books(a, b, base, ceiling))

if STAGE >= 2:
    # 2. The books, shelf by shelf, each spine one mass.
    for spine, colour, geo, base in spines:
        s.block_in(spine, "flat", colour, density=1.0, size=0.02, direction="vertical", edge="hard")
if STAGE >= 3:
    # 3. The oak case over them: shelves, sides, the divider and the top, lit from the left.
    for base in shelves:
        s.block_in(polygon([(-0.01, base), (1.01, base + 0.002), (1.01, base + board), (-0.01, base + board - 0.002)]), "flat", "case", density=1.0, size=0.02, solid=True, direction="horizontal", edge="hard")
    s.block_in(polygon([(-0.01, -0.01), (1.01, -0.01), (1.01, top_board), (-0.01, top_board)]), "flat", "case", density=1.0, size=0.025, solid=True, direction="horizontal", edge="hard")
    for x0, x1, c in ((-0.01, upright, "case_lit" if SIDE == "left" else "case"), (1.0 - upright, 1.01, "case"), (divider - 0.012, divider + 0.012, "case")):
        s.block_in(polygon([(x0, -0.01), (x1, -0.01), (x1, 1.01), (x0, 1.01)]), "flat", c, density=1.0, size=0.018, solid=True, direction="vertical", edge="hard")
    # The space under the last shelf: the case's plinth.
    s.block_in(polygon([(-0.01, shelves[-1] + board), (1.01, shelves[-1] + board), (1.01, 1.01), (-0.01, 1.01)]), "bristle", p.at_value(oak, 0.28), density=1.0, size=0.04, direction="horizontal", edge="hard")
if STAGE >= 4:
    # 4. Light: the front edge of each shelf catches it; the books' tops a little; shadow under each shelf.
    for base in shelves:
        s.stroke([(0.0, base + 0.004), (0.5, base + 0.005), (1.0, base + 0.004)], "round_hard", "case_lit", size=0.005, opacity=0.7, pressure="lift_off" if SIDE == "left" else "press_in")
        s.stroke([(0.02, base + board + 0.006), (0.98, base + board + 0.007)], "bristle", "shadow", size=0.012, opacity=0.45)
if STAGE >= 5:
    # 5. A few spines carry bands or a title in faded gilt: small, few, varied.
    gilt = p.at_value(p.mix("yellow_ochre", "titanium_white", 0.2), 0.62)
    for spine, colour, (x, w, h, lean), base in spines:
        if rng.random() < 0.28 and w > 0.026:
            y = base - h * rng.uniform(0.72, 0.86)
            s.stroke([(x + lean * 0.8 + w * 0.15, y), (x + lean * 0.8 + w * 0.85, y)], "liner", gilt, size=0.003, opacity=0.6)
            if rng.random() < 0.5:
                y2 = base - h * rng.uniform(0.12, 0.22)
                s.stroke([(x + lean * 0.2 + w * 0.15, y2), (x + lean * 0.2 + w * 0.85, y2)], "liner", gilt, size=0.003, opacity=0.5)
s.look(path=f"{SIDE}_stage{STAGE}.png")
print(s.budget_line() if hasattr(s, "budget_line") else s.stroke_count)
if STAGE >= 99:
    s.stroke([(0.93, 0.965), (0.945, 0.955), (0.96, 0.962)], "liner", "shadow", size=0.003, opacity=0.6, note="signature")
    print(s.checklist())
    s.export(f"shelf_{SIDE}.png")
