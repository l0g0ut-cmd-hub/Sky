using System;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Небесная крепость-причал: модель (STM1/STM2 из tools/convert_glb*.py), низ уходит в море облаков и растворяется
    /// в нём (объёмные облака рисуются поверх по глубине сцены). Вокруг — безопасная зона: выстрел игрока внутри неё
    /// делает крепость враждебной на hostileTime секунд, и пушки башен бьют по нему той же баллистикой, что у врага.
    /// У причала на малом ходу корабль чинится. Сквозь крепость не пройти — карта высот (SkyFortress_data.json).
    /// Данные модели (дула, причал, карта высот) — в координатах модели; tools/fortress_data.py.
    /// </summary>
    public class SkyFortress : MonoBehaviour
    {
        public static SkyFortress Instance { get; private set; }

        // Размеры — под крепость втрое крупнее исходной модели (1490×1000×1380 м): причал в ~880 м от центра.
        public float safeRadius = 1400f;
        public float hostileTime = 45f;
        // Пушки достают всю безопасную зону с башен: быстрые ядра (220 м/с) и долгий полёт (до 12 с).
        public float gunRange = 2400f, gunCooldown = 3.2f, spreadDeg = 1.2f, muzzleSpeed = 220f, gravity = 9.81f, shellLife = 12f;
        [Tooltip("Калибр орудий башен относительно корабельной пушки: размер ядра, вспышки, дыма и урон.")]
        public float shellCaliber = 3f;
        public float berthRadius = 100f, berthMaxSpeed = 5f, repairPerSecond = 6f;

        [Serializable]
        class Data
        {
            public float[] guns;        // x,y,z на дуло (модель)
            public float[] berth;       // x,y,z точки причала (модель)
            public float[] pierDir;     // x,z направления пирса от центра (модель)
            public float[] gridOrigin;  // x,z угла карты высот (модель)
            public float deck;          // высота главной площадки (модель): ниже неё — скала основания
            public float cell = 10f;
            public int gw, gh;
            public float[] heights;     // верх модели в клетке (модель, м); ниже −1e3 — пусто
        }

        Transform model;
        Vector3[] guns = new Vector3[0];
        Vector3 berthLocal;
        bool hasBerth;
        Data grid;
        float hostileUntil = -1f, nextShot;
        int gunCursor;
        Vector3 lastPlayerPos, playerVel;

        public bool Hostile { get { return Time.time < hostileUntil; } }
        public bool PlayerDocked { get; private set; }
        public Vector3 Center { get { return transform.position; } }
        /// <summary>Время, когда игрок последний раз вошёл в зону (для оповещения HUD).</summary>
        public float EnteredAt { get; private set; } = -100f;
        public bool PlayerInside { get; private set; }

        public static bool InSafeZone(Vector3 p)
        {
            SkyFortress f = Instance;
            if (f == null) return false;
            Vector3 d = p - f.transform.position; d.y = 0f;
            return d.sqrMagnitude < f.safeRadius * f.safeRadius;
        }

        /// <summary>Игрок дал залп: внутри безопасной зоны — крепость открывает огонь.</summary>
        public static void NotifyPlayerFired(Vector3 at)
        {
            SkyFortress f = Instance;
            if (f == null || !InSafeZone(at)) return;
            if (!f.Hostile) f.nextShot = Time.time + 2.5f;   // предупреждение на HUD, расчёты встают к орудиям — 2.5 с до первого выстрела
            f.hostileUntil = Time.time + f.hostileTime;
            FortressAlarmAt = Time.time;
        }
        public static float FortressAlarmAt = -100f;

        /// <summary>Ставит крепость в <paramref name="position"/> пирсом к <paramref name="faceTowards"/> (+ yawOffset°),
        /// причал — на высоте pierHeight (модель поднимается/опускается целиком; низ уходит в облака).</summary>
        /// Данные (дула, причал, карта высот) остаются в метрах исходной модели: масштаб применяется к узлу модели,
        /// все переводы идут через его Transform.
        public void Setup(Mesh mesh, Material[] materials, TextAsset data, Vector3 position, Vector3 faceTowards, float yawOffset, float pierHeight, float scale)
        {
            Instance = this;
            scale = Mathf.Max(0.1f, scale);
            if (data != null)
            {
                grid = JsonUtility.FromJson<Data>(data.text);
                if (grid.guns != null)
                {
                    guns = new Vector3[grid.guns.Length / 3];
                    for (int i = 0; i < guns.Length; i++) guns[i] = new Vector3(grid.guns[i * 3], grid.guns[i * 3 + 1], grid.guns[i * 3 + 2]);
                }
                if (grid.berth != null && grid.berth.Length >= 3) { berthLocal = new Vector3(grid.berth[0], grid.berth[1], grid.berth[2]); hasBerth = true; }
                if (grid.heights == null || grid.heights.Length != grid.gw * grid.gh) grid.heights = null;
                if (grid.gridOrigin != null && grid.gridOrigin.Length >= 2)
                {
                    float ex = Mathf.Max(Mathf.Abs(grid.gridOrigin[0]), Mathf.Abs(grid.gridOrigin[0] + grid.gw * grid.cell));
                    float ez = Mathf.Max(Mathf.Abs(grid.gridOrigin[1]), Mathf.Abs(grid.gridOrigin[1] + grid.gh * grid.cell));
                    boundRadius = Mathf.Sqrt(ex * ex + ez * ez) * scale;
                }
            }
            float yaw = yawOffset;
            Vector3 want = faceTowards - position; want.y = 0f;
            if (grid != null && grid.pierDir != null && grid.pierDir.Length >= 2 && want.sqrMagnitude > 1f)
            {
                Vector3 pier = new Vector3(grid.pierDir[0], 0f, grid.pierDir[1]);
                yaw += Vector3.SignedAngle(pier, want, Vector3.up);
            }
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            GameObject go = new GameObject("Fortress model");
            go.transform.SetParent(transform, false);
            model = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            model.localScale = Vector3.one * scale;
            if (hasBerth) model.localPosition = Vector3.up * (pierHeight - position.y - berthLocal.y * scale);
            // Шейдеру: ниже главной площадки серый камень — скала основания, выше — кладка.
            if (grid != null && materials != null)
            {
                float rockTop = model.TransformPoint(new Vector3(0f, grid.deck - 4f, 0f)).y;
                foreach (Material m in materials) if (m != null && m.HasProperty("_RockTop")) m.SetFloat("_RockTop", rockTop);
            }
        }

        public Vector3 BerthWorld { get { return model != null ? model.TransformPoint(berthLocal) : transform.position; } }

        /// <summary>Верх крепости над точкой мира (м) или −∞, если там пусто.</summary>
        public float TopAt(Vector3 world)
        {
            if (grid == null || grid.heights == null || model == null) return float.NegativeInfinity;
            Vector3 l = model.InverseTransformPoint(world);
            int ix = Mathf.FloorToInt((l.x - grid.gridOrigin[0]) / grid.cell), iz = Mathf.FloorToInt((l.z - grid.gridOrigin[1]) / grid.cell);
            if (ix < 0 || iz < 0 || ix >= grid.gw || iz >= grid.gh) return float.NegativeInfinity;
            float h = grid.heights[iz * grid.gw + ix];
            if (h < -999f) return float.NegativeInfinity;
            return model.TransformPoint(new Vector3(l.x, h, l.z)).y;
        }

        /// <summary>Ядро на отрезке a→b входит в крепость (по карте высот)? Точка входа — с шагом ~3 м.</summary>
        public bool TryHit(Vector3 a, Vector3 b, out Vector3 hit)
        {
            hit = b;
            if (grid == null || grid.heights == null) return false;
            // дешёвый отсев: далеко от крепости или выше башен
            Vector3 c = transform.position; float r = boundRadius + 10f;
            float dx = b.x - c.x, dz = b.z - c.z;
            if (dx * dx + dz * dz > r * r) return false;
            float len = (b - a).magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / 3f), 1, 24);
            for (int i = 1; i <= n; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, i / (float)n);
                if (q.y < TopAt(q)) { hit = q; return true; }
            }
            return false;
        }
        float boundRadius = 400f;
        /// <summary>Радиус описанного круга крепости в плане (м, с учётом масштаба).</summary>
        public float BoundRadius { get { return boundRadius; } }

        void Update()
        {
            long pt = PerfTimers.Start();
            Tick(Time.deltaTime);
            PerfTimers.Stop("Крепость", pt);
        }

        void Tick(float dt)
        {
            SkyTideBootstrap game = SkyTideBootstrap.Instance;
            PlayerHull hull = PlayerHull.Instance;
            if (game == null || game.Ship == null || hull == null || dt <= 0f) return;
            ShipController ship = game.Ship;
            Vector3 pp = hull.Center;
            playerVel = Vector3.Lerp(playerVel, (pp - lastPlayerPos) / dt, 0.2f); lastPlayerPos = pp;

            bool inside = InSafeZone(ship.transform.position);
            if (inside && !PlayerInside) EnteredAt = Time.time;
            PlayerInside = inside;

            // Причал: рядом и на малом ходу — ремонт.
            PlayerDocked = hasBerth && !Hostile && hull.Alive
                && (ship.transform.position - BerthWorld).sqrMagnitude < berthRadius * berthRadius && Mathf.Abs(ship.Speed) < berthMaxSpeed;
            if (PlayerDocked) hull.Repair(repairPerSecond * dt);

            Collide(ship);
            foreach (TargetShip t in TargetShip.All) if (t != null && t.Alive) { ShipController c = t.Controller; if (c != null) Collide(c); }

            if (!Hostile || !hull.Alive || guns.Length == 0 || Time.time < nextShot) return;
            // Залп по одному орудию, по очереди башен: плотный, но не мгновенный огонь.
            Vector3 muzzle = model.TransformPoint(guns[gunCursor % guns.Length]);
            gunCursor++;
            nextShot = Time.time + gunCooldown / Mathf.Max(1, guns.Length) + UnityEngine.Random.Range(0f, 0.25f);
            if ((pp - muzzle).sqrMagnitude > gunRange * gunRange) return;
            Vector3 dir;
            if (!Solve(muzzle, pp, out dir)) return;
            dir = Quaternion.Euler(UnityEngine.Random.Range(-spreadDeg, spreadDeg), UnityEngine.Random.Range(-spreadDeg, spreadDeg), 0f) * dir;
            if (FxSystem.Instance != null) FxSystem.Instance.Fire(muzzle, dir, Vector3.zero, muzzleSpeed, true, shellLife, shellCaliber);
        }

        /// <summary>Сквозь крепость не пройти: нос, центр и корма корабля проверяются по карте высот.</summary>
        void Collide(ShipController ship)
        {
            if (grid == null || grid.heights == null) return;
            Vector3 p = ship.transform.position, f = ship.transform.forward;
            float keel = p.y - 4f;   // днище: пирс на высоте корабля тоже упор, причаливают к его торцу
            foreach (float along in new[] { 28f, 0f, -28f })
            {
                Vector3 q = p + f * along;
                if (TopAt(q) < keel) continue;
                // Отталкиваем от центра крепости и гасим ход — «упёрлись в стену».
                Vector3 away = q - transform.position; away.y = 0f;
                if (away.sqrMagnitude < 1e-3f) away = -f;
                ship.Block(away.normalized * 0.8f, 0.3f);
                return;
            }
        }

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
                aim = to + playerVel * (tFlight * 0.85f);
            }
            return true;
        }
    }
}
