#!/usr/bin/env python3
"""Сажает на опору мелкие детали модели, висящие в воздухе (фонари пирса над тумбами, шпили, столбики).

  python3 tools/ground_parts.py Assets/_Project/Models/SkyFortress.bytes [--dry]

Модель Meshy — сотни отдельных кусков; часть мелких (фонарь со столбиком) стоит не на своей тумбе, а в 1–7 м
над ней (в игре при масштабе ×3 — до 20 м: «фонари висят в воздухе»). Для каждого связного куска до 150
треугольников (и от 20: обрывки не трогаются), не касающегося остальной модели ни с какой стороны,
из его нижних вершин опускается луч вниз; если до опоры от 0.3 до 16 м
(и кусок не высокое полотнище: высота ≤ 25 м или зазор ≤ 1.5 м) — кусок целиком опускается на опору
с заглублением 0.2 м. Подвешенное выше 16 м (ящики на кранах, флаги) не трогается.
Меняются только позиции вершин; нормали, UV и треугольники те же.
"""
import struct, sys
import numpy as np


def near_other(Q, tris, eps):
    """Есть ли вершина куска ближе eps к чужому треугольнику (расстояние точка–треугольник)."""
    if len(tris) == 0: return False
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    ab, ac = b - a, c - a
    n = np.cross(ab, ac); nl = np.linalg.norm(n, axis=1); ok = nl > 1e-12
    n = n / np.maximum(nl, 1e-12)[:, None]
    d00 = (ab * ab).sum(1); d01 = (ab * ac).sum(1); d11 = (ac * ac).sum(1)
    den = d00 * d11 - d01 * d01
    for q in Q:
        ap = q - a
        dist = (ap * n).sum(1)
        pr = ap - dist[:, None] * n
        d20 = (pr * ab).sum(1); d21 = (pr * ac).sum(1)
        v = (d11 * d20 - d01 * d21) / np.where(np.abs(den) > 1e-12, den, 1)
        w = (d00 * d21 - d01 * d20) / np.where(np.abs(den) > 1e-12, den, 1)
        ins = ok & (v >= 0) & (w >= 0) & (v + w <= 1)
        if (ins & (np.abs(dist) < eps)).any(): return True
        # рёбра и вершины: расстояние до трёх отрезков
        for p0, e in ((a, ab), (a, ac), (b, c - b)):
            t = np.clip(((q - p0) * e).sum(1) / np.maximum((e * e).sum(1), 1e-12), 0, 1)
            if (np.linalg.norm(q - (p0 + t[:, None] * e), axis=1) < eps).any(): return True
    return False


def main():
    path = sys.argv[1]; dry = '--dry' in sys.argv
    b = bytearray(open(path, 'rb').read())
    magic = bytes(b[:4]); vc, second = struct.unpack('<ii', b[4:12]); o = 12
    P = np.frombuffer(b, '<f4', vc * 3, o).reshape(-1, 3).astype(np.float64)
    o += vc * 32
    if magic == b'STM1':
        T = np.frombuffer(b, '<i4', second * 3, o).reshape(-1, 3)
    else:
        L = []
        for _ in range(second):
            _, cnt = struct.unpack('<ii', b[o:o + 8]); o += 8
            L.append(np.frombuffer(b, '<i4', cnt, o).reshape(-1, 3)); o += cnt * 4
        T = np.concatenate(L)
    key = np.round(P / 1e-3).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True); inv = inv.ravel()
    W = P[first]; WT = inv[T]
    parent = np.arange(len(W))
    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for t in WT:
        a, c, d = find(t[0]), find(t[1]), find(t[2]); parent[c] = a; parent[d] = a
    root = np.array([find(i) for i in range(len(W))])
    troot = root[WT[:, 0]]
    tri = W[WT]
    tlo = tri[:, :, [0, 2]].min(1); thi = tri[:, :, [0, 2]].max(1)
    moved = []
    shift = np.zeros(len(W))
    for rid, cnt in zip(*np.unique(troot, return_counts=True)):
        if cnt > 150: continue
        own = troot == rid
        vs = np.where(root == rid)[0]; Q = W[vs]
        lo, hi = Q.min(0), Q.max(0)
        if max(hi[0] - lo[0], hi[2] - lo[2]) > 20: continue
        low = Q[Q[:, 1] < lo[1] + 0.5]
        cand = ~own & (tlo[:, 0] < hi[0]) & (thi[:, 0] > lo[0]) & (tlo[:, 1] < hi[2]) & (thi[:, 1] > lo[2])
        if not cand.any(): continue
        ct = tri[cand]
        gap = None; inside = False
        for q in low:
            a, b2, c = ct[:, 0], ct[:, 1], ct[:, 2]
            v0 = b2[:, [0, 2]] - a[:, [0, 2]]; v1 = c[:, [0, 2]] - a[:, [0, 2]]
            den = v0[:, 0] * v1[:, 1] - v0[:, 1] * v1[:, 0]
            ok = np.abs(den) > 1e-9
            w = q[[0, 2]] - a[:, [0, 2]]
            u = np.where(ok, (w[:, 0] * v1[:, 1] - w[:, 1] * v1[:, 0]) / np.where(ok, den, 1), -1)
            v = np.where(ok, (v0[:, 0] * w[:, 1] - v0[:, 1] * w[:, 0]) / np.where(ok, den, 1), -1)
            hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1)
            if not hit.any(): continue
            y = a[hit, 1] + u[hit] * (b2[hit, 1] - a[hit, 1]) + v[hit] * (c[hit, 1] - a[hit, 1])
            # Опора — ближайшая поверхность под вершиной; поверхность в пределах высоты куска над низом —
            # кусок уже стоит в чём-то (вставлен в стену/тумбу).
            if ((y > q[1] - 0.3) & (y < hi[1])).any(): inside = True; break
            below = y[y <= q[1] - 0.3]
            if len(below):
                g = q[1] - below.max()
                gap = g if gap is None else min(gap, g)
        if '--why' in sys.argv and cnt >= 20 and hi[1] - lo[1] < 30: print('?', int(cnt), lo.round(1), hi.round(1), 'inside' if inside else 'gap %s' % (None if gap is None else round(float(gap), 2)))
        if inside or gap is None: continue
        if cnt < 20: continue                       # обрывки в 1–12 треугольников не трогаем
        if near_other(Q, tri[~own & (tlo[:, 0] < hi[0] + 1) & (thi[:, 0] > lo[0] - 1) & (tlo[:, 1] < hi[2] + 1) & (thi[:, 1] > lo[2] - 1)], 0.6):
            continue                                # касается стены или кронштейна сбоку — не висит
        height = hi[1] - lo[1]
        if gap > 16 or (height > 25 and gap > 1.5): continue
        shift[vs] = gap + 0.2
        moved.append((int(cnt), lo.round(1), hi.round(1), round(float(gap), 2)))
    for m in sorted(moved, key=lambda m: -m[3]): print('опущен на %.2f м: %d треуг., %s … %s' % (m[3], m[0], m[1], m[2]))
    print('всего кусков опущено: %d' % len(moved))
    if dry or not moved: return
    P2 = P.copy(); P2[:, 1] -= shift[inv]
    b[12:12 + vc * 12] = P2.astype('<f4').tobytes()
    open(path, 'wb').write(b)


if __name__ == '__main__':
    main()
