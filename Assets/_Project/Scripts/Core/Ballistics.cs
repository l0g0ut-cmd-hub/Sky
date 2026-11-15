namespace SkyTide
{
    /// <summary>
    /// Чистая баллистика без Unity (только float и System.MathF) — её гоняют быстрые тесты tools/logictests
    /// (dotnet, минута вместо 40-минутной сборки). Ось y вверх, x — горизонтальная дальность.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>Настильный угол (рад) попадания в точку (x по горизонтали, y по высоте) ядром со скоростью v.
        /// Нет решения (не достаёт) — float.NaN.</summary>
        public static float LowAngle(float x, float y, float v, float g)
        {
            float disc = v * v * v * v - g * (g * x * x + 2f * y * v * v);
            if (disc < 0f || x < 1f) return float.NaN;
            return System.MathF.Atan((v * v - System.MathF.Sqrt(disc)) / (g * x));
        }

        /// <summary>Как LowAngle, но в пределах наводки: нет решения — предельное возвышение (ядро ляжет недолётом).</summary>
        public static float ClampedLowAngle(float x, float y, float v, float g, float minRad, float maxRad)
        {
            float a = LowAngle(x, y, v, g);
            if (float.IsNaN(a)) a = maxRad;
            return a < minRad ? minRad : a > maxRad ? maxRad : a;
        }

        /// <summary>Время полёта до горизонтальной дальности x под углом theta.</summary>
        public static float FlightTime(float x, float v, float theta)
        {
            float c = v * System.MathF.Cos(theta);
            return x / (c > 1f ? c : 1f);
        }

        /// <summary>Предельная дальность на ровной высоте при угле maxRad.</summary>
        public static float MaxRange(float v, float g, float maxRad) { return v * v * System.MathF.Sin(2f * maxRad) / g; }

        /// <summary>
        /// Упреждение по движущейся цели: d0 — вектор от дула до цели сейчас (dx, dy, dz), rel — скорость цели
        /// относительно стреляющего. Возвращает время полёта t и точку прицела d0 + rel·t (через out).
        /// 10 итераций: при 4 на 500+ м и встречном ходе промах доходил до 9 м (поймано тестом).
        /// </summary>
        public static float Lead(float dx, float dy, float dz, float rx, float ry, float rz, float v, float g, float minRad, float maxRad,
            out float ax, out float ay, out float az, int iterations = 10)
        {
            float t = 0f;
            for (int i = 0; i < iterations; i++)
            {
                float px = dx + rx * t, pz = dz + rz * t, py = dy + ry * t;
                float x = System.MathF.Sqrt(px * px + pz * pz);
                float th = ClampedLowAngle(x, py, v, g, minRad, maxRad);
                t = FlightTime(x, v, th);
            }
            ax = dx + rx * t; ay = dy + ry * t; az = dz + rz * t;
            return t;
        }
    }
}
