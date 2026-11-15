using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Прицельная стрелка борта (владелец 15.11: «как в Black Flag — объёмная полупрозрачная стрелка»): лента по траекториям
    /// носовой и кормовой пушек борта (ширина = накрытие залпа), с наконечником у места падения/попадания. Два слоя: горизонтальная
    /// лента и тонкая «стенка» под ней — на экране читается как объёмная. Янтарная — борт готов, серая — перезарядка, красноватая —
    /// залп ложится в корпус цели. Строится каждый кадр, один меш, один вызов отрисовки.
    /// </summary>
    public class AimArrow : MonoBehaviour
    {
        static AimArrow instance;
        Mesh mesh; Material mat; MeshRenderer mr;
        readonly List<Vector3> v = new List<Vector3>(400);
        readonly List<Vector2> uv = new List<Vector2>(400);
        readonly List<Color> col = new List<Color>(400);
        readonly List<int> tri = new List<int>(1200);
        int drawnFrame = -1;

        public static AimArrow Get()
        {
            if (instance != null) return instance;
            Shader sh = Shader.Find("SkyTide/AimArrow");
            if (sh == null) return null;
            GameObject go = new GameObject("SkyTide.AimArrow");
            instance = go.AddComponent<AimArrow>();
            instance.mesh = new Mesh { name = "AimArrow" }; instance.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = instance.mesh;
            instance.mr = go.AddComponent<MeshRenderer>();
            instance.mat = new Material(sh);
            instance.mr.sharedMaterial = instance.mat;
            instance.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            instance.mr.receiveShadows = false;
            return instance;
        }

        static Vector3 Sample(List<Vector3> path, float t)
        {
            if (path.Count == 1) return path[0];
            float f = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Min(path.Count - 2, Mathf.FloorToInt(f));
            return Vector3.Lerp(path[i], path[i + 1], f - i);
        }

        /// <summary>a, b — точки траекторий крайних пушек (от дула до конца). state: 0 готов, 1 перезарядка, 2 в корпус.</summary>
        public void Draw(List<Vector3> a, List<Vector3> b, int state, float alpha)
        {
            if (a == null || b == null || a.Count < 2 || b.Count < 2) { Hide(); return; }
            v.Clear(); uv.Clear(); col.Clear(); tri.Clear();
            Color c = state == 2 ? new Color(1f, 0.45f, 0.3f, alpha) : state == 1 ? new Color(0.75f, 0.75f, 0.8f, alpha * 0.7f) : new Color(1f, 0.78f, 0.38f, alpha);
            const int N = 36;
            float minWidth = 6f;
            Vector3 lastC = Vector3.zero, lastDir = Vector3.forward, lastSide = Vector3.right; float lastHalf = 3f;
            for (int i = 0; i <= N; i++)
            {
                // Начало — на 6% пути от борта (у самого борта широкая лента торчала «плавником» над кораблём, снимки 15.11).
                float t = 0.06f + i / (float)N * 0.86f;   // последние 8% — под наконечник
                Vector3 pa = Sample(a, t), pb = Sample(b, t);
                Vector3 centre = (pa + pb) * 0.5f;
                Vector3 side = pb - pa; side.y = 0f;
                float y0 = (t - 0.06f) / 0.86f;
                // Сужение у борта: от 1.5 м до полного накрытия к трети пути — стрелка «вырастает» из борта.
                float half = Mathf.Lerp(1.5f, Mathf.Max(minWidth * 0.5f, side.magnitude * 0.5f + 2f), Mathf.SmoothStep(0f, 1f, y0 / 0.35f));
                side = side.sqrMagnitude > 0.01f ? side.normalized : lastSide;
                Vector3 ahead = Sample(a, t + 0.02f) + Sample(b, t + 0.02f); ahead = ahead * 0.5f - centre;
                if (ahead.sqrMagnitude > 1e-4f) lastDir = ahead.normalized;
                float y = y0;
                v.Add(centre - side * half); uv.Add(new Vector2(0f, y)); col.Add(c);
                v.Add(centre + side * half); uv.Add(new Vector2(1f, y)); col.Add(c);
                // «Стенка» под лентой — объём: узкая вертикальная полоса по центру, бледнее.
                float wall = Mathf.Lerp(0.5f, 2.2f, y);
                v.Add(centre); uv.Add(new Vector2(0.5f, y)); col.Add(new Color(c.r, c.g, c.b, c.a * 0.45f));
                v.Add(centre - Vector3.up * wall); uv.Add(new Vector2(0f, y)); col.Add(new Color(c.r, c.g, c.b, 0f));
                if (i > 0)
                {
                    int k = (i - 1) * 4, n = i * 4;
                    tri.Add(k); tri.Add(k + 1); tri.Add(n + 1); tri.Add(k); tri.Add(n + 1); tri.Add(n);
                    tri.Add(k + 2); tri.Add(k + 3); tri.Add(n + 3); tri.Add(k + 2); tri.Add(n + 3); tri.Add(n + 2);
                }
                lastC = centre; lastSide = side; lastHalf = half;
            }
            // Наконечник: шире ленты, до точки чуть дальше конца траектории.
            Vector3 tipBase = lastC, tipEnd = (a[a.Count - 1] + b[b.Count - 1]) * 0.5f;
            Vector3 fwd = tipEnd - tipBase; if (fwd.sqrMagnitude < 4f) fwd = lastDir * Mathf.Max(8f, lastHalf * 1.6f);
            tipEnd = tipBase + fwd * 1.25f;
            float headHalf = lastHalf * 1.7f + 3f;
            int s = v.Count;
            v.Add(tipBase - lastSide * headHalf); uv.Add(new Vector2(0f, 0.97f)); col.Add(c);
            v.Add(tipBase + lastSide * headHalf); uv.Add(new Vector2(1f, 0.97f)); col.Add(c);
            v.Add(tipEnd); uv.Add(new Vector2(0.5f, 1f)); col.Add(new Color(c.r, c.g, c.b, c.a * 1.2f));
            tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetColors(col); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            mr.enabled = true; drawnFrame = Time.frameCount;
        }

        public void Hide() { if (mr != null) mr.enabled = false; }

        void LateUpdate() { if (drawnFrame < Time.frameCount - 1 && mr != null) mr.enabled = false; }
    }
}
