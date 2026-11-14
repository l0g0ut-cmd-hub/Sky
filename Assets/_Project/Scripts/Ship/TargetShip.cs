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
        public float respawnDelay = 6f;

        ShipController ctrl;
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

        void Pick()
        {
            Vector3 c = player != null ? player.position : Vector3.zero;
            float a = Random.Range(0f, Mathf.PI * 2f);
            float d = Random.Range(minRange, maxRange);
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
            Lamps();
            if (!Alive)
            {
                if (Time.time >= deadUntil) Respawn();
                return;
            }
            CollideWithPlayer();
            ctrl.SetDamage(Health01);
            if (nextDaze <= 0f) nextDaze = Time.time + Random.Range(10f, 20f);
            if (Time.time >= nextDaze && !Dazed)
            {
                dazeUntil = Time.time + Random.Range(2.5f, 6f);
                dazeTurn = Random.value < 0.5f ? 0f : Random.Range(-0.8f, 0.8f);
                nextDaze = dazeUntil + Random.Range(10f, 22f);
            }
            if (Dazed) { ctrl.turn = dazeTurn; ctrl.throttle = 0.08f; return; }
            Vector3 toPlayer = player != null ? player.position - transform.position : Vector3.zero;
            toPlayer.y = 0f;
            PlayerHull playerHull = PlayerHull.Instance;
            if (player != null && playerHull != null && playerHull.Alive && !SkyFortress.InSafeZone(player.position)) { Engage(toPlayer); return; }
            fightSide = 0f;
            if (player != null && toPlayer.magnitude > leash && !SkyFortress.InSafeZone(player.position)) { waypoint = player.position; nextPick = Time.time + 8f; }
            Vector3 to = waypoint - transform.position;
            to.y = 0f;
            if (to.magnitude < 40f || Time.time > nextPick) Pick();
            float ang = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? 0.25f : 0.55f;
        }

        /// <summary>Ореолы фонарей и окон: издалека корабль читается по огням (вблизи их даёт само свечение).</summary>
        void Lamps()
        {
            FxSystem fx = FxSystem.Instance; Camera cam = Camera.main;
            if (fx == null || cam == null || model == null) return;
            bool lit = Alive || (damageVisual != null && damageVisual.Sinking && !damageVisual.Hidden);
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lit) { fx.ClearLamp(30 + i); continue; }
                Vector3 p = model.TransformPoint(lamps[i]);
                float d = Vector3.Distance(eye, p);
                fx.SetLamp(30 + i, p, Mathf.Clamp(d * 0.014f, 1.5f, 16f), new Color(1f, 0.72f, 0.38f, 0.08f * Mathf.Clamp01((d - 90f) / 220f)));
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
            Broadside threat = player != null ? player.GetComponent<Broadside>() : null;
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
            ShipController other = player != null ? player.GetComponent<ShipController>() : null;
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

        public void OnHit(Vector3 point)
        {
            if (!Alive) return;
            hits++;
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
                SkySound.ShipDestroyed(point);
                if (fx != null) fx.Explosion(model.TransformPoint(box.center), 3f);
                deadUntil = Time.time + respawnDelay;
                if (damageVisual != null) damageVisual.BeginSink(point, velocity);
                ctrl.throttle = 0f;
                ctrl.turn = 0f;
            }
            else if (fx != null) fx.HullImpact(point,normal,velocity);
        }

        void Respawn()
        {
            deadUntil = -1f;
            hits = 0;
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
