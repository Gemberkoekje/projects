"""Grey stand-in 'photographs' for the mock (../mock/img/stand-in-*.jpg): one-point concrete
interiors with a skylight, light shafts, beams and formwork texture. Deterministic per seed.

    python3 tools/stand_ins.py mock/img

Renders at 1440 px, saves at most 1200 px on the long side at JPEG quality 74, so the sixteen
files stay near 1.4 MB: gembernodes serves them from two ConfigMaps of at most 1 MiB each.
Needs numpy and Pillow."""
import sys, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]


def noise(h, w, scale, rng):
    sh, sw = max(2, h // scale), max(2, w // scale)
    small = Image.fromarray((rng.random((sh, sw)) * 255).astype(np.uint8))
    return np.asarray(small.resize((w, h), Image.BICUBIC)).astype(np.float32) / 255.0


def poly_mask(w, h, pts, blur=0):
    m = Image.new('L', (w, h), 0)
    ImageDraw.Draw(m).polygon([tuple(map(float, p)) for p in pts], fill=255)
    if blur:
        m = m.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(m).astype(np.float32) / 255.0


def lerp(a, b, t):
    return a + (b - a) * t


def render(seed, w, h, kind):
    rng = np.random.default_rng(seed)
    r = random.Random(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    img = np.zeros((h, w), np.float32)

    # vanishing point and back wall
    vx = w * r.uniform(0.35, 0.65)
    vy = h * r.uniform(0.38, 0.62)
    bs = r.uniform(0.16, 0.34)
    bw, bh = w * bs, h * bs * r.uniform(1.1, 1.8)
    x0, x1 = vx - bw / 2, vx + bw / 2
    y0, y1 = vy - bh / 2, vy + bh / 2

    tone = {
        'ceil': r.uniform(0.10, 0.22), 'left': r.uniform(0.30, 0.55),
        'right': r.uniform(0.12, 0.30), 'floor': r.uniform(0.28, 0.48),
        'back': r.uniform(0.18, 0.40),
    }
    if r.random() < 0.5:
        tone['left'], tone['right'] = tone['right'], tone['left']

    regions = {
        'ceil': [(0, 0), (w, 0), (x1, y0), (x0, y0)],
        'floor': [(0, h), (w, h), (x1, y1), (x0, y1)],
        'left': [(0, 0), (x0, y0), (x0, y1), (0, h)],
        'right': [(w, 0), (x1, y0), (x1, y1), (w, h)],
        'back': [(x0, y0), (x1, y0), (x1, y1), (x0, y1)],
    }
    # depth: 0 at the frame edge, 1 at the back wall
    dl = np.clip(xx / max(x0, 1), 0, 1)
    dr = np.clip((w - xx) / max(w - x1, 1), 0, 1)
    dc = np.clip(yy / max(y0, 1), 0, 1)
    df = np.clip((h - yy) / max(h - y1, 1), 0, 1)
    depth = {'left': dl, 'right': dr, 'ceil': dc, 'floor': df}
    for k, pts in regions.items():
        m = poly_mask(w, h, pts)
        v = np.full((h, w), tone[k], np.float32)
        if k in depth:
            fall = r.uniform(-0.25, 0.2)
            v = v * (1 + fall * depth[k])
        img = img * (1 - m) + v * m

    # an opening in the back wall, bright
    if kind in ('door', 'stairs') or r.random() < 0.55:
        ow = bw * r.uniform(0.35, 0.7)
        oh = bh * r.uniform(0.5, 0.85)
        ox = lerp(x0, x1 - ow, r.random())
        oy = y1 - oh
        m = poly_mask(w, h, [(ox, oy), (ox + ow, oy), (ox + ow, y1), (ox, y1)], blur=1.2)
        img = img * (1 - m) + r.uniform(0.82, 0.97) * m
        if kind == 'stairs':
            steps = r.randint(6, 11)
            for i in range(steps):
                sy = oy + oh * (0.45 + 0.55 * i / steps)
                img -= 0.18 * poly_mask(w, h, [(ox, sy), (ox + ow, sy), (ox + ow, sy + 1.6), (ox, sy + 1.6)])

    # formwork texture: board lines and tie holes on the walls
    tex = 0.7 * noise(h, w, 90, rng) + 0.3 * noise(h, w, 18, rng)
    img *= 0.86 + 0.28 * tex
    board = r.choice([h / 9, h / 12, h / 7])
    lines = (np.abs(((yy + 0.5 * xx * r.uniform(-0.05, 0.05)) % board) - board / 2) > board / 2 - 1.2).astype(np.float32)
    img *= 1 - 0.07 * lines * noise(h, w, 40, rng)

    # skylight slit and light shafts
    shafts = np.zeros((h, w), np.float32)
    patches = np.zeros((h, w), np.float32)
    n_shafts = 1 if kind == 'single' else r.randint(1, 3)
    ang = r.uniform(-0.55, 0.55)
    for i in range(n_shafts):
        t = r.uniform(0.1, 0.9)
        sx = lerp(0, w, t) * 0.7 + vx * 0.3
        sy = lerp(0, y0, r.uniform(0.0, 0.6))
        sw = w * r.uniform(0.015, 0.05)
        slit = [(sx - sw, sy), (sx + sw, sy), (sx + sw * 0.6, sy + 6), (sx - sw * 0.6, sy + 6)]
        patches += poly_mask(w, h, slit, blur=1)
        # where the shaft lands: floor or a side wall
        land_y = h * r.uniform(0.72, 1.05)
        land_x = sx + ang * (land_y - sy)
        pw = w * r.uniform(0.08, 0.2)
        pd = h * r.uniform(0.04, 0.09)
        patch = [(land_x - pw, land_y), (land_x + pw, land_y),
                 (land_x + pw * 0.8 + ang * pd, land_y - pd), (land_x - pw * 0.8 + ang * pd, land_y - pd)]
        patches += 0.9 * poly_mask(w, h, patch, blur=w * 0.004)
        vol = [(sx - sw, sy), (sx + sw, sy), patch[1], patch[0]]
        shafts += r.uniform(0.18, 0.32) * poly_mask(w, h, vol, blur=w * 0.012)
    img = img + shafts * (1 - img) + patches * (1 - img) * 0.85

    # a dark diagonal beam crossing the frame, lit on its top edge
    if kind in ('beam', 'single') or r.random() < 0.4:
        a = r.uniform(-0.45, 0.45)
        cy = h * r.uniform(0.18, 0.45)
        th = h * r.uniform(0.04, 0.08)
        p = [(-10, cy - a * w / 2), (w + 10, cy + a * w / 2), (w + 10, cy + a * w / 2 + th), (-10, cy - a * w / 2 + th)]
        m = poly_mask(w, h, p, blur=0.8)
        img = img * (1 - m) + 0.07 * m
        edge = [(-10, cy - a * w / 2), (w + 10, cy + a * w / 2), (w + 10, cy + a * w / 2 + 3), (-10, cy - a * w / 2 + 3)]
        img += 0.25 * poly_mask(w, h, edge, blur=0.8)

    # a small figure, sometimes
    if kind in ('figure', 'door') or r.random() < 0.25:
        fh = h * r.uniform(0.07, 0.13)
        fx = lerp(x0, x1, r.uniform(0.2, 0.8))
        fy = y1 + (h - y1) * r.uniform(0.0, 0.25)
        body = [(fx - fh * 0.13, fy - fh * 0.8), (fx + fh * 0.13, fy - fh * 0.8), (fx + fh * 0.1, fy - fh * 0.42), (fx + fh * 0.06, fy), (fx - fh * 0.06, fy), (fx - fh * 0.1, fy - fh * 0.42)]
        m = poly_mask(w, h, body, blur=0.9)
        hd = Image.new('L', (w, h), 0)
        ImageDraw.Draw(hd).ellipse([fx - fh * 0.075, fy - fh * 0.98, fx + fh * 0.075, fy - fh * 0.78], fill=255)
        m = np.maximum(m, np.asarray(hd.filter(ImageFilter.GaussianBlur(0.9))).astype(np.float32) / 255)
        img = img * (1 - m) + 0.05 * m

    # tone curve, vignette, softness, grain
    img = np.clip(img, 0, 1)
    img = 0.5 + (img - 0.5) * 1.15
    d = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2)
    img *= 1 - 0.28 * np.clip(d - 0.45, 0, 1)
    img = np.clip(img, 0, 1)
    pil = Image.fromarray((img * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))
    arr = np.asarray(pil).astype(np.float32) + rng.normal(0, 4.5, (h, w))
    gray = np.clip(arr, 0, 255).astype(np.uint8)
    # a faint warm-neutral split tone, like a fibre print
    g = gray.astype(np.float32) / 255
    rgb = np.stack([g * 255 * 1.0, g * 255 * 0.992, g * 255 * 0.975], -1)
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))


SIZES = {'p': (960, 1280), 'l': (1440, 960), 's': (1120, 1120)}
PLAN = [
    ('p', 'beam'), ('l', 'door'), ('p', 'single'), ('s', 'stairs'), ('l', 'beam'),
    ('p', 'figure'), ('l', 'single'), ('s', 'door'), ('p', 'stairs'), ('l', 'figure'),
    ('p', 'door'), ('s', 'beam'), ('l', 'stairs'), ('p', 'single'), ('l', 'door'), ('s', 'figure'),
]
for i, (fmt, kind) in enumerate(PLAN, start=1):
    w, h = SIZES[fmt]
    im = render(1000 + i * 37, w, h, kind)
    name = f'{OUT}/stand-in-{i:02d}.jpg'
    im.thumbnail((1200, 1200), Image.LANCZOS)
    im.save(name, quality=74, optimize=True, progressive=True)
    print(name, fmt, kind)
