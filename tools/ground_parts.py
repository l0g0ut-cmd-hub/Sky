#!/usr/bin/env python3
"""Сажает на место детали модели, висящие в воздухе: фонари над тумбами, полотнища и светильники у стен, шпили.

  python3 tools/ground_parts.py Assets/_Project/Models/SkyFortress.bytes [--dry]

Модель Meshy — сотни отдельных кусков, и около сорока из них не касаются остальной модели вовсе: фонарь пирса
стоит в 1–14 м над своей тумбой (в игре при масштабе ×3 — до 40 м), светильник — в метре от стены.
Кусок считается висящим, если он не касается и не пересекает остальную модель: ни его вершины не подходят
к чужим граням ближе 0.6 м, ни чужие — к его граням, ни рёбра не протыкают грани (в обе стороны).
Для висящего куска ищется кратчайший сдвиг до касания:
  • ближайшая опора — внизу (сдвиг в основном вниз) или сбоку не дальше 2.5 м — кусок сдвигается к ней;
  • иначе, если под нижними вершинами есть опора не дальше 16 м, — опускается на неё;
  • иначе, если до модели не дальше 8 м, — сдвигается по кратчайшему пути; дальше — остаётся и попадает в отчёт.
Сдвиг — с заглублением 0.25 м. Проход повторяется, пока висящих не останется (кусок мог сесть на другой висящий).
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


def closest_points(Q, tris):
    """Для каждой точки Q — ближайшая точка на наборе треугольников (Эриксон) и расстояние до неё."""
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    best_d = np.full(len(Q), np.inf); best_p = np.zeros((len(Q), 3))
    for i, p in enumerate(Q):
        ab, ac, ap = b - a, c - a, p - a
        d1 = (ab * ap).sum(1); d2 = (ac * ap).sum(1)
        bp = p - b; d3 = (ab * bp).sum(1); d4 = (ac * bp).sum(1)
        cp = p - c; d5 = (ab * cp).sum(1); d6 = (ac * cp).sum(1)
        va = d3 * d6 - d5 * d4; vb = d5 * d2 - d1 * d6; vc = d1 * d4 - d3 * d2
        den = np.where(np.abs(va + vb + vc) > 1e-20, va + vb + vc, 1.0)
        v = vb / den; w = vc / den
        res = a + ab * v[:, None] + ac * w[:, None]                         # внутри грани
        def put(mask, pts): res[mask] = pts[mask]
        m = (vc <= 0) & (d1 >= 0) & (d3 <= 0); put(m, a + ab * (d1 / np.where(np.abs(d1 - d3) > 1e-20, d1 - d3, 1.0))[:, None])
        m = (vb <= 0) & (d2 >= 0) & (d6 <= 0); put(m, a + ac * (d2 / np.where(np.abs(d2 - d6) > 1e-20, d2 - d6, 1.0))[:, None])
        m = (va <= 0) & (d4 - d3 >= 0) & (d5 - d6 >= 0)
        put(m, b + (c - b) * ((d4 - d3) / np.where(np.abs((d4 - d3) + (d5 - d6)) > 1e-20, (d4 - d3) + (d5 - d6), 1.0))[:, None])
        put((d1 <= 0) & (d2 <= 0), a); put((d3 >= 0) & (d4 <= d3), b); put((d6 >= 0) & (d5 <= d6), c)
        dist = np.linalg.norm(res - p, axis=1)
        k = int(np.argmin(dist)); best_d[i] = dist[k]; best_p[i] = res[k]
    return best_p, best_d


def nearest_shift(own_tris, own_verts, other_tris):
    """Кратчайший сдвиг куска до касания остальной модели (вектор) или None."""
    if len(other_tris) == 0: return None
    p, d = closest_points(own_verts, other_tris)
    k = int(np.argmin(d)); best = (d[k], p[k] - own_verts[k])
    ov = np.unique(other_tris.reshape(-1, 3), axis=0)
    p2, d2 = closest_points(ov, own_tris)
    k = int(np.argmin(d2))
    if d2[k] < best[0]: best = (d2[k], ov[k] - p2[k])
    return best[1]


def seg_tri(p0, p1, tris):
    """Пересекает ли хоть один отрезок p0[i]→p1[i] хоть один треугольник (Мёллер–Трумбор, все пары)."""
    if len(p0) == 0 or len(tris) == 0: return False
    a = tris[:, 0][None]; e1 = (tris[:, 1] - tris[:, 0])[None]; e2 = (tris[:, 2] - tris[:, 0])[None]
    for i0 in range(0, len(p0), 256):
        o = p0[i0:i0 + 256][:, None]; d = (p1[i0:i0 + 256] - p0[i0:i0 + 256])[:, None]
        h = np.cross(d, e2); det = (e1 * h).sum(-1)
        ok = np.abs(det) > 1e-12
        inv = 1.0 / np.where(ok, det, 1.0)
        sv = o - a
        u = (sv * h).sum(-1) * inv
        q = np.cross(sv, e1)
        v = (d * q).sum(-1) * inv
        t = (e2 * q).sum(-1) * inv
        if (ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t >= 0) & (t <= 1)).any(): return True
    return False


def touches(own_tris, own_verts, other_tris, eps):
    """Кусок касается остальной модели: вершины рядом с чужими гранями (в обе стороны) или грани пересекаются."""
    if len(other_tris) == 0: return False
    if near_other(own_verts, other_tris, eps): return True
    ov = np.unique(other_tris.reshape(-1, 3), axis=0)
    lo, hi = own_verts.min(0) - eps, own_verts.max(0) + eps
    ov = ov[((ov >= lo) & (ov <= hi)).all(1)]
    if len(ov) and near_other(ov, own_tris, eps): return True
    def edges(t): return np.concatenate([t[:, 0], t[:, 1], t[:, 2]]), np.concatenate([t[:, 1], t[:, 2], t[:, 0]])
    a, b = edges(own_tris)
    if seg_tri(a, b, other_tris): return True
    a, b = edges(other_tris)
    return seg_tri(a, b, own_tris)


def load(path):
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
    return b, vc, P, T


def components(P, T):
    key = np.round(P / 1e-3).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True); inv = inv.ravel()
    WT = inv[T]
    parent = np.arange(len(first))
    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for t in WT:
        a, c, d = find(t[0]), find(t[1]), find(t[2]); parent[c] = a; parent[d] = a
    root = np.array([find(i) for i in range(len(first))])
    return first, inv, WT, root


def support_below(Q, lo, hi, ct):
    """Зазор от нижних вершин куска до ближайшей поверхности под ними (луч вниз) или None."""
    gap = None
    if len(ct) == 0: return None
    a, b2, c = ct[:, 0], ct[:, 1], ct[:, 2]
    v0 = b2[:, [0, 2]] - a[:, [0, 2]]; v1 = c[:, [0, 2]] - a[:, [0, 2]]
    den = v0[:, 0] * v1[:, 1] - v0[:, 1] * v1[:, 0]
    ok = np.abs(den) > 1e-9; dd = np.where(ok, den, 1)
    for q in Q[Q[:, 1] < lo[1] + 0.5]:
        w = q[[0, 2]] - a[:, [0, 2]]
        u = np.where(ok, (w[:, 0] * v1[:, 1] - w[:, 1] * v1[:, 0]) / dd, -1)
        v = np.where(ok, (v0[:, 0] * w[:, 1] - v0[:, 1] * w[:, 0]) / dd, -1)
        hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1)
        if not hit.any(): continue
        y = a[hit, 1] + u[hit] * (b2[hit, 1] - a[hit, 1]) + v[hit] * (c[hit, 1] - a[hit, 1])
        below = y[y < q[1]]
        if len(below):
            g = q[1] - below.max(); gap = g if gap is None else min(gap, g)
    return gap


def floating(W, WT, root, max_tris=400):
    """Список висящих кусков: (корень, вершины, маска треугольников)."""
    troot = root[WT[:, 0]]
    tri = W[WT]; tlo = tri.min(1); thi = tri.max(1)
    out = []
    for rid, cnt in zip(*np.unique(troot, return_counts=True)):
        if cnt > max_tris: continue
        own = troot == rid
        vs = np.where(root == rid)[0]; Q = W[vs]; lo, hi = Q.min(0), Q.max(0)
        near = ~own & (tlo < hi + 1).all(1) & (thi > lo - 1).all(1)
        if touches(tri[own], Q, tri[near], 0.6): continue
        out.append((rid, vs, own))
    return out, tri, tlo, thi


def main():
    path = sys.argv[1]; dry = '--dry' in sys.argv
    b, vc, P, T = load(path)
    first, inv, WT, root = components(P, T)
    W = P[first].copy()
    total = np.zeros((len(W), 3)); log = []
    for rnd in range(4):
        fl, tri, tlo, thi = floating(W, WT, root)
        if not fl: break
        moved = 0
        for rid, vs, own in fl:
            Q = W[vs]; lo, hi = Q.min(0), Q.max(0)
            wide = ~own & (tlo < hi + 40).all(1) & (thi > lo - 40).all(1)
            v = nearest_shift(tri[own], Q, tri[wide])
            col = ~own & (tlo[:, 0] < hi[0]) & (thi[:, 0] > lo[0]) & (tlo[:, 2] < hi[2]) & (thi[:, 2] > lo[2])
            gap = support_below(Q, lo, hi, tri[col])
            d = None if v is None else float(np.linalg.norm(v))
            if d is not None and d > 1e-6 and ((v[1] < 0 and -v[1] >= 0.7 * d and d <= 16) or d <= 2.5):
                sh = v * (1 + 0.25 / d); how = 'к ближайшей опоре'
            elif gap is not None and gap <= 16:
                sh = np.array([0.0, -(gap + 0.25), 0.0]); how = 'опущен на опору'
            elif d is not None and d <= 8:
                sh = v * (1 + 0.25 / d); how = 'по кратчайшему пути'
            else:
                if rnd == 0: log.append('  ОСТАЁТСЯ висеть: %d треуг., %s … %s (до модели %s м)' % (int(own.sum()), lo.round(1), hi.round(1), None if d is None else round(d, 1)))
                continue
            W[vs] += sh; total[vs] += sh; moved += 1
            log.append('  %s на %.2f м (%s): %d треуг., %s … %s' % (how, float(np.linalg.norm(sh)), sh.round(1), int(own.sum()), lo.round(1), hi.round(1)))
        if moved == 0: break
    left, _, _, _ = floating(W, WT, root)
    print('\n'.join(log))
    print('сдвинуто кусков: %d; висящих осталось: %d' % (len(np.unique(root[np.abs(total).sum(1) > 0])), len(left)))
    if dry or not np.abs(total).sum(): return
    P2 = P + total[inv]
    b[12:12 + vc * 12] = P2.astype('<f4').tobytes()
    open(path, 'wb').write(b)


if __name__ == '__main__':
    main()
