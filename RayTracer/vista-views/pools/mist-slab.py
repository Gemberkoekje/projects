"""The mist's multiple scattering against a Monte Carlo of a slab (vista-plan section 10, M1.13b; vista-log/M1.13b.md).

A plane-parallel mist deck (Henyey-Greenstein g 0.85, no absorption) over a Lambertian ground (albedo 0.2), lit by the sun alone
(irradiance 1 across the beam): the radiance leaving its top toward a camera looking down at it, by Monte Carlo with next-event
estimation toward every view at once, against the march's octaves (Wrenninge et al. 2013, a = b = c = 1/2, six) integrated along the
camera's ray, plain and as weighted since M1.13b (the second octave 2 * mu0^(1/4), the rest 1.6 * mu0, mu0 the sine of the sun's
elevation; LowAirSettings.ForwardLobeWeight). Prints, per optical depth and sun, each view's ratio of the stand-in to the truth.

Usage: python mist-slab.py [photons] [cache.json]   (needs numpy; about two minutes at 400000 photons; runs below normal priority)
"""
import ctypes, json, math, os, sys
import numpy as np

try:
    ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)  # below normal: the dev box is shared
except Exception:
    pass

G = 0.85
GROUND = 0.2
TAUS = [1.0, 2.0, 3.5, 6.0]
SUNS = [3.0, 6.0, 12.0, 25.0, 45.0]
# The camera's ray: its elevation (down) and its azimuth from the sun's (0: looking toward the sun).
VIEWS = [(-3, 180), (-3, 90), (-3, 30), (-6, 243), (-6, 150), (-6, 30), (-6, 0), (-12, 120), (-12, 30), (-25, 243), (-25, 60), (-45, 120)]
STANDINS = {'plain octaves': (1.0, 0.0, 1.0, 0.0), 'since M1.13b': (2.0, 0.25, 1.6, 1.0)}


def direction(elevation, azimuth):
    e, a = math.radians(elevation), math.radians(azimuth)
    return np.array([math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)])


def hg(cos, g):
    return (1 - g * g) / (4 * math.pi * (1 + g * g - 2 * g * cos) ** 1.5)


def sample_hg(n, rng):
    s = (1 - G * G) / (1 - G + 2 * G * rng.random(n))
    return np.clip((1 + G * G - s * s) / (2 * G), -1.0, 1.0)


def rotate(d, cos, rng):
    sin = np.sqrt(np.maximum(0.0, 1 - cos * cos))
    phi = 2 * math.pi * rng.random(len(d))
    axis = np.where((np.abs(d[:, 0]) < 0.9)[:, None], np.array([1.0, 0.0, 0.0]), np.array([0.0, 1.0, 0.0]))
    t = np.cross(d, axis)
    t /= np.linalg.norm(t, axis=1)[:, None]
    b = np.cross(d, t)
    return cos[:, None] * d + sin[:, None] * (np.cos(phi)[:, None] * t + np.sin(phi)[:, None] * b)


def truth(tau, sun, photons, rng):
    """The radiance leaving the slab's top toward each view (z up, optical height from the ground)."""
    mu0 = math.sin(math.radians(sun))
    outs = [-direction(*v) for v in VIEWS]
    acc = np.zeros(len(outs))
    z = np.full(photons, tau)
    d = np.tile(-direction(sun, 0.0), (photons, 1))
    w = np.full(photons, mu0)
    alive = np.ones(photons, bool)
    for _ in range(600):
        idx = np.nonzero(alive)[0]
        if len(idx) == 0:
            break
        znew = z[idx] - np.log(1 - rng.random(len(idx))) * d[idx, 2]
        top = znew >= tau
        alive[idx[top]] = False
        ground = (znew <= 0.0) & ~top
        gi = idx[ground]
        if len(gi):
            for k, o in enumerate(outs):
                acc[k] += np.sum(w[gi]) * GROUND / math.pi * o[2] * math.exp(-tau / o[2])
            w[gi] *= GROUND
            z[gi] = 0.0
            u1, u2 = rng.random(len(gi)), rng.random(len(gi))
            d[gi] = np.stack([np.sqrt(u1) * np.cos(2 * math.pi * u2), np.sqrt(u1) * np.sin(2 * math.pi * u2), np.sqrt(1 - u1)], axis=1)
            alive[gi[w[gi] < 1e-4]] = False
        ci = idx[~top & ~ground]
        z[ci] = znew[~top & ~ground]
        for k, o in enumerate(outs):
            acc[k] += np.sum(w[ci] * hg(d[ci] @ o, G) * np.exp(-(tau - z[ci]) / o[2]))
        d[ci] = rotate(d[ci], sample_hg(len(ci), rng), rng)
    return [acc[k] / photons / outs[k][2] for k in range(len(outs))]


def standin(tau, sun, view, weights, steps=800):
    """The octaves along the camera's ray through the slab, and the ground through it lit by the mist's total transmission."""
    forward, forward_power, diffuse, diffuse_power = weights
    mu0 = math.sin(math.radians(sun))
    v = direction(*view)
    out_z = -v[2]
    nu = float(direction(sun, 0.0) @ v)
    z = tau * (np.arange(steps) + 0.5) / steps
    to_sun = (tau - z) / mu0
    emission = 0.0
    for n in range(6):
        weight = 1.0 if n == 0 else forward * mu0 ** forward_power if n == 1 else diffuse * mu0 ** diffuse_power
        emission = emission + weight * 0.5 ** n * hg(nu, G * 0.5 ** n) * np.exp(-(0.5 ** n) * to_sun)
    camera = np.exp(-(tau - z) / out_z) * (tau / steps) / out_z
    ground = GROUND / math.pi * mu0 / (1 + 0.75 * (1 - G) * tau / mu0) * math.exp(-tau / out_z)
    return float(np.sum(camera * emission)) + ground


def main():
    photons = int(sys.argv[1]) if len(sys.argv) > 1 else 400000
    cache = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.environ.get('TEMP', '.'), 'vista-mist-slab.json')
    results = json.load(open(cache)) if os.path.exists(cache) else {}
    rng = np.random.default_rng(7)
    for tau in TAUS:
        for sun in SUNS:
            key = '%g/%g/%d' % (tau, sun, photons)
            if key not in results:
                results[key] = truth(tau, sun, photons, rng)
                json.dump(results, open(cache, 'w'))
    print('views (elevation/azimuth from the sun):', ' '.join('%d/%d' % v for v in VIEWS))
    logs = {name: [] for name in STANDINS}
    for tau in TAUS:
        for sun in SUNS:
            values = results['%g/%g/%d' % (tau, sun, photons)]
            for name, weights in STANDINS.items():
                ratios = [standin(tau, sun, v, weights) / t for v, t in zip(VIEWS, values)]
                logs[name] += [math.log(r) for r, v in zip(ratios, VIEWS) if v[0] >= -12]
                print('tau %3.1f sun %4.1f  %-14s ' % (tau, sun, name) + ' '.join('%4.2f' % r for r in ratios))
    for name, l in logs.items():
        l = np.array(l)
        print('%-14s views 3-12 deg down: rms log ratio %.2f, from %.2f to %.2f times the truth' % (name, math.sqrt(np.mean(l ** 2)), math.exp(l.min()), math.exp(l.max())))


if __name__ == '__main__':
    main()
