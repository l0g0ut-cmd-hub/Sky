using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Автобой своего корабля (кнопка «АВТОБОЙ»): дозор, бой, абордаж и ремонт без игрока.
    /// Как ведёт бой:
    /// • Цель: не просто ближайший враг, а самый выгодный — тот, кто бьёт торговца или ведомого, подбитый (добить),
    ///   ближний; тяжёлого — в последнюю очередь, если есть другие.
    /// • Дистанция: за пределом пушек цели (её предел + 120 м), но не дальше 80% своего — бьёт, а в ответ не достают.
    ///   Если в цели 2+ врага рядом — держится ещё дальше от ближнего.
    /// • Положение: ставит цель точно на траверз БОРТА, КОТОРЫЙ ЗАРЯЖЕН (второй перезаряжается — разворачивается
    ///   другим бортом), курс — по касательной к окружности нужного радиуса с поправкой по дальности.
    /// • Ход: подстраивается, чтобы цель не уходила с траверза — цель впереди траверза — прибавляет, позади — сбавляет.
    /// • Залп: когда расчёт борта говорит «в прицеле» и борт заряжен; держит залп, пока цель уклоняется (крутит руль).
    /// • Заряд: убегающую или быструю цель (разведчик, уходящий в порт) — книппелями, чтобы остановить; остальных — ядрами.
    /// • Уклонение: после залпа врага в нашу сторону на 4 с меняет ход (сбивает его пристрелку).
    /// • Абордаж: подбитый ниже 20% враг рядом и нет других угроз ближе 500 м — идёт на абордаж.
    /// • Ремонт: ниже 35% — к причалу в обход крепости, чинится до 98%; врагов нет — дозор за краем зоны.
    /// Любое касание штурвала или рукояти хода возвращает управление игроку.
    /// </summary>
    public class PlayerAutopilot : MonoBehaviour
    {
        public static float RepairBelowDefault = 0.35f;
        public float repairBelow = 0.35f, repairUntil = 0.98f;
        ShipController ship; PlayerHull hull; Broadside guns; PlayerBoarding boarding;
        bool repairing; int wantSide; string state = "";
        float dodgeUntil, dodgeThrottle, lastVolleySeen = -100f, nextTargetPick;
        TargetShip foe; AllyShip helping;
        public string State { get { return state; } }
        const float RadarRange = 1500f;
        Vector3 lastSeen; float lastSeenAt = -100f; Vector3 searchPoint; float nextSearch;
        static bool SeenByAllies(TargetShip t)
        {
            foreach (AllyShip al in AllyShip.All)
                if (al != null && al.Alive && (al.transform.position - t.transform.position).sqrMagnitude < 1000f * 1000f) return true;
            return false;
        }
        public float Throttle { get; private set; }

        string loggedState = "";
        public void Tick()
        {
            TickInner();
            DebugReport.StateTick(state, Time.deltaTime);
            if (state != loggedState) { loggedState = state; if (state.Length > 0) DebugReport.Event(state + (foe != null ? " → " + foe.name + " " + Mathf.RoundToInt(Vector3.Distance(foe.transform.position, transform.position)) + " м" : "")); }
        }
        void TickInner()
        {
            if (ship == null) { ship = GetComponent<ShipController>(); hull = GetComponent<PlayerHull>(); guns = GetComponent<Broadside>(); boarding = GetComponent<PlayerBoarding>(); }
            if (ship == null || hull == null || !hull.Alive) { state = ""; return; }
            SkyFortress fort = SkyFortress.Instance;
            repairBelow = RepairBelowDefault;
            if (hull.Health01 < repairBelow) repairing = true;
            else if (hull.Health01 >= repairUntil) repairing = false;
            if (repairing && fort != null) { foe = null; Repair(fort); return; }
            if (foe == null || !foe.Alive || (foe.InPort && !foe.Outlaw) || Time.time >= nextTargetPick)
            {
                nextTargetPick = Time.time + 3f;
                // Свой под огнём (до 3.5 км) — помощь важнее всего: цель — тот, кто его бьёт.
                AllyShip mate = AllyShip.Distressed(transform.position, 3500f);
                TargetShip rescue = mate != null ? mate.Attacker : null;
                if (rescue != null && !(rescue.InPort && !rescue.Outlaw)) { foe = rescue; helping = mate; }
                else { foe = PickFoe(); helping = null; }
            }
            if (foe == null) { if (!Hunt()) Patrol(fort); return; }
            if (TryBoard()) return;
            Fight(foe, fort);
        }

        /// <summary>Оценка цели: меньше — лучше. Расстояние, со скидками за угрозу своим и за низкую прочность.</summary>
        TargetShip PickFoe()
        {
            TargetShip best = null; float bestScore = float.MaxValue;
            int count = 0;
            foreach (TargetShip t in TargetShip.All) if (t != null && t.Alive && !(t.InPort && !t.Outlaw)) count++;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                if (t.InPort && !t.Outlaw) continue;   // гость порта — не цель
                float d = Vector3.Distance(t.transform.position, transform.position);
                // Видим только то, что на радаре (1.5 км) или что заметили свои корабли (у них свои глаза — 1 км).
                if (d > RadarRange && !SeenByAllies(t)) continue;
                lastSeen = t.transform.position; lastSeenAt = Time.time;
                float score = d;
                // Бьёт своих (торговца, ведомого, сторожевого) — в приоритет.
                foreach (AllyShip al in AllyShip.All)
                    if (al != null && al.Alive && (al.transform.position - t.transform.position).sqrMagnitude < 450f * 450f) { score *= al.role == AllyShip.Role.Merchant ? 0.45f : 0.7f; break; }
                score *= Mathf.Lerp(0.55f, 1f, t.Health01);   // подбитого — добить
                if (t.Heavy && count > 1) score *= 1.8f;      // тяжёлого — последним
                if (t == foe) score *= 0.8f;                  // не мечется между целями
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        /// <summary>Врагов на радаре нет: идём туда, где видели последнего (до 90 с назад), иначе прочёсываем карту
        /// — точки в 1.5–2.5 км в стороне от уже осмотренного, вне зоны крепости. false — нечего искать (дозор).</summary>
        bool Hunt()
        {
            bool any = false;
            foreach (TargetShip t in TargetShip.All) if (t != null && t.Alive && !(t.InPort && !t.Outlaw)) { any = true; break; }
            if (!any) return false;
            Vector3 goal;
            if (Time.time - lastSeenAt < 90f && (lastSeen - transform.position).sqrMagnitude > 150f * 150f) { goal = lastSeen; state = "АВТОБОЙ · ПРЕСЛЕДОВАНИЕ"; }
            else
            {
                if (Time.time >= nextSearch || (searchPoint - transform.position).sqrMagnitude < 250f * 250f)
                {
                    nextSearch = Time.time + 45f;
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    searchPoint = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(1500f, 2500f);
                    Vector2 flat = new Vector2(searchPoint.x, searchPoint.z);
                    if (flat.magnitude > 3000f) { flat = flat.normalized * 2600f; searchPoint.x = flat.x; searchPoint.z = flat.y; }
                    SkyFortress f = SkyFortress.Instance;
                    if (f != null)
                    {
                        Vector3 off = searchPoint - f.Center; off.y = 0f;
                        float r = f.safeRadius + 200f;
                        if (off.sqrMagnitude < r * r) searchPoint = f.Center + (off.sqrMagnitude > 1f ? off.normalized : Vector3.forward) * r;
                    }
                }
                goal = searchPoint; state = "АВТОБОЙ · ПОИСК ВРАГА";
            }
            Vector3 to = goal - transform.position; to.y = 0f;
            Steer(to, 1f);
            return true;
        }

        bool TryBoard()
        {
            if (boarding == null) boarding = GetComponent<PlayerBoarding>();
            if (boarding == null) return false;
            if (boarding.Active) { state = "АВТОБОЙ · АБОРДАЖ"; Throttle = 0f; return true; }
            TargetShip c = boarding.Candidate;
            if (c == null) return false;
            foreach (TargetShip t in TargetShip.All)
                if (t != null && t.Alive && t != c && Vector3.Distance(t.transform.position, transform.position) < 500f) return false;
            boarding.Begin();
            return boarding.Active;
        }

        // Дерзость автобоя (владелец 15.11): иногда начинаем бой в порту — по врагу у причала; крепость за огонь в зоне поднимет
        // тревогу и начнёт бить. Сваливаем, когда пушки прижимают: тревога и минус 15% корпуса с начала, ниже 45% или 40 с.
        bool bold; float boldHull, boldUntil, nextBoldThink, fleeUntil;
        bool Bold(TargetShip foe, SkyFortress fort)
        {
            if (bold)
            {
                bool pressed = (fort.Hostile && hull.Health01 < boldHull - 0.15f) || hull.Health01 < 0.45f || Time.time > boldUntil;
                if (pressed)
                {
                    bold = false; fleeUntil = Time.time + 25f;
                    DebugReport.Event("автобой: сваливаем из порта (" + Mathf.RoundToInt(hull.Health01 * 100f) + "%)");
                    HudNotice.Show("АВТОБОЙ · ПРОЧЬ ОТ КРЕПОСТИ", 0, 3f);
                }
                return bold;
            }
            if (Time.time < fleeUntil || Time.time < nextBoldThink) return false;
            nextBoldThink = Time.time + Random.Range(18f, 28f);
            if (foe == null || foe.Outlaw || hull.Health01 < 0.7f) return false;
            if (!SkyFortress.InSafeZone(foe.transform.position) && !SkyFortress.InSafeZone(transform.position)) return false;
            if (Random.value > 0.3f) return false;
            bold = true; boldHull = hull.Health01; boldUntil = Time.time + 40f;
            DebugReport.Count("дерзостей автобоя"); DebugReport.Event("автобой: дерзим — бой в порту по " + foe.name);
            HudNotice.Show("АВТОБОЙ · ДЕРЗИМ — БОЙ В ПОРТУ", 0, 3f);
            return true;
        }

        void Fight(TargetShip foe, SkyFortress fort)
        {
            // Свалили из-под пушек: сначала выйти из зоны, потом воевать.
            if (Time.time < fleeUntil && fort != null && SkyFortress.InSafeZone(transform.position))
            {
                Vector3 outDir = transform.position - fort.Center; outDir.y = 0f;
                if (outDir.sqrMagnitude > 1f) Steer(outDir.normalized, 1f);
                state = "АВТОБОЙ · УХОДИМ ИЗ ПОРТА";
                return;
            }
            state = helping != null && helping.UnderAttack ? "АВТОБОЙ · НА ПОМОЩЬ " + AllyShip.RoleNames[(int)helping.role] : "АВТОБОЙ · БОЙ";
            Vector3 to = foe.transform.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 1f ? to / dist : transform.forward;
            EnemyGunner eg = foe.GetComponent<EnemyGunner>();
            float enemyReach = eg != null ? eg.maxRange : 420f;
            float own = guns != null ? guns.MaxRange : 600f;
            float want = Mathf.Clamp(enemyReach + 120f, 260f, Mathf.Max(280f, own * 0.8f));
            // Ещё один враг ближе 700 м — держимся дальше и от него.
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || t == foe || !t.Alive) continue;
                float d2 = Vector3.Distance(t.transform.position, transform.position);
                if (d2 < 700f) { want = Mathf.Min(own * 0.85f, want + 60f); break; }
            }

            // Борт, которым бьём: заряженный; оба заряжены — тот, что ближе к цели сейчас.
            float localX = Vector3.Dot(transform.right, dir);
            if (guns != null)
            {
                bool portReady = guns.Readiness(-1) >= 0.999f, stbdReady = guns.Readiness(1) >= 0.999f;
                if (portReady && !stbdReady) wantSide = -1;
                else if (stbdReady && !portReady) wantSide = 1;
                else if (wantSide == 0 || (portReady && stbdReady && Mathf.Abs(localX) > 0.6f)) wantSide = localX >= 0f ? 1 : -1;
            }
            else wantSide = localX >= 0f ? 1 : -1;

            bool far = dist > want + 400f;
            Vector3 heading;
            if (far)
            {
                // Перехват, а не погоня: точка встречи с упреждением по ходу цели, и заход с её кормы — там её пушки
                // не достают (враг бьёт только бортом, 55–125° от носа).
                ShipController tc = foe.Controller;
                Vector3 tv = tc != null ? foe.transform.forward * tc.Speed : Vector3.zero;
                float closing = Mathf.Max(8f, ship.maxSpeed - Vector3.Dot(tv, dir));
                float t = Mathf.Min(40f, dist / closing);
                Vector3 meet = foe.transform.position + tv * t - foe.transform.forward * Mathf.Min(320f, want * 0.6f);
                Vector3 toMeet = meet - transform.position; toMeet.y = 0f;
                heading = toMeet.sqrMagnitude > 1f ? toMeet.normalized : dir;
                state = helping != null && helping.UnderAttack ? "АВТОБОЙ · НА ПОМОЩЬ " + AllyShip.RoleNames[(int)helping.role] : "АВТОБОЙ · ПЕРЕХВАТ";
            }
            else
            {
                // Касательная: цель на траверзе нужного борта; ближе нужного — отворот наружу, дальше — внутрь (до 40°).
                float radial = Mathf.Clamp((want - dist) / 120f, -1f, 1f) * 40f;
                heading = Quaternion.Euler(0f, -wantSide * (90f + radial), 0f) * dir;
            }
            // Курс не заводит в зону крепости (кроме охоты на нарушителя) и не идёт в камень.
            if (fort != null)
            {
                Vector3 off = transform.position - fort.Center; off.y = 0f;
                float edge = foe.Outlaw ? fort.BoundRadius + 150f : fort.safeRadius + 120f;
                if (!Bold(foe, fort) && off.sqrMagnitude < edge * edge && off.sqrMagnitude > 1f)
                {
                    Vector3 outward = off.normalized; float inward = Vector3.Dot(heading, outward);
                    if (inward < 0.2f) heading = (heading - outward * inward + outward * 0.4f).normalized;
                }
            }

            // Ход: держим цель на траверзе — впереди траверза прибавляем, позади сбавляем.
            float bearing = Vector3.SignedAngle(transform.forward, dir, Vector3.up) * wantSide;   // 90 — ровно на траверзе нужного борта
            float throttle = far ? 1f : Mathf.Clamp(0.6f + (90f - bearing) / 70f, 0.25f, 1f);
            if (!far && dist < want - 120f) throttle = 1f;   // слишком близко — уходим полным
            // Уклонение: враг дал залп — 4 с другим ходом (его пристрелка сбивается при смене хода).
            if (EnemyGunner.LastVolleyTime > lastVolleySeen + 0.1f && dist < enemyReach + 150f)
            {
                lastVolleySeen = EnemyGunner.LastVolleyTime;
                dodgeUntil = Time.time + 4f;
                dodgeThrottle = throttle > 0.6f ? 0.3f : 1f;
            }
            if (Time.time < dodgeUntil) throttle = dodgeThrottle;
            Steer(heading, throttle);

            if (guns == null) return;
            // Заряд: цель быстрая или уходит — книппели (остановить), иначе ядра.
            ShipController fc = foe.Controller;
            float foeSpeed = fc != null ? Mathf.Abs(fc.Speed) : 0f;
            bool runner = (foeSpeed > 23f || foe.InPort || (foe.Health01 < 0.3f && Vector3.Dot(foe.transform.forward, dir) > 0.5f)) && !fc.BalloonHit && dist < own * 0.7f;
            guns.chainShot = runner;

            bool zone = SkyFortress.InSafeZone(transform.position);
            if (zone && !foe.Outlaw && !bold) return;
            // Расчёт борта наводится на ближайшего врага — стреляем, когда это и есть цель (или любой враг в прицеле: не жалеем ядер).
            if (guns.AimOnTarget && guns.Readiness(guns.AimSide) >= 0.999f) guns.Fire(guns.AimSide);
            // Окружили — второй борт бьёт свою цель, если его залп ложится в корпус.
            if (guns.OffOnTarget && guns.Readiness(guns.OffSide) >= 0.999f) { guns.Fire(guns.OffSide); DebugReport.Count("залпов вторым бортом"); }
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
                state = fort.Hostile ? "АВТОБОЙ · ЖДЁМ ОТБОЯ ТРЕВОГИ" : "АВТОБОЙ · РЕМОНТ";
                ship.turn = 0f; Throttle = 0f; ship.throttle = 0f;
                return;
            }
            state = "АВТОБОЙ · К ПРИЧАЛУ";
            // По пути отстреливаемся: враг в прицеле и борт заряжен — залп (вне зоны крепости).
            if (guns != null && !SkyFortress.InSafeZone(transform.position) && guns.AimOnTarget && guns.Readiness(guns.AimSide) >= 0.999f) guns.Fire(guns.AimSide);
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
            guns = guns != null ? guns : GetComponent<Broadside>();
            if (guns != null) guns.chainShot = false;
            if (fort == null) { Steer(transform.forward, 0.68f); return; }
            // Дозор по кругу за краем зоны, по часовой — навстречу сторожевым; торговец в пути — идём к нему.
            foreach (AllyShip al in AllyShip.All)
                if (al != null && al.Alive && al.role == AllyShip.Role.Merchant && !SkyFortress.InSafeZone(al.transform.position))
                {
                    Vector3 escort = al.transform.position + al.transform.right * 120f - transform.position; escort.y = 0f;
                    state = "АВТОБОЙ · ОХРАНА ТОРГОВЦА";
                    Steer(escort, escort.magnitude > 200f ? 1f : 0.5f);
                    return;
                }
            float radius = fort.safeRadius + 350f;
            Vector3 off = transform.position - fort.Center; off.y = 0f;
            float here = Mathf.Atan2(off.z, off.x) - 0.2f;
            Vector3 goal = fort.Center + new Vector3(Mathf.Cos(here), 0f, Mathf.Sin(here)) * radius;
            Steer(goal - transform.position, 0.68f);
        }

        void Steer(Vector3 heading, float throttle)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = transform.forward;
            float ang = Vector3.SignedAngle(transform.forward, heading.normalized, Vector3.up);
            ship.turn = Mathf.Clamp(ang / 20f, -1f, 1f);
            Throttle = throttle; ship.throttle = throttle;
        }
    }
}
