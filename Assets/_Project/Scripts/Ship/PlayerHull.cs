using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Прочность корпуса игрока. Вражеское ядро, прошедшее через габарит модели, — взрыв на корпусе и −damagePerHit.
    /// Повреждения видны, как у врага: удар качает корпус и выбивает щепу, ниже 60% прочности из пробоин идёт дым,
    /// ниже 30% — огонь; ремонт у причала гасит их. На нуле — большой взрыв, корабль кренится и уходит в облака
    /// с обломками (камера провожает), через respawnDelay секунд возвращается целым у причала.
    /// </summary>
    public class PlayerHull : MonoBehaviour
    {
        public static PlayerHull Instance { get; private set; }
        public float maxHealth = 300f;   // втрое прочнее (просьба владельца): ~50 попаданий до гибели
        public float damagePerHit = 6f;
        public float respawnDelay = 4f;
        public float Health { get; private set; }
        public float Health01 { get { return Mathf.Clamp01(Health / maxHealth); } }
        public bool Alive { get { return deadUntil < 0f; } }
        public float LastHitTime { get; private set; } = -100f;
        public float DeadFor { get { return Alive ? 0f : respawnDelay - (deadUntil - Time.time); } }
        /// <summary>Число очагов (пробоин), из которых идёт дым или огонь.</summary>
        public int VentCount { get { return ventCount; } }

        ShipController ctrl;
        Transform model;
        Bounds box;
        float deadUntil = -1f;
        // Очаги на обшивке (координаты модели): последние три попадания.
        ShipHitSurface surface;
        readonly Vector3[] vents = new Vector3[3];
        readonly Vector3[] ventNormals = new Vector3[3];
        int ventCount, ventCursor;
        float smokeClock;
        // Гибель: крен, падение, снос по ходу.
        float sinkAge = -1f, sinkSide = 1f, sinkDrift;
        bool splashed;
        WreckDebris debris;

        public void Setup(ShipController c)
        {
            ctrl = c;
            Instance = this;
            Health = maxHealth;
            Refresh();
        }

        /// <summary>Модель могла смениться — габарит берём с текущей.</summary>
        public void Refresh()
        {
            Transform v = ctrl != null ? ctrl.visual : null;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
            ventCount = ventCursor = 0;
            surface = null;
            SkyTideBootstrap game = SkyTideBootstrap.Instance;
            ShipVariant variant = game != null ? game.CurrentShip : null;
            // Точная обшивка для очагов пожара — по облегчённой копии корпуса (только в игре: кадрам CI она не нужна).
            if (Application.isPlaying && variant != null && variant.lite != null)
            {
                try { surface = new ShipHitSurface(variant.lite); }
                catch (System.Exception e) { surface = null; Debug.LogWarning("SkyTide: обшивка корабля игрока не построена — " + e.Message); }
            }
            if (debris == null && game != null && game.materials != null && game.materials.darkWood != null) debris = new WreckDebris(game.materials.darkWood);
        }

        /// <summary>Ремонт у причала.</summary>
        public void Repair(float amount) { if (Alive) Health = Mathf.Min(maxHealth, Health + amount); }
        public Vector3 Center { get { return model != null ? model.TransformPoint(box.center) : transform.position; } }

        public bool TryHit(Vector3 a, Vector3 b, out Vector3 point)
        {
            point = b;
            if (!Alive || model == null) return false;
            Bounds bb = box;
            bb.Expand(0.6f);
            for (int i = 0; i <= 6; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 6f);
                if (bb.Contains(model.InverseTransformPoint(p))) { point = p; return true; }
            }
            return false;
        }

        /// <summary>Точка на борту у пушечной палубы напротив места попадания (ядро считается по габариту модели и
        /// может «попасть» в воздух у баллона — очаг и щепа должны быть на обшивке).</summary>
        bool HullPoint(Vector3 world, out Vector3 point, out Vector3 normal)
        {
            point = world; normal = Vector3.up;
            if (surface == null || model == null) return false;
            Vector3 local = model.InverseTransformPoint(world);
            float side = local.x >= box.center.x ? 1f : -1f;
            float deckY, zMin, zMax;
            Broadside guns = GetComponent<Broadside>();
            if (guns == null || !guns.GunDeck(out deckY, out zMin, out zMax))
            {
                deckY = box.min.y + box.size.y * 0.25f; zMin = box.min.z + box.size.z * 0.2f; zMax = box.max.z - box.size.z * 0.2f;
            }
            float y = deckY + Random.Range(-1.4f, 0.6f);
            float z = Mathf.Clamp(local.z, zMin - 2f, zMax + 2f);
            ShipHitSurface.Contact c;
            if (!surface.Trace(new Vector3(box.center.x + side * (box.extents.x + 4f), y, z), new Vector3(box.center.x, y, z), out c)) return false;
            point = model.TransformPoint(c.point);
            normal = model.TransformDirection(c.normal).normalized;
            return true;
        }

        /// <summary>Попадание ядра; power — калибр (ядро крепости втрое тяжелее корабельного).</summary>
        public void OnHit(Vector3 point, float power = 1f)
        {
            if (!Alive) return;
            power = Mathf.Max(1f, power);
            Health -= damagePerHit * power;
            LastHitTime = Time.time;
            // Удар по корпусу встряхивает камеру (сильнее — от тяжёлого ядра крепости).
            OrbitCamera shake = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (shake != null) shake.HitImpulse(power);
            FxSystem fx = FxSystem.Instance;
            SkySound.HullHit(point, Health <= damagePerHit * 2f);
            Vector3 velocity = ctrl != null ? ctrl.transform.forward * ctrl.Speed : Vector3.zero;
            float side = model != null && model.InverseTransformPoint(point).x < box.center.x ? -1f : 1f;
            Vector3 at, normal;
            bool onHull = HullPoint(point, out at, out normal);
            if (onHull)
            {
                vents[ventCursor] = model.InverseTransformPoint(at);
                ventNormals[ventCursor] = model.InverseTransformDirection(normal).normalized;
                ventCursor = (ventCursor + 1) % vents.Length;
                ventCount = Mathf.Min(ventCount + 1, vents.Length);
            }
            // Удар качает корпус от подбитого борта и выбивает щепу.
            if (ctrl != null) ctrl.Recoil(side * 0.5f * Mathf.Min(2f, power));
            if (debris != null) debris.Emit(onHull ? at : point, onHull ? normal : Vector3.up, velocity, 3, false);
            if (Health <= 0f)
            {
                Health = 0f;
                SkySound.ShipDestroyed(Center);
                if (fx != null) fx.Explosion(Center, 3f);
                deadUntil = Time.time + respawnDelay;
                // Корабль не исчезает во вспышке: кренится на подбитый борт и уходит в облака.
                sinkAge = 0f; sinkSide = side; splashed = false;
                sinkDrift = ctrl != null ? ctrl.Speed : 0f;
                if (ventCount == 0 && model != null) { vents[0] = box.center; ventNormals[0] = Vector3.up; ventCount = 1; }
                if (debris != null) debris.Emit(Center, Vector3.up, velocity, 20, true);
                if (ctrl != null) { ctrl.Block(Vector3.zero, 0f); ctrl.enabled = false; }
            }
            else if (fx != null)
            {
                if (onHull) fx.HullImpact(at, normal, velocity);
                else fx.Explosion(point, 0.8f);
            }
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Корпус игрока",__pt);}
        void UpdateTimed()
        {
            float dt = Time.deltaTime;
            FxSystem fx = FxSystem.Instance;
            if (debris != null && dt > 0f) debris.Simulate(dt, fx);
            if (ctrl != null) ctrl.SetDamage(Alive ? Health01 : 1f);   // подбитый корабль теряет ход, ремонт возвращает
            if (!Alive)
            {
                if (Time.time >= deadUntil) { Respawn(); return; }
                Sink(dt, fx);
            }
            else if (Health >= maxHealth - 0.01f) ventCount = ventCursor = 0;   // отремонтирован — пробоины заделаны

            // Дым из пробоин ниже 60% прочности, огонь ниже 30%; тонущий корабль дымит, но не горит.
            float damage = 1f - Health01;
            if (ventCount > 0 && fx != null && model != null && dt > 0f && damage >= 0.4f)
            {
                smokeClock += dt;
                if (smokeClock >= 0.18f)
                {
                    smokeClock %= 0.18f;
                    Vector3 velocity = Alive && ctrl != null ? ctrl.transform.forward * ctrl.Speed : Vector3.zero;
                    for (int i = 0; i < ventCount; i++)
                    {
                        Vector3 at = model.TransformPoint(vents[i]), normal = model.TransformDirection(ventNormals[i]);
                        fx.DamageSmoke(at + normal * 0.25f, damage);
                        if (Alive && damage >= 0.7f) fx.DamageFire(at + normal * 0.4f, normal, damage, velocity);
                    }
                }
            }
        }

        /// <summary>Гибель: крен на подбитый борт, корма вниз, падение в море облаков; камера опускает взгляд следом.</summary>
        void Sink(float dt, FxSystem fx)
        {
            if (ctrl == null || ctrl.visual == null || sinkAge < 0f || dt <= 0f) return;
            sinkAge += dt;
            float fall = 3f * sinkAge + 5.5f * sinkAge * sinkAge;
            float pitch = -20f * Mathf.SmoothStep(0f, 1f, sinkAge / 3.2f);
            float roll = sinkSide * 60f * Mathf.SmoothStep(0f, 1f, sinkAge / 3.6f);
            ctrl.visual.localRotation = Quaternion.Euler(pitch, 0f, roll);
            ctrl.visual.localPosition = Vector3.down * fall;
            sinkDrift = Mathf.MoveTowards(sinkDrift, 0f, 6f * dt);
            ctrl.transform.position += ctrl.transform.forward * (sinkDrift * dt);
            OrbitCamera oc = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (oc != null && oc.target == ctrl.transform) oc.lookDrop = fall * 0.45f;
            if (!splashed && fx != null && ctrl.transform.position.y - fall + box.min.y < fx.deckTop)
            {
                splashed = true;
                Vector3 at = ctrl.transform.position; at.y = fx.deckTop;
                fx.CloudSplash(at, 2.4f);
            }
        }

        /// <summary>Возврат целым: к причалу крепости, носом от неё, без хода; крепость снимает тревогу.</summary>
        void Respawn()
        {
            deadUntil = -1f;
            Health = maxHealth;
            sinkAge = -1f;
            ventCount = ventCursor = 0;
            OrbitCamera oc = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (oc != null) oc.lookDrop = 0f;
            if (ctrl == null) return;
            ctrl.enabled = true;
            if (ctrl.visual != null)
            {
                ctrl.visual.localPosition = Vector3.zero;
                ctrl.visual.localRotation = Quaternion.identity;
                ctrl.visual.gameObject.SetActive(true);
            }
            ctrl.Block(Vector3.zero, 0f);
            // Рукоять хода — на «СТОП»: у причала корабль стоит, пока игрок сам не даст ход.
            SkyTideBootstrap game = SkyTideBootstrap.Instance;
            TouchControls controls = game != null ? game.GetComponent<TouchControls>() : null;
            if (controls != null) controls.SetGear(1);
            SkyFortress fort = SkyFortress.Instance;
            if (fort != null)
            {
                Vector3 berth = fort.BerthWorld, away = berth - fort.Center;
                away.y = 0f;
                if (away.sqrMagnitude < 1f) away = Vector3.forward;
                away.Normalize();
                Vector3 at = berth + away * 30f;
                at.y = ctrl.transform.position.y;
                ctrl.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away, Vector3.up));
                fort.Pardon();
            }
            if (oc != null) oc.Snap();
        }

        void OnDestroy() { if (debris != null) debris.Dispose(); }
    }
}
