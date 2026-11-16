using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Движение кораблей между собой (владелец 16.11: «корабли избегали столкновения, мирные — чтобы в конфликт не вступали,
    /// но иногда бы это случалось и разгорался конфликт»).
    /// • Avoid — расхождение: кто сблизится ближе суммы полудлин + запас за ближайшие 14 с, тот отворачивает (правило —
    ///   уходить вправо, к своему правому борту) и сбавляет ход на встречном курсе. Свою цель в бою не обходит.
    /// • Tick — корпуса не проходят друг сквозь друга (раздвигаются по отрезкам корпусов). Удар на ходу корсара о корабль
    ///   гарнизона — повод: корсар решает по нраву (TargetShip.Collided), может и открыть огонь.
    /// </summary>
    public static class Traffic
    {
        struct Body { public Transform t; public ShipController c; public float half; public TargetShip enemy; public AllyShip ally; }
        static readonly List<Body> bodies = new List<Body>();
        static int frame = -1;

        static void Collect()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            bodies.Clear();
            foreach (TargetShip t in TargetShip.All)
                if (t != null && t.Alive && t.Controller != null && t.gameObject.activeInHierarchy)
                    bodies.Add(new Body { t = t.transform, c = t.Controller, half = t.Length * 0.5f, enemy = t });
            foreach (AllyShip a in AllyShip.All)
                if (a != null && a.Alive && a.Controller != null && a.gameObject.activeInHierarchy)
                    bodies.Add(new Body { t = a.transform, c = a.Controller, half = Mathf.Max(10f, a.Length * 0.5f), ally = a });
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            PlayerHull ph = PlayerHull.Instance;
            if (g != null && g.Ship != null && ph != null && ph.Alive) bodies.Add(new Body { t = g.Ship.transform, c = g.Ship, half = 19f });
        }

        /// <summary>Поправка руля и хода, выставленных ИИ, чтобы разойтись с соседями. ignore — своя цель в бою.</summary>
        public static void Avoid(ShipController self, float half, Transform ignore)
        {
            if (self == null) return;
            Collect();
            Vector3 p = self.transform.position, v = self.transform.forward * Mathf.Max(2f, self.Speed);
            float bestT = 14f; Vector3 bestRel = Vector3.zero; bool found = false; float bestMiss = 0f, bestR = 0f;
            for (int i = 0; i < bodies.Count; i++)
            {
                Body b = bodies[i];
                if (b.c == self || b.t == ignore) continue;
                Vector3 rel = b.t.position - p; rel.y = 0f;
                float R = half + b.half + 25f;
                if (rel.sqrMagnitude > (R + 600f) * (R + 600f)) continue;
                Vector3 vr = b.t.forward * b.c.Speed - v; vr.y = 0f;
                float vv = vr.sqrMagnitude;
                float tc = vv > 1e-3f ? -Vector3.Dot(rel, vr) / vv : 0f;
                if (tc < 0f) tc = 0f;
                if (tc > bestT) continue;
                Vector3 closest = rel + vr * tc;
                float miss = closest.magnitude;
                if (miss > R) continue;
                bestT = tc; bestRel = rel; found = true; bestMiss = miss; bestR = R;
            }
            if (!found) return;
            Vector3 local = self.transform.InverseTransformDirection(bestRel);
            if (local.z < -bestR * 0.3f && bestRel.magnitude > bestR) return;   // догоняют сзади издалека — пусть обходит догоняющий
            // Отворачиваем от соседа; прямо по носу — вправо (морское правило). Чем ближе по времени, тем круче.
            float side = Mathf.Abs(local.x) < bestR * 0.25f ? 1f : -Mathf.Sign(local.x);
            float urgency = Mathf.Clamp01(1f - bestT / 14f) * Mathf.Clamp01((bestR - bestMiss) / bestR + 0.4f);
            self.turn = Mathf.Clamp(Mathf.Lerp(self.turn, side, urgency), -1f, 1f);
            if (local.z > 0f && bestT < 6f) self.throttle = Mathf.Min(self.throttle, Mathf.Lerp(self.throttle, 0.25f, urgency));
        }

        static float nextReport;
        /// <summary>Раз в кадр (из SkyTideBootstrap.Update): корпуса ИИ-кораблей не проходят друг сквозь друга.</summary>
        public static void Tick()
        {
            if (!Application.isPlaying) return;
            Collect();
            for (int i = 0; i < bodies.Count; i++)
            {
                Body a = bodies[i];
                if (a.enemy == null && a.ally == null) continue;   // флагман с врагами — TargetShip.CollideWithPlayer
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    Body b = bodies[j];
                    if (b.enemy == null && b.ally == null) continue;
                    Vector3 d = b.t.position - a.t.position; d.y = 0f;
                    float reach = a.half + b.half;
                    if (d.sqrMagnitude > reach * reach) continue;
                    // Ближайшие точки отрезков корпусов (нос–корма), зазор — полуширины (≈0,34 полудлины + 3 м).
                    Vector3 pa, pb;
                    Closest(a.t.position, a.t.forward * a.half * 0.85f, b.t.position, b.t.forward * b.half * 0.85f, out pa, out pb);
                    Vector3 n = pa - pb; n.y = 0f;
                    float gap = a.half * 0.3f + b.half * 0.3f + 3f, dist = n.magnitude;
                    if (dist >= gap) continue;
                    if (dist < 1e-3f) n = a.t.right; else n /= dist;
                    float push = (gap - dist) * 0.5f;
                    // Тяжёлый двигается меньше: доли по длине (адмиральский почти не сдвигается от брига).
                    float wa = b.half / (a.half + b.half), wb = 1f - wa;
                    a.c.Block(n * push * 2f * wa, 0.85f);
                    b.c.Block(-n * push * 2f * wb, 0.85f);
                    float closing = Mathf.Abs(a.c.Speed) + Mathf.Abs(b.c.Speed);
                    if (closing > 8f && Time.time >= nextReport)
                    {
                        nextReport = Time.time + 1f;
                        DebugReport.Count("столкновений кораблей");
                        FxSystem fx = FxSystem.Instance;
                        if (fx != null) fx.Scrape((pa + pb) * 0.5f + Vector3.up * 2f, n);
                        // Корсар задел корабль гарнизона — может вспыхнуть конфликт (а корсар с корсаром — только ругань).
                        if (a.enemy != null && b.ally != null) a.enemy.Collided(b.ally, closing);
                        else if (b.enemy != null && a.ally != null) b.enemy.Collided(a.ally, closing);
                    }
                }
            }
        }

        static void Closest(Vector3 c1, Vector3 h1, Vector3 c2, Vector3 h2, out Vector3 p1, out Vector3 p2)
        {
            // Отрезки c1±h1 и c2±h2 в плоскости XZ: перебор 5×5 точек — достаточно для корпусов.
            float best = float.MaxValue; p1 = c1; p2 = c2;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 x = c1 + h1 * (i * 0.5f), y = c2 + h2 * (j * 0.5f);
                    float d = (x.x - y.x) * (x.x - y.x) + (x.z - y.z) * (x.z - y.z);
                    if (d < best) { best = d; p1 = x; p2 = y; }
                }
        }
    }
}
