#!/usr/bin/env python3
"""Данные небесной крепости для SkyFortress.cs (в координатах модели Unity, как в .bytes из convert_glb.py).

  python3 tools/fortress_data.py Assets/_Project/Models/SkyFortress.bytes Assets/_Project/Models/SkyFortress_data.json

Что считается:
  heights — карта высот верха модели (клетка --cell м): растеризация треугольников сверху, дыры внутри
            контура крепости (дворы, проёмы) закрыты высотой окружения, чтобы корабль не влезал внутрь;
  guns    — дула на кольцах пушек башен: две самые высокие колонны модели, кольцо — самый широкий ярус
            в верхней половине башни, по 4 дула на диагоналях;
  berth   — точка причала: край пирса (самая низкая широкая площадка вне центра), в 16 м за его концом;
  pierDir — направление пирса от центра (x,z) — по нему выбирается поворот крепости в сцене.
"""
import json, struct, sys, math
import numpy as np
from scipy import ndimage


def load(path):
    b = open(path, 'rb').read()
    assert b[:3] == b'STM', b[:4]
    vc, tc = struct.unpack('<ii', b[4:12]); o = 12
    P = np.frombuffer(b, '<f4', vc * 3, o).reshape(-1, 3); o += vc * 12 * 2 + vc * 8
    T = np.frombuffer(b, '<i4', tc * 3, o).reshape(-1, 3)
    return P.astype(np.float64), T


def raster(P, T, cell):
    lo = P.min(0)
    gw = int((P[:, 0].max() - lo[0]) / cell) + 1
    gh = int((P[:, 2].max() - lo[2]) / cell) + 1
    H = np.full((gh, gw), np.nan)
    for t in P[T]:
        a, b, c = t[:, [0, 2]]
        x0, z0 = t[:, [0, 2]].min(0); x1, z1 = t[:, [0, 2]].max(0)
        i0, i1 = int(math.ceil((x0 - lo[0]) / cell - 0.5)), int(math.floor((x1 - lo[0]) / cell - 0.5))
        j0, j1 = int(math.ceil((z0 - lo[2]) / cell - 0.5)), int(math.floor((z1 - lo[2]) / cell - 0.5))
        # тонкий треугольник уже клетки — всё равно отмечаем ближайшую клетку (шпили, перила)
        if i1 < i0: i0 = i1 = int((0.5 * (x0 + x1) - lo[0]) / cell)
        if j1 < j0: j0 = j1 = int((0.5 * (z0 + z1) - lo[2]) / cell)
        I, J = np.meshgrid(np.arange(i0, i1 + 1), np.arange(j0, j1 + 1))
        px = lo[0] + (I + 0.5) * cell; pz = lo[2] + (J + 0.5) * cell
        v0 = b - a; v1 = c - a; d = v0[0] * v1[1] - v0[1] * v1[0]
        if abs(d) < 1e-6 * max(1.0, (x1 - x0) * (z1 - z0)) or abs(d) < 1e-6:
            # вертикальная стенка: в проекции — отрезок; отмечаем клетки вдоль него
            e = [(a, b), (b, c), (a, c)]
            p0, p1 = max(e, key=lambda q: np.sum((q[1] - q[0]) ** 2))
            dv = p1 - p0; L2 = max(1e-9, dv @ dv)
            tt = np.clip(((px - p0[0]) * dv[0] + (pz - p0[1]) * dv[1]) / L2, 0, 1)
            m = np.hypot(px - p0[0] - tt * dv[0], pz - p0[1] - tt * dv[1]) < cell * 0.71
            y = np.full(I.shape, t[:, 1].max())
        else:
            wx = px - a[0]; wz = pz - a[1]
            u = (wx * v1[1] - wz * v1[0]) / d; v = (v0[0] * wz - v0[1] * wx) / d
            m = (u >= -0.05) & (v >= -0.05) & (u + v <= 1.05)
            if not m.any() and (i0 == i1 or j0 == j1):
                m = np.ones_like(I, bool)
            y = t[0, 1] + np.clip(u, 0, 1) * (t[1, 1] - t[0, 1]) + np.clip(v, 0, 1) * (t[2, 1] - t[0, 1])
            y = np.minimum(y, t[:, 1].max())
        if not m.any(): continue
        H[J[m], I[m]] = np.fmax(H[J[m], I[m]], y[m])
    return H, lo


def main():
    src, dst = sys.argv[1], sys.argv[2]
    cell = 5.0
    P, T = load(src)
    H, lo = raster(P, T, cell)
    bottom = P[:, 1].min()

    # Основная палуба — медиана верхов «твёрдой» части; ниже палубы на 60 м — дыры/проёмы.
    solid = ~np.isnan(H)
    hv = H[solid]
    cnt, edges = np.histogram(hv, bins=np.arange(np.floor(hv.min()), hv.max() + 5, 5.0))
    k = int(np.argmax(cnt)); deck = float(edges[k + 1])   # верх самого массового яруса — палуба
    H0 = H.copy()
    # Закрыть дыры внутри контура (дворы, проёмы): клетки ниже палубы, со всех сторон окружённые палубой/пирсом.
    top = solid & (H >= deck - 30)
    hole = ndimage.binary_fill_holes(top) & ~top
    if hole.any():
        Hf = np.where(top, H, np.nan)
        for _ in range(80):
            nb = ndimage.maximum_filter(np.nan_to_num(Hf, nan=-1e9), size=3)
            fill = hole & np.isnan(Hf) & (nb > -1e8)
            if not fill.any(): break
            Hf[fill] = np.minimum(nb[fill], deck)
        H = np.where(hole & ~np.isnan(Hf), Hf, H)

    # Башни: две самые высокие колонны по разные стороны центра (по x).
    guns = []
    towers = []
    for side in (-1, 1):
        m = P[:, 0] * side > 30
        Q = P[m]
        top = Q[Q[:, 1] > Q[:, 1].max() - 25]
        cx, cz = top[:, 0].mean(), top[:, 2].mean()
        r = np.hypot(Q[:, 0] - cx, Q[:, 2] - cz)
        ytop = Q[:, 1].max()
        # кольцо пушек: самый широкий ярус в верхних 45% башни (ниже купола)
        best, by = 0, ytop - 70
        for y0 in np.arange(ytop - 90, ytop - 30, 5.0):
            mm = (Q[:, 1] >= y0) & (Q[:, 1] < y0 + 5) & (r < 90)
            if mm.sum() < 20: continue
            rr = np.percentile(r[mm], 75)
            if rr > best: best, by = rr, y0 + 5
        towers.append((cx, cz, ytop, by, best))
        for k in range(4):
            a = math.radians(45 + 90 * k)
            guns += [cx + math.cos(a) * best, by + 2, cz + math.sin(a) * best]

    # Пирс: широкая площадка ниже палубы, дальняя от центра.
    lo_x, lo_z = lo[0], lo[2]
    gh, gw = H.shape
    J, I = np.mgrid[0:gh, 0:gw]
    X = lo_x + (I + 0.5) * cell; Z = lo_z + (J + 0.5) * cell
    R = np.hypot(X, Z)
    cand = ~np.isnan(H0) & (H0 < deck - 6) & (H0 > deck - 30) & (R > 120)
    lab, n = ndimage.label(cand)
    sizes = ndimage.sum(cand, lab, range(1, n + 1))
    k = int(np.argmax(sizes)) + 1
    pm = lab == k
    pyh = float(np.median(H0[pm]))
    pc = np.array([X[pm].mean(), Z[pm].mean()])
    # ось пирса — главная компонента его клеток, наружу от центра крепости
    XY = np.stack([X[pm] - pc[0], Z[pm] - pc[1]], 1)
    w, V = np.linalg.eigh(XY.T @ XY)
    pdir = V[:, 1] / np.linalg.norm(V[:, 1])
    if pdir @ pc < 0: pdir = -pdir
    # конец пирса: самая дальняя вдоль направления клетка
    along = (X[pm] - pc[0]) * pdir[0] + (Z[pm] - pc[1]) * pdir[1]
    tip = along.max()
    side = np.array([-pdir[1], pdir[0]])
    across = (X[pm] - pc[0]) * side[0] + (Z[pm] - pc[1]) * side[1]
    near = along > tip - 20
    mid = across[near].mean()
    bpos = pc + pdir * (tip + 16) + side * mid
    print('pier centre', pc.round(1), 'length %.0f width %.0f' % (along.max() - along.min(), across.max() - across.min()))

    hs = np.where(np.isnan(H), -1e4, H)
    out = dict(guns=[round(float(v), 2) for v in guns],
               berth=[round(float(bpos[0]), 2), round(pyh, 2), round(float(bpos[1]), 2)],
               pierDir=[round(float(pdir[0]), 4), round(float(pdir[1]), 4)],
               gridOrigin=[round(float(lo_x), 3), round(float(lo_z), 3)],
               cell=cell, gw=int(gw), gh=int(gh),
               bottom=round(float(bottom), 2), deck=round(deck, 2),
               heights=[round(float(v), 1) for v in hs.ravel()])
    json.dump(out, open(dst, 'w'), separators=(',', ':'))
    print('deck %.1f bottom %.1f pier y %.1f dir %s tip %.1f berth %s' % (deck, bottom, pyh, pdir.round(3), tip, bpos.round(1)))
    for t in towers: print('tower c(%.1f,%.1f) top %.1f ring y %.1f r %.1f' % t)
    print('grid %dx%d' % (gw, gh))


if __name__ == '__main__':
    main()
