#!/usr/bin/env python3
"""Чинит нормали меша STM1/STM2 на месте: без нулевых и без «вывернутых» (на сгибах листов добавляет копии вершин).

  python3 tools/fix_normals.py Assets/_Project/Models/SkyFortress.bytes

Зачем: модели Meshy — тонкие незамкнутые оболочки. При сглаживании нормалей по общей позиции две стороны
тонкого листа (флаг, вымпел, перила) складываются в ноль, а у 14% площади крепости нормаль вершины смотрела
против своей же грани. Нулевая нормаль в шейдере — normalize(0) = NaN: на телефоне это чёрное пятно с белым
ореолом свечения («ядерные фонари» в сборке 89).

Как: для каждой вершины файла опорное направление — сумма нормалей её собственных граней (по индексу, с весом
площади); если она вырождена — нормаль самой большой грани. К нему добавляются грани соседей по позиции,
но только смотрящие в ту же сторону (угол < 60°): сглаживание через швы UV остаётся, встречные листы не гасят.
"""
import struct, sys
import numpy as np


def main():
    path = sys.argv[1]
    b = bytearray(open(path, 'rb').read())
    magic = bytes(b[:4]); vc, second = struct.unpack('<ii', b[4:12]); o = 12
    P = np.frombuffer(b, '<f4', vc * 3, o).reshape(-1, 3).astype(np.float64); no = o + vc * 12
    N0 = np.frombuffer(b, '<f4', vc * 3, no).reshape(-1, 3).astype(np.float64)
    o = no + vc * 12 + vc * 8
    if magic == b'STM1':
        T = np.frombuffer(b, '<i4', second * 3, o).reshape(-1, 3)
    else:
        L = []; subs = []
        for _ in range(second):
            slot, cnt = struct.unpack('<ii', b[o:o + 8]); o += 8; subs.append((slot, cnt))
            L.append(np.frombuffer(b, '<i4', cnt, o).reshape(-1, 3)); o += cnt * 4
        T = np.concatenate(L)
    fn = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])      # длина = 2·площадь
    area = np.linalg.norm(fn, axis=1)
    unit = fn / np.maximum(area, 1e-20)[:, None]
    # собственные грани вершины
    own = np.zeros((vc, 3)); big = np.zeros(vc); bigN = np.zeros((vc, 3))
    for k in range(3):
        np.add.at(own, T[:, k], fn)
    for t in np.argsort(area):                                             # последняя запись — самая большая грань
        for k in range(3):
            bigN[T[t, k]] = unit[t]
    ref = own.copy()
    ln = np.linalg.norm(ref, axis=1)
    weak = ln < 0.2 * np.maximum(np.bincount(T.ravel(), np.repeat(area, 3), vc), 1e-20)   # грани гасят друг друга
    ref[weak] = bigN[weak]
    ref /= np.maximum(np.linalg.norm(ref, axis=1), 1e-20)[:, None]
    # соседи по позиции
    key = np.round(P / 1e-4).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
    faces_at = [[] for _ in range(inv.max() + 1)]
    for t in range(len(T)):
        for k in range(3):
            faces_at[inv[T[t, k]]].append(t)
    N = np.zeros((vc, 3))
    for v in range(vc):
        fs = np.unique(faces_at[inv[v]])
        if len(fs) == 0: N[v] = N0[v]; continue
        m = unit[fs] @ ref[v] > 0.5
        s = fn[fs][m].sum(0) if m.any() else ref[v]
        l = np.linalg.norm(s)
        N[v] = s / l if l > 1e-12 else ref[v]
    used = np.zeros(vc, bool); used[T.ravel()] = True
    N[~used] = np.where(np.linalg.norm(N0[~used], axis=1, keepdims=True) > 0.5, N0[~used], [0, 1, 0])
    # Вершина, общая (по индексу) для граней, смотрящих в разные стороны (сгиб тонкого листа), одной нормалью
    # не обойдётся: углы граней, чья нормаль расходится с нормалью вершины больше ~70°, получают свою копию вершины.
    T2 = T.copy(); extraP, extraN, extraU, extraSrc = [], [], [], {}
    U = np.frombuffer(b, '<f4', vc * 2, no + vc * 12).reshape(-1, 2)
    for t in range(len(T)):
        for k in range(3):
            v = T[t, k]
            if unit[t] @ N[v] >= 0.3: continue
            fs = np.unique(faces_at[inv[v]])
            m = unit[fs] @ unit[t] > 0.5
            sacc = fn[fs][m].sum(0); l = np.linalg.norm(sacc)
            nn = sacc / l if l > 1e-12 else unit[t]
            kk = (int(v),) + tuple(np.round(nn * 6).astype(int))
            if kk not in extraSrc:
                extraSrc[kk] = vc + len(extraP); extraP.append(P[v]); extraN.append(nn); extraU.append(U[v])
            T2[t, k] = extraSrc[kk]
    l0 = np.linalg.norm(N0, axis=1)
    d = np.stack([(unit * N0[T[:, k]]).sum(1) for k in range(3)], 1).min(1)
    NA = np.concatenate([N, np.array(extraN).reshape(-1, 3)]); PA = np.concatenate([P, np.array(extraP).reshape(-1, 3)])
    UA = np.concatenate([U, np.array(extraU).reshape(-1, 2)])
    d2 = np.stack([(unit * NA[T2[:, k]]).sum(1) for k in range(3)], 1).min(1)
    print('было: вершин %d, нулевых нормалей %d, граней с нормалью вершины против грани %d (%.1f%% площади)' % (vc, (l0 < 0.5).sum(), (d < -0.2).sum(), area[d < -0.2].sum() / area.sum() * 100))
    print('стало: вершин %d (+%d копий на сгибах), нулевых %d, против грани %d (%.1f%% площади), мин. cos к грани %.2f' % (len(PA), len(extraP), (np.linalg.norm(NA, axis=1) < 0.5).sum(), (d2 < -0.2).sum(), area[d2 < -0.2].sum() / area.sum() * 100, d2.min()))
    out = bytearray(magic + struct.pack('<ii', len(PA), second))
    out += PA.astype('<f4').tobytes() + NA.astype('<f4').tobytes() + UA.astype('<f4').tobytes()
    if magic == b'STM1':
        out += T2.astype('<i4').tobytes()
    else:
        pos = 0
        for slot, cnt in subs:
            out += struct.pack('<ii', slot, cnt) + T2[pos:pos + cnt // 3].astype('<i4').tobytes(); pos += cnt // 3
    open(path, 'wb').write(out)


if __name__ == '__main__':
    main()
