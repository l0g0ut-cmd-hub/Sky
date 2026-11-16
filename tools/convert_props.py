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
