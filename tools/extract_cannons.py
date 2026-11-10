"""
Точки вылета ядер для «сценовой» модели (узлы Cannon_Port_NN / Cannon_Starboard_NN в GLB).

    python3 tools/extract_cannons.py model.glb --name CorsairRepaired --length 38 --nose -x

Тот же перевод координат, что в convert_glb_scene.py (проверяется сверкой с <name>.bytes).
Для каждой пушки: ось ствола — главная ось вершин узла (PCA), направлена от диаметральной плоскости корабля
наружу; дуло — центр вершин в крайних 4 % длины вдоль оси. Пишет <name>_cannons.json:
  {"cannons": [{"name", "side": -1 левый/+1 правый, "muzzle": [x,y,z], "dir": [x,y,z], "length"}]}
"""
import argparse, json, os, struct, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from convert_glb_scene import load, accessor, node_matrix, ROOT


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('glb')
    ap.add_argument('--name', default='CorsairRepaired')
    ap.add_argument('--length', type=float, default=38.0)
    ap.add_argument('--nose', default='-x', choices=['-x', '+x', '-z', '+z'])
    ap.add_argument('--out', default=os.path.join(ROOT, 'Assets', '_Project', 'Models'))
    a = ap.parse_args()
    j, b = load(a.glb)
    scene = j['scenes'][j.get('scene', 0)]
    world, owner = {}, {}

    def walk(ni, parent, cannon):
        m = parent @ node_matrix(j['nodes'][ni])
        world[ni] = m
        nm = j['nodes'][ni].get('name', '')
        if nm.startswith('Cannon_'):
            cannon = nm
        owner[ni] = cannon
        for c in j['nodes'][ni].get('children', []):
            walk(c, m, cannon)
    for ni in scene['nodes']:
        walk(ni, np.eye(4), None)

    P, tag = [], []
    for ni, m in world.items():
        node = j['nodes'][ni]
        if 'mesh' not in node:
            continue
        for prim in j['meshes'][node['mesh']]['primitives']:
            if prim.get('mode', 4) != 4:
                continue
            pos = accessor(j, b, prim['attributes']['POSITION']).astype(np.float64)
            P.append(pos @ m[:3, :3].T + m[:3, 3])
            tag += [owner[ni]] * len(pos)
    P = np.concatenate(P)
    tag = np.array([t or '' for t in tag])

    def conv(v):
        v = np.stack([-v[:, 0], v[:, 1], v[:, 2]], 1)
        axis = 0 if a.nose in ('-x', '+x') else 2
        sign = {'-x': 1, '+x': -1, '-z': 1, '+z': -1}[a.nose] * (1 if axis == 0 else -1)
        if axis == 0:
            return np.stack([-v[:, 2] * sign, v[:, 1], v[:, 0] * sign], 1)
        return np.stack([v[:, 0] * sign, v[:, 1], v[:, 2] * sign], 1)
    Pu = conv(P)
    lo, hi = Pu.min(0), Pu.max(0)
    s = a.length / (hi[2] - lo[2])
    Pu = (Pu - (lo + hi) / 2) * s + np.array([0.0, 10.0, 0.0])

    # Сверка с готовым мешем.
    with open(os.path.join(a.out, a.name + '.bytes'), 'rb') as f:
        assert f.read(4) == b'STM2'
        vc, _ = struct.unpack('<ii', f.read(8))
        ref = np.frombuffer(f.read(vc * 12), '<f4').reshape(-1, 3)
    if vc != len(Pu):
        print('ВНИМАНИЕ: число вершин не совпадает', vc, len(Pu))
    else:
        err = np.abs(ref - Pu).max()
        print('сверка с мешем: макс. отклонение', err)
        assert err < 1e-3, 'параметры перевода не совпадают с мешем (проверь --nose/--length)'

    out = []
    for nm in sorted(set(tag) - {''}):
        V = Pu[tag == nm]
        c = V.mean(0)
        _, _, vt = np.linalg.svd(V - c, full_matrices=False)
        ax = vt[0]
        side = 1.0 if c[0] > 0 else -1.0
        if ax[0] * side < 0:
            ax = -ax
        proj = (V - c) @ ax
        L = proj.max() - proj.min()
        tip = V[proj > proj.max() - 0.04 * L]
        muzzle = tip.mean(0)
        out.append(dict(name=nm, side=side, muzzle=[round(float(x), 4) for x in muzzle],
                        dir=[round(float(x), 4) for x in ax], length=round(float(L), 3)))
        print(nm, 'сторона', side, 'дуло', np.round(muzzle, 2), 'ось', np.round(ax, 2), 'длина', round(L, 2))
    with open(os.path.join(a.out, a.name + '_cannons.json'), 'w') as f:
        json.dump(dict(cannons=out), f, ensure_ascii=False, indent=1)


if __name__ == '__main__':
    main()
