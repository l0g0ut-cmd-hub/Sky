using UnityEngine;

namespace SkyTide
{
    /// <summary>Одна геометрия в пикселях (начало сверху слева) для рисования и попадания пальца.</summary>
    public struct HudLayout
    {
        public Rect safe, port, starboard, mode, ammo, board, spy, auto, clouds, ship, course, speed, fps, version, hint, radar, radarInfo, radarModule, console, tabEnemy, tabBerth, tabRadar, telegraph, pause;
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
            float w = Mathf.Max(u * 0.33f, Mathf.Min(u * 0.39f, safeArea.width * 0.18f)) * ButtonScale * 0.84f, h = u * 0.15f * ButtonScale * 0.84f;   // ×0,84 — открыть обзор (владелец 16.11)   // кнопки бортов: поле внутри рамки вмещает название, пушку и состояние крупно
            float rgap = gap * 0.8f, rh = u * 0.06f;
            l.starboard = new Rect(safeArea.xMax - m * 0.5f - w, safeArea.yMax - u * 0.075f - h, w, h);
            l.port = new Rect(l.starboard.x - rgap - w, l.starboard.y, w, h);
            l.mode = new Rect(l.port.x, l.port.y - rgap - rh, w * 2f + rgap, rh);
            l.auto = new Rect(l.mode.x, l.mode.y - rgap - rh, l.mode.width, rh);   // «АВТОБОЙ» — над режимом залпа
            // Строка режима делится: слева — залп волной/весь борт, справа — заряд (ядра/книппели).
            float half = (l.mode.width - rgap) * 0.5f;
            l.ammo = new Rect(l.mode.x + half + rgap, l.mode.y, half, l.mode.height);
            l.mode = new Rect(l.mode.x, l.mode.y, half, l.mode.height);
            l.clouds = new Rect(safeArea.xMax - m - u * 0.43f, safeArea.y + m, u * 0.43f, u * 0.105f);
            l.ship = new Rect(l.clouds.x, l.clouds.yMax + gap, l.clouds.width, u * 0.08f);
            l.course = new Rect(safeArea.center.x - u * 0.2f, safeArea.y + m, u * 0.4f, u * 0.075f);   // шире: лента компаса была заметно уже полосы корпуса
            // Левая колонка — одна ширина и один левый край для всего: FPS+ТРУБА, радар, плашка радара, плашка хода;
            // штурвал — по её середине; зазоры между блоками равные (владелец 15.11: «выровняй нормально»).
            float colW = u * 0.34f, colX = safeArea.x + m;
            float topH = u * 0.065f, fpsW = u * 0.2f;   // шире: в новых кнопках с завитками текст не лезет на орнамент
            l.fps = new Rect(colX, safeArea.y + m, fpsW, topH);
            l.spy = new Rect(colX + fpsW + gap, l.fps.y, u * 0.26f, topH);   // «ТРУБА» шире: в половине колонки надпись ужималась в мелочь
            // Радар со вкладками — одна картинка (ui_radar_module.png 1536×1024): круг радара слева (центр 357,357, r 247),
            // вкладки ВРАГ / ПРИЧАЛ / РАДАР справа, ниже — вкладка хода. Высота — сколько влезает до штурвала.
            l.radius = u * stickRadius;
            // Пульт «штурвал + телеграф» (ui_console.png 1536×1024): гнездо штурвала — центр (385,560), латунь — y 169…908;
            // ступица телеграфа — (1118,728). Масштаб — по штурвалу (диаметр 2,35 радиуса = 560 пикселей пульта).
            float ck = l.radius * 2.0f / 560f;   // меньше (владелец 16.11)
            float edgeX = safeArea.x * 0.4f + m * 0.2f;   // ближе к краю экрана — в запас безопасной области
            l.console = new Rect(edgeX - 20f * ck, safeArea.yMax - m * 0.5f - 908f * ck, 1536f * ck, 1024f * ck);
            l.wheel = new Vector2(l.console.x + 385f * ck, l.console.y + 560f * ck);
            float mTop = l.fps.yMax + gap, mBottom = l.console.y + 169f * ck - gap;
            float mH = Mathf.Max(u * 0.3f, Mathf.Min(u * 0.41f, mBottom - mTop)), k = mH / 1024f;   // меньше: загораживал обзор (владелец 16.11)
            l.radarModule = new Rect(edgeX - 4f * k, mTop, 1536f * k, mH);   // и левее — к краю экрана
            float mx = l.radarModule.x, my = l.radarModule.y;
            l.radar = new Rect(mx + (357f - 244f) * k, my + (377.5f - 244f) * k, 488f * k, 488f * k);   // круг обода по замеру: центр (357; 377,5), r 244   // стекло радара (касание — масштаб)
            l.tabEnemy = new Rect(mx + 900f * k, my + 119f * k, 505f * k, 112f * k);
            l.tabBerth = new Rect(mx + 900f * k, my + 318f * k, 505f * k, 112f * k);
            l.tabRadar = new Rect(mx + 900f * k, my + 513f * k, 505f * k, 112f * k);
            l.speed = new Rect(mx + 340f * k, my + 734f * k, 1065f * k, 127f * k);
            l.radarInfo = l.tabEnemy;
            l.version = new Rect(l.port.x, safeArea.yMax - u * 0.065f, l.ammo.xMax - l.port.x, u * 0.04f);
            l.pause = new Rect(safeArea.xMax - m - u * 0.085f, safeArea.y + m, u * 0.085f, u * 0.075f);   // кнопка паузы — в правом верхнем углу
            // Машинный телеграф: полукруглый циферблат справа от штурвала (5 секторов: назад, стоп, малый, средний, полный).
            l.telegraph = new Rect(l.console.x + 740f * ck, l.console.y + 380f * ck, 780f * ck, 440f * ck);   // поле касаний циферблата
            float left = l.telegraph.xMax + m;   // вкладка хода теперь наверху, в модуле радара
            l.hint = new Rect(left, safeArea.yMax - u * 0.1f, Mathf.Max(0f, l.port.x - m - left), u * 0.075f);
            // «НА АБОРДАЖ» — над подсказкой по центру между штурвалом и кнопками бортов (появляется, только когда можно).
            l.board = new Rect(l.hint.center.x - u * 0.17f, l.hint.y - gap - u * 0.09f, u * 0.34f, u * 0.09f);
            return l;
        }

        /// <summary>Картинка телеграфа (telegraph_dial.png): размер, ось рычага и углы секторов (0 — назад, слева; 4 — полный, справа).</summary>
        public Vector2 TelegraphHub => new Vector2(console.x + console.width * 1118f / 1536f, console.y + console.height * 728f / 1024f);
        // Центры цветных секторов пульта (замер по ui_console.png: перегородки на 35°, 71°, 110°, 146°, края 0° и 180°).
        static readonly float[] GearAngles = { 163f, 128f, 90.5f, 53f, 17.5f };
        public static float GearAngle(int g) => GearAngles[Mathf.Clamp(g, 0, 4)];   // градусы от «вправо», против часовой
        public Vector2 GearPoint(int g)
        {
            float a = GearAngle(g) * Mathf.Deg2Rad, r = console.width * 230f / 1536f;
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
                // Панель — картинка ui_pause.png (999×1265): шапка с табличкой, поле строк, нижняя плашка.
                float h = Mathf.Min(unit * 0.96f, safe.height * 0.96f), w = h * 999f / 1265f;
                return new Rect(safe.center.x - w * 0.5f, safe.center.y - h * 0.5f, w, h);
            }
        }
        public Rect PauseRow(int i)
        {
            Rect p = PausePanel;
            // Поле строк на картинке: x 41…958 из 999, y 286…1140 из 1265; строки делят его поровну с равными зазорами.
            float fy0 = p.y + p.height * 286f / 1265f, fy1 = p.y + p.height * 1140f / 1265f, pad = p.height * 0.0096f;
            float rh = p.height * 0.0812f, gap = (fy1 - fy0 - pad * 2f - rh * PauseRows) / (PauseRows - 1);
            float x0 = p.x + p.width * 41f / 999f + p.height * 0.029f, x1 = p.x + p.width * 958f / 999f - p.height * 0.029f;
            return new Rect(x0, fy0 + pad + i * (rh + gap), x1 - x0, rh);
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
