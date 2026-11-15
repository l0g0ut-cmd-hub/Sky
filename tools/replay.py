#!/usr/bin/env python3
"""Видео боя сверху из reports/replay.bin (ReplayRecorder): python3 tools/replay.py replay.bin out.mp4 [скорость=8]"""
import struct, sys, math
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.animation import FFMpegWriter

src = sys.argv[1]; out = sys.argv[2] if len(sys.argv) > 2 else "replay.mp4"; speed = float(sys.argv[3]) if len(sys.argv) > 3 else 8.0
b = open(src, "rb").read()
assert b[:4] == b"SKYR", "не replay.bin"
ver = b[4]; build, fx, fz = struct.unpack_from("<ihh", b, 5); p = 13
frames = []
while p + 5 <= len(b):
    t, n = struct.unpack_from("<fB", b, p); p += 5
    ships = []
    for _ in range(n):
        sid, kind, x, z, hd, hp, fl = struct.unpack_from("<BBhhBBB", b, p); p += 9
        ships.append((sid, kind, x * 2, z * 2, hd * 1.5, hp, fl))
    frames.append((t, ships))
print("сборка", build, "кадров", len(frames), f"{frames[0][0]:.0f}–{frames[-1][0]:.0f} с" if frames else "")
colors = {0: "#d4a017", 1: "#c0392b", 2: "#7b241c", 3: "#2e86c1", 4: "#5dade2", 5: "#27ae60"}
names = {0: "флагман", 1: "враг", 2: "тяжёлый", 3: "страж", 4: "ведомый", 5: "торговец"}
allx = [s[2] for _, ss in frames for s in ss] + [fx * 2]; allz = [s[3] for _, ss in frames for s in ss] + [fz * 2]
pad = 300
fig, ax = plt.subplots(figsize=(8, 8), dpi=100)
trail = {}
step = max(1, int(round(speed / 2 / 15 * 2)))  # 2 кадра/с записи, видео 15 к/с
writer = FFMpegWriter(fps=15)
with writer.saving(fig, out, 100):
    for i in range(0, len(frames), step):
        t, ships = frames[i]
        ax.clear(); ax.set_facecolor("#dfe8f0")
        ax.set_xlim(min(allx) - pad, max(allx) + pad); ax.set_ylim(min(allz) - pad, max(allz) + pad); ax.set_aspect("equal")
        ax.plot(fx * 2, fz * 2, "s", color="#555", ms=14)
        for sid, kind, x, z, hd, hp, fl in ships:
            tr = trail.setdefault(sid, []); tr.append((x, z)); del tr[:-60]
            c = colors.get(kind, "k")
            ax.plot([q[0] for q in tr], [q[1] for q in tr], color=c, lw=1, alpha=0.5)
            if fl & 1:
                r = math.radians(hd); L = 90 if kind == 0 else 60
                ax.annotate("", (x + math.sin(r) * L, z + math.cos(r) * L), (x, z), arrowprops=dict(arrowstyle="-|>", color=c, lw=2))
                ax.text(x + 25, z + 25, f"{names.get(kind,'?')} {hp}%" + (" А" if fl & 2 else ""), fontsize=7, color=c)
            else:
                ax.plot(x, z, "x", color="black", ms=10, mew=2)
        ax.set_title(f"Повтор боя · сборка {build} · {t:.0f} с · ×{speed:.0f}")
        writer.grab_frame()
print(out)
