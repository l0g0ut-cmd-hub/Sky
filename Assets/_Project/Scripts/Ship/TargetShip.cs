using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Корабль-враг. В бою держит игрока на траверзе в своей дальности (сближается наискось, слишком близко —
    /// отворачивает, подбитый ниже 30% — отходит на дальнюю дистанцию и отстреливается). Пока игрок в безопасной
    /// зоне крепости или подбит — ходит по случайным точкам вокруг него (ShipController получает руль и ход как от джойстика).
    /// Попадание ядра — взрыв на корпусе; после hitsToSink попаданий — большой взрыв, мишень исчезает и
    /// через respawnDelay появляется в другом месте.
    /// Попадание считается по отрезку пути ядра за кадр против габарита меша модели (в его локальных координатах).
    /// </summary>
    public class TargetShip : MonoBehaviour, IAimTarget
    {
        public static readonly List<TargetShip> All = new List<TargetShip>();

        public Transform player;
        public float minRange = 140f, maxRange = 320f;
        /// <summary>Дальше этого мишень бросает свою точку и идёт обратно к игроку — не убегает.</summary>
        public float leash = 450f;
        [Tooltip("Дальность боя бортом и дальность отхода подбитого (ниже 30% прочности), м")]
        public float fightRange = 330f, retreatRange = 520f;
        [Tooltip("На каком расстоянии от игрока появляется новый враг — за дальними облаками, а не в кадре")]
        public float spawnRange = 1200f;
        float fightSide, nextScrape;
        // Уклонение: когда борт игрока заряжен и наведён, враг на пару секунд резко отворачивает полным ходом.
        float evadeUntil = -1f, nextEvade, evadeTurn;
        [System.Serializable] class LightList { public float[] lights; }
        Vector3[] lamps = new Vector3[0];
        // «Затупы»: время от времени капитан мишени теряется — сбрасывает ход, дрейфует, крутит руль не туда.
        float dazeUntil = -1f, nextDaze, dazeTurn;
        public bool Dazed { get { return Time.time < dazeUntil; } }
        public int hitsToSink = 30;   // ~4 удачных бортовых залпа
        // ---------- капитан, порт, глупости ----------
        // Выучка капитана — на каждого нового врага своя: новичок чаще теряется, мажет и держится дальше; ветеран — наоборот.
        public enum Skill { Novice, Seasoned, Veteran }
        public Skill skill = Skill.Seasoned;
        public static readonly string[] SkillNames = { "НОВИЧОК", "БЫВАЛЫЙ", "ВЕТЕРАН" };
        float dazeScale = 1f;
        // Порт: подбитый (а иногда и целый) враг уходит чиниться к причалу крепости — там он мирный. Иногда по глупости
        // даёт залп прямо в порту: тогда крепость бьёт уже по нему, и он удирает из зоны.
        enum Mode { Fight, ToPort, InPort, Flee }
        // Дерзость (владелец 15.11): иногда враг начинает бой прямо в порту — под пушками крепости; сваливает, когда пушки
        // всерьёз прижимают (потерял ≥20% прочности с начала, ниже 35% или дольше 45 с). Дерзость капитана: новичок 0.08,
        // бывалый 0.16, ветеран 0.3 — шанс на каждую «мысль» (раз в 15–25 с), пока флагман или свои рядом в зоне.
        public bool Bold { get; private set; }
        float boldHealth, boldUntil, nextBoldThink;
        // Политика отношений: дерзость и глупости — только у «дурней» (владелец 16.11: «всяких дурней тоже оставь… у них будет
        // печальная судьба»): ~7% капитанов мелких и средних кораблей. Дерзит в порту, бьёт первым, таранит, не уступает дорогу.
        public bool Fool { get; private set; }
        float Boldness { get { return Fool ? 0.3f : 0f; } }
        /// <summary>Огонь в зоне крепости: нарушитель на 45 с, крепость отвечает.</summary>
        public void MarkOutlaw() { if (!Outlaw) { DebugReport.Count("дерзких залпов в порту"); } OutlawUntil = Time.time + 45f; SkyFortress.NotifyEnemyFired(this); }
        Mode mode = Mode.Fight;
        float nextPortThink, portUntil, dumbAt = -1f, nextPortRepair, burnClock;
        /// <summary>До какого времени крепость считает этот корабль нарушителем и стреляет по нему.</summary>
        public float OutlawUntil { get; private set; } = -1f;
        public bool Outlaw { get { return Time.time < OutlawUntil; } }
        public bool InPort { get { return mode == Mode.ToPort || mode == Mode.InPort; } }
        EnemyGunner gunner; bool captainRolled;
        BerthRing ring; bool ringTried;

        // ---------- отношения (Core/Relations) ----------
        /// <summary>Дело корабля в мирное время: идёт по своим делам, дозор своих вод, ходит в порт.</summary>
        public enum Duty { Travel, Patrol, Port }
        public static readonly string[] DutyNames = { "ПО ДЕЛАМ", "ДОЗОР", "В ПОРТ" };
        [System.NonSerialized] public Duty duty = Duty.Travel;
        public Temper temper = Temper.Cautious;
        public Attitude Mood { get; private set; } = Attitude.Calm;
        /// <summary>В бою с гарнизоном (флагман и свои): только тогда пушки стреляют.</summary>
        public bool AtWar { get { return Alive && Mood == Attitude.Hostile; } }
        /// <summary>Враждебен именно флагману: бой не со своим кораблём (столкновение, таможня), либо флагман сам ввязался
        /// (попал в него за последние 30 с), либо набег. Иначе корсар бьётся только со своим противником, флагман не трогает.</summary>
        public bool HostileToPlayer { get { return AtWar && (FoeAlly == null || Raiding || Time.time - lastPlayerHitAt < 30f); } }
        public bool Fleeing { get { return Mood == Attitude.Flee; } }
        public bool Surrendered { get { return Mood == Attitude.Surrender; } }
        /// <summary>Короткая плашка намерения над кораблём (HUD рисует ближе 600 м).</summary>
        public string MoodText { get; private set; } = "";
        public float MoodTextUntil { get; private set; } = -1f;
        public bool Raiding { get; private set; }
        float zoneClock;
        float moodUntil, aimClock, calmClock, fightStartHealth = 1f, nextMorale, surrenderAt, lastHitAt = -100f, warnedAt = -100f, nextRelate;
        string moodWhy = "";
        AllyShip foeAlly;
        public AllyShip FoeAlly { get { return foeAlly != null && foeAlly.Alive ? foeAlly : null; } }
        static float lastWatersNotice = -100f;

        void SetMood(Attitude a, string why, string plate = null)
        {
            if (a == Mood && why == moodWhy) return;
            Attitude was = Mood; Mood = a; moodWhy = why;
            WorldLog.Raw("НАСТРОЙ " + WorldLog.Name(this) + " " + was + " → " + a + " («" + why + "»)");
            if (a == Attitude.Hostile && was != Attitude.Hostile) { fightStartHealth = Health01; nextMorale = Time.time + 4f; calmClock = 0f; }
            if (a == Attitude.Calm) { Raiding = false; foeAlly = null; aimClock = 0f; }
            if (a == Attitude.Surrender) { surrenderAt = Time.time; ctrl.throttle = 0f; ctrl.turn = 0f; }
            MoodText = plate ?? Relations.Name(a); MoodTextUntil = Time.time + 4f;
            if (Application.isPlaying) Announce(was, a, why);
            if (Application.isPlaying)
            {
                DebugReport.Event(name + " (" + (Class != ShipClass.None ? ShipClasses.Get(Class).name : "корсар") + "): " + Relations.Name(was) + " → " + Relations.Name(a) + " (" + why + ")");
                DebugReport.Count("корсар: " + Relations.Name(a).ToLower());
            }
        }
        /// <summary>Сообщение игроку о смене отношения (HudNotice): только если его касается или рядом. Кто — класс, почему —
        /// по-человечески, где — сторона от флагмана и дальность; метка на корабле.</summary>
        void Announce(Attitude was, Attitude a, string why)
        {
            float d = HudNotice.DistToPlayer(transform.position);
            string who = HudNotice.Who(this), where = HudNotice.Where(transform.position);
            bool vsPlayer = FoeAlly == null;
            string reason = HudNotice.Reason(why);
            string sub = reason.Length > 0 ? reason + " · " + where : where;
            string k = "mood" + GetInstanceID();
            switch (a)
            {
                case Attitude.Hostile:
                    if (vsPlayer && (d < 2500f || Raiding)) HudNotice.Post(who + " ВРАЖДЕБЕН", sub, 1, 4f, HudNotice.Prio.Critical, k, transform, 10f);
                    else if (d < 1500f) HudNotice.Post(who + " АТАКУЕТ " + HudNotice.Who(FoeAlly), where, 1, 3.5f, HudNotice.Prio.Info, k, transform, 10f);
                    break;
                case Attitude.Wary:
                    if (d < 1500f) HudNotice.Post(who + (why.StartsWith("прицел") ? ": ОТВЕДИ ПУШКИ" : ": УХОДИ ИЗ НАШИХ ВОД"),
                        (why.StartsWith("прицел") ? "ТЫ ДЕРЖИШЬ ЕГО НА ПРИЦЕЛЕ" : "ВОДЫ КОРСАРОВ · " + Mathf.RoundToInt(Relations.WarnSeconds) + " С") + " · " + where,
                        1, 4f, HudNotice.Prio.Critical, k, transform, 20f, new Color(1f, 0.85f, 0.35f, 1f));
                    break;
                case Attitude.Flee:
                    if (d < 1200f && was == Attitude.Hostile) HudNotice.Post(who + " БЕЖИТ", where, 0, 3f, HudNotice.Prio.Combat, k, transform, 8f, new Color(1f, 0.6f, 0.3f, 1f));
                    break;
                case Attitude.Surrender:
                    if (d < 1500f) HudNotice.Post(who + " СПУСТИЛ ФЛАГ", "НЕ СТРЕЛЯЙ · АБОРДАЖ — ПРИЗ · " + where, 0, 4.5f, HudNotice.Prio.Combat, k, transform, 0f, new Color(0.85f, 0.85f, 0.85f, 1f));
                    break;
                case Attitude.Calm:
                    if (was == Attitude.Surrender && d < 1500f) HudNotice.Post(who + " ОТПУЩЕН", "УХОДИТ К СВОИМ · " + where, 0, 3f, HudNotice.Prio.Info, k, transform);
                    else if (was == Attitude.Hostile && vsPlayer && d < 2500f && why != "противника нет") HudNotice.Post(who + " ПРЕКРАТИЛ БОЙ", where, 0, 3f, HudNotice.Prio.Info, k, transform, 10f, new Color(0.95f, 0.92f, 0.85f, 1f));
                    break;
            }
        }
        /// <summary>Набег (Relations.Tick): враждебен к флагману без повода, идёт к нему через всю карту.</summary>
        public void StartRaid() { if (mode != Mode.Fight) mode = Mode.Fight; SetMood(Attitude.Hostile, "набег"); Raiding = true; raidAt = Time.time; }
        float raidAt = -1000f;
        /// <summary>Отладка (команда mood): поставить отношение вручную.</summary>
        public void ForceMood(Attitude a) { SetMood(a, "команда"); if (a == Attitude.Wary) moodUntil = Time.time + Relations.WarnSeconds; }
        /// <summary>Таможня/сторожевой гарнизона подошёл к чужому в своих водах: уходи. Лихой изредка отвечает огнём.</summary>
        public void Warned(AllyShip by)
        {
            if (Mood != Attitude.Calm || Time.time - warnedAt < 40f) return;
            warnedAt = Time.time;
            if (temper == Temper.Bold && Random.value < 0.12f) { foeAlly = by; Decide("отказ уйти из вод гарнизона", true, false); return; }
            MoodText = "УХОДИТ ИЗ ВОД ГАРНИЗОНА"; MoodTextUntil = Time.time + 4f;
            waypoint = Relations.CorsairPoint(transform.position.y); nextPick = Time.time + 60f;
            DebugReport.Count("корсаров выпровожено");
        }
        /// <summary>Столкнулись на ходу с кораблём гарнизона (Traffic): обида — иногда это и начинает бой.</summary>
        public void Collided(AllyShip with, float closing)
        {
            if (!Alive || Mood != Attitude.Calm) return;
            if (SkyFortress.InSafeZone(transform.position) && !Fool) { MoodText = "ОСТОРОЖНЕЕ!"; MoodTextUntil = Time.time + 3f; return; }   // у крепости не дерутся (кроме дурня)
            float chance = Fool ? 0.95f : temper == Temper.Bold ? 0.6f : temper == Temper.Cautious ? 0.3f : 0.1f;
            if (Random.value < chance * Mathf.Clamp01(closing / 15f)) { foeAlly = with; Decide("столкновение со своим", false, false); }   // сообщение — Announce («АТАКУЕТ …»)
            else { MoodText = "ОСТОРОЖНЕЕ!"; MoodTextUntil = Time.time + 3f; }
        }

        /// <summary>Решение, когда задели интересы: отпор, бегство или сдача — по силам (класс, прочность, друзья ближе 1,5 км),
        /// нраву капитана и тому, защищает ли он свои воды.</summary>
        void Decide(string why, bool defending, bool allowSurrender)
        {
            if (!Alive) return;
            if (mode != Mode.Fight) mode = Mode.Fight;
            float mine = Relations.Power(this), friends = 0f;
            foreach (TargetShip t in All)
                if (t != null && t != this && t.Alive && !t.Fleeing && !t.Surrendered && (t.transform.position - transform.position).sqrMagnitude < 1500f * 1500f) friends += Relations.Power(t) * 0.6f;
            AllyShip fa = FoeAlly;
            float theirs = fa != null && (player == null || (fa.transform.position - transform.position).sqrMagnitude < (player.position - transform.position).sqrMagnitude)
                ? fa.hitsToSink * fa.Health01 * Mathf.Max(1, fa.gunsPerSide) : Relations.PlayerPower();
            float ratio = (mine + friends) / Mathf.Max(1f, theirs);
            float need = temper == Temper.Bold ? 0.3f : temper == Temper.Cautious ? 0.7f : 1.3f;
            if (defending) need *= 0.7f;
            if (Class == ShipClass.Cutter || Class == ShipClass.Sloop || Class == ShipClass.Fireship) need = 99f;   // мелочь не дерётся — бежит
            if (Class == ShipClass.Admiral) need = 0f;                                                              // адмиральский не бежит
            if (allowSurrender && Health01 < 0.25f && Class != ShipClass.Admiral) { SetMood(Attitude.Surrender, why, "СПУСТИЛ ФЛАГ"); DebugReport.Count("сдач"); return; }
            if (ratio >= need) { SetMood(Attitude.Hostile, why + ", силы ×" + ratio.ToString("F1")); CallHelp(); }
            else SetMood(Attitude.Flee, why + ", силы ×" + ratio.ToString("F1"));
        }
        /// <summary>Зов о помощи: свои ближе 1,5 км приходят по нраву (лихой 75%, осторожный 45%, трус 15%) — не вся карта.</summary>
        float callAt = -999f, helperUntil = -999f;
        /// <summary>Зов о помощи. 16.11 (прокат): цепная реакция — каждый пришедший звал следующих, «зов о помощи» — главный
        /// повод вражды (≈60 из 89). Теперь: не чаще раза в 90 с, ближе 800 м, не больше двух, только вне вод гарнизона
        /// (кроме набега), пришедший сам минуту никого не зовёт; шансы ниже (лихой 35%, осторожный 15%, трус 3%).</summary>
        void CallHelp()
        {
            if (Time.time < helperUntil || Time.time - callAt < 90f) return;
            callAt = Time.time;
            int came = 0;
            foreach (TargetShip t in All)
            {
                if (came >= 2) break;
                if (t == null || t == this || !t.Alive || t.InPort || (t.Mood != Attitude.Calm && t.Mood != Attitude.Wary)) continue;
                if ((t.transform.position - transform.position).sqrMagnitude > 800f * 800f) continue;
                Waters w = Relations.At(t.transform.position);
                if ((w == Waters.Garrison || w == Waters.Safe) && !t.Raiding) continue;
                float chance = t.temper == Temper.Bold ? 0.35f : t.temper == Temper.Cautious ? 0.15f : 0.03f;
                if (Random.value < chance)
                {
                    t.foeAlly = foeAlly; t.helperUntil = Time.time + 60f;
                    t.SetMood(Attitude.Hostile, "зов о помощи от " + name, "ИДЁТ НА ПОМОЩЬ"); came++;
                }
            }
        }

        /// <summary>Поводы раз в 0,25 с: флагман в водах корсаров рядом, долгий прицел в упор; остывание вражды.</summary>
        void Relate(float dt)
        {
            PlayerHull ph = PlayerHull.Instance;
            bool playerOk = ph != null && ph.Alive && player != null;
            float dist = playerOk ? Vector3.Distance(player.position, transform.position) : float.MaxValue;
            // Прицел в упор: игрок держит заряженный борт на этом корабле ближе AimRange.
            if (playerFor != player) { playerFor = player; playerGuns = player != null ? player.GetComponent<Broadside>() : null; playerCtrl = player != null ? player.GetComponent<ShipController>() : null; }
            bool aimed = playerOk && playerGuns != null && playerGuns.AimOnTarget && playerGuns.AimTargetShip == this && dist < Relations.AimRange && playerGuns.Readiness(playerGuns.AimSide) >= 0.999f;
            aimClock = aimed ? aimClock + dt : Mathf.Max(0f, aimClock - dt * 0.5f);
            if (Time.time < nextRelate) return;
            nextRelate = Time.time + 0.25f;
            switch (Mood)
            {
                case Attitude.Calm:
                    if (!playerOk || InPort) break;
                    if (aimClock >= Relations.AimSeconds) { SetMood(Attitude.Wary, "прицел в упор", "ОТВЕДИ ПУШКИ"); moodUntil = Time.time + 6f; break; }
                    // Дурень: изредка лезет на флагман без повода (в нейтральных и своих водах, не у крепости).
                    if (Fool && dist < 450f && Relations.At(transform.position) != Waters.Safe && Random.value < 0.005f) { Decide("дурень лезет в драку", false, false); if (Mood == Attitude.Flee) SetMood(Attitude.Hostile, "дурень всё равно лезет", "ДЕРЗИТ"); break; }
                    if (Relations.At(player.position) == Waters.Corsair && dist < 1200f)
                    {
                        SetMood(Attitude.Wary, "флагман в водах корсаров", "УХОДИ ИЗ НАШИХ ВОД");
                        moodUntil = Time.time + Relations.WarnSeconds;
                        lastWatersNotice = Time.time;   // сообщение — в Announce (кто, где, сколько секунд)
                    }
                    break;
                case Attitude.Wary:
                    if (!playerOk) { SetMood(Attitude.Calm, "флагман погиб"); break; }
                    if (moodWhy == "флагман в водах корсаров")
                    {
                        if (Relations.At(player.position) != Waters.Corsair || dist > 1500f) { SetMood(Attitude.Calm, "флагман ушёл из вод"); break; }
                        if (Time.time >= moodUntil) { Relations.AddGrudge(4f, "не ушёл из вод корсаров"); Decide("не ушёл из наших вод", true, false); }
                        else { MoodText = "УХОДИ ИЗ НАШИХ ВОД · " + Mathf.CeilToInt(moodUntil - Time.time) + " С"; MoodTextUntil = Time.time + 1f; }
                    }
                    else if (Time.time >= moodUntil)
                    {
                        if (aimClock > 0.5f && temper == Temper.Bold && Random.value < 0.5f) Decide("держал на прицеле — бьёт первым", false, false);
                        else if (aimClock > 0.5f) SetMood(Attitude.Flee, "держал на прицеле", "ОТВОРАЧИВАЕТ");
                        else SetMood(Attitude.Calm, "пушки отведены");
                    }
                    break;
                case Attitude.Hostile:
                    if (!playerOk && FoeAlly == null) { Bold = false; SetMood(Attitude.Calm, "противника нет"); break; }
                    if (Bold && (Health01 < boldHealth - 0.2f || Health01 < 0.35f || Time.time > boldUntil))
                    {
                        Bold = false; SetMood(Attitude.Flee, "дерзкий сваливает из порта", "СВАЛИВАЕТ");

                        break;
                    }
                    // Враждебный в безопасной зоне дольше 6 с (не проходом) — крепость считает нарушителем и бьёт.
                    zoneClock = SkyFortress.InSafeZone(transform.position) ? zoneClock + 0.25f : 0f;
                    if (zoneClock > 6f && !Outlaw)
                    {
                        MarkOutlaw();
                        if (HudNotice.DistToPlayer(transform.position) < 2500f)
                            HudNotice.Post("КРЕПОСТЬ БЬЁТ: " + HudNotice.Who(this), "ВРАЖДЕБНЫЙ В ЗОНЕ КРЕПОСТИ · " + HudNotice.Where(transform.position), 0, 3f, HudNotice.Prio.Combat, "fortout" + GetInstanceID(), transform);
                    }
                    // Остывает: противник далеко дольше CalmAfter (набег — не остывает, пока флагман жив).
                    bool near = (playerOk && dist < Mathf.Max(1800f, aggroRange)) || (FoeAlly != null && (FoeAlly.transform.position - transform.position).sqrMagnitude < 1800f * 1800f);
                    calmClock = near || (Raiding && Time.time - raidAt < 300f) ? 0f : calmClock + 0.25f;   // набег остывает через 5 мин без боя
                    if (calmClock > Relations.CalmAfter) { SetMood(Attitude.Calm, "противник ушёл"); break; }
                    // Мораль: потерял 40% прочности с начала боя — решает заново (бежать или сдаться).
                    if (Time.time >= nextMorale) { nextMorale = Time.time + 4f; if (Health01 < fightStartHealth - 0.4f) { fightStartHealth = Health01; Decide("мораль", false, true); } }
                    break;
                case Attitude.Flee:
                    {
                        Vector3 threat = ThreatPos();
                        float td = Vector3.Distance(threat, transform.position);
                        if (td > 1500f) { SetMood(Attitude.Calm, "оторвался"); break; }
                        float threatSpeed = FoeAlly != null ? FoeAlly.Velocity.magnitude : (playerCtrl != null ? Mathf.Abs(playerCtrl.Speed) : 0f);
                        if (td < 250f && threatSpeed > Mathf.Abs(ctrl.Speed) + 2f && lastHitAt > Time.time - 20f)   // догнали и бьют — загнанный
                        {
                            if (Health01 < 0.25f && Class != ShipClass.Admiral) { SetMood(Attitude.Surrender, "загнан", "СПУСТИЛ ФЛАГ"); DebugReport.Count("сдач"); }
                            else SetMood(Attitude.Hostile, "загнан — дерётся", "ЗАГНАН — ДЕРЁТСЯ");
                        }
                        break;
                    }
                case Attitude.Surrender:
                    // Отпустили: 20 с без попаданий и флагман не на абордаже рядом — уходит к своим, злоба меньше.
                    if (!BeingBoarded && Time.time - surrenderAt > 20f && Time.time - lastHitAt > 20f && dist > 120f)
                    {
                        Relations.AddGrudge(-6f, "отпущен сдавшийся");
                        SetMood(Attitude.Calm, "отпущен", "ОТПУЩЕН — УХОДИТ");
                        waypoint = Relations.CorsairPoint(transform.position.y); nextPick = Time.time + 90f;
                    }
                    break;
            }
        }
        Vector3 ThreatPos() { AllyShip fa = FoeAlly; if (fa != null && (player == null || (fa.transform.position - transform.position).sqrMagnitude < (player.position - transform.position).sqrMagnitude)) return fa.transform.position; return player != null ? player.position : transform.position; }

        /// <summary>Поведение по отношению: true — кадр занят (бой, бегство, сдача, настороже).</summary>
        bool ActByMood()
        {
            switch (Mood)
            {
                case Attitude.Surrender: Ai("СДАЛСЯ"); ctrl.throttle = 0f; ctrl.turn = Mathf.MoveTowards(ctrl.turn, 0f, Time.deltaTime); return true;
                case Attitude.Flee:
                    {
                        Vector3 away = transform.position - ThreatPos(); away.y = 0f;
                        Vector3 home = Relations.CorsairPoint(transform.position.y) - transform.position; home.y = 0f;
                        Vector3 h = (away.normalized * 0.65f + home.normalized * 0.35f);
                        Ai("БЕЖИТ", Mathf.RoundToInt(away.magnitude) + " м");
                        SteerTo(transform.position + h.normalized * 500f, 1f);
                        return true;
                    }
                case Attitude.Wary:
                    {
                        // Настороже — бортом к флагману на своей дальности (готов ответить), без огня.
                        if (player == null) return false;
                        Vector3 tp = player.position - transform.position; tp.y = 0f;
                        Ai("НАСТОРОЖЕ", moodWhy);
                        Engage(tp); return true;
                    }
                case Attitude.Hostile:
                    {
                        AllyShip fa = FoeAlly;
                        PlayerHull ph = PlayerHull.Instance;
                        Vector3 tp = player != null ? player.position - transform.position : Vector3.zero; tp.y = 0f;
                        bool playerTarget = ph != null && ph.Alive && player != null && (!SkyFortress.InSafeZone(player.position) || Bold);
                        if (fa != null && !Bold && SkyFortress.InSafeZone(fa.transform.position)) fa = null;   // свой под пушками крепости — не лезем
                        if (!HostileToPlayer) playerTarget = false;   // бой со своим кораблём — флагман не цель, пока сам не ввяжется
                        if (fa != null && (!playerTarget || (fa.transform.position - transform.position).sqrMagnitude < tp.sqrMagnitude * 0.8f))
                        {
                            Vector3 ta = fa.transform.position - transform.position; ta.y = 0f;
                            Ai("БОЙ-" + AllyShip.RoleNames[(int)fa.role].Substring(0, 4), Mathf.RoundToInt(ta.magnitude) + " м"); Engage(ta); return true;
                        }
                        if (playerTarget) { Ai(Raiding ? "НАБЕГ" : Health01 < 0.3f ? "ОТХОД" : "БОЙ", "флагман " + Mathf.RoundToInt(tp.magnitude) + " м"); Engage(tp); return true; }
                        SkyFortress sf = SkyFortress.Instance;
                        if (sf != null)
                        {
                            // Флагман под пушками крепости — ждём снаружи, на 300 м за краем зоны, не заходя в порт.
                            Vector3 off = transform.position - sf.Center; off.y = 0f; if (off.sqrMagnitude < 1f) off = Vector3.forward;
                            Vector3 wait = sf.Center + off.normalized * (sf.safeRadius + 300f); wait.y = transform.position.y;
                            Ai("ЖДЁТ", "у края зоны крепости");
                            SteerTo(wait, (wait - transform.position).magnitude > 150f ? 0.6f : 0.15f);
                            return true;
                        }
                        return false;
                    }
            }
            return false;
        }
        /// <summary>Поведение сейчас (для хроники и журнала решений ИИ отладочного отчёта).</summary>
        public string AiState { get; private set; } = "";
        void Ai(string s, string why = "")
        {
            if (s == AiState) return;
            if (Application.isPlaying) DebugReport.Ai(name.Replace(" Ship", "") + ": " + (AiState.Length > 0 ? AiState : "старт") + " → " + s + (why.Length > 0 ? " (" + why + ")" : ""));
            AiState = s;
        }
        /// <summary>Залп игрока: сколько ядер подряд легло в корпус (HUD показывает итог).</summary>
        public static int Tally; public static float TallyAt = -100f;

        // Тяжёлый: иногда рядовой враг выходит тяжёлым — вдвое прочнее, медленнее, 6 пушек на борт, бьёт дальше.
        [System.NonSerialized] public bool canBeHeavy;
        [System.NonSerialized] public float heavyChance = 0.22f;
        /// <summary>Конфиг: новые базовые числа (ход, поворот, прочность, дальность, перезарядка) — с учётом тяжёлого.</summary>
        public void Tune(float speed, float yaw, int hitsMax, float range, float cooldown)
        {
            baseSaved = true; baseSpeed = speed; baseYaw = yaw; baseHits = hitsMax; baseRange = range; baseCooldown = cooldown;
            if (gunner == null) gunner = GetComponent<EnemyGunner>();
            if (gunner != null && baseGuns == 0) baseGuns = gunner.gunsPerSide;
            hitsToSink = Heavy ? baseHits * 2 : baseHits;
            if (ctrl != null) { ctrl.maxSpeed = Heavy ? baseSpeed * 0.75f : baseSpeed; ctrl.maxYawRate = Heavy ? baseYaw * 0.75f : baseYaw; }
            if (gunner != null) { gunner.maxRange = Heavy ? baseRange * 1.15f : baseRange; gunner.cooldown = Heavy ? baseCooldown * 1.35f : baseCooldown; }
        }
        /// <summary>Тест: прочность в долях (0..1).</summary>
        public void SetHealth01(float f) { if (Alive) hits = Mathf.Clamp(Mathf.RoundToInt((1f - f) * hitsToSink), 0, hitsToSink - 1); }
        public bool Heavy { get; private set; }
        /// <summary>Класс корабля (ShipClasses): своя модель, размер и огневая мощь. None — прежний Корсар (рядовой/разведчик).</summary>
        public ShipClass Class { get; private set; } = ShipClass.None;
        /// <summary>Длина корпуса, м (по модели): столкновения и дистанции масштабируются по ней.</summary>
        public float Length { get { return Mathf.Max(10f, box.size.z * transform.lossyScale.z); } }
        /// <summary>Корпус для столкновений (Traffic): полуширина и полудлина корпуса по модели (без бушприта и баллонов).</summary>
        public float HullHalfWidth { get { return Mathf.Max(2f, box.extents.x * 0.9f * transform.lossyScale.x); } }
        public float HullHalfLength { get { return Mathf.Max(6f, box.extents.z * 0.88f * transform.lossyScale.z); } }
        public void SetClass(ShipClass c)
        {
            Class = c; canBeHeavy = false; Footprint.Forget(transform);   // контур — по новой модели
            ShipClassInfo k = ShipClasses.Get(c);
            if (gunner == null) gunner = GetComponent<EnemyGunner>();
            baseSaved = true; baseHits = k.hits; baseSpeed = k.speed; baseYaw = k.yaw; baseGuns = k.guns; baseRange = k.range; baseCooldown = k.cooldown;
            if (gunner != null) { gunner.caliber = k.caliber; gunner.muzzleSpeed = k.muzzle; gunner.classError = k.accuracy; }
            if (ctrl != null) { ctrl.acceleration = k.accel; ctrl.deceleration = k.accel * 1.2f; }
            ApplyClass();
        }
        /// <summary>Конфиг для классов: множители к базе класса (относительно брига: ход 20, поворот 24, 30 попаданий, 420 м, 6 с).</summary>
        public void TuneClass(float speedMul, float yawMul, float hitsMul, float rangeMul, float cooldownMul)
        {
            if (Class == ShipClass.None) return;
            ShipClassInfo k = ShipClasses.Get(Class);
            baseSpeed = k.speed * speedMul; baseYaw = k.yaw * yawMul; baseHits = Mathf.Max(1, Mathf.RoundToInt(k.hits * hitsMul));
            baseRange = k.range * rangeMul; baseCooldown = k.cooldown * cooldownMul; baseGuns = k.guns;
            ApplyClass();
        }
        bool baseSaved; int baseHits; float baseSpeed, baseYaw; int baseGuns; float baseRange, baseCooldown;
        /// <summary>Взят на абордаж (а не потоплен) — HUD пишет «ПРИЗ ВЗЯТ».</summary>
        public bool Captured { get; private set; }
        /// <summary>Идёт абордаж: корабль лежит в дрейфе.</summary>
        [System.NonSerialized] public bool BeingBoarded;
        public void Capture()
        {
            if (!Alive) return;
            Captured = true; BeingBoarded = false;
            DebugReport.Count("призов взято"); DebugReport.Event(name + " взят на абордаж");
            deadUntil = Time.time + respawnDelay;
            ctrl.throttle = 0f; ctrl.turn = 0f;
            ctrl.visual.gameObject.SetActive(false);
        }
        void ApplyClass()
        {
            if (gunner == null) gunner = GetComponent<EnemyGunner>();
            if (!baseSaved)
            {
                baseSaved = true; baseHits = hitsToSink; baseSpeed = ctrl.maxSpeed; baseYaw = ctrl.maxYawRate;
                if (gunner != null) { baseGuns = gunner.gunsPerSide; baseRange = gunner.maxRange; baseCooldown = gunner.cooldown; }
            }
            if (Class != ShipClass.None)
            {
                // Класс: числа — из таблицы классов, без случайного «тяжёлого» и без тёмного тона (у модели своя окраска).
                Heavy = ShipClasses.IsHeavy(Class);
                hitsToSink = baseHits; ctrl.maxSpeed = baseSpeed; ctrl.maxYawRate = baseYaw;
                if (gunner != null) { gunner.gunsPerSide = baseGuns; gunner.maxRange = baseRange; gunner.cooldown = baseCooldown; }
                return;   // «линейный корабль врага» при появлении — шум: о гиганте скажет его враждебность (Announce)
            }
            Heavy = canBeHeavy && captainRolled && Random.value < heavyChance;
            hitsToSink = Heavy ? baseHits * 2 : baseHits;
            ctrl.maxSpeed = Heavy ? baseSpeed * 0.75f : baseSpeed; ctrl.maxYawRate = Heavy ? baseYaw * 0.75f : baseYaw;
            if (gunner != null) { gunner.gunsPerSide = Heavy ? 6 : baseGuns; gunner.maxRange = Heavy ? baseRange * 1.15f : baseRange; gunner.cooldown = Heavy ? baseCooldown * 1.35f : baseCooldown; }
            // Тяжёлый — тёмный, почти чёрный корпус: виден издалека.
            MaterialPropertyBlock tint = new MaterialPropertyBlock();
            tint.SetColor("_Color", Heavy ? new Color(0.55f, 0.42f, 0.4f) : Color.white);
            foreach (MeshRenderer mr in ctrl.visual.GetComponentsInChildren<MeshRenderer>(true)) mr.SetPropertyBlock(tint);

        }

        void RollCaptain()
        {
            Captured = false; BeingBoarded = false;
            ApplyClass();
            float r = Random.value;
            skill = r < 0.35f ? Skill.Novice : r < 0.8f ? Skill.Seasoned : Skill.Veteran;
            dazeScale = skill == Skill.Novice ? 0.6f : skill == Skill.Veteran ? 2.2f : 1f;
            fightRange = skill == Skill.Novice ? 300f : skill == Skill.Veteran ? 220f : 260f;   // в пределах своих 420 м
            if (Class != ShipClass.None) { float kr = baseRange / 420f; fightRange *= kr; retreatRange = 380f * kr; }   // дистанция боя — по дальности класса
            if (gunner == null) gunner = GetComponent<EnemyGunner>();
            if (gunner != null)
            {
                gunner.skillError = skill == Skill.Novice ? 1.5f : skill == Skill.Veteran ? 0.7f : 1f;
                gunner.skillCooldown = skill == Skill.Novice ? 1.25f : skill == Skill.Veteran ? 0.85f : 1f;
            }
            mode = Mode.Fight; OutlawUntil = -1f; dumbAt = -1f; Bold = false; FreeGuestSlot();
            float tr = Random.value;
            temper = tr < 0.3f ? Temper.Coward : tr < 0.8f ? Temper.Cautious : Temper.Bold;
            if (Class == ShipClass.Admiral || Class == ShipClass.ShipOfLine) temper = Temper.Bold;
            Fool = !ShipClasses.IsHeavy(Class) && Random.value < 0.07f;
            float dr = Random.value;
            duty = dr < 0.45f ? Duty.Travel : dr < 0.8f ? Duty.Patrol : Duty.Port;
            Mood = Attitude.Calm; moodWhy = ""; Raiding = false; foeAlly = null; aimClock = 0f; zoneClock = 0f; lastPlayerHitAt = -100f;
            nextPortThink = Time.time + Random.Range(25f, 45f);
            if (ctrl != null) ctrl.ClearCrits();
        }

        /// <summary>Курс на точку в обход камня крепости: внутри круга причала курс не ведёт к центру, пока цель далеко.</summary>
        public Vector3 Goal;
        /// <summary>Таймеры отношений (журнал мира).</summary>
        public string Timers()
        {
            return "зона " + zoneClock.ToString("F0") + "с прицел " + aimClock.ToString("F1") + "с покой " + calmClock.ToString("F0") + "с"
                + (Outlaw ? " нарушитель ещё " + (OutlawUntil - Time.time).ToString("F0") + "с" : "")
                + (Surrendered ? " сдался " + (Time.time - surrenderAt).ToString("F0") + "с назад" : "")
                + (Raiding ? " набег " + (Time.time - raidAt).ToString("F0") + "с" : "") + (moodWhy != null && moodWhy.Length > 0 ? " повод «" + moodWhy + "»" : "");
        }
        void SteerTo(Vector3 goal, float throttle)
        {
            Goal = goal;
            Vector3 to = goal - transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 heading = dist > 1f ? to / dist : transform.forward;
            SkyFortress f = SkyFortress.Instance;
            if (f != null && dist > 200f)
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
            if (dist > 150f + Length * 0.5f) heading = Traffic.FortressDetour(transform, heading, Length * 0.5f).normalized;
            float ang = Vector3.SignedAngle(transform.forward, heading, Vector3.up);
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? Mathf.Min(throttle, 0.3f) : throttle;
        }

        /// <summary>true — корабль занят портом или бегством, бой в этот кадр не ведётся.</summary>
        bool PortLife()
        {
            SkyFortress f = SkyFortress.Instance;
            if (f == null) { mode = Mode.Fight; return false; }
            if (Bold)
            {
                bool pressed = Health01 < boldHealth - 0.2f || Health01 < 0.35f || Time.time > boldUntil;
                if (!pressed) { Ai("ДЕРЗИТ", "бой в порту"); return false; }
                Bold = false; mode = Mode.Flee;
                DebugReport.Event(name + ": дерзкий сваливает из порта (" + Mathf.RoundToInt(Health01 * 100f) + "%)");
                if (player != null && (player.position - transform.position).sqrMagnitude < 900f * 900f) HudNotice.Post(HudNotice.Who(this) + " СВАЛИВАЕТ ИЗ ПОРТА", "ДЕРЗИЛ · " + HudNotice.Where(transform.position), 9, 3f, HudNotice.Prio.Info, "bold" + GetInstanceID(), transform);
            }
            else if (Time.time >= nextBoldThink && Alive && Health01 > 0.5f && (SkyFortress.InSafeZone(transform.position) || mode == Mode.ToPort))
            {
                nextBoldThink = Time.time + Random.Range(15f, 25f);
                bool foeHere = player != null && SkyFortress.InSafeZone(player.position) && (player.position - transform.position).sqrMagnitude < 700f * 700f;
                if (!foeHere) foreach (AllyShip al in AllyShip.All) if (al != null && al.Alive && SkyFortress.InSafeZone(al.transform.position) && (al.transform.position - transform.position).sqrMagnitude < 600f * 600f) { foeHere = true; break; }
                if (foeHere && Random.value < Boldness)
                {
                    Bold = true; boldHealth = Health01; boldUntil = Time.time + 45f; mode = Mode.Fight;
                    SetMood(Attitude.Hostile, "дурень дерзит в порту", "ДЕРЗИТ");
                    DebugReport.Count("дерзостей врага"); DebugReport.Event(name + ": дерзит — бой в порту");
                    if (player != null && (player.position - transform.position).sqrMagnitude < 900f * 900f) HudNotice.Post(HudNotice.Who(this) + " ДЕРЗИТ В ПОРТУ", "ДУРЕНЬ · КРЕПОСТЬ ЕГО НАКАЖЕТ · " + HudNotice.Where(transform.position), 9, 3.5f, HudNotice.Prio.Critical, "bold" + GetInstanceID(), transform);
                    return false;
                }
            }
            if (Outlaw && mode != Mode.Flee) mode = Mode.Flee;
            if (mode == Mode.Fight)
            {
                if (Time.time < nextPortThink) return false;
                nextPortThink = Time.time + Random.Range(20f, 35f);
                bool go = Health01 < 0.45f ? Random.value < 0.6f : Random.value < (duty == Duty.Port ? 0.6f : 0.06f);
                // Гости порта — по местам (их шесть, у мелких и средних; тяжёлый фрегат и крупнее в порт не ходят). Нет свободного —
                // не идёт (раньше все шли в одну точку — пробка у причала, владелец 16.11).
                if (go && (ShipClasses.IsHeavy(Class) || !TakeGuestSlot())) go = false;
                if (!go) return false;
                mode = Mode.ToPort;
                DebugReport.Count("врагов ушло в порт");
                // Глупость случается не с каждым: новичок срывается чаще, ветеран — почти никогда.
                float dumb = skill == Skill.Novice ? 0.55f : skill == Skill.Veteran ? 0.08f : 0.3f;
                dumbAt = Fool && Random.value < Mathf.Max(0.6f, dumb) ? Time.time + Random.Range(25f, 70f) : -1f;   // глупый залп в порту — только дурень

            }
            Vector3 outDir = f.BerthWorld - f.Center; outDir.y = 0f;
            outDir = outDir.sqrMagnitude > 1f ? outDir.normalized : Vector3.forward;
            if (mode == Mode.Flee)
            {
                Ai("БЕГ", Outlaw ? "крепость бьёт" : "из зоны");
                Vector3 off = transform.position - f.Center; off.y = 0f;
                if (!Outlaw && off.magnitude > f.safeRadius + 200f) { mode = Mode.Fight; Pick(); return false; }
                SteerTo(transform.position + (off.sqrMagnitude > 1f ? off.normalized : outDir) * 600f, 1f);
                return true;
            }
            // Место гостя — своё из шести (GuestSpot): дугой вокруг крепости по обе стороны, в стороне от стоянки и места флагмана.
            if (guestSlot < 0 && !TakeGuestSlot()) { mode = Mode.Fight; Pick(); return false; }
            Vector3 spot = GuestSpot(f, guestSlot, HullHalfWidth, HullHalfLength); spot.y = transform.position.y;
            float d = Vector3.Distance(new Vector3(transform.position.x, spot.y, transform.position.z), spot);
            bool inZone = SkyFortress.InSafeZone(transform.position);
            // Глупость: в зоне крепости, игрок рядом и жив — залп. Крепость этого не прощает.
            PlayerHull ph = PlayerHull.Instance;
            if (dumbAt > 0f && Time.time >= dumbAt && inZone && ph != null && ph.Alive && player != null && SkyFortress.InSafeZone(player.position))
            {
                dumbAt = -1f;
                if (gunner == null) gunner = GetComponent<EnemyGunner>();
                if (gunner != null && gunner.ForceVolley()) { DebugReport.Count("глупых залпов врага в зоне"); OutlawUntil = Time.time + 40f; SkyFortress.NotifyEnemyFired(this); mode = Mode.Flee; SetMood(Attitude.Flee, "дурень дал залп в порту", "ГЛУПЫЙ ЗАЛП"); return true; }
            }
            if (mode == Mode.ToPort)
            {
                Ai("В ПОРТ", Mathf.RoundToInt(Health01 * 100f) + "%");
                if (d < 45f) { mode = Mode.InPort; portUntil = Time.time + Random.Range(18f, 35f); }
                else
                {
                    // Подход к месту — снаружи, с его стороны от причала (17.11: гости шли к месту сквозь пирс и толпу у
                    // зоны ремонта, толкались по трое в 30 м). Сначала точка в 220 м от места наружу, потом — к месту.
                    Vector3 axis = f.BerthWorld - f.Center; axis.y = 0f; axis = axis.sqrMagnitude > 1f ? axis.normalized : Vector3.forward;
                    Vector3 rel = spot - f.Center; rel.y = 0f;
                    Vector3 side = rel - axis * Vector3.Dot(rel, axis); side = side.sqrMagnitude > 1f ? side.normalized : Vector3.Cross(Vector3.up, axis);
                    Vector3 gate = spot + side * 220f;
                    Vector3 me = transform.position - f.Center; me.y = 0f;
                    bool onSide = Vector3.Dot(me, side) > Vector3.Dot(rel, side) + 60f;   // уже снаружи, со своей стороны
                    if (!onSide && d > 140f) SteerTo(gate, 0.7f);
                    else SteerTo(spot, d > 320f ? 0.75f : d > 120f ? 0.4f : 0.2f);
                }
                return true;
            }
            Ai("ПОРТ");
            // У причала: стоит бортом к пирсу носом от крепости, чинится (попадание в 1.5 с), потом уходит.
            ctrl.throttle = 0f; ctrl.turn = 0f;
            transform.SetPositionAndRotation(Vector3.MoveTowards(transform.position, spot, 3f * Time.deltaTime),
                Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(outDir, Vector3.up), 8f * Time.deltaTime));
            if (Time.time >= nextPortRepair && hits > 0)
            {
                nextPortRepair = Time.time + 1.5f; hits--;
                if (hits == 0 && damageVisual != null) damageVisual.ResetDamage();
            }
            if (Time.time >= portUntil && hits <= hitsToSink / 4) { mode = Mode.Fight; FreeGuestSlot(); nextPortThink = Time.time + Random.Range(40f, 70f); Pick(); return false; }
            return true;
        }

        // ---------- места гостей порта ----------
        static readonly TargetShip[] guests = new TargetShip[6];
        int guestSlot = -1;
        bool TakeGuestSlot()
        {
            if (guestSlot >= 0 && guests[guestSlot] == this) return true;
            for (int i = 0; i < guests.Length; i++)
                if (guests[i] == null || !guests[i].Alive || guests[i].guestSlot != i || (guests[i].mode != Mode.ToPort && guests[i].mode != Mode.InPort))
                { if (guests[i] != null && guests[i] != this) guests[i].guestSlot = -1; guests[i] = this; guestSlot = i; return true; }
            return false;
        }
        void FreeGuestSlot() { if (guestSlot >= 0 && guests[guestSlot] == this) guests[guestSlot] = null; guestSlot = -1; }
        /// <summary>Место гостя — сбоку у причала (владелец 16.11: «не перед причалом, где ремонт, а с боков у причала»):
        /// по три с каждой стороны, вдоль пирса к крепости (нос в 12 м за кромкой причала, шаг 85 м), бортом к пирсу, зазор 10 м;
        /// край пирса ищется по поверхности крепости (TopAt) — гость встаёт сразу за ним.</summary>
        static Vector3 GuestSpot(SkyFortress f, int i, float halfW, float halfL)
        {
            Vector3 o = f.BerthWorld - f.Center; o.y = 0f; o = o.sqrMagnitude > 1f ? o.normalized : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, o) * (i % 2 == 0 ? 1f : -1f);
            Vector3 along = f.BerthWorld - o * (12f + halfL + (i / 2) * 85f);   // нос — не дальше кромки причала (там овал ремонта)
            float edge = 12f;
            for (float w = 8f; w < 260f; w += 4f) { if (f.TopAt(along + across * w) < f.BerthWorld.y - 40f) { edge = w; break; } }
            Vector3 spot = along + across * (edge + halfW + 10f);
            // Место не на стоянке (17.11: корсар-гость 5 с толкался в адмиральский стоянки) — отодвинуть наружу,
            // пока до каждого корабля стоянки не останется полкорпуса гостя + полкорпуса стоянки + 25 м.
            for (int k = 0; k < 12; k++)
            {
                bool clash = false;
                foreach (AllyShip h in AllyShip.All)
                {
                    if (h == null || !h.Alive || h.role != AllyShip.Role.Harbor) continue;
                    Vector3 d = h.transform.position - spot; d.y = 0f;
                    if (d.magnitude < halfL + h.Length * 0.5f + 25f) { clash = true; break; }
                }
                if (!clash) break;
                spot += across * 45f;
            }
            return spot;
        }
        public float respawnDelay = 6f;

        ShipController ctrl;
        Transform playerFor; Broadside playerGuns; ShipController playerCtrl;   // без покадрового GetComponent
        Transform model;
        Bounds box;
        /// <summary>Угол рамки корпуса в мире (i = 0..7) — для рамки цели на экране.</summary>
        public Vector3 Velocity { get { return ctrl != null ? transform.forward * ctrl.Speed : Vector3.zero; } }
        public Vector3 BoxCorner(int i) { Vector3 e = box.extents; Vector3 l = box.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z); return model != null ? model.TransformPoint(l) : transform.position; }
        Vector3 waypoint;
        float nextPick;
        int hits;
        float deadUntil = -1f;
        ShipDamageVisual damageVisual;
        ShipHitSurface surface;
        ShipHitSurface.Contact lastContact;
        bool hasContact;

        public bool Alive { get { return deadUntil < 0f; } }
        public ShipController Controller { get { return ctrl; } }
        public ShipDamageVisual DamageVisual { get { return damageVisual; } }
        public int HitCount { get { return hits; } }
        /// <summary>Остаток прочности 0–1 (для метки врага на HUD).</summary>
        public float Health01 { get { return Mathf.Clamp01(1f - hits / (float)Mathf.Max(1, hitsToSink)); } }
        /// <summary>Середина корпуса в мире.</summary>
        public Vector3 Center { get { return model != null ? model.TransformPoint(box.center) : transform.position; } }
        public ShipHitSurface Surface {get {return surface;}}
        public Vector3 ImpactNormal {get {return model.TransformDirection(lastContact.normal).normalized;}}

        public void Setup(ShipController c, Transform player, Material debrisWood = null,TextAsset geometry=null,TextAsset lights=null)
        {
            if (lights != null)
            {
                LightList list = JsonUtility.FromJson<LightList>(lights.text);
                if (list != null && list.lights != null)
                {
                    lamps = new Vector3[Mathf.Min(10, list.lights.Length / 3)];
                    for (int i = 0; i < lamps.Length; i++) lamps[i] = new Vector3(list.lights[i * 3], list.lights[i * 3 + 1], list.lights[i * 3 + 2]);
                }
            }
            // OnEnable в редакторе (кадры CI) не вызывается — регистрируемся сами, иначе попадания и доклад впередсмотрящего не видят мишень.
            if (!All.Contains(this)) All.Add(this);
            if (!Created.Contains(this)) Created.Add(this);
            ctrl = c;
            this.player = player;
            Transform v = c.visual;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
            damageVisual = gameObject.AddComponent<ShipDamageVisual>();
            if(geometry!=null)surface=ShipHitSurface.For(geometry);
            damageVisual.Configure(model, box, debrisWood,surface);
            Pick();
        }

        /// <summary>Все враги в порядке создания (и выключенные конфигом).</summary>
        public static readonly List<TargetShip> Created = new List<TargetShip>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Видит цель (флагман, свой корабль) ближе этого — идёт в бой; дальше — бродит по карте своим курсом.</summary>
        [System.NonSerialized] public float aggroRange = 1300f;
        /// <summary>Кто-то из наших недавно попал по нему — он помнит и преследует дальше обычного.</summary>
        float angryUntil = -1f;
        void Pick()
        {
            // В игре враг бродит по всей карте (дозор/охота), а не кружит у флагмана: точка в 500–1400 м от себя,
            // в пределах района боя. В кадрах CI — как раньше (рядом с игроком).
            bool roam = Application.isPlaying;
            Vector3 c = roam ? transform.position : (player != null ? player.position : Vector3.zero);
            float a = Random.Range(0f, Mathf.PI * 2f);
            float d = roam ? Random.Range(500f, 1400f) : Random.Range(minRange, maxRange);
            if (roam && new Vector2(c.x + Mathf.Cos(a) * d, c.z + Mathf.Sin(a) * d).magnitude > 3000f)
                a = Mathf.Atan2(-c.z, -c.x) + Random.Range(-0.8f, 0.8f);   // не уходить за край района — разворот к центру
            waypoint = new Vector3(c.x + Mathf.Cos(a) * d, transform.position.y, c.z + Mathf.Sin(a) * d);
            SkyFortress ff = SkyFortress.Instance;
            if (roam && ff != null)
            {
                // Политика: дозор — по кругу у границы своих вод; по делам — дальние точки, но не в воды гарнизона.
                Vector3 fc = ff.Center; fc.y = 0f;
                if (duty == Duty.Patrol)
                {
                    Vector3 off = transform.position - fc; off.y = 0f;
                    float ang = Mathf.Atan2(off.z, off.x) + Random.Range(0.25f, 0.6f) * (GetInstanceID() % 2 == 0 ? 1f : -1f);
                    waypoint = fc + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (Relations.CorsairRadius + Random.Range(150f, 500f));
                    if (new Vector2(waypoint.x, waypoint.z).magnitude > 3300f) waypoint = Relations.CorsairPoint(transform.position.y);
                }
                else
                {
                    for (int i = 0; i < 10; i++)
                    {
                        float aa = Random.Range(0f, Mathf.PI * 2f), dd = Random.Range(1200f, 3200f);
                        Vector3 w = transform.position + new Vector3(Mathf.Cos(aa), 0f, Mathf.Sin(aa)) * dd;
                        if (new Vector2(w.x, w.z).magnitude > 3300f) continue;
                        Waters wt = Relations.At(w);
                        if (wt == Waters.Garrison || wt == Waters.Safe) continue;
                        waypoint = w; break;
                    }
                }
                waypoint.y = transform.position.y;
            }
            // В безопасную зону крепости враг не заходит — точку выносим за её край.
            SkyFortress f = SkyFortress.Instance;
            if (f != null)
            {
                Vector3 off = waypoint - f.Center; off.y = 0f;
                float r = f.safeRadius + 120f;
                if (off.sqrMagnitude < r * r) { if (off.sqrMagnitude < 1f) off = Vector3.forward; waypoint = f.Center + off.normalized * r; waypoint.y = transform.position.y; }
            }
            nextPick = Time.time + (roam ? Random.Range(60f, 120f) : Random.Range(10f, 18f));
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Мишень",__pt);}
        void UpdateTimed()
        {
            Think();
            // Расхождение с соседями (Traffic): мирные и бегущие уступают дорогу; в бою — только чтобы не таранить чужих.
            // Дурень половину времени прёт напролом (не уступает дорогу) — отсюда и столкновения.
            if (ctrl != null && Alive && !BeingBoarded && Mood != Attitude.Surrender && Application.isPlaying && !(Fool && Mathf.Repeat(Time.time + GetInstanceID(), 20f) < 10f))
                Traffic.Avoid(ctrl, Mathf.Max(10f, Length * 0.5f), Mood == Attitude.Hostile ? ThreatTransform() : null);
            // Кольцо под кораблём — только у взятой цели: цвет по отношению (красный — враждебен, светлый — спокоен…).
            if (Application.isPlaying)
            {
                bool locked = (object)Broadside.PlayerLock == this && Alive && gameObject.activeInHierarchy;
                if (locked && ring == null && !ringTried) { ringTried = true; ring = BerthRing.Create(transform.position, Length * 0.42f, Length * 0.62f, transform.rotation, -3f, 5f); }
                if (ring != null)
                {
                    Color rc = Mood == Attitude.Hostile ? new Color(1f, 0.38f, 0.3f, 0.9f) : Mood == Attitude.Wary ? new Color(1f, 0.85f, 0.35f, 0.85f)
                             : Mood == Attitude.Flee ? new Color(1f, 0.6f, 0.3f, 0.85f) : Mood == Attitude.Surrender ? new Color(0.7f, 0.7f, 0.7f, 0.8f) : new Color(0.95f, 0.92f, 0.85f, 0.8f);
                    Vector3 at = Center; at.y = BoxCorner(0).y + 1f;
                    ring.TickTarget(at, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), locked, rc, Time.time);
                }
            }
        }
        Transform ThreatTransform() { AllyShip fa = FoeAlly; return fa != null ? fa.transform : player; }
        void Think()
        {
            if (ctrl == null) return;
            if (!captainRolled && Application.isPlaying) { RollCaptain(); captainRolled = true; }
            if (BeingBoarded && Alive) { Ai("АБОРДАЖ"); ctrl.throttle = 0f; ctrl.turn = 0f; return; }
            Lamps();
            if (!Alive)
            {
                if (Time.time >= deadUntil) Respawn();
                return;
            }
            CollideWithPlayer();
            ctrl.SetDamage(Health01);
            // Пожар добивает: ниже 30% прочности корабль теряет «попадание» каждые 7 с, пока не починится в порту.
            if (Health01 < 0.3f && mode != Mode.InPort && !Surrendered)   // спустил флаг — команда тушит пожар
            {
                burnClock += Time.deltaTime;
                if (burnClock >= 7f) { burnClock = 0f; Burn(); if (!Alive) return; }
            }
            else burnClock = 0f;
            if (Application.isPlaying) Relate(Time.deltaTime);
            if (Mood != Attitude.Calm && (InPort || mode == Mode.ToPort)) { mode = Mode.Fight; FreeGuestSlot(); }   // враждебный или бегущий — не гость порта
            if (Mood == Attitude.Calm && PortLife()) return;
            if (nextDaze <= 0f) nextDaze = Time.time + Random.Range(10f, 20f) * dazeScale;
            if (Time.time >= nextDaze && !Dazed)
            {
                dazeUntil = Time.time + Random.Range(2.5f, 6f);
                dazeTurn = Random.value < 0.5f ? 0f : Random.Range(-0.8f, 0.8f);
                nextDaze = dazeUntil + Random.Range(10f, 22f) * dazeScale;
            }
            if (Dazed) { Ai("ТУПИТ"); ctrl.turn = dazeTurn; ctrl.throttle = 0.08f; return; }
            Vector3 toPlayer = player != null ? player.position - transform.position : Vector3.zero;
            toPlayer.y = 0f;
            // Политика отношений: в бой — только по поводу (Relate/Decide), а не «всех, кого видим».
            if (Application.isPlaying && ActByMood()) return;
            fightSide = 0f;
            if (!Application.isPlaying && player != null && toPlayer.magnitude > leash && !SkyFortress.InSafeZone(player.position)) { waypoint = player.position; nextPick = Time.time + 8f; }
            Vector3 to = waypoint - transform.position;
            to.y = 0f;
            Ai(Application.isPlaying ? DutyNames[(int)duty] : "ДОЗОР");
            if (to.magnitude < 40f || Time.time > nextPick) Pick();
            float ang = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? 0.25f : 0.55f;
        }

        /// <summary>Ореолы фонарей и окон: издалека корабль читается по огням (вблизи их даёт само свечение).</summary>
        /// <summary>Первый слот ореолов в FxSystem (10 штук); меньше нуля — без ореолов (второй враг: слоты заняты).</summary>
        [System.NonSerialized] public int lampSlot = 30;
        void Lamps()
        {
            if (lampSlot < 0) return;
            FxSystem fx = FxSystem.Instance; Camera cam = Camera.main;
            if (fx == null || cam == null || model == null) return;
            bool lit = Alive || (damageVisual != null && damageVisual.Sinking && !damageVisual.Hidden);
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lit) { fx.ClearLamp(lampSlot + i); continue; }
                Vector3 p = model.TransformPoint(lamps[i]);
                float d = Vector3.Distance(eye, p);
                fx.SetLamp(lampSlot + i, p, Mathf.Clamp(d * 0.014f, 1.5f, 16f), new Color(1f, 0.72f, 0.38f, 0.08f * Mathf.Clamp01((d - 90f) / 220f)));
            }
        }

        /// <summary>Бой бортом: курс такой, чтобы игрок был на нужном курсовом угле с выбранного борта.</summary>
        void Engage(Vector3 toPlayer)
        {
            float dist = toPlayer.magnitude;
            if (dist < 1f) { ctrl.turn = 0f; ctrl.throttle = 0.5f; return; }
            Vector3 dir = toPlayer / dist;
            // Борт, которым идём к игроку, меняется, только когда игрок уверенно ушёл на другую сторону.
            float localX = Vector3.Dot(transform.right, dir);
            if (fightSide == 0f) fightSide = localX >= 0f ? 1f : -1f;
            else if (Mathf.Sign(localX) != fightSide && Mathf.Abs(localX) > 0.45f) fightSide = -fightSide;
            bool hurt = Health01 < 0.3f;
            float want = hurt ? retreatRange : fightRange;
            // Курсовой угол на игрока: 90° — на траверзе; дальше своей дальности — острее (сближение), ближе — тупее (отход).
            float bearing = 90f + Mathf.Clamp((want - dist) / 90f, -1f, 1f) * 40f;
            bool far = dist > want + 250f;
            if (far) bearing = 20f;
            Vector3 heading = Quaternion.Euler(0f, -fightSide * bearing, 0f) * dir;
            // В безопасную зону крепости враг не заходит: у её края курс не ведёт внутрь.
            SkyFortress f = SkyFortress.Instance;
            if (f != null)
            {
                Vector3 off = transform.position - f.Center; off.y = 0f;
                float edge = f.safeRadius + 160f;
                if (!Bold && off.sqrMagnitude < edge * edge && off.sqrMagnitude > 1f)   // дерзкий в зону идёт
                {
                    Vector3 outward = off.normalized; float inward = Vector3.Dot(heading, outward);
                    if (inward < 0.2f) heading = (heading - outward * inward + outward * 0.35f).normalized;
                }
            }
            float ang = Vector3.SignedAngle(transform.forward, heading, Vector3.up);
            // Уклонение: борт игрока заряжен и его ядра ложатся в наш корпус — на 2.2 с руль на борт и полный ход,
            // чтобы уйти из-под залпа; не чаще раза в 7–11 с (иначе в него было бы не попасть).
            if (playerFor != player) { playerFor = player; playerGuns = player != null ? player.GetComponent<Broadside>() : null; playerCtrl = player != null ? player.GetComponent<ShipController>() : null; }
            Broadside threat = playerGuns;
            if (threat != null && threat.AimOnTarget && threat.Readiness(threat.AimSide) >= 0.999f && Time.time >= nextEvade)
            {
                evadeUntil = Time.time + 2.2f;
                nextEvade = Time.time + Random.Range(7f, 11f);
                evadeTurn = Mathf.Abs(ang) > 8f ? Mathf.Sign(ang) : (Random.value < 0.5f ? -1f : 1f);
            }
            if (Time.time < evadeUntil) { ctrl.turn = evadeTurn; ctrl.throttle = 1f; return; }
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? 0.3f : far ? 0.85f : hurt ? 0.7f : 0.6f;
        }

        /// <summary>Корабли не проходят друг сквозь друга: борта сталкиваются — расталкивает, оба теряют ход,
        /// летит щепа. Урона нет. Корпуса — по пять точек вдоль оси, сближение меньше 13 м — упор.</summary>
        void CollideWithPlayer()
        {
            PlayerHull hull = PlayerHull.Instance;
            if (playerFor != player) { playerFor = player; playerGuns = player != null ? player.GetComponent<Broadside>() : null; playerCtrl = player != null ? player.GetComponent<ShipController>() : null; }
            ShipController other = playerCtrl;
            if (other == null || hull == null || !hull.Alive) return;
            Vector3 a0 = transform.position, b0 = player.position;
            // Корпус врага — по его длине (катер 20 м … адмиральский 135 м); флагман — 38 м. Корсар (38 м): шаг 8 м, зазор 13.
            float half = Length * 0.5f, step = half * 0.42f;
            if ((a0 - b0).sqrMagnitude > (41f + half) * (41f + half)) return;
            Vector3 af = transform.forward, bf = player.forward;
            float best = float.MaxValue; Vector3 pa = a0, pb = b0;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 x = a0 + af * (i * step), y = b0 + bf * (j * 8f);
                    float d = (x.x - y.x) * (x.x - y.x) + (x.z - y.z) * (x.z - y.z);
                    if (d < best) { best = d; pa = x; pb = y; }
                }
            float gap = 6.5f + half * 0.34f;
            if (best >= gap * gap) return;
            Vector3 n = pa - pb; n.y = 0f;
            if (n.sqrMagnitude < 1e-3f) n = transform.right;
            n.Normalize();
            float push = (gap - Mathf.Sqrt(best)) * 0.5f;
            bool hard = Time.time >= nextScrape;
            ctrl.Block(n * push, hard ? 0.75f : 1f);
            other.Block(-n * push, hard ? 0.75f : 1f);
            if (!hard) return;
            nextScrape = Time.time + 0.7f;
            // Таран: удар бортами на ходу бьёт оба корпуса (чем быстрее сближение, тем сильнее), на малом ходу — только скрежет.
            float closing = Mathf.Abs(ctrl.Speed) + Mathf.Abs(other.Speed);
            if (closing > 22f)
            {
                Vector3 ramAt = (pa + pb) * 0.5f; ramAt.y = a0.y + 2f;
                DebugReport.Count("таранов");
                hull.OnHit(ramAt, closing > 45f ? 2f : 1f);
                OnHit(ramAt, false, true);   // таран — повод (политика отношений)
                if (!Alive) return;
            }
            Vector3 at = (pa + pb) * 0.5f; at.y = a0.y + 2f;
            FxSystem fx = FxSystem.Instance;
            if (fx != null) fx.Scrape(at, n);
            SkySound.HullHit(at, false);
            OrbitCamera oc = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (oc != null) oc.HitImpulse(1f);
        }

        /// <summary>Пересекает ли путь ядра a→b корпус; point — точка попадания.</summary>
        public bool TryHit(Vector3 a, Vector3 b, out Vector3 point)
        {
            point = b;
            if (!Alive || model == null) return false;
            if(surface!=null)
            {
                hasContact=surface.Trace(model.InverseTransformPoint(a),model.InverseTransformPoint(b),out lastContact);
                if(hasContact)point=model.TransformPoint(lastContact.point);
                return hasContact;
            }
            Bounds bb = box;
            bb.Expand(1.2f);
            const int n = 6;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                if (bb.Contains(model.InverseTransformPoint(p))) { point = p; return true; }
            }
            return false;
        }

        float chainAcc;
        /// <summary>Потоплен флагманом (последнее попадание или пожар после его попадания) — только тогда «ЦЕЛЬ УНИЧТОЖЕНА» по центру.</summary>
        public bool SunkByPlayer { get; private set; }
        float lastPlayerHitAt = -100f, lastHitCaliber = 1f;
        public void OnHit(Vector3 point, bool chain = false, bool byPlayer = false, float caliber = 1f)
        {
            if (!Alive) return;
            lastHitAt = Time.time; lastHitCaliber = caliber;
            if (byPlayer) lastPlayerHitAt = Time.time;
            if (Application.isPlaying)
            {
                if (Mood == Attitude.Surrender) { if (byPlayer) Relations.AddGrudge(8f, "огонь по сдавшемуся"); }   // сдавшийся остаётся сдавшимся
                else if (Mood != Attitude.Hostile)
                {
                    if (!byPlayer) { AllyShip near = null; float nd = 1200f * 1200f; foreach (AllyShip al in AllyShip.All) { if (al == null || !al.Alive) continue; float d2 = (al.transform.position - transform.position).sqrMagnitude; if (d2 < nd) { nd = d2; near = al; } } if (near != null) foeAlly = near; }
                    Decide(byPlayer ? "попадание флагмана" : "попадание гарнизона", Relations.At(transform.position) == Waters.Corsair, true);
                }
            }
            // Книппель режет баллоны и такелаж: ход падает надолго, а корпус получает треть удара.
            if (chain)
            {
                if (!ctrl.BalloonHit || Random.value < 0.5f) { ctrl.HitBalloon(16f); if (byPlayer) HudNotice.Post("КНИППЕЛЬ: " + HudNotice.Who(this) + " ТЕРЯЕТ ХОД", HudNotice.Where(transform.position), 0, 2.5f, HudNotice.Prio.Combat, "dmg" + GetInstanceID(), transform, 0f, new Color(1f, 0.85f, 0.45f, 1f)); }
                chainAcc += 0.34f;
                if (chainAcc < 1f) { FxSystem cfx = FxSystem.Instance; if (cfx != null) cfx.Explosion(point, 0.5f); return; }
                chainAcc -= 1f;
            }
            hits += Mathf.Max(1, Mathf.RoundToInt(caliber));   // тяжёлое ядро (класс, крепость) — больше урона
            // Сдавшегося свои и крепость не добивают: ядра, выпущенные до сдачи, долетают — но не топят (17.11: «СГОРЕЛ — СВОИМИ»).
            if (Surrendered && !byPlayer && hits >= hitsToSink) hits = hitsToSink - 1;
            angryUntil = Time.time + 40f;
            if (byPlayer) { if (Time.time - TallyAt > 2.5f) Tally = 0; Tally++; TallyAt = Time.time; }   // «ПОПАДАНИЙ · N» — только твои
            // Попадание по месту: корма — руль, верх с баллонами — ход (на 12 с).
            if (Random.value < 0.4f && model != null)
            {
                Vector3 lp = model.InverseTransformPoint(point);
                Color gold = new Color(1f, 0.85f, 0.45f, 1f);
                if (lp.z < box.min.z + box.size.z * 0.18f && !ctrl.RudderHit) { ctrl.HitRudder(12f); if (byPlayer) HudNotice.Post(HudNotice.Who(this) + ": СБИТ РУЛЬ", HudNotice.Where(transform.position), 0, 2.5f, HudNotice.Prio.Combat, "dmg" + GetInstanceID(), transform, 0f, gold); }
                else if (lp.y > box.min.y + box.size.y * 0.6f && !ctrl.BalloonHit) { ctrl.HitBalloon(12f); if (byPlayer) HudNotice.Post(HudNotice.Who(this) + ": ПРОБИТ БАЛЛОН", HudNotice.Where(transform.position), 0, 2.5f, HudNotice.Prio.Combat, "dmg" + GetInstanceID(), transform, 0f, gold); }
            }
            // Подстрелили в порту — мирной стоянке конец: враг возвращается в бой.
            if (InPort) { mode = Mode.Fight; nextPortThink = Time.time + Random.Range(30f, 50f); }
            FxSystem fx = FxSystem.Instance;
            Vector3 velocity = transform.forward * ctrl.Speed;
            Vector3 normal=(point-model.TransformPoint(box.center)).normalized;
            if(hasContact&&(point-model.TransformPoint(lastContact.point)).sqrMagnitude<.01f)normal=ImpactNormal;
            else if(surface!=null)
            {
                Vector3 local=model.InverseTransformPoint(point),outward=(local-box.center).normalized;
                if(outward.sqrMagnitude<.1f)outward=Vector3.right;
                if(surface.Trace(local+outward*5f,local-outward*15f,out lastContact)){point=model.TransformPoint(lastContact.point);normal=ImpactNormal;}
            }
            hasContact=false;
            if (damageVisual != null) damageVisual.Hit(point, hits / (float)Mathf.Max(1, hitsToSink), velocity,normal);
            SkySound.HullHit(point, hits % 3 == 0);
            if (hits >= hitsToSink)
            {
                SunkByPlayer = byPlayer;
                if (Application.isPlaying) Sunk(byPlayer, caliber);
                DebugReport.Event(name + " потоплен");
                DebugReport.Count("врагов потоплено");
                SkySound.ShipDestroyed(point);
                if (fx != null) fx.Explosion(model.TransformPoint(box.center), 3f);
                deadUntil = Time.time + respawnDelay;
                if (damageVisual != null) damageVisual.BeginSink(point, velocity);
                ctrl.throttle = 0f;
                ctrl.turn = 0f;
            }
            else if (fx != null) fx.HullImpact(point,normal,velocity);
        }

        /// <summary>Пожар: потеря прочности без попадания ядра (без счёта залпа и критов).</summary>
        void Burn()
        {
            hits++;
            if (hits < hitsToSink) return;
            Vector3 at = Center, velocity = transform.forward * ctrl.Speed;
            SkySound.ShipDestroyed(at);
            FxSystem fx = FxSystem.Instance;
            if (fx != null) fx.Explosion(at, 3f);
            deadUntil = Time.time + respawnDelay;
            if (damageVisual != null) damageVisual.BeginSink(at, velocity);
            ctrl.throttle = 0f; ctrl.turn = 0f;
            SunkByPlayer = Time.time - lastPlayerHitAt < 30f;   // пожар от твоего попадания — твоя победа
            if (Application.isPlaying) Sunk(SunkByPlayer, lastHitCaliber, true);
            DebugReport.Count("врагов сгорело");
        }

        /// <summary>Потоплен: злоба корсаров — только если это сделал флагман (свои и крепость — их дело); сообщение о чужой
        /// победе — малой плашкой и только рядом (ближе 1,5 км), своя — центральный баннер (ShipHud).</summary>
        void Sunk(bool byPlayer, float caliber, bool burned = false)
        {
            if (byPlayer) Relations.AddGrudge(Mood == Attitude.Surrender ? 30f : 12f, Mood == Attitude.Surrender ? "добит сдавшийся " + name : "потоплен " + name);
            else
            {
                float d = HudNotice.DistToPlayer(transform.position);
                if (d < 1500f) HudNotice.Post(HudNotice.Who(this) + (burned ? " СГОРЕЛ" : " ПОТОПЛЕН"), (caliber >= 2.9f ? "КРЕПОСТЬЮ" : "СВОИМИ") + " · " + HudNotice.Where(transform.position),
                    0, 3f, HudNotice.Prio.Info, "sunk" + GetInstanceID(), null);
            }
        }
        void Respawn()
        {
            SunkByPlayer = false;
            deadUntil = -1f;
            hits = 0;
            RollCaptain();
            if (damageVisual != null) damageVisual.ResetDamage();
            Vector3 c = player != null ? player.position : Vector3.zero;
            float a = Random.Range(0f, Mathf.PI * 2f);
            // Новый враг приходит издалека (spawnRange), а не возникает в кадре в 320 м; в игре — в водах корсаров.
            Vector3 at = new Vector3(c.x + Mathf.Cos(a) * spawnRange, transform.position.y, c.z + Mathf.Sin(a) * spawnRange);
            if (Application.isPlaying && SkyFortress.Instance != null)
            {
                for (int i = 0; i < 8; i++) { Vector3 q = Relations.CorsairPoint(transform.position.y); if (player == null || (q - player.position).sqrMagnitude > 900f * 900f) { at = q; break; } }
            }
            // Новый враг не появляется в безопасной зоне крепости (и тем более внутри неё) — выносим за край.
            SkyFortress f = SkyFortress.Instance;
            if (f != null)
            {
                Vector3 off = at - f.Center; off.y = 0f;
                float r = f.safeRadius + 150f;
                if (off.sqrMagnitude < r * r) { if (off.sqrMagnitude < 1f) off = Vector3.forward; at = f.Center + off.normalized * r; at.y = transform.position.y; }
            }
            transform.position = at;
            Vector3 back = c - at; back.y = 0f;
            transform.rotation = back.sqrMagnitude > 1f ? Quaternion.LookRotation(back.normalized, Vector3.up) : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            fightSide = 0f;
            ctrl.visual.gameObject.SetActive(true);
            Pick();
        }

#if UNITY_EDITOR
        public void ResetDamageCapture()
        {
            hits = 0; deadUntil = -1f;
            if (damageVisual != null) damageVisual.ResetDamage();
        }
#endif
    }
}
