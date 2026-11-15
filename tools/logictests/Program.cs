using System;
using SkyTide;

// Быстрые тесты логики: баллистика, упреждение, дальность. Код 0 — всё прошло, 1 — есть провалы.
static class Program
{
    static int fails, passes;
    static void Check(bool ok, string what) { if (ok) passes++; else { fails++; if (fails <= 25) Console.WriteLine("ПРОВАЛ: " + what); } }
    const float G = 9.81f;

    // Полёт ядра: старт в 0, скорость (vx,vy,vz); цель движется из (tx,ty,tz) со скоростью (ux,uy,uz). Минимальный промах.
    static float Miss(float vx, float vy, float vz, float tx, float ty, float tz, float ux, float uy, float uz, float tmax = 30f)
    {
        float x = 0, y = 0, z = 0, best = float.MaxValue, dt = 0.002f;
        for (float t = 0; t < tmax; t += dt)
        {
            vy -= G * dt; x += vx * dt; y += vy * dt; z += vz * dt;
            float px = tx + ux * (t + dt), py = ty + uy * (t + dt), pz = tz + uz * (t + dt);
            float d = MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py) + (z - pz) * (z - pz));
            if (d < best) best = d;
            if (y < -400f) break;
        }
        return best;
    }

    static int Main()
    {
        // 1) Неподвижная цель: угол LowAngle приводит ядро в точку (±1.5 м) на всей сетке дальностей и высот.
        foreach (float v in new[] { 100f, 125f, 160f })
            for (float x = 40f; x <= 1400f; x += 45f)
                for (float y = -80f; y <= 80f; y += 20f)
                {
                    float a = Ballistics.LowAngle(x, y, v, G);
                    if (float.IsNaN(a)) continue;
                    float m = Miss(v * MathF.Cos(a), v * MathF.Sin(a), 0f, x, y, 0f, 0f, 0f, 0f);
                    Check(m < 1.5f, $"неподвижная цель v={v} x={x} y={y}: промах {m:F2} м");
                }

        // 2) Дальность: чуть ближе предела — решение есть, дальше предела (на той же высоте, угол ≤45°) — нет.
        foreach (float v in new[] { 100f, 125f })
        {
            float r45 = Ballistics.MaxRange(v, G, MathF.PI / 4f);
            Check(!float.IsNaN(Ballistics.LowAngle(r45 * 0.98f, 0f, v, G)), $"v={v}: нет решения у 98% предела");
            Check(float.IsNaN(Ballistics.LowAngle(r45 * 1.03f, 0f, v, G)), $"v={v}: решение за пределом");
        }

        // 3) Упреждение: свой ход (снаряд уносит скорость корабля) и цель идёт поперёк/навстречу/вдогонку.
        //    Промах < 4 м (корпус ~ 10–14 м) в пределах наводки 24°.
        float maxEl = 24f * MathF.PI / 180f, minEl = -8f * MathF.PI / 180f, sv = 125f;
        var rnd = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            float dist = 120f + (float)rnd.NextDouble() * 700f, bearing = (float)rnd.NextDouble() * MathF.PI * 2f;
            float tx = MathF.Cos(bearing) * dist, tz = MathF.Sin(bearing) * dist, ty = -10f + (float)rnd.NextDouble() * 20f;
            float th = (float)rnd.NextDouble() * MathF.PI * 2f, ts = 10f + (float)rnd.NextDouble() * 18f;
            float ux = MathF.Cos(th) * ts, uz = MathF.Sin(th) * ts;
            float oh = (float)rnd.NextDouble() * MathF.PI * 2f, os = (float)rnd.NextDouble() * 40f;
            float ox = MathF.Cos(oh) * os, oz = MathF.Sin(oh) * os;
            float t = Ballistics.Lead(tx, ty, tz, ux - ox, 0f, uz - oz, sv, G, minEl, maxEl, out float ax, out float ay, out float az);
            float hx = MathF.Sqrt(ax * ax + az * az);
            float a = Ballistics.LowAngle(hx, ay, sv, G);
            if (float.IsNaN(a) || a > maxEl) continue;   // вне досягаемости — игра не стреляет
            float dx = ax / hx, dz = az / hx;
            float m = Miss(dx * MathF.Cos(a) * sv + ox, MathF.Sin(a) * sv, dz * MathF.Cos(a) * sv + oz, tx, ty, tz, ux, 0f, uz);
            Check(m < 4f, $"упреждение: дист {dist:F0} цель {ts:F0} м/с свой {os:F0} м/с: промах {m:F1} м (t={t:F2})");
        }

        // 4) Предел наводки: за пределом ClampedLowAngle отдаёт максимум, а не NaN.
        Check(Math.Abs(Ballistics.ClampedLowAngle(5000f, 0f, 125f, G, minEl, maxEl) - maxEl) < 1e-5f, "за пределом не максимум");
        Check(Ballistics.ClampedLowAngle(80f, -60f, 125f, G, minEl, maxEl) >= minEl, "ниже минимума наводки");

        Console.WriteLine($"логика: {passes} проверок прошло, провалов {fails}");
        return fails == 0 ? 0 : 1;
    }
}
