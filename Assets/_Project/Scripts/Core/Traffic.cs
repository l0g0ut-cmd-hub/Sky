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
        struct Body { public Transform t; public ShipController c; public float half, hullHalf, hullW; public TargetShip enemy; public AllyShip ally; public bool player; }
        static readonly List<Body> bodies = new List<Body>();
        static int frame = -1;

        static void Collect()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            bodies.Clear();
            foreach (TargetShip t in TargetShip.All)
                if (t != null && t.Alive && t.Controller != null && t.gameObject.activeInHierarchy)
                    bodies.Add(new Body { t = t.transform, c = t.Controller, half = t.Length * 0.5f, hullHalf = t.HullHalfLength, hullW = t.HullHalfWidth, enemy = t });
            foreach (AllyShip a in AllyShip.All)
                if (a != null && a.Alive && a.Controller != null && a.gameObject.activeInHierarchy)
                    bodies.Add(new Body { t = a.transform, c = a.Controller, half = Mathf.Max(10f, a.Length * 0.5f), hullHalf = a.HullHalfLength, hullW = a.HullHalfWidth, ally = a });
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            PlayerHull ph = PlayerHull.Instance;
            // Флагман (38 м): корпус ±17 м, полуширина 7 м.
            if (g != null && g.Ship != null && ph != null && ph.Alive) bodies.Add(new Body { t = g.Ship.transform, c = g.Ship, half = 19f, hullHalf = 17f, hullW = 7f, player = true });
        }

        /// <summary>Поправка руля и хода, выставленных ИИ, чтобы разойтись с соседями. ignore — своя цель в бою.</summary>
        public static void Avoid(ShipController self, float half, Transform ignore) { Avoid(self, half, ignore, false); }
        /// <summary>Только строения — для автобоя флагмана (с кораблями он разбирается сам, в бою).</summary>
        public static void AvoidProps(ShipController self, float half) { Avoid(self, half, null, true); }
        static void Avoid(ShipController self, float half, Transform ignore, bool propsOnly)
        {
            if (self == null) return;
            Collect();
            Vector3 p = self.transform.position, v = self.transform.forward * Mathf.Max(2f, self.Speed);
            float bestT = 14f; Vector3 bestRel = Vector3.zero; bool found = false; float bestMiss = 0f, bestR = 0f;
            for (int i = 0; i < (propsOnly ? 0 : bodies.Count); i++)
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
            // Строения (WorldProps): стоят на месте — считаем как неподвижного соседа.
            for (int i = 0; i < WorldProps.Solids.Count; i++)
            {
                WorldProps.Solid s = WorldProps.Solids[i];
                Vector3 rel = s.c - p; rel.y = 0f;
                float R = half + s.r + 30f;
                if (rel.sqrMagnitude > (R + 600f) * (R + 600f)) continue;
                Vector3 vr = -v; vr.y = 0f;
                float vv = vr.sqrMagnitude;
                float tc = vv > 1e-3f ? Mathf.Max(0f, -Vector3.Dot(rel, vr) / vv) : 0f;
                if (tc > bestT) continue;
                float miss = (rel + vr * tc).magnitude;
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
        /// <summary>Раз в кадр (из SkyTideBootstrap.Update): корпуса не проходят друг сквозь друга — все со всеми, включая флагман
        /// и стоящих у причала (владелец 16.11: «правь всем скелеты, а то я сквозь них могу проезжать»). Корпус — отрезок от носа
        /// до кормы с полушириной по модели (капсула): у адмиральского ±115 м и полуширина 33 м. Раздвигаются по нормали; лёгкий
        /// сдвигается больше (доли по длине корпуса). Флагман с врагами: тут — только не пройти насквозь; урон тарана считает
        /// TargetShip.CollideWithPlayer.</summary>
        public static void Tick()
        {
            if (!Application.isPlaying) return;
            Collect();
            for (int i = 0; i < bodies.Count; i++)
            {
                Body a = bodies[i];
                if (WorldProps.Solids.Count > 0) Statics(a);
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    Body b = bodies[j];
                    Vector3 d = b.t.position - a.t.position; d.y = 0f;
                    float reach = a.hullHalf + b.hullHalf + a.hullW + b.hullW;
                    if (d.sqrMagnitude > reach * reach) continue;
                    Vector3 pa, pb;
                    Closest(a.t.position, Flat(a.t.forward) * a.hullHalf, b.t.position, Flat(b.t.forward) * b.hullHalf, out pa, out pb);
                    Vector3 n = pa - pb; n.y = 0f;
                    float gap = a.hullW + b.hullW, dist = n.magnitude;
                    if (dist >= gap) continue;
                    if (dist < 1e-3f) { n = a.t.position - b.t.position; n.y = 0f; if (n.sqrMagnitude < 1e-4f) n = a.t.right; dist = 0f; }
                    n.Normalize();
                    float over = gap - dist;
                    // Доли сдвига по размеру: адмиральский от брига почти не сдвигается, стоящий у причала — тоже тяжёлый.
                    float ma = a.hullHalf * (a.ally != null && a.ally.role == AllyShip.Role.Harbor ? 3f : 1f);
                    float mb = b.hullHalf * (b.ally != null && b.ally.role == AllyShip.Role.Harbor ? 3f : 1f);
                    float wa = mb / (ma + mb), wb = 1f - wa;
                    a.c.Block(n * over * wa, a.player ? 0.6f : 0.85f);
                    b.c.Block(-n * over * wb, b.player ? 0.6f : 0.85f);
                    float closing = Mathf.Abs(a.c.Speed) + Mathf.Abs(b.c.Speed);
                    if (closing > 8f && Time.time >= nextReport)
                    {
                        nextReport = Time.time + 1f;
                        DebugReport.Count(a.player || b.player ? "столкновений флагмана с кораблями" : "столкновений кораблей");
                        FxSystem fx = FxSystem.Instance;
                        if (fx != null) fx.Scrape((pa + pb) * 0.5f + Vector3.up * 2f, n);
                        // Корсар задел корабль гарнизона — может вспыхнуть конфликт (а корсар с корсаром — только ругань).
                        if (a.enemy != null && b.ally != null) a.enemy.Collided(b.ally, closing);
                        else if (b.enemy != null && a.ally != null) b.enemy.Collided(a.ally, closing);
                    }
                }
            }
        }
        /// <summary>Корпус против строения: капсула корпуса не входит в круг строения (раздвигается целиком корабль).</summary>
        static void Statics(Body a)
        {
            Vector3 h = Flat(a.t.forward) * a.hullHalf, c = a.t.position, a0 = c - h, d = h * 2f;
            for (int k = 0; k < WorldProps.Solids.Count; k++)
            {
                WorldProps.Solid s = WorldProps.Solids[k];
                if (c.y < s.y0 || c.y > s.y1) continue;
                float reach = a.hullHalf + a.hullW + s.r;
                float dx = c.x - s.c.x, dz = c.z - s.c.z;
                if (dx * dx + dz * dz > reach * reach) continue;
                Vector3 r = s.c - a0; r.y = 0f; d.y = 0f;
                float tt = d.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(r, d) / d.sqrMagnitude) : 0.5f;
                Vector3 q = a0 + d * tt; q.y = 0f;
                Vector3 n = q - s.c; n.y = 0f;
                float dist = n.magnitude, gap = a.hullW + s.r;
                if (dist >= gap) continue;
                if (dist < 1e-3f) { n = -a.t.forward; n.y = 0f; dist = 0f; }
                n.Normalize();
                a.c.Block(n * (gap - dist), a.player ? 0.5f : 0.8f);
                if (Mathf.Abs(a.c.Speed) > 6f && Time.time >= nextReport)
                {
                    nextReport = Time.time + 1f;
                    DebugReport.Count(a.player ? "столкновений флагмана со строениями" : "столкновений кораблей со строениями");
                    FxSystem fx = FxSystem.Instance;
                    if (fx != null) fx.Scrape(s.c + n * s.r + Vector3.up * c.y, n);
                }
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// <summary>Ближайшие точки двух отрезков c1±h1 и c2±h2 (точно, в плоскости XZ).</summary>
        static void Closest(Vector3 c1, Vector3 h1, Vector3 c2, Vector3 h2, out Vector3 p1, out Vector3 p2)
        {
            Vector3 a0 = c1 - h1, d1 = h1 * 2f, b0 = c2 - h2, d2 = h2 * 2f, r = a0 - b0;
            a0.y = b0.y = d1.y = d2.y = r.y = 0f;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), ff = Vector3.Dot(d2, r);
            float s, t;
            if (aa < 1e-6f && ee < 1e-6f) { s = t = 0f; }
            else if (aa < 1e-6f) { s = 0f; t = Mathf.Clamp01(ff / ee); }
            else
            {
                float cc = Vector3.Dot(d1, r);
                if (ee < 1e-6f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), den = aa * ee - bb * bb;
                    s = den > 1e-6f ? Mathf.Clamp01((bb * ff - cc * ee) / den) : 0f;
                    t = (bb * s + ff) / ee;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - cc) / aa); }
                }
            }
            p1 = c1 - h1 + h1 * 2f * s; p2 = c2 - h2 + h2 * 2f * t;
        }
    }
}
