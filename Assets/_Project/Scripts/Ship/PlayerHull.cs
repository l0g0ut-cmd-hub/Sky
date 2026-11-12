using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Прочность корпуса игрока. Вражеское ядро, прошедшее через габарит модели, — взрыв на корпусе и −damagePerHit.
    /// На нуле — большой взрыв, корабль скрыт respawnDelay секунд, затем возвращается целым.
    /// </summary>
    public class PlayerHull : MonoBehaviour
    {
        public static PlayerHull Instance { get; private set; }
        public float maxHealth = 100f;
        public float damagePerHit = 6f;
        public float respawnDelay = 4f;
        public float Health { get; private set; }
        public float Health01 { get { return Mathf.Clamp01(Health / maxHealth); } }
        public bool Alive { get { return deadUntil < 0f; } }
        public float LastHitTime { get; private set; } = -100f;
        public float DeadFor { get { return Alive ? 0f : respawnDelay - (deadUntil - Time.time); } }

        ShipController ctrl;
        Transform model;
        Bounds box;
        float deadUntil = -1f;

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
        }

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

        public void OnHit(Vector3 point)
        {
            if (!Alive) return;
            Health -= damagePerHit;
            LastHitTime = Time.time;
            FxSystem fx = FxSystem.Instance;
            SkySound.HullHit(point, Health <= damagePerHit * 2f);
            if (Health <= 0f)
            {
                Health = 0f;
                SkySound.ShipDestroyed(Center);
                if (fx != null) fx.Explosion(Center, 3f);
                deadUntil = Time.time + respawnDelay;
                if (ctrl != null && ctrl.visual != null) ctrl.visual.gameObject.SetActive(false);
            }
            else if (fx != null) fx.Explosion(point, 0.8f);
        }

        void Update()
        {
            if (!Alive && Time.time >= deadUntil)
            {
                deadUntil = -1f;
                Health = maxHealth;
                if (ctrl != null && ctrl.visual != null) ctrl.visual.gameObject.SetActive(true);
            }
        }
    }
}
