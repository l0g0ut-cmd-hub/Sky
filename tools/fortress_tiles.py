#!/usr/bin/env python3
"""Материалы крепости из нарисованных плиток (ChatGPT): кладка, доски, скала → детальные текстуры для шейдера Fortress.

  python3 tools/fortress_tiles.py wall.png deck.png rock.png Assets/_Project/Models

Модель Meshy несёт цвет (~75 см на тексель при масштабе ×3), плитки дают резкость вблизи: шейдер умножает
цвет модели на плитку×2. Поэтому плитка приводится к «нейтральной»: в линейном пространстве каждый канал
делится на среднее (в основном по яркости, оттенка — треть) и умножается на 0.4 (0.4 = «не менять цвет»,
шейдер делит на 0.4), затем обратно в sRGB.
Края сводятся (если шов заметен): узкая полоса у каждого края смешивается с зеркальной стороной.
Выход: SkyFortress_Wall.png, SkyFortress_Deck.png, SkyFortress_Rock.png, 1024×1024.
"""
import sys, os
import numpy as np
from PIL import Image


NEUTRAL = 0.4   # значение «не менять цвет»: шейдер умножает на плитку / NEUTRAL (запас до 2.5× среднего)
CHROMA = 0.33   # доля оттенка плитки


def to_lin(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def to_srgb(c): return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0), 1 / 2.4) - 0.055)


def seam(a):
    """Отношение скачка через край к среднему скачку между соседними столбцами/строками."""
    inner_x = np.abs(np.diff(a, axis=1)).mean(); edge_x = np.abs(a[:, 0] - a[:, -1]).mean()
    inner_y = np.abs(np.diff(a, axis=0)).mean(); edge_y = np.abs(a[0] - a[-1]).mean()
    return edge_x / inner_x, edge_y / inner_y


def blend_edges(a, band):
    """Сводит края: в полосе band у края пиксели плавно тянутся к среднему между двумя сторонами шва."""
    h, w, _ = a.shape
    out = a.copy()
    t = (np.arange(band) + 0.5) / band            # 0 у края → 1 внутри
    k = (0.5 * (1 - t))[None, :, None]            # у самого края — полусумма сторон
    left = a[:, :band]; right = a[:, ::-1][:, :band]
    out[:, :band] = left * (1 - k) + right * k
    out[:, ::-1][:, :band] = right * (1 - k) + left * k
    a = out; out = a.copy()
    k = (0.5 * (1 - t))[:, None, None]
    top = a[:band]; bot = a[::-1][:band]
    out[:band] = top * (1 - k) + bot * k
    out[::-1][:band] = bot * (1 - k) + top * k
    return out


def main():
    srcs, dst = sys.argv[1:4], sys.argv[4]
    for path, name in zip(srcs, ('Wall', 'Deck', 'Rock')):
        im = Image.open(path).convert('RGB').resize((1024, 1024), Image.LANCZOS)
        a = np.asarray(im).astype(np.float64) / 255.0
        sx, sy = seam(a)
        if max(sx, sy) > 1.6:
            a = blend_edges(a, 20)
        sx2, sy2 = seam(a)
        lin = to_lin(a)
        mean = lin.reshape(-1, 3).mean(0)
        W = np.array([0.2126, 0.7152, 0.0722])
        per = lin / mean                                   # отношение по каналам: несёт и оттенок
        lum = (lin @ W) / (mean @ W)                       # отношение по яркости: только светотень
        # Оттенка — треть: у тёмно-коричневых плиток синий канал почти нулевой, и полное поканальное
        # отношение раздувает его в сине-жёлтые разводы.
        ratio = lum[..., None] * (1 - CHROMA) + per * CHROMA
        d = np.clip(ratio * NEUTRAL, 0, 1)
        out = (to_srgb(d) * 255).round().astype(np.uint8)
        Image.fromarray(out).save(os.path.join(dst, 'SkyFortress_%s.png' % name))
        print('%s: шов x %.2f y %.2f → %.2f %.2f; среднее (лин.) %s; пересвет %.2f%%' % (name, sx, sy, sx2, sy2, mean.round(3), (ratio * NEUTRAL > 1).mean() * 100))


if __name__ == '__main__':
    main()
