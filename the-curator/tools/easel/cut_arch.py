"""Cuts the arch opening out of the painted wall: alpha 0 inside, anti-aliased edge."""
import sys
from PIL import Image, ImageDraw

src, dst = sys.argv[1], sys.argv[2]
cx, cy, r, bottom = 960, 400, 260, 720
k = 4                                   # supersampling for a smooth edge
im = Image.open(src).convert("RGBA")
w, h = im.size
mask = Image.new("L", (w * k, h * k), 255)
d = ImageDraw.Draw(mask)
d.ellipse([(cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k], fill=0)
d.rectangle([(cx - r) * k, cy * k, (cx + r) * k, (bottom + 10) * k], fill=0)
im.putalpha(mask.resize((w, h), Image.LANCZOS))
im.save(dst)
print(dst, im.size)
