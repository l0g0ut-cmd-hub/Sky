using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Мишень отвечает огнём: когда игрок у её борта (курсовой угол 55–125°) в 90–650 м, борт даёт залп из четырёх
    /// пушек. Наводка — баллистика с упреждением по относительной скорости и небольшим разбросом,
    /// поэтому по манёвренному кораблю попадает не всегда. Дула — на обшивке модели (луч по точной поверхности
    /// корпуса; у модели Meshy нет узлов пушек), без поверхности — по габариту.
    /// </summary>
    public class EnemyGunner : MonoBehaviour
    {
        public float muzzleSpeed = 85f, gravity = 9.81f;
        public float cooldown = 6f, minRange = 90f, maxRange = 650f;
        /// <summary>Время последнего вражеского залпа — HUD показывает оповещение.</summary>
        public static float LastVolleyTime = -100f;
        public float spreadDeg = 2.4f;
        // Пристрелка, как у морской артиллерии: первый залп по новой цели ложится с большой ошибкой по дальности
        // и направлению, каждый следующий — точнее (×0.6), пока игрок держит курс и скорость. Резкий манёвр
        // (смена скорости или курса) сбивает пристрелку — враг снова мажет.
        public float rangeError = 0.08f, sideError = 0.03f;
        float errScale = 1f; Vector3 aimErr, lastVolleyVel; bool engaged;
        public int gunsPerSide = 4;
        /// <summary>Калибр (класс корабля): во сколько раз ядро тяжелее обычного — урон по флагману и своим.</summary>
        public float caliber = 1f;
        /// <summary>Точность класса: множитель ошибки наводки (мелочь мажет чаще, линейные канониры точнее).</summary>
        [System.NonSerialized] public float classError = 1f;

        TargetShip target;
        ShipController ctrl;
        Transform model;
        Bounds box;
        float ready;
        /// <summary>Выучка расчёта (задаёт TargetShip по капитану): множитель ошибки и перезарядки.</summary>
        [System.NonSerialized] public float skillError = 1f, skillCooldown = 1f;
        bool volleyAtOnce;
        int pendingSide; float pendingAt; int pendingLeft;
        AllyShip victimAlly;   // цель залпа: null — корабль игрока, иначе свой корабль (сторожевой, ведомый, торговец)
        Vector3 VictimCenter(Vector3 pp) { return victimAlly != null && victimAlly.Alive ? victimAlly.Center : pp; }
        Vector3 VictimVel { get { return victimAlly != null && victimAlly.Alive ? victimAlly.Velocity : playerVel; } }
        Vector3 lastPlayerPos, playerVel, lastPos, selfVel;

        public void Setup(TargetShip t, ShipController c)
        {
            target = t;
            ctrl = c;
            Transform v = c.visual;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
            ready = Time.time + 6f;   // дать игроку осмотреться
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Огонь врага",__pt);}
        void UpdateTimed()
        {
            float dt = Time.deltaTime;
            PlayerHull player = PlayerHull.Instance;
            if (player == null || target == null || model == null || dt <= 0f) return;
            Vector3 pp = player.Center;
            playerVel = Vector3.Lerp(playerVel, (pp - lastPlayerPos) / dt, 0.2f); lastPlayerPos = pp;
            selfVel = Vector3.Lerp(selfVel, (transform.position - lastPos) / dt, 0.2f); lastPos = transform.position;
            if (!target.Alive) { pendingLeft = 0; engaged = false; return; }

            if (pendingLeft > 0 && Time.time >= pendingAt)
            {
                if (victimAlly == null && !player.Alive) { pendingLeft = 0; return; }
                Shoot(pendingSide, gunsPerSide - pendingLeft, VictimCenter(pp) + aimErr);
                pendingLeft--;
                pendingAt = Time.time + (volleyAtOnce ? Random.Range(0.01f, 0.04f) : Random.Range(0.12f, 0.2f));
                return;
            }
            if (Time.time < ready) return;
            if (target.Dazed) return;   // капитан растерялся — и пушки молчат
            bool bold = target.Bold;
            if (!bold && SkyFortress.InSafeZone(transform.position)) return;   // из зоны крепости не стреляет (кроме глупости и дерзости)
            // Цель залпа — ближайший на траверзе в пределах пушек: корабль игрока или свой (сторожевой, ведомый, торговец).
            // Под пушками крепости (в безопасной зоне) враг ни по кому не стреляет.
            // Политика отношений: стреляет только в бою с гарнизоном (повод был) — по флагману и своим вне зоны крепости.
            if (Application.isPlaying && !target.AtWar) { engaged = false; return; }
            victimAlly = null; float dist = float.MaxValue; Vector3 rel = Vector3.zero, local = Vector3.zero;
            if (player.Alive && (!Application.isPlaying || target.HostileToPlayer || bold) && (bold || !SkyFortress.InSafeZone(pp))) TryVictim(pp, null, ref dist, ref rel, ref local);
            foreach (AllyShip al in AllyShip.All)
                if (al != null && al.Alive && (bold || !SkyFortress.InSafeZone(al.transform.position))) TryVictim(al.Center, al, ref dist, ref rel, ref local);
            if (dist == float.MaxValue) return;
            // Дерзкий залп в зоне (своей или цели) — нарушитель: крепость откроет огонь.
            if (bold && (SkyFortress.InSafeZone(transform.position) || SkyFortress.InSafeZone(pp))) target.MarkOutlaw();
            // Затупы расчёта: иногда залп запаздывает (замешкались с фитилями), иногда — «на авось» с двойной ошибкой.
            if (Random.value < 0.2f) { ready = Time.time + Random.Range(1f, 3f); return; }
            pendingSide = local.x > 0f ? 1 : -1;
            Vector3 dv = VictimVel - lastVolleyVel;
            if (!engaged || dv.magnitude > 5f) errScale = 1f; else errScale = Mathf.Max(0.2f, errScale * 0.6f);
            engaged = true; lastVolleyVel = VictimVel;
            Vector3 fwd = rel / Mathf.Max(dist, 1f), sideDir = Vector3.Cross(Vector3.up, fwd);
            float wild = Random.value < 0.2f ? 2.2f : 1f;
            // Подбитый стреляет хуже и реже: ниже половины прочности ошибка ×1.5, ниже 30% — ×2.2.
            float hurt = target.Health01 < 0.3f ? 2.2f : target.Health01 < 0.5f ? 1.5f : 1f;
            aimErr = (fwd * Gauss() * rangeError * dist + sideDir * Gauss() * sideError * dist + Vector3.up * Gauss() * 0.012f * dist) * errScale * wild * hurt * skillError * classError;
            volleyAtOnce = Random.value < 0.35f;   // иногда весь борт разом, чаще — волной
            pendingLeft = gunsPerSide;
            pendingAt = Time.time;
            // Залп по флагману — кто и откуда (метка на стрелявшем); по своим — молча (свой сам скажет «ПОД ОГНЁМ»).
            if (victimAlly == null)
            {
                LastVolleyTime = Time.time;
                HudNotice.Post(HudNotice.Who(target) + " ДАЁТ ЗАЛП", HudNotice.Where(transform.position), 1, 2.4f, HudNotice.Prio.Critical, "volley", transform);
            }
            DebugReport.Count(victimAlly != null ? "залпов врага по своим" : "залпов врага по флагману");
            DebugReport.Volley(name.Replace(" Ship", "") + " → " + (victimAlly != null ? AllyShip.RoleNames[(int)victimAlly.role].ToLower() : "флагман") + " " + Mathf.RoundToInt(dist) + " м, борт " + (pendingSide > 0 ? "прав" : "лев") + ", ошибка ×" + (errScale * skillError).ToString("F2"));
            ready = Time.time + (cooldown + Random.Range(0f, 1.5f)) * skillCooldown * (hurt > 1.4f ? 1.3f : 1f);
        }

        /// <summary>Залп «по глупости» — без проверок дальности и зоны, с большой ошибкой. false — стрелять нечем/не в кого.</summary>
        public bool ForceVolley()
        {
            PlayerHull player = PlayerHull.Instance;
            if (player == null || !player.Alive || target == null || !target.Alive || pendingLeft > 0) return false;
            Vector3 pp = player.Center, rel = pp - transform.position; rel.y = 0f;
            float dist = rel.magnitude;
            if (dist < 30f || dist > maxRange) return false;
            victimAlly = null;
            pendingSide = transform.InverseTransformDirection(rel).x > 0f ? 1 : -1;
            aimErr = new Vector3(Gauss(), 0f, Gauss()) * (0.07f * dist);
            volleyAtOnce = false; pendingLeft = gunsPerSide; pendingAt = Time.time; LastVolleyTime = Time.time;
            ready = Time.time + cooldown + 2f;
            return true;
        }

        void Shoot(int side, int index, Vector3 aimAt)
        {
            FxSystem fx = FxSystem.Instance;
            if (fx == null) return;
            Vector3 localMuzzle = MuzzleLocal(side, index);
            Vector3 muzzle = model.TransformPoint(localMuzzle);
            Vector3 dir;
            if (!Solve(muzzle, aimAt, out dir)) return;
            dir = Quaternion.Euler(Random.Range(-spreadDeg, spreadDeg), Random.Range(-spreadDeg, spreadDeg), 0f) * dir;
            fx.Fire(muzzle, dir, selfVel, muzzleSpeed, true, 7f, caliber);
        }

        Vector3[] muzzles; bool[] muzzleFound;
        readonly float[] widths = new float[8]; readonly Vector3[] hits = new Vector3[8];
        /// <summary>Дуло в координатах модели. Луч снаружи к оси корпуса на восьми высотах нижней части модели; из
        /// попаданий берётся среднее по ширине (самое широкое может быть рулём или парусом, самое узкое — килем).</summary>
        Vector3 MuzzleLocal(int side, int index)
        {
            int n = Mathf.Max(1, gunsPerSide);
            if (muzzles == null || muzzles.Length != n * 2) { muzzles = new Vector3[n * 2]; muzzleFound = new bool[n * 2]; }
            index = Mathf.Clamp(index, 0, n - 1);
            int slot = (side > 0 ? n : 0) + index;
            if (muzzleFound[slot]) return muzzles[slot];
            float z = Mathf.Lerp(box.min.z + box.size.z * 0.3f, box.max.z - box.size.z * 0.3f, n > 1 ? index / (float)(n - 1) : 0.5f);
            Vector3 best = new Vector3(box.center.x + side * (box.extents.x * 0.92f), box.min.y + box.size.y * 0.38f, z);
            ShipHitSurface surface = target != null ? target.Surface : null;
            if (surface != null)
            {
                int found = 0;
                for (int k = 0; k < 8; k++)
                {
                    float y = box.min.y + box.size.y * (0.12f + 0.035f * k);
                    ShipHitSurface.Contact c;
                    if (!surface.Trace(new Vector3(box.center.x + side * (box.extents.x + 4f), y, z), new Vector3(box.center.x, y, z), out c)) continue;
                    float w = Mathf.Abs(c.point.x - box.center.x);
                    int at = found;
                    while (at > 0 && widths[at - 1] > w) { widths[at] = widths[at - 1]; hits[at] = hits[at - 1]; at--; }
                    widths[at] = w; hits[at] = c.point; found++;
                }
                if (found > 0) best = hits[found / 2] + new Vector3(side * 0.35f, 0f, 0f);
            }
            muzzles[slot] = best; muzzleFound[slot] = true;
            return best;
        }

        void TryVictim(Vector3 c, AllyShip al, ref float best, ref Vector3 bestRel, ref Vector3 bestLocal)
        {
            Vector3 r = c - transform.position; r.y = 0f;
            float d = r.magnitude;
            if (d < minRange || d > maxRange || d >= best) return;
            Vector3 l = transform.InverseTransformDirection(r);
            float bearing = Mathf.Atan2(Mathf.Abs(l.x), l.z) * Mathf.Rad2Deg;
            if (bearing < 55f || bearing > 125f) return;
            best = d; bestRel = r; bestLocal = l; victimAlly = al;
        }

        static float Gauss()
        {
            float u1 = Mathf.Max(1e-4f, Random.value), u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        /// <summary>Низкая баллистическая траектория с упреждением (ядро несёт и скорость стреляющего корабля).</summary>
        bool Solve(Vector3 from, Vector3 to, out Vector3 dir)
        {
            dir = Vector3.forward;
            Vector3 aim = to;
            for (int it = 0; it < 3; it++)
            {
                Vector3 d = aim - from;
                float x = new Vector2(d.x, d.z).magnitude, y = d.y, v = muzzleSpeed, g = gravity;
                float disc = v * v * v * v - g * (g * x * x + 2f * y * v * v);
                if (disc < 0f || x < 1f) return false;
                float theta = Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * x));
                float tFlight = x / (v * Mathf.Cos(theta));
                Vector3 h = new Vector3(d.x, 0f, d.z).normalized;
                dir = h * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta);
                aim = to + (VictimVel - selfVel) * (tFlight * 0.8f);   // упреждение неполное — наводчик не идеален
            }
            return true;
        }
    }
}
