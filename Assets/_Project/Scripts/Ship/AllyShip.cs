using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Союзник: сторожевой корабль крепости. Ходит дозором по кругу у края безопасной зоны; увидел врага ближе
    /// engageRange (или нарушителя, по которому бьёт крепость) — выходит на него бортом и стреляет залпами по четыре
    /// ядра с упреждением. Дальше leash от центра крепости за врагом не идёт — возвращается в дозор.
    /// Ядра «свои» (не hostile): бьют по кораблям врага, игрока и крепость у дула не задевают.
    /// Первая версия: враг по союзнику не стреляет и повредить его нельзя — он под защитой крепости.
    /// </summary>
    public class AllyShip : MonoBehaviour
    {
        public static readonly List<AllyShip> All = new List<AllyShip>();
        public float engageRange = 750f, fightRange = 300f, leash = 2300f, cooldown = 7f, muzzleSpeed = 85f;
        public float rangeError = 0.06f, sideError = 0.025f;
        public int gunsPerSide = 4;

        ShipController ctrl;
        Transform model;
        Bounds box;
        float patrolAngle, ready, pendingAt, fightSide;
        int pendingLeft, pendingSide;
        Vector3 aimErr, lastPos, selfVel;
        TargetShip foe;
        bool announced;

        public ShipController Controller { get { return ctrl; } }
        public bool Fighting { get { return foe != null; } }

        public void Setup(ShipController c)
        {
            ctrl = c;
            Transform v = c.visual;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
            // Свои — светлее и холоднее врага: тот же корпус, другой тон (и на облегчённой копии).
            MaterialPropertyBlock tint = new MaterialPropertyBlock();
            tint.SetColor("_Color", new Color(0.78f, 0.95f, 1.2f));
            foreach (MeshRenderer r in c.visual.GetComponentsInChildren<MeshRenderer>(true)) r.SetPropertyBlock(tint);
            ctrl.maxSpeed = 22f; ctrl.maxYawRate = 26f;
            ready = Time.time + 8f;
            if (!All.Contains(this)) All.Add(this);
        }
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Update()
        {
            long pt = PerfTimers.Start();
            Tick(Time.deltaTime);
            PerfTimers.Stop("Союзник", pt);
        }

        void Tick(float dt)
        {
            SkyFortress f = SkyFortress.Instance;
            if (ctrl == null || f == null || dt <= 0f) return;
            selfVel = Vector3.Lerp(selfVel, (transform.position - lastPos) / dt, 0.2f); lastPos = transform.position;
            foe = PickFoe(f);
            if (foe == null) { announced = false; pendingLeft = 0; Patrol(f); return; }
            if (!announced)
            {
                announced = true;
                PlayerHull ph = PlayerHull.Instance;
                if (ph != null && (ph.transform.position - transform.position).sqrMagnitude < 1500f * 1500f) HudNotice.Show("СТОРОЖЕВОЙ ИДЁТ НА ВРАГА", 0, 3.5f);
            }
            Engage(foe);
            Gunnery(foe);
        }

        TargetShip PickFoe(SkyFortress f)
        {
            TargetShip best = null; float bestD = float.MaxValue;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                if (t.InPort && !t.Outlaw) continue;   // мирно стоящий в порту — гость, не цель
                Vector3 off = t.transform.position - f.Center; off.y = 0f;
                if (off.magnitude > leash) continue;
                float d = Vector3.Distance(t.transform.position, transform.position);
                if (!t.Outlaw && d > engageRange) continue;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        void Patrol(SkyFortress f)
        {
            // Дозор по кругу чуть внутри края зоны, против часовой стрелки.
            float radius = f.safeRadius - 180f;
            Vector3 off = transform.position - f.Center; off.y = 0f;
            float here = Mathf.Atan2(off.z, off.x);
            patrolAngle = here + 0.22f;
            Vector3 goal = f.Center + new Vector3(Mathf.Cos(patrolAngle), 0f, Mathf.Sin(patrolAngle)) * radius;
            Steer(goal - transform.position, 0.5f);
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
            ctrl.throttle = Mathf.Abs(ang) > 100f ? 0.3f : throttle;
        }

        void Gunnery(TargetShip t)
        {
            FxSystem fx = FxSystem.Instance;
            if (fx == null || model == null) return;
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
            // Над своим портом сторожевой не палит по тем, кто стоит у самого причала рядом с игроком.
            pendingSide = local.x > 0f ? 1 : -1;
            Vector3 fwd = rel / Mathf.Max(dist, 1f), side = Vector3.Cross(Vector3.up, fwd);
            aimErr = fwd * Gauss() * rangeError * dist + side * Gauss() * sideError * dist;
            pendingLeft = gunsPerSide; pendingAt = Time.time;
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
