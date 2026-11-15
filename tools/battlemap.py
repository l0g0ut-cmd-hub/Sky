#!/usr/bin/env python3
"""Карта боя из отчёта игры (раздел «КАРТА БОЯ»): траектории всех кораблей сверху -> PNG.
   python3 tools/battlemap.py reports/latest.txt out.png"""
import re, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

src, out = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else "battlemap.png")
lines = open(src, encoding="utf-8", errors="replace").read().splitlines()
try:
    i = next(k for k, l in enumerate(lines) if l.startswith("КАРТА БОЯ"))
except StopIteration:
    sys.exit("в отчёте нет раздела КАРТА БОЯ")
fort = None; tracks = {}; deaths = {}; times = []
for l in lines[i + 1:]:
    if not l.startswith(" "): break
    l = l.strip()
    m = re.match(r"крепость (-?\d+),(-?\d+)", l)
    if m: fort = (int(m[1]) * 10, int(m[2]) * 10); continue
    parts = [p.strip() for p in l.split("|")]
    head = parts[0].split(" ", 1)
    try: t = int(head[0])
    except ValueError: continue
    times.append(t)
    items = ([head[1]] if len(head) > 1 else []) + parts[1:]
    for it in items:
        bits = it.split(" ")
        if len(bits) < 2: continue
        key, val = bits[0], bits[1]
        if val == "x":
            deaths.setdefault(key, t); continue
        v = val.split(",")
        x, z = int(v[0]) * 10, int(v[1]) * 10
        tracks.setdefault(key, []).append((t, x, z, v[2:] if len(v) > 2 else []))

fig, ax = plt.subplots(figsize=(11, 11), dpi=110)
ax.set_facecolor("#dfe8f0")
style = lambda k: ("#d4a017", 2.6, "флагман") if k == "F" else ("#c0392b", 1.4, "враг " + k[1:]) if k.startswith("E") else ("#2e86c1", 1.4, {"G": "страж", "W": "ведомый", "M": "торговец"}.get(k[1:2], k) + " " + k[2:])
for k, pts in tracks.items():
    c, w, name = style(k)
    xs = [p[1] for p in pts]; zs = [p[2] for p in pts]
    ax.plot(xs, zs, color=c, lw=w, alpha=0.85, label=name + (" (T)" if "T" in k else ""))
    ax.plot(xs[-1], zs[-1], "o", color=c, ms=7 if k == "F" else 5)
    ax.annotate(k, (xs[-1], zs[-1]), fontsize=8, xytext=(4, 4), textcoords="offset points")
    if k in deaths: ax.plot(xs[-1], zs[-1], "x", color="black", ms=12, mew=2.5)
    if k == "F":
        for t, x, z, extra in pts[::15]:
            ax.annotate(str(t), (x, z), fontsize=6, color="#7d6608")
if fort:
    ax.plot(*fort, "s", color="#555", ms=16); ax.annotate("крепость", fort, fontsize=9, xytext=(8, -12), textcoords="offset points")
ax.set_aspect("equal"); ax.grid(alpha=0.3)
ax.set_title(f"Карта боя {min(times) if times else 0}–{max(times) if times else 0} с (x/z, м; × — потоплен; цифры — секунды флагмана)")
ax.legend(fontsize=7, loc="upper right", ncol=2)
fig.tight_layout(); fig.savefig(out)
print(out, "кораблей", len(tracks), "кадров", len(times))
