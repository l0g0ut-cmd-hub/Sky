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
        class Body { public Transform t; public ShipController c; public float half, hullHalf, hullW, reach; public TargetShip enemy; public AllyShip ally; public bool player;
            public Vector2[] local; public readonly List<Vector2> world = new List<Vector2>(32); }
        static readonly List<Body> pool = new List<Body>();
        static Body Next(int i) { while (pool.Count <= i) pool.Add(new Body()); return pool[i]; }
        static readonly List<Body> bodies = new List<Body>();
        static int frame = -1;

        static void Collect()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            bodies.Clear();
            foreach (TargetShip t in TargetShip.All)
                if (t != null && t.Alive && t.Controller != null && t.gameObject.activeInHierarchy)
                    Add(t.transform, t.Controller, t.Length * 0.5f, t.HullHalfLength, t.HullHalfWidth, t, null, false);
            foreach (AllyShip a in AllyShip.All)
                if (a != null && a.Alive && a.Controller != null && a.gameObject.activeInHierarchy)
                    Add(a.transform, a.Controller, Mathf.Max(10f, a.Length * 0.5f), a.HullHalfLength, a.HullHalfWidth, null, a, false);
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            PlayerHull ph = PlayerHull.Instance;
            // Флагман (38 м): запасной корпус ±17 м, полуширина 7 м — если контура модели нет.
            if (g != null && g.Ship != null && ph != null && ph.Alive) Add(g.Ship.transform, g.Ship, 19f, 17f, 7f, null, null, true);
        }

        static void Add(Transform t, ShipController c, float half, float hullHalf, float hullW, TargetShip enemy, AllyShip ally, bool player)
        {
            Body b = Next(bodies.Count);
            b.t = t; b.c = c; b.half = half; b.hullHalf = hullHalf; b.hullW = hullW; b.enemy = enemy; b.ally = ally; b.player = player;
            float r;
            b.local = Footprint.Of(t, out r);
            if (b.local == null)
            {
                // Нет контура — прямоугольник с обрезанными углами по габаритам.
                b.local = new[] { new Vector2(hullW, -hullHalf), new Vector2(hullW, hullHalf * 0.8f), new Vector2(0f, hullHalf + hullW),
                                  new Vector2(-hullW, hullHalf * 0.8f), new Vector2(-hullW, -hullHalf) };
                r = hullHalf + hullW;
            }
            b.reach = r;
            Footprint.ToWorld(t, b.local, b.world);
            bodies.Add(b);
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
                    float reach = a.reach + b.reach;
                    if (d.sqrMagnitude > reach * reach) continue;
                    // Точные контуры моделей в плане (выпуклые оболочки): борт к борту — впритык, внутрь — нельзя.
                    Vector2 n2; float over;
                    if (!Footprint.Overlap(a.world, b.world, out n2, out over)) continue;
                    Vector3 n = new Vector3(n2.x, 0f, n2.y);
                    over += 0.05f;
                    Vector2 mid = (Footprint.Centroid(a.world) + Footprint.Centroid(b.world)) * 0.5f;
                    Vector3 pa = new Vector3(mid.x, a.t.position.y, mid.y), pb = pa;
                    // Доли сдвига по размеру: адмиральский от брига почти не сдвигается, стоящий у причала — тоже тяжёлый.
                    float ma = a.hullHalf * (a.ally != null && a.ally.role == AllyShip.Role.Harbor ? 3f : 1f);
                    float mb = b.hullHalf * (b.ally != null && b.ally.role == AllyShip.Role.Harbor ? 3f : 1f);
                    float wa = mb / (ma + mb), wb = 1f - wa;
                    a.c.Block(n * over * wa, a.player ? 0.6f : 0.85f);
                    b.c.Block(-n * over * wb, b.player ? 0.6f : 0.85f);
                    float closing = Mathf.Abs(a.c.Speed) + Mathf.Abs(b.c.Speed);
                    WorldLog.Bump(a.player ? "ФЛАГМАН" : a.t.name, b.player ? "ФЛАГМАН" : b.t.name, (pa + pb) * 0.5f, closing);
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
        /// <summary>Обход стен и причала крепости по её карте высот (17.11: адмиральский стоянки упирался в стену по пути
        /// в дозор, гости шли к месту сквозь причал). Впереди на 30…150 м + полкорпуса стены выше корабля — курс
        /// поворачивается на ближайший свободный (±25…±160°); если свободного нет — от центра крепости.</summary>
        public static Vector3 FortressDetour(Transform t, Vector3 heading, float halfLen)
        {
            SkyFortress f = SkyFortress.Instance;
            heading.y = 0f;
            if (f == null || heading.sqrMagnitude < 1e-4f) return heading;
            Vector3 p = t.position;
            if (new Vector2(p.x - f.Center.x, p.z - f.Center.z).magnitude > f.BoundRadius + halfLen + 200f) return heading;
            heading.Normalize();
            if (!WallAhead(f, p, heading, halfLen)) return heading;
            foreach (float a in new[] { 25f, -25f, 50f, -50f, 80f, -80f, 115f, -115f, 160f, -160f })
            {
                Vector3 h = Quaternion.Euler(0f, a, 0f) * heading;
                if (!WallAhead(f, p, h, halfLen)) return h;
            }
            Vector3 o = p - f.Center; o.y = 0f; return o.sqrMagnitude > 1f ? o.normalized : -heading;
        }
        static bool WallAhead(SkyFortress f, Vector3 p, Vector3 dir, float halfLen)
        {
            float y = p.y - 4f;
            foreach (float d in new[] { 30f, 60f, 100f, 150f })
                if (f.TopAt(p + dir * (d + halfLen)) > y) return true;
            return false;
        }

        /// <summary>Корпус против строения: капсула корпуса не входит в круг строения (раздвигается целиком корабль).</summary>
        static void Statics(Body a)
        {
            Vector3 c = a.t.position;
            for (int k = 0; k < WorldProps.Solids.Count; k++)
            {
                WorldProps.Solid s = WorldProps.Solids[k];
                if (c.y < s.y0 || c.y > s.y1) continue;
                float reach = a.reach + s.r;
                float dx = c.x - s.c.x, dz = c.z - s.c.z;
                if (dx * dx + dz * dz > reach * reach) continue;
                Vector2 n2; float over;
                if (!Footprint.CircleOverlap(a.world, new Vector2(s.c.x, s.c.z), s.r, out n2, out over)) continue;
                Vector3 n = new Vector3(n2.x, 0f, n2.y);
                a.c.Block(n * (over + 0.05f), a.player ? 0.5f : 0.8f);
                WorldLog.Bump(a.player ? "ФЛАГМАН" : a.t.name, "строение " + s.name, new Vector3(s.c.x, c.y, s.c.z), Mathf.Abs(a.c.Speed));
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
