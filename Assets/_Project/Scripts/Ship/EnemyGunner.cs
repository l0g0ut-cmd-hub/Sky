using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Мишень отвечает огнём: когда игрок у её борта (курсовой угол 55–125°) в 90–650 м, борт даёт залп из четырёх
    /// пушек. Наводка — баллистика с упреждением по относительной скорости и небольшим разбросом,
    /// поэтому по манёвренному кораблю попадает не всегда. Дула — по габариту модели (у модели Meshy нет узлов пушек).
    /// </summary>
    public class EnemyGunner : MonoBehaviour
    {
        public float muzzleSpeed = 85f, gravity = 9.81f;
        public float cooldown = 4.5f, minRange = 90f, maxRange = 650f;
        /// <summary>Время последнего вражеского залпа — HUD показывает оповещение.</summary>
        public static float LastVolleyTime = -100f;
        public float spreadDeg = 1.4f;
        public int gunsPerSide = 4;

        TargetShip target;
        ShipController ctrl;
        Transform model;
        Bounds box;
        float ready;
        int pendingSide; float pendingAt; int pendingLeft;
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

        void Update()
        {
            float dt = Time.deltaTime;
            PlayerHull player = PlayerHull.Instance;
            if (player == null || target == null || model == null || dt <= 0f) return;
            Vector3 pp = player.Center;
            playerVel = Vector3.Lerp(playerVel, (pp - lastPlayerPos) / dt, 0.2f); lastPlayerPos = pp;
            selfVel = Vector3.Lerp(selfVel, (transform.position - lastPos) / dt, 0.2f); lastPos = transform.position;
            if (!target.Alive || !player.Alive) { pendingLeft = 0; return; }

            if (pendingLeft > 0 && Time.time >= pendingAt)
            {
                Shoot(pendingSide, gunsPerSide - pendingLeft, pp);
                pendingLeft--;
                pendingAt = Time.time + Random.Range(0.12f, 0.2f);
                return;
            }
            if (Time.time < ready) return;
            Vector3 rel = pp - transform.position; rel.y = 0f;
            float dist = rel.magnitude;
            if (dist < minRange || dist > maxRange) return;
            Vector3 local = transform.InverseTransformDirection(rel);
            float bearing = Mathf.Atan2(Mathf.Abs(local.x), local.z) * Mathf.Rad2Deg;
            if (bearing < 55f || bearing > 125f) return;
            pendingSide = local.x > 0f ? 1 : -1;
            pendingLeft = gunsPerSide;
            pendingAt = Time.time;
            LastVolleyTime = Time.time;
            ready = Time.time + cooldown + Random.Range(0f, 1.5f);
        }

        void Shoot(int side, int index, Vector3 aimAt)
        {
            FxSystem fx = FxSystem.Instance;
            if (fx == null) return;
            float z = Mathf.Lerp(box.min.z + box.size.z * 0.3f, box.max.z - box.size.z * 0.3f, gunsPerSide > 1 ? index / (float)(gunsPerSide - 1) : 0.5f);
            Vector3 localMuzzle = new Vector3(side * (box.extents.x * 0.92f), box.min.y + box.size.y * 0.38f, z);
            Vector3 muzzle = model.TransformPoint(localMuzzle);
            Vector3 dir;
            if (!Solve(muzzle, aimAt, out dir)) return;
            dir = Quaternion.Euler(Random.Range(-spreadDeg, spreadDeg), Random.Range(-spreadDeg, spreadDeg), 0f) * dir;
            fx.Fire(muzzle, dir, selfVel, muzzleSpeed, true);
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
                aim = to + (playerVel - selfVel) * tFlight;
            }
            return true;
        }
    }
}
