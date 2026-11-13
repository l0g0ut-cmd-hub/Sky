#!/usr/bin/env python3
"""Облегчённая копия меша корабля (STM1/STM2) для дальнего LOD и тени-заместителя.

  g++ -O2 -include cstdint -Itools/decimate tools/decimate/dec.cpp -o /tmp/dec
  python3 tools/decimate_ship.py /tmp/dec Models/Corsair.bytes Models/Corsair_lod.bytes 40000

Метод — quadric edge collapse (Garland–Heckbert) в реализации Fast-Quadric-Mesh-Simplification
(sp4cerat, MIT, tools/decimate/). Перед упрощением вершины сваривается по позиции (иначе швы UV —
открытые края, которые упроститель не трогает), UV — по углам треугольников, подмеши сохраняются.
Нормали переносятся с исходника: для каждого угла — нормаль ближайшей исходной вершины, наиболее
совпадающая с нормалью грани (жёсткие рёбра остаются жёсткими). Результат — всегда STM2.
"""
import struct, subprocess, sys, tempfile, os
import numpy as np
from scipy.spatial import cKDTree


def load(path):
    b = open(path, 'rb').read()
    magic = b[:4]; vc, second = struct.unpack('<ii', b[4:12]); o = 12
    P = np.frombuffer(b, '<f4', vc * 3, o).reshape(-1, 3); o += vc * 12
    N = np.frombuffer(b, '<f4', vc * 3, o).reshape(-1, 3); o += vc * 12
    U = np.frombuffer(b, '<f4', vc * 2, o).reshape(-1, 2); o += vc * 8
    tris, mats = [], []
    if magic == b'STM1':
        T = np.frombuffer(b, '<i4', second * 3, o).reshape(-1, 3); tris.append(T); mats.append(np.zeros(len(T), int)); slots = [0]
    else:
        slots = []
        for s in range(second):
            slot, cnt = struct.unpack('<ii', b[o:o + 8]); o += 8
            T = np.frombuffer(b, '<i4', cnt, o).reshape(-1, 3); o += cnt * 4
            tris.append(T); mats.append(np.full(len(T), s)); slots.append(slot)
    return P, N, U, np.concatenate(tris), np.concatenate(mats), slots


def main():
    exe, src, dst, target = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
    shadow = '--shadow' in sys.argv   # заместитель тени: только позиции, вершины общие (вдвое меньше вершин)
    P, N, U, T, M, slots = load(src)
    # сварка по позиции
    # Сварка по позиции: без неё швы UV — открытые края, и при упрощении корпус рвётся дырами (проверено).
    key = np.round(P / 1e-5).astype(np.int64)
    _, weld, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inv = inv.ravel()
    WP = P[weld]; WT = inv[T]
    keep = (WT[:, 0] != WT[:, 1]) & (WT[:, 1] != WT[:, 2]) & (WT[:, 0] != WT[:, 2])
    WT, UT, MT = WT[keep], U[T[keep]], M[keep]
    with tempfile.TemporaryDirectory() as d:
        fin, fout = os.path.join(d, 'in.raw'), os.path.join(d, 'out.raw')
        with open(fin, 'wb') as f:
            f.write(struct.pack('<ii', len(WP), len(WT)))
            f.write(WP.astype('<f4').tobytes())
            rec = np.zeros(len(WT), dtype=[('v', '<i4', 3), ('uv', '<f4', 6), ('m', '<i4')])
            rec['v'] = WT; rec['uv'] = UT.reshape(-1, 6); rec['m'] = MT
            f.write(rec.tobytes())
        subprocess.run([exe, fin, fout, str(target)], check=True, stdout=subprocess.DEVNULL)
        b = open(fout, 'rb').read()
    nv, nt = struct.unpack('<ii', b[:8])
    OP = np.frombuffer(b, '<f4', nv * 3, 8).reshape(-1, 3)
    rec = np.frombuffer(b, dtype=[('v', '<i4', 3), ('uv', '<f4', 6), ('m', '<i4')], count=nt, offset=8 + nv * 12)
    OT, OU, OM = rec['v'], rec['uv'].reshape(-1, 3, 2), rec['m']
    # нормали граней
    a, bb, c = OP[OT[:, 0]], OP[OT[:, 1]], OP[OT[:, 2]]
    fn = np.cross(bb - a, c - a); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    # перенос нормалей: k ближайших исходных вершин, лучшая по совпадению с нормалью грани
    tree = cKDTree(P)
    corners = OP[OT].reshape(-1, 3)
    dist, idx = tree.query(corners, k=8)
    cand = N[idx]                                     # (c,8,3)
    fnc = np.repeat(fn, 3, axis=0)[:, None, :]
    score = (cand * fnc).sum(-1) - dist / (dist[:, :1] + 1e-3) * 0.05
    best = idx[np.arange(len(idx)), score.argmax(1)]
    CN = N[best]
    # углы → вершины Unity: ключ (позиция, uv, нормаль)
    CP = corners; CU = OU.reshape(-1, 2)
    if shadow:
        k = np.round(CP / 1e-5).astype(np.int64)
    else:
        k = np.concatenate([np.round(CP / 1e-5), np.round(CU * 512), np.round(CN * 20)], 1).astype(np.int64)
    _, first, cinv = np.unique(k, axis=0, return_index=True, return_inverse=True)
    cinv = cinv.ravel()
    VP, VN, VU = CP[first], CN[first], CU[first]
    VT = cinv.reshape(-1, 3)
    out = bytearray(b'STM2' + struct.pack('<ii', len(VP), len(slots)))
    out += VP.astype('<f4').tobytes() + VN.astype('<f4').tobytes() + VU.astype('<f4').tobytes()
    for s, slot in enumerate(slots):
        idxs = VT[OM == s].astype('<i4').ravel()
        out += struct.pack('<ii', slot, len(idxs)) + idxs.tobytes()
    open(dst, 'wb').write(out)
    # Исходная вершина для каждой новой — чтобы в игре перенести цвета вершин (веса ветра парусов,
    # номер ствола для отката): ShipLod копирует full.colors32[src[i]].
    np.asarray(best[first], '<i4').tofile(os.path.splitext(dst)[0] + '_src.bytes')
    print('%s: %d tris -> %d tris, %d verts, %d submeshes' % (os.path.basename(src), len(T), len(VT), len(VP), len(slots)))


if __name__ == '__main__':
    main()
