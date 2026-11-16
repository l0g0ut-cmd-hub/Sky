using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Контур корабля в плане — выпуклая оболочка всех его моделей (корпус, баллоны, пушки) в осях X/Z.
    /// Раньше столкновения считались капсулой: полуширина = 90% ширины всей модели (с баллонами), и та же полуширина
    /// торчала перед носом и за кормой — «невидимая стена» в 5–15 м (владелец 16.11). Теперь борт к борту можно встать
    /// впритык, а внутрь модели не заехать.
    /// </summary>
    public static class Footprint
    {
        /// <summary>Оболочка каждой сетки, загруженной ShipMeshLoader (снимается до UploadMeshData — потом вершины не читаются).</summary>
        public static readonly Dictionary<Mesh, Vector2[]> ByMesh = new Dictionary<Mesh, Vector2[]>();

        public static void Register(Mesh m, Vector3[] v)
        {
            if (m == null || v == null || v.Length < 3) return;
            List<Vector2> pts = new List<Vector2>(v.Length);
            for (int i = 0; i < v.Length; i++) pts.Add(new Vector2(v[i].x, v[i].z));
            ByMesh[m] = Hull(pts);
        }

        class Entry { public Mesh key; public Vector2[] hull; public float radius, checkAt; }
        static readonly Dictionary<Transform, Entry> cache = new Dictionary<Transform, Entry>();
        static readonly List<MeshFilter> filters = new List<MeshFilter>();
        static readonly List<Vector2> work = new List<Vector2>();

        /// <summary>Контур корабля в его локальных осях (без масштаба корня). null — моделей с контуром нет.</summary>
        public static Vector2[] Of(Transform root, out float radius)
        {
            radius = 0f;
            if (root == null) return null;
            Entry e;
            if (cache.TryGetValue(root, out e) && Time.time < e.checkAt) { radius = e.radius; return e.hull; }
            // Раз в 2 с: не сменилась ли модель (класс корабля задаётся после появления).
            MeshFilter main = root.GetComponentInChildren<MeshFilter>(true);
            Mesh key = main != null ? main.sharedMesh : null;
            if (e != null && e.key == key && key != null) { e.checkAt = Time.time + 2f; radius = e.radius; return e.hull; }
            if (e == null) { e = new Entry(); cache[root] = e; }
            e.key = key; e.checkAt = Time.time + 2f; e.hull = null; e.radius = 0f;
            root.GetComponentsInChildren(true, filters);
            work.Clear();
            Matrix4x4 toRoot = Matrix4x4.TRS(root.position, Yaw(root), Vector3.one).inverse;   // только курс: крен/дифферент не в счёт
            foreach (MeshFilter mf in filters)
            {
                Vector2[] h;
                if (mf == null || mf.sharedMesh == null || !ByMesh.TryGetValue(mf.sharedMesh, out h)) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                for (int i = 0; i < h.Length; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(new Vector3(h[i].x, 0f, h[i].y));
                    work.Add(new Vector2(p.x, p.z));
                }
            }
            if (work.Count >= 3)
            {
                e.hull = Hull(work);
                foreach (Vector2 p in e.hull) e.radius = Mathf.Max(e.radius, p.magnitude);
            }
            radius = e.radius;
            return e.hull;
        }

        public static void Forget(Transform root) { if (root != null) cache.Remove(root); }

        /// <summary>Выпуклая оболочка (монотонная цепь), против часовой в осях (x, z).</summary>
        public static Vector2[] Hull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            int n = pts.Count, k = 0;
            Vector2[] h = new Vector2[2 * n];
            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cross(h[k - 2], h[k - 1], pts[i]) <= 0f) k--;
                h[k++] = pts[i];
            }
            for (int i = n - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(h[k - 2], h[k - 1], pts[i]) <= 0f) k--;
                h[k++] = pts[i];
            }
            int m = Mathf.Max(1, k - 1);
            // Упростить до ≤ 32 вершин: убрать почти коллинеарные (контур — только для столкновений).
            List<Vector2> r = new List<Vector2>(m);
            for (int i = 0; i < m; i++) r.Add(h[i]);
            while (r.Count > 32)
            {
                int best = 0; float bestA = float.MaxValue;
                for (int i = 0; i < r.Count; i++)
                {
                    float a = Mathf.Abs(Cross(r[(i + r.Count - 1) % r.Count], r[i], r[(i + 1) % r.Count]));
                    if (a < bestA) { bestA = a; best = i; }
                }
                r.RemoveAt(best);   // выпуклость сохраняется; контур чуть «внутрь» — на доли метра
            }
            return r.ToArray();
        }

        static Quaternion Yaw(Transform t) { return Quaternion.Euler(0f, t.eulerAngles.y, 0f); }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); }

        /// <summary>Контур в мировых X/Z (Vector2: x, z).</summary>
        public static void ToWorld(Transform root, Vector2[] local, List<Vector2> into)
        {
            into.Clear();
            Matrix4x4 m = Matrix4x4.TRS(root.position, Yaw(root), Vector3.one);
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(new Vector3(local[i].x, 0f, local[i].y));
                into.Add(new Vector2(p.x, p.z));
            }
        }

        /// <summary>Пересечение двух выпуклых контуров (SAT). n — куда сдвинуть A, чтобы разойтись, depth — на сколько.</summary>
        public static bool Overlap(List<Vector2> a, List<Vector2> b, out Vector2 n, out float depth)
        {
            n = Vector2.zero; depth = float.MaxValue;
            if (!Axes(a, b, ref n, ref depth)) return false;
            if (!Axes(b, a, ref n, ref depth)) return false;
            Vector2 ca = Centroid(a), cb = Centroid(b);
            if (Vector2.Dot(ca - cb, n) < 0f) n = -n;
            return true;
        }

        static bool Axes(List<Vector2> p, List<Vector2> q, ref Vector2 n, ref float depth)
        {
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 e = p[(i + 1) % p.Count] - p[i];
                Vector2 ax = new Vector2(-e.y, e.x);
                float len = ax.magnitude;
                if (len < 1e-5f) continue;
                ax /= len;
                float pmin, pmax, qmin, qmax;
                Project(p, ax, out pmin, out pmax); Project(q, ax, out qmin, out qmax);
                float o = Mathf.Min(pmax, qmax) - Mathf.Max(pmin, qmin);
                if (o <= 0f) return false;
                if (o < depth) { depth = o; n = ax; }
            }
            return true;
        }

        static void Project(List<Vector2> p, Vector2 ax, out float min, out float max)
        {
            min = max = Vector2.Dot(p[0], ax);
            for (int i = 1; i < p.Count; i++) { float d = Vector2.Dot(p[i], ax); if (d < min) min = d; else if (d > max) max = d; }
        }

        public static Vector2 Centroid(List<Vector2> p)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < p.Count; i++) c += p[i];
            return p.Count > 0 ? c / p.Count : c;
        }

        /// <summary>Круг (строение) против контура: n — куда сдвинуть контур, depth — на сколько.</summary>
        public static bool CircleOverlap(List<Vector2> p, Vector2 c, float r, out Vector2 n, out float depth)
        {
            n = Vector2.zero; depth = 0f;
            bool inside = true; float bestOut = float.MaxValue, maxSigned = -float.MaxValue; Vector2 bestN = Vector2.zero, closest = c;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count], e = b - a;
                Vector2 outN = new Vector2(e.y, -e.x).normalized;   // наружу для обхода против часовой
                float s = Vector2.Dot(c - a, outN);
                if (s > 0f) inside = false;
                if (s > maxSigned) { maxSigned = s; bestN = outN; }
                float t = Mathf.Clamp01(Vector2.Dot(c - a, e) / Mathf.Max(1e-6f, e.sqrMagnitude));
                Vector2 q = a + e * t; float d = (c - q).sqrMagnitude;
                if (d < bestOut) { bestOut = d; closest = q; }
            }
            if (inside) { n = -bestN; depth = r - maxSigned; return true; }   // центр внутри: maxSigned ≤ 0
            float dist = Mathf.Sqrt(bestOut);
            if (dist >= r) return false;
            n = (closest - c) / Mathf.Max(1e-4f, dist); depth = r - dist;
            return true;
        }
    }
}
