import time
from easel import Session, cell, span, blob, region, polygon, ribbon

t=time.time()
s = Session(560, 600, texture="linen", ground="toned_grey", seed=11, budget=140, out_dir=".")
p = s.palette
p["stone"] = p.at_value(p.mix("titanium_white", "burnt_umber", 0.35), 0.46)
p["stone_dark"] = p.at_value(p.mix("ultramarine", "burnt_umber", 0.5), 0.28)
p["lit"] = p.at_value(p.mix("yellow_ochre", "titanium_white", 0.25), 0.76)
p["floor"] = p.at_value(p.mix("burnt_umber", "ultramarine", 0.3), 0.24)
for k in ["stone","stone_dark","lit","floor"]:
    print(k, round(p.value_of(p[k]),2), p.hex(p[k]))
# drawing: floor line, window light patch thrown from the left
floor = polygon([(0.0, 0.80), (1.0, 0.78), (1.0, 1.0), (0.0, 1.0)])
patch = polygon([(0.0, 0.18), (0.42, 0.26), (0.46, 0.80), (0.0, 0.80)])
s.pencil(floor, pressure=0.5)
s.pencil(patch, pressure=0.5)
s.look(grid=True, path="p0_draw.png")
print("seconds", round(time.time()-t,2))
