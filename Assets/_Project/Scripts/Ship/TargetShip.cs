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
    public class TargetShip : MonoBehaviour
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
        Mode mode = Mode.Fight;
        float nextPortThink, portUntil, dumbAt = -1f, nextPortRepair, burnClock;
        /// <summary>До какого времени крепость считает этот корабль нарушителем и стреляет по нему.</summary>
        public float OutlawUntil { get; private set; } = -1f;
        public bool Outlaw { get { return Time.time < OutlawUntil; } }
        public bool InPort { get { return mode == Mode.ToPort || mode == Mode.InPort; } }
        EnemyGunner gunner; bool captainRolled;
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
        public bool Heavy { get; private set; }
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
            Heavy = canBeHeavy && captainRolled && Random.value < 0.22f;
            hitsToSink = Heavy ? baseHits * 2 : baseHits;
            ctrl.maxSpeed = Heavy ? baseSpeed * 0.75f : baseSpeed; ctrl.maxYawRate = Heavy ? baseYaw * 0.75f : baseYaw;
            if (gunner != null) { gunner.gunsPerSide = Heavy ? 6 : baseGuns; gunner.maxRange = Heavy ? baseRange * 1.15f : baseRange; gunner.cooldown = Heavy ? baseCooldown * 1.35f : baseCooldown; }
            // Тяжёлый — тёмный, почти чёрный корпус: виден издалека.
            MaterialPropertyBlock tint = new MaterialPropertyBlock();
            tint.SetColor("_Color", Heavy ? new Color(0.55f, 0.42f, 0.4f) : Color.white);
            foreach (MeshRenderer mr in ctrl.visual.GetComponentsInChildren<MeshRenderer>(true)) mr.SetPropertyBlock(tint);
            if (Heavy && player != null && (player.position - transform.position).sqrMagnitude < 2500f * 2500f) HudNotice.Show("ТЯЖЁЛЫЙ КОРАБЛЬ ВРАГА", 1, 4f);
        }

        void RollCaptain()
        {
            Captured = false; BeingBoarded = false;
            ApplyClass();
            float r = Random.value;
            skill = r < 0.35f ? Skill.Novice : r < 0.8f ? Skill.Seasoned : Skill.Veteran;
            dazeScale = skill == Skill.Novice ? 0.6f : skill == Skill.Veteran ? 2.2f : 1f;
            fightRange = skill == Skill.Novice ? 300f : skill == Skill.Veteran ? 220f : 260f;   // в пределах своих 420 м
            if (gunner == null) gunner = GetComponent<EnemyGunner>();
            if (gunner != null)
            {
                gunner.skillError = skill == Skill.Novice ? 1.5f : skill == Skill.Veteran ? 0.7f : 1f;
                gunner.skillCooldown = skill == Skill.Novice ? 1.25f : skill == Skill.Veteran ? 0.85f : 1f;
            }
            mode = Mode.Fight; OutlawUntil = -1f; dumbAt = -1f;
            nextPortThink = Time.time + Random.Range(25f, 45f);
            if (ctrl != null) ctrl.ClearCrits();
        }

        /// <summary>Курс на точку в обход камня крепости: внутри круга причала курс не ведёт к центру, пока цель далеко.</summary>
        void SteerTo(Vector3 goal, float throttle)
        {
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
            float ang = Vector3.SignedAngle(transform.forward, heading, Vector3.up);
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? Mathf.Min(throttle, 0.3f) : throttle;
        }

        /// <summary>true — корабль занят портом или бегством, бой в этот кадр не ведётся.</summary>
        bool PortLife()
        {
            SkyFortress f = SkyFortress.Instance;
            if (f == null) { mode = Mode.Fight; return false; }
            if (Outlaw && mode != Mode.Flee) mode = Mode.Flee;
            if (mode == Mode.Fight)
            {
                if (Time.time < nextPortThink) return false;
                nextPortThink = Time.time + Random.Range(20f, 35f);
                bool go = Health01 < 0.45f ? Random.value < 0.6f : Random.value < 0.1f;
                if (!go) return false;
                mode = Mode.ToPort;
                DebugReport.Count("врагов ушло в порт");
                // Глупость случается не с каждым: новичок срывается чаще, ветеран — почти никогда.
                float dumb = skill == Skill.Novice ? 0.55f : skill == Skill.Veteran ? 0.08f : 0.3f;
                dumbAt = Random.value < dumb ? Time.time + Random.Range(25f, 70f) : -1f;
                if (player != null && (player.position - transform.position).sqrMagnitude < 900f * 900f) HudNotice.Show("ВРАГ УХОДИТ В ПОРТ", 9, 3.5f);
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
            // Место гостя — в стороне от места игрока и пришвартованных кораблей.
            Vector3 spot = f.BerthWorld + outDir * 70f + Vector3.Cross(Vector3.up, outDir) * 170f; spot.y = transform.position.y;
            float d = Vector3.Distance(new Vector3(transform.position.x, spot.y, transform.position.z), spot);
            bool inZone = SkyFortress.InSafeZone(transform.position);
            // Глупость: в зоне крепости, игрок рядом и жив — залп. Крепость этого не прощает.
            PlayerHull ph = PlayerHull.Instance;
            if (dumbAt > 0f && Time.time >= dumbAt && inZone && ph != null && ph.Alive && player != null && SkyFortress.InSafeZone(player.position))
            {
                dumbAt = -1f;
                if (gunner == null) gunner = GetComponent<EnemyGunner>();
                if (gunner != null && gunner.ForceVolley()) { DebugReport.Count("глупых залпов врага в зоне"); OutlawUntil = Time.time + 40f; SkyFortress.NotifyEnemyFired(this); mode = Mode.Flee; return true; }
            }
            if (mode == Mode.ToPort)
            {
                Ai("В ПОРТ", Mathf.RoundToInt(Health01 * 100f) + "%");
                if (d < 45f) { mode = Mode.InPort; portUntil = Time.time + Random.Range(18f, 35f); }
                else SteerTo(spot, d > 320f ? 0.75f : d > 120f ? 0.4f : 0.2f);
                return true;
            }
            Ai("ПОРТ");
            // У причала: стоит, чинится (попадание в 1.5 с), потом уходит обратно в бой.
            ctrl.throttle = 0f; ctrl.turn = 0f;
            if (Time.time >= nextPortRepair && hits > 0)
            {
                nextPortRepair = Time.time + 1.5f; hits--;
                if (hits == 0 && damageVisual != null) damageVisual.ResetDamage();
            }
            if (Time.time >= portUntil && hits <= hitsToSink / 4) { mode = Mode.Fight; nextPortThink = Time.time + Random.Range(40f, 70f); Pick(); return false; }
            return true;
        }
        public float respawnDelay = 6f;

        ShipController ctrl;
        Transform playerFor; Broadside playerGuns; ShipController playerCtrl;   // без покадрового GetComponent
        Transform model;
        Bounds box;
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
            ctrl = c;
            this.player = player;
            Transform v = c.visual;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
            damageVisual = gameObject.AddComponent<ShipDamageVisual>();
            if(geometry!=null)surface=new ShipHitSurface(geometry);
            damageVisual.Configure(model, box, debrisWood,surface);
            Pick();
        }

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
            // В безопасную зону крепости враг не заходит — точку выносим за её край.
            SkyFortress f = SkyFortress.Instance;
            if (f != null)
            {
                Vector3 off = waypoint - f.Center; off.y = 0f;
                float r = f.safeRadius + 120f;
                if (off.sqrMagnitude < r * r) { if (off.sqrMagnitude < 1f) off = Vector3.forward; waypoint = f.Center + off.normalized * r; waypoint.y = transform.position.y; }
            }
            nextPick = Time.time + Random.Range(10f, 18f);
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Мишень",__pt);}
        void UpdateTimed()
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
            if (Health01 < 0.3f && mode != Mode.InPort)
            {
                burnClock += Time.deltaTime;
                if (burnClock >= 7f) { burnClock = 0f; Burn(); if (!Alive) return; }
            }
            else burnClock = 0f;
            if (PortLife()) return;
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
            PlayerHull playerHull = PlayerHull.Instance;
            float aggro = Time.time < angryUntil ? aggroRange * 1.6f : aggroRange;
            bool playerFair = player != null && playerHull != null && playerHull.Alive && !SkyFortress.InSafeZone(player.position) && toPlayer.magnitude < aggro;
            // Свои корабли тоже цели: торговца враг выбирает охотнее (лёгкая добыча), сторожевого и ведомого — если они
            // заметно ближе флагмана. Всё — вне безопасной зоны крепости.
            AllyShip prey = null; float preyD = playerFair ? toPlayer.magnitude * 0.7f : aggro;
            foreach (AllyShip al in AllyShip.All)
            {
                if (al == null || !al.Alive || SkyFortress.InSafeZone(al.transform.position)) continue;
                Vector3 ta = al.transform.position - transform.position; ta.y = 0f;
                float d = ta.magnitude * (al.role == AllyShip.Role.Merchant ? 0.6f : 1f);
                if (d < preyD && ta.magnitude < aggro) { preyD = d; prey = al; }
            }
            if (prey != null) { Vector3 tp = prey.transform.position - transform.position; tp.y = 0f; Ai("БОЙ-" + AllyShip.RoleNames[(int)prey.role].Substring(0, 4), Mathf.RoundToInt(tp.magnitude) + " м"); Engage(tp); return; }
            if (playerFair) { Ai(Health01 < 0.3f ? "ОТХОД" : "БОЙ", "флагман " + Mathf.RoundToInt(toPlayer.magnitude) + " м"); Engage(toPlayer); return; }
            fightSide = 0f;
            if (!Application.isPlaying && player != null && toPlayer.magnitude > leash && !SkyFortress.InSafeZone(player.position)) { waypoint = player.position; nextPick = Time.time + 8f; }
            Vector3 to = waypoint - transform.position;
            to.y = 0f;
            Ai("ДОЗОР");
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
                if (off.sqrMagnitude < edge * edge && off.sqrMagnitude > 1f)
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
            if ((a0 - b0).sqrMagnitude > 60f * 60f) return;
            Vector3 af = transform.forward, bf = player.forward;
            float best = float.MaxValue; Vector3 pa = a0, pb = b0;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 x = a0 + af * (i * 8f), y = b0 + bf * (j * 8f);
                    float d = (x.x - y.x) * (x.x - y.x) + (x.z - y.z) * (x.z - y.z);
                    if (d < best) { best = d; pa = x; pb = y; }
                }
            const float gap = 13f;
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
                OnHit(ramAt);
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
        public void OnHit(Vector3 point, bool chain = false)
        {
            if (!Alive) return;
            // Книппель режет баллоны и такелаж: ход падает надолго, а корпус получает треть удара.
            if (chain)
            {
                if (!ctrl.BalloonHit || Random.value < 0.5f) { ctrl.HitBalloon(16f); HudNotice.Show("КНИППЕЛЬ · ВРАГ ТЕРЯЕТ ХОД", 0); }
                chainAcc += 0.34f;
                if (chainAcc < 1f) { FxSystem cfx = FxSystem.Instance; if (cfx != null) cfx.Explosion(point, 0.5f); return; }
                chainAcc -= 1f;
            }
            hits++;
            angryUntil = Time.time + 40f;
            if (Time.time - TallyAt > 2.5f) Tally = 0;
            Tally++; TallyAt = Time.time;
            // Попадание по месту: корма — руль, верх с баллонами — ход (на 12 с).
            if (Random.value < 0.4f && model != null)
            {
                Vector3 lp = model.InverseTransformPoint(point);
                if (lp.z < box.min.z + box.size.z * 0.18f && !ctrl.RudderHit) { ctrl.HitRudder(12f); HudNotice.Show("ВРАГУ СБИТ РУЛЬ", 0); }
                else if (lp.y > box.min.y + box.size.y * 0.6f && !ctrl.BalloonHit) { ctrl.HitBalloon(12f); HudNotice.Show("ВРАГУ ПРОБИТ БАЛЛОН", 0); }
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
            HudNotice.Show("ВРАГ СГОРЕЛ", 0);
            DebugReport.Count("врагов сгорело");
        }

        void Respawn()
        {
            deadUntil = -1f;
            hits = 0;
            RollCaptain();
            if (damageVisual != null) damageVisual.ResetDamage();
            Vector3 c = player != null ? player.position : Vector3.zero;
            float a = Random.Range(0f, Mathf.PI * 2f);
            // Новый враг приходит издалека (spawnRange), а не возникает в кадре в 320 м.
            Vector3 at = new Vector3(c.x + Mathf.Cos(a) * spawnRange, transform.position.y, c.z + Mathf.Sin(a) * spawnRange);
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
