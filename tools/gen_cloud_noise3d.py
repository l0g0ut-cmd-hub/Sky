"""
Бесшовный 3D-шум для объёмных облаков (64³, RGBA8) → Assets/_Project/Art/CloudNoise3D.bytes.
  R — Perlin-Worley: основная форма кучевых облаков (пухлые «головы»)
  G — фрактальный Worley: средняя детализация (эрозия краёв)
  B — мелкий Worley: тонкая детализация
Порядок байтов как у Texture3D.SetPixelData: x быстрее всего, потом y, потом z.
"""
import os
import numpy as np

N = 64
rng = np.random.default_rng(1337)
ax = np.arange(N) / N
X, Y, Z = np.meshgrid(ax, ax, ax, indexing='ij')   # [x, y, z]


def worley(cells):
    pts = rng.random((cells, cells, cells, 3))
    px, py, pz = X * cells, Y * cells, Z * cells
    ix, iy, iz = np.floor(px).astype(int), np.floor(py).astype(int), np.floor(pz).astype(int)
    best = np.full(X.shape, 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cx, cy, cz = ix + dx, iy + dy, iz + dz
                p = pts[cx % cells, cy % cells, cz % cells]
                d = (cx + p[..., 0] - px) ** 2 + (cy + p[..., 1] - py) ** 2 + (cz + p[..., 2] - pz) ** 2
                best = np.minimum(best, d)
    return 1.0 - np.clip(np.sqrt(best), 0, 1)          # 1 в центрах ячеек


def value_noise(cells):
    lat = rng.random((cells, cells, cells))
    f = [A * cells for A in (X, Y, Z)]
    i0 = [np.floor(a).astype(int) for a in f]
    t = [a - b for a, b in zip(f, i0)]
    t = [a * a * (3 - 2 * a) for a in t]
    out = 0
    for cx in (0, 1):
        for cy in (0, 1):
            for cz in (0, 1):
                w = (t[0] if cx else 1 - t[0]) * (t[1] if cy else 1 - t[1]) * (t[2] if cz else 1 - t[2])
                out = out + w * lat[(i0[0] + cx) % cells, (i0[1] + cy) % cells, (i0[2] + cz) % cells]
    return out


def fbm(fn, base, octs, gain=0.5):
    s, a, tot = 0, 1.0, 0
    for o in range(octs):
        s = s + fn(base * 2 ** o) * a
        tot += a
        a *= gain
    return s / tot


def remap(v, lo, hi, nlo, nhi):
    return nlo + (v - lo) / (hi - lo) * (nhi - nlo)


def norm(v):
    return (v - v.min()) / (v.max() - v.min())


perlin = norm(fbm(value_noise, 4, 4))
wfbm = norm(worley(4) * 0.625 + worley(8) * 0.25 + worley(16) * 0.125)
R = np.clip(remap(perlin, wfbm - 1.0, 1.0, 0.0, 1.0), 0, 1)
R = norm(R)
G = norm(worley(8) * 0.625 + worley(16) * 0.25 + worley(32) * 0.125)
B = norm(worley(16) * 0.625 + worley(32) * 0.375)

rgba = np.stack([R, G, B, np.ones_like(R)], -1)
# Texture3D: индекс = x + y*N + z*N*N → массив в порядке [z][y][x]
data = (rgba.transpose(2, 1, 0, 3) * 255).round().astype(np.uint8)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', '_Project', 'Art', 'CloudNoise3D.bytes')
data.tofile(out)
print('ok', data.shape, os.path.getsize(out), 'R mean %.2f G mean %.2f' % (R.mean(), G.mean()))
