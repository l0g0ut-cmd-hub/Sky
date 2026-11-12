using UnityEngine;

namespace SkyTide
{
    /// <summary>Одна геометрия в пикселях (начало сверху слева) для рисования и попадания пальца.</summary>
    public struct HudLayout
    {
        public Rect safe, port, starboard, mode, clouds, ship, course, speed, fps, version, hint, radar;
        public Vector2 wheel;
        public float unit, radius;

        public static HudLayout Build(int width, int height, Rect safeArea, float stickRadius)
        {
            HudLayout l = new HudLayout();
            l.safe = safeArea;
            l.unit = Mathf.Min(safeArea.height, safeArea.width);
            float u = l.unit, m = u * 0.03f, gap = u * 0.025f;
            float w = u * 0.285f, h = u * 0.14f;
            l.starboard = new Rect(safeArea.xMax - m - w, safeArea.yMax - u * 0.09f - h, w, h);
            l.port = new Rect(l.starboard.x - gap - w, l.starboard.y, w, h);
            l.mode = new Rect(l.port.x, l.port.y - gap - u * 0.072f, w * 2f + gap, u * 0.072f);
            l.clouds = new Rect(safeArea.xMax - m - u * 0.43f, safeArea.y + m, u * 0.43f, u * 0.105f);
            l.ship = new Rect(l.clouds.x, l.clouds.yMax + gap, l.clouds.width, u * 0.08f);
            l.course = new Rect(safeArea.center.x - u * 0.15f, safeArea.y + m, u * 0.3f, u * 0.075f);
            l.fps = new Rect(safeArea.x + m, safeArea.y + m, u * 0.22f, u * 0.065f);
            l.speed = new Rect(safeArea.x + m, safeArea.yMax - u * 0.425f, u * 0.34f, u * 0.07f);
            float rd = Mathf.Min(u * 0.3f, l.speed.y - l.fps.yMax - gap * 2f);
            l.radar = new Rect(safeArea.x + m, l.fps.yMax + gap, rd, rd);
            l.version = new Rect(l.port.x, safeArea.yMax - u * 0.065f, l.mode.width, u * 0.04f);
            l.radius = u * stickRadius;
            l.wheel = new Vector2(safeArea.x + u * 0.19f, safeArea.yMax - u * 0.215f);
            float left = l.speed.xMax + m;
            l.hint = new Rect(left, safeArea.yMax - u * 0.1f, Mathf.Max(0f, l.port.x - m - left), u * 0.075f);
            return l;
        }

        public Vector2 ClampWheel(Vector2 origin)
        {
            float r = radius * 1.15f;
            return new Vector2(Mathf.Clamp(origin.x, safe.x + r, safe.center.x - r),
                Mathf.Clamp(origin.y, safe.y + r, safe.yMax - r));
        }
    }
}
