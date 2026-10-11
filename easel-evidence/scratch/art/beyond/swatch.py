from easel import Session, Region
s = Session(900, 200, ground="toned_grey", seed=9, out_dir=".")
p = s.palette
cands = {
  "umber+white": p.at_value(p.mix("burnt_umber", "titanium_white", 0.5), 0.47),
  "umber+ochre": p.at_value(p.mix("burnt_umber", "yellow_ochre", 0.25), 0.47),
  "umber+ultra(0.25)": p.at_value(p.mix("burnt_umber", "ultramarine", 0.25), 0.47),
  "umber+viridian": p.at_value(p.mix("burnt_umber", "viridian", 0.3), 0.47),
  "sienna+ultra": p.at_value(p.mix("burnt_sienna", "ultramarine", 0.5), 0.47),
  "lit: white+ochre .08": p.at_value(p.mix("titanium_white", "yellow_ochre", 0.08), 0.76),
  "lit: white+umber+ochre": p.at_value(p.mix(p.mix("titanium_white", "yellow_ochre", 0.1), "burnt_umber", 0.05), 0.76),
  "floor: umber+ultra .2": p.at_value(p.mix("burnt_umber", "ultramarine", 0.2), 0.24),
}
for i, (name, c) in enumerate(cands.items()):
    band = Region(0.02 + i * 0.12, 0.15, 0.12 + i * 0.12, 0.85)
    s.block_in(band, "flat", c, size=0.03, solid=True)
    print(f"{name:24s} value {p.value_of(c):.2f}  chroma {p.chroma_of(c):.3f}  {p.hex(c)}")
s.look(path="swatch.png")
