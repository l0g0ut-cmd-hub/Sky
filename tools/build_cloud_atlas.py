"""
Сборка атласа облаков из листов ChatGPT (PNG с прозрачностью).

    python3 tools/build_cloud_atlas.py --neutral a.png --lit b.png --shadow c.png --strips d.png --towers e.png

Листы neutral/lit/shadow — одни и те же 6 форм (сетка 3×2) в трёх вариантах света: шейдер плавно переводит облако
между ними по углу к солнцу. strips — 6 полос дымки (по строкам), towers — грозовые башни (по столбцам).
Пишет Assets/_Project/Art/CloudAtlas.png (4096×2048) и Scripts/World/CloudAtlas.cs (разметка).
"""
import argparse, os
import numpy as np, cv2
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')


def extract(path, mode):
    im = np.asarray(Image.open(path).convert('RGBA')).astype(np.uint8)
    H, W = im.shape[:2]
    a = im[..., 3]
    n, lab, st, cen = cv2.connectedComponentsWithStats((a > 40).astype(np.uint8), 8)
    idx = [i for i in range(1, n) if st[i, 4] > 6000]
    if mode == 'grid':
        idx = sorted(idx, key=lambda i: (int(cen[i][1] > H / 2), cen[i][0]))
    elif mode == 'rows':
        idx = sorted(idx, key=lambda i: cen[i][1])
    else:
        idx = sorted(idx, key=lambda i: cen[i][0])
    big = np.isin(lab, idx)
    # Мелкие клочья относим к ближайшему крупному облаку.
    L = cv2.distanceTransformWithLabels((~big).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)[1]
    ys, xs = np.nonzero(big)
    lut = np.zeros(L.max() + 1, np.int32)
    lut[L[ys, xs]] = lab[ys, xs]
    owner = lut[L]
    out = []
    for i in idx:
        mine = (owner == i) & (a > 3)
        ys, xs = np.nonzero(mine)
        y0, y1 = max(0, ys.min() - 4), min(H, ys.max() + 5)
        x0, x1 = max(0, xs.min() - 4), min(W, xs.max() + 5)
        c = im[y0:y1, x0:x1].copy()
        c[..., 3] = np.where(mine[y0:y1, x0:x1], c[..., 3], 0)
        # Цвет под прозрачностью — ближайший видимый (без тёмных/белых ореолов на мипмапах).
        mk = (c[..., 3] > 8).astype(np.uint8)
        L2 = cv2.distanceTransformWithLabels(1 - mk, cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)[1]
        yy, xx = np.nonzero(mk)
        lt = np.zeros((L2.max() + 1, 3), np.uint8)
        lt[L2[yy, xx]] = c[yy, xx, :3]
        c[..., :3] = lt[L2]
        out.append(c)
    return out


def main():
    ap = argparse.ArgumentParser()
    for k in ('neutral', 'lit', 'shadow', 'strips', 'towers'):
        ap.add_argument('--' + k, required=True)
    ap.add_argument('--scale', type=float, default=0.93)
    a = ap.parse_args()
    S = {'lit': extract(a.lit, 'grid'), 'neutral': extract(a.neutral, 'grid'), 'shadow': extract(a.shadow, 'grid'),
         'strips': extract(a.strips, 'rows'), 'towers': extract(a.towers, 'cols')}
    for k in ('lit', 'neutral', 'shadow'):
        assert len(S[k]) == 6, k + ': нужно ровно 6 облаков, найдено %d' % len(S[k])

    AW, AH, pad = 4096, 2048, 6
    items = [(k, i, c) for k in ('lit', 'neutral', 'shadow', 'strips', 'towers') for i, c in enumerate(S[k])]
    items = [(k, i, np.asarray(Image.fromarray(c).resize((max(1, int(c.shape[1] * a.scale)), max(1, int(c.shape[0] * a.scale))), Image.LANCZOS))) for k, i, c in items]
    items.sort(key=lambda t: -t[2].shape[0])
    atlas = np.zeros((AH, AW, 4), np.uint8)
    x = y = rowh = 0
    rects = {}
    for k, i, c in items:
        h, w = c.shape[:2]
        if x + w + pad > AW:
            x, y, rowh = 0, y + rowh + pad, 0
        assert y + h <= AH, 'облака не влезли в атлас — уменьшите --scale'
        atlas[y:y + h, x:x + w] = c
        rects[(k, i)] = (x, y, w, h)
        x += w + pad
        rowh = max(rowh, h)
    Image.fromarray(atlas).save(os.path.join(ROOT, 'Assets/_Project/Art/CloudAtlas.png'), optimize=True)

    def r(k, i):
        x, y, w, h = rects[(k, i)]
        return 'new Rect(%.5ff, %.5ff, %.5ff, %.5ff)' % (x / AW, 1 - (y + h) / AH, w / AW, h / AH)

    L = ['        /// <summary>Кучевые облака: [i] = (против солнца, нейтральное, в тени) одной формы.</summary>',
         '        public static readonly Rect[][] Puffs =', '        {']
    L += ['            new Rect[] { %s, %s, %s },' % (r('lit', i), r('neutral', i), r('shadow', i)) for i in range(6)]
    L += ['        };', '        /// <summary>Длинные полосы и дымка.</summary>', '        public static readonly Rect[] Strips =', '        {']
    L += ['            %s,' % r('strips', i) for i in range(len(S['strips']))]
    L += ['        };', '        /// <summary>Грозовые башни (дальний план).</summary>', '        public static readonly Rect[] Towers =', '        {']
    L += ['            %s,' % r('towers', i) for i in range(len(S['towers']))]
    L += ['        };', '        public const float Aspect = %.1ff;   // ширина / высота атласа' % (AW / AH)]
    cs = ('using UnityEngine;\n\nnamespace SkyTide\n{\n    /// <summary>\n'
          '    /// Разметка атласа Art/CloudAtlas.png. Сгенерировано tools/build_cloud_atlas.py — не править руками.\n'
          '    /// UV-прямоугольники, начало снизу слева.\n    /// </summary>\n    public static class CloudAtlas\n    {\n'
          + '\n'.join(L) + '\n    }\n}\n')
    open(os.path.join(ROOT, 'Assets/_Project/Scripts/World/CloudAtlas.cs'), 'w', encoding='utf-8').write(cs)
    print('атлас: высота занята', y + rowh, 'из', AH)


if __name__ == '__main__':
    main()
