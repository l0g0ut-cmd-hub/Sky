using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Корабль-мишень: ходит по случайным точкам вокруг игрока (ShipController получает руль и ход как от джойстика).
    /// Попадание ядра — взрыв на корпусе; после hitsToSink попаданий — большой взрыв, мишень исчезает и
    /// через respawnDelay появляется в другом месте.
    /// Попадание считается по отрезку пути ядра за кадр против габарита меша модели (в его локальных координатах).
    /// </summary>
    public class TargetShip : MonoBehaviour
    {
        public static readonly List<TargetShip> All = new List<TargetShip>();

        public Transform player;
        public float minRange = 160f, maxRange = 420f;
        public int hitsToSink = 10;
        public float respawnDelay = 6f;

        ShipController ctrl;
        Transform model;
        Bounds box;
        Vector3 waypoint;
        float nextPick;
        int hits;
        float deadUntil = -1f;

        public bool Alive { get { return deadUntil < 0f; } }

        public void Setup(ShipController c, Transform player)
        {
            ctrl = c;
            this.player = player;
            Transform v = c.visual;
            model = v != null && v.childCount > 0 ? v.GetChild(0) : v;
            MeshFilter mf = model != null ? model.GetComponent<MeshFilter>() : null;
            box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 8f, new Vector3(10f, 14f, 36f));
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
            nextPick = Time.time + Random.Range(12f, 22f);
        }

        void Update()
        {
            if (ctrl == null) return;
            if (!Alive)
            {
                if (Time.time >= deadUntil) Respawn();
                return;
            }
            Vector3 to = waypoint - transform.position;
            to.y = 0f;
            if (to.magnitude < 40f || Time.time > nextPick) Pick();
            float ang = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            ctrl.turn = Mathf.Clamp(ang / 35f, -1f, 1f);
            ctrl.throttle = Mathf.Abs(ang) > 100f ? 0.25f : 0.55f;
        }

        /// <summary>Пересекает ли путь ядра a→b корпус; point — точка попадания.</summary>
        public bool TryHit(Vector3 a, Vector3 b, out Vector3 point)
        {
            point = b;
            if (!Alive || model == null) return false;
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
            if (hits >= hitsToSink)
            {
                if (fx != null) fx.Explosion(model.TransformPoint(box.center), 3f);
                deadUntil = Time.time + respawnDelay;
                ctrl.visual.gameObject.SetActive(false);
                ctrl.throttle = 0f;
                ctrl.turn = 0f;
            }
            else if (fx != null) fx.Explosion(point, 1f);
        }

        void Respawn()
        {
            deadUntil = -1f;
            hits = 0;
            Vector3 c = player != null ? player.position : Vector3.zero;
            float a = Random.Range(0f, Mathf.PI * 2f);
            transform.position = new Vector3(c.x + Mathf.Cos(a) * maxRange, transform.position.y, c.z + Mathf.Sin(a) * maxRange);
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            ctrl.visual.gameObject.SetActive(true);
            Pick();
        }
    }
}
