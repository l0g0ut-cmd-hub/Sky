using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Свои корабли (корпус модели врага в светлом тоне, без пушечных моделей игрока и экипажа):
    /// • Guard — сторожевой крепости: дозор у края безопасной зоны, выходит на врагов ближе engageRange от себя
    ///   (и на нарушителя, по которому бьёт крепость);
    /// • Wingman — ведомый: держится справа-сзади от флагмана, бьёт врагов, подошедших к флагману ближе 750 м;
    /// • Merchant — торговец: идёт издалека к причалу крепости, стоит, уходит; пушек нет. Дошёл — флагману ремонт +10%.
    /// Все уязвимы: ядра врага бьют по ним (FxSystem), потоплен — через respawnDelay возвращается (боевые — от причала,
    /// торговец — издалека). Подбитый ниже 35% (боевые) уходит чиниться к причалу и возвращается целым.
    /// </summary>
    public class AllyShip : MonoBehaviour
    {
        public enum Role { Guard, Wingman, Merchant }
        public static readonly List<AllyShip> All = new List<AllyShip>();
        public Role role = Role.Guard;
        public float engageRange = 750f, fightRange = 300f, leash = 2300f, cooldown = 7f, muzzleSpeed = 85f;
        public float rangeError = 0.06f, sideError = 0.025f, respawnDelay = 30f;
        public int gunsPerSide = 4, hitsToSink = 30;
        public static readonly string[] RoleNames = { "СТОРОЖЕВОЙ", "ВЕДОМЫЙ", "ТОРГОВЕЦ" };

        ShipController ctrl;
        Transform model;
        Bounds box;
        ShipHitSurface surface;
        ShipDamageVisual damage;
        float ready, pendingAt, fightSide, deadUntil = -1f, nextRepair, waitUntil;
        int pendingLeft, pendingSide, hits;
        Vector3 aimErr, lastPos, selfVel;
        TargetShip foe;
        bool announced, repairing;
        enum Leg { Inbound, Docked, Outbound }
        Leg leg;
        Vector3 merchantFrom;

        public ShipController Controller { get { return ctrl; } }
        public string AiState { get; private set; } = "";
        void Ai(string st, string why = "")
        {
            if (st == AiState) return;
            if (Application.isPlaying) DebugReport.Ai(RoleNames[(int)role].ToLower() + " " + name.Replace("Guard Ship ", "") + ": " + (AiState.Length > 0 ? AiState : "старт") + " → " + st + (why.Length > 0 ? " (" + why + ")" : ""));
            AiState = st;
        }
        public bool Alive { get { return deadUntil < 0f; } }
        public bool Fighting { get { return foe != null; } }
        public float Health01 { get { return Mathf.Clamp01(1f - hits / (float)Mathf.Max(1, hitsToSink)); } }
        public Vector3 Center { get { return model != null ? model.TransformPoint(box.center) : transform.position; } }
        public Vector3 Velocity { get { return ctrl != null ? transform.forward * ctrl.Speed : Vector3.zero; } }
        /// <summary>Под огнём: попадание за последние 15 с. Флагман на автобое и свои идут на помощь.</summary>
        public float LastHitTime { get; private set; } = -100f;
        public bool UnderAttack { get { return Alive && Time.time - LastHitTime < 15f; } }
        /// <summary>Ближайший к нему живой враг (кто, скорее всего, и бьёт) — в пределах 900 м.</summary>
        public TargetShip Attacker
        {
            get
            {
                TargetShip best = null; float bd = 900f * 900f;
                foreach (TargetShip t in TargetShip.All)
                {
                    if (t == null || !t.Alive) continue;
                    float d = (t.transform.position - transform.position).sqrMagnitude;
                    if (d < bd) { bd = d; best = t; }
                }
                return best;
            }
        }
        /// <summary>Самый «горячий» из своих под огнём (ближайший к точке from), или null.</summary>
        public static AllyShip Distressed(Vector3 from, float within, AllyShip except = null)
        {
            AllyShip best = null; float bd = within * within;
            foreach (AllyShip a in All)
            {
                if (a == null || a == except || !a.UnderAttack) continue;
                float d = (a.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        public void Setup(ShipController c, Role r, TextAsset geometry, Material wood)
        {
            ctrl = c; role = r;
            Transform v = c.visual;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
            if (geometry != null) surface = new ShipHitSurface(geometry);
            damage = gameObject.AddComponent<ShipDamageVisual>();
            damage.Configure(model, box, wood, surface);
            // Свои — светлее и холоднее врага (боевые голубоватые, торговец — светлое дерево с зеленцой).
            MaterialPropertyBlock tint = new MaterialPropertyBlock();
            tint.SetColor("_Color", r == Role.Merchant ? new Color(1.05f, 1.1f, 0.85f) : new Color(0.78f, 0.95f, 1.2f));
            foreach (MeshRenderer mr in c.visual.GetComponentsInChildren<MeshRenderer>(true)) mr.SetPropertyBlock(tint);
            if (r == Role.Merchant) { ctrl.maxSpeed = 16f; ctrl.maxYawRate = 20f; hitsToSink = 20; respawnDelay = 45f; gunsPerSide = 0; }
            else if (r == Role.Wingman) { ctrl.maxSpeed = 34f; ctrl.maxYawRate = 34f; hitsToSink = 34; }
            else { ctrl.maxSpeed = 22f; ctrl.maxYawRate = 26f; hitsToSink = 40; }
            ready = Time.time + 8f;
            merchantFrom = transform.position;
            if (!All.Contains(this)) All.Add(this);
        }
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        // ---------- попадания ----------
        public bool TryHit(Vector3 a, Vector3 b, out Vector3 point)
        {
            point = b;
            if (!Alive || model == null) return false;
            if (surface != null)
            {
                ShipHitSurface.Contact c;
                if (!surface.Trace(model.InverseTransformPoint(a), model.InverseTransformPoint(b), out c)) return false;
                point = model.TransformPoint(c.point);
                return true;
            }
            Bounds bb = box; bb.Expand(1.2f);
            for (int i = 0; i <= 6; i++) { Vector3 p = Vector3.Lerp(a, b, i / 6f); if (bb.Contains(model.InverseTransformPoint(p))) { point = p; return true; } }
            return false;
        }

        public void OnHit(Vector3 point, float power = 1f)
        {
            if (!Alive) return;
            hits += power > 1.5f ? 3 : 1;
            // Первое попадание после затишья — сигнал бедствия: оповещение флагману с расстоянием.
            if (Time.time - LastHitTime > 20f)
            {
                PlayerHull ph = PlayerHull.Instance;
                float d = ph != null ? Vector3.Distance(ph.transform.position, transform.position) : 0f;
                HudNotice.Show(RoleNames[(int)role] + " ПОД ОГНЁМ · " + (d >= 1000f ? (d / 1000f).ToString("F1") + " КМ" : Mathf.RoundToInt(d / 10f) * 10 + " М"), 1, 3f);
            }
            LastHitTime = Time.time;
            DebugReport.Count("попаданий врага по своим");
            Vector3 normal = (point - Center).normalized;
            if (damage != null) damage.Hit(point, hits / (float)Mathf.Max(1, hitsToSink), Velocity, normal);
            SkySound.HullHit(point, false);
            FxSystem fx = FxSystem.Instance;
            if (hits >= hitsToSink)
            {
                SkySound.ShipDestroyed(point);
                if (fx != null) fx.Explosion(Center, 3f);
                deadUntil = Time.time + respawnDelay;
                if (damage != null) damage.BeginSink(point, Velocity);
                ctrl.throttle = 0f; ctrl.turn = 0f;
                HudNotice.Show(RoleNames[(int)role] + " ПОТОПЛЕН", 1, 3.5f);
                DebugReport.Count("своих потоплено");
            }
            else if (fx != null) fx.HullImpact(point, normal, Velocity);
        }

        void Respawn()
        {
            deadUntil = -1f; hits = 0; repairing = false; foe = null; pendingLeft = 0;
            if (damage != null) damage.ResetDamage();
            SkyFortress f = SkyFortress.Instance;
            Vector3 at;
            if (role == Role.Merchant || f == null)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 c = f != null ? f.Center : transform.position;
                at = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ((f != null ? f.safeRadius : 1400f) + 1500f);
                leg = Leg.Inbound; merchantFrom = at;
            }
            else
            {
                Vector3 outDir = f.BerthWorld - f.Center; outDir.y = 0f; outDir = outDir.sqrMagnitude > 1f ? outDir.normalized : Vector3.forward;
                at = f.BerthWorld + outDir * 160f + Vector3.Cross(Vector3.up, outDir) * (role == Role.Wingman ? -220f : 220f);
            }
            at.y = transform.position.y;
            transform.position = at;
            ctrl.visual.gameObject.SetActive(true);
            ctrl.enabled = true;
        }

        void Update()
        {
            long pt = PerfTimers.Start();
            Tick(Time.deltaTime);
            PerfTimers.Stop("Союзники", pt);
        }

        void Tick(float dt)
        {
            if (ctrl == null || dt <= 0f) return;
            if (!Alive)
            {
                if (damage != null && damage.Hidden) ctrl.visual.gameObject.SetActive(false);
                if (Time.time >= deadUntil) Respawn();
                return;
            }
            selfVel = Vector3.Lerp(selfVel, (transform.position - lastPos) / dt, 0.2f); lastPos = transform.position;
            ctrl.SetDamage(Health01);
            SkyFortress f = SkyFortress.Instance;
            if (role == Role.Merchant) { Ai(leg == Leg.Inbound ? "ИДЁТ" : leg == Leg.Docked ? "ПОРТ" : "УХОДИТ"); Merchant(f); return; }
            if (Health01 < 0.35f) repairing = true;
            if (repairing && f != null) { Ai("РЕМОНТ", Mathf.RoundToInt(Health01 * 100f) + "%"); foe = null; pendingLeft = 0; GoRepair(f); return; }
            foe = PickFoe(f);
            // Свой под огнём в 2.5 км — идём на помощь и бьём того, кто его атакует (торговец сам не воюет).
            if (foe == null)
            {
                AllyShip mate = Distressed(transform.position, 2500f, this);
                if (mate != null) foe = mate.Attacker;
            }
            if (foe == null)
            {
                announced = false; pendingLeft = 0;
                Ai(role == Role.Wingman ? "СТРОЙ" : "ДОЗОР");
                if (role == Role.Wingman) Follow(); else Patrol(f);
                return;
            }
            if (!announced)
            {
                announced = true;
                PlayerHull ph = PlayerHull.Instance;
                if (ph != null && (ph.transform.position - transform.position).sqrMagnitude < 1500f * 1500f)
                    HudNotice.Show(role == Role.Wingman ? "ВЕДОМЫЙ АТАКУЕТ" : "СТОРОЖЕВОЙ ИДЁТ НА ВРАГА", 0, 3f);
            }
            Ai("БОЙ", foe.name.Replace(" Ship", "") + " " + Mathf.RoundToInt(Vector3.Distance(foe.transform.position, transform.position)) + " м");
            Engage(foe);
            Gunnery(foe);
        }

        TargetShip PickFoe(SkyFortress f)
        {
            PlayerHull ph = PlayerHull.Instance;
            Vector3 anchor = role == Role.Wingman && ph != null ? ph.transform.position : (f != null ? f.Center : transform.position);
            TargetShip best = null; float bestD = float.MaxValue;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                if (t.InPort && !t.Outlaw) continue;   // гость порта — не цель
                Vector3 off = t.transform.position - anchor; off.y = 0f;
                float limit = role == Role.Wingman ? engageRange : float.MaxValue;   // сторожевые ходят по всей карте — бьют, где встретят
                if (off.magnitude > limit) continue;
                float d = Vector3.Distance(t.transform.position, transform.position);
                if (role == Role.Guard && !t.Outlaw && d > engageRange) continue;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        // Дозор по карте: точки в 900–2000 м от себя в пределах района (3000 м), в обход камня крепости; дошёл или
        // прошло 70 с — следующая. Половину точек сторожевой берёт у края безопасной зоны — возвращается к крепости.
        Vector3 roamPoint; float nextRoam;
        void Patrol(SkyFortress f)
        {
            if (Time.time >= nextRoam || (roamPoint - transform.position).sqrMagnitude < 150f * 150f)
            {
                nextRoam = Time.time + 70f;
                float a = Random.Range(0f, Mathf.PI * 2f);
                if (f != null && Random.value < 0.5f)
                    roamPoint = f.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (f.safeRadius + 150f);
                else
                {
                    roamPoint = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(900f, 2000f);
                    Vector2 flat = new Vector2(roamPoint.x, roamPoint.z);
                    if (flat.magnitude > 3000f) { flat = flat.normalized * 2600f; roamPoint.x = flat.x; roamPoint.z = flat.y; }
                }
                roamPoint.y = transform.position.y;
            }
            if (f != null) SteerAround(f, roamPoint, 0.6f); else Steer(roamPoint - transform.position, 0.6f);
        }

        void Follow()
        {
            PlayerHull ph = PlayerHull.Instance;
            if (ph == null) { Steer(transform.forward, 0.4f); return; }
            Transform lead = ph.transform;
            Vector3 slot = lead.position + lead.right * 110f - lead.forward * 70f; slot.y = transform.position.y;
            Vector3 to = slot - transform.position; to.y = 0f;
            float d = to.magnitude;
            ShipController lc = lead.GetComponent<ShipController>();
            float leadSpeed = lc != null ? Mathf.Abs(lc.Speed) : 0f;
            // Далеко — догоняет полным ходом, близко к своему месту — идёт курсом и ходом флагмана.
            if (d > 60f) Steer(to, Mathf.Clamp01(d / 300f + leadSpeed / Mathf.Max(1f, ctrl.maxSpeed)));
            else Steer(lead.forward + to * 0.01f, Mathf.Clamp01(leadSpeed / Mathf.Max(1f, ctrl.maxSpeed)));
        }

        void GoRepair(SkyFortress f)
        {
            Vector3 outDir = f.BerthWorld - f.Center; outDir.y = 0f; outDir = outDir.sqrMagnitude > 1f ? outDir.normalized : Vector3.forward;
            Vector3 spot = f.BerthWorld + outDir * 90f + Vector3.Cross(Vector3.up, outDir) * (role == Role.Wingman ? -150f : 150f);
            Vector3 to = spot - transform.position; to.y = 0f;
            if (to.magnitude > 45f) { SteerAround(f, spot, to.magnitude > 300f ? 0.8f : 0.3f); return; }
            ctrl.throttle = 0f; ctrl.turn = 0f;
            if (Time.time >= nextRepair && hits > 0) { nextRepair = Time.time + 1.2f; hits--; if (hits == 0 && damage != null) damage.ResetDamage(); }
            if (hits == 0) repairing = false;
        }

        void Merchant(SkyFortress f)
        {
            if (f == null) return;
            Vector3 outDir = f.BerthWorld - f.Center; outDir.y = 0f; outDir = outDir.sqrMagnitude > 1f ? outDir.normalized : Vector3.forward;
            Vector3 spot = f.BerthWorld + outDir * 70f - Vector3.Cross(Vector3.up, outDir) * 170f; spot.y = transform.position.y;
            Vector3 to = spot - transform.position; to.y = 0f;
            if (leg == Leg.Inbound)
            {
                if (to.magnitude < 50f)
                {
                    leg = Leg.Docked; waitUntil = Time.time + 25f;
                    DebugReport.Count("торговцев дошло");
                    PlayerHull ph = PlayerHull.Instance;
                    if (ph != null && ph.Alive) { ph.Repair(ph.maxHealth * 0.1f); HudNotice.Show("ТОРГОВЕЦ ДОШЁЛ · РЕМОНТ +10%", 0, 3.5f); }
                }
                else SteerAround(f, spot, to.magnitude > 300f ? 0.9f : 0.35f);
                return;
            }
            if (leg == Leg.Docked)
            {
                ctrl.throttle = 0f; ctrl.turn = 0f;
                if (Time.time >= waitUntil) leg = Leg.Outbound;
                return;
            }
            // Уходит: по направлению от крепости; далеко — исчезает и через паузу приходит новый.
            Vector3 off = transform.position - f.Center; off.y = 0f;
            Steer(off.sqrMagnitude > 1f ? off.normalized : outDir, 0.9f);
            if (off.magnitude > f.safeRadius + 1700f) { deadUntil = Time.time + respawnDelay; ctrl.visual.gameObject.SetActive(false); }
        }

        /// <summary>Курс на точку в обход камня крепости.</summary>
        void SteerAround(SkyFortress f, Vector3 goal, float throttle)
        {
            Vector3 to = goal - transform.position; to.y = 0f;
            Vector3 heading = to.sqrMagnitude > 1f ? to.normalized : transform.forward;
            if (to.magnitude > 200f)
            {
                Vector3 off = transform.position - f.Center; off.y = 0f;
                Vector3 berth = f.BerthWorld - f.Center; berth.y = 0f;
                float keep = berth.magnitude + 70f;
                if (off.sqrMagnitude < keep * keep && off.sqrMagnitude > 1f)
                {
                    Vector3 outward = off.normalized; float inward = Vector3.Dot(heading, outward);
                    if (inward < 0.25f) heading = (heading - outward * inward + outward * 0.45f).normalized;
                }
            }
            Steer(heading, throttle);
        }

        void Engage(TargetShip t)
        {
            Vector3 to = t.transform.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist < 1f) { ctrl.turn = 0f; ctrl.throttle = 0.4f; return; }
            Vector3 dir = to / dist;
            float localX = Vector3.Dot(transform.right, dir);
            if (fightSide == 0f) fightSide = localX >= 0f ? 1f : -1f;
            else if (Mathf.Sign(localX) != fightSide && Mathf.Abs(localX) > 0.45f) fightSide = -fightSide;
            float bearing = dist > fightRange + 250f ? 15f : 90f + Mathf.Clamp((fightRange - dist) / 90f, -1f, 1f) * 40f;
            Steer(Quaternion.Euler(0f, -fightSide * bearing, 0f) * dir, dist > fightRange + 250f ? 1f : 0.65f);
        }

        void Steer(Vector3 heading, float throttle)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = transform.forward;
            float ang = Vector3.SignedAngle(transform.forward, heading.normalized, Vector3.up);
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? Mathf.Min(0.3f, throttle) : throttle;
        }

        void Gunnery(TargetShip t)
        {
            FxSystem fx = FxSystem.Instance;
            if (fx == null || model == null || gunsPerSide <= 0) return;
            Vector3 tp = t.Center;
            if (pendingLeft > 0 && Time.time >= pendingAt)
            {
                int index = gunsPerSide - pendingLeft;
                float z = Mathf.Lerp(box.min.z + box.size.z * 0.3f, box.max.z - box.size.z * 0.3f, gunsPerSide > 1 ? index / (float)(gunsPerSide - 1) : 0.5f);
                Vector3 muzzle = model.TransformPoint(new Vector3(box.center.x + pendingSide * (box.extents.x * 0.95f + 0.6f), box.min.y + box.size.y * 0.28f, z));
                Vector3 tv = t.Controller != null ? t.transform.forward * t.Controller.Speed : Vector3.zero;
                Vector3 dir;
                if (Solve(muzzle, tp + aimErr, tv, fx.gravity, out dir))
                    fx.Fire(muzzle, Quaternion.Euler(Random.Range(-2f, 2f), Random.Range(-2f, 2f), 0f) * dir, selfVel, muzzleSpeed);
                pendingLeft--; pendingAt = Time.time + Random.Range(0.12f, 0.2f);
                return;
            }
            if (Time.time < ready) return;
            Vector3 rel = tp - transform.position; rel.y = 0f;
            float dist = rel.magnitude;
            if (dist < 80f || dist > 620f) return;
            Vector3 local = transform.InverseTransformDirection(rel);
            float bearing = Mathf.Atan2(Mathf.Abs(local.x), local.z) * Mathf.Rad2Deg;
            if (bearing < 55f || bearing > 125f) return;
            // По врагу, который трётся о флагман, не бьём: ядра могли бы лечь в своих (свои ядра своих не ранят, но смотрится глупо).
            PlayerHull ph = PlayerHull.Instance;
            if (ph != null && (ph.transform.position - t.transform.position).sqrMagnitude < 45f * 45f) return;
            pendingSide = local.x > 0f ? 1 : -1;
            Vector3 fwd = rel / Mathf.Max(dist, 1f), side = Vector3.Cross(Vector3.up, fwd);
            aimErr = fwd * Gauss() * rangeError * dist + side * Gauss() * sideError * dist;
            pendingLeft = gunsPerSide; pendingAt = Time.time;
            DebugReport.Volley(RoleNames[(int)role].ToLower() + " → " + t.name.Replace(" Ship", "") + " " + Mathf.RoundToInt(dist) + " м");
            ready = Time.time + cooldown + Random.Range(0f, 2f);
        }

        static float Gauss()
        {
            float u1 = Mathf.Max(1e-4f, Random.value), u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        bool Solve(Vector3 from, Vector3 to, Vector3 targetVel, float g, out Vector3 dir)
        {
            dir = Vector3.forward;
            Vector3 aim = to;
            for (int it = 0; it < 3; it++)
            {
                Vector3 d = aim - from;
                float x = new Vector2(d.x, d.z).magnitude, y = d.y, v = muzzleSpeed;
                float disc = v * v * v * v - g * (g * x * x + 2f * y * v * v);
                if (disc < 0f || x < 1f) return false;
                float theta = Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * x));
                float tFlight = x / (v * Mathf.Cos(theta));
                Vector3 h = new Vector3(d.x, 0f, d.z).normalized;
                dir = h * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta);
                aim = to + (targetVel - selfVel) * (tFlight * 0.85f);
            }
            return true;
        }
    }
}
