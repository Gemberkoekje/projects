"""Cuts a painting out along its outline: alpha from the same shapes the paint was laid in, edge softened.

    python cut_outline.py painting_rgb.png outline.json painting.png

outline.json maps names to polygons in pixels; every polygon is kept (their union).
"""
import json, sys
from PIL import Image, ImageDraw, ImageFilter

src, outline, dst = sys.argv[1], sys.argv[2], sys.argv[3]
shapes = json.load(open(outline))
k = 4
im = Image.open(src).convert("RGBA")
w, h = im.size
mask = Image.new("L", (w * k, h * k), 0)
d = ImageDraw.Draw(mask)
for name in sorted(shapes):
    d.polygon([(x * k, y * k) for x, y in shapes[name]], fill=255)
mask = mask.resize((w, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.8))
im.putalpha(mask)
im.save(dst)
print(dst, im.size)
