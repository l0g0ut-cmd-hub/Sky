using UnityEngine;

namespace SkyTide
{
    /// <summary>Одна геометрия в пикселях (начало сверху слева) для рисования и попадания пальца.</summary>
    public struct HudLayout
    {
        public Rect safe, port, starboard, mode, ammo, board, spy, auto, clouds, ship, course, speed, fps, version, hint, radar, telegraph, pause;
        public Vector2 wheel;
        public float unit, radius;

        /// <summary>Размер кнопок бортов (настройка в паузе): 1 — обычные, 1.2 — крупные.</summary>
        public static float ButtonScale = 1f;

        public static HudLayout Build(int width, int height, Rect safeArea, float stickRadius)
        {
            HudLayout l = new HudLayout();
            l.safe = safeArea;
            l.unit = Mathf.Min(safeArea.height, safeArea.width);
            float u = l.unit, m = u * 0.03f, gap = u * 0.025f;
            float w = Mathf.Max(u * 0.33f, Mathf.Min(u * 0.39f, safeArea.width * 0.18f)) * ButtonScale, h = u * 0.15f * ButtonScale;   // кнопки бортов: поле внутри рамки вмещает название, пушку и состояние крупно
            l.starboard = new Rect(safeArea.xMax - m - w, safeArea.yMax - u * 0.09f - h, w, h);
            l.port = new Rect(l.starboard.x - gap - w, l.starboard.y, w, h);
            l.mode = new Rect(l.port.x, l.port.y - gap - u * 0.072f, w * 2f + gap, u * 0.072f);
            l.auto = new Rect(l.mode.x, l.mode.y - gap - u * 0.072f, l.mode.width, u * 0.072f);   // «АВТОБОЙ» — над режимом залпа
            // Строка режима делится: слева — залп волной/весь борт, справа — заряд (ядра/книппели).
            float half = (l.mode.width - gap) * 0.5f;
            l.ammo = new Rect(l.mode.x + half + gap, l.mode.y, half, l.mode.height);
            l.mode = new Rect(l.mode.x, l.mode.y, half, l.mode.height);
            l.clouds = new Rect(safeArea.xMax - m - u * 0.43f, safeArea.y + m, u * 0.43f, u * 0.105f);
            l.ship = new Rect(l.clouds.x, l.clouds.yMax + gap, l.clouds.width, u * 0.08f);
            l.course = new Rect(safeArea.center.x - u * 0.2f, safeArea.y + m, u * 0.4f, u * 0.075f);   // шире: лента компаса была заметно уже полосы корпуса
            l.fps = new Rect(safeArea.x + m, safeArea.y + m, u * 0.22f, u * 0.065f);
            l.speed = new Rect(safeArea.x + m, safeArea.yMax - u * 0.46f, u * 0.34f, u * 0.07f);   // выше штурвала: на 0.425 верхняя рукоять и шарик джойстика лезли на текст (видео сборки 102)
            // Под радаром — две строки дальностей (враг, причал): место под них вычитается из радара на низких экранах.
            float rd = Mathf.Min(u * 0.3f, l.speed.y - l.fps.yMax - gap * 2f - u * 0.125f);
            l.radar = new Rect(safeArea.x + m, l.fps.yMax + gap, rd, rd);
            l.version = new Rect(l.port.x, safeArea.yMax - u * 0.065f, l.ammo.xMax - l.port.x, u * 0.04f);
            l.pause = new Rect(safeArea.xMax - m - u * 0.085f, safeArea.y + m, u * 0.085f, u * 0.075f);   // кнопка паузы — в правом верхнем углу
            l.radius = u * stickRadius;
            l.wheel = new Vector2(safeArea.x + u * 0.19f, safeArea.yMax - u * 0.215f);
            // Машинный телеграф: полукруглый циферблат справа от штурвала (5 секторов: назад, стоп, малый, средний, полный).
            float dw = u * 0.31f, dh = dw * TelegraphH / TelegraphW;
            l.telegraph = new Rect(l.wheel.x + l.radius * 1.2f, l.wheel.y - dh * 0.5f, dw, dh);
            float left = Mathf.Max(l.speed.xMax, l.telegraph.xMax) + m;
            l.hint = new Rect(left, safeArea.yMax - u * 0.1f, Mathf.Max(0f, l.port.x - m - left), u * 0.075f);
            // «НА АБОРДАЖ» — над подсказкой по центру между штурвалом и кнопками бортов (появляется, только когда можно).
            l.board = new Rect(l.hint.center.x - u * 0.17f, l.hint.y - gap - u * 0.09f, u * 0.34f, u * 0.09f);
            // Подзорная труба — плашка «ТРУБА» справа от счётчика FPS.
            l.spy = new Rect(l.fps.xMax + gap, l.fps.y, u * 0.2f, l.fps.height);   // был квадрат 0,085 — внутри рамки «×4» сжималось в точку, кнопку не было видно
            return l;
        }

        /// <summary>Картинка телеграфа (telegraph_dial.png): размер, ось рычага и углы секторов (0 — назад, слева; 4 — полный, справа).</summary>
        public const float TelegraphW = 1222f, TelegraphH = 876f, HubX = 600f, HubY = 594f;
        public Vector2 TelegraphHub => new Vector2(telegraph.x + telegraph.width * HubX / TelegraphW, telegraph.y + telegraph.height * HubY / TelegraphH);
        static readonly float[] GearAngles = { 170f, 130f, 86f, 44f, 10f };   // центры секторов циферблата (замер по картинке)
        public static float GearAngle(int g) => GearAngles[Mathf.Clamp(g, 0, 4)];   // градусы от «вправо», против часовой
        public Vector2 GearPoint(int g)
        {
            float a = GearAngle(g) * Mathf.Deg2Rad, r = telegraph.width * 0.3f;
            return TelegraphHub + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * r;
        }
        public int GearAt(Vector2 p)
        {
            Vector2 h = TelegraphHub;
            if (p.y > h.y) return p.x < h.x ? 0 : 4;
            float a = Mathf.Atan2(h.y - p.y, p.x - h.x) * Mathf.Rad2Deg;
            int best = 0; for (int g = 1; g < 5; g++) if (Mathf.Abs(GearAngles[g] - a) < Mathf.Abs(GearAngles[best] - a)) best = g;
            return best;
        }

        /// <summary>Меню паузы: панель по центру и её строки (одна геометрия для рисования и касаний).</summary>
        public const int PauseRows = 7;
        public Rect PausePanel
        {
            get
            {
                float w = Mathf.Min(safe.width * 0.42f, unit * 0.86f), h = unit * 0.88f;
                return new Rect(safe.center.x - w * 0.5f, safe.center.y - h * 0.5f, w, h);
            }
        }
        public Rect PauseRow(int i)
        {
            Rect p = PausePanel;
            float rh = unit * 0.086f, gap = unit * 0.014f;
            return new Rect(p.x + unit * 0.05f, p.y + unit * 0.13f + i * (rh + gap), p.width - unit * 0.1f, rh);
        }

        public Vector2 ClampWheel(Vector2 origin)
        {
            float r = radius * 1.15f;
            // Плавающий штурвал не залезает на плашку хода и строки радара над ней (видео сборки 107): верх — ниже плашки.
            float top = Mathf.Min(speed.yMax + r * 0.95f, safe.yMax - r);
            return new Vector2(Mathf.Clamp(origin.x, safe.x + r, safe.center.x - r),
                Mathf.Clamp(origin.y, Mathf.Max(safe.y + r, top), safe.yMax - r));
        }
    }
}
