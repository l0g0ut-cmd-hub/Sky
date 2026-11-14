using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Автобой своего корабля (кнопка «АВТОБОЙ»): дозор, бой и ремонт без игрока.
    /// • Бой: держит врага на траверзе на дистанции ДАЛЬШЕ предела его пушек (свои достают вдвое дальше) — бьёт сам,
    ///   под ответный огонь не подставляется; сближается — отворачивает и уходит полным ходом.
    /// • Стреляет, как только расчёт борта говорит «в прицеле» и борт заряжен. В зоне крепости огня не открывает
    ///   (кроме случая, когда крепость сама бьёт по врагу-нарушителю), по мирно стоящим в порту — тоже.
    /// • Корпус ниже 35% — выходит из боя и идёт к причалу в обход камня крепости, встаёт на «СТОП» и чинится до целого.
    /// • Врагов нет — дозор по кругу за краем безопасной зоны.
    /// Любое касание джойстика или рукояти хода возвращает управление игроку.
    /// </summary>
    public class PlayerAutopilot : MonoBehaviour
    {
        public float repairBelow = 0.35f, repairUntil = 0.98f;
        ShipController ship; PlayerHull hull; Broadside guns;
        bool repairing; float fightSide; string state = "";
        /// <summary>Что корабль делает сейчас — для плашки HUD.</summary>
        public string State { get { return state; } }
        public float Throttle { get; private set; }

        public void Tick()
        {
            if (ship == null) { ship = GetComponent<ShipController>(); hull = GetComponent<PlayerHull>(); guns = GetComponent<Broadside>(); }
            if (ship == null || hull == null || !hull.Alive) { state = ""; return; }
            SkyFortress fort = SkyFortress.Instance;
            if (hull.Health01 < repairBelow) repairing = true;
            else if (hull.Health01 >= repairUntil) repairing = false;
            if (repairing && fort != null) { Repair(fort); return; }

            TargetShip foe = PickFoe();
            if (foe == null) { Patrol(fort); return; }
            Fight(foe, fort);
        }

        TargetShip PickFoe()
        {
            TargetShip best = null; float bestD = float.MaxValue;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                if (t.InPort && !t.Outlaw) continue;   // гость порта — не цель
                float d = (t.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        void Fight(TargetShip foe, SkyFortress fort)
        {
            state = "АВТОБОЙ · БОЙ";
            Vector3 to = foe.transform.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 1f ? to / dist : transform.forward;
            // Своя дистанция: за пределом пушек врага, но уверенно в пределах своих.
            EnemyGunner eg = foe.GetComponentInParent<EnemyGunner>();
            float enemyReach = eg != null ? eg.maxRange : 420f;
            float own = guns != null ? guns.MaxRange : 600f;
            float want = Mathf.Clamp(enemyReach + 90f, 250f, Mathf.Max(260f, own * 0.7f));
            float localX = Vector3.Dot(transform.right, dir);
            if (fightSide == 0f) fightSide = localX >= 0f ? 1f : -1f;
            else if (Mathf.Sign(localX) != fightSide && Mathf.Abs(localX) > 0.35f) fightSide = -fightSide;
            bool far = dist > want + 350f;
            // Курсовой угол на врага: 90° на своей дистанции; ближе — отворачиваем от него (до 135°), дальше — доворачиваем.
            float bearing = far ? 12f : 90f + Mathf.Clamp((want - dist) / 80f, -1f, 1f) * 45f;
            Vector3 heading = Quaternion.Euler(0f, -fightSide * bearing, 0f) * dir;
            // В зоне крепости бой не ведём — курс не заводит внутрь (кроме охоты на нарушителя).
            if (fort != null && !foe.Outlaw)
            {
                Vector3 off = transform.position - fort.Center; off.y = 0f;
                float edge = fort.safeRadius + 120f;
                if (off.sqrMagnitude < edge * edge && off.sqrMagnitude > 1f)
                {
                    Vector3 outward = off.normalized; float inward = Vector3.Dot(heading, outward);
                    if (inward < 0.2f) heading = (heading - outward * inward + outward * 0.4f).normalized;
                }
            }
            Steer(heading, far || dist < want - 60f ? 1f : 0.68f);

            if (guns == null) return;
            bool zone = SkyFortress.InSafeZone(transform.position);
            if (zone && !foe.Outlaw) return;
            // Расчёт борта наводится на БЛИЖАЙШЕГО живого врага — стреляем, только если это и есть наша цель.
            TargetShip nearest = null; float nd = float.MaxValue;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                float d = Vector3.Distance(t.transform.position, transform.position);
                if (d < nd) { nd = d; nearest = t; }
            }
            if (nearest == foe && guns.AimOnTarget && guns.Readiness(guns.AimSide) >= 0.999f) guns.Fire(guns.AimSide);
        }

        void Repair(SkyFortress fort)
        {
            Vector3 outDir = fort.BerthWorld - fort.Center; outDir.y = 0f;
            outDir = outDir.sqrMagnitude > 1f ? outDir.normalized : Vector3.forward;
            Vector3 spot = fort.BerthWorld + outDir * 30f; spot.y = transform.position.y;
            Vector3 to = spot - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (fort.PlayerDocked || dist < fort.berthRadius * 0.6f)
            {
                // У причала: «СТОП» — крепость сама подтянет к месту и чинит.
                state = fort.Hostile ? "АВТОБОЙ · ЖДЁМ ОТБОЯ ТРЕВОГИ" : "АВТОБОЙ · РЕМОНТ";
                ship.turn = 0f; Throttle = 0f; ship.throttle = 0f;
                return;
            }
            state = "АВТОБОЙ · К ПРИЧАЛУ";
            Vector3 heading = to / Mathf.Max(dist, 1f);
            if (dist > 220f)
            {
                Vector3 off = transform.position - fort.Center; off.y = 0f;
                Vector3 berth = fort.BerthWorld - fort.Center; berth.y = 0f;
                float keep = berth.magnitude + 70f;
                if (off.sqrMagnitude < keep * keep && off.sqrMagnitude > 1f)
                {
                    Vector3 outward = off.normalized; float inward = Vector3.Dot(heading, outward);
                    if (inward < 0.25f) heading = (heading - outward * inward + outward * 0.45f).normalized;
                }
            }
            Steer(heading, dist > 500f ? 1f : dist > 220f ? 0.68f : 0.35f);
        }

        void Patrol(SkyFortress fort)
        {
            state = "АВТОБОЙ · ДОЗОР";
            if (fort == null) { Steer(transform.forward, 0.68f); return; }
            float radius = fort.safeRadius + 350f;
            Vector3 off = transform.position - fort.Center; off.y = 0f;
            float here = Mathf.Atan2(off.z, off.x) - 0.2f;   // по часовой — навстречу сторожевому, зона прикрыта с двух сторон
            Vector3 goal = fort.Center + new Vector3(Mathf.Cos(here), 0f, Mathf.Sin(here)) * radius;
            Steer(goal - transform.position, 0.68f);
        }

        void Steer(Vector3 heading, float throttle)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = transform.forward;
            float ang = Vector3.SignedAngle(transform.forward, heading.normalized, Vector3.up);
            ship.turn = Mathf.Clamp(ang / 25f, -1f, 1f);
            Throttle = throttle; ship.throttle = throttle;
        }
    }
}
