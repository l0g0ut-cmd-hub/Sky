"""
Бесшовный 3D-шум для объёмных облаков (64³, RGBA8) → Assets/_Project/Art/CloudNoise3D.bytes.
  R — «клубы»: Perlin-Worley с перевёрнутым Worley в 3 октавах + искажение координат (закрытые округлые лопасти)
  G — Worley fBm средней частоты (эрозия лопастей)
  B — Worley fBm высокой частоты (тонкие края)
  A — Perlin fBm (искажение и разнообразие)
Порядок байтов как у Texture3D.SetPixelData: x быстрее всего, потом y, потом z.
Рядом пишет tools/out/noise_slices.png — срезы XY/XZ/YZ и гистограммы для проверки.
"""
import os
import numpy as np

N = 64
rng = np.random.default_rng(2026)
ax = np.arange(N) / N
X, Y, Z = np.meshgrid(ax, ax, ax, indexing='ij')


def worley(cells, px=None, py=None, pz=None):
    pts = rng.random((cells, cells, cells, 3))
    px = (X if px is None else px) * cells
    py = (Y if py is None else py) * cells
    pz = (Z if pz is None else pz) * cells
    ix, iy, iz = np.floor(px).astype(int), np.floor(py).astype(int), np.floor(pz).astype(int)
    best = np.full(X.shape, 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cx, cy, cz = ix + dx, iy + dy, iz + dz
                p = pts[cx % cells, cy % cells, cz % cells]
                d = (cx + p[..., 0] - px) ** 2 + (cy + p[..., 1] - py) ** 2 + (cz + p[..., 2] - pz) ** 2
                best = np.minimum(best, d)
    return np.clip(np.sqrt(best), 0, 1)       # F1: 0 в центрах ячеек


def gradient_noise(cells, px=None, py=None, pz=None):
    """Бесшовный Perlin (градиентный) шум."""
    g = rng.normal(size=(cells, cells, cells, 3))
    g /= np.linalg.norm(g, axis=-1, keepdims=True)
    P = [(X if px is None else px) * cells, (Y if py is None else py) * cells, (Z if pz is None else pz) * cells]
    i0 = [np.floor(a).astype(int) for a in P]
    f = [a - b for a, b in zip(P, i0)]
    u = [t * t * t * (t * (t * 6 - 15) + 10) for t in f]
    out = 0
    for cx in (0, 1):
        for cy in (0, 1):
            for cz in (0, 1):
                gv = g[(i0[0] + cx) % cells, (i0[1] + cy) % cells, (i0[2] + cz) % cells]
                dot = gv[..., 0] * (f[0] - cx) + gv[..., 1] * (f[1] - cy) + gv[..., 2] * (f[2] - cz)
                w = (u[0] if cx else 1 - u[0]) * (u[1] if cy else 1 - u[1]) * (u[2] if cz else 1 - u[2])
                out = out + w * dot
    return out * 0.5 + 0.5


def fbm(fn, base, octs, gain=0.5, **kw):
    s, a, t = 0, 1.0, 0
    for o in range(octs):
        s = s + fn(base * 2 ** o, **kw) * a
        t += a
        a *= gain
    return s / t


def norm(v, lo=1, hi=99):
    a, b = np.percentile(v, lo), np.percentile(v, hi)
    return np.clip((v - a) / (b - a), 0, 1)


# Искажение координат (domain warp) — чтобы клетки Worley не выглядели одинаковыми шариками.
warp = 0.06
wx = X + (gradient_noise(4) - 0.5) * warp
wy = Y + (gradient_noise(4) - 0.5) * warp
wz = Z + (gradient_noise(4) - 0.5) * warp

perlin = norm(fbm(gradient_noise, 4, 4, px=wx, py=wy, pz=wz))
billow = 1 - (worley(4, wx, wy, wz) * 0.6 + worley(8, wx, wy, wz) * 0.28 + worley(16, wx, wy, wz) * 0.12)
billow = norm(billow)
# Perlin-Worley: Perlin, «прижатый» к клеткам Worley → округлые лопасти с плавными переходами.
R = norm(np.clip((perlin - (billow - 1.0) * 0.0 - (1 - billow) * 0.85) , -1, 1) * 0.5 + billow * 0.5)
G = norm(1 - (worley(8) * 0.62 + worley(16) * 0.26 + worley(32) * 0.12))
B = norm(1 - (worley(16) * 0.6 + worley(32) * 0.4))
A = norm(fbm(gradient_noise, 8, 3))

rgba = np.stack([R, G, B, A], -1)
data = (rgba.transpose(2, 1, 0, 3) * 255).round().astype(np.uint8)   # [z][y][x]
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, '..', 'Assets', '_Project', 'Art', 'CloudNoise3D.bytes')
data.tofile(out)

try:
    from PIL import Image
    os.makedirs(os.path.join(here, 'out'), exist_ok=True)
    rows = []
    for c in range(4):
        v = rgba[..., c]
        sl = [v[:, :, 20].T, v[:, 20, :].T, v[20, :, :].T]
        rows.append(np.hstack([np.tile(s, (2, 2)) for s in sl]))
    img = (np.vstack(rows) * 255).astype(np.uint8)
    Image.fromarray(img).resize((img.shape[1] * 2, img.shape[0] * 2), Image.NEAREST).save(os.path.join(here, 'out', 'noise_slices.png'))
except Exception as e:
    print('превью не сохранено:', e)
for c, n in zip(range(4), 'RGBA'):
    h = np.histogram(rgba[..., c], bins=10, range=(0, 1))[0] / rgba[..., c].size
    print(n, 'гистограмма', ' '.join('%.2f' % x for x in h))
print('ok', os.path.getsize(out))
