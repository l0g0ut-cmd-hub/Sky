using UnityEngine;

namespace SkyTide
{
    /// <summary>Одна геометрия в пикселях (начало сверху слева) для рисования и попадания пальца.</summary>
    public struct HudLayout
    {
        public Rect safe, port, starboard, mode, clouds, ship, course, speed, fps, version, hint, radar, telegraph, pause;
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
            l.clouds = new Rect(safeArea.xMax - m - u * 0.43f, safeArea.y + m, u * 0.43f, u * 0.105f);
            l.ship = new Rect(l.clouds.x, l.clouds.yMax + gap, l.clouds.width, u * 0.08f);
            l.course = new Rect(safeArea.center.x - u * 0.15f, safeArea.y + m, u * 0.3f, u * 0.075f);
            l.fps = new Rect(safeArea.x + m, safeArea.y + m, u * 0.22f, u * 0.065f);
            l.speed = new Rect(safeArea.x + m, safeArea.yMax - u * 0.425f, u * 0.34f, u * 0.07f);
            // Под радаром — две строки дальностей (враг, причал): место под них вычитается из радара на низких экранах.
            float rd = Mathf.Min(u * 0.3f, l.speed.y - l.fps.yMax - gap * 2f - u * 0.125f);
            l.radar = new Rect(safeArea.x + m, l.fps.yMax + gap, rd, rd);
            l.version = new Rect(l.port.x, safeArea.yMax - u * 0.065f, l.mode.width, u * 0.04f);
            l.pause = new Rect(safeArea.xMax - m - u * 0.085f, safeArea.y + m, u * 0.085f, u * 0.075f);   // кнопка паузы — в правом верхнем углу
            l.radius = u * stickRadius;
            l.wheel = new Vector2(safeArea.x + u * 0.19f, safeArea.yMax - u * 0.215f);
            // Рукоять хода: вертикальная шкала справа от штурвала, подписи ступеней — правее неё.
            float th = l.radius * 2.2f;
            l.telegraph = new Rect(l.wheel.x + l.radius * 1.32f, l.wheel.y - th * 0.5f, u * 0.03f, th);
            float left = Mathf.Max(l.speed.xMax, l.telegraph.xMax + u * 0.17f) + m;
            l.hint = new Rect(left, safeArea.yMax - u * 0.1f, Mathf.Max(0f, l.port.x - m - left), u * 0.075f);
            return l;
        }

        /// <summary>Меню паузы: панель по центру и её строки (одна геометрия для рисования и касаний).</summary>
        public const int PauseRows = 5;
        public Rect PausePanel
        {
            get
            {
                float w = Mathf.Min(safe.width * 0.42f, unit * 0.86f), h = unit * 0.78f;
                return new Rect(safe.center.x - w * 0.5f, safe.center.y - h * 0.5f, w, h);
            }
        }
        public Rect PauseRow(int i)
        {
            Rect p = PausePanel;
            float rh = unit * 0.1f, gap = unit * 0.022f;
            return new Rect(p.x + unit * 0.05f, p.y + unit * 0.14f + i * (rh + gap), p.width - unit * 0.1f, rh);
        }

        public Vector2 ClampWheel(Vector2 origin)
        {
            float r = radius * 1.15f;
            return new Vector2(Mathf.Clamp(origin.x, safe.x + r, safe.center.x - r),
                Mathf.Clamp(origin.y, safe.y + r, safe.yMax - r));
        }
    }
}
