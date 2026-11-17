#!/usr/bin/env python3
"""Пушка сторожевой башни — отдельной сеткой (владелец 18.11: «пушка башни должна поворачиваться (без всей башни)»).
    python3 tools/split_tower_gun.py        (один раз, после convert_props.py watchtower)
Пушку на лафете (один кусок над полом верхней площадки) вырезает из Prop_watchtower(.bytes, _lod, _far) и пишет в
Prop_watchtower_gun.bytes — в координатах от оси поворота; под ней — поворотный круг (закрывает дыры пола под лафетом).
Ось, радиус и срез ствола обмерены по сетке 18.11: в радиусе обмаха (4,9 м) над полом ничего другого нет."""
import sys, os
import numpy as np
from collections import defaultdict
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
from convert_glb import write_bytes
from decimate_ship import load

D = os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Project', 'Models', 'Props')
PIVOT = np.array([6.94, 32.32, 7.25])   # ось поворота (центр лафета по XZ) и пол площадки
SWEEP = 4.86                             # радиус обмаха пушки
FLOOR, TOP = 32.45, 39.6


def read(name):
    r = load(os.path.join(D, name))
    return [np.array(x) for x in r[:4]]


def in_cyl(V):
    d = np.hypot(V[:, :, 0] - PIVOT[0], V[:, :, 2] - PIVOT[2])
    return (d < SWEEP).all(1) & (V[:, :, 1] < TOP).all(1)


def gun_full(P, T):
    V = P[T]
    key = np.round(P, 2); _, inv = np.unique(key, axis=0, return_inverse=True); TI = inv.ravel()[T]
    ok = (V[:, :, 1].min(1) > 32.32) & (np.hypot(V[:, :, 0] - PIVOT[0], V[:, :, 2] - PIVOT[2]).max(1) < 6.5) & (V[:, :, 1].max(1) < 40)
    seed = ok & (V[:, :, 2].min(1) > 9) & (V[:, :, 1].min(1) > 36) & (abs(V[:, :, 0] - 7).max(1) < 1.5)
    vt = defaultdict(list)
    for i in np.where(ok)[0]:
        for v in TI[i]: vt[v].append(i)
    sel = set(np.where(seed)[0]); st = list(sel)
    while st:
        i = st.pop()
        for v in TI[i]:
            for j in vt[v]:
                if j not in sel: sel.add(j); st.append(j)
    m = np.zeros(len(T), bool); m[list(sel)] = True
    sliver = (V[:, :, 1].min(1) < 32.7) & ((V[:, :, 1].max(1) - V[:, :, 1].min(1)) < 0.45)
    m &= ~sliver
    m |= in_cyl(V) & (V[:, :, 1].min(1) > 32.6)
    return m


def gun_lod(P, T):
    V = P[T]
    span = V[:, :, 1].max(1) - V[:, :, 1].min(1)
    return in_cyl(V) & (V[:, :, 1].min(1) > 32.3) & ((V[:, :, 1].min(1) > 32.6) | (span > 0.45))


def compact(P, N, U, T):
    used = np.unique(T); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
    return P[used], N[used], U[used], remap[T]


def turntable(uv):
    n = 48; R = SWEEP - 0.02; yt = FLOOR - PIVOT[1]; yb = yt - 0.3
    a = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ring = np.stack([np.cos(a) * R, np.full(n, yt), np.sin(a) * R], 1)
    P = [np.array([[0, yt, 0]]), ring, ring, ring * [1, 0, 1] + [0, yb, 0]]
    N = [np.array([[0, 1, 0]]), np.tile([0, 1, 0], (n, 1)), np.stack([np.cos(a), np.zeros(n), np.sin(a)], 1),
         np.stack([np.cos(a), np.zeros(n), np.sin(a)], 1)]
    P = np.concatenate(P).astype(float); N = np.concatenate(N).astype(float)
    U = np.tile(uv, (len(P), 1))
    T = []
    for i in range(n):
        j = (i + 1) % n
        T.append([0, 1 + j, 1 + i])                                  # верх (обход по часовой сверху — лицо вверх в Unity)
        s0, s1 = 1 + n + i, 1 + n + j; b0, b1 = 1 + 2 * n + i, 1 + 2 * n + j
        T.append([s0, s1, b1]); T.append([s0, b1, b0])               # бортик
    return P, N, U, np.array(T)


def main():
    gpath = os.path.join(D, 'Prop_watchtower_gun.bytes')
    if os.path.exists(gpath) and '--force' not in sys.argv:
        print('пушка уже вырезана'); return
    P, N, U, T = read('Prop_watchtower.bytes')
    m = gun_full(P, T)
    gP, gN, gU, gT = compact(P, N, U, T[m]); gP = gP - PIVOT
    # Цвет круга — доска пола у оси.
    V = P[T]; c = V.mean(1)
    floor = (abs(c[:, 1] - 32.3) < 0.15) & (np.hypot(c[:, 0] - PIVOT[0], c[:, 2] - PIVOT[2]) < 7) & ~m
    uv = U[T[floor]].mean(1)[len(np.where(floor)[0]) // 2]
    tP, tN, tU, tT = turntable(uv)
    gP = np.concatenate([gP, tP]); gN = np.concatenate([gN, tN]); gU = np.concatenate([gU, tU]); gT = np.concatenate([gT, tT + len(gP) - len(tP)])
    write_bytes(gpath, gP, gN, gU, gT)
    print('пушка', m.sum(), 'тр + круг', len(tT), '; срез ствола от оси', (np.array([6.99, 37.77, 11.45]) - PIVOT).round(2))
    write_bytes(os.path.join(D, 'Prop_watchtower.bytes'), *compact(P, N, U, T[~m]))
    for s in ('_lod', '_far'):
        P, N, U, T = read('Prop_watchtower' + s + '.bytes')
        k = gun_lod(P, T); print(s, 'вырезано', k.sum(), 'из', len(T))
        write_bytes(os.path.join(D, 'Prop_watchtower' + s + '.bytes'), *compact(P, N, U, T[~k]))


if __name__ == '__main__':
    main()
