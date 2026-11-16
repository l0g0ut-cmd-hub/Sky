#!/usr/bin/env python3
"""Строения из Meshy (meshy/<имя>/model.glb) → Models/Props/Prop_<имя>.bytes + _lod/_far + текстуры.
    python3 tools/convert_props.py /путь/к/meshy /tmp/decbin [имена…]
Модели Meshy стоят вертикально (Y вверх). Размер — по высоте или по длине (м); низ модели — в y = 0, центр по XZ — в 0.
Ориентация: зеркало X (правая СК glTF → левая Unity) с исправлением обхода треугольников."""
import sys, os, subprocess
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
from convert_glb import read_glb, write_bytes, dilate
from decimate_ship import load as load_stm

# Ствол вниз (владелец 16.11: «башня висит в воздухе… основание скрыть облаками»): цоколь убирается, вместо него —
# копии кирпичного пояса (доля высоты band) уходят вниз на PILLAR м; первые две — полные, дальше — облегчённые (25%).
PILLAR = {'watchtower': ((0.10, 0.25), 200.0)}


def compact(P, N, U, T):
    used = np.unique(T); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
    return P[used], N[used], U[used], remap[T]


def pillar(name, P, N, U, T, dec):
    (f0, f1), depth = PILLAR[name]
    H = P[:, 1].max(); yb, yt = H * f0, H * f1; period = yt - yb
    ty = P[T][:, :, 1]
    top = compact(P, N, U, T[ty.max(1) > yb])
    band = compact(P, N, U, T[(ty.min(1) < yt) & (ty.max(1) > yb)])
    tmp = '/tmp/_band.bytes'; tmpl = '/tmp/_band_lite.bytes'
    write_bytes(tmp, *band)
    subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'decimate_ship.py'), dec, tmp, tmpl,
                    str(max(400, len(band[3]) // 4))], check=True)
    bl = load_stm(tmpl); lite = (np.array(bl[0], float), np.array(bl[1], float), np.array(bl[2], float), np.array(bl[3]))
    parts = [top]
    k = 1
    while k * period < depth:
        src = band if k <= 2 else lite
        q = src[0].copy(); q[:, 1] -= k * period
        parts.append((q, src[1], src[2], src[3])); k += 1
    Ps, Ns, Us, Ts, off = [], [], [], [], 0
    for (a, b, c, t) in parts:
        Ps.append(a); Ns.append(b); Us.append(c); Ts.append(t + off); off += len(a)
    print(name, 'ствол', k - 1, 'копий по', round(period, 1), 'м')
    return np.concatenate(Ps), np.concatenate(Ns), np.concatenate(Us), np.concatenate(Ts)

# имя: (ось размера 'y' — высота / 'x' — длина, метры)
PROPS = {
    'lighthouse': ('y', 120), 'watchtower': ('y', 55), 'buoy': ('y', 22), 'hideout': ('y', 80),
    'wreck': ('x', 80), 'depot': ('x', 110), 'mooringmast': ('y', 110),
}
OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Project', 'Models', 'Props')


def main():
    src, dec = sys.argv[1], sys.argv[2]
    only = sys.argv[3:] or list(PROPS)
    os.makedirs(OUT, exist_ok=True)
    for name in only:
        axis, size = PROPS[name]
        m, albedo, normal = read_glb(os.path.join(src, name, 'model.glb'))
        P = m['pos'].astype(np.float64).copy(); N = m['nrm'].astype(np.float64).copy(); T = m['idx'].copy()
        P[:, 0] *= -1; N[:, 0] *= -1
        g = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])
        flip = (g * N[T].sum(1)).sum(1) < 0
        T[flip] = T[flip][:, [0, 2, 1]]
        lo, hi = P.min(0), P.max(0)
        s = size / (hi[1] - lo[1] if axis == 'y' else hi[0] - lo[0])
        c = (lo + hi) / 2
        P = (P - np.array([c[0], lo[1], c[2]])) * s
        U = np.stack([m['uv'][:, 0], 1 - m['uv'][:, 1]], 1)
        base = os.path.join(OUT, 'Prop_' + name)
        if name in PILLAR: P, N, U, T = pillar(name, P, N, U, T, dec)
        write_bytes(base + '.bytes', P, N, U, T)
        dilate(albedo.resize((2048, 2048), Image.LANCZOS), m['uv'], m['idx']).save(base + '_Albedo.jpg', quality=90)
        if normal is not None:
            dilate(normal.resize((1024, 1024), Image.LANCZOS), m['uv'], m['idx']).save(base + '_Normal.png')
        n = len(T)
        for suffix, frac in (('_lod', 0.35), ('_far', 0.08)):
            subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'decimate_ship.py'), dec,
                            base + '.bytes', base + suffix + '.bytes', str(max(800, int(n * frac)))], check=True)
        print(name, 'треуг.', n, 'габарит', np.round(P.min(0), 1), np.round(P.max(0), 1))


if __name__ == '__main__':
    main()
