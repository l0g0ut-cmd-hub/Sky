"""
Конвертер модели корабля из GLB (Meshy и т.п.) в формат прототипа.

    python3 tools/convert_glb.py model.glb --name Corsair --length 38 --cell 0.011 --uvcell 0.03

Что делает:
  1. Читает GLB (один меш, один материал PBR) без сторонних библиотек (нужны numpy, Pillow, opencv).
  2. Упрощает меш кластеризацией вершин (сетка по позиции + по UV, чтобы не рвать швы текстуры).
     --cell — размер ячейки в единицах модели (больше → меньше треугольников).
  3. Переводит в координаты Unity: левая СК, нос в +Z, длина --length метров, центр габаритов на высоте 10 м.
     Нос исходной модели — по --nose (-x по умолчанию, как у Meshy). Порядок вершин треугольников
     выравнивается по нормалям (у Meshy материал двусторонний, а Standard рисует одну сторону).
  4. Пишет Assets/_Project/Models/<name>.bytes и текстуры: Albedo (4096 jpg), Normal (2048 png),
     Emission (2048 png, маска ярко-жёлтых окон и фонарей).
"""
import argparse, io, json, struct, os
import numpy as np
import cv2
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
CT = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def read_glb(path):
    with open(path, 'rb') as f:
        f.read(12)
        clen, _ = struct.unpack('<II', f.read(8))
        j = json.loads(f.read(clen))
        blen, _ = struct.unpack('<II', f.read(8))
        b = f.read(blen)

    def acc(i):
        a = j['accessors'][i]
        bv = j['bufferViews'][a['bufferView']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        n = NC[a['type']]
        arr = np.frombuffer(b, dtype=CT[a['componentType']], count=a['count'] * n, offset=off)
        return arr.reshape(-1, n) if n > 1 else arr

    def image(tex_index):
        img = j['images'][j['textures'][tex_index]['source']]
        bv = j['bufferViews'][img['bufferView']]
        off = bv.get('byteOffset', 0)
        return Image.open(io.BytesIO(b[off:off + bv['byteLength']])).convert('RGB')

    p = j['meshes'][0]['primitives'][0]
    mesh = dict(pos=acc(p['attributes']['POSITION']).astype(np.float64),
                nrm=acc(p['attributes']['NORMAL']).astype(np.float64),
                uv=acc(p['attributes']['TEXCOORD_0']).astype(np.float64),
                idx=acc(p['indices']).astype(np.int64).reshape(-1, 3))
    mat = j['materials'][p.get('material', 0)]
    albedo = image(mat['pbrMetallicRoughness']['baseColorTexture']['index'])
    normal = image(mat['normalTexture']['index']) if 'normalTexture' in mat else None
    return mesh, albedo, normal


def decimate(m, cell, uvcell):
    pos, nrm, uv, idx = m['pos'], m['nrm'], m['uv'], m['idx']
    q = np.floor(pos / cell).astype(np.int64)
    qu = np.floor(uv / uvcell).astype(np.int64)
    key = np.stack([q[:, 0], q[:, 1], q[:, 2], qu[:, 0], qu[:, 1]], 1)
    _, cl = np.unique(key, axis=0, return_inverse=True)
    cl = cl.ravel()
    k = cl.max() + 1
    cnt = np.bincount(cl, minlength=k)[:, None]

    def avg(a):
        return np.stack([np.bincount(cl, a[:, i], k) for i in range(a.shape[1])], 1) / cnt

    P, U, N = avg(pos), avg(uv), avg(nrm)
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
    t = cl[idx]
    ok = (t[:, 0] != t[:, 1]) & (t[:, 1] != t[:, 2]) & (t[:, 0] != t[:, 2])
    t = t[ok]
    _, ui = np.unique(np.sort(t, 1), axis=0, return_index=True)
    t = t[np.sort(ui)]
    used = np.unique(t)
    remap = -np.ones(k, np.int64)
    remap[used] = np.arange(len(used))
    return dict(pos=P[used], nrm=N[used], uv=U[used], idx=remap[t])


def to_unity(m, nose, length):
    P, N, T = m['pos'], m['nrm'], m['idx'].copy()
    # glTF → Unity: зеркалим X (правая СК → левая), затем поворачиваем вокруг Y так, чтобы нос смотрел в +Z.
    def conv(a):
        a = np.stack([-a[:, 0], a[:, 1], a[:, 2]], 1)          # Unity-координаты, нос glTF -x стал +x
        axis = {'-x': 0, '+x': 0, '-z': 2, '+z': 2}[nose]
        sign = {'-x': 1, '+x': -1, '-z': 1, '+z': -1}[nose] * (1 if axis == 0 else -1)
        if axis == 0:   # нос вдоль ±X → поворот на ∓90° вокруг Y: (x, z) → (-z·s, x·s)
            return np.stack([-a[:, 2] * sign, a[:, 1], a[:, 0] * sign], 1)
        return np.stack([a[:, 0] * sign, a[:, 1], a[:, 2] * sign], 1)

    Pu, Nu = conv(P), conv(N)
    g = np.cross(Pu[T[:, 1]] - Pu[T[:, 0]], Pu[T[:, 2]] - Pu[T[:, 0]])
    flip = (g * Nu[T].sum(1)).sum(1) < 0
    T[flip] = T[flip][:, [0, 2, 1]]
    lo, hi = Pu.min(0), Pu.max(0)
    s = length / (hi[2] - lo[2])
    Pu = (Pu - (lo + hi) / 2) * s + np.array([0.0, 10.0, 0.0])
    Uu = np.stack([m['uv'][:, 0], 1 - m['uv'][:, 1]], 1)
    print('размер, м:', np.round((hi - lo) * s, 1), ' треугольников:', len(T), ' вершин:', len(Pu))
    return Pu, Nu, Uu, T


def write_bytes(path, P, N, U, T):
    with open(path, 'wb') as f:
        f.write(b'STM1')
        f.write(struct.pack('<ii', len(P), len(T)))
        for a in (P, N, U):
            f.write(a.astype('<f4').tobytes())
        f.write(T.astype('<i4').tobytes())


def emission_from_albedo(img):
    a = np.asarray(img).astype(np.float32) / 255
    hsv = cv2.cvtColor((a * 255).astype(np.uint8), cv2.COLOR_RGB2HSV).astype(np.float32)
    h, s, v = hsv[..., 0] * 2, hsv[..., 1] / 255, hsv[..., 2] / 255
    m = np.clip((v - 0.72) / 0.15, 0, 1) * np.clip((s - 0.45) / 0.2, 0, 1) * ((h > 30) & (h < 62))
    m = cv2.GaussianBlur(m, (0, 0), 1.2)
    return Image.fromarray((a * m[..., None] * 255).clip(0, 255).astype(np.uint8))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('glb')
    ap.add_argument('--name', default='Corsair')
    ap.add_argument('--length', type=float, default=38.0)
    ap.add_argument('--cell', type=float, default=0.011)
    ap.add_argument('--uvcell', type=float, default=0.03)
    ap.add_argument('--nose', default='-x', choices=['-x', '+x', '-z', '+z'])
    ap.add_argument('--out', default=os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Project', 'Models'))
    a = ap.parse_args()

    mesh, albedo, normal = read_glb(a.glb)
    print('исходник: треугольников', len(mesh['idx']), ' альбедо', albedo.size)
    P, N, U, T = to_unity(decimate(mesh, a.cell, a.uvcell), a.nose, a.length)
    os.makedirs(a.out, exist_ok=True)
    write_bytes(os.path.join(a.out, a.name + '.bytes'), P, N, U, T)
    albedo.resize((4096, 4096), Image.LANCZOS).save(os.path.join(a.out, a.name + '_Albedo.jpg'), quality=92)
    if normal is not None:
        normal.resize((2048, 2048), Image.LANCZOS).save(os.path.join(a.out, a.name + '_Normal.png'))
    emission_from_albedo(albedo.resize((2048, 2048), Image.LANCZOS)).save(os.path.join(a.out, a.name + '_Emission.png'))
    print('готово →', os.path.abspath(a.out))


if __name__ == '__main__':
    main()
