"""
Сшивает панораму неба из 4 кусков по 90° (ChatGPT) в equirectangular 2:1 для Skybox/Panoramic.

    python3 tools/stitch_sky.py p1.png p2.png p3.png p4.png --base old_panorama.png --out Assets/_Project/Art/SkyPanorama.jpg

Куски идут слева направо, p1 — с солнцем в центре (в игре это направление +X, туда же светит свет).
Горизонт каждого куска ищется автоматически и ставится на середину высоты. Стыки: перекрытие с плавным
переходом + выравнивание яркости по обе стороны шва. Выше нарисованного неба — продолжение с затемнением к зениту,
ниже — продолжение моря облаков.
"""
import argparse
import numpy as np
import cv2
from PIL import Image

W, H = 6144, 3072
PPD = W / 360.0          # пикселей на градус
OVER = 96                # перекрытие на стыке (px)
STRETCH = 1.35           # вертикальное растяжение нарисованной части (небо уходит выше)


def horizon(a):
    lum = a.mean(2).mean(1)
    h = a.shape[0]
    r0, r1 = int(h * 0.5), int(h * 0.75)
    return r0 + int(np.argmax(lum[r0:r1]))


def piece(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    hz = horizon(a)
    w = int(round(90 * PPD)) + OVER
    sx = w / a.shape[1]
    sy = sx * STRETCH
    top = cv2.resize(a[:hz], (w, int(round(hz * sy))), interpolation=cv2.INTER_LANCZOS4)
    bot = cv2.resize(a[hz:], (w, int(round((a.shape[0] - hz) * sy))), interpolation=cv2.INTER_LANCZOS4)
    return top, bot


def extend_up(top, rows):
    """Продолжение неба вверх: верхние строки, всё сильнее размытые и затемнённые к зениту."""
    if rows <= 0:
        return top
    seed = top[:48].mean(0, keepdims=True)
    ext = np.repeat(seed, rows, 0)
    k = np.linspace(1, 0, rows)[:, None, None]          # 1 у нарисованного края, 0 в зените
    zenith = top[:48].mean((0, 1)) * 0.55
    noise = cv2.GaussianBlur(np.repeat(top[:rows % 64 + 64][::-1], rows // 64 + 2, 0)[:rows], (0, 0), 6)
    ext = (ext * 0.6 + noise * 0.4) * k + zenith * (1 - k)
    return np.concatenate([ext, top], 0)


def extend_down(bot, rows):
    if rows <= 0:
        return bot
    seed = bot[-64:]
    tiles = np.concatenate([seed[::-1], seed] * (rows // 128 + 2), 0)[:rows]
    k = np.linspace(1, 0, rows)[:, None, None]
    floor = bot[-64:].mean((0, 1)) * 0.7
    ext = cv2.GaussianBlur(tiles, (0, 0), 3) * k + floor * (1 - k)
    return np.concatenate([bot, ext], 0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('pieces', nargs=4)
    ap.add_argument('--out', required=True)
    ap.add_argument('--base', help='старая панорама 2:1 (солнце в центре) — подложка выше и ниже нарисованного пояса')
    a = ap.parse_args()

    cols = []
    bands = []
    for p in a.pieces:
        top, bot = piece(p)
        half = H // 2
        bands.append((min(top.shape[0], half), min(bot.shape[0], half)))
        top = extend_up(top, half - top.shape[0]) if top.shape[0] < half else top[-half:]
        bot = extend_down(bot, half - bot.shape[0]) if bot.shape[0] < half else bot[:half]
        cols.append(np.concatenate([top, bot], 0))

    # Выравнивание яркости по обе стороны каждого шва (включая шов 4→1).
    n = len(cols)
    for i in range(n):
        L, R = cols[i], cols[(i + 1) % n]
        mL = L[:, -OVER - 120:-OVER].mean(1, keepdims=True)
        mR = R[:, OVER:OVER + 120].mean(1, keepdims=True)
        mid = (mL + mR) / 2
        gL = np.clip(mid / np.maximum(mL, 1), 0.6, 1.6)
        gR = np.clip(mid / np.maximum(mR, 1), 0.6, 1.6)
        gL = cv2.GaussianBlur(gL, (1, 0), sigmaX=1, sigmaY=40) if False else gL
        span = 360
        rampL = np.clip((np.arange(L.shape[1]) - (L.shape[1] - span)) / span, 0, 1)[None, :, None]
        rampR = np.clip(1 - np.arange(R.shape[1]) / span, 0, 1)[None, :, None]
        cols[i] = L * (1 + (gL - 1) * rampL)
        cols[(i + 1) % n] = R * (1 + (gR - 1) * rampR)

    # Склейка с перекрытием.
    w90 = int(round(90 * PPD))
    out = np.zeros((H, W + OVER, 3), np.float32)
    wsum = np.zeros((1, W + OVER, 1), np.float32)
    for i, c in enumerate(cols):
        x0 = i * w90
        wt = np.ones(c.shape[1], np.float32)
        wt[:OVER] = np.linspace(0, 1, OVER)
        wt[-OVER:] = np.linspace(1, 0, OVER)
        out[:, x0:x0 + c.shape[1]] += c * wt[None, :, None]
        wsum[:, x0:x0 + c.shape[1]] += wt[None, :, None]
    # хвост за 360° переносим в начало (шов 4→1)
    out[:, :OVER] += out[:, W:W + OVER]
    wsum[:, :OVER] += wsum[:, W:W + OVER]
    out = out[:, :W] / np.maximum(wsum[:, :W], 1e-4)

    # Кусок 1 (солнце) должен быть в центре картинки: сейчас его центр на 45°.
    out = np.roll(out, W // 2 - (w90 // 2 + OVER // 2), axis=1)

    # Подложка: выше и ниже нарисованного пояса — старая панорама (тот же стиль), плавный переход.
    if a.base:
        base = cv2.resize(np.asarray(Image.open(a.base).convert('RGB')).astype(np.float32), (W, H), interpolation=cv2.INTER_CUBIC)
        up = min(b[0] for b in bands)
        dn = min(b[1] for b in bands)
        y = np.arange(H)
        feather = 220
        top_edge = H // 2 - up
        bot_edge = H // 2 + dn
        w = np.clip((y - top_edge) / feather, 0, 1) * np.clip((bot_edge - y) / feather, 0, 1)
        w = (w * w * (3 - 2 * w))[:, None, None]
        # подгоняем яркость подложки к поясу на границе
        gT = out[top_edge + feather:top_edge + feather + 60].mean((0, 1)) / np.maximum(base[top_edge + feather:top_edge + feather + 60].mean((0, 1)), 1)
        gB = out[bot_edge - feather - 60:bot_edge - feather].mean((0, 1)) / np.maximum(base[bot_edge - feather - 60:bot_edge - feather].mean((0, 1)), 1)
        g = np.where(y[:, None] < H // 2, gT[None, :], gB[None, :])[:, None, :]
        out = out * w + base * np.clip(g, 0.5, 2.0) * (1 - w)
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(a.out, quality=94)
    print('ok', out.shape)


if __name__ == '__main__':
    main()
