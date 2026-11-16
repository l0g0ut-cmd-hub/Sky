#!/usr/bin/env python3
"""Флот из Meshy (ветка meshy-out, meshy/<класс>/model.glb) → Models/Ship_<класс>.bytes + текстуры + LOD.
    g++ -O2 -include cstdint -Itools/decimate tools/decimate/dec.cpp -o /tmp/dec
    python3 tools/convert_fleet.py /путь/к/meshy /tmp/dec
Размер — по классу (длина в метрах: катер 16 … адмиральский 260, ≈7 корсаров по 38 м); киль на −2,2·(L/38), как у Корсара.
Сетка Meshy уже облегчена (remesh 12–71k), поэтому без кластеризации; лёгкая копия ~40%, дальняя ~12%."""
import sys, os, subprocess
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
from convert_glb import read_glb, to_unity, write_bytes, dilate, emission_from_albedo

# класс: (длина м, нос модели в glTF)
FLEET = {
    'cutter': (16, '-x'), 'sloop': (22, '-x'), 'fireship': (20, '-x'), 'corvette': (30, '-x'), 'brig': (40, '-x'),
    'ketch': (42, '+x'), 'frigate': (70, '-x'), 'heavyfrigate': (100, '-x'), 'ship_of_line': (160, '-x'), 'admiral': (260, '+x'),
}
OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Project', 'Models', 'Fleet')

def main():
    src, dec = sys.argv[1], sys.argv[2]
    only = sys.argv[3:] or list(FLEET)
    os.makedirs(OUT, exist_ok=True)
    for cid in only:
        length, nose = FLEET[cid]
        mesh, albedo, normal = read_glb(os.path.join(src, cid, 'model.glb'))
        P, N, U, T = to_unity(mesh, nose, length)
        P[:, 1] += (-2.2 * length / 38.0) - P[:, 1].min()      # киль — как у Корсара в масштабе
        name = 'Ship_' + cid
        write_bytes(os.path.join(OUT, name + '.bytes'), P, N, U, T)
        alb = dilate(albedo.resize((2048, 2048), Image.LANCZOS), mesh['uv'], mesh['idx'])
        alb.save(os.path.join(OUT, name + '_Albedo.jpg'), quality=90)
        if normal is not None:
            dilate(normal.resize((2048, 2048), Image.LANCZOS), mesh['uv'], mesh['idx']).save(os.path.join(OUT, name + '_Normal.png'))
        # Свечения нет: у моделей Meshy окна не светятся (маска окон пустая) — без лишней текстуры.
        n = len(T)
        for suffix, frac in (('_lod', 0.4), ('_far', 0.12)):
            subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'decimate_ship.py'), dec,
                            os.path.join(OUT, name + '.bytes'), os.path.join(OUT, name + suffix + '.bytes'), str(max(1500, int(n * frac)))], check=True)
        print(cid, 'длина', length, 'треуг.', n, 'габарит', np.round(P.min(0), 1), np.round(P.max(0), 1))

if __name__ == '__main__':
    main()
