"""
Конвертер «сценовой» модели корабля из GLB (Blender: много мешей, иерархия узлов, несколько PBR-материалов).

    python3 tools/convert_glb_scene.py model.glb --name CorsairRepaired --length 38 --nose +x

Пишет в Assets/_Project/Models/:
  <name>.bytes  — меш STM2: все узлы запечены в одну систему координат Unity (левая СК, нос в +Z, метры),
                  подмеши по материалам: "STM2", int vc, int submeshes, float3 pos[], float3 nrm[], float2 uv[],
                  затем на каждый подмеш: int material, int indexCount, int idx[].
  <name>.json   — материалы: цвет, металличность, гладкость, текстуры (альбедо, нормали, металл/гладкость, эмиссия).
  <name>_*.png  — текстуры (альбедо до 4096, остальное до 2048; metallicRoughness glTF → металл R + гладкость A для Standard).
Упрощения меша нет: модель уже собрана руками.
"""
import argparse, io, json, os, struct
import numpy as np
import cv2
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
CT = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')


def load(path):
    with open(path, 'rb') as f:
        f.read(12)
        clen, _ = struct.unpack('<II', f.read(8))
        j = json.loads(f.read(clen))
        blen, _ = struct.unpack('<II', f.read(8))
        b = f.read(blen)
    return j, b


def accessor(j, b, i):
    a = j['accessors'][i]
    bv = j['bufferViews'][a['bufferView']]
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    n = NC[a['type']]
    stride = bv.get('byteStride')
    dt = np.dtype(CT[a['componentType']])
    if stride and stride != dt.itemsize * n:
        raw = np.frombuffer(b, np.uint8, count=stride * (a['count'] - 1) + dt.itemsize * n, offset=off)
        arr = np.stack([np.frombuffer(raw[k * stride:k * stride + dt.itemsize * n].tobytes(), dt) for k in range(a['count'])])
    else:
        arr = np.frombuffer(b, dtype=dt, count=a['count'] * n, offset=off).reshape(-1, n) if n > 1 else \
              np.frombuffer(b, dtype=dt, count=a['count'], offset=off)
    return arr


def quat_mat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def node_matrix(n):
    if 'matrix' in n:
        return np.array(n['matrix'], np.float64).reshape(4, 4).T
    m = np.eye(4)
    s = np.array(n.get('scale', [1, 1, 1]))
    r = quat_mat(n.get('rotation', [0, 0, 0, 1]))
    m[:3, :3] = r * s[None, :]
    m[:3, 3] = n.get('translation', [0, 0, 0])
    return m


def image(j, b, tex_index):
    img = j['images'][j['textures'][tex_index]['source']]
    bv = j['bufferViews'][img['bufferView']]
    off = bv.get('byteOffset', 0)
    return Image.open(io.BytesIO(b[off:off + bv['byteLength']]))


def emission_from_albedo(img):
    a = np.asarray(img.convert('RGB')).astype(np.float32) / 255
    hsv = cv2.cvtColor((a * 255).astype(np.uint8), cv2.COLOR_RGB2HSV).astype(np.float32)
    h, s, v = hsv[..., 0] * 2, hsv[..., 1] / 255, hsv[..., 2] / 255
    m = np.clip((v - 0.72) / 0.15, 0, 1) * np.clip((s - 0.45) / 0.2, 0, 1) * ((h > 30) & (h < 62))
    m = cv2.GaussianBlur(m, (0, 0), 1.2)
    return Image.fromarray((a * m[..., None] * 255).clip(0, 255).astype(np.uint8)), float(m.mean())


def fit(img, maxsize):
    if max(img.size) > maxsize:
        img = img.resize((maxsize, maxsize) if img.size[0] == img.size[1] else
                         (int(img.size[0] * maxsize / max(img.size)), int(img.size[1] * maxsize / max(img.size))), Image.LANCZOS)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('glb')
    ap.add_argument('--name', default='CorsairRepaired')
    ap.add_argument('--length', type=float, default=38.0)
    ap.add_argument('--nose', default='+x', choices=['-x', '+x', '-z', '+z'])
    ap.add_argument('--out', default=os.path.join(ROOT, 'Assets', '_Project', 'Models'))
    a = ap.parse_args()
    j, b = load(a.glb)

    # Обход сцены: мировые матрицы узлов.
    scene = j['scenes'][j.get('scene', 0)]
    world = {}

    def walk(ni, parent):
        m = parent @ node_matrix(j['nodes'][ni])
        world[ni] = m
        for c in j['nodes'][ni].get('children', []):
            walk(c, m)
    for ni in scene['nodes']:
        walk(ni, np.eye(4))

    P, N, U, groups = [], [], [], {}
    base = 0
    for ni, m in world.items():
        node = j['nodes'][ni]
        if 'mesh' not in node:
            continue
        nm = np.linalg.inv(m[:3, :3]).T
        flip = np.linalg.det(m[:3, :3]) < 0
        for prim in j['meshes'][node['mesh']]['primitives']:
            if prim.get('mode', 4) != 4:
                continue
            at = prim['attributes']
            pos = accessor(j, b, at['POSITION']).astype(np.float64)
            nrm = accessor(j, b, at['NORMAL']).astype(np.float64) if 'NORMAL' in at else np.zeros_like(pos)
            uv = accessor(j, b, at['TEXCOORD_0']).astype(np.float64) if 'TEXCOORD_0' in at else np.zeros((len(pos), 2))
            idx = accessor(j, b, prim['indices']).astype(np.int64).reshape(-1, 3) if 'indices' in prim else \
                np.arange(len(pos)).reshape(-1, 3)
            if flip:
                idx = idx[:, [0, 2, 1]]
            P.append(pos @ m[:3, :3].T + m[:3, 3])
            nn = nrm @ nm.T
            N.append(nn / np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9))
            U.append(uv)
            groups.setdefault(prim.get('material', 0), []).append(idx + base)
            base += len(pos)
    P, N, U = np.concatenate(P), np.concatenate(N), np.concatenate(U)

    # glTF (правая СК) → Unity (левая СК) с носом в +Z: та же схема, что в convert_glb.py.
    def conv(v):
        v = np.stack([-v[:, 0], v[:, 1], v[:, 2]], 1)
        axis = 0 if a.nose in ('-x', '+x') else 2
        sign = {'-x': 1, '+x': -1, '-z': 1, '+z': -1}[a.nose] * (1 if axis == 0 else -1)
        if axis == 0:
            return np.stack([-v[:, 2] * sign, v[:, 1], v[:, 0] * sign], 1)
        return np.stack([v[:, 0] * sign, v[:, 1], v[:, 2] * sign], 1)
    Pu, Nu = conv(P), conv(N)
    lo, hi = Pu.min(0), Pu.max(0)
    s = a.length / (hi[2] - lo[2])
    Pu = (Pu - (lo + hi) / 2) * s + np.array([0.0, 10.0, 0.0])
    Uu = np.stack([U[:, 0], 1 - U[:, 1]], 1)

    subs = []
    for mi in sorted(groups):
        T = np.concatenate(groups[mi])
        # Порядок вершин — по нормалям (материалы двусторонние, а Standard рисует одну сторону).
        g = np.cross(Pu[T[:, 1]] - Pu[T[:, 0]], Pu[T[:, 2]] - Pu[T[:, 0]])
        bad = (g * Nu[T].sum(1)).sum(1) < 0
        T[bad] = T[bad][:, [0, 2, 1]]
        subs.append((mi, T))
    print('размер, м:', np.round((hi - lo) * s, 1), ' вершин:', len(Pu), ' треугольников:', sum(len(t) for _, t in subs),
          ' материалов:', len(subs))

    os.makedirs(a.out, exist_ok=True)
    with open(os.path.join(a.out, a.name + '.bytes'), 'wb') as f:
        f.write(b'STM2')
        f.write(struct.pack('<ii', len(Pu), len(subs)))
        for arr in (Pu, Nu, Uu):
            f.write(arr.astype('<f4').tobytes())
        for slot, (mi, T) in enumerate(subs):
            f.write(struct.pack('<ii', slot, T.size))
            f.write(T.astype('<i4').tobytes())

    mats = []
    for slot, (mi, _) in enumerate(subs):
        m = j['materials'][mi] if 'materials' in j else {}
        pbr = m.get('pbrMetallicRoughness', {})
        e = dict(name=m.get('name', 'Material%d' % mi), color=pbr.get('baseColorFactor', [1, 1, 1, 1]),
                 metallic=pbr.get('metallicFactor', 1.0), smoothness=1.0 - pbr.get('roughnessFactor', 1.0),
                 albedo=None, normal=None, normalScale=1.0, metalSmooth=None, emission=None)
        tag = '%s_m%d' % (a.name, slot)
        if 'baseColorTexture' in pbr:
            img = fit(image(j, b, pbr['baseColorTexture']['index']).convert('RGBA'), 4096)
            has_alpha = np.asarray(img)[..., 3].min() < 250
            fn = tag + ('_Albedo.png' if has_alpha else '_Albedo.jpg')
            if has_alpha:
                img.save(os.path.join(a.out, fn))
            else:
                img.convert('RGB').save(os.path.join(a.out, fn), quality=92)
            e['albedo'] = fn
            em, cover = emission_from_albedo(fit(img, 2048))
            if cover > 0.0005:
                fn = tag + '_Emission.png'
                em.save(os.path.join(a.out, fn))
                e['emission'] = fn
        if 'normalTexture' in m:
            fn = tag + '_Normal.png'
            fit(image(j, b, m['normalTexture']['index']).convert('RGB'), 2048).save(os.path.join(a.out, fn))
            e['normal'] = fn
            e['normalScale'] = m['normalTexture'].get('scale', 1.0)
        if 'metallicRoughnessTexture' in pbr:
            mr = np.asarray(fit(image(j, b, pbr['metallicRoughnessTexture']['index']).convert('RGB'), 2048)).astype(np.float32) / 255
            metal = np.clip(mr[..., 2] * e['metallic'], 0, 1)
            smooth = np.clip(1 - mr[..., 1] * pbr.get('roughnessFactor', 1.0), 0, 1)
            out = np.dstack([metal, metal, metal, smooth])
            fn = tag + '_MetalSmooth.png'
            Image.fromarray((out * 255).astype(np.uint8)).save(os.path.join(a.out, fn))
            e['metalSmooth'] = fn
        # Стекло фонарей и окон светится тёплым светом.
        low = e['name'].lower()
        if any(k in low for k in ('glass', 'lantern', 'window', 'стекл')):
            e['emissionColor'] = [min(1.0, c * 1.0) for c in e['color'][:3]]
            e['emissionIntensity'] = 2.4
        mats.append(e)
        print('  материал %d: %s  текстуры: %s' % (slot, e['name'], [k for k in ('albedo', 'normal', 'metalSmooth', 'emission') if e[k]]))
    json.dump(dict(materials=mats), open(os.path.join(a.out, a.name + '.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('готово →', os.path.abspath(a.out))


if __name__ == '__main__':
    main()
